const { createServer } = require('http');
const { Server } = require('socket.io');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

// ─── Carga de .env (sin dependencias) ────────────────────────────────────────
(function cargarEnv() {
    const archivo = path.join(__dirname, '.env');
    if (!fs.existsSync(archivo)) return;
    for (const linea of fs.readFileSync(archivo, 'utf8').split('\n')) {
        const limpia = linea.trim();
        if (!limpia || limpia.startsWith('#')) continue;
        const i = limpia.indexOf('=');
        if (i === -1) continue;
        const clave = limpia.slice(0, i).trim();
        let valor = limpia.slice(i + 1).trim();
        if ((valor.startsWith('"') && valor.endsWith('"')) || (valor.startsWith("'") && valor.endsWith("'")))
            valor = valor.slice(1, -1);
        if (!(clave in process.env)) process.env[clave] = valor;
    }
})();

const PORT = parseInt(process.env.PORT || '3001', 10);
const JWT_SECRET = process.env.JWT_SECRET || 'CAMBIAR-secreto-compartido-con-el-signaling-minimo-32-bytes';
const BACKEND_URL = process.env.BACKEND_URL || 'http://localhost:5000';
const INTERNAL_SECRET = process.env.INTERNAL_SECRET || '';
const MODO_LAN = (process.env.MODO_LAN || 'true').toLowerCase() === 'true';
const CORS_ORIGINS_RAW = process.env.CORS_ORIGINS || 'http://localhost:5000,http://localhost:5500';
const CORS_ANY = CORS_ORIGINS_RAW.trim() === '*';
const CORS_ORIGINS = CORS_ANY
    ? '*'
    : CORS_ORIGINS_RAW.split(',').map(o => o.trim()).filter(Boolean);

// ─── JWT HS256: verificación manual con algoritmo FIJADO ──────────────────────
function b64urlDecode(str) {
    let s = String(str).replace(/-/g, '+').replace(/_/g, '/');
    while (s.length % 4) s += '=';
    return Buffer.from(s, 'base64');
}

function verificarJwt(token) {
    if (typeof token !== 'string') return null;
    const partes = token.split('.');
    if (partes.length !== 3) return null;
    const [h, p, s] = partes;

    let header;
    try { header = JSON.parse(b64urlDecode(h).toString('utf8')); } catch { return null; }
    // Anti algorithm-confusion: SOLO HS256. Rechaza alg:none y cualquier otro.
    if (!header || header.alg !== 'HS256' || header.typ !== 'JWT') return null;

    const esperado = crypto.createHmac('sha256', JWT_SECRET).update(`${h}.${p}`).digest();
    const recibido = b64urlDecode(s);
    if (esperado.length !== recibido.length) return null;
    if (!crypto.timingSafeEqual(esperado, recibido)) return null; // tiempo constante

    let payload;
    try { payload = JSON.parse(b64urlDecode(p).toString('utf8')); } catch { return null; }
    if (!payload) return null;
    if (payload.exp && Math.floor(Date.now() / 1000) > payload.exp) return null;
    return payload;
}

// ─── Validación / sanitización de payloads ────────────────────────────────────
const esRoomIdValido = (r) => typeof r === 'string' && /^[A-F0-9]{32}$/i.test(r);

function descripcionValida(d) {
    return d && typeof d === 'object'
        && (d.type === 'offer' || d.type === 'answer')
        && typeof d.sdp === 'string' && d.sdp.length > 0 && d.sdp.length < 100000;
}

function candidatoValido(c) {
    if (!c || typeof c !== 'object') return false;
    try { return JSON.stringify(c).length < 8000; } catch { return false; }
}

// ─── Estado de la sesión en la DB (verdad de la fuente) ───────────────────────
async function sesionActivaEnDb(idSesion) {
    try {
        const headers = INTERNAL_SECRET ? { 'X-Internal-Secret': INTERNAL_SECRET } : {};
        const resp = await fetch(`${BACKEND_URL}/sesiones/${idSesion}/estado`, { headers });
        if (!resp.ok) return MODO_LAN ? null : false;
        const data = await resp.json();
        return data.activa === true;
    } catch {
        // Backend inalcanzable: en LAN seguimos (demo); online rechazamos.
        return MODO_LAN ? null : false;
    }
}

// ─── Rate limiting simple por IP ──────────────────────────────────────────────
// In-memory: no sobrevive reinicios ni escala a multi-instancia. Se revisa en cloud.
function crearLimitador(ventanaMs, maximo) {
    const intentos = new Map(); // ip -> [timestamps]
    setInterval(() => {
        const ahora = Date.now();
        for (const [ip, lista] of intentos) {
            const vivos = lista.filter(t => ahora - t < ventanaMs);
            if (vivos.length) intentos.set(ip, vivos); else intentos.delete(ip);
        }
    }, ventanaMs).unref();
    return (ip) => {
        const ahora = Date.now();
        const lista = (intentos.get(ip) || []).filter(t => ahora - t < ventanaMs);
        lista.push(ahora);
        intentos.set(ip, lista);
        return lista.length > maximo;
    };
}

// Conexiones (handshake): control anti-flood de corto plazo.
const limitado = crearLimitador(10000, 15);
// join-room: máximo 5 por 5 minutos por IP (configurable por env).
const JOIN_VENTANA_MS = parseInt(process.env.JOIN_VENTANA_MS || '300000', 10);
const JOIN_MAX = parseInt(process.env.JOIN_MAX || '30', 10);
const limitadoJoin = crearLimitador(JOIN_VENTANA_MS, JOIN_MAX);

// ─── HTTP + Socket.IO ─────────────────────────────────────────────────────────
const httpServer = createServer((req, res) => {
    if (req.url === '/health') {
        res.writeHead(200, { 'Content-Type': 'text/plain' });
        res.end('ok');
        return;
    }
    res.writeHead(404);
    res.end();
});

const io = new Server(httpServer, {
    cors: { origin: CORS_ORIGINS, methods: ['GET', 'POST'] }
});

// Autenticación en el handshake: sin JWT válido => no se conecta.
io.use((socket, next) => {
    const ip = socket.handshake.address;
    if (limitado(ip)) return next(new Error('demasiados intentos'));

    const token = socket.handshake.auth && socket.handshake.auth.token;
    const payload = verificarJwt(token);
    if (!payload || !payload.roomId || !payload.idSesion || !payload.rol) {
        return next(new Error('no autorizado'));
    }
    socket.data.roomId = payload.roomId;   // sala autoritativa: la del token
    socket.data.idSesion = payload.idSesion;
    socket.data.rol = payload.rol;
    socket.data.idUsuario = payload.idUsuario;
    next();
});

const log = (...a) => console.log(new Date().toISOString(), ...a);

io.on('connection', (socket) => {
    const roomId = socket.data.roomId;
    const quien = `${socket.data.rol}/sesion=${socket.data.idSesion}/room=${String(roomId).slice(0, 8)}`;
    log('CONNECT', quien, socket.id.slice(0, 6));

    socket.on('join-room', async ({ roomId: pedido } = {}) => {
        if (limitadoJoin(socket.handshake.address)) {
            log('JOIN rechazado: rate-limit', quien);
            socket.emit('rechazado', { motivo: 'Demasiados intentos. Esperá unos minutos.' });
            return socket.disconnect(true);
        }

        // El cliente no elige sala: si manda una distinta a la del token, se rechaza.
        if (pedido !== undefined && pedido !== roomId) {
            log('JOIN rechazado: sala distinta', quien, 'pedido=', String(pedido).slice(0, 8));
            socket.emit('rechazado', { motivo: 'La sala no coincide con tu acceso.' });
            return socket.disconnect(true);
        }

        const activa = await sesionActivaEnDb(socket.data.idSesion);
        if (activa === false) {
            log('JOIN rechazado: sesion no activa', quien);
            socket.emit('rechazado', { motivo: 'La sesión no está activa.' });
            return socket.disconnect(true);
        }

        // Reconexión: desalojar un socket previo del MISMO rol (fantasma que aún no
        // expiró por ping-timeout) para que el mismo participante pueda volver a
        // entrar sin chocar con "sala completa". Se marca 'reemplazado' para que su
        // 'disconnect' NO le avise 'user-left' al otro peer (no es una salida real).
        const sala = io.sockets.adapter.rooms.get(roomId);
        if (sala) {
            for (const sid of [...sala]) {
                if (sid === socket.id) continue;
                const previo = io.sockets.sockets.get(sid);
                if (previo && previo.data.rol === socket.data.rol) {
                    previo.data.reemplazado = true;
                    previo.disconnect(true);
                }
            }
        }

        // Máximo 2 participantes por sala (tras desalojar el duplicado del mismo rol).
        const salaTras = io.sockets.adapter.rooms.get(roomId);
        if (salaTras && salaTras.size >= 2) {
            log('JOIN rechazado: sala completa', quien, 'size=', salaTras.size);
            socket.emit('rechazado', { motivo: 'La sala ya está completa.' });
            return socket.disconnect(true);
        }

        socket.join(roomId);
        const total = io.sockets.adapter.rooms.get(roomId)?.size ?? 1;
        log('JOIN ok', quien, '-> participantes en sala:', total);
        socket.emit('joined', { roomId, rol: socket.data.rol });
        socket.to(roomId).emit('user-joined', { rol: socket.data.rol });
    });

    socket.on('offer', ({ offer } = {}) => {
        if (!descripcionValida(offer)) { log('OFFER inválida descartada', quien); return; }
        log('OFFER relay', quien);
        socket.to(roomId).emit('offer', { offer });
    });

    socket.on('answer', ({ answer } = {}) => {
        if (!descripcionValida(answer)) { log('ANSWER inválida descartada', quien); return; }
        log('ANSWER relay', quien);
        socket.to(roomId).emit('answer', { answer });
    });

    socket.on('ice-candidate', ({ candidate } = {}) => {
        if (!candidatoValido(candidate)) return;
        socket.to(roomId).emit('ice-candidate', { candidate });
    });

    // Disparo de re-emisión de tokens cuando el profesional prolonga la sesión.
    socket.on('sesion-actualizada', () => {
        if (socket.data.rol !== 'profesional') return;
        const otros = (io.sockets.adapter.rooms.get(roomId)?.size ?? 1) - 1;
        log('SESION-ACTUALIZADA relay desde profesional -> otros en sala:', otros);
        socket.to(roomId).emit('sesion-actualizada', {});
    });

    socket.on('disconnect', () => {
        if (socket.data.reemplazado) { log('DISCONNECT (reemplazo, sin user-left)', quien); return; }
        log('DISCONNECT', quien);
        socket.to(roomId).emit('user-left', { rol: socket.data.rol });
    });
});

if (require.main === module) {
    httpServer.listen(PORT, () => {
        console.log(`Senalizacion en puerto ${PORT} | CORS: ${CORS_ORIGINS_RAW} | modoLan=${MODO_LAN}`);
    });
}

module.exports = { verificarJwt };
