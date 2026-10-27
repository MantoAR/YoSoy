/* ═══════════════════════════════════════════════════════════════
   YoSoy · piezas compartidas de la sala de video (window.YS.video)

   Las usan las dos sesion_video.html (paciente y profesional), para no repetir en cada una cómo se pide el
   acceso a la sala, qué se le dice a la persona cuando algo sale mal ni cómo se mantiene la conexión.

   Quién decide qué:
     · La API decide si la persona puede entrar, a qué sala y con qué rol (POST /video/acceso). La pantalla solo
       dice DE QUÉ SESIÓN quiere entrar: { origen: "turno" | "admision", id }.
     · La señalización toma la sala y el rol del token que firmó la API. La pantalla no manda ni el código de sala
       ni el rol.
     · Los servidores ICE (STUN y, si hay, TURN con credenciales efímeras) vienen en la respuesta de la API: no se
       arman a mano.

   Va después de shared/ui.js (define window.YS, YS.api y YS.param) y, para usar entrar(), también después del
   cliente de Socket.IO (shared/vendor/socket.io.min.js). Contrato completo: docs/VIDEOLLAMADAS.md.
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS = window.YS || {};

    // ── Funciones que existen pero están apagadas ───────────────
    // Se encienden cambiando el valor acá, en una revisión del código; no hay forma de prenderlas desde la URL ni
    // desde el navegador de quien usa la pantalla.
    //   transcripcion: el reconocimiento de voz del navegador (Web Speech). En Chrome manda el audio a servidores
    //   de Google, así que no se ofrece hasta resolver la privacidad y el consentimiento (PR 6).
    const FLAGS = Object.freeze({ transcripcion: false });

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

    // ── De qué sesión se trata ──────────────────────────────────
    // Las pantallas se abren con ?turno=<IdTurno> o ?admision=<IdInforme>. También se acepta ?sala=<código> porque
    // las notificaciones, los links viejos y el campo "Código de sala" del paciente solo tienen el código, y los
    // códigos que genera la API llevan el id de su origen: SALA-<IdTurno>-<hex> y ADM-<IdInforme>-<hex>. De ahí se
    // saca QUÉ sesión se pide; el código en sí no abre nada: la API verifica que la persona sea parte de esa sesión
    // y devuelve la sala vigente.
    const RE_CODIGO = /^(SALA|ADM)-(\d{1,9})-[0-9A-Z]+$/i;

    // Un id son solo dígitos: ni signo, ni decimales, ni notación científica (Number("1e3") daría 1000).
    function enteroPositivo(valor) {
        if (!/^\d{1,10}$/.test(String(valor))) return null;
        const n = Number(valor);
        return n > 0 && n < 2147483648 ? n : null;
    }

    /** { origen, id } a partir de un código de sala, o null si no tiene el formato de los que genera la API. */
    function sesionDeCodigo(codigo) {
        const m = RE_CODIGO.exec(String(codigo || "").trim());
        const id = m && enteroPositivo(m[2]);
        return id ? { origen: m[1].toUpperCase() === "SALA" ? "turno" : "admision", id } : null;
    }

    /** { origen, id } de la URL de la página (?turno=, ?admision= o, si no, ?sala=), o null. */
    function sesionDeLaUrl() {
        const turno = enteroPositivo(YS.param("turno"));
        if (turno) return { origen: "turno", id: turno };
        const admision = enteroPositivo(YS.param("admision"));
        if (admision) return { origen: "admision", id: admision };
        return sesionDeCodigo(YS.param("sala"));
    }

    // ── Acceso a la sala: POST /video/acceso ────────────────────
    /**
     * "a las 09:50" (o "el 14/10 a las 09:50" si no es hoy), en la hora de quien mira. Se arma a mano y no con
     * toLocale*String: según el navegador, 2-digit se ignora ("5/1") y hour12:false puede dar "24:30".
     */
    function cuando(iso) {
        const f = new Date(iso);
        if (isNaN(f)) return null;
        const dos = n => String(n).padStart(2, "0");
        const hora = `${dos(f.getHours())}:${dos(f.getMinutes())}`;
        if (f.toDateString() === new Date().toDateString()) return `a las ${hora}`;
        return `el ${dos(f.getDate())}/${dos(f.getMonth() + 1)} a las ${hora}`;
    }

    /**
     * Qué decirle a la persona cuando la API no la deja entrar. err es el Error de YS.api (.status y .datos con el
     * cuerpo { mensaje, codigo, abreUtc | cerroUtc }). paraRol ("paciente" | "profesional") solo cambia el texto de
     * "sala no generada", que es distinto según quién lo lee.
     */
    function mensajeAcceso(err, paraRol) {
        const status = err && err.status;
        const datos = (err && err.datos) || {};
        if (status === 0 || (err && err.sinConexion))
            return "No se pudo conectar con el servidor. Revisá tu conexión y probá de nuevo.";
        switch (status) {
            case 400:
                return "No se pudo identificar la sesión a la que querés entrar. Volvé a abrirla desde tu panel.";
            case 401:
                return "Tu sesión de YoSoy venció. Ingresá de nuevo para entrar a la sala.";
            case 404:
                // La API contesta lo mismo si la sesión no existe y si existe pero no es tuya: no se puede distinguir.
                return "No encontramos esa sesión. Revisá que el código sea el correcto y que la sesión sea tuya.";
            case 409:
                switch (datos.codigo) {
                    case "aun-no-abre": {
                        const hora = cuando(datos.abreUtc);
                        return "Todavía no se puede entrar a la sala" + (hora ? `: se abre ${hora}.` : ": se abre unos minutos antes del horario.");
                    }
                    case "ya-termino": {
                        const hora = cuando(datos.cerroUtc);
                        return "Esta sesión ya terminó" + (hora ? ` (la sala cerró ${hora}).` : ".");
                    }
                    case "estado-invalido":
                        // El detalle (cancelado, pendiente, realizado…) lo sabe la API.
                        return datos.mensaje || "Esta sesión no está disponible para una videollamada.";
                    case "sala-no-generada":
                        return paraRol === "profesional"
                            ? "Este turno todavía no tiene sala. Generala desde Inicio (\"Iniciar sesión\") y volvé a entrar."
                            : "Tu profesional todavía no generó la sala de esta sesión. Probá de nuevo en unos minutos.";
                    case "sala-invalida":
                        return "La sala de esta sesión no está disponible por un problema del sistema. Avisale al equipo de YoSoy.";
                }
                return datos.mensaje || "No se puede entrar a la sala en este momento.";
            case 429:
                return "Hiciste demasiados intentos seguidos. Esperá un minuto y probá de nuevo.";
            case 503:
                return "La videollamada no está disponible por el momento. Probá de nuevo más tarde; si sigue igual, avisale al equipo de YoSoy.";
        }
        if (status >= 500) return "El servidor tuvo un problema. Probá de nuevo en un rato.";
        return datos.mensaje || YS.mensajeHttp(status);
    }

    /**
     * Pide el acceso a la sala de una sesión con el JWT de la sesión de YoSoy (YS.api lo manda). Devuelve la
     * respuesta de la API { token, sala, rol, expiraUtc, turnDisponible, iceServers }. Si no, tira un Error con el
     * texto listo para mostrar (.message) y .status / .codigo de la API.
     */
    async function pedirAcceso(sesion, { paraRol } = {}) {
        let datos;
        try {
            // sinRedireccion: un 401 acá no tiene que sacar a la persona de la página (perdería lo que ya cargó).
            datos = await YS.api("/video/acceso", {
                method: "POST", json: { origen: sesion.origen, id: sesion.id }, sinRedireccion: true
            });
        } catch (err) {
            throw Object.assign(new Error(mensajeAcceso(err, paraRol)),
                { status: err && err.status, codigo: err && err.datos && err.datos.codigo });
        }
        if (!datos || typeof datos.token !== "string" || typeof datos.rol !== "string" || !Array.isArray(datos.iceServers))
            throw Object.assign(new Error("La respuesta del servidor no es la esperada. Probá de nuevo; si sigue igual, avisale al equipo de YoSoy."), { status: 200 });
        return datos;
    }

    // ── Señalización y conexión entre navegadores ───────────────
    const MAX_FALLOS = 5;               // connect_error seguidos antes de darse por vencido
    const TIMEOUT_MS = 10000;           // por intento (el default de Socket.IO son 20 s)
    const GRACIA_DISCONNECTED_MS = 4000;  // "disconnected" suele ser un bache: se espera antes de reiniciar ICE
    const PACIENCIA_MEDIOS_MS = 30000;  // sin volver a "connected" en este tiempo, se da por perdida la conexión
    const MAX_REINICIOS_ICE = 3;
    const SUGERENCIA = "Revisá tu conexión y probá de nuevo; si sigue igual, avisale al equipo de YoSoy.";

    // Fallas de transporte (el servidor no contesta, un proxy devuelve 502…) vs. un rechazo
    // del propio servidor de señalización.
    const TRANSPORTE = /(poll|websocket|xhr).*error|transport|timeout/i;

    /** Qué decirle al usuario ante un connect_error de transporte y si tiene sentido seguir reintentando. */
    function describirFalloConexion(err) {
        const msg = String((err && err.message) || "").trim();
        if ((err && err.type === "TransportError") || TRANSPORTE.test(msg))
            return { texto: "No se pudo conectar con el servicio de videollamada.", definitivo: false };
        // El servidor sí atendió y dijo que no: reintentar no lo cambia.
        return { texto: `No se pudo entrar a la sala${msg ? ": " + msg : ""}.`, definitivo: true };
    }

    // Lo que ve la persona cuando la señalización la saca de la sala (evento "rechazado", con su código).
    function mensajeRechazo(datos, rol) {
        switch (datos && datos.codigo) {
            case "rol-ocupado":
                return `Ya hay otra persona conectada en esta sala como ${rol}. Si fuiste vos desde otra pestaña, otro navegador o `
                    + "otro equipo, cerralo; si es otra persona, esperá a que salga. Después probá de nuevo.";
            case "sala-llena":
                return "La sala ya tiene a sus dos participantes. Si esperabas entrar a esta sesión, avisale al equipo de YoSoy.";
            case "reemplazado":
                return "Entraste a esta sala desde otro lugar (otra pestaña, navegador o equipo), así que esta conexión se cerró.";
            case "limite-eventos":
                return "Se enviaron demasiados mensajes seguidos y se cortó la conexión. Volvé a entrar a la sala.";
        }
        return (datos && datos.motivo) || "No tenés acceso a esta sala.";
    }

    /** ¿La llamada ya está andando? Una vez conectado, el video no depende de la señalización. */
    function conexionActiva(pc) {
        if (!pc) return false;
        return pc.connectionState === "connected"
            || pc.iceConnectionState === "connected" || pc.iceConnectionState === "completed";
    }

    /**
     * Entra a la sala con el acceso que dio la API: abre la señalización mandando el token, arma la conexión entre
     * navegadores con los iceServers de la respuesta y la mantiene (reinicia ICE si se corta, pide un acceso nuevo
     * si el token vence). Devuelve un control { cerrar() }, o null si no está cargado el cliente de Socket.IO (caso
     * en el que ya se llamó a alFallo).
     *
     *   sesion   { origen, id }     la misma que se pasó a pedirAcceso (hace falta para renovar el token)
     *   acceso   respuesta de pedirAcceso
     *   stream   MediaStream local (getUserMedia ya resuelto); lo cierra la pantalla, no esto
     *   paraRol  "paciente" | "profesional": solo cambia textos (el rol real es acceso.rol, el de la API)
     *
     * Avisos a la pantalla (todos opcionales):
     *   alVideoRemoto(stream)    llegó el video de la otra persona (una vez por conexión; puede repetirse si ella
     *                            vuelve a entrar). La pantalla lo pone en su <video>.
     *   alConectado()            la llamada quedó andando (la primera vez y cada vez que se recupera)
     *   alOtroSeFue({ motivo })  la otra persona salió de la sala
     *   alAviso(texto)           problema pasajero; se sigue reintentando
     *   alRecuperar()            se reconectó la señalización después de un alAviso (con la llamada todavía sin
     *                            andar: con la llamada andando no se avisa nada de la señalización)
     *   alFallo(texto, { llamadaActiva })  problema definitivo; se llama una sola vez y no hay más reintentos. Con
     *                            llamadaActiva la conexión entre navegadores sigue viva y la pantalla decide si la
     *                            deja; si no, ya quedó todo cerrado y conviene volver a la pantalla de espera.
     */
    function entrar({
        sesion, acceso, stream, paraRol,
        alVideoRemoto = () => {}, alConectado = () => {}, alOtroSeFue = () => {},
        alAviso = () => {}, alRecuperar = () => {}, alFallo = () => {}, alLlamadaTerminada = () => {}
    }) {
        if (typeof io !== "function") {
            alFallo("No se pudo cargar el componente de videollamada. Recargá la página.", { llamadaActiva: false });
            return null;
        }
        if (typeof RTCPeerConnection !== "function") {
            alFallo("Este navegador no permite videollamadas. Usá una versión actualizada de Chrome, Edge, Firefox o Safari.", { llamadaActiva: false });
            return null;
        }

        const rol = acceso.rol;               // lo decidió la API, no la pantalla
        const ofrece = rol === "paciente";    // un solo lado arma las ofertas: no hay ofertas cruzadas
        let token = acceso.token;             // se reemplaza al renovar el acceso
        let tokenDelIntento = null;           // el que mandó el último intento de conexión
        let iceServers = acceso.iceServers;
        let pc = null, remoto = null, candidatosPendientes = [];
        let socket = null;
        let terminado = false;                // ya no hay más intentos (falla definitiva o cerrar())
        let renovando = false, renovaciones = 0;
        let fallos = 0, avisado = false;      // señalización
        let otroSeFue = false;                // llegó un "user-left" y la otra persona no volvió todavía
        let reinicios = 0, avisoMedios = false;   // ICE
        let timerGracia = null, timerPaciencia = null;
        let cola = Promise.resolve();

        const mediaActiva = () => conexionActiva(pc);
        // Los eventos de señalización se atienden de a uno, en orden: sin esto, un ice-candidate puede intentar
        // sumarse antes de que setRemoteDescription termine y se pierde.
        const encolar = tarea => { cola = cola.then(tarea).catch(e => console.warn("Videollamada:", e && (e.message || e.name))); };
        const enviar = (evento, carga) => { if (socket && socket.connected) socket.emit(evento, carga); };
        const avisar = texto => { if (mediaActiva()) return; avisado = true; alAviso(texto); };

        // ── Conexión entre navegadores ──
        function crearConexion() {
            const nueva = new RTCPeerConnection({ iceServers });
            stream.getTracks().forEach(t => nueva.addTrack(t, stream));
            nueva.ontrack = e => {
                if (nueva !== pc) return;
                const s = e.streams && e.streams[0];
                // ontrack llega una vez por pista (audio y video): el video remoto se avisa una sola vez por conexión.
                if (!s || s === remoto) return;
                remoto = s;
                alVideoRemoto(s);
            };
            nueva.onicecandidate = e => { if (nueva === pc && e.candidate) enviar("ice-candidate", { candidate: e.candidate }); };
            nueva.oniceconnectionstatechange = () => { if (nueva === pc) alCambioDeIce(); };
            return nueva;
        }

        function cerrarConexion() {
            clearTimeout(timerGracia); clearTimeout(timerPaciencia);
            if (pc) { pc.ontrack = pc.onicecandidate = pc.oniceconnectionstatechange = null; pc.close(); }
            pc = null; remoto = null; candidatosPendientes = []; reinicios = 0; avisoMedios = false;
        }

        // La otra persona entró (o volvió): una conexión que ya se usó no sirve para alguien nuevo (otro navegador, otra
        // página recargada), así que se arma una limpia. Si todavía no se negoció nada, se aprovecha la que hay.
        function prepararConexion() {
            if (pc && !pc.remoteDescription) return;
            cerrarConexion();
            pc = crearConexion();
        }

        async function ofrecer({ reiniciar = false } = {}) {
            if (!ofrece || !pc || terminado || pc.signalingState === "closed") return;
            await pc.setLocalDescription(await pc.createOffer({ iceRestart: reiniciar }));
            // localDescription (y no la oferta a secas) trae los candidatos que ya se juntaron.
            enviar("offer", { offer: pc.localDescription });
        }

        async function responder(offer) {
            if (ofrece || !offer || !pc || terminado) return;
            await pc.setRemoteDescription(offer);
            await vaciarCandidatos();
            await pc.setLocalDescription(await pc.createAnswer());
            enviar("answer", { answer: pc.localDescription });
        }

        async function aplicarRespuesta(answer) {
            if (!ofrece || !answer || !pc || pc.signalingState !== "have-local-offer") return;
            await pc.setRemoteDescription(answer);
            await vaciarCandidatos();
        }

        async function agregarCandidato(candidate) {
            if (!candidate || !pc) return;
            if (!pc.remoteDescription) { candidatosPendientes.push(candidate); return; }
            try { await pc.addIceCandidate(candidate); } catch (e) { console.warn("Videollamada: candidato descartado —", e && e.name); }
        }

        async function vaciarCandidatos() {
            const lista = candidatosPendientes;
            candidatosPendientes = [];
            for (const c of lista) { try { await pc.addIceCandidate(c); } catch { /* uno malo no frena al resto */ } }
        }

        // ── Si la conexión se corta ──
        function problemaDeMedios(texto) {
            avisoMedios = true;
            alAviso(texto);
            // Si no vuelve en un rato razonable, se da por perdida: mejor decirlo que dejar "Reintentando…" para siempre.
            if (!timerPaciencia) timerPaciencia = setTimeout(() => {
                if (!mediaActiva()) fallar("Se perdió la conexión de video y no se pudo recuperar. Probá entrar de nuevo a la sala.", { cerrarLlamada: true });
            }, PACIENCIA_MEDIOS_MS);
        }

        function alCambioDeIce() {
            clearTimeout(timerGracia);
            const estado = pc.iceConnectionState;
            if (estado === "connected" || estado === "completed") {
                clearTimeout(timerPaciencia); timerPaciencia = null;
                reinicios = 0; otroSeFue = false;
                avisoMedios = false;
                alConectado();
                return;
            }
            if (terminado || otroSeFue) return;   // la otra persona se fue: no hay a quién reconectar
            if (estado === "disconnected") {
                problemaDeMedios("Se cortó la conexión de video. Reintentando…");
                timerGracia = setTimeout(() => { if (pc && pc.iceConnectionState === "disconnected") reiniciarIce(); }, GRACIA_DISCONNECTED_MS);
            } else if (estado === "failed") {
                problemaDeMedios("Se perdió la conexión de video. Reintentando…");
                reiniciarIce();
            }
        }

        // Reinicio de ICE: una oferta nueva con credenciales nuevas, sobre la misma conexión. La arma quien ofrece (el
        // paciente); el otro lado espera esa oferta. Con varios reinicios sin éxito, lo corta el tiempo de paciencia.
        function reiniciarIce() {
            if (terminado || otroSeFue || !pc || reinicios >= MAX_REINICIOS_ICE) return;
            reinicios++;
            if (ofrece) encolar(() => ofrecer({ reiniciar: true }));
        }

        // ── Señalización ──
        function fallar(texto, { cerrarLlamada = false } = {}) {
            if (terminado) return;
            terminado = true;
            clearTimeout(timerGracia); clearTimeout(timerPaciencia);
            if (socket) socket.disconnect();   // frena los reintentos automáticos de Socket.IO
            const llamadaActiva = mediaActiva() && !cerrarLlamada;
            alFallo(texto, { llamadaActiva });
            if (!llamadaActiva) cerrarConexion();
        }

        // El token venció (o la señalización no lo acepta): se pide un acceso nuevo UNA vez. Si con el nuevo vuelve a
        // fallar, o la API ya no deja entrar (la sesión terminó, por ejemplo), se dice por qué y se corta.
        async function renovarAcceso(motivo) {
            if (terminado || renovando) return;
            if (renovaciones >= 1) {
                return fallar(motivo === "no-autorizado"
                    ? "No tenés acceso a esta sala."
                    : "Tu acceso a la sala venció y no se pudo renovar. Volvé a entrar.");
            }
            renovando = true; renovaciones++;
            // Cuando el servidor corta por vencimiento cierra el transporte y Socket.IO reintenta solo, con el token
            // vencido. Se frena ese reintento mientras se pide el acceso nuevo, y después se conecta a mano.
            socket.disconnect();
            try {
                const nuevo = await pedirAcceso(sesion, { paraRol });
                if (terminado) return;
                if (nuevo.rol !== rol) return fallar("El acceso a la sala cambió. Volvé a entrar desde tu panel.");
                token = nuevo.token;
                iceServers = nuevo.iceServers;   // también las credenciales TURN, que son efímeras
                try { if (pc) pc.setConfiguration({ ...pc.getConfiguration(), iceServers }); } catch { /* se usan en la próxima conexión */ }
                socket.connect();
            } catch (err) {
                fallar(err.message);
            } finally {
                renovando = false;
            }
        }

        function conectarSenalizacion() {
            // auth como función: cada (re)conexión manda el token vigente, no el del primer intento.
            socket = io(SIGNAL, { auth: cb => { tokenDelIntento = token; cb({ token }); }, timeout: TIMEOUT_MS });

            socket.on("connect", () => {
                fallos = 0;
                if (avisado && !mediaActiva()) alRecuperar();
                avisado = false;
                // En cada (re)conexión el servidor perdió las salas del socket anterior: hay que volver a entrar. No se
                // manda sala ni rol: salen del token.
                socket.emit("join-room");
            });

            socket.on("connect_error", err => {
                console.warn("Señalización: no se pudo conectar —", err && err.message);
                const codigo = err && err.data && err.data.codigo;
                if (codigo === "token-vencido" || codigo === "no-autorizado") {
                    // Si ese intento había salido con un token anterior al vigente, el rechazo es viejo: ya se renovó.
                    if (tokenDelIntento !== token) return;
                    return renovarAcceso(codigo);
                }
                const f = describirFalloConexion(err);
                fallos++;
                if (f.definitivo) return fallar(f.texto);
                // Con la llamada andando se sigue reintentando por si hay que renegociar; sin ella, no.
                if (fallos >= MAX_FALLOS && !mediaActiva()) return fallar(`${f.texto} ${SUGERENCIA}`);
                avisar(`${f.texto} Reintentando…`);
            });

            // La señalización explica por qué saca a alguien de la sala y después corta.
            socket.on("rechazado", datos => {
                // El token llegó a su exp con la persona adentro. renovarAcceso se marca "renovando" en el acto, así el
                // "disconnect" que viene detrás (el servidor corta después de avisar) no se toma por un corte inesperado.
                if (datos && datos.codigo === "sesion-vencida") return renovarAcceso("sesion-vencida");
                // reemplazado: la sala quedó en manos de la otra pestaña; esta llamada no debe seguir en paralelo.
                fallar(mensajeRechazo(datos, rol), { cerrarLlamada: !!datos && datos.codigo === "reemplazado" });
            });

            socket.on("disconnect", motivo => {
                if (terminado || renovando || motivo === "io client disconnect") return;
                if (motivo === "io server disconnect") return fallar("El servicio de videollamada cerró la conexión.");
                avisar("Se perdió la conexión con el servicio de videollamada. Reintentando…");
            });

            socket.on("joined", () => {
                renovaciones = 0;
                otroSeFue = false;
                encolar(async () => { prepararConexion(); await ofrecer(); });
            });

            socket.on("user-joined", () => {
                otroSeFue = false;
                encolar(async () => { prepararConexion(); await ofrecer(); });
            });

            socket.on("user-left", datos => {
                otroSeFue = true;
                clearTimeout(timerGracia); clearTimeout(timerPaciencia); timerPaciencia = null;
                alOtroSeFue({ motivo: datos && datos.motivo });
            });

            // «Finalizar» del otro lado: la llamada terminó para los dos. Distinto de "user-left" (salir): ahí la sala sigue abierta y se puede volver a entrar.
            socket.on("call-end", datos => { if (!terminado) alLlamadaTerminada({ rol: datos && datos.rol }); });
            socket.on("offer", ({ offer } = {}) => encolar(() => responder(offer)));
            socket.on("answer", ({ answer } = {}) => encolar(() => aplicarRespuesta(answer)));
            socket.on("ice-candidate", ({ candidate } = {}) => encolar(() => agregarCandidato(candidate)));
        }

        pc = crearConexion();
        conectarSenalizacion();

        return {
            cerrar() {
                terminado = true;
                if (socket) socket.disconnect();
                cerrarConexion();
            },
            // «Finalizar»: avisa a la otra persona (por la señalización) y recién después se corta. Sin conexión a la señalización el aviso no sale y la otra persona ve que te fuiste.
            terminar() {
                enviar("call-end");
                // Un instante para que el aviso salga antes de cortar: el cierre inmediato puede llevárselo por delante.
                terminado = true;
                setTimeout(() => this.cerrar(), 250);
            }
        };
    }

    // ── La escena de la sala (la misma para el profesional y para el paciente) ──
    // El video del otro participante ocupa el recuadro principal; el propio es una miniatura flotante (picture-in-picture) en la esquina inferior derecha, espejada, 4:3, con la etiqueta «Vos».
    // Mientras el otro todavía no está, el recuadro NO queda negro: dice «Esperando…» sobre un fondo neutro y la cámara propia se ve grande. Sin cámara (permiso denegado, cámara apagada o
    // en uso por otro programa) la miniatura dice «Sin cámara» con un ícono, nunca una caja negra. Los estilos están en shared/sala.css.

    /** 'activa' si el stream tiene una pista de video viva y encendida; 'sin-camara' en cualquier otro caso (sin stream, sin pista, apagada o terminada). */
    function estadoDeCamara(stream) {
        const pistas = stream && stream.getVideoTracks ? stream.getVideoTracks() : [];
        return pistas.some(p => p.readyState !== "ended" && p.enabled !== false && p.muted !== true) ? "activa" : "sin-camara";
    }

    /** Las clases de estado del marco, en función de si el otro participante está y de si hay cámara propia. Pura: se prueba sin navegador. */
    function clasesDeEscena({ otroPresente, camara }) {
        return ["sala-marco", otroPresente ? "sala--conectada" : "sala--esperando", camara === "activa" ? "sala--con-camara" : "sala--sin-camara"];
    }

    /**
     * Arma la escena sobre un recuadro que ya tiene el video del otro (principal) y el propio (propio, muted).
     * opciones: { marco, principal, propio, textoEspera: "Esperando al paciente…" }. Devuelve { usarStream(stream), otroConectado(bool), estado() }.
     */
    function escena({ marco, principal, propio, textoEspera }) {
        const el = (etiqueta, clase, texto) => { const e = document.createElement(etiqueta); e.className = clase; if (texto) e.textContent = texto; return e; };
        principal.classList.add("sala-principal");
        propio.classList.add("sala-propio-video");

        const espera = el("div", "sala-espera");
        espera.setAttribute("role", "status");
        espera.append(el("div", "sala-espera-titulo", textoEspera || "Esperando…"), el("div", "sala-espera-nota", "Cuando llegue, la llamada se conecta sola."));

        const miniatura = el("div", "sala-propio");
        const sinCamara = el("div", "sala-sin-camara");
        const icono = el("span", "sala-sin-camara-icono", "\u{1F4F7}");
        icono.setAttribute("aria-hidden", "true");
        sinCamara.append(icono, el("span", "sala-sin-camara-texto", "Sin cámara"));
        miniatura.append(propio, sinCamara, el("span", "sala-propio-etiqueta", "Vos"));
        marco.append(espera, miniatura);

        let otroPresente = false, camara = "sin-camara", stream = null;
        const pintar = () => {
            for (const c of ["sala--conectada", "sala--esperando", "sala--con-camara", "sala--sin-camara"]) marco.classList.remove(c);
            clasesDeEscena({ otroPresente, camara }).forEach(c => marco.classList.add(c));
        };
        const revisarCamara = () => { camara = estadoDeCamara(stream); pintar(); };
        pintar();

        return {
            usarStream(nuevo) {
                stream = nuevo;
                propio.srcObject = nuevo;
                const pistas = nuevo && nuevo.getVideoTracks ? nuevo.getVideoTracks() : [];
                // Si la cámara se apaga o se desconecta a mitad de la llamada, la miniatura pasa a «Sin cámara» sola.
                pistas.forEach(p => { p.addEventListener("ended", revisarCamara); p.addEventListener("mute", revisarCamara); p.addEventListener("unmute", revisarCamara); });
                revisarCamara();
            },
            otroConectado(presente) { otroPresente = !!presente; pintar(); },
            estado() { return { otroPresente, camara }; }
        };
    }

    YS.video = {
        SIGNAL, FLAGS, mensajeMedios, conexionActiva,
        sesionDeCodigo, sesionDeLaUrl, mensajeAcceso, pedirAcceso, entrar,
        estadoDeCamara, clasesDeEscena, escena
    };
})();
