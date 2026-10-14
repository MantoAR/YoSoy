// Puente HTTP interno entre la API de YoSoy y WhatsApp. Solo escucha en loopback — ver
// README.md para qué hace falta instalado en el VPS y cómo se vincula el chip.
const crypto = require("crypto");
const express = require("express");
const { ColaEnvios } = require("./cola");
const { ServicioWhatsApp } = require("./whatsapp");

const log = {
    info: msg => console.log(`[${new Date().toISOString()}] ${msg}`),
    error: msg => console.error(`[${new Date().toISOString()}] ${msg}`)
};

const PUERTO = parseInt(process.env.WHATSAPP_SERVICE_PORT || "3002", 10);
const SESSION_PATH = process.env.WHATSAPP_SESSION_PATH || "./.wwebjs_auth";
const API_BASE_URL = process.env.API_BASE_URL;
const SECRETO = process.env.Mensajeria__WhatsApp__Secreto;
// Opcional — ver README.md "Fijar la versión de WhatsApp Web". Sin definir, whatsapp-web.js
// usa la versión que trae pineada por default (comportamiento actual, sin cambios).
const WEB_VERSION = process.env.WHATSAPP_WEB_VERSION || null;
// Opcional, apagado por default — ver README.md "Diagnóstico". Loguea console/pageerror de
// la página real y una sonda del estado interno si "ready" tarda — prender solo mientras se
// esté diagnosticando un problema puntual, no dejarlo siempre encendido.
const DEBUG = process.env.WHATSAPP_DEBUG === "1";

if (!API_BASE_URL) { log.error("Falta API_BASE_URL en el entorno."); process.exit(1); }
if (!SECRETO) { log.error("Falta Mensajeria__WhatsApp__Secreto en el entorno."); process.exit(1); }

function secretoValido(recibido) {
    const esperado = Buffer.from(SECRETO, "utf8");
    const dado = Buffer.from(recibido || "", "utf8");
    // Los buffers tienen que medir lo mismo para timingSafeEqual; si no, ya es inválido
    // (comparar igual mide tiempo constante para esa rama, no filtra la longitud real).
    if (esperado.length !== dado.length) return false;
    return crypto.timingSafeEqual(esperado, dado);
}

const wa = new ServicioWhatsApp({
    dataPath: SESSION_PATH,
    apiBaseUrl: API_BASE_URL,
    secreto: SECRETO,
    webVersion: WEB_VERSION,
    debug: DEBUG,
    log
});

const cola = new ColaEnvios((telefono, texto) => wa.enviar(telefono, texto), log);

const app = express();
app.use(express.json());

app.get("/salud", (req, res) => res.json({ ok: true }));

// Antes no pedía secreto — tenía sentido cuando solo devolvía conectado/numero/qrPendiente,
// pero ahora el QR viaja en esta misma respuesta (ver ServicioWhatsApp.estado()), así que se
// gatea igual que /enviar. try/catch explícito: Express 4 no atrapa solo el rechazo de un
// handler async, así que sin esto un error acá (p. ej. QRCode.toDataURL) dejaría el pedido
// colgado en vez de responder.
app.get("/estado", async (req, res) => {
    if (!secretoValido(req.headers["x-shared-secret"])) return res.status(401).json({ error: "Secreto inválido." });
    try {
        res.json(await wa.estado());
    } catch (err) {
        log.error(`Error obteniendo el estado: ${err.message || err}`);
        res.status(500).json({ error: "Error interno." });
    }
});

app.post("/enviar", (req, res) => {
    if (!secretoValido(req.headers["x-shared-secret"])) return res.status(401).json({ exito: false, error: "Secreto inválido." });
    const { telefono, texto } = req.body || {};
    if (!telefono || !texto) return res.status(400).json({ exito: false, error: "Faltan telefono o texto." });
    const resultado = cola.encolar(telefono, texto);
    if (!resultado.ok) return res.status(503).json({ exito: false, error: resultado.error });
    res.json({ exito: true, idExterno: resultado.idExterno });
});

// Responde apenas valida el secreto, sin esperar a que termine el reinicio (que puede tardar
// varios segundos) — mismo criterio que /enviar, que responde apenas encola, no cuando el
// mensaje sale de verdad.
app.post("/desvincular", (req, res) => {
    if (!secretoValido(req.headers["x-shared-secret"])) return res.status(401).json({ exito: false, error: "Secreto inválido." });
    wa.desvincular().catch(err => log.error(`Error desvinculando: ${err.message || err}`));
    res.json({ exito: true });
});

const servidor = app.listen(PUERTO, "127.0.0.1", () => {
    log.info(`whatsapp-service escuchando en 127.0.0.1:${PUERTO}`);
    wa.iniciar();
});

// Red de seguridad: un error sin capturar en el código de Puppeteer/whatsapp-web.js (fuera
// de nuestro control directo) no tiene por qué tirar abajo todo el proceso — se loguea
// (nunca el objeto entero del error, puede traer de más) y se intenta recuperar el cliente
// por el mismo camino que un "disconnected" transitorio. Si el problema es realmente
// persistente, _reiniciar ya tiene su propio reintento con backoff — esto no lo duplica,
// solo lo dispara una vez más por si el cliente quedó en un estado raro que un evento
// normal de whatsapp-web.js no iba a reportar solo.
process.on("unhandledRejection", razon => {
    log.error(`unhandledRejection (no se cae el proceso): ${razon?.message || razon}`);
    wa.recuperar();
});
process.on("uncaughtException", err => {
    log.error(`uncaughtException (no se cae el proceso): ${err?.message || err}`);
    wa.recuperar();
});

// Apagado limpio (systemd manda SIGTERM al hacer stop/restart; TimeoutStopSec=20 en la
// unit le da margen de sobra a este timeout interno de 10s + el tiempo propio de Node/SO).
// Orden: dejar de aceptar pedidos nuevos (servidor + cola), después cerrar el cliente de
// WhatsApp con un tope de tiempo — nunca bloquear el apagado esperando a Puppeteer.
let apagando = false;
async function apagar(señal) {
    if (apagando) return;
    apagando = true;
    log.info(`${señal} recibido, apagando...`);
    cola.detener();
    servidor.close();
    try {
        await Promise.race([
            wa.cerrar(),
            new Promise((_, reject) => setTimeout(() => reject(new Error("Tiempo de espera agotado cerrando el cliente de WhatsApp.")), 10000))
        ]);
    } catch (err) {
        log.error(`${err.message || err} — se sale igual.`);
    }
    process.exit(0);
}
process.on("SIGTERM", () => apagar("SIGTERM"));
process.on("SIGINT", () => apagar("SIGINT"));
