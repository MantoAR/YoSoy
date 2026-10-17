// Carga de Baileys y "fachada" con lo poquito que usa este servicio.
//
// Baileys es ESM puro ("type": "module"), y este servicio es CommonJS: se carga con import() dinámico, y SOLO acá. Nada más del
// servicio importa la librería; el resto recibe la fachada. Eso tiene tres consecuencias buscadas:
//   - los tests le pasan una fachada falsa y nunca se abre una conexión real a WhatsApp;
//   - un cambio de versión de la librería se nota en un solo lugar (y lo vigila test/contrato-baileys.test.js);
//   - con WHATSAPP_BACKEND=webjs (el valor por defecto) la librería ni se carga.
//
// Cargar la librería NO abre ninguna conexión: eso recién pasa cuando alguien llama a makeWASocket.
"use strict";

const NOMBRE_PAQUETE = "@whiskeysockets/baileys";

// Códigos de DisconnectReason que usa la lógica de reconexión (baileys/reconexion.js). Si la versión instalada no los trae como
// números, no se arranca: una reconexión que se equivoca de motivo puede terminar en un bucle contra los servidores de WhatsApp.
const MOTIVOS_REQUERIDOS = ["loggedOut", "forbidden", "connectionReplaced", "badSession", "restartRequired", "timedOut", "connectionClosed", "connectionLost"];

function importarPaquete() {
    return import("@whiskeysockets/baileys");
}

// El espacio de nombres de un import() trae los exports con nombre y, en "default", la función makeWASocket. Si alguna versión
// futura se publicara como CommonJS, los exports quedarían dentro de "default" como objeto: se contemplan las dos formas.
function crearFachada(modulo) {
    if (!modulo || (typeof modulo !== "object" && typeof modulo !== "function")) {
        throw new Error(`El paquete ${NOMBRE_PAQUETE} no devolvió un módulo utilizable.`);
    }
    const base = (modulo.default && typeof modulo.default === "object") ? modulo.default : modulo;
    const makeWASocket = typeof base.makeWASocket === "function" ? base.makeWASocket
        : (typeof modulo.default === "function" ? modulo.default : base.default);

    const fachada = {
        makeWASocket,
        useMultiFileAuthState: base.useMultiFileAuthState,
        DisconnectReason: base.DisconnectReason,
        Browsers: base.Browsers,
        // Opcionales: mejoras que se usan si están.
        fetchLatestBaileysVersion: base.fetchLatestBaileysVersion,
        makeCacheableSignalKeyStore: base.makeCacheableSignalKeyStore
    };
    validarFachada(fachada);
    return Object.freeze(fachada);
}

function validarFachada(f) {
    const faltan = [];
    if (typeof f.makeWASocket !== "function") faltan.push("makeWASocket (función)");
    if (typeof f.useMultiFileAuthState !== "function") faltan.push("useMultiFileAuthState (función)");
    if (!f.Browsers || typeof f.Browsers.ubuntu !== "function") faltan.push("Browsers.ubuntu (función)");
    if (!f.DisconnectReason || typeof f.DisconnectReason !== "object") {
        faltan.push("DisconnectReason (objeto)");
    } else {
        for (const motivo of MOTIVOS_REQUERIDOS) {
            if (typeof f.DisconnectReason[motivo] !== "number") faltan.push(`DisconnectReason.${motivo} (número)`);
        }
    }
    if (faltan.length > 0) {
        throw new Error(
            `La versión instalada de ${NOMBRE_PAQUETE} no expone lo que usa whatsapp-service: ${faltan.join(", ")}. ` +
            "¿Se cambió la versión? Revisar baileys/cargar.js y correr `npm test`.");
    }
}

// `importar` se puede reemplazar en los tests para no tocar el paquete real.
async function cargarBaileys(importar = importarPaquete) {
    let modulo;
    try {
        modulo = await importar();
    } catch (err) {
        if (err && (err.code === "ERR_MODULE_NOT_FOUND" || err.code === "MODULE_NOT_FOUND")) {
            const nuevo = new Error(`No está instalado ${NOMBRE_PAQUETE}. Instalar las dependencias con \`npm ci --omit=dev\` (ver README.md).`);
            nuevo.code = err.code;
            throw nuevo;
        }
        throw err;
    }
    return crearFachada(modulo);
}

module.exports = { cargarBaileys, crearFachada, validarFachada, NOMBRE_PAQUETE, MOTIVOS_REQUERIDOS };
