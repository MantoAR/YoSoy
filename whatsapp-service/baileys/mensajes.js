// Mensajes entrantes de Baileys: qué se ignora, de quién es y qué trae. Todo puro, sin red.
//
// Importante para la privacidad: el texto de un paciente y el nombre de su perfil de WhatsApp viajan de acá a la API en memoria y NUNCA se escriben en un log (ver
// baileys/servicio.js); acá solo se devuelven.
"use strict";

const { esJidDeUsuario, numeroDeJid } = require("./numeros");

const MAX_TEXTO = 4000;    // la API recorta a 4000 (ServicioMensajes.Recortar): no tiene sentido mandarle más
const MAX_NOMBRE = 100;    // Conversacion.NombreContacto

// Lo que no es texto no se descarga ni se guarda (decisión D4): viaja un AVISO del tipo, con la leyenda a continuación si la trae ("[Imagen] mi receta"), y la persona del
// equipo mira el archivo en el teléfono del número. Son los mismos avisos que arma la API (TiposDeMensaje.Aviso) cuando un cliente no los manda.
const AVISOS = Object.freeze({
    audio: "[Audio]", imagen: "[Imagen]", video: "[Video]", documento: "[Documento]", sticker: "[Sticker]", ubicacion: "[Ubicación]", contacto: "[Contacto]"
});

// Los controles (saltos de línea, tabuladores...) y los caracteres que invierten o esconden texto: marcas de dirección, aislamientos, espacios de ancho cero y la marca de orden de
// bytes. NO se sacan el unión de ancho cero ni el separador (U+200C y U+200D): hacen falta para los emojis compuestos y algunas escrituras. La API vuelve a limpiar el nombre con
// las mismas reglas (DatosDelEntrante.LimpiarNombre): acá se limpia para que ni siquiera viaje.
const OCULTOS = [0x200B, 0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E, 0x2060, 0x2061, 0x2062, 0x2063, 0x2064, 0x2066, 0x2067, 0x2068, 0x2069, 0xFEFF];
const INVISIBLES = new RegExp(`[\\p{Cc}${OCULTOS.map(c => String.fromCodePoint(c)).join("")}]+`, "gu");

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

// El texto que escribió la persona. Una imagen, un audio, una ubicación, una reacción o un mensaje de protocolo no tienen texto: devuelve null (ver clasificar).
function extraerTexto(mensaje) {
    const m = desenvolver(mensaje);
    if (!m) return null;
    if (typeof m.conversation === "string") return m.conversation;
    if (typeof m.extendedTextMessage?.text === "string") return m.extendedTextMessage.text;
    return null;
}

function conAviso(tipo, leyenda) {
    const dicha = typeof leyenda === "string" ? leyenda.trim() : "";
    return { tipo, texto: dicha === "" ? AVISOS[tipo] : `${AVISOS[tipo]} ${dicha}` };
}

// Qué trae un mensaje: { tipo, texto }, donde texto es lo que escribió la persona o el aviso del tipo (con la leyenda a continuación si la trae). null si no es algo que una
// persona le escribió a la clínica: reacciones, mensajes de protocolo (ediciones, borrados, sincronización), encuestas, botones... Ni el nombre de un archivo ni lo que trae una
// ubicación o un contacto compartido (que es de un tercero) viajan: solo el aviso del tipo.
function clasificar(mensaje) {
    const m = desenvolver(mensaje);
    if (!m) return null;
    const texto = extraerTexto(mensaje);
    if (texto !== null) return { tipo: "texto", texto };
    if (m.imageMessage) return conAviso("imagen", m.imageMessage.caption);
    if (m.videoMessage) return conAviso("video", m.videoMessage.caption);                 // los GIF de WhatsApp también son videoMessage
    if (m.audioMessage) return conAviso("audio");                                         // y las notas de voz
    if (m.documentMessage) return conAviso("documento", m.documentMessage.caption);
    if (m.stickerMessage) return conAviso("sticker");
    if (m.locationMessage || m.liveLocationMessage) return conAviso("ubicacion");
    if (m.contactMessage || m.contactsArrayMessage) return conAviso("contacto");
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

// La hora del mensaje en segundos desde 1970 (WhatsApp la manda como número o como un Long de protobuf), o null si no es un número razonable.
function segundosDe(marca) {
    const n = typeof marca === "number" ? marca : (typeof marca?.toNumber === "function" ? marca.toNumber() : Number(marca));
    return Number.isFinite(n) && n > 0 ? Math.floor(n) : null;
}

// El nombre del perfil de WhatsApp (pushName): sin invisibles ni saltos de línea, con los espacios juntos y de hasta MAX_NOMBRE caracteres (sin partir un emoji), o null si no
// queda nada. Es un dato personal: nunca se loguea.
function nombreDe(pushName) {
    if (typeof pushName !== "string") return null;
    const limpio = pushName.replace(INVISIBLES, " ").replace(/\s+/g, " ").trim();
    if (limpio === "") return null;
    return Array.from(limpio).slice(0, MAX_NOMBRE).join("").trim();
}

// Devuelve { ignorar: "motivo" } o { telefono, texto, tipo, id, ts?, nombre? }. Los motivos son para contar/depurar, no incluyen datos del paciente.
function interpretarEntrante(msg) {
    const clave = msg?.key;
    if (!clave || typeof clave.remoteJid !== "string") return { ignorar: "sin_clave" };
    if (clave.fromMe) return { ignorar: "propio" };

    const jid = clave.remoteJid;
    if (jid.endsWith("@g.us")) return { ignorar: "grupo" };                        // grupos
    if (jid.endsWith("@broadcast")) return { ignorar: "difusion" };                // listas de difusión y "status@broadcast"
    if (jid.endsWith("@newsletter")) return { ignorar: "canal" };                  // canales

    const contenido = clasificar(msg.message);
    if (contenido === null) return { ignorar: "no_textual" };
    const limpio = contenido.texto.trim();
    if (limpio === "") return { ignorar: "vacio" };

    const telefono = telefonoDeClave(clave);
    if (!telefono) return { ignorar: "sin_numero" };

    const entrante = { telefono, texto: limpio.length > MAX_TEXTO ? limpio.slice(0, MAX_TEXTO) : limpio, tipo: contenido.tipo, id: clave.id ?? null };
    const ts = segundosDe(msg.messageTimestamp);
    if (ts !== null) entrante.ts = ts;
    const nombre = nombreDe(msg.pushName);
    if (nombre !== null) entrante.nombre = nombre;
    return entrante;
}

module.exports = { extraerTexto, clasificar, telefonoDeClave, interpretarEntrante, segundosDe, nombreDe, MAX_TEXTO, MAX_NOMBRE, AVISOS };
