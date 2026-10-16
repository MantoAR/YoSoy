// Doble de prueba de Baileys: un "módulo" con la misma forma que devuelve import("@whiskeysockets/baileys") y un socket falso que
// no abre ninguna conexión. Los tests del servicio usan SOLO esto; el paquete real se importa únicamente en
// test/contrato-baileys.test.js (para mirar qué exporta, sin conectar).
"use strict";

// Los mismos números que la librería real (los fija test/contrato-baileys.test.js contra la versión instalada).
const DisconnectReason = Object.freeze({
    connectionClosed: 428,
    connectionLost: 408,
    connectionReplaced: 440,
    timedOut: 408,
    loggedOut: 401,
    badSession: 500,
    restartRequired: 515,
    multideviceMismatch: 411,
    forbidden: 403,
    unavailableService: 503
});

function fachadaFalsa() {
    const modulo = {
        default: () => { throw new Error("makeWASocket falso: este doble no abre sockets (se amplía en baileys-falso.js)"); },
        useMultiFileAuthState: async () => ({ state: { creds: {}, keys: {} }, saveCreds: async () => {} }),
        DisconnectReason: { ...DisconnectReason },
        Browsers: { ubuntu: navegador => ["Ubuntu", navegador, "22.04.4"] },
        fetchLatestBaileysVersion: async () => ({ version: [2, 3000, 0], isLatest: true }),
        makeCacheableSignalKeyStore: keys => keys
    };
    modulo.makeWASocket = modulo.default;
    return { modulo };
}

module.exports = { DisconnectReason, fachadaFalsa };
