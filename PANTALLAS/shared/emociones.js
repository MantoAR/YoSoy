/* ═══════════════════════════════════════════════════════════════
   YoSoy · consentimiento y lectura de emociones de la sala de video (window.YS.emociones)

   Lo usan las dos sesion_video.html (paciente y profesional). Cuenta cómo se decide, cómo se obedece esa decisión y qué
   se guarda; las pantallas solo ponen la interfaz.

   Quién decide qué:
     · La API es la AUTORIDAD. El estado del consentimiento vive solo en el servidor (tabla ConsentimientoVideo): ni acá
       ni en el navegador se guarda nada de eso. La pantalla pregunta y obedece.
     · El paciente decide —acepta o rechaza— y puede revocar en cualquier momento. Rechazar no impide la llamada: la
       videollamada anda igual, sin análisis.
     · La pantalla del profesional (o del Configurador) pregunta GET /video/emociones/permiso al arrancar y cada pocos
       segundos. Si la API dice que no, o no contesta a tiempo, el análisis se FRENA (falla cerrada). Si el paciente
       revoca o rechaza, lo acumulado se DESCARTA y no se guarda nada de esa sesión.
     · Lo que se guarda es el promedio de la sesión y la ventana en que se tomó; la API lo vuelve a validar contra el
       consentimiento antes de guardarlo (GuardiaEmocionesService) y siempre lo guarda cifrado.
     · Límite honesto: ninguna pantalla ni servidor puede impedir que un navegador alterado analice el video que recibe.
       Lo que sí se controla es lo que se guarda, y queda auditado.

   Va después de shared/ui.js (define window.YS y YS.api) y, si se quiere el texto de los errores de la sala, de
   shared/video.js. Contrato completo: docs/VIDEOLLAMADAS.md.
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS = window.YS || {};

    // Las siete expresiones del modelo y cómo se llaman en pantalla (mismas que el panel del profesional de siempre).
    const ETIQUETAS = Object.freeze({
        neutral: "Neutral", happy: "Felicidad", sad: "Tristeza", angry: "Enojo",
        fearful: "Miedo", surprised: "Sorpresa", disgusted: "Disgusto"
    });

    // Si la API no dice otra cosa: preguntar cada 3 s y que una respuesta valga 10 s.
    const CADA_MS_POR_DEFECTO = 3000;
    const VIGENCIA_MS_POR_DEFECTO = 10000;

    const limitar = (n, min, max, porDefecto) => (Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : porDefecto);
    const consulta = sesion => `origen=${encodeURIComponent(sesion.origen)}&id=${encodeURIComponent(sesion.id)}`;

    // ── Mensajes ────────────────────────────────────────────────

    /**
     * Qué decirle a la persona cuando una llamada del consentimiento falla. err es el Error de YS.api (.status y .datos
     * con el cuerpo { mensaje, codigo }). Lo propio del consentimiento se dice acá; lo que es de la sala (la sesión no
     * existe, ya terminó, demasiados pedidos…) se dice igual que al pedir el acceso (YS.video.mensajeAcceso).
     */
    function mensajeError(err, paraRol) {
        const status = err && err.status;
        const datos = (err && err.datos) || {};
        if (status === 0 || (err && err.sinConexion))
            return "No se pudo conectar con el servidor. Revisá tu conexión y probá de nuevo.";
        if (status === 409) {
            switch (datos.codigo) {
                case "version-desactualizada":
                    return "El texto del consentimiento cambió mientras lo leías. Volvé a leerlo y decidí de nuevo.";
                case "aceptacion-video-pendiente":
                    return datos.mensaje || "Antes tenés que aceptar usar la videollamada.";
                case "usar-revocar":
                    return "Ya habías aceptado: para dejar de compartir tus expresiones usá el botón de revocar.";
                case "menor-de-edad":
                case "edad-desconocida":
                case "no-configurado":
                    return datos.mensaje || "La lectura de expresiones no está disponible.";
            }
        }
        if (YS.video && typeof YS.video.mensajeAcceso === "function") return YS.video.mensajeAcceso(err, paraRol);
        if (status === 401) return "Tu sesión de YoSoy venció. Ingresá de nuevo para continuar.";
        if (status === 404) return "No encontramos esa sesión.";
        if (status === 429) return "Hiciste demasiados intentos seguidos. Esperá un minuto y probá de nuevo.";
        return datos.mensaje || "No se pudo completar la operación. Probá de nuevo en un rato.";
    }

    /**
     * Qué le dice la pantalla del profesional sobre la lectura de emociones, según lo que contestó el permiso
     * ({ permitido, estado, motivo }) o el estado interno ("consultando", "sin-conexion"…).
     */
    function textoDeEstado(v) {
        if (!v) return "Consultando si el paciente aceptó la lectura de emociones…";
        if (v.permitido) return "Lectura de emociones activa: el paciente la aceptó.";
        switch (v.estado) {
            case "consultando":
                return "Consultando si el paciente aceptó la lectura de emociones…";
            case "sin-decision":
                return "Esperando que el paciente decida si acepta la lectura de emociones. Mientras tanto no se analiza nada.";
            case "rechaza":
                return "El paciente no aceptó la lectura de emociones. La videollamada sigue con normalidad, sin análisis.";
            case "revocado":
                return "El paciente dejó de compartir sus expresiones. Se detuvo el análisis y no se guarda nada de esta sesión.";
            case "sin-conexion":
                return "No se pudo confirmar el consentimiento del paciente (sin conexión con el servidor). El análisis está en pausa.";
            case "sin-sesion":
                return "No se pudo comprobar el consentimiento de esta sesión. No se analiza nada.";
            case "sesion-vencida":
                return "Tu sesión de YoSoy venció, así que no se puede confirmar el consentimiento. El análisis está en pausa: ingresá de nuevo en otra pestaña.";
            case "no-disponible":
                switch (v.motivo) {
                    case "menor-de-edad":
                        return "La lectura de emociones no está disponible para esta persona (menor de edad o con tutor). La videollamada sigue con normalidad.";
                    case "edad-desconocida":
                        return "La lectura de emociones no está disponible: el paciente no tiene fecha de nacimiento cargada y no se puede comprobar que sea mayor de edad.";
                    case "no-configurado":
                        return "La lectura de emociones no está disponible: el servidor no tiene configurado el cifrado de los datos.";
                    case "video-no-aceptado":
                        return "El paciente no aceptó (o revocó) la videollamada de esta sesión: no se analiza nada.";
                }
                return "La lectura de emociones no está disponible para esta sesión.";
        }
        return "La lectura de emociones no está disponible.";
    }

    // ── Paciente: leer el texto, decidir, revocar ───────────────

    async function llamar(ruta, opciones, paraRol) {
        try {
            // sinRedireccion: un 401 acá no tiene que sacar a la persona de la página (perdería lo que ya cargó).
            return await YS.api(ruta, Object.assign({ sinRedireccion: true }, opciones));
        } catch (err) {
            throw Object.assign(new Error(mensajeError(err, paraRol)),
                { status: err && err.status, codigo: err && err.datos && err.datos.codigo });
        }
    }

    /**
     * El estado de la decisión del paciente para esa sesión y, si la lectura está disponible, el texto que tiene que leer:
     * { disponible, estado, motivo, version, provisorio, titulo, texto[], decididoUtc, revocadoUtc }.
     * estado: "sin-decision" | "acepta" | "rechaza" | "revocado" | "no-disponible".
     */
    const obtener = sesion => llamar(`/video/consentimiento?${consulta(sesion)}`, {}, "paciente");

    /** Registra "acepta" o "rechaza" sobre ESA versión del texto (la API rechaza una versión que ya no es la vigente). */
    const decidir = (sesion, decision, version) => llamar("/video/consentimiento",
        { method: "PUT", json: { origen: sesion.origen, id: sesion.id, decision, version } }, "paciente");

    /** Deja de compartir: corta el análisis y la API anula las emociones ya guardadas de esa sesión. */
    const revocar = sesion => llamar("/video/consentimiento/revocar",
        { method: "POST", json: { origen: sesion.origen, id: sesion.id } }, "paciente");

    // ── Paciente: aceptar la videollamada de la sesión (obligatoria), revocarla ──

    /** { exigida, estado: "pendiente" | "acepta" | "revocada", version, titulo, texto[], marcaProvisoria, decididoUtc, revocadoUtc } */
    const obtenerVideo = sesion => llamar(`/video/aceptacion?${consulta(sesion)}`, {}, "paciente");

    /** Acepta usar la videollamada de ESA sesión sobre ESA versión del texto. */
    const aceptarVideo = (sesion, version) => llamar("/video/aceptacion",
        { method: "PUT", json: { origen: sesion.origen, id: sesion.id, version } }, "paciente");

    /** Deja de usar el video: la API corta la lectura de emociones, descarta lo acumulado y le avisa al profesional. */
    const revocarVideo = sesion => llamar("/video/aceptacion/revocar",
        { method: "POST", json: { origen: sesion.origen, id: sesion.id } }, "paciente");

    // ── Profesional: el permiso, vigilado ───────────────────────

    /**
     * Pregunta a la API, ya y cada pocos segundos, si se puede analizar las expresiones del paciente de esta sesión.
     *
     *   sesion      { origen, id }
     *   alCambiar   (vista) cada vez que cambia lo que la pantalla tiene que mostrar o hacer. vista:
     *               { permitido, estado, motivo, desdeUtc, revocadoUtc, version }
     *               estado: "consultando" (todavía sin respuesta) · "sin-decision" · "acepta" · "rechaza" · "revocado" ·
     *               "no-disponible" (con motivo) · "sin-conexion" (no contesta: se frena) · "sin-sesion" (404) ·
     *               "sesion-vencida" (401)
     *   api, ahora, programar, cancelar   solo para probar sin red ni relojes.
     *
     * FALLA CERRADA: permitido solo es true con una respuesta de "sí" que llegó hace menos de vigenciaSegundos (lo dice
     * la API; 10 s por defecto). Si la consulta falla o no vuelve, a los pocos segundos pasa a "sin-conexion" y se frena.
     */
    function vigilar(opts) {
        const { sesion, alCambiar = () => {} } = opts;
        const api = opts.api || (() => YS.api(`/video/emociones/permiso?${consulta(sesion)}`, { sinRedireccion: true }));
        const ahora = opts.ahora || (() => Date.now());
        const programar = opts.programar || ((f, ms) => setTimeout(f, ms));
        const cancelar = opts.cancelar || (t => clearTimeout(t));

        let ultimo = null;              // última respuesta buena de la API
        let recibidaEn = 0;             // reloj local en el que llegó
        let desfase = 0;                // reloj del servidor − reloj local, en ms
        let cadaMs = CADA_MS_POR_DEFECTO;
        let vigenciaMs = VIGENCIA_MS_POR_DEFECTO;
        let problema = null;            // 404 o 401: la API contestó que no
        let activo = false;
        let timerCiclo = null, timerVence = null;
        let notificado = null;

        // video: lo que el paciente aceptó de la videollamada de esta sesión ({ exigido, estado: "pendiente" | "acepta" | "revocada", revocadoUtc }), para el indicador del profesional.
        function vista() {
            if (problema) return { permitido: false, estado: problema, motivo: null, desdeUtc: null, revocadoUtc: null, version: null, video: null };
            if (!ultimo) return { permitido: false, estado: "consultando", motivo: null, desdeUtc: null, revocadoUtc: null, version: null, video: null };
            if (ahora() - recibidaEn > vigenciaMs)
                return { permitido: false, estado: "sin-conexion", motivo: null, desdeUtc: ultimo.desdeUtc || null, revocadoUtc: null, version: ultimo.version || null, video: null };
            return {
                permitido: ultimo.permitido === true,
                estado: String(ultimo.estado || ""),
                motivo: ultimo.motivo || null,
                desdeUtc: ultimo.desdeUtc || null,
                revocadoUtc: ultimo.revocadoUtc || null,
                version: ultimo.version || null,
                video: ultimo.video || null
            };
        }

        function notificar() {
            const v = vista();
            const clave = [v.permitido, v.estado, v.motivo, v.desdeUtc, v.revocadoUtc, v.video && v.video.estado].join("|");
            if (clave === notificado) return;
            notificado = clave;
            alCambiar(v);
        }

        // La respuesta deja de valer a los vigenciaMs: se revisa en ese momento para frenar aunque no llegue nada más.
        function vigilarVencimiento() {
            if (timerVence !== null) cancelar(timerVence);
            timerVence = programar(() => { timerVence = null; notificar(); }, vigenciaMs + 50);
        }

        // Devuelve true si ESTA consulta trajo una respuesta válida de la API.
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
                // 404 y 401 son respuestas de la API (la sesión no es de esta persona / la sesión de YoSoy venció):
                // no se espera a que venza nada. Cualquier otra falla (red, 429, 5xx) deja la última respuesta
                // hasta que venza sola.
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
                notificar();   // "consultando"
                ciclo();
            },
            detener() {
                activo = false;
                if (timerCiclo !== null) cancelar(timerCiclo);
                if (timerVence !== null) cancelar(timerVence);
                timerCiclo = timerVence = null;
            },
            /** Pregunta ahora, sin esperar al próximo ciclo. fresca: ESTA consulta trajo la respuesta (no es una vieja). */
            async consultarAhora() {
                const anduvo = await consultar();
                return Object.assign(vista(), { fresca: anduvo && problema === null });
            },
            vista,
            permitido: () => vista().permitido,
            /** La hora del servidor, calibrada con la última respuesta (el reloj del navegador puede estar corrido). */
            ahoraServidorMs: () => ahora() + desfase
        };
    }

    // ── Profesional: acumular lo que se mide ────────────────────

    /**
     * Suma de las muestras de UNA ventana de consentimiento: cuántas, el total de cada expresión y de cuándo a cuándo.
     * No guarda cada muestra (no hace falta: si el paciente revoca se descarta todo): solo sumas y contadores.
     */
    function crearAcumulador() {
        let n = 0, desdeMs = null, hastaMs = null;
        let sumas = {};

        return {
            /** Una muestra: { neutral: 0..1, happy: 0..1, … } tomada en tMs (hora del servidor). Devuelve false si no sirve. */
            agregar(expresiones, tMs) {
                if (!expresiones || typeof expresiones !== "object" || !Number.isFinite(tMs)) return false;
                let alguna = false;
                for (const k of Object.keys(ETIQUETAS)) {
                    const v = Number(expresiones[k]);
                    if (Number.isFinite(v) && v >= 0) { sumas[k] = (sumas[k] || 0) + Math.min(1, v); alguna = true; }
                }
                if (!alguna) return false;
                n++;
                if (desdeMs === null) desdeMs = tMs;
                hastaMs = tMs;
                return true;
            },
            limpiar() { n = 0; desdeMs = hastaMs = null; sumas = {}; },
            cantidad: () => n,
            /** Promedio por expresión, de 0 a 100 y entero, y la ventana. null si no hay muestras. */
            resumen() {
                if (n === 0) return null;
                const promedios = {};
                for (const k of Object.keys(ETIQUETAS)) promedios[k] = Math.round((sumas[k] || 0) / n * 100);
                return { muestras: n, promedios, desdeMs, hastaMs };
            },
            /** La expresión con más peso hasta ahora (para mostrar), o null. */
            dominante() {
                let mejor = null, valor = 0;
                for (const k of Object.keys(ETIQUETAS)) if ((sumas[k] || 0) > valor) { mejor = k; valor = sumas[k]; }
                return mejor;
            }
        };
    }

    /**
     * Todo junto, para la pantalla del profesional: vigila el permiso y acumula SOLO mientras lo hay.
     *   · Con permiso: muestra() suma. Sin permiso (o en pausa por falta de conexión): no suma.
     *   · Si el paciente rechaza o revoca, o la aceptación vigente es otra (revocó y volvió a aceptar), lo acumulado se
     *     descarta: no se guarda nada de una ventana que ya no vale. Una pausa por conexión (o por una sesión de YoSoy
     *     vencida) NO descarta nada.
     *   · paraGuardar() pregunta de nuevo a la API y arma lo que se manda para guardar; si no hay permiso FRESCO,
     *     devuelve emociones: null y el motivo, y la sesión se guarda sin emociones.
     * alCambiar(vista, { acumuladas }) se llama con cada cambio de estado.
     */
    function analizar(opts) {
        const { sesion, alCambiar = () => {} } = opts;
        const acumulador = crearAcumulador();
        let ventana = null;   // desdeUtc (ms) de la aceptación con la que se está acumulando

        const vigilante = vigilar(Object.assign({}, opts, {
            alCambiar: v => {
                if (v.permitido) {
                    const desde = Date.parse(v.desdeUtc);
                    if (ventana !== null && desde !== ventana) acumulador.limpiar();   // otra aceptación: otra ventana
                    ventana = desde;
                } else if (v.estado !== "sin-conexion" && v.estado !== "consultando" && v.estado !== "sesion-vencida") {
                    // Rechazó, revocó, no está disponible o la sesión no es de esta persona: se descarta lo acumulado.
                    // "sin-conexion" y "sesion-vencida" solo PAUSAN: no se sabe nada nuevo, y lo ya medido valía.
                    acumulador.limpiar();
                    ventana = null;
                }
                alCambiar(v, { acumuladas: acumulador.cantidad() });
            }
        }));

        return {
            iniciar: () => vigilante.iniciar(),
            detener: () => vigilante.detener(),
            vista: () => vigilante.vista(),
            permitido: () => vigilante.permitido(),
            ahoraServidorMs: () => vigilante.ahoraServidorMs(),
            /** Suma una muestra si hay permiso. Devuelve true si la sumó. */
            muestra(expresiones) {
                if (!vigilante.permitido()) return false;
                return acumulador.agregar(expresiones, vigilante.ahoraServidorMs());
            },
            resumen: () => acumulador.resumen(),
            cantidad: () => acumulador.cantidad(),
            dominante: () => acumulador.dominante(),
            limpiar() { acumulador.limpiar(); ventana = null; },
            /**
             * Lo que va en `emociones` del pedido de POST /sesiones-clinicas, o null si no corresponde mandar nada, con el
             * motivo. Siempre consulta de nuevo a la API antes: un permiso viejo no alcanza.
             */
            async paraGuardar() {
                const v = await vigilante.consultarAhora();
                if (!v.fresca) return { emociones: null, motivo: "sin-confirmacion", vista: v };
                if (!v.permitido) return { emociones: null, motivo: v.estado, vista: v };
                const r = acumulador.resumen();
                if (!r || r.muestras < 2) return { emociones: null, motivo: "sin-muestras", vista: v };

                const consentimientoMs = Date.parse(v.desdeUtc);
                const desde = Math.max(r.desdeMs, Number.isNaN(consentimientoMs) ? r.desdeMs : consentimientoMs);
                const hasta = r.hastaMs;
                if (hasta - desde < 1000) return { emociones: null, motivo: "sin-muestras", vista: v };
                return {
                    emociones: {
                        origen: sesion.origen, id: sesion.id,
                        desdeUtc: new Date(desde).toISOString(), hastaUtc: new Date(hasta).toISOString(),
                        muestras: r.muestras, promedios: r.promedios
                    },
                    motivo: null,
                    vista: v
                };
            }
        };
    }

    YS.emociones = {
        ETIQUETAS, CADA_MS_POR_DEFECTO, VIGENCIA_MS_POR_DEFECTO,
        mensajeError, textoDeEstado,
        obtener, decidir, revocar,
        obtenerVideo, aceptarVideo, revocarVideo,
        vigilar, crearAcumulador, analizar
    };
})();
