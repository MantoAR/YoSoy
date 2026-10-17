// Contrato con la librería de Baileys y con cómo se instala. NO abre ninguna conexión: solo carga el paquete real para mirar qué
// exporta (importarlo no conecta nada; la red, además, está bloqueada en los tests: ver helpers/sin-red.js) y lee package.json /
// package-lock.json. Si alguien sube la versión de la librería y algo de lo que usa este servicio cambió, falla acá.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { cargarBaileys, crearFachada, validarFachada, MOTIVOS_REQUERIDOS } = require("../baileys/cargar");
const { fachadaFalsa } = require("./helpers/baileys-falso");

const RAIZ = path.join(__dirname, "..");
const paquete = JSON.parse(fs.readFileSync(path.join(RAIZ, "package.json"), "utf8"));
const lock = JSON.parse(fs.readFileSync(path.join(RAIZ, "package-lock.json"), "utf8"));
const instalado = fs.existsSync(path.join(RAIZ, "node_modules", "@whiskeysockets", "baileys", "package.json"));

// ---------- La versión: exacta, de un solo lugar, sin dependencias por git ----------

test("package.json fija @whiskeysockets/baileys con una versión exacta (sin ^, ~ ni rangos)", () => {
    const version = paquete.dependencies["@whiskeysockets/baileys"];
    assert.ok(version, "falta @whiskeysockets/baileys en dependencies");
    assert.match(version, /^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$/, `la versión tiene que ser exacta, y es "${version}"`);
});

test("el lock y package.json dicen la misma versión de Baileys, y libsignal se instala desde el registro de npm", () => {
    const fijada = paquete.dependencies["@whiskeysockets/baileys"];
    const enLock = lock.packages["node_modules/@whiskeysockets/baileys"];
    assert.equal(enLock.version, fijada);
    assert.match(enLock.resolved, /^https:\/\/registry\.npmjs\.org\//);
    assert.ok(enLock.integrity, "falta la integridad de Baileys en el lock");

    // Baileys 6.x pide libsignal por git (sin integridad, y `npm ci` en el VPS necesitaría una clave SSH de GitHub). El override
    // lo cambia por la misma versión publicada en npm (el código es idéntico al del commit de git: ver PROGRESO_BAILEYS.md).
    assert.equal(paquete.overrides?.libsignal, "6.0.0");
    const libsignal = lock.packages["node_modules/libsignal"];
    assert.equal(libsignal.version, "6.0.0");
    assert.match(libsignal.resolved, /^https:\/\/registry\.npmjs\.org\//);
    assert.ok(libsignal.integrity);
});

test("ningún paquete del lock viene de git ni de una URL suelta sin integridad", () => {
    const porGit = [];
    const sinIntegridad = [];
    for (const [nombre, datos] of Object.entries(lock.packages)) {
        if (!nombre || !datos.resolved) continue;
        if (/^(git|github:|git\+)/.test(datos.resolved)) porGit.push(nombre);
        if (!datos.integrity) sinIntegridad.push(nombre);
    }
    assert.deepEqual(porGit, [], `Dependencias por git en el lock: ${porGit.join(", ")}`);
    assert.deepEqual(sinIntegridad, [], `Paquetes sin hash de integridad en el lock: ${sinIntegridad.join(", ")}`);
});

// ---------- Lo que expone la librería instalada ----------

test("la librería instalada expone lo que usa el servicio (solo carga, no conecta)", { skip: instalado ? false : "node_modules sin Baileys: correr npm ci" }, async () => {
    const f = await cargarBaileys();

    assert.equal(typeof f.makeWASocket, "function");
    assert.equal(typeof f.useMultiFileAuthState, "function");
    assert.equal(typeof f.fetchLatestBaileysVersion, "function");
    assert.equal(typeof f.makeCacheableSignalKeyStore, "function");

    const navegador = f.Browsers.ubuntu("YoSoy");
    assert.ok(Array.isArray(navegador) && navegador.length === 3 && navegador.every(x => typeof x === "string"), "Browsers.ubuntu devuelve [sistema, navegador, versión]");

    // Los números en los que se apoya la reconexión (baileys/reconexion.js). Si cambian, hay que revisar esa lógica a propósito.
    const DR = f.DisconnectReason;
    assert.equal(DR.loggedOut, 401);
    assert.equal(DR.forbidden, 403);
    assert.equal(DR.connectionReplaced, 440);
    assert.equal(DR.badSession, 500);
    assert.equal(DR.restartRequired, 515);
    assert.equal(DR.connectionClosed, 428);
    assert.equal(DR.connectionLost, 408);
    assert.equal(DR.timedOut, 408);
});

test("la versión que quedó instalada en node_modules es la fijada en package.json", { skip: instalado ? false : "node_modules sin Baileys: correr npm ci" }, () => {
    const real = JSON.parse(fs.readFileSync(path.join(RAIZ, "node_modules", "@whiskeysockets", "baileys", "package.json"), "utf8"));
    assert.equal(real.version, paquete.dependencies["@whiskeysockets/baileys"]);
});

// ---------- La fachada ----------

test("la fachada falsa de los tests cumple el mismo contrato que exige la real", () => {
    const f = crearFachada(fachadaFalsa().modulo);
    validarFachada(f);
    for (const motivo of MOTIVOS_REQUERIDOS) assert.equal(typeof f.DisconnectReason[motivo], "number", motivo);
});

test("una versión que no expone lo necesario se rechaza con un mensaje que dice qué falta", () => {
    assert.throws(() => crearFachada({}), /no expone lo que usa whatsapp-service: .*makeWASocket/);
    const { modulo } = fachadaFalsa();
    delete modulo.DisconnectReason.loggedOut;
    assert.throws(() => crearFachada(modulo), /DisconnectReason\.loggedOut/);
});

test("si el paquete no está instalado, el error lo dice en castellano y nombra el comando", async () => {
    const noEncontrado = Object.assign(new Error("Cannot find package"), { code: "ERR_MODULE_NOT_FOUND" });
    await assert.rejects(() => cargarBaileys(() => Promise.reject(noEncontrado)), /No está instalado @whiskeysockets\/baileys.*npm ci/);
});

test("la fachada acepta tanto el espacio de nombres ESM como un módulo CommonJS con los exports dentro de default", () => {
    const { modulo } = fachadaFalsa();
    const comoEsm = crearFachada(modulo);
    const comoCjs = crearFachada({ default: { ...modulo, makeWASocket: modulo.default } });
    assert.equal(typeof comoEsm.makeWASocket, "function");
    assert.equal(typeof comoCjs.makeWASocket, "function");
});

// ---------- Guardia: ningún test usa la librería real para conectar ----------

// Saca los comentarios (de bloque y de línea) para mirar solo el código. Alcanza para esta guardia: no interpreta cadenas.
function sinComentarios(texto) {
    return texto.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:\\])\/\/.*$/gm, "$1");
}

test("solo este archivo nombra el paquete real, y ninguno llama a makeWASocket de la librería real", () => {
    const carpeta = __dirname;
    const archivos = fs.readdirSync(carpeta).filter(f => f.endsWith(".js")).concat(
        fs.readdirSync(path.join(carpeta, "helpers")).filter(f => f.endsWith(".js")).map(f => path.join("helpers", f)));
    const culpables = [];
    for (const archivo of archivos) {
        if (archivo === path.basename(__filename)) continue;
        // Una mención en un comentario no importa; un require/import del paquete, sí.
        const texto = sinComentarios(fs.readFileSync(path.join(carpeta, archivo), "utf8"));
        if (/(require\(|import\()\s*["']@whiskeysockets\/baileys["']/.test(texto)) culpables.push(archivo);
    }
    assert.deepEqual(culpables, [], `Estos tests importan el paquete real de Baileys: ${culpables.join(", ")}`);

    const propio = sinComentarios(fs.readFileSync(__filename, "utf8"));
    assert.ok(!/(?<![.\w])makeWASocket\s*\(/.test(propio), "este archivo no tiene que llamar a makeWASocket");
});
