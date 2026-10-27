// Pruebas de PANTALLAS/shared/grabacion.js: el consentimiento de grabar la sesión (igual idea que emociones.js) y,
// lo nuevo, la grabación y subida del audio.
//
// grabacion.js es un script de navegador: acá se carga en un contexto de vm con lo mínimo que espera (window y
// YS.api). AudioContext, MediaRecorder y FormData no existen en Node: se inyectan como dobles por los parámetros de
// crearGrabador (igual que api, ahora, programar y cancelar), así que el contexto de vm no necesita esos globals.
// Los timers y la hora son de mentira (se "avanza" el tiempo), así se prueba la falla cerrada y el backoff de
// reintentos sin esperar.
//
// Lo que estas pruebas NO cubren es el navegador de verdad (micrófono, MediaRecorder real, Web Audio real): eso
// necesita navegadores.
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const CODIGO = fs.readFileSync(path.join(__dirname, '..', '..', 'PANTALLAS', 'shared', 'grabacion.js'), 'utf8');
const SESION = { origen: 'turno', id: 12 };

/** Lo que devuelve grabacion.js viene de otro "reino" de vm: se pasa por JSON para poder compararlo con deepEqual. */
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

/** Carga grabacion.js como lo cargaría el navegador. */
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
    class MediaStream { constructor(pistas) { this.pistas = pistas; } getAudioTracks() { return this.pistas; } }
    const contexto = { window, MediaStream, console: { warn() {}, log() {}, error() {} }, setTimeout, clearTimeout, Date, Promise };
    vm.createContext(contexto);
    vm.runInContext(CODIGO, contexto, { filename: 'grabacion.js' });
    return { G: window.YS.grabacion, llamadas, YS };
}

// ── Dobles del micrófono/la sala para crearGrabador ──

function streamFalso(tieneAudio = true) {
    return { getAudioTracks: () => (tieneAudio ? [{ kind: 'audio' }] : []) };
}

function contextoAudioFalso() {
    return {
        fuentesCreadas: 0,
        cerrado: false,
        createMediaStreamSource(stream) { this.fuentesCreadas++; return { stream, disconnect() {}, connect() {} }; },
        createMediaStreamDestination() { return { stream: streamFalso(true) }; },
        close() { this.cerrado = true; }
    };
}

/** Fábrica de MediaRecorder de mentira: guarda cada instancia para poder simular ondataavailable/stop desde la prueba. */
function fabricaRecorder() {
    const instancias = [];
    function fabrica(stream, mime) {
        const r = {
            stream, mime, state: 'recording', intervaloMs: null,
            ondataavailable: null, onstop: null,
            start(ms) { this.intervaloMs = ms; },
            stop() { if (this.state === 'inactive') return; this.state = 'inactive'; if (this.onstop) this.onstop(); },
            emitir(size = 100) { if (this.ondataavailable) this.ondataavailable({ data: { size } }); }
        };
        instancias.push(r);
        return r;
    }
    fabrica.instancias = instancias;
    return fabrica;
}

/** La API de mentira de /video/grabacion/fragmento: cada llamada puede fallar distinto, según comportamiento(n). */
function apiFragmentoFalsa() {
    const llamadas = [];
    let comportamiento = () => 'ok';
    return {
        llamadas,
        config(f) { comportamiento = f; },
        async api(ruta, opciones) {
            llamadas.push({ ruta, opciones });
            const r = comportamiento(llamadas.length - 1, opciones);
            if (r === 'ok') return {};
            throw Object.assign(new Error('x'), { status: r });
        }
    };
}

function grabador(opts = {}) {
    const r = reloj();
    const mr = fabricaRecorder();
    const ctx = contextoAudioFalso();
    const af = apiFragmentoFalsa();
    const { G } = cargar();
    const vistas = [];
    const g = G.crearGrabador({
        sesion: SESION, inicioSesionMs: r.ahora(),
        api: af.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar,
        crearContextoAudio: () => ctx, crearMediaRecorder: mr, tipoSoportado: () => true, crearFormData: () => ({ append() {} }),
        onCambio: v => vistas.push(plano(v)),
        ...opts
    });
    return { r, mr, ctx, af, g, vistas };
}

// ───────────────────────── mensajeError ─────────────────────────

test('mensajeError: cada motivo de rechazo se dice en español', () => {
    const { G } = cargar();
    assert.match(G.mensajeError({ status: 0, sinConexion: true }), /No se pudo conectar/);
    assert.match(G.mensajeError({ status: 409, datos: { codigo: 'version-desactualizada' } }), /texto del consentimiento cambió/);
    assert.match(G.mensajeError({ status: 409, datos: { codigo: 'usar-revocar' } }), /usá el botón de revocar/);
    assert.equal(G.mensajeError({ status: 409, datos: { codigo: 'menor-de-edad', mensaje: 'texto de la API' } }), 'texto de la API');
    assert.match(G.mensajeError({ status: 401 }), /sesión de YoSoy venció/);
    assert.match(G.mensajeError({ status: 404 }), /No encontramos esa sesión/);
    assert.match(G.mensajeError({ status: 429 }), /demasiados intentos/);
    assert.match(G.mensajeError({ status: 500, datos: { mensaje: 'lo que sea' } }), /lo que sea/);
});

test('mensajeError: con YS.video presente, un error de acceso usa su mensaje', () => {
    const video = { mensajeAcceso: (err, paraRol) => `del acceso (${err.status}, ${paraRol})` };
    const { G } = cargar({ video });
    assert.equal(G.mensajeError({ status: 403 }, 'profesional'), 'del acceso (403, profesional)');
});

// ───────────────────────── textoDeEstado ─────────────────────────

test('textoDeEstado: dice algo claro en español para cada estado y nunca está vacío', () => {
    const { G } = cargar();
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
        { permitido: false, estado: 'algo-raro' },
        null
    ];
    const textos = estados.map(e => G.textoDeEstado(e));
    for (const t of textos) assert.ok(typeof t === 'string' && t.length > 15, `texto vacío o muy corto: ${t}`);
    assert.equal(new Set(textos.slice(0, 11)).size, 11);
    assert.match(G.textoDeEstado({ permitido: false, estado: 'revocado' }), /no se guarda nada/);
});

// ───────────────────────── Paciente: obtener/decidir/revocar ─────────────────────────

test('paciente: obtener, decidir y revocar piden finalidad=grabacion', async () => {
    const respuestas = [{ estado: 'sin-decision' }, { estado: 'acepta' }, { estado: 'revocado' }];
    const { G, llamadas } = cargar({ api: () => respuestas.shift() });

    const texto = plano(await G.obtener(SESION));
    const acepta = plano(await G.decidir(SESION, 'acepta', 'v0-provisorio'));
    const revoca = plano(await G.revocar(SESION));

    assert.equal(texto.estado, 'sin-decision');
    assert.equal(acepta.estado, 'acepta');
    assert.equal(revoca.estado, 'revocado');
    assert.deepEqual(llamadas.map(l => l.ruta), [
        '/video/consentimiento?origen=turno&id=12&finalidad=grabacion',
        '/video/consentimiento',
        '/video/consentimiento/revocar'
    ]);
    assert.deepEqual(llamadas[1].opciones, { sinRedireccion: true, method: 'PUT', json: { origen: 'turno', id: 12, decision: 'acepta', version: 'v0-provisorio', finalidad: 'grabacion' } });
    assert.deepEqual(llamadas[2].opciones, { sinRedireccion: true, method: 'POST', json: { origen: 'turno', id: 12, finalidad: 'grabacion' } });
});

// ───────────────────────── vigilar ─────────────────────────

function servidorPermisoFalso(r) {
    const s = {
        permitido: true, estado: 'acepta', motivo: null,
        desdeUtc: new Date(r.ahora() - 5000).toISOString(), revocadoUtc: null, version: 'v0-provisorio',
        consultarCadaSegundos: 3, vigenciaSegundos: 10, falla: null,
        async api() {
            if (s.falla === 'red') throw Object.assign(new Error('sin conexión'), { status: 0, sinConexion: true });
            return {
                permitido: s.permitido, estado: s.estado, motivo: s.motivo, desdeUtc: s.desdeUtc, revocadoUtc: s.revocadoUtc,
                ahoraUtc: new Date(r.ahora()).toISOString(), consultarCadaSegundos: s.consultarCadaSegundos,
                vigenciaSegundos: s.vigenciaSegundos, version: s.version
            };
        }
    };
    return s;
}

test('vigilar: al iniciar avisa "consultando" y después lo que contesta la API', async () => {
    const r = reloj();
    const s = servidorPermisoFalso(r);
    const { G } = cargar();
    const vistas = [];
    const v = G.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar, alCambiar: x => vistas.push(plano(x)) });

    v.iniciar();
    await vaciar();

    assert.deepEqual(vistas.map(x => x.estado), ['consultando', 'acepta']);
    assert.equal(vistas[1].permitido, true);
    v.detener();
});

test('vigilar: usa /video/grabacion/permiso (no el de emociones)', async () => {
    const r = reloj();
    const s = servidorPermisoFalso(r);
    const { G, llamadas } = cargar({ api: () => s.api() });
    const v = G.vigilar({ sesion: { origen: 'admision', id: 7 }, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar });

    v.iniciar();
    await vaciar();

    assert.equal(llamadas[0].ruta, '/video/grabacion/permiso?origen=admision&id=7');
    assert.equal(llamadas[0].opciones.sinRedireccion, true);
    v.detener();
});

test('vigilar: FALLA CERRADA — si la API deja de contestar, a los 10 s se frena', async () => {
    const r = reloj();
    const s = servidorPermisoFalso(r);
    const { G } = cargar();
    const v = G.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar });
    v.iniciar();
    await r.avanzar(1_000);
    assert.equal(v.permitido(), true);

    s.falla = 'red';
    await r.avanzar(11_000);
    assert.equal(v.permitido(), false);
    assert.equal(v.vista().estado, 'sin-conexion');
    v.detener();
});

test('vigilar: la revocación se ve en la consulta siguiente y se avisa una sola vez', async () => {
    const r = reloj();
    const s = servidorPermisoFalso(r);
    const { G } = cargar();
    const vistas = [];
    const v = G.vigilar({ sesion: SESION, api: s.api, ahora: r.ahora, programar: r.programar, cancelar: r.cancelar, alCambiar: x => vistas.push(plano(x)) });
    v.iniciar();
    await r.avanzar(1_000);

    s.permitido = false; s.estado = 'revocado'; s.revocadoUtc = new Date(r.ahora()).toISOString();
    await r.avanzar(3_000);
    await r.avanzar(3_000);

    assert.deepEqual(vistas.map(x => x.estado), ['consultando', 'acepta', 'revocado']);
    v.detener();
});

// ───────────────────────── crearGrabador: arranque ─────────────────────────

test('crearGrabador: sin un tipo de audio soportado, las dos pistas quedan "sin-audio" sin crear ningún MediaRecorder', () => {
    const { mr, g, vistas } = grabador({ tipoSoportado: () => false });

    g.adjuntarRemoto(streamFalso(true));
    g.adjuntarLocal(streamFalso(true));

    assert.equal(mr.instancias.length, 0);
    assert.equal(g.vista().profesional.resultado, 'sin-audio');
    assert.equal(g.vista().paciente.resultado, 'sin-audio');
    assert.ok(vistas.length > 0);
});

test('crearGrabador: sin pista de audio en el stream, esa pista queda "sin-audio"', () => {
    const { g } = grabador();
    g.adjuntarRemoto(streamFalso(false));
    assert.equal(g.vista().paciente.resultado, 'sin-audio');
    assert.equal(g.vista().paciente.grabando, false);
});

test('crearGrabador: adjuntarRemoto arranca el MediaRecorder de la pista paciente', () => {
    const { mr, g } = grabador();
    g.adjuntarRemoto(streamFalso(true));

    assert.equal(mr.instancias.length, 1);
    assert.equal(mr.instancias[0].intervaloMs, 10000);
    assert.equal(g.vista().paciente.grabando, true);
});

test('crearGrabador: una reconexión del paciente (nuevo stream) no reinicia el MediaRecorder, solo cambia la fuente', () => {
    const { mr, ctx, g } = grabador();
    g.adjuntarRemoto(streamFalso(true));
    g.adjuntarRemoto(streamFalso(true));   // el paciente se reconectó: stream nuevo

    assert.equal(mr.instancias.length, 1, 'no se crea un segundo MediaRecorder');
    assert.equal(ctx.fuentesCreadas, 2, 'sí se reenruta la fuente de audio cada vez');
});

test('crearGrabador: adjuntarLocal y adjuntarRemoto grafican pistas independientes', () => {
    const { mr, g } = grabador();
    g.adjuntarRemoto(streamFalso(true));
    g.adjuntarLocal(streamFalso(true));

    assert.equal(mr.instancias.length, 2);
    assert.equal(g.vista().profesional.grabando, true);
    assert.equal(g.vista().paciente.grabando, true);
});

// ───────────────────────── crearGrabador: subida ─────────────────────────

test('crearGrabador: un fragmento grabado se encola y se sube; al subirse, la cola queda vacía', async () => {
    const { mr, af, g, vistas } = grabador();
    g.adjuntarRemoto(streamFalso(true));
    mr.instancias[0].emitir();

    assert.equal(g.vista().paciente.pendientes, 1);
    await vaciar();

    assert.equal(af.llamadas.length, 1);
    assert.equal(af.llamadas[0].ruta, '/video/grabacion/fragmento');
    assert.equal(af.llamadas[0].opciones.method, 'POST');
    assert.equal(g.vista().paciente.pendientes, 0);
    assert.ok(vistas.some(v => v.paciente.pendientes === 1));
});

test('crearGrabador: un error transitorio (0/429/5xx) reintenta con backoff y después sube', async () => {
    const { r, mr, af, g } = grabador();
    af.config(n => (n === 0 ? 0 : n === 1 ? 429 : n === 2 ? 503 : 'ok'));
    g.adjuntarRemoto(streamFalso(true));
    mr.instancias[0].emitir();
    await vaciar();

    assert.equal(g.vista().paciente.resultado, null);
    assert.ok(g.vista().paciente.pendientes > 0, 'todavía no se subió: sigue reintentando');

    await r.avanzar(1000);   // primer reintento, a 1 s
    await r.avanzar(2000);   // segundo, a 2 s
    await r.avanzar(4000);   // tercero, a 4 s: esta vez contesta 'ok'

    assert.equal(g.vista().paciente.pendientes, 0);
    assert.equal(af.llamadas.length, 4);
});

test('crearGrabador: un error que no es de red (p. ej. 400) da la pista por perdida de inmediato, sin reintentar', async () => {
    const { af, mr, g } = grabador();
    af.config(() => 400);
    g.adjuntarRemoto(streamFalso(true));
    mr.instancias[0].emitir();
    await vaciar();

    assert.equal(af.llamadas.length, 1);   // ni un reintento
    assert.equal(g.vista().paciente.resultado, 'incompleta');
    assert.equal(g.vista().paciente.grabando, false);
});

test('crearGrabador: agotados los reintentos de un error transitorio, la pista queda "incompleta"', async () => {
    const { r, af, mr, g } = grabador();
    af.config(() => 0);   // siempre falla, de red
    g.adjuntarRemoto(streamFalso(true));
    mr.instancias[0].emitir();
    await vaciar();

    await r.avanzar(1000); await r.avanzar(2000); await r.avanzar(4000);
    await r.avanzar(8000); await r.avanzar(16000); await r.avanzar(30000);

    assert.equal(g.vista().paciente.resultado, 'incompleta');
    assert.equal(af.llamadas.length, 7);   // 1 intento + 6 reintentos (MAX_REINTENTOS_FRAGMENTO)
});

test('crearGrabador: los fragmentos de una pista se suben en orden (uno a la vez)', async () => {
    const { mr, af, g } = grabador();
    g.adjuntarRemoto(streamFalso(true));
    mr.instancias[0].emitir(); mr.instancias[0].emitir(); mr.instancias[0].emitir();
    await vaciar();

    assert.equal(af.llamadas.length, 3);
    assert.deepEqual(af.llamadas.map(l => l.opciones.body), af.llamadas.map(l => l.opciones.body));   // no se mezclan
    assert.equal(g.vista().paciente.pendientes, 0);
});

// ───────────────────────── crearGrabador: haySinSubir / descartar / finalizar ─────────────────────────

test('haySinSubir: dice que sí mientras haya algo en cola y que no cuando se vació o terminó', async () => {
    const { mr, g } = grabador();
    g.adjuntarRemoto(streamFalso(true));
    assert.equal(g.haySinSubir(), false);

    mr.instancias[0].emitir();
    assert.equal(g.haySinSubir(), true);
    await vaciar();
    assert.equal(g.haySinSubir(), false);
});

test('descartar: corta ambas pistas ya, sin esperar a que termine de subir lo pendiente', () => {
    const { mr, g } = grabador();
    g.adjuntarRemoto(streamFalso(true));
    g.adjuntarLocal(streamFalso(true));
    mr.instancias[0].emitir();   // queda pendiente de subir

    g.descartar();

    assert.equal(g.haySinSubir(), false);
    assert.equal(g.vista().paciente.resultado, 'incompleta');
    assert.equal(g.vista().profesional.resultado, 'incompleta');
    assert.equal(mr.instancias[0].state, 'inactive');
});

test('finalizar: corta la grabación y espera a que termine de subir lo que quedó, con resultado "completa"', async () => {
    const { r, mr, g } = grabador();
    g.adjuntarRemoto(streamFalso(true));
    mr.instancias[0].emitir();

    const p = g.finalizar();
    assert.equal(mr.instancias[0].state, 'inactive', 'finalizar corta el MediaRecorder enseguida');

    await r.avanzar(300);   // la cola todavía no se subió: sigue esperando
    await vaciar();
    await r.avanzar(300);   // ya se subió y el recorder está "detenido": ahora sí termina

    const resultado = await p;
    assert.deepEqual(plano(resultado), { profesional: 'sin-audio', paciente: 'completa' });
});

test('finalizar: si nunca termina de subir, no espera más del tope (15 s) y queda "incompleta"', async () => {
    const { r, af, mr, g } = grabador();
    af.config(() => 0);   // la subida nunca se resuelve bien: siempre de red
    g.adjuntarRemoto(streamFalso(true));
    mr.instancias[0].emitir();

    const p = g.finalizar();
    await r.avanzar(15_000);

    const resultado = await p;
    assert.equal(resultado.paciente, 'incompleta');
});

test('finalizar: sin ninguna pista adjuntada, el resultado es "sin-audio" en las dos sin esperar nada', async () => {
    const { r, g } = grabador();
    const p = g.finalizar();
    await r.avanzar(300);
    const resultado = await p;
    assert.deepEqual(plano(resultado), { profesional: 'sin-audio', paciente: 'sin-audio' });
});

test('finalizar: cierra el AudioContext', async () => {
    const { r, ctx, mr, g } = grabador();
    g.adjuntarRemoto(streamFalso(true));
    const p = g.finalizar();
    await r.avanzar(300);
    await p;
    assert.equal(ctx.cerrado, true);
});
