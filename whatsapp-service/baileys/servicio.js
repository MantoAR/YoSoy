// Backend de WhatsApp con Baileys (WhatsApp Web sin navegador). Misma interfaz que el de whatsapp-web.js (ver backend.js).
//
// Estados (los que informa GET /salud y GET /estado):
//   detenido           todavía no se llamó a iniciar(), o ya se apagó
//   iniciando          abriendo la sesión y creando el socket
//   conectado          listo para enviar y recibir
//   reconectando       se cortó y se reintenta solo (con espera creciente)
//   requiere_vincular  no hay sesión válida: hace falta escanear un QR (motivo dice por qué). NO se reintenta en bucle.
//
// Reglas que este archivo respeta:
//   - Baileys llega por la "fachada" que se le inyecta (nunca se importa acá): los tests le pasan una falsa.
//   - Un socket cerrado no se reutiliza: cada conexión crea uno nuevo, y los eventos de uno viejo se ignoran.
//   - Privacidad: no se loguea el texto de ningún mensaje, ni un número completo de un paciente, ni el QR, ni credenciales.
//   - Todo lo que depende del tiempo usa el reloj inyectado (en producción, el real).
"use strict";

const QRCode = require("qrcode");
const { ocultarNumeros } = require("../privacidad");
const { candidatos, ultimosDiez, numeroDeJid } = require("./numeros");
const { interpretarEntrante } = require("./mensajes");
const { crearLoggerBaileys } = require("./logger");

const ESTADOS = Object.freeze({
    DETENIDO: "detenido",
    INICIANDO: "iniciando",
    CONECTADO: "conectado",
    RECONECTANDO: "reconectando",
    REQUIERE_VINCULAR: "requiere_vincular"
});

const RELOJ_REAL = Object.freeze({
    setTimeout: (...a) => setTimeout(...a),
    clearTimeout: (...a) => clearTimeout(...a),
    ahora: () => Date.now()
});

const TIMEOUT_ENVIO_MS = 20000;
const TIMEOUT_CONSULTA_MS = 15000;
const TIMEOUT_LOGOUT_MS = 15000;
const VIGENCIA_JID_MS = 24 * 60 * 60 * 1000;      // un número que existe en WhatsApp se recuerda un día
const VIGENCIA_SIN_WHATSAPP_MS = 60 * 60 * 1000;  // uno que no existe, una hora (menos consultas a los servidores de WhatsApp)
const MAX_VISTOS = 1000;
const VIGENCIA_VISTOS_MS = 60 * 60 * 1000;

// Baileys cierra con un Boom: el código HTTP está en error.output.statusCode.
function codigoDeError(error) {
    const c = error?.output?.statusCode ?? error?.statusCode ?? error?.data?.statusCode;
    return typeof c === "number" ? c : null;
}

class ServicioBaileys {
    constructor({ fachada, sesion, log, reenviar, reloj = RELOJ_REAL, versionWa = null, buscarVersionWa = false, navegador = "YoSoy" }) {
        this._f = fachada;
        this._sesion = sesion;
        this._log = log;
        this._reenviar = reenviar;                 // (telefono, texto) => void: hacia la API de YoSoy
        this._reloj = reloj;
        this._versionWa = versionWa;
        this._buscarVersionWa = buscarVersionWa;
        this._navegador = navegador;
        this._loggerBaileys = crearLoggerBaileys(log, { ahora: () => reloj.ahora() });

        this._estado = ESTADOS.DETENIDO;
        this._motivo = null;
        this._sock = null;
        this._qr = null;
        this._qrAvisado = false;
        this._numero = null;
        this._cerrando = false;
        this._desvinculando = false;
        this._generacion = 0;                      // cada intento de conectar (o desvincular/cerrar) invalida a los anteriores
        this._temporizadorReconexion = null;
        this._guardado = Promise.resolve();        // las escrituras de credenciales se encadenan: no se pisan ni se pierden
        this._vistos = new Map();                  // id del mensaje → cuándo se vio (para no procesar dos veces el mismo)
        this._jids = new Map();                    // últimos 10 dígitos → { jid, hasta }
    }

    // ── Ciclo de vida ───────────────────────────────────────────────────────────────

    iniciar() {
        if (this._estado !== ESTADOS.DETENIDO) return;
        this._cerrando = false;
        this._cambiarEstado(ESTADOS.INICIANDO, null);
        this._conectar().catch(err => this._falloAlConectar(err));
    }

    // Apagado ordenado (SIGTERM): deja de reconectar, suelta el socket y espera a que termine de escribir lo pendiente.
    async cerrar() {
        this._cerrando = true;
        this._cancelarReconexion();
        const sock = this._sock;
        this._sock = null;
        this._qr = null;
        this._numero = null;
        this._cambiarEstado(ESTADOS.DETENIDO, null);
        this._soltar(sock);
        await this._guardado;
    }

    // Red de seguridad ante un error sin capturar del proceso (ver index.js): no asume que la sesión esté muerta, pero tampoco
    // fuerza una reconexión si ya hay una en marcha o si el estado pide intervención humana.
    recuperar() {
        if (this._cerrando || this._estado === ESTADOS.DETENIDO || this._estado === ESTADOS.REQUIERE_VINCULAR) return;
        if (this._sock || this._temporizadorReconexion) return;
        this._programarReconexion(5000);
    }

    salud() {
        const conectado = this._estado === ESTADOS.CONECTADO;
        return {
            ok: conectado,
            canal: "baileys",
            estado: this._estado,
            motivo: this._motivo,
            conectado,
            requiereVincular: this._estado === ESTADOS.REQUIERE_VINCULAR,
            reconectando: this._estado === ESTADOS.RECONECTANDO
        };
    }

    async estado() {
        return {
            conectado: this._estado === ESTADOS.CONECTADO,
            numero: this._numero,
            qrPendiente: this._qr !== null,
            qr: this._qr ? await QRCode.toDataURL(this._qr, { errorCorrectionLevel: "L", margin: 2, scale: 6 }) : null,
            estado: this._estado,
            motivo: this._motivo
        };
    }

    // Cierra la sesión en WhatsApp, borra las credenciales locales y deja un QR nuevo para vincular otro número
    // (lo mismo que hace el servicio con whatsapp-web.js; es lo que dispara el botón del Configurador).
    async desvincular() {
        this._desvinculando = true;
        this._generacion++;                        // un intento de conectar que estuviera a medias queda descartado
        this._cancelarReconexion();
        const sock = this._sock;
        try {
            if (sock && this._estado === ESTADOS.CONECTADO) {
                try { await this._conTimeout(sock.logout(), TIMEOUT_LOGOUT_MS, "cerrando sesión"); }
                catch (err) { this._log.error(`Error en logout, se continúa con el borrado igual: ${err?.message || err}`); }
            }
            this._sock = null;
            this._soltar(sock);
            await this._guardado;
            await this._sesion.limpiar();
        } catch (err) {
            // No se pudo borrar la sesión: no se sigue como si nada. Queda pidiendo intervención y el error sube al log.
            this._cambiarEstado(ESTADOS.REQUIERE_VINCULAR, "error_borrando_sesion");
            throw err;
        } finally {
            this._qr = null;
            this._numero = null;
            this._desvinculando = false;
        }
        if (this._cerrando) return;
        this._cambiarEstado(ESTADOS.INICIANDO, null);
        try { await this._conectar(); }
        catch (err) { this._falloAlConectar(err); }
    }

    // ── Conexión ────────────────────────────────────────────────────────────────────

    async _conectar() {
        const mia = ++this._generacion;
        const vigente = () => mia === this._generacion && !this._cerrando;
        await this._guardado;
        if (!vigente()) return;
        const { state, saveCreds } = await this._sesion.abrirAuth(this._f);
        if (!vigente()) return;
        const version = await this._resolverVersionWa();
        if (!vigente()) return;

        // Si quedó un socket de antes, se descarta: nunca hay dos conectados a la vez.
        const previo = this._sock;
        if (previo) { this._sock = null; this._soltar(previo); }

        // Nada de `await` entre crear el socket y registrar los handlers: Baileys emite "connecting" en el próximo tick.
        const sock = this._f.makeWASocket(this._configSocket(state, version));
        this._sock = sock;
        sock.ev.on("creds.update", () => { if (sock === this._sock) this._guardarCreds(saveCreds); });
        sock.ev.on("connection.update", update => { if (sock === this._sock) this._alActualizarConexion(sock, update); });
        sock.ev.on("messages.upsert", evento => { if (sock === this._sock) this._alRecibir(evento); });
    }

    _configSocket(state, version) {
        const f = this._f;
        const claves = typeof f.makeCacheableSignalKeyStore === "function"
            ? f.makeCacheableSignalKeyStore(state.keys, this._loggerBaileys)
            : state.keys;
        const config = {
            auth: { creds: state.creds, keys: claves },
            logger: this._loggerBaileys,
            browser: f.Browsers.ubuntu(this._navegador),
            // Parecer una persona y no un servicio: no figurar "en línea" siempre (el teléfono sigue recibiendo sus avisos),
            // no bajar el historial de chats (privacidad y memoria) ni generar vistas previas.
            markOnlineOnConnect: false,
            syncFullHistory: false,
            shouldSyncHistoryMessage: () => false,
            generateHighQualityLinkPreview: false,
            getMessage: async () => undefined
        };
        if (version) config.version = version;
        return config;
    }

    async _resolverVersionWa() {
        if (this._versionWa) return this._versionWa;
        if (this._buscarVersionWa && typeof this._f.fetchLatestBaileysVersion === "function") {
            try {
                const r = await this._f.fetchLatestBaileysVersion();
                if (Array.isArray(r?.version)) {
                    this._log.info(`Versión de WhatsApp Web que se usa: ${r.version.join(".")}${r.isLatest ? "" : " (no se pudo consultar la última; es la que trae la librería)"}.`);
                    return r.version;
                }
            } catch (err) {
                this._log.error(`No se pudo consultar la última versión de WhatsApp Web: ${err?.message || err}`);
            }
        }
        return undefined;   // la que trae la librería
    }

    _guardarCreds(saveCreds) {
        this._guardado = this._guardado
            .then(() => saveCreds())
            .catch(err => this._log.error(`No se pudieron guardar las credenciales de WhatsApp: ${err?.message || err}`));
    }

    _alActualizarConexion(sock, { connection, lastDisconnect, qr }) {
        if (qr) this._alQr(qr);
        if (connection === "open") this._alAbrir(sock);
        if (connection === "close") this._alCerrar(sock, lastDisconnect);
    }

    // El QR es un secreto de vinculación: vive solo en memoria, no se escribe en el log (se ve en GET /estado, que pide secreto).
    _alQr(qr) {
        this._qr = qr;
        this._cambiarEstado(ESTADOS.REQUIERE_VINCULAR, "esperando_escaneo");
        if (!this._qrAvisado) {
            this._qrAvisado = true;
            this._log.info("WhatsApp: hay un código QR para vincular el número (se ve en el Configurador, sección WhatsApp).");
        }
    }

    _alAbrir(sock) {
        this._qr = null;
        this._qrAvisado = false;
        this._numero = numeroDeJid(sock.user?.id);
        this._cambiarEstado(ESTADOS.CONECTADO, null);
        this._log.info(`WhatsApp conectado (número ${ocultarNumeros(this._numero ?? "desconocido")}).`);
    }

    _alCerrar(sock, lastDisconnect) {
        const codigo = codigoDeError(lastDisconnect?.error);
        this._sock = null;
        this._qr = null;
        this._qrAvisado = false;
        this._numero = null;
        this._soltar(sock);
        if (this._cerrando || this._desvinculando) return;

        const DR = this._f.DisconnectReason;
        if (codigo === DR.loggedOut) {
            // La sesión ya no vale (se desvinculó desde el teléfono o WhatsApp la cerró): reintentar solo repetiría el rechazo.
            this._log.error("WhatsApp cerró la sesión (loggedOut): hay que volver a vincular el número.");
            this._cambiarEstado(ESTADOS.REQUIERE_VINCULAR, "sesion_cerrada");
            // Después de lo que haya quedado por escribir, para que un guardado pendiente no vuelva a crear los archivos.
            this._guardado = this._guardado
                .then(() => this._sesion.limpiar())
                .catch(err => this._log.error(`No se pudo borrar la sesión cerrada: ${err?.message || err}`));
            return;
        }
        const demoraMs = codigo === DR.restartRequired ? 0 : 5000;
        this._log.info(`WhatsApp desconectado (código ${codigo ?? "?"}); se reintenta${demoraMs ? ` en ${demoraMs / 1000} s` : " ya"}.`);
        this._programarReconexion(demoraMs);
    }

    _falloAlConectar(err) {
        this._log.error(ocultarNumeros(`Error conectando con WhatsApp: ${err?.message || err}`));
        if (this._cerrando || this._desvinculando) return;
        this._programarReconexion(5000);
    }

    _programarReconexion(demoraMs) {
        this._cancelarReconexion();
        this._cambiarEstado(ESTADOS.RECONECTANDO, null);
        this._temporizadorReconexion = this._reloj.setTimeout(() => {
            this._temporizadorReconexion = null;
            if (this._cerrando) return;
            this._conectar().catch(err => this._falloAlConectar(err));
        }, demoraMs);
    }

    _cancelarReconexion() {
        if (this._temporizadorReconexion) {
            this._reloj.clearTimeout(this._temporizadorReconexion);
            this._temporizadorReconexion = null;
        }
    }

    // Un socket que se descarta: se le sacan los handlers y se lo cierra (sin que su evento de cierre vuelva a entrar acá).
    _soltar(sock) {
        if (!sock) return;
        try { sock.end(undefined); } catch { /* ya estaba cerrado */ }
        try { sock.ev?.removeAllListeners?.("connection.update"); } catch { /* idem */ }
    }

    _cambiarEstado(estado, motivo) {
        if (this._estado === estado && this._motivo === motivo) return;
        this._estado = estado;
        this._motivo = motivo;
        this._log.info(`WhatsApp: estado ${estado}${motivo ? ` (${motivo})` : ""}.`);
    }

    // ── Envío ───────────────────────────────────────────────────────────────────────

    async enviar(telefono, texto) {
        const sock = this._sock;
        if (this._estado !== ESTADOS.CONECTADO || !sock) throw new Error("WhatsApp no está conectado.");
        const jid = await this._resolverJid(sock, telefono);
        if (!jid) throw new Error("Ese número no tiene WhatsApp.");
        await this._conTimeout(sock.sendMessage(jid, { text: texto }), TIMEOUT_ENVIO_MS, "enviando mensaje");
    }

    // Pregunta a WhatsApp cuál de las formas del número existe (con y sin el "9" de celular, en Argentina) y recuerda la respuesta,
    // para no repetir la consulta a sus servidores cada vez que se le escribe a la misma persona.
    async _resolverJid(sock, telefono) {
        const clave = ultimosDiez(telefono);
        const ahora = this._reloj.ahora();
        const guardado = this._jids.get(clave);
        if (guardado && guardado.hasta > ahora) return guardado.jid;

        const lista = candidatos(telefono);
        if (lista.length === 0) return null;
        const resultados = await this._conTimeout(sock.onWhatsApp(...lista), TIMEOUT_CONSULTA_MS, "resolviendo número");
        const encontrado = (resultados || []).find(r => r?.exists);
        const jid = encontrado?.jid ?? null;
        this._jids.set(clave, { jid, hasta: ahora + (jid ? VIGENCIA_JID_MS : VIGENCIA_SIN_WHATSAPP_MS) });
        return jid;
    }

    _conTimeout(promesa, ms, etiqueta) {
        let temporizador;
        const limite = new Promise((_, rechazar) => {
            temporizador = this._reloj.setTimeout(() => rechazar(new Error(`Tiempo de espera agotado (${etiqueta}).`)), ms);
        });
        return Promise.race([promesa, limite]).finally(() => this._reloj.clearTimeout(temporizador));
    }

    // ── Entrantes ───────────────────────────────────────────────────────────────────

    // Solo "notify" son mensajes nuevos; "append" es historial y sincronización. Cada mensaje de texto de una persona se reenvía a
    // la API. El texto pasa por memoria y nunca por el log.
    _alRecibir(evento) {
        if (evento?.type !== "notify" || !Array.isArray(evento.messages)) return;
        for (const msg of evento.messages) {
            try {
                const entrante = interpretarEntrante(msg);
                if (entrante.ignorar) continue;
                if (this._yaVisto(entrante.id)) continue;
                Promise.resolve(this._reenviar(entrante.telefono, entrante.texto)).catch(() => { /* el reenviador ya loguea */ });
            } catch (err) {
                this._log.error(ocultarNumeros(`Error procesando un mensaje entrante: ${err?.message || err}`));
            }
        }
    }

    // WhatsApp puede entregar el mismo mensaje dos veces (por ejemplo al reconectar): se descartan los repetidos.
    _yaVisto(id) {
        if (!id) return false;
        const ahora = this._reloj.ahora();
        if (this._vistos.has(id)) return true;
        this._vistos.set(id, ahora);
        if (this._vistos.size > MAX_VISTOS) {
            for (const [clave, cuando] of this._vistos) {
                if (ahora - cuando > VIGENCIA_VISTOS_MS || this._vistos.size > MAX_VISTOS) this._vistos.delete(clave);
                else break;
            }
        }
        return false;
    }
}

module.exports = { ServicioBaileys, ESTADOS, RELOJ_REAL, codigoDeError };
