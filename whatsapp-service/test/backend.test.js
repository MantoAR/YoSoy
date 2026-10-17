// Selección del backend (backend.js): cada librería se carga solo si se elige, y los dos backends cumplen la misma interfaz.
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
    backend: "baileys",
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

// La interfaz que usa servidor.js / index.js, en común para los dos backends.
const INTERFAZ = ["iniciar", "cerrar", "estado", "enviar", "desvincular", "recuperar"];

const cargadas = () => Object.keys(require.cache).filter(ruta => /whatsapp-web\.js|puppeteer/.test(ruta));

test("con backend baileys se arma un ServicioBaileys, sin iniciarlo ni abrir sockets, y NO se carga whatsapp-web.js", conCarpeta(async carpeta => {
    const falso = fachadaFalsa();
    const backend = await crearBackend(configBaileys(carpeta), log, { cargarBaileys: async () => falso.fachada, reenviar: () => {} });

    assert.ok(backend instanceof ServicioBaileys);
    assert.equal(backend.salud().estado, "detenido");
    assert.equal(falso.sockets.length, 0);
    assert.deepEqual(cargadas(), [], "con baileys no hay Puppeteer ni whatsapp-web.js en memoria");
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

test("con Baileys, los mensajes que no son de una persona (grupos, propios, historial) no dan ninguna constancia", conCarpeta(async carpeta => {
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
    sock.recibir({ de: "5491155550001@s.whatsapp.net", mensaje: { audioMessage: {} } });

    assert.equal(backend.opcionesDeCola.consentimiento.cantidad(), 0);
    await backend.cerrar();
}));

test("con whatsapp-web.js la cola es la de siempre: sin consentimiento ni topes", async () => {
    const enviados = [];
    const backendWebjs = { enviar: async (telefono, texto) => { enviados.push([telefono, texto]); } };      // no trae opcionesDeCola
    const cola = crearColaParaBackend(backendWebjs, { info() {}, error() {} });
    assert.equal(cola.encolar("1155550001", "hola").ok, true, "se acepta a cualquiera, como hasta ahora");
    await esperarHasta(() => enviados.length === 1, { descripcion: "el envío" });
    assert.deepEqual(enviados, [["1155550001", "hola"]]);
    assert.deepEqual(cola.resumen(), { pendientes: 0 });
    cola.detener();
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

test("el backend baileys cumple la interfaz que usa el servidor, y además informa su salud", () => {
    for (const metodo of INTERFAZ) assert.equal(typeof ServicioBaileys.prototype[metodo], "function", metodo);
    assert.equal(typeof ServicioBaileys.prototype.salud, "function");
});

test("el backend de whatsapp-web.js (el de siempre) cumple la misma interfaz y no tiene salud() propia: /salud sigue siendo {ok:true}", () => {
    const { ServicioWhatsApp } = require("../whatsapp");   // carga la librería, pero no crea ningún cliente
    for (const metodo of INTERFAZ) assert.equal(typeof ServicioWhatsApp.prototype[metodo], "function", metodo);
    assert.equal(ServicioWhatsApp.prototype.salud, undefined);
    assert.equal(ServicioWhatsApp.prototype.vincular, undefined);
});
