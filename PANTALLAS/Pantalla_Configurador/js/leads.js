// Administración › CRM › Leads (vista única): un lead es CUALQUIER conversación, con un paciente identificado o con alguien sin identificar. Tabla de SOLO
// LECTURA sobre GET /mensajeria/leads (no se crean ni se borran leads), con buscador, filtros por tipo, estado y etiqueta, y paginación en el servidor.
// Al hacer clic en una fila se abre esa conversación en Chats. Muestra lo mínimo para listar (nombre, teléfono, tipo, estado, etiquetas, última
// actividad): nunca el texto de los mensajes ni nada clínico. Se carga antes de dashboard.js, como usuarios.js.

const LEADS = { q: "", tipo: "todos", estado: "todos", etiqueta: "", pagina: 1, cantidad: 25, total: 0, filas: [], pedido: 0, etiquetas: new Map(), modo: "asistente" };

const LEADS_TIPOS = [
    { id: "todos", texto: "Todos" },
    { id: "paciente", texto: "Pacientes" },
    { id: "sin-identificar", texto: "Sin identificar" }
];
// Los mismos textos y colores que la bandeja de Chats (en modo "personal" lo que espera al equipo no es una "escalada").
const LEADS_ESTADO = {
    Activa: { texto: "Bot", chip: "marca" },
    DerivadaHumano: { texto: "Escalada a persona", chip: "warn" },
    Urgente: { texto: "Urgente", chip: "danger" },
    Cerrada: { texto: "Cerrada", chip: "neutral" }
};
const LEADS_ESTADO_PERSONAL = { DerivadaHumano: { texto: "Para atender", chip: "warn" } };
const LEADS_ESTADOS_FILTRO = ["Activa", "DerivadaHumano", "Urgente", "Cerrada"];

function leadsEstado(estado) {
    const t = (LEADS.modo === "personal" && LEADS_ESTADO_PERSONAL[estado]) || LEADS_ESTADO[estado] || { texto: estado, chip: "neutral" };
    return `<span class="ui-chip ui-chip--${t.chip}">${YS.esc(t.texto)}</span>`;
}

async function renderLeads(c) {
    if (YS.shell.actual !== "leads") return;
    const [caps, etiquetas] = await Promise.all([
        YS.shell.capacidades.catch(() => ({})),
        YS.api("/mensajeria/etiquetas").catch(() => [])
    ]);
    LEADS.modo = caps?.modoAtencion === "personal" ? "personal" : "asistente";
    LEADS.etiquetas = new Map(etiquetas.map(e => [e.idEtiqueta, e]));
    if (YS.shell.actual !== "leads") return;

    YS.shell.encabezado({ subtitulo: "Todas las conversaciones: pacientes y personas sin identificar" });
    YS.shell.acciones([{ texto: "Actualizar", icono: "refrescar", alClick: () => leadsCargar() }]);

    const buscador = YS.el("input", { class: "ui-input", type: "search", id: "leadsBuscar", value: LEADS.q,
        placeholder: "Buscar por nombre o teléfono", "aria-label": "Buscar leads" });
    buscador.addEventListener("input", YS.debounce(() => { LEADS.q = buscador.value.trim(); LEADS.pagina = 1; leadsCargar(); }, 300));

    const filtroTipo = YS.el("div", { class: "ui-filtros", role: "group", "aria-label": "Filtrar por tipo", id: "leadsTipos" });
    filtroTipo.addEventListener("click", e => {
        const b = e.target.closest("[data-tipo]");
        if (!b) return;
        LEADS.tipo = b.dataset.tipo; LEADS.pagina = 1; leadsPintarTipos(); leadsCargar();
    });

    const selectEstado = YS.el("select", { class: "ui-select", id: "leadsEstado", "aria-label": "Filtrar por estado" });
    selectEstado.innerHTML = `<option value="todos">Todos los estados</option>` + LEADS_ESTADOS_FILTRO.map(e =>
        `<option value="${e}">${YS.esc(((LEADS.modo === "personal" && LEADS_ESTADO_PERSONAL[e]) || LEADS_ESTADO[e]).texto)}</option>`).join("");
    selectEstado.value = LEADS.estado;
    selectEstado.addEventListener("change", () => { LEADS.estado = selectEstado.value; LEADS.pagina = 1; leadsCargar(); });

    const selectEtiqueta = YS.el("select", { class: "ui-select", id: "leadsEtiqueta", "aria-label": "Filtrar por etiqueta" });
    selectEtiqueta.innerHTML = `<option value="">Todas las etiquetas</option>` + etiquetas.map(e => `<option value="${e.idEtiqueta}">${YS.esc(e.nombre)}</option>`).join("");
    selectEtiqueta.value = LEADS.etiqueta;
    selectEtiqueta.addEventListener("change", () => { LEADS.etiqueta = selectEtiqueta.value; LEADS.pagina = 1; leadsCargar(); });

    c.innerHTML = "";
    const barra = YS.el("div", { class: "ui-barra" },
        YS.el("label", { class: "ui-buscador" }, YS.el("span", { html: YS.icono("buscar") }).firstChild, buscador),
        filtroTipo, selectEstado, ...(etiquetas.length ? [selectEtiqueta] : []),
        YS.el("span", { class: "ui-barra-espacio" }),
        YS.el("span", { class: "ui-barra-info", id: "leadsInfo", "aria-live": "polite" }));
    const panel = YS.el("div", { class: "ui-tabla-panel" });
    panel.innerHTML = `
        <div class="ui-tabla-scroll ui-tabla-scroll--alta" id="leadsScroll">
            <table class="ui-tabla ui-tabla--densa" id="leadsTabla" aria-busy="false">
                <thead><tr><th>Contacto</th><th>Tipo</th><th>Teléfono</th><th>Estado</th><th>Etiquetas</th><th>Última actividad</th></tr></thead>
                <tbody id="leadsCuerpo"></tbody>
            </table>
        </div>
        <div class="ui-paginacion" id="leadsPie"></div>`;
    c.append(barra, panel);
    panel.querySelector("#leadsPie").addEventListener("click", e => {
        const b = e.target.closest("[data-pag]");
        if (b && !b.disabled) { LEADS.pagina += parseInt(b.dataset.pag); leadsCargar(); document.getElementById("leadsScroll").scrollTop = 0; }
    });
    // Clic o Enter en una fila: abre esa conversación en Chats.
    const cuerpo = panel.querySelector("#leadsCuerpo");
    cuerpo.addEventListener("click", e => { const f = e.target.closest("[data-lead]"); if (f) leadsAbrir(parseInt(f.dataset.lead)); });
    cuerpo.addEventListener("keydown", e => { if (e.key === "Enter") { const f = e.target.closest("[data-lead]"); if (f) leadsAbrir(parseInt(f.dataset.lead)); } });

    leadsPintarTipos();
    await leadsCargar(true);
}

function leadsPintarTipos() {
    const cont = document.getElementById("leadsTipos");
    if (!cont) return;
    cont.innerHTML = LEADS_TIPOS.map(t => {
        const si = t.id === LEADS.tipo;
        return `<button type="button" class="ui-filtro${si ? " activo" : ""}" data-tipo="${t.id}" aria-pressed="${si}">${YS.esc(t.texto)}</button>`;
    }).join("");
}

// Si llegan dos respuestas desordenadas (tecleando rápido), solo vale la del último pedido.
async function leadsCargar(primera = false) {
    const mio = ++LEADS.pedido;
    const tabla = document.getElementById("leadsTabla");
    const cuerpo = document.getElementById("leadsCuerpo");
    if (!tabla || !cuerpo) return;
    tabla.setAttribute("aria-busy", "true");
    if (primera || !LEADS.filas.length) cuerpo.innerHTML = `<tr><td colspan="6">${YS.cargando()}</td></tr>`;
    else tabla.classList.add("usr-cargando");
    const consulta = new URLSearchParams({ tipo: LEADS.tipo, pagina: LEADS.pagina, cantidad: LEADS.cantidad });
    if (LEADS.estado !== "todos") consulta.set("estado", LEADS.estado);
    if (LEADS.etiqueta) consulta.set("idEtiqueta", LEADS.etiqueta);
    if (LEADS.q) consulta.set("q", LEADS.q);
    try {
        const res = await YS.fetch(`/mensajeria/leads?${consulta}`);
        if (mio !== LEADS.pedido) return;
        if (!res.ok) throw new Error(await YS.leerMensaje(res, "No se pudo cargar la lista de leads."));
        const d = await res.json();
        LEADS.filas = d.filas; LEADS.total = d.total; LEADS.pagina = d.pagina;
        leadsPintar(d);
    } catch (e) {
        if (mio !== LEADS.pedido) return;
        cuerpo.innerHTML = `<tr><td colspan="6">${YS.vacio({ icono: "alerta", titulo: "No se pudo cargar la lista", texto: e.message || "Probá de nuevo en unos segundos." })}</td></tr>`;
        document.getElementById("leadsPie")?.classList.add("oculto");
    } finally {
        if (mio === LEADS.pedido) { tabla.setAttribute("aria-busy", "false"); tabla.classList.remove("usr-cargando"); }
    }
}

function leadsPintar(d) {
    const cuerpo = document.getElementById("leadsCuerpo");
    document.getElementById("leadsInfo").textContent = `${d.total} ${d.total === 1 ? "lead" : "leads"}`;
    if (!d.filas.length) {
        const filtrado = LEADS.q || LEADS.tipo !== "todos" || LEADS.estado !== "todos" || LEADS.etiqueta;
        cuerpo.innerHTML = `<tr><td colspan="6">${YS.vacio(filtrado
            ? { icono: "buscar", titulo: "Sin resultados", texto: "Probá con otra búsqueda o sacá algún filtro." }
            : { icono: "chat", titulo: "Todavía no hay conversaciones" })}</td></tr>`;
    } else {
        cuerpo.innerHTML = d.filas.map(f => {
            const nombre = f.nombre || f.nombreContacto || "Sin nombre";
            const etiquetas = (f.idsEtiquetas || []).map(id => LEADS.etiquetas.get(id)).filter(Boolean)
                .map(e => `<span class="ui-chip ui-chip--${YS.esc(e.tono)}">${YS.esc(e.nombre)}</span>`).join(" ") || '<span class="ui-muted">—</span>';
            return `<tr data-lead="${f.idConversacion}" tabindex="0" class="usr-fila-clic" role="link" aria-label="Abrir la conversación de ${YS.esc(nombre)}">
                <td><span class="ui-flex">${YS.avatar(f.nombre || f.nombreContacto || null, f.idConversacion, "sm")}
                    <span><span class="principal">${YS.esc(nombre)}</span>${f.noLeidos ? ` <span class="ui-contador">${f.noLeidos}</span>` : ""}
                    ${f.nombre && f.nombreContacto && f.nombreContacto !== f.nombre ? `<span class="secundario">${YS.esc(f.nombreContacto)}</span>` : ""}</span></span></td>
                <td>${f.tipo === "paciente" ? '<span class="ui-chip ui-chip--sin-punto ui-chip--info">Paciente</span>' : '<span class="ui-chip ui-chip--sin-punto ui-chip--neutral">Sin identificar</span>'}</td>
                <td class="ui-num">${YS.esc(f.telefonoMostrar || f.telefono)}</td>
                <td>${leadsEstado(f.estado)}</td>
                <td>${etiquetas}</td>
                <td>${YS.esc(YS.fecha.hace(f.fechaUltimaActividad))}</td>
            </tr>`;
        }).join("");
    }
    const paginas = Math.max(1, d.paginas || 1);
    const desde = d.total ? (d.pagina - 1) * d.cantidad + 1 : 0;
    const pie = document.getElementById("leadsPie");
    pie.classList.toggle("oculto", d.total <= d.cantidad);
    pie.innerHTML = `<span>${d.total ? `${desde}–${Math.min(d.pagina * d.cantidad, d.total)} de ${d.total}` : ""}</span>
        <div class="ui-paginacion-botones">
            <button type="button" class="ui-btn ui-btn--sm" data-pag="-1"${d.pagina <= 1 ? " disabled" : ""}>Anterior</button>
            <span class="ui-paginacion-pagina">${d.pagina} / ${paginas}</span>
            <button type="button" class="ui-btn ui-btn--sm" data-pag="1"${d.pagina >= paginas ? " disabled" : ""}>Siguiente</button>
        </div>`;
}

// Abre la conversación en Chats (la bandeja se muestra y la selecciona). Si todavía no cargó, al menos lleva a Chats.
function leadsAbrir(idConversacion) {
    if (window.YS.bandeja?.abrir) YS.bandeja.abrir(idConversacion);
    else location.hash = "#chats";
}
