// Ayudas compartidas por los tests: un servidor en un puerto libre con los logs capturados, clientes de
// socket.io-client (4.8.3, la misma versión que el cliente vendorizado en PANTALLAS/shared/vendor) y esperas.
'use strict';

const test = require('node:test');
const crypto = require('crypto');
const jwt = require('jsonwebtoken');
const { io } = require('socket.io-client');
const { crearServidor } = require('../server');
const { emitirToken } = require('../token');

const SECRETO = 'secreto-de-pruebas-que-no-es-de-nadie-0123456789';   // 48 caracteres

// Límites altísimos por defecto: cada test que prueba un límite baja el que le toca.
const CONFIG_BASE = Object.freeze({
    secreto: SECRETO,
    host: '127.0.0.1',
    puerto: 0,
    origenesCors: [],
    maxPayloadBytes: 64 * 1024,
    maxHandshakesPorMin: 100000,
    maxEventosPor10s: 100000
});

const ahora = () => Math.floor(Date.now() / 1000);

/** Token válido (por 5 minutos salvo que se pida otra cosa). */
function tk(sala, rol, sub, extra = {}) {
    return emitirToken({ sala, rol, sub, expiraEnSeg: 300, ...extra }, SECRETO);
}

/** Token armado a mano, para los casos que el emisor no produce (sin exp, vencido, otro algoritmo…). */
function firmar(payload, opciones = {}, secreto = SECRETO) {
    return jwt.sign(payload, secreto, { algorithm: 'HS256', noTimestamp: true, ...opciones });
}

const b64 = objeto => Buffer.from(JSON.stringify(objeto)).toString('base64url');

/**
 * Token HS256 con firma VÁLIDA sobre cualquier payload, incluso los que jwt.sign se niega a firmar (exp que no
 * es número, payload que no es un objeto…). Sirve para probar que el servidor no confía en la forma del payload.
 */
function firmarCrudo(payload, secreto = SECRETO) {
    const cuerpo = `${b64({ alg: 'HS256', typ: 'JWT' })}.${b64(payload)}`;
    return `${cuerpo}.${crypto.createHmac('sha256', secreto).update(cuerpo).digest('base64url')}`;
}

/** Token sin firma (alg:none), el clásico ataque contra librerías que lo aceptan. */
function sinFirma(payload) {
    return `${b64({ alg: 'none', typ: 'JWT' })}.${b64(payload)}.`;
}

function pausa(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

/** Espera (sondeando) a que se cumpla una condición. Devuelve si se cumplió. */
async function hasta(condicion, ms = 3000) {
    const fin = Date.now() + ms;
    while (Date.now() < fin) {
        if (condicion()) return true;
        await pausa(25);
    }
    return condicion();
}

/** Espera un evento del socket (rechaza si no llega a tiempo). */
function esperar(socket, evento, ms = 3000) {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error(`timeout esperando "${evento}"`)), ms);
        socket.once(evento, primero => {   // "disconnect" trae (motivo, detalle): solo interesa el primero
            clearTimeout(timer);
            resolve(primero);
        });
    });
}

/** true si durante `ms` NO llegó el evento. */
async function noLlega(socket, evento, ms = 400) {
    let llego = false;
    const marcar = () => { llego = true; };
    socket.on(evento, marcar);
    await pausa(ms);
    socket.off(evento, marcar);
    return !llego;
}

/** Pide un join y devuelve 'joined' o el código con el que lo rechazaron. */
function unir(socket, ms = 3000) {
    const resultado = new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('timeout esperando joined / rechazado')), ms);
        socket.once('joined', () => { clearTimeout(timer); resolve('joined'); });
        socket.once('rechazado', r => { clearTimeout(timer); resolve(r.codigo); });
    });
    socket.emit('join-room');
    return resultado;
}

/**
 * Levanta un servidor propio del test (en un puerto libre, con los logs capturados) y lo baja al terminar,
 * junto con todos los clientes que se hayan creado por esta vía.
 */
async function entorno(t, extraConfig = {}) {
    const logs = [];
    const servidor = crearServidor({ ...CONFIG_BASE, ...extraConfig }, { log: (evento, campos) => logs.push({ evento, ...campos }) });
    await servidor.iniciar();
    const url = `http://127.0.0.1:${servidor.puerto}`;
    const sockets = [];

    t.after(async () => {
        for (const s of sockets) s.close();
        await servidor.detener();
    });

    const api = {
        servidor, logs, url,
        conectar(token, opciones = {}) {
            const socket = io(url, {
                auth: token === undefined ? {} : { token },
                transports: ['websocket'],
                reconnection: false,
                forceNew: true,
                ...opciones
            });
            sockets.push(socket);
            return socket;
        },
        async conectado(token, opciones) {
            const socket = api.conectar(token, opciones);
            await esperar(socket, 'connect');
            return socket;
        },
        async unido(token, opciones) {
            const socket = await api.conectado(token, opciones);
            const resultado = await unir(socket);
            if (resultado !== 'joined') throw new Error(`no pudo unirse: ${resultado}`);
            return socket;
        }
    };
    return api;
}

/** test() con tope de tiempo por prueba, para que una espera colgada no cuelgue toda la corrida. */
function prueba(nombre, fn, opciones = {}) {
    return test(nombre, { timeout: 30000, ...opciones }, fn);
}

module.exports = { SECRETO, CONFIG_BASE, ahora, tk, firmar, firmarCrudo, sinFirma, pausa, hasta, esperar, noLlega, unir, entorno, prueba };
