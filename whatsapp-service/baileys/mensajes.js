// Mensajes entrantes de Baileys: qué se ignora, de quién es y qué texto trae. Todo puro, sin red.
//
// Importante para la privacidad: el texto de un paciente viaja de acá a la API en memoria y NUNCA se escribe en un log (ver
// baileys/servicio.js); acá solo se devuelve.
"use strict";

const { esJidDeUsuario, numeroDeJid } = require("./numeros");

const MAX_TEXTO = 4000;   // la API recorta a 4000 (ServicioMensajes.Recortar): no tiene sentido mandarle más

// Los mensajes pueden venir envueltos (efímeros, "ver una vez", documento con leyenda...). Se desenvuelve hasta 5 niveles.
function desenvolver(mensaje) {
    let actual = mensaje;
    for (let i = 0; i < 5 && actual; i++) {
        const interno = actual.ephemeralMessage?.message
            ?? actual.viewOnceMessage?.message
            ?? actual.viewOnceMessageV2?.message
            ?? actual.viewOnceMessageV2Extension?.message
            ?? actual.documentWithCaptionMessage?.message;
        if (!interno) break;
        actual = interno;
    }
    return actual;
}

// Solo texto. Imágenes, audios, ubicaciones, reacciones, ediciones y mensajes de protocolo no son una conversación que el bot
// entienda: devuelven null y se ignoran (no se loguea su contenido ni su tipo detallado).
function extraerTexto(mensaje) {
    const m = desenvolver(mensaje);
    if (!m) return null;
    if (typeof m.conversation === "string") return m.conversation;
    if (typeof m.extendedTextMessage?.text === "string") return m.extendedTextMessage.text;
    return null;
}

// El número real del remitente. En un chat normal el remoteJid ya es "número@s.whatsapp.net". WhatsApp está migrando a
// identificadores anónimos ("...@lid"): ahí el número viene en otro campo de la clave, que según la versión de Baileys se
// llama senderPn / participantPn (6.x) o remoteJidAlt / participantAlt (7.x). Se prueban todos; sin número resoluble, se ignora.
function telefonoDeClave(clave) {
    const candidatos = [clave.remoteJid, clave.senderPn, clave.remoteJidAlt, clave.participantPn, clave.participantAlt];
    for (const jid of candidatos) {
        if (esJidDeUsuario(jid)) return numeroDeJid(jid);
    }
    return null;
}

// Devuelve { ignorar: "motivo" } o { telefono, texto, id }. Los motivos son para contar/depurar, no incluyen datos del paciente.
function interpretarEntrante(msg) {
    const clave = msg?.key;
    if (!clave || typeof clave.remoteJid !== "string") return { ignorar: "sin_clave" };
    if (clave.fromMe) return { ignorar: "propio" };

    const jid = clave.remoteJid;
    if (jid.endsWith("@g.us")) return { ignorar: "grupo" };                        // grupos
    if (jid.endsWith("@broadcast")) return { ignorar: "difusion" };                // listas de difusión y "status@broadcast"
    if (jid.endsWith("@newsletter")) return { ignorar: "canal" };                  // canales

    const texto = extraerTexto(msg.message);
    if (texto === null) return { ignorar: "no_textual" };
    const limpio = texto.trim();
    if (limpio === "") return { ignorar: "vacio" };

    const telefono = telefonoDeClave(clave);
    if (!telefono) return { ignorar: "sin_numero" };

    return { telefono, texto: limpio.length > MAX_TEXTO ? limpio.slice(0, MAX_TEXTO) : limpio, id: clave.id ?? null };
}

module.exports = { extraerTexto, telefonoDeClave, interpretarEntrante, MAX_TEXTO };
