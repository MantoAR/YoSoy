// Registro de consentimiento para mandar WhatsApp (solo con Baileys). Responde una pregunta: "¿a este teléfono se le puede escribir?".
//
// Se puede si (y solo si):
//   - la persona NO pidió la baja (escribió BAJA, STOP...; eso pesa más que todo lo demás hasta que escriba ALTA), y
//   - o bien la API afirma que aceptó (optIn:true en el pedido de envío),
//   - o bien ella le escribió primero al número de la clínica hace menos de `diasDeVigencia` días (cada mensaje suyo lo renueva).
// En cualquier otro caso, no: el valor por defecto es NO enviar. Sin consentimiento no hay envíos, ni masivos ni de a uno.
//
// Qué se guarda (datos/consentimientos.json, solo del usuario del servicio): por cada persona, la HUELLA de su teléfono (no el
// teléfono: ver privacidad.js) y tres fechas. Nunca el texto de un mensaje. Este archivo no lo borra "desvincular": es el registro
// de quién le escribió a ESTE número; para empezar de cero con otro número, borrar la carpeta datos/.
"use strict";

const { huellaDeTelefono, interpretarComando, ocultarNumeros } = require("./privacidad");

const UN_DIA_MS = 24 * 60 * 60 * 1000;
const MAX_CONTACTOS = 50000;        // tope de entradas del archivo: al pasarlo se olvidan primero las constancias más viejas

class RegistroDeConsentimiento {
    constructor({ almacen, clave, diasDeVigencia = 365, reloj = { ahora: () => Date.now() }, log, maxContactos = MAX_CONTACTOS }) {
        this._almacen = almacen;
        this._clave = clave;
        this._vigenciaMs = diasDeVigencia * UN_DIA_MS;
        this._reloj = reloj;
        this._log = log;
        this._maxContactos = maxContactos;
        this._contactos = new Map();         // huella → { desde, ultimo, baja }
    }

    async cargar() {
        const guardado = await this._almacen.leer();
        if (guardado && guardado.contactos && typeof guardado.contactos === "object") {
            for (const [huella, c] of Object.entries(guardado.contactos)) {
                if (c && Number.isFinite(c.ultimo)) {
                    this._contactos.set(huella, { desde: Number.isFinite(c.desde) ? c.desde : c.ultimo, ultimo: c.ultimo, baja: Number.isFinite(c.baja) ? c.baja : null });
                }
            }
        }
        this._podar();
        return this._contactos.size;
    }

    // ¿Se le puede escribir a este teléfono? { ok:true, via } o { ok:false, estado, codigo, error }.
    permite(telefono, { optIn = false } = {}) {
        const huella = huellaDeTelefono(telefono, this._clave);
        if (huella === null) {
            return { ok: false, estado: 400, codigo: "telefono_invalido", error: "El teléfono no es válido." };
        }
        const c = this._contactos.get(huella);
        if (c?.baja) {
            return { ok: false, estado: 403, codigo: "baja", error: "La persona pidió no recibir más mensajes por WhatsApp (BAJA)." };
        }
        if (optIn === true) return { ok: true, via: "optin_api", huella };
        if (c && this._reloj.ahora() - c.ultimo <= this._vigenciaMs) return { ok: true, via: "escribio_primero", huella };
        return {
            ok: false, estado: 403, codigo: "sin_consentimiento",
            error: "No hay constancia de que la persona acepte recibir WhatsApp: no le escribió a este número y la API no marcó optIn."
        };
    }

    // Un mensaje de TEXTO de una persona al número de la clínica. Es la constancia de que escribió primero, o un pedido de baja/alta.
    alMensajeEntrante(telefono, texto) {
        const huella = huellaDeTelefono(telefono, this._clave);
        if (huella === null) return null;
        const ahora = this._reloj.ahora();
        const comando = interpretarComando(texto);
        const c = this._contactos.get(huella) ?? { desde: ahora, ultimo: ahora, baja: null };
        c.ultimo = ahora;
        if (comando === "baja") {
            c.baja = ahora;
            this._log?.info(ocultarNumeros(`Una persona (${telefono}) pidió no recibir más mensajes por WhatsApp.`));
        } else if (comando === "alta") {
            if (c.baja) this._log?.info(ocultarNumeros(`Una persona (${telefono}) volvió a aceptar mensajes por WhatsApp.`));
            c.baja = null;
        }
        this._contactos.set(huella, c);
        this._podar();
        this._guardar();
        return comando;
    }

    cantidad() { return this._contactos.size; }

    cerrar() { return this._almacen.cerrar(); }

    // Se olvidan las constancias vencidas (las bajas NO: una baja se recuerda siempre) y, si el archivo crece de más, las más viejas.
    _podar() {
        const limite = this._reloj.ahora() - this._vigenciaMs;
        for (const [huella, c] of this._contactos) {
            if (!c.baja && c.ultimo < limite) this._contactos.delete(huella);
        }
        if (this._contactos.size > this._maxContactos) {
            // Se baja hasta el 90 % del tope, para no ordenar todo el registro en cada mensaje que llega.
            const objetivo = Math.floor(this._maxContactos * 0.9);
            const viejos = [...this._contactos].filter(([, c]) => !c.baja).sort((a, b) => a[1].ultimo - b[1].ultimo);
            for (const [huella] of viejos.slice(0, Math.max(0, this._contactos.size - objetivo))) this._contactos.delete(huella);
        }
    }

    _guardar() {
        this._almacen.guardar({ v: 1, contactos: Object.fromEntries(this._contactos) });
    }
}

module.exports = { RegistroDeConsentimiento, MAX_CONTACTOS };
