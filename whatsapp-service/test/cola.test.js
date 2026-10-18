// La cola de envíos (cola.js) con sus protecciones: ritmo, pausas largas, consentimiento, topes y espera por la conexión.
// Reloj de mentira y azar fijo: cada espera se prueba exacta. (Las pruebas de la cola de siempre, con tiempos reales, están en
// privacidad.test.js.)
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const { ColaEnvios, CAPACIDAD_MAXIMA } = require("../cola");
const { RegistroDeConsentimiento } = require("../consentimiento");
const { LimiteDeEnvios } = require("../limites");
const { derivarClaveDeHuellas } = require("../privacidad");
const { RelojFalso, drenar } = require("./helpers/reloj-falso");

const CLAVE = derivarClaveDeHuellas("secreto-de-prueba");
const almacenEnMemoria = () => ({ leer: async () => null, guardar() {}, cerrar: async () => {} });

function armar(opciones = {}, { falla = null } = {}) {
    const reloj = new RelojFalso();
    const enviados = [];
    const lineas = [];
    const log = { info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
    const estado = { falla };
    const cola = new ColaEnvios(async (telefono, texto, meta) => {
        if (estado.falla) throw estado.falla;
        enviados.push({ telefono, texto, meta, en: reloj.ahora() });
    }, log, { reloj, aleatorio: () => 0, ...opciones });
    return { cola, reloj, enviados, lineas, estado, log, texto: () => lineas.join("\n") };
}

const tel = n => `11555500${String(n).padStart(2, "0")}`;
const encolarN = (c, n, desde = 1) => { for (let i = 0; i < n; i++) assert.equal(c.cola.encolar(tel(desde + i), `mensaje ${desde + i}`).ok, true); };

// ---------- Ritmo ----------

test("sin opciones es la cola de siempre: el primero sale enseguida y los demás cada 3 a 5 segundos", async () => {
    const c = armar();
    encolarN(c, 3);
    await drenar();
    assert.equal(c.enviados.length, 1);
    await c.reloj.avanzar(2999);
    assert.equal(c.enviados.length, 1);
    await c.reloj.avanzar(1);
    assert.equal(c.enviados.length, 2, "con el azar en 0, a los 3000 ms exactos");
    await c.reloj.avanzar(3000);
    assert.equal(c.enviados.length, 3);
});

test("la pausa de siempre llega hasta 4999 ms con el azar al máximo", async () => {
    const c = armar({ aleatorio: () => 0.9999999 });
    encolarN(c, 2);
    await drenar();
    await c.reloj.avanzar(4998);
    assert.equal(c.enviados.length, 1);
    await c.reloj.avanzar(1);
    assert.equal(c.enviados.length, 2);
});

test("el espaciado se configura: entre 4 y 9 segundos al azar", async () => {
    const minimo = armar({ espaciado: { minMs: 4000, maxMs: 9000 } });
    encolarN(minimo, 2);
    await drenar();
    await minimo.reloj.avanzar(3999);
    assert.equal(minimo.enviados.length, 1);
    await minimo.reloj.avanzar(1);
    assert.equal(minimo.enviados.length, 2);

    const maximo = armar({ espaciado: { minMs: 4000, maxMs: 9000 }, aleatorio: () => 0.9999999 });
    encolarN(maximo, 2);
    await drenar();
    await maximo.reloj.avanzar(8998);
    assert.equal(maximo.enviados.length, 1);
    await maximo.reloj.avanzar(1);
    assert.equal(maximo.enviados.length, 2);
});

test("cada tanto hay una pausa larga, y después se vuelve al ritmo normal", async () => {
    const c = armar({ espaciado: { minMs: 4000, maxMs: 9000 }, pausaLarga: { cada: [2, 2], ms: [30000, 30000] } });
    encolarN(c, 6);
    await drenar();                                         // 1.º
    await c.reloj.avanzar(4000);                            // 2.º (normal)
    assert.equal(c.enviados.length, 2);
    await c.reloj.avanzar(29999);                           // después del 2.º, pausa larga
    assert.equal(c.enviados.length, 2);
    await c.reloj.avanzar(1);                               // 3.º
    assert.equal(c.enviados.length, 3);
    await c.reloj.avanzar(4000);                            // 4.º (normal)
    assert.equal(c.enviados.length, 4);
    await c.reloj.avanzar(30000);                           // otra larga después del 4.º → 5.º
    assert.equal(c.enviados.length, 5);
});

test("los mensajes se mandan en el orden en que llegaron y llevan su meta", async () => {
    const c = armar();
    c.cola.encolar("1155550001", "uno", { optIn: true });
    c.cola.encolar("1155550002", "dos", { optIn: false });
    await drenar();
    await c.reloj.avanzar(3000);
    assert.deepEqual(c.enviados.map(e => [e.telefono, e.texto, e.meta]), [["1155550001", "uno", { optIn: true }], ["1155550002", "dos", { optIn: false }]]);
});

// ---------- Consentimiento ----------

function conConsentimiento(opciones = {}) {
    const reloj = new RelojFalso();
    const lineas = [];
    const log = { info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
    const consentimiento = new RegistroDeConsentimiento({ almacen: almacenEnMemoria(), clave: CLAVE, reloj, log });
    const c = armar({ consentimiento, reloj, ...opciones });
    return { ...c, consentimiento, reloj };
}

test("sin consentimiento no se encola nada: 403 sin_consentimiento, y queda anotado sin el teléfono completo", async () => {
    const c = conConsentimiento();
    const r = c.cola.encolar("1155550001", "Tenés turno mañana");
    assert.equal(r.ok, false);
    assert.equal(r.estado, 403);
    assert.equal(r.codigo, "sin_consentimiento");
    assert.equal(c.cola.tamano(), 0);
    await drenar();
    assert.equal(c.enviados.length, 0);
    assert.match(c.texto(), /INFO Envío rechazado \(sin_consentimiento\) a \*\*\*0001\./);
    assert.equal(c.texto().includes("1155550001"), false);
    assert.equal(c.texto().includes("turno"), false, "el texto del mensaje tampoco");
});

test("con optIn:true (exactamente true) de la API se encola; con otra cosa, no", async () => {
    const c = conConsentimiento();
    assert.equal(c.cola.encolar("1155550001", "hola", { optIn: true }).ok, true);
    for (const falso of [1, "true", undefined, false]) {
        assert.equal(c.cola.encolar("1155550002", "hola", { optIn: falso }).codigo, "sin_consentimiento", String(falso));
    }
});

test("si la persona le escribió primero al número, se le puede mandar sin optIn; si pidió la baja, no", async () => {
    const c = conConsentimiento();
    c.consentimiento.alMensajeEntrante("5491155550001", "hola");
    assert.equal(c.cola.encolar("1155550001", "respuesta").ok, true);
    c.consentimiento.alMensajeEntrante("5491155550001", "BAJA");
    const r = c.cola.encolar("1155550001", "otro", { optIn: true });
    assert.equal(r.codigo, "baja");
    assert.equal(r.estado, 403);
});

test("un teléfono imposible es un 400", async () => {
    const c = conConsentimiento();
    const r = c.cola.encolar("123", "hola", { optIn: true });
    assert.equal(r.estado, 400);
    assert.equal(r.codigo, "telefono_invalido");
});

test("a la política de consentimiento le llega optIn:true solo si la API mandó exactamente true", async () => {
    const recibidos = [];
    const consentimiento = { permite: (telefono, { optIn }) => { recibidos.push(optIn); return { ok: true }; } };
    const c = armar({ consentimiento });
    c.cola.encolar(tel(1), "a", { optIn: true });
    c.cola.encolar(tel(2), "b", { optIn: 1 });
    c.cola.encolar(tel(3), "c", { optIn: "true" });
    c.cola.encolar(tel(4), "d", {});
    c.cola.encolar(tel(5), "e");
    assert.deepEqual(recibidos, [true, false, false, false, false]);
});

// ---------- Topes ----------

function conLimites(opciones = {}, extraLimites = {}) {
    const reloj = new RelojFalso();
    const lineas = [];
    const log = { info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
    const limites = new LimiteDeEnvios({
        topeDiario: 3, topePorHora: 99, topePorDestinatario: 99, zonaHoraria: "America/Argentina/Buenos_Aires",
        clave: CLAVE, almacen: almacenEnMemoria(), reloj, log, ...extraLimites
    });
    const c = armar({ limites, reloj, ...opciones });
    return { ...c, limites, reloj };
}

test("el tope del día cuenta lo que espera en la cola: el cuarto se rechaza al instante con 429", async () => {
    const c = conLimites();
    encolarN(c, 3);
    const r = c.cola.encolar(tel(4), "mensaje 4");
    assert.equal(r.ok, false);
    assert.equal(r.estado, 429);
    assert.equal(r.codigo, "tope_diario");
    await drenar();
    assert.equal(c.cola.tamano(), 2, "los 3 aceptados siguen su camino; el rechazado no entró");
});

test("un envío que falla devuelve su lugar; uno que sale lo gasta", async () => {
    const c = conLimites({}, { topeDiario: 2 });
    c.estado.falla = new Error("WhatsApp no está conectado.");
    encolarN(c, 1);
    await drenar();
    assert.match(c.texto(), /ERROR Error enviando cola-1 a \*\*\*0001: WhatsApp no está conectado\./);
    assert.equal(c.limites.resumen().enviadosHoy, 0);
    assert.equal(c.limites.resumen().enEspera, 0, "devolvió el lugar");

    c.estado.falla = null;
    encolarN(c, 2, 2);
    await drenar();
    await c.reloj.avanzar(3000);
    assert.equal(c.limites.resumen().enviadosHoy, 2);
    assert.equal(c.cola.encolar(tel(9), "otro").codigo, "tope_diario");
});

test("tope por persona: la segunda vez en el día pasa, la tercera no", async () => {
    const c = conLimites({}, { topeDiario: 99, topePorDestinatario: 2 });
    assert.equal(c.cola.encolar("1155550001", "uno").ok, true);
    assert.equal(c.cola.encolar("+54 9 11 5555-0001", "dos").ok, true);
    const r = c.cola.encolar("11 5555-0001", "tres");
    assert.equal(r.estado, 429);
    assert.equal(r.codigo, "limite_destinatario");
});

test("si la persona está conversando rige el tope de una conversación; un aviso suelto sigue con el de siempre", async () => {
    // El consentimiento dice quién escribió hace poco (consentimiento.js); la cola solo lo pasa a los topes.
    const conversando = new Set(["1155550001"]);
    const consentimiento = { permite: telefono => ({ ok: true, enConversacion: conversando.has(telefono) }) };
    const c = conLimites({ consentimiento }, { topeDiario: 99, topePorDestinatario: 2, topeEnConversacion: 4 });

    for (let i = 1; i <= 4; i++) assert.equal(c.cola.encolar("1155550001", `respuesta ${i}`).ok, true, `respuesta ${i}`);
    const quinta = c.cola.encolar("1155550001", "respuesta 5");
    assert.equal(quinta.estado, 429);
    assert.equal(quinta.codigo, "limite_destinatario");

    assert.equal(c.cola.encolar("1166667777", "aviso 1").ok, true);
    assert.equal(c.cola.encolar("1166667777", "aviso 2").ok, true);
    assert.equal(c.cola.encolar("1166667777", "aviso 3").codigo, "limite_destinatario", "quien no escribió hace poco sigue con el tope de un aviso suelto");
});

test("sin consentimiento que diga lo contrario, todo se trata como un aviso suelto (el tope chico)", async () => {
    const consentimiento = { permite: () => ({ ok: true }) };
    const c = conLimites({ consentimiento }, { topeDiario: 99, topePorDestinatario: 1, topeEnConversacion: 10 });
    assert.equal(c.cola.encolar("1155550001", "uno").ok, true);
    assert.equal(c.cola.encolar("1155550001", "dos").codigo, "limite_destinatario");
});

test("tope por hora: lo que se pasa espera en la cola y sale cuando se libera lugar", async () => {
    const c = conLimites({ espaciado: { minMs: 1000, maxMs: 2000 } }, { topeDiario: 99, topePorHora: 2 });
    encolarN(c, 3);
    await drenar();
    await c.reloj.avanzar(10 * 1000);
    assert.equal(c.enviados.length, 2, "dos salieron, el tercero espera");
    assert.equal(c.cola.tamano(), 1);
    await c.reloj.avanzar(58 * 60 * 1000);
    assert.equal(c.enviados.length, 2, "todavía no pasó la hora desde el primero");
    await c.reloj.avanzar(2 * 60 * 1000);
    assert.equal(c.enviados.length, 3, "al liberarse el lugar, sale");
});

test("si la cola está llena, se rechaza ANTES de reservar lugar en los topes: no se pierde cupo", async () => {
    const reloj = new RelojFalso();
    const log = { info() {}, error() {} };
    const limites = new LimiteDeEnvios({
        topeDiario: 10, topePorHora: 99, topePorDestinatario: 99, zonaHoraria: "UTC", clave: CLAVE, almacen: almacenEnMemoria(), reloj, log
    });
    let terminar;
    const cola = new ColaEnvios(() => new Promise(resolver => { terminar = resolver; }), log, { reloj, limites, capacidad: 2, aleatorio: () => 0 });
    assert.equal(cola.encolar(tel(1), "a").ok, true);            // este queda enviándose: no termina
    await drenar();
    assert.equal(cola.encolar(tel(2), "b").ok, true);
    assert.equal(cola.encolar(tel(3), "c").ok, true);

    const llena = cola.encolar(tel(4), "d");

    assert.equal(llena.codigo, "cola_llena");
    assert.equal(llena.estado, 503);
    assert.equal(limites.resumen().enEspera, 3, "el rechazado por cola llena no reservó nada");
    cola.detener();
    terminar();
});

// ---------- Conexión ----------

test("sin conexión de la que se espera recuperar (reconectando), los mensajes esperan y salen cuando vuelve", async () => {
    let estadoDeConexion = "reconectando";
    const c = armar({ estadoDeConexion: () => estadoDeConexion });
    assert.equal(c.cola.encolar("1155550001", "hola").ok, true, "se acepta");
    await drenar();
    await c.reloj.avanzar(60 * 1000);
    assert.equal(c.enviados.length, 0, "pero no sale mientras no haya conexión");
    estadoDeConexion = "conectado";
    await c.reloj.avanzar(1000);
    assert.equal(c.enviados.length, 1);
});

test("si WhatsApp pide a una persona (vincular, atención) o está detenido, no se acepta nada: 503 no_conectado", async () => {
    for (const estado of ["requiere_vincular", "requiere_atencion", "detenido"]) {
        const c = armar({ estadoDeConexion: () => estado });
        const r = c.cola.encolar("1155550001", "hola");
        assert.equal(r.ok, false, estado);
        assert.equal(r.estado, 503);
        assert.equal(r.codigo, "no_conectado");
        assert.match(r.error, new RegExp(estado));
    }
});

test("la conexión se mira primero: sin conexión no se pregunta por el consentimiento ni se gasta cupo", async () => {
    let preguntas = 0;
    const consentimiento = { permite: () => { preguntas++; return { ok: true }; } };
    const c = armar({ estadoDeConexion: () => "requiere_vincular", consentimiento });
    c.cola.encolar("1155550001", "hola");
    assert.equal(preguntas, 0);
});

test("si la conexión cae del todo con mensajes esperando, se descartan (y devuelven su lugar) con aviso en el log", async () => {
    let estadoDeConexion = "reconectando";
    const c = conLimites({ estadoDeConexion: () => estadoDeConexion }, { topeDiario: 5 });
    encolarN(c, 2);
    await drenar();
    estadoDeConexion = "requiere_atencion";
    await c.reloj.avanzar(1000);
    assert.equal(c.enviados.length, 0);
    assert.equal(c.cola.tamano(), 0);
    assert.match(c.texto(), /ERROR Se descartó cola-1 a \*\*\*0001: WhatsApp no está disponible \(requiere_atencion\)\./);
    assert.equal(c.limites.resumen().enEspera, 0);
    assert.equal(c.limites.resumen().enviadosHoy, 0);
});

test("un mensaje que lleva más de 3 horas esperando se descarta: ya no sirve", async () => {
    let estadoDeConexion = "reconectando";
    const c = armar({ estadoDeConexion: () => estadoDeConexion });
    c.cola.encolar("1155550001", "recordatorio");
    await drenar();
    await c.reloj.avanzar(2 * 60 * 60 * 1000 + 59 * 60 * 1000);
    assert.equal(c.cola.tamano(), 1, "a las 2 h 59 todavía espera");
    await c.reloj.avanzar(60 * 1000 + 2000);
    assert.match(c.texto(), /Se descartó cola-1 a \*\*\*0001: lleva demasiado tiempo esperando\./);
    estadoDeConexion = "conectado";
    await c.reloj.avanzar(10000);
    assert.equal(c.enviados.length, 0);
});

// ---------- Apagado y resumen ----------

test("esperarEnCurso: sin nada en curso termina enseguida", async () => {
    const c = armar();
    assert.equal(await c.cola.esperarEnCurso(5000), true);
    encolarN(c, 1);
    await drenar();
    await c.reloj.avanzar(10);
    c.cola.detener();
    assert.equal(await c.cola.esperarEnCurso(5000), true, "el envío ya terminó");
});

test("esperarEnCurso: espera al envío que está saliendo y devuelve true cuando termina", async () => {
    const reloj = new RelojFalso();
    const log = { info() {}, error() {} };
    let terminar;
    const cola = new ColaEnvios(() => new Promise(resolver => { terminar = resolver; }), log, { reloj, aleatorio: () => 0 });
    cola.encolar(tel(1), "a");
    cola.encolar(tel(2), "b");
    await drenar();
    cola.detener();

    let resultado = "pendiente";
    const espera = cola.esperarEnCurso(5000).then(r => { resultado = r; });
    await drenar();
    assert.equal(resultado, "pendiente", "el envío sigue en curso");
    terminar();
    await espera;
    assert.equal(resultado, true);
    assert.equal(cola.tamano(), 1, "y el segundo ya no sale: la cola está detenida");
    assert.equal(reloj.pendientes(), 0, "sin temporizadores sueltos");
});

test("esperarEnCurso: si el envío se cuelga, a los maxMs devuelve false", async () => {
    const reloj = new RelojFalso();
    const cola = new ColaEnvios(() => new Promise(() => {}), { info() {}, error() {} }, { reloj });
    cola.encolar(tel(1), "a");
    await drenar();
    cola.detener();
    let resultado = "pendiente";
    const espera = cola.esperarEnCurso(5000).then(r => { resultado = r; });
    await reloj.avanzar(4999);
    assert.equal(resultado, "pendiente");
    await reloj.avanzar(1);
    await espera;
    assert.equal(resultado, false);
});

test("esperarEnCurso: un envío que falla también cuenta como terminado", async () => {
    const reloj = new RelojFalso();
    let fallar;
    const cola = new ColaEnvios(() => new Promise((_, rechazar) => { fallar = rechazar; }), { info() {}, error() {} }, { reloj });
    cola.encolar(tel(1), "a");
    await drenar();
    cola.detener();
    let resultado = "pendiente";
    const espera = cola.esperarEnCurso(5000).then(r => { resultado = r; });
    await drenar();
    assert.equal(resultado, "pendiente");
    fallar(new Error("falló"));
    await espera;
    assert.equal(resultado, true);
});

test("detener() durante la pausa entre mensajes despierta el loop y esperarEnCurso termina al toque", async () => {
    const c = armar({ espaciado: { minMs: 60000, maxMs: 90000 } });
    encolarN(c, 3);
    await drenar();
    c.cola.detener();
    assert.equal(await c.cola.esperarEnCurso(5000), true);
    assert.equal(c.reloj.pendientes(), 0);
});

test("detener() corta enseguida, incluso en una pausa: no queda nada dormido ni se manda lo que falta", async () => {
    const c = armar({ espaciado: { minMs: 60000, maxMs: 90000 } });
    encolarN(c, 3);
    await drenar();
    assert.equal(c.enviados.length, 1);
    assert.equal(c.reloj.pendientes(), 1, "dormida en la pausa");
    c.cola.detener();
    await drenar();
    assert.equal(c.reloj.pendientes(), 0, "se canceló la espera");
    await c.reloj.avanzar(10 * 60 * 1000);
    assert.equal(c.enviados.length, 1);
    assert.equal(c.cola.encolar(tel(9), "x").codigo, "reiniciando");
});

test("la cola llena se rechaza con 503 cola_llena, con el mismo texto de siempre", async () => {
    const c = armar({ espaciado: { minMs: 60000, maxMs: 90000 } });
    for (let i = 0; i < CAPACIDAD_MAXIMA; i++) assert.equal(c.cola.encolar(tel(i), "x").ok, true);
    const r = c.cola.encolar("1166667777", "uno más");
    assert.deepEqual(r, { ok: false, estado: 503, codigo: "cola_llena", error: "Cola llena, probá de nuevo en unos minutos." });
    await drenar();
    assert.equal(c.cola.tamano(), CAPACIDAD_MAXIMA - 1, "uno ya salió; el rechazado no entró");
});

test("resumen() junta lo de la cola y lo de los topes", async () => {
    const c = conLimites({}, { topeDiario: 10, topePorHora: 5, topePorDestinatario: 3 });
    encolarN(c, 2);
    await drenar();
    assert.deepEqual(c.cola.resumen(), {
        pendientes: 1, dia: "2026-10-16", enviadosHoy: 1, enEspera: 1, topeDiario: 10, enviadosUltimaHora: 1, topePorHora: 5, topePorDestinatario: 3,
        topeEnConversacion: 3
    });
    assert.deepEqual(armar().cola.resumen(), { pendientes: 0 }, "sin topes, solo lo de la cola");
});
