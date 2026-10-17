// El apagado ordenado (apagado.js): qué se hace, en qué orden, y qué pasa cuando algo tarda o falla. Sin procesos reales ni WhatsApp:
// el servidor, la cola y el backend son dobles que anotan lo que les piden, y el reloj es de mentira.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { crearApagado } = require("../apagado");
const { ColaEnvios } = require("../cola");
const { LimiteDeEnvios } = require("../limites");
const { crearAlmacenJson } = require("../almacen");
const { derivarClaveDeHuellas } = require("../privacidad");
const { RelojFalso, drenar } = require("./helpers/reloj-falso");

function armar({ servidor, cola, wa } = {}) {
    const eventos = [];
    const reloj = new RelojFalso();
    const lineas = [];
    const log = { info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
    const dobles = {
        servidor: servidor ?? { close: () => eventos.push("servidor.close"), closeIdleConnections: () => eventos.push("servidor.closeIdleConnections") },
        cola: cola ?? { detener: () => eventos.push("cola.detener"), esperarEnCurso: async ms => { eventos.push(`cola.esperarEnCurso(${ms})`); return true; } },
        wa: wa ?? { cerrar: async () => { eventos.push("wa.cerrar"); }, datos: { cerrar: async () => { eventos.push("datos.cerrar"); } } }
    };
    const salidas = [];
    const apagar = crearApagado({ ...dobles, log, reloj, salir: codigo => { eventos.push(`salir(${codigo})`); salidas.push(codigo); } });
    return { apagar, eventos, reloj, lineas, salidas, texto: () => lineas.join("\n") };
}

const posicion = (eventos, evento) => eventos.indexOf(evento);

test("el orden: nada nuevo, esperar al envío en curso, cerrar WhatsApp y los datos, y recién entonces salir con 0", async () => {
    const a = armar();
    await a.apagar("SIGTERM");

    const { eventos } = a;
    assert.ok(posicion(eventos, "cola.detener") >= 0 && posicion(eventos, "servidor.close") >= 0);
    const espera = posicion(eventos, "cola.esperarEnCurso(5000)");
    assert.ok(espera > posicion(eventos, "cola.detener"), "primero se frena la cola");
    assert.ok(espera > posicion(eventos, "servidor.close"), "y se cierra el puerto");
    assert.ok(posicion(eventos, "wa.cerrar") > espera, "WhatsApp se cierra DESPUÉS de esperar al envío en curso");
    assert.ok(posicion(eventos, "datos.cerrar") > espera, "y los datos también: el último envío ya está contado");
    assert.equal(eventos.at(-1), "salir(0)");
    assert.deepEqual(a.salidas, [0]);
    assert.match(a.texto(), /INFO SIGTERM recibido, apagando\.\.\./);
    assert.match(a.texto(), /INFO Apagado completo\./);
    assert.doesNotMatch(a.texto(), /ERROR/);
});

test("también se sueltan las conexiones inactivas del servidor, si esta versión de Node puede", async () => {
    const a = armar();
    await a.apagar("SIGINT");
    assert.ok(a.eventos.includes("servidor.closeIdleConnections"));

    const sinEso = armar({ servidor: { close: () => {} } });             // una versión de Node que no tiene closeIdleConnections
    await sinEso.apagar("SIGTERM");
    assert.deepEqual(sinEso.salidas, [0]);
});

test("un segundo pedido de apagado mientras se apaga no repite nada", async () => {
    const a = armar();
    await Promise.all([a.apagar("SIGTERM"), a.apagar("SIGINT")]);
    assert.equal(a.eventos.filter(e => e === "wa.cerrar").length, 1);
    assert.equal(a.eventos.filter(e => e === "cola.detener").length, 1);
    assert.deepEqual(a.salidas, [0]);
    assert.match(a.texto(), /INFO SIGINT recibido de nuevo: ya se está apagando\./);
});

test("si el envío en curso no termina a tiempo, se avisa y se sigue con el apagado", async () => {
    const a = armar({ cola: { detener() {}, esperarEnCurso: async () => false } });
    await a.apagar("SIGTERM");
    assert.match(a.texto(), /ERROR Un envío no terminó en 5 s; se sigue con el apagado\./);
    assert.ok(a.eventos.includes("wa.cerrar") && a.eventos.includes("datos.cerrar"));
    assert.deepEqual(a.salidas, [0]);
});

test("si cerrar WhatsApp falla, se anota (con los teléfonos enmascarados) y los datos se guardan igual", async () => {
    const eventos = [];
    const a = armar({ wa: {
        cerrar: async () => { throw new Error("falló con 5491122334455"); },
        datos: { cerrar: async () => { eventos.push("datos.cerrar"); } }
    } });
    await a.apagar("SIGTERM");
    assert.match(a.texto(), /ERROR Error cerrando el cliente de WhatsApp: falló con \*\*\*4455/);
    assert.doesNotMatch(a.texto(), /5491122334455/);
    assert.deepEqual(eventos, ["datos.cerrar"]);
    assert.deepEqual(a.salidas, [0]);
});

test("si guardar los datos falla, WhatsApp se cierra igual", async () => {
    const eventos = [];
    const a = armar({ wa: {
        cerrar: async () => { eventos.push("wa.cerrar"); },
        datos: { cerrar: async () => { throw new Error("disco lleno"); } }
    } });
    await a.apagar("SIGTERM");
    assert.match(a.texto(), /ERROR Error cerrando los datos del servicio: disco lleno/);
    assert.deepEqual(eventos, ["wa.cerrar"]);
    assert.deepEqual(a.salidas, [0]);
});

test("si cerrar el servidor falla, se anota y el apagado sigue", async () => {
    const a = armar({ servidor: { close: () => { throw new Error("no se pudo cerrar el puerto 3002"); } } });
    await a.apagar("SIGTERM");
    assert.match(a.texto(), /ERROR Error cerrando el servidor HTTP: no se pudo cerrar el puerto 3002/);
    assert.ok(a.eventos.includes("wa.cerrar"));
    assert.deepEqual(a.salidas, [0]);
});

test("con whatsapp-web.js (sin datos) alcanza con cerrar el cliente", async () => {
    const eventos = [];
    const a = armar({ wa: { cerrar: async () => { eventos.push("wa.cerrar"); } } });
    await a.apagar("SIGTERM");
    assert.deepEqual(eventos, ["wa.cerrar"]);
    assert.deepEqual(a.salidas, [0]);
});

test("si WhatsApp no suelta el socket, los datos se guardan igual y a los 10 segundos se sale con lo que haya", async () => {
    const eventos = [];
    const a = armar({ wa: {
        cerrar: () => new Promise(() => {}),
        datos: { cerrar: async () => { eventos.push("datos.cerrar"); } }
    } });
    let terminado = false;
    const apagado = a.apagar("SIGTERM").then(() => { terminado = true; });

    await a.reloj.avanzar(9999);
    assert.equal(terminado, false);
    assert.deepEqual(eventos, ["datos.cerrar"], "los datos ya estaban guardados");
    assert.deepEqual(a.salidas, []);

    await a.reloj.avanzar(1);
    await apagado;
    assert.equal(terminado, true);
    assert.match(a.texto(), /ERROR El apagado ordenado no terminó en 10 s — se sale igual\./);
    assert.deepEqual(a.salidas, [0], "con código 0: fue un pedido de apagado, no una falla");
});

test("al terminar a tiempo no queda ningún temporizador del tope", async () => {
    const a = armar();
    await a.apagar("SIGTERM");
    assert.equal(a.reloj.pendientes(), 0);
});

// ---------- Con la cola y los contadores de verdad ----------

test("el último envío queda contado en el disco antes de salir: se espera al envío en curso y recién después se guardan los contadores", async () => {
    const raiz = fs.mkdtempSync(path.join(os.tmpdir(), "yosoy-apagado-"));
    try {
        const reloj = new RelojFalso();
        const lineas = [];
        const log = { info: m => lineas.push(m), error: m => lineas.push(m) };
        const limites = new LimiteDeEnvios({
            topeDiario: 10, topePorHora: 10, topePorDestinatario: 10, zonaHoraria: "America/Argentina/Buenos_Aires",
            clave: derivarClaveDeHuellas("secreto"), almacen: crearAlmacenJson(path.join(raiz, "envios.json"), { log }), reloj, log
        });
        let terminarEnvio;
        const enviados = [];
        const cola = new ColaEnvios(() => new Promise(resolver => { terminarEnvio = () => { enviados.push("salió"); resolver(); }; }), log, { reloj, limites, aleatorio: () => 0 });
        assert.equal(cola.encolar("1155550001", "uno").ok, true);
        assert.equal(cola.encolar("1155550002", "dos").ok, true);
        await drenar();                                                 // el primero está saliendo; el segundo espera

        const enDisco = [];
        const salidas = [];
        const apagar = crearApagado({
            servidor: { close() {} }, cola, log, reloj,
            wa: { cerrar: async () => {}, datos: { cerrar: () => limites.cerrar() } },
            salir: codigo => {
                salidas.push(codigo);
                enDisco.push(fs.existsSync(path.join(raiz, "envios.json")) ? JSON.parse(fs.readFileSync(path.join(raiz, "envios.json"), "utf8")) : null);
            }
        });
        const apagado = apagar("SIGTERM");
        await drenar();
        assert.deepEqual(salidas, [], "mientras el envío no termina, no se sale");

        terminarEnvio();
        await apagado;

        assert.deepEqual(salidas, [0]);
        assert.equal(enDisco[0]?.enviados, 1, "al salir, el envío ya estaba contado en el disco");
        assert.deepEqual(enviados, ["salió"], "y el segundo mensaje no se mandó: la cola estaba detenida");
        assert.equal(cola.tamano(), 1);
        assert.equal(reloj.pendientes(), 0);
    } finally {
        fs.rmSync(raiz, { recursive: true, force: true });
    }
});

// ---------- El cableado en index.js ----------

test("index.js usa este apagado para SIGTERM (systemd) y SIGINT (Ctrl+C)", () => {
    const fuente = fs.readFileSync(path.join(__dirname, "..", "index.js"), "utf8");
    assert.match(fuente, /require\("\.\/apagado"\)/);
    assert.match(fuente, /const apagar = crearApagado\(\{ servidor, cola, wa, log \}\);/);
    assert.match(fuente, /process\.on\("SIGTERM", \(\) => apagar\("SIGTERM"\)\);/);
    assert.match(fuente, /process\.on\("SIGINT", \(\) => apagar\("SIGINT"\)\);/);
    assert.doesNotMatch(fuente, /process\.exit\(0\)/, "el único lugar que sale con 0 es el apagado ordenado");
});
