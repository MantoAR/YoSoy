// ServicioBaileys con el socket de Baileys simulado: ninguna conexión real, ningún mensaje real (la red está bloqueada, ver sin-red).
require("./helpers/sin-red");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { crearEntorno } = require("./helpers/entorno-baileys");
const { esperarHasta, drenar } = require("./helpers/reloj-falso");
const { DisconnectReason, errorBoom } = require("./helpers/baileys-falso");
const { crearSesion } = require("../baileys/sesion");

const QR = "ref-secreta-1,ruido,identidad,adv";
const TEL_PACIENTE = "5491122334455";

// Corre un test con su entorno y lo limpia siempre.
function conEntorno(nombre, opciones, cuerpo) {
    if (typeof opciones === "function") { cuerpo = opciones; opciones = {}; }
    test(nombre, async () => {
        const e = crearEntorno(opciones);
        try { await cuerpo(e); }
        finally { await e.servicio.cerrar().catch(() => {}); e.limpiar(); }
    });
}

// ---------- Arranque y QR ----------

conEntorno("sin sesión: crea un socket, queda esperando el QR y lo muestra en /estado como imagen", async e => {
    assert.equal(e.servicio.salud().estado, "detenido");
    const sock = await e.iniciar();

    assert.equal(e.falso.sockets.length, 1);
    assert.equal(e.servicio.salud().estado, "iniciando");

    sock.emitirQr(QR);
    const estado = await e.servicio.estado();
    assert.equal(estado.conectado, false);
    assert.equal(estado.qrPendiente, true);
    assert.match(estado.qr, /^data:image\/png;base64,/);
    assert.equal(estado.estado, "requiere_vincular");
    assert.equal(estado.motivo, "esperando_escaneo");
    assert.equal(e.servicio.salud().requiereVincular, true);
});

conEntorno("el QR es un secreto de vinculación: no se escribe en el log, ni entero ni a pedazos", async e => {
    const sock = await e.iniciar();
    sock.emitirQr(QR);
    sock.emitirQr("ref-secreta-2,ruido,identidad,adv");
    await e.servicio.estado();

    const log = e.logCompleto();
    assert.doesNotMatch(log, /ref-secreta|ruido|identidad|adv/);
    assert.equal(e.lineas.filter(l => /código QR/.test(l)).length, 1, "el aviso de que hay QR sale una sola vez por ventana");
});

conEntorno("con sesión: conecta, informa el número de la clínica y lo enmascara en el log", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir("5491199998888");

    const estado = await e.servicio.estado();
    assert.equal(estado.conectado, true);
    assert.equal(estado.numero, "5491199998888");
    assert.equal(estado.qrPendiente, false);
    assert.equal(estado.qr, null);
    assert.equal(e.servicio.salud().ok, true);

    assert.match(e.logCompleto(), /WhatsApp conectado \(número \*\*\*8888\)/);
    assert.doesNotMatch(e.logCompleto(), /5491199998888/);
});

conEntorno("al abrir se descarta el QR que hubiera quedado pendiente", async e => {
    const sock = await e.iniciar();
    sock.emitirQr(QR);
    sock.abrir();
    const estado = await e.servicio.estado();
    assert.equal(estado.qrPendiente, false);
    assert.equal(estado.qr, null);
});

// ---------- Cómo se crea el socket ----------

conEntorno("el socket se crea parecido a una persona y sin bajar historial", { registrada: true }, async e => {
    await e.iniciar();
    const c = e.falso.ultimo.config;

    assert.equal(c.markOnlineOnConnect, false);
    assert.equal(c.syncFullHistory, false);
    assert.equal(c.shouldSyncHistoryMessage(), false);
    assert.equal(c.generateHighQualityLinkPreview, false);
    assert.deepEqual(c.browser, ["Ubuntu", "YoSoy", "22.04.4"]);
    assert.equal(c.logger.level, "warn");
    assert.equal(await c.getMessage(), undefined);
    assert.equal("version" in c, false, "sin versión fijada se usa la que trae la librería");
    assert.equal(e.falso.llamadas.makeCacheableSignalKeyStore, 1);
    assert.equal(e.falso.llamadas.fetchLatestBaileysVersion, 0, "no se consulta nada a internet si no se pidió");
});

conEntorno("una versión de WhatsApp Web fijada a mano se pasa al socket", { opcionesServicio: { versionWa: [2, 3000, 99] } }, async e => {
    await e.iniciar();
    assert.deepEqual(e.falso.ultimo.config.version, [2, 3000, 99]);
    assert.equal(e.falso.llamadas.fetchLatestBaileysVersion, 0);
});

conEntorno("si se pide buscar la última versión, se usa la que contesta Baileys", { opcionesServicio: { buscarVersionWa: true } }, async e => {
    await e.iniciar();
    assert.deepEqual(e.falso.ultimo.config.version, [2, 3000, 1]);
    assert.equal(e.falso.llamadas.fetchLatestBaileysVersion, 1);
});

// ---------- Envío ----------

conEntorno("enviar: consulta cuál forma del número existe y manda el texto a ese JID", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    await e.servicio.enviar("1122334455", "Tenés turno mañana");

    assert.deepEqual(sock.consultas, [["5491122334455", "541122334455"]]);
    assert.deepEqual(sock.enviados, [{ jid: "5491122334455@s.whatsapp.net", contenido: { text: "Tenés turno mañana" } }]);
});

conEntorno("enviar recuerda el JID: al mismo paciente no se le vuelve a consultar a WhatsApp", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    await e.servicio.enviar("1122334455", "uno");
    await e.servicio.enviar("11 2233-4455", "dos");
    await e.servicio.enviar("+54 9 11 2233 4455", "tres");

    assert.equal(sock.consultas.length, 1);
    assert.equal(sock.enviados.length, 3);
});

conEntorno("un número sin WhatsApp da un error claro y se recuerda un rato para no insistir", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.todosExisten = false;

    await assert.rejects(() => e.servicio.enviar("1100000000", "hola"), /Ese número no tiene WhatsApp/);
    await assert.rejects(() => e.servicio.enviar("1100000000", "hola"), /Ese número no tiene WhatsApp/);

    assert.equal(sock.consultas.length, 1, "la segunda vez no se consulta");
    assert.equal(sock.enviados.length, 0);

    await e.reloj.avanzar(61 * 60 * 1000);   // pasada la hora, se vuelve a preguntar
    sock.todosExisten = true;
    await e.servicio.enviar("1100000000", "hola");
    assert.equal(sock.consultas.length, 2);
    assert.equal(sock.enviados.length, 1);
});

conEntorno("un teléfono imposible ni siquiera se consulta", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    await assert.rejects(() => e.servicio.enviar("123", "hola"), /Ese número no tiene WhatsApp/);
    assert.equal(sock.consultas.length, 0);
});

conEntorno("sin conexión, enviar falla en el acto y no toca el socket", { registrada: true }, async e => {
    await assert.rejects(() => e.servicio.enviar("1122334455", "hola"), /WhatsApp no está conectado/);   // todavía detenido
    const sock = await e.iniciar();                                                                       // iniciando, no abierto
    await assert.rejects(() => e.servicio.enviar("1122334455", "hola"), /WhatsApp no está conectado/);
    assert.equal(sock.enviados.length, 0);
});

conEntorno("un envío que se cuelga se corta a los 20 segundos y no deja trabado al servicio", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.envioColgado = true;

    const intento = assert.rejects(() => e.servicio.enviar("1122334455", "hola"), /Tiempo de espera agotado \(enviando mensaje\)/);
    await e.reloj.avanzar(20000);
    await intento;

    sock.envioColgado = false;
    await e.servicio.enviar("1122334455", "otra vez");
    assert.equal(sock.enviados.length, 1);
});

conEntorno("un error del socket al enviar sube tal cual, sin dejar el texto en el log", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.errorAlEnviar = new Error("rate-overlimit");
    await assert.rejects(() => e.servicio.enviar("1122334455", "texto privado"), /rate-overlimit/);
    assert.doesNotMatch(e.logCompleto(), /texto privado/);
});

// ---------- Entrantes ----------

conEntorno("un mensaje de texto de una persona se reenvía a la API con su teléfono completo", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    sock.recibir({ de: `${TEL_PACIENTE}@s.whatsapp.net`, texto: "Quiero un turno" });

    assert.deepEqual(e.reenviados, [{ telefono: TEL_PACIENTE, texto: "Quiero un turno" }]);
});

conEntorno("el texto y el teléfono de un entrante NO aparecen en el log", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.recibir({ de: `${TEL_PACIENTE}@s.whatsapp.net`, texto: "Tengo un problema de salud privado" });
    sock.recibir({ de: `${TEL_PACIENTE}@s.whatsapp.net`, mensaje: { imageMessage: {} } });

    assert.doesNotMatch(e.logCompleto(), /problema de salud|5491122334455/);
});

conEntorno("no se reenvía lo propio, los grupos, los estados, lo que no es texto ni el historial", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    sock.recibir({ clave: { fromMe: true } });
    sock.recibir({ de: "120363000000000000@g.us" });
    sock.recibir({ de: "status@broadcast" });
    sock.recibir({ mensaje: { audioMessage: {} } });
    sock.recibir({ tipo: "append" });

    assert.deepEqual(e.reenviados, []);
});

conEntorno("el mismo mensaje entregado dos veces se procesa una sola vez", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    sock.recibir({ id: "IGUAL-1", texto: "1" });
    sock.recibir({ id: "IGUAL-1", texto: "1" });
    sock.recibir({ id: "OTRO-2", texto: "1" });

    assert.equal(e.reenviados.length, 2);
});

conEntorno("un entrante mal formado no tira abajo al servicio", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.ev.emit("messages.upsert", { type: "notify", messages: [null, {}, { key: null }, undefined] });
    sock.ev.emit("messages.upsert", undefined);
    sock.ev.emit("messages.upsert", { type: "notify" });
    sock.recibir({ texto: "sigue andando" });
    assert.equal(e.reenviados.length, 1);
});

// ---------- Se cierra la sesión (loggedOut): sin reintentos en bucle ----------

conEntorno("loggedOut: queda en requiere_vincular, no reintenta (ni pasados 30 minutos) y lo avisa por /salud", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    sock.cerrar(DisconnectReason.loggedOut, "Intentional Logout");
    await e.reloj.avanzar(0);

    const salud = e.servicio.salud();
    assert.equal(salud.ok, false);
    assert.equal(salud.estado, "requiere_vincular");
    assert.equal(salud.motivo, "sesion_cerrada");
    assert.equal(salud.requiereVincular, true);
    assert.equal(salud.reconectando, false);

    await e.reloj.avanzar(30 * 60 * 1000);
    assert.equal(e.falso.sockets.length, 1, "no se creó ningún socket nuevo");
    assert.equal(e.reloj.pendientes(), 0, "no quedó ningún reintento programado");
    assert.match(e.logCompleto(), /WhatsApp cerró la sesión \(loggedOut\)/);
});

conEntorno("loggedOut borra las credenciales muertas con su respaldo (y solo ellos)", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.ev.emit("creds.update", {});             // un guardado deja el respaldo al lado de creds.json
    await esperarHasta(() => fs.existsSync(path.join(e.dirSesion, "creds.json.bak")), { descripcion: "el respaldo" });
    assert.ok(fs.existsSync(path.join(e.dirSesion, "creds.json")));

    sock.cerrar(DisconnectReason.loggedOut);
    await esperarHasta(() => fs.readdirSync(e.dirSesion).length === 0, { descripcion: "el borrado de la sesión" });

    assert.ok(fs.existsSync(e.dirSesion), "la carpeta en sí no se borra");
});

conEntorno("en requiere_vincular, enviar falla y recuperar() no reconecta", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.cerrar(DisconnectReason.loggedOut);
    await e.reloj.avanzar(0);

    await assert.rejects(() => e.servicio.enviar("1122334455", "hola"), /WhatsApp no está conectado/);
    e.servicio.recuperar();
    await e.reloj.avanzar(60000);
    assert.equal(e.falso.sockets.length, 1);
});

// ---------- Reconexión básica ----------

conEntorno("un corte transitorio pasa a reconectando y vuelve a conectar con un socket nuevo", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    sock.cerrar(DisconnectReason.connectionClosed);
    assert.equal(e.servicio.salud().estado, "reconectando");
    assert.equal(e.servicio.salud().reconectando, true);
    assert.equal(e.falso.sockets.length, 1);

    await e.reloj.avanzar(5000);
    await esperarHasta(() => e.falso.sockets.length === 2, { descripcion: "el segundo socket" });

    e.falso.ultimo.abrir();
    assert.equal(e.servicio.salud().estado, "conectado");
});

conEntorno("restartRequired (lo pide WhatsApp después de vincular) reconecta enseguida", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    sock.cerrar(DisconnectReason.restartRequired);
    await e.reloj.avanzar(0);
    await esperarHasta(() => e.falso.sockets.length === 2, { descripcion: "el segundo socket" });
});

conEntorno("vincular con éxito: se guardan las credenciales y el socket nuevo arranca con ellas", async e => {
    const sock = await e.iniciar();
    sock.emitirQr(QR);

    sock.vincularConExito("5491177776666");
    await e.reloj.avanzar(0);                // la reconexión que pide WhatsApp es inmediata (0 ms), pero va por el reloj
    await esperarHasta(() => e.falso.sockets.length === 2, { descripcion: "el socket posterior a la vinculación" });

    const guardadas = JSON.parse(fs.readFileSync(path.join(e.dirSesion, "creds.json"), "utf8"));
    assert.equal(guardadas.registered, true);
    assert.equal(guardadas.me.id, "5491177776666:7@s.whatsapp.net");
    assert.equal(e.falso.ultimo.config.auth.creds.registered, true, "el socket nuevo usa las credenciales ya vinculadas");

    e.falso.ultimo.abrir("5491177776666");
    const estado = await e.servicio.estado();
    assert.equal(estado.conectado, true);
    assert.equal(estado.numero, "5491177776666");
    assert.equal(estado.qr, null);
});

for (const [sufijo, opcionesFachada] of [
    ["", undefined],
    [" (aunque el emisor de eventos no deje quitar los listeners)", { conservaListeners: true }]
]) {
    conEntorno(`lo que digan los eventos de un socket viejo ya no cuenta${sufijo}`, { registrada: true, opcionesFachada }, async e => {
        const viejo = await e.iniciar();
        viejo.abrir();
        viejo.cerrar(DisconnectReason.connectionClosed);
        await e.reloj.avanzar(5000);
        await esperarHasta(() => e.falso.sockets.length === 2, { descripcion: "el socket nuevo" });

        // El viejo "resucita" y emite cosas: nada de eso debe cambiar el estado ni reenviar mensajes.
        viejo.ev.emit("connection.update", { connection: "open" });
        viejo.ev.emit("connection.update", { qr: QR });
        viejo.ev.emit("connection.update", { connection: "close", lastDisconnect: { error: errorBoom(DisconnectReason.loggedOut) } });
        viejo.recibir({ texto: "mensaje de un socket viejo" });
        assert.equal(e.servicio.salud().estado, "reconectando");
        assert.equal(e.servicio.salud().motivo, null);
        assert.deepEqual(e.reenviados, []);
        assert.equal((await e.servicio.estado()).qrPendiente, false);
        assert.equal(e.reloj.pendientes(), 0, "ningún evento del socket viejo programó un reintento");
    });
}

conEntorno("las credenciales de un socket viejo no resucitan una sesión que se borró", { registrada: true }, async e => {
    const viejo = await e.iniciar();
    viejo.abrir();
    await e.servicio.desvincular();
    assert.ok(!fs.existsSync(path.join(e.dirSesion, "creds.json")));

    viejo.ev.emit("creds.update", {});         // el socket descartado todavía tiene las credenciales viejas en memoria
    await e.servicio.cerrar();                 // espera lo que hubiera quedado por escribir

    assert.ok(!fs.existsSync(path.join(e.dirSesion, "creds.json")), "no se volvió a escribir creds.json");
});

conEntorno("si quedó un socket de antes, se lo descarta sin que su cierre dispare una reconexión: nunca hay dos a la vez", { registrada: true }, async e => {
    const primero = await e.iniciar();
    primero.abrir();

    await e.servicio._conectar();              // por la interfaz pública no se llega acá con un socket vivo: es la red de seguridad

    assert.equal(e.falso.sockets.length, 2);
    assert.ok(primero.vecesQueSeCerro >= 1, "el anterior se cerró");
    assert.equal(e.servicio.salud().reconectando, false, "su cierre no cuenta como un corte de la conexión");
    assert.equal(e.reloj.pendientes(), 0, "y no dejó ninguna reconexión programada");
});

conEntorno("si crear el socket falla, se reintenta solo (no queda muerto) y el error no filtra números", { registrada: true,
    opcionesFachada: { errorAlCrear: new Error("no se pudo abrir el socket de 5491122334455") } }, async e => {
    e.servicio.iniciar();
    await esperarHasta(() => e.lineas.some(l => /Error conectando con WhatsApp/.test(l)), { descripcion: "el error en el log" });

    assert.equal(e.servicio.salud().estado, "reconectando");
    assert.equal(e.reloj.pendientes(), 1);
    assert.doesNotMatch(e.logCompleto(), /5491122334455/);
});

// ---------- Guardado de credenciales ----------

conEntorno("creds.update guarda las credenciales; si falla, se loguea y el servicio sigue", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    sock.config.auth.creds.contador = 7;
    sock.ev.emit("creds.update", {});
    await esperarHasta(() => JSON.parse(fs.readFileSync(path.join(e.dirSesion, "creds.json"), "utf8")).contador === 7, { descripcion: "el guardado" });

    // Ahora el guardado falla: la carpeta pasa a ser un archivo.
    fs.rmSync(e.dirSesion, { recursive: true, force: true });
    fs.writeFileSync(e.dirSesion, "no es una carpeta");
    sock.ev.emit("creds.update", {});
    await esperarHasta(() => e.lineas.some(l => /No se pudieron guardar las credenciales/.test(l)), { descripcion: "el aviso de error" });
    assert.equal(e.servicio.salud().estado, "conectado");
});

// ---------- Desvincular ----------

conEntorno("desvincular: cierra la sesión en WhatsApp, borra las credenciales y deja un QR nuevo, sin reintentar el 401 propio", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    await e.servicio.desvincular();

    assert.equal(sock.cerroSesion, true);
    assert.ok(!fs.existsSync(path.join(e.dirSesion, "creds.json")), "las credenciales viejas se borraron");
    await esperarHasta(() => e.falso.sockets.length === 2, { descripcion: "el socket de la vinculación nueva" });
    assert.equal(e.falso.ultimo.config.auth.creds.registered, false, "el socket nuevo arranca sin sesión");

    e.falso.ultimo.emitirQr(QR);
    assert.equal((await e.servicio.estado()).qrPendiente, true);
    assert.equal(e.falso.sockets.length, 2, "el 401 del propio logout no disparó otra conexión");
    await e.reloj.avanzar(10 * 60 * 1000);
    assert.equal(e.falso.sockets.length, 2);
    assert.doesNotMatch(e.logCompleto(), /cerró la sesión|sesion_cerrada/, "una desvinculación pedida no se registra como una sesión cerrada por WhatsApp");
});

conEntorno("desvincular sin nada conectado (QR pendiente) no pide logout y genera un QR nuevo", async e => {
    const sock = await e.iniciar();
    sock.emitirQr(QR);

    await e.servicio.desvincular();

    assert.equal(sock.cerroSesion, false);
    await esperarHasta(() => e.falso.sockets.length === 2, { descripcion: "el socket nuevo" });
});

conEntorno("si el logout se cuelga, se sigue igual a los 15 segundos", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.logoutColgado = true;

    const terminado = e.servicio.desvincular();
    await e.reloj.avanzar(15000);
    await terminado;

    assert.match(e.logCompleto(), /Error en logout, se continúa con el borrado igual/);
    await esperarHasta(() => e.falso.sockets.length === 2, { descripcion: "el socket nuevo" });
});

conEntorno("si no se puede borrar la sesión, no se sigue como si nada: queda pidiendo intervención y el error sube", {
    registrada: true,
    sesionPersonalizada: (dir, log) => { const real = crearSesion({ dir, log }); return { dir, abrirAuth: f => real.abrirAuth(f), limpiar: async () => { throw new Error("EACCES"); } }; }
}, async e => {
    const sock = await e.iniciar();
    sock.abrir();

    await assert.rejects(() => e.servicio.desvincular(), /EACCES/);

    assert.equal(e.servicio.salud().estado, "requiere_vincular");
    assert.equal(e.servicio.salud().motivo, "error_borrando_sesion");
    assert.equal(e.falso.sockets.length, 1);
});

// ---------- Carreras ----------

test("desvincular espera lo que quedó por guardar antes de borrar la sesión: no la resucita", async () => {
    let soltarGuardado;
    const compuertaGuardado = new Promise(resolver => { soltarGuardado = resolver; });
    let seLlamoAlBorrado = false;
    const e = crearEntorno({
        registrada: true,
        opcionesFachada: { compuertaGuardado },
        sesionPersonalizada: (dir, log) => {
            const real = crearSesion({ dir, log });
            return { dir, abrirAuth: f => real.abrirAuth(f), limpiar: () => { seLlamoAlBorrado = true; return real.limpiar(); } };
        }
    });
    try {
        const sock = await e.iniciar();
        sock.abrir();
        sock.config.auth.creds.contador = 99;
        sock.ev.emit("creds.update", {});                      // el guardado queda esperando la compuerta

        const desvinculado = e.servicio.desvincular();
        await drenar();
        assert.equal(seLlamoAlBorrado, false, "todavía no se borró nada: se espera a que termine de guardar");

        soltarGuardado();
        await desvinculado;
        await esperarHasta(() => e.falso.sockets.length === 2, { descripcion: "el socket de la vinculación nueva" });
        assert.ok(!fs.existsSync(path.join(e.dirSesion, "creds.json")), "las credenciales viejas no resucitaron");
        assert.equal(e.falso.ultimo.config.auth.creds.registered, false);
    } finally {
        await e.servicio.cerrar().catch(() => {});
        e.limpiar();
    }
});

// Una sesión cuyo primer abrirAuth (y su limpiar) esperan compuertas que abre el test: así el orden de las carreras queda fijo.
function sesionConCompuertas() {
    const c = { llamadasAbrirAuth: 0 };
    const compuertaAbrirAuth = new Promise(resolver => { c.soltarAbrirAuth = resolver; });
    const compuertaLimpiar = new Promise(resolver => { c.soltarLimpiar = resolver; });
    c.terminoPrimerAbrirAuth = new Promise(resolver => { c.avisarPrimerAbrirAuth = resolver; });
    c.sesionPersonalizada = (dir, log) => {
        const real = crearSesion({ dir, log });
        return {
            dir,
            abrirAuth: async f => {
                const esLaPrimera = ++c.llamadasAbrirAuth === 1;
                if (esLaPrimera) await compuertaAbrirAuth;
                const resultado = await real.abrirAuth(f);
                if (esLaPrimera) c.avisarPrimerAbrirAuth();
                return resultado;
            },
            limpiar: async () => { await compuertaLimpiar; return real.limpiar(); }
        };
    };
    return c;
}

// El intento de conectar queda detenido en abrirAuth y reanuda MIENTRAS desvincular() sigue esperando que se borre la sesión. Si
// desvincular() no lo invalidara por sí mismo, ese intento crearía un socket con las credenciales que se están borrando.
test("desvincular mientras se estaba conectando: el intento viejo se descarta, sin crear un socket con las credenciales viejas", async () => {
    const c = sesionConCompuertas();
    const e = crearEntorno({ registrada: true, sesionPersonalizada: c.sesionPersonalizada });
    try {
        e.servicio.iniciar();
        await drenar();
        assert.equal(c.llamadasAbrirAuth, 1, "el intento 1 está esperando en abrirAuth");

        const desvinculado = e.servicio.desvincular();         // lo invalida y queda esperando en limpiar
        await drenar();

        c.soltarAbrirAuth();                                   // el intento 1 reanuda con desvincular() todavía a medias
        await c.terminoPrimerAbrirAuth;
        await drenar();
        assert.equal(e.falso.sockets.length, 0, "el intento viejo no creó ningún socket");

        c.soltarLimpiar();
        await desvinculado;
        await esperarHasta(() => e.falso.sockets.length >= 1, { descripcion: "el socket de la vinculación nueva" });
        await e.reloj.avanzar(0);
        assert.equal(e.falso.sockets.length, 1, "un solo socket, el de la vinculación nueva");
        assert.equal(e.falso.ultimo.config.auth.creds.registered, false, "y arranca sin las credenciales viejas");
    } finally {
        await e.servicio.cerrar().catch(() => {});
        e.limpiar();
    }
});

test("cerrar mientras se estaba conectando: el intento que quedó a medias no crea ningún socket después", async () => {
    const c = sesionConCompuertas();
    const e = crearEntorno({ registrada: true, sesionPersonalizada: c.sesionPersonalizada });
    try {
        e.servicio.iniciar();
        await drenar();
        assert.equal(c.llamadasAbrirAuth, 1, "el intento está esperando en abrirAuth");

        const cerrado = e.servicio.cerrar();
        c.soltarAbrirAuth();
        await cerrado;
        await c.terminoPrimerAbrirAuth;
        await drenar();
        await e.reloj.avanzar(60000);

        assert.equal(e.falso.sockets.length, 0);
        assert.equal(e.servicio.salud().estado, "detenido");
    } finally {
        e.limpiar();
    }
});

// ---------- Apagado ----------

conEntorno("cerrar: suelta el socket, cancela reconexiones y queda detenido", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.cerrar(DisconnectReason.connectionClosed);          // deja una reconexión programada
    assert.equal(e.reloj.pendientes(), 1);

    await e.servicio.cerrar();

    assert.equal(e.reloj.pendientes(), 0);
    assert.equal(e.servicio.salud().estado, "detenido");
    await e.reloj.avanzar(10 * 60 * 1000);
    assert.equal(e.falso.sockets.length, 1);
});

conEntorno("cerrar con el socket abierto lo cierra (end) y espera lo que haya por guardar", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    sock.config.auth.creds.contador = 42;
    sock.ev.emit("creds.update", {});

    await e.servicio.cerrar();

    assert.equal(sock.vecesQueSeCerro >= 1, true);
    assert.equal(JSON.parse(fs.readFileSync(path.join(e.dirSesion, "creds.json"), "utf8")).contador, 42, "lo pendiente se escribió antes de salir");
    assert.equal(e.servicio.salud().estado, "detenido");
});

const aperturas = { cuenta: 0 };
conEntorno("iniciar dos veces no crea dos conexiones", {
    registrada: true,
    sesionPersonalizada: (dir, log) => {
        const real = crearSesion({ dir, log });
        return { dir, abrirAuth: f => { aperturas.cuenta++; return real.abrirAuth(f); }, limpiar: () => real.limpiar() };
    }
}, async e => {
    await e.iniciar();
    e.servicio.iniciar();
    await e.reloj.avanzar(0);
    assert.equal(aperturas.cuenta, 1, "la segunda llamada ni siquiera intentó abrir la sesión");
    assert.equal(e.falso.sockets.length, 1);
});

// ---------- recuperar() ----------

conEntorno("recuperar(): con el servicio andando no hace nada; sin socket ni reintento programado, reintenta", { registrada: true }, async e => {
    const sock = await e.iniciar();
    sock.abrir();
    e.servicio.recuperar();
    assert.equal(e.reloj.pendientes(), 0, "conectado: nada que recuperar");

    sock.cerrar(DisconnectReason.connectionClosed);
    const antes = e.reloj.pendientes();
    e.servicio.recuperar();
    assert.equal(e.reloj.pendientes(), antes, "ya hay un reintento programado: no se agrega otro");
});
