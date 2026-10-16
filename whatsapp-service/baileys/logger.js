// Logger que se le pasa a Baileys. La librería espera una interfaz tipo pino (level, child y un método por nivel).
//
// Privacidad: lo que Baileys loguea puede traer JIDs, textos de mensajes y claves dentro de los objetos que acompañan a cada
// línea. Acá NUNCA se escribe un objeto: de cada llamada se toma solo el texto de la línea y, si hay un error, su message; todo
// pasa por ocultarNumeros() y se corta a 300 caracteres. trace/debug/info se descartan (son los niveles más charlatanes).
// Además hay un tope de líneas por minuto, para que una falla en bucle de la librería no llene el journal.
"use strict";

const { ocultarNumeros } = require("../privacidad");

const MAX_CARACTERES = 300;

function textoSeguro(primero, segundo) {
    const linea = typeof primero === "string" ? primero : (typeof segundo === "string" ? segundo : "");
    const objeto = primero !== null && typeof primero === "object" ? primero : null;
    const error = objeto?.err ?? objeto?.error;
    const detalle = error && typeof error.message === "string" ? error.message : "";
    return [linea, detalle].filter(Boolean).join(" — ").slice(0, MAX_CARACTERES) || "(sin texto)";
}

function crearLoggerBaileys(log, { maxPorMinuto = 30, ahora = Date.now } = {}) {
    let inicioVentana = ahora();
    let emitidas = 0;
    let suprimidas = 0;

    function emitir(nivel, primero, segundo) {
        const t = ahora();
        if (t - inicioVentana >= 60000) {
            if (suprimidas > 0) log.error(`Baileys: se omitieron ${suprimidas} líneas de log en el último minuto.`);
            inicioVentana = t;
            emitidas = 0;
            suprimidas = 0;
        }
        if (emitidas >= maxPorMinuto) { suprimidas++; return; }
        emitidas++;
        log.error(ocultarNumeros(`Baileys ${nivel}: ${textoSeguro(primero, segundo)}`));
    }

    const callar = () => {};
    const logger = {
        level: "warn",
        trace: callar,
        debug: callar,
        info: callar,
        warn: (a, b) => emitir("warn", a, b),
        error: (a, b) => emitir("error", a, b),
        child: () => logger
    };
    return logger;
}

module.exports = { crearLoggerBaileys, textoSeguro };
