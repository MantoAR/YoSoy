// Consentimiento (consentimiento.js + las reglas de privacidad.js), el almacén de datos (almacen.js) y los topes de envío (limites.js).
// Sin red ni WhatsApp: relojes de mentira y carpetas temporales.
require("./helpers/sin-red");
const test = require("node:test");
const { mock } = test;
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { interpretarComando, derivarClaveDeHuellas, huellaDeTelefono } = require("../privacidad");
const { crearAlmacenJson } = require("../almacen");
const { RegistroDeConsentimiento } = require("../consentimiento");
const { LimiteDeEnvios, diaEnZona } = require("../limites");

const CLAVE = derivarClaveDeHuellas("secreto-compartido-de-prueba");
const TEL = "1155550001";
const OTRO = "1166667777";
const DIA = 24 * 60 * 60 * 1000;
const HORA = 60 * 60 * 1000;

// Un reloj que se mueve a mano (cada prueba lo maneja).
function relojManual(inicio = Date.UTC(2026, 9, 16, 15, 0, 0)) {       // 2026-10-16 12:00 en Buenos Aires
    const r = { t: inicio, ahora: () => r.t, avanzar: ms => { r.t += ms; } };
    return r;
}

function conCarpeta(cuerpo) {
    return async () => {
        const raiz = fs.mkdtempSync(path.join(os.tmpdir(), "yosoy-consentimiento-"));
        const lineas = [];
        const log = { info: m => lineas.push(`INFO ${m}`), error: m => lineas.push(`ERROR ${m}`) };
        try { await cuerpo(raiz, { log, lineas, texto: () => lineas.join("\n") }); }
        finally { fs.rmSync(raiz, { recursive: true, force: true }); }
    };
}

// ---------- privacidad.js: comandos y huellas ----------

test("BAJA, STOP y compañía (el mensaje entero, sin importar mayúsculas, tildes ni signos) piden la baja", () => {
    for (const texto of ["BAJA", "baja", " Baja. ", "¡STOP!", "stop", "Parar", "basta", "No molestar", "dar de baja", "Darme de baja", "no quiero recibir mensajes", "No quiero más mensajes"]) {
        assert.equal(interpretarComando(texto), "baja", texto);
    }
});

test("ALTA y START vuelven a aceptar mensajes", () => {
    for (const texto of ["ALTA", "alta", " Alta! ", "START", "Reanudar", "quiero recibir mensajes"]) {
        assert.equal(interpretarComando(texto), "alta", texto);
    }
});

test("lo que parece pero no es: cancelar un turno, una frase con 'baja', texto vacío", () => {
    for (const texto of ["cancelar", "CANCELAR", "cancelar turno", "quiero dar de baja mi turno", "baja del turno", "el turno se dio de baja", "alta médica", "hola", "", "   ", null, undefined, 123]) {
        assert.equal(interpretarComando(texto), null, String(texto));
    }
});

test("la clave de las huellas sale del secreto compartido: sin secreto no hay clave, y otro secreto da otra clave", () => {
    assert.throws(() => derivarClaveDeHuellas(""), /Falta el secreto compartido/);
    assert.throws(() => derivarClaveDeHuellas(undefined), /Falta el secreto compartido/);
    assert.deepEqual(derivarClaveDeHuellas("a"), derivarClaveDeHuellas("a"));
    assert.notDeepEqual(derivarClaveDeHuellas("a"), derivarClaveDeHuellas("b"));
});

test("la huella de un teléfono: la misma para todas sus formas, distinta entre personas y entre claves, y sin el número adentro", () => {
    const h = huellaDeTelefono("11 5555-0001", CLAVE);
    assert.match(h, /^[0-9a-f]{32}$/);
    for (const forma of ["1155550001", "+54 9 11 5555-0001", "5491155550001", "(011) 5555-0001".replace("(011)", "11")]) {
        assert.equal(huellaDeTelefono(forma, CLAVE), h, forma);
    }
    assert.notEqual(huellaDeTelefono(OTRO, CLAVE), h);
    assert.notEqual(huellaDeTelefono(TEL, derivarClaveDeHuellas("otro secreto")), h);
    assert.equal(h.includes("5555"), false);
});

test("un teléfono imposible no tiene huella", () => {
    for (const malo of ["", null, undefined, "abc", "123", "1234567"]) assert.equal(huellaDeTelefono(malo, CLAVE), null, String(malo));
    assert.notEqual(huellaDeTelefono("12345678", CLAVE), null, "8 dígitos es el piso");
});

// ---------- almacen.js ----------

test("el almacén guarda y lee un objeto, y no deja temporales", conCarpeta(async (raiz, { log }) => {
    const almacen = crearAlmacenJson(path.join(raiz, "x.json"), { log, nombre: "lo de prueba" });
    assert.equal(await almacen.leer(), null, "sin archivo no hay nada y no se avisa de nada");
    await almacen.guardar({ a: 1, b: [2, 3] });
    assert.deepEqual(await almacen.leer(), { a: 1, b: [2, 3] });
    assert.deepEqual(fs.readdirSync(raiz), ["x.json"]);
}));

test("varios guardados seguidos se juntan: queda lo último y no se pisan", conCarpeta(async (raiz, { log }) => {
    const almacen = crearAlmacenJson(path.join(raiz, "x.json"), { log });
    for (let i = 1; i <= 20; i++) almacen.guardar({ n: i });
    await almacen.cerrar();
    assert.deepEqual(await almacen.leer(), { n: 20 });
    assert.deepEqual(fs.readdirSync(raiz), ["x.json"]);
}));

test("el guardado toma una copia en el momento: cambiar el objeto después no cambia lo que se escribe", conCarpeta(async (raiz, { log }) => {
    const almacen = crearAlmacenJson(path.join(raiz, "x.json"), { log });
    const valor = { n: 1 };
    almacen.guardar(valor);
    valor.n = 2;
    await almacen.cerrar();
    assert.deepEqual(await almacen.leer(), { n: 1 });
}));

test("un archivo dañado se aparta (nombre.corrupto.bak), se avisa y se empieza vacío", conCarpeta(async (raiz, { log, texto }) => {
    const ruta = path.join(raiz, "x.json");
    fs.writeFileSync(ruta, "{\"a\": ");
    const almacen = crearAlmacenJson(ruta, { log, nombre: "los datos de prueba" });
    assert.equal(await almacen.leer(), null);
    assert.match(texto(), /ERROR El archivo de los datos de prueba estaba dañado/);
    assert.deepEqual(fs.readdirSync(raiz), ["x.json.corrupto.bak"]);
}));

test("si no se puede escribir, se avisa (sin romper) y el siguiente guardado funciona", conCarpeta(async (raiz, { log, texto }) => {
    const almacen = crearAlmacenJson(path.join(raiz, "x.json"), { log, nombre: "los contadores" });
    const renombre = mock.method(fs.promises, "rename", async () => { throw Object.assign(new Error("disco lleno"), { code: "ENOSPC" }); });
    try { await almacen.guardar({ n: 1 }); } finally { renombre.mock.restore(); }
    assert.match(texto(), /ERROR No se pudo guardar los contadores \(ENOSPC\)/);
    assert.equal(fs.existsSync(path.join(raiz, "x.json.tmp")), false, "sin temporal suelto");
    await almacen.guardar({ n: 2 });
    assert.deepEqual(await almacen.leer(), { n: 2 });
}));

// ---------- RegistroDeConsentimiento ----------

async function registro(raiz, log, extra = {}) {
    const almacen = crearAlmacenJson(path.join(raiz, "consentimientos.json"), { log, nombre: "los consentimientos" });
    const reloj = extra.reloj ?? relojManual();
    const r = new RegistroDeConsentimiento({ almacen, clave: CLAVE, log, reloj, ...extra });
    await r.cargar();
    return { r, reloj, almacen };
}

test("por defecto NO se envía: sin constancia, 403 sin_consentimiento", conCarpeta(async (raiz, { log }) => {
    const { r } = await registro(raiz, log);
    const res = r.permite(TEL);
    assert.equal(res.ok, false);
    assert.equal(res.estado, 403);
    assert.equal(res.codigo, "sin_consentimiento");
    assert.match(res.error, /No hay constancia/);
}));

test("optIn:true de la API alcanza; cualquier otra cosa (1, 'true', 'si') no", conCarpeta(async (raiz, { log }) => {
    const { r } = await registro(raiz, log);
    assert.deepEqual(Object.assign({}, r.permite(TEL, { optIn: true })), { ok: true, via: "optin_api", huella: huellaDeTelefono(TEL, CLAVE) });
    for (const falso of [1, "true", "si", {}, false, undefined, null]) {
        assert.equal(r.permite(TEL, { optIn: falso }).codigo, "sin_consentimiento", String(falso));
    }
}));

test("optIn de la API no se acumula: se evalúa en cada pedido", conCarpeta(async (raiz, { log }) => {
    const { r } = await registro(raiz, log);
    assert.equal(r.permite(TEL, { optIn: true }).ok, true);
    assert.equal(r.permite(TEL).ok, false, "el siguiente pedido, sin optIn, vuelve a ser rechazado");
    assert.equal(r.cantidad(), 0, "y la API no deja nada anotado");
}));

test("si la persona le escribió primero al número, se le puede responder (con cualquier forma de su teléfono)", conCarpeta(async (raiz, { log }) => {
    const { r } = await registro(raiz, log);
    r.alMensajeEntrante("5491155550001", "hola, quiero un turno");
    for (const forma of ["1155550001", "+54 9 11 5555-0001", "11 5555-0001"]) {
        const res = r.permite(forma);
        assert.equal(res.ok, true, forma);
        assert.equal(res.via, "escribio_primero");
    }
    assert.equal(r.permite(OTRO).ok, false, "a otra persona, no");
}));

test("la constancia de haber escrito primero vence al año, y cada mensaje nuevo la renueva", conCarpeta(async (raiz, { log }) => {
    const { r, reloj } = await registro(raiz, log, { diasDeVigencia: 365 });
    r.alMensajeEntrante(TEL, "hola");
    reloj.avanzar(365 * DIA);
    assert.equal(r.permite(TEL).ok, true, "el día 365 todavía vale");
    reloj.avanzar(DIA);
    assert.equal(r.permite(TEL).ok, false, "el 366 ya no");
    r.alMensajeEntrante(TEL, "hola de nuevo");
    assert.equal(r.permite(TEL).ok, true, "un mensaje nuevo la renueva");
}));

test("BAJA gana sobre todo: ni escribiendo primero ni con optIn de la API se le puede mandar", conCarpeta(async (raiz, { log, texto }) => {
    const { r } = await registro(raiz, log);
    r.alMensajeEntrante(TEL, "hola");
    assert.equal(r.alMensajeEntrante(TEL, "BAJA"), "baja");
    const res = r.permite(TEL, { optIn: true });
    assert.equal(res.ok, false);
    assert.equal(res.estado, 403);
    assert.equal(res.codigo, "baja");
    assert.equal(r.permite(TEL).codigo, "baja");
    assert.match(texto(), /INFO Una persona \(\*\*\*0001\) pidió no recibir más mensajes/);
    assert.equal(texto().includes("1155550001"), false, "el teléfono no está completo en el log");
}));

test("un mensaje común después de la baja NO la levanta; ALTA sí", conCarpeta(async (raiz, { log }) => {
    const { r } = await registro(raiz, log);
    r.alMensajeEntrante(TEL, "baja");
    r.alMensajeEntrante(TEL, "hola, necesito un turno");
    assert.equal(r.permite(TEL).codigo, "baja");
    assert.equal(r.alMensajeEntrante(TEL, "ALTA"), "alta");
    assert.equal(r.permite(TEL).ok, true);
}));

test("'cancelar' (un turno) no es una baja", conCarpeta(async (raiz, { log }) => {
    const { r } = await registro(raiz, log);
    assert.equal(r.alMensajeEntrante(TEL, "cancelar"), null);
    assert.equal(r.permite(TEL).ok, true);
}));

test("la baja se recuerda para siempre, aunque venza la constancia de lo demás", conCarpeta(async (raiz, { log }) => {
    const { r, reloj } = await registro(raiz, log, { diasDeVigencia: 30 });
    r.alMensajeEntrante(TEL, "baja");
    r.alMensajeEntrante(OTRO, "hola");
    reloj.avanzar(3650 * DIA);
    r.alMensajeEntrante("1100000000", "hola");                       // cualquier movimiento dispara la poda
    assert.equal(r.permite(TEL, { optIn: true }).codigo, "baja");
    assert.equal(r.permite(OTRO).ok, false, "la constancia vencida se olvidó");
    assert.equal(r.cantidad(), 2);
}));

test("un teléfono imposible da 400, no un rechazo de consentimiento", conCarpeta(async (raiz, { log }) => {
    const { r } = await registro(raiz, log);
    const res = r.permite("123");
    assert.equal(res.estado, 400);
    assert.equal(res.codigo, "telefono_invalido");
    assert.equal(r.alMensajeEntrante("123", "hola"), null);
    assert.equal(r.cantidad(), 0);
}));

test("el registro sobrevive a un reinicio, y el archivo no tiene teléfonos ni texto de mensajes", conCarpeta(async (raiz, { log }) => {
    const primero = await registro(raiz, log);
    primero.r.alMensajeEntrante(TEL, "hola, soy María y tengo un dolor de cabeza");
    primero.r.alMensajeEntrante(OTRO, "BAJA");
    await primero.r.cerrar();

    const segundo = await registro(raiz, log, { reloj: primero.reloj });
    assert.equal(segundo.r.permite(TEL).ok, true);
    assert.equal(segundo.r.permite(OTRO).codigo, "baja");

    const crudo = fs.readFileSync(path.join(raiz, "consentimientos.json"), "utf8");
    for (const prohibido of ["1155550001", "1166667777", "María", "dolor", "hola", "BAJA"]) {
        assert.equal(crudo.includes(prohibido), false, `el archivo no puede incluir "${prohibido}"`);
    }
}));

test("con otra clave (se cambió el secreto compartido) el registro viejo no coincide: se vuelve a no enviar", conCarpeta(async (raiz, { log }) => {
    const primero = await registro(raiz, log);
    primero.r.alMensajeEntrante(TEL, "hola");
    await primero.r.cerrar();
    const almacen = crearAlmacenJson(path.join(raiz, "consentimientos.json"), { log });
    const otro = new RegistroDeConsentimiento({ almacen, clave: derivarClaveDeHuellas("otro secreto"), log, reloj: primero.reloj });
    await otro.cargar();
    assert.equal(otro.permite(TEL).codigo, "sin_consentimiento");
}));

test("cargar ignora las entradas rotas y no se cae", conCarpeta(async (raiz, { log }) => {
    fs.writeFileSync(path.join(raiz, "consentimientos.json"), JSON.stringify({ v: 1, contactos: {
        [huellaDeTelefono(TEL, CLAVE)]: { desde: Date.UTC(2026, 9, 1), ultimo: Date.UTC(2026, 9, 10), baja: null },
        malo1: null, malo2: { ultimo: "ayer" }, malo3: "texto", malo4: []
    } }));
    const { r } = await registro(raiz, log);
    assert.equal(r.cantidad(), 1);
    assert.equal(r.permite(TEL).ok, true);
}));

test("si el archivo crece de más, se olvidan las constancias más viejas pero nunca las bajas", conCarpeta(async (raiz, { log }) => {
    const { r, reloj } = await registro(raiz, log, { maxContactos: 10 });
    r.alMensajeEntrante("1100000001", "baja");               // la más vieja, pero es una baja
    for (let i = 2; i <= 14; i++) { reloj.avanzar(1000); r.alMensajeEntrante(`11000000${String(i).padStart(2, "0")}`, "hola"); }
    assert.ok(r.cantidad() <= 10);
    assert.equal(r.permite("1100000001").codigo, "baja", "la baja sigue");
    assert.equal(r.permite("1100000014").ok, true, "la más nueva sigue");
    assert.equal(r.permite("1100000002").ok, false, "la más vieja se olvidó");
}));

// ---------- limites.js ----------

async function limites(raiz, log, extra = {}) {
    const almacen = crearAlmacenJson(path.join(raiz, "envios.json"), { log, nombre: "los contadores" });
    const reloj = extra.reloj ?? relojManual();
    const l = new LimiteDeEnvios({
        topeDiario: 5, topePorHora: 3, topePorDestinatario: 2, zonaHoraria: "America/Argentina/Buenos_Aires",
        clave: CLAVE, almacen, reloj, log, ...extra
    });
    await l.cargar();
    return { l, reloj, almacen };
}

// Pide lugar y confirma, como hace la cola: devuelve el resultado de reservar.
function mandar(l, telefono) {
    const r = l.reservar(telefono);
    if (r.ok) l.confirmar(r.huella);
    return r;
}

test("el día en la zona de la clínica: 23:59 y 00:00 de Buenos Aires", () => {
    const BA = "America/Argentina/Buenos_Aires";
    assert.equal(diaEnZona(Date.UTC(2026, 9, 17, 2, 59, 59), BA), "2026-10-16");
    assert.equal(diaEnZona(Date.UTC(2026, 9, 17, 3, 0, 0), BA), "2026-10-17");
    assert.equal(diaEnZona(Date.UTC(2026, 9, 17, 2, 59, 59), "UTC"), "2026-10-17");
});

test("tope diario: al llegar, 429 tope_diario, y no gasta cupo lo que se rechaza", conCarpeta(async (raiz, { log }) => {
    const { l } = await limites(raiz, log, { topePorDestinatario: 99, topePorHora: 99 });
    for (let i = 0; i < 5; i++) assert.equal(mandar(l, `11555500${String(i).padStart(2, "0")}`).ok, true);
    const res = l.reservar("1155559999");
    assert.equal(res.ok, false);
    assert.equal(res.estado, 429);
    assert.equal(res.codigo, "tope_diario");
    assert.match(res.error, /tope de 5 mensajes por día/);
    assert.equal(l.resumen().enviadosHoy, 5);
}));

test("los aceptados que esperan en la cola ya cuentan contra el tope del día; los que fallan devuelven su lugar", conCarpeta(async (raiz, { log }) => {
    const { l } = await limites(raiz, log, { topeDiario: 2, topePorDestinatario: 99 });
    const a = l.reservar("1155550001");
    const b = l.reservar("1155550002");
    assert.equal(l.reservar("1155550003").codigo, "tope_diario", "hay 2 esperando: no cabe otro");
    assert.equal(l.resumen().enEspera, 2);
    l.liberar(a.huella);                                         // el primero falló
    assert.equal(l.reservar("1155550003").ok, true, "ahora sí");
    l.confirmar(b.huella);
    assert.equal(l.resumen().enviadosHoy, 1);
}));

test("tope por persona: la tercera vez en el día se rechaza (limite_destinatario), aunque venga con otro formato", conCarpeta(async (raiz, { log }) => {
    const { l } = await limites(raiz, log, { topePorHora: 99 });
    assert.equal(mandar(l, "1155550001").ok, true);
    assert.equal(mandar(l, "+54 9 11 5555-0001").ok, true);
    const res = l.reservar("11 5555-0001");
    assert.equal(res.codigo, "limite_destinatario");
    assert.equal(res.estado, 429);
    assert.equal(mandar(l, "1155550002").ok, true, "otra persona, sí");
}));

test("tope por hora: al llegar hay que esperar, con el tiempo exacto hasta que salga el más viejo", conCarpeta(async (raiz, { log }) => {
    const { l, reloj } = await limites(raiz, log, { topePorDestinatario: 99 });
    assert.deepEqual(l.puedeEnviarYa(), { ok: true });
    mandar(l, "1155550001");
    reloj.avanzar(10 * 60 * 1000);
    mandar(l, "1155550002");
    reloj.avanzar(10 * 60 * 1000);
    mandar(l, "1155550003");
    const espera = l.puedeEnviarYa();
    assert.equal(espera.ok, false);
    assert.equal(espera.esperarMs, 40 * 60 * 1000 + 1, "falta para que salga el primero de la última hora");
    reloj.avanzar(espera.esperarMs);
    assert.deepEqual(l.puedeEnviarYa(), { ok: true });
}));

test("a medianoche (de Buenos Aires) los contadores del día vuelven a cero", conCarpeta(async (raiz, { log }) => {
    const { l, reloj } = await limites(raiz, log, { topeDiario: 2, topePorHora: 99, topePorDestinatario: 1 });
    reloj.t = Date.UTC(2026, 9, 17, 2, 58, 0);                    // 23:58 del 16
    mandar(l, "1155550001");
    mandar(l, "1155550002");
    assert.equal(l.reservar("1155550003").codigo, "tope_diario");
    assert.equal(l.reservar("1155550001").codigo, "tope_diario");
    reloj.avanzar(2 * 60 * 1000);                                  // 00:00 del 17
    assert.equal(l.resumen().dia, "2026-10-17");
    assert.equal(l.resumen().enviadosHoy, 0);
    assert.equal(mandar(l, "1155550001").ok, true, "ni el tope por persona sigue");
}));

test("los contadores sobreviven a un reinicio: reiniciar el servicio no devuelve el cupo del día", conCarpeta(async (raiz, { log }) => {
    const primero = await limites(raiz, log, { topePorDestinatario: 99, topePorHora: 99 });
    for (let i = 0; i < 4; i++) mandar(primero.l, `11555500${String(i).padStart(2, "0")}`);
    await primero.l.cerrar();

    const segundo = await limites(raiz, log, { reloj: primero.reloj, topePorDestinatario: 99, topePorHora: 99 });
    assert.equal(segundo.l.resumen().enviadosHoy, 4);
    assert.equal(mandar(segundo.l, "1155559998").ok, true);
    assert.equal(segundo.l.reservar("1155559999").codigo, "tope_diario");
}));

test("tras un reinicio, el tope por hora sigue contando lo de la última hora, y lo de ayer no cuenta", conCarpeta(async (raiz, { log }) => {
    const primero = await limites(raiz, log, { topePorDestinatario: 99 });
    for (let i = 0; i < 3; i++) mandar(primero.l, `11555500${String(i).padStart(2, "0")}`);
    await primero.l.cerrar();

    const mismoDia = await limites(raiz, log, { reloj: primero.reloj, topePorDestinatario: 99 });
    assert.equal(mismoDia.l.puedeEnviarYa().ok, false);

    primero.reloj.avanzar(DIA + HORA);
    const otroDia = await limites(raiz, log, { reloj: primero.reloj, topePorDestinatario: 99 });
    assert.equal(otroDia.l.resumen().enviadosHoy, 0, "el contador de ayer no se arrastra");
    assert.equal(otroDia.l.puedeEnviarYa().ok, true);
}));

test("los contadores no guardan teléfonos: por persona va la huella", conCarpeta(async (raiz, { log }) => {
    const { l } = await limites(raiz, log);
    mandar(l, "1155550001");
    await l.cerrar();
    const crudo = fs.readFileSync(path.join(raiz, "envios.json"), "utf8");
    assert.equal(crudo.includes("5555"), false);
    assert.ok(crudo.includes(huellaDeTelefono("1155550001", CLAVE)));
}));

test("un teléfono imposible da 400 y no gasta cupo", conCarpeta(async (raiz, { log }) => {
    const { l } = await limites(raiz, log);
    const res = l.reservar("12");
    assert.equal(res.estado, 400);
    assert.equal(res.codigo, "telefono_invalido");
    assert.equal(l.resumen().enEspera, 0);
}));

test("el resumen informa lo necesario para /salud", conCarpeta(async (raiz, { log }) => {
    const { l } = await limites(raiz, log);
    mandar(l, "1155550001");
    l.reservar("1155550002");
    assert.deepEqual(l.resumen(), {
        dia: "2026-10-16", enviadosHoy: 1, enEspera: 1, topeDiario: 5, enviadosUltimaHora: 1, topePorHora: 3, topePorDestinatario: 2
    });
}));
