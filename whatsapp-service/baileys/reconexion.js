// Cuándo y si se reintenta la conexión con WhatsApp. Dos piezas, sin tocar la librería ni el reloj real:
//
//   PlanDeReconexion        cuánto esperar entre intentos: backoff exponencial (2 s, 4 s, 8 s ... hasta 5 minutos) con ±20 % al azar
//                           para no reintentar a intervalos clavados; el contador vuelve a cero recién cuando la conexión se
//                           sostuvo un rato (una que abre y se cae enseguida sigue espaciándose).
//   clasificarDesconexion   qué significa el código con el que WhatsApp cortó. Hay cortes que se arreglan reintentando (la red,
//                           los servidores) y cortes que NO: reintentarlos en bucle no los arregla y contra WhatsApp es justo lo
//                           que puede terminar en un bloqueo del número.
"use strict";

// 405: WhatsApp rechaza la versión de WhatsApp Web con la que se identifica el cliente. No figura en DisconnectReason.
const VERSION_RECHAZADA = 405;

const TIPOS = Object.freeze({
    SESION_CERRADA: "sesion_cerrada",             // 401: se desvinculó desde el teléfono o WhatsApp la cerró → hay que volver a vincular
    CUENTA_RESTRINGIDA: "cuenta_restringida",     // 403: el número está restringido o bloqueado
    SESION_REEMPLAZADA: "sesion_reemplazada",     // 440: otra conexión usa las mismas credenciales
    VERSION_RECHAZADA: "version_rechazada",       // 405
    SESION_INVALIDA: "sesion_invalida",           // 500: WhatsApp rechaza la sesión guardada (solo cuenta si se repite)
    REINICIO: "reinicio",                         // 515: WhatsApp pide abrir la conexión de nuevo (pasa al terminar de vincular)
    TRANSITORIA: "transitoria"                    // todo lo demás: red, servidores, ping perdido...
});

function clasificarDesconexion(codigo, DR) {
    // Sin código numérico no se sabe nada: es un corte común. (Y `undefined === DR.x` nunca debe dar verdadero por una x que falta.)
    if (typeof codigo !== "number") return TIPOS.TRANSITORIA;
    if (codigo === DR.loggedOut) return TIPOS.SESION_CERRADA;
    if (codigo === DR.forbidden) return TIPOS.CUENTA_RESTRINGIDA;
    if (codigo === DR.connectionReplaced) return TIPOS.SESION_REEMPLAZADA;
    if (codigo === VERSION_RECHAZADA) return TIPOS.VERSION_RECHAZADA;
    if (codigo === DR.badSession) return TIPOS.SESION_INVALIDA;
    if (codigo === DR.restartRequired) return TIPOS.REINICIO;
    return TIPOS.TRANSITORIA;
}

class PlanDeReconexion {
    constructor({
        baseMs = 2000,
        topeMs = 5 * 60 * 1000,
        factor = 2,
        jitter = 0.2,
        estableMs = 60 * 1000,              // una conexión que se sostiene tanto vuelve el contador a cero
        reiniciosSeguidos = 3,              // pedidos de reinicio (515) que se atienden enseguida dentro de la ventana...
        ventanaReinicioMs = 60 * 1000,      // ...y recién después se espacian como cualquier otro corte
        aleatorio = Math.random
    } = {}) {
        this._baseMs = baseMs;
        this._topeMs = topeMs;
        this._factor = factor;
        this._jitter = jitter;
        this._estableMs = estableMs;
        this._reiniciosSeguidos = reiniciosSeguidos;
        this._ventanaReinicioMs = ventanaReinicioMs;
        this._aleatorio = aleatorio;
        this.reiniciar();
    }

    // Una persona pidió algo (arranque, vincular, desvincular): se empieza de cero.
    reiniciar() {
        this._intentos = 0;
        this._abiertaDesde = null;
        this._reinicios = [];
        this._ultimoTipo = null;
        this._seguidas = 0;
    }

    get intentos() { return this._intentos; }

    alAbrir(ahora) {
        this._abiertaDesde = ahora;
        this._ultimoTipo = null;
        this._seguidas = 0;
    }

    // Se llama en cada corte, antes de pedir la demora: si la conexión que se cayó se había sostenido, el contador vuelve a cero.
    alCerrar(ahora) {
        if (this._abiertaDesde !== null && ahora - this._abiertaDesde >= this._estableMs) {
            this._intentos = 0;
            this._reinicios = [];
        }
        this._abiertaDesde = null;
    }

    // Cuántos cortes seguidos del mismo tipo van (sin que la conexión llegara a abrir entre uno y otro).
    registrarFalla(tipo) {
        if (tipo === this._ultimoTipo) this._seguidas++;
        else { this._ultimoTipo = tipo; this._seguidas = 1; }
        return this._seguidas;
    }

    // La demora del próximo intento, en milisegundos: base · factor^intentos, con tope, ±jitter, y nunca más que el tope.
    siguiente() {
        const bruta = Math.min(this._topeMs, this._baseMs * this._factor ** this._intentos);
        this._intentos++;
        const variacion = 1 + (this._aleatorio() * 2 - 1) * this._jitter;
        return Math.min(this._topeMs, Math.max(0, Math.round(bruta * variacion)));
    }

    // WhatsApp pide abrir la conexión de nuevo (515). Se atiende enseguida, pero no sin fin: si pide demasiadas veces en poco
    // tiempo, se espacia como cualquier otro corte.
    reinicioPedido(ahora) {
        this._reinicios = this._reinicios.filter(t => ahora - t < this._ventanaReinicioMs);
        if (this._reinicios.length >= this._reiniciosSeguidos) return this.siguiente();
        this._reinicios.push(ahora);
        return 0;
    }
}

module.exports = { PlanDeReconexion, clasificarDesconexion, TIPOS, VERSION_RECHAZADA };
