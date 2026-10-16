// Cola de envío: /enviar responde apenas se encola, no cuando WhatsApp confirma la
// entrega. Necesario para que una ráfaga de recordatorios (NotificadorTurnos manda
// varios turnos juntos) no haga que el HttpClient del lado .NET (timeout 10s) venza
// esperando el turno de un mensaje atrás en la cola, dispare un reintento
// (MaxIntentosEnvio=3) y termine mandando el mismo mensaje dos veces de verdad.
// Con esto, "encolado" (Exito:true) y "entregado" son cosas distintas — mismo
// criterio laxo que ya usa el canal Simulado del lado .NET (tampoco confirma lectura).
const { ocultarNumeros } = require("./privacidad");

const CAPACIDAD_MAXIMA = 50;
const ESPACIADO_MIN_MS = 3000;
const ESPACIADO_JITTER_MS = 2000;

class ColaEnvios {
    constructor(enviarAhora, log) {
        this._enviarAhora = enviarAhora; // (telefono, texto) => Promise<void>
        this._log = log;
        this._pendientes = [];
        this._procesando = false;
        this._siguienteId = 1;
        this._detenida = false;
    }

    // Apagado limpio (ver apagar() en index.js): deja de aceptar pedidos nuevos y hace que
    // el loop de _procesarEnSegundoPlano corte después del envío en curso, en vez de seguir
    // vaciando la cola — el proceso se está yendo, no hace falta terminarla entera.
    detener() {
        this._detenida = true;
    }

    // Devuelve { ok: true, idExterno } o { ok: false, error } — nunca espera al envío real.
    encolar(telefono, texto) {
        if (this._detenida) {
            return { ok: false, error: "El servicio se está reiniciando, probá de nuevo en un momento." };
        }
        if (this._pendientes.length >= CAPACIDAD_MAXIMA) {
            return { ok: false, error: "Cola llena, probá de nuevo en unos minutos." };
        }
        const idExterno = `cola-${this._siguienteId++}`;
        this._pendientes.push({ idExterno, telefono, texto });
        this._procesarEnSegundoPlano();
        return { ok: true, idExterno };
    }

    tamano() {
        return this._pendientes.length;
    }

    async _procesarEnSegundoPlano() {
        if (this._procesando) return;
        this._procesando = true;
        try {
            while (this._pendientes.length > 0 && !this._detenida) {
                const item = this._pendientes.shift();
                try {
                    await this._enviarAhora(item.telefono, item.texto);
                } catch (err) {
                    // Ya no hay a quién devolverle el error: la respuesta HTTP de /enviar
                    // ya salió hace rato. Queda solo en el log de este proceso.
                    // El teléfono es de un paciente: en el log va enmascarado (los últimos 4 dígitos), también si el
                    // mensaje de error de la librería lo repite.
                    this._log.error(ocultarNumeros(`Error enviando ${item.idExterno} a ${item.telefono}: ${err.message || err}`));
                }
                if (this._pendientes.length > 0) {
                    const espera = ESPACIADO_MIN_MS + Math.floor(Math.random() * ESPACIADO_JITTER_MS);
                    await new Promise(resolve => setTimeout(resolve, espera));
                }
            }
        } finally {
            this._procesando = false;
        }
    }
}

module.exports = { ColaEnvios, CAPACIDAD_MAXIMA };
