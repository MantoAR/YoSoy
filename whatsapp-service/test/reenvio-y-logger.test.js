// Reenvío de entrantes a la API y logger que se le pasa a Baileys: sin red (fetch falso) y sin la librería real.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const { crearReenviador } = require("../reenvio");
const { crearLoggerBaileys, textoSeguro } = require("../baileys/logger");

function logCapturado() {
    const lineas = [];
    return { lineas, info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
}

// ---------- Reenvío ----------

test("reenvía telefono y texto a la API con el secreto compartido", async () => {
    const pedidos = [];
    const log = logCapturado();
    const reenviar = crearReenviador({
        apiBaseUrl: "http://127.0.0.1:5000/", secreto: "S3CRETO", log,
        fetchFn: async (url, opciones) => { pedidos.push({ url, opciones }); return { ok: true, status: 200, arrayBuffer: async () => new ArrayBuffer(0) }; }
    });

    await reenviar("5491122334455", "hola, quiero un turno");

    assert.equal(pedidos.length, 1);
    assert.equal(pedidos[0].url, "http://127.0.0.1:5000/mensajeria/whatsapp/entrante");
    assert.equal(pedidos[0].opciones.method, "POST");
    assert.equal(pedidos[0].opciones.headers["X-Shared-Secret"], "S3CRETO");
    assert.deepEqual(JSON.parse(pedidos[0].opciones.body), { telefono: "5491122334455", texto: "hola, quiero un turno" });
    assert.deepEqual(log.lineas, []);
});

test("si la API contesta con error, el log dice el estado y nada del mensaje", async () => {
    const log = logCapturado();
    const reenviar = crearReenviador({
        apiBaseUrl: "http://127.0.0.1:5000", secreto: "S3CRETO", log,
        fetchFn: async () => ({ ok: false, status: 404, arrayBuffer: async () => new ArrayBuffer(0) })
    });

    await reenviar("5491122334455", "texto privado del paciente");

    assert.deepEqual(log.lineas, ["ERROR La API respondió 404 al mensaje entrante."]);
});

test("si no se puede contactar a la API, el log no trae el texto ni el teléfono completo", async () => {
    const log = logCapturado();
    const reenviar = crearReenviador({
        apiBaseUrl: "http://127.0.0.1:5000", secreto: "S3CRETO", log,
        fetchFn: async () => { throw new Error("connect ECONNREFUSED al reenviar a 5491122334455"); }
    });

    await reenviar("5491122334455", "texto privado del paciente");

    assert.equal(log.lineas.length, 1);
    assert.match(log.lineas[0], /No se pudo reenviar el mensaje entrante a la API/);
    assert.match(log.lineas[0], /\*\*\*4455/);
    assert.doesNotMatch(log.lineas[0], /5491122334455|texto privado|S3CRETO/);
});

test("el reenvío tiene un tope de tiempo: aborta el pedido", async () => {
    let senal;
    let vence;
    const log = logCapturado();
    const reenviar = crearReenviador({
        apiBaseUrl: "http://127.0.0.1:5000", secreto: "S3CRETO", log, timeoutMs: 1234,
        programar: (fn, ms) => { vence = { fn, ms }; return 7; },
        cancelar: () => {},
        fetchFn: (url, opciones) => { senal = opciones.signal; return new Promise((_, rechazar) => senal.addEventListener("abort", () => rechazar(new Error("abortado")))); }
    });

    const pendiente = reenviar("5491122334455", "hola");
    assert.equal(vence.ms, 1234);
    vence.fn();
    await pendiente;

    assert.ok(senal.aborted);
    assert.match(log.lineas[0], /abortado/);
});

// ---------- Logger de Baileys ----------

test("el logger de Baileys cumple la interfaz que la librería usa (level, child y un método por nivel)", () => {
    const logger = crearLoggerBaileys(logCapturado());
    assert.equal(typeof logger.level, "string");
    for (const nivel of ["trace", "debug", "info", "warn", "error"]) assert.equal(typeof logger[nivel], "function", nivel);
    assert.equal(logger.child({ class: "ns" }), logger);
});

test("trace, debug e info se descartan; warn y error salen al log con los números enmascarados", () => {
    const log = logCapturado();
    const logger = crearLoggerBaileys(log);

    logger.trace("detalle"); logger.debug("detalle"); logger.info({ jid: "5491122334455@s.whatsapp.net" }, "conectado");
    assert.deepEqual(log.lineas, []);

    logger.warn("aviso de la librería sobre 5491122334455");
    logger.error({ err: new Error("falló con 5491122334455") }, "no se pudo");
    assert.equal(log.lineas.length, 2);
    assert.equal(log.lineas[0], "ERROR Baileys warn: aviso de la librería sobre ***4455");
    assert.equal(log.lineas[1], "ERROR Baileys error: no se pudo — falló con ***4455");
});

test("del objeto que acompaña a una línea NUNCA se escribe nada, salvo el message del error", () => {
    const log = logCapturado();
    const logger = crearLoggerBaileys(log);

    logger.error({ key: { remoteJid: "5491122334455@s.whatsapp.net" }, message: { conversation: "texto privado" }, creds: { noiseKey: "CLAVE" } }, "falló un mensaje");

    assert.equal(log.lineas.length, 1);
    assert.equal(log.lineas[0], "ERROR Baileys error: falló un mensaje");
    assert.doesNotMatch(log.lineas[0], /texto privado|CLAVE|remoteJid|5491122334455/);
});

test("una línea larga se corta a 300 caracteres y una vacía no rompe nada", () => {
    assert.equal(textoSeguro("x".repeat(1000)).length, 300);
    assert.equal(textoSeguro(undefined), "(sin texto)");
    assert.equal(textoSeguro({}, undefined), "(sin texto)");
    assert.equal(textoSeguro({ err: { message: "solo el detalle" } }), "solo el detalle");
    assert.equal(textoSeguro(new Error("un error suelto")), "un error suelto", "un Error como primer argumento aporta su mensaje");
    assert.equal(textoSeguro(new Error("el error"), "y el texto"), "y el texto — el error");
});

test("tope de líneas por minuto: una falla en bucle no llena el journal, y se avisa cuántas se omitieron", () => {
    let t = 0;
    const log = logCapturado();
    const logger = crearLoggerBaileys(log, { maxPorMinuto: 3, ahora: () => t });

    for (let i = 0; i < 10; i++) logger.error(`falla ${i}`);
    assert.equal(log.lineas.length, 3);

    t = 61000;
    logger.error("falla nueva");
    assert.equal(log.lineas.length, 5);
    assert.match(log.lineas[3], /se omitieron 7 líneas/);
    assert.match(log.lineas[4], /falla nueva/);
});
