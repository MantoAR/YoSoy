// Teléfonos y JID (los identificadores de WhatsApp: "5491122334455@s.whatsapp.net"). Todo puro, sin red.
"use strict";

const soloDigitos = valor => String(valor ?? "").replace(/\D/g, "");

// Igual que Telefono.Normalizar de la API (C#): deja solo los dígitos y se queda con los últimos 10. Es la clave con la que la
// API identifica a un paciente por teléfono ("+54 9 11 5555-0001" y "11 5555-0001" dan lo mismo).
function ultimosDiez(valor) {
    const digitos = soloDigitos(valor);
    return digitos.length > 10 ? digitos.slice(-10) : digitos;
}

// Números a consultar contra WhatsApp (en orden de preferencia) para un teléfono que manda la API.
// La API manda normalmente 10 dígitos (área + abonado, sin 54 ni 9): un celular argentino en WhatsApp es "549" + eso, y a veces
// figura como "54" + eso, así que se consultan los dos y WhatsApp contesta cuál existe. Con prefijo de país completo se respeta.
// [] si no puede ser un teléfono (muy corto o muy largo).
function candidatos(telefono) {
    const d = soloDigitos(telefono);
    if (d.length < 8 || d.length > 15) return [];
    if (d.length === 11 && d.startsWith("0")) return candidatos(d.slice(1));   // 0 de discado nacional
    if (d.length === 10) return [`549${d}`, `54${d}`];
    if (d.length === 13 && d.startsWith("549")) return [d, `54${d.slice(3)}`];
    if (d.length === 12 && d.startsWith("54")) return [`549${d.slice(2)}`, d];
    return [d];
}

const jidDeNumero = numero => `${soloDigitos(numero)}@s.whatsapp.net`;
const esJidDeUsuario = jid => typeof jid === "string" && jid.endsWith("@s.whatsapp.net");

// "5491122334455:12@s.whatsapp.net" (con número de dispositivo) o "5491122334455@s.whatsapp.net" → "5491122334455".
function numeroDeJid(jid) {
    if (typeof jid !== "string") return null;
    const parte = jid.split("@")[0].split(":")[0];
    const digitos = soloDigitos(parte);
    return digitos.length > 0 && digitos === parte ? digitos : null;
}

module.exports = { soloDigitos, ultimosDiez, candidatos, jidDeNumero, esJidDeUsuario, numeroDeJid };
