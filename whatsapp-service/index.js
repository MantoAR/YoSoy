// Puente HTTP interno entre la API de YoSoy y WhatsApp. Solo escucha en loopback — ver README.md para qué hace falta instalado en
// el VPS y cómo se vincula el número.
//
// Dos backends detrás de la misma interfaz HTTP (WHATSAPP_BACKEND):
//   webjs   (por defecto) whatsapp-web.js + Chromium
//   baileys WhatsApp Web sin navegador, más liviano
// La lógica de cada uno está en whatsapp.js y baileys/; la app HTTP, en servidor.js; la configuración, en config.js.
"use strict";

const { leerConfig, ErrorDeConfiguracion } = require("./config");
const { crearBackend } = require("./backend");
const { crearApp, escuchar } = require("./servidor");
const { ColaEnvios } = require("./cola");

const log = {
    info: msg => console.log(`[${new Date().toISOString()}] ${msg}`),
    error: msg => console.error(`[${new Date().toISOString()}] ${msg}`)
};

async function main() {
    let config;
    try {
        config = leerConfig(process.env);
    } catch (err) {
        if (!(err instanceof ErrorDeConfiguracion)) throw err;
        log.error(err.message);
        process.exit(1);
    }

    let wa;
    try {
        wa = await crearBackend(config, log);
    } catch (err) {
        log.error(`No se pudo preparar el backend de WhatsApp (${config.backend}): ${err?.message || err}`);
        process.exit(1);
    }

    const cola = new ColaEnvios((telefono, texto, meta) => wa.enviar(telefono, texto, meta), log);
    const app = crearApp({ servicio: wa, cola, secreto: config.secreto, log });

    const servidor = escuchar(app, config.puerto, () => {
        log.info(`whatsapp-service escuchando en 127.0.0.1:${config.puerto} (backend ${config.backend})`);
        wa.iniciar();
    });
    servidor.on("error", err => {
        log.error(`No se pudo abrir el puerto ${config.puerto}: ${err.message || err}`);
        process.exit(1);
    });

    // Red de seguridad: un error sin capturar en el código de la librería (fuera de nuestro control directo) no tiene por qué
    // tirar abajo todo el proceso — se loguea (nunca el objeto entero del error, puede traer de más) y se le pide al backend que
    // se recupere por el mismo camino que un corte transitorio.
    process.on("unhandledRejection", razon => {
        log.error(`unhandledRejection (no se cae el proceso): ${razon?.message || razon}`);
        wa.recuperar();
    });
    process.on("uncaughtException", err => {
        log.error(`uncaughtException (no se cae el proceso): ${err?.message || err}`);
        wa.recuperar();
    });

    // Apagado limpio (systemd manda SIGTERM al hacer stop/restart; TimeoutStopSec=20 en la unit le da margen de sobra a este
    // timeout interno de 10s + el tiempo propio de Node/SO). Orden: dejar de aceptar pedidos nuevos (servidor + cola), después
    // cerrar el cliente de WhatsApp con un tope de tiempo — nunca bloquear el apagado esperando a la librería.
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
}

main().catch(err => {
    log.error(`Error fatal al arrancar: ${err?.message || err}`);
    process.exit(1);
});
