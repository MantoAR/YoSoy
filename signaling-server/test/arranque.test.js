// Tests del proceso real (node server.js) y de la herramienta emitir-token.js.
'use strict';

const assert = require('node:assert/strict');
const path = require('path');
const { spawn, spawnSync } = require('child_process');
const { io } = require('socket.io-client');
const { verificarToken } = require('../token');
const { SECRETO, ahora, tk, pausa, hasta, esperar, unir, prueba } = require('./ayudas');

const RAIZ = path.join(__dirname, '..');
const VARIABLES_DEL_SERVIDOR = ['SIGNALING_SECRETO', 'HOST', 'PORT', 'SIGNALING_ORIGENES_CORS',
    'SIGNALING_MAX_PAYLOAD_BYTES', 'SIGNALING_MAX_HANDSHAKES_POR_MIN', 'SIGNALING_MAX_EVENTOS_POR_10S'];

/** El entorno de esta máquina, sin ninguna variable del servidor (que no se cuele la de alguien) más lo que se pida. */
function entornoCon(extra = {}) {
    const env = { ...process.env };
    for (const k of VARIABLES_DEL_SERVIDOR) delete env[k];
    return { ...env, ...extra };
}

function correr(archivo, extra, args = []) {
    return spawnSync(process.execPath, [path.join(RAIZ, archivo), ...args], {
        cwd: RAIZ, env: entornoCon(extra), encoding: 'utf8', timeout: 15000
    });
}

/** Arranca server.js de verdad (puerto libre) y espera a que diga dónde escucha. */
async function arrancar(t, extra = {}) {
    const hijo = spawn(process.execPath, [path.join(RAIZ, 'server.js')], {
        cwd: RAIZ, env: entornoCon({ SIGNALING_SECRETO: SECRETO, PORT: '0', ...extra })
    });
    let salida = '';
    hijo.stdout.on('data', d => { salida += d; });
    hijo.stderr.on('data', d => { salida += d; });
    t.after(() => { if (hijo.exitCode === null) hijo.kill(); });

    const donde = await new Promise((resolve, reject) => {
        const limite = Date.now() + 10000;
        const mirar = () => {
            const m = /ARRANQUE escuchando=([\d.]+):(\d+)/.exec(salida);
            if (m) return resolve({ host: m[1], puerto: Number(m[2]) });
            if (hijo.exitCode !== null) return reject(new Error(`el servidor terminó (${hijo.exitCode}): ${salida}`));
            if (Date.now() > limite) return reject(new Error(`el servidor no arrancó: ${salida}`));
            setTimeout(mirar, 25);
        };
        mirar();
    });
    return { hijo, salida: () => salida, ...donde };
}

// ── Falla al arrancar ──

prueba('sin SIGNALING_SECRETO el servidor no arranca', () => {
    const r = correr('server.js', {});
    assert.equal(r.status, 1);
    assert.match(r.stderr, /Falta SIGNALING_SECRETO/);
    assert.equal(r.stdout, '', 'no llegó a escuchar ni a loguear nada');
});

prueba('con un secreto débil (menos de 32 caracteres) no arranca y el mensaje no lo repite', () => {
    const debil = 'Sup3rS3creto-de-31-caracteres!!';
    assert.equal(debil.length, 31);
    const r = correr('server.js', { SIGNALING_SECRETO: debil });
    assert.equal(r.status, 1);
    assert.match(r.stderr, /demasiado corto \(31 caracteres; mínimo 32\)/);
    assert.ok(!r.stderr.includes(debil) && !r.stdout.includes(debil));
});

prueba('una configuración inválida (PORT, CORS con "*") tampoco arranca', () => {
    const puerto = correr('server.js', { SIGNALING_SECRETO: SECRETO, PORT: 'no-es-un-puerto' });
    assert.equal(puerto.status, 1);
    assert.match(puerto.stderr, /PORT tiene que ser un entero/);
    const cors = correr('server.js', { SIGNALING_SECRETO: SECRETO, SIGNALING_ORIGENES_CORS: '*' });
    assert.equal(cors.status, 1);
    assert.match(cors.stderr, /no admite "\*"/);
});

prueba('si el puerto está ocupado falla con un mensaje claro en vez de quedarse colgado', async t => {
    const primero = await arrancar(t);
    const r = correr('server.js', { SIGNALING_SECRETO: SECRETO, PORT: String(primero.puerto) });
    assert.equal(r.status, 1);
    assert.match(r.stderr, /No se pudo abrir 127\.0\.0\.1:\d+: EADDRINUSE/);
});

// ── Arranque y funcionamiento ──

prueba('con la configuración mínima escucha en 127.0.0.1 y responde /health', async t => {
    const s = await arrancar(t);
    assert.equal(s.host, '127.0.0.1', 'por defecto, solo loopback');
    assert.match(s.salida(), /cors=ninguno/);
    const cuerpo = await new Promise((resolve, reject) => {
        require('http').get({ host: '127.0.0.1', port: s.puerto, path: '/health' }, res => {
            let d = '';
            res.on('data', c => { d += c; });
            res.on('end', () => resolve(d));
        }).on('error', reject);
    });
    assert.equal(cuerpo, 'ok');
});

prueba('HOST y PORT por entorno se respetan', async t => {
    const s = await arrancar(t, { HOST: '127.0.0.1', PORT: '0' });
    assert.equal(s.host, '127.0.0.1');
    assert.ok(s.puerto > 0);
});

prueba('los logs reales (stdout) tienen conexión, rechazo y desconexión, y ni el token ni el secreto', async t => {
    const s = await arrancar(t);
    const bueno = tk('S1', 'paciente', '42');
    const malo = tk('S1', 'paciente', '43') + 'x';   // firma rota
    const url = `http://127.0.0.1:${s.puerto}`;
    const opciones = token => ({ auth: { token }, transports: ['websocket'], reconnection: false, forceNew: true });

    const cliente = io(url, opciones(bueno));
    t.after(() => cliente.close());
    await esperar(cliente, 'connect');
    assert.equal(await unir(cliente), 'joined');
    const rechazado = io(url, opciones(malo));
    t.after(() => rechazado.close());
    await esperar(rechazado, 'connect_error');
    cliente.close();
    assert.ok(await hasta(() => /DESCONEXION/.test(s.salida())), 'se loguea la desconexión');

    const salida = s.salida();
    assert.match(salida, /CONEXION ip=127\.0\.0\.1 rol=paciente sub=42 sala=S1/);
    assert.match(salida, /RECHAZO motivo=token-invalido ip=127\.0\.0\.1/);
    assert.match(salida, /DESCONEXION .*sub=42 .*razon=/);
    assert.ok(!salida.includes(SECRETO), 'el secreto no aparece');
    for (const token of [bueno, malo]) {
        assert.ok(!salida.includes(token), 'un token completo no aparece');
        for (const parte of token.split('.').filter(p => p.length > 12)) assert.ok(!salida.includes(parte), 'un trozo de token no aparece');
    }
});

prueba('al recibir SIGTERM (simulada con process.emit) cierra y sale con 0, dejándolo en el log', async t => {
    // Lo que systemd le manda al parar el servicio. Se emite adentro del proceso para que valga en cualquier plataforma.
    const hijo = spawn(process.execPath, ['-e', "require('./server.js').main(); setTimeout(() => process.emit('SIGTERM'), 700);"], {
        cwd: RAIZ, env: entornoCon({ SIGNALING_SECRETO: SECRETO, PORT: '0' })
    });
    let salida = '';
    hijo.stdout.on('data', d => { salida += d; });
    hijo.stderr.on('data', d => { salida += d; });
    t.after(() => { if (hijo.exitCode === null) hijo.kill(); });
    const codigo = await new Promise(resolve => hijo.once('exit', resolve));
    assert.equal(codigo, 0, salida);
    assert.match(salida, /ARRANQUE escuchando=127\.0\.0\.1:\d+/);
    assert.match(salida, /PARADA senal=SIGTERM/);
});

prueba('con la señal SIGTERM de verdad corta limpio (salida 0)', async t => {
    const s = await arrancar(t);
    const termino = new Promise(resolve => s.hijo.once('exit', (codigo, senal) => resolve({ codigo, senal })));
    s.hijo.kill('SIGTERM');
    const { codigo } = await termino;
    assert.equal(codigo, 0);
    assert.match(s.salida(), /PARADA senal=SIGTERM/);
}, { skip: process.platform === 'win32' && 'en Windows SIGTERM mata el proceso sin pasar por el handler' });

// ── Herramienta de desarrollo ──

prueba('emitir-token produce un token que el servidor acepta', () => {
    const r = correr('emitir-token.js', { SIGNALING_SECRETO: SECRETO }, ['--sala', 'SALA-9-ABC', '--rol', 'paciente', '--sub', '3', '--minutos', '5']);
    assert.equal(r.status, 0, r.stderr);
    const token = r.stdout.trim();
    assert.equal(token.split('.').length, 3, 'imprime solo el token');
    const t = verificarToken(token, SECRETO);
    assert.deepEqual({ sala: t.sala, rol: t.rol, sub: t.sub }, { sala: 'SALA-9-ABC', rol: 'paciente', sub: '3' });
    assert.ok(t.exp - ahora() > 290 && t.exp - ahora() <= 300);
    assert.ok(!r.stderr.includes(SECRETO));
});

prueba('emitir-token se niega sin secreto, con un rol inválido o con una sala que el servidor rechazaría', () => {
    const sinSecreto = correr('emitir-token.js', {}, ['--sala', 'S1', '--rol', 'paciente']);
    assert.equal(sinSecreto.status, 1);
    assert.match(sinSecreto.stderr, /Falta SIGNALING_SECRETO/);
    assert.equal(sinSecreto.stdout, '');

    const rol = correr('emitir-token.js', { SIGNALING_SECRETO: SECRETO }, ['--sala', 'S1', '--rol', 'admin']);
    assert.equal(rol.status, 1);
    assert.match(rol.stderr, /Uso: npm run token/);

    const sala = correr('emitir-token.js', { SIGNALING_SECRETO: SECRETO }, ['--sala', 'sala con espacios', '--rol', 'paciente']);
    assert.equal(sala.status, 1);
    assert.match(sala.stderr, /rechazaría el token \(claims-invalidos\)/);
    assert.equal(sala.stdout, '');
});
