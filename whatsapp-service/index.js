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
    log
});

const cola = new ColaEnvios((telefono, texto) => wa.enviar(telefono, texto), log);

const app = express();
app.use(express.json());

app.get("/salud", (req, res) => res.json({ ok: true }));

app.get("/estado", (req, res) => res.json(wa.estado()));

app.post("/enviar", (req, res) => {
    if (!secretoValido(req.headers["x-shared-secret"])) return res.status(401).json({ exito: false, error: "Secreto inválido." });
    const { telefono, texto } = req.body || {};
    if (!telefono || !texto) return res.status(400).json({ exito: false, error: "Faltan telefono o texto." });
    const resultado = cola.encolar(telefono, texto);
    if (!resultado.ok) return res.status(503).json({ exito: false, error: resultado.error });
    res.json({ exito: true, idExterno: resultado.idExterno });
});

app.listen(PUERTO, "127.0.0.1", () => {
    log.info(`whatsapp-service escuchando en 127.0.0.1:${PUERTO}`);
    wa.iniciar();
});
