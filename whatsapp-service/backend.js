// Elige y arma el backend de WhatsApp según WHATSAPP_BACKEND. Cada librería se carga SOLO si se elige: con "baileys" no se
// carga whatsapp-web.js (ni Puppeteer), y con "webjs" (el valor por defecto) no se carga Baileys.
//
// Los dos backends tienen la misma interfaz, que es la que usa servidor.js:
//   iniciar()                 empieza a conectar
//   cerrar()                  apagado ordenado
//   estado()                  { conectado, numero, qrPendiente, qr, ... }
//   enviar(telefono, texto)   lo llama la cola de envíos
//   desvincular()             cierra la sesión y deja listo un QR nuevo
//   recuperar()               red de seguridad ante un error sin capturar del proceso
//   salud()      (opcional)   { ok, estado, ... } para GET /salud
//   vincular()   (opcional)   arranca una vinculación por QR a pedido
"use strict";

async function crearBackend(config, log, deps = {}) {
    if (config.backend === "baileys") {
        const { crearSesion } = require("./baileys/sesion");
        const { ServicioBaileys } = require("./baileys/servicio");
        const { crearReenviador } = require("./reenvio");
        // `deps` existe para los tests: reemplaza lo que toca la librería real y la red.
        const cargar = deps.cargarBaileys ?? require("./baileys/cargar").cargarBaileys;

        const fachada = await cargar();
        const sesion = crearSesion({ dir: config.baileys.sessionPath, log });
        const reenviar = deps.reenviar ?? crearReenviador({ apiBaseUrl: config.apiBaseUrl, secreto: config.secreto, log });
        return new ServicioBaileys({
            fachada,
            sesion,
            log,
            reenviar,
            versionWa: config.baileys.versionWa,
            buscarVersionWa: config.baileys.buscarVersionWa
        });
    }

    const { ServicioWhatsApp } = require("./whatsapp");   // acá recién se carga whatsapp-web.js
    return new ServicioWhatsApp({
        dataPath: config.webjs.sessionPath,
        apiBaseUrl: config.apiBaseUrl,
        secreto: config.secreto,
        webVersion: config.webjs.webVersion,
        debug: config.debug,
        log
    });
}

module.exports = { crearBackend };
