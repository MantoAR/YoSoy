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
        filtro: "todas", busqueda: "", firmaLista: "",
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
                    </div>
                    <ul class="chats-conversaciones" id="chatsConversaciones" aria-label="Conversaciones"></ul>
                </section>
                <section class="chats-hilo" id="chatsHilo" aria-label="Conversación"></section>
                <aside class="chats-contexto" id="chatsContexto" aria-label="Información del lead"></aside>
            </div>`;
        $("#chatsFiltros").innerHTML = FILTROS.map(f =>
            `<button class="ui-filtro${f.id === est.filtro ? " activo" : ""}" type="button" data-filtro="${f.id}" aria-pressed="${f.id === est.filtro}"${f.titulo ? ` title="${YS.esc(f.titulo)}"` : ""}>${YS.esc(f.texto)}${f.contar ? '<span class="ui-contador ui-contador--suave oculto"></span>' : ""}</button>`).join("");
        $("#chatsFiltros").addEventListener("click", e => {
            const b = e.target.closest("[data-filtro]");
            if (b) elegirFiltro(b.dataset.filtro);
        });
        $("#chatsBuscar").addEventListener("input", YS.debounce(() => { est.busqueda = $("#chatsBuscar").value.trim(); dibujarLista(); }, 120));
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
        if (est.modo === "pagina") { if (idConversacion) seleccionar(idConversacion); if (filtro) elegirFiltro(filtro); return; }
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
            .filter(c => !q || YS.normalizar(c.nombrePaciente).includes(q) || (digitos.length >= 3 && c.telefono.includes(digitos)))
            .sort((a, b) => (a.estado === "Urgente" ? 0 : 1) - (b.estado === "Urgente" ? 0 : 1)
                || new Date(b.fechaUltimoMensaje || b.fechaUltimaActividad) - new Date(a.fechaUltimoMensaje || a.fechaUltimaActividad));
    };

    function dibujarLista() {
        const ul = $("#chatsConversaciones");
        const lista = visibles();
        // No se redibuja si nada cambió: evita parpadeos y no pierde el foco con el polling
        const firma = JSON.stringify([est.filtro, est.busqueda, est.actual, lista]);
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
                    : YS.vacio({ icono: "bandeja", titulo: "Nada por acá", texto: textoFiltroVacio(), accion: { texto: "Ver todas", onclick: "YS.bandeja.filtrar('todas')" } });
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
                    <span class="chats-conv-fila chats-conv-fila--chip"><span class="ui-chip ui-chip--${e.chip}">${YS.esc(e.texto)}</span>${c.nombrePaciente ? `<span class="chats-conv-tel">${YS.esc(formatearTelefono(c.telefono))}</span>` : ""}</span>
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
        if (est.expandido || est.modo === "pagina") mostrarContexto(est.contexto || est.expandido || est.modo === "pagina");
        cargarContexto();
        await cargarHilo();
        $("#chatsTexto")?.focus();
    }

    function dibujarHiloVacio() {
        $("#chatsHilo").innerHTML = `<div class="chats-hilo-vacio">${YS.vacio({ icono: "chat", titulo: "Elegí una conversación", texto: "Las urgentes aparecen arriba de todo. Atajos: / para buscar, Alt+↓ y Alt+↑ para cambiar de conversación, ? para ver todos." })}</div>`;
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
        const firma = JSON.stringify([c.estado, nombre, c.telefono, est.contexto]);
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
            ${c.estado === "Urgente" || c.estado === "DerivadaHumano"
                ? `<button class="ui-btn ui-btn--sm ${c.estado === "Urgente" ? "ui-btn--danger" : ""}" type="button" id="chatsBtnResolver">${c.estado === "Urgente" ? "Marcar como resuelta" : "Cerrar escalamiento"}</button>` : ""}
            <button class="ui-btn-icono${est.contexto ? " activo" : ""}" type="button" id="chatsBtnContexto" aria-pressed="${est.contexto}" aria-label="Información del lead" title="Información del lead">${YS.icono("panel-der")}</button>`;
        cab.querySelector(".chats-volver").addEventListener("click", () => { raiz.classList.remove("con-conversacion"); });
        cab.querySelector("#chatsBtnResolver")?.addEventListener("click", cerrarDerivacion);
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
    const estaMirando = id => id === est.actual && (est.abierto || est.modo === "pagina") && !document.hidden && estaAbajo();

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

    // ── Respuestas rápidas (plantillas.json) ────────────────────
    async function cargarPlantillas() {
        try {
            const r = await fetch(YS.rutas.chats + "plantillas.json", { cache: "no-cache" });
            if (r.ok) est.plantillas = (await r.json()).plantillas || [];
        } catch { est.plantillas = []; }
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
            : `<div class="chats-plantillas-vacio">${est.plantillas.length ? "Ninguna respuesta coincide." : "No hay respuestas rápidas cargadas (plantillas.json)."}</div>`;
        lista.classList.remove("oculto");
        $("#chatsBtnPlantillas").setAttribute("aria-expanded", "true");
        lista.querySelectorAll(".chats-plantilla").forEach(b => b.addEventListener("mousedown", e => { e.preventDefault(); usarPlantilla(parseInt(b.dataset.i)); }));
        $("#chatsTexto").focus();
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
        let info, consentimiento = null;
        try { info = await YS.api(`${BASE}/conversaciones/${id}/paciente`); }
        catch (e) { if (gen === est.generacion) dibujarContexto(YS.errorCarga({ texto: e.message, reintentar: "YS.bandeja.recargarContexto()" })); return; }
        if (info.paciente) {
            try { consentimiento = await YS.api(`/pacientes/${info.paciente.idPaciente}/consentimiento-telemedicina`); } catch { }
        }
        if (gen !== est.generacion) return;
        const p = info.paciente;
        const dato = (etiqueta, valor) => `<div class="ui-dato"><span class="ui-dato-etiqueta">${YS.esc(etiqueta)}</span><span class="ui-dato-valor${valor ? "" : " vacio"}">${YS.esc(valor || "Sin cargar")}</span></div>`;
        const datoHtml = (etiqueta, html) => `<div class="ui-dato"><span class="ui-dato-etiqueta">${YS.esc(etiqueta)}</span><span class="ui-dato-valor">${html}</span></div>`;
        const CANAL_TEXTO = { Simulado: "Chat simulado" };
        const e = ESTADO[info.estado] || { texto: info.estado, chip: "neutral" };
        let html = `
            <div class="chats-contexto-persona">
                ${YS.avatar(p?.nombre, info.idConversacion, "lg")}
                <div><div class="chats-contexto-nombre">${YS.esc(p?.nombre || "Paciente sin identificar")}</div>
                     <span class="ui-chip ui-chip--${e.chip}">${YS.esc(e.texto)}</span></div>
            </div>`;
        if (!p) {
            html += `<div class="chats-contexto-bloque">${dato("Teléfono", formatearTelefono(info.telefono))}</div>
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
                        ${dato("ID de la conversación", String(info.idConversacion))}
                        ${dato("Creado", info.fechaCreacion ? `${YS.fecha.corta(info.fechaCreacion)} ${YS.fecha.hora(info.fechaCreacion)}` : "")}
                        ${dato("Origen", CANAL_TEXTO[info.canal] || info.canal || "")}
                        ${datoHtml("Estado", `<span class="ui-chip ui-chip--${e.chip}">${YS.esc(e.texto)}</span>`)}
                        ${datoHtml("Próximo turno", turnoHtml)}
                        ${datoHtml("Consentimiento", consentimientoHtml)}
                    </div>
                    ${consentimiento != null && !consentimiento.firmado ? `<p class="chats-contexto-nota">Sin firma no se pueden confirmar sus turnos (RN-02).</p>
                        <button class="ui-btn ui-btn--sm" type="button" id="chatsLinkFirma">Insertar link para firmar</button>` : ""}
                    <div class="chats-contexto-acciones">
                        <button class="ui-btn ui-btn--sm" type="button" id="chatsVerTurnos">${YS.icono("agenda")}Ver turnos del paciente</button>
                        <button class="ui-btn ui-btn--sm" type="button" id="chatsVerFicha">${YS.icono("usuario")}Ver ficha</button>
                    </div>
                </div>`;
        }
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
        $("#chatsVerFicha")?.addEventListener("click", () => irAlPanel(`usuarios?paciente=${p.idPaciente}`));
    }

    // Accesos al panel del configurador: si ya estamos ahí, cambia de sección sin recargar.
    function irAlPanel(hash) {
        if (location.pathname.toLowerCase() === YS.rutas.configurador.toLowerCase()) {
            location.hash = hash;
            if (est.expandido) expandir(false);
            cerrar();
        } else location.href = YS.rutas.configurador + "#" + hash;
    }

    // ── Avisos: contador, toast y sonido ────────────────────────
    function avisarNuevos(nuevos) {
        if ((est.abierto || est.modo === "pagina") && !document.hidden) return;   // ya lo ve en la lista
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

    // Se escucha en captura: si hay un modal o un menú abierto, Esc es para ellos y no cierra los chats.
    function teclasGlobales(e) {
        const visible = est.abierto || est.modo === "pagina";
        if (!raiz || !visible || document.querySelector(".ui-modal-fondo")) return;
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
        const viendo = est.abierto || est.modo === "pagina";
        const base = viendo ? 3000 : 20000, max = viendo ? 12000 : 45000;
        return Math.min(max, Math.round(base * Math.pow(1.4, Math.min(est.vueltasSinCambios, 8))));
    }

    function programar(ms) {
        clearTimeout(est.timer);
        const t = ms ?? intervalo();
        if (t != null) est.timer = setTimeout(pulso, t);
    }

    async function pulso() {
        if (est.pulsando) return;
        est.pulsando = true;
        clearTimeout(est.timer);
        try {
            // Cada minuto una vuelta completa (marcas de lectura de otros usuarios); el resto, solo lo nuevo.
            const completa = !est.cargada || Date.now() - est.ultimaCompleta > 60000;
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
            // alPulsarBoton: lo usa el configurador integrado (CRM > Chats es una sección propia,
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
        dibujarLista();
        pulso();
        instalarAvisosInternos();
    }

    // ── Avisos internos del módulo (campana del configurador) ───
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
        limpiarBusqueda() { $("#chatsBuscar").value = ""; est.busqueda = ""; dibujarLista(); $("#chatsBuscar").focus(); },
        recargarHilo() { if (est.actual) { const id = est.actual; est.actual = null; seleccionar(id); } },
        recargarContexto: () => cargarContexto(),
        /** Pide las novedades ya, sin esperar la próxima vuelta del polling. */
        actualizar() { est.vueltasSinCambios = 0; return pulso(); },
        get resumen() { return [...est.convs.values()]; }
    };
})();
