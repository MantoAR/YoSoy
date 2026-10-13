// Envoltorio sobre whatsapp-web.js: conexión/QR, filtro de entrantes y resolución de
// números. Nada de esto se prueba solo (no hay WhatsApp real en CI) — se verifica a mano
// en la Etapa 2, con el chip ya vinculado. Ver whatsapp-service/README.md.
const { Client, LocalAuth } = require("whatsapp-web.js");
const qrcodeTerminal = require("qrcode-terminal");
const QRCode = require("qrcode");

class ServicioWhatsApp {
    constructor({ dataPath, apiBaseUrl, secreto, onEntrante, log }) {
        this._dataPath = dataPath;
        this._apiBaseUrl = apiBaseUrl;
        this._secreto = secreto;
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
        const client = new Client({
            authStrategy: new LocalAuth({ dataPath: this._dataPath }),
            // --no-sandbox: AppArmor en Ubuntu 24.04 restringe los user namespaces sin
            // privilegios que usa el sandbox normal de Chromium — ver README.md.
            puppeteer: { args: ["--no-sandbox", "--disable-setuid-sandbox"] }
        });

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
        });

        // "LOGOUT" es el motivo exacto que emite la librería cuando la página navega a la
        // URL de post-logout (node_modules/whatsapp-web.js/src/Client.js:495-497) — pasa
        // tanto si llamamos nosotros a logout() como si se desvincula desde el teléfono
        // (WhatsApp empuja el logout, la página navega sola). Ahí la sesión está
        // confirmada muerta: se borra y se fuerza un QR nuevo, igual que auth_failure.
        // Cualquier otro motivo (corte de red, etc.) solo recrea el cliente sin tocar la
        // sesión guardada, para poder reconectar solo si fue algo transitorio.
        client.on("disconnected", razon => {
            if (client !== this._client) return;
            this._conectado = false;
            this._numero = null;
            this._log.error(`WhatsApp desconectado: ${razon}`);
            this._reiniciar({ borrarSesion: razon === "LOGOUT" });
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

    // Reemplaza por completo el cliente actual: destruye el viejo (best-effort), opcionalmente
    // borra la sesión guardada y arranca uno nuevo. Bandera de reentrancia porque un mismo
    // logout puede disparar esto dos veces (desvincular() de forma directa, y el evento
    // "disconnected" que ese mismo logout puede terminar generando) — la segunda llamada
    // concurrente es un no-op.
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
