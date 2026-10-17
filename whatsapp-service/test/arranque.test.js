// El arranque real (index.js como proceso): una configuración inválida corta con código 1 y un mensaje que dice qué falta, ANTES
// de cargar ninguna librería de WhatsApp y de abrir el puerto. Ninguna de estas combinaciones llega a conectar con nada.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const INDEX = path.join(__dirname, "..", "index.js");
const SIN_RED = path.join(__dirname, "helpers", "sin-red.js");
const SECRETO = "secreto-que-no-tiene-que-aparecer-en-ninguna-salida";

function arrancar(variables) {
    // El entorno de la prueba, sin las variables del servicio que pudieran estar definidas en la máquina de quien corre esto.
    const env = { ...process.env };
    for (const k of Object.keys(env)) if (/^(WHATSAPP_|API_BASE_URL$|Mensajeria__)/.test(k)) delete env[k];
    Object.assign(env, variables, { NODE_OPTIONS: `--require=${SIN_RED}` });
    const r = spawnSync(process.execPath, [INDEX], { env, encoding: "utf8", timeout: 20000 });
    return { codigo: r.status, salida: `${r.stdout}${r.stderr}` };
}

test("sin API_BASE_URL no arranca y lo dice", () => {
    const r = arrancar({ Mensajeria__WhatsApp__Secreto: SECRETO });
    assert.equal(r.codigo, 1);
    assert.match(r.salida, /Falta API_BASE_URL en el entorno\./);
});

test("sin el secreto compartido no arranca y lo dice, sin imprimir ningún secreto", () => {
    const r = arrancar({ API_BASE_URL: "http://127.0.0.1:5000" });
    assert.equal(r.codigo, 1);
    assert.match(r.salida, /Falta Mensajeria__WhatsApp__Secreto en el entorno\./);
    assert.doesNotMatch(r.salida, new RegExp(SECRETO));
});

test("con WHATSAPP_BACKEND=baileys y sin carpeta de sesión no arranca y nombra la variable", () => {
    const r = arrancar({ API_BASE_URL: "http://127.0.0.1:5000", Mensajeria__WhatsApp__Secreto: SECRETO, WHATSAPP_BACKEND: "baileys" });
    assert.equal(r.codigo, 1);
    assert.match(r.salida, /Falta WHATSAPP_BAILEYS_SESSION_PATH/);
    assert.doesNotMatch(r.salida, new RegExp(SECRETO));
});

test("un WHATSAPP_BACKEND que no existe no arranca", () => {
    const r = arrancar({ API_BASE_URL: "http://127.0.0.1:5000", Mensajeria__WhatsApp__Secreto: SECRETO, WHATSAPP_BACKEND: "oficial" });
    assert.equal(r.codigo, 1);
    assert.match(r.salida, /WHATSAPP_BACKEND tiene que ser "webjs" o "baileys"/);
});

test("con baileys, el log del proceso tapa el secreto y enmascara los teléfonos aunque aparezcan en un mensaje de error", () => {
    // Una carpeta de sesión DENTRO del repositorio se rechaza sin cargar la librería; el mensaje repite la ruta, y esta lleva el secreto
    // y un teléfono, para ver que lo que sale por la salida de errores del proceso real ya viene filtrado.
    const secretoLargo = "SECRETO-DE-PRUEBA-QUE-NO-PUEDE-SALIR-0123";
    const ruta = path.join(path.dirname(INDEX), `${secretoLargo}-5491122334455`);
    const r = arrancar({
        API_BASE_URL: "http://127.0.0.1:5000", Mensajeria__WhatsApp__Secreto: secretoLargo,
        WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: ruta
    });
    assert.equal(r.codigo, 1);
    assert.match(r.salida, /No se pudo preparar el backend de WhatsApp \(baileys\)/);
    assert.match(r.salida, /\[secreto\]-\*\*\*4455/, "el secreto tapado y el teléfono enmascarado, en la ruta que repite el mensaje");
    assert.doesNotMatch(r.salida, /SECRETO-DE-PRUEBA|5491122334455/);
    assert.equal(fs.existsSync(ruta), false, "y no se creó nada");
});

test("con baileys y la API en otra máquina sin HTTPS no arranca: el secreto no viaja en claro", () => {
    const r = arrancar({
        API_BASE_URL: "http://api.ejemplo.test:5000", Mensajeria__WhatsApp__Secreto: SECRETO,
        WHATSAPP_BACKEND: "baileys", WHATSAPP_BAILEYS_SESSION_PATH: path.resolve(path.sep, "datos", "sesion")
    });
    assert.equal(r.codigo, 1);
    assert.match(r.salida, /otra máquina sin HTTPS/);
});
