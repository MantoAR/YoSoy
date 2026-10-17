// Apagado ordenado del proceso (systemd manda SIGTERM al hacer stop o restart; la unit da 20 segundos de margen, TimeoutStopSec).
//
// El orden importa, porque lo que se pierde en un apagado a medias son mensajes y credenciales:
//   1. dejar de aceptar pedidos nuevos: se cierra el puerto y la cola deja de despachar (lo que ya salió, salió);
//   2. esperar (con tope) al envío que esté en curso, para que no se corte a la mitad y para que el contador del día lo cuente;
//   3. cerrar WhatsApp SIN cerrar la sesión (el número sigue vinculado) y guardar lo que quede por escribir: las credenciales, los
//      consentimientos y los contadores. Se hacen a la vez y cada uno por su cuenta: si WhatsApp tarda en soltar el socket, igual
//      se guardan los datos.
// Todo el apagado tiene un tope; si se pasa, se sale igual con lo que haya: nunca se bloquea esperando a una librería.
// Se sale con código 0: fue un pedido de apagado, no una falla (con otro código, Restart=on-failure lo volvería a levantar).
"use strict";

const { ocultarNumeros } = require("./privacidad");

const RELOJ_REAL = Object.freeze({
    setTimeout: (...a) => setTimeout(...a),
    clearTimeout: (...a) => clearTimeout(...a)
});

function crearApagado({ servidor, cola, wa, log, salir = codigo => process.exit(codigo), reloj = RELOJ_REAL, presupuestoMs = 10000, esperaEnvioMs = 5000 }) {
    let apagando = false;

    const intentar = async (que, accion) => {
        try { await accion(); }
        catch (err) { log.error(ocultarNumeros(`Error cerrando ${que}: ${err?.message || err}`)); }
    };

    return async function apagar(señal) {
        if (apagando) {
            log.info(`${señal} recibido de nuevo: ya se está apagando.`);
            return;
        }
        apagando = true;
        log.info(`${señal} recibido, apagando...`);

        // 1. Nada nuevo.
        cola.detener();
        try { servidor.close(); servidor.closeIdleConnections?.(); }
        catch (err) { log.error(ocultarNumeros(`Error cerrando el servidor HTTP: ${err?.message || err}`)); }

        // 2 y 3, con un tope para todo.
        let temporizador;
        const tope = new Promise((_, rechazar) => {
            temporizador = reloj.setTimeout(() => rechazar(new Error(`El apagado ordenado no terminó en ${Math.round(presupuestoMs / 1000)} s`)), presupuestoMs);
        });
        const ordenado = (async () => {
            const termino = await cola.esperarEnCurso(esperaEnvioMs);
            if (!termino) log.error(`Un envío no terminó en ${Math.round(esperaEnvioMs / 1000)} s; se sigue con el apagado.`);
            await Promise.all([
                intentar("el cliente de WhatsApp", () => wa.cerrar()),
                intentar("los datos del servicio", () => wa.datos?.cerrar())
            ]);
        })();
        ordenado.catch(() => {});                      // si el tope gana, lo que le pase después a la secuencia ya no importa
        try {
            await Promise.race([ordenado, tope]);
            log.info("Apagado completo.");
        } catch (err) {
            log.error(ocultarNumeros(`${err?.message || err} — se sale igual.`));
        } finally {
            reloj.clearTimeout(temporizador);
        }
        salir(0);
    };
}

module.exports = { crearApagado };
