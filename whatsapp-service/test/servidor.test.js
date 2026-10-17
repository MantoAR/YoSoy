// La app HTTP (servidor.js): la interfaz que usa la API de YoSoy. Servidor real en 127.0.0.1 con un backend y una cola falsos.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const { crearApp, escuchar, secretoValido, MAX_TEXTO } = require("../servidor");
const { ColaEnvios } = require("../cola");

const SECRETO = "secreto-compartido-de-prueba-0123456789";

function crearLog() {
    const lineas = [];
    return { lineas, info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
}

function servicioFalso(extra = {}) {
    return {
        llamadas: { desvincular: 0, vincular: 0 },
        async estado() { return { conectado: true, numero: "5491100000000", qrPendiente: false, qr: null }; },
        async desvincular() { this.llamadas.desvincular++; },
        async enviar() {},
        ...extra
    };
}

// Levanta la app en un puerto libre de loopback y devuelve una función para pedirle cosas.
async function conServidor({ servicio = servicioFalso(), cola, log = crearLog(), uptime } = {}, cuerpo) {
    const colaUsada = cola ?? new ColaEnvios(async () => {}, log);
    const app = crearApp({ servicio, cola: colaUsada, secreto: SECRETO, log, ...(uptime ? { uptime } : {}) });
    const servidor = await new Promise(resolver => { const s = escuchar(app, 0, () => resolver(s)); });
    const { port, address } = servidor.address();
    const pedir = (ruta, { metodo = "GET", secreto = SECRETO, cuerpo: body, crudo } = {}) => fetch(`http://127.0.0.1:${port}${ruta}`, {
        method: metodo,
        headers: { ...(secreto ? { "X-Shared-Secret": secreto } : {}), ...(body !== undefined || crudo !== undefined ? { "Content-Type": "application/json" } : {}) },
        body: crudo !== undefined ? crudo : (body !== undefined ? JSON.stringify(body) : undefined)
    });
    try { await cuerpo({ pedir, servicio, cola: colaUsada, log, address }); }
    finally { colaUsada.detener(); await new Promise(resolver => servidor.close(resolver)); }
}

// ---------- Salud ----------

test("GET /salud sin secreto y con un backend sin salud() responde {ok:true}, igual que siempre", async () => {
    await conServidor({}, async ({ pedir }) => {
        const r = await pedir("/salud", { secreto: null });
        assert.equal(r.status, 200);
        assert.deepEqual(await r.json(), { ok: true });
    });
});

test("GET /salud con un backend que informa su estado: 200 si está bien y 503 si no, con el estado, el tiempo en marcha y la cola", async () => {
    let salud = { ok: true, canal: "baileys", estado: "conectado" };
    await conServidor({ servicio: servicioFalso({ salud: () => salud }), uptime: () => 41.6 }, async ({ pedir }) => {
        let r = await pedir("/salud", { secreto: null });
        assert.equal(r.status, 200);
        assert.deepEqual(await r.json(), { ...salud, uptimeSegundos: 42, cola: { pendientes: 0 } });

        salud = { ok: false, canal: "baileys", estado: "requiere_vincular", motivo: "sesion_cerrada" };
        r = await pedir("/salud", { secreto: null });
        assert.equal(r.status, 503);
        assert.equal((await r.json()).estado, "requiere_vincular");
    });
});

test("GET /salud no se cachea, no pide secreto y no trae teléfonos ni textos", async () => {
    const servicio = servicioFalso({
        salud: () => ({ ok: false, canal: "baileys", estado: "reconectando", motivo: null, desde: "2026-10-16T15:00:00.000Z", conectado: false, qrPendiente: false,
            requiereVincular: false, requiereAtencion: false, reconectando: true, intentosDeReconexion: 3 })
    });
    const cola = { tamano: () => 2, detener() {}, resumen: () => ({ pendientes: 2, dia: "2026-10-16", enviadosHoy: 7, enEspera: 2, topeDiario: 100, enviadosUltimaHora: 7, topePorHora: 40, topePorDestinatario: 3 }) };
    await conServidor({ servicio, cola }, async ({ pedir }) => {
        const r = await pedir("/salud", { secreto: null });
        assert.equal(r.status, 503);
        assert.equal(r.headers.get("cache-control"), "no-store");
        const cuerpo = await r.json();
        assert.deepEqual(Object.keys(cuerpo).sort(), ["canal", "cola", "conectado", "desde", "estado", "intentosDeReconexion", "motivo", "ok", "qrPendiente",
            "reconectando", "requiereAtencion", "requiereVincular", "uptimeSegundos"].sort());
        assert.deepEqual(cuerpo.cola, { pendientes: 2, dia: "2026-10-16", enviadosHoy: 7, enEspera: 2, topeDiario: 100, enviadosUltimaHora: 7, topePorHora: 40, topePorDestinatario: 3 });
        assert.equal(typeof cuerpo.uptimeSegundos, "number");
        assert.doesNotMatch(JSON.stringify(cuerpo), /\d{7,}/, "ningún número que parezca un teléfono");
    });
});

test("GET /salud con whatsapp-web.js (sin salud()) sigue siendo {ok:true}, también sin cabeceras raras", async () => {
    await conServidor({}, async ({ pedir }) => {
        const r = await pedir("/salud", { secreto: null });
        assert.equal(r.headers.get("cache-control"), "no-store");
        assert.deepEqual(await r.json(), { ok: true });
    });
});

// ---------- Estado ----------

test("GET /estado pide el secreto: sin él o con uno equivocado, 401", async () => {
    await conServidor({}, async ({ pedir }) => {
        assert.equal((await pedir("/estado", { secreto: null })).status, 401);
        assert.equal((await pedir("/estado", { secreto: "otro" })).status, 401);
        assert.equal((await pedir("/estado", { secreto: SECRETO + "x" })).status, 401);
    });
});

test("GET /estado con secreto devuelve el estado del backend y pide no guardarlo en cachés (puede traer un QR)", async () => {
    await conServidor({}, async ({ pedir }) => {
        const r = await pedir("/estado");
        assert.equal(r.status, 200);
        assert.equal(r.headers.get("cache-control"), "no-store");
        assert.deepEqual(await r.json(), { conectado: true, numero: "5491100000000", qrPendiente: false, qr: null });
    });
});

test("si el backend falla al dar el estado: 500 genérico, sin el detalle del error", async () => {
    const servicio = servicioFalso({ estado: async () => { throw new Error("detalle interno con 5491122334455"); } });
    await conServidor({ servicio }, async ({ pedir, log }) => {
        const r = await pedir("/estado");
        assert.equal(r.status, 500);
        assert.deepEqual(await r.json(), { error: "Error interno." });
        assert.ok(log.lineas.some(l => /Error atendiendo GET \/estado/.test(l)));
    });
});

// ---------- Enviar ----------

test("POST /enviar sin secreto: 401, y no encola nada", async () => {
    await conServidor({}, async ({ pedir, cola }) => {
        const r = await pedir("/enviar", { metodo: "POST", secreto: null, cuerpo: { telefono: "1122334455", texto: "hola" } });
        assert.equal(r.status, 401);
        assert.equal(cola.tamano(), 0);
    });
});

test("POST /enviar encola y responde apenas lo hace, con el id", async () => {
    const enviados = [];
    const cola = new ColaEnvios(async (tel, texto, meta) => { enviados.push({ tel, texto, meta }); }, crearLog());
    await conServidor({ cola }, async ({ pedir }) => {
        const r = await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: "1122334455", texto: "Tenés turno" } });
        assert.equal(r.status, 200);
        const cuerpo = await r.json();
        assert.equal(cuerpo.exito, true);
        assert.match(cuerpo.idExterno, /^cola-\d+$/);
        await new Promise(resolver => setTimeout(resolver, 30));
        assert.deepEqual(enviados, [{ tel: "1122334455", texto: "Tenés turno", meta: { optIn: false } }]);
    });
});

test("POST /enviar acepta el teléfono como número y pasa optIn a la cola solo si es exactamente true", async () => {
    const metas = [];
    const cola = { encolar: (tel, texto, meta) => { metas.push({ tel, meta }); return { ok: true, idExterno: "cola-1" }; }, detener() {}, tamano: () => 0 };
    await conServidor({ cola }, async ({ pedir }) => {
        await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: 1122334455, texto: "a", optIn: true } });
        await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: "1122334455", texto: "b", optIn: "true" } });
        await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: "1122334455", texto: "c" } });
    });
    assert.deepEqual(metas, [
        { tel: "1122334455", meta: { optIn: true } },
        { tel: "1122334455", meta: { optIn: false } },
        { tel: "1122334455", meta: { optIn: false } }
    ]);
});

test("POST /enviar valida el cuerpo: faltan datos, tipos raros o un texto demasiado largo dan 400", async () => {
    await conServidor({}, async ({ pedir }) => {
        for (const cuerpo of [{}, { telefono: "1122334455" }, { texto: "hola" }, { telefono: "", texto: "hola" }, { telefono: "1122334455", texto: "   " },
            { telefono: { x: 1 }, texto: "hola" }, { telefono: "1122334455", texto: 123 }, { telefono: ["1"], texto: "hola" }]) {
            const r = await pedir("/enviar", { metodo: "POST", cuerpo });
            assert.equal(r.status, 400, JSON.stringify(cuerpo));
            assert.equal((await r.json()).exito, false);
        }
        const largo = await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: "1122334455", texto: "x".repeat(MAX_TEXTO + 1) } });
        assert.equal(largo.status, 400);
        assert.match((await largo.json()).error, /demasiado largo/);
        const justo = await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: "1122334455", texto: "x".repeat(MAX_TEXTO) } });
        assert.equal(justo.status, 200);
    });
});

test("POST /enviar con la cola llena o detenida: 503 con el motivo (la API lo trata como envío fallido)", async () => {
    const cola = new ColaEnvios(() => new Promise(() => {}), crearLog());   // el primer envío no termina nunca: la cola se llena
    await conServidor({ cola }, async ({ pedir }) => {
        let ultimo;
        for (let i = 0; i < 60; i++) ultimo = await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: "1122334455", texto: `m${i}` } });
        assert.equal(ultimo.status, 503);
        const cuerpo = await ultimo.json();
        assert.equal(cuerpo.exito, false);
        assert.match(cuerpo.error, /Cola llena/);
        cola.detener();
        const detenida = await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: "1122334455", texto: "x" } });
        assert.equal(detenida.status, 503);
        assert.match((await detenida.json()).error, /se está reiniciando/);
    });
});

test("POST /enviar respeta el estado HTTP y el código que devuelva la cola (consentimiento, topes)", async () => {
    const cola = {
        resultado: { ok: false, estado: 403, error: "sin consentimiento", codigo: "sin_consentimiento" },
        encolar() { return this.resultado; }, detener() {}, tamano: () => 0
    };
    await conServidor({ cola }, async ({ pedir }) => {
        let r = await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: "1122334455", texto: "hola" } });
        assert.equal(r.status, 403);
        assert.deepEqual(await r.json(), { exito: false, error: "sin consentimiento", codigo: "sin_consentimiento" });

        cola.resultado = { ok: false, estado: 429, error: "tope", codigo: "tope_diario" };
        r = await pedir("/enviar", { metodo: "POST", cuerpo: { telefono: "1122334455", texto: "hola" } });
        assert.equal(r.status, 429);
    });
});

// ---------- Desvincular y vincular ----------

test("POST /desvincular pide secreto y responde sin esperar a que termine", async () => {
    let terminar;
    const servicio = servicioFalso({ desvincular() { this.llamadas.desvincular++; return new Promise(resolver => { terminar = resolver; }); } });
    await conServidor({ servicio }, async ({ pedir }) => {
        assert.equal((await pedir("/desvincular", { metodo: "POST", secreto: null })).status, 401);
        assert.equal(servicio.llamadas.desvincular, 0);

        const r = await pedir("/desvincular", { metodo: "POST" });
        assert.equal(r.status, 200);
        assert.deepEqual(await r.json(), { exito: true });
        assert.equal(servicio.llamadas.desvincular, 1);
        terminar();
    });
});

test("si desvincular falla, se loguea y el pedido igual respondió bien (ya había salido)", async () => {
    const servicio = servicioFalso({ desvincular: async () => { throw new Error("EACCES"); } });
    await conServidor({ servicio }, async ({ pedir, log }) => {
        assert.equal((await pedir("/desvincular", { metodo: "POST" })).status, 200);
        await new Promise(resolver => setTimeout(resolver, 20));
        assert.ok(log.lineas.some(l => /Error desvinculando: EACCES/.test(l)));
    });
});

test("POST /vincular: 401 sin secreto, 404 si el backend no lo soporta, y con soporte devuelve el estado", async () => {
    await conServidor({}, async ({ pedir }) => {
        assert.equal((await pedir("/vincular", { metodo: "POST", secreto: null })).status, 401);
        const r = await pedir("/vincular", { metodo: "POST" });
        assert.equal(r.status, 404);
        assert.equal((await r.json()).exito, false);
    });

    const servicio = servicioFalso({ vincular: async () => ({ estado: "requiere_vincular", qrPendiente: false }) });
    await conServidor({ servicio }, async ({ pedir }) => {
        const r = await pedir("/vincular", { metodo: "POST" });
        assert.equal(r.status, 200);
        assert.equal(r.headers.get("cache-control"), "no-store");
        assert.deepEqual(await r.json(), { exito: true, estado: "requiere_vincular", qrPendiente: false });
    });
});

test("POST /vincular: si el servicio dice que no corresponde (ya hay un número...), es un 409 con el motivo", async () => {
    const servicio = servicioFalso({
        vincular: async () => ({ exito: false, codigo: "ya_vinculado", error: "Ya hay un número vinculado.", estado: "conectado" })
    });
    await conServidor({ servicio }, async ({ pedir }) => {
        const r = await pedir("/vincular", { metodo: "POST" });
        assert.equal(r.status, 409);
        assert.equal(r.headers.get("cache-control"), "no-store");
        assert.deepEqual(await r.json(), { exito: false, codigo: "ya_vinculado", error: "Ya hay un número vinculado.", estado: "conectado" });
    });
});

test("POST /vincular: si el servicio falla, 500 en JSON y sin filtrar el detalle", async () => {
    const log = crearLog();
    const servicio = servicioFalso({ vincular: async () => { throw new Error("detalle interno 5491122334455"); } });
    await conServidor({ servicio, log }, async ({ pedir }) => {
        const r = await pedir("/vincular", { metodo: "POST" });
        assert.equal(r.status, 500);
        assert.deepEqual(await r.json(), { exito: false, error: "Error interno." });
    });
});

test("ninguna ruta devuelve el texto crudo del QR: /estado entrega la imagen", async () => {
    const QR_CRUDO = "ref-secreta-1,ruido,identidad,adv";
    const servicio = servicioFalso({
        estado: async () => ({ conectado: false, numero: null, qrPendiente: true, qr: "data:image/png;base64,AAAA" }),
        vincular: async () => ({ exito: true, estado: "requiere_vincular", motivo: "esperando_escaneo", qrPendiente: true }),
        qrTexto: () => QR_CRUDO
    });
    await conServidor({ servicio }, async ({ pedir }) => {
        for (const [ruta, metodo] of [["/estado", "GET"], ["/vincular", "POST"], ["/salud", "GET"], ["/qr", "GET"], ["/qrTexto", "GET"]]) {
            const r = await pedir(ruta, { metodo });
            assert.equal((await r.text()).includes(QR_CRUDO), false, `${metodo} ${ruta} no puede devolver el QR crudo`);
        }
    });
});

// ---------- Robustez ----------

test("un JSON roto o enorme da 400 o 413 en JSON, nunca el HTML con la pila de Express", async () => {
    await conServidor({}, async ({ pedir, log }) => {
        const roto = await pedir("/enviar", { metodo: "POST", crudo: "{no es json" });
        assert.equal(roto.status, 400);
        assert.match(roto.headers.get("content-type"), /application\/json/);
        assert.deepEqual(await roto.json(), { exito: false, error: "El cuerpo no es un JSON válido." });

        const enorme = await pedir("/enviar", { metodo: "POST", crudo: JSON.stringify({ telefono: "1", texto: "x".repeat(40000) }) });
        assert.equal(enorme.status, 413);
        assert.match((await enorme.text()), /demasiado grande/);
        assert.deepEqual(log.lineas, []);
    });
});

test("una ruta que no existe da 404 en JSON y no se anuncia que es Express", async () => {
    await conServidor({}, async ({ pedir }) => {
        const r = await pedir("/no-existe");
        assert.equal(r.status, 404);
        assert.deepEqual(await r.json(), { exito: false, error: "No existe." });
        assert.equal(r.headers.get("x-powered-by"), null);
    });
});

test("solo escucha en loopback", async () => {
    await conServidor({}, async ({ address }) => {
        assert.equal(address, "127.0.0.1");
    });
});

test("el secreto se compara completo y sin importar la longitud de lo recibido", () => {
    assert.equal(secretoValido("abc", "abc"), true);
    assert.equal(secretoValido("abc", "abd"), false);
    assert.equal(secretoValido("abc", "ab"), false);
    assert.equal(secretoValido("abc", "abcd"), false);
    assert.equal(secretoValido("abc", undefined), false);
    assert.equal(secretoValido("abc", ["abc"]), false);
    assert.equal(secretoValido("áéí", "áéí"), true);
});
