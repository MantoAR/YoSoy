// El comando de vinculación por QR (vincular.js): se prueba con el socket de Baileys simulado y un reloj falso. Ninguna prueba
// abre una conexión real ni genera un QR de verdad: los "QR" son textos inventados que el doble emite a mano.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const net = require("node:net");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { ejecutarVinculacion, puertoOcupado, dibujarQr } = require("../vincular");
const { fachadaFalsa, DisconnectReason } = require("./helpers/baileys-falso");
const { RelojFalso, esperarHasta } = require("./helpers/reloj-falso");
const { conRutaDentroDeLaAplicacion } = require("./helpers/dentro-de-la-aplicacion");

const QR1 = "ref-secreta-1,ruido,identidad,adv";
const QR2 = "ref-secreta-2,ruido,identidad,adv";
const VENCE_LA_VENTANA = "QR refs attempts ended";
const NUMERO = "5491177776666";

// Un "pasos" es una lista de funciones: la i-ésima corre en la i-ésima vuelta de espera del comando, así cada prueba maneja
// el socket falso en el orden exacto que quiere. Después de cada paso, el reloj falso avanza lo que pidió el comando.
function armar({ registrada = false, env = {}, pasos = [], opcionesFachada, extra = {} } = {}) {
    const raiz = fs.mkdtempSync(path.join(os.tmpdir(), "yosoy-vincular-"));
    const dir = path.join(raiz, "sesion");
    if (registrada) {
        fs.mkdirSync(dir, { recursive: true });
        fs.writeFileSync(path.join(dir, "creds.json"), JSON.stringify({ registered: false, me: { id: `${NUMERO}:7@s.whatsapp.net` } }));
    }
    const falso = fachadaFalsa(opcionesFachada);
    const reloj = new RelojFalso();
    const salida = [];
    const errores = [];
    const ctx = { falso, reloj, dir, raiz };
    const esperar = async ms => {
        const paso = pasos.shift();
        if (paso) await paso(ctx);
        await reloj.avanzar(ms);
    };
    const llamadas = { hayServicio: [], cargarBaileys: 0 };
    return {
        ...ctx,
        llamadas,
        salida: () => salida.join(""),
        errores: () => errores.join(""),
        ejecutar: (sobre = {}) => ejecutarVinculacion({
            env: { WHATSAPP_BAILEYS_SESSION_PATH: dir, ...env },
            stdout: { write: s => salida.push(s) },
            stderr: { write: s => errores.push(s) },
            esTTY: true,
            uid: 1000,
            hayServicio: async puerto => { llamadas.hayServicio.push(puerto); return false; },
            cargarBaileys: async () => { llamadas.cargarBaileys++; return falso.fachada; },
            dibujar: qr => `[QR ${qr.split(",")[0]}]`,
            reloj,
            esperar,
            consultaMs: 500,
            ...extra,
            ...sobre
        }),
        limpiar: () => fs.rmSync(raiz, { recursive: true, force: true })
    };
}

function conCli(nombre, opciones, cuerpo) {
    test(nombre, async () => {
        const c = armar(opciones);
        try { await cuerpo(c); }
        finally { c.limpiar(); }
    });
}

const esperarSocket = (c, n) => esperarHasta(() => c.falso.sockets.length >= n, { descripcion: `el socket ${n}` });
const cuantas = (texto, pieza) => texto.split(pieza).length - 1;

// ---------- Se niega cuando no corresponde ----------

conCli("si la salida no es una terminal, se niega: el QR no puede terminar en un archivo ni en el journal", {}, async c => {
    assert.equal(await c.ejecutar({ esTTY: false }), 1);
    assert.match(c.errores(), /correrlo en una terminal/);
    assert.equal(c.salida(), "");
    assert.equal(c.llamadas.cargarBaileys, 0);
    assert.equal(c.falso.sockets.length, 0);
});

conCli("como root se niega: la sesión quedaría de root y el servicio no la podría leer", {}, async c => {
    assert.equal(await c.ejecutar({ uid: 0 }), 1);
    assert.match(c.errores(), /No correrlo como root/);
    assert.equal(c.falso.sockets.length, 0);
    assert.equal(fs.existsSync(c.dir), false, "no creó la carpeta");
});

conCli("si el servicio está corriendo, se niega y dice cómo pararlo (dos conexiones con la misma cuenta se pisan)", { env: { WHATSAPP_SERVICE_PORT: "4567" } }, async c => {
    assert.equal(await c.ejecutar({ hayServicio: async puerto => { c.llamadas.hayServicio.push(puerto); return true; } }), 1);
    assert.deepEqual(c.llamadas.hayServicio, [4567], "mira el puerto configurado");
    assert.match(c.errores(), /sudo systemctl stop whatsapp-service/);
    assert.equal(c.falso.sockets.length, 0);
    assert.equal(fs.existsSync(c.dir), false);
});

conCli("sin WHATSAPP_BAILEYS_SESSION_PATH no empieza y nombra la variable", { env: { WHATSAPP_BAILEYS_SESSION_PATH: undefined } }, async c => {
    assert.equal(await c.ejecutar(), 1);
    assert.match(c.errores(), /Falta WHATSAPP_BAILEYS_SESSION_PATH/);
    assert.equal(c.falso.sockets.length, 0);
});

conCli("una carpeta de sesión dentro del repositorio la rechaza igual que el servicio, sin crear nada ni cargar la librería", {}, c =>
    conRutaDentroDeLaAplicacion("sesion-vincular-que-no-tiene-que-existir", async adentro => {
        assert.equal(await c.ejecutar({ env: { WHATSAPP_BAILEYS_SESSION_PATH: adentro } }), 1);
        assert.match(c.errores(), /FUERA/);
        assert.equal(fs.existsSync(adentro), false);
        assert.equal(c.llamadas.cargarBaileys, 0);
    }));

// ---------- Vinculación ----------

conCli("vincula con éxito: dibuja cada QR una sola vez, guarda la sesión, termina con 0 y no filtra nada", {
    pasos: [
        async c => { await esperarSocket(c, 1); c.falso.sockets[0].emitirQr(QR1); },
        async c => { c.falso.sockets[0].emitirQr(QR1); },                      // el mismo código otra vez: no se vuelve a dibujar
        async c => { c.falso.sockets[0].emitirQr(QR2); },                      // WhatsApp lo renovó: reemplaza al anterior
        async c => { c.falso.sockets[0].vincularConExito(NUMERO); },
        async c => { await esperarSocket(c, 2); c.falso.sockets[1].abrir(NUMERO); }
    ]
}, async c => {
    assert.equal(await c.ejecutar(), 0);

    const pantalla = c.salida();
    assert.equal(cuantas(pantalla, "[QR ref-secreta-1]"), 1);
    assert.equal(cuantas(pantalla, "[QR ref-secreta-2]"), 1);
    assert.equal(cuantas(pantalla, "\x1b[H\x1b[J"), 2, "cada código nuevo reemplaza al anterior en la pantalla");
    assert.match(pantalla, /Dispositivos vinculados/);

    const errores = c.errores();
    assert.match(errores, /Listo: el número \*\*\*6666 quedó vinculado/);
    assert.match(errores, /sudo systemctl start whatsapp-service/);
    for (const prohibido of ["ref-secreta", "ruido", "identidad", "adv", NUMERO]) {
        assert.equal(errores.includes(prohibido), false, `la salida de errores no puede incluir "${prohibido}"`);
    }

    const guardada = JSON.parse(fs.readFileSync(path.join(c.dir, "creds.json"), "utf8"));
    assert.equal(guardada.me.id, `${NUMERO}:7@s.whatsapp.net`);
    assert.deepEqual(fs.readdirSync(c.dir).filter(n => n !== "creds.json" && n !== "creds.json.bak"), [], "no quedó ningún otro archivo (ni el QR)");
    assert.ok(c.falso.ultimo.vecesQueSeCerro >= 1, "al terminar, el socket queda cerrado");
});

conCli("con una sesión que ya estaba vinculada, no pide escanear nada y lo dice", {
    registrada: true,
    pasos: [async c => { await esperarSocket(c, 1); c.falso.sockets[0].abrir(NUMERO); }]
}, async c => {
    assert.equal(await c.ejecutar(), 0);
    assert.match(c.errores(), /ya estaba vinculada \(número \*\*\*6666\)/);
    assert.equal(c.salida(), "", "no dibujó ningún QR");
    assert.doesNotMatch(c.errores(), /ventana de vinculación/);
});

conCli("no reenvía a la API los mensajes que lleguen mientras corre", {
    registrada: true,
    pasos: [async c => { await esperarSocket(c, 1); c.falso.sockets[0].recibir({ texto: "hola, quiero un turno" }); c.falso.sockets[0].abrir(NUMERO); }]
}, async c => {
    assert.equal(await c.ejecutar(), 0);
    assert.doesNotMatch(c.errores() + c.salida(), /quiero un turno/);
});

// ---------- El QR vence ----------

conCli("si la ventana de QR vence sin escanearse, termina con 2 y dice cómo pedir otra", {
    env: { WHATSAPP_BAILEYS_VENTANAS_QR: "1" },
    pasos: [
        async c => { await esperarSocket(c, 1); c.falso.sockets[0].emitirQr(QR1); },
        async c => { c.falso.sockets[0].cerrar(DisconnectReason.timedOut, VENCE_LA_VENTANA); }
    ]
}, async c => {
    assert.equal(await c.ejecutar(), 2);
    assert.match(c.errores(), /El QR venció sin escanearse/);
    assert.equal(cuantas(c.salida(), "[QR ref-secreta-1]"), 1);
    assert.equal(c.falso.sockets.length, 1);
});

conCli("con el cupo por defecto abre una segunda ventana antes de rendirse, y no más", {
    pasos: [
        async c => { await esperarSocket(c, 1); c.falso.sockets[0].emitirQr(QR1); },
        async c => { c.falso.sockets[0].cerrar(DisconnectReason.timedOut, VENCE_LA_VENTANA); },
        async c => { await c.reloj.avanzar(2000); await esperarSocket(c, 2); c.falso.sockets[1].emitirQr(QR2); },
        async c => { c.falso.sockets[1].cerrar(DisconnectReason.timedOut, VENCE_LA_VENTANA); }
    ]
}, async c => {
    assert.equal(await c.ejecutar(), 2);
    assert.equal(c.falso.sockets.length, 2);
    assert.equal(cuantas(c.salida(), "[QR ref-secreta-1]"), 1);
    assert.equal(cuantas(c.salida(), "[QR ref-secreta-2]"), 1);
});

conCli("si pasa demasiado tiempo sin que pase nada, se rinde con 2", { env: { WHATSAPP_BAILEYS_VENTANAS_QR: "1" } }, async c => {
    assert.equal(await c.ejecutar({ consultaMs: 60000 }), 2);
    assert.match(c.errores(), /Se agotó el tiempo/);
});

// ---------- Otros finales ----------

conCli("si WhatsApp cierra la sesión mientras se vincula, termina con 1", {
    pasos: [
        async c => { await esperarSocket(c, 1); c.falso.sockets[0].emitirQr(QR1); },
        async c => { c.falso.sockets[0].cerrar(DisconnectReason.loggedOut); }
    ]
}, async c => {
    assert.equal(await c.ejecutar(), 1);
    assert.match(c.errores(), /No se pudo vincular \(sesion_cerrada\)/);
});

const interrupcion = new AbortController();
conCli("Ctrl+C: termina con 130 y cierra el socket", {
    pasos: [
        async c => { await esperarSocket(c, 1); c.falso.sockets[0].emitirQr(QR1); },
        async () => { interrupcion.abort(); }
    ]
}, async c => {
    assert.equal(await c.ejecutar({ senal: interrupcion.signal }), 130);
    assert.match(c.errores(), /Interrumpido/);
    assert.ok(c.falso.sockets[0].vecesQueSeCerro >= 1);
});

// ---------- Piezas sueltas ----------

test("puertoOcupado: ve un servidor escuchando en loopback y no ve uno que no está", async () => {
    const servidor = net.createServer();
    await new Promise(resolver => servidor.listen(0, "127.0.0.1", resolver));
    const { port } = servidor.address();
    assert.equal(await puertoOcupado(port), true);
    await new Promise(resolver => servidor.close(resolver));
    assert.equal(await puertoOcupado(port), false);
});

test("dibujarQr arma un dibujo de varias líneas con bloques, sin imprimir nada por sí mismo", () => {
    const escrito = [];
    const original = console.log;
    console.log = (...a) => escrito.push(a.join(" "));
    let dibujo;
    try { dibujo = dibujarQr(QR1); } finally { console.log = original; }
    assert.ok(dibujo.split("\n").length > 10);
    assert.match(dibujo, /[▀▄█]/);
    assert.deepEqual(escrito, []);
});

test("el comando como proceso: sin una terminal se niega con código 1, sin tocar nada", () => {
    const env = { ...process.env };
    for (const k of Object.keys(env)) if (/^(WHATSAPP_|API_BASE_URL$|Mensajeria__)/.test(k)) delete env[k];
    Object.assign(env, { NODE_OPTIONS: `--require=${path.join(__dirname, "helpers", "sin-red.js")}` });
    const r = spawnSync(process.execPath, [path.join(__dirname, "..", "vincular.js")], { env, encoding: "utf8", timeout: 20000 });
    assert.equal(r.status, 1);
    assert.match(r.stderr, /correrlo en una terminal/);
    assert.equal(r.stdout, "");
});
