// Contadores de ventana fija. Viven en memoria: alcanza porque hay un solo proceso, y reiniciar el servicio los
// pone en cero (nadie de afuera puede provocar eso).
'use strict';

/**
 * Cuenta intentos por clave (la IP). consumir(clave) suma uno y devuelve cuántos lleva en la ventana actual;
 * quien llama compara contra su máximo.
 */
function crearLimitador({ ventanaMs }) {
    const cuentas = new Map();
    const barrido = setInterval(() => {
        const ahora = Date.now();
        for (const [clave, c] of cuentas) if (ahora - c.inicio >= ventanaMs) cuentas.delete(clave);
    }, ventanaMs);
    barrido.unref();   // que el barrido no mantenga vivo el proceso
    return {
        consumir(clave) {
            const ahora = Date.now();
            let c = cuentas.get(clave);
            if (!c || ahora - c.inicio >= ventanaMs) {
                c = { inicio: ahora, n: 0 };
                cuentas.set(clave, c);
            }
            return ++c.n;
        },
        detener() { clearInterval(barrido); }
    };
}

/** Un solo contador (el de un socket). Devuelve true mientras no se pasó del máximo de la ventana. */
function crearContador({ ventanaMs, max }) {
    let inicio = 0;
    let n = 0;
    return () => {
        const ahora = Date.now();
        if (ahora - inicio >= ventanaMs) { inicio = ahora; n = 0; }
        return ++n <= max;
    };
}

module.exports = { crearLimitador, crearContador };
