// whatsapp-web.js (y con él Puppeteer, Chromium y patch-package) se sacó por completo: Baileys es el único motor. Esta guardia vigila que no
// vuelva a colarse por un descuido: ni como dependencia, ni como código, ni como unit, ni como variable que elija un motor.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const RAIZ = path.join(__dirname, "..");
const paquete = JSON.parse(fs.readFileSync(path.join(RAIZ, "package.json"), "utf8"));
const lock = JSON.parse(fs.readFileSync(path.join(RAIZ, "package-lock.json"), "utf8"));

// Los archivos de código del servicio: todo lo que no son las pruebas ni node_modules.
function archivosDeCodigo(carpeta = RAIZ) {
    const salida = [];
    for (const entrada of fs.readdirSync(carpeta, { withFileTypes: true })) {
        if (entrada.name === "node_modules" || entrada.name === "test" || entrada.name === ".git") continue;
        const ruta = path.join(carpeta, entrada.name);
        if (entrada.isDirectory()) salida.push(...archivosDeCodigo(ruta));
        else if (/\.(js|cjs|mjs)$/.test(entrada.name)) salida.push(ruta);
    }
    return salida;
}

const PROHIBIDOS = ["whatsapp-web.js", "puppeteer", "puppeteer-core", "patch-package"];

test("ni whatsapp-web.js, ni Puppeteer, ni patch-package son dependencias del servicio", () => {
    const declaradas = Object.keys({ ...paquete.dependencies, ...paquete.devDependencies, ...paquete.optionalDependencies });
    for (const nombre of PROHIBIDOS) assert.ok(!declaradas.includes(nombre), `${nombre} no tiene que estar en package.json`);
    assert.deepEqual(declaradas.sort(), ["@whiskeysockets/baileys", "express", "qrcode", "qrcode-terminal"], "las dependencias de producción son exactamente estas cuatro");
    assert.equal(paquete.scripts.postinstall, undefined, "sin patch-package no hay postinstall");
    assert.match(paquete.description, /Baileys/);
    assert.doesNotMatch(paquete.description, /whatsapp-web\.js/);
});

test("el package-lock.json no trae ninguno de esos paquetes ni nada que baje un navegador", () => {
    const instalados = Object.keys(lock.packages).filter(k => k !== "").map(k => k.replace(/^.*node_modules\//, ""));
    for (const nombre of PROHIBIDOS) assert.ok(!instalados.includes(nombre), `${nombre} sigue en el package-lock.json`);
    assert.deepEqual(instalados.filter(n => /^(puppeteer.*|chromium.*|chrome-launcher|extract-zip|pac-proxy-agent|proxy-agent|basic-ftp)$/.test(n)), [], "ni la cadena que baja Chromium");
    assert.ok(instalados.includes("@whiskeysockets/baileys"));
    assert.ok(instalados.length < 250, `el árbol tiene ${instalados.length} paquetes (eran 429 con whatsapp-web.js y Puppeteer)`);
});

test("ningún archivo del servicio carga whatsapp-web.js ni Puppeteer, y los archivos del motor viejo ya no existen", () => {
    for (const ruta of archivosDeCodigo()) {
        const codigo = fs.readFileSync(ruta, "utf8");
        for (const nombre of PROHIBIDOS) {
            const carga = new RegExp(`(require|import)\\s*\\(?\\s*["']${nombre.replace(".", "\\.")}["']`);
            assert.doesNotMatch(codigo, carga, `${path.relative(RAIZ, ruta)} carga ${nombre}`);
        }
    }
    assert.equal(fs.existsSync(path.join(RAIZ, "whatsapp.js")), false, "whatsapp.js era el motor de whatsapp-web.js");
    assert.equal(fs.existsSync(path.join(RAIZ, "patches")), false, "patches/ era de whatsapp-web.js");
});

test("el motor no se puede elegir: config.js ya no tiene el selector ni la configuración de whatsapp-web.js", () => {
    const fuente = fs.readFileSync(path.join(RAIZ, "config.js"), "utf8");
    const codigo = fuente.split("\n").filter(linea => !linea.trim().startsWith("//")).join("\n");      // sin los comentarios, que sí hablan de lo que se sacó
    assert.doesNotMatch(codigo, /BACKENDS|\bwebjs\b|\.wwebjs_auth/);
    assert.match(codigo, /WHATSAPP_BACKEND/, "pero sigue conociendo la variable, para rechazar cualquier valor que no sea baileys");
    const { leerConfig } = require("../config");
    const env = { API_BASE_URL: "http://127.0.0.1:5000", Mensajeria__WhatsApp__Secreto: "x", WHATSAPP_BAILEYS_SESSION_PATH: path.resolve(path.sep, "datos", "sesion") };
    assert.throws(() => leerConfig({ ...env, WHATSAPP_BACKEND: "webjs" }), /ya no existe/);
});
