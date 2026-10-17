// La app HTTP de whatsapp-service (Express), separada de index.js para poder probarla con un backend falso.
// Es la interfaz que usa la API de YoSoy (CanalWhatsAppWebJs y WhatsAppAdminService): no cambia según el backend.
//
//   GET  /salud        sin secreto, solo loopback. Con Baileys dice en qué estado está (ver servicio.salud()).
//   GET  /estado       { conectado, numero, qrPendiente, qr } (más campos extra según el backend). Pide secreto.
//   POST /enviar       { telefono, texto, optIn? } → { exito, idExterno } o { exito:false, error, codigo }. Pide secreto.
//                      Con Baileys puede rechazar el pedido: 400 telefono_invalido · 403 sin_consentimiento / baja ·
//                      429 tope_diario / limite_destinatario · 503 no_conectado / cola_llena / reiniciando.
//   POST /desvincular  Pide secreto. Responde apenas valida, sin esperar al reinicio.
//   POST /vincular     Solo Baileys: abre una ventana de vinculación por QR (el QR se ve en GET /estado). Pide secreto.
//                      409 si no corresponde (ya hay un número vinculado, todavía se está conectando...).
//
// El secreto es X-Shared-Secret (Mensajeria__WhatsApp__Secreto), comparado en tiempo constante.
"use strict";

const crypto = require("node:crypto");
const express = require("express");
const { ocultarNumeros } = require("./privacidad");

const MAX_TEXTO = 4096;

function secretoValido(esperadoTexto, recibido) {
    const esperado = Buffer.from(esperadoTexto, "utf8");
    const dado = Buffer.from(typeof recibido === "string" ? recibido : "", "utf8");
    // Los buffers tienen que medir lo mismo para timingSafeEqual; si no, ya es inválido
    // (comparar igual mide tiempo constante para esa rama, no filtra la longitud real).
    if (esperado.length !== dado.length) return false;
    return crypto.timingSafeEqual(esperado, dado);
}

function crearApp({ servicio, cola, secreto, log }) {
    const app = express();
    app.disable("x-powered-by");
    app.use(express.json({ limit: "32kb" }));

    const autorizado = req => secretoValido(secreto, req.headers["x-shared-secret"]);

    // Express 4 no atrapa solo el rechazo de un handler async: sin esto, un error dejaría el pedido colgado.
    const asincrono = (manejador, respuestaDeError) => async (req, res) => {
        try {
            await manejador(req, res);
        } catch (err) {
            log.error(ocultarNumeros(`Error atendiendo ${req.method} ${req.path}: ${err?.message || err}`));
            if (!res.headersSent) res.status(500).json(respuestaDeError);
        }
    };

    app.get("/salud", (req, res) => {
        if (typeof servicio.salud !== "function") return res.json({ ok: true });
        const salud = servicio.salud();
        res.status(salud.ok ? 200 : 503).json(salud);
    });

    // El QR viaja en esta misma respuesta (ver estado() de cada backend), así que se gatea igual que /enviar.
    app.get("/estado", asincrono(async (req, res) => {
        if (!autorizado(req)) return res.status(401).json({ error: "Secreto inválido." });
        res.set("Cache-Control", "no-store");
        res.json(await servicio.estado());
    }, { error: "Error interno." }));

    app.post("/enviar", (req, res) => {
        if (!autorizado(req)) return res.status(401).json({ exito: false, error: "Secreto inválido." });
        const { telefono, texto, optIn } = req.body || {};
        const telefonoValido = (typeof telefono === "string" || typeof telefono === "number") && String(telefono).trim() !== "";
        if (!telefonoValido || typeof texto !== "string" || texto.trim() === "") {
            return res.status(400).json({ exito: false, error: "Faltan telefono o texto." });
        }
        if (texto.length > MAX_TEXTO) {
            return res.status(400).json({ exito: false, error: `El texto es demasiado largo (máximo ${MAX_TEXTO} caracteres).` });
        }
        const resultado = cola.encolar(String(telefono), texto, { optIn: optIn === true });
        if (!resultado.ok) {
            return res.status(resultado.estado ?? 503).json({ exito: false, error: resultado.error, ...(resultado.codigo ? { codigo: resultado.codigo } : {}) });
        }
        res.json({ exito: true, idExterno: resultado.idExterno });
    });

    // Responde apenas valida el secreto, sin esperar a que termine el reinicio (puede tardar varios segundos): mismo criterio que
    // /enviar, que responde apenas encola.
    app.post("/desvincular", (req, res) => {
        if (!autorizado(req)) return res.status(401).json({ exito: false, error: "Secreto inválido." });
        servicio.desvincular().catch(err => log.error(ocultarNumeros(`Error desvinculando: ${err?.message || err}`)));
        res.json({ exito: true });
    });

    app.post("/vincular", asincrono(async (req, res) => {
        if (!autorizado(req)) return res.status(401).json({ exito: false, error: "Secreto inválido." });
        if (typeof servicio.vincular !== "function") {
            return res.status(404).json({ exito: false, error: "Este backend no tiene vinculación a pedido." });
        }
        res.set("Cache-Control", "no-store");
        const resultado = await servicio.vincular();
        res.status(resultado.exito === false ? 409 : 200).json({ exito: true, ...resultado });
    }, { exito: false, error: "Error interno." }));

    app.use((req, res) => res.status(404).json({ exito: false, error: "No existe." }));

    // Siempre JSON, nunca el HTML con la pila de errores que devuelve Express por defecto.
    // eslint-disable-next-line no-unused-vars
    app.use((err, req, res, next) => {
        if (err?.type === "entity.parse.failed") return res.status(400).json({ exito: false, error: "El cuerpo no es un JSON válido." });
        if (err?.type === "entity.too.large") return res.status(413).json({ exito: false, error: "El cuerpo es demasiado grande." });
        log.error(ocultarNumeros(`Error no esperado en el servidor HTTP: ${err?.message || err}`));
        res.status(500).json({ exito: false, error: "Error interno." });
    });

    return app;
}

// Solo loopback, a propósito: nunca se expone a internet.
function escuchar(app, puerto, alEscuchar, host = "127.0.0.1") {
    return app.listen(puerto, host, alEscuchar);
}

module.exports = { crearApp, escuchar, secretoValido, MAX_TEXTO };
