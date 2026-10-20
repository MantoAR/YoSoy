/* Traduce el estado de WhatsApp (lo que responde GET /mensajeria/whatsapp/estado) a lo que se le muestra a la persona.
 *
 * Una función pura, sin DOM ni red: la usan la pantalla de WhatsApp del Configurador (js/dashboard.js) y la bandeja de Chats del CRM
 * (Pantalla_ChatSimulado/js/bandeja.js), y se prueba con Node (YoSoy.Tests/WhatsAppEstadoJsTests.cs).
 *
 * Entrada: { servicioDisponible, conectado, numero, qrPendiente, qr, estado, motivo, desde, intentosDeReconexion }
 *   estado/motivo son los de whatsapp-service: detenido · iniciando · conectado · reconectando · requiere_vincular
 *   (esperando_escaneo · qr_vencido · sesion_cerrada · error_borrando_sesion) · requiere_atencion (cuenta_restringida · sesion_reemplazada
 *   · version_rechazada · sesion_invalida). Una API más vieja puede no mandarlos: se deduce de conectado y qrPendiente.
 *
 * Salida: { clave, tono, titulo, detalle, desconectado, conectando, puedePedirQr, puedeGenerarNuevo, puedeDesvincular, mostrarQr }
 *   clave: no-disponible · conectado · conectando · escanear · vincular · atencion · desconocido
 *   Los estados en los que el servicio está abriendo la conexión (iniciando, reconectando, detenido) son "conectando": no se pasa por
 *   "desconectado" en el medio, que era lo que hacía saltar la pantalla entre estados. Si iniciando o reconectando llevan más de
 *   MINUTOS_SIN_CONECTAR (según `desde`), el texto lo dice. Un estado que esta pantalla no conoce se muestra como tal, sin botones.
 *   `ahora` (milisegundos, opcional) reemplaza a Date.now(): para probarlo.
 */
(function (raiz) {
    "use strict";

    // Desde cuántos intentos seguidos de reconexión se aclara que sigue intentando (la espera crece hasta 5 minutos: no es un error).
    const INTENTOS_PARA_AVISAR = 3;
    // Desde cuántos minutos en iniciando/reconectando el texto deja de ser "un momento" y dice cuánto lleva sin conectar.
    const MINUTOS_SIN_CONECTAR = 5;

    const MOTIVOS_ATENCION = {
        cuenta_restringida: "WhatsApp restringió o bloqueó el número. Hay que revisar el teléfono de la clínica; el servicio no vuelve a intentar solo.",
        sesion_reemplazada: "Otra conexión está usando la misma sesión de WhatsApp. Hay que verificar que no haya otra copia del servicio en marcha; el servicio no vuelve a intentar solo.",
        version_rechazada: "WhatsApp rechazó la versión que usa el servicio. Hace falta actualizarlo; el servicio no vuelve a intentar solo.",
        sesion_invalida: "WhatsApp rechazó la sesión guardada varias veces seguidas. El servicio no vuelve a intentar solo.",
        error_borrando_sesion: "No se pudo borrar la sesión anterior (permisos de la carpeta de la sesión). Un código QR no lo arregla."
    };
    const AVISO_ATENCION = " Avisale a quien administra el servidor.";

    function base(clave, tono, titulo, detalle, extra) {
        return Object.assign({
            clave, tono, titulo, detalle,
            desconectado: clave !== "conectado",
            conectando: clave === "conectando",
            puedePedirQr: false, puedeGenerarNuevo: false, puedeDesvincular: false, mostrarQr: false
        }, extra || {});
    }

    function vista(e) {
        e = e || {};
        if (!e.servicioDisponible)
            return base("no-disponible", "neutral", "Servicio no disponible",
                "No se pudo conectar con el servicio de WhatsApp. Si recién se reinició puede demorar unos segundos; si sigue así, avisale a quien administra el servidor.");
        if (e.conectado)
            return base("conectado", "ok", "Conectado", "", { puedeDesvincular: true });

        const estado = e.estado || null;
        const motivo = e.motivo || null;

        // Una API que todavía no manda estado/motivo: se deduce de lo que sí manda.
        if (!estado) {
            if (e.qrPendiente || e.qr) return base("escanear", "warn", "Esperando que escanees el código", "", { mostrarQr: !!e.qr, puedeGenerarNuevo: !!e.qr });
            return base("conectando", "info", "Conectando…", "Un momento: se está abriendo la conexión con WhatsApp.");
        }

        if (estado === "iniciando" || estado === "reconectando" || estado === "detenido") {
            const ms = estado !== "detenido" && e.desde ? (e.ahora ?? Date.now()) - new Date(e.desde).getTime() : NaN;
            if (ms > MINUTOS_SIN_CONECTAR * 60000) {
                const minutos = Math.floor(ms / 60000);
                const partes = ["Lleva más de " + minutos + " minutos sin conectar."];
                if (e.intentosDeReconexion) partes.push("Intento " + e.intentosDeReconexion + ".");
                if (motivo) partes.push("Motivo: " + motivo + ".");
                return base("conectando", "warn", "Conectando…", partes.join(" "));
            }
            const largo = estado === "reconectando" && (e.intentosDeReconexion || 0) >= INTENTOS_PARA_AVISAR;
            return base("conectando", "info", "Conectando…",
                largo ? "Sigue intentando conectarse (intento " + e.intentosDeReconexion + "); la espera entre intentos va creciendo. No hace falta hacer nada."
                      : "Un momento: se está abriendo la conexión con WhatsApp.");
        }

        if (estado === "requiere_vincular") {
            if (motivo === "esperando_escaneo")
                return base("escanear", "warn", "Esperando que escanees el código",
                    e.qr ? "" : "Preparando el código QR…", { mostrarQr: !!e.qr, puedeGenerarNuevo: !!e.qr });
            if (motivo === "error_borrando_sesion")
                return base("atencion", "danger", "El servicio necesita revisión", MOTIVOS_ATENCION.error_borrando_sesion + AVISO_ATENCION);
            if (motivo === "sesion_cerrada")
                return base("vincular", "danger", "Hace falta vincular el número",
                    "WhatsApp cerró la sesión (por ejemplo, se desvinculó desde el teléfono). Hay que escanear un código QR para volver a vincular.", { puedePedirQr: true });
            return base("vincular", "warn", "Hace falta vincular el número",
                "El código QR venció sin escanearse. Pedí uno nuevo y escanealo desde el teléfono de la clínica.", { puedePedirQr: true });
        }

        if (estado === "requiere_atencion")
            return base("atencion", "danger", "El servicio necesita revisión", (MOTIVOS_ATENCION[motivo] || "WhatsApp cortó la conexión por un motivo que reintentar no arregla; el servicio no vuelve a intentar solo.") + AVISO_ATENCION);

        // Un estado que esta pantalla no conoce: se muestra tal cual vino, sin botones (no se inventa qué hacer).
        return base("desconocido", "warn", "Estado no reconocido (" + estado + ")", motivo ? "Motivo: " + motivo + "." : "");
    }

    // El aviso de la bandeja de Chats (módulo CRM). Quien atiende los chats no vincula el número: eso lo hace el Configurador.
    // Entrada: lo que responde GET /mensajeria/conexion-whatsapp ({ aplica, servicioDisponible, conectado, estado, motivo }).
    // Devuelve null si no hay nada que avisar (el canal no es WhatsApp, o está conectado).
    function avisoDeBandeja(c) {
        if (!c || c.aplica === false || c.conectado) return null;
        const v = vista({ servicioDisponible: c.servicioDisponible !== false, conectado: false, estado: c.estado, motivo: c.motivo });
        if (v.clave === "conectando")
            return { tono: "info", texto: "WhatsApp se está conectando. Mientras tanto los mensajes no salen ni llegan." };
        return {
            tono: "warn",
            texto: "WhatsApp está desconectado: los mensajes no salen ni llegan hasta que se vuelva a vincular. La vinculación la hace el Configurador (Configurador › WhatsApp)."
        };
    }

    const api = { vista, avisoDeBandeja, INTENTOS_PARA_AVISAR };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else { raiz.YS = raiz.YS || {}; raiz.YS.whatsappEstado = api; }
})(typeof window !== "undefined" ? window : globalThis);
