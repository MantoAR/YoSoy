// Pruebas de PANTALLAS/shared/video.js (la lógica de la sala en el navegador) contra el servidor de señalización REAL.
//
// video.js es un script de navegador: acá se carga en un contexto de vm con lo mínimo que espera (window, YS,
// location, navigator, io y RTCPeerConnection). El cliente de Socket.IO es el mismo (socket.io-client 4.8.3) que el
// vendorizado en PANTALLAS/shared/vendor; la conexión entre navegadores es un doble que anota lo que se le pide.
//
// Lo que estas pruebas NO cubren es WebRTC de verdad (cámara, micrófono, ICE, TURN): eso necesita navegadores.
'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { io } = require('socket.io-client');
const { tk, firmar, ahora, hasta, pausa, entorno, prueba } = require('./ayudas');

const CODIGO_VIDEO = fs.readFileSync(path.join(__dirname, '..', '..', 'PANTALLAS', 'shared', 'video.js'), 'utf8');
const SALA = 'SALA-1-A1B2C3';
const CANDIDATO = { candidate: 'candidate:1 1 udp 2122260223 192.168.0.5 54321 typ host', sdpMid: '0', sdpMLineIndex: 0, usernameFragment: 'abcd' };

/** Lo que devuelve video.js viene de otro "reino" de vm: se pasa por JSON para poder compararlo con deepEqual. */
const plano = x => JSON.parse(JSON.stringify(x));

// ── Dobles de lo que da el navegador ──

/** RTCPeerConnection de mentira: anota lo que se le pide y deja simular los estados de ICE. */
class ConexionFalsa {
    constructor(config) {
        this.config = config;
        this.signalingState = 'stable';
        this.connectionState = 'new';
        this.iceConnectionState = 'new';
        this.localDescription = null;
        this.remoteDescription = null;
        this.pistas = [];
        this.candidatos = [];
        this.opcionesDeOferta = [];
        this.cantidadRemotas = 0;
        this.cerrada = false;
    }
    addTrack(pista) { this.pistas.push(pista); }
    async createOffer(opciones = {}) {
        this.opcionesDeOferta.push(opciones);
        return { type: 'offer', sdp: `v=0\r\no=- 1 ${this.opcionesDeOferta.length} IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n` };
    }
    async createAnswer() { return { type: 'answer', sdp: 'v=0\r\no=- 2 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n' }; }
    async setLocalDescription(d) {
        this.localDescription = { type: d.type, sdp: d.sdp };
        this.signalingState = d.type === 'offer' ? 'have-local-offer' : 'stable';
    }
    async setRemoteDescription(d) {
        this.cantidadRemotas++;
        this.remoteDescription = { type: d.type, sdp: d.sdp };
        this.signalingState = d.type === 'offer' ? 'have-remote-offer' : 'stable';
    }
    async addIceCandidate(c) { this.candidatos.push(c); }
    getConfiguration() { return { iceServers: this.config.iceServers }; }
    setConfiguration(c) { this.config = c; }
    close() { this.cerrada = true; this.signalingState = 'closed'; this.connectionState = 'closed'; }
    // para las pruebas
    simularEstado(estado) { this.iceConnectionState = estado; this.connectionState = estado; if (this.oniceconnectionstatechange) this.oniceconnectionstatechange(); }
    simularTrack(flujo) { if (this.ontrack) this.ontrack({ streams: [flujo] }); }
}

const flujoLocal = () => ({ getTracks: () => [{ kind: 'audio' }, { kind: 'video' }] });

/**
 * Carga video.js como lo cargaría el navegador. Devuelve YS.video, lo que la página le mandó a la señalización
 * (emisiones), las conexiones que creó (pcs) y los sockets. param simula la URL de la página; api, a YS.api.
 */
function cargarVideo(t, url, { param = {}, api } = {}) {
    const emisiones = [];
    const sockets = [];
    const pcs = [];
    class Conexion extends ConexionFalsa { constructor(c) { super(c); pcs.push(this); } }
    const contexto = {
        console: { warn() {}, log() {}, error() {}, debug() {} },
        setTimeout, clearTimeout, URLSearchParams, Date, Promise,
        // Fuera de localhost la señalización es el origen de la página: así se apunta al servidor de la prueba.
        location: { protocol: 'https:', hostname: 'ys.test', origin: url, search: '' },
        navigator: { mediaDevices: {} },
        RTCPeerConnection: Conexion,
        io: (u, opciones) => {
            const socket = io(u, { transports: ['websocket'], forceNew: true, reconnectionDelay: 30, reconnectionDelayMax: 60, ...opciones });
            const emitir = socket.emit.bind(socket);
            socket.emit = (...args) => { emisiones.push(args); return emitir(...args); };
            sockets.push(socket);
            return socket;
        }
    };
    contexto.window = contexto;
    contexto.YS = {
        param: nombre => (param[nombre] === undefined ? null : param[nombre]),
        api: api || (async () => { throw new Error('YS.api no se esperaba'); }),
        mensajeHttp: status => `Error ${status}`
    };
    vm.createContext(contexto);
    vm.runInContext(CODIGO_VIDEO, contexto, { filename: 'video.js' });
    t.after(() => sockets.forEach(s => s.close()));
    return { video: contexto.YS.video, emisiones, sockets, pcs };
}

/** Respuesta de POST /video/acceso con un token de la señalización de la prueba. */
function accesoDe(rol, sub, { token, iceServers = [{ urls: ['stun:stun.prueba.test:3478'] }] } = {}) {
    return { token: token || tk(SALA, rol, sub), sala: SALA, rol, expiraUtc: new Date(Date.now() + 3600000).toISOString(), turnDisponible: false, iceServers };
}

/** Una persona entrando a la sala desde su página: lo que le avisa video.js queda anotado en eventos. */
function persona(t, e, rol, sub, { api, acceso } = {}) {
    const pagina = cargarVideo(t, e.url, { api });
    const eventos = { remotos: 0, conectado: 0, otroSeFue: [], avisos: [], recuperar: 0, fallos: [], terminada: [] };
    const datos = acceso || accesoDe(rol, sub);
    const control = pagina.video.entrar({
        sesion: { origen: 'turno', id: 1 }, acceso: datos, stream: flujoLocal(), paraRol: rol,
        alVideoRemoto: () => { eventos.remotos++; },
        alConectado: () => { eventos.conectado++; },
        alOtroSeFue: info => { eventos.otroSeFue.push(plano(info)); },
        alAviso: texto => { eventos.avisos.push(texto); },
        alRecuperar: () => { eventos.recuperar++; },
        alFallo: (texto, info) => { eventos.fallos.push({ texto, ...plano(info) }); },
        alLlamadaTerminada: info => { eventos.terminada.push(plano(info)); }
    });
    t.after(() => control && control.cerrar());
    return { ...pagina, eventos, control, acceso: datos };
}

const conexiones = e => e.logs.filter(l => l.evento === 'CONEXION').length;

// ───────────────────────── Qué sesión se pide ─────────────────────────

prueba('sesionDeCodigo saca el origen y el id de los códigos que genera la API, y de nada más', t => {
    const { video } = cargarVideo(t, 'http://x');
    const casos = [
        ['SALA-12-8DE5A3F3A1B2C3D', { origen: 'turno', id: 12 }],
        ['sala-12-8de5a3f3a1b2c3d', { origen: 'turno', id: 12 }],
        ['  ADM-5-380CA33090D6  ', { origen: 'admision', id: 5 }],
        ['SALA-999999999-ABC', { origen: 'turno', id: 999999999 }],
        ['SALA-0-ABC', null], ['SALA-12', null], ['SALA-12-', null], ['SALA-x-ABC', null], ['SALA--ABC', null],
        ['SALA-1234567890-ABC', null],            // más de 9 dígitos
        ['SALA-12-ABC DEF', null], ['SALA-12-ABC;X', null], ['OTRA-12-ABC', null], ['HOLA', null], ['', null], [null, null], [undefined, null]
    ];
    for (const [codigo, esperado] of casos) assert.deepEqual(plano(video.sesionDeCodigo(codigo)), esperado, `código ${JSON.stringify(codigo)}`);
});

prueba('sesionDeLaUrl: ?turno= y ?admision= mandan sobre ?sala=, y lo que no es un entero positivo no vale', t => {
    const de = param => plano(cargarVideo(t, 'http://x', { param }).video.sesionDeLaUrl());
    assert.deepEqual(de({ turno: '12' }), { origen: 'turno', id: 12 });
    assert.deepEqual(de({ admision: '5' }), { origen: 'admision', id: 5 });
    assert.deepEqual(de({ turno: '12', sala: 'ADM-5-AB' }), { origen: 'turno', id: 12 });
    assert.deepEqual(de({ sala: 'ADM-5-AB' }), { origen: 'admision', id: 5 });
    assert.deepEqual(de({ sala: 'SALA-7-AB' }), { origen: 'turno', id: 7 });
    for (const malo of ['abc', '-3', '0', '1.5', '', '1e3', '2147483648']) assert.equal(de({ turno: malo }), null, `?turno=${malo}`);
    assert.equal(de({}), null);
    assert.equal(de({ sala: 'cualquier cosa' }), null);
});

// ───────────────────────── Mensajes de la API ─────────────────────────

prueba('mensajeAcceso: un mensaje en español para cada respuesta de POST /video/acceso', t => {
    const { video } = cargarVideo(t, 'http://x');
    const dice = (err, rol) => video.mensajeAcceso(err, rol);
    const e409 = (codigo, extra = {}) => ({ status: 409, datos: { codigo, ...extra } });

    assert.match(dice({ status: 0, sinConexion: true }), /No se pudo conectar con el servidor/);
    assert.match(dice({ status: 400 }), /No se pudo identificar la sesión/);
    assert.match(dice({ status: 401 }), /Tu sesión de YoSoy venció/);
    assert.match(dice({ status: 404, datos: { codigo: 'no-existe' } }), /No encontramos esa sesión/);
    assert.match(dice(e409('estado-invalido', { mensaje: 'El turno fue cancelado.' })), /El turno fue cancelado\./);
    assert.match(dice(e409('estado-invalido')), /no está disponible para una videollamada/);
    assert.match(dice(e409('sala-invalida')), /problema del sistema/);
    assert.match(dice({ status: 429 }), /demasiados intentos/);
    assert.match(dice({ status: 503, datos: { codigo: 'video-no-disponible' } }), /no está disponible por el momento/);
    assert.match(dice({ status: 500 }), /El servidor tuvo un problema/);
    assert.match(dice({ status: 502 }), /El servidor tuvo un problema/);
    // "sala sin generar" se lo lee distinto una persona que otra
    assert.match(dice(e409('sala-no-generada'), 'paciente'), /profesional todavía no generó la sala/);
    assert.match(dice(e409('sala-no-generada'), 'profesional'), /Generala desde Inicio/);
    // ninguno revela cosas de la configuración
    for (const err of [{ status: 503 }, { status: 500 }, e409('sala-invalida')]) assert.doesNotMatch(dice(err), /secreto|Video__|Jwt|token/i);
});

prueba('mensajeAcceso: "todavía no abre" dice a qué hora abre y "ya terminó" cuándo cerró (en la hora de quien mira)', t => {
    const { video } = cargarVideo(t, 'http://x');
    const dos = n => String(n).padStart(2, '0');
    const hora = d => `${dos(d.getHours())}:${dos(d.getMinutes())}`;

    const hoy = new Date();
    const deHoy = video.mensajeAcceso({ status: 409, datos: { codigo: 'aun-no-abre', abreUtc: hoy.toISOString() } });
    assert.match(deHoy, /^Todavía no se puede entrar a la sala: se abre a las /);
    assert.ok(deHoy.includes(`a las ${hora(hoy)}`), deHoy);
    assert.doesNotMatch(deHoy, /se abre el /);

    const otroDia = new Date('2099-01-05T15:30:00Z');
    const deOtroDia = video.mensajeAcceso({ status: 409, datos: { codigo: 'aun-no-abre', abreUtc: otroDia.toISOString() } });
    assert.ok(deOtroDia.includes(`a las ${hora(otroDia)}`), deOtroDia);
    assert.ok(deOtroDia.includes(`el ${dos(otroDia.getDate())}/${dos(otroDia.getMonth() + 1)} a las `), deOtroDia);

    const cerro = new Date();
    assert.match(video.mensajeAcceso({ status: 409, datos: { codigo: 'ya-termino', cerroUtc: cerro.toISOString() } }),
        new RegExp(`^Esta sesión ya terminó \\(la sala cerró a las ${hora(cerro)}\\)\\.$`));
    // sin hora (o con una hora que no se entiende) el mensaje sigue siendo claro
    assert.match(video.mensajeAcceso({ status: 409, datos: { codigo: 'aun-no-abre' } }), /unos minutos antes del horario/);
    assert.equal(video.mensajeAcceso({ status: 409, datos: { codigo: 'ya-termino', cerroUtc: 'basura' } }), 'Esta sesión ya terminó.');
});

prueba('pedirAcceso: manda { origen, id } con el JWT de la sesión (YS.api) y sin redirigir al login', async t => {
    const llamadas = [];
    const respuesta = accesoDe('paciente', '20');
    const { video } = cargarVideo(t, 'http://x', { api: async (ruta, opciones) => { llamadas.push([ruta, opciones]); return respuesta; } });

    const acceso = await video.pedirAcceso({ origen: 'turno', id: 12 }, { paraRol: 'paciente' });

    assert.equal(acceso.rol, 'paciente');
    assert.deepEqual(plano(llamadas), [['/video/acceso', { method: 'POST', json: { origen: 'turno', id: 12 }, sinRedireccion: true }]]);
});

prueba('pedirAcceso: un error de la API sale con el texto en español y su status; una respuesta rara también falla', async t => {
    const fallo = Object.assign(new Error('x'), { status: 409, datos: { codigo: 'estado-invalido', mensaje: 'El turno fue cancelado.' } });
    const a = cargarVideo(t, 'http://x', { api: async () => { throw fallo; } });
    await assert.rejects(() => a.video.pedirAcceso({ origen: 'turno', id: 1 }), err => {
        assert.equal(err.message, 'El turno fue cancelado.');
        assert.equal(err.status, 409);
        assert.equal(err.codigo, 'estado-invalido');
        return true;
    });

    for (const raro of [null, {}, { token: 'x' }, { token: 'x', rol: 'paciente' }, { token: 'x', rol: 'paciente', iceServers: 'no' }]) {
        const b = cargarVideo(t, 'http://x', { api: async () => raro });
        await assert.rejects(() => b.video.pedirAcceso({ origen: 'turno', id: 1 }), err => /no es la esperada/.test(err.message), JSON.stringify(raro));
    }
});

// ───────────────────────── Entrar a la sala ─────────────────────────

prueba('entrar: el paciente ofrece y el profesional responde; la página no manda ni sala ni rol; los iceServers son los de la API', async t => {
    const e = await entorno(t);
    const pro = persona(t, e, 'profesional', '10');
    const pac = persona(t, e, 'paciente', '20');

    assert.ok(await hasta(() => pro.pcs[0].localDescription && pro.pcs[0].localDescription.type === 'answer'), 'el profesional respondió');
    assert.ok(await hasta(() => pac.pcs[0].remoteDescription), 'el paciente recibió la respuesta');
    assert.equal(pac.pcs[0].remoteDescription.type, 'answer');
    assert.equal(pro.pcs[0].remoteDescription.type, 'offer');

    // Lo que cada página le manda a la señalización: join-room vacío, ofertas solo del paciente y sin sala ni rol.
    assert.deepEqual(plano(pac.emisiones.filter(x => x[0] === 'join-room')), [['join-room']]);
    assert.deepEqual(plano(pro.emisiones.filter(x => x[0] === 'join-room')), [['join-room']]);
    assert.equal(pro.emisiones.filter(x => x[0] === 'offer').length, 0, 'el profesional no arma ofertas');
    for (const [evento, carga] of [...pac.emisiones, ...pro.emisiones].filter(x => ['offer', 'answer', 'ice-candidate'].includes(x[0])))
        assert.equal(Object.keys(carga).length, 1, `${evento} lleva solo su contenido (nada de roomId ni role)`);

    // Los servidores ICE son los que dio la API: la página no arma ninguno.
    assert.deepEqual(plano(pac.pcs[0].config.iceServers), plano(pac.acceso.iceServers));
    assert.deepEqual(plano(pro.pcs[0].config.iceServers), plano(pro.acceso.iceServers));
    assert.equal(pac.pcs[0].pistas.length, 2, 'las pistas locales se agregan una vez');

    // Los candidatos llegan al otro lado.
    pac.pcs[0].onicecandidate({ candidate: CANDIDATO });
    assert.ok(await hasta(() => pro.pcs[0].candidatos.length === 1), 'el candidato llegó');
    assert.equal(pro.pcs[0].candidatos[0].candidate, CANDIDATO.candidate);
});

prueba('entrar: ontrack llega una vez por pista (audio y video) y el video remoto se avisa UNA sola vez', async t => {
    const e = await entorno(t);
    const pro = persona(t, e, 'profesional', '10');
    const pac = persona(t, e, 'paciente', '20');
    await hasta(() => pac.pcs[0].remoteDescription);
    const flujo = {};

    pro.pcs[0].simularTrack(flujo);   // pista de audio
    pro.pcs[0].simularTrack(flujo);   // pista de video
    assert.equal(pro.eventos.remotos, 1);

    pro.pcs[0].simularTrack({});      // otro flujo en la misma conexión (otra persona): se avisa de nuevo
    assert.equal(pro.eventos.remotos, 2);

    pac.pcs[0].simularEstado('connected');
    assert.equal(pac.eventos.conectado, 1);
});

prueba('entrar: si la otra persona sale y vuelve, se arma una conexión nueva en los dos lados', async t => {
    const e = await entorno(t);
    const pro = persona(t, e, 'profesional', '10');
    const pac = persona(t, e, 'paciente', '20');
    assert.ok(await hasta(() => pro.pcs[0].localDescription && pac.pcs[0].remoteDescription));
    pac.pcs[0].simularEstado('connected');

    pro.control.cerrar();   // el profesional recarga la página
    assert.ok(await hasta(() => pac.eventos.otroSeFue.length === 1), 'el paciente se entera de que se fue');

    const proNuevo = persona(t, e, 'profesional', '10');   // y vuelve a entrar desde una página nueva
    assert.ok(await hasta(() => pac.pcs.length === 2), 'el paciente armó una conexión nueva');
    assert.ok(pac.pcs[0].cerrada, 'y cerró la vieja');
    assert.ok(await hasta(() => proNuevo.pcs[0].remoteDescription && proNuevo.pcs[0].remoteDescription.type === 'offer'), 'el profesional nuevo recibió la oferta');
    assert.ok(await hasta(() => pac.pcs[1].remoteDescription && pac.pcs[1].remoteDescription.type === 'answer'), 'y el paciente la respuesta');
    assert.deepEqual(pac.eventos.fallos, []);
});

prueba('entrar: ICE "failed" reinicia con una oferta nueva sobre la misma conexión; "disconnected" espera antes de hacerlo', async t => {
    const e = await entorno(t);
    const pro = persona(t, e, 'profesional', '10');
    const pac = persona(t, e, 'paciente', '20');
    assert.ok(await hasta(() => pac.pcs[0].remoteDescription));
    pac.pcs[0].simularEstado('connected');
    const pc = pac.pcs[0];
    assert.deepEqual(plano(pc.opcionesDeOferta), [{ iceRestart: false }]);

    // failed: reinicio inmediato
    pc.simularEstado('failed');
    assert.ok(await hasta(() => pc.opcionesDeOferta.length === 2), 'ofreció de nuevo');
    assert.deepEqual(plano(pc.opcionesDeOferta[1]), { iceRestart: true });
    assert.equal(pac.pcs.length, 1, 'sobre la misma conexión');
    assert.ok(pac.eventos.avisos.some(a => /Reintentando/.test(a)), 'avisa que reintenta');
    assert.ok(await hasta(() => pc.cantidadRemotas === 2 && pc.signalingState === 'stable'), 'el profesional respondió a la oferta de reinicio');

    // volvió: se avisa que está conectado otra vez
    pc.simularEstado('connected');
    assert.equal(pac.eventos.conectado, 2);

    // disconnected que sigue: no reinicia de inmediato, sí pasada la gracia
    pc.simularEstado('disconnected');
    await pausa(1500);
    assert.equal(pc.opcionesDeOferta.length, 2, 'todavía no reinició');
    assert.ok(await hasta(() => pc.opcionesDeOferta.length === 3, 5000), 'reinició pasada la gracia');
    assert.deepEqual(plano(pc.opcionesDeOferta[2]), { iceRestart: true });
    pc.simularEstado('connected');

    // disconnected que se arregla solo: no reinicia
    pc.simularEstado('disconnected');
    await pausa(800);
    pc.simularEstado('connected');
    await pausa(5000);
    assert.equal(pc.opcionesDeOferta.length, 3, 'un bache que se recupera solo no reinicia nada');
});

prueba('entrar: quien no ofrece (el profesional) solo avisa ante un corte de ICE y no arma ofertas por su cuenta', async t => {
    const e = await entorno(t);
    const pro = persona(t, e, 'profesional', '10');
    const pac = persona(t, e, 'paciente', '20');
    assert.ok(await hasta(() => pro.pcs[0].localDescription && pac.pcs[0].remoteDescription));
    pro.pcs[0].simularEstado('connected');

    pro.pcs[0].simularEstado('failed');
    await pausa(400);

    assert.ok(pro.eventos.avisos.some(a => /Reintentando/.test(a)));
    assert.equal(pro.emisiones.filter(x => x[0] === 'offer').length, 0);
    assert.equal(pac.pcs[0].opcionesDeOferta.length, 1, 'el paciente tampoco recibió nada que lo haga ofrecer de nuevo');
});

prueba('entrar: con la otra persona fuera de la sala no se reinicia ICE (no hay a quién reconectar)', async t => {
    const e = await entorno(t);
    const pro = persona(t, e, 'profesional', '10');
    const pac = persona(t, e, 'paciente', '20');
    assert.ok(await hasta(() => pac.pcs[0].remoteDescription));
    pac.pcs[0].simularEstado('connected');
    pro.control.cerrar();
    assert.ok(await hasta(() => pac.eventos.otroSeFue.length === 1));

    pac.pcs[0].simularEstado('failed');
    await pausa(400);

    assert.equal(pac.pcs[0].opcionesDeOferta.length, 1, 'no armó ninguna oferta de reinicio');
    assert.deepEqual(pac.eventos.avisos, [], 'tampoco avisa de reintentos: no hay a quién reconectar (y mucho menos da la sala por perdida)');
    assert.deepEqual(pac.eventos.fallos, []);
});

// ───────────────────────── Token vencido ─────────────────────────

prueba('entrar: si el token vence con la persona adentro, pide un acceso nuevo UNA vez y vuelve a entrar', async t => {
    const e = await entorno(t);
    const pedidos = [];
    const iceNuevo = [{ urls: ['turn:turn.prueba.test:3478'], username: 'u', credential: 'c' }];
    const api = async (ruta, opciones) => { pedidos.push([ruta, opciones.json]); return accesoDe('profesional', '10', { iceServers: iceNuevo }); };
    const pro = persona(t, e, 'profesional', '10', { api, acceso: accesoDe('profesional', '10', { token: tk(SALA, 'profesional', '10', { expiraEnSeg: 2 }) }) });
    assert.ok(await hasta(() => conexiones(e) === 1), 'entró');

    assert.ok(await hasta(() => conexiones(e) === 2, 8000), 'volvió a entrar con el token nuevo');

    assert.deepEqual(plano(pedidos), [['/video/acceso', { origen: 'turno', id: 1 }]], 'un solo pedido de acceso');
    assert.deepEqual(pro.eventos.fallos, []);
    assert.deepEqual(plano(pro.pcs[0].config.iceServers), plano(iceNuevo), 'las credenciales TURN nuevas quedan en la conexión');
});

prueba('entrar: un token que ya llegó vencido también se renueva una vez antes de entrar', async t => {
    const e = await entorno(t);
    let pedidos = 0;
    const api = async () => { pedidos++; return accesoDe('paciente', '20'); };
    const vencido = firmar({ sala: SALA, rol: 'paciente', sub: '20', exp: ahora() - 10 });

    const pac = persona(t, e, 'paciente', '20', { api, acceso: accesoDe('paciente', '20', { token: vencido }) });

    assert.ok(await hasta(() => conexiones(e) === 1), 'entró con el token nuevo');
    assert.equal(pedidos, 1);
    assert.deepEqual(pac.eventos.fallos, []);
});

prueba('entrar: si la API ya no deja entrar al renovar (la sesión terminó), lo dice y corta; con la llamada andando no la tira', async t => {
    const e = await entorno(t);
    const cerroUtc = new Date(Date.now() - 60000).toISOString();
    const api = async () => { throw Object.assign(new Error('x'), { status: 409, datos: { codigo: 'ya-termino', cerroUtc } }); };
    const corto = () => accesoDe('profesional', '10', { token: tk(SALA, 'profesional', '10', { expiraEnSeg: 2 }) });

    // sin llamada andando: queda todo cerrado
    const sin = persona(t, e, 'profesional', '10', { api, acceso: corto() });
    assert.ok(await hasta(() => sin.eventos.fallos.length === 1, 8000), 'avisó que ya terminó');
    assert.match(sin.eventos.fallos[0].texto, /^Esta sesión ya terminó \(la sala cerró a las /);
    assert.equal(sin.eventos.fallos[0].llamadaActiva, false);
    assert.ok(sin.pcs[0].cerrada, 'la conexión quedó cerrada');
    await pausa(300);
    assert.equal(sin.eventos.fallos.length, 1, 'se avisa una sola vez');
    assert.equal(conexiones(e), 1, 'no vuelve a conectarse en bucle');

    // con la llamada andando: se avisa pero la conexión entre navegadores sigue
    const con = persona(t, e, 'profesional', '11', { api, acceso: accesoDe('profesional', '11', { token: tk('SALA-2-ZZ9', 'profesional', '11', { expiraEnSeg: 2 }) }) });
    assert.ok(await hasta(() => con.pcs.length === 1 && conexiones(e) === 2));
    con.pcs[0].simularEstado('connected');
    assert.ok(await hasta(() => con.eventos.fallos.length === 1, 8000));
    assert.equal(con.eventos.fallos[0].llamadaActiva, true);
    assert.equal(con.pcs[0].cerrada, false, 'la llamada sigue');
});

prueba('entrar: un token que la señalización no acepta se renueva una vez y, si sigue sin aceptarlo, dice que no hay acceso', async t => {
    const e = await entorno(t);
    let pedidos = 0;
    const malo = () => firmar({ sala: SALA, rol: 'paciente', sub: '20', exp: ahora() + 300 }, {}, 'otro-secreto-que-no-es-el-del-servidor-0123456789');
    const api = async () => { pedidos++; return accesoDe('paciente', '20', { token: malo() }); };

    const pac = persona(t, e, 'paciente', '20', { api, acceso: accesoDe('paciente', '20', { token: malo() }) });

    assert.ok(await hasta(() => pac.eventos.fallos.length === 1, 5000));
    assert.equal(pac.eventos.fallos[0].texto, 'No tenés acceso a esta sala.');
    assert.equal(pedidos, 1, 'se pidió un acceso nuevo una sola vez');
    assert.equal(conexiones(e), 0);
    assert.ok(pac.pcs[0].cerrada);
});

// ───────────────────────── Rechazos de la señalización ─────────────────────────

prueba('entrar: dos profesionales distintos en la misma sala: al segundo se le dice claro y queda cerrado', async t => {
    const e = await entorno(t);
    const uno = persona(t, e, 'profesional', '10');
    assert.ok(await hasta(() => uno.pcs.length === 1 && e.servidor.cantidadSalas() === 1));

    const dos = persona(t, e, 'profesional', '11');

    assert.ok(await hasta(() => dos.eventos.fallos.length === 1));
    assert.match(dos.eventos.fallos[0].texto, /Ya hay otra persona conectada en esta sala como profesional/);
    assert.match(dos.eventos.fallos[0].texto, /otra pestaña, otro navegador o otro equipo/);
    assert.equal(dos.eventos.fallos[0].llamadaActiva, false);
    assert.ok(dos.pcs[0].cerrada);
    assert.deepEqual(uno.eventos.fallos, [], 'el que ya estaba adentro sigue adentro');
});

prueba('entrar: el mismo usuario desde otro lugar reemplaza al anterior, y el anterior no sigue la llamada en paralelo', async t => {
    const e = await entorno(t);
    const viejo = persona(t, e, 'profesional', '10');
    assert.ok(await hasta(() => e.servidor.cantidadSalas() === 1));
    viejo.pcs[0].simularEstado('connected');   // aunque su llamada esté andando

    const nuevo = persona(t, e, 'profesional', '10');

    assert.ok(await hasta(() => viejo.eventos.fallos.length === 1));
    assert.match(viejo.eventos.fallos[0].texto, /Entraste a esta sala desde otro lugar/);
    assert.equal(viejo.eventos.fallos[0].llamadaActiva, false, 'se cierra aunque estuviera conectado');
    assert.ok(viejo.pcs[0].cerrada);
    assert.deepEqual(nuevo.eventos.fallos, []);
});

prueba('entrar: una sala que ya tiene a sus dos participantes rechaza a un tercero con un mensaje claro', async t => {
    const e = await entorno(t);
    persona(t, e, 'profesional', '10');
    persona(t, e, 'paciente', '20');
    assert.ok(await hasta(() => conexiones(e) === 2));

    const tercero = persona(t, e, 'profesional', '12');

    assert.ok(await hasta(() => tercero.eventos.fallos.length === 1));
    assert.match(tercero.eventos.fallos[0].texto, /ya tiene a sus dos participantes/);
});

prueba('entrar: sin el cliente de Socket.IO o sin WebRTC avisa en vez de quedarse esperando', t => {
    for (const [falta, mensaje] of [['io', /No se pudo cargar el componente de videollamada/], ['RTCPeerConnection', /no permite videollamadas/]]) {
        const contexto = {
            console: { warn() {} }, setTimeout, clearTimeout, URLSearchParams, Date, Promise,
            location: { protocol: 'https:', hostname: 'ys.test', origin: 'http://x', search: '' }, navigator: {},
            io: () => { throw new Error('no se esperaba abrir un socket'); }, RTCPeerConnection: ConexionFalsa
        };
        delete contexto[falta];
        contexto.window = contexto;
        contexto.YS = { param: () => null, api: async () => ({}), mensajeHttp: s => `Error ${s}` };
        vm.createContext(contexto);
        vm.runInContext(CODIGO_VIDEO, contexto);
        const fallos = [];

        const control = contexto.YS.video.entrar({ sesion: { origen: 'turno', id: 1 }, acceso: accesoDe('paciente', '20'), stream: flujoLocal(),
            alFallo: (texto, info) => fallos.push({ texto, ...plano(info) }) });

        assert.equal(control, null, `sin ${falta}`);
        assert.equal(fallos.length, 1, `sin ${falta}`);
        assert.match(fallos[0].texto, mensaje);
        assert.equal(fallos[0].llamadaActiva, false);
    }
});

// ───────────────────────── Salir y Finalizar ─────────────────────────

prueba('Finalizar: terminar() le avisa a la otra persona quién la terminó, y esa persona NO lo toma por una salida', async t => {
    const e = await entorno(t);
    const pro = persona(t, e, 'profesional', '10');
    const pac = persona(t, e, 'paciente', '20');
    assert.ok(await hasta(() => pro.pcs[0].localDescription && pac.pcs[0].remoteDescription));

    pac.control.terminar();   // el paciente toca «Finalizar»
    assert.ok(await hasta(() => pro.eventos.terminada.length === 1), 'el profesional se entera de que la llamada terminó');
    assert.deepEqual(pro.eventos.terminada, [{ rol: 'paciente' }]);
    assert.equal(pac.eventos.terminada.length, 0, 'quien finaliza no recibe su propio aviso');
    assert.ok(await hasta(() => pac.pcs[0].cerrada), 'quien finaliza corta su conexión');
});

prueba('Salir: cerrar() NO avisa que la llamada terminó (solo que la persona se fue) y se puede volver a entrar', async t => {
    const e = await entorno(t);
    const pro = persona(t, e, 'profesional', '10');
    const pac = persona(t, e, 'paciente', '20');
    assert.ok(await hasta(() => pro.pcs[0].localDescription && pac.pcs[0].remoteDescription));

    pac.control.cerrar();   // el paciente toca «Salir»
    assert.ok(await hasta(() => pro.eventos.otroSeFue.length === 1), 'el profesional ve que se fue');
    await pausa(300);
    assert.equal(pro.eventos.terminada.length, 0, 'la llamada no se da por terminada');

    const pacNuevo = persona(t, e, 'paciente', '20');   // vuelve a entrar con la sesión todavía abierta
    assert.ok(await hasta(() => pacNuevo.pcs[0] && pacNuevo.pcs[0].remoteDescription && pacNuevo.pcs[0].remoteDescription.type === 'answer'), 'la conexión se arma de nuevo');
    assert.deepEqual(pacNuevo.eventos.fallos, []);
});

prueba('Finalizar con el profesional: el aviso también llega al paciente', async t => {
    const e = await entorno(t);
    const pro = persona(t, e, 'profesional', '10');
    const pac = persona(t, e, 'paciente', '20');
    assert.ok(await hasta(() => pro.pcs[0].localDescription && pac.pcs[0].remoteDescription));
    pro.control.terminar();
    assert.ok(await hasta(() => pac.eventos.terminada.length === 1));
    assert.deepEqual(pac.eventos.terminada, [{ rol: 'profesional' }]);
});

// ── La escena de la sala (video del otro, miniatura propia «Vos», esperando, sin cámara) ──

/** Un DOM mínimo: lo justo para que YS.video.escena arme y cambie sus clases. */
function domFalso() {
    const elemento = (etiqueta = 'div') => {
        const clases = new Set();
        const e = {
            etiqueta, hijos: [], textContent: '', atributos: {}, srcObject: null, oyentes: {},
            classList: { add: c => clases.add(c), remove: c => clases.delete(c), contains: c => clases.has(c) },
            set className(v) { clases.clear(); String(v).split(/\s+/).filter(Boolean).forEach(c => clases.add(c)); },
            get className() { return [...clases].join(' '); },
            append(...ns) { e.hijos.push(...ns); }, appendChild(n) { e.hijos.push(n); return n; },
            setAttribute(k, v) { e.atributos[k] = v; }
        };
        return e;
    };
    return { elemento, documento: { createElement: elemento } };
}

function pista(estado = {}) {
    const oyentes = {};
    return { readyState: 'live', enabled: true, muted: false, ...estado, oyentes, addEventListener(ev, fn) { (oyentes[ev] ||= []).push(fn); } };
}
const conPistas = (...video) => ({ getVideoTracks: () => video });

prueba('estadoDeCamara: activa solo con una pista de video viva y encendida', t => {
    const { video } = cargarVideo(t, 'http://127.0.0.1:1');
    assert.equal(video.estadoDeCamara(conPistas(pista())), 'activa');
    assert.equal(video.estadoDeCamara(conPistas(pista({ enabled: false }))), 'sin-camara');
    assert.equal(video.estadoDeCamara(conPistas(pista({ readyState: 'ended' }))), 'sin-camara');
    assert.equal(video.estadoDeCamara(conPistas()), 'sin-camara');
    assert.equal(video.estadoDeCamara(null), 'sin-camara');
});

prueba('clasesDeEscena: esperando al otro o conectada, con o sin cámara propia', t => {
    const { video } = cargarVideo(t, 'http://127.0.0.1:1');
    assert.deepEqual(plano(video.clasesDeEscena({ otroPresente: false, camara: 'activa' })), ['sala-marco', 'sala--esperando', 'sala--con-camara']);
    assert.deepEqual(plano(video.clasesDeEscena({ otroPresente: true, camara: 'sin-camara' })), ['sala-marco', 'sala--conectada', 'sala--sin-camara']);
});

prueba('la escena arranca esperando, pasa a miniatura al conectarse el otro y muestra «Sin cámara» si la cámara se apaga', t => {
    const { video } = cargarVideo(t, 'http://127.0.0.1:1');
    const { elemento, documento } = domFalso();
    const contexto = vm.createContext({ document: documento });   // el DOM de la escena es el de la página
    const pagina = cargarVideo(t, 'http://127.0.0.1:1');
    void video; void contexto; void pagina;
    // video.js ya está cargado en su propio contexto: se vuelve a cargar con un document falso para que escena() lo use.
    const ctx = { console: { warn() {} }, setTimeout, clearTimeout, URLSearchParams, Date, Promise, document: documento, location: { protocol: 'https:', hostname: 'ys.test', origin: 'http://127.0.0.1:1', search: '' }, navigator: { mediaDevices: {} }, RTCPeerConnection: class {}, io: () => ({}) };
    ctx.window = ctx; ctx.YS = { param: () => null, api: async () => ({}), mensajeHttp: s => String(s) };
    vm.createContext(ctx);
    vm.runInContext(CODIGO_VIDEO, ctx);

    const marco = elemento(), principal = elemento('video'), propio = elemento('video');
    const escena = ctx.YS.video.escena({ marco, principal, propio, textoEspera: 'Esperando al paciente…' });

    assert.ok(marco.classList.contains('sala--esperando'));           // el otro todavía no está: estado claro, no caja negra
    assert.ok(principal.classList.contains('sala-principal'));
    const espera = marco.hijos[0];
    assert.equal(espera.hijos[0].textContent, 'Esperando al paciente…');
    const miniatura = marco.hijos[1];
    assert.equal(miniatura.hijos[2].textContent, 'Vos');                 // etiqueta de la miniatura
    assert.equal(miniatura.hijos[1].hijos[1].textContent, 'Sin cámara');  // y el estado sin cámara ya está armado

    const p = pista();
    const flujo = conPistas(p);
    escena.usarStream(flujo);
    assert.equal(propio.srcObject, flujo);
    assert.ok(marco.classList.contains('sala--con-camara'));

    escena.otroConectado(true);                                         // llega el otro: la propia pasa a miniatura
    assert.ok(marco.classList.contains('sala--conectada'));
    assert.ok(!marco.classList.contains('sala--esperando'));

    p.readyState = 'ended';                                             // la cámara se desconecta a mitad de la llamada
    p.oyentes.ended.forEach(fn => fn());
    assert.ok(marco.classList.contains('sala--sin-camara'));
    assert.ok(!marco.classList.contains('sala--con-camara'));

    escena.otroConectado(false);                                        // el otro se fue: vuelve a esperar
    assert.ok(marco.classList.contains('sala--esperando'));
});

prueba('sala.css: la propia es una miniatura 4:3 espejada abajo a la derecha, nunca una tira, y la espera no es una caja negra', t => {
    const css = fs.readFileSync(path.join(__dirname, '..', '..', 'PANTALLAS', 'shared', 'sala.css'), 'utf8');
    assert.match(css, /\.sala-propio\s*\{[^}]*right:\s*12px;[^}]*bottom:\s*12px/s);
    assert.match(css, /\.sala-propio\s*\{[^}]*width:\s*min\(200px/s);
    assert.match(css, /\.sala-propio\s*\{[^}]*aspect-ratio:\s*4\s*\/\s*3/s);
    assert.match(css, /\.sala-propio-video\s*\{[^}]*object-fit:\s*cover;[^}]*scaleX\(-1\)/s);
    assert.match(css, /\.sala--esperando \.sala-espera\s*\{\s*display:\s*flex/);
    assert.match(css, /\.sala--sin-camara \.sala-sin-camara\s*\{\s*display:\s*flex/);
    assert.doesNotMatch(css, /height:\s*90px/);                      // la tira de 90 px del profesional
    for (const pagina of ['Pantalla_Profesional', 'Pantalla_Paciente']) {
        const html = fs.readFileSync(path.join(__dirname, '..', '..', 'PANTALLAS', pagina, 'sesion_video.html'), 'utf8');
        assert.match(html, /shared\/sala\.css/);
        assert.match(html, /YS\.video\.escena\(/);                   // las dos salas usan el mismo componente
        assert.doesNotMatch(html, /class="mini-video"/);
    }
});
