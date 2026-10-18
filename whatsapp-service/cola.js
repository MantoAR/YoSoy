// Cola de envío: /enviar responde apenas se encola, no cuando WhatsApp confirma la
// entrega. Necesario para que una ráfaga de mensajes (por ejemplo, varios avisos de turno
// juntos) no haga que el HttpClient del lado .NET (timeout 10s) venza
// esperando el turno de un mensaje atrás en la cola, dispare un reintento
// (MaxIntentosEnvio=3) y termine mandando el mismo mensaje dos veces de verdad.
// Con esto, "encolado" (Exito:true) y "entregado" son cosas distintas — mismo
// criterio laxo que ya usa el canal Simulado del lado .NET (tampoco confirma lectura).
//
// WhatsApp puede bloquear el número si parece un bot, así que la cola del servicio lleva, por opciones, estas protecciones (el servicio las
// arma siempre: crearColaParaBackend no arma una cola sin ellas):
//   - consentimiento: solo se acepta el envío a quien aceptó recibir mensajes (consentimiento.js); si no, 403;
//   - topes de envío: por día, por persona y por hora (limites.js); al pasarse del día o de la persona, 429;
//   - ritmo humano: pausa al azar entre mensajes y, cada tanto, una pausa larga;
//   - conexión: si WhatsApp está reconectando, los mensajes esperan; si hace falta una persona (vincular, atención), se rechazan con 503.
const { ocultarNumeros } = require("./privacidad");

const CAPACIDAD_MAXIMA = 50;
const ESPACIADO_MIN_MS = 3000;
const ESPACIADO_JITTER_MS = 2000;
const MAX_ANTIGUEDAD_MS = 3 * 60 * 60 * 1000;      // un aviso que lleva más de 3 horas esperando ya no sirve: se descarta
const ESPERA_DE_CONEXION_MS = 1000;
const NO_DISPONIBLES = new Set(["requiere_vincular", "requiere_atencion", "detenido"]);

const RELOJ_REAL = Object.freeze({
    setTimeout: (...a) => setTimeout(...a),
    clearTimeout: (...a) => clearTimeout(...a),
    ahora: () => Date.now()
});

class ColaEnvios {
    constructor(enviarAhora, log, opciones = {}) {
        const {
            reloj = RELOJ_REAL,
            aleatorio = Math.random,
            espaciado = { minMs: ESPACIADO_MIN_MS, maxMs: ESPACIADO_MIN_MS + ESPACIADO_JITTER_MS },
            pausaLarga = null,                     // { cada: [minimo, maximo] mensajes, ms: [minimo, maximo] }
            capacidad = CAPACIDAD_MAXIMA,
            consentimiento = null,                 // { permite(telefono, { optIn }) }
            limites = null,                        // LimiteDeEnvios
            estadoDeConexion = null,               // () => "conectado" | "reconectando" | "requiere_vincular" ...
            maxAntiguedadMs = MAX_ANTIGUEDAD_MS
        } = opciones;
        this._enviarAhora = enviarAhora; // (telefono, texto, meta) => Promise<void>
        this._log = log;
        this._reloj = reloj;
        this._aleatorio = aleatorio;
        this._espaciado = espaciado;
        this._pausaLarga = pausaLarga;
        this._capacidad = capacidad;
        this._consentimiento = consentimiento;
        this._limites = limites;
        this._estadoDeConexion = estadoDeConexion;
        this._maxAntiguedadMs = maxAntiguedadMs;

        this._pendientes = [];
        this._procesando = false;
        this._enCurso = null;                      // la promesa del loop que está despachando
        this._siguienteId = 1;
        this._detenida = false;
        this._espera = null;                       // { temporizador, despertar } mientras el loop duerme
        this._desdeLaPausaLarga = 0;
        this._hastaLaPausaLarga = pausaLarga ? this._entero(...pausaLarga.cada) : Infinity;
    }

    // Apagado limpio (ver apagar() en index.js): deja de aceptar pedidos nuevos y hace que
    // el loop de _procesarEnSegundoPlano corte después del envío en curso, en vez de seguir
    // vaciando la cola — el proceso se está yendo, no hace falta terminarla entera.
    detener() {
        this._detenida = true;
        if (this._espera) {                        // no hay por qué esperar una pausa si el proceso se está yendo
            this._reloj.clearTimeout(this._espera.temporizador);
            this._espera.despertar();
        }
    }

    // Devuelve { ok: true, idExterno } o { ok: false, estado, codigo, error } — nunca espera al envío real. `meta` viaja con el
    // pedido hasta el envío (por ejemplo, optIn: la API asegura que el paciente aceptó recibir WhatsApp).
    encolar(telefono, texto, meta = {}) {
        if (this._detenida) {
            return { ok: false, estado: 503, codigo: "reiniciando", error: "El servicio se está reiniciando, probá de nuevo en un momento." };
        }
        const conexion = this._estadoDeConexion?.();
        if (conexion && NO_DISPONIBLES.has(conexion)) {
            return { ok: false, estado: 503, codigo: "no_conectado", error: `WhatsApp no está disponible en este momento (${conexion}).` };
        }
        let enConversacion = false;                // la persona escribió hace poco: rige el tope de una conversación (limites.js)
        if (this._consentimiento) {
            const permiso = this._consentimiento.permite(telefono, { optIn: meta.optIn === true });
            if (!permiso.ok) return this._rechazar(telefono, permiso);
            enConversacion = permiso.enConversacion === true;
        }
        if (this._pendientes.length >= this._capacidad) {
            return { ok: false, estado: 503, codigo: "cola_llena", error: "Cola llena, probá de nuevo en unos minutos." };
        }
        let huella = null;
        if (this._limites) {
            const lugar = this._limites.reservar(telefono, { enConversacion });
            if (!lugar.ok) return this._rechazar(telefono, lugar);
            huella = lugar.huella;
        }
        const idExterno = `cola-${this._siguienteId++}`;
        this._pendientes.push({ idExterno, telefono, texto, meta, huella, encoladoEn: this._reloj.ahora() });
        if (!this._procesando) this._enCurso = this._procesarEnSegundoPlano();
        return { ok: true, idExterno };
    }

    tamano() {
        return this._pendientes.length;
    }

    // Espera (hasta maxMs) a que termine el envío que esté en curso; con la cola detenida no sale ningún otro. true si terminó, false si
    // se acabó el tiempo. Lo usa el apagado: así un mensaje no se corta a la mitad ni se queda sin contar en el tope del día.
    async esperarEnCurso(maxMs) {
        if (!this._procesando || !this._enCurso) return true;
        let temporizador;
        const vencio = new Promise(resolver => { temporizador = this._reloj.setTimeout(() => resolver(false), maxMs); });
        const termino = this._enCurso.then(() => true, () => true);
        const resultado = await Promise.race([termino, vencio]);
        this._reloj.clearTimeout(temporizador);
        return resultado;
    }

    resumen() {
        return { pendientes: this._pendientes.length, ...(this._limites ? this._limites.resumen() : {}) };
    }

    _rechazar(telefono, motivo) {
        this._log.info(ocultarNumeros(`Envío rechazado (${motivo.codigo}) a ${telefono}.`));
        return { ok: false, estado: motivo.estado, codigo: motivo.codigo, error: motivo.error };
    }

    async _procesarEnSegundoPlano() {
        if (this._procesando) return;
        this._procesando = true;
        try {
            while (this._pendientes.length > 0 && !this._detenida) {
                const item = this._pendientes[0];          // se mira, y se saca recién cuando le toca
                const descarte = await this._esperarQueSePuedaEnviar(item);
                if (this._detenida) break;
                this._pendientes.shift();
                if (descarte) {
                    if (item.huella) this._limites.liberar(item.huella);
                    this._log.error(ocultarNumeros(`Se descartó ${item.idExterno} a ${item.telefono}: ${descarte}.`));
                    continue;
                }
                let salio = false;
                try {
                    await this._enviarAhora(item.telefono, item.texto, item.meta);
                    salio = true;
                } catch (err) {
                    // Ya no hay a quién devolverle el error: la respuesta HTTP de /enviar
                    // ya salió hace rato. Queda solo en el log de este proceso.
                    // El teléfono es de un paciente: en el log va enmascarado (los últimos 4 dígitos), también si el
                    // mensaje de error de la librería lo repite.
                    this._log.error(ocultarNumeros(`Error enviando ${item.idExterno} a ${item.telefono}: ${err.message || err}`));
                }
                // Lo que salió gasta cupo; lo que no, lo devuelve.
                if (item.huella) { if (salio) this._limites.confirmar(item.huella); else this._limites.liberar(item.huella); }
                if (this._pendientes.length > 0) await this._dormir(this._pausaDespuesDeEnviar());
            }
        } finally {
            this._procesando = false;
        }
    }

    // Espera a que se pueda mandar el mensaje: WhatsApp conectado y sin pasarse del tope por hora. Devuelve null si ya se puede, o el
    // motivo por el que hay que descartarlo (vencido de viejo, o WhatsApp pide una persona).
    async _esperarQueSePuedaEnviar(item) {
        for (;;) {
            if (this._detenida) return null;
            if (this._reloj.ahora() - item.encoladoEn > this._maxAntiguedadMs) return "lleva demasiado tiempo esperando";

            const conexion = this._estadoDeConexion?.();
            if (conexion && conexion !== "conectado") {
                if (NO_DISPONIBLES.has(conexion)) return `WhatsApp no está disponible (${conexion})`;
                await this._dormir(ESPERA_DE_CONEXION_MS);       // reconectando: se espera
                continue;
            }
            const hora = this._limites?.puedeEnviarYa();
            if (hora && !hora.ok) {
                await this._dormir(Math.min(hora.esperarMs, 60 * 1000));
                continue;
            }
            return null;
        }
    }

    // Pausa al azar después de cada mensaje; cada tanto (al azar también), una larga, como una persona que se levanta un rato.
    _pausaDespuesDeEnviar() {
        this._desdeLaPausaLarga++;
        if (this._pausaLarga && this._desdeLaPausaLarga >= this._hastaLaPausaLarga) {
            this._desdeLaPausaLarga = 0;
            this._hastaLaPausaLarga = this._entero(...this._pausaLarga.cada);
            return this._entero(...this._pausaLarga.ms);
        }
        return this._entero(this._espaciado.minMs, this._espaciado.maxMs - 1);
    }

    // Un entero al azar entre minimo y maximo, ambos incluidos.
    _entero(minimo, maximo) {
        return minimo + Math.floor(this._aleatorio() * (maximo - minimo + 1));
    }

    _dormir(ms) {
        if (this._detenida) return Promise.resolve();            // con la cola detenida no se espera nada
        return new Promise(resolver => {
            const despertar = () => { this._espera = null; resolver(); };
            this._espera = { despertar, temporizador: this._reloj.setTimeout(despertar, ms) };
        });
    }
}

// La cola de un backend, con el consentimiento, los topes y el ritmo que arma backend.js (opcionesDeCola). Si el backend no los trae NO se
// arma la cola: una cola "pelada" aceptaría mandarle a cualquiera sin límite, que es justo lo que bloquea un número de WhatsApp.
function crearColaParaBackend(backend, log) {
    const opciones = backend.opcionesDeCola;
    if (!opciones || !opciones.consentimiento || !opciones.limites) {
        throw new Error("El backend de WhatsApp no trae consentimiento ni topes de envío: no se arma la cola de envíos sin esas protecciones.");
    }
    return new ColaEnvios((telefono, texto, meta) => backend.enviar(telefono, texto, meta), log, opciones);
}

module.exports = { ColaEnvios, crearColaParaBackend, CAPACIDAD_MAXIMA, MAX_ANTIGUEDAD_MS };
