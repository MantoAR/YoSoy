// Envoltorio sobre whatsapp-web.js: conexión/QR, filtro de entrantes y resolución de
// números. Nada de esto se prueba solo (no hay WhatsApp real en CI) — se verifica a mano
// en la Etapa 2, con el chip ya vinculado. Ver whatsapp-service/README.md.
const { Client, LocalAuth } = require("whatsapp-web.js");
const qrcodeTerminal = require("qrcode-terminal");
const QRCode = require("qrcode");

class ServicioWhatsApp {
    constructor({ dataPath, apiBaseUrl, secreto, webVersion, debug, onEntrante, log }) {
        this._dataPath = dataPath;
        this._apiBaseUrl = apiBaseUrl;
        this._secreto = secreto;
        this._webVersion = webVersion || null;
        this._debug = !!debug;
        this._onEntrante = onEntrante;
        this._log = log;
        this._conectado = false;
        this._numero = null;
        this._qrPendiente = false;
        this._qrActual = null;
        this._reiniciando = false;

        this._client = this._crearCliente();
    }

    // Separado del constructor para poder llamarlo de nuevo al reiniciar (ver _reiniciar),
    // no solo una vez al arrancar.
    _crearCliente() {
        const opciones = {
            authStrategy: new LocalAuth({ dataPath: this._dataPath }),
            // --no-sandbox: AppArmor en Ubuntu 24.04 restringe los user namespaces sin
            // privilegios que usa el sandbox normal de Chromium — ver README.md.
            puppeteer: { args: ["--no-sandbox", "--disable-setuid-sandbox"] }
        };

        // WHATSAPP_WEB_VERSION (opcional, ver README.md "Fijar la versión de WhatsApp
        // Web"): mitigación manual para cuando WhatsApp Web rompe algo del lado de ellos
        // y todavía no hay una versión publicada de whatsapp-web.js con el parche — fija
        // una build conocida en vez de la que trae la librería por default. remotePath
        // apunta al índice de versiones que ya usa la comunidad de whatsapp-web.js para
        // esto (wppconnect-team/wa-version), no un servidor propio.
        if (this._webVersion) {
            opciones.webVersion = this._webVersion;
            opciones.webVersionCache = {
                type: "remote",
                remotePath: `https://raw.githubusercontent.com/wppconnect-team/wa-version/main/html/${this._webVersion}.html`
            };
        }

        const client = new Client(opciones);
        if (this._debug) this._engancharDiagnosticoPagina(client);

        // Cada handler empieza chequeando que "client" (el cliente que armó ESTE handler)
        // siga siendo this._client — destroy() no garantiza que un cliente reemplazado deje
        // de emitir al instante, y un evento tardío del cliente viejo no tiene que pisar el
        // estado del cliente actual ni disparar un reinicio sobre él.
        client.on("qr", qr => {
            if (client !== this._client) return;
            this._qrPendiente = true;
            this._qrActual = qr;
            this._log.info("Escaneá este código QR con el chip de WhatsApp dedicado:");
            qrcodeTerminal.generate(qr, { small: true });
        });

        client.on("ready", () => {
            if (client !== this._client) return;
            this._conectado = true;
            this._qrPendiente = false;
            this._qrActual = null;
            this._numero = client.info?.wid?.user ?? null;
            this._log.info(`WhatsApp conectado (número ${this._numero}).`);
            if (this._debug) this._log.info("WHATSAPP_DEBUG: ready llegó.");
        });

        // Diagnóstico (Problema 2: vinculado en el teléfono pero la web se queda sin
        // llegar nunca a "ready") — una línea por evento, nunca el payload entero (puede
        // traer datos de la cuenta real).
        client.on("authenticated", () => {
            if (client !== this._client) return;
            this._log.info("WhatsApp: authenticated");
            if (this._debug) this._diagnosticoTrasAuthenticated(client);
        });

        client.on("loading_screen", (percent) => {
            if (client !== this._client) return;
            this._log.info(`WhatsApp: loading_screen ${percent}%`);
        });

        client.on("change_state", (estado) => {
            if (client !== this._client) return;
            this._log.info(`WhatsApp: change_state ${estado}`);
        });

        // Solo lo emite la estrategia RemoteAuth de la librería, no LocalAuth (la que usa
        // este servicio) — queda cableado para no perder el log si algún día cambia la
        // estrategia, pero con la configuración actual nunca va a disparar.
        client.on("remote_session_saved", () => {
            if (client !== this._client) return;
            this._log.info("WhatsApp: remote_session_saved");
        });

        // "LOGOUT" es el motivo exacto que emite la librería cuando la página navega a la
        // URL de post-logout (node_modules/whatsapp-web.js/src/Client.js:495-497) — pasa
        // tanto si llamamos nosotros a logout() como si se desvincula desde el teléfono
        // (WhatsApp empuja el logout, la página navega sola).
        client.on("disconnected", razon => {
            if (client !== this._client) return;
            this._conectado = false;
            this._numero = null;
            this._log.error(`WhatsApp desconectado: ${razon}`);

            if (razon === "LOGOUT") {
                // La librería misma reacciona a esto en su propio listener interno de
                // framenavigated (Client.js:495-504): llama a authStrategy.logout() —que
                // ya borra la sesión, LocalAuth.js:56-68— y vuelve a llamar a inject()
                // sobre ESTE MISMO cliente/página, sin destruir nada. Verificado leyendo
                // inject() (Client.js, rama needAuthentication): si la página queda en
                // estado UNPAIRED/UNPAIRED_IDLE, reinscribe el callback de QR y dispara
                // uno nuevo sola. Si nosotros ADEMÁS destruíamos el cliente acá (lo que
                // hacía este handler antes), competíamos por la misma página con la
                // librería — esa carrera fue la causa real de un crash en producción
                // ("Execution context was destroyed", en Client.inject() disparado desde
                // el mismo framenavigated). Por eso acá NO se llama a _reiniciar: solo se
                // invalida el QR viejo que teníamos guardado, la librería hace el resto.
                this._qrActual = null;
                this._qrPendiente = false;
                return;
            }

            // Cualquier otro motivo (corte de red, etc.): recrear el cliente sin tocar la
            // sesión guardada, para poder reconectar solo si fue algo transitorio.
            this._reiniciar({ borrarSesion: false });
        });

        client.on("auth_failure", msg => {
            if (client !== this._client) return;
            this._log.error(`Fallo de autenticación: ${msg}`);
            this._reiniciar({ borrarSesion: true });
        });

        client.on("message", msg => {
            if (client !== this._client) return;
            this._manejarEntrante(msg);
        });

        return client;
    }

    // ── WHATSAPP_DEBUG=1 (apagada por defecto, ver README.md "Diagnóstico") ──────
    // pupPage no existe todavía cuando se crea el Client (whatsapp-web.js recién lo arma
    // dentro de initialize(), sin emitir ningún evento propio de "ya hay página") — se
    // sondea un toque hasta que aparece. Sin await desde _crearCliente(): no debe bloquear
    // la creación del cliente ni que arranque initialize().
    async _engancharDiagnosticoPagina(client) {
        for (let i = 0; i < 100 && !client.pupPage; i++) await new Promise(r => setTimeout(r, 100));
        if (client !== this._client) return;
        if (!client.pupPage) {
            this._log.error("WHATSAPP_DEBUG: no apareció pupPage a tiempo, sin diagnóstico de página para este cliente.");
            return;
        }
        // console/pageerror de la página real de WhatsApp Web — contenido que no
        // controlamos nosotros, no algo que nuestro código construya con datos de la
        // cuenta. Igual se trunca a 300 caracteres. Por esto WHATSAPP_DEBUG queda apagado
        // por default: prender solo mientras se esté diagnosticando, no dejarlo siempre
        // encendido en producción.
        client.pupPage.on("pageerror", err => {
            if (client !== this._client) return;
            this._log.error(`WHATSAPP_DEBUG pageerror: ${String(err?.message || err).slice(0, 300)}`);
        });
        client.pupPage.on("console", msg => {
            if (client !== this._client) return;
            const tipo = msg.type();
            if (tipo !== "error" && tipo !== "warning") return;
            this._log.error(`WHATSAPP_DEBUG console.${tipo}: ${msg.text().slice(0, 300)}`);
        });
    }

    // Si "ready" no llega a los 60s y de nuevo a los 180s desde "authenticated", sondea el
    // estado interno de la página — pensado para correlacionar con los pasos de inject()
    // (después de onAppStateHasSyncedEvent) que podrían colgarse sin tirar ningún error
    // visible; ver whatsapp-service/patches/README.md, "Diagnóstico de la página" para el
    // detalle de qué pasos son esos y de qué depende cada uno.
    async _diagnosticoTrasAuthenticated(client) {
        try {
            const version = await client.getWWebVersion();
            this._log.info(`WHATSAPP_DEBUG: WWebVersion = ${version}`);
        } catch (err) {
            this._log.error(`WHATSAPP_DEBUG: error obteniendo WWebVersion: ${err.message || err}`);
        }

        let listo = false;
        const avisarListo = () => { listo = true; };
        client.once("ready", avisarListo);

        for (const segundos of [60, 180]) {
            await new Promise(r => setTimeout(r, segundos * 1000));
            if (listo || client !== this._client) break;
            await this._sondaPagina(client, segundos);
        }
        client.removeListener("ready", avisarListo);
    }

    async _sondaPagina(client, segundos) {
        if (!client.pupPage) {
            this._log.error(`WHATSAPP_DEBUG sonda (${segundos}s sin ready): no hay pupPage.`);
            return;
        }
        try {
            const resultado = await client.pupPage.evaluate(() => {
                try {
                    const AppState = window.require("WAWebSocketModel").Socket;
                    return {
                        appStateState: AppState?.state ?? null,
                        hasSynced: AppState?.hasSynced ?? null,
                        tipoStore: typeof window.Store,
                        tipoWWebJS: typeof window.WWebJS,
                        tipoRequire: typeof window.require
                    };
                } catch (err) {
                    return { errorEnPagina: String(err?.message || err) };
                }
            });
            this._log.info(`WHATSAPP_DEBUG sonda (${segundos}s sin ready): ${JSON.stringify(resultado)}`);
        } catch (err) {
            this._log.error(`WHATSAPP_DEBUG sonda (${segundos}s sin ready): error evaluando en la página: ${err.message || err}`);
        }
    }

    // Si initialize() falla (p. ej. Puppeteer no pudo arrancar), no queda como una promesa
    // rechazada sin capturar — mismo mecanismo de reintento cada 30s que usa _reiniciar.
    // Sin borrar la sesión: un fallo acá no dice nada sobre si la sesión guardada sigue
    // sirviendo o no.
    iniciar() {
        this._client.initialize().catch(err => {
            this._log.error(`Error inicializando WhatsApp: ${err.message || err}`);
            setTimeout(() => this._reiniciar({ borrarSesion: false }), 30000);
        });
    }

    async estado() {
        return {
            conectado: this._conectado,
            numero: this._numero,
            qrPendiente: this._qrPendiente,
            qr: this._qrActual ? await QRCode.toDataURL(this._qrActual) : null
        };
    }

    // Red de seguridad ante un unhandledRejection/uncaughtException del proceso (ver
    // index.js) — no asume que la sesión esté muerta: mismo criterio que un
    // "disconnected" transitorio (no borra sesión), por si el cliente quedó en un estado
    // raro pero recuperable.
    recuperar() {
        this._reiniciar({ borrarSesion: false });
    }

    // Apagado limpio (SIGTERM/SIGINT, ver index.js) — no pasa por _reiniciar: no tiene
    // sentido reintentar ni recrear nada, el proceso se está yendo. Quien llama a esto le
    // pone su propio timeout (ver apagar() en index.js).
    async cerrar() {
        try { await this._client.destroy(); }
        catch (err) { this._log.error(`Error destruyendo el cliente al apagar: ${err.message || err}`); }
    }

    // Reemplaza por completo el cliente actual: destruye el viejo (best-effort), opcionalmente
    // borra la sesión guardada y arranca uno nuevo. Bandera de reentrancia porque un mismo
    // logout puede disparar esto dos veces (desvincular() de forma directa, y el evento
    // "disconnected" que ese mismo logout puede terminar generando) — la segunda llamada
    // concurrente es un no-op. NO se usa para el caso "disconnected" con motivo LOGOUT (ver
    // ese handler más arriba) — ahí la librería ya se reinicializa sola.
    async _reiniciar({ borrarSesion }) {
        if (this._reiniciando) return;
        this._reiniciando = true;
        // Si logout() se cuelga (desvincular) y "disconnected" nunca llega a disparar solo,
        // esto es lo único que garantiza que el estado no quede "conectado" con un QR
        // pendiente a la vez.
        this._conectado = false;
        this._numero = null;
        this._qrActual = null;
        this._qrPendiente = false;
        let reintentar = false;
        try {
            try { await this._client.destroy(); }
            catch (err) { this._log.error(`Error destruyendo el cliente: ${err.message || err}`); }

            if (borrarSesion) {
                // LocalAuth.logout() borra solo dataPath/session/ (un subdirectorio), nunca
                // dataPath en sí — confirmado leyendo
                // node_modules/whatsapp-web.js/src/authStrategies/LocalAuth.js:56-68. Por eso
                // alcanza con esto: el directorio que necesita seguir existiendo para
                // ReadWritePaths de systemd (WHATSAPP_SESSION_PATH) nunca se toca.
                try { await this._client.authStrategy.logout(); }
                catch (err) { this._log.error(`Error borrando la sesión: ${err.message || err}`); }
            }

            this._client = this._crearCliente();
            await this._client.initialize();
        } catch (err) {
            this._log.error(`Error reinicializando WhatsApp: ${err.message || err}`);
            reintentar = true;
        } finally {
            this._reiniciando = false;
        }
        // No es un único reintento: si _reiniciar vuelve a fallar, esta misma llamada se
        // reprograma otra vez — reintenta cada 30s mientras siga fallando, sin loop apretado
        // (y sin bloquear la llamada que disparó este intento).
        if (reintentar) setTimeout(() => this._reiniciar({ borrarSesion }), 30000);
    }

    // Logout real contra los servidores de WhatsApp (no solo limpieza local) — con timeout
    // propio porque Client.logout() cierra el browser muy poco después de pedir el logout en
    // la página, y puede no llegar a confirmar nada si se cuelga. Se reinicia igual aunque el
    // logout falle o se agote el tiempo: el objetivo ("sesión limpia, QR nuevo") no depende de
    // que este paso salga bien. Si no había nada conectado (p. ej. el botón "generar uno
    // nuevo" mientras se espera el QR), no tiene sentido pedirle un logout real a WhatsApp —
    // directo a _reiniciar, sin esperar los 15s de un timeout que nunca iba a hacer falta.
    async desvincular() {
        if (this._conectado) {
            try {
                await this._conTimeout(this._client.logout(), 15000, "cerrando sesión");
            } catch (err) {
                this._log.error(`Error en logout, se continúa con el reinicio igual: ${err.message || err}`);
            }
        }
        await this._reiniciar({ borrarSesion: true });
    }

    _conTimeout(promise, ms, etiqueta) {
        return Promise.race([
            promise,
            new Promise((_, reject) => setTimeout(() => reject(new Error(`Tiempo de espera agotado (${etiqueta}).`)), ms))
        ]);
    }

    // Prueba los dos formatos posibles para Argentina (con y sin el "9" de línea móvil)
    // contra client.getNumberId(), que resuelve contra WhatsApp mismo — así no hay que
    // adivinar la longitud del código de área. null si ninguno de los dos existe en WhatsApp.
    async resolverNumero(diezDigitos) {
        const candidatos = [`54${diezDigitos}`, `549${diezDigitos}`];
        for (const candidato of candidatos) {
            const id = await this._conTimeout(this._client.getNumberId(candidato), 15000, "resolviendo número");
            if (id) return id._serialized;
        }
        return null;
    }

    async enviar(diezDigitos, texto) {
        const chatId = await this.resolverNumero(diezDigitos);
        if (!chatId) throw new Error("Ese número no tiene WhatsApp.");
        // Un reinicio en curso puede dejar sendMessage colgado (página de Puppeteer
        // destruida a mitad de camino) en vez de rechazar limpio — el timeout acota ese
        // peor caso: el envío puntual falla, pero ColaEnvios no queda trabada para siempre.
        await this._conTimeout(this._client.sendMessage(chatId, texto), 15000, "enviando mensaje");
    }

    async _manejarEntrante(msg) {
        try {
            if (msg.fromMe) return;
            // Grupos (@g.us), estados/difusiones (@broadcast, incluye "status@broadcast"):
            // nada de esto es una conversación con un paciente — ignorar.
            if (msg.from.endsWith("@g.us") || msg.from.endsWith("@broadcast")) return;

            // El remitente puede llegar como @lid (identificador vinculado) en vez del
            // número real — resolver siempre por el contacto, nunca confiar en msg.from.
            const contacto = await msg.getContact();
            const telefono = contacto?.number;
            if (!telefono) {
                this._log.error(`Mensaje entrante sin número de contacto resoluble (from=${msg.from}), se ignora.`);
                return;
            }

            // No bloquea el procesamiento del próximo evento: timeout corto propio,
            // error solo logueado, sin await que frene la cola de eventos de whatsapp-web.js.
            this._reenviarSinBloquear(telefono, msg.body ?? "");
        } catch (err) {
            this._log.error(`Error procesando mensaje entrante: ${err.message || err}`);
        }
    }

    _reenviarSinBloquear(telefono, texto) {
        const controlador = new AbortController();
        const vencimiento = setTimeout(() => controlador.abort(), 5000);
        fetch(`${this._apiBaseUrl}/mensajeria/whatsapp/entrante`, {
            method: "POST",
            headers: { "Content-Type": "application/json", "X-Shared-Secret": this._secreto },
            body: JSON.stringify({ telefono, texto }),
            signal: controlador.signal
        })
            .catch(err => this._log.error(`No se pudo reenviar el mensaje entrante al API: ${err.message || err}`))
            .finally(() => clearTimeout(vencimiento));
    }
}

module.exports = { ServicioWhatsApp };
