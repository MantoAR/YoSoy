// Puente HTTP interno entre la API de YoSoy y WhatsApp. Solo escucha en loopback — ver README.md para qué hace falta instalado en
// el VPS y cómo se vincula el número.
//
// El único motor es Baileys (WhatsApp Web sin navegador): baileys/ tiene la lógica de la conexión; backend.js arma el servicio con
// el consentimiento y los topes de envío; la app HTTP está en servidor.js y la configuración, en config.js.
"use strict";

const { leerConfig, variablesRetiradasPresentes, ErrorDeConfiguracion } = require("./config");
const { crearBackend } = require("./backend");
const { crearApp, escuchar } = require("./servidor");
const { crearColaParaBackend } = require("./cola");
const { crearApagado } = require("./apagado");
const { crearLogDelProceso, logParaBackend } = require("./logs");
const { ocultarNumeros } = require("./privacidad");

const logBase = crearLogDelProceso();

async function main() {
    let config;
    try {
        config = leerConfig(process.env);
    } catch (err) {
        if (!(err instanceof ErrorDeConfiguracion)) throw err;
        logBase.error(err.message);               // un error de configuración nunca trae un secreto (lo vigilan las pruebas)
        process.exit(1);
    }

    // Todo lo que se escribe pasa por un filtro de privacidad: los números de teléfono quedan enmascarados y los secretos (el
    // compartido con la API y el QR vigente) tapados, aunque algún mensaje de error de una librería los repita.
    let wa;
    const log = logParaBackend(config, logBase, () => [wa?.qrTexto?.()]);

    // Variables de whatsapp-web.js que quedaron en el entorno (por ejemplo en whatsapp.env): no hacen nada. Solo se dice el nombre.
    const retiradas = variablesRetiradasPresentes(process.env);
    if (retiradas.length > 0) {
        log.info(`Aviso: estas variables de entorno ya no se usan (se sacó whatsapp-web.js y el único motor es Baileys): ${retiradas.join(", ")}. Se pueden borrar.`);
    }

    try {
        wa = await crearBackend(config, log);
    } catch (err) {
        log.error(`No se pudo preparar WhatsApp (Baileys): ${err?.message || err}`);
        process.exit(1);
    }

    const cola = crearColaParaBackend(wa, log);
    const app = crearApp({ servicio: wa, cola, secreto: config.secreto, log });

    const servidor = escuchar(app, config.puerto, () => {
        log.info(`whatsapp-service escuchando en 127.0.0.1:${config.puerto} (Baileys)`);
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
        log.error(`unhandledRejection (no se cae el proceso): ${ocultarNumeros(razon?.message || razon)}`);
        wa.recuperar();
    });
    process.on("uncaughtException", err => {
        log.error(`uncaughtException (no se cae el proceso): ${ocultarNumeros(err?.message || err)}`);
        wa.recuperar();
    });

    // Apagado ordenado (ver apagado.js): systemd manda SIGTERM al hacer stop o restart.
    const apagar = crearApagado({ servidor, cola, wa, log });
    process.on("SIGTERM", () => apagar("SIGTERM"));
    process.on("SIGINT", () => apagar("SIGINT"));
}

main().catch(err => {
    logBase.error(`Error fatal al arrancar: ${ocultarNumeros(err?.message || err)}`);
    process.exit(1);
});
