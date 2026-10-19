// Etapa 6: privacidad en TODOS los logs del servicio. Tres capas de prueba:
//   1. el filtro (crearLogSeguro / logParaBackend) y la máscara de números, sueltos;
//   2. una guardia sobre el código fuente: ninguna llamada de log mete en el mensaje un teléfono, un texto, el QR, una credencial o
//      un secreto sin enmascarar, y nadie escribe en la consola por fuera de logs.js y vincular.js;
//   3. escenarios completos con datos "marcados" (teléfonos, textos de mensajes, claves, QR, secreto compartido): se recorre el
//      servicio, la cola, el HTTP y el comando, y al final el log entero no puede contener ni un marcador ni un teléfono sin enmascarar.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { ocultarNumeros, crearLogSeguro } = require("../privacidad");
const { logParaBackend, secretosDelQr, crearLogDelProceso } = require("../logs");
const { crearReenviador } = require("../reenvio");
const { crearBackend } = require("../backend");
const { crearColaParaBackend } = require("../cola");
const { crearApp, escuchar } = require("../servidor");
const { leerConfigEnvio } = require("../config");
const { ejecutarVinculacion } = require("../vincular");
const { fachadaFalsa, DisconnectReason } = require("./helpers/baileys-falso");
const { crearEntorno } = require("./helpers/entorno-baileys");
const { RelojFalso, esperarHasta, drenar } = require("./helpers/reloj-falso");
const { extraerLlamadasDeLog, mencionaAlgoSensible } = require("./helpers/llamadas-de-log");

// Datos de mentira con marcadores que se reconocen a simple vista. NADA de esto puede aparecer en un log.
const M = {
    telefonoCompleto: "5491122334455",
    telefonoApi: "1122334455",
    telefonoConFormato: "11 2233-4455",
    otroCompleto: "5491166667777",
    nuestroNumero: "5491177776666",
    texto: "TEXTO-PRIVADO-DEL-PACIENTE-ABC",
    saliente: "RECORDATORIO-PRIVADO-SALIENTE-DEF",
    claveNoise: "MARCADOR-CLAVE-NOISE-PRIVADA",
    adv: "MARCADOR-SECRETO-ADV-GHI",
    qr1: "REF-MARCADOR-UNO-XXXXXXXXXXXX,CLAVE-NOISE-PUBLICA-MARCADOR-UNO,CLAVE-IDENTIDAD-MARCADOR-UNO,SECRETO-ADV-MARCADOR-UNO",
    qr2: "REF-MARCADOR-DOS-XXXXXXXXXXXX,CLAVE-NOISE-PUBLICA-MARCADOR-DOS,CLAVE-IDENTIDAD-MARCADOR-DOS,SECRETO-ADV-MARCADOR-DOS",
    secreto: "SECRETO-COMPARTIDO-MARCADOR-JKL-0123",
    nombrePerfil: "NOMBRE-PERFIL-MARCADOR-PQR"        // el nombre del perfil de WhatsApp (D5): nunca en un log
};
const CREDS_CON_MARCADORES = { noiseKey: { private: M.claveNoise, public: "pub" }, advSecretKey: M.adv };
const PROHIBIDOS = [M.telefonoCompleto, M.telefonoApi, M.telefonoConFormato, M.otroCompleto, M.nuestroNumero, "2233-4455", "5491122334455",
    M.texto, M.saliente, M.claveNoise, M.adv, M.secreto, M.nombrePerfil, "MARCADOR"];

// Ninguno de los datos marcados puede estar en el texto. (Un archivo de datos tiene fechas en milisegundos, que parecen teléfonos.)
function sinMarcadores(texto, descripcion) {
    for (const prohibido of PROHIBIDOS) assert.equal(texto.includes(prohibido), false, `${descripcion} no puede incluir "${prohibido}"`);
}

// Un LOG, además, no puede tener nada con forma de teléfono sin enmascarar.
function sinNadaSensible(log, descripcion = "el log") {
    assert.equal(ocultarNumeros(log), log, `${descripcion} tiene algo con forma de teléfono sin enmascarar`);
    sinMarcadores(log, descripcion);
}

// ======================================================================================
// 1. El filtro y la máscara
// ======================================================================================

test("la versión de WhatsApp Web no se enmascara (sirve para diagnosticar y no es de nadie); un teléfono sí", () => {
    assert.equal(ocultarNumeros("Versión que se usa: 2.3000.1037753511."), "Versión que se usa: 2.3000.1037753511.");
    assert.equal(ocultarNumeros("Versión 2.3000.1037753511 (no se pudo consultar)"), "Versión 2.3000.1037753511 (no se pudo consultar)");
    assert.equal(ocultarNumeros("a 5491122334455 y 1122334455"), "a ***4455 y ***4455");
    assert.equal(ocultarNumeros("11.2233.4455"), "***4455", "un teléfono con puntos no es una versión");
});

test("crearLogSeguro tapa los secretos y enmascara los teléfonos, en los dos niveles", () => {
    const salida = [];
    const log = crearLogSeguro({ info: m => salida.push(`I ${m}`), error: m => salida.push(`E ${m}`) }, { secretos: () => [M.secreto] });
    log.info(`la cabecera era ${M.secreto} y el paciente ${M.telefonoCompleto}`);
    log.error(`${M.secreto}${M.secreto} ${M.telefonoApi}`);
    assert.deepEqual(salida, ["I la cabecera era [secreto] y el paciente ***4455", "E [secreto][secreto] ***4455"]);
});

test("los secretos se tapan ANTES de enmascarar: uno con dígitos y guiones no queda a medias", () => {
    const salida = [];
    const log = crearLogSeguro({ info: m => salida.push(m), error: m => salida.push(m) }, { secretos: () => ["abc-1234-5678-9012-xyz"] });
    log.info("clave: abc-1234-5678-9012-xyz");
    assert.deepEqual(salida, ["clave: [secreto]"]);
});

test("los secretos son una función: lo que cambia (el QR vigente) se tapa desde que existe, y lo corto o vacío se ignora", () => {
    const salida = [];
    let vivo = null;
    const log = crearLogSeguro({ info: m => salida.push(m), error: m => salida.push(m) }, { secretos: () => [vivo, "corto", "", undefined, 12345678] });
    log.info("antes: nada que tapar, corto");
    vivo = "QR-VIGENTE-12345";
    log.info("ahora: QR-VIGENTE-12345");
    assert.deepEqual(salida, ["antes: nada que tapar, corto", "ahora: [secreto]"]);
});

test("el QR se tapa entero y también por partes (la referencia y las tres claves)", () => {
    assert.deepEqual(secretosDelQr(null), []);
    assert.deepEqual(secretosDelQr(""), []);
    const partes = secretosDelQr(M.qr1);
    assert.equal(partes[0], M.qr1);
    assert.ok(partes.length === 5 && partes.every(p => p.length >= 20));
    const salida = [];
    const log = logParaBackend({ secreto: M.secreto }, { info: m => salida.push(m), error: m => salida.push(m) }, () => [M.qr1]);
    log.error(`falló con ${M.qr1.split(",")[1]} adentro`);
    log.info(M.qr1);
    assert.deepEqual(salida, ["falló con [secreto] adentro", "[secreto]"]);
});

test("el filtro tapa también el secreto compartido de la configuración", () => {
    const salida = [];
    const log = logParaBackend({ secreto: M.secreto }, { info: m => salida.push(m), error: m => salida.push(m) });
    log.error(`fetch failed (X-Shared-Secret: ${M.secreto})`);
    assert.deepEqual(salida, ["fetch failed (X-Shared-Secret: [secreto])"]);
});

test("el log del proceso escribe la hora y manda los errores a la salida de errores", () => {
    const lineas = [];
    const consola = { log: m => lineas.push(`out ${m}`), error: m => lineas.push(`err ${m}`) };
    const log = crearLogDelProceso({ consola, ahora: () => new Date(Date.UTC(2026, 9, 16, 15, 0, 0)) });
    log.info("hola");
    log.error("mal");
    assert.deepEqual(lineas, ["out [2026-10-16T15:00:00.000Z] hola", "err [2026-10-16T15:00:00.000Z] mal"]);
});

test("un reenvío a la API que falla con el secreto adentro del error no lo deja en el log", async () => {
    const salida = [];
    const log = logParaBackend({ secreto: M.secreto }, { info: m => salida.push(m), error: m => salida.push(m) });
    const reenviar = crearReenviador({
        apiBaseUrl: "http://127.0.0.1:5000", secreto: M.secreto, log,
        reintentosMs: [],          // un solo intento: lo que se mira es lo que queda en el log (con reintentos: ver reenvio-y-logger.test.js)
        fetchFn: async (url, opciones) => { throw new Error(`fetch failed ${url} con ${opciones.headers["X-Shared-Secret"]} para ${M.telefonoCompleto}`); }
    });
    await reenviar(M.telefonoCompleto, M.texto, { nombre: M.nombrePerfil, id: "X1", tipo: "texto" });
    assert.equal(salida.length, 1);
    sinNadaSensible(salida.join("\n"), "el log del reenvío");
    assert.match(salida[0], /\[secreto\].*\*\*\*4455/);
});

// ======================================================================================
// 2. Guardia sobre el código fuente
// ======================================================================================

const RAIZ = path.join(__dirname, "..");
const ARCHIVOS = [
    ...fs.readdirSync(RAIZ).filter(f => f.endsWith(".js")).map(f => path.join(RAIZ, f)),
    ...fs.readdirSync(path.join(RAIZ, "baileys")).filter(f => f.endsWith(".js")).map(f => path.join(RAIZ, "baileys", f))
];
const nombre = ruta => path.relative(RAIZ, ruta).replace(/\\/g, "/");

test("la guardia mira todos los archivos del servicio", () => {
    const nombres = ARCHIVOS.map(nombre);
    for (const esperado of ["index.js", "cola.js", "servidor.js", "logs.js", "consentimiento.js", "limites.js", "baileys/servicio.js", "baileys/sesion.js", "baileys/logger.js", "vincular.js"]) {
        assert.ok(nombres.includes(esperado), `falta ${esperado} en la guardia`);
    }
});

test("ninguna llamada de log mete un teléfono, un texto, un QR, credenciales o secretos sin pasar por ocultarNumeros()", () => {
    const sospechosas = [];
    for (const ruta of ARCHIVOS) {
        for (const llamada of extraerLlamadasDeLog(fs.readFileSync(ruta, "utf8"))) {
            if (mencionaAlgoSensible(llamada.codigo) && !/\bocultarNumeros\(/.test(llamada.codigo)) sospechosas.push(`${nombre(ruta)}:${llamada.linea}  ${llamada.texto.slice(0, 90)}`);
        }
    }
    assert.deepEqual(sospechosas, [], `Estas llamadas de log nombran algo sensible sin enmascarar:\n${sospechosas.join("\n")}`);
});

test("en los archivos que manejan el mensaje de una persona, ninguna llamada de log nombra el nombre del perfil (nombre, pushName)", () => {
    // almacen.js usa `nombre` para decir qué archivo es ("los consentimientos"): no es una persona. Acá solo importan los que ven el mensaje.
    const delMensaje = ["reenvio.js", "backend.js", "baileys/servicio.js", "baileys/mensajes.js"];
    const hallazgos = [];
    for (const ruta of ARCHIVOS.filter(r => delMensaje.includes(nombre(r)))) {
        for (const llamada of extraerLlamadasDeLog(fs.readFileSync(ruta, "utf8"))) {
            if (/\bnombre\b|pushName/i.test(llamada.codigo)) hallazgos.push(`${nombre(ruta)}:${llamada.linea}`);
        }
    }
    assert.deepEqual(hallazgos, []);
});

test("la guardia del nombre del perfil sí ve una llamada mala: reenvio.js con `${extra.nombre}` en el log", () => {
    const [mala] = extraerLlamadasDeLog("log.error(`falló para ${extra.nombre}`);");
    assert.ok(/\bnombre\b|pushName/i.test(mala.codigo));
    const [inocua] = extraerLlamadasDeLog("log.error(`falló (el nombre del perfil nunca se loguea)`);");
    assert.equal(/\bnombre\b|pushName/i.test(inocua.codigo), false, "las palabras del texto no cuentan");
});

test("ninguna llamada de log vuelca un objeto entero (JSON.stringify, inspect): puede traer mensajes o claves", () => {
    const volcados = [];
    for (const ruta of ARCHIVOS) {
        for (const llamada of extraerLlamadasDeLog(fs.readFileSync(ruta, "utf8"))) {
            if (/JSON\.stringify|\binspect\(|util\./.test(llamada.codigo)) volcados.push(`${nombre(ruta)}:${llamada.linea}`);
        }
    }
    assert.deepEqual(volcados, []);
});

test("nadie escribe en la consola por fuera de logs.js, ni en la salida estándar por fuera de vincular.js", () => {
    const porFuera = [];
    for (const ruta of ARCHIVOS) {
        const n = nombre(ruta);
        fs.readFileSync(ruta, "utf8").split(/\r?\n/).forEach((linea, i) => {
            const codigo = linea.trim();
            if (codigo.startsWith("//") || codigo.startsWith("*")) return;
            if (n !== "logs.js" && /\bconsole\.(?:log|error|warn|info|debug)\(/.test(codigo)) porFuera.push(`${n}:${i + 1}  console`);
            if (n !== "logs.js" && n !== "vincular.js" && /process\.(?:stdout|stderr)\.write\(/.test(codigo)) porFuera.push(`${n}:${i + 1}  process.std*`);
        });
    }
    assert.deepEqual(porFuera, []);
});

test("las pruebas de la guardia: reconoce una llamada mala, deja pasar la buena y no se confunde con el texto de los mensajes", () => {
    const mala = extraerLlamadasDeLog("function f() { this._log.info(`mensaje de ${item.telefono}; fin`); }");
    assert.equal(mala.length, 1);
    assert.ok(mencionaAlgoSensible(mala[0].codigo));

    const buena = extraerLlamadasDeLog("this._log.info(ocultarNumeros(`mensaje de ${telefono}`));");
    assert.ok(mencionaAlgoSensible(buena[0].codigo) && /\bocultarNumeros\(/.test(buena[0].codigo), "nombra algo sensible, pero enmascarado");

    const solotexto = extraerLlamadasDeLog('log.error("el texto del paciente, el qr y el secreto nunca se loguean; (nunca)");');
    assert.equal(solotexto.length, 1);
    assert.equal(mencionaAlgoSensible(solotexto[0].codigo), false);

    for (const peligrosa of ["log.info(`QR: ${this._qr}`)", "log.info(`ultimo ${ultimoQr}`)", "log.error(`x ${msg.from}`)", "decir(`k ${config.secreto}`)",
        "log?.info(`c ${state.creds.me}`)", "log.info(`${huella}`)", "this._log.error(`t ${token}`)"]) {
        const [l] = extraerLlamadasDeLog(peligrosa);
        assert.ok(l && mencionaAlgoSensible(l.codigo), peligrosa);
    }
    for (const inocua of ["log.info(`estado ${estado} (${motivo})`)", "log.error(`intento ${this._plan.intentos}`)", "log.info(`Sesión borrada (${propias.length} archivos)`)"]) {
        const [l] = extraerLlamadasDeLog(inocua);
        assert.ok(l && !mencionaAlgoSensible(l.codigo), inocua);
    }
    assert.equal(extraerLlamadasDeLog("catalog.error(x); consola.log(y);").length, 0, "no toma por log lo que solo se le parece");
    const [contador] = extraerLlamadasDeLog("this._log.info(`ventana ${this._ventanasQr} de ${this._maxVentanasQr}`)");
    assert.equal(mencionaAlgoSensible(contador.codigo), false, "el contador de ventanas de QR no es el QR");
    const [qrDeVerdad] = extraerLlamadasDeLog("this._log.info(`ventana ${this._ventanasQr}: ${this._qr}`)");
    assert.ok(mencionaAlgoSensible(qrDeVerdad.codigo), "pero el QR sí");
    const multilinea = extraerLlamadasDeLog('this._log.info("a; b (c)" +\n   `d ${telefono}`);\nlog.info("otra");');
    assert.equal(multilinea.length, 2);
    assert.ok(mencionaAlgoSensible(multilinea[0].codigo));
});

// ======================================================================================
// 3. Escenarios completos con datos marcados
// ======================================================================================

// Lo que la librería real le pasa a su logger: objetos enormes con claves, mensajes y teléfonos adentro.
function ruidoDeLaLibreria(sock) {
    const logger = sock.config.logger;
    const creds = sock.config.auth.creds;
    logger.warn({ creds, key: { remoteJid: `${M.telefonoCompleto}@s.whatsapp.net` }, msg: { conversation: M.texto } }, `no se pudo con ${M.telefonoCompleto}`);
    logger.error(Object.assign(new Error(`Timed Out con ${M.telefonoCompleto}`), { data: { texto: M.texto, creds } }), "error de la librería");
    logger.error({ err: Object.assign(new Error(`fallo con ${M.telefonoApi}`), { output: { payload: { creds } } }) });
    logger.info({ creds, texto: M.texto }, "esto es info y no sale");
    logger.debug({ creds, texto: M.texto }, "esto es debug y no sale");
    logger.trace({ creds, texto: M.texto }, "esto es trace y no sale");
}

// Lo que NO tiene que salir aunque la librería lo diga (los niveles charlatanes se descartan).
const NO_SALE = /esto es (?:info|debug|trace) y no sale/;

async function siguienteSocket(e, cantidad) {
    await esperarHasta(() => e.falso.sockets.length >= cantidad, { descripcion: `el socket ${cantidad}` });
    await e.reloj.avanzar(0);
    return e.falso.ultimo;
}

test("escenario del servicio: del QR al mensaje, con cortes, cierre de sesión y bloqueo, y nada sensible en el log", async () => {
    const e = crearEntorno({ opcionesFachada: { credsIniciales: CREDS_CON_MARCADORES } });
    try {
        // Sin número: se abre la primera ventana de QR, la librería hace ruido, y la ventana vence.
        const s1 = await e.iniciar();
        s1.emitirQr(M.qr1);
        ruidoDeLaLibreria(s1);
        await e.servicio.estado();
        s1.cerrar(DisconnectReason.timedOut, "QR refs attempts ended");

        // Segunda ventana: se escanea y queda vinculado.
        await e.reloj.avanzar(2000);
        const s2 = await siguienteSocket(e, 2);
        s2.emitirQr(M.qr2);
        s2.vincularConExito(M.nuestroNumero);
        await e.reloj.avanzar(0);
        const s3 = await siguienteSocket(e, 3);
        s3.abrir(M.nuestroNumero);
        ruidoDeLaLibreria(s3);

        // Entran mensajes (el texto pasa por memoria hacia la API y nunca por el log) y salen otros.
        s3.recibir({ de: `${M.telefonoCompleto}@s.whatsapp.net`, texto: M.texto, id: "E1", nombre: M.nombrePerfil });
        s3.recibir({ de: `${M.otroCompleto}@s.whatsapp.net`, texto: "BAJA", id: "E2", nombre: M.nombrePerfil });
        s3.recibir({ de: `${M.telefonoCompleto}@s.whatsapp.net`, mensaje: { imageMessage: { caption: M.texto } }, id: "E3", nombre: M.nombrePerfil });
        s3.ev.emit("messages.upsert", { type: "notify", messages: [{ key: { remoteJid: `${M.telefonoCompleto}@s.whatsapp.net`, id: "E4" }, message: { conversation: M.texto }, extra: { creds: s3.config.auth.creds } }, null] });
        await e.servicio.enviar(M.telefonoApi, M.saliente);
        s3.errorAlEnviar = new Error(`no se pudo enviar a ${M.telefonoCompleto}`);
        await assert.rejects(() => e.servicio.enviar(M.telefonoConFormato, M.saliente), /no se pudo enviar/);
        s3.errorAlEnviar = null;
        s3.todosExisten = false;
        await assert.rejects(() => e.servicio.enviar(M.otroCompleto, M.saliente), /no tiene WhatsApp/);
        s3.todosExisten = true;

        // La creación del socket falla con un teléfono adentro del mensaje.
        // Cortes: uno común, un pedido de reinicio, y WhatsApp cierra la sesión.
        s3.cerrar(DisconnectReason.connectionClosed);
        await e.reloj.avanzar(2000);
        const s4 = await siguienteSocket(e, 4);
        s4.abrir(M.nuestroNumero);
        s4.cerrar(DisconnectReason.restartRequired);
        await e.reloj.avanzar(0);
        const s5 = await siguienteSocket(e, 5);
        s5.abrir(M.nuestroNumero);
        s5.cerrar(DisconnectReason.loggedOut);
        await esperarHasta(() => !fs.existsSync(path.join(e.dirSesion, "creds.json")), { descripcion: "el borrado de la sesión muerta" });

        // Se vuelve a vincular y después el número queda restringido.
        await e.servicio.vincular();
        const s6 = await siguienteSocket(e, 6);
        s6.emitirQr(M.qr1);
        s6.vincularConExito(M.nuestroNumero);
        await e.reloj.avanzar(0);
        const s7 = await siguienteSocket(e, 7);
        s7.abrir(M.nuestroNumero);
        s7.cerrar(DisconnectReason.forbidden);

        // Y una persona desvincula para empezar de cero.
        await e.servicio.desvincular();
        await siguienteSocket(e, 8);
        await e.servicio.cerrar();

        const log = e.logCompleto();
        assert.match(log, /WhatsApp conectado \(número \*\*\*6666\)/, "el escenario llegó hasta conectar");
        assert.match(log, /rechazó la cuenta \(403\)/, "y hasta el bloqueo");
        assert.match(log, /Baileys error/, "y la librería llegó a hacer ruido");
        assert.doesNotMatch(log, NO_SALE, "lo que la librería dice en info, debug o trace no se escribe");
        sinNadaSensible(log, "el log del servicio");
        assert.deepEqual(e.reenviados.map(r => r.telefono), [M.telefonoCompleto, M.otroCompleto, M.telefonoCompleto, M.telefonoCompleto], "los mensajes sí viajaron a la API (por memoria): el 3.º es la foto");
        assert.equal(e.extras[0].nombre, M.nombrePerfil, "y con el nombre del perfil, que es lo que no puede salir en el log");
        assert.deepEqual(e.extras.map(x => x.tipo), ["texto", "texto", "imagen", "texto"]);
        sinMarcadores(JSON.stringify(e.servicio.salud()), "GET /salud");
    } finally {
        await e.servicio.cerrar().catch(() => {});
        e.limpiar();
    }
});

test("el servicio no descarga archivos ni escribe a disco lo que una persona manda: ni fotos, ni audios, ni documentos", () => {
    const peligrosos = [];
    for (const ruta of ARCHIVOS) {
        const fuente = fs.readFileSync(ruta, "utf8");
        for (const [nombrePatron, patron] of [["descarga de media", /\bdownload(?:MediaMessage|ContentFromMessage)\b/], ["escritura de un stream", /\bcreateWriteStream\b/]]) {
            if (patron.test(fuente)) peligrosos.push(`${nombre(ruta)}: ${nombrePatron}`);
        }
    }
    assert.deepEqual(peligrosos, []);
});

test("el reenvío a la API (con sus reintentos) vive solo en memoria: no usa fs", () => {
    const fuente = fs.readFileSync(path.join(RAIZ, "reenvio.js"), "utf8");
    assert.doesNotMatch(fuente, /require\(["'](?:node:)?fs/);
});

// Los mensajes de error de una librería o del sistema pueden repetir un teléfono. Cada lugar donde el servicio los escribe tiene que
// enmascararlos POR SU CUENTA (el filtro del proceso es una red de seguridad más, pero un módulo no depende de eso).
const ERROR_CON_TELEFONO = `rechazado para ${M.telefonoCompleto} (${M.telefonoApi})`;
const CASOS_DE_ERROR = [
    ["al cerrar la sesión en WhatsApp", {}, e => { e.sock.errorAlCerrarSesion = new Error(ERROR_CON_TELEFONO); return e.servicio.desvincular(); }],
    ["al consultar la última versión de WhatsApp Web", { opcionesFachada: { errorAlBuscarVersion: new Error(ERROR_CON_TELEFONO) }, opcionesServicio: { buscarVersionWa: true } }, () => null],
    ["al guardar las credenciales", { opcionesFachada: { errorAlGuardar: new Error(ERROR_CON_TELEFONO) } }, e => { e.sock.ev.emit("creds.update", {}); return e.servicio.cerrar(); }],
    ["al crear el socket", { opcionesFachada: { errorAlCrear: new Error(ERROR_CON_TELEFONO) } }, () => null],
    ["al borrar la sesión cerrada", { sesionPersonalizada: (dir, log) => {
        const real = require("../baileys/sesion").crearSesion({ dir, log });
        return { dir, abrirAuth: f => real.abrirAuth(f), limpiar: async () => { throw new Error(ERROR_CON_TELEFONO); } };
    } }, e => { e.sock.cerrar(DisconnectReason.loggedOut); return e.reloj.avanzar(0); }],
    ["al procesar un mensaje entrante", {}, e => { e.sock.ev.emit("messages.upsert", { type: "notify", messages: [{ get key() { throw new Error(ERROR_CON_TELEFONO); } }] }); return null; }],
    ["al mostrar 'escribiendo'", { opcionesServicio: { simularEscritura: true } }, async e => { e.sock.errorEnPresencia = new Error(ERROR_CON_TELEFONO); await e.servicio.enviar(M.telefonoApi, M.saliente); }]
];

for (const [descripcion, opciones, provocar] of CASOS_DE_ERROR) {
    test(`un error ${descripcion} que repite un teléfono se escribe con el teléfono enmascarado`, async () => {
        const e = crearEntorno({ registrada: true, ...opciones });
        try {
            if (opciones.opcionesFachada?.errorAlCrear) {
                e.servicio.iniciar();
                await esperarHasta(() => e.lineas.some(l => /Error conectando/.test(l)), { descripcion: "el error al crear el socket" });
            } else if (opciones.opcionesServicio?.buscarVersionWa) {
                e.servicio.iniciar();
                await esperarHasta(() => e.lineas.some(l => /No se pudo consultar la última versión/.test(l)), { descripcion: "el error de la versión" });
            } else {
                e.sock = await e.iniciar();
                e.sock.abrir();
                await provocar(e);
                await e.reloj.avanzar(0);
                await drenar();
            }
            await e.servicio.cerrar().catch(() => {});
            const log = e.logCompleto();
            assert.match(log, /\*\*\*4455/, "el error sí quedó anotado, con el final del teléfono");
            sinNadaSensible(log, "el log");
        } finally {
            await e.servicio.cerrar().catch(() => {});
            e.limpiar();
        }
    });
}

test("escenario del servicio con una sesión ya vinculada: arranque, conexión y apagado no dejan nada sensible", async () => {
    const e = crearEntorno({ registrada: true, credsExtra: CREDS_CON_MARCADORES, opcionesFachada: { credsIniciales: CREDS_CON_MARCADORES } });
    try {
        const sock = await e.iniciar();
        sock.abrir(M.nuestroNumero);
        ruidoDeLaLibreria(sock);
        sock.ev.emit("creds.update", {});
        await e.servicio.cerrar();
        const log = e.logCompleto();
        assert.match(log, /WhatsApp conectado/);
        sinNadaSensible(log, "el log del arranque");
    } finally {
        await e.servicio.cerrar().catch(() => {});
        e.limpiar();
    }
});

test("escenario de la cola: consentimiento, baja, topes, fallas y descarte, con el log filtrado igual que en producción", async () => {
    const raiz = fs.mkdtempSync(path.join(os.tmpdir(), "yosoy-privacidad-"));
    try {
        const falso = fachadaFalsa({ credsIniciales: CREDS_CON_MARCADORES });
        const reloj = new RelojFalso();
        const lineas = [];
        const base = { info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
        let backend = null;
        const config = {
            puerto: 3002, apiBaseUrl: "http://127.0.0.1:5000", secreto: M.secreto,
            baileys: {
                sessionPath: path.join(raiz, "sesion"), versionWa: null, buscarVersionWa: false, maxVentanasQr: 2,
                envio: { ...leerConfigEnvio({}), topeDiario: 3, simularEscritura: false }
            }
        };
        const log = logParaBackend(config, base, () => [backend?.qrTexto?.()]);
        backend = await crearBackend(config, log, { cargarBaileys: async () => falso.fachada, reenviar: () => {}, reloj });
        const cola = crearColaParaBackend(backend, log);

        backend.iniciar();
        const sock = await (async () => { await esperarHasta(() => falso.sockets.length === 1, { descripcion: "el socket" }); await reloj.avanzar(0); return falso.ultimo; })();
        sock.abrir(M.nuestroNumero);
        ruidoDeLaLibreria(sock);

        // Sin constancia: se rechaza. Ella escribe: se acepta. Otra pide la baja: se rechaza con optIn y todo.
        assert.equal(cola.encolar(M.telefonoApi, M.saliente).codigo, "sin_consentimiento");
        sock.recibir({ de: `${M.telefonoCompleto}@s.whatsapp.net`, texto: M.texto, id: "A1" });
        sock.recibir({ de: `${M.otroCompleto}@s.whatsapp.net`, texto: "BAJA", id: "A2" });
        assert.equal(cola.encolar(M.otroCompleto, M.saliente, { optIn: true }).codigo, "baja");
        assert.equal(cola.encolar("123", M.saliente, { optIn: true }).codigo, "telefono_invalido");

        // Un envío falla con el teléfono adentro del error; otro sale; el tope del día se agota.
        sock.errorAlEnviar = new Error(`rate-overlimit para ${M.telefonoCompleto}: ${M.telefonoApi}`);
        assert.equal(cola.encolar(M.telefonoConFormato, M.saliente).ok, true);
        await esperarHasta(() => lineas.some(l => /Error enviando/.test(l)), { descripcion: "el error del envío" });
        sock.errorAlEnviar = null;
        for (let i = 0; i < 3; i++) cola.encolar(M.telefonoApi, M.saliente, { optIn: true });
        assert.equal(cola.encolar(M.telefonoApi, M.saliente, { optIn: true }).ok, false);

        // La conexión se cae del todo con mensajes esperando: se descartan.
        await drenar();
        sock.cerrar(DisconnectReason.forbidden);
        await reloj.avanzar(10000);                       // más que la pausa más larga entre mensajes: la cola despierta y descarta lo que espera
        cola.detener();
        await backend.datos.cerrar();
        await backend.cerrar();

        const texto = lineas.join("\n");
        assert.match(texto, /Envío rechazado \(sin_consentimiento\) a \*\*\*4455/);
        assert.match(texto, /Se descartó cola-\d+ a \*\*\*4455: WhatsApp no está disponible \(requiere_atencion\)\./, "y el descarte llegó a ocurrir");
        assert.match(texto, /Error enviando cola-\d+ a \*\*\*4455: rate-overlimit para \*\*\*4455: \*\*\*4455/);
        sinNadaSensible(texto, "el log de la cola");

        // Lo que se guardó en disco tampoco tiene teléfonos ni textos.
        for (const archivo of ["consentimientos.json", "envios.json"]) {
            const ruta = path.join(raiz, "sesion", "datos", archivo);
            if (fs.existsSync(ruta)) sinMarcadores(fs.readFileSync(ruta, "utf8"), archivo);
        }
    } finally {
        fs.rmSync(raiz, { recursive: true, force: true });
    }
});

test("escenario del HTTP: un error con teléfono y secreto adentro no sale en la respuesta ni queda sin filtrar en el log", async () => {
    const lineas = [];
    const config = { secreto: M.secreto };
    const log = logParaBackend(config, { info: m => lineas.push(m), error: m => lineas.push(m) });
    const servicio = {
        estado: async () => { throw new Error(`falló ${M.telefonoCompleto} con ${M.secreto} y ${M.texto}`); },
        desvincular: async () => { throw new Error(`no se pudo desvincular ${M.telefonoCompleto} ${M.secreto}`); },
        vincular: async () => { throw new Error(`vincular ${M.telefonoApi}`); },
        salud: () => ({ ok: true, canal: "baileys", estado: "conectado" }),
        enviar: async () => {}
    };
    const cola = { encolar: () => { throw new Error(`cola ${M.telefonoCompleto}`); }, tamano: () => 0 };
    const app = crearApp({ servicio, cola, secreto: M.secreto, log });
    const servidor = await new Promise(resolver => { const s = escuchar(app, 0, () => resolver(s)); });
    const { port } = servidor.address();
    const pedir = async (ruta, { metodo = "GET", secreto = M.secreto, cuerpo } = {}) => {
        const r = await fetch(`http://127.0.0.1:${port}${ruta}`, {
            method: metodo,
            headers: { "Content-Type": "application/json", ...(secreto ? { "X-Shared-Secret": secreto } : {}) },
            body: cuerpo === undefined ? undefined : cuerpo
        });
        return { status: r.status, texto: await r.text() };
    };
    try {
        const respuestas = [
            await pedir("/estado"),
            await pedir("/desvincular", { metodo: "POST" }),
            await pedir("/vincular", { metodo: "POST" }),
            await pedir("/enviar", { metodo: "POST", cuerpo: JSON.stringify({ telefono: M.telefonoApi, texto: M.texto }) }),
            await pedir("/enviar", { metodo: "POST", cuerpo: `{"telefono": "${M.telefonoCompleto}", "texto": "${M.texto}"` }),   // JSON roto
            await pedir("/estado", { secreto: "secreto-equivocado-que-alguien-probó" }),
            await pedir("/ruta-que-no-existe")
        ];
        await drenar();
        await new Promise(resolver => setTimeout(resolver, 20));        // lo de desvincular se loguea cuando termina
        assert.deepEqual(respuestas.map(r => r.status), [500, 200, 500, 500, 400, 401, 404]);
        for (const r of respuestas) sinNadaSensible(r.texto, `la respuesta ${r.status}`);
        const texto = lineas.join("\n");
        assert.match(texto, /Error atendiendo GET \/estado: falló \*\*\*4455 con \[secreto\] y TEXTO/);
        sinNadaSensible(texto.replace(M.texto, "TEXTO-REDACTADO-A-PROPOSITO"), "el log del HTTP");
        assert.equal(texto.includes("secreto-equivocado"), false, "el secreto que probó un desconocido tampoco se escribe");
    } finally {
        await new Promise(resolver => servidor.close(resolver));
    }
});

test("el HTTP con el log SIN filtro: igual enmascara los teléfonos de los errores que escribe", async () => {
    const lineas = [];
    const log = { info: m => lineas.push(m), error: m => lineas.push(m) };
    const servicio = {
        estado: async () => { throw new Error(`falló ${M.telefonoCompleto}`); },
        desvincular: async () => { throw new Error(`no se pudo desvincular ${M.telefonoCompleto}`); },
        salud: () => ({ ok: true }), enviar: async () => {}
    };
    const app = crearApp({ servicio, cola: { encolar: () => ({ ok: true, idExterno: "x" }), tamano: () => 0 }, secreto: M.secreto, log });
    const servidor = await new Promise(resolver => { const s = escuchar(app, 0, () => resolver(s)); });
    const { port } = servidor.address();
    try {
        const cabeceras = { "X-Shared-Secret": M.secreto, "Content-Type": "application/json" };
        await fetch(`http://127.0.0.1:${port}/estado`, { headers: cabeceras });
        await fetch(`http://127.0.0.1:${port}/desvincular`, { method: "POST", headers: cabeceras });
        await new Promise(resolver => setTimeout(resolver, 20));
        // Un error "no esperado" de Express (no uno de la ruta): se escribe con el teléfono enmascarado también.
        const texto = lineas.join("\n");
        assert.match(texto, /Error atendiendo GET \/estado: falló \*\*\*4455/);
        assert.match(texto, /Error desvinculando: no se pudo desvincular \*\*\*4455/);
        sinNadaSensible(texto, "el log del HTTP");
    } finally {
        await new Promise(resolver => servidor.close(resolver));
    }
});

test("escenario del comando de vinculación: el QR va solo a la pantalla; nada de claves ni teléfonos por la salida de errores", async () => {
    const raiz = fs.mkdtempSync(path.join(os.tmpdir(), "yosoy-privacidad-cli-"));
    try {
        const falso = fachadaFalsa({ credsIniciales: CREDS_CON_MARCADORES });
        const reloj = new RelojFalso();
        const salida = [];
        const errores = [];
        const pasos = [
            async () => {
                await esperarHasta(() => falso.sockets.length >= 1, { descripcion: "el socket 1" });
                falso.sockets[0].emitirQr(M.qr1);
                ruidoDeLaLibreria(falso.sockets[0]);
                // Un mensaje de la librería que repite el QR vigente (no debería, pero si pasara, no puede llegar a la salida de errores).
                falso.sockets[0].config.logger.error(new Error(`fallo raro con ${M.qr1} adentro`));
            },
            async () => { falso.sockets[0].emitirQr(M.qr2); },
            async () => { falso.sockets[0].vincularConExito(M.nuestroNumero); },
            async () => { await esperarHasta(() => falso.sockets.length >= 2, { descripcion: "el socket 2" }); falso.sockets[1].abrir(M.nuestroNumero); }
        ];
        const codigo = await ejecutarVinculacion({
            env: { WHATSAPP_BAILEYS_SESSION_PATH: path.join(raiz, "sesion") },
            stdout: { write: s => salida.push(s) }, stderr: { write: s => errores.push(s) },
            esTTY: true, uid: 1000, hayServicio: async () => false, cargarBaileys: async () => falso.fachada,
            dibujar: qr => `[dibujo de ${qr.split(",")[0]}]`, reloj,
            esperar: async ms => { const paso = pasos.shift(); if (paso) await paso(); await reloj.avanzar(ms); }, consultaMs: 500
        });
        assert.equal(codigo, 0);
        assert.match(salida.join(""), /\[dibujo de REF-MARCADOR-UNO/);
        const erroresTexto = errores.join("");
        assert.match(erroresTexto, /Listo: el número \*\*\*6666 quedó vinculado/);
        sinNadaSensible(erroresTexto, "la salida de errores del comando");
        for (const parte of [...secretosDelQr(M.qr1), ...secretosDelQr(M.qr2)]) assert.equal(erroresTexto.includes(parte), false, `el QR (${parte.slice(0, 20)}...) no puede ir por la salida de errores`);
        assert.match(erroresTexto, /Baileys error: fallo raro con \[secreto\] adentro/, "el mensaje de la librería salió, con el QR tapado");
        assert.doesNotMatch(erroresTexto, NO_SALE);
    } finally {
        fs.rmSync(raiz, { recursive: true, force: true });
    }
});
