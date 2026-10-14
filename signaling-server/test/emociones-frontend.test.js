// Pruebas de PANTALLAS/shared/emociones.js: cómo la pantalla obedece el consentimiento de la lectura de emociones.
//
// emociones.js es un script de navegador: acá se carga en un contexto de vm con lo mínimo que espera (window, YS.api y
// timers). No hay red ni relojes reales: la API es un doble que contesta lo que cada prueba le pide y los timers y la hora
// son de mentira (se "avanza" el tiempo), así se prueba la falla cerrada (a los 10 s sin respuesta se frena) sin esperar.
//
// Lo que estas pruebas NO cubren es el navegador de verdad (cámara, face-api, la interfaz): eso necesita navegadores.
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const CODIGO = fs.readFileSync(path.join(__dirname, '..', '..', 'PANTALLAS', 'shared', 'emociones.js'), 'utf8');
const SESION = { origen: 'turno', id: 12 };

/** Lo que devuelve emociones.js viene de otro "reino" de vm: se pasa por JSON para poder compararlo con deepEqual. */
const plano = x => JSON.parse(JSON.stringify(x));

const vaciar = async () => { for (let i = 0; i < 10; i++) await new Promise(r => setImmediate(r)); };

/** Hora y timers de mentira. avanzar(ms) corre lo que vence en ese lapso, en orden, y deja que se resuelvan las promesas. */
function reloj(inicio = 1_800_000_000_000) {
    let t = inicio;
    const timers = new Map();
    let id = 0;
    return {
        ahora: () => t,
        programar: (f, ms) => { const k = ++id; timers.set(k, { f, en: t + ms }); return k; },
        cancelar: k => { timers.delete(k); },
        pendientes: () => timers.size,
        async avanzar(ms) {
            const fin = t + ms;
            for (;;) {
                await vaciar();
                const proximo = [...timers.entries()].filter(([, v]) => v.en <= fin).sort((a, b) => a[1].en - b[1].en)[0];
                if (!proximo) break;
                const [k, v] = proximo;
                timers.delete(k);
                t = Math.max(t, v.en);
                v.f();
            }
            t = fin;
            await vaciar();
        }
    };
}

/** La API de mentira: lo que contesta GET /video/emociones/permiso. Se cambia entre pruebas y entre consultas. */
function servidorFalso(r, { desfaseMs = 0 } = {}) {
    const s = {
        permitido: true, estado: 'acepta', motivo: null,
        desdeUtc: new Date(r.ahora() - 5000).toISOString(), revocadoUtc: null, version: 'v0-provisorio',
        consultarCadaSegundos: 3, vigenciaSegundos: 10,
        falla: null,        // null | { status } | 'red' | 'basura'
        consultas: 0,
        async api() {
            s.consultas++;
            if (s.falla === 'red') throw Object.assign(new Error('sin conexión'), { status: 0, sinConexion: true });
            if (s.falla && s.falla.status) throw Object.assign(new Error('http'), { status: s.falla.status });
            if (s.falla === 'basura') return { cualquier: 'cosa' };
            return {
                permitido: s.permitido, estado: s.estado, motivo: s.motivo, desdeUtc: s.desdeUtc, revocadoUtc: s.revocadoUtc,
                ahoraUtc: new Date(r.ahora() + desfaseMs).toISOString(),
                consultarCadaSegundos: s.consultarCadaSegundos, vigenciaSegundos: s.vigenciaSegundos, version: s.version
            };
        }
    };
    return s;
}

/** Carga emociones.js como lo cargaría el navegador. */
function cargar({ api, video } = {}) {
    const llamadas = [];
    const YS = {
        api: async (ruta, opciones) => {
            llamadas.push(plano({ ruta, opciones }));
            if (!api) return {};
            return api(ruta, opciones);
        }
    };
    if (video) YS.video = video;
    const window = { YS };
    const contexto = { window, console: { warn() {}, log() {}, error() {} }, setTimeout, clearTimeout, Date, Promise };
    vm.createContext(contexto);
    vm.runInContext(CODIGO, contexto, { filename: 'emociones.js' });
    return { E: window.YS.emociones, llamadas, YS };
}

const expresiones = (neutral, sad = 0, happy = 0) => ({ neutral, sad, happy, angry: 0, fearful: 0, surprised: 0, disgusted: 0 });

// ───────────────────────── vigilar ─────────────────────────

test('vigilar: al iniciar avisa "consultando" y después lo que contesta la API', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    const { E } = cargar();
    const vistas = [];
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar, alCambiar: x => vistas.push(plano(x)) });

    v.iniciar();
    await vaciar();

    assert.deepEqual(vistas.map(x => x.estado), ['consultando', 'acepta']);
    assert.equal(vistas[0].permitido, false);
    assert.equal(vistas[1].permitido, true);
    assert.equal(vistas[1].desdeUtc, s.desdeUtc);
    v.detener();
});

test('vigilar: pregunta cada tantos segundos como dice la API y no avisa si nada cambió', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    const { E } = cargar();
    let avisos = 0;
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar, alCambiar: () => { avisos++; } });

    v.iniciar();
    await r.avanzar(10_000);   // consultas en 0, 3, 6 y 9 s

    assert.equal(s.consultas, 4);
    assert.equal(avisos, 2);   // "consultando" y "acepta": las demás respuestas eran iguales
    v.detener();
});

test('vigilar: la API manda el ritmo (consultarCadaSegundos)', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    s.consultarCadaSegundos = 1;
    const { E } = cargar();
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar });

    v.iniciar();
    await r.avanzar(5_000);

    assert.equal(s.consultas, 6);   // 0, 1, 2, 3, 4 y 5 s
    v.detener();
});

test('vigilar: FALLA CERRADA — si la API deja de contestar, a los 10 s se frena, y vuelve cuando contesta', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    const { E } = cargar();
    const vistas = [];
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar, alCambiar: x => vistas.push(plano(x)) });
    v.iniciar();
    await r.avanzar(1_000);
    assert.equal(v.permitido(), true);

    s.falla = 'red';
    await r.avanzar(8_000);                       // 9 s desde la última respuesta buena: todavía vale
    assert.equal(v.permitido(), true);
    await r.avanzar(3_000);                       // 12 s: venció
    assert.equal(v.permitido(), false);
    assert.equal(v.vista().estado, 'sin-conexion');
    assert.equal(vistas.at(-1).estado, 'sin-conexion');

    s.falla = null;
    await r.avanzar(3_500);
    assert.equal(v.permitido(), true);
    assert.equal(vistas.at(-1).estado, 'acepta');
    v.detener();
});

test('vigilar: una consulta que falla suelta no frena nada mientras la última respuesta siga vigente', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    const { E } = cargar();
    const estados = [];
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar, alCambiar: x => estados.push(x.estado) });
    v.iniciar();
    await r.avanzar(1_000);

    s.falla = 'red';
    await r.avanzar(3_000);   // una consulta que falla
    s.falla = null;
    await r.avanzar(3_000);

    assert.deepEqual(estados, ['consultando', 'acepta']);
    assert.equal(v.permitido(), true);
    v.detener();
});

test('vigilar: una respuesta que no tiene la forma esperada no cuenta como "sí"', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    s.falla = 'basura';
    const { E } = cargar();
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar });

    v.iniciar();
    await r.avanzar(20_000);

    assert.equal(v.permitido(), false);
    assert.equal(v.vista().estado, 'consultando');
    v.detener();
});

test('vigilar: 404 y 401 se avisan enseguida, sin esperar al vencimiento', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    const { E } = cargar();
    const estados = [];
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar, alCambiar: x => estados.push(x.estado) });
    v.iniciar();
    await r.avanzar(1_000);

    s.falla = { status: 404 };
    await r.avanzar(3_000);
    assert.equal(v.vista().estado, 'sin-sesion');
    assert.equal(v.permitido(), false);

    s.falla = { status: 401 };
    await r.avanzar(3_000);
    assert.equal(v.vista().estado, 'sesion-vencida');

    s.falla = null;
    await r.avanzar(3_000);
    assert.equal(v.permitido(), true);
    assert.deepEqual(estados, ['consultando', 'acepta', 'sin-sesion', 'sesion-vencida', 'acepta']);
    v.detener();
});

test('vigilar: la revocación se ve en la consulta siguiente y se avisa una sola vez', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    const { E } = cargar();
    const vistas = [];
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar, alCambiar: x => vistas.push(plano(x)) });
    v.iniciar();
    await r.avanzar(1_000);

    s.permitido = false; s.estado = 'revocado'; s.revocadoUtc = new Date(r.ahora()).toISOString();
    await r.avanzar(3_000);
    await r.avanzar(3_000);

    assert.deepEqual(vistas.map(x => x.estado), ['consultando', 'acepta', 'revocado']);
    assert.equal(vistas.at(-1).permitido, false);
    assert.equal(vistas.at(-1).revocadoUtc, s.revocadoUtc);
    v.detener();
});

test('vigilar: respeta la vigencia que dice la API', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    s.vigenciaSegundos = 4;
    const { E } = cargar();
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar });
    v.iniciar();
    await r.avanzar(1_000);

    s.falla = 'red';
    await r.avanzar(3_500);   // 4,5 s desde la última buena: ya venció con vigencia de 4
    assert.equal(v.permitido(), false);
    v.detener();
});

test('vigilar: consultarAhora pregunta de nuevo y dice si la respuesta es de verdad de ahora', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    const { E } = cargar();
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar });
    v.iniciar();
    await r.avanzar(1_000);

    const buena = await v.consultarAhora();
    assert.equal(buena.fresca, true);
    assert.equal(buena.permitido, true);

    s.falla = 'red';
    const mala = await v.consultarAhora();
    assert.equal(mala.fresca, false);           // el "sí" que se ve es viejo: no alcanza para guardar nada
    assert.equal(mala.permitido, true);         // (sigue vigente para seguir mirando, pero no es "fresca")

    s.falla = null; s.permitido = false; s.estado = 'revocado';
    const revocada = await v.consultarAhora();
    assert.equal(revocada.fresca, true);
    assert.equal(revocada.permitido, false);
    v.detener();
});

test('vigilar: calibra la hora del servidor (el reloj del navegador puede estar corrido)', async () => {
    const r = reloj();
    const cincoMinutos = 5 * 60 * 1000;
    const s = servidorFalso(r, { desfaseMs: cincoMinutos });
    const { E } = cargar();
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar });

    v.iniciar();
    await r.avanzar(1_000);

    assert.equal(v.ahoraServidorMs() - r.ahora(), cincoMinutos);
    v.detener();
});

test('vigilar: detener cancela los timers y no pregunta más', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    const { E } = cargar();
    const v = E.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar });
    v.iniciar();
    await r.avanzar(1_000);
    const antes = s.consultas;

    v.detener();
    await r.avanzar(30_000);

    assert.equal(s.consultas, antes);
    assert.equal(r.pendientes(), 0);
});

test('vigilar: sin api propia usa YS.api con la ruta, el origen y el id correctos y sin redirigir al login', async () => {
    const r = reloj();
    const s = servidorFalso(r);
    const { E, llamadas } = cargar({ api: () => s.api() });
    const v = E.vigilar({ sesion: { origen: 'admision', id: 7 }, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar });

    v.iniciar();
    await vaciar();

    assert.equal(llamadas[0].ruta, '/video/emociones/permiso?origen=admision&id=7');
    assert.equal(llamadas[0].opciones.sinRedireccion, true);
    v.detener();
});

// ───────────────────────── crearAcumulador ─────────────────────────

test('acumulador: promedia, redondea y guarda solo sumas', () => {
    const { E } = cargar();
    const a = E.crearAcumulador();

    assert.equal(a.resumen(), null);
    assert.equal(a.agregar(expresiones(0.6, 0.4), 1000), true);
    assert.equal(a.agregar(expresiones(0.8, 0.2), 4000), true);
    assert.equal(a.agregar(expresiones(0.7, 0.1, 0.2), 9000), true);

    const r = plano(a.resumen());
    assert.equal(r.muestras, 3);
    assert.deepEqual(r.promedios, { neutral: 70, happy: 7, sad: 23, angry: 0, fearful: 0, surprised: 0, disgusted: 0 });
    assert.equal(r.desdeMs, 1000);
    assert.equal(r.hastaMs, 9000);
    assert.equal(a.dominante(), 'neutral');
    assert.deepEqual(Object.keys(r).sort(), ['desdeMs', 'hastaMs', 'muestras', 'promedios']);   // nada por muestra
});

test('acumulador: rechaza lo que no es una muestra y acota los valores', () => {
    const { E } = cargar();
    const a = E.crearAcumulador();

    assert.equal(a.agregar(null, 1), false);
    assert.equal(a.agregar({}, 1), false);
    assert.equal(a.agregar({ otra: 1 }, 1), false);
    assert.equal(a.agregar(expresiones(0.5), NaN), false);
    assert.equal(a.cantidad(), 0);

    a.agregar(expresiones(5), 1);   // un valor mayor que 1 se acota
    a.agregar(expresiones(-1), 2);  // uno negativo no suma
    assert.equal(plano(a.resumen()).promedios.neutral, 50);   // (1 + 0) / 2
});

test('acumulador: limpiar deja todo en cero', () => {
    const { E } = cargar();
    const a = E.crearAcumulador();
    a.agregar(expresiones(1), 1);
    a.agregar(expresiones(1), 2);

    a.limpiar();

    assert.equal(a.cantidad(), 0);
    assert.equal(a.resumen(), null);
    assert.equal(a.dominante(), null);
});

// ───────────────────────── analizar ─────────────────────────

function analisis(opciones = {}) {
    const r = reloj();
    const s = servidorFalso(r, opciones);
    const { E } = cargar();
    const vistas = [];
    const a = E.analizar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar, alCambiar: (v, i) => vistas.push({ ...plano(v), acumuladas: i.acumuladas }) });
    return { r, s, a, vistas };
}

test('analizar: sin permiso no suma nada, con permiso sí', async () => {
    const { r, s, a } = analisis();
    s.permitido = false; s.estado = 'sin-decision';
    a.iniciar();
    await r.avanzar(1_000);

    assert.equal(a.muestra(expresiones(0.5)), false);
    assert.equal(a.cantidad(), 0);

    s.permitido = true; s.estado = 'acepta'; s.desdeUtc = new Date(r.ahora()).toISOString();
    await r.avanzar(3_000);

    assert.equal(a.muestra(expresiones(0.5)), true);
    assert.equal(a.cantidad(), 1);
    a.detener();
});

test('analizar: si el paciente revoca, lo acumulado se descarta y no se suma más', async () => {
    const { r, s, a } = analisis();
    a.iniciar();
    await r.avanzar(1_000);
    for (let i = 0; i < 5; i++) a.muestra(expresiones(0.5));
    assert.equal(a.cantidad(), 5);

    s.permitido = false; s.estado = 'revocado'; s.revocadoUtc = new Date(r.ahora()).toISOString();
    await r.avanzar(3_000);

    assert.equal(a.cantidad(), 0);
    assert.equal(a.muestra(expresiones(0.5)), false);
    assert.equal(a.resumen(), null);
    a.detener();
});

test('analizar: rechazar, "no disponible" o una sesión que no es de esta persona también descartan', async () => {
    for (const caso of [
        { estado: 'rechaza' },
        { estado: 'no-disponible', motivo: 'menor-de-edad' },
        { estado: 'sin-decision' }
    ]) {
        const { r, s, a } = analisis();
        a.iniciar();
        await r.avanzar(1_000);
        a.muestra(expresiones(0.5)); a.muestra(expresiones(0.5));
        assert.equal(a.cantidad(), 2);

        s.permitido = false; s.estado = caso.estado; s.motivo = caso.motivo || null;
        await r.avanzar(3_000);

        assert.equal(a.cantidad(), 0, `con ${caso.estado} tenía que descartar`);
        a.detener();
    }

    const { r, s, a } = analisis();
    a.iniciar();
    await r.avanzar(1_000);
    a.muestra(expresiones(0.5));
    s.falla = { status: 404 };
    await r.avanzar(3_000);
    assert.equal(a.cantidad(), 0);
    a.detener();
});

test('analizar: una pausa por falta de conexión frena pero NO descarta, y se reanuda con la misma aceptación', async () => {
    const { r, s, a } = analisis();
    a.iniciar();
    await r.avanzar(1_000);
    a.muestra(expresiones(0.5)); a.muestra(expresiones(0.5)); a.muestra(expresiones(0.5));

    s.falla = 'red';
    await r.avanzar(12_000);
    assert.equal(a.permitido(), false);
    assert.equal(a.muestra(expresiones(0.5)), false);    // en pausa: no suma
    assert.equal(a.cantidad(), 3);                       // y no se perdió nada

    s.falla = null;
    await r.avanzar(3_500);
    assert.equal(a.permitido(), true);
    assert.equal(a.muestra(expresiones(0.5)), true);
    assert.equal(a.cantidad(), 4);
    a.detener();
});

test('analizar: una sesión de YoSoy vencida (401) también solo pausa: lo cargado en la sala no se pierde', async () => {
    const { r, s, a } = analisis();
    a.iniciar();
    await r.avanzar(1_000);
    a.muestra(expresiones(0.5)); a.muestra(expresiones(0.5));

    s.falla = { status: 401 };
    await r.avanzar(3_000);

    assert.equal(a.permitido(), false);
    assert.equal(a.cantidad(), 2);
    a.detener();
});

test('analizar: si revocó y volvió a aceptar, lo anterior no vale y arranca una ventana nueva', async () => {
    const { r, s, a } = analisis();
    a.iniciar();
    await r.avanzar(1_000);
    a.muestra(expresiones(0.5)); a.muestra(expresiones(0.5));

    // Se salta el "revocado" intermedio (no alcanzó a verse), pero la aceptación vigente es otra.
    s.desdeUtc = new Date(r.ahora() + 1000).toISOString();
    await r.avanzar(3_000);

    assert.equal(a.cantidad(), 0);
    assert.equal(a.muestra(expresiones(0.5)), true);
    assert.equal(a.cantidad(), 1);
    a.detener();
});

test('analizar.paraGuardar: con permiso fresco arma el pedido con la ventana y los promedios', async () => {
    const { r, s, a } = analisis();
    s.desdeUtc = new Date(r.ahora() - 1000).toISOString();
    a.iniciar();
    await r.avanzar(1_000);
    a.muestra(expresiones(0.6, 0.4));
    await r.avanzar(2_000);
    a.muestra(expresiones(0.8, 0.2));
    const primera = r.ahora() - 2000;
    const ultima = r.ahora();

    const g = plano(await a.paraGuardar());

    assert.equal(g.motivo, null);
    assert.deepEqual(g.emociones, {
        origen: 'turno', id: 12,
        desdeUtc: new Date(primera).toISOString(), hastaUtc: new Date(ultima).toISOString(),
        muestras: 2, promedios: { neutral: 70, happy: 0, sad: 30, angry: 0, fearful: 0, surprised: 0, disgusted: 0 }
    });
    a.detener();
});

test('analizar.paraGuardar: la ventana nunca empieza antes de la aceptación del servidor', async () => {
    const { r, s, a } = analisis();
    // Un corrimiento de relojes: para el servidor la aceptación es un poco DESPUÉS de la primera muestra que tomó la
    // pantalla (la hora del servidor, calibrada, tiene un error de unos cientos de ms). Se manda la de la aceptación.
    s.desdeUtc = new Date(r.ahora() + 1500).toISOString();
    a.iniciar();
    await r.avanzar(1_000);
    a.muestra(expresiones(0.5));       // t = 1 s, antes de la aceptación
    await r.avanzar(2_000);
    a.muestra(expresiones(0.5));       // t = 3 s

    const g = plano(await a.paraGuardar());

    assert.notEqual(g.emociones, null);
    assert.equal(g.emociones.desdeUtc, s.desdeUtc);
    assert.equal(g.emociones.hastaUtc, new Date(r.ahora()).toISOString());
    a.detener();
});

test('analizar.paraGuardar: consulta de nuevo y, si el paciente revocó, no manda nada', async () => {
    const { r, s, a } = analisis();
    a.iniciar();
    await r.avanzar(1_000);
    a.muestra(expresiones(0.5)); a.muestra(expresiones(0.5));
    await r.avanzar(2_000);
    // El profesional toca "Guardar" antes de que la consulta periódica se entere de la revocación.
    s.permitido = false; s.estado = 'revocado';

    const g = plano(await a.paraGuardar());

    assert.equal(g.emociones, null);
    assert.equal(g.motivo, 'revocado');
    assert.equal(a.cantidad(), 0);            // y lo acumulado se descartó
    a.detener();
});

test('analizar.paraGuardar: sin una confirmación fresca de la API no manda emociones', async () => {
    const { r, s, a } = analisis();
    a.iniciar();
    await r.avanzar(1_000);
    a.muestra(expresiones(0.5)); a.muestra(expresiones(0.5));

    s.falla = 'red';
    const g = plano(await a.paraGuardar());

    assert.equal(g.emociones, null);
    assert.equal(g.motivo, 'sin-confirmacion');
    assert.equal(a.cantidad(), 2);            // no se tira nada: se puede reintentar
    a.detener();
});

test('analizar.paraGuardar: con una sola muestra o sin muestras no hay qué guardar', async () => {
    const { r, a } = analisis();
    a.iniciar();
    await r.avanzar(1_000);

    assert.equal(plano(await a.paraGuardar()).motivo, 'sin-muestras');
    a.muestra(expresiones(0.5));
    assert.equal(plano(await a.paraGuardar()).motivo, 'sin-muestras');
    a.detener();
});

test('analizar: después de detenerse todavía puede guardar (el profesional escribe sus notas al terminar)', async () => {
    const { r, s, a } = analisis();
    s.desdeUtc = new Date(r.ahora() - 1000).toISOString();
    a.iniciar();
    await r.avanzar(1_000);
    a.muestra(expresiones(0.5));
    await r.avanzar(2_000);
    a.muestra(expresiones(0.5));

    a.detener();
    await r.avanzar(60_000);

    const g = plano(await a.paraGuardar());
    assert.notEqual(g.emociones, null);
    assert.equal(g.emociones.muestras, 2);
});

// ───────────────────────── Paciente ─────────────────────────

test('paciente: obtener, decidir y revocar llaman a la API con el método, la ruta y el cuerpo correctos', async () => {
    const respuestas = [{ disponible: true, estado: 'sin-decision', version: 'v0-provisorio', texto: ['a'] }, { estado: 'acepta' }, { estado: 'revocado' }];
    const { E, llamadas } = cargar({ api: () => respuestas.shift() });

    const texto = plano(await E.obtener(SESION));
    const acepta = plano(await E.decidir(SESION, 'acepta', 'v0-provisorio'));
    const revoca = plano(await E.revocar(SESION));

    assert.equal(texto.estado, 'sin-decision');
    assert.equal(acepta.estado, 'acepta');
    assert.equal(revoca.estado, 'revocado');
    assert.deepEqual(llamadas.map(l => l.ruta), ['/video/consentimiento?origen=turno&id=12', '/video/consentimiento', '/video/consentimiento/revocar']);
    assert.deepEqual(llamadas[1].opciones, { sinRedireccion: true, method: 'PUT', json: { origen: 'turno', id: 12, decision: 'acepta', version: 'v0-provisorio' } });
    assert.deepEqual(llamadas[2].opciones, { sinRedireccion: true, method: 'POST', json: { origen: 'turno', id: 12 } });
    assert.equal(llamadas[0].opciones.sinRedireccion, true);
});

test('paciente: los errores de la API se dicen en español y con el código', async () => {
    const casos = [
        [{ status: 409, datos: { codigo: 'version-desactualizada' } }, /texto del consentimiento cambió/],
        [{ status: 409, datos: { codigo: 'usar-revocar' } }, /usá el botón de revocar/],
        [{ status: 409, datos: { codigo: 'menor-de-edad', mensaje: 'La lectura de emociones no está disponible para esta persona.' } }, /no está disponible/],
        [{ status: 0, sinConexion: true }, /No se pudo conectar con el servidor/],
        [{ status: 404, datos: { codigo: 'no-existe' } }, /No encontramos esa sesión/],
        [{ status: 429 }, /demasiados intentos/],
        [{ status: 401 }, /sesión de YoSoy venció/]
    ];
    for (const [err, patron] of casos) {
        const { E } = cargar({ api: () => { throw Object.assign(new Error('x'), err); } });
        await assert.rejects(() => E.decidir(SESION, 'acepta', 'v'), e => {
            assert.match(e.message, patron);
            assert.equal(e.status, err.status);
            assert.equal(e.codigo, err.datos && err.datos.codigo);
            return true;
        });
    }
});

test('paciente: si está YS.video, los errores de la sala se dicen como al pedir el acceso', async () => {
    const video = { mensajeAcceso: (err, paraRol) => `del acceso (${err.status}, ${paraRol})` };
    const { E } = cargar({ video, api: () => { throw Object.assign(new Error('x'), { status: 409, datos: { codigo: 'ya-termino' } }); } });

    await assert.rejects(() => E.obtener(SESION), e => e.message === 'del acceso (409, paciente)');
});

// ───────────────────────── Textos ─────────────────────────

test('textoDeEstado: dice algo claro en español para cada estado y nunca está vacío', () => {
    const { E } = cargar();
    const estados = [
        { permitido: true, estado: 'acepta' },
        { permitido: false, estado: 'consultando' },
        { permitido: false, estado: 'sin-decision' },
        { permitido: false, estado: 'rechaza' },
        { permitido: false, estado: 'revocado' },
        { permitido: false, estado: 'sin-conexion' },
        { permitido: false, estado: 'sin-sesion' },
        { permitido: false, estado: 'sesion-vencida' },
        { permitido: false, estado: 'no-disponible', motivo: 'menor-de-edad' },
        { permitido: false, estado: 'no-disponible', motivo: 'edad-desconocida' },
        { permitido: false, estado: 'no-disponible', motivo: 'no-configurado' },
        { permitido: false, estado: 'no-disponible', motivo: 'otro' },
        { permitido: false, estado: 'algo-raro' },
        null
    ];
    const textos = estados.map(e => E.textoDeEstado(e));
    for (const t of textos) assert.ok(typeof t === 'string' && t.length > 20, `texto vacío o muy corto: ${t}`);
    // Cada situación distinta se dice distinto.
    assert.equal(new Set(textos.slice(0, 11)).size, 11);
    assert.match(E.textoDeEstado({ permitido: false, estado: 'revocado' }), /no se guarda nada/);
    assert.match(E.textoDeEstado({ permitido: false, estado: 'rechaza' }), /sigue con normalidad/);
});

test('textos: la tabla de etiquetas es la de siempre y no se puede cambiar desde afuera', () => {
    const { E } = cargar();
    assert.deepEqual(plano(E.ETIQUETAS), {
        neutral: 'Neutral', happy: 'Felicidad', sad: 'Tristeza', angry: 'Enojo', fearful: 'Miedo', surprised: 'Sorpresa', disgusted: 'Disgusto'
    });
    assert.equal(Object.isFrozen(E.ETIQUETAS), true);
});
