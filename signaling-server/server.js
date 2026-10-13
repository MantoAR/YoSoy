// Servidor de señalización WebRTC de las sesiones de video de YoSoy.
//
// Reenvía offer / answer / candidatos ICE entre las dos personas de una sala (un profesional y un paciente) y
// nada más. No guarda estado, no usa la base y no llama a la API: quién puede entrar a qué sala lo decide la API
// al emitir un token firmado (contrato en README.md). La sala y el rol salen SOLO del token; lo que el cliente
// diga de sí mismo se ignora. Escucha en loopback; el único camino desde internet es Caddy.
'use strict';

const http = require('http');
const net = require('net');
const { Server } = require('socket.io');
const { leerConfig } = require('./config');
const { verificarToken } = require('./token');
const { crearLimitador, crearContador } = require('./limitador');

const MAX_SDP = 30000;        // caracteres; un SDP real de audio y video ronda los 5 mil
const MAX_CANDIDATO = 2048;
const MAX_TIMER_MS = 2 ** 31 - 1;   // setTimeout no admite más: con un número mayor dispara enseguida
const EVENTOS_DEL_CLIENTE = new Set(['join-room', 'offer', 'answer', 'ice-candidate']);
const OTRO_ROL = { profesional: 'paciente', paciente: 'profesional' };

// Lo que ve el usuario cuando el servidor lo saca de la sala (evento "rechazado").
const MOTIVOS = {
    'sala-llena': () => 'La sala ya está completa.',
    'rol-ocupado': rol => `Ya hay otra persona conectada como ${rol} en esta sala.`,
    'reemplazado': () => 'Esta sesión se abrió en otro lugar.',
    'sesion-vencida': () => 'La sesión terminó.',
    'limite-eventos': () => 'Se enviaron demasiados mensajes. Volvé a entrar a la sala.'
};

function sinPrefijoV4(ip) {
    return typeof ip === 'string' && ip.startsWith('::ffff:') ? ip.slice(7) : ip;
}

function esLoopback(ip) {
    return typeof ip === 'string' && (ip === '::1' || ip.startsWith('127.'));
}

/**
 * IP real del cliente.
 *
 * Detrás de Caddy la conexión TCP siempre viene de 127.0.0.1, así que la IP real es la que Caddy anota en
 * X-Forwarded-For. Ese header SOLO se cree si la conexión viene de loopback, o sea, de Caddy en esta misma
 * máquina: el servidor escucha en 127.0.0.1, nadie de afuera le habla directo y por eso nadie puede falsificarlo.
 * Si alguien lo pusiera a escuchar en otra interfaz, las conexiones directas se limitan por su IP de origen y el
 * header se ignora. De la lista se toma la ÚLTIMA entrada, la que agregó el proxy: lo que el cliente haya puesto
 * antes queda a la izquierda y no cuenta.
 */
function ipReal(req) {
    const origen = sinPrefijoV4(req.socket && req.socket.remoteAddress) || 'desconocida';
    if (!esLoopback(origen)) return origen;
    const xff = req.headers && req.headers['x-forwarded-for'];
    if (typeof xff === 'string') {
        const ultima = sinPrefijoV4(xff.split(',').pop().trim());
        if (net.isIP(ultima)) return ultima;
    }
    return origen;
}

/** Copia saneada de una descripción de sesión (offer / answer), o null si no sirve. */
function descripcion(d, tipo) {
    if (!d || typeof d !== 'object' || d.type !== tipo) return null;
    if (typeof d.sdp !== 'string' || d.sdp.length === 0 || d.sdp.length > MAX_SDP) return null;
    return { type: tipo, sdp: d.sdp };
}

/** Copia saneada de un candidato ICE (los cuatro campos de RTCIceCandidateInit), o null si no sirve. */
function candidato(c) {
    if (!c || typeof c !== 'object' || typeof c.candidate !== 'string' || c.candidate.length > MAX_CANDIDATO) return null;
    const r = { candidate: c.candidate };
    if (c.sdpMid === null || (typeof c.sdpMid === 'string' && c.sdpMid.length <= 64)) r.sdpMid = c.sdpMid;
    if (c.sdpMLineIndex === null || (Number.isInteger(c.sdpMLineIndex) && c.sdpMLineIndex >= 0 && c.sdpMLineIndex < 256)) {
        r.sdpMLineIndex = c.sdpMLineIndex;
    }
    if (c.usernameFragment === null || (typeof c.usernameFragment === 'string' && c.usernameFragment.length <= 256)) {
        r.usernameFragment = c.usernameFragment;
    }
    return r;
}

// Nunca recibe tokens ni secretos: solo ip, rol, sub, sala (todo validado) y motivos fijos.
function logPorDefecto(evento, campos = {}) {
    const pares = Object.entries(campos)
        .map(([k, v]) => `${k}=${String(v).replace(/\s/g, '_').replace(/[^\x21-\x7E]/g, '?')}`)
        .join(' ');
    console.log(`${new Date().toISOString()} ${evento}${pares ? ' ' + pares : ''}`);
}

function crearServidor(config, { log = logPorDefecto } = {}) {
    // sala -> { profesional: socket | null, paciente: socket | null }. Un asiento por rol: máximo dos personas.
    const salas = new Map();
    const limiteHandshakes = crearLimitador({ ventanaMs: 60000 });

    const httpServer = http.createServer((req, res) => {
        if (req.url === '/health') {
            res.writeHead(200, { 'Content-Type': 'text/plain' });
            res.end('ok');
            return;
        }
        res.writeHead(404);
        res.end();
    });

    const io = new Server(httpServer, {
        serveClient: false,                        // el cliente va vendorizado en PANTALLAS/shared/vendor
        maxHttpBufferSize: config.maxPayloadBytes, // tope por mensaje: ws corta la conexión si lo pasan
        connectTimeout: 10000,                     // tiempo máximo para completar el handshake de Socket.IO
        // Sin lista no se manda ningún header CORS: las páginas llegan por el mismo origen (Caddy).
        cors: config.origenesCors.length ? { origin: config.origenesCors, methods: ['GET', 'POST'] } : undefined,
        // Se llama una vez por conexión nueva, antes de crear nada. Si pasa del límite, el cliente ve un error de
        // transporte y reintenta con espera.
        allowRequest: (req, callback) => {
            const ip = ipReal(req);
            const n = limiteHandshakes.consumir(ip);
            if (n > config.maxHandshakesPorMin) {
                if (n === config.maxHandshakesPorMin + 1) log('RECHAZO', { motivo: 'limite-conexiones', ip });
                return callback('demasiadas conexiones', false);
            }
            callback(null, true);
        }
    });

    // ── Autenticación: el token viaja en socket.handshake.auth.token (no en la URL, que queda en logs) ──
    io.use((socket, next) => {
        const ip = ipReal(socket.request);
        const auth = socket.handshake.auth;
        try {
            const t = verificarToken(auth && auth.token, config.secreto);
            Object.assign(socket.data, t, { ip, unido: false });
            next();
        } catch (e) {
            const codigo = (e && e.codigo) || 'token-invalido';
            log('RECHAZO', { motivo: codigo, ip });
            const vencido = codigo === 'token-vencido';
            const error = new Error(vencido ? 'la sesión ya terminó' : 'no autorizado');
            error.data = { codigo: vencido ? 'token-vencido' : 'no-autorizado' };
            next(error);
        }
    });

    // ── Salas ──
    function contraparte(socket) {
        const { sala: codigo, rol } = socket.data;
        const sala = salas.get(codigo);
        if (!sala || sala[rol] !== socket) return null;
        return sala[OTRO_ROL[rol]];
    }

    /** Le avisa por qué se lo saca ("rechazado") y corta. Sirve para el rechazo de un join y para el resto. */
    function echar(socket, codigo) {
        const d = socket.data;
        if (d.cerrando) return;
        d.cerrando = true;
        d.motivoSalida = codigo;
        log('RECHAZO', { motivo: codigo, ip: d.ip, rol: d.rol, sub: d.sub, sala: d.sala });
        socket.emit('rechazado', { motivo: MOTIVOS[codigo](d.rol), codigo });
        socket.disconnect(true);
    }

    // Sin awaits entre mirar los asientos y ocuparlos: dos uniones simultáneas no pueden ganar las dos.
    function unir(socket) {
        const d = socket.data;
        if (d.unido || d.cerrando) return;
        let sala = salas.get(d.sala);
        if (!sala) {
            sala = { profesional: null, paciente: null };
            salas.set(d.sala, sala);
        }
        const previo = sala[d.rol];
        if (previo && previo !== socket) {
            if (previo.data.sub !== d.sub) {
                return echar(socket, sala[OTRO_ROL[d.rol]] ? 'sala-llena' : 'rol-ocupado');
            }
            // El mismo usuario volviendo (se le cayó la red, recargó la página): su conexión anterior puede seguir
            // "viva" hasta 45 s por el ping timeout. La nueva la reemplaza y el otro participante no ve una salida.
            previo.data.reemplazado = true;
            sala[d.rol] = null;
            echar(previo, 'reemplazado');
        }
        sala[d.rol] = socket;
        d.unido = true;
        socket.emit('joined', { rol: d.rol });
        const par = sala[OTRO_ROL[d.rol]];
        if (par) par.emit('user-joined', { rol: d.rol });
    }

    function salir(socket, razon) {
        const d = socket.data;
        clearTimeout(d.timerExp);
        let par = null;
        const sala = salas.get(d.sala);
        if (d.unido && sala && sala[d.rol] === socket) {
            sala[d.rol] = null;
            par = sala[OTRO_ROL[d.rol]];
            if (!sala.profesional && !sala.paciente) salas.delete(d.sala);
        }
        if (par && !d.reemplazado) {
            par.emit('user-left', d.motivoSalida === 'sesion-vencida' ? { rol: d.rol, motivo: 'sesion-vencida' } : { rol: d.rol });
        }
        log('DESCONEXION', { ip: d.ip, rol: d.rol, sub: d.sub, sala: d.sala, razon });
    }

    // Solo se reenvía a la contraparte de la sala del token, y solo si este socket tiene un join aceptado.
    function reenviar(socket, evento, carga) {
        if (!socket.data.unido) return;
        const par = contraparte(socket);
        if (par) par.emit(evento, carga);
    }

    // El token vence aunque la conexión siga abierta: se corta sin consultar a nadie.
    function vigilarVencimiento(socket) {
        const d = socket.data;
        const falta = d.exp * 1000 - Date.now();
        if (falta <= 0) return echar(socket, 'sesion-vencida');
        d.timerExp = setTimeout(() => vigilarVencimiento(socket), Math.min(falta, MAX_TIMER_MS));
        d.timerExp.unref();
    }

    io.on('connection', socket => {
        const d = socket.data;
        log('CONEXION', { ip: d.ip, rol: d.rol, sub: d.sub, sala: d.sala });
        vigilarVencimiento(socket);

        // Un error inesperado en un handler no tiene que tirar el proceso.
        const seguro = fn => (...args) => {
            try { fn(...args); } catch (e) { log('ERROR', { detalle: e && e.name }); }
        };
        const dentroDelLimite = crearContador({ ventanaMs: 10000, max: config.maxEventosPor10s });

        socket.on('error', () => {});   // sin esto Socket.IO imprime el stack de cualquier error de middleware

        // Pasa por acá cada evento que manda el cliente, sea cual sea.
        socket.use((paquete, next) => {
            if (!dentroDelLimite()) return echar(socket, 'limite-eventos');
            if (!EVENTOS_DEL_CLIENTE.has(paquete[0])) return;   // evento que no existe en el protocolo: se descarta
            next();
        });

        // join-room no lee nada de lo que manda el cliente (ni roomId ni role): todo sale del token.
        socket.on('join-room', seguro(() => unir(socket)));
        socket.on('offer', seguro(msg => {
            const offer = descripcion(msg && msg.offer, 'offer');
            if (offer) reenviar(socket, 'offer', { offer });
        }));
        socket.on('answer', seguro(msg => {
            const answer = descripcion(msg && msg.answer, 'answer');
            if (answer) reenviar(socket, 'answer', { answer });
        }));
        socket.on('ice-candidate', seguro(msg => {
            const c = candidato(msg && msg.candidate);
            if (c) reenviar(socket, 'ice-candidate', { candidate: c });
        }));
        socket.on('disconnect', seguro(razon => salir(socket, razon)));
    });

    return {
        io,
        httpServer,
        iniciar() {
            return new Promise((resolve, reject) => {
                httpServer.once('error', reject);
                httpServer.listen(config.puerto, config.host, () => {
                    httpServer.off('error', reject);
                    resolve();
                });
            });
        },
        get puerto() { return httpServer.address().port; },
        get host() { return httpServer.address().address; },
        cantidadSalas() { return salas.size; },
        detener() {
            limiteHandshakes.detener();
            return new Promise(resolve => io.close(() => resolve()));
        }
    };
}

/** Arranque del proceso: lee la configuración (si es inválida, no arranca), escucha y atiende SIGTERM / SIGINT. */
function main(env = process.env) {
    let config;
    try {
        config = leerConfig(env);
    } catch (e) {
        console.error(`No se puede arrancar el servidor de señalización: ${e.message}`);
        process.exit(1);
    }
    const servidor = crearServidor(config);
    servidor.iniciar().then(() => {
        logPorDefecto('ARRANQUE', {
            escuchando: `${servidor.host}:${servidor.puerto}`,
            cors: config.origenesCors.length ? config.origenesCors.join(',') : 'ninguno',
            maxHandshakesPorMin: config.maxHandshakesPorMin,
            maxEventosPor10s: config.maxEventosPor10s,
            maxPayloadBytes: config.maxPayloadBytes
        });
        if (!esLoopback(servidor.host)) {
            logPorDefecto('ATENCION', { detalle: 'HOST-no-es-loopback: este-servicio-no-se-expone-sin-proxy' });
        }
    }).catch(e => {
        console.error(`No se pudo abrir ${config.host}:${config.puerto}: ${e.code || e.message}`);
        process.exit(1);
    });
    // systemd para el servicio con SIGTERM: se cierran las conexiones y se sale con 0.
    for (const senal of ['SIGTERM', 'SIGINT']) {
        process.on(senal, () => {
            logPorDefecto('PARADA', { senal });
            servidor.detener().then(() => process.exit(0));
        });
    }
    return servidor;
}

if (require.main === module) main();

module.exports = { crearServidor, ipReal, main };
