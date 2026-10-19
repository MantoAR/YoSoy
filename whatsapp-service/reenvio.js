// Reenvío de los mensajes entrantes a la API de YoSoy: POST {API_BASE_URL}/mensajeria/whatsapp/entrante con el secreto compartido.
//
// Cuerpo (contrato v2 del webhook; la API vieja ignora lo que no conoce y la nueva acepta el cuerpo de siempre):
//     { telefono, texto, tipo?, id?, ts?, nombre? }
//   tipo: texto, audio, imagen, video, documento, sticker, ubicacion o contacto; id: el del mensaje en WhatsApp (la API lo usa para no guardar dos veces el mismo mensaje);
//   ts: la hora a la que se escribió, en segundos desde 1970; nombre: el nombre del perfil de WhatsApp (decisión D5: la API lo guarda en la conversación y NUNCA se escribe en
//   un log, ni acá ni allá).
//
// Si la API falla se REINTENTA, solo EN MEMORIA (decisión D6): el texto de un mensaje nunca se escribe a disco.
//   · se reintenta ante lo que puede ser pasajero: no se puede conectar, no contesta a tiempo (timeoutMs), 5xx o 429. NO ante otro 4xx (un pedido mal formado, un secreto que no
//     coincide): reintentarlo daría lo mismo;
//   · hasta 3 reintentos, a los 5 s, 30 s y 2 min del intento anterior (unos 2 minutos y medio en total);
//   · los mensajes de una misma persona salen de a uno y en orden: mientras uno espera su reintento, los siguientes de esa persona esperan atrás;
//   · como mucho `maxEnCola` mensajes en espera a la vez; los que no entran se pierden (y se cuentan);
//   · un reintento de algo que la API sí había recibido no se guarda dos veces: la API reconoce el mismo mensaje por su id.
//   Al apagar el servicio lo que está esperando se pierde (detener()): no hay otro lugar donde guardarlo, y escribirlo a disco está descartado.
//
// Nunca bloquea el procesamiento del próximo evento y, por privacidad, el log no incluye el texto, ni el nombre, ni el teléfono completo, ni el cuerpo de la respuesta: solo el
// estado HTTP o el motivo del fallo, con los números enmascarados. resumen() devuelve solo números (es lo que sale en GET /salud).
"use strict";

const { ocultarNumeros } = require("./privacidad");

const REINTENTOS_MS = Object.freeze([5000, 30000, 120000]);
const MAX_EN_COLA = 200;
const CAMPOS_V2 = Object.freeze(["tipo", "id", "ts", "nombre"]);

const intentos = n => `${n} ${n === 1 ? "intento" : "intentos"}`;

function camposV2(extra) {
    const r = {};
    for (const campo of CAMPOS_V2) {
        if (extra?.[campo] !== undefined && extra[campo] !== null) r[campo] = extra[campo];
    }
    return r;
}

function crearReenviador({
    apiBaseUrl, secreto, log, fetchFn = (...a) => fetch(...a), timeoutMs = 5000, programar = setTimeout, cancelar = clearTimeout,
    reintentosMs = REINTENTOS_MS, maxEnCola = MAX_EN_COLA
}) {
    const url = `${String(apiBaseUrl).replace(/\/+$/, "")}/mensajeria/whatsapp/entrante`;
    const contadores = { reenviados: 0, reintentados: 0, fallidos: 0, perdidos: 0 };
    const cadenas = new Map();     // los últimos 10 dígitos de la persona → su último reenvío (los de una misma persona salen en orden)
    const esperas = new Set();     // las esperas entre reintentos, para poder cortarlas al apagar
    let enCola = 0;                // los que todavía no terminaron: en su primer intento, esperando un reintento o esperando su turno
    let detenido = false;

    // Un intento. { ok } o { ok:false, reintentable, motivo }. El motivo nunca trae el texto del mensaje ni el cuerpo de la respuesta.
    async function intentar(cuerpo) {
        const controlador = new AbortController();
        const vencimiento = programar(() => controlador.abort(), timeoutMs);
        try {
            const respuesta = await fetchFn(url, {
                method: "POST",
                headers: { "Content-Type": "application/json", "X-Shared-Secret": secreto },
                body: cuerpo,
                signal: controlador.signal
            });
            try { await respuesta.arrayBuffer(); } catch { /* se descarta: solo importa el estado */ }
            if (respuesta.ok) return { ok: true };
            return {
                ok: false, reintentable: respuesta.status >= 500 || respuesta.status === 429,
                motivo: `La API respondió ${respuesta.status} al mensaje entrante.`
            };
        } catch (err) {
            return { ok: false, reintentable: true, motivo: ocultarNumeros(`No se pudo reenviar el mensaje entrante a la API: ${err?.message || err}`) };
        } finally {
            cancelar(vencimiento);
        }
    }

    function dormir(ms) {
        return new Promise(resolver => {
            const espera = { id: null, resolver };
            espera.id = programar(() => { esperas.delete(espera); resolver(); }, ms);
            esperas.add(espera);
        });
    }

    async function reenviarConReintentos(cuerpo) {
        const reintentos = reintentosMs.length;
        for (let intento = 0; ; intento++) {
            if (detenido) { contadores.perdidos++; return; }
            const r = await intentar(cuerpo);
            if (r.ok) { contadores.reenviados++; return; }
            if (!r.reintentable) { contadores.fallidos++; log.error(r.motivo); return; }
            if (intento >= reintentos || detenido) {
                contadores.perdidos++;
                log.error(`${r.motivo} Se perdió el mensaje (${intentos(intento + 1)}).`);
                return;
            }
            contadores.reintentados++;
            log.error(`${r.motivo} Se reintenta (${intento + 1} de ${reintentos}).`);
            await dormir(reintentosMs[intento]);
        }
    }

    // Devuelve la promesa solo para que los tests puedan esperarla (se resuelve cuando se sabe cómo terminó el mensaje, reintentos incluidos); quien llama no la espera.
    function reenviar(telefono, texto, extra = {}) {
        if (detenido) { contadores.perdidos++; return Promise.resolve(); }
        if (enCola >= maxEnCola) {
            contadores.perdidos++;
            log.error(`Se perdió un mensaje entrante: ya hay ${maxEnCola} esperando para reenviarse a la API.`);
            return Promise.resolve();
        }
        const cuerpo = JSON.stringify({ telefono, texto, ...camposV2(extra) });
        const persona = String(telefono ?? "").replace(/\D/g, "").slice(-10);
        enCola++;
        const anterior = cadenas.get(persona) ?? Promise.resolve();
        const mio = anterior.then(() => reenviarConReintentos(cuerpo)).finally(() => {
            enCola--;
            if (cadenas.get(persona) === mio) cadenas.delete(persona);
        });
        cadenas.set(persona, mio);
        return mio;
    }

    // Solo números: sin teléfonos, textos ni nombres.
    reenviar.resumen = () => ({ ...contadores, pendientes: enCola });

    // Apagado: no se reenvía más y se cortan las esperas. Devuelve cuántos mensajes quedaban sin reenviar (se pierden: son solo memoria).
    reenviar.detener = () => {
        detenido = true;
        for (const espera of esperas) { cancelar(espera.id); espera.resolver(); }
        esperas.clear();
        return enCola;
    };

    return reenviar;
}

module.exports = { crearReenviador, REINTENTOS_MS, MAX_EN_COLA };
