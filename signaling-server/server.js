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
const CORS_ORIGINS = (process.env.CORS_ORIGINS || 'http://localhost:5000,http://localhost:5500')
    .split(',').map(o => o.trim()).filter(Boolean);

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

// ─── Rate limiting simple por IP (conexiones / joins) ─────────────────────────
const intentos = new Map(); // ip -> [timestamps]
const VENTANA_MS = 10000;
const MAX_INTENTOS = 15;
function limitado(ip) {
    const ahora = Date.now();
    const lista = (intentos.get(ip) || []).filter(t => ahora - t < VENTANA_MS);
    lista.push(ahora);
    intentos.set(ip, lista);
    return lista.length > MAX_INTENTOS;
}
setInterval(() => {
    const ahora = Date.now();
    for (const [ip, lista] of intentos) {
        const vivos = lista.filter(t => ahora - t < VENTANA_MS);
        if (vivos.length) intentos.set(ip, vivos); else intentos.delete(ip);
    }
}, VENTANA_MS).unref();

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

io.on('connection', (socket) => {
    const roomId = socket.data.roomId;

    socket.on('join-room', async ({ roomId: pedido } = {}) => {
        // El cliente no elige sala: si manda una distinta a la del token, se rechaza.
        if (pedido !== undefined && pedido !== roomId) {
            socket.emit('rechazado', { motivo: 'La sala no coincide con tu acceso.' });
            return socket.disconnect(true);
        }

        const activa = await sesionActivaEnDb(socket.data.idSesion);
        if (activa === false) {
            socket.emit('rechazado', { motivo: 'La sesión no está activa.' });
            return socket.disconnect(true);
        }

        // Máximo 2 participantes por sala.
        const sala = io.sockets.adapter.rooms.get(roomId);
        if (sala && sala.size >= 2) {
            socket.emit('rechazado', { motivo: 'La sala ya está completa.' });
            return socket.disconnect(true);
        }

        socket.join(roomId);
        socket.emit('joined', { roomId, rol: socket.data.rol });
        socket.to(roomId).emit('user-joined', { rol: socket.data.rol });
    });

    socket.on('offer', ({ offer } = {}) => {
        if (!descripcionValida(offer)) return;
        socket.to(roomId).emit('offer', { offer });
    });

    socket.on('answer', ({ answer } = {}) => {
        if (!descripcionValida(answer)) return;
        socket.to(roomId).emit('answer', { answer });
    });

    socket.on('ice-candidate', ({ candidate } = {}) => {
        if (!candidatoValido(candidate)) return;
        socket.to(roomId).emit('ice-candidate', { candidate });
    });

    // Disparo de re-emisión de tokens cuando el profesional prolonga la sesión.
    socket.on('sesion-actualizada', () => {
        if (socket.data.rol !== 'profesional') return;
        socket.to(roomId).emit('sesion-actualizada', {});
    });

    socket.on('disconnect', () => {
        socket.to(roomId).emit('user-left', { rol: socket.data.rol });
    });
});

if (require.main === module) {
    httpServer.listen(PORT, () => {
        console.log(`Senalizacion en puerto ${PORT} | CORS: ${CORS_ORIGINS.join(', ')} | modoLan=${MODO_LAN}`);
    });
}

module.exports = { verificarJwt };
