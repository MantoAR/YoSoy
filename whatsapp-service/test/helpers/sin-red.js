// Bloqueo de red para los tests de whatsapp-service.
//
// Regla del proyecto: ningún test abre una conexión real a WhatsApp, ni a nada de internet, ni envía mensajes. Para que no
// dependa de la buena voluntad de cada test, este archivo se carga en CADA proceso de test (npm test corre
// `node --require ./test/helpers/sin-red.js --test ...`, y cada archivo de test lo vuelve a pedir arriba de todo) y hace que
// cualquier intento de salir a una dirección que no sea loopback falle en el acto, con el código ERR_RED_BLOQUEADA, sin
// llegar a mandar un solo paquete (tampoco consultas DNS).
//
// Lo que sí funciona: conectarse a 127.0.0.1 / localhost / ::1 (los tests de HTTP levantan un servidor propio ahí) y los
// sockets de dominio Unix.
//
// No hay variable de entorno para apagarlo, a propósito.
"use strict";

const net = require("node:net");
const dns = require("node:dns");

if (!globalThis.__sinRed) {
    const intentos = [];

    function esLoopback(host) {
        if (host === undefined || host === null || host === "") return true; // net usa "localhost" si no se indica host
        const h = String(host).toLowerCase().replace(/^\[|\]$/g, "");
        return h === "localhost" || h === "::1" || h === "0:0:0:0:0:0:0:1" || h === "::ffff:127.0.0.1" || /^127(\.\d{1,3}){3}$/.test(h);
    }

    function errorDeRed(destino) {
        const err = new Error(`Red bloqueada en los tests: no se permite conectar a ${destino}.`);
        err.code = "ERR_RED_BLOQUEADA";
        return err;
    }

    // Misma normalización de argumentos que hace net.Socket.prototype.connect: (options[, cb]), (path[, cb]), (port[, host][, cb])
    // o la forma interna [options, cb] que usan net.connect/net.createConnection/tls.connect.
    function opcionesDe(args) {
        const a0 = args[0];
        if (Array.isArray(a0)) return a0[0] || {};
        if (a0 !== null && typeof a0 === "object") return a0;
        if (typeof a0 === "string" && Number.isNaN(Number(a0))) return { path: a0 };
        return { port: a0, host: typeof args[1] === "string" ? args[1] : undefined };
    }

    const conectarOriginal = net.Socket.prototype.connect;
    net.Socket.prototype.connect = function (...args) {
        const opciones = opcionesDe(args);
        if (!opciones.path && !esLoopback(opciones.host)) {
            intentos.push({ host: String(opciones.host), puerto: opciones.port });
            const err = errorDeRed(`${opciones.host}:${opciones.port}`);
            process.nextTick(() => this.destroy(err));
            return this;
        }
        return conectarOriginal.apply(this, args);
    };

    function bloquearDns(objeto, nombre) {
        const original = objeto[nombre];
        if (typeof original !== "function") return;
        objeto[nombre] = function (hostname, ...resto) {
            if (esLoopback(hostname)) return original.call(this, hostname, ...resto);
            intentos.push({ host: String(hostname), dns: true });
            const err = errorDeRed(String(hostname));
            const cb = resto.find(x => typeof x === "function");
            if (cb) { process.nextTick(cb, err); return {}; }
            throw err;
        };
    }
    for (const n of ["lookup", "resolve", "resolve4", "resolve6"]) bloquearDns(dns, n);

    // Las versiones con promesas (dns.promises / dns/promises) devuelven una promesa rechazada en lugar de usar callback.
    for (const n of ["lookup", "resolve", "resolve4", "resolve6"]) {
        const original = dns.promises[n];
        if (typeof original !== "function") continue;
        dns.promises[n] = function (hostname, ...resto) {
            if (esLoopback(hostname)) return original.call(this, hostname, ...resto);
            intentos.push({ host: String(hostname), dns: true });
            return Promise.reject(errorDeRed(String(hostname)));
        };
    }

    globalThis.__sinRed = { intentos, esLoopback };
}

module.exports = globalThis.__sinRed;
