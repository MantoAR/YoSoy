// El plan de espera entre reconexiones y la clasificación de los códigos con los que WhatsApp corta (baileys/reconexion.js).
// Lógica pura: sin reloj, sin red y sin la librería.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const { PlanDeReconexion, clasificarDesconexion, TIPOS, VERSION_RECHAZADA } = require("../baileys/reconexion");
const { DisconnectReason } = require("./helpers/baileys-falso");

const sinAzar = (extra = {}) => new PlanDeReconexion({ aleatorio: () => 0.5, ...extra });
const esperas = (plan, n) => Array.from({ length: n }, () => plan.siguiente());

// ---------- Backoff ----------

test("la espera se duplica en cada intento, de 2 s hasta un tope de 5 minutos", () => {
    assert.deepEqual(esperas(sinAzar(), 11), [2000, 4000, 8000, 16000, 32000, 64000, 128000, 256000, 300000, 300000, 300000]);
});

test("el azar mueve cada espera hasta ±20 %, y nunca pasa del tope", () => {
    assert.equal(new PlanDeReconexion({ aleatorio: () => 0 }).siguiente(), 1600);
    assert.equal(new PlanDeReconexion({ aleatorio: () => 1 }).siguiente(), 2400);
    const alTope = new PlanDeReconexion({ aleatorio: () => 1 });
    assert.equal(esperas(alTope, 12).at(-1), 300000, "con el máximo de azar, en el tope no se pasa");
});

test("con el azar de verdad, las esperas caen dentro del rango", () => {
    for (let i = 0; i < 500; i++) {
        const ms = new PlanDeReconexion().siguiente();
        assert.ok(ms >= 1600 && ms <= 2400, `${ms} fuera de 1600..2400`);
    }
});

test("los parámetros se pueden cambiar", () => {
    const plan = new PlanDeReconexion({ baseMs: 1000, factor: 3, topeMs: 10000, jitter: 0, aleatorio: () => 0.9 });
    assert.deepEqual(esperas(plan, 5), [1000, 3000, 9000, 10000, 10000]);
});

test("intentos cuenta los pedidos de espera, y reiniciar() vuelve todo a cero", () => {
    const plan = sinAzar();
    assert.equal(plan.intentos, 0);
    esperas(plan, 3);
    assert.equal(plan.intentos, 3);
    plan.reiniciar();
    assert.equal(plan.intentos, 0);
    assert.equal(plan.siguiente(), 2000);
});

// ---------- Cuándo vuelve a empezar ----------

test("una conexión que se sostuvo un minuto vuelve la espera a 2 s; una que duró menos, no", () => {
    const sostenida = sinAzar();
    esperas(sostenida, 3);
    sostenida.alAbrir(1_000_000);
    sostenida.alCerrar(1_000_000 + 60_000);
    assert.equal(sostenida.siguiente(), 2000);

    const casi = sinAzar();
    esperas(casi, 3);
    casi.alAbrir(1_000_000);
    casi.alCerrar(1_000_000 + 59_999);
    assert.equal(casi.siguiente(), 16000);
});

test("un corte sin que la conexión haya abierto no reinicia nada", () => {
    const plan = sinAzar();
    esperas(plan, 2);
    plan.alCerrar(5_000_000);
    assert.equal(plan.siguiente(), 8000);
});

test("una conexión se cuenta una sola vez: después de cerrar, el siguiente corte ya no la considera abierta", () => {
    const plan = sinAzar();
    plan.alAbrir(0);
    plan.alCerrar(120_000);                 // estable: reinicia
    esperas(plan, 2);
    plan.alCerrar(500_000);                 // sin abrir de nuevo: no reinicia
    assert.equal(plan.siguiente(), 8000);
});

// ---------- Reinicios que pide WhatsApp ----------

test("los pedidos de reinicio (515) se atienden enseguida hasta 3 por minuto; el cuarto se espacia", () => {
    const plan = sinAzar();
    assert.deepEqual([0, 1000, 2000].map(t => plan.reinicioPedido(t)), [0, 0, 0]);
    assert.equal(plan.reinicioPedido(3000), 2000);
});

test("pasado un minuto, los reinicios vuelven a atenderse enseguida", () => {
    const plan = sinAzar();
    [0, 1000, 2000].forEach(t => plan.reinicioPedido(t));
    assert.equal(plan.reinicioPedido(3000), 2000);
    assert.equal(plan.reinicioPedido(62_001), 0, "los tres primeros ya salieron de la ventana de un minuto");
});

test("una conexión estable borra la cuenta de reinicios", () => {
    const plan = sinAzar();
    [0, 1000, 2000].forEach(t => plan.reinicioPedido(t));
    plan.alAbrir(3000);
    plan.alCerrar(3000 + 60_000);
    assert.equal(plan.reinicioPedido(3000 + 60_001), 0);
});

// ---------- Cortes seguidos del mismo tipo ----------

test("registrarFalla cuenta los cortes seguidos del mismo tipo; otro tipo, o una conexión que abre, empiezan de nuevo", () => {
    const plan = sinAzar();
    assert.equal(plan.registrarFalla(TIPOS.SESION_INVALIDA), 1);
    assert.equal(plan.registrarFalla(TIPOS.SESION_INVALIDA), 2);
    assert.equal(plan.registrarFalla(TIPOS.TRANSITORIA), 1);
    assert.equal(plan.registrarFalla(TIPOS.SESION_INVALIDA), 1);
    assert.equal(plan.registrarFalla(TIPOS.SESION_INVALIDA), 2);
    plan.alAbrir(0);
    assert.equal(plan.registrarFalla(TIPOS.SESION_INVALIDA), 1);
    plan.reiniciar();
    assert.equal(plan.registrarFalla(TIPOS.SESION_INVALIDA), 1);
});

// ---------- Clasificación de los códigos ----------

test("cada código de desconexión se clasifica como corresponde", () => {
    const DR = DisconnectReason;
    const casos = [
        [DR.loggedOut, TIPOS.SESION_CERRADA],
        [DR.forbidden, TIPOS.CUENTA_RESTRINGIDA],
        [DR.connectionReplaced, TIPOS.SESION_REEMPLAZADA],
        [VERSION_RECHAZADA, TIPOS.VERSION_RECHAZADA],
        [DR.badSession, TIPOS.SESION_INVALIDA],
        [DR.restartRequired, TIPOS.REINICIO],
        [DR.connectionClosed, TIPOS.TRANSITORIA],
        [DR.connectionLost, TIPOS.TRANSITORIA],
        [DR.timedOut, TIPOS.TRANSITORIA],
        [DR.unavailableService, TIPOS.TRANSITORIA],
        [DR.multideviceMismatch, TIPOS.TRANSITORIA],
        [null, TIPOS.TRANSITORIA],
        [undefined, TIPOS.TRANSITORIA],
        [999, TIPOS.TRANSITORIA]
    ];
    for (const [codigo, esperado] of casos) assert.equal(clasificarDesconexion(codigo, DR), esperado, `código ${codigo}`);
});

test("los números son los de WhatsApp: 401, 403, 405, 440, 500 y 515", () => {
    assert.deepEqual(
        [DisconnectReason.loggedOut, DisconnectReason.forbidden, VERSION_RECHAZADA, DisconnectReason.connectionReplaced, DisconnectReason.badSession, DisconnectReason.restartRequired],
        [401, 403, 405, 440, 500, 515]);
});

test("sin código numérico es un corte común, aunque la librería no traiga alguno de los motivos (undefined nunca coincide con undefined)", () => {
    const DRIncompleto = { loggedOut: 401 };      // una versión que no trae forbidden, connectionReplaced, ...
    for (const sinCodigo of [null, undefined, "401", NaN, {}]) {
        assert.equal(clasificarDesconexion(sinCodigo, DRIncompleto), TIPOS.TRANSITORIA, String(sinCodigo));
    }
    assert.equal(clasificarDesconexion(401, DRIncompleto), TIPOS.SESION_CERRADA);
    assert.equal(clasificarDesconexion(403, DRIncompleto), TIPOS.TRANSITORIA, "lo que la librería no define no se reconoce");
});
