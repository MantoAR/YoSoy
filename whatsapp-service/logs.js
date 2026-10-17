// El log del proceso: una línea por mensaje, con la hora, a la salida estándar (info) o a la de errores (error); systemd las junta en
// el journal. Con Baileys todo lo que se escribe pasa además por un filtro de privacidad (privacidad.js, crearLogSeguro): los números
// de teléfono quedan enmascarados y los secretos (el compartido con la API, el QR que esté vigente) tapados, aunque algún mensaje de
// error de una librería los repita. Es la última red: cada módulo ya enmascara lo suyo y una prueba lo vigila.
"use strict";

const { crearLogSeguro } = require("./privacidad");

function crearLogDelProceso({ consola = console, ahora = () => new Date() } = {}) {
    return {
        info: mensaje => consola.log(`[${ahora().toISOString()}] ${mensaje}`),
        error: mensaje => consola.error(`[${ahora().toISOString()}] ${mensaje}`)
    };
}

// El QR vale como un secreto mientras no se escanea o vence: entero, y también cada una de sus partes (la referencia y las tres claves).
function secretosDelQr(qr) {
    if (typeof qr !== "string" || qr === "") return [];
    return [qr, ...qr.split(",").filter(parte => parte.length >= 20)];
}

// Con whatsapp-web.js, el log de siempre, tal cual. Con Baileys, el filtrado. `secretosVivos` devuelve, en cada línea, lo que en ese
// momento no puede aparecer (por ejemplo, el texto del QR vigente).
function logParaBackend(config, log, secretosVivos = () => []) {
    if (config.backend !== "baileys") return log;
    return crearLogSeguro(log, { secretos: () => [config.secreto, ...secretosVivos().flatMap(secretosDelQr)] });
}

module.exports = { crearLogDelProceso, logParaBackend, secretosDelQr };
