// Tests unitarios de verificarToken y de ipReal (sin red).
'use strict';

const assert = require('node:assert/strict');
const jwt = require('jsonwebtoken');
const { verificarToken, emitirToken, ErrorToken } = require('../token');
const { ipReal } = require('../server');
const { SECRETO, ahora, tk, firmar, firmarCrudo, sinFirma, prueba } = require('./ayudas');

const BUENOS = { sala: 'SALA-12-ABCDEF0123', rol: 'paciente', sub: '34' };

function codigoDe(token, secreto = SECRETO) {
    try {
        verificarToken(token, secreto);
    } catch (e) {
        assert.ok(e instanceof ErrorToken, `se esperaba ErrorToken y llegó ${e && e.name}`);
        return e.codigo;
    }
    return null;
}

prueba('verificarToken devuelve solo sala, rol, sub y exp de un token válido', () => {
    const exp = ahora() + 600;
    const t = verificarToken(firmar({ ...BUENOS, exp, extra: 'no pasa' }), SECRETO);
    assert.deepEqual(t, { sala: BUENOS.sala, rol: 'paciente', sub: '34', exp });
});

prueba('emitirToken genera tokens que verificarToken acepta, para los dos roles', () => {
    for (const rol of ['profesional', 'paciente']) {
        const t = verificarToken(emitirToken({ sala: 'S1', rol, sub: 7, expiraEnSeg: 60 }, SECRETO), SECRETO);
        assert.equal(t.rol, rol);
        assert.equal(t.sub, '7');
        assert.ok(t.exp > ahora());
    }
});

prueba('rechaza lo que no es un token: ausente, tipo equivocado, basura, demasiado largo', () => {
    for (const malo of [undefined, null, '', 42, {}, [], 'basura', 'a.b.c', 'a'.repeat(5000)]) {
        assert.equal(codigoDe(malo), malo === 'basura' || malo === 'a.b.c' ? 'token-invalido' : 'token-ausente', String(malo).slice(0, 20));
    }
});

prueba('rechaza una firma hecha con otro secreto', () => {
    const ajeno = firmar({ ...BUENOS, exp: ahora() + 600 }, {}, 'otro-secreto-que-tampoco-es-el-de-verdad-123456');
    assert.equal(codigoDe(ajeno), 'token-invalido');
});

prueba('rechaza un token con el payload alterado (la firma ya no corresponde)', () => {
    const [h, , f] = tk('S1', 'paciente', '1').split('.');
    const otroPayload = Buffer.from(JSON.stringify({ ...BUENOS, rol: 'profesional', exp: ahora() + 600 })).toString('base64url');
    assert.equal(codigoDe(`${h}.${otroPayload}.${f}`), 'token-invalido');
});

prueba('rechaza alg:none, con y sin firma', () => {
    const payload = { ...BUENOS, exp: ahora() + 600 };
    assert.equal(codigoDe(sinFirma(payload)), 'token-invalido');
    assert.equal(codigoDe(sinFirma(payload) + 'AAAA'), 'token-invalido');
});

prueba('rechaza otros algoritmos aunque estén firmados con el secreto correcto', () => {
    const payload = { ...BUENOS, exp: ahora() + 600 };
    for (const algorithm of ['HS384', 'HS512']) {
        assert.equal(codigoDe(firmar(payload, { algorithm })), 'token-invalido', algorithm);
    }
});

prueba('un token vencido se distingue de uno inválido, y solo si la firma es buena', () => {
    const vencido = firmar({ ...BUENOS, exp: ahora() - 30 });
    assert.equal(codigoDe(vencido), 'token-vencido');
    const vencidoYAjeno = firmar({ ...BUENOS, exp: ahora() - 30 }, {}, 'otro-secreto-que-tampoco-es-el-de-verdad-123456');
    assert.equal(codigoDe(vencidoYAjeno), 'token-invalido');
});

prueba('un token sin exp no sirve (jsonwebtoken lo dejaría valer para siempre)', () => {
    assert.equal(codigoDe(firmar({ ...BUENOS })), 'token-invalido');
    assert.equal(codigoDe(firmarCrudo({ ...BUENOS, exp: 'mañana' })), 'token-invalido');
    assert.equal(codigoDe(firmarCrudo({ ...BUENOS, exp: null })), 'token-invalido');
});

prueba('un payload bien firmado pero que no es un objeto tampoco sirve', () => {
    for (const payload of ['texto', [1, 2, 3], 42, null]) {
        assert.equal(codigoDe(firmarCrudo(payload)), 'token-invalido', JSON.stringify(payload));
    }
});

prueba('un token que todavía no rige (nbf) no sirve', () => {
    assert.equal(codigoDe(firmar({ ...BUENOS, exp: ahora() + 600, nbf: ahora() + 300 })), 'token-invalido');
});

prueba('claims inválidos: rol, sala y sub', () => {
    const exp = ahora() + 600;
    const casos = {
        'rol desconocido': { ...BUENOS, rol: 'admin', exp },
        'rol en mayúsculas': { ...BUENOS, rol: 'Paciente', exp },
        'sin rol': { sala: BUENOS.sala, sub: BUENOS.sub, exp },
        'sala vacía': { ...BUENOS, sala: '', exp },
        'sala con espacios': { ...BUENOS, sala: 'SALA 1', exp },
        'sala con salto de línea': { ...BUENOS, sala: 'S1\nCONEXION', exp },
        'sala demasiado larga': { ...BUENOS, sala: 'S'.repeat(101), exp },
        'sala no es string': { ...BUENOS, sala: 123, exp },
        'sin sub': { sala: BUENOS.sala, rol: BUENOS.rol, exp },
        'sub con caracteres raros': { ...BUENOS, sub: 'a b\n', exp },
        'sub objeto': { ...BUENOS, sub: { id: 1 }, exp }
    };
    for (const [nombre, payload] of Object.entries(casos)) {
        assert.equal(codigoDe(firmar(payload)), 'claims-invalidos', nombre);
    }
});

prueba('un sub numérico entero se normaliza a string', () => {
    const t = verificarToken(firmar({ sala: 'S1', rol: 'paciente', sub: 34, exp: ahora() + 60 }), SECRETO);
    assert.equal(t.sub, '34');
});

prueba('el secreto se interpreta como UTF-8, igual que Encoding.UTF8.GetBytes en .NET', () => {
    const secreto = 'clave-con-ñ-y-acentos-áéíóú-de-más-de-32-caracteres';
    const token = jwt.sign({ ...BUENOS, exp: ahora() + 60 }, Buffer.from(secreto, 'utf8'), { algorithm: 'HS256', noTimestamp: true });
    assert.equal(verificarToken(token, secreto).sala, BUENOS.sala);
});

// ── ipReal ──
const pedido = (origen, xff) => ({ socket: { remoteAddress: origen }, headers: xff === undefined ? {} : { 'x-forwarded-for': xff } });

prueba('ipReal: desde loopback (Caddy) toma X-Forwarded-For', () => {
    assert.equal(ipReal(pedido('127.0.0.1', '203.0.113.9')), '203.0.113.9');
    assert.equal(ipReal(pedido('::1', '203.0.113.9')), '203.0.113.9');
    assert.equal(ipReal(pedido('::ffff:127.0.0.1', '203.0.113.9')), '203.0.113.9');
    assert.equal(ipReal(pedido('127.0.0.1', '2001:db8::1')), '2001:db8::1');
});

prueba('ipReal: de la lista toma la última entrada, la que agregó el proxy', () => {
    assert.equal(ipReal(pedido('127.0.0.1', '1.1.1.1, 203.0.113.9')), '203.0.113.9');
    assert.equal(ipReal(pedido('127.0.0.1', '1.1.1.1,2.2.2.2 ,  203.0.113.9 ')), '203.0.113.9');
});

prueba('ipReal: sin header o con un header que no es una IP, queda la del origen', () => {
    assert.equal(ipReal(pedido('127.0.0.1')), '127.0.0.1');
    assert.equal(ipReal(pedido('127.0.0.1', 'no-soy-una-ip')), '127.0.0.1');
    assert.equal(ipReal(pedido('127.0.0.1', '203.0.113.9, <script>')), '127.0.0.1');
    assert.equal(ipReal({ headers: {} }), 'desconocida');
});

prueba('ipReal: si la conexión NO viene de loopback, X-Forwarded-For se ignora', () => {
    assert.equal(ipReal(pedido('198.51.100.4', '9.9.9.9')), '198.51.100.4');
    assert.equal(ipReal(pedido('::ffff:198.51.100.4', '9.9.9.9')), '198.51.100.4');
});
