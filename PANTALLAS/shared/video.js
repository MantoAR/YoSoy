/* ═══════════════════════════════════════════════════════════════
   YoSoy · piezas compartidas de la sala de video (window.YS.video)

   Las usan las dos sesion_video.html (paciente y profesional), para no repetir en cada una
   dónde está la señalización ni cómo se le explica un problema a quien está por entrar a una
   sesión. Va después de shared/ui.js (define window.YS) y, para usar conectar(), también
   después del cliente de Socket.IO (shared/vendor/socket.io.min.js).
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS = window.YS || {};

    // ── Dónde está la señalización ──────────────────────────────
    // Producción: Caddy manda /socket.io/ del mismo dominio al proceso de señalización, así
    // que alcanza con el origen de la página (https → wss). Desarrollo: la API sirve las
    // pantallas en http://localhost:5000 y la señalización es otro proceso, en el :3001 del
    // mismo host. Abierta como archivo suelto (file://) cae a localhost, igual que YS.API.
    const HOSTS_LOCALES = ["localhost", "127.0.0.1", "[::1]"];
    function urlSenalizacion() {
        if (!location.protocol.startsWith("http")) return "http://localhost:3001";
        return HOSTS_LOCALES.includes(location.hostname) ? `http://${location.hostname}:3001` : location.origin;
    }
    const SIGNAL = urlSenalizacion();

    // ── Cámara y micrófono ──────────────────────────────────────
    /** Qué decirle a quien quiere entrar a una sala, según por qué falló getUserMedia. */
    function mensajeMedios(err) {
        switch (err && err.name) {
            case "NotAllowedError":
            case "PermissionDeniedError":
                return "El navegador no tiene permiso para usar la cámara y el micrófono. Habilitalos desde el candado de la barra de direcciones (y revisá los permisos de privacidad del sistema) y probá de nuevo.";
            case "NotFoundError":
            case "DevicesNotFoundError":
            case "OverconstrainedError":
                return "No se encontró una cámara o un micrófono en este equipo. Conectá lo que falte y probá de nuevo.";
            case "NotReadableError":
            case "TrackStartError":
            case "AbortError":
                return "No se pudo usar la cámara o el micrófono: puede que otra aplicación (o pestaña) los esté usando. Cerrala y probá de nuevo.";
            case "SecurityError":
                return "El navegador bloqueó la cámara y el micrófono en esta página. YoSoy tiene que abrirse por una dirección segura (https).";
        }
        // Fuera de https (y de localhost) el navegador ni siquiera expone navigator.mediaDevices.
        if (err instanceof TypeError || !navigator.mediaDevices)
            return "Este navegador no permite usar la cámara y el micrófono desde esta dirección. Abrí YoSoy por https con un navegador actualizado.";
        return "No se pudo acceder a la cámara o al micrófono" + (err && err.name ? ` (${err.name})` : "") + ".";
    }

    // ── Señalización ────────────────────────────────────────────
    const MAX_FALLOS = 5;       // connect_error seguidos antes de darse por vencido
    const TIMEOUT_MS = 10000;   // por intento (el default de Socket.IO son 20 s)
    const SUGERENCIA = "Revisá tu conexión y probá de nuevo; si sigue igual, avisale al equipo de YoSoy.";

    // Fallas de transporte (el servidor no contesta, un proxy devuelve 502…) vs. un rechazo
    // del propio servidor de señalización.
    const TRANSPORTE = /(poll|websocket|xhr).*error|transport|timeout/i;
    // Rechazos del handshake (next(new Error("..."))) que conviene decir con otras palabras.
    const RECHAZOS = { "no autorizado": "No tenés acceso a esta sala." };

    /** Qué decirle al usuario ante un connect_error y si tiene sentido seguir reintentando. */
    function describirFalloConexion(err) {
        const msg = String((err && err.message) || "").trim();
        if ((err && err.type === "TransportError") || TRANSPORTE.test(msg))
            return { texto: "No se pudo conectar con el servicio de videollamada.", definitivo: false };
        // El servidor sí atendió y dijo que no: reintentar no lo cambia.
        return { texto: RECHAZOS[msg.toLowerCase()] || `No se pudo entrar a la sala${msg ? ": " + msg : ""}.`, definitivo: true };
    }

    /** ¿La llamada ya está andando? Una vez conectado, el video no depende de la señalización. */
    function conexionActiva(pc) {
        if (!pc) return false;
        return pc.connectionState === "connected"
            || pc.iceConnectionState === "connected" || pc.iceConnectionState === "completed";
    }

    /**
     * Abre el socket con la señalización y se ocupa de lo que le pasa a la conexión, para que
     * ninguna pantalla se quede en "Conectando…" sin decir por qué. Devuelve el socket (cada
     * pantalla le suma sus eventos: joined, offer, answer…) o null si no está cargado el
     * cliente de Socket.IO, caso en el que ya se llamó a alFallo.
     *   mediaActiva()  → true si la llamada ya está andando (ver conexionActiva).
     *   alAviso(texto) → problema pasajero: Socket.IO sigue reintentando. Solo se llama mientras
     *                    la llamada no está activa, porque con la llamada andando no hay nada
     *                    que avisar.
     *   alRecuperar()  → se reconectó después de un alAviso; hay que sacar el aviso.
     *   alFallo(texto, { llamadaActiva }) → problema definitivo: la señalización rechazó el
     *                    ingreso, cortó, o no hubo forma de conectar tras varios intentos. Se
     *                    llama una sola vez y el socket queda cerrado, sin más reintentos.
     */
    function conectar({ mediaActiva = () => false, alAviso = () => {}, alRecuperar = () => {}, alFallo = () => {} } = {}) {
        if (typeof io !== "function") {
            alFallo("No se pudo cargar el componente de videollamada. Recargá la página.", { llamadaActiva: false });
            return null;
        }
        const socket = io(SIGNAL, { timeout: TIMEOUT_MS });
        let fallos = 0;        // connect_error seguidos desde la última vez que conectó
        let avisado = false;   // ¿hay un "Reintentando…" en pantalla que desmentir al reconectar?
        let terminado = false;

        const fallar = texto => {
            if (terminado) return;
            terminado = true;
            socket.disconnect();   // frena los reintentos automáticos de Socket.IO
            alFallo(texto, { llamadaActiva: mediaActiva() });
        };
        const avisar = texto => {
            if (mediaActiva()) return;
            avisado = true;
            alAviso(texto);
        };

        socket.on("connect", () => {
            fallos = 0;
            if (avisado && !mediaActiva()) alRecuperar();
            avisado = false;
        });

        socket.on("connect_error", err => {
            console.warn("Señalización: no se pudo conectar —", err && err.message);
            const f = describirFalloConexion(err);
            fallos++;
            if (f.definitivo) return fallar(f.texto);
            // Con la llamada andando se sigue reintentando por si hay que renegociar; sin ella, no.
            if (fallos >= MAX_FALLOS && !mediaActiva()) return fallar(`${f.texto} ${SUGERENCIA}`);
            avisar(`${f.texto} Reintentando…`);
        });

        // La señalización explica por qué rechaza (sala completa, sin acceso…) y después corta.
        socket.on("rechazado", datos => fallar((datos && datos.motivo) || "No tenés acceso a esta sala."));

        socket.on("disconnect", motivo => {
            if (terminado || motivo === "io client disconnect") return;
            if (motivo === "io server disconnect") return fallar("El servicio de videollamada cerró la conexión.");
            avisar("Se perdió la conexión con el servicio de videollamada. Reintentando…");
        });

        return socket;
    }

    YS.video = { SIGNAL, mensajeMedios, conexionActiva, conectar };
})();
