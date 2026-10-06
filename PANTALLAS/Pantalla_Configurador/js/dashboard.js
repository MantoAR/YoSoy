const API = YS.API;   // mismo origen que la página (shared/ui.js)

// Avisos y confirmaciones del sistema de diseño (shared/ui.js). Los botones de la
// confirmación conservan los ids confirmSi / confirmNo.
function toast(msg, tipo = "ok", duracion = 3500) {
    YS.toast(msg, tipo, duracion);
}

function confirmar(msg, opciones = {}) {
    return YS.confirmar({ mensaje: msg, idConfirmar: "confirmSi", idCancelar: "confirmNo", ...opciones });
}

// ── Modales fijos de dashboard.html (#modal… y #modalDeriv…) ──
// Se cierran con Esc, con la X, con Cancelar o haciendo clic afuera; el foco vuelve a
// donde estaba.
let focoAntesDelModal = null;

function abrirModalFijo(id, titulo, textoConfirmar) {
    focoAntesDelModal = document.activeElement;
    if (titulo) document.getElementById(id + "Titulo").textContent = titulo;
    if (textoConfirmar) document.getElementById(id + "Confirmar").textContent = textoConfirmar;
    document.getElementById(id + "Overlay").classList.add("visible");
    document.getElementById(id + "Panel").classList.add("visible");
    document.body.classList.add("ui-sin-scroll");
    setTimeout(() => {
        const panel = document.getElementById(id + "Panel");
        (panel.querySelector(".ui-modal-cuerpo select, .ui-modal-cuerpo input, .ui-modal-cuerpo textarea") || panel).focus();
    }, 30);
}

function cerrarModalFijo(id) {
    document.getElementById(id + "Overlay").classList.remove("visible");
    document.getElementById(id + "Panel").classList.remove("visible");
    document.body.classList.remove("ui-sin-scroll");
    if (focoAntesDelModal && document.contains(focoAntesDelModal)) focoAntesDelModal.focus();
    focoAntesDelModal = null;
}

document.addEventListener("keydown", e => {
    if (e.key !== "Escape") return;
    if (document.getElementById("modalOverlay")?.classList.contains("visible")) { e.preventDefault(); cerrarModal(); }
    else if (document.getElementById("modalDerivOverlay")?.classList.contains("visible")) { e.preventDefault(); cerrarModalDeriv(); }
});

const SOLO_LECTURA = ["evoluciones", "eventoscrisis"];

const campos = {
    turnos:          ["idPaciente", "idProfesional", "idEstadoTurno", "fecha", "hora", "modalidad", "motivo"],
    agendas:         ["idProfesional", "dia", "horaInicio", "horaFin", "disponible"],
    roles:           ["nombre", "estado"],
    especialidades:  ["nombre", "estado"],
    coberturas:      ["idPaciente", "idObraSocial", "nroAfiliado", "planCobertura", "estado"],
    interaccionesia: ["idPaciente", "fecha", "consulta", "respuesta"],
};

const idCampo = {
    turnos:          "idTurno",
    agendas:         "idAgenda",
    roles:           "idRol",
    especialidades:  "idEspecialidad",
    coberturas:      "idCobertura",
    interaccionesia: "idInteraccion",
};

const columnas = {
    turnos:          ["idTurno", "idPaciente", "idProfesional", "fecha", "hora", "modalidad"],
    agendas:         ["idAgenda", "idProfesional", "dia", "horaInicio", "horaFin", "disponible"],
    evoluciones:     ["idEvolucion", "idHistoria", "idProfesional", "fecha", "diagnostico"],
    eventoscrisis:   ["idEvento", "idPaciente", "fecha", "nivelRiesgo", "acciones"],
    roles:           ["idRol", "nombre", "estado"],
    especialidades:  ["idEspecialidad", "nombre", "estado"],
    coberturas:      ["idCobertura", "idPaciente", "nroAfiliado", "planCobertura", "estado"],
    interaccionesia: ["idInteraccion", "idPaciente", "fecha", "consulta"],
};

const titulos = {
    usuarios:        "Usuarios",
    solicitudes:     "Solicitudes de Registro",
    derivaciones:    "Solicitudes de Cambio de Profesional",
    turnos:          "Turnos",
    agendas:         "Agenda",
    evoluciones:     "Evoluciones",
    eventoscrisis:   "Eventos de Crisis",
    roles:           "Roles",
    especialidades:  "Especialidades",
    coberturas:      "Coberturas",
    interaccionesia: "Interacciones IA",
    auditoria:       "Log de Auditoría",
    recuperacion:    "Recuperación de Contraseña",
};

let tabActual = null;
let datosActuales = [];
let filaEnEdicion = null;

// ── NAVEGACIÓN ───────────────────────────────────────────────
// Cada sección del menú superior (shared/shell.js) se dibuja en #contenido.
const SECCIONES = ["inicio", "chats", "escalamientos", "usuarios", "solicitudes", "derivaciones", "recuperacion", "auditoria", "admisiones",
    "reportes", "turnos", "agendas", "evoluciones", "eventoscrisis", "roles", "especialidades", "coberturas", "interaccionesia"];

// La bandeja (js/bandeja.js) la carga shared/shell.js solo si la mensajería está disponible, en
// paralelo con el resto del arranque: si se navega a Chats o Escalamientos antes de que termine
// de cargar, se espera un toque en vez de mostrar la sección vacía.
async function esperarBandeja() {
    for (let i = 0; i < 40 && !window.YS.bandeja; i++) await new Promise(r => setTimeout(r, 50));
    return !!window.YS.bandeja;
}

// Las tarjetas de "Chats sin leer" / "Chats escalados" del Inicio navegan a Chats o
// Escalamientos ya con ese filtro aplicado (antes abrían el cajón; ahora es una sección propia).
function irAChatsFiltrados(filtro) {
    YS.bandeja?.filtrar(filtro);
    location.hash = filtro === "derivadas" ? "#escalamientos" : "#chats";
}

async function mostrarTab(tab, params = {}) {
    if (!SECCIONES.includes(tab)) tab = "inicio";
    YS.shell.marcar(tab);

    const vistaChats = document.getElementById("vistaChatsCRM");
    if (tab === "chats" || tab === "escalamientos") {
        document.getElementById("contenido").classList.add("oculto");
        vistaChats.classList.remove("oculto");
        if (!(await esperarBandeja())) { vistaChats.innerHTML = errorHtml(tab); return; }
        if (tab === "escalamientos") YS.bandeja.filtrar("derivadas");
        return;
    }
    vistaChats.classList.add("oculto");
    document.getElementById("contenido").classList.remove("oculto");

    if (tab === 'inicio') { inicioConfigurador(); return; }
    if (tab === 'reportes') { reportes(); return; }
    if (tab === 'admisiones') { admisiones(); return; }
    tabActual = tab;

    const contenido = document.getElementById("contenido");
    contenido.innerHTML = YS.cargando();

    try {
        if (tab === "usuarios")     return await renderUsuarios(contenido, params);
        if (tab === "solicitudes")  return await renderSolicitudes(contenido);
        if (tab === "derivaciones") return await renderDerivaciones(contenido);
        if (tab === "auditoria")    return await renderAuditoria(contenido);
        if (tab === "recuperacion") return await renderRecuperacion(contenido);

        if (tab === "turnos") return await renderTurnos(contenido, params);
        const datos = await YS.fetch(`/${tab}`).then(r => r.json());
        datosActuales = datos;
        await renderTabla(titulos[tab], columnas[tab], datos, tab, contenido);

    } catch {
        contenido.innerHTML = errorHtml(tab);
    }
}

// ── USUARIOS ─────────────────────────────────────────────────
async function renderUsuarios(c, params = {}) {
    const [pacientes, profesionales] = await Promise.all([
        YS.fetch(`/pacientes`).then(r => r.json()),
        YS.fetch(`/profesionales`).then(r => r.json()),
    ]);

    const pacProfs = {};
    const profPacs = {};
    await Promise.all(pacientes.map(async p => {
        const profs = await YS.fetch(`/pacientes/${p.idPaciente}/mis-profesionales`).then(r => r.json()).catch(() => []);
        pacProfs[p.idPaciente] = new Set(profs.map(pr => pr.idProfesional));
        profs.forEach(pr => {
            if (!profPacs[pr.idProfesional]) profPacs[pr.idProfesional] = new Set();
            profPacs[pr.idProfesional].add(p.idPaciente);
        });
    }));

    window._pacProfs = pacProfs;
    window._profPacs = profPacs;
    window._pacientes = pacientes;
    window._profesionales = profesionales;
    window._mostrarDadosDeBaja = false;
    window._filtroUsuarios = "";

    if (YS.shell.actual !== "usuarios") return;
    const buscador = YS.el("input", { class: "ui-input", type: "search", id: "buscarUsuarios",
        placeholder: "Buscar por nombre, DNI o matrícula", "aria-label": "Buscar pacientes y profesionales" });
    buscador.addEventListener("input", YS.debounce(() => { window._filtroUsuarios = YS.normalizar(buscador.value); renderCardsUsuarios(); }, 120));
    YS.shell.acciones([
        YS.el("label", { class: "ui-buscador" }, YS.el("span", { html: YS.icono("buscar") }).firstChild, buscador),
        YS.el("label", { class: "ui-check" },
            YS.el("input", { type: "checkbox", id: "chkDadosDeBaja", on: { change: toggleDadosDeBaja } }), "Mostrar dados de baja")
    ]);
    c.innerHTML = `
        <div class="split-view">
            <section class="split-col" aria-labelledby="tituloPacientes">
                <div class="split-header"><span id="tituloPacientes">Pacientes</span> <span class="split-badge" id="badgePacientes"></span></div>
                <div class="split-list" id="listaPacientes"></div>
            </section>
            <section class="split-col" aria-labelledby="tituloProfesionales">
                <div class="split-header"><span id="tituloProfesionales">Profesionales</span> <span class="split-badge" id="badgeProfesionales"></span></div>
                <div class="split-list" id="listaProfesionales"></div>
            </section>
        </div>
        <div class="split-info" id="splitInfo" aria-live="polite"></div>`;

    renderCardsUsuarios();

    // Desde los chats: "Ver ficha" llega con #usuarios?paciente=N
    const idPac = parseInt(params.paciente);
    if (idPac && document.getElementById(`pac-${idPac}`)) {
        seleccionarPaciente(idPac);
        document.getElementById(`pac-${idPac}`).scrollIntoView({ block: "center" });
    }
}

function renderCardsUsuarios() {
    const mostrarBaja = window._mostrarDadosDeBaja;
    const q = window._filtroUsuarios || "";
    const coincide = (...v) => !q || YS.normalizar(v.join(" ")).includes(q);
    const deLaLista = p => mostrarBaja ? p.estado === "Inactivo" : p.estado !== "Inactivo";
    const pacientes = window._pacientes.filter(p => deLaLista(p) && coincide(p.nombre, p.dni));
    const profesionales = window._profesionales.filter(p => deLaLista(p) && coincide(YS.nombreCompleto(p), p.matricula, p.dni, p.especialidad));

    document.getElementById("badgePacientes").textContent = pacientes.length;
    document.getElementById("badgeProfesionales").textContent = profesionales.length;

    // El estado se muestra solo si no es el esperado para la lista (p. ej. un profesional pendiente)
    const estadoExtra = p => p.estado && p.estado !== "Activo" && p.estado !== "Inactivo" ? " · " + YS.esc(p.estado) : "";
    const tarjeta = (id, nombre, sub, semilla, alClick) => `
        <button type="button" class="usr-card${mostrarBaja ? " usr-card-inactivo" : ""}" id="${id}" onclick="${alClick}">
            ${YS.avatar(nombre || "?", semilla, "sm")}
            <span class="usr-textos"><span class="usr-nombre">${YS.esc(nombre || "-")}</span><span class="usr-sub">${sub}</span></span>
        </button>`;
    const vacio = (singular, plural) => `<div class="split-vacio">${q
        ? `Ningún ${singular} coincide con la búsqueda.`
        : mostrarBaja ? `No hay ${plural} dados de baja.` : `No hay ${plural} activos.`}</div>`;

    document.getElementById("listaPacientes").innerHTML = pacientes.map(p =>
        tarjeta(`pac-${p.idPaciente}`, p.nombre, `DNI ${YS.esc(p.dni ?? "-")}${estadoExtra(p)}`, p.idPaciente, `seleccionarPaciente(${p.idPaciente})`)
    ).join("") || vacio("paciente", "pacientes");
    document.getElementById("listaProfesionales").innerHTML = profesionales.map(p =>
        tarjeta(`prof-${p.idProfesional}`, YS.nombreCompleto(p),
            `Mat. ${YS.esc(p.matricula ?? "-")}${p.especialidad ? " · " + YS.esc(p.especialidad) : ""}${estadoExtra(p)}`,
            "p" + p.idProfesional, `seleccionarProfesional(${p.idProfesional})`)
    ).join("") || vacio("profesional", "profesionales");

    // Si lo elegido sigue en la lista (p. ej. después de buscar) se mantiene la selección
    if (window._pacSeleccionado && document.getElementById(`pac-${window._pacSeleccionado}`)) seleccionarPaciente(window._pacSeleccionado);
    else if (window._profSeleccionado && document.getElementById(`prof-${window._profSeleccionado}`)) seleccionarProfesional(window._profSeleccionado);
    else document.getElementById("splitInfo").innerHTML =
        '<span class="ui-muted">Elegí un paciente o un profesional para ver con quién está relacionado y sus acciones.</span>';
}

function toggleDadosDeBaja() {
    window._mostrarDadosDeBaja = document.getElementById("chkDadosDeBaja").checked;
    window._pacSeleccionado = null;
    window._profSeleccionado = null;
    renderCardsUsuarios();
}

// Marca la tarjeta elegida y, en la otra columna, las relacionadas
function marcarTarjetas(idElegida, otraLista, prefijo, relacionados) {
    document.querySelectorAll(".usr-card").forEach(el => { el.classList.remove("seleccionado", "relacionado"); el.removeAttribute("aria-pressed"); });
    const elegida = document.getElementById(idElegida);
    elegida?.classList.add("seleccionado");
    elegida?.setAttribute("aria-pressed", "true");
    document.querySelectorAll(`${otraLista} .usr-card`).forEach(el => {
        if (relacionados.has(parseInt(el.id.replace(prefijo, "")))) el.classList.add("relacionado");
    });
}

function seleccionarPaciente(idPac) {
    window._pacSeleccionado = idPac;
    window._profSeleccionado = null;
    const relacionados = window._pacProfs[idPac] ?? new Set();
    marcarTarjetas(`pac-${idPac}`, "#listaProfesionales", "prof-", relacionados);

    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    const nombres = [...relacionados].map(idP => YS.nombreCompleto(window._profesionales.find(p => p.idProfesional === idP)) || `#${idP}`);
    const inactivo = pac?.estado === "Inactivo";
    const botones = [
        !inactivo && relacionados.size ? `<button class="ui-btn ui-btn--sm" onclick="abrirModalCambioDirecto(${idPac})">Cambiar profesional</button>` : "",
        !inactivo ? `<button class="ui-btn ui-btn--sm" onclick="abrirModalAsignar(${idPac})">Asignar profesional</button>` : "",
        inactivo
            ? `<button class="ui-btn ui-btn--sm ui-btn--ok" onclick="darDeAltaPaciente(${idPac})">Dar de alta</button>`
            : `<button class="ui-btn ui-btn--sm ui-btn--danger" onclick="darDeBajaPaciente(${idPac})">Dar de baja</button>`
    ].join("");
    document.getElementById("splitInfo").innerHTML = `
        <div class="split-info-texto"><strong>${YS.esc(pac?.nombre)}</strong> ${relacionados.size
            ? `tiene asignado${relacionados.size === 1 ? "" : "s"}: ${YS.esc(nombres.join(", "))}.`
            : "no tiene profesionales asignados."}</div>
        <div class="split-info-acciones">${botones}</div>`;
}

function seleccionarProfesional(idProf) {
    window._profSeleccionado = idProf;
    window._pacSeleccionado = null;
    const relacionados = window._profPacs[idProf] ?? new Set();
    marcarTarjetas(`prof-${idProf}`, "#listaPacientes", "pac-", relacionados);

    const prof = window._profesionales.find(p => p.idProfesional === idProf);
    const nombres = [...relacionados].map(idPac => window._pacientes.find(p => p.idPaciente === idPac)?.nombre ?? `#${idPac}`);
    const boton = prof?.estado === "Inactivo"
        ? `<button class="ui-btn ui-btn--sm ui-btn--ok" onclick="darDeAltaProfesional(${idProf})">Dar de alta</button>`
        : `<button class="ui-btn ui-btn--sm ui-btn--danger" onclick="darDeBajaProfesional(${idProf})">Dar de baja</button>`;
    document.getElementById("splitInfo").innerHTML = `
        <div class="split-info-texto"><strong>${YS.esc(YS.nombreCompleto(prof))}</strong> ${relacionados.size
            ? `atiende a: ${YS.esc(nombres.join(", "))}.`
            : "no tiene pacientes asignados."}</div>
        <div class="split-info-acciones">${boton}</div>`;
}

async function cambiarEstadoUsuario({ url, metodo, pregunta, titulo, textoConfirmar, peligro, ok, error }) {
    if (!await confirmar(pregunta, { titulo, textoConfirmar, peligro })) return;
    try {
        const res = await YS.fetch(url, { method: metodo, headers: auditorHeaders() });
        if (res.ok || res.status === 204) { toast(ok); mostrarTab("usuarios"); }
        else toast(await YS.leerMensaje(res, error), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

function darDeBajaPaciente(idPac) {
    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    return cambiarEstadoUsuario({ url: `${API}/pacientes/${idPac}`, metodo: "DELETE",
        pregunta: `¿Dar de baja a ${pac?.nombre ?? "este paciente"}?`, titulo: "Dar de baja", textoConfirmar: "Dar de baja", peligro: true,
        ok: "Paciente dado de baja.", error: "No se pudo dar de baja al paciente." });
}

function darDeAltaPaciente(idPac) {
    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    return cambiarEstadoUsuario({ url: `${API}/pacientes/${idPac}/alta`, metodo: "PUT",
        pregunta: `¿Dar de alta a ${pac?.nombre ?? "este paciente"}?`, titulo: "Dar de alta", textoConfirmar: "Dar de alta",
        ok: "Paciente dado de alta.", error: "No se pudo dar de alta al paciente." });
}

function darDeBajaProfesional(idProf) {
    const nombre = YS.nombreCompleto(window._profesionales.find(p => p.idProfesional === idProf)) || "este profesional";
    return cambiarEstadoUsuario({ url: `${API}/profesionales/${idProf}`, metodo: "DELETE",
        pregunta: `¿Dar de baja a ${nombre}?`, titulo: "Dar de baja", textoConfirmar: "Dar de baja", peligro: true,
        ok: "Profesional dado de baja.", error: "No se pudo dar de baja al profesional." });
}

function darDeAltaProfesional(idProf) {
    const nombre = YS.nombreCompleto(window._profesionales.find(p => p.idProfesional === idProf)) || "este profesional";
    return cambiarEstadoUsuario({ url: `${API}/profesionales/${idProf}/alta`, metodo: "PUT",
        pregunta: `¿Dar de alta a ${nombre}?`, titulo: "Dar de alta", textoConfirmar: "Dar de alta",
        ok: "Profesional dado de alta.", error: "No se pudo dar de alta al profesional." });
}

// ── SOLICITUDES DE REGISTRO ──────────────────────────────────
async function renderSolicitudes(c) {
    const datos = await YS.fetch(`/profesionales/solicitudes`).then(r => r.json());
    if (!encabezadoListado("solicitudes", datos.length, "pendientes")) return;

    YS.tabla(c, {
        filas: datos, unidad: ["solicitud", "solicitudes"], placeholder: "Buscar por nombre, DNI, matrícula o mail",
        buscar: p => [p.nombre, p.apellido, p.dni, p.matricula, p.mail, p.provincia].join(" "),
        ordenInicial: { col: 4, desc: true },
        vacio: { icono: "usuario-mas", titulo: "No hay solicitudes pendientes", texto: "Cuando un profesional pida su cuenta desde el registro, aparece acá." },
        columnas: [
            { titulo: "Profesional", orden: p => YS.normalizar(YS.nombreCompleto(p)),
              html: p => `<span class="ui-flex">${YS.avatar(YS.nombreCompleto(p), p.idProfesional, "sm")}<span><span class="principal">${YS.esc(YS.nombreCompleto(p))}</span><span class="secundario">${YS.esc(p.mail || "")}</span></span></span>` },
            { titulo: "DNI", html: p => celda(p.dni), orden: p => p.dni || "" },
            { titulo: "Matrícula", html: p => celda(p.matricula), orden: p => p.matricula || "" },
            { titulo: "Provincia", html: p => celda(p.provincia), orden: p => p.provincia || "" },
            { titulo: "Pedida", html: p => fechaHoraCelda(p.fechaSolicitud), orden: p => p.fechaSolicitud || "" },
            { titulo: "", clase: "acciones", html: p => `
                <button class="ui-btn ui-btn--sm ui-btn--ok" onclick="aprobarSolicitud(${p.idProfesional})">Aprobar</button>
                <button class="ui-btn ui-btn--sm ui-btn--danger" onclick="rechazarSolicitud(${p.idProfesional})">Rechazar</button>` }
        ]
    });
}

// ── Celdas comunes de los listados ───────────────────────────
function celda(v) {
    return v === null || v === undefined || v === "" ? '<span class="ui-muted">—</span>' : YS.esc(v);
}
// Textos largos (motivo, detalle…): se cortan a dos líneas y el texto completo queda en el title
function celdaLarga(v) {
    return `<span class="celda-larga" title="${YS.esc(v ?? "")}">${celda(v)}</span>`;
}
function fechaHoraCelda(v) {
    return v ? `<span class="ui-num">${YS.esc(new Date(v).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "short" }))}</span>` : celda(null);
}
function chipEstadoCuenta(estado) {
    const tono = { Activo: "ok", Activa: "ok", Inactivo: "neutral", Pendiente: "warn", Rechazado: "danger" }[estado] || "neutral";
    return estado ? `<span class="ui-chip ui-chip--${tono}">${YS.esc(estado)}</span>` : celda(null);
}

// Nombres de pacientes y profesionales para mostrar en lugar de los ids
async function cargarNombres() {
    const [pacientes, profesionales] = await Promise.all([
        YS.api("/pacientes").catch(() => []), YS.api("/profesionales").catch(() => [])
    ]);
    const pac = new Map(pacientes.map(p => [p.idPaciente, YS.nombreCompleto(p)]));
    const pro = new Map(profesionales.map(p => [p.idProfesional, YS.nombreCompleto(p)]));
    return {
        profesionales,
        paciente: id => pac.get(id) || (id ? `Paciente #${id}` : "—"),
        profesional: id => pro.get(id) || (id ? `Profesional #${id}` : "—")
    };
}

function auditorHeaders() {
    return { "Content-Type": "application/json", "X-Usuario-Id": localStorage.getItem("idUsuario") ?? "0" };
}

async function aprobarSolicitud(id) {
    if (!await confirmar("¿Aprobar esta solicitud? Al profesional le llega un mail para crear su usuario.")) return;
    const res = await YS.fetch(`/profesionales/${id}/aprobar`, { method: "PUT", headers: auditorHeaders() });
    if (res.ok) toast("Solicitud aprobada.");
    else toast(await YS.leerMensaje(res, "No se pudo aprobar la solicitud."), "error");
    mostrarTab("solicitudes");
}

async function rechazarSolicitud(id) {
    if (!await confirmar("¿Rechazar esta solicitud de registro?", { titulo: "Rechazar solicitud", textoConfirmar: "Rechazar", peligro: true })) return;
    const res = await YS.fetch(`/profesionales/${id}/rechazar`, { method: "PUT", headers: auditorHeaders() });
    if (res.ok) toast("Solicitud rechazada.", "warn");
    else toast(await YS.leerMensaje(res, "No se pudo rechazar la solicitud."), "error");
    mostrarTab("solicitudes");
}

// ── SOLICITUDES DE CAMBIO ────────────────────────────────────
async function renderDerivaciones(c) {
    const datos = await YS.fetch(`/derivaciones/detalle`).then(r => r.json());
    window._derivaciones = datos;
    if (!encabezadoListado("derivaciones", datos.length, "solicitudes")) return;
    const tono = { Pendiente: "warn", Aceptada: "ok", Rechazada: "danger" };

    YS.tabla(c, {
        filas: datos, unidad: ["solicitud", "solicitudes"], placeholder: "Buscar por paciente, profesional o motivo",
        buscar: d => [d.nombrePaciente, d.nombreProfesionalOrigen, d.nombreProfesionalDestino, d.especialidad, d.motivo].join(" "),
        filtros: [
            { id: "pendientes", texto: "Pendientes", pasa: d => d.estado === "Pendiente" },
            { id: "todas", texto: "Todas", pasa: () => true },
            { id: "aceptadas", texto: "Aceptadas", pasa: d => d.estado === "Aceptada" },
            { id: "rechazadas", texto: "Rechazadas", pasa: d => d.estado === "Rechazada" }
        ],
        ordenInicial: { col: 4, desc: true },
        vacio: { icono: "flechas", titulo: "No hay solicitudes de cambio", texto: "Las piden los pacientes desde Mis profesionales." },
        columnas: [
            { titulo: "Paciente", orden: d => YS.normalizar(d.nombrePaciente),
              html: d => `<span class="ui-flex">${YS.avatar(d.nombrePaciente, d.idPaciente, "sm")}<span class="principal">${YS.esc(d.nombrePaciente)}</span></span>` },
            { titulo: "Profesional actual", html: d => celda(d.nombreProfesionalOrigen), orden: d => d.nombreProfesionalOrigen || "" },
            { titulo: "Especialidad", html: d => celda(d.especialidad), orden: d => d.especialidad || "" },
            { titulo: "Motivo", html: d => celdaLarga(d.motivo) },
            { titulo: "Fecha", html: d => `<span class="ui-num">${YS.esc(YS.fecha.corta(d.fecha))}</span>`, orden: d => d.fecha || "" },
            { titulo: "Estado", html: d => `<span class="ui-chip ui-chip--${tono[d.estado] || "neutral"}">${YS.esc(d.estado)}</span>`, orden: d => d.estado },
            { titulo: "", clase: "acciones", html: d => d.estado === "Pendiente" ? `
                <button class="ui-btn ui-btn--sm ui-btn--ok" onclick="abrirModalDeriv(${d.idDerivacion}, ${d.idEspecialidad}, ${d.idProfesionalOrigen})">Aceptar</button>
                <button class="ui-btn ui-btn--sm ui-btn--danger" onclick="rechazarDerivacion(${d.idDerivacion})">Rechazar</button>`
                : `<span class="ui-muted texto-chico">${d.estado === "Aceptada" ? "Asignado: " + YS.esc(d.nombreProfesionalDestino) : ""}</span>` }
        ]
    });
}

// ── ASIGNAR, CAMBIAR Y ACEPTAR SOLICITUD DE CAMBIO ──────────
// Las tres acciones usan el mismo modal de dashboard.html (#modalDeriv…).
function campoSelect(id, etiqueta, opciones, extra = "") {
    return `<div class="ui-campo"><label for="${id}">${etiqueta}</label><select id="${id}" class="ui-select"${extra}>${opciones}</select></div>`;
}

const opcionesConElegir = (opciones, sinOpciones) =>
    opciones ? '<option value="">Elegí un profesional</option>' + opciones : `<option value="">${sinOpciones}</option>`;

async function abrirModalAsignar(idPac) {
    window._cambioPaciente = idPac;
    window._modoDirecto = false;
    window._modoAsignar = true;
    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    const yaAsignados = window._pacProfs[idPac] ?? new Set();

    const opciones = window._profesionales
        .filter(p => p.estado === "Activo" && !yaAsignados.has(p.idProfesional))
        .map(p => `<option value="${p.idProfesional}">${YS.esc(YS.nombreCompleto(p))}${p.especialidad ? " — " + YS.esc(p.especialidad) : ""}</option>`)
        .join("");

    document.getElementById("modalDerivBody").innerHTML = `
        <div class="modal-campos">
            <p class="modal-texto">Paciente: <strong>${YS.esc(pac?.nombre)}</strong></p>
            ${campoSelect("selectProfAsignar", "Profesional", opcionesConElegir(opciones, "Todos los profesionales activos ya están asignados"))}
        </div>`;
    abrirModalFijo("modalDeriv", "Asignar profesional", "Asignar");
}

async function abrirModalCambioDirecto(idPac) {
    window._cambioPaciente = idPac;
    window._modoAsignar = false;
    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    const relacionados = [...(window._pacProfs[idPac] ?? new Set())];

    const opcionesOrigen = relacionados.map(idP => {
        const pro = window._profesionales.find(p => p.idProfesional === idP);
        return `<option value="${idP}" data-esp="${pro?.idEspecialidad ?? 0}">${YS.esc(YS.nombreCompleto(pro) || "#" + idP)}</option>`;
    }).join("");

    document.getElementById("modalDerivBody").innerHTML = `
        <div class="modal-campos">
            <p class="modal-texto">Paciente: <strong>${YS.esc(pac?.nombre)}</strong></p>
            ${campoSelect("selectProfOrigen", "Profesional actual", opcionesOrigen, ' onchange="cargarProfesionalesDestino()"')}
            ${campoSelect("selectProfDestino", "Nuevo profesional", '<option value="">Cargando…</option>')}
            <div class="ui-campo">
                <label for="motivoCambioDir">Motivo <span class="ui-muted">(opcional)</span></label>
                <textarea id="motivoCambioDir" rows="2" class="ui-textarea"></textarea>
            </div>
        </div>`;

    window._derivIdActual = null;
    window._modoDirecto = true;
    abrirModalFijo("modalDeriv", "Cambiar de profesional", "Hacer el cambio");

    await cargarProfesionalesDestino();
}

async function cargarProfesionalesDestino() {
    const sel = document.getElementById("selectProfOrigen");
    const destino = document.getElementById("selectProfDestino");
    const idEsp = parseInt(sel.options[sel.selectedIndex]?.dataset.esp ?? 0);
    const idOrigen = parseInt(sel.value);

    if (!idEsp) { destino.innerHTML = '<option value="">El profesional actual no tiene especialidad cargada</option>'; return; }
    try {
        const profs = await YS.api(`/profesionales/activos/especialidad/${idEsp}`);
        const opciones = profs
            .filter(p => p.idProfesional !== idOrigen)
            .map(p => `<option value="${p.idProfesional}">${YS.esc(YS.nombreCompleto(p))}</option>`)
            .join("");
        destino.innerHTML = opcionesConElegir(opciones, "No hay otros profesionales de esa especialidad");
    } catch {
        destino.innerHTML = '<option value="">No se pudo cargar la lista</option>';
    }
}

let _derivIdActual = null;

async function abrirModalDeriv(idDeriv, idEsp, idProfOrigen) {
    window._modoDirecto = false;
    window._modoAsignar = false;
    _derivIdActual = idDeriv;

    let profs;
    try { profs = await YS.api(`/profesionales/activos/especialidad/${idEsp}`); }
    catch (e) { toast(e.message || "No se pudo cargar la lista de profesionales.", "error"); return; }
    const opciones = profs
        .filter(p => p.idProfesional !== idProfOrigen)
        .map(p => `<option value="${p.idProfesional}">${YS.esc(YS.nombreCompleto(p))} — Mat. ${YS.esc(p.matricula ?? "-")}</option>`)
        .join("");
    const d = (window._derivaciones || []).find(x => x.idDerivacion === idDeriv);

    document.getElementById("modalDerivBody").innerHTML = `
        <div class="modal-campos">
            ${d ? `<p class="modal-texto">Paciente: <strong>${YS.esc(d.nombrePaciente)}</strong> · ahora con ${YS.esc(d.nombreProfesionalOrigen || "-")}
                ${d.motivo ? `<span class="modal-motivo">Motivo: ${YS.esc(d.motivo)}</span>` : ""}</p>` : ""}
            ${campoSelect("selectProfDeriv", "Nuevo profesional", opcionesConElegir(opciones, "No hay otros profesionales de esa especialidad"))}
        </div>`;
    abrirModalFijo("modalDeriv", "Aceptar solicitud de cambio", "Aceptar y asignar");
}

function cerrarModalDeriv() {
    cerrarModalFijo("modalDeriv");
    _derivIdActual = null;
    window._modoDirecto = false;
    window._modoAsignar = false;
}

async function confirmarAceptarDeriv() {
    let pedido;
    if (window._modoAsignar) {
        const idProf = parseInt(document.getElementById("selectProfAsignar").value);
        if (!idProf) { YS.form.marcar("selectProfAsignar", "Elegí un profesional."); return; }
        pedido = { url: `${API}/pacientes/${window._cambioPaciente}/profesionales`, method: "POST", body: { idProfesional: idProf },
                   ok: "Profesional asignado.", error: "No se pudo asignar el profesional.", volver: "usuarios" };
    } else if (window._modoDirecto) {
        const idOrigen  = parseInt(document.getElementById("selectProfOrigen").value);
        const idDestino = parseInt(document.getElementById("selectProfDestino").value);
        const motivo    = document.getElementById("motivoCambioDir").value.trim() || null;
        if (!idDestino) { YS.form.marcar("selectProfDestino", "Elegí el nuevo profesional."); return; }
        pedido = { url: `${API}/derivaciones/cambio-directo`, method: "POST",
                   body: { idPaciente: window._cambioPaciente, idProfesionalOrigen: idOrigen, idProfesionalDestino: idDestino, motivo },
                   ok: "Cambio hecho. El nuevo profesional quedó asignado.", error: "No se pudo hacer el cambio.", volver: "usuarios" };
    } else {
        const idProf = parseInt(document.getElementById("selectProfDeriv").value);
        if (!idProf) { YS.form.marcar("selectProfDeriv", "Elegí un profesional."); return; }
        pedido = { url: `${API}/derivaciones/${_derivIdActual}/aceptar`, method: "PUT", body: { idProfesionalDestino: idProf },
                   ok: "Solicitud aceptada. El nuevo profesional quedó asignado.", error: "No se pudo aceptar la solicitud.", volver: "derivaciones" };
    }

    // Si falla, el modal queda abierto para reintentar o cancelar
    const boton = document.getElementById("modalDerivConfirmar");
    boton.disabled = true;
    try {
        const res = await YS.fetch(pedido.url, { method: pedido.method, headers: auditorHeaders(), body: JSON.stringify(pedido.body) });
        if (!res.ok) { toast(await YS.leerMensaje(res, pedido.error), "error"); return; }
        cerrarModalDeriv();
        toast(pedido.ok);
        mostrarTab(pedido.volver);
    } catch {
        toast("No se pudo conectar con el servidor.", "error");
    } finally {
        boton.disabled = false;
    }
}

async function rechazarDerivacion(id) {
    if (!await confirmar("¿Rechazar esta solicitud de cambio de profesional?", { titulo: "Rechazar solicitud", textoConfirmar: "Rechazar", peligro: true })) return;
    const res = await YS.fetch(`/derivaciones/${id}/rechazar`, { method: "PUT", headers: auditorHeaders() });
    if (res.ok) toast("Solicitud rechazada.", "warn");
    else toast(await YS.leerMensaje(res, "No se pudo rechazar la solicitud."), "error");
    mostrarTab("derivaciones");
}

// ── TABLA GENÉRICA ───────────────────────────────────────────
const ETIQUETAS = {
    idPaciente: "Paciente", idProfesional: "Profesional", idHistoria: "Historia", fecha: "Fecha", hora: "Hora",
    modalidad: "Modalidad", dia: "Día", horaInicio: "Desde", horaFin: "Hasta", disponible: "Disponible",
    diagnostico: "Diagnóstico", nivelRiesgo: "Nivel de riesgo", acciones: "Acciones tomadas", nombre: "Nombre",
    estado: "Estado", nroAfiliado: "N.º de afiliado", planCobertura: "Plan", consulta: "Consulta"
};

function valorCelda(clave, valor, nombres) {
    if (clave === "idPaciente") return `<span class="principal">${YS.esc(nombres.paciente(valor))}</span>`;
    if (clave === "idProfesional") return `<span class="principal">${YS.esc(nombres.profesional(valor))}</span>`;
    if (clave === "fecha") return valor ? `<span class="ui-num">${YS.esc(YS.fecha.corta(valor))}</span>` : celda(null);
    if (clave === "hora" || clave === "horaInicio" || clave === "horaFin") return `<span class="ui-num">${YS.esc(YS.fecha.horaTexto(valor))}</span>`;
    if (clave === "disponible") return valor ? '<span class="ui-chip ui-chip--ok">Sí</span>' : '<span class="ui-chip ui-chip--neutral">No</span>';
    if (clave === "estado") return chipEstadoCuenta(valor);
    if (clave === "dia") return celda(YS.dias.nombre(valor));
    if (clave === idCampo[tabActual] || /^id[A-Z]/.test(clave)) return `<span class="ui-muted ui-num">#${YS.esc(valor)}</span>`;
    return celdaLarga(valor);
}

async function renderTabla(titulo, cols, datos, tab, contenido) {
    const editable = !SOLO_LECTURA.includes(tab);
    const conNombres = cols.some(c => c === "idPaciente" || c === "idProfesional");
    const nombres = conNombres ? await cargarNombres() : { paciente: id => `#${id}`, profesional: id => `#${id}` };
    window._nombres = conNombres ? nombres : null;
    if (!encabezadoListado(tab, datos?.length ?? 0, "registros", SOLO_LECTURA.includes(tab) ? "Solo lectura" : "")) return;

    const columnasTabla = cols.map(c => ({
        titulo: c === idCampo[tab] || /^id[A-Z]\w*$/.test(c) && !ETIQUETAS[c] ? "ID" : (ETIQUETAS[c] || c),
        html: row => valorCelda(c, row[c], nombres),
        orden: row => {
            const v = row[c];
            if (c === "idPaciente") return YS.normalizar(nombres.paciente(v));
            if (c === "idProfesional") return YS.normalizar(nombres.profesional(v));
            if (c === "dia") return YS.dias.orden(v);
            return typeof v === "number" ? v : YS.normalizar(v);
        }
    }));
    if (editable) columnasTabla.push({
        titulo: "", clase: "acciones",
        html: row => `
            <button class="ui-btn ui-btn--sm ui-btn--ghost ui-btn--icono" title="Editar" aria-label="Editar" onclick="editarFila(${row[idCampo[tab]]})">${YS.icono("lapiz")}</button>
            <button class="ui-btn ui-btn--sm ui-btn--ghost ui-btn--icono btn-icono-peligro" title="Eliminar" aria-label="Eliminar" onclick="eliminar('${tab}', ${row[idCampo[tab]]})">${YS.icono("papelera")}</button>`
    });

    YS.tabla(contenido, {
        filas: datos || [], columnas: columnasTabla, unidad: ["registro", "registros"],
        buscar: row => cols.map(c => c === "idPaciente" ? nombres.paciente(row[c]) : c === "idProfesional" ? nombres.profesional(row[c]) : row[c]).join(" "),
        placeholder: `Buscar en ${titulo.toLowerCase()}`,
        vacio: { icono: "bandeja", titulo: "No hay registros para mostrar" }
    });
}

// Editar desde la tabla: busca la fila por id (antes iba el JSON entero en el onclick,
// que se rompía con un apóstrofo en los datos)
function editarFila(id) {
    const fila = datosActuales.find(r => r[idCampo[tabActual]] === id);
    if (fila) abrirModal(fila);
}

// ── TURNOS ───────────────────────────────────────────────────
// Desde los chats, "Ver turnos del paciente" llega con #turnos?paciente=N
async function renderTurnos(c, params = {}) {
    const [turnos, nombres, estados] = await Promise.all([
        YS.api("/turnos"), cargarNombres(), YS.api("/estadosturnos").catch(() => [])
    ]);
    datosActuales = turnos;
    window._nombres = nombres;
    tabActual = "turnos";
    if (YS.shell.actual !== "turnos") return;

    const idPac = parseInt(params.paciente) || null;
    const lista = idPac ? turnos.filter(t => t.idPaciente === idPac) : turnos;
    const textoEstado = id => estados.find(e => e.idEstadoTurno === id)?.descripcion || YS.turno.texto(id);
    const momento = t => { const d = YS.fecha.parse(t.fecha); const [h, m] = YS.fecha.horaTexto(t.hora).split(":").map(Number); return d ? new Date(d.getFullYear(), d.getMonth(), d.getDate(), h || 0, m || 0) : new Date(0); };
    const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
    const deEstado = texto => t => YS.normalizar(textoEstado(t.idEstadoTurno)).startsWith(texto);

    YS.shell.encabezado({ subtitulo: idPac ? `Turnos de ${nombres.paciente(idPac)}` : `${turnos.length} turno${turnos.length === 1 ? "" : "s"} en total` });
    YS.shell.acciones([
        ...(idPac ? [{ texto: "Ver todos los turnos", alClick: () => { location.hash = "#turnos"; } }] : []),
        { texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab("turnos", params) }
    ]);

    // Filtro por profesional (además de los chips de estado)
    const selProf = YS.el("select", { class: "ui-select", "aria-label": "Profesional" },
        YS.el("option", { value: "", text: "Todos los profesionales" }),
        ...nombres.profesionales.filter(p => lista.some(t => t.idProfesional === p.idProfesional))
            .map(p => YS.el("option", { value: p.idProfesional, text: YS.nombreCompleto(p) })));
    const tabla = YS.tabla(c, {
        filas: lista, unidad: ["turno", "turnos"], placeholder: "Buscar por paciente, profesional o motivo",
        buscar: t => [nombres.paciente(t.idPaciente), nombres.profesional(t.idProfesional), t.motivo, t.modalidad].join(" "),
        filtros: [
            { id: "todos", texto: "Todos", pasa: () => true },
            { id: "proximos", texto: "Próximos", pasa: t => momento(t) >= hoy && !deEstado("cancel")(t) },
            { id: "pendientes", texto: "Pendientes", pasa: deEstado("pendiente") },
            { id: "confirmados", texto: "Confirmados", pasa: deEstado("confirm") },
            { id: "cancelados", texto: "Cancelados", pasa: deEstado("cancel") },
            { id: "realizados", texto: "Realizados", pasa: deEstado("realiz") }
        ],
        ordenInicial: { col: 0, desc: true },
        barra: [selProf],
        vacio: { icono: "agenda", titulo: "No hay turnos", texto: idPac ? "Este paciente todavía no tiene turnos." : "Todavía no se cargó ningún turno." },
        columnas: [
            { titulo: "Fecha", orden: t => momento(t).getTime(),
              html: t => `<span class="principal ui-num">${YS.esc(YS.fecha.corta(t.fecha))}</span><span class="secundario">${YS.esc(YS.capitalizar(YS.fecha.parse(t.fecha)?.toLocaleDateString("es-AR", { weekday: "long" }) || ""))}</span>` },
            { titulo: "Hora", html: t => `<span class="ui-num">${YS.esc(YS.fecha.horaTexto(t.hora))}</span>`, orden: t => YS.fecha.horaTexto(t.hora) },
            { titulo: "Paciente", orden: t => YS.normalizar(nombres.paciente(t.idPaciente)),
              html: t => `<span class="ui-flex">${YS.avatar(nombres.paciente(t.idPaciente), t.idPaciente, "sm")}<span class="principal">${YS.esc(nombres.paciente(t.idPaciente))}</span></span>` },
            { titulo: "Profesional", html: t => YS.esc(nombres.profesional(t.idProfesional)), orden: t => YS.normalizar(nombres.profesional(t.idProfesional)) },
            { titulo: "Modalidad", html: t => celda(t.modalidad) },
            { titulo: "Motivo", html: t => celdaLarga(t.motivo) },
            { titulo: "Pedido por", html: t => celda({ paciente: "Paciente", profesional: "Profesional" }[t.creadoPor]) },
            { titulo: "Estado", orden: t => textoEstado(t.idEstadoTurno),
              html: t => YS.turno.chip(textoEstado(t.idEstadoTurno), t.idEstadoTurno && t.creadoPor === "profesional" && deEstado("pendiente")(t) ? { propuesto: true, texto: "Propuesto" } : {}) },
            { titulo: "", clase: "acciones", html: t => `
                <button class="ui-btn ui-btn--sm ui-btn--ghost ui-btn--icono" title="Editar" aria-label="Editar turno" onclick="editarFila(${t.idTurno})">${YS.icono("lapiz")}</button>
                <button class="ui-btn ui-btn--sm ui-btn--ghost ui-btn--icono btn-icono-peligro" title="Eliminar" aria-label="Eliminar turno" onclick="eliminar('turnos', ${t.idTurno})">${YS.icono("papelera")}</button>` }
        ]
    });
    selProf.addEventListener("change", () => {
        const id = parseInt(selProf.value);
        tabla.actualizar(id ? lista.filter(t => t.idProfesional === id) : lista);
    });
}

// ── MODAL EDICIÓN ────────────────────────────────────────────
const ETIQUETAS_EDICION = { idEstadoTurno: "Estado del turno", idObraSocial: "Obra social", motivo: "Motivo", respuesta: "Respuesta" };
const AYUDAS_EDICION = {
    idEstadoTurno: "1 Pendiente · 2 Confirmado · 3 Cancelado · 4 Realizado",
    fecha: "Año-mes-día, por ejemplo 2026-10-05",
    hora: "Hora y minutos, por ejemplo 14:30", horaInicio: "Hora y minutos, por ejemplo 09:00", horaFin: "Hora y minutos, por ejemplo 18:00",
    dia: "Lunes, Martes, Miércoles…"
};
const CAMPOS_LARGOS = ["motivo", "consulta", "respuesta"];

function abrirModal(row) {
    filaEnEdicion = row;
    const tab = tabActual;
    const nombres = window._nombres;

    document.getElementById("modalTitulo").textContent = `Editar · ${titulos[tab]}`;

    const body = document.getElementById("modalBody");
    body.innerHTML = `<div class="ui-form-grid">${(campos[tab] ?? []).map(c => {
        let val = row[c] ?? "";
        // Fechas sin hora: se muestra solo el día (al guardar vuelve igual)
        if (c === "fecha" && /^\d{4}-\d{2}-\d{2}T00:00:00$/.test(val)) val = val.slice(0, 10);
        const esId = /^id[A-Z]/.test(c);
        const etiqueta = (ETIQUETAS_EDICION[c] || ETIQUETAS[c] || c) + (esId ? " (ID)" : "");
        let ayuda = AYUDAS_EDICION[c] || "";
        if (c === "idPaciente" && nombres && row[c]) ayuda = "Ahora: " + nombres.paciente(row[c]);
        if (c === "idProfesional" && nombres && row[c]) ayuda = "Ahora: " + nombres.profesional(row[c]);
        const id = `field_${c}`;
        // Los booleanos (p. ej. "disponible" de la agenda) se eligen y se mandan como true/false
        const control = typeof row[c] === "boolean"
            ? `<select id="${id}" class="ui-select"><option value="true"${row[c] ? " selected" : ""}>Sí</option><option value="false"${row[c] ? "" : " selected"}>No</option></select>`
            : CAMPOS_LARGOS.includes(c)
                ? `<textarea id="${id}" class="ui-textarea" rows="3">${YS.esc(val)}</textarea>`
                : `<input type="text" id="${id}" value="${YS.esc(val)}" class="ui-input"${esId ? ' inputmode="numeric"' : ""}>`;
        return `<div class="ui-campo${CAMPOS_LARGOS.includes(c) ? " ui-campo--ancho" : ""}">
            <label for="${id}">${YS.esc(etiqueta)}</label>
            ${control}
            ${ayuda ? `<div class="ui-campo-ayuda">${YS.esc(ayuda)}</div>` : ""}
        </div>`;
    }).join("")}</div>`;

    abrirModalFijo("modal");
}

function cerrarModal() {
    cerrarModalFijo("modal");
    filaEnEdicion = null;
}

// Antes de mandar se revisa solo lo que la API rechazaría igual (ids, fechas, horas).
// Un campo que venía vacío puede quedar vacío y se manda como null.
function errorDeCampo(c, val, original) {
    if (val === "" && original == null) return null;
    if (/^id[A-Z]/.test(c) && !/^\d+$/.test(val)) return "Tiene que ser un número.";
    if (c === "fecha" && !/^\d{4}-\d{2}-\d{2}/.test(val)) return "Usá el formato año-mes-día, por ejemplo 2026-10-05.";
    if (/^hora/.test(c) && !/^\d{1,2}:\d{2}(:\d{2})?$/.test(val)) return "Usá el formato hora:minutos, por ejemplo 14:30.";
    return null;
}

async function guardar() {
    const tab = tabActual;
    const id = filaEnEdicion[idCampo[tab]];
    const body = { ...filaEnEdicion };

    let primeroConError = null;
    (campos[tab] ?? []).forEach(c => {
        const input = document.getElementById(`field_${c}`);
        if (!input) return;
        const original = filaEnEdicion[c];
        const error = errorDeCampo(c, input.value.trim(), original);
        if (error) { YS.form.marcar(input, error); primeroConError ??= input; return; }
        if (typeof original === "boolean") body[c] = input.value === "true";
        else body[c] = input.value.trim() === "" && original == null ? null : input.value;
    });
    if (primeroConError) { primeroConError.focus(); return; }

    const boton = document.getElementById("modalConfirmar");
    boton.disabled = true;
    try {
        const res = await YS.fetch(`/${tab}/${id}`, {
            method: "PUT",
            headers: auditorHeaders(),
            body: JSON.stringify(body)
        });
        if (res.ok) { cerrarModal(); toast("Cambios guardados."); mostrarTab(tab); }
        else toast(await YS.leerMensaje(res, "No se pudieron guardar los cambios."), "error");
    } catch {
        toast("No se pudo conectar con el servidor.", "error");
    } finally {
        boton.disabled = false;
    }
}

async function eliminar(tab, id) {
    if (!await confirmar("¿Eliminar este registro?", { titulo: "Eliminar registro", textoConfirmar: "Eliminar", peligro: true })) return;
    try {
        const res = await YS.fetch(`/${tab}/${id}`, { method: "DELETE", headers: auditorHeaders() });
        if (res.ok || res.status === 204) { toast("Registro eliminado."); mostrarTab(tab); }
        else toast(await YS.leerMensaje(res, "No se pudo eliminar el registro."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

// ── UTILS ────────────────────────────────────────────────────
function errorHtml(tab) {
    return YS.errorCarga({ titulo: "No se pudo cargar la sección", texto: `El servidor no respondió (${API}). Revisá que la API esté levantada.`, reintentar: `mostrarTab('${tab}')` });
}

async function cerrarSesion() {
    await YS.sesion.cerrar();
}

async function renderAuditoria(c, pagina = 1) {
    const [datos, usuarios] = await Promise.all([
        YS.fetch(`/auditoria?pagina=${pagina}&cantidad=50`).then(r => r.json()),
        YS.fetch(`/usuarios`).then(r => r.json()).catch(() => [])
    ]);
    if (YS.shell.actual !== "auditoria") return;
    YS.shell.encabezado({ subtitulo: `Página ${pagina} · los más recientes primero` });
    YS.shell.acciones([{ texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab("auditoria") }]);

    const nombreUsuario = new Map(usuarios.map(u => [u.idUsuario, u.username]));

    const anterior = YS.el("button", { class: "ui-btn ui-btn--sm", type: "button", text: "Más recientes", disabled: pagina <= 1,
        on: { click: () => renderAuditoria(c, pagina - 1) } });
    const siguiente = YS.el("button", { class: "ui-btn ui-btn--sm", type: "button", text: "Más antiguos", disabled: datos.length < 50,
        on: { click: () => renderAuditoria(c, pagina + 1) } });
    YS.tabla(c, {
        filas: datos, porPagina: 0, unidad: ["registro en esta página", "registros en esta página"],
        placeholder: "Buscar en esta página (acción, entidad, detalle, usuario)",
        buscar: d => [d.accion, d.entidad, d.detalle, d.idEntidad, d.idUsuario, nombreUsuario.get(d.idUsuario)].join(" "),
        barra: [anterior, siguiente],
        vacio: { icono: "lista", titulo: "No hay registros de auditoría" },
        columnas: [
            { titulo: "Fecha y hora", html: d => fechaHoraCelda(d.fechaHora) },
            { titulo: "Acción", html: d => `<span class="principal">${celda(d.accion)}</span>`, orden: d => d.accion || "" },
            { titulo: "Entidad", html: d => celda(d.entidad), orden: d => d.entidad || "" },
            { titulo: "ID", html: d => d.idEntidad != null ? `<span class="ui-muted ui-num">#${YS.esc(d.idEntidad)}</span>` : celda(null) },
            { titulo: "Detalle", html: d => celdaLarga(d.detalle) },
            { titulo: "Usuario", html: d => celda(nombreUsuario.get(d.idUsuario) ? nombreUsuario.get(d.idUsuario) : `#${d.idUsuario}`), orden: d => nombreUsuario.get(d.idUsuario) || "" }
        ]
    });
}

async function renderRecuperacion(c) {
    const datos = await YS.fetch(`/recuperacion-password/pendientes`).then(r => r.json());
    if (!encabezadoListado("recuperacion", datos.length, "pendientes")) return;

    YS.tabla(c, {
        filas: datos, unidad: ["solicitud", "solicitudes"], placeholder: "Buscar por usuario o mail",
        buscar: d => [d.username, d.mail].join(" "),
        ordenInicial: { col: 2, desc: true },
        vacio: { icono: "candado", titulo: "No hay solicitudes pendientes", texto: "Las piden pacientes y profesionales desde el ingreso." },
        columnas: [
            { titulo: "Usuario", html: d => `<span class="principal">${celda(d.username)}</span>`, orden: d => d.username || "" },
            { titulo: "Mail", html: d => celda(d.mail), orden: d => d.mail || "" },
            { titulo: "Pedida", html: d => fechaHoraCelda(d.fechaSolicitud), orden: d => d.fechaSolicitud || "" },
            { titulo: "", clase: "acciones", html: d => `
                <button class="ui-btn ui-btn--sm ui-btn--ok" onclick="aceptarRecuperacion(${d.idRecuperacion})">Generar contraseña</button>
                <button class="ui-btn ui-btn--sm ui-btn--danger" onclick="rechazarRecuperacion(${d.idRecuperacion})">Rechazar</button>` }
        ]
    });
}

async function aceptarRecuperacion(id) {
    if (!await confirmar("¿Generar contraseña temporal para este usuario?")) return;
    const res = await YS.fetch(`/recuperacion-password/${id}/aceptar`, { method: "PUT" });
    if (!res.ok) { toast(await YS.leerMensaje(res, "Error al procesar la solicitud."), "error"); return; }
    const data = await res.json();
    const cuerpo = YS.el("div", {},
        YS.el("p", { text: "Pasale esta contraseña temporal al usuario. También la puede ver desde el ingreso, en «Consultar una solicitud»." }),
        YS.el("div", { class: "clave-temporal" },
            YS.el("code", { text: data.passwordTemporal }),
            YS.el("button", { class: "ui-btn ui-btn--sm", type: "button", text: "Copiar", on: { click: () => YS.copiar(data.passwordTemporal, "Contraseña copiada") } })));
    await YS.modal({ titulo: "Solicitud aceptada", cuerpo, tamano: "chico",
        acciones: [{ texto: "Listo", tipo: "primary", foco: true, alClick: c => c.cerrar() }] }).resultado;
    mostrarTab("recuperacion");
}

async function rechazarRecuperacion(id) {
    if (!await confirmar("¿Rechazar esta solicitud de recuperación de contraseña?", { titulo: "Rechazar solicitud", textoConfirmar: "Rechazar", peligro: true })) return;
    const res = await YS.fetch(`/recuperacion-password/${id}/rechazar`, { method: "PUT" });
    if (res.ok) { toast("Solicitud rechazada.", "warn"); mostrarTab("recuperacion"); }
    else toast(await YS.leerMensaje(res, "No se pudo rechazar la solicitud."), "error");
}

async function reportes() {
    const c = document.getElementById('contenido');
    c.innerHTML = YS.cargando();

    let profesionales = [];
    try {
        const r = await YS.fetch('/profesionales');
        profesionales = await r.json();
    } catch(e) {}

    const opts = profesionales
        .filter(function(p) { return p.estado === 'Activo'; })
        .map(function(p) { return '<option value="' + p.idProfesional + '">' + p.nombre + '</option>'; })
        .join('');

    if (YS.shell.actual !== "reportes") return;
    c.innerHTML =
        '<div class="tarjeta">' +
        '<h3 class="tarjeta-titulo">Atenciones por profesional</h3>' +
        '<div class="fila-form">' +
        '<div class="campo-form campo-form--ancho">' +
        '<label for="filtroProf">Profesional</label>' +
        '<select id="filtroProf">' +
        '<option value="">Todos los profesionales</option>' +
        opts +
        '</select>' +
        '</div>' +
        '<button class="ui-btn ui-btn--primary" onclick="descargarReporteConf()">' + YS.icono("descargar") + 'Descargar Excel</button>' +
        '<button class="ui-btn" onclick="descargarReporteHtml()">' + YS.icono("documento") + 'Descargar reporte HTML</button>' +
        '<button class="ui-btn" onclick="verEstadisticas()">' + YS.icono("reportes") + 'Ver estadísticas</button>' +
        '</div>' +
        '<p class="texto-suave">Elegí un profesional o dejalo en blanco para ver todos.</p>' +
        '</div>' +
        '<div id="panelEstadisticas" class="tarjeta oculto">' +
        '<div class="ui-entre mb-12">' +
        '<h3 class="tarjeta-titulo mb-0">Estadísticas</h3>' +
        '<button class="ui-btn ui-btn--sm" onclick="descargarGrafico()">' + YS.icono("descargar") + 'Descargar imagen</button>' +
        '</div>' +
        '<canvas id="graficoAtenciones" class="canvas-grafico"></canvas>' +
        '</div>';
}

async function verEstadisticas() {
    const idProf = document.getElementById('filtroProf').value;
    const url = API + '/reportes/atenciones' + (idProf ? '?idProfesional=' + idProf : '');
    let datos = [];
    try {
        const r = await YS.fetch(url);
        datos = await r.json();
    } catch(e) {
        YS.toast('No se pudieron obtener los datos del reporte.', 'error');
        return;
    }
    if (!datos.length) {
        YS.toast('No hay atenciones para mostrar con ese filtro.', 'warn');
        return;
    }

    // Agrupar por profesional
    const conteo = {};
    datos.forEach(function(d) {
        const key = d.nombreProfesional;
        conteo[key] = (conteo[key] || 0) + 1;
    });
    const labels = Object.keys(conteo);
    const valores = Object.values(conteo);
    const colores = labels.map(function(_, i) {
        const palette = ['#6A5ACD','#0d6efd','#198754','#fd7e14','#dc3545','#20c997','#6610f2','#0dcaf0'];
        return palette[i % palette.length];
    });

    const panel = document.getElementById('panelEstadisticas');
    panel.classList.remove('oculto');

    // Destruir grafico anterior si existe
    if (window._chartAtenciones) {
        window._chartAtenciones.destroy();
    }

    // Cargar Chart.js si no esta cargado
    if (!window.Chart) {
        await new Promise(function(resolve) {
            const script = document.createElement('script');
            script.src = 'https://cdn.jsdelivr.net/npm/chart.js';
            script.onload = resolve;
            document.head.appendChild(script);
        });
    }

    const ctx = document.getElementById('graficoAtenciones').getContext('2d');
    window._chartAtenciones = new window.Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Cantidad de atenciones',
                data: valores,
                backgroundColor: colores,
                borderRadius: 8
            }]
        },
        options: {
            responsive: true,
            plugins: {
                legend: { display: false },
                title: {
                    display: true,
                    text: 'Atenciones por Profesional',
                    font: { size: 16 }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: { stepSize: 1 }
                }
            }
        }
    });

    panel.scrollIntoView({ behavior: 'smooth' });
}

function descargarGrafico() {
    const canvas = document.getElementById('graficoAtenciones');
    if (!canvas) { YS.toast('Primero generá las estadísticas.', 'warn'); return; }
    const link = document.createElement('a');
    link.href = canvas.toDataURL('image/png');
    link.download = 'estadisticas_atenciones.png';
    link.click();
}

async function descargarReporteConf() {
    const idProf = document.getElementById('filtroProf').value;
    const url = API + '/reportes/atenciones' + (idProf ? '?idProfesional=' + idProf : '');
    let datos = [];
    try {
        const r = await YS.fetch(url);
        datos = await r.json();
    } catch(e) {
        YS.toast('No se pudieron obtener los datos del reporte.', 'error');
        return;
    }
    if (!datos.length) {
        YS.toast('No hay atenciones para el filtro elegido.', 'warn');
        return;
    }
    const encabezado = ['Nombre y Apellido', 'DNI', 'NHC', 'Fecha', 'Hora', 'Tipo de Cierre', 'Profesional'];
    const filas = datos.map(function(d) {
        return [d.nombre, d.dni, d.nhc || '', d.fecha, d.hora, d.tipoCierre || '', d.nombreProfesional];
    });
    const csvContent = [encabezado].concat(filas)
        .map(function(row) { return row.map(function(v) { return '"' + String(v).replace(/"/g, '""') + '"'; }).join(','); })
        .join('\n');
    const blob = new Blob(['\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'atenciones_por_profesional.csv';
    link.click();
}

// Descarga el reporte HTML interactivo. A diferencia del CSV, que se arma acá en
// el navegador, este lo genera el servidor: hay que pedirlo con fetch para poder
// mandar el header X-Usuario-Id. Una navegación (window.location) no lleva
// headers, y sin ese header el control de acceso CB-04 no puede saber quién está
// pidiendo el reporte y trata a cualquiera como configurador.
async function descargarReporteHtml() {
    const idProf = document.getElementById('filtroProf').value;
    const url = API + '/reportes/atenciones/html' + (idProf ? '?idProfesional=' + idProf : '');
    let res;
    try {
        res = await YS.fetch(url, { headers: { "X-Usuario-Id": localStorage.getItem("idUsuario") ?? "0" } });
    } catch(e) {
        YS.toast('No se pudo generar el reporte.', 'error');
        return;
    }
    if (res.status === 403) {
        let msg = 'Sin permiso para descargar este reporte.';
        try { const j = await res.json(); if (j && j.mensaje) msg = j.mensaje; } catch(e) {}
        YS.avisar({ titulo: 'No se pudo descargar el reporte', mensaje: msg });
        return;
    }
    if (!res.ok) {
        YS.toast('No se pudo generar el reporte.', 'error');
        return;
    }
    // El nombre con la fecha lo decide el servidor en el Content-Disposition.
    let nombre = 'Atenciones_por_Profesional.html';
    const cd = res.headers.get('Content-Disposition');
    if (cd) {
        const m = /filename\*=UTF-8''([^;]+)/i.exec(cd) || /filename="?([^";]+)"?/i.exec(cd);
        if (m) nombre = decodeURIComponent(m[1].trim());
    }
    // blob() conserva los bytes tal cual los mandó el servidor, así que el UTF-8
    // del reporte llega intacto al archivo descargado.
    const blob = await res.blob();
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = nombre;
    link.click();
    URL.revokeObjectURL(link.href);
}

let _admisionesCache = [];

async function admisiones() {
    const c = document.getElementById("contenido");
    c.innerHTML = YS.cargando();
    try {
        const r = await YS.fetch("/informes-admision-detalle");
        _admisionesCache = await r.json();
    } catch(e) { _admisionesCache = []; }

    if (YS.shell.actual !== "admisiones") return;
    YS.shell.encabezado({ subtitulo: `${_admisionesCache.length} informe${_admisionesCache.length === 1 ? "" : "s"}` });
    YS.shell.acciones([{ texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab("admisiones") }]);

    const tono = { "Pendiente": "warn", "En entrevista": "info", "Derivado": "ok", "Rechazado": "danger" };
    c.innerHTML = '<div id="tablaAdmisiones"></div><div id="detalleAdmision" class="mt-16"></div>';
    YS.tabla(document.getElementById("tablaAdmisiones"), {
        filas: _admisionesCache, unidad: ["informe", "informes"], placeholder: "Buscar por paciente, DNI o especialidad",
        buscar: a => [a.nombrePaciente, a.dni, a.especialidad].join(" "),
        filtros: [
            { id: "abiertas", texto: "Por resolver", pasa: a => a.estado !== "Derivado" },
            { id: "todas", texto: "Todas", pasa: () => true },
            { id: "pendientes", texto: "Pendientes", pasa: a => a.estado === "Pendiente" },
            { id: "entrevista", texto: "En entrevista", pasa: a => a.estado === "En entrevista" },
            { id: "derivadas", texto: "Derivadas", pasa: a => a.estado === "Derivado" },
            { id: "rechazadas", texto: "Rechazadas", pasa: a => a.estado === "Rechazado" }
        ],
        vacio: { icono: "portapapeles", titulo: "No hay informes de admisión", texto: "Los completan los pacientes desde su panel." },
        columnas: [
            { titulo: "Paciente", orden: a => YS.normalizar(a.nombrePaciente),
              html: a => `<span class="ui-flex">${YS.avatar(a.nombrePaciente, a.idPaciente, "sm")}<span class="principal">${YS.esc(a.nombrePaciente)}</span></span>` },
            { titulo: "DNI", html: a => celda(a.dni) },
            { titulo: "Especialidad", html: a => celda(a.especialidad), orden: a => a.especialidad || "" },
            { titulo: "Fecha", html: a => celda(a.fechaCreacion) },
            { titulo: "Estado", html: a => `<span class="ui-chip ui-chip--${tono[a.estado] || "neutral"}">${YS.esc(a.estado)}</span>`, orden: a => a.estado },
            { titulo: "", clase: "acciones", html: a => `<button class="ui-btn ui-btn--sm" onclick="verAdmision(${a.idInforme})">Ver informe</button>` }
        ]
    });
}

function verAdmision(idInforme) {
    const a = _admisionesCache.find(function(x) { return x.idInforme === idInforme; });
    if (a === undefined) return;

    const campo = function(label, valor) {
        if (valor === null || valor === undefined || valor === "") return "";
        return '<div class="dato-admision"><span class="etiqueta-dato">' + label + '</span><span class="valor-dato">' + YS.esc(valor) + '</span></div>';
    };

    const esEntrevista = a.estado === "En entrevista" || a.estado === "Rechazado";
    const esPendiente  = a.estado === "Pendiente";
    const esDerivado   = a.estado === "Derivado";

    let acciones = "";
    if (esPendiente) {
        acciones = '<div class="caja-accion">' +
            '<h4>Programar primera entrevista</h4>' +
            '<div class="fila-form">' +
            '<div class="campo-form ui-campo"><label for="entFecha">Fecha</label><input type="date" id="entFecha" class="ui-input"></div>' +
            '<div class="campo-form ui-campo"><label for="entHora">Hora</label><input type="time" id="entHora" class="ui-input"></div>' +
            '<button class="ui-btn ui-btn--primary" onclick="programarEntrevista(' + a.idInforme + ')">Programar</button>' +
            '</div></div>';
    }
    if (esEntrevista) {
        acciones = '<div class="caja-accion">' +
            '<h4>Derivar al profesional</h4>' +
            (a.estado === "Rechazado" ? '<p class="ui-alerta ui-alerta--danger mb-12"><span><strong>Rechazado por el profesional anterior:</strong> ' + YS.esc(a.motivoRechazo || "Sin motivo") + '</span></p>' : '') +
            '<p class="texto-chico mb-4">Entrevista programada: ' + YS.esc(a.fechaEntrevista || "-") + '</p>' +
            '<p class="mb-12">Código de sala: <strong class="codigo-sala">' + YS.esc(a.codigoSala || "-") + '</strong> <button onclick="iniciarEntrevistaVideo(\'' + a.codigoSala + '\')" class="ui-btn ui-btn--sm ui-btn--suave">Iniciar entrevista</button></p>' +
            '<div class="campo-form ui-campo mb-12"><label for="derObs">Observaciones de la entrevista</label>' +
            '<textarea id="derObs" rows="3" class="ui-textarea">' + YS.esc(a.observacionesEntrevista || "") + '</textarea></div>' +
            '<div class="fila-form">' +
            '<div class="campo-form ui-campo campo-form--ancho"><label for="derProf">Profesional asignado</label><select id="derProf" class="ui-select"><option value="">Cargando…</option></select></div>' +
            '<button class="ui-btn ui-btn--primary" onclick="derivarAdmision(' + a.idInforme + ')">Derivar</button>' +
            '</div></div>';
    }
    if (esDerivado) {
        acciones = '<div class="ui-alerta ui-alerta--ok ui-alerta--bloque mt-16">' +
            '<strong>Derivado a: ' + YS.esc(a.nombreProfesionalDerivado || "-") + '</strong>' +
            (a.observacionesEntrevista ? '<p class="mt-8 mb-0">' + YS.esc(a.observacionesEntrevista) + '</p>' : "") +
            '</div>';
    }

    document.getElementById("detalleAdmision").innerHTML =
        '<div class="tarjeta">' +
        '<h3 class="tarjeta-titulo">Informe de ' + YS.esc(a.nombrePaciente) + '</h3>' +
        '<div class="grilla-2">' +
        campo("DNI", a.dni) +
        campo("Fecha de nacimiento", a.fechaNacimiento) +
        campo("Especialidad solicitada", a.especialidad) +
        campo("Tipo de entrevista", a.tipoEntrevista) +
        campo("Motivo de consulta", a.motivoConsulta) +
        campo("Tratamiento anterior", a.tratamientoAnterior ? ("Si. " + (a.tratamientoAnteriorDetalle || "")) : "No") +
        campo("Medicacion actual", a.recibeMedicacion ? ("Si. " + (a.medicacionDetalle || "")) : "No") +
        campo("Gustos e intereses", a.gustosIntereses) +
        campo("Preferencias de atencion", a.preferenciasAtencion) +
        campo("Red de apoyo", a.redApoyo) +
        campo("Objetivos del paciente", a.objetivosPaciente) +
        '</div>' +
        '<button class="ui-btn ui-btn--sm mt-8" onclick="verHCAdmision(' + a.idPaciente + ')">Ver historia clínica completa</button>' +
        acciones +
        '</div>';

    if (esEntrevista) cargarProfesionalesDerivacion();
    document.getElementById("detalleAdmision").scrollIntoView({ behavior: "smooth" });
}

async function cargarProfesionalesDerivacion() {
    let profs = [];
    try { profs = await (await YS.fetch("/profesionales")).json(); } catch(e) {}
    const sel = document.getElementById("derProf");
    if (sel === null) return;
    sel.innerHTML = '<option value="">Elegí un profesional</option>' + profs
        .filter(function(p) { return p.estado === "Activo"; })
        .map(function(p) { return '<option value="' + p.idProfesional + '">' + YS.esc(YS.nombreCompleto(p)) + '</option>'; })
        .join("");
}

async function programarEntrevista(idInforme) {
    const fecha = document.getElementById("entFecha").value;
    const hora  = document.getElementById("entHora").value;
    if (!fecha) YS.form.marcar("entFecha", "Elegí la fecha.");
    if (!hora) YS.form.marcar("entHora", "Elegí la hora.");
    if (!fecha || !hora) return;
    const r = await YS.fetch("/informes-admision/" + idInforme + "/entrevista", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fecha: fecha + "T" + hora + ":00", observaciones: null })
    });
    if (r.ok) { const resp = await r.json(); toast("Entrevista programada. Código de sala: " + resp.codigoSala + ". Se le avisó al paciente."); admisiones(); }
    else toast(await YS.leerMensaje(r, "No se pudo programar la entrevista."), "error");
}

async function derivarAdmision(idInforme) {
    const idProf = parseInt(document.getElementById("derProf").value);
    const obs    = document.getElementById("derObs").value || null;
    if (isNaN(idProf)) { YS.form.marcar("derProf", "Elegí el profesional."); return; }
    const r = await YS.fetch("/informes-admision/" + idInforme + "/derivar", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ idProfesional: idProf, observaciones: obs })
    });
    if (r.ok) { toast("Paciente derivado. Ya aparece en Mis pacientes del profesional."); admisiones(); }
    else toast(await YS.leerMensaje(r, "No se pudo derivar al paciente."), "error");
}

async function verHCAdmision(idPaciente) {
    let paciente = null, historia = null, evoluciones = [], sesiones = [];
    try { paciente = await (await YS.fetch("/pacientes/" + idPaciente)).json(); } catch(e) {}
    try { historia = await (await YS.fetch("/pacientes/" + idPaciente + "/historiaclinica")).json(); } catch(e) {}
    if (historia !== null && historia.idHistoria !== undefined) {
        try { evoluciones = await (await YS.fetch("/historiasclinicas/" + historia.idHistoria + "/evoluciones")).json(); } catch(e) {}
    }
    try { sesiones = await (await YS.fetch("/pacientes/" + idPaciente + "/sesiones-clinicas")).json(); } catch(e) {}

    const evHtml = evoluciones.map(function(e) {
        return '<div class="item-historia">' +
            '<strong>' + new Date(e.fecha).toLocaleDateString("es-AR") + '</strong>' +
            (e.diagnostico ? '<div><em>Diagnóstico:</em> ' + YS.esc(e.diagnostico) + '</div>' : "") +
            (e.observaciones ? '<div><em>Observaciones:</em> ' + YS.esc(e.observaciones) + '</div>' : "") +
            '</div>';
    }).join("") || '<p class="texto-suave">Sin evoluciones.</p>';

    const sesHtml = sesiones.map(function(s) {
        return '<div class="item-historia item-historia--sesion">' +
            '<strong>' + new Date(s.fechaHora).toLocaleDateString("es-AR") + '</strong>' +
            (s.tipoCierre ? " - " + YS.esc(s.tipoCierre) : "") +
            (s.notasEvolucion ? '<div>' + YS.esc(s.notasEvolucion) + '</div>' : "") +
            '</div>';
    }).join("") || '<p class="texto-suave">Sin sesiones clinicas.</p>';

    document.getElementById("detalleAdmision").innerHTML +=
        '<div class="tarjeta mt-16">' +
        '<h3 class="tarjeta-titulo">Historia clínica - ' + YS.esc(paciente !== null ? paciente.nombre : "") + '</h3>' +
        '<h4 class="mt-16">Evoluciones</h4>' + evHtml +
        '<h4 class="mt-16">Sesiones clínicas</h4>' + sesHtml +
        '</div>';
}

async function iniciarEntrevistaVideo(codigoSala) {
    if (codigoSala === "" || codigoSala === "null" || codigoSala === "-") { toast("Este informe no tiene codigo de sala.", "warn"); return; }
    let idAdm = null;
    try {
        const profs = await (await YS.fetch("/profesionales")).json();
        const adm = profs.find(function(p) { return p.matricula === "ADM-001"; });
        if (adm !== undefined) idAdm = adm.idProfesional;
    } catch(e) {}
    if (idAdm === null) { toast("Falta crear el profesional admisionista (ADM-001).", "error"); return; }
    localStorage.setItem("idProfesional", idAdm);
    window.open("../Pantalla_Profesional/sesion_video.html?sala=" + encodeURIComponent(codigoSala), "_blank");
}

// ── ENCABEZADO DE LISTADOS ───────────────────────────────────
// Cantidad en el subtítulo y "Actualizar" como acción. Devuelve false si el usuario
// ya pasó a otra sección mientras se cargaban los datos (para no pisarla).
function encabezadoListado(tab, cantidad, palabra = "registros", extra = "") {
    if (YS.shell.actual !== tab) return false;
    const singular = { registros: "registro", pendientes: "pendiente", solicitudes: "solicitud" };
    const texto = `${cantidad} ${cantidad === 1 ? (singular[palabra] || palabra) : palabra}`;
    YS.shell.encabezado({ subtitulo: [texto, extra].filter(Boolean).join(" · ") });
    YS.shell.acciones([{ texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab(tab) }]);
    return true;
}

// ── INICIO: resumen de lo que está pendiente ─────────────────
async function inicioConfigurador() {
    const c = document.getElementById("contenido");
    c.innerHTML = YS.esqueleto.bloques(4);
    YS.shell.usuario.then(u => {
        if (YS.shell.actual === "inicio")
            YS.shell.encabezado({ titulo: u.nombre ? `Hola, ${u.nombre}` : "Inicio", subtitulo: YS.capitalizar(YS.fecha.larga(new Date())) });
    });

    const pedir = ruta => YS.api(ruta).catch(() => null);
    const [solicitudes, derivaciones, recuperacion, informes, pacientes, profesionales, turnos, actividad] = await Promise.all([
        pedir("/profesionales/solicitudes"), pedir("/derivaciones/detalle"), pedir("/recuperacion-password/pendientes"),
        pedir("/informes-admision-detalle"), pedir("/pacientes"), pedir("/profesionales"), pedir("/turnos"),
        pedir("/auditoria?pagina=1&cantidad=8")
    ]);
    const conChats = (await YS.shell.capacidades)?.mensajeria;
    const chats = conChats ? await pedir("/mensajeria/conversaciones") : null;
    if (YS.shell.actual !== "inicio") return;

    const cuenta = (lista, filtro = () => true) => Array.isArray(lista) ? lista.filter(filtro).length : null;
    const hoy = new Date();
    const tarjeta = (seccion, etiqueta, icono, valor, pie, alerta = false) => `
        <a class="ui-stat${alerta && valor ? " ui-stat--warn" : ""}" href="#${seccion}">
            <span class="ui-stat-etiqueta">${YS.icono(icono)}${YS.esc(etiqueta)}</span>
            <span class="ui-stat-valor">${valor ?? "—"}</span>
            <span class="ui-stat-pie">${YS.esc(pie)}</span>
        </a>`;
    const nRegistro = cuenta(solicitudes);
    const nCambio = cuenta(derivaciones, d => d.estado === "Pendiente");
    const nRecuperacion = cuenta(recuperacion);
    const nAdmision = cuenta(informes, a => ["Pendiente", "En entrevista", "Rechazado"].includes(a.estado));
    const nTurnosHoy = cuenta(turnos, t => YS.fecha.mismoDia(t.fecha, hoy) && t.idEstadoTurno !== 3);
    const nPacientes = cuenta(pacientes, p => p.estado !== "Inactivo");
    const nProfesionales = cuenta(profesionales, p => p.estado === "Activo");
    const nChatsSinLeer = cuenta(chats, ch => ch.noLeidos > 0);
    const nChatsDerivados = cuenta(chats, ch => ch.estado === "DerivadaHumano" || ch.estado === "Urgente");
    const nChatsUrgentes = cuenta(chats, ch => ch.estado === "Urgente");
    const tarjetaChats = (filtro, etiqueta, valor, pie, peligro) => `
        <button class="ui-stat${valor ? (peligro ? " ui-stat--danger" : " ui-stat--warn") : ""}" type="button" onclick="irAChatsFiltrados('${filtro}')">
            <span class="ui-stat-etiqueta">${YS.icono("chat")}${YS.esc(etiqueta)}</span>
            <span class="ui-stat-valor">${valor ?? "—"}</span>
            <span class="ui-stat-pie">${YS.esc(pie)}</span>
        </button>`;

    const filasActividad = (actividad || []).map(a => `
        <tr><td class="ui-num">${YS.esc(new Date(a.fechaHora).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "short" }))}</td>
            <td class="principal">${YS.esc(a.accion)}</td><td>${YS.esc(a.entidad)}${a.idEntidad != null ? " #" + YS.esc(a.idEntidad) : ""}</td>
            <td>${YS.esc(a.idUsuario)}</td></tr>`).join("");

    c.innerHTML = `
        <div class="ui-titulo-seccion texto-chico ui-muted">Para revisar</div>
        <div class="ui-stats">
            ${tarjeta("solicitudes", "Solicitudes de registro", "usuario-mas", nRegistro, "Profesionales esperando aprobación", true)}
            ${tarjeta("derivaciones", "Solicitudes de cambio", "flechas", nCambio, "Pacientes que pidieron otro profesional", true)}
            ${tarjeta("recuperacion", "Recuperación de contraseña", "candado", nRecuperacion, "Pedidos de contraseña temporal", true)}
            ${tarjeta("admisiones", "Admisiones por resolver", "portapapeles", nAdmision, "Pendientes, en entrevista o rechazadas", true)}
            ${conChats ? tarjetaChats("noLeidas", "Chats sin leer", nChatsSinLeer, "Conversaciones con mensajes nuevos") : ""}
            ${conChats ? tarjetaChats("derivadas", "Chats escalados", nChatsDerivados, nChatsUrgentes ? `${nChatsUrgentes} urgente${nChatsUrgentes === 1 ? "" : "s"}` : "Esperan a una persona del equipo", nChatsUrgentes > 0) : ""}
        </div>
        <div class="ui-titulo-seccion texto-chico ui-muted">Hoy en YoSoy</div>
        <div class="ui-stats">
            ${tarjeta("turnos", "Turnos de hoy", "reloj", nTurnosHoy, "Sin contar los cancelados")}
            ${tarjeta("usuarios", "Pacientes activos", "pacientes", nPacientes, "Sin contar los dados de baja")}
            ${tarjeta("usuarios", "Profesionales activos", "usuario", nProfesionales, "Con cuenta aprobada")}
        </div>
        <div class="ui-card ui-card--sin-padding">
            <div class="ui-card-header inicio-actividad-cabecera">
                <h2 class="ui-card-titulo">Actividad reciente</h2>
                <a class="ui-btn ui-btn--sm" href="#auditoria">Ver auditoría</a>
            </div>
            ${filasActividad
                ? `<div class="ui-tabla-scroll"><table class="ui-tabla ui-tabla--densa"><thead><tr><th>Fecha</th><th>Acción</th><th>Entidad</th><th>Usuario</th></tr></thead><tbody>${filasActividad}</tbody></table></div>`
                : YS.vacio({ icono: "lista", texto: actividad ? "Todavía no hay acciones registradas." : "No se pudo cargar la auditoría." })}
        </div>`;
}

YS.shell.iniciar({
    rol: "configurador", navegar: (id, params) => mostrarTab(id, params),
    chats: { modo: "pagina", contenedor: document.getElementById("vistaChatsCRM"), alPulsarBoton: () => { location.hash = "#chats"; } }
});
