// Tests unitarios de leerConfig (sin red).
'use strict';

const assert = require('node:assert/strict');
const { leerConfig, leerSecreto } = require('../config');
const { prueba } = require('./ayudas');

const SECRETO_OK = 'x'.repeat(32);

prueba('los valores por defecto: loopback, puerto 3001, sin CORS y límites chicos', () => {
    const c = leerConfig({ SIGNALING_SECRETO: SECRETO_OK });
    assert.equal(c.host, '127.0.0.1');
    assert.equal(c.puerto, 3001);
    assert.deepEqual(c.origenesCors, []);
    assert.equal(c.maxPayloadBytes, 64 * 1024);
    assert.equal(c.maxHandshakesPorMin, 60);
    assert.equal(c.maxEventosPor10s, 100);
    assert.ok(Object.isFrozen(c));
});

prueba('sin SIGNALING_SECRETO no hay configuración: ni vacío ni solo espacios de más', () => {
    assert.throws(() => leerConfig({}), /Falta SIGNALING_SECRETO/);
    assert.throws(() => leerConfig({ SIGNALING_SECRETO: '' }), /Falta SIGNALING_SECRETO/);
    assert.throws(() => leerSecreto({}), /Falta SIGNALING_SECRETO/);
});

prueba('un secreto de menos de 32 caracteres es débil y no se acepta; con 32 sí', () => {
    assert.throws(() => leerConfig({ SIGNALING_SECRETO: 'x'.repeat(31) }), /demasiado corto \(31 caracteres; mínimo 32\)/);
    assert.equal(leerConfig({ SIGNALING_SECRETO: 'x'.repeat(32) }).secreto, 'x'.repeat(32));
});

prueba('el secreto de la señalización no puede ser el mismo que Jwt__Key de la API', () => {
    const igual = 'k'.repeat(40);
    assert.throws(() => leerConfig({ SIGNALING_SECRETO: igual, Jwt__Key: igual }), /mismo valor que Jwt__Key/);
    assert.equal(leerConfig({ SIGNALING_SECRETO: igual, Jwt__Key: 'j'.repeat(40) }).secreto, igual);
    try {
        leerConfig({ SIGNALING_SECRETO: igual, Jwt__Key: igual });
    } catch (e) {
        assert.ok(!e.message.includes(igual));
    }
});

prueba('los mensajes de error nunca incluyen el valor del secreto', () => {
    const corto = 'S3cr3t0-corto-y-real';
    try {
        leerConfig({ SIGNALING_SECRETO: corto });
        assert.fail('tendría que haber tirado');
    } catch (e) {
        assert.ok(!e.message.includes(corto));
    }
});

prueba('HOST y PORT se pueden cambiar; un PORT que no es un puerto, no', () => {
    const c = leerConfig({ SIGNALING_SECRETO: SECRETO_OK, HOST: '10.0.0.5', PORT: '4000' });
    assert.equal(c.host, '10.0.0.5');
    assert.equal(c.puerto, 4000);
    assert.equal(leerConfig({ SIGNALING_SECRETO: SECRETO_OK, HOST: '   ' }).host, '127.0.0.1');
    for (const malo of ['abc', '-1', '70000', '3001x', '30.5', '0x10']) {
        assert.throws(() => leerConfig({ SIGNALING_SECRETO: SECRETO_OK, PORT: malo }), /PORT tiene que ser un entero/, malo);
    }
});

prueba('CORS: lista explícita de orígenes, nunca "*"', () => {
    const c = leerConfig({ SIGNALING_SECRETO: SECRETO_OK, SIGNALING_ORIGENES_CORS: 'http://localhost:5000, https://yosoytelemedicina.tech' });
    assert.deepEqual(c.origenesCors, ['http://localhost:5000', 'https://yosoytelemedicina.tech']);
    assert.deepEqual(leerConfig({ SIGNALING_SECRETO: SECRETO_OK, SIGNALING_ORIGENES_CORS: '  ' }).origenesCors, []);
    assert.throws(() => leerConfig({ SIGNALING_SECRETO: SECRETO_OK, SIGNALING_ORIGENES_CORS: '*' }), /no admite "\*"/);
    assert.throws(() => leerConfig({ SIGNALING_SECRETO: SECRETO_OK, SIGNALING_ORIGENES_CORS: 'http://a.com,*' }), /no admite "\*"/);
    for (const malo of ['localhost:5000', 'http://a.com/', 'http://a.com/ruta', 'ftp://a.com', 'a.com']) {
        assert.throws(() => leerConfig({ SIGNALING_SECRETO: SECRETO_OK, SIGNALING_ORIGENES_CORS: malo }), /no es un origen válido/, malo);
    }
});

prueba('los límites se pueden ajustar, dentro de un rango razonable', () => {
    const c = leerConfig({
        SIGNALING_SECRETO: SECRETO_OK,
        SIGNALING_MAX_PAYLOAD_BYTES: '32768',
        SIGNALING_MAX_HANDSHAKES_POR_MIN: '10',
        SIGNALING_MAX_EVENTOS_POR_10S: '50'
    });
    assert.equal(c.maxPayloadBytes, 32768);
    assert.equal(c.maxHandshakesPorMin, 10);
    assert.equal(c.maxEventosPor10s, 50);
    assert.throws(() => leerConfig({ SIGNALING_SECRETO: SECRETO_OK, SIGNALING_MAX_PAYLOAD_BYTES: '10' }), /SIGNALING_MAX_PAYLOAD_BYTES/);
    assert.throws(() => leerConfig({ SIGNALING_SECRETO: SECRETO_OK, SIGNALING_MAX_HANDSHAKES_POR_MIN: '0' }), /SIGNALING_MAX_HANDSHAKES_POR_MIN/);
});
