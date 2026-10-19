// Teléfonos/JID y mensajes entrantes de Baileys: todo puro, sin red.
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const { soloDigitos, ultimosDiez, candidatos, jidDeNumero, esJidDeUsuario, numeroDeJid } = require("../baileys/numeros");
const { extraerTexto, clasificar, telefonoDeClave, interpretarEntrante, segundosDe, nombreDe, MAX_TEXTO, AVISOS } = require("../baileys/mensajes");

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

test("imágenes, audios, ubicaciones, reacciones y mensajes de protocolo no tienen TEXTO (extraerTexto: las fotos y los audios viajan como un aviso del tipo, ver clasificar)", () => {
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

test("un mensaje de una persona devuelve su teléfono, su texto, su tipo y su id", () => {
    assert.deepEqual(interpretarEntrante(msg({})), { telefono: "5491122334455", texto: "hola", tipo: "texto", id: "M1" });
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

test("lo que no es nada de lo que una persona escribió o está en blanco se ignora; una foto ya no (ver más abajo)", () => {
    assert.deepEqual(interpretarEntrante(msg({}, { reactionMessage: { text: "x" } })), { ignorar: "no_textual" });
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
    assert.deepEqual(interpretarEntrante(msg({ remoteJid: lid, senderPn: "5491122334455@s.whatsapp.net" })), { telefono: "5491122334455", texto: "hola", tipo: "texto", id: "M1" });
});

test("un @lid sin número resoluble se ignora: no se adivina un teléfono", () => {
    assert.deepEqual(interpretarEntrante(msg({ remoteJid: "99887766554433@lid" })), { ignorar: "sin_numero" });
    assert.equal(telefonoDeClave({ remoteJid: "99887766554433@lid" }), null);
});

test("el motivo de ignorar nunca incluye datos del paciente", () => {
    const r = interpretarEntrante(msg({ remoteJid: "120363000000000000@g.us" }, { conversation: "dato clínico 5491122334455" }));
    assert.doesNotMatch(JSON.stringify(r), /5491122334455|clínico/);
});

// ---------- Lo que no es texto: un aviso del tipo (decisión D4) ----------

const TIPOS = [
    ["imageMessage", "imagen", "[Imagen]"], ["videoMessage", "video", "[Video]"], ["audioMessage", "audio", "[Audio]"], ["documentMessage", "documento", "[Documento]"],
    ["stickerMessage", "sticker", "[Sticker]"], ["locationMessage", "ubicacion", "[Ubicación]"], ["liveLocationMessage", "ubicacion", "[Ubicación]"],
    ["contactMessage", "contacto", "[Contacto]"], ["contactsArrayMessage", "contacto", "[Contacto]"]
];

for (const [clave, tipo, aviso] of TIPOS) {
    test(`${clave} se reenvía como un aviso de tipo ${tipo}, sin descargar nada`, () => {
        assert.deepEqual(clasificar({ [clave]: {} }), { tipo, texto: aviso });
        assert.deepEqual(interpretarEntrante(msg({}, { [clave]: {} })), { telefono: "5491122334455", texto: aviso, tipo, id: "M1" });
    });
}

test("los avisos son siete, con corchetes, y distintos entre sí", () => {
    assert.equal(Object.keys(AVISOS).length, 7);
    for (const aviso of Object.values(AVISOS)) assert.match(aviso, /^\[[^\]]+\]$/);
    assert.equal(new Set(Object.values(AVISOS)).size, 7);
});

test("la leyenda de una foto, un video o un documento viaja a continuación del aviso", () => {
    assert.equal(clasificar({ imageMessage: { caption: "mi receta" } }).texto, "[Imagen] mi receta");
    assert.equal(clasificar({ videoMessage: { caption: "así me duele" } }).texto, "[Video] así me duele");
    assert.equal(clasificar({ documentMessage: { caption: "estudios" } }).texto, "[Documento] estudios");
    assert.equal(clasificar({ imageMessage: { caption: "  con espacios  " } }).texto, "[Imagen] con espacios");
});

test("una leyenda en blanco o que no es texto no agrega nada", () => {
    assert.equal(clasificar({ imageMessage: { caption: "   " } }).texto, "[Imagen]");
    assert.equal(clasificar({ imageMessage: { caption: 5 } }).texto, "[Imagen]");
    assert.equal(clasificar({ videoMessage: {} }).texto, "[Video]");
});

test("las notas de voz son audios y los GIF son videos", () => {
    assert.deepEqual(clasificar({ audioMessage: { ptt: true, seconds: 12 } }), { tipo: "audio", texto: "[Audio]" });
    assert.deepEqual(clasificar({ videoMessage: { gifPlayback: true } }), { tipo: "video", texto: "[Video]" });
});

test("lo que trae una ubicación, un contacto compartido o un archivo (nombre, dirección, tarjeta) NO viaja: solo el aviso", () => {
    const ubicacion = clasificar({ locationMessage: { name: "Casa de Juan", address: "Calle Falsa 123", degreesLatitude: -34.6, degreesLongitude: -58.4 } });
    const contacto = clasificar({ contactMessage: { displayName: "Juan Pérez", vcard: "BEGIN:VCARD\nTEL:+5491122334455\nEND:VCARD" } });
    const archivo = clasificar({ documentMessage: { fileName: "historia-clinica-juan.pdf", mimetype: "application/pdf" } });

    assert.deepEqual([ubicacion.texto, contacto.texto, archivo.texto], ["[Ubicación]", "[Contacto]", "[Documento]"]);
    assert.doesNotMatch(JSON.stringify([ubicacion, contacto, archivo]), /Juan|Calle|5491122334455|historia|pdf/);
});

test("las envolturas (efímero, ver una vez, documento con leyenda) también se desenvuelven para lo que no es texto", () => {
    assert.deepEqual(clasificar({ viewOnceMessageV2: { message: { imageMessage: { caption: "una vez" } } } }), { tipo: "imagen", texto: "[Imagen] una vez" });
    assert.deepEqual(clasificar({ ephemeralMessage: { message: { audioMessage: {} } } }), { tipo: "audio", texto: "[Audio]" });
    assert.deepEqual(clasificar({ documentWithCaptionMessage: { message: { documentMessage: { caption: "informe" } } } }), { tipo: "documento", texto: "[Documento] informe" });
});

test("reacciones, mensajes de protocolo, encuestas, claves de cifrado y mensajes vacíos no son algo que una persona escribió: se ignoran", () => {
    for (const mensaje of [{ reactionMessage: { text: "👍" } }, { protocolMessage: { type: 0 } }, { pollCreationMessage: { name: "¿?" } }, { senderKeyDistributionMessage: {} }, {}, null, undefined]) {
        assert.equal(clasificar(mensaje), null);
        if (mensaje !== undefined) assert.deepEqual(interpretarEntrante(msg({}, mensaje)), { ignorar: "no_textual" });
    }
});

test("el texto gana: si un mensaje trae texto y además un archivo, viaja el texto", () => {
    assert.deepEqual(clasificar({ conversation: "hola", imageMessage: {} }), { tipo: "texto", texto: "hola" });
});

test("un aviso con una leyenda larguísima se recorta a 4000 caracteres como el texto", () => {
    const entrante = interpretarEntrante(msg({}, { imageMessage: { caption: "x".repeat(MAX_TEXTO + 500) } }));
    assert.equal(entrante.texto.length, MAX_TEXTO);
    assert.ok(entrante.texto.startsWith("[Imagen] xxx"));
});

// ---------- La hora y el nombre del perfil ----------

test("la hora del mensaje viaja en segundos, venga como número, como Long de protobuf o como texto", () => {
    assert.equal(segundosDe(1760000000), 1760000000);
    assert.equal(segundosDe(1760000000.9), 1760000000);
    assert.equal(segundosDe({ toNumber: () => 1760000001, low: 5, high: 0 }), 1760000001);
    assert.equal(segundosDe("1760000002"), 1760000002);
});

test("una hora que no es un número razonable no viaja", () => {
    for (const marca of [undefined, null, 0, -5, NaN, Infinity, "abc", {}, [], { toNumber: () => NaN }]) assert.equal(segundosDe(marca), null, String(marca));
});

test("interpretarEntrante trae la hora si el mensaje la tiene y el nombre si trae pushName, y si no, esos campos no están", () => {
    assert.deepEqual(interpretarEntrante({ ...msg({}), messageTimestamp: 1760000123, pushName: "Juan Pérez" }),
        { telefono: "5491122334455", texto: "hola", tipo: "texto", id: "M1", ts: 1760000123, nombre: "Juan Pérez" });
    const sin = interpretarEntrante(msg({}));
    assert.equal("ts" in sin, false);
    assert.equal("nombre" in sin, false);
    assert.equal("nombre" in interpretarEntrante({ ...msg({}), pushName: "   " }), false);
});

test("el nombre del perfil se limpia: sin saltos de línea, sin invisibles que invierten o esconden texto, con los espacios juntos", () => {
    const c = cp => String.fromCodePoint(cp);
    assert.equal(nombreDe("  Juan   Pérez  "), "Juan Pérez");
    assert.equal(nombreDe("Juan\nPérez\t\r\n"), "Juan Pérez");
    assert.equal(nombreDe(`${c(0x202E)}Juan${c(0x202C)}`), "Juan");
    assert.equal(nombreDe(`Ju${c(0x200B)}an`), "Ju an");
    assert.equal(nombreDe(`${c(0xFEFF)}Juan`), "Juan");
});

test("el nombre del perfil conserva los emojis, incluidos los compuestos con unión de ancho cero", () => {
    const c = cp => String.fromCodePoint(cp);
    const familia = `${c(0x1F468)}${c(0x200D)}${c(0x1F469)}${c(0x200D)}${c(0x1F467)}`;
    assert.equal(nombreDe(`Ana ${c(0x1F338)}`), `Ana ${c(0x1F338)}`);
    assert.equal(nombreDe(`${familia} familia`), `${familia} familia`);
});

test("el nombre del perfil tiene a lo sumo 100 caracteres y no parte un emoji", () => {
    assert.equal(nombreDe("a".repeat(150)), "a".repeat(100));
    const flor = String.fromCodePoint(0x1F338);
    const cortado = nombreDe("a".repeat(99) + flor + " más");
    assert.equal(Array.from(cortado).length, 100);
    assert.equal(cortado, "a".repeat(99) + flor);
    assert.ok(cortado.isWellFormed(), "ninguna mitad de emoji suelta");
});

test("lo que no es un nombre (vacío, solo invisibles, no es texto) es null", () => {
    for (const valor of [undefined, null, 5, {}, "", "   ", "\n\t", String.fromCodePoint(0x200B) + String.fromCodePoint(0x202E)]) assert.equal(nombreDe(valor), null);
});

test("al ignorar un mensaje no viaja ni la hora ni el nombre del perfil", () => {
    const r = interpretarEntrante({ ...msg({ remoteJid: "120363000000000000@g.us" }), messageTimestamp: 1760000123, pushName: "Zoraida Xylofona" });
    assert.deepEqual(r, { ignorar: "grupo" });
    assert.doesNotMatch(JSON.stringify(r), /Zoraida|1760000123/);
});
