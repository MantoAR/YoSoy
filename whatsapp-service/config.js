// Configuración del servicio: se lee del entorno una sola vez, se valida y se devuelve ya convertida. Un valor inválido corta el
// arranque con un mensaje que dice QUÉ variable está mal; los mensajes nunca incluyen el valor de un secreto.
//
// El único motor de WhatsApp es Baileys (WhatsApp Web sin navegador). whatsapp-web.js (Puppeteer y Chromium) se sacó por completo:
//   - WHATSAPP_BACKEND ya no elige nada. Sin la variable, o con "baileys" (como la dejaba la unit propuesta), todo sigue igual; con
//     cualquier otro valor (por ejemplo "webjs") el servicio NO arranca: así nadie cree que está usando un motor que ya no existe.
//   - Las variables de whatsapp-web.js (WHATSAPP_SESSION_PATH, WHATSAPP_WEB_VERSION, WHATSAPP_DEBUG, PUPPETEER_*) ya no se leen. Si el
//     entorno todavía las trae, el arranque lo avisa (solo el nombre, nunca el valor) para que se limpien (variablesRetiradasPresentes).
"use strict";

const path = require("node:path");

class ErrorDeConfiguracion extends Error {
    constructor(mensaje) {
        super(mensaje);
        this.name = "ErrorDeConfiguracion";
    }
}

const MOTOR = "baileys";

// Variables que eran de whatsapp-web.js (y la que elegía el motor): ya no hacen nada.
const VARIABLES_RETIRADAS = Object.freeze([
    "WHATSAPP_BACKEND", "WHATSAPP_SESSION_PATH", "WHATSAPP_WEB_VERSION", "WHATSAPP_DEBUG",
    "PUPPETEER_CACHE_DIR", "PUPPETEER_SKIP_DOWNLOAD", "PUPPETEER_EXECUTABLE_PATH"
]);

function texto(env, nombre) {
    const valor = env[nombre];
    if (valor === undefined || valor === null) return undefined;
    const limpio = String(valor).trim();
    return limpio === "" ? undefined : limpio;
}

function entero(env, nombre, porDefecto, { min = 0, max = Number.MAX_SAFE_INTEGER } = {}) {
    const crudo = texto(env, nombre);
    if (crudo === undefined) return porDefecto;
    if (!/^\d+$/.test(crudo)) throw new ErrorDeConfiguracion(`${nombre} tiene que ser un número entero (y es "${crudo}").`);
    const n = Number(crudo);
    if (n < min || n > max) throw new ErrorDeConfiguracion(`${nombre} tiene que estar entre ${min} y ${max} (y es ${n}).`);
    return n;
}

function esLoopback(host) {
    const h = String(host).toLowerCase().replace(/^\[|\]$/g, "");
    return h === "localhost" || h === "::1" || /^127(\.\d{1,3}){3}$/.test(h);
}

// El secreto compartido viaja en cada reenvío a la API: con http solo se acepta hablar con la propia máquina.
function validarApiBaseUrl(valor) {
    let url;
    try { url = new URL(valor); } catch { throw new ErrorDeConfiguracion("API_BASE_URL no es una URL válida (por ejemplo http://127.0.0.1:5000)."); }
    if (url.protocol !== "http:" && url.protocol !== "https:") throw new ErrorDeConfiguracion("API_BASE_URL tiene que empezar con http:// o https://.");
    if (url.protocol === "http:" && !esLoopback(url.hostname)) {
        throw new ErrorDeConfiguracion("API_BASE_URL apunta a otra máquina sin HTTPS: el secreto compartido viajaría en claro. Usar https:// o la propia máquina (127.0.0.1).");
    }
    return valor.replace(/\/+$/, "");
}

// WHATSAPP_BACKEND ya no elige el motor: solo se tolera "baileys" (o que no esté). Cualquier otro valor corta el arranque.
function validarMotor(env) {
    const pedido = texto(env, "WHATSAPP_BACKEND");
    if (pedido === undefined || pedido.toLowerCase() === MOTOR) return;
    throw new ErrorDeConfiguracion(
        `WHATSAPP_BACKEND="${pedido.slice(0, 40)}" ya no existe: WhatsApp Web JS (whatsapp-web.js) se sacó y el único motor es Baileys. ` +
        "Borrá la variable (o dejala en \"baileys\").");
}

// Los nombres de las variables retiradas que el entorno todavía trae, para avisarlo en el arranque. Nunca devuelve un valor.
function variablesRetiradasPresentes(env = process.env) {
    return VARIABLES_RETIRADAS.filter(nombre => texto(env, nombre) !== undefined);
}

// "2.3000.1037753511" → [2, 3000, 1037753511] (el formato que espera makeWASocket).
function versionWa(env, nombre) {
    const crudo = texto(env, nombre);
    if (crudo === undefined) return null;
    if (!/^\d+\.\d+\.\d+$/.test(crudo)) throw new ErrorDeConfiguracion(`${nombre} tiene que tener la forma 2.3000.1234567890 (y es "${crudo}").`);
    return crudo.split(".").map(Number);
}

// Lo que necesita Baileys. Aparte de leerConfig() porque el comando de vinculación (vincular.js) solo necesita esto: ni la API ni
// el secreto compartido.
function leerConfigBaileys(env = process.env) {
    const sessionPath = texto(env, "WHATSAPP_BAILEYS_SESSION_PATH");
    if (!sessionPath) {
        throw new ErrorDeConfiguracion(
            "Falta WHATSAPP_BAILEYS_SESSION_PATH: la carpeta donde Baileys guarda la sesión de WhatsApp (es una credencial). " +
            "Tiene que ser una ruta absoluta FUERA del repositorio y de la carpeta de la aplicación, por ejemplo /opt/yosoy/whatsapp/sesion-baileys.");
    }
    if (!path.isAbsolute(sessionPath)) {
        throw new ErrorDeConfiguracion("WHATSAPP_BAILEYS_SESSION_PATH tiene que ser una ruta absoluta.");
    }
    return {
        sessionPath: path.resolve(sessionPath),
        // Opcionales: si WhatsApp rechaza la versión que trae la librería (conexión cerrada con 405 al arrancar), se puede
        // fijar una a mano o pedirle a Baileys la última conocida (esto último hace un pedido a raw.githubusercontent.com).
        versionWa: versionWa(env, "WHATSAPP_BAILEYS_VERSION_WA"),
        buscarVersionWa: env.WHATSAPP_BAILEYS_BUSCAR_VERSION === "1",
        // Ventanas de QR (cada una dura ~2,7 minutos) que se abren solas, sin número vinculado, antes de esperar a que una
        // persona pida otra. Evita pedirle a WhatsApp un QR tras otro sin que nadie mire.
        maxVentanasQr: entero(env, "WHATSAPP_BAILEYS_VENTANAS_QR", 2, { min: 1, max: 5 })
    };
}

function zonaHoraria(env, nombre, porDefecto) {
    const zona = texto(env, nombre) ?? porDefecto;
    try {
        new Intl.DateTimeFormat("en-CA", { timeZone: zona });
    } catch {
        throw new ErrorDeConfiguracion(`${nombre} no es una zona horaria válida (y es "${zona}"). Ejemplo: America/Argentina/Buenos_Aires.`);
    }
    return zona;
}

// Protecciones contra el bloqueo del número: topes, ritmo y consentimiento. Todo opcional, con valores prudentes.
function leerConfigEnvio(env = process.env) {
    const pausaMinSeg = entero(env, "WHATSAPP_PAUSA_MIN_S", 4, { min: 1, max: 300 });
    const pausaMaxSeg = entero(env, "WHATSAPP_PAUSA_MAX_S", 9, { min: 1, max: 600 });
    if (pausaMaxSeg < pausaMinSeg) {
        throw new ErrorDeConfiguracion(`WHATSAPP_PAUSA_MAX_S (${pausaMaxSeg}) no puede ser menor que WHATSAPP_PAUSA_MIN_S (${pausaMinSeg}).`);
    }
    return {
        topeDiario: entero(env, "WHATSAPP_TOPE_DIARIO", 100, { min: 1, max: 1000 }),
        topePorHora: entero(env, "WHATSAPP_TOPE_POR_HORA", 40, { min: 1, max: 500 }),
        topePorPersona: entero(env, "WHATSAPP_TOPE_POR_PERSONA", 3, { min: 1, max: 20 }),
        topeEnConversacion: entero(env, "WHATSAPP_TOPE_EN_CONVERSACION", 30, { min: 1, max: 200 }),
        diasDeConsentimiento: entero(env, "WHATSAPP_CONSENTIMIENTO_DIAS", 365, { min: 1, max: 3650 }),
        zonaHoraria: zonaHoraria(env, "WHATSAPP_ZONA_HORARIA", "America/Argentina/Buenos_Aires"),
        pausaMinSeg,
        pausaMaxSeg,
        simularEscritura: env.WHATSAPP_SIMULAR_ESCRITURA !== "0"
    };
}

function leerPuerto(env = process.env) {
    return entero(env, "WHATSAPP_SERVICE_PORT", 3002, { min: 1, max: 65535 });
}

function leerConfig(env = process.env) {
    const apiBaseUrl = texto(env, "API_BASE_URL");
    if (!apiBaseUrl) throw new ErrorDeConfiguracion("Falta API_BASE_URL en el entorno.");
    const secreto = env.Mensajeria__WhatsApp__Secreto;
    if (!secreto) throw new ErrorDeConfiguracion("Falta Mensajeria__WhatsApp__Secreto en el entorno.");
    validarMotor(env);

    return {
        puerto: leerPuerto(env),
        apiBaseUrl: validarApiBaseUrl(apiBaseUrl),
        secreto,
        baileys: { ...leerConfigBaileys(env), envio: leerConfigEnvio(env) }
    };
}

module.exports = { leerConfig, leerConfigBaileys, leerConfigEnvio, leerPuerto, variablesRetiradasPresentes, ErrorDeConfiguracion, validarApiBaseUrl, VARIABLES_RETIRADAS };
