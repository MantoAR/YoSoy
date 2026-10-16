// Topes de envío para no parecer un bot de spam ante WhatsApp (solo con Baileys):
//   - por día (en la zona horaria de la clínica): al llegar, no se acepta ningún pedido más hasta el día siguiente;
//   - por persona y por día: ningún paciente recibe una ráfaga, aunque la API se equivoque y repita un envío;
//   - por hora: al llegar, los mensajes ya aceptados esperan en la cola a que se libere lugar.
// Los contadores se guardan en datos/envios.json: un reinicio del servicio NO devuelve el cupo del día. Por persona se guarda la
// huella del teléfono, no el teléfono (ver privacidad.js).
//
// Cómo se cuenta: pedir lugar (reservar) cuando el mensaje entra a la cola, y confirmarlo cuando realmente salió. Un envío que
// falla devuelve su lugar (liberar): lo que no salió no gasta cupo. Los que están esperando en la cola ya cuentan contra el tope
// del día, así que todo lo que se acepta se puede mandar hoy.
"use strict";

const { huellaDeTelefono } = require("./privacidad");

const UNA_HORA_MS = 60 * 60 * 1000;

// "2026-10-16" para ese instante en esa zona horaria.
function diaEnZona(ahoraMs, zonaHoraria) {
    return new Intl.DateTimeFormat("en-CA", { timeZone: zonaHoraria, year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(ahoraMs));
}

class LimiteDeEnvios {
    constructor({ topeDiario, topePorHora, topePorDestinatario, zonaHoraria, clave, almacen, reloj = { ahora: () => Date.now() }, log }) {
        this._topeDiario = topeDiario;
        this._topePorHora = topePorHora;
        this._topePorDestinatario = topePorDestinatario;
        this._zona = zonaHoraria;
        this._clave = clave;
        this._almacen = almacen;
        this._reloj = reloj;
        this._log = log;

        this._dia = diaEnZona(reloj.ahora(), zonaHoraria);
        this._enviados = 0;
        this._porDestinatario = new Map();           // huella → enviados hoy
        this._ultimos = [];                          // cuándo salieron los de la última hora
        this._reservados = 0;                        // aceptados que esperan en la cola
        this._reservadosPor = new Map();             // huella → aceptados que esperan
    }

    async cargar() {
        const g = await this._almacen.leer();
        if (!g) return;
        if (g.dia === this._dia) {
            this._enviados = Number.isFinite(g.enviados) ? g.enviados : 0;
            for (const [huella, n] of Object.entries(g.porDestinatario ?? {})) if (Number.isFinite(n)) this._porDestinatario.set(huella, n);
        }
        this._ultimos = (Array.isArray(g.ultimos) ? g.ultimos : []).filter(t => Number.isFinite(t));      // lo de más de una hora lo poda puedeEnviarYa()
    }

    // Pide lugar para un mensaje a este teléfono. { ok:true, huella } o { ok:false, estado:429, codigo, error }.
    reservar(telefono) {
        this._cambioDeDia();
        const huella = huellaDeTelefono(telefono, this._clave);
        if (huella === null) return { ok: false, estado: 400, codigo: "telefono_invalido", error: "El teléfono no es válido." };

        if (this._enviados + this._reservados >= this._topeDiario) {
            return { ok: false, estado: 429, codigo: "tope_diario", error: `Se llegó al tope de ${this._topeDiario} mensajes por día; se vuelve a poder mañana.` };
        }
        const deEstaPersona = (this._porDestinatario.get(huella) ?? 0) + (this._reservadosPor.get(huella) ?? 0);
        if (deEstaPersona >= this._topePorDestinatario) {
            return { ok: false, estado: 429, codigo: "limite_destinatario", error: `A esta persona ya se le mandaron ${this._topePorDestinatario} mensajes hoy; se vuelve a poder mañana.` };
        }
        this._reservados++;
        this._reservadosPor.set(huella, (this._reservadosPor.get(huella) ?? 0) + 1);
        return { ok: true, huella };
    }

    // El mensaje salió: gasta cupo de verdad (y se guarda).
    confirmar(huella) {
        this._soltarReserva(huella);
        this._cambioDeDia();
        this._enviados++;
        this._porDestinatario.set(huella, (this._porDestinatario.get(huella) ?? 0) + 1);
        this._ultimos.push(this._reloj.ahora());
        this._guardar();
    }

    // El mensaje no salió (falló o se descartó): devuelve el lugar.
    liberar(huella) {
        this._soltarReserva(huella);
    }

    // ¿Se puede mandar uno ahora, sin pasar el tope por hora? { ok:true } o { ok:false, esperarMs }.
    puedeEnviarYa() {
        const ahora = this._reloj.ahora();
        this._ultimos = this._ultimos.filter(t => ahora - t < UNA_HORA_MS);
        if (this._ultimos.length < this._topePorHora) return { ok: true };
        return { ok: false, esperarMs: this._ultimos[0] + UNA_HORA_MS - ahora + 1 };
    }

    resumen() {
        this._cambioDeDia();
        this.puedeEnviarYa();
        return {
            dia: this._dia,
            enviadosHoy: this._enviados,
            enEspera: this._reservados,
            topeDiario: this._topeDiario,
            enviadosUltimaHora: this._ultimos.length,
            topePorHora: this._topePorHora,
            topePorDestinatario: this._topePorDestinatario
        };
    }

    cerrar() { return this._almacen.cerrar(); }

    _soltarReserva(huella) {
        if (this._reservados > 0) this._reservados--;
        const n = (this._reservadosPor.get(huella) ?? 0) - 1;
        if (n > 0) this._reservadosPor.set(huella, n); else this._reservadosPor.delete(huella);
    }

    // Al cambiar el día (en la zona de la clínica) los contadores empiezan de cero; lo que está esperando en la cola sigue contando.
    _cambioDeDia() {
        const hoy = diaEnZona(this._reloj.ahora(), this._zona);
        if (hoy === this._dia) return;
        this._dia = hoy;
        this._enviados = 0;
        this._porDestinatario.clear();
    }

    _guardar() {
        this._almacen.guardar({
            v: 1,
            dia: this._dia,
            enviados: this._enviados,
            porDestinatario: Object.fromEntries(this._porDestinatario),
            ultimos: this._ultimos
        });
    }
}

module.exports = { LimiteDeEnvios, diaEnZona };
