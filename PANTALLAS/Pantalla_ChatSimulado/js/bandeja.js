/* ═══════════════════════════════════════════════════════════════
   YoSoy · Bandeja de chats

   Se abre desde el botón Chats de la barra superior como un cajón que se
   superpone a la pantalla actual (sin recargar ni salir), se puede expandir
   a pantalla completa y se cierra con Esc. En Pantalla_ChatSimulado/index.html
   se usa además en modo página, junto al simulador del paciente.

   - Lo carga shared/shell.js solo si /capacidades dice que la mensajería está
     disponible (mismo control que el 404 de /mensajeria). Vive en esta carpeta,
     que con el módulo apagado ni se sirve.
   - Tiempo real por polling eficiente: pide solo las conversaciones con mensajes
     nuevos (?desdeMensaje), se frena con la pestaña oculta y espacia los pedidos
     cuando no hay novedades. Sin WebSocket.
   - Todo el texto que viene del servidor se inserta con textContent o YS.esc.
   ═══════════════════════════════════════════════════════════════ */
(function () {
    "use strict";
    const YS = window.YS;
    const BASE = "/mensajeria";

    const ESTADO = {
        Activa:         { texto: "Bot",      chip: "marca",   desc: "La atiende el asistente automático" },
        DerivadaHumano: { texto: "Escalada a persona", chip: "warn",    desc: "Pidió hablar con una persona del equipo" },
        Urgente:        { texto: "Urgente",  chip: "danger",  desc: "Posible situación de riesgo: el bot quedó en pausa" },
        Cerrada:        { texto: "Cerrada",  chip: "neutral", desc: "Cerrada por el equipo; el bot vuelve si el paciente escribe" }
    };
    const AUTOR = { Bot: "Asistente", Humano: "Equipo YoSoy", Sistema: "Aviso automático" };
    const PREFIJO = { Bot: "Asistente: ", Humano: "Vos: ", Sistema: "Aviso: " };
    const FILTROS = [
        { id: "todas",     texto: "Todas",     pasa: () => true },
        { id: "noLeidas",  texto: "No leídas", pasa: c => c.noLeidos > 0, contar: true },
        { id: "derivadas", texto: "Escaladas a persona", pasa: c => c.estado === "DerivadaHumano" || c.estado === "Urgente", contar: true,
          titulo: "Escaladas a una persona del equipo, incluidas las urgentes" },
        { id: "bot",       texto: "Del bot",   pasa: c => c.estado === "Activa" },
        { id: "cerradas",  texto: "Cerradas",  pasa: c => c.estado === "Cerrada" }
    ];
    const PAGINA = 50;              // mensajes por página de historial
    const AGRUPAR_MS = 5 * 60000;   // mensajes seguidos del mismo autor se agrupan
    const CLAVE_SONIDO = "ys-chats-sonido";

    const est = {
        modo: "cajon", abierto: false, expandido: false, contexto: false,
        convs: new Map(), maxId: 0, cargada: false, ultimaCompleta: 0,
        filtro: "todas", soloMias: false, busqueda: "", filtroEtiqueta: null, firmaLista: "",
        actual: null, generacion: 0, mensajes: [], primerId: null, ultimoId: 0, hayMas: false,
        cargandoArriba: false, nuevosAbajo: 0, pendientes: [], idTmp: 0, leidaHasta: 0,
        timer: null, pulsando: false, vueltasSinCambios: 0, sinConexion: false,
        plantillas: [], sonido: false, audio: null, boton: null
    };
    let raiz = null;
    const $ = sel => raiz.querySelector(sel);

    // ── Armado ──────────────────────────────────────────────────
    function crear() {
        raiz = YS.el("section", { class: "chats", "aria-label": "Chats", "aria-labelledby": "chatsTitulo" });
        raiz.innerHTML = `
            <header class="chats-cabecera">
                <h2 class="chats-titulo" id="chatsTitulo">Chats <span class="ui-contador oculto" id="chatsTotal"></span></h2>
                <div class="chats-cabecera-acciones">
                    <span class="chats-conexion oculto" id="chatsConexion" role="status">${YS.icono("sin-conexion")} Sin conexión, reintentando…</span>
                    <button class="ui-btn-icono" type="button" id="chatsSonido"></button>
                    <button class="ui-btn-icono" type="button" id="chatsAyuda" aria-label="Atajos de teclado" title="Atajos de teclado (?)">${YS.icono("teclado")}</button>
                    <button class="ui-btn-icono solo-cajon" type="button" id="chatsExpandir"></button>
                    <button class="ui-btn-icono solo-cajon" type="button" id="chatsCerrar" aria-label="Cerrar chats (Esc)" title="Cerrar (Esc)">${YS.icono("cerrar")}</button>
                </div>
            </header>
            <div class="chats-cuerpo">
                <section class="chats-lista" aria-label="Conversaciones">
                    <div class="chats-lista-herramientas">
                        <label class="ui-buscador chats-buscador">
                            <span class="sr-only">Buscar conversación</span>${YS.icono("buscar")}
                            <input class="ui-input" id="chatsBuscar" type="search" placeholder="Buscar por nombre o teléfono" autocomplete="off">
                        </label>
                        <div class="ui-filtros chats-filtros" id="chatsFiltros" role="group" aria-label="Filtrar conversaciones"></div>
                        <select class="ui-select chats-filtro-etiqueta" id="chatsFiltroEtiqueta" aria-label="Filtrar por etiqueta">
                            <option value="">Todas las etiquetas</option>
                        </select>
                    </div>
                    <ul class="chats-conversaciones" id="chatsConversaciones" aria-label="Conversaciones"></ul>
                </section>
                <section class="chats-hilo" id="chatsHilo" aria-label="Conversación"></section>
                <aside class="chats-contexto" id="chatsContexto" aria-label="Información del lead"></aside>
            </div>`;
        // "Mías" es aparte de FILTROS: se puede combinar con cualquier estado (no es
        // mutuamente excluyente), y solo tiene sentido para Profesional - CRM no tiene
        // cartera propia de pacientes. Por default arranca prendido para Profesional
        // (ve su cartera al abrir, no la bandeja entera) y no existe para CRM.
        est.soloMias = YS.shell?.rol === "profesional";
        $("#chatsFiltros").innerHTML = FILTROS.map(f =>
            `<button class="ui-filtro${f.id === est.filtro ? " activo" : ""}" type="button" data-filtro="${f.id}" aria-pressed="${f.id === est.filtro}"${f.titulo ? ` title="${YS.esc(f.titulo)}"` : ""}>${YS.esc(f.texto)}${f.contar ? '<span class="ui-contador ui-contador--suave oculto"></span>' : ""}</button>`).join("") +
            (YS.shell?.rol === "profesional"
                ? `<button class="ui-filtro ui-filtro--separado${est.soloMias ? " activo" : ""}" type="button" data-mias aria-pressed="${est.soloMias}" title="Solo conversaciones de tus pacientes">Mías</button>`
                : "");
        $("#chatsFiltros").addEventListener("click", e => {
            const b = e.target.closest("[data-filtro]");
            if (b) elegirFiltro(b.dataset.filtro);
            if (e.target.closest("[data-mias]")) alternarMias();
        });
        $("#chatsBuscar").addEventListener("input", YS.debounce(() => { est.busqueda = $("#chatsBuscar").value.trim(); dibujarLista(); }, 120));
        $("#chatsFiltroEtiqueta").addEventListener("change", e => {
            est.filtroEtiqueta = e.target.value ? parseInt(e.target.value) : null;
            dibujarLista();
        });
        $("#chatsBuscar").addEventListener("keydown", e => {
            if (e.key === "ArrowDown") { e.preventDefault(); raiz.querySelector(".chats-conv")?.focus(); }
        });
        $("#chatsConversaciones").addEventListener("click", e => {
            const b = e.target.closest(".chats-conv");
            if (b) seleccionar(parseInt(b.dataset.id));
        });
        $("#chatsConversaciones").addEventListener("keydown", e => {
            if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
            const items = [...raiz.querySelectorAll(".chats-conv")];
            const i = items.indexOf(document.activeElement);
            if (i < 0) return;
            e.preventDefault();
            items[Math.max(0, Math.min(items.length - 1, i + (e.key === "ArrowDown" ? 1 : -1)))].focus();
        });
        $("#chatsSonido").addEventListener("click", alternarSonido);
        $("#chatsAyuda").addEventListener("click", mostrarAtajos);
        $("#chatsExpandir").addEventListener("click", () => expandir(!est.expandido));
        $("#chatsCerrar").addEventListener("click", cerrar);
        dibujarSonido();
        dibujarExpandir();
        dibujarHiloVacio();
        dibujarContexto();
    }

    // ── Abrir, cerrar y expandir el cajón ───────────────────────
    function abrir(idConversacion = null, filtro = null) {
        if (est.modo === "pagina") {
            // bandeja.js no sabe nada de pestañas: si quien la contiene (dashboard.js) la
            // tiene tapada con su propia navegación por hash, un evento es la forma de
            // avisarle "mostrame" sin acoplarse a cómo arma sus tabs. Antes, pedir abrir una
            // conversación desde el toast de un aviso no hacía nada visible si la pantalla
            // estaba en otra sección: la seleccionaba igual, pero detrás de lo que ya se veía.
            document.dispatchEvent(new CustomEvent("ys:chats-ir-a-pagina"));
            if (idConversacion) seleccionar(idConversacion); if (filtro) elegirFiltro(filtro); return;
        }
        if (!est.abierto) {
            est.abierto = true;
            raiz.classList.add("abierto");
            document.body.classList.add("chats-abierto");
            est.boton?.boton.setAttribute("aria-expanded", "true");
            est.vueltasSinCambios = 0;
            programar(0);
        }
        if (filtro) elegirFiltro(filtro);
        if (idConversacion) seleccionar(idConversacion);
        else setTimeout(() => (raiz.querySelector(".chats-conv.activa") || $("#chatsBuscar")).focus(), 30);
        marcarLeidaSiCorresponde();
    }

    function cerrar() {
        if (est.modo === "pagina" || !est.abierto) return;
        est.abierto = false;
        raiz.classList.remove("abierto");
        document.body.classList.remove("chats-abierto");
        cerrarPlantillas();
        est.boton?.boton.setAttribute("aria-expanded", "false");
        est.boton?.boton.focus();
        programar();
    }

    function expandir(si) {
        est.expandido = si;
        raiz.classList.toggle("expandida", si);
        // Con la pantalla completa hay lugar para los datos del paciente; en el cajón angosto
        // se superponen al hilo, así que al achicar se ocultan (se vuelven a abrir con su botón).
        if (est.actual) mostrarContexto(si);
        dibujarExpandir();
    }

    function dibujarExpandir() {
        const b = $("#chatsExpandir");
        b.innerHTML = YS.icono(est.expandido ? "contraer" : "expandir");
        const t = est.expandido ? "Volver al panel lateral" : "Expandir a pantalla completa";
        b.setAttribute("aria-label", t); b.title = t;
    }

    // ── Lista de conversaciones ─────────────────────────────────
    function aplicarResumen(lista, completa) {
        const antes = est.convs;
        if (completa) est.convs = new Map(lista.map(c => [c.idConversacion, c]));
        else { est.convs = new Map(antes); lista.forEach(c => est.convs.set(c.idConversacion, c)); }
        let cambios = completa ? JSON.stringify([...antes.values()]) !== JSON.stringify(lista) : lista.length > 0;
        est.maxId = Math.max(est.maxId, ...[...est.convs.values()].map(c => c.idUltimoMensaje || 0), 0);

        if (est.cargada) {
            const llegaron = lista.filter(c => c.noLeidos > (antes.get(c.idConversacion)?.noLeidos ?? 0));
            if (llegaron.length) sonar();
            const nuevos = llegaron.filter(c => !estaMirando(c.idConversacion));
            if (nuevos.length) avisarNuevos(nuevos);
        }
        est.cargada = true;
        if (completa) est.ultimaCompleta = Date.now();
        actualizarContadores();
        dibujarLista();
        if (est.actual) dibujarCabeceraHilo();
        return cambios;
    }

    const visibles = () => {
        const q = YS.normalizar(est.busqueda);
        const digitos = est.busqueda.replace(/\D/g, "").slice(-10);   // mismo criterio que el backend
        const filtro = FILTROS.find(f => f.id === est.filtro);
        return [...est.convs.values()]
            .filter(c => filtro.pasa(c))
            .filter(c => !est.soloMias || c.idProfesionalAsignado === YS.sesion.idProfesional())
            .filter(c => !est.filtroEtiqueta || (c.idsEtiquetas || []).includes(est.filtroEtiqueta))
            .filter(c => !q || YS.normalizar(c.nombrePaciente).includes(q) || (digitos.length >= 3 && c.telefono.includes(digitos)))
            .sort((a, b) => (a.estado === "Urgente" ? 0 : 1) - (b.estado === "Urgente" ? 0 : 1)
                || new Date(b.fechaUltimoMensaje || b.fechaUltimaActividad) - new Date(a.fechaUltimoMensaje || a.fechaUltimaActividad));
    };

    function alternarMias() {
        est.soloMias = !est.soloMias;
        raiz.querySelector("[data-mias]")?.classList.toggle("activo", est.soloMias);
        raiz.querySelector("[data-mias]")?.setAttribute("aria-pressed", est.soloMias);
        dibujarLista();
    }

    // Paso 5: filtro por etiqueta en la bandeja, sobre lo ya visible para esta identidad
    // (est.convs, que llega filtrado por pertenencia - mismo criterio que "Mías"/búsqueda,
    // client-side sobre esa misma lista en vez de un viaje al servidor).
    function dibujarFiltroEtiquetas() {
        const sel = $("#chatsFiltroEtiqueta");
        if (!sel) return;
        const activas = est.etiquetasCatalogo || [];
        if (est.filtroEtiqueta && !activas.some(et => et.idEtiqueta === est.filtroEtiqueta)) est.filtroEtiqueta = null;
        sel.innerHTML = `<option value="">Todas las etiquetas</option>` +
            activas.map(et => `<option value="${et.idEtiqueta}"${et.idEtiqueta === est.filtroEtiqueta ? " selected" : ""}>${YS.esc(et.nombre)}</option>`).join("");
    }

    function dibujarLista() {
        const ul = $("#chatsConversaciones");
        const lista = visibles();
        // No se redibuja si nada cambió: evita parpadeos y no pierde el foco con el polling
        // est.etiquetasCatalogo entra en la firma: cambia qué chip se ve por fila sin que
        // cambien los datos de "lista" (llega después, por separado - ver iniciar()).
        const firma = JSON.stringify([est.filtro, est.soloMias, est.filtroEtiqueta, est.busqueda, est.actual, lista, est.etiquetasCatalogo]);
        if (firma === est.firmaLista) return;
        est.firmaLista = firma;
        const foco = document.activeElement?.closest?.(".chats-conv")?.dataset.id;

        if (!est.cargada) { ul.innerHTML = `<li>${YS.esqueleto.lista(5)}</li>`; return; }
        if (!lista.length) {
            const hayAlguna = est.convs.size > 0;
            const vacio = est.busqueda
                ? YS.vacio({ icono: "buscar", titulo: "Sin resultados", texto: "Ninguna conversación coincide con la búsqueda.", accion: { texto: "Limpiar búsqueda", onclick: "YS.bandeja.limpiarBusqueda()" } })
                : !hayAlguna
                    ? YS.vacio({ icono: "chat", titulo: "Todavía no hay conversaciones", texto: "Cuando un paciente escriba por WhatsApp (o desde el simulador) vas a verlo acá." })
                    : YS.vacio({
                        icono: "bandeja", titulo: "Nada por acá",
                        texto: textoFiltroVacio() + (est.soloMias ? " Estás viendo solo tus pacientes." : ""),
                        accion: est.soloMias
                            ? { texto: "Ver de todos", onclick: "YS.bandeja.quitarMias()" }
                            : { texto: "Ver todas", onclick: "YS.bandeja.filtrar('todas')" }
                    });
            ul.innerHTML = `<li class="chats-lista-vacia">${vacio}</li>`;
            return;
        }
        ul.innerHTML = lista.map(c => {
            const nombre = c.nombrePaciente || formatearTelefono(c.telefono);
            const e = ESTADO[c.estado] || { texto: c.estado, chip: "neutral" };
            const previa = (c.ultimoMensaje ? (PREFIJO[c.origenUltimoMensaje] || "") + c.ultimoMensaje : "Sin mensajes");
            const clases = ["chats-conv", c.idConversacion === est.actual ? "activa" : "", c.noLeidos ? "no-leida" : "", c.estado === "Urgente" ? "urgente" : ""].join(" ");
            const etiqueta = `${nombre}, ${e.texto}${c.noLeidos ? `, ${c.noLeidos} sin leer` : ""}`;
            return `<li><button type="button" class="${clases}" data-id="${c.idConversacion}" aria-label="${YS.esc(etiqueta)}"${c.idConversacion === est.actual ? ' aria-current="true"' : ""}>
                <span class="chats-conv-avatar">${YS.avatar(c.nombrePaciente, c.idConversacion)}<span class="chats-conv-canal" title="Chat simulado" aria-hidden="true">${YS.icono("chat")}</span></span>
                <span class="chats-conv-cuerpo">
                    <span class="chats-conv-fila">
                        <span class="chats-conv-nombre">${YS.esc(nombre)}</span>
                        <span class="chats-conv-hora">${YS.esc(YS.fecha.relativa(c.fechaUltimoMensaje || c.fechaUltimaActividad))}</span>
                    </span>
                    <span class="chats-conv-fila">
                        <span class="chats-conv-previa">${YS.esc(previa)}</span>
                        ${c.noLeidos ? `<span class="ui-contador${c.estado === "Urgente" ? " ui-contador--peligro" : ""}">${c.noLeidos > 99 ? "99+" : c.noLeidos}</span>` : ""}
                    </span>
                    <span class="chats-conv-fila chats-conv-fila--chip">
                        <span class="ui-chip ui-chip--${e.chip}">${YS.esc(e.texto)}</span>
                        ${(c.idsEtiquetas || []).map(ide => est.etiquetasCatalogo?.find(et => et.idEtiqueta === ide))
                            .filter(Boolean).map(et => `<span class="ui-chip ui-chip--${et.tono}">${YS.esc(et.nombre)}</span>`).join("")}
                        ${c.nombrePaciente ? `<span class="chats-conv-tel">${YS.esc(formatearTelefono(c.telefono))}</span>` : ""}
                    </span>
                </span>
            </button></li>`;
        }).join("");
        if (foco) ul.querySelector(`.chats-conv[data-id="${foco}"]`)?.focus();
    }

    function textoFiltroVacio() {
        return {
            noLeidas: "No hay conversaciones sin leer.",
            derivadas: "No hay conversaciones escaladas a una persona.",
            bot: "No hay conversaciones atendidas por el asistente.",
            cerradas: "No hay conversaciones cerradas."
        }[est.filtro] || "No hay conversaciones.";
    }

    function elegirFiltro(id) {
        est.filtro = FILTROS.some(f => f.id === id) ? id : "todas";
        raiz.querySelectorAll("[data-filtro]").forEach(b => {
            const si = b.dataset.filtro === est.filtro;
            b.classList.toggle("activo", si); b.setAttribute("aria-pressed", si);
        });
        dibujarLista();
    }

    function actualizarContadores() {
        const convs = [...est.convs.values()];
        const sinLeer = convs.filter(c => c.noLeidos > 0);
        const urgente = sinLeer.some(c => c.estado === "Urgente");
        est.boton?.contador(sinLeer.length, urgente);
        YS.shell.contadorTitulo(sinLeer.length);
        const total = $("#chatsTotal");
        total.textContent = sinLeer.length; total.classList.toggle("oculto", !sinLeer.length);
        total.classList.toggle("ui-contador--peligro", urgente);
        FILTROS.filter(f => f.contar).forEach(f => {
            const n = convs.filter(f.pasa).length;
            const c = raiz.querySelector(`[data-filtro="${f.id}"] .ui-contador`);
            c.textContent = n; c.classList.toggle("oculto", !n);
        });
        document.dispatchEvent(new CustomEvent("ys:chats-resumen", { detail: { sinLeer: sinLeer.length, derivadas: convs.filter(FILTROS[2].pasa).length } }));
    }

    // ── Hilo ────────────────────────────────────────────────────
    async function seleccionar(id) {
        if (!id) return;
        if (id === est.actual) {
            // Ya estaba elegida (p. ej. se abre desde el aviso de un mensaje nuevo):
            // se baja al último mensaje para que se vea y quede leída.
            if (est.hiloCargado) { irAbajo(false); marcarLeidaSiCorresponde(); }
            $("#chatsTexto")?.focus();
            return;
        }
        est.generacion++;
        est.actual = id;
        est.hiloCargado = false;
        est.mensajes = []; est.primerId = null; est.ultimoId = 0; est.hayMas = false;
        est.nuevosAbajo = 0; est.pendientes = []; est.leidaHasta = 0;
        raiz.classList.add("con-conversacion");
        dibujarHilo();
        dibujarLista();
        // Mantiene la preferencia ya elegida (est.contexto, con su propio default inicial en
        // iniciar()/expandir()) en vez de forzarla a visible en cada selección — antes, cerrar
        // el panel y elegir otra conversación lo volvía a abrir solo, sin que lo pidieran.
        if (est.expandido || est.modo === "pagina") mostrarContexto(est.contexto);
        cargarContexto();
        await cargarHilo();
        $("#chatsTexto")?.focus();
    }

    function dibujarHiloVacio() {
        $("#chatsHilo").innerHTML = `<div class="chats-hilo-vacio">${YS.vacio({ icono: "chat", titulo: "Elegí una conversación", texto: "Las urgentes aparecen arriba de todo. Atajos: / para buscar, Alt+↓ y Alt+↑ para cambiar de conversación, ? para ver todos." })}</div>`;
    }

    // La pertenencia la decide el servidor en cada pedido (Paso 2): esto puede pasar en
    // cualquier momento mientras la conversación está abierta (otra persona la tomó, el
    // CRM la reasignó). En vez de un error genérico, se saca de la vista como si
    // nunca hubiera estado - para esta identidad, deja de existir.
    function yaNoVisible(id) {
        est.convs.delete(id);
        dibujarLista();
        if (est.actual !== id) return;
        est.actual = null; est.hiloCargado = false; est.mensajes = [];
        raiz.classList.remove("con-conversacion");
        dibujarHiloVacio();
        dibujarContexto();
        YS.toast("Ya no podés ver esta conversación: puede que otra persona la haya tomado o te la hayan reasignado.", "info");
    }

    function dibujarHilo() {
        const c = est.convs.get(est.actual);
        $("#chatsHilo").innerHTML = `
            <header class="chats-hilo-cabecera" id="chatsHiloCabecera"></header>
            <div class="ui-alerta ui-alerta--danger chats-banner oculto" id="chatsBannerUrgente" role="alert">
                ${YS.icono("alerta")}<span>Posible situación de riesgo. El asistente le mandó al paciente las líneas de emergencia y quedó en pausa: respondé vos y, cuando esté resuelto, marcá la conversación como resuelta.</span>
            </div>
            <div class="chats-mensajes" id="chatsMensajes" role="log" aria-live="polite" aria-label="Mensajes">${YS.cargando("Cargando mensajes…")}</div>
            <button class="chats-ir-abajo oculto" type="button" id="chatsIrAbajo" aria-label="Ir al último mensaje">${YS.icono("flecha-abajo")}<span id="chatsNuevosAbajo"></span></button>
            <form class="chats-composer" id="chatsComposer" autocomplete="off">
                <div class="chats-composer-canal">
                    <label for="chatsCanalEnvio">Enviar por</label>
                    <select class="ui-select" id="chatsCanalEnvio" aria-label="Canal de envío">
                        <option value="Simulado">Chat simulado</option>
                    </select>
                </div>
                <div class="chats-composer-fila">
                    <div class="chats-plantillas oculto" id="chatsPlantillas" role="listbox" aria-label="Respuestas rápidas"></div>
                    <button class="ui-btn-icono" type="button" id="chatsBtnPlantillas" aria-label="Respuestas rápidas (/)" title="Respuestas rápidas (/)" aria-expanded="false">${YS.icono("rayo")}</button>
                    <label class="sr-only" for="chatsTexto">Respuesta</label>
                    <textarea id="chatsTexto" rows="1" maxlength="4000" placeholder="Escribí un mensaje — &quot;/&quot; respuestas rápidas"></textarea>
                    <button class="ui-btn ui-btn--primary chats-enviar" type="submit" id="chatsEnviar" aria-label="Enviar (Enter)" title="Enviar (Enter)">${YS.icono("enviar")}</button>
                </div>
            </form>
            <div class="chats-composer-ayuda"><span class="ui-kbd">Enter</span> envía · <span class="ui-kbd">Shift</span>+<span class="ui-kbd">Enter</span> nueva línea · <span class="ui-kbd">/</span> respuestas rápidas</div>`;
        dibujarCabeceraHilo();
        const m = $("#chatsMensajes");
        m.addEventListener("scroll", alScrollHilo, { passive: true });
        $("#chatsIrAbajo").addEventListener("click", () => irAbajo(true));
        const t = $("#chatsTexto");
        t.addEventListener("input", () => { ajustarAlto(); filtrarPlantillasDesdeTexto(); });
        t.addEventListener("keydown", teclasComposer);
        $("#chatsComposer").addEventListener("submit", e => { e.preventDefault(); enviar(); });
        $("#chatsBtnPlantillas").addEventListener("click", () => {
            est.plantillasPorTexto = false;
            $("#chatsPlantillas").classList.contains("oculto") ? abrirPlantillas() : cerrarPlantillas();
        });
        actualizarComposer(c);
    }

    function dibujarCabeceraHilo() {
        const c = est.convs.get(est.actual);
        const cab = $("#chatsHiloCabecera");
        if (!cab || !c) return;
        const e = ESTADO[c.estado] || { texto: c.estado, chip: "neutral", desc: "" };
        const nombre = c.nombrePaciente || "Paciente sin identificar";
        const escalada = c.estado === "Urgente" || c.estado === "DerivadaHumano";
        const esCrm = esAdministradorChats();
        const yo = YS.shell.rol === "profesional" ? YS.sesion.idProfesional() : null;
        // Pertenencia (Paso 3): Tomar es para cualquier profesional cuando está sin asignar;
        // Devolver, para quien la tiene (o CRM) - CRM reasigna directo desde el panel de
        // información, no "toma".
        const puedeTomar = escalada && yo != null && !c.idProfesionalAsignado;
        const puedeDevolver = escalada && c.idProfesionalAsignado != null && (esCrm || c.idProfesionalAsignado === yo);
        const firma = JSON.stringify([c.estado, nombre, c.telefono, c.idProfesionalAsignado, est.contexto]);
        if (cab.dataset.firma === firma) return;
        cab.dataset.firma = firma;
        cab.innerHTML = `
            <button class="ui-btn-icono chats-volver" type="button" aria-label="Volver a la lista">${YS.icono("chevron-izq")}</button>
            ${YS.avatar(c.nombrePaciente, c.idConversacion)}
            <div class="chats-hilo-titulos">
                <div class="chats-hilo-nombre">${YS.esc(nombre)}</div>
                <div class="chats-hilo-sub">Tel. ${YS.esc(formatearTelefono(c.telefono))} · WhatsApp ${YS.esc(c.canal === "Simulado" ? "(simulado)" : "")}</div>
            </div>
            <span class="ui-chip ui-chip--${e.chip}" title="${YS.esc(e.desc)}">${YS.esc(e.texto)}</span>
            <span class="ui-espacio"></span>
            ${puedeTomar ? `<button class="ui-btn ui-btn--sm ui-btn--primary" type="button" id="chatsBtnTomar">Tomar</button>` : ""}
            ${puedeDevolver ? `<button class="ui-btn ui-btn--sm" type="button" id="chatsBtnDevolver">Devolver</button>` : ""}
            ${escalada
                ? `<button class="ui-btn ui-btn--sm ${c.estado === "Urgente" ? "ui-btn--danger" : ""}" type="button" id="chatsBtnResolver">${c.estado === "Urgente" ? "Marcar como resuelta" : "Cerrar escalamiento"}</button>` : ""}
            ${c.estado === "Cerrada" ? `<button class="ui-btn ui-btn--sm" type="button" id="chatsBtnReabrir">Reabrir</button>` : ""}
            <button class="ui-btn-icono${est.contexto ? " activo" : ""}" type="button" id="chatsBtnContexto" aria-pressed="${est.contexto}" aria-label="Información del lead" title="Información del lead">${YS.icono("panel-der")}</button>`;
        cab.querySelector(".chats-volver").addEventListener("click", () => { raiz.classList.remove("con-conversacion"); });
        cab.querySelector("#chatsBtnTomar")?.addEventListener("click", tomarConversacion);
        cab.querySelector("#chatsBtnDevolver")?.addEventListener("click", devolverConversacion);
        cab.querySelector("#chatsBtnResolver")?.addEventListener("click", cerrarDerivacion);
        cab.querySelector("#chatsBtnReabrir")?.addEventListener("click", reabrirConversacion);
        cab.querySelector("#chatsBtnContexto").addEventListener("click", () => mostrarContexto(!est.contexto));
        $("#chatsBannerUrgente")?.classList.toggle("oculto", c.estado !== "Urgente");
        actualizarComposer(c);
    }

    function actualizarComposer(c) {
        const t = $("#chatsTexto");
        if (!t || !c) return;
        const cerrada = c.estado === "Cerrada";
        t.disabled = cerrada;
        $("#chatsEnviar").disabled = cerrada;
        $("#chatsBtnPlantillas").disabled = cerrada;
        t.placeholder = cerrada ? "La conversación está cerrada. Si el paciente vuelve a escribir, la atiende el asistente." : 'Escribí un mensaje — "/" respuestas rápidas';
    }

    async function cargarHilo() {
        const gen = est.generacion, id = est.actual;
        try {
            const lista = await YS.api(`${BASE}/conversaciones/${id}/mensajes?limite=${PAGINA}`);
            if (gen !== est.generacion) return;
            est.mensajes = lista;
            est.primerId = lista[0]?.idMensaje ?? null;
            est.ultimoId = lista.length ? lista[lista.length - 1].idMensaje : 0;
            est.hayMas = lista.length === PAGINA;
            est.hiloCargado = true;
            renderMensajes();
            irAbajo(false);
            marcarLeidaSiCorresponde();
        } catch (e) {
            if (gen !== est.generacion) return;
            if (e.status === 404) { yaNoVisible(id); return; }
            $("#chatsMensajes").innerHTML = YS.errorCarga({ texto: e.message, reintentar: "YS.bandeja.recargarHilo()" });
        }
    }

    // Historial hacia arriba: al llegar arriba de todo se piden los 50 anteriores.
    async function cargarAnteriores() {
        if (!est.hayMas || est.cargandoArriba || !est.primerId) return;
        est.cargandoArriba = true;
        const gen = est.generacion, m = $("#chatsMensajes");
        const boton = m.querySelector(".chats-cargar-mas");
        if (boton) boton.innerHTML = '<span class="ui-spinner" aria-hidden="true"></span> Cargando…';
        try {
            const lista = await YS.api(`${BASE}/conversaciones/${est.actual}/mensajes?antes=${est.primerId}&limite=${PAGINA}`);
            if (gen !== est.generacion) return;
            const alto = m.scrollHeight, arriba = m.scrollTop;
            est.mensajes = lista.concat(est.mensajes);
            est.primerId = est.mensajes[0]?.idMensaje ?? est.primerId;
            est.hayMas = lista.length === PAGINA;
            renderMensajes();
            m.scrollTop = m.scrollHeight - alto + arriba;   // el usuario no pierde de vista lo que leía
        } catch (e) {
            YS.toast("No se pudieron cargar los mensajes anteriores. " + e.message, "error");
            if (boton) boton.textContent = "Cargar mensajes anteriores";
        } finally { est.cargandoArriba = false; }
    }

    async function traerNuevos() {
        if (!est.actual) return;
        const gen = est.generacion;
        try {
            const nuevos = await YS.api(`${BASE}/conversaciones/${est.actual}/mensajes?desde=${est.ultimoId}`);
            if (gen !== est.generacion || !nuevos.length) return;
            const estabaAbajo = estaAbajo();
            const propios = new Set(est.mensajes.map(m => m.idMensaje));
            const agregar = nuevos.filter(n => !propios.has(n.idMensaje));
            if (!agregar.length) return;
            est.mensajes.push(...agregar);
            est.ultimoId = Math.max(est.ultimoId, ...agregar.map(n => n.idMensaje));
            agregarMensajes(agregar);
            if (estabaAbajo) irAbajo(true);
            else {
                est.nuevosAbajo += agregar.filter(n => n.direccion === "Entrante").length || agregar.length;
                mostrarIrAbajo();
            }
            marcarLeidaSiCorresponde();
        } catch { /* lo reintenta el próximo pulso */ }
    }

    function renderMensajes() {
        const m = $("#chatsMensajes");
        if (!m) return;
        m.replaceChildren();
        if (est.hayMas) {
            const b = YS.el("button", { class: "chats-cargar-mas", type: "button", text: "Cargar mensajes anteriores", on: { click: cargarAnteriores } });
            m.appendChild(b);
        } else if (est.mensajes.length) {
            m.appendChild(YS.el("div", { class: "chats-inicio-conv", text: "Inicio de la conversación" }));
        }
        if (!est.mensajes.length && !est.pendientes.length) {
            m.insertAdjacentHTML("beforeend", YS.vacio({ icono: "chat", texto: "Todavía no hay mensajes en esta conversación." }));
            return;
        }
        let previo = null;
        est.mensajes.forEach(msg => { m.appendChild(...nodosMensaje(msg, previo)); previo = msg; });
        est.pendientes.forEach(p => m.appendChild(nodoPendiente(p)));
    }

    function agregarMensajes(lista) {
        const m = $("#chatsMensajes");
        m.querySelector(".ui-vacio-estado")?.remove();
        const pendientes = [...m.querySelectorAll(".bm--pendiente")];
        let previo = est.mensajes[est.mensajes.length - lista.length - 1] || null;
        lista.forEach(msg => {
            nodosMensaje(msg, previo).forEach(n => pendientes.length ? m.insertBefore(n, pendientes[0]) : m.appendChild(n));
            previo = msg;
        });
    }

    // Devuelve [separadorDeDía?, burbuja]
    function nodosMensaje(msg, previo) {
        const nodos = [];
        const fecha = YS.fecha.parse(msg.fechaHora);
        if (!previo || !YS.fecha.mismoDia(previo.fechaHora, fecha)) {
            nodos.push(YS.el("div", { class: "bm-dia", role: "separator" }, YS.el("span", { text: YS.fecha.separador(fecha) })));
        }
        const delPaciente = msg.direccion === "Entrante";
        const seguido = previo && previo.origen === msg.origen && YS.fecha.mismoDia(previo.fechaHora, fecha)
            && fecha - YS.fecha.parse(previo.fechaHora) < AGRUPAR_MS;
        const b = YS.el("div", {
            class: `bm ${delPaciente ? "bm--izq" : "bm--der"} bm--${(msg.origen || "").toLowerCase()}${seguido ? " bm--seguido" : ""}`,
            dataset: { id: msg.idMensaje }
        });
        if (!delPaciente && !seguido) {
            b.appendChild(YS.el("div", { class: "bm-autor", html: (msg.origen === "Bot" ? YS.icono("bot") : msg.origen === "Sistema" ? YS.icono("info") : "") + YS.esc(AUTOR[msg.origen] || msg.origen) }));
        }
        const texto = YS.el("div", { class: "bm-texto" });
        textoConLinks(texto, msg.texto);
        b.appendChild(texto);
        const meta = YS.el("div", { class: "bm-meta" + (msg.estadoEnvio === "Error" ? " bm-meta--error" : "") });
        meta.append(YS.fecha.hora(fecha));
        if (!delPaciente) {
            if (msg.estadoEnvio === "Error") meta.append(" · no entregado");
            else if (msg.estadoEnvio === "Pendiente") meta.append(YS.el("span", { class: "bm-estado", title: "Enviando", text: " · enviando" }));
            else meta.appendChild(YS.el("span", { class: "bm-estado", title: "Enviado", "aria-label": "Enviado", html: YS.icono("check") }));
        }
        b.appendChild(meta);
        nodos.push(b);
        return nodos;
    }

    function nodoPendiente(p) {
        const b = YS.el("div", { class: "bm bm--der bm--humano bm--pendiente" + (p.error ? " bm--fallo" : ""), dataset: { tmp: p.id } });
        b.appendChild(YS.el("div", { class: "bm-texto", text: p.texto }));
        const meta = YS.el("div", { class: "bm-meta" + (p.error ? " bm-meta--error" : "") });
        if (p.error) {
            meta.append(`No se pudo enviar${p.error !== true ? ": " + p.error : ""} · `,
                YS.el("button", { type: "button", class: "bm-accion", text: "Reintentar", on: { click: () => reintentar(p.id) } }),
                " · ",
                YS.el("button", { type: "button", class: "bm-accion", text: "Descartar", on: { click: () => descartar(p.id) } }));
        } else meta.append("Enviando…");
        b.appendChild(meta);
        return b;
    }

    function textoConLinks(cont, texto) {
        String(texto ?? "").split(/(https?:\/\/[^\s]+)/g).forEach(parte => {
            if (/^https?:\/\//.test(parte)) cont.appendChild(YS.el("a", { href: parte, target: "_blank", rel: "noopener noreferrer", text: parte }));
            else if (parte) cont.appendChild(document.createTextNode(parte));
        });
    }

    const estaAbajo = () => { const m = $("#chatsMensajes"); return !m || m.scrollHeight - m.scrollTop - m.clientHeight < 80; };

    function irAbajo(suave) {
        const m = $("#chatsMensajes");
        if (!m) return;
        m.scrollTo({ top: m.scrollHeight, behavior: suave ? "smooth" : "auto" });
        est.nuevosAbajo = 0;
        mostrarIrAbajo();
    }

    function alScrollHilo() {
        const m = $("#chatsMensajes");
        if (m.scrollTop < 60) cargarAnteriores();
        if (estaAbajo()) { est.nuevosAbajo = 0; marcarLeidaSiCorresponde(); }
        mostrarIrAbajo();
    }

    function mostrarIrAbajo() {
        const b = $("#chatsIrAbajo");
        if (!b) return;
        b.classList.toggle("oculto", estaAbajo());
        $("#chatsNuevosAbajo").textContent = est.nuevosAbajo ? `${est.nuevosAbajo} nuevo${est.nuevosAbajo === 1 ? "" : "s"}` : "";
        b.classList.toggle("con-nuevos", est.nuevosAbajo > 0);
    }

    // ── Leído ───────────────────────────────────────────────────
    // En modo cajón, "visible" es que esté abierto. En modo "pagina" no hay abrir/cerrar
    // (ver abrir()), pero el contenedor puede estar tapado por otra pestaña del panel que
    // lo tapa con la clase .oculto en vez de desmontarlo (Pantalla_Configurador/js/dashboard.js,
    // mostrarTab) — sin este chequeo, un mensaje nuevo en una conversación abierta se marcaba
    // leído (y no avisaba con toast ni sonido) aunque la persona estuviera en otra pestaña.
    const estaVisible = () => est.modo === "pagina" ? raiz?.offsetParent !== null : est.abierto;
    const estaMirando = id => id === est.actual && estaVisible() && !document.hidden && estaAbajo();

    const marcarLeidaSiCorresponde = YS.debounce(async () => {
        const c = est.convs.get(est.actual);
        if (!c || !estaMirando(c.idConversacion) || !est.ultimoId || est.leidaHasta >= est.ultimoId) return;
        const hasta = est.ultimoId, id = c.idConversacion;
        est.leidaHasta = hasta;
        try {
            await YS.api(`${BASE}/conversaciones/${id}/leida`, { method: "PUT", json: { hasta } });
            const actual = est.convs.get(id);
            if (actual) { actual.noLeidos = 0; est.firmaLista = ""; actualizarContadores(); dibujarLista(); }
        } catch { est.leidaHasta = 0; }
    }, 500);

    // ── Envío ───────────────────────────────────────────────────
    function ajustarAlto() {
        const t = $("#chatsTexto");
        t.style.height = "auto";
        t.style.height = Math.min(t.scrollHeight, 140) + "px";
    }

    function teclasComposer(e) {
        const lista = $("#chatsPlantillas");
        if (!lista.classList.contains("oculto")) {
            const items = [...lista.querySelectorAll(".chats-plantilla")];
            let i = items.findIndex(x => x.classList.contains("activa"));
            if (e.key === "ArrowDown" || e.key === "ArrowUp") {
                e.preventDefault();
                i = (i + (e.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
                items.forEach((x, k) => x.classList.toggle("activa", k === i));
                items[i]?.scrollIntoView({ block: "nearest" });
                return;
            }
            if ((e.key === "Enter" || e.key === "Tab") && items.length) { e.preventDefault(); usarPlantilla(parseInt((items[i] || items[0]).dataset.i)); return; }
            if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); cerrarPlantillas(); return; }
        }
        if (e.key === "Enter" && !e.shiftKey && !e.isComposing) { e.preventDefault(); enviar(); }
    }

    async function enviar() {
        const t = $("#chatsTexto");
        const texto = t.value.trim();
        const c = est.convs.get(est.actual);
        if (!texto || !c || c.estado === "Cerrada") return;
        const p = { id: ++est.idTmp, texto, conv: c.idConversacion, error: null };
        est.pendientes.push(p);
        t.value = ""; ajustarAlto(); cerrarPlantillas();
        $("#chatsMensajes").querySelector(".ui-vacio-estado")?.remove();
        $("#chatsMensajes").appendChild(nodoPendiente(p));
        irAbajo(true);
        await mandar(p);
        t.focus();
    }

    async function mandar(p) {
        const gen = est.generacion;
        try {
            await YS.api(`${BASE}/conversaciones/${p.conv}/responder`, { method: "POST", json: { texto: p.texto } });
            est.pendientes = est.pendientes.filter(x => x.id !== p.id);
            if (gen !== est.generacion) return;
            raiz.querySelector(`[data-tmp="${p.id}"]`)?.remove();
            await traerNuevos();
            irAbajo(true);
            est.vueltasSinCambios = 0; programar(400);
        } catch (e) {
            p.error = e.message || true;
            if (gen !== est.generacion) return;
            raiz.querySelector(`[data-tmp="${p.id}"]`)?.replaceWith(nodoPendiente(p));
        }
    }

    function reintentar(idTmp) {
        const p = est.pendientes.find(x => x.id === idTmp);
        if (!p) return;
        p.error = null;
        raiz.querySelector(`[data-tmp="${p.id}"]`)?.replaceWith(nodoPendiente(p));
        mandar(p);
    }

    function descartar(idTmp) {
        const p = est.pendientes.find(x => x.id === idTmp);
        est.pendientes = est.pendientes.filter(x => x.id !== idTmp);
        raiz.querySelector(`[data-tmp="${idTmp}"]`)?.remove();
        const t = $("#chatsTexto");
        if (p && t && !t.value) { t.value = p.texto; ajustarAlto(); t.focus(); }
    }

    // ── Respuestas rápidas (antes plantillas.json; ahora con ABM propio) ────
    async function cargarPlantillas() {
        try { est.plantillas = await YS.api(`${BASE}/respuestas-rapidas`); }
        catch { est.plantillas = []; }
    }

    function abrirPlantillas(filtro = "") {
        const lista = $("#chatsPlantillas");
        if (!lista) return;
        const q = YS.normalizar(filtro);
        const items = est.plantillas.map((p, i) => ({ ...p, i }))
            .filter(p => !q || YS.normalizar(p.titulo).includes(q) || YS.normalizar(p.texto).includes(q));
        lista.innerHTML = items.length
            ? `<div class="chats-plantillas-titulo">Respuestas rápidas</div>` + items.map((p, k) => `
                <button type="button" class="chats-plantilla${k === 0 ? " activa" : ""}" role="option" data-i="${p.i}">
                    <span class="chats-plantilla-titulo">${YS.esc(p.titulo)}</span>
                    <span class="chats-plantilla-texto">${YS.esc(p.texto)}</span>
                </button>`).join("")
            : `<div class="chats-plantillas-vacio">${est.plantillas.length ? "Ninguna respuesta coincide." : "Todavía no hay respuestas rápidas."}</div>`;
        lista.insertAdjacentHTML("beforeend", `<button type="button" class="chats-plantillas-administrar">${YS.icono("lapiz")} Administrar respuestas rápidas</button>`);
        lista.classList.remove("oculto");
        $("#chatsBtnPlantillas").setAttribute("aria-expanded", "true");
        lista.querySelectorAll(".chats-plantilla").forEach(b => b.addEventListener("mousedown", e => { e.preventDefault(); usarPlantilla(parseInt(b.dataset.i)); }));
        lista.querySelector(".chats-plantillas-administrar").addEventListener("mousedown", e => { e.preventDefault(); cerrarPlantillas(); abrirAdministrarPlantillas(); });
        $("#chatsTexto").focus();
    }

    // Globales (dueño null) solo las toca CRM; las de dueño son siempre propias
    // (la API ya filtra a "mías + globales" - ver RespuestaRapidaDAO.ObtenerParaUso).
    function abrirAdministrarPlantillas() {
        const esConfigurador = esAdministradorChats();   // (nombre histórico: hoy es CRM)
        const puedeEditar = p => p.idProfesional === null ? esConfigurador : true;
        const dibujar = () => `
            <div class="chats-admin-plantillas-lista">
                ${est.plantillas.length ? est.plantillas.map(p => `
                    <div class="chats-admin-plantilla">
                        <div class="chats-admin-plantilla-datos">
                            <div class="chats-plantilla-titulo">${YS.esc(p.titulo)} ${p.idProfesional === null ? '<span class="ui-chip ui-chip--neutral">Global</span>' : ""}</div>
                            <div class="chats-plantilla-texto">${YS.esc(p.texto)}</div>
                        </div>
                        ${puedeEditar(p) ? `<div class="chats-admin-plantilla-acciones">
                            <button type="button" class="ui-btn-icono" data-editar="${p.idRespuestaRapida}" aria-label="Editar">${YS.icono("lapiz")}</button>
                            <button type="button" class="ui-btn-icono" data-borrar="${p.idRespuestaRapida}" aria-label="Borrar">${YS.icono("papelera")}</button>
                        </div>` : ""}
                    </div>`).join("") : `<p class="chats-dato-vacio">Todavía no hay respuestas rápidas.</p>`}
            </div>
            <div class="chats-admin-plantilla-nueva">
                <div class="ui-campo"><label for="plantillaNuevaTitulo">Título</label><input class="ui-input" id="plantillaNuevaTitulo" maxlength="60" placeholder="Por ejemplo: Saludo"></div>
                <div class="ui-campo"><label for="plantillaNuevaTexto">Texto</label><textarea class="ui-textarea" id="plantillaNuevaTexto" rows="2" maxlength="2000"></textarea></div>
                <button class="ui-btn ui-btn--sm ui-btn--primary" type="button" id="chatsPlantillaAgregar">${esConfigurador ? "Agregar (global)" : "Agregar (propia)"}</button>
            </div>`;
        const m = YS.modal({ titulo: "Respuestas rápidas", tamano: "grande", enfocarCampo: false, cuerpo: dibujar(),
            acciones: [{ texto: "Cerrar", tipo: "primary", foco: true, alClick: c => c.cerrar() }] });
        const reDibujar = async () => { await cargarPlantillas(); m.cuerpo.innerHTML = dibujar(); };
        m.cuerpo.addEventListener("click", async e => {
            const btnE = e.target.closest("[data-editar]"), btnB = e.target.closest("[data-borrar]"), btnA = e.target.closest("#chatsPlantillaAgregar");
            if (btnE) editarPlantilla(parseInt(btnE.dataset.editar), reDibujar);
            else if (btnB) await borrarPlantilla(parseInt(btnB.dataset.borrar), reDibujar);
            else if (btnA) {
                const titulo = m.cuerpo.querySelector("#plantillaNuevaTitulo").value.trim();
                const texto = m.cuerpo.querySelector("#plantillaNuevaTexto").value.trim();
                if (!titulo || !texto) { YS.toast("Completá título y texto.", "error"); return; }
                try { await YS.api(`${BASE}/respuestas-rapidas`, { method: "POST", json: { titulo, texto } }); await reDibujar(); }
                catch (err) { YS.toast(err.message, "error"); }
            }
        });
    }

    function editarPlantilla(id, alGuardar) {
        const p = est.plantillas.find(x => x.idRespuestaRapida === id);
        if (!p) return;
        YS.modal({
            titulo: "Editar respuesta rápida",
            cuerpo: `
                <div class="ui-campo"><label for="plantillaEditarTitulo">Título</label><input class="ui-input" id="plantillaEditarTitulo" maxlength="60" value="${YS.esc(p.titulo)}"></div>
                <div class="ui-campo"><label for="plantillaEditarTexto">Texto</label><textarea class="ui-textarea" id="plantillaEditarTexto" rows="3" maxlength="2000">${YS.esc(p.texto)}</textarea></div>`,
            acciones: [
                { texto: "Cancelar", tipo: "ghost", alClick: c => c.cerrar() },
                { texto: "Guardar", tipo: "primary", foco: true, alClick: async c => {
                    const titulo = c.cuerpo.querySelector("#plantillaEditarTitulo").value.trim();
                    const texto = c.cuerpo.querySelector("#plantillaEditarTexto").value.trim();
                    if (!titulo || !texto) { YS.toast("Completá título y texto.", "error"); return; }
                    try { await YS.api(`${BASE}/respuestas-rapidas/${id}`, { method: "PUT", json: { titulo, texto } }); c.cerrar(); await alGuardar(); }
                    catch (e) { YS.toast(e.message, "error"); }
                } }
            ]
        });
    }

    async function borrarPlantilla(id, alGuardar) {
        if (!await YS.confirmar({ titulo: "Borrar respuesta rápida", mensaje: "Deja de estar disponible en el composer.", textoConfirmar: "Borrar", peligro: true })) return;
        try { await YS.api(`${BASE}/respuestas-rapidas/${id}`, { method: "DELETE" }); await alGuardar(); }
        catch (e) { YS.toast(e.message, "error"); }
    }

    function cerrarPlantillas() {
        $("#chatsPlantillas")?.classList.add("oculto");
        $("#chatsBtnPlantillas")?.setAttribute("aria-expanded", "false");
    }

    // Escribir "/" al principio de la respuesta abre las plantillas y filtra por lo que sigue.
    function filtrarPlantillasDesdeTexto() {
        const v = $("#chatsTexto").value;
        if (v.startsWith("/") && !v.includes("\n")) { est.plantillasPorTexto = true; abrirPlantillas(v.slice(1)); }
        else if (est.plantillasPorTexto) { est.plantillasPorTexto = false; cerrarPlantillas(); }
    }

    function usarPlantilla(i) {
        const p = est.plantillas[i];
        const t = $("#chatsTexto");
        if (!p || !t) return;
        const actual = t.value.startsWith("/") ? "" : t.value;
        t.value = actual ? actual.replace(/\s*$/, " ") + p.texto : p.texto;
        cerrarPlantillas(); ajustarAlto(); t.focus();
        t.setSelectionRange(t.value.length, t.value.length);
    }

    // ── Pertenencia: Tomar / Devolver (Paso 3) ───────────────────
    // Sin confirmación a propósito: tomar una escalación sin asignar tiene que ser rápido,
    // y es fácil de revertir con Devolver. Si alguien más se la llevó un instante antes, el
    // servidor responde 409 ("ya la tomó otra persona") y cargarContexto() (que sigue
    // llamándose siempre, haya salido bien o no) la saca de la vista si ya no es visible
    // (yaNoVisible). No espera a pulso(): una vuelta incremental no trae de vuelta un cambio
    // de asignación sin mensaje nuevo (ver pulso), así que el propio idProfesionalAsignado se
    // corrige en el acto - es un dato que esta misma respuesta ya confirmó.
    async function tomarConversacion() {
        const c = est.convs.get(est.actual);
        if (!c) return;
        try {
            await YS.api(`${BASE}/conversaciones/${c.idConversacion}/tomar`, { method: "PUT" });
            YS.toast("Conversación tomada.");
            c.idProfesionalAsignado = YS.sesion.idProfesional();
            dibujarCabeceraHilo(); dibujarLista();
        } catch (e) { YS.toast(e.message, "error"); }
        cargarContexto();
    }

    async function devolverConversacion() {
        const c = est.convs.get(est.actual);
        if (!c) return;
        let motivos;
        try { motivos = await YS.api("/casos/motivos-reasignacion"); }
        catch (e) { YS.toast(e.message, "error"); return; }
        YS.modal({
            titulo: "Devolver conversación",
            cuerpo: `
                <p>Vuelve a quedar sin asignar: cualquier profesional va a poder tomarla.</p>
                <div class="ui-campo"><label for="devolverMotivo">Motivo</label>
                    <select class="ui-select" id="devolverMotivo">
                        <option value="">Elegir…</option>
                        ${motivos.map(m => `<option value="${m}">${YS.esc(TEXTO_MOTIVO_ASIGNACION[m] || m)}</option>`).join("")}
                    </select>
                </div>`,
            acciones: [
                { texto: "Cancelar", tipo: "ghost", alClick: ctrl => ctrl.cerrar() },
                { texto: "Devolver", tipo: "primary", foco: true, alClick: async ctrl => {
                    const motivoCategoria = ctrl.cuerpo.querySelector("#devolverMotivo").value;
                    if (!motivoCategoria) { YS.toast("Elegí un motivo.", "error"); return; }
                    try {
                        await YS.api(`${BASE}/conversaciones/${c.idConversacion}/devolver`, { method: "PUT", json: { motivoCategoria } });
                        ctrl.cerrar();
                        YS.toast("Conversación devuelta a sin asignar.");
                        c.idProfesionalAsignado = null;
                        dibujarCabeceraHilo(); dibujarLista();
                        cargarContexto();
                    } catch (e) { YS.toast(e.message, "error"); }
                } }
            ]
        });
    }

    // ── Cerrar escalamiento ─────────────────────────────────────
    async function cerrarDerivacion() {
        const c = est.convs.get(est.actual);
        if (!c) return;
        const urgente = c.estado === "Urgente";
        if (!await YS.confirmar({
            titulo: urgente ? "Marcar como resuelta" : "Cerrar escalamiento",
            mensaje: "La conversación pasa a Cerrada y se le avisa al paciente. Si vuelve a escribir, lo atiende el asistente desde el menú.",
            textoConfirmar: urgente ? "Marcar como resuelta" : "Cerrar escalamiento"
        })) return;
        try {
            await YS.api(`${BASE}/conversaciones/${c.idConversacion}/cerrar`, { method: "PUT" });
            YS.toast(urgente ? "Conversación marcada como resuelta." : "Escalamiento cerrado.");
            est.vueltasSinCambios = 0;
            await pulso();
            cargarContexto();
        } catch (e) { YS.toast(e.message, "error"); }
    }

    // La reabre como "escalada a persona" (no vuelve al bot): alguien del equipo decidió
    // seguir atendiéndola.
    async function reabrirConversacion() {
        const c = est.convs.get(est.actual);
        if (!c) return;
        if (!await YS.confirmar({
            titulo: "Reabrir conversación",
            mensaje: "Vuelve a quedar escalada a una persona del equipo.",
            textoConfirmar: "Reabrir"
        })) return;
        try {
            await YS.api(`${BASE}/conversaciones/${c.idConversacion}/reabrir`, { method: "PUT" });
            YS.toast("Conversación reabierta.");
            est.vueltasSinCambios = 0;
            await pulso();
        } catch (e) { YS.toast(e.message, "error"); }
    }

    // ── Panel de contexto ───────────────────────────────────────
    function mostrarContexto(si) {
        est.contexto = si;
        raiz.classList.toggle("con-contexto", si);
        $("#chatsBtnContexto")?.classList.toggle("activo", si);
        $("#chatsBtnContexto")?.setAttribute("aria-pressed", si);
    }

    function dibujarContexto(contenido) {
        $("#chatsContexto").innerHTML = `
            <header class="chats-contexto-cabecera">
                <span>Información del lead</span>
                <span class="chats-contexto-configurar" aria-disabled="true" title="Próximamente">Configurar vista</span>
                <button class="ui-btn-icono" type="button" id="chatsCerrarContexto" aria-label="Ocultar información del lead">${YS.icono("cerrar")}</button>
            </header>
            <div class="chats-contexto-cuerpo" id="chatsContextoCuerpo">${contenido ?? YS.vacio({ icono: "usuario", texto: "Elegí una conversación para ver la información del lead." })}</div>`;
        $("#chatsCerrarContexto").addEventListener("click", () => mostrarContexto(false));
    }

    async function cargarContexto() {
        const id = est.actual, gen = est.generacion;
        if (!id) return;
        dibujarContexto(YS.esqueleto.lista(3));
        if (!est.etiquetasCatalogo) { try { est.etiquetasCatalogo = await YS.api(`${BASE}/etiquetas`); } catch { est.etiquetasCatalogo = []; } }
        let info, consentimiento = null, caso = null, notas = [];
        try { info = await YS.api(`${BASE}/conversaciones/${id}/paciente`); }
        catch (e) {
            if (gen !== est.generacion) return;
            if (e.status === 404) { yaNoVisible(id); return; }
            dibujarContexto(YS.errorCarga({ texto: e.message, reintentar: "YS.bandeja.recargarContexto()" }));
            return;
        }
        if (info.paciente) {
            try { consentimiento = await YS.api(`/pacientes/${info.paciente.idPaciente}/consentimiento-telemedicina`); } catch { }
            try { caso = await YS.api(`/pacientes/${info.paciente.idPaciente}/caso`); } catch { }   // 404 = todavía no tiene caso (Fase 2)
        }
        try { notas = await YS.api(`${BASE}/conversaciones/${id}/notas`); } catch { }
        if (gen !== est.generacion) return;
        const p = info.paciente;
        const dato = (etiqueta, valor) => `<div class="ui-dato"><span class="ui-dato-etiqueta">${YS.esc(etiqueta)}</span><span class="ui-dato-valor${valor ? "" : " vacio"}">${YS.esc(valor || "Sin cargar")}</span></div>`;
        const datoHtml = (etiqueta, html) => `<div class="ui-dato"><span class="ui-dato-etiqueta">${YS.esc(etiqueta)}</span><span class="ui-dato-valor">${html}</span></div>`;
        const CANAL_TEXTO = { Simulado: "Chat simulado" };
        const e = ESTADO[info.estado] || { texto: info.estado, chip: "neutral" };
        const idsEtiquetas = est.convs.get(id)?.idsEtiquetas || [];
        const chipsEtiquetas = idsEtiquetas.map(ide => est.etiquetasCatalogo.find(et => et.idEtiqueta === ide)).filter(Boolean);
        // Quién atiende ESTE hilo (Conversacion.IdProfesionalAsignado) - distinto de "Profesional
        // asignado" más abajo, que es el equipo tratante del paciente (PacienteProfesional).
        const escalada = info.estado === "Urgente" || info.estado === "DerivadaHumano";
        const atendidaPor = info.idProfesionalAsignado ? (info.nombreProfesionalAsignado || "—") : "Sin asignar";
        let html = `
            <div class="chats-contexto-persona">
                ${YS.avatar(p?.nombre, info.idConversacion, "lg")}
                <div><div class="chats-contexto-nombre">${YS.esc(p?.nombre || "Paciente sin identificar")}</div>
                     <span class="ui-chip ui-chip--${e.chip}">${YS.esc(e.texto)}</span></div>
            </div>
            <div class="chats-contexto-etiquetas">
                ${chipsEtiquetas.map(et => `<span class="ui-chip ui-chip--${et.tono} chats-etiqueta">${YS.esc(et.nombre)}</span>`).join("")}
                <button class="chats-etiqueta-agregar" type="button" id="chatsBtnEtiquetas" aria-label="Administrar etiquetas">${YS.icono("etiqueta")} ${chipsEtiquetas.length ? "" : "Etiquetar"}</button>
            </div>`;
        if (!p) {
            html += `<div class="chats-contexto-bloque">
                ${dato("Teléfono", formatearTelefono(info.telefono))}
                ${escalada ? dato("Atendida por", atendidaPor) : ""}</div>
                <p class="chats-contexto-nota">El asistente todavía no pudo identificar a este paciente: el teléfono no coincide con ningún paciente y no pasó su DNI.</p>`;
        } else {
            const t = info.proximoTurno;
            const participantes = [{ nombre: p.nombre, sub: "Paciente" }, ...info.profesionales.map(pr => ({ nombre: pr.nombre, sub: pr.especialidad || "Profesional" }))];
            const nombresProfesionales = info.profesionales.map(pr => pr.nombre).join(", ");
            const turnoHtml = t
                ? `${YS.esc(YS.capitalizar(YS.fecha.larga(t.momento)))} · ${YS.esc(YS.fecha.hora(t.momento))}<br>` +
                  `Con ${YS.esc(t.nombreProfesional)} · ${YS.esc(t.modalidad)} ${YS.turno.chip(t.estado)}`
                : `<span class="chats-dato-vacio">Sin turno próximo</span>`;
            const consentimientoHtml = consentimiento == null
                ? `<span class="chats-dato-vacio">No se pudo consultar</span>`
                : consentimiento.firmado
                    ? `<span class="ui-chip ui-chip--ok">Firmado el ${YS.esc(YS.fecha.corta(consentimiento.fechaFirma))}</span>`
                    : `<span class="ui-chip ui-chip--warn">Sin firmar</span>`;
            html += `
                <div class="chats-contexto-seccion">
                    <div class="chats-contexto-seccion-titulo">Participantes</div>
                    <div class="chats-participantes">
                        ${participantes.map(pp => `
                            <div class="chats-participante">
                                ${YS.avatar(pp.nombre, pp.nombre)}
                                <div class="chats-participante-datos"><span>${YS.esc(pp.nombre)}</span><small>${YS.esc(pp.sub)}</small></div>
                            </div>`).join("")}
                        <button class="chats-participante-agregar" type="button" disabled title="Próximamente" aria-label="Agregar participante">+</button>
                    </div>
                </div>
                <div class="chats-contexto-seccion">
                    <div class="chats-contexto-seccion-titulo">General</div>
                    <div class="ui-datos chats-contexto-datos">
                        ${dato("Nombre", p.nombre)}
                        ${dato("DNI", p.dni)}
                        ${dato("Teléfono", p.telefono ? formatearTelefono(p.telefono) : formatearTelefono(info.telefono))}
                        ${dato("Profesional asignado", nombresProfesionales || "Sin asignar")}
                        ${escalada ? dato("Atendida por", atendidaPor) : ""}
                        ${dato("ID de la conversación", String(info.idConversacion))}
                        ${dato("Creado", info.fechaCreacion ? `${YS.fecha.corta(info.fechaCreacion)} ${YS.fecha.hora(info.fechaCreacion)}` : "")}
                        ${dato("Origen", CANAL_TEXTO[info.canal] || info.canal || "")}
                        ${datoHtml("Estado", `<span class="ui-chip ui-chip--${e.chip}">${YS.esc(e.texto)}</span>`)}
                        ${datoHtml("Próximo turno", turnoHtml)}
                        ${datoHtml("Consentimiento", consentimientoHtml)}
                        ${caso ? datoHtml("Caso", `<a href="#casos?caso=${caso.idCaso}">${YS.esc(caso.estado)}${caso.nombreGrupo ? " · " + YS.esc(caso.nombreGrupo) : ""}${caso.nombreProfesionalAsignado ? " · " + YS.esc(caso.nombreProfesionalAsignado) : ""}</a>`) : ""}
                    </div>
                    ${consentimiento != null && !consentimiento.firmado ? `<p class="chats-contexto-nota">Sin firma no se pueden confirmar sus turnos (RN-02).</p>
                        <button class="ui-btn ui-btn--sm" type="button" id="chatsLinkFirma">Insertar link para firmar</button>` : ""}
                    <div class="chats-contexto-acciones">
                        ${puedeVerTurnos() ? `<button class="ui-btn ui-btn--sm" type="button" id="chatsVerTurnos">${YS.icono("agenda")}Ver turnos del paciente</button>` : ""}
                        ${esAdministradorChats() ? (escalada
                            ? `<button class="ui-btn ui-btn--sm" type="button" id="chatsBtnAsignar">${info.idProfesionalAsignado ? "Reasignar" : "Asignar"}</button>`
                            : `<button class="ui-btn ui-btn--sm" type="button" disabled title="Solo se puede asignar una conversación derivada a una persona o urgente">Asignar</button>`) : ""}
                    </div>
                </div>`;
        }
        html += `
            <div class="chats-contexto-seccion chats-contexto-notas">
                <div class="chats-contexto-seccion-titulo">Notas internas <span class="ui-muted">· no las ve el paciente</span></div>
                <div class="chats-notas-lista">${notas.length ? notas.map(n => `
                    <div class="chats-nota">
                        <div class="chats-nota-cuerpo">${YS.esc(n.texto)}</div>
                        <div class="chats-nota-meta">
                            <span>${YS.esc(n.nombreUsuario || "Equipo")} · ${YS.esc(YS.fecha.corta(n.fechaCreacion))} ${YS.esc(YS.fecha.hora(n.fechaCreacion))}</span>
                            ${esAdministradorChats() || n.idUsuario === YS.sesion.idUsuario()
                                ? `<button type="button" class="ui-btn-icono chats-nota-borrar" data-id="${n.idNota}" aria-label="Borrar nota">${YS.icono("papelera")}</button>` : ""}
                        </div>
                    </div>`).join("") : `<p class="chats-dato-vacio">Sin notas todavía.</p>`}</div>
                <div class="chats-nota-nueva">
                    <label class="sr-only" for="chatsNotaTexto">Nueva nota interna</label>
                    <textarea class="ui-textarea" id="chatsNotaTexto" rows="2" maxlength="1000" placeholder="Agregar una nota interna…"></textarea>
                    <button class="ui-btn ui-btn--sm ui-btn--primary" type="button" id="chatsNotaAgregar">Agregar</button>
                </div>
            </div>`;
        if (info.estado === "Urgente" || info.estado === "DerivadaHumano") {
            html += `<div class="chats-contexto-bloque chats-contexto-resolver">
                <button class="ui-btn ${info.estado === "Urgente" ? "ui-btn--danger" : ""} ui-btn--bloque" type="button" id="chatsResolverContexto">${info.estado === "Urgente" ? "Marcar como resuelta" : "Cerrar escalamiento"}</button>
                <p class="chats-contexto-nota">Pasa a Cerrada y el asistente vuelve a atender si el paciente escribe de nuevo.</p></div>`;
        }
        dibujarContexto(html);
        $("#chatsResolverContexto")?.addEventListener("click", cerrarDerivacion);
        $("#chatsLinkFirma")?.addEventListener("click", () => {
            const t = $("#chatsTexto");
            if (!t || t.disabled) return;
            const link = YS.API + "/pantallas/Pantalla_Paciente/consentimiento.html";
            t.value = (t.value ? t.value.replace(/\s*$/, " ") : "Podés firmar el consentimiento acá: ") + link;
            ajustarAlto(); t.focus();
        });
        $("#chatsVerTurnos")?.addEventListener("click", () => irAlPanel(`turnos?paciente=${p.idPaciente}`));
        $("#chatsBtnAsignar")?.addEventListener("click", () => abrirAsignar(id, info.idProfesionalAsignado));
        $("#chatsBtnEtiquetas")?.addEventListener("click", () => abrirEtiquetas(id, idsEtiquetas));
        $("#chatsNotaAgregar")?.addEventListener("click", () => agregarNota(id));
        raiz.querySelectorAll(".chats-nota-borrar").forEach(b => b.addEventListener("click", () => borrarNota(id, parseInt(b.dataset.id))));
    }

    // ── Notas internas ──────────────────────────────────────────
    async function agregarNota(idConversacion) {
        const t = $("#chatsNotaTexto");
        const texto = t.value.trim();
        if (!texto) return;
        try {
            await YS.api(`${BASE}/conversaciones/${idConversacion}/notas`, { method: "POST", json: { texto } });
            if (est.actual === idConversacion) cargarContexto();
        } catch (e) { YS.toast(e.message, "error"); }
    }

    async function borrarNota(idConversacion, idNota) {
        if (!await YS.confirmar({ titulo: "Borrar nota", mensaje: "Esta nota interna se borra para todo el equipo.", textoConfirmar: "Borrar", peligro: true })) return;
        try {
            await YS.api(`${BASE}/conversaciones/${idConversacion}/notas/${idNota}`, { method: "DELETE" });
            if (est.actual === idConversacion) cargarContexto();
        } catch (e) { YS.toast(e.message, "error"); }
    }

    // ── Etiquetas ───────────────────────────────────────────────
    async function abrirEtiquetas(idConversacion, idsActuales) {
        // Se vuelve a pedir acá (no alcanza con el caché de cargarContexto): es una acción
        // deliberada de la persona, y el catálogo puede haber cambiado en lo que estuvo
        // mirando la conversación.
        try { est.etiquetasCatalogo = await YS.api(`${BASE}/etiquetas`); } catch (e) { YS.toast(e.message, "error"); return; }
        const catalogo = est.etiquetasCatalogo;
        if (!catalogo.length) {
            YS.toast("Todavía no hay etiquetas en el catálogo. Las crea CRM y atención al cliente.", "info");
            return;
        }
        const idsElegidos = new Set(idsActuales);
        YS.modal({
            titulo: "Etiquetas de la conversación", tamano: "chico",
            cuerpo: `<div class="chats-etiquetas-picker">${catalogo.map(et => `
                <label class="ui-check">
                    <input type="checkbox" value="${et.idEtiqueta}"${idsElegidos.has(et.idEtiqueta) ? " checked" : ""}>
                    <span class="ui-chip ui-chip--${et.tono}">${YS.esc(et.nombre)}</span>
                </label>`).join("")}</div>`,
            acciones: [
                { texto: "Cerrar", tipo: "ghost", alClick: ctrl => ctrl.cerrar() },
                { texto: "Guardar", tipo: "primary", foco: true, alClick: async ctrl => {
                    const marcados = new Set([...ctrl.cuerpo.querySelectorAll("input[type=checkbox]:checked")].map(i => parseInt(i.value)));
                    const agregar = [...marcados].filter(i => !idsElegidos.has(i));
                    const quitar = [...idsElegidos].filter(i => !marcados.has(i));
                    try {
                        await Promise.all([
                            ...agregar.map(i => YS.api(`${BASE}/conversaciones/${idConversacion}/etiquetas/${i}`, { method: "POST" })),
                            ...quitar.map(i => YS.api(`${BASE}/conversaciones/${idConversacion}/etiquetas/${i}`, { method: "DELETE" }))
                        ]);
                        ctrl.cerrar();
                        // El pulso incremental solo trae conversaciones con un MensajeChat
                        // nuevo (ListarResumen) - poner o sacar una etiqueta no genera uno,
                        // así que esperar al próximo pulso la dejaría desactualizada hasta
                        // la vuelta completa (cada 60s). Se corrige el estado local, ya que
                        // la propia acción confirma que el servidor quedó así.
                        const conv = est.convs.get(idConversacion);
                        if (conv) conv.idsEtiquetas = [...marcados];
                        if (est.actual === idConversacion) cargarContexto();
                        dibujarLista();
                    } catch (e) { YS.toast(e.message, "error"); }
                } }
            ]
        });
    }

    // ── Asignar / reasignar (solo CRM) ──────────────────
    const TEXTO_MOTIVO_ASIGNACION = {
        CargaEquilibrada: "Carga equilibrada", PedidoDelPaciente: "Pedido del paciente", CambioDeEspecialidad: "Cambio de especialidad",
        ProfesionalNoDisponible: "Profesional no disponible", Otro: "Otro"
    };

    // idProfesionalAsignado acá es el de la CONVERSACIÓN (info.idProfesionalAsignado), no el
    // del Caso: ya no hace falta un caso abierto para asignar quién atiende el chat.
    async function abrirAsignar(idConversacion, idProfesionalAsignado) {
        let profesionales, motivos;
        try { [profesionales, motivos] = await Promise.all([YS.api("/profesionales"), YS.api("/casos/motivos-reasignacion")]); }
        catch (e) { YS.toast(e.message, "error"); return; }
        const activos = profesionales.filter(p => p.estado === "Activo");
        YS.modal({
            titulo: idProfesionalAsignado ? "Reasignar conversación" : "Asignar conversación",
            cuerpo: `
                <div class="ui-form-grid">
                    <div class="ui-campo"><label for="asignarProfesional">Profesional</label>
                        <select class="ui-select" id="asignarProfesional">
                            ${activos.map(p => `<option value="${p.idProfesional}"${p.idProfesional === idProfesionalAsignado ? " selected" : ""}>${YS.esc(YS.nombreCompleto(p))}${p.especialidad ? " — " + YS.esc(p.especialidad) : ""}</option>`).join("")}
                        </select>
                    </div>
                    <div class="ui-campo"><label for="asignarMotivo">Motivo</label>
                        <select class="ui-select" id="asignarMotivo">
                            <option value="">Elegir…</option>
                            ${motivos.map(m => `<option value="${m}">${YS.esc(TEXTO_MOTIVO_ASIGNACION[m] || m)}</option>`).join("")}
                        </select>
                    </div>
                </div>`,
            acciones: [
                { texto: "Cancelar", tipo: "ghost", alClick: ctrl => ctrl.cerrar() },
                { texto: idProfesionalAsignado ? "Reasignar" : "Asignar", tipo: "primary", foco: true, alClick: async ctrl => {
                    const idProfesional = parseInt(ctrl.cuerpo.querySelector("#asignarProfesional").value);
                    const motivoCategoria = ctrl.cuerpo.querySelector("#asignarMotivo").value;
                    if (!motivoCategoria) { YS.toast("Elegí un motivo.", "error"); return; }
                    try {
                        await YS.api(`${BASE}/conversaciones/${idConversacion}/asignar`, { method: "PUT", json: { idProfesional, motivoCategoria } });
                        ctrl.cerrar();
                        YS.toast("Conversación asignada.");
                        const c = est.convs.get(idConversacion);
                        if (c) { c.idProfesionalAsignado = idProfesional; dibujarCabeceraHilo(); dibujarLista(); }
                        cargarContexto();
                    } catch (e) { YS.toast(e.message, "error"); }
                } }
            ]
        });
    }

    // Etapa 3: el lado administrador de los chats es el módulo CRM (antes el Configurador).
    function esAdministradorChats() { return YS.shell?.rol === "crm"; }
    // "Ver turnos del paciente" lleva a CRM > Turnos, que existe solo con Gestión de agendas.
    // ("Ver ficha" se sacó: llevaba a Usuarios del Configurador, que ya no opera chats; los
    // datos de contacto del paciente ya están en este panel.)
    function puedeVerTurnos() { return esAdministradorChats() && YS.sesion.tienePrivilegio("gestion_agendas"); }

    // Accesos al panel de gestión (CRM): si ya estamos ahí, cambia de sección sin recargar.
    function irAlPanel(hash) {
        if (location.pathname.toLowerCase() === YS.rutas.gestion.toLowerCase()) {
            location.hash = hash;
            if (est.expandido) expandir(false);
            cerrar();
        } else location.href = YS.rutas.gestion + "#" + hash;
    }

    // ── Avisos: contador, toast y sonido ────────────────────────
    function avisarNuevos(nuevos) {
        if (estaVisible() && !document.hidden) return;   // ya lo ve en la lista
        const urgente = nuevos.find(c => c.estado === "Urgente");
        if (nuevos.length === 1 || urgente) {
            const c = urgente || nuevos[0];
            const nombre = c.nombrePaciente || formatearTelefono(c.telefono);
            YS.toast(`${urgente ? "URGENTE · " : ""}${nombre}: ${recortar(c.ultimoMensaje || "nuevo mensaje", 70)}`, urgente ? "error" : "info",
                { duracion: urgente ? 0 : 6000, accion: { texto: "Abrir", alClick: () => abrir(c.idConversacion) } });
        } else {
            YS.toast(`${nuevos.length} conversaciones con mensajes nuevos`, "info",
                { accion: { texto: "Ver", alClick: () => abrir(null, "noLeidas") } });
        }
    }

    function alternarSonido() {
        est.sonido = !est.sonido;
        try { localStorage.setItem(CLAVE_SONIDO, est.sonido ? "1" : "0"); } catch { }
        dibujarSonido();
        if (est.sonido) sonar(true);   // dentro del clic: el navegador habilita el audio
    }

    function dibujarSonido() {
        const b = $("#chatsSonido");
        b.innerHTML = YS.icono(est.sonido ? "sonido" : "sin-sonido");
        const t = est.sonido ? "Sonido activado al llegar mensajes (clic para silenciar)" : "Sonido desactivado (clic para activar)";
        b.setAttribute("aria-label", t); b.title = t;
        b.setAttribute("aria-pressed", est.sonido);
    }

    // Un "ding" corto generado en el momento (sin archivos de audio).
    function sonar(forzar = false) {
        if (!est.sonido && !forzar) return;
        try {
            const ctx = est.audio ??= new (window.AudioContext || window.webkitAudioContext)();
            if (ctx.state === "suspended") ctx.resume();
            const o = ctx.createOscillator(), g = ctx.createGain(), t = ctx.currentTime;
            o.type = "sine"; o.frequency.setValueAtTime(880, t); o.frequency.exponentialRampToValueAtTime(660, t + 0.18);
            g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.12, t + 0.02); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.3);
            o.connect(g).connect(ctx.destination); o.start(t); o.stop(t + 0.32);
        } catch { }
    }

    // ── Atajos ──────────────────────────────────────────────────
    function mostrarAtajos() {
        const fila = (teclas, que) => `<tr><td>${teclas.map(t => `<span class="ui-kbd">${YS.esc(t)}</span>`).join(" + ")}</td><td>${YS.esc(que)}</td></tr>`;
        YS.modal({
            titulo: "Atajos de teclado", tamano: "chico", enfocarCampo: false,
            cuerpo: `<table class="ui-tabla ui-tabla--densa chats-atajos"><tbody>
                ${fila(["Esc"], "Cerrar los chats (o las respuestas rápidas)")}
                ${fila(["/"], "Buscar una conversación")}
                ${fila(["Alt", "↓"], "Conversación siguiente")}
                ${fila(["Alt", "↑"], "Conversación anterior")}
                ${fila(["Enter"], "Enviar la respuesta")}
                ${fila(["Shift", "Enter"], "Nueva línea")}
                ${fila(["/"], "Al empezar la respuesta: respuestas rápidas")}
                ${fila(["?"], "Ver esta ayuda")}
            </tbody></table>`,
            acciones: [{ texto: "Entendido", tipo: "primary", foco: true, alClick: c => c.cerrar() }]
        });
    }

    // Hay modales viejos (Usuarios/Solicitudes en dashboard.js) que reusan la clase
    // .ui-modal-fondo por el estilo pero, a diferencia de YS.modal (que se hace remove() al
    // cerrar), solo la tapan con display:none y la dejan en el DOM — querySelector sola los
    // encuentra igual que a uno realmente abierto y los atajos de Chats quedaban apagados
    // para el resto de la sesión en cuanto se abría cualquiera de esos, aunque ya estuviera
    // cerrado. Hay que mirar si alguno de los que hay está de verdad visible, no solo si existe.
    const hayModalAbierto = () => [...document.querySelectorAll(".ui-modal-fondo")].some(f => getComputedStyle(f).display !== "none");

    // Se escucha en captura: si hay un modal o un menú abierto, Esc es para ellos y no cierra los chats.
    function teclasGlobales(e) {
        const visible = estaVisible();
        if (!raiz || !visible || hayModalAbierto()) return;
        const escribiendo = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName) || document.activeElement?.isContentEditable;
        if (e.key === "Escape") {
            if (!$("#chatsPlantillas")?.classList.contains("oculto")) { e.preventDefault(); cerrarPlantillas(); return; }
            if (document.querySelector(".ui-desplegable.abierto")) return;
            if (est.modo === "cajon") { e.preventDefault(); cerrar(); }
            return;
        }
        if (e.altKey && (e.key === "ArrowDown" || e.key === "ArrowUp")) {
            e.preventDefault();
            const lista = visibles();
            const i = lista.findIndex(c => c.idConversacion === est.actual);
            const sig = lista[Math.max(0, Math.min(lista.length - 1, i + (e.key === "ArrowDown" ? 1 : -1)))];
            if (sig) seleccionar(sig.idConversacion);
            return;
        }
        if (escribiendo) return;
        if (e.key === "/") { e.preventDefault(); $("#chatsBuscar").focus(); $("#chatsBuscar").select(); }
        else if (e.key === "?") { e.preventDefault(); mostrarAtajos(); }
    }

    // ── Polling ─────────────────────────────────────────────────
    // Con los chats a la vista: cada 3 s, y se va espaciando (hasta 12 s) si no hay novedades.
    // Cerrados: cada 20 s para el contador (hasta 45 s). Con la pestaña oculta: nada.
    function intervalo() {
        if (document.hidden) return null;
        const viendo = estaVisible();
        const base = viendo ? 3000 : 20000, max = viendo ? 12000 : 45000;
        return Math.min(max, Math.round(base * Math.pow(1.4, Math.min(est.vueltasSinCambios, 8))));
    }

    function programar(ms) {
        clearTimeout(est.timer);
        const t = ms ?? intervalo();
        if (t != null) est.timer = setTimeout(pulso, t);
    }

    // forzarCompleta: para YS.bandeja.actualizar() (refresco manual/pruebas) - una vuelta
    // incremental no alcanza para notar que una conversación dejó de ser visible (pertenencia:
    // Tomar/Devolver/reasignación de OTRA sesión no agregan un mensaje nuevo, así que
    // ?desdeMensaje nunca la traería de vuelta ni la sacaría de est.convs). "Actualizar" pide
    // la verdad ahora, no la aproximación de la próxima vuelta incremental.
    async function pulso(forzarCompleta = false) {
        if (est.pulsando) return;
        est.pulsando = true;
        clearTimeout(est.timer);
        try {
            // Una vuelta completa cada 20 s (antes 60: con pertenencia de conversaciones de por
            // medio, no es solo "marcas de lectura de otros usuarios" - es la ventana máxima en
            // la que alguien puede seguir viendo, por un rato, una conversación que ya no es
            // suya porque otra sesión la tomó o la reasignó; ver CAMBIOS_AGUS.md); el resto, solo
            // lo nuevo.
            const completa = forzarCompleta || !est.cargada || Date.now() - est.ultimaCompleta > 20000;
            const lista = await YS.api(`${BASE}/conversaciones${completa ? "" : `?desdeMensaje=${est.maxId}`}`);
            est.sinConexion = false;
            const cambios = aplicarResumen(lista, completa);
            const c = est.convs.get(est.actual);
            if (c && est.hiloCargado && (c.idUltimoMensaje || 0) > est.ultimoId) await traerNuevos();
            est.vueltasSinCambios = cambios ? 0 : est.vueltasSinCambios + 1;
        } catch (e) {
            if (e.status === 404) { apagar(); return; }
            est.sinConexion = true;
            est.vueltasSinCambios++;
        } finally {
            est.pulsando = false;
            $("#chatsConexion")?.classList.toggle("oculto", !est.sinConexion);
            if (raiz) programar();
        }
    }

    // El módulo se apagó con la pantalla abierta (o el pedido dejó de ser local): se saca todo.
    function apagar() {
        clearTimeout(est.timer);
        est.boton?.boton.remove();
        if (est.modo === "cajon") raiz?.remove();
        raiz = null;
        YS.shell.contadorTitulo(0);
    }

    // ── Utilidades ──────────────────────────────────────────────
    const recortar = (s, n) => (s.length > n ? s.slice(0, n - 1) + "…" : s);
    function formatearTelefono(t) {
        const d = String(t ?? "").replace(/\D/g, "");
        return d.length === 10 ? `${d.slice(0, 2)} ${d.slice(2, 6)}-${d.slice(6)}` : (t || "");
    }

    // ── Inicio ──────────────────────────────────────────────────
    /**
     * modo "cajon": se superpone a la pantalla actual desde el botón Chats (lo usa shell.js).
     * modo "pagina": dibujado dentro de opciones.contenedor (Pantalla_ChatSimulado/index.html).
     */
    function iniciar(opciones = {}) {
        if (raiz) return;
        est.modo = opciones.modo === "pagina" ? "pagina" : "cajon";
        try { est.sonido = localStorage.getItem(CLAVE_SONIDO) === "1"; } catch { }
        crear();
        if (est.modo === "pagina") {
            raiz.classList.add("chats--pagina", "abierto");
            opciones.contenedor.appendChild(raiz);
            // Debajo de 1500px la información del lead no entra cómoda al lado de lista y chat
            // (bandeja.css): arranca colapsada ahí, el botón de la cabecera del hilo la muestra.
            mostrarContexto(window.innerWidth >= 1500);
        } else {
            raiz.classList.add("chats--cajon");
            raiz.setAttribute("role", "dialog");
            raiz.setAttribute("aria-modal", "false");
            document.body.appendChild(raiz);
        }
        if (YS.shell?.boton) {
            // alPulsarBoton: lo usa el módulo CRM (Chats es una sección propia,
            // no un cajón) para que el botón navegue ahí en vez de abrir/cerrar algo.
            est.boton = YS.shell.boton({
                id: "btnChats", icono: "chat", etiqueta: "Chats", antes: true,
                alClick: opciones.alPulsarBoton || (() => est.modo === "pagina" ? $("#chatsBuscar").focus() : (est.abierto ? cerrar() : abrir()))
            });
            if (!opciones.alPulsarBoton) est.boton.boton.setAttribute("aria-expanded", "false");
        }
        document.addEventListener("keydown", teclasGlobales, true);
        document.addEventListener("mousedown", e => {
            if (raiz && !e.target.closest("#chatsComposer")) cerrarPlantillas();
        });
        document.addEventListener("visibilitychange", () => {
            if (!document.hidden) { est.vueltasSinCambios = 0; programar(0); marcarLeidaSiCorresponde(); }
        });
        cargarPlantillas();
        // El catálogo lo necesita dibujarLista() (chips por fila) desde el primer dibujo,
        // no solo cargarContexto()/abrirEtiquetas() (que lo refrescan cuando hace falta).
        YS.api(`${BASE}/etiquetas`).then(c => { est.etiquetasCatalogo = c; dibujarFiltroEtiquetas(); dibujarLista(); }).catch(() => { est.etiquetasCatalogo = []; });
        dibujarLista();
        pulso();
        instalarAvisosInternos();
    }

    // ── Avisos internos del módulo (campana de CRM) ───
    // Los que antes estaban en la campanita de la pantalla de mensajería: urgencias y
    // escalamientos a una persona (los de "un paciente avisó que no va a asistir" son para el
    // profesional, no para acá: se piden aparte con destinatario=Administrador). Una misma
    // conversación puede acumular varios avisos (cada vez que el bot deriva o detecta una
    // urgencia); se agrupan en uno solo con la cantidad, en vez de listarlos todos sueltos.
    function instalarAvisosInternos() {
        if (!YS.shell?.instalarCampana) return;
        YS.shell.instalarCampana({
            titulo: "Avisos",
            async cargar() {
                const lista = await YS.api(`${BASE}/notificaciones-internas?destinatario=Administrador`);
                const grupos = new Map();
                lista.forEach(a => {
                    const clave = a.idConversacion ?? `a${a.idNotificacionInterna}`;
                    if (!grupos.has(clave)) grupos.set(clave, []);
                    grupos.get(clave).push(a);
                });
                const items = [...grupos.values()].map(avisos => {
                    avisos.sort((x, y) => new Date(y.fechaCreacion) - new Date(x.fechaCreacion));
                    // La urgencia manda el título del grupo aunque no sea la más nueva; si ya está
                    // leída (se resolvió) vale el aviso más reciente, como si fuera uno solo.
                    const urgente = avisos.find(a => a.tipo === "Urgencia" && !a.leida);
                    const principal = urgente || avisos[0];
                    const noLeidos = avisos.filter(a => !a.leida);
                    const idConversacion = avisos[0].idConversacion;
                    return {
                        id: principal.idNotificacionInterna, urgente: !!urgente, noLeido: noLeidos.length > 0,
                        titulo: principal.titulo,
                        texto: avisos.length > 1 ? `${principal.mensaje} (${avisos.length} avisos de esta conversación)` : principal.mensaje,
                        fecha: `Para: ${principal.destinatario} · ${YS.fecha.corta(principal.fechaCreacion)} ${YS.fecha.hora(principal.fechaCreacion)}`,
                        acciones: [
                            idConversacion ? { texto: "Abrir chat", tipo: "primary", alClick: async () => { await marcarAvisos(noLeidos, false); YS.shell.campana.cerrar(); abrir(idConversacion); } } : null,
                            noLeidos.length ? { texto: noLeidos.length > 1 ? `Marcar leídos (${noLeidos.length})` : "Marcar leído", alClick: () => marcarAvisos(noLeidos) } : null
                        ].filter(Boolean)
                    };
                });
                return { noLeidos: lista.filter(a => !a.leida).length, items };
            },
            vacio: "No hay avisos."
        });
    }

    async function marcarAvisos(avisos, refrescar = true) {
        if (avisos.length) {
            try { await Promise.all(avisos.map(a => YS.api(`${BASE}/notificaciones-internas/${a.idNotificacionInterna}/leida`, { method: "PUT" }))); }
            catch (e) { YS.toast(e.message, "error"); return; }
        }
        if (refrescar) YS.shell.campana.refrescar();
    }

    YS.bandeja = {
        iniciar, abrir, cerrar, expandir,
        filtrar: elegirFiltro,
        quitarMias() { if (est.soloMias) alternarMias(); },
        limpiarBusqueda() { $("#chatsBuscar").value = ""; est.busqueda = ""; dibujarLista(); $("#chatsBuscar").focus(); },
        recargarHilo() { if (est.actual) { const id = est.actual; est.actual = null; seleccionar(id); } },
        recargarContexto: () => cargarContexto(),
        /** Pide las novedades ya, sin esperar la próxima vuelta del polling. */
        actualizar() { est.vueltasSinCambios = 0; return pulso(true); },
        get resumen() { return [...est.convs.values()]; }
    };
})();
