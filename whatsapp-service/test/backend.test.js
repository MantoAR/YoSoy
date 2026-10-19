// El backend de WhatsApp (backend.js): Baileys es el único motor. Se arma con el consentimiento, los topes y el ritmo de envío, y la cola no se
// arma sin esas protecciones.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { crearBackend } = require("../backend");
const { crearColaParaBackend } = require("../cola");
const { leerConfigEnvio } = require("../config");
const { ServicioBaileys } = require("../baileys/servicio");
const { fachadaFalsa } = require("./helpers/baileys-falso");
const { RelojFalso, esperarHasta } = require("./helpers/reloj-falso");
const { conRutaDentroDeLaAplicacion } = require("./helpers/dentro-de-la-aplicacion");

const log = { info() {}, error() {} };
const configBaileys = sessionPath => ({
    puerto: 3002,
    apiBaseUrl: "http://127.0.0.1:5000",
    secreto: "S3CRETO",
    baileys: { sessionPath, versionWa: null, buscarVersionWa: false, maxVentanasQr: 3, envio: leerConfigEnvio({}) }
});

// Una carpeta de sesión en el directorio temporal (fuera del repo), que se borra al terminar.
function conCarpeta(cuerpo) {
    return async () => {
        const raiz = fs.mkdtempSync(path.join(os.tmpdir(), "yosoy-backend-"));
        try { await cuerpo(path.join(raiz, "sesion")); }
        finally { fs.rmSync(raiz, { recursive: true, force: true }); }
    };
}

// La interfaz que usa servidor.js / index.js.
const INTERFAZ = ["iniciar", "cerrar", "estado", "enviar", "desvincular", "recuperar", "salud", "vincular"];

test("se arma un ServicioBaileys, sin iniciarlo ni abrir sockets, y no se carga nada de whatsapp-web.js ni de Puppeteer", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const backend = await crearBackend(configBaileys(carpeta), log, { cargarBaileys: async () => falso.fachada, reenviar: () => {} });

    assert.ok(backend instanceof ServicioBaileys);
    assert.equal(backend.salud().estado, "detenido");
    assert.equal(falso.sockets.length, 0);
    assert.deepEqual(Object.keys(require.cache).filter(ruta => /whatsapp-web\.js|puppeteer/.test(ruta)), [], "no hay Puppeteer ni whatsapp-web.js en memoria");
    assert.ok(fs.statSync(carpeta).isDirectory(), "la carpeta de la sesión quedó creada y lista");
    assert.equal(backend._maxVentanasQr, 3, "el cupo de ventanas de QR de la configuración llega al servicio");
    assert.equal(backend._simularEscritura, true, "y 'escribiendo...' viene prendido");
    assert.ok(fs.statSync(path.join(carpeta, "datos")).isDirectory(), "los datos del servicio van en datos/");
    assert.equal(typeof backend.datos.cerrar, "function");
    assert.ok(backend.opcionesDeCola.consentimiento && backend.opcionesDeCola.limites);
}));

// ---------- Consentimiento y topes, de punta a punta (sin WhatsApp: socket simulado y reloj falso) ----------

test("con Baileys, quien escribe primero queda anotado ANTES de reenviar a la API, y la cola respeta el consentimiento y la baja", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const reloj = new RelojFalso();
    const lineas = [];
    const log = { info: m => lineas.push(m), error: m => lineas.push(m) };
    const alReenviar = [];
    let backend;
    const reenviar = (telefono, texto) => {
        alReenviar.push({ telefono, texto, yaTeniaPermiso: backend.opcionesDeCola.consentimiento.permite(telefono).ok });
    };
    backend = await crearBackend(configBaileys(carpeta), log, { cargarBaileys: async () => falso.fachada, reenviar, reloj });
    assert.equal(backend.opcionesDeCola.estadoDeConexion(), "detenido", "la cola mira el estado real del servicio: todavía no arrancó");
    backend.iniciar();
    await esperarHasta(() => falso.sockets.length === 1, { descripcion: "el socket" });
    await reloj.avanzar(0);
    const sock = falso.ultimo;
    sock.abrir();
    const cola = crearColaParaBackend(backend, log);
    assert.equal(backend.opcionesDeCola.estadoDeConexion(), "conectado");
    assert.deepEqual(backend.opcionesDeCola.espaciado, { minMs: 4000, maxMs: 9000 }, "pausa de 4 a 9 segundos entre mensajes");
    assert.deepEqual(backend.opcionesDeCola.pausaLarga, { cada: [8, 14], ms: [30000, 75000] });

    // Sin constancia: no.
    assert.equal(cola.encolar("1155550001", "Tenés turno mañana").codigo, "sin_consentimiento");

    // Ella escribe primero: se anota antes de reenviar, y ahora sí.
    sock.recibir({ de: "5491155550001@s.whatsapp.net", texto: "hola, quiero un turno", id: "A1" });
    assert.deepEqual(alReenviar.map(a => [a.telefono, a.texto, a.yaTeniaPermiso]), [["5491155550001", "hola, quiero un turno", true]]);
    assert.equal(cola.encolar("1155550001", "Claro, ¿para qué día?").ok, true);

    // A otra persona, solo si la API dice que aceptó.
    assert.equal(cola.encolar("1166667777", "Recordatorio").codigo, "sin_consentimiento");
    assert.equal(cola.encolar("1166667777", "Recordatorio", { optIn: true }).ok, true);

    // Escribe BAJA: se sigue reenviando a la API, pero ya no se le manda nada.
    sock.recibir({ de: "5491155550001@s.whatsapp.net", texto: "BAJA", id: "A2" });
    assert.equal(alReenviar.length, 2);
    assert.equal(cola.encolar("1155550001", "otro", { optIn: true }).codigo, "baja");

    await esperarHasta(() => sock.presencias.length >= 2, { descripcion: "el 'escribiendo' del primer mensaje" });
    await reloj.avanzar(1500);                                                                          // lo que 'escribe' (hasta 1,5 s)
    await esperarHasta(() => sock.enviados.length === 1, { descripcion: "el primer mensaje" });       // el segundo espera su turno

    // Nada del contenido ni el teléfono entero en el log.
    assert.doesNotMatch(lineas.join("\n"), /hola, quiero un turno|5491155550001|1155550001/);

    await backend.datos.cerrar();
    const crudo = fs.readFileSync(path.join(carpeta, "datos", "consentimientos.json"), "utf8");
    assert.doesNotMatch(crudo, /5550001|hola|BAJA/);
    const contadores = JSON.parse(fs.readFileSync(path.join(carpeta, "datos", "envios.json"), "utf8"));
    assert.equal(contadores.enviados, 1, "el envío quedó contado, y se esperó a que se guardara");
    assert.doesNotMatch(JSON.stringify(contadores), /5550001/);
    cola.detener();
    await backend.cerrar();
}));

test("con los topes por defecto, una charla con el asistente no se corta a los 3 mensajes; un aviso suelto a otra persona sí tiene el tope chico", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const reloj = new RelojFalso();
    const backend = await crearBackend(configBaileys(carpeta), { info() {}, error() {} }, { cargarBaileys: async () => falso.fachada, reenviar: () => {}, reloj });
    backend.iniciar();
    await esperarHasta(() => falso.sockets.length === 1, { descripcion: "el socket" });
    await reloj.avanzar(0);
    falso.ultimo.abrir();
    const cola = crearColaParaBackend(backend, log);
    assert.equal(backend.opcionesDeCola.limites.resumen().topePorDestinatario, 3);
    assert.equal(backend.opcionesDeCola.limites.resumen().topeEnConversacion, 30);

    // Ella escribe y el asistente le contesta: pedir un turno lleva cinco mensajes, no tres.
    falso.ultimo.recibir({ de: "5491155550001@s.whatsapp.net", texto: "hola", id: "C1" });
    for (let i = 1; i <= 8; i++) assert.equal(cola.encolar("1155550001", `respuesta ${i}`).ok, true, `respuesta ${i}`);

    // A otra persona que nunca escribió, solo un aviso que ella aceptó: el tope de siempre.
    assert.equal(cola.encolar("1166667777", "aviso 1", { optIn: true }).ok, true);
    assert.equal(cola.encolar("1166667777", "aviso 2", { optIn: true }).ok, true);
    assert.equal(cola.encolar("1166667777", "aviso 3", { optIn: true }).ok, true);
    const cuarto = cola.encolar("1166667777", "aviso 4", { optIn: true });
    assert.equal(cuarto.codigo, "limite_destinatario");
    assert.match(cuarto.error, /ya se le mandaron 3 mensajes hoy/);

    cola.detener();
    await backend.cerrar();
}));

test("al apagar, datos.cerrar() espera a los dos archivos: los consentimientos y los contadores", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const backend = await crearBackend(configBaileys(carpeta), { info() {}, error() {} }, { cargarBaileys: async () => falso.fachada, reenviar: () => {} });
    const esperados = [];
    const { consentimiento, limites } = backend.opcionesDeCola;
    consentimiento.cerrar = async () => { esperados.push("consentimientos"); };
    limites.cerrar = async () => { esperados.push("contadores"); };
    await backend.datos.cerrar();
    assert.deepEqual(esperados.sort(), ["consentimientos", "contadores"]);
}));

test("con Baileys, los mensajes que no son de una persona (grupos, propios, historial, reacciones) no dan ninguna constancia", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const reloj = new RelojFalso();
    const log = { info() {}, error() {} };
    const backend = await crearBackend(configBaileys(carpeta), log, { cargarBaileys: async () => falso.fachada, reenviar: () => {}, reloj });
    backend.iniciar();
    await esperarHasta(() => falso.sockets.length === 1, { descripcion: "el socket" });
    await reloj.avanzar(0);
    const sock = falso.ultimo;
    sock.abrir();

    sock.recibir({ de: "120363000000000000@g.us" });
    sock.recibir({ de: "5491155550001@s.whatsapp.net", clave: { fromMe: true } });
    sock.recibir({ de: "5491155550001@s.whatsapp.net", tipo: "append" });
    sock.recibir({ de: "5491155550001@s.whatsapp.net", mensaje: { reactionMessage: { text: "x" } } });

    assert.equal(backend.opcionesDeCola.consentimiento.cantidad(), 0);
    await backend.cerrar();
}));

test("una foto o un audio SÍ son constancia de que escribió primero, y su aviso (o su leyenda) no se interpreta como una BAJA", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const reloj = new RelojFalso();
    const backend = await crearBackend(configBaileys(carpeta), log, { cargarBaileys: async () => falso.fachada, reenviar: () => {}, reloj });
    backend.iniciar();
    await esperarHasta(() => falso.sockets.length === 1, { descripcion: "el socket" });
    await reloj.avanzar(0);
    const sock = falso.ultimo;
    sock.abrir();
    const consentimiento = backend.opcionesDeCola.consentimiento;

    assert.equal(consentimiento.permite("1155550001").codigo, "sin_consentimiento");
    sock.recibir({ de: "5491155550001@s.whatsapp.net", mensaje: { imageMessage: { caption: "BAJA" } }, id: "I1" });    // una foto con la leyenda "BAJA": no es un pedido de baja
    assert.equal(consentimiento.cantidad(), 1);
    assert.equal(consentimiento.permite("1155550001").ok, true, "escribió primero (con una foto): el equipo le puede contestar");

    sock.recibir({ de: "5491166667777@s.whatsapp.net", mensaje: { audioMessage: { ptt: true } }, id: "I2" });
    assert.equal(consentimiento.permite("1166667777").ok, true, "también con un audio");

    sock.recibir({ de: "5491155550001@s.whatsapp.net", texto: "BAJA", id: "I3" });                                        // un TEXTO "BAJA" sí lo es
    assert.equal(consentimiento.permite("1155550001").codigo, "baja");
    await backend.cerrar();
}));

test("el reenviador recibe el texto y lo que lo acompaña (tipo, id, hora, nombre), y el backend expone sus contadores", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const reloj = new RelojFalso();
    const recibidos = [];
    const reenviar = Object.assign((telefono, texto, extra) => { recibidos.push({ telefono, texto, extra }); }, { resumen: () => ({ reenviados: 5, pendientes: 1 }) });
    const backend = await crearBackend(configBaileys(carpeta), log, { cargarBaileys: async () => falso.fachada, reenviar, reloj });
    backend.iniciar();
    await esperarHasta(() => falso.sockets.length === 1, { descripcion: "el socket" });
    await reloj.avanzar(0);
    const sock = falso.ultimo;
    sock.abrir();

    sock.recibir({ de: "5491155550001@s.whatsapp.net", mensaje: { imageMessage: { caption: "receta" } }, id: "I1", nombre: "Juan", marca: 1760000123 });

    assert.deepEqual(recibidos, [{ telefono: "5491155550001", texto: "[Imagen] receta", extra: { tipo: "imagen", id: "I1", ts: 1760000123, nombre: "Juan" } }]);
    assert.deepEqual(backend.salud().entrantes, { recibidos: 1, duplicados: 0, ignorados: {}, reenviados: 5, pendientes: 1 });
    await backend.cerrar();
}));

test("al apagar se avisa cuántos mensajes quedaban sin reenviar a la API (solo el número) y se cortan los reintentos", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const lineas = [];
    const registro = { info: m => lineas.push(m), error: m => lineas.push(m) };
    let detenido = 0;
    const reenviar = Object.assign(() => {}, { detener: () => { detenido++; return 3; }, resumen: () => ({}) });
    const backend = await crearBackend(configBaileys(carpeta), registro, { cargarBaileys: async () => falso.fachada, reenviar });

    await backend.datos.cerrar();

    assert.equal(detenido, 1);
    assert.equal(lineas.filter(l => /Al apagar quedaron 3 mensajes entrantes sin reenviar a la API/.test(l)).length, 1);
}));

test("al apagar sin nada esperando no se escribe ningún aviso", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const lineas = [];
    const registro = { info: m => lineas.push(m), error: m => lineas.push(m) };
    const reenviar = Object.assign(() => {}, { detener: () => 0, resumen: () => ({}) });
    const backend = await crearBackend(configBaileys(carpeta), registro, { cargarBaileys: async () => falso.fachada, reenviar });
    const antes = lineas.length;                       // lo que el backend dijo al armarse (la carpeta de la sesión)

    await backend.datos.cerrar();

    assert.deepEqual(lineas.slice(antes), []);
}));

test("la cola NO se arma sin consentimiento ni topes: un backend sin esas protecciones no puede mandarle a cualquiera", () => {
    const enviados = [];
    const enviar = async (telefono, texto) => { enviados.push([telefono, texto]); };
    const consentimiento = { permite: () => ({ ok: true }) };
    const limites = { reservar: () => ({ ok: true, huella: "h" }) };

    assert.throws(() => crearColaParaBackend({ enviar }, log), /no trae consentimiento ni topes de envío/);                      // nada
    assert.throws(() => crearColaParaBackend({ enviar, opcionesDeCola: {} }, log), /no se arma la cola/);                        // vacías
    assert.throws(() => crearColaParaBackend({ enviar, opcionesDeCola: { consentimiento } }, log), /no se arma la cola/);        // sin topes
    assert.throws(() => crearColaParaBackend({ enviar, opcionesDeCola: { limites } }, log), /no se arma la cola/);               // sin consentimiento
    assert.deepEqual(enviados, []);
});

test("si Baileys no se puede cargar, el error sube (index.js lo informa y sale)", conCarpeta(async carpeta => {
    await assert.rejects(
        () => crearBackend(configBaileys(carpeta), log, { cargarBaileys: async () => { throw new Error("No está instalado @whiskeysockets/baileys."); } }),
        /No está instalado/);
}));

test("una carpeta de sesión dentro de la aplicación frena el arranque ANTES de cargar la librería y no crea nada", () =>
    conRutaDentroDeLaAplicacion("sesion-backend-que-no-tiene-que-existir", async adentro => {
        let seCargo = false;
        await assert.rejects(
            () => crearBackend(configBaileys(adentro), log, { cargarBaileys: async () => { seCargo = true; return fachadaFalsa().fachada; } }),
            /FUERA/);
        assert.equal(seCargo, false, "la librería ni se cargó");
        assert.equal(fs.existsSync(adentro), false, "y no se creó la carpeta");
    }));

test("una carpeta de sesión dentro de la carpeta de la aplicación (aunque no sea un repo) frena el arranque", conCarpeta(async carpeta => {
    const app = path.dirname(carpeta);                       // la "aplicación" de esta prueba es la carpeta temporal
    const adentro = path.join(app, "sesion-adentro");
    await assert.rejects(
        () => crearBackend(configBaileys(adentro), log, { appDir: app, cargarBaileys: async () => fachadaFalsa().fachada }),
        /dentro de la carpeta de la aplicación/);
    assert.equal(fs.existsSync(adentro), false);
}));

test("una carpeta de sesión con archivos ajenos frena el arranque y no toca nada", conCarpeta(async carpeta => {
    fs.mkdirSync(carpeta);
    fs.writeFileSync(path.join(carpeta, "notas.txt"), "no es de la sesión");
    let seCargo = false;
    await assert.rejects(
        () => crearBackend(configBaileys(carpeta), log, { cargarBaileys: async () => { seCargo = true; return fachadaFalsa().fachada; } }),
        /no son de la sesión/);
    assert.equal(seCargo, false);
    assert.deepEqual(fs.readdirSync(carpeta), ["notas.txt"]);
}));

test("el backend cumple la interfaz que usa el servidor, incluidas la salud y la vinculación a pedido", () => {
    for (const metodo of INTERFAZ) assert.equal(typeof ServicioBaileys.prototype[metodo], "function", metodo);
});
