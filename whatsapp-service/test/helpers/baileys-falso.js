// Doble de prueba de Baileys: un "módulo" con la misma forma que devuelve el import() del paquete y un socket falso que NO abre
// ninguna conexión. Los tests del servicio usan SOLO esto; el paquete real se importa únicamente en test/contrato-baileys.test.js
// (para mirar qué exporta, sin conectar).
//
// Es fiel a lo que hace la librería real en lo que le importa al servicio (leído en baileys@6.7.24, Socket/socket.js):
//   - makeWASocket emite "connecting" en el próximo tick (no antes de que quien lo creó registre sus handlers);
//   - end() emite UN "close" y después quita todos los listeners de "connection.update": un socket cerrado no se reutiliza;
//   - logout() termina en un cierre con el código 401 (el mismo que una desvinculación desde el teléfono);
//   - al vincular, llegan "creds.update" e "isNewLogin" y enseguida el servidor pide reiniciar (515).
"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { EventEmitter } = require("node:events");
const { crearFachada } = require("../../baileys/cargar");

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

// Un error como el que arma Baileys (un Boom): el código HTTP está en output.statusCode.
function errorBoom(codigo, mensaje = "Desconexión simulada") {
    const err = new Error(mensaje);
    err.isBoom = true;
    err.output = { statusCode: codigo, payload: { statusCode: codigo, message: mensaje } };
    return err;
}

class SocketFalso {
    constructor(config, { conservaListeners = false } = {}) {
        this.config = config;
        this.ev = new EventEmitter();
        // Simula un emisor que no deja quitar los listeners (ni end() ni el servicio lo logran): así se prueba que el servicio se
        // defiende solo de los eventos de un socket que ya descartó, sin apoyarse en que la librería los quite.
        if (conservaListeners) this.ev.removeAllListeners = () => this.ev;
        this.user = undefined;
        this.enviados = [];                // { jid, contenido }
        this.consultas = [];               // listas de números consultadas con onWhatsApp
        this.presencias = [];              // { tipo, jid }
        this.vecesQueSeCerro = 0;          // cuántas veces se llamó a end()
        this.cerroSesion = false;          // alguien llamó a logout()
        this.todosExisten = true;
        this.existentes = new Set();       // números (solo dígitos) que "tienen WhatsApp" cuando todosExisten es false
        this.errorAlEnviar = null;
        this.envioColgado = false;
        this.logoutColgado = false;
        this._cerrado = false;
    }

    // ── Lo que el servicio le pide al socket ──
    async sendMessage(jid, contenido) {
        if (this.errorAlEnviar) throw this.errorAlEnviar;
        if (this.envioColgado) return new Promise(() => {});
        this.enviados.push({ jid, contenido });
        return { key: { id: `FALSO-${this.enviados.length}`, remoteJid: jid, fromMe: true } };
    }

    async onWhatsApp(...numeros) {
        this.consultas.push(numeros);
        return numeros.filter(n => this.todosExisten || this.existentes.has(n)).map(n => ({ jid: `${n}@s.whatsapp.net`, exists: true }));
    }

    async presenceSubscribe(jid) { this.presencias.push({ tipo: "suscripcion", jid }); }
    async sendPresenceUpdate(tipo, jid) { this.presencias.push({ tipo, jid }); }

    async logout() {
        this.cerroSesion = true;
        if (this.logoutColgado) return new Promise(() => {});
        this.end(errorBoom(DisconnectReason.loggedOut, "Intentional Logout"));
    }

    end(error) {
        this.vecesQueSeCerro++;
        if (this._cerrado) return;
        this._cerrado = true;
        this.ev.emit("connection.update", { connection: "close", lastDisconnect: { error, date: new Date() } });
        this.ev.removeAllListeners("connection.update");
    }

    // ── Lo que hace "WhatsApp" (lo dispara el test) ──
    emitirQr(qr = "ref-1,ruido,identidad,adv") { this.ev.emit("connection.update", { qr }); }

    abrir(numero = "5491100000000") {
        this.user = { id: `${numero}:7@s.whatsapp.net`, name: "Clínica" };
        this.ev.emit("connection.update", { connection: "open" });
    }

    // El servidor cierra la conexión con un código (o la librería la da por perdida).
    cerrar(codigo, mensaje) { this.end(errorBoom(codigo, mensaje)); }

    // Vinculación exitosa: se actualizan las credenciales, llegan creds.update e isNewLogin y el servidor pide reiniciar (515).
    vincularConExito(numero = "5491100000000") {
        const creds = this.config.auth.creds;
        creds.registered = true;
        creds.me = { id: `${numero}:7@s.whatsapp.net`, name: "Clínica" };
        this.ev.emit("creds.update", { me: creds.me });
        this.ev.emit("connection.update", { isNewLogin: true, qr: undefined });
        this.cerrar(DisconnectReason.restartRequired, "Stream Errored (restart required)");
    }

    // Mensaje entrante con la forma de WAMessage que entrega Baileys.
    recibir({ de = "5491122334455@s.whatsapp.net", texto = "hola", id = `ENT-${Math.random().toString(36).slice(2)}`, tipo = "notify", clave = {}, mensaje } = {}) {
        const msg = {
            key: { remoteJid: de, fromMe: false, id, ...clave },
            message: mensaje ?? { conversation: texto },
            messageTimestamp: 1760000000
        };
        this.ev.emit("messages.upsert", { messages: [msg], type: tipo });
        return msg;
    }
}

// compuertaGuardado: una promesa que el test abre a mano; mientras tanto saveCreds() espera (un guardado lento, para probar el orden).
function fachadaFalsa({ errorAlCrear = null, versionBuscada = { version: [2, 3000, 1], isLatest: true }, conservaListeners = false, compuertaGuardado = null } = {}) {
    const sockets = [];

    function makeWASocket(config) {
        if (errorAlCrear) throw errorAlCrear;
        const socket = new SocketFalso(config, { conservaListeners });
        sockets.push(socket);
        // Como la librería real: "connecting" se emite en el próximo tick, ya con los handlers del servicio registrados.
        process.nextTick(() => { if (!socket._cerrado) socket.ev.emit("connection.update", { connection: "connecting" }); });
        return socket;
    }

    // Como useMultiFileAuthState: crea la carpeta, lee creds.json si existe y devuelve { state, saveCreds }.
    async function useMultiFileAuthState(dir) {
        await fs.promises.mkdir(dir, { recursive: true });
        const archivo = path.join(dir, "creds.json");
        let creds = { registered: false };
        try { creds = JSON.parse(await fs.promises.readFile(archivo, "utf8")); } catch { /* sin credenciales todavía */ }
        return {
            state: { creds, keys: { get: async () => ({}), set: async () => {} } },
            saveCreds: async () => {
                if (compuertaGuardado) await compuertaGuardado;
                await fs.promises.writeFile(archivo, JSON.stringify(creds));
            }
        };
    }

    const llamadas = { fetchLatestBaileysVersion: 0, makeCacheableSignalKeyStore: 0 };
    const modulo = {
        default: makeWASocket,
        makeWASocket,
        useMultiFileAuthState,
        DisconnectReason: { ...DisconnectReason },
        Browsers: { ubuntu: navegador => ["Ubuntu", navegador, "22.04.4"] },
        fetchLatestBaileysVersion: async () => { llamadas.fetchLatestBaileysVersion++; return versionBuscada; },
        makeCacheableSignalKeyStore: keys => { llamadas.makeCacheableSignalKeyStore++; return keys; }
    };

    return {
        modulo,
        fachada: crearFachada(modulo),
        sockets,
        llamadas,
        get ultimo() { return sockets[sockets.length - 1]; }
    };
}

module.exports = { DisconnectReason, SocketFalso, fachadaFalsa, errorBoom };
