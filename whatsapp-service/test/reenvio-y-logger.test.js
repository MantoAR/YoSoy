// Reenvío de entrantes a la API y logger que se le pasa a Baileys: sin red (fetch falso) y sin la librería real.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const { crearReenviador, REINTENTOS_MS, MAX_EN_COLA } = require("../reenvio");
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
        reintentosMs: [],          // un solo intento: lo que se mira es lo que queda en el log
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
        apiBaseUrl: "http://127.0.0.1:5000", secreto: "S3CRETO", log, timeoutMs: 1234, reintentosMs: [],
        programar: (fn, ms) => { vence = { fn, ms }; return 7; },
        cancelar: () => {},
        fetchFn: (url, opciones) => { senal = opciones.signal; return new Promise((_, rechazar) => senal.addEventListener("abort", () => rechazar(new Error("abortado")))); }
    });

    const pendiente = reenviar("5491122334455", "hola");
    await new Promise(resolver => setImmediate(resolver));      // el primer intento arranca en la próxima vuelta (cada persona tiene su cadena)
    assert.equal(vence.ms, 1234);
    vence.fn();
    await pendiente;

    assert.ok(senal.aborted);
    assert.match(log.lineas[0], /abortado/);
});

// ---------- Contrato v2 y reintentos en memoria (D5 y D6) ----------

const TIMEOUT = 777;                         // distinto de cualquier espera entre reintentos: así el reloj falso distingue el tope de un pedido de una espera

// Un reloj falso para el reenviador: guarda lo que se programa y lo dispara a mano. `programar` sirve para el tope de tiempo de cada pedido y para la espera entre reintentos.
function relojDeReintentos() {
    const tareas = [];
    return {
        tareas,
        programar: (fn, ms) => { const t = { fn, ms, activa: true }; tareas.push(t); return t; },
        cancelar: t => { if (t) t.activa = false; },
        esperas: () => tareas.filter(t => t.activa && t.ms !== TIMEOUT),
        // Dispara la espera más vieja que siga pendiente. Devuelve cuántos ms esperaba.
        async pasar() {
            const t = tareas.find(x => x.activa && x.ms !== TIMEOUT);
            assert.ok(t, "no había ninguna espera pendiente");
            t.activa = false;
            t.fn();
            await vaciar();
            return t.ms;
        }
    };
}

// Deja avanzar las promesas pendientes (los reintentos son una cadena de awaits).
async function vaciar() { for (let i = 0; i < 25; i++) await new Promise(resolver => setImmediate(resolver)); }

const respuesta = estado => ({ ok: estado >= 200 && estado < 300, status: estado, arrayBuffer: async () => new ArrayBuffer(0) });

// Un fetch que contesta lo que se le dice, en orden (el último se repite), y anota cada cuerpo que recibe.
function apiQueContesta(...respuestas) {
    const cuerpos = [];
    let i = 0;
    const fetchFn = async (url, opciones) => {
        cuerpos.push(JSON.parse(opciones.body));
        const r = respuestas[Math.min(i++, respuestas.length - 1)];
        if (r instanceof Error) throw r;
        return respuesta(r);
    };
    return { fetchFn, cuerpos, get llamadas() { return i; } };
}

function armar({ api, reintentosMs, maxEnCola, reloj = relojDeReintentos() }) {
    const log = logCapturado();
    const reenviar = crearReenviador({
        apiBaseUrl: "http://127.0.0.1:5000", secreto: "S3CRETO", log, fetchFn: api.fetchFn, timeoutMs: TIMEOUT,
        programar: reloj.programar, cancelar: reloj.cancelar, ...(reintentosMs ? { reintentosMs } : {}), ...(maxEnCola ? { maxEnCola } : {})
    });
    return { reenviar, log, reloj };
}

const TEL = "5491122334455";
const TEXTO = "texto privado del paciente";

test("el cuerpo v2 lleva tipo, id, ts y nombre cuando vienen, y SOLO esos campos", async () => {
    const api = apiQueContesta(200);
    const { reenviar } = armar({ api });

    await reenviar(TEL, "[Imagen] mi receta", { tipo: "imagen", id: "A1", ts: 1760000000, nombre: "Juan Pérez", otraCosa: "no viaja", telefono: "pisado" });

    assert.deepEqual(api.cuerpos, [{ telefono: TEL, texto: "[Imagen] mi receta", tipo: "imagen", id: "A1", ts: 1760000000, nombre: "Juan Pérez" }]);
});

test("lo que no viene (undefined o null) no está en el cuerpo, y sin extras es el cuerpo de siempre", async () => {
    const api = apiQueContesta(200);
    const { reenviar } = armar({ api });

    await reenviar(TEL, "hola", { tipo: "texto", id: null, ts: undefined });
    await reenviar(TEL, "hola");

    assert.deepEqual(api.cuerpos, [{ telefono: TEL, texto: "hola", tipo: "texto" }, { telefono: TEL, texto: "hola" }]);
});

test("un 503 se reintenta a los 5 s y el mensaje llega: el cuerpo es el mismo (mismo id) y se cuenta como reenviado y reintentado", async () => {
    const api = apiQueContesta(503, 200);
    const { reenviar, log, reloj } = armar({ api });

    const terminado = reenviar(TEL, TEXTO, { tipo: "texto", id: "A1", nombre: "Juan" });
    await vaciar();
    assert.equal(api.llamadas, 1);
    assert.equal(reenviar.resumen().pendientes, 1);
    assert.deepEqual(reloj.esperas().map(t => t.ms), [5000]);

    assert.equal(await reloj.pasar(), 5000);
    await terminado;

    assert.equal(api.llamadas, 2);
    assert.deepEqual(api.cuerpos[0], api.cuerpos[1], "el reintento manda exactamente lo mismo: la API reconoce el id");
    assert.deepEqual(reenviar.resumen(), { reenviados: 1, reintentados: 1, fallidos: 0, perdidos: 0, pendientes: 0 });
    assert.deepEqual(log.lineas, ["ERROR La API respondió 503 al mensaje entrante. Se reintenta (1 de 3)."]);
});

test("las esperas son de 5 s, 30 s y 2 minutos, y tras el 3.er reintento fallido el mensaje se pierde (y se dice, sin su texto)", async () => {
    const api = apiQueContesta(500);
    const { reenviar, log, reloj } = armar({ api });

    const terminado = reenviar(TEL, TEXTO, { tipo: "texto", id: "A1", nombre: "Zoraida Xylofona" });
    await vaciar();
    const esperas = [];
    for (let i = 0; i < 3; i++) esperas.push(await reloj.pasar());
    await terminado;

    assert.deepEqual(esperas, [5000, 30000, 120000]);
    assert.equal(api.llamadas, 4, "el intento de siempre y tres reintentos");
    assert.deepEqual(reenviar.resumen(), { reenviados: 0, reintentados: 3, fallidos: 0, perdidos: 1, pendientes: 0 });
    assert.equal(log.lineas.length, 4);
    assert.match(log.lineas[0], /Se reintenta \(1 de 3\)/);
    assert.match(log.lineas[3], /Se perdió el mensaje \(4 intentos\)/);
    assert.equal(reloj.esperas().length, 0, "no queda nada programado");
    assert.doesNotMatch(log.lineas.join("\n"), /texto privado|Zoraida|Xylofona|5491122334455|S3CRETO/);
});

test("las esperas por defecto son las de REINTENTOS_MS", () => {
    assert.deepEqual([...REINTENTOS_MS], [5000, 30000, 120000]);
    assert.equal(MAX_EN_COLA, 200);
});

test("un error de red o un tope de tiempo se reintentan, igual que un 5xx y un 429", async () => {
    for (const falla of [new Error("connect ECONNREFUSED 127.0.0.1:5000"), new Error("This operation was aborted"), 500, 502, 504, 429]) {
        const api = apiQueContesta(falla, 200);
        const { reenviar, reloj } = armar({ api });

        const terminado = reenviar(TEL, "hola", { id: "A1" });
        await vaciar();
        await reloj.pasar();
        await terminado;

        assert.equal(api.llamadas, 2, String(falla));
        assert.equal(reenviar.resumen().reenviados, 1, String(falla));
    }
});

test("un 4xx (pedido mal formado, secreto que no coincide, demasiado grande) NO se reintenta: es un fallido y no queda nada esperando", async () => {
    for (const estado of [400, 401, 403, 404, 413, 422]) {
        const api = apiQueContesta(estado);
        const { reenviar, log, reloj } = armar({ api });

        await reenviar(TEL, TEXTO, { id: "A1" });

        assert.equal(api.llamadas, 1, String(estado));
        assert.deepEqual(reenviar.resumen(), { reenviados: 0, reintentados: 0, fallidos: 1, perdidos: 0, pendientes: 0 }, String(estado));
        assert.deepEqual(log.lineas, [`ERROR La API respondió ${estado} al mensaje entrante.`]);
        assert.equal(reloj.esperas().length, 0);
    }
});

test("sin reintentos configurados un fallo pasajero es una pérdida inmediata", async () => {
    const api = apiQueContesta(503);
    const { reenviar, log } = armar({ api, reintentosMs: [] });

    await reenviar(TEL, "hola");

    assert.equal(api.llamadas, 1);
    assert.equal(reenviar.resumen().perdidos, 1);
    assert.deepEqual(log.lineas, ["ERROR La API respondió 503 al mensaje entrante. Se perdió el mensaje (1 intento)."]);
});

test("los mensajes de una misma persona salen de a uno y en orden: mientras uno espera su reintento, los siguientes esperan atrás", async () => {
    const api = apiQueContesta(503, 200);                    // el primer intento del primer mensaje falla; después todo anda
    const { reenviar, reloj } = armar({ api });

    const uno = reenviar(TEL, "uno", { id: "A1" });
    const dos = reenviar(TEL, "dos", { id: "A2" });
    const tres = reenviar(TEL, "tres", { id: "A3" });
    await vaciar();
    assert.deepEqual(api.cuerpos.map(c => c.id), ["A1"], "el 2 y el 3 todavía no salieron");
    assert.equal(reenviar.resumen().pendientes, 3);

    await reloj.pasar();
    await Promise.all([uno, dos, tres]);

    assert.deepEqual(api.cuerpos.map(c => c.id), ["A1", "A1", "A2", "A3"], "el reintento del 1, y después el 2 y el 3 en orden");
    assert.deepEqual(reenviar.resumen(), { reenviados: 3, reintentados: 1, fallidos: 0, perdidos: 0, pendientes: 0 });
});

test("lo de una persona no frena a otra: el mensaje de otro teléfono sale aunque el primero esté esperando", async () => {
    const api = apiQueContesta(503, 200);
    const { reenviar, reloj } = armar({ api });

    const deJuan = reenviar("5491122334455", "de Juan", { id: "J1" });
    const deAna = reenviar("5491166667777", "de Ana", { id: "N1" });
    await vaciar();

    assert.deepEqual(api.cuerpos.map(c => c.id), ["J1", "N1"], "Ana no espera a Juan");
    await reloj.pasar();
    await Promise.all([deJuan, deAna]);
    assert.equal(reenviar.resumen().reenviados, 2);
});

test("un mensaje que se pierde no bloquea a los que vienen detrás de la misma persona", async () => {
    const api = apiQueContesta(500, 500, 500, 500, 200);     // el 1.º agota sus 4 intentos; el 2.º entra al primero
    const { reenviar, reloj } = armar({ api });

    const uno = reenviar(TEL, "uno", { id: "A1" });
    const dos = reenviar(TEL, "dos", { id: "A2" });
    await vaciar();
    for (let i = 0; i < 3; i++) await reloj.pasar();
    await Promise.all([uno, dos]);

    assert.deepEqual(reenviar.resumen(), { reenviados: 1, reintentados: 3, fallidos: 0, perdidos: 1, pendientes: 0 });
    assert.equal(api.cuerpos.at(-1).id, "A2");
});

test("hay un tope de mensajes en espera: el que no entra se pierde, se cuenta y el log no trae su texto ni su teléfono", async () => {
    const api = apiQueContesta(503);
    const { reenviar, log } = armar({ api, maxEnCola: 2 });

    reenviar("5491100000001", "uno");
    reenviar("5491100000002", "dos");
    const tercero = reenviar("5491100000003", TEXTO, { nombre: "Zoraida Xylofona" });
    await tercero;
    await vaciar();

    assert.equal(api.llamadas, 2, "el 3.º ni salió");
    assert.deepEqual(reenviar.resumen(), { reenviados: 0, reintentados: 2, fallidos: 0, perdidos: 1, pendientes: 2 });
    assert.ok(log.lineas.some(l => /Se perdió un mensaje entrante: ya hay 2 esperando/.test(l)));
    assert.doesNotMatch(log.lineas.join("\n"), /texto privado|Zoraida|Xylofona|549110000000/);
});

test("al apagar (detener) se cortan las esperas, se dice cuántos mensajes quedaban y no se reenvía más nada", async () => {
    const api = apiQueContesta(503);
    const { reenviar, reloj } = armar({ api });

    const esperando = reenviar(TEL, "uno", { id: "A1" });
    const detras = reenviar(TEL, "dos", { id: "A2" });
    await vaciar();
    assert.equal(reenviar.resumen().pendientes, 2);

    assert.equal(reenviar.detener(), 2, "cuántos quedaban sin reenviar");
    await Promise.all([esperando, detras]);

    assert.equal(api.llamadas, 1, "ni el reintento ni el 2.º salieron");
    assert.deepEqual(reenviar.resumen(), { reenviados: 0, reintentados: 1, fallidos: 0, perdidos: 2, pendientes: 0 });
    assert.equal(reloj.esperas().length, 0, "no queda ningún temporizador");

    await reenviar(TEL, "después de apagar");
    assert.equal(api.llamadas, 1);
    assert.equal(reenviar.resumen().perdidos, 3);
});

test("resumen() solo tiene números: nada de teléfonos, textos ni nombres", async () => {
    const api = apiQueContesta(503, 200);
    const { reenviar, reloj } = armar({ api });
    const terminado = reenviar(TEL, TEXTO, { id: "A1", nombre: "Zoraida Xylofona" });
    await vaciar();

    const resumen = reenviar.resumen();

    assert.deepEqual(Object.keys(resumen).sort(), ["fallidos", "pendientes", "perdidos", "reenviados", "reintentados"]);
    assert.ok(Object.values(resumen).every(Number.isInteger));
    assert.doesNotMatch(JSON.stringify(resumen), /5491122334455|texto privado|Zoraida/);
    await reloj.pasar();
    await terminado;
});

test("los reintentos son solo memoria: el módulo no toca el disco", () => {
    const fuente = require("node:fs").readFileSync(require.resolve("../reenvio"), "utf8");

    assert.doesNotMatch(fuente, /require\(["'](?:node:)?fs(?:\/promises)?["']\)/);
    assert.doesNotMatch(fuente, /\b(?:writeFile|appendFile|createWriteStream|writeFileSync|appendFileSync)\b/);
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
