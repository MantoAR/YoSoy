// Teléfonos/JID y mensajes entrantes de Baileys: todo puro, sin red.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const { soloDigitos, ultimosDiez, candidatos, jidDeNumero, esJidDeUsuario, numeroDeJid } = require("../baileys/numeros");
const { extraerTexto, telefonoDeClave, interpretarEntrante, MAX_TEXTO } = require("../baileys/mensajes");

// ---------- Números ----------

test("ultimosDiez hace lo mismo que Telefono.Normalizar de la API", () => {
    assert.equal(ultimosDiez("+54 9 11 5555-0001"), "1155550001");
    assert.equal(ultimosDiez("11 5555-0001"), "1155550001");
    assert.equal(ultimosDiez("5491155550001"), "1155550001");
    assert.equal(ultimosDiez("12345"), "12345");
    assert.equal(ultimosDiez(null), "");
});

test("un teléfono de 10 dígitos se prueba como celular argentino con 9 y sin 9", () => {
    assert.deepEqual(candidatos("1122334455"), ["5491122334455", "541122334455"]);
    assert.deepEqual(candidatos("11 2233-4455"), ["5491122334455", "541122334455"]);
});

test("con el prefijo de país se respeta y se ofrece la otra forma", () => {
    assert.deepEqual(candidatos("5491122334455"), ["5491122334455", "541122334455"]);
    assert.deepEqual(candidatos("541122334455"), ["5491122334455", "541122334455"]);
    assert.deepEqual(candidatos("+54 9 11 2233-4455"), ["5491122334455", "541122334455"]);
});

test("con el 0 de discado nacional se lo saca", () => {
    assert.deepEqual(candidatos("01122334455"), ["5491122334455", "541122334455"]);
});

test("un número de otro país se usa tal cual", () => {
    assert.deepEqual(candidatos("+598 99 123 456"), ["59899123456"]);
});

test("lo que no puede ser un teléfono no da candidatos", () => {
    assert.deepEqual(candidatos(""), []);
    assert.deepEqual(candidatos("123456"), []);
    assert.deepEqual(candidatos("1234567890123456"), []);
    assert.deepEqual(candidatos(undefined), []);
    assert.deepEqual(candidatos("abc"), []);
});

test("numeroDeJid saca el número con o sin el dispositivo", () => {
    assert.equal(numeroDeJid("5491122334455@s.whatsapp.net"), "5491122334455");
    assert.equal(numeroDeJid("5491122334455:12@s.whatsapp.net"), "5491122334455");
    assert.equal(numeroDeJid("abc@s.whatsapp.net"), null);
    assert.equal(numeroDeJid(undefined), null);
    assert.equal(numeroDeJid(""), null);
});

test("jidDeNumero y esJidDeUsuario", () => {
    assert.equal(jidDeNumero("+54 9 11 2233-4455"), "5491122334455@s.whatsapp.net");
    assert.ok(esJidDeUsuario("5491122334455@s.whatsapp.net"));
    assert.ok(!esJidDeUsuario("123@g.us"));
    assert.ok(!esJidDeUsuario("123@lid"));
    assert.ok(!esJidDeUsuario(null));
    assert.equal(soloDigitos("a1b2-3"), "123");
});

// ---------- Texto del mensaje ----------

test("el texto sale de conversation y de extendedTextMessage (respuestas, links, menciones)", () => {
    assert.equal(extraerTexto({ conversation: "hola" }), "hola");
    assert.equal(extraerTexto({ extendedTextMessage: { text: "con link https://x.test" } }), "con link https://x.test");
});

test("los mensajes efímeros, de ver una vez y documentos con leyenda se desenvuelven", () => {
    assert.equal(extraerTexto({ ephemeralMessage: { message: { conversation: "efímero" } } }), "efímero");
    assert.equal(extraerTexto({ viewOnceMessageV2: { message: { extendedTextMessage: { text: "una vez" } } } }), "una vez");
    assert.equal(extraerTexto({ ephemeralMessage: { message: { viewOnceMessage: { message: { conversation: "doble envoltorio" } } } } }), "doble envoltorio");
});

test("imágenes, audios, ubicaciones, reacciones y mensajes de protocolo no tienen texto que el bot entienda", () => {
    assert.equal(extraerTexto({ imageMessage: { caption: "foto" } }), null);
    assert.equal(extraerTexto({ audioMessage: {} }), null);
    assert.equal(extraerTexto({ locationMessage: {} }), null);
    assert.equal(extraerTexto({ reactionMessage: { text: "👍" } }), null);
    assert.equal(extraerTexto({ protocolMessage: { type: 0 } }), null);
    assert.equal(extraerTexto(null), null);
    assert.equal(extraerTexto(undefined), null);
});

// ---------- Qué se ignora y de quién es ----------

function msg(clave, mensaje = { conversation: "hola" }) { return { key: { remoteJid: "5491122334455@s.whatsapp.net", fromMe: false, id: "M1", ...clave }, message: mensaje }; }

test("un mensaje de una persona devuelve su teléfono, su texto y su id", () => {
    assert.deepEqual(interpretarEntrante(msg({})), { telefono: "5491122334455", texto: "hola", id: "M1" });
});

test("se ignoran los propios, los de grupos, difusiones, estados y canales", () => {
    assert.deepEqual(interpretarEntrante(msg({ fromMe: true })), { ignorar: "propio" });
    assert.deepEqual(interpretarEntrante(msg({ remoteJid: "120363000000000000@g.us" })), { ignorar: "grupo" });
    assert.deepEqual(interpretarEntrante(msg({ remoteJid: "status@broadcast" })), { ignorar: "difusion" });
    assert.deepEqual(interpretarEntrante(msg({ remoteJid: "5491122334455@broadcast" })), { ignorar: "difusion" });
    assert.deepEqual(interpretarEntrante(msg({ remoteJid: "120363000000@newsletter" })), { ignorar: "canal" });
});

test("sin clave o con una clave rota no tira: se ignora", () => {
    assert.deepEqual(interpretarEntrante(undefined), { ignorar: "sin_clave" });
    assert.deepEqual(interpretarEntrante({}), { ignorar: "sin_clave" });
    assert.deepEqual(interpretarEntrante({ key: {} }), { ignorar: "sin_clave" });
    assert.deepEqual(interpretarEntrante({ key: { remoteJid: 5 } }), { ignorar: "sin_clave" });
});

test("lo que no es texto o está en blanco se ignora", () => {
    assert.deepEqual(interpretarEntrante(msg({}, { imageMessage: {} })), { ignorar: "no_textual" });
    assert.deepEqual(interpretarEntrante(msg({}, { conversation: "   \n " })), { ignorar: "vacio" });
    assert.deepEqual(interpretarEntrante({ key: msg({}).key }), { ignorar: "no_textual" });
});

test("el texto se recorta de espacios y a 4000 caracteres", () => {
    assert.equal(interpretarEntrante(msg({}, { conversation: "  hola  " })).texto, "hola");
    const largo = interpretarEntrante(msg({}, { conversation: "x".repeat(MAX_TEXTO + 500) })).texto;
    assert.equal(largo.length, MAX_TEXTO);
});

test("un remitente anónimo (@lid) se resuelve por el número que trae la clave, según la versión de Baileys", () => {
    const lid = "99887766554433@lid";
    // Baileys 6.x
    assert.equal(telefonoDeClave({ remoteJid: lid, senderPn: "5491122334455@s.whatsapp.net" }), "5491122334455");
    // Baileys 7.x
    assert.equal(telefonoDeClave({ remoteJid: lid, remoteJidAlt: "5491122334455@s.whatsapp.net" }), "5491122334455");
    assert.deepEqual(interpretarEntrante(msg({ remoteJid: lid, senderPn: "5491122334455@s.whatsapp.net" })), { telefono: "5491122334455", texto: "hola", id: "M1" });
});

test("un @lid sin número resoluble se ignora: no se adivina un teléfono", () => {
    assert.deepEqual(interpretarEntrante(msg({ remoteJid: "99887766554433@lid" })), { ignorar: "sin_numero" });
    assert.equal(telefonoDeClave({ remoteJid: "99887766554433@lid" }), null);
});

test("el motivo de ignorar nunca incluye datos del paciente", () => {
    const r = interpretarEntrante(msg({ remoteJid: "120363000000000000@g.us" }, { conversation: "dato clínico 5491122334455" }));
    assert.doesNotMatch(JSON.stringify(r), /5491122334455|clínico/);
});
