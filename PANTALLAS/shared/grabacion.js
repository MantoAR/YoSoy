/* ═══════════════════════════════════════════════════════════════
   YoSoy · consentimiento y grabación de audio de la sala de video, para la transcripción local (window.YS.grabacion)

   Lo usan las dos sesion_video.html (paciente y profesional). Misma idea que shared/emociones.js, pero para
   "finalidad=grabacion": acá además de vigilar el permiso se graba y se sube el audio.

   Quién decide qué:
     · La API es la AUTORIDAD. El estado del consentimiento vive solo en el servidor (tabla ConsentimientoVideo,
       finalidad "grabacion"): ni acá ni en el navegador se guarda nada de eso. La pantalla pregunta y obedece.
     · El paciente decide —acepta o rechaza— y puede revocar en cualquier momento. Rechazar no impide la llamada.
     · La pantalla del profesional pregunta GET /video/grabacion/permiso al arrancar y cada pocos segundos. Si la
       API dice que no, o no contesta a tiempo, la grabación se FRENA (falla cerrada): la pantalla tiene que llamar
       a descartar() del grabador.
     · El audio se sube en pedazos a POST /video/grabacion/fragmento, uno por pista ("profesional" | "paciente"),
       en orden. Reintentar la misma secuencia no duplica nada; si el servidor contesta que falta una antes, o
       cualquier otro error que no sea de red, esa pista se da por perdida (no se puede recuperar un fragmento que
       el MediaRecorder ya descartó) y queda "incompleta": la transcripción sigue con lo que se pudo subir.

   Va después de shared/ui.js (define window.YS y YS.api) y, si se quiere el texto de los errores de la sala, de
   shared/video.js. Contrato completo: docs/VIDEOLLAMADAS.md.
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS = window.YS || {};

    const PISTAS = Object.freeze({ Profesional: "profesional", Paciente: "paciente" });

    // Si la API no dice otra cosa: preguntar cada 3 s y que una respuesta valga 10 s (igual que emociones).
    const CADA_MS_POR_DEFECTO = 3000;
    const VIGENCIA_MS_POR_DEFECTO = 10000;

    // Un fragmento de audio cada 10 s; hasta 6 reintentos por fragmento (≈ 1 minuto de backoff) antes de dar la
    // pista por perdida; hasta 15 s de espera al finalizar para que termine de subir lo que falta.
    const MIME_CANDIDATOS = ["audio/webm;codecs=opus", "audio/webm"];
    const TAMANO_FRAGMENTO_MS = 10000;
    const MAX_REINTENTOS_FRAGMENTO = 6;
    const ESPERA_MINIMA_MS = 1000;
    const ESPERA_MAXIMA_MS = 30000;
    const TOPE_FINALIZAR_MS = 15000;
    const REVISAR_FINALIZAR_MS = 300;

    const limitar = (n, min, max, porDefecto) => (Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : porDefecto);
    const consulta = sesion => `origen=${encodeURIComponent(sesion.origen)}&id=${encodeURIComponent(sesion.id)}`;

    // ── Mensajes ────────────────────────────────────────────────

    /** Igual que YS.emociones.mensajeError, pero para la grabación. err es el Error de YS.api. */
    function mensajeError(err, paraRol) {
        const status = err && err.status;
        const datos = (err && err.datos) || {};
        if (status === 0 || (err && err.sinConexion))
            return "No se pudo conectar con el servidor. Revisá tu conexión y probá de nuevo.";
        if (status === 409) {
            switch (datos.codigo) {
                case "version-desactualizada":
                    return "El texto del consentimiento cambió mientras lo leías. Volvé a leerlo y decidí de nuevo.";
                case "usar-revocar":
                    return "Ya habías aceptado: para dejar de grabar la sesión usá el botón de revocar.";
                case "menor-de-edad":
                case "edad-desconocida":
                case "no-configurado":
                    return datos.mensaje || "La grabación de la sesión no está disponible.";
            }
        }
        if (YS.video && typeof YS.video.mensajeAcceso === "function") return YS.video.mensajeAcceso(err, paraRol);
        if (status === 401) return "Tu sesión de YoSoy venció. Ingresá de nuevo para continuar.";
        if (status === 404) return "No encontramos esa sesión.";
        if (status === 429) return "Hiciste demasiados intentos seguidos. Esperá un minuto y probá de nuevo.";
        return datos.mensaje || "No se pudo completar la operación. Probá de nuevo en un rato.";
    }

    /** Qué le dice la pantalla del profesional sobre la grabación, según el permiso o el estado interno. */
    function textoDeEstado(v) {
        if (!v) return "Consultando si el paciente aceptó grabar la sesión…";
        if (v.permitido) return "Grabación activa: el paciente la aceptó.";
        switch (v.estado) {
            case "consultando":
                return "Consultando si el paciente aceptó grabar la sesión…";
            case "sin-decision":
                return "Esperando que el paciente decida si acepta grabar la sesión. Mientras tanto no se graba nada.";
            case "rechaza":
                return "El paciente no aceptó grabar la sesión. La videollamada sigue con normalidad, sin grabación.";
            case "revocado":
                return "El paciente dejó de permitir la grabación. Se detuvo y no se guarda nada de esta sesión.";
            case "sin-conexion":
                return "No se pudo confirmar el consentimiento del paciente (sin conexión con el servidor). La grabación está en pausa.";
            case "sin-sesion":
                return "No se pudo comprobar el consentimiento de esta sesión. No se graba nada.";
            case "sesion-vencida":
                return "Tu sesión de YoSoy venció, así que no se puede confirmar el consentimiento. La grabación está en pausa: ingresá de nuevo en otra pestaña.";
            case "no-disponible":
                switch (v.motivo) {
                    case "menor-de-edad":
                        return "La grabación de la sesión no está disponible para esta persona (menor de edad o con tutor). La videollamada sigue con normalidad.";
                    case "edad-desconocida":
                        return "La grabación de la sesión no está disponible: el paciente no tiene fecha de nacimiento cargada y no se puede comprobar que sea mayor de edad.";
                    case "no-configurado":
                        return "La grabación de la sesión no está disponible: el servidor no tiene configurada la transcripción o el cifrado de los datos.";
                }
                return "La grabación de la sesión no está disponible para esta sesión.";
        }
        return "La grabación de la sesión no está disponible.";
    }

    // ── Paciente: leer el texto, decidir, revocar ───────────────

    async function llamar(ruta, opciones, paraRol) {
        try {
            return await YS.api(ruta, Object.assign({ sinRedireccion: true }, opciones));
        } catch (err) {
            throw Object.assign(new Error(mensajeError(err, paraRol)),
                { status: err && err.status, codigo: err && err.datos && err.datos.codigo });
        }
    }

    /** Igual forma que YS.emociones.obtener, pidiendo finalidad=grabacion. */
    const obtener = sesion => llamar(`/video/consentimiento?${consulta(sesion)}&finalidad=grabacion`, {}, "paciente");

    /** Registra "acepta" o "rechaza" sobre ESA versión del texto de la grabación. */
    const decidir = (sesion, decision, version) => llamar("/video/consentimiento",
        { method: "PUT", json: { origen: sesion.origen, id: sesion.id, decision, version, finalidad: "grabacion" } }, "paciente");

    /** Deja de grabar: corta la subida y la API anula la transcripción (y borra el audio) de esta sesión. */
    const revocar = sesion => llamar("/video/consentimiento/revocar",
        { method: "POST", json: { origen: sesion.origen, id: sesion.id, finalidad: "grabacion" } }, "paciente");

    // ── Profesional: el permiso, vigilado (idéntico a YS.emociones.vigilar, apuntando a /video/grabacion/permiso) ──

    /**
     * Pregunta a la API, ya y cada pocos segundos, si se puede grabar esta sesión. Misma forma que
     * YS.emociones.vigilar: ver ahí la documentación completa de alCambiar y de los estados.
     */
    function vigilar(opts) {
        const { sesion, alCambiar = () => {} } = opts;
        const api = opts.api || (() => YS.api(`/video/grabacion/permiso?${consulta(sesion)}`, { sinRedireccion: true }));
        const ahora = opts.ahora || (() => Date.now());
        const programar = opts.programar || ((f, ms) => setTimeout(f, ms));
        const cancelar = opts.cancelar || (t => clearTimeout(t));

        let ultimo = null;
        let recibidaEn = 0;
        let desfase = 0;
        let cadaMs = CADA_MS_POR_DEFECTO;
        let vigenciaMs = VIGENCIA_MS_POR_DEFECTO;
        let problema = null;
        let activo = false;
        let timerCiclo = null, timerVence = null;
        let notificado = null;

        function vista() {
            if (problema) return { permitido: false, estado: problema, motivo: null, desdeUtc: null, revocadoUtc: null, version: null };
            if (!ultimo) return { permitido: false, estado: "consultando", motivo: null, desdeUtc: null, revocadoUtc: null, version: null };
            if (ahora() - recibidaEn > vigenciaMs)
                return { permitido: false, estado: "sin-conexion", motivo: null, desdeUtc: ultimo.desdeUtc || null, revocadoUtc: null, version: ultimo.version || null };
            return {
                permitido: ultimo.permitido === true,
                estado: String(ultimo.estado || ""),
                motivo: ultimo.motivo || null,
                desdeUtc: ultimo.desdeUtc || null,
                revocadoUtc: ultimo.revocadoUtc || null,
                version: ultimo.version || null
            };
        }

        function notificar() {
            const v = vista();
            const clave = [v.permitido, v.estado, v.motivo, v.desdeUtc, v.revocadoUtc].join("|");
            if (clave === notificado) return;
            notificado = clave;
            alCambiar(v);
        }

        function vigilarVencimiento() {
            if (timerVence !== null) cancelar(timerVence);
            timerVence = programar(() => { timerVence = null; notificar(); }, vigenciaMs + 50);
        }

        async function consultar() {
            const t0 = ahora();
            let anduvo = false;
            try {
                const r = await api();
                const t1 = ahora();
                if (!r || typeof r.permitido !== "boolean" || typeof r.estado !== "string") throw new Error("respuesta inválida");
                ultimo = r;
                recibidaEn = t1;
                problema = null;
                const servidor = Date.parse(r.ahoraUtc);
                if (!Number.isNaN(servidor)) desfase = servidor - Math.round((t0 + t1) / 2);
                cadaMs = limitar(r.consultarCadaSegundos, 1, 60, CADA_MS_POR_DEFECTO / 1000) * 1000;
                vigenciaMs = limitar(r.vigenciaSegundos, 2, 300, VIGENCIA_MS_POR_DEFECTO / 1000) * 1000;
                anduvo = true;
                if (activo) vigilarVencimiento();
            } catch (err) {
                const status = err && err.status;
                if (status === 404) problema = "sin-sesion";
                else if (status === 401) problema = "sesion-vencida";
            }
            notificar();
            return anduvo;
        }

        function ciclo() {
            if (!activo) return;
            consultar().then(() => { if (activo) timerCiclo = programar(ciclo, cadaMs); });
        }

        return {
            iniciar() {
                if (activo) return;
                activo = true;
                notificar();
                ciclo();
            },
            detener() {
                activo = false;
                if (timerCiclo !== null) cancelar(timerCiclo);
                if (timerVence !== null) cancelar(timerVence);
                timerCiclo = timerVence = null;
            },
            async consultarAhora() {
                const anduvo = await consultar();
                return Object.assign(vista(), { fresca: anduvo && problema === null });
            },
            vista,
            permitido: () => vista().permitido,
            ahoraServidorMs: () => ahora() + desfase
        };
    }

    // ── Profesional: grabar y subir ──────────────────────────────

    /**
     * Grava el audio de las dos pistas de la sesión (micrófono del profesional y audio remoto del paciente) y lo va
     * subiendo a POST /video/grabacion/fragmento, cada pista con su propio MediaRecorder y su propia cola de subida
     * en orden. El audio remoto se re-enruta con la Web Audio API (createMediaStreamSource → el mismo
     * MediaStreamAudioDestinationNode de siempre) para que una reconexión del paciente (stream nuevo) no corte ni
     * reinicie el MediaRecorder de esa pista: solo se cambia de dónde sale el sonido.
     *
     *   sesion            { origen, id }
     *   inicioSesionMs    reloj (de ahora()) del momento en que arrancó la sesión, para calcular el inicioMs que
     *                      exige la secuencia 0 de cada pista. Si no se da, se toma el momento de crear el grabador.
     *   onCambio(vista)   cada vez que cambia algo mostrable: vista()[pista] = { grabando, pendientes, resultado }.
     *   api, ahora, programar, cancelar, crearContextoAudio, crearMediaRecorder, tipoSoportado, crearFormData
     *                      inyectables, solo para probar sin navegador real.
     *
     * Por pista, resultado es null mientras se grava y, al finalizar, "completa" (se subió todo), "incompleta"
     * (algo quedó sin subir: la transcripción sigue con lo que se pudo) o "sin-audio" (no había pista de audio, o
     * el navegador no soporta grabar audio/webm).
     */
    function crearGrabador(opts) {
        const { sesion, onCambio = () => {} } = opts;
        const api = opts.api || ((ruta, o) => YS.api(ruta, o));
        const ahora = opts.ahora || (() => Date.now());
        const programar = opts.programar || ((f, ms) => setTimeout(f, ms));
        const cancelar = opts.cancelar || (t => clearTimeout(t));
        const crearContextoAudio = opts.crearContextoAudio || (() => new AudioContext());
        const crearMediaRecorder = opts.crearMediaRecorder || ((stream, mime) => new MediaRecorder(stream, mime ? { mimeType: mime } : undefined));
        const tipoSoportado = opts.tipoSoportado ||
            (mime => (typeof MediaRecorder !== "undefined" && typeof MediaRecorder.isTypeSupported === "function" && MediaRecorder.isTypeSupported(mime)));
        const crearFormData = opts.crearFormData || (() => new FormData());

        const mime = MIME_CANDIDATOS.find(tipoSoportado) || null;
        const inicioSesionMs = Number.isFinite(opts.inicioSesionMs) ? opts.inicioSesionMs : ahora();

        let contexto = null;
        const pistas = {};

        function estadoDePista(pista) {
            return pistas[pista] || (pistas[pista] = {
                destino: null, fuente: null, recorder: null, detenido: false,
                siguienteSecuencia: 0, inicioMs: null,
                cola: [], subiendo: false, intentos: 0, timerReintento: null,
                terminada: false, resultado: null
            });
        }

        function vista() {
            const v = {};
            for (const pista of Object.keys(pistas)) {
                const e = pistas[pista];
                v[pista] = { grabando: !!e.recorder && !e.terminada, pendientes: e.cola.length, resultado: e.resultado };
            }
            return v;
        }

        function avisar() { onCambio(vista()); }

        function asegurarContexto() {
            if (!contexto) contexto = crearContextoAudio();
            return contexto;
        }

        // Conecta (o reconecta) el audio de streamFuente al MediaStreamAudioDestinationNode de esta pista. Si la
        // pista ya tenía un destino, lo reusa: por eso una reconexión no interrumpe el MediaRecorder que ya está
        // grabando ese destino.stream.
        function reenrutar(pista, streamFuente) {
            const pistasAudio = typeof streamFuente.getAudioTracks === "function" ? streamFuente.getAudioTracks() : [];
            if (!pistasAudio.length) return null;
            const ctx = asegurarContexto();
            const e = estadoDePista(pista);
            if (e.fuente && typeof e.fuente.disconnect === "function") { try { e.fuente.disconnect(); } catch { /* ya desconectada */ } }
            const soloAudio = new MediaStream([pistasAudio[0]]);
            e.fuente = ctx.createMediaStreamSource(soloAudio);
            if (!e.destino) e.destino = ctx.createMediaStreamDestination();
            e.fuente.connect(e.destino);
            return e.destino.stream;
        }

        function iniciarRecorder(pista, streamParaGrabar) {
            const e = estadoDePista(pista);
            if (e.recorder) return;
            if (!mime) { e.resultado = "sin-audio"; avisar(); return; }
            const recorder = crearMediaRecorder(streamParaGrabar, mime);
            e.recorder = recorder;
            e.inicioMs = Math.max(0, Math.round(ahora() - inicioSesionMs));
            recorder.ondataavailable = ev => { if (ev.data && ev.data.size > 0) encolar(pista, ev.data); };
            recorder.onstop = () => { e.detenido = true; avisar(); };
            recorder.start(TAMANO_FRAGMENTO_MS);
            avisar();
        }

        /** El micrófono del profesional: una sola vez, al arrancar la sesión. */
        function adjuntarLocal(streamLocal) {
            const streamGrabable = reenrutar(PISTAS.Profesional, streamLocal);
            if (!streamGrabable) { estadoDePista(PISTAS.Profesional).resultado = "sin-audio"; avisar(); return; }
            iniciarRecorder(PISTAS.Profesional, streamGrabable);
        }

        /** El audio remoto del paciente: la primera vez arranca la grabación; si vuelve a llamarse (reconexión), solo cambia la fuente. */
        function adjuntarRemoto(streamRemoto) {
            const yaGrababa = !!estadoDePista(PISTAS.Paciente).recorder;
            const streamGrabable = reenrutar(PISTAS.Paciente, streamRemoto);
            if (!streamGrabable) {
                if (!yaGrababa) { estadoDePista(PISTAS.Paciente).resultado = "sin-audio"; avisar(); }
                return;
            }
            if (!yaGrababa) iniciarRecorder(PISTAS.Paciente, streamGrabable);
        }

        function encolar(pista, blob) {
            const e = estadoDePista(pista);
            if (e.terminada) return;
            const secuencia = e.siguienteSecuencia++;
            e.cola.push({ secuencia, blob, inicioMs: secuencia === 0 ? e.inicioMs : null });
            avisar();
            procesarCola(pista);
        }

        async function subirFragmento(fragmento) {
            const form = crearFormData();
            form.append("origen", sesion.origen);
            form.append("id", String(sesion.id));
            form.append("pista", fragmento.pista);
            form.append("secuencia", String(fragmento.secuencia));
            if (fragmento.inicioMs !== null) form.append("inicioMs", String(fragmento.inicioMs));
            form.append("archivo", fragmento.blob, "fragmento.webm");
            try {
                await api("/video/grabacion/fragmento", { method: "POST", body: form, sinRedireccion: true });
                return "ok";
            } catch (err) {
                const status = err && err.status;
                if (status === 0 || status === 429 || (status >= 500 && status < 600)) return "reintentar";
                return "fallida";
            }
        }

        function marcarTerminada(pista, resultado) {
            const e = estadoDePista(pista);
            if (e.terminada) return;
            e.terminada = true;
            e.resultado = resultado;
            if (e.timerReintento !== null) { cancelar(e.timerReintento); e.timerReintento = null; }
            if (e.recorder && e.recorder.state !== "inactive") { try { e.recorder.stop(); } catch { /* ya detenido */ } }
            avisar();
        }

        function procesarCola(pista) {
            const e = estadoDePista(pista);
            if (e.subiendo || e.terminada || !e.cola.length) return;
            e.subiendo = true;
            subirFragmento(Object.assign({ pista }, e.cola[0])).then(resultado => {
                e.subiendo = false;
                if (e.terminada) return;
                if (resultado === "ok") {
                    e.cola.shift();
                    e.intentos = 0;
                    avisar();
                    procesarCola(pista);
                } else if (resultado === "reintentar" && e.intentos < MAX_REINTENTOS_FRAGMENTO) {
                    e.intentos++;
                    const espera = Math.min(ESPERA_MAXIMA_MS, ESPERA_MINIMA_MS * Math.pow(2, e.intentos - 1));
                    e.timerReintento = programar(() => { e.timerReintento = null; procesarCola(pista); }, espera);
                } else {
                    marcarTerminada(pista, "incompleta");
                }
            });
        }

        /** true si queda algo grabado sin terminar de subir (para avisar antes de cerrar la pestaña). */
        function haySinSubir() {
            return Object.values(pistas).some(e => !e.terminada && e.cola.length > 0);
        }

        /**
         * Para cuando la llamada se cae antes de guardar nada en la HC: corta todo sin esperar a que termine de
         * subir lo que quedó pendiente.
         */
        function descartar() {
            for (const pista of Object.keys(pistas)) marcarTerminada(pista, pistas[pista].resultado || "incompleta");
            if (contexto && typeof contexto.close === "function") { try { contexto.close(); } catch { /* ya cerrado */ } }
        }

        /**
         * Corta la grabación de las dos pistas y espera (con un tope de tiempo) a que termine de subirse lo que
         * ya se grabó. Devuelve { profesional, paciente } con el resultado de cada una.
         */
        function finalizar() {
            for (const pista of Object.values(PISTAS)) {
                const e = estadoDePista(pista);
                if (e.recorder && e.recorder.state !== "inactive") { try { e.recorder.stop(); } catch { /* ya detenido */ } }
            }
            return new Promise(resolve => {
                let resuelto = false;
                const limite = programar(() => terminar(), TOPE_FINALIZAR_MS);

                function listaPista(e) {
                    if (!e || e.terminada || !e.recorder) return true;
                    return e.detenido && !e.subiendo && e.cola.length === 0;
                }

                function terminar() {
                    if (resuelto) return;
                    resuelto = true;
                    cancelar(limite);
                    const r = {};
                    for (const pista of Object.values(PISTAS)) {
                        const e = pistas[pista];
                        if (!e || !e.recorder) { r[pista] = (e && e.resultado) || "sin-audio"; continue; }
                        if (!e.resultado) e.resultado = e.cola.length ? "incompleta" : "completa";
                        e.terminada = true;
                        r[pista] = e.resultado;
                    }
                    if (contexto && typeof contexto.close === "function") { try { contexto.close(); } catch { /* ya cerrado */ } }
                    avisar();
                    resolve(r);
                }

                function revisar() {
                    if (Object.values(PISTAS).map(p => pistas[p]).every(listaPista)) terminar();
                    else programar(revisar, REVISAR_FINALIZAR_MS);
                }
                programar(revisar, REVISAR_FINALIZAR_MS);
            });
        }

        return { adjuntarLocal, adjuntarRemoto, haySinSubir, descartar, finalizar, vista };
    }

    YS.grabacion = {
        PISTAS, CADA_MS_POR_DEFECTO, VIGENCIA_MS_POR_DEFECTO,
        mensajeError, textoDeEstado,
        obtener, decidir, revocar,
        vigilar, crearGrabador
    };
})();
