// El bloqueo de red de los tests (test/helpers/sin-red.js) funciona. Usa direcciones de documentación (RFC 5737: 203.0.113.0/24)
// y nombres reservados (.invalid, RFC 2606), que no existen en internet: aunque el bloqueo fallara, estas pruebas no podrían
// tocar un servidor real.
const sinRed = require("./helpers/sin-red");
const { test } = require("node:test");
const assert = require("node:assert/strict");
const net = require("node:net");
const tls = require("node:tls");
const dns = require("node:dns");

const TEST_NET = "203.0.113.7";

function conectar(modulo, opciones) {
    return new Promise(resolve => {
        const socket = modulo.connect(opciones);
        socket.once("error", err => resolve(err));
        socket.once("connect", () => { socket.destroy(); resolve(null); });
    });
}

test("el bloqueo está activo en este proceso", () => {
    assert.ok(globalThis.__sinRed, "falta sin-red.js: los tests se corren con `npm test` (usa --require ./test/helpers/sin-red.js)");
    assert.strictEqual(sinRed, globalThis.__sinRed);
});

test("net.connect a una dirección que no es loopback falla con ERR_RED_BLOQUEADA y no manda nada", async () => {
    const antes = sinRed.intentos.length;
    const err = await conectar(net, { host: TEST_NET, port: 443 });
    assert.ok(err, "tendría que haber fallado");
    assert.strictEqual(err.code, "ERR_RED_BLOQUEADA");
    assert.strictEqual(sinRed.intentos.length, antes + 1);
    assert.deepEqual(sinRed.intentos.at(-1), { host: TEST_NET, puerto: 443 });
});

test("tls.connect (la vía de wss:// y https://) también queda bloqueado", async () => {
    const err = await conectar(tls, { host: TEST_NET, port: 443, servername: "web.whatsapp.com.invalid" });
    assert.ok(err);
    assert.strictEqual(err.code, "ERR_RED_BLOQUEADA");
});

test("fetch a un nombre de internet se rechaza sin resolver el nombre", async () => {
    const antes = sinRed.intentos.length;
    await assert.rejects(() => fetch("https://web.whatsapp.invalid/"), err => {
        const causa = err.cause || err;
        return causa.code === "ERR_RED_BLOQUEADA";
    });
    assert.ok(sinRed.intentos.length > antes);
});

test("dns.lookup y dns.promises.lookup de un nombre externo se bloquean (no sale ni una consulta DNS)", async () => {
    const err = await new Promise(resolve => dns.lookup("web.whatsapp.invalid", e => resolve(e)));
    assert.strictEqual(err.code, "ERR_RED_BLOQUEADA");
    await assert.rejects(() => dns.promises.lookup("web.whatsapp.invalid"), { code: "ERR_RED_BLOQUEADA" });
});

test("loopback sigue andando: un servidor local y un cliente en 127.0.0.1 se hablan", async () => {
    const servidor = net.createServer(socket => socket.end("hola"));
    await new Promise(resolve => servidor.listen(0, "127.0.0.1", resolve));
    try {
        const { port } = servidor.address();
        const recibido = await new Promise((resolve, reject) => {
            const socket = net.connect({ host: "127.0.0.1", port });
            let datos = "";
            socket.on("data", d => { datos += d; });
            socket.on("end", () => resolve(datos));
            socket.on("error", reject);
        });
        assert.strictEqual(recibido, "hola");
    } finally {
        servidor.close();
    }
});

test("esLoopback reconoce las formas habituales y no se deja engañar por nombres parecidos", () => {
    for (const ok of ["127.0.0.1", "127.1.2.3", "localhost", "LOCALHOST", "::1", "[::1]", undefined, ""]) {
        assert.ok(sinRed.esLoopback(ok), `${ok} es loopback`);
    }
    for (const mal of ["203.0.113.7", "0.0.0.0", "10.0.0.5", "localhost.evil.invalid", "127.0.0.1.evil.invalid", "web.whatsapp.com"]) {
        assert.ok(!sinRed.esLoopback(mal), `${mal} NO es loopback`);
    }
});
