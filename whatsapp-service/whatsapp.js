// Envoltorio sobre whatsapp-web.js: conexión/QR, filtro de entrantes y resolución de
// números. Nada de esto se prueba solo (no hay WhatsApp real en CI) — se verifica a mano
// en la Etapa 2, con el chip ya vinculado. Ver whatsapp-service/README.md.
const { Client, LocalAuth } = require("whatsapp-web.js");
const qrcodeTerminal = require("qrcode-terminal");

class ServicioWhatsApp {
    constructor({ dataPath, apiBaseUrl, secreto, onEntrante, log }) {
        this._apiBaseUrl = apiBaseUrl;
        this._secreto = secreto;
        this._onEntrante = onEntrante;
        this._log = log;
        this._conectado = false;
        this._numero = null;
        this._qrPendiente = false;

        this._client = new Client({
            authStrategy: new LocalAuth({ dataPath }),
            // --no-sandbox: AppArmor en Ubuntu 24.04 restringe los user namespaces sin
            // privilegios que usa el sandbox normal de Chromium — ver README.md.
            puppeteer: { args: ["--no-sandbox", "--disable-setuid-sandbox"] }
        });

        this._client.on("qr", qr => {
            this._qrPendiente = true;
            this._log.info("Escaneá este código QR con el chip de WhatsApp dedicado:");
            qrcodeTerminal.generate(qr, { small: true });
        });

        this._client.on("ready", () => {
            this._conectado = true;
            this._qrPendiente = false;
            this._numero = this._client.info?.wid?.user ?? null;
            this._log.info(`WhatsApp conectado (número ${this._numero}).`);
        });

        this._client.on("disconnected", razon => {
            this._conectado = false;
            this._numero = null;
            this._log.error(`WhatsApp desconectado: ${razon}`);
        });

        this._client.on("message", msg => this._manejarEntrante(msg));
    }

    iniciar() {
        this._client.initialize();
    }

    estado() {
        return { conectado: this._conectado, numero: this._numero, qrPendiente: this._qrPendiente };
    }

    // Prueba los dos formatos posibles para Argentina (con y sin el "9" de línea móvil)
    // contra client.getNumberId(), que resuelve contra WhatsApp mismo — así no hay que
    // adivinar la longitud del código de área. null si ninguno de los dos existe en WhatsApp.
    async resolverNumero(diezDigitos) {
        const candidatos = [`54${diezDigitos}`, `549${diezDigitos}`];
        for (const candidato of candidatos) {
            const id = await this._client.getNumberId(candidato);
            if (id) return id._serialized;
        }
        return null;
    }

    async enviar(diezDigitos, texto) {
        const chatId = await this.resolverNumero(diezDigitos);
        if (!chatId) throw new Error("Ese número no tiene WhatsApp.");
        await this._client.sendMessage(chatId, texto);
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
