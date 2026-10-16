// Selección del backend (backend.js): cada librería se carga solo si se elige, y los dos backends cumplen la misma interfaz.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const os = require("node:os");
const path = require("node:path");
const { crearBackend } = require("../backend");
const { ServicioBaileys } = require("../baileys/servicio");
const { fachadaFalsa } = require("./helpers/baileys-falso");

const log = { info() {}, error() {} };
const configBaileys = () => ({
    backend: "baileys",
    puerto: 3002,
    apiBaseUrl: "http://127.0.0.1:5000",
    secreto: "S3CRETO",
    baileys: { sessionPath: path.join(os.tmpdir(), "yosoy-no-se-usa"), versionWa: null, buscarVersionWa: false }
});

// La interfaz que usa servidor.js / index.js, en común para los dos backends.
const INTERFAZ = ["iniciar", "cerrar", "estado", "enviar", "desvincular", "recuperar"];

const cargadas = () => Object.keys(require.cache).filter(ruta => /whatsapp-web\.js|puppeteer/.test(ruta));

test("con backend baileys se arma un ServicioBaileys, sin iniciarlo ni abrir sockets, y NO se carga whatsapp-web.js", async () => {
    const falso = fachadaFalsa();
    const backend = await crearBackend(configBaileys(), log, { cargarBaileys: async () => falso.fachada, reenviar: () => {} });

    assert.ok(backend instanceof ServicioBaileys);
    assert.equal(backend.salud().estado, "detenido");
    assert.equal(falso.sockets.length, 0);
    assert.deepEqual(cargadas(), [], "con baileys no hay Puppeteer ni whatsapp-web.js en memoria");
});

test("si Baileys no se puede cargar, el error sube (index.js lo informa y sale)", async () => {
    await assert.rejects(
        () => crearBackend(configBaileys(), log, { cargarBaileys: async () => { throw new Error("No está instalado @whiskeysockets/baileys."); } }),
        /No está instalado/);
});

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
