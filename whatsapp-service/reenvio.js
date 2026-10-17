// Reenvío de los mensajes entrantes a la API de YoSoy: POST {API_BASE_URL}/mensajeria/whatsapp/entrante con el secreto compartido.
// Es lo mismo que hace whatsapp.js (_reenviarSinBloquear) para whatsapp-web.js, para que los dos backends hablen igual con la API.
//
// Nunca bloquea el procesamiento del próximo evento (timeout corto propio, el error solo se loguea) y, por privacidad, el log
// no incluye el texto del mensaje ni el cuerpo de la respuesta: solo el estado HTTP o el motivo del fallo, con los números enmascarados.
"use strict";

const { ocultarNumeros } = require("./privacidad");

function crearReenviador({ apiBaseUrl, secreto, log, fetchFn = (...a) => fetch(...a), timeoutMs = 5000, programar = setTimeout, cancelar = clearTimeout }) {
    const base = String(apiBaseUrl).replace(/\/+$/, "");

    // Devuelve la promesa solo para que los tests puedan esperarla; quien llama no la espera.
    return function reenviar(telefono, texto) {
        const controlador = new AbortController();
        const vencimiento = programar(() => controlador.abort(), timeoutMs);
        return fetchFn(`${base}/mensajeria/whatsapp/entrante`, {
            method: "POST",
            headers: { "Content-Type": "application/json", "X-Shared-Secret": secreto },
            body: JSON.stringify({ telefono, texto }),
            signal: controlador.signal
        })
            .then(async respuesta => {
                try { await respuesta.arrayBuffer(); } catch { /* se descarta: solo importa el estado */ }
                if (!respuesta.ok) log.error(`La API respondió ${respuesta.status} al mensaje entrante.`);
            })
            .catch(err => log.error(ocultarNumeros(`No se pudo reenviar el mensaje entrante a la API: ${err?.message || err}`)))
            .finally(() => cancelar(vencimiento));
    };
}

module.exports = { crearReenviador };
