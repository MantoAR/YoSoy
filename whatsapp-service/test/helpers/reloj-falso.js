// Reloj de mentira para los tests: setTimeout/clearTimeout/ahora controlados a mano, así la espera de una reconexión de cinco
// minutos se prueba en un instante y de forma exacta. Misma forma que RELOJ_REAL de baileys/servicio.js.
"use strict";

// Deja correr las promesas pendientes y los callbacks de I/O ya listos, sin esperar tiempo real.
async function drenar() {
    for (let i = 0; i < 10; i++) await Promise.resolve();
    await new Promise(resolver => setImmediate(resolver));
}

class RelojFalso {
    constructor(inicio = Date.UTC(2026, 9, 16, 15, 0, 0)) {   // 2026-10-16 12:00 en Buenos Aires
        this._ahora = inicio;
        this._timers = [];
        this._siguienteId = 1;
        this.setTimeout = (fn, ms = 0, ...args) => {
            const id = this._siguienteId++;
            this._timers.push({ id, en: this._ahora + Math.max(0, ms), fn, args });
            return id;
        };
        this.clearTimeout = id => { this._timers = this._timers.filter(t => t.id !== id); };
        this.ahora = () => this._ahora;
    }

    pendientes() { return this._timers.length; }
    proximo() { return this._timers.length ? Math.min(...this._timers.map(t => t.en)) - this._ahora : null; }

    // Avanza el tiempo ms milisegundos, disparando en orden los temporizadores que vencen.
    async avanzar(ms) {
        const hasta = this._ahora + ms;
        await drenar();
        for (;;) {
            this._timers.sort((a, b) => a.en - b.en || a.id - b.id);
            const siguiente = this._timers[0];
            if (!siguiente || siguiente.en > hasta) break;
            this._timers.shift();
            this._ahora = Math.max(this._ahora, siguiente.en);
            siguiente.fn(...siguiente.args);
            await drenar();
        }
        this._ahora = hasta;
        await drenar();
    }
}

// Espera (en tiempo real, con un tope) a que se cumpla una condición: para lo que depende de I/O real, como los archivos.
async function esperarHasta(condicion, { tope = 3000, descripcion = "la condición" } = {}) {
    const inicio = Date.now();
    while (!condicion()) {
        if (Date.now() - inicio > tope) throw new Error(`Se esperó ${tope} ms a que se cumpla ${descripcion} y no pasó.`);
        await new Promise(resolver => setTimeout(resolver, 2));
    }
}

module.exports = { RelojFalso, esperarHasta, drenar };
