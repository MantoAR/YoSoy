// Tests de integración: servidor real en un puerto libre y clientes socket.io-client.
'use strict';

const assert = require('node:assert/strict');
const http = require('http');
const { SECRETO, ahora, tk, firmar, sinFirma, pausa, hasta, esperar, noLlega, unir, entorno, prueba } = require('./ayudas');

const SDP = 'v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n';
const OFERTA = { type: 'offer', sdp: SDP };
const RESPUESTA = { type: 'answer', sdp: SDP };
const CANDIDATO = { candidate: 'candidate:1 1 udp 2122260223 192.168.0.5 54321 typ host', sdpMid: '0', sdpMLineIndex: 0, usernameFragment: 'abcd' };

/** GET con los headers que se pidan; devuelve status, headers y cuerpo. */
function pedir(url, ruta, headers = {}) {
    return new Promise((resolve, reject) => {
        http.get(url + ruta, { headers }, res => {
            const trozos = [];
            res.on('data', c => trozos.push(c));
            res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, cuerpo: Buffer.concat(trozos).toString('utf8') }));
        }).on('error', reject);
    });
}

// ── Autenticación en el handshake ──

prueba('un token válido conecta; la sala y el rol salen del token, no de lo que diga el cliente', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '10'));
    // Este cliente se "declara" profesional de otra sala, por todos los medios posibles: se ignora todo.
    const pac = await e.conectado(tk('S1', 'paciente', '20'), {
        auth: { token: tk('S1', 'paciente', '20'), sala: 'OTRA', rol: 'profesional', roomId: 'OTRA', role: 'profesional' },
        query: { sala: 'OTRA', rol: 'profesional' }
    });
    const avisoAlPro = esperar(pro, 'user-joined');
    const joined = esperar(pac, 'joined');
    pac.emit('join-room', { roomId: 'OTRA', role: 'profesional', sala: 'OTRA', rol: 'profesional' });
    assert.deepEqual(await joined, { rol: 'paciente' });
    assert.deepEqual(await avisoAlPro, { rol: 'paciente' });
    assert.equal(e.servidor.cantidadSalas(), 1, 'una sola sala: la del token');
});

prueba('los errores de handshake llegan como connect_error con un mensaje claro', async t => {
    const e = await entorno(t);
    const exp = ahora() + 600;
    const base = { sala: 'S1', rol: 'paciente', sub: '1' };
    const casos = [
        ['sin token', undefined, 'no autorizado', 'no-autorizado'],
        ['basura', 'esto-no-es-un-token', 'no autorizado', 'no-autorizado'],
        ['firma de otro secreto', firmar({ ...base, exp }, {}, 'otro-secreto-que-tampoco-es-el-de-verdad-123456'), 'no autorizado', 'no-autorizado'],
        ['alg:none', sinFirma({ ...base, exp }), 'no autorizado', 'no-autorizado'],
        ['HS512', firmar({ ...base, exp }, { algorithm: 'HS512' }), 'no autorizado', 'no-autorizado'],
        ['sin exp', firmar(base), 'no autorizado', 'no-autorizado'],
        ['rol inválido', firmar({ ...base, rol: 'admin', exp }), 'no autorizado', 'no-autorizado'],
        ['vencido', firmar({ ...base, exp: ahora() - 30 }), 'la sesión ya terminó', 'token-vencido']
    ];
    for (const [nombre, token, mensaje, codigo] of casos) {
        const socket = e.conectar(token);
        const err = await esperar(socket, 'connect_error');
        assert.equal(err.message, mensaje, nombre);
        assert.equal(err.data && err.data.codigo, codigo, nombre);
        assert.equal(socket.connected, false, nombre);
    }
    assert.equal(e.logs.filter(l => l.evento === 'CONEXION').length, 0, 'ninguno llegó a conectarse');
    assert.equal(e.logs.filter(l => l.evento === 'RECHAZO').length, casos.length);
});

// ── Reenvío ──

prueba('offer, answer e ice-candidate pasan solo al otro participante, con los campos justos y sin eco', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.unido(tk('S1', 'paciente', '2'));

    const oferta = esperar(pac, 'offer');
    pro.emit('offer', { roomId: 'OTRA', offer: { ...OFERTA, basura: 'no pasa' }, extra: 1 });
    assert.deepEqual(await oferta, { offer: OFERTA });

    const respuesta = esperar(pro, 'answer');
    pac.emit('answer', { answer: { ...RESPUESTA, basura: 'no pasa' } });
    assert.deepEqual(await respuesta, { answer: RESPUESTA });

    const ice1 = esperar(pac, 'ice-candidate');
    pro.emit('ice-candidate', { candidate: { ...CANDIDATO, basura: 'no pasa' } });
    assert.deepEqual(await ice1, { candidate: CANDIDATO });
    const ice2 = esperar(pro, 'ice-candidate');
    pac.emit('ice-candidate', { candidate: { candidate: '', sdpMid: null, sdpMLineIndex: null } });   // fin de candidatos
    assert.deepEqual(await ice2, { candidate: { candidate: '', sdpMid: null, sdpMLineIndex: null } });

    assert.ok(await noLlega(pro, 'offer') && await noLlega(pac, 'answer'), 'el que manda no recibe su propio mensaje');
});

prueba('un mensaje con el roomId de otra sala no llega a esa sala', async t => {
    const e = await entorno(t);
    const proA = await e.unido(tk('A', 'profesional', '1'));
    const pacA = await e.unido(tk('A', 'paciente', '2'));
    const proB = await e.unido(tk('B', 'profesional', '3'));
    const pacB = await e.unido(tk('B', 'paciente', '4'));

    const aLaSuya = esperar(pacA, 'offer');
    const enB = noLlega(pacB, 'offer', 500);
    const enBIce = noLlega(proB, 'ice-candidate', 500);
    proA.emit('offer', { roomId: 'B', offer: OFERTA });
    pacA.emit('ice-candidate', { roomId: 'B', candidate: CANDIDATO });
    assert.deepEqual(await aLaSuya, { offer: OFERTA }, 'se reenvía a la sala del token (la A)');
    assert.ok(await enB, 'la sala B no recibe la oferta');
    assert.ok(await enBIce, 'la sala B no recibe el candidato');
    assert.equal(proB.connected && pacB.connected, true);
});

prueba('un socket que todavía no hizo join no puede mandar ni recibir nada', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.conectado(tk('S1', 'paciente', '2'));   // autenticado, pero sin join-room

    const nadaParaElPro = noLlega(pro, 'offer', 500);
    pac.emit('offer', { offer: OFERTA });
    pac.emit('answer', { answer: RESPUESTA });
    pac.emit('ice-candidate', { candidate: CANDIDATO });
    assert.ok(await nadaParaElPro && await noLlega(pro, 'ice-candidate', 100), 'lo que manda sin join no llega');

    const nadaParaElPac = noLlega(pac, 'offer', 500);
    pro.emit('offer', { offer: OFERTA });
    assert.ok(await nadaParaElPac, 'y lo que le mandan a esa sala tampoco le llega');
});

prueba('un socket rechazado no alcanza a mandar nada antes de que lo corten', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.unido(tk('S1', 'paciente', '2'));
    const intruso = await e.conectado(tk('S1', 'paciente', '3'));
    const nadaAlPro = noLlega(pro, 'offer', 600);
    const nadaAlPac = noLlega(pac, 'offer', 600);
    intruso.emit('join-room');
    intruso.emit('offer', { offer: OFERTA });      // llega pegado al join, antes de que cierre la conexión
    intruso.emit('ice-candidate', { candidate: CANDIDATO });
    assert.ok(await nadaAlPro && await nadaAlPac);
});

// ── Capacidad de la sala ──

prueba('el tercero que quiere entrar a una sala completa recibe "rechazado" y se desconecta', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.unido(tk('S1', 'paciente', '2'));
    const tercero = await e.conectado(tk('S1', 'paciente', '3'));

    const rechazo = esperar(tercero, 'rechazado');
    const cierre = esperar(tercero, 'disconnect');
    tercero.emit('join-room');
    const [r, motivoDelCierre] = await Promise.all([rechazo, cierre]);
    assert.deepEqual(r, { motivo: 'La sala ya está completa.', codigo: 'sala-llena' });
    assert.match(motivoDelCierre, /io server disconnect|transport close/);

    // Los dos que ya estaban siguen conectados, no vieron nada raro y se siguen hablando.
    assert.ok(await noLlega(pro, 'user-left', 200) && await noLlega(pac, 'user-left', 200) && await noLlega(pro, 'user-joined', 100));
    const oferta = esperar(pac, 'offer');
    pro.emit('offer', { offer: OFERTA });
    assert.deepEqual(await oferta, { offer: OFERTA });
    assert.ok(e.logs.some(l => l.evento === 'RECHAZO' && l.motivo === 'sala-llena' && l.sub === '3'));
});

prueba('un tercero con el rol del profesional también se rechaza si la sala está completa', async t => {
    const e = await entorno(t);
    await e.unido(tk('S1', 'profesional', '1'));
    await e.unido(tk('S1', 'paciente', '2'));
    const tercero = await e.conectado(tk('S1', 'profesional', '3'));
    assert.equal(await unir(tercero), 'sala-llena');
});

prueba('dos personas con el mismo rol: la segunda es rechazada aunque el otro asiento esté libre', async t => {
    const e = await entorno(t);
    const pro1 = await e.unido(tk('S1', 'profesional', '1'));
    const pro2 = await e.conectado(tk('S1', 'profesional', '2'));

    const rechazo = esperar(pro2, 'rechazado');
    const cierre = esperar(pro2, 'disconnect');
    pro2.emit('join-room');
    const [r] = await Promise.all([rechazo, cierre]);
    assert.equal(r.codigo, 'rol-ocupado');
    assert.equal(r.motivo, 'Ya hay otra persona conectada como profesional en esta sala.');

    // El primero conserva su asiento: cuando llega el paciente, es a él a quien le avisan.
    const aviso = esperar(pro1, 'user-joined');
    const pac = await e.conectado(tk('S1', 'paciente', '3'));
    pac.emit('join-room');
    assert.deepEqual(await aviso, { rol: 'paciente' });
});

prueba('uniones simultáneas por el mismo asiento: gana exactamente una (no hay carrera)', async t => {
    const e = await entorno(t);
    const sockets = [];
    for (const sub of ['1', '2', '3', '4']) sockets.push(await e.conectado(tk('S1', 'profesional', sub)));
    const resultados = await Promise.all(sockets.map(s => unir(s)));
    assert.equal(resultados.filter(r => r === 'joined').length, 1, JSON.stringify(resultados));
    assert.deepEqual([...new Set(resultados.filter(r => r !== 'joined'))], ['rol-ocupado']);

    // Cuatro sockets, dos de cada rol, todos a la vez: un asiento por rol, nunca más de dos adentro.
    const dos = [];
    for (const [rol, sub] of [['profesional', 'a'], ['profesional', 'b'], ['paciente', 'c'], ['paciente', 'd']]) {
        dos.push([rol, await e.conectado(tk('S2', rol, sub))]);
    }
    const res = await Promise.all(dos.map(([, s]) => unir(s)));
    const ganadores = dos.filter((_, i) => res[i] === 'joined').map(([rol]) => rol).sort();
    assert.deepEqual(ganadores, ['paciente', 'profesional']);
    assert.ok(res.filter(r => r !== 'joined').every(r => r === 'rol-ocupado' || r === 'sala-llena'));
});

prueba('el mismo usuario volviendo reemplaza a su conexión anterior, sin que el otro vea una salida', async t => {
    const e = await entorno(t);
    const viejo = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.unido(tk('S1', 'paciente', '2'));

    // Su conexión vieja sigue "viva" (cortó la red, ping timeout pendiente) y él reconecta con el mismo token.
    const nuevo = await e.conectado(tk('S1', 'profesional', '1'));
    const echado = esperar(viejo, 'rechazado');
    const sinSalida = noLlega(pac, 'user-left', 600);
    const reingreso = esperar(pac, 'user-joined');
    nuevo.emit('join-room');
    assert.deepEqual(await echado, { motivo: 'Esta sesión se abrió en otro lugar.', codigo: 'reemplazado' });
    assert.deepEqual(await reingreso, { rol: 'profesional' }, 'el otro se entera de que "entró" para renegociar');
    assert.ok(await sinSalida, 'pero nunca de que "se fue"');

    const oferta = esperar(pac, 'offer');
    nuevo.emit('offer', { offer: OFERTA });
    assert.deepEqual(await oferta, { offer: OFERTA });
    const aLaNueva = esperar(nuevo, 'answer');
    pac.emit('answer', { answer: RESPUESTA });
    assert.deepEqual(await aLaNueva, { answer: RESPUESTA }, 'lo que manda el paciente llega a la conexión nueva');
    assert.equal(e.servidor.cantidadSalas(), 1);
});

prueba('otra persona con el mismo rol NO puede reemplazar a nadie (el reemplazo es solo del mismo sub)', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    await e.unido(tk('S1', 'paciente', '2'));
    const impostor = await e.conectado(tk('S1', 'profesional', '99'));
    assert.equal(await unir(impostor), 'sala-llena');
    assert.equal(pro.connected, true);
});

prueba('cuando alguien se va, el otro lo ve; la sala se libera cuando se van los dos', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.unido(tk('S1', 'paciente', '2'));
    const salida = esperar(pro, 'user-left');
    pac.close();
    assert.deepEqual(await salida, { rol: 'paciente' });
    assert.equal(e.servidor.cantidadSalas(), 1);
    pro.close();
    assert.ok(await hasta(() => e.servidor.cantidadSalas() === 0), 'no queda la sala en memoria');

    // y el asiento se puede volver a ocupar
    const otro = await e.unido(tk('S1', 'paciente', '5'));
    assert.equal(otro.connected, true);
});

// ── Tamaño y forma de lo que se reenvía ──

prueba('un mensaje de más de 64 KB corta la conexión del que lo manda y no se reenvía', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.unido(tk('S1', 'paciente', '2'));

    const cierre = esperar(pro, 'disconnect', 5000);
    const salida = esperar(pac, 'user-left', 5000);
    const nada = noLlega(pac, 'offer', 1500);
    pro.emit('offer', { offer: { type: 'offer', sdp: 'a'.repeat(200 * 1024) } });
    const [motivo, ul, noRecibio] = await Promise.all([cierre, salida, nada]);
    assert.match(motivo, /transport (close|error)/);
    assert.deepEqual(ul, { rol: 'profesional' });
    assert.ok(noRecibio, 'el paciente no recibió nada');
    assert.equal(pac.connected, true);
});

prueba('un SDP de 29 mil caracteres pasa; uno de 31 mil (bajo el tope de 64 KB) se descarta por su forma', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.unido(tk('S1', 'paciente', '2'));
    const primera = esperar(pac, 'offer');
    pro.emit('offer', { offer: { type: 'offer', sdp: 'a'.repeat(31000) } });
    pro.emit('offer', { offer: { type: 'offer', sdp: 'b'.repeat(29000) } });
    const llego = await primera;
    assert.equal(llego.offer.sdp.length, 29000, 'la primera en llegar es la de 29 mil: la de 31 mil se descartó');
});

prueba('payloads mal formados, vacíos o de más se descartan sin romper nada', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.unido(tk('S1', 'paciente', '2'));

    const primeraOferta = esperar(pac, 'offer');
    const primeraRespuesta = esperar(pac, 'answer');
    const primerIce = esperar(pac, 'ice-candidate');
    const malos = {
        offer: [undefined, null, 5, 'texto', [], {}, { offer: 'x' }, { offer: { type: 'answer', sdp: 'v=0' } },
            { offer: { type: 'offer' } }, { offer: { type: 'offer', sdp: '' } }, { offer: { type: 'offer', sdp: 12 } }],
        answer: [undefined, null, { answer: { type: 'offer', sdp: 'v=0' } }, { answer: 'x' }, { answer: {} }],
        'ice-candidate': [undefined, null, 7, { candidate: 5 }, { candidate: {} }, { candidate: { candidate: 'x'.repeat(3000) } },
            { candidate: { candidate: 12 } }, { candidate: 'texto' }]
    };
    for (const [evento, lista] of Object.entries(malos)) {
        pro.emit(evento);                                  // sin argumentos
        for (const m of lista) pro.emit(evento, m);
    }
    // Si algo malo se hubiera reenviado, esto llegaría antes que lo bueno:
    pro.emit('offer', { offer: OFERTA });
    pro.emit('answer', { answer: RESPUESTA });
    pro.emit('ice-candidate', { candidate: CANDIDATO });
    assert.deepEqual(await primeraOferta, { offer: OFERTA });
    assert.deepEqual(await primeraRespuesta, { answer: RESPUESTA });
    assert.deepEqual(await primerIce, { candidate: CANDIDATO });
    assert.equal(pro.connected, true);
});

prueba('los eventos que no son del protocolo se ignoran', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1'));
    const pac = await e.unido(tk('S1', 'paciente', '2'));
    const nada = noLlega(pac, 'hack', 300);
    pro.emit('hack', { x: 1 });
    pro.emit('sesion-actualizada', {});
    pro.emit('__proto__', {});
    assert.ok(await nada);
    const oferta = esperar(pac, 'offer');
    pro.emit('offer', { offer: OFERTA });
    assert.deepEqual(await oferta, { offer: OFERTA }, 'el servidor sigue andando');
});

// ── Vencimiento del token ──

prueba('al llegar el exp del token se corta el socket, se avisa al otro y el asiento queda libre', async t => {
    const e = await entorno(t);
    const pro = await e.unido(tk('S1', 'profesional', '1', { expiraEnSeg: 3 }));
    const pac = await e.unido(tk('S1', 'paciente', '2'));

    const [r, motivoDelCierre, salida] = await Promise.all([
        esperar(pro, 'rechazado', 6000), esperar(pro, 'disconnect', 6000), esperar(pac, 'user-left', 6000)
    ]);
    assert.deepEqual(r, { motivo: 'La sesión terminó.', codigo: 'sesion-vencida' });
    assert.match(motivoDelCierre, /io server disconnect|transport close/);
    assert.deepEqual(salida, { rol: 'profesional', motivo: 'sesion-vencida' });
    assert.equal(pac.connected, true, 'el paciente sigue (su token dura más)');

    const nuevo = await e.unido(tk('S1', 'profesional', '9'));    // el asiento se liberó
    assert.equal(nuevo.connected, true);
    assert.ok(e.logs.some(l => l.evento === 'RECHAZO' && l.motivo === 'sesion-vencida' && l.sub === '1'));
});

prueba('un socket que se conectó pero nunca hizo join también se corta al vencer su token', async t => {
    const e = await entorno(t);
    const socket = await e.conectado(tk('S1', 'paciente', '1', { expiraEnSeg: 3 }));
    const [r] = await Promise.all([esperar(socket, 'rechazado', 6000), esperar(socket, 'disconnect', 6000)]);
    assert.equal(r.codigo, 'sesion-vencida');
});

prueba('un token que vence dentro de 60 días no corta la conexión enseguida (límite de setTimeout)', async t => {
    const e = await entorno(t);
    const socket = await e.conectado(tk('S1', 'paciente', '1', { expiraEnSeg: 60 * 24 * 3600 }));
    await pausa(700);
    assert.equal(socket.connected, true);
    assert.equal(await unir(socket), 'joined');
});

// ── Límites ──

prueba('el límite de conexiones por IP usa la IP real de X-Forwarded-For (la última entrada) y no la de Caddy', async t => {
    const e = await entorno(t, { maxHandshakesPorMin: 3 });
    const desde = xff => e.conectar(tk('S1', 'paciente', '1'), { extraHeaders: { 'X-Forwarded-For': xff } });

    for (let i = 0; i < 3; i++) {
        const s = desde('198.51.100.1');
        await esperar(s, 'connect');
        s.close();
    }
    const bloqueado = desde('198.51.100.1');
    const err = await esperar(bloqueado, 'connect_error');
    assert.match(err.message, /websocket error|xhr poll error|403/i);

    const otra = desde('198.51.100.2');
    await esperar(otra, 'connect');           // otra IP real: no la afecta
    assert.equal(otra.connected, true);

    const disfrazado = desde('203.0.113.77, 198.51.100.1');   // antepone una IP falsa: igual cuenta como la última
    await esperar(disfrazado, 'connect_error');

    const rechazos = e.logs.filter(l => l.evento === 'RECHAZO' && l.motivo === 'limite-conexiones');
    assert.equal(rechazos.length, 1, 'se loguea el primer rechazo, no cada intento');
    assert.equal(rechazos[0].ip, '198.51.100.1');
});

prueba('un cliente que empieza por polling y sube a websocket (como el navegador) cuenta como UNA conexión para el límite', async t => {
    const e = await entorno(t, { maxHandshakesPorMin: 3 });
    const comoElNavegador = { transports: ['polling', 'websocket'] };
    const pro = await e.unido(tk('S1', 'profesional', '1'), comoElNavegador);
    const pac = await e.unido(tk('S1', 'paciente', '2'), comoElNavegador);
    assert.ok(await hasta(() => pro.io.engine.transport.name === 'websocket' && pac.io.engine.transport.name === 'websocket', 5000),
        'los dos subieron a websocket');

    // Todo el tráfico del polling y del upgrade no cuenta como conexiones nuevas: siguen andando y quedan 1 cupo.
    for (let i = 0; i < 5; i++) {
        const recibido = esperar(pac, 'ice-candidate');
        pro.emit('ice-candidate', { candidate: CANDIDATO });
        await recibido;
    }
    assert.equal(e.logs.filter(l => l.evento === 'RECHAZO').length, 0);
    const tercera = await e.conectado(tk('S2', 'paciente', '3'), comoElNavegador);     // la tercera entra
    assert.equal(tercera.connected, true);
    await esperar(e.conectar(tk('S2', 'paciente', '4'), comoElNavegador), 'connect_error');   // la cuarta no
    assert.equal(e.logs.filter(l => l.motivo === 'limite-conexiones').length, 1);
});

prueba('también funciona solo con polling: el token viaja en el handshake y se reenvía igual', async t => {
    const e = await entorno(t);
    const solo = { transports: ['polling'] };
    const pro = await e.unido(tk('S1', 'profesional', '1'), solo);
    const pac = await e.unido(tk('S1', 'paciente', '2'), solo);
    assert.equal(pro.io.engine.transport.name, 'polling');
    const oferta = esperar(pac, 'offer');
    pro.emit('offer', { offer: OFERTA });
    assert.deepEqual(await oferta, { offer: OFERTA });
    const mal = e.conectar('basura', solo);
    assert.equal((await esperar(mal, 'connect_error')).message, 'no autorizado');
});

prueba('un socket que manda demasiados eventos es expulsado y el otro se entera', async t => {
    const e = await entorno(t, { maxEventosPor10s: 6 });
    const pro = await e.unido(tk('S1', 'profesional', '1'));     // el join cuenta como uno
    const pac = await e.unido(tk('S1', 'paciente', '2'));

    const todo = Promise.all([esperar(pro, 'rechazado'), esperar(pro, 'disconnect'), esperar(pac, 'user-left')]);
    for (let i = 0; i < 12; i++) pro.emit('ice-candidate', { candidate: CANDIDATO });
    const [r, , salida] = await todo;
    assert.equal(r.codigo, 'limite-eventos');
    assert.deepEqual(salida, { rol: 'profesional' });
    assert.equal(e.logs.filter(l => l.evento === 'RECHAZO' && l.motivo === 'limite-eventos').length, 1, 'se loguea una sola vez');
});

// ── Entorno HTTP ──

prueba('CORS: sin lista no se manda ningún header; con lista, solo para los orígenes listados', async t => {
    const sinLista = await entorno(t);
    const a = await pedir(sinLista.url, '/socket.io/?EIO=4&transport=polling', { Origin: 'http://malo.example' });
    assert.equal(a.headers['access-control-allow-origin'], undefined);

    const conLista = await entorno(t, { origenesCors: ['http://localhost:5000'] });
    const b = await pedir(conLista.url, '/socket.io/?EIO=4&transport=polling', { Origin: 'http://localhost:5000' });
    assert.equal(b.headers['access-control-allow-origin'], 'http://localhost:5000');
    const c = await pedir(conLista.url, '/socket.io/?EIO=4&transport=polling', { Origin: 'http://malo.example' });
    assert.equal(c.headers['access-control-allow-origin'], undefined);
    for (const r of [a, b, c]) assert.notEqual(r.headers['access-control-allow-origin'], '*');
});

prueba('/health responde ok, lo demás 404, y no se sirve el cliente de socket.io', async t => {
    const e = await entorno(t);
    const salud = await pedir(e.url, '/health');
    assert.equal(salud.status, 200);
    assert.equal(salud.cuerpo, 'ok');
    assert.equal((await pedir(e.url, '/')).status, 404);
    assert.equal((await pedir(e.url, '/otra-cosa')).status, 404);
    const cliente = await pedir(e.url, '/socket.io/socket.io.js');
    assert.notEqual(cliente.status, 200);
});

prueba('escucha solo donde se le dijo (loopback)', async t => {
    const e = await entorno(t);
    assert.equal(e.servidor.host, '127.0.0.1');
});

// ── Logs ──

prueba('los logs tienen lo mínimo (conexión, rechazo con motivo, desconexión) y nunca un token ni el secreto', async t => {
    const e = await entorno(t);
    const exp = ahora() + 600;
    const tokens = {
        profesional: tk('S1', 'profesional', '1'),
        paciente: tk('S1', 'paciente', '2'),
        intruso: tk('S1', 'paciente', '3'),
        vencido: firmar({ sala: 'S1', rol: 'paciente', sub: '4', exp: ahora() - 30 }),
        ajeno: firmar({ sala: 'S1', rol: 'paciente', sub: '5', exp }, {}, 'otro-secreto-que-tampoco-es-el-de-verdad-123456'),
        sinFirma: sinFirma({ sala: 'S1', rol: 'paciente', sub: '6', exp })
    };
    const pro = await e.unido(tokens.profesional);
    const pac = await e.unido(tokens.paciente);
    const intruso = await e.conectado(tokens.intruso);
    await unir(intruso);
    for (const malo of [tokens.vencido, tokens.ajeno, tokens.sinFirma, 'basura']) await esperar(e.conectar(malo), 'connect_error');
    pac.close();
    pro.close();
    await hasta(() => e.logs.filter(l => l.evento === 'DESCONEXION').length >= 3);

    const texto = JSON.stringify(e.logs);
    for (const [nombre, token] of Object.entries(tokens)) {
        assert.ok(!texto.includes(token), `el log contiene el token "${nombre}"`);
        for (const parte of token.split('.').filter(p => p.length > 12)) assert.ok(!texto.includes(parte), `el log contiene un trozo del token "${nombre}"`);
    }
    assert.ok(!texto.includes(SECRETO), 'el log contiene el secreto');

    const eventos = new Set(e.logs.map(l => l.evento));
    for (const esperado of ['CONEXION', 'RECHAZO', 'DESCONEXION']) assert.ok(eventos.has(esperado), `falta ${esperado}`);
    const conexion = e.logs.find(l => l.evento === 'CONEXION');
    assert.deepEqual(Object.keys(conexion).sort(), ['evento', 'ip', 'rol', 'sala', 'sub']);
    assert.ok(e.logs.filter(l => l.evento === 'RECHAZO').every(l => typeof l.motivo === 'string' && l.motivo.length > 0), 'todo rechazo trae su motivo');
});
