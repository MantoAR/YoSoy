// Selección del backend (backend.js): cada librería se carga solo si se elige, y los dos backends cumplen la misma interfaz.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { crearBackend } = require("../backend");
const { ServicioBaileys } = require("../baileys/servicio");
const { fachadaFalsa } = require("./helpers/baileys-falso");

const log = { info() {}, error() {} };
const configBaileys = sessionPath => ({
    backend: "baileys",
    puerto: 3002,
    apiBaseUrl: "http://127.0.0.1:5000",
    secreto: "S3CRETO",
    baileys: { sessionPath, versionWa: null, buscarVersionWa: false }
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
}));

test("si Baileys no se puede cargar, el error sube (index.js lo informa y sale)", conCarpeta(async carpeta => {
    await assert.rejects(
        () => crearBackend(configBaileys(carpeta), log, { cargarBaileys: async () => { throw new Error("No está instalado @whiskeysockets/baileys."); } }),
        /No está instalado/);
}));

test("una carpeta de sesión dentro de la aplicación frena el arranque ANTES de cargar la librería y no crea nada", async () => {
    const adentro = path.join(__dirname, "..", "sesion-que-no-tiene-que-existir");
    let seCargo = false;
    await assert.rejects(
        () => crearBackend(configBaileys(adentro), log, { cargarBaileys: async () => { seCargo = true; return fachadaFalsa().fachada; } }),
        /FUERA/);
    assert.equal(seCargo, false, "la librería ni se cargó");
    assert.equal(fs.existsSync(adentro), false, "y no se creó la carpeta");
});

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
