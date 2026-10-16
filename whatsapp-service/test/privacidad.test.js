// Privacidad de los logs del whatsapp-service: el teléfono de un paciente no se escribe completo (sin red, sin WhatsApp).
// Se corre con `npm test` (node --test), igual que el servidor de señalización.
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { ocultarNumeros } = require('../privacidad');
const { ColaEnvios } = require('../cola');

// ---------- ocultarNumeros ----------

test('un número solo deja los últimos 4 dígitos', () => {
    assert.equal(ocultarNumeros('1122334455'), '***4455');
    assert.equal(ocultarNumeros('5491122334455'), '***4455');
});

test('con "+", espacios, guiones y paréntesis sueltos también', () => {
    assert.equal(ocultarNumeros('+54 9 11 2233-4455'), '***4455');
    assert.equal(ocultarNumeros('+5491122334455'), '***4455');
    assert.equal(ocultarNumeros('11 5555-0001'), '***0001');
    assert.equal(ocultarNumeros('(011) 4455-6677'), '(011) ***6677');       // del código de área queda lo que no llega a 7 dígitos
});

test('los identificadores de WhatsApp (@c.us y @lid) conservan el sufijo', () => {
    assert.equal(ocultarNumeros('5491122334455@c.us'), '***4455@c.us');
    assert.equal(ocultarNumeros('10234567890123@lid'), '***0123@lid');
});

test('un texto con varios números los oculta todos y deja el resto', () => {
    assert.equal(
        ocultarNumeros('Error enviando cola-12 a 1122334455: no existe 5491166667777'),
        'Error enviando cola-12 a ***4455: no existe ***7777');
});

test('lo que no es un número de teléfono no se toca: puertos, ids cortos, texto sin dígitos', () => {
    assert.equal(ocultarNumeros('escuchando en 127.0.0.1:3002'), 'escuchando en 127.0.0.1:3002');
    assert.equal(ocultarNumeros('cola-12'), 'cola-12');
    assert.equal(ocultarNumeros('123456'), '123456');                        // 6 dígitos: no alcanza
    assert.equal(ocultarNumeros('Falló el envío'), 'Falló el envío');
    assert.equal(ocultarNumeros('loading_screen 87%'), 'loading_screen 87%');
});

test('7 dígitos es el piso', () => {
    assert.equal(ocultarNumeros('1234567'), '***4567');
});

test('null, undefined y vacío dan un texto vacío y no tiran', () => {
    assert.equal(ocultarNumeros(null), '');
    assert.equal(ocultarNumeros(undefined), '');
    assert.equal(ocultarNumeros(''), '');
});

test('acepta cosas que no son texto', () => {
    assert.equal(ocultarNumeros(5491122334455), '***4455');
    assert.equal(ocultarNumeros(new Error('falló 1122334455')), 'Error: falló ***4455');
});

// ---------- La cola: el error de un envío no deja el teléfono en el log ----------

async function esperarQueTermine(cola) {
    for (let i = 0; i < 200; i++) {
        if (!cola._procesando && cola.tamano() === 0) return;
        await new Promise(resolve => setTimeout(resolve, 10));
    }
    throw new Error('La cola no terminó de procesar.');
}

test('si un envío falla, el log trae el id y el final del teléfono, no el teléfono', async () => {
    const mensajes = [];
    const log = { info: m => mensajes.push(m), error: m => mensajes.push(m) };
    const cola = new ColaEnvios(async () => { throw new Error('No se pudo enviar a 5491122334455'); }, log);

    const { idExterno } = cola.encolar('1122334455', 'Hola, tenés turno mañana');
    await esperarQueTermine(cola);

    assert.equal(mensajes.length, 1);
    assert.match(mensajes[0], new RegExp(idExterno));
    assert.match(mensajes[0], /\*\*\*4455/);
    assert.doesNotMatch(mensajes[0], /1122334455/);          // ni el número que mandó la API...
    assert.doesNotMatch(mensajes[0], /5491122334455/);       // ...ni el que repite el mensaje de error
    assert.doesNotMatch(mensajes[0], /tenés turno/);         // y nunca el texto del mensaje
});

test('si el envío sale bien no se loguea nada', async () => {
    const mensajes = [];
    const cola = new ColaEnvios(async () => {}, { info: m => mensajes.push(m), error: m => mensajes.push(m) });

    cola.encolar('1122334455', 'Hola');
    await esperarQueTermine(cola);

    assert.deepEqual(mensajes, []);
});

// ---------- Guardia: ninguna llamada de log de este servicio interpola un teléfono sin enmascarar ----------

test('toda llamada de log que interpola un teléfono o un remitente pasa por ocultarNumeros', () => {
    const carpeta = path.join(__dirname, '..');
    const archivos = fs.readdirSync(carpeta).filter(f => f.endsWith('.js'));
    const llamadaDeLog = /\b(?:this\._log|_log|log)\.(?:info|warn|error|debug)\(/;
    const interpolaTelefono = /\$\{[^}]*(?:telefono|\.from\b)[^}]*\}/i;
    const sinEnmascarar = [];

    for (const archivo of archivos) {
        fs.readFileSync(path.join(carpeta, archivo), 'utf8').split(/\r?\n/).forEach((linea, i) => {
            if (llamadaDeLog.test(linea) && interpolaTelefono.test(linea) && !linea.includes('ocultarNumeros('))
                sinEnmascarar.push(`${archivo}:${i + 1}`);
        });
    }

    assert.deepEqual(sinEnmascarar, [], `Estas llamadas de log interpolan un teléfono sin ocultarNumeros(): ${sinEnmascarar.join(', ')}`);
});

test('el detector de la guardia reconoce una llamada sin enmascarar', () => {
    const llamadaDeLog = /\b(?:this\._log|_log|log)\.(?:info|warn|error|debug)\(/;
    const interpolaTelefono = /\$\{[^}]*(?:telefono|\.from\b)[^}]*\}/i;
    const mala = 'this._log.error(`Error enviando a ${item.telefono}`);';
    const buena = 'this._log.error(`Error enviando a ${ocultarNumeros(item.telefono)}`);';

    assert.ok(llamadaDeLog.test(mala) && interpolaTelefono.test(mala) && !mala.includes('ocultarNumeros('));
    assert.ok(llamadaDeLog.test(buena) && interpolaTelefono.test(buena) && buena.includes('ocultarNumeros('));
});
