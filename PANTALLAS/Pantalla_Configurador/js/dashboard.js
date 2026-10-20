const API = YS.API;   // mismo origen que la página (shared/ui.js)

// Roles y permisos: esta pantalla es el panel de GESTIÓN y la usan tres módulos,
// cada uno con sus propias secciones (el menú lo arma shared/shell.js con los permisos y
// privilegios de la sesión; el servidor vuelve a controlar cada pedido):
//   - Configurador: usuarios, permisos, configuración y auditoría administrativa.
//   - CRM y atención al cliente: chats, casos, derivaciones de casos y etiquetas; con Gestión
//     de agendas, también turnos y horarios de todos.
//   - Profesional: con Admisión, admisiones, solicitudes de cambio y asignaciones; si es
//     auditor, reportes de la organización, lecturas clínicas y auditoría clínica.
const MODULO = ["configurador", "crm", "profesional"].includes(YS.sesion.modulo()) ? YS.sesion.modulo() : "configurador";
const SECCIONES_POR_MODULO = {
    // Configurador: sin Inicio. Fuera del menu (su codigo y sus endpoints siguen): grupos, reglasderivacion, coberturas, privilegios, roles, recuperacion.
    configurador: ["altausuario", "permisos", "usuarios", "solicitudes", "whatsapp", "especialidades", "auditoria",
          ...(YS.sesion.tienePermiso("crm") ? ["chats"] : [])],
    crm: ["inicio", "chats", "escalamientos", "etiquetas", "casos", "casosderivaciones",
          ...(YS.sesion.tienePrivilegio("gestion_agendas") ? ["turnos", "agendas"] : [])],
    profesional: [
        ...(YS.sesion.tienePrivilegio("admision") ? ["admisiones", "derivaciones", "asignaciones"] : []),
        ...(YS.sesion.tienePermiso("auditor") ? ["reportesgenerales", "evoluciones", "eventoscrisis", "interaccionesia", "auditoriaclinica"] : [])
    ]
};

// Donde se aterriza al ingresar o ante un enlace a una seccion que no esta en el menu: la primera con permiso (Configurador: Usuarios).
const ATERRIZAJE = MODULO === "configurador" ? "usuarios" : "inicio";
// Chats: el modulo CRM, o el Configurador que trae el permiso crm.
const CON_CHATS = MODULO === "crm" || (MODULO === "configurador" && YS.sesion.tienePermiso("crm"));

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
    else if (document.getElementById("modalCasoOverlay")?.classList.contains("visible")) { e.preventDefault(); cerrarModalCaso(); }
});

// ── Modal compartido de la Fase 2 (Grupos, Reglas, Derivaciones, Reasignar) ──
let _accionModalCaso = null;

function abrirModalCaso(titulo, textoConfirmar, cuerpoHtml, alConfirmar) {
    document.getElementById("modalCasoBody").innerHTML = cuerpoHtml;
    _accionModalCaso = alConfirmar;
    abrirModalFijo("modalCaso", titulo, textoConfirmar);
}

function cerrarModalCaso() {
    cerrarModalFijo("modalCaso");
    _accionModalCaso = null;
    // Una contraseña temporal mostrada (data-secreto) no se queda en el DOM del modal ya cerrado
    const cuerpo = document.getElementById("modalCasoBody");
    if (cuerpo.querySelector("[data-secreto]")) cuerpo.innerHTML = "";
}

async function confirmarModalCaso() {
    if (!_accionModalCaso) return;
    const boton = document.getElementById("modalCasoConfirmar");
    boton.disabled = true;
    try { await _accionModalCaso(); }
    finally { boton.disabled = false; }
}

const SOLO_LECTURA = ["evoluciones", "eventoscrisis", "interaccionesia"];

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
    asignaciones:    "Asignaciones",
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
    whatsapp:        "WhatsApp",
};

let tabActual = null;
let datosActuales = [];
let filaEnEdicion = null;

// ── NAVEGACIÓN ───────────────────────────────────────────────
// Cada sección del menú superior (shared/shell.js) se dibuja en #contenido.
const SECCIONES = SECCIONES_POR_MODULO[MODULO];

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

// abrir() de bandeja.js, en modo "pagina", no sabe de pestanas propias: pide esto cuando
// hace falta mostrar la seccion (por ejemplo, "Abrir" desde el toast de un aviso con otra
// pestana puesta) en vez de seleccionar la conversacion detras de lo que ya se ve.
document.addEventListener("ys:chats-ir-a-pagina", () => {
    if (location.hash !== "#chats" && location.hash !== "#escalamientos") location.hash = "#chats";
});

async function mostrarTab(tab, params = {}) {
    if (!SECCIONES.includes(tab)) tab = SECCIONES.includes(ATERRIZAJE) ? ATERRIZAJE : SECCIONES.includes("inicio") ? "inicio" : SECCIONES[0];
    if (!tab) { sinSecciones(); return; }
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

    if (tab === 'inicio') { MODULO === "crm" ? inicioCrm() : inicioConfigurador(); return; }
    if (tab === 'reportesgenerales') { reportes(); return; }
    if (tab === 'admisiones') { admisiones(); return; }
    tabActual = tab;

    const contenido = document.getElementById("contenido");
    contenido.innerHTML = YS.cargando();

    try {
        if (tab === "usuarios" || tab === "asignaciones") return await renderUsuarios(contenido, params, tab);
        if (tab === "permisos")     return await renderPermisos(contenido);
        if (tab === "altausuario")  return await renderAltaUsuario(contenido);
        if (tab === "privilegios")  return await renderPrivilegios(contenido);
        if (tab === "auditoriaclinica") return await renderAuditoria(contenido, 1, tab);
        if (tab === "solicitudes")  return await renderSolicitudes(contenido);
        if (tab === "derivaciones") return await renderDerivaciones(contenido);
        if (tab === "auditoria")    return await renderAuditoria(contenido);
        if (tab === "casos")              return await renderCasos(contenido, params);
        if (tab === "casosderivaciones")  return await renderCasosDerivaciones(contenido);
        if (tab === "grupos")             return await renderGrupos(contenido);
        if (tab === "reglasderivacion")   return await renderReglasDerivacion(contenido);
        if (tab === "etiquetas")          return await renderEtiquetas(contenido);
        if (tab === "whatsapp")           return await renderWhatsapp(contenido);

        if (tab === "turnos") return await renderTurnos(contenido, params);
        const datos = await YS.fetch(`/${tab}`).then(r => r.json());
        datosActuales = datos;
        await renderTabla(titulos[tab], columnas[tab], datos, tab, contenido);

    } catch {
        contenido.innerHTML = errorHtml(tab);
    }
}

// ── USUARIOS ─────────────────────────────────────────────────
// Dos modos (roles y permisos):
//   - "usuarios" (Configurador › Usuarios): altas, bajas y link público. Sin relaciones: quién
//     atiende a quién es información de salud y el Configurador no la necesita.
//   - "asignaciones" (Profesional con Admisión): las relaciones paciente-profesional,
//     asignar y cambiar de profesional.
async function renderUsuarios(c, params = {}, modo = "usuarios") {
    window._modoUsuarios = modo;
    const [pacientes, profesionales, sinUsuario] = await Promise.all([
        YS.api(`/pacientes`),
        YS.api(`/profesionales`),
        // Profesionales Activos que todavía no pueden ingresar (para ofrecer "Crear usuario"); solo lo ve el Configurador: si falla (otro
        // módulo) o no hay, no se ofrece.
        YS.fetch(`/profesionales/sin-usuario`).then(r => r.ok ? r.json() : []).catch(() => []),
    ]);

    const pacProfs = {};
    const profPacs = {};
    if (modo === "asignaciones") await Promise.all(pacientes.map(async p => {
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
    window._profSinUsuario = new Set(sinUsuario);
    window._mostrarDadosDeBaja = false;
    window._filtroUsuarios = "";

    if (YS.shell.actual !== modo) return;
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
    else document.getElementById("splitInfo").innerHTML = window._modoUsuarios === "asignaciones"
        ? '<span class="ui-muted">Elegí un paciente o un profesional para ver con quién está relacionado y asignar.</span>'
        : '<span class="ui-muted">Elegí un paciente o un profesional para ver sus acciones. Los permisos del equipo están en Usuarios › Permisos.</span>';
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
    const asignando = window._modoUsuarios === "asignaciones";
    const botones = (asignando ? [
        !inactivo && relacionados.size ? `<button class="ui-btn ui-btn--sm" onclick="abrirModalCambioDirecto(${idPac})">Cambiar profesional</button>` : "",
        !inactivo ? `<button class="ui-btn ui-btn--sm" onclick="abrirModalAsignar(${idPac})">Asignar profesional</button>` : ""
    ] : [
        inactivo
            ? `<button class="ui-btn ui-btn--sm ui-btn--ok" onclick="darDeAltaPaciente(${idPac})">Dar de alta</button>`
            : `<button class="ui-btn ui-btn--sm ui-btn--danger" onclick="darDeBajaPaciente(${idPac})">Dar de baja</button>`
    ]).join("");
    const texto = !asignando ? `· DNI ${YS.esc(pac?.dni ?? "-")}` : relacionados.size
        ? `tiene asignado${relacionados.size === 1 ? "" : "s"}: ${YS.esc(nombres.join(", "))}.`
        : "no tiene profesionales asignados.";
    document.getElementById("splitInfo").innerHTML = `
        <div class="split-info-texto"><strong>${YS.esc(pac?.nombre)}</strong> ${texto}</div>
        <div class="split-info-acciones">${botones}</div>`;
}

function seleccionarProfesional(idProf) {
    window._profSeleccionado = idProf;
    window._pacSeleccionado = null;
    const relacionados = window._profPacs[idProf] ?? new Set();
    marcarTarjetas(`prof-${idProf}`, "#listaPacientes", "pac-", relacionados);

    const prof = window._profesionales.find(p => p.idProfesional === idProf);
    const nombres = [...relacionados].map(idPac => window._pacientes.find(p => p.idPaciente === idPac)?.nombre ?? `#${idPac}`);
    const asignando = window._modoUsuarios === "asignaciones";
    const boton = asignando ? "" : prof?.estado === "Inactivo"
        ? `<button class="ui-btn ui-btn--sm ui-btn--ok" onclick="darDeAltaProfesional(${idProf})">Dar de alta</button>`
        : `<button class="ui-btn ui-btn--sm ui-btn--danger" onclick="darDeBajaProfesional(${idProf})">Dar de baja</button>`;
    const botonLink = !asignando && prof?.linkPublicoActivo
        ? `<button class="ui-btn ui-btn--sm" onclick="apagarLinkPublicoAdmin(${idProf})">Apagar link público</button>` : "";
    // Un profesional aprobado no puede ingresar hasta que tiene usuario (solo en Usuarios, que es del Configurador)
    const sinUsuario = !asignando && prof?.estado === "Activo" && window._profSinUsuario?.has(idProf);
    const botonUsuario = sinUsuario
        ? `<button class="ui-btn ui-btn--sm ui-btn--primary" onclick="abrirModalCrearUsuario(${idProf})">Crear usuario</button>` : "";
    const texto = !asignando ? `· Mat. ${YS.esc(prof?.matricula ?? "-")}` : relacionados.size
        ? `atiende a: ${YS.esc(nombres.join(", "))}.`
        : "no tiene pacientes asignados.";
    document.getElementById("splitInfo").innerHTML = `
        <div class="split-info-texto"><strong>${YS.esc(YS.nombreCompleto(prof))}</strong> ${texto}
            ${sinUsuario ? ' <span class="ui-chip ui-chip--warn">Sin usuario: todavía no puede ingresar</span>' : ""}
            ${!asignando && prof?.linkPublicoActivo ? ' <span class="ui-chip ui-chip--ok">Link público activo</span>' : ""}</div>
        <div class="split-info-acciones">${botonUsuario}${boton}${botonLink}</div>`;
}

async function apagarLinkPublicoAdmin(idProf) {
    const nombre = YS.nombreCompleto(window._profesionales.find(p => p.idProfesional === idProf)) || "este profesional";
    if (!await confirmar(`¿Apagar el link público de reserva de ${nombre}? Deja de funcionar al instante.`,
        { titulo: "Link público", textoConfirmar: "Apagar", peligro: true })) return;
    try {
        const res = await YS.fetch(`${API}/profesionales/${idProf}/link-publico`, {
            method: "PUT", headers: auditorHeaders(), body: JSON.stringify({ activo: false })
        });
        if (res.ok) { toast("Link público apagado."); mostrarTab("usuarios"); }
        else toast(await YS.leerMensaje(res, "No se pudo cambiar el link público."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
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

// ── CREAR EL USUARIO DE UN PROFESIONAL ───────────────────────
// Un profesional aprobado no puede ingresar hasta que tiene usuario. Un solo pedido crea el usuario y lo vincula
// a su ficha (POST /profesionales/{id}/usuario): en dos pasos sueltos, un usuario sin vínculo entra como
// configurador. La contraseña temporal se muestra una sola vez y no queda guardada en ningún lado.
function sugerirUsuario(p) {
    const base = [p?.nombre, p?.apellido].filter(Boolean).join(".");
    return YS.normalizar(base).replace(/\s+/g, ".").replace(/[^a-z0-9._-]/g, "").slice(0, 50);
}

// datos: la ficha del profesional si no está en window._profesionales (por ejemplo, recién aprobado desde Solicitudes)
function abrirModalCrearUsuario(idProf, datos) {
    const prof = datos || window._profesionales?.find(p => p.idProfesional === idProf);
    const nombre = YS.nombreCompleto(prof) || "este profesional";
    abrirModalCaso("Crear usuario", "Crear usuario", `
        <p>Se crea el usuario con el que <strong>${YS.esc(nombre)}</strong> ingresa a YoSoy y queda vinculado a su ficha.</p>
        <div class="ui-form-grid">
            <div class="ui-campo ui-campo--ancho"><label for="campoUsuarioNombre">Nombre de usuario</label>
                <input type="text" id="campoUsuarioNombre" class="ui-input" maxlength="50" autocomplete="off" value="${YS.esc(sugerirUsuario(prof))}">
                <div class="ui-campo-ayuda">De 3 a 50 caracteres: letras, números, punto, guion o guion bajo.</div></div>
            <div class="ui-campo ui-campo--ancho"><label for="campoUsuarioMail">Mail</label>
                <input type="email" id="campoUsuarioMail" class="ui-input" maxlength="100" autocomplete="off" value="${YS.esc(prof?.mail || "")}">
                <div class="ui-campo-ayuda">Tiene que ser único. Con este mail el profesional puede recuperar su contraseña.</div></div>
            <div class="ui-campo ui-campo--ancho">
                <label class="ui-check"><input type="checkbox" id="campoUsuarioGenerar" checked onchange="alternarClaveManual()"> Generar una contraseña temporal</label>
                <div class="ui-campo-ayuda">Se muestra una sola vez, al terminar. Si no, escribí una (al menos 8 caracteres).</div></div>
            <div class="ui-campo ui-campo--ancho oculto" id="grupoClaveManual"><label for="campoUsuarioClave">Contraseña</label>
                <input type="password" id="campoUsuarioClave" class="ui-input" maxlength="100" autocomplete="new-password"></div>
        </div>`, () => confirmarCrearUsuario(idProf, nombre));
}

function alternarClaveManual() {
    const generar = document.getElementById("campoUsuarioGenerar").checked;
    document.getElementById("grupoClaveManual").classList.toggle("oculto", generar);
    if (!generar) document.getElementById("campoUsuarioClave").focus();
}

async function confirmarCrearUsuario(idProf, nombre) {
    const generar = document.getElementById("campoUsuarioGenerar").checked;
    const reglas = [
        { id: "campoUsuarioNombre", requerido: "Escribí un nombre de usuario.", patron: /^[a-zA-Z0-9_.-]{3,50}$/,
          mensajePatron: "De 3 a 50 caracteres: letras, números, punto, guion o guion bajo." },
        { id: "campoUsuarioMail", requerido: "Escribí el mail.", patron: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, mensajePatron: "Escribí un mail válido." }
    ];
    if (!generar) reglas.push({ id: "campoUsuarioClave", requerido: "Escribí una contraseña.", minimo: 8, mensajeMinimo: "Tiene que tener al menos 8 caracteres." });
    if (!YS.form.validar(reglas)) return;
    try {
        const res = await YS.fetch(`/profesionales/${idProf}/usuario`, {
            method: "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                username: document.getElementById("campoUsuarioNombre").value.trim(),
                mail: document.getElementById("campoUsuarioMail").value.trim(),
                generarContrasena: generar,
                contrasena: generar ? null : document.getElementById("campoUsuarioClave").value
            })
        });
        if (res.status === 201) {
            const d = await res.json();
            mostrarUsuarioCreado(nombre, d);
            if (YS.shell.actual === "usuarios") mostrarTab("usuarios");
        } else toast(await YS.leerMensaje(res, "No se pudo crear el usuario."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

function mostrarUsuarioCreado(nombre, d) {
    const clave = d.contrasenaTemporal
        ? `<div class="ui-campo ui-campo--ancho"><label>Contraseña temporal</label>
              <div class="ui-flex">
                  <code id="claveTemporal" data-secreto="1" style="font-family: var(--mono); font-size: 1.1em; user-select: all">${YS.esc(d.contrasenaTemporal)}</code>
                  <button type="button" class="ui-btn ui-btn--sm" onclick="YS.copiar(document.getElementById('claveTemporal').textContent, 'Contraseña copiada')">Copiar</button>
              </div>
              <div class="ui-campo-ayuda">Se muestra <strong>una sola vez</strong>: no queda guardada en ningún lado. Pasásela al profesional por un medio seguro. Si la pierde, puede pedir una nueva con "Olvidé mi contraseña" (le llega a su mail).</div>
           </div>`
        : `<p class="ui-muted">La contraseña es la que escribiste: no se vuelve a mostrar.</p>`;
    abrirModalCaso("Usuario creado", "Listo", `
        <p>El usuario <strong>${YS.esc(d.username)}</strong> quedó creado y vinculado a <strong>${YS.esc(nombre)}</strong>. Ya puede ingresar con el rol de profesional.</p>
        ${clave}`, () => cerrarModalCaso());
}

// ── SOLICITUDES DE REGISTRO ──────────────────────────────────
async function renderSolicitudes(c) {
    const datos = await YS.fetch(`/profesionales/solicitudes`).then(r => r.json());
    window._solicitudes = datos;
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
    const [pacientes, profesionales, especialidades, grupos] = await Promise.all([
        YS.api("/pacientes").catch(() => []), YS.api("/profesionales").catch(() => []),
        YS.api("/especialidades").catch(() => []), YS.api("/grupos").catch(() => [])
    ]);
    const pac = new Map(pacientes.map(p => [p.idPaciente, YS.nombreCompleto(p)]));
    const pro = new Map(profesionales.map(p => [p.idProfesional, YS.nombreCompleto(p)]));
    const esp = new Map(especialidades.map(e => [e.idEspecialidad, e.nombre]));
    const grp = new Map(grupos.map(g => [g.idGrupo, g.nombre]));
    return {
        profesionales, especialidades, grupos,
        paciente: id => pac.get(id) || (id ? `Paciente #${id}` : "—"),
        profesional: id => pro.get(id) || (id ? `Profesional #${id}` : "—"),
        especialidad: id => esp.get(id) || (id ? `Especialidad #${id}` : "—"),
        grupo: id => grp.get(id) || (id ? `Grupo #${id}` : "—")
    };
}

function auditorHeaders() {
    return { "Content-Type": "application/json", "X-Usuario-Id": localStorage.getItem("idUsuario") ?? "0" };
}

async function aprobarSolicitud(id) {
    if (!await confirmar("¿Aprobar esta solicitud? El profesional queda activo, pero todavía no puede ingresar: hay que crearle el usuario.")) return;
    const res = await YS.fetch(`/profesionales/${id}/aprobar`, { method: "PUT", headers: auditorHeaders() });
    if (res.ok) toast("Solicitud aprobada.");
    else toast(await YS.leerMensaje(res, "No se pudo aprobar la solicitud."), "error");
    mostrarTab("solicitudes");
    if (res.ok && await confirmar("¿Querés crearle el usuario ahora para que pueda ingresar? También podés hacerlo después desde Pacientes y profesionales.",
        { titulo: "Crear usuario", textoConfirmar: "Crear usuario" })) {
        abrirModalCrearUsuario(id, (window._solicitudes || []).find(p => p.idProfesional === id));
    }
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
                   ok: "Profesional asignado.", error: "No se pudo asignar el profesional.", volver: "asignaciones" };
    } else if (window._modoDirecto) {
        const idOrigen  = parseInt(document.getElementById("selectProfOrigen").value);
        const idDestino = parseInt(document.getElementById("selectProfDestino").value);
        const motivo    = document.getElementById("motivoCambioDir").value.trim() || null;
        if (!idDestino) { YS.form.marcar("selectProfDestino", "Elegí el nuevo profesional."); return; }
        pedido = { url: `${API}/derivaciones/cambio-directo`, method: "POST",
                   body: { idPaciente: window._cambioPaciente, idProfesionalOrigen: idOrigen, idProfesionalDestino: idDestino, motivo },
                   ok: "Cambio hecho. El nuevo profesional quedó asignado.", error: "No se pudo hacer el cambio.", volver: "asignaciones" };
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
    estado: "Estado", nroAfiliado: "N.º de afiliado", planCobertura: "Plan", consulta: "Consulta",
    idEspecialidad: "Especialidad solicitada", idGrupoDestino: "Grupo destino", prioridad: "Prioridad", activa: "Activa"
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

// Roles y permisos: la misma vista sirve para las dos auditorías. Qué registros llegan lo decide el
// servidor según el módulo (Configurador: administrativa; Profesional auditor: clínica).
// No hay botones para modificar ni borrar: la base no lo permite (RN-18).
async function renderAuditoria(c, pagina = 1, tab = "auditoria") {
    const datos = await YS.api(`/auditoria?pagina=${pagina}&cantidad=50`);
    if (YS.shell.actual !== tab) return;
    YS.shell.encabezado({ subtitulo: `Página ${pagina} · los más recientes primero · solo lectura, nadie puede modificar ni borrar estos registros` });
    YS.shell.acciones([{ texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab(tab) }]);

    const nombreUsuario = new Map(datos.filter(d => d.nombreUsuario).map(d => [d.idUsuario, d.nombreUsuario]));

    const anterior = YS.el("button", { class: "ui-btn ui-btn--sm", type: "button", text: "Más recientes", disabled: pagina <= 1,
        on: { click: () => renderAuditoria(c, pagina - 1, tab) } });
    const siguiente = YS.el("button", { class: "ui-btn ui-btn--sm", type: "button", text: "Más antiguos", disabled: datos.length < 50,
        on: { click: () => renderAuditoria(c, pagina + 1, tab) } });
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
            { titulo: "Usuario", html: d => celda(d.idUsuario == null ? "Sin sesión" : nombreUsuario.get(d.idUsuario) || `#${d.idUsuario}`), orden: d => nombreUsuario.get(d.idUsuario) || "" },
            { titulo: "Módulo", html: d => celda(d.espacio), orden: d => d.espacio || "" }
        ]
    });
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
        .map(function(p) { return '<option value="' + p.idProfesional + '">' + YS.esc(p.nombre) + '</option>'; })
        .join('');

    if (YS.shell.actual !== "reportesgenerales") return;
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

// Roles y permisos: la entrevista la hace un profesional con el privilegio Admisión, con su propia
// sesión. Antes el Configurador reemplazaba el idProfesional guardado en el navegador por el
// del profesional "ADM-001" para poder entrar a la sala; eso ya no hace falta.
async function iniciarEntrevistaVideo(codigoSala) {
    if (codigoSala === "" || codigoSala === "null" || codigoSala === "-") { toast("Este informe no tiene codigo de sala.", "warn"); return; }
    window.open("../Pantalla_Profesional/sesion_video.html?sala=" + encodeURIComponent(codigoSala), "_blank");
}

// ── ENCABEZADO DE LISTADOS ───────────────────────────────────
// Cantidad en el subtítulo y "Actualizar" como acción. Devuelve false si el usuario
// ya pasó a otra sección mientras se cargaban los datos (para no pisarla).
function encabezadoListado(tab, cantidad, palabra = "registros", extra = "") {
    if (YS.shell.actual !== tab) return false;
    const singular = { registros: "registro", pendientes: "pendiente", solicitudes: "solicitud", "usuarios del equipo": "usuario del equipo" };
    const texto = `${cantidad} ${cantidad === 1 ? (singular[palabra] || palabra) : palabra}`;
    YS.shell.encabezado({ subtitulo: [texto, extra].filter(Boolean).join(" · ") });
    YS.shell.acciones([{ texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab(tab) }]);
    return true;
}

// ── INICIO: resumen de lo que está pendiente ─────────────────
async function inicioConfigurador() {
    const c = document.getElementById("contenido");
    c.innerHTML = YS.esqueleto.bloques(4);
    saludar();

    const pedir = ruta => YS.api(ruta).catch(() => null);
    const [solicitudes, recuperacion, accesos, pacientes, profesionales] = await Promise.all([
        pedir("/profesionales/solicitudes"), pedir("/recuperacion-password/pendientes"), pedir("/usuarios/accesos"),
        pedir("/pacientes"), pedir("/profesionales")
    ]);
    if (YS.shell.actual !== "inicio") return;

    const cuenta = (lista, filtro = () => true) => Array.isArray(lista) ? lista.filter(filtro).length : null;
    c.innerHTML = `
        <div class="ui-titulo-seccion texto-chico ui-muted">Para revisar</div>
        <div class="ui-stats">
            ${tarjetaInicio("solicitudes", "Solicitudes de registro", "usuario-mas", cuenta(solicitudes), "Profesionales esperando aprobación", true)}
            ${tarjetaInicio("recuperacion", "Recuperación de contraseña", "candado", cuenta(recuperacion), "Pedidos de contraseña temporal", true)}
            ${tarjetaInicio("permisos", "Usuarios sin permisos", "escudo", cuenta(accesos, a => a.sinPermisos), "No ven nada hasta que les asignes un módulo", true)}
            ${tarjetaInicio("permisos", "Invitaciones sin confirmar", "usuario-mas", cuenta(accesos, a => a.estado === "Pendiente"), "Todavía no eligieron su contraseña", true)}
        </div>
        <div class="ui-titulo-seccion texto-chico ui-muted">Cuentas</div>
        <div class="ui-stats">
            ${tarjetaInicio("permisos", "Usuarios del equipo", "escudo", cuenta(accesos), "Con o sin permisos asignados")}
            ${tarjetaInicio("usuarios", "Pacientes activos", "pacientes", cuenta(pacientes, p => p.estado !== "Inactivo"), "Sin contar los dados de baja")}
            ${tarjetaInicio("usuarios", "Profesionales activos", "usuario", cuenta(profesionales, p => p.estado === "Activo"), "Con cuenta aprobada")}
        </div>
        <div class="inicio-ir-auditoria">
            <a class="ui-btn ui-btn--sm" href="#auditoria">${YS.icono("lista")}Ver auditoría administrativa</a>
        </div>`;
}

// ── INICIO de CRM y atención al cliente ─────────────────────
async function inicioCrm() {
    const c = document.getElementById("contenido");
    c.innerHTML = YS.esqueleto.bloques(4);
    saludar();

    const pedir = ruta => YS.api(ruta).catch(() => null);
    const conAgendas = YS.sesion.tienePrivilegio("gestion_agendas");
    const conChats = (await YS.shell.capacidades)?.mensajeria;
    const [derivaciones, casos, chats, turnos] = await Promise.all([
        pedir("/casosderivaciones/pendientes"), pedir("/casos?sinAsignar=true"),
        conChats ? pedir("/mensajeria/conversaciones") : null, conAgendas ? pedir("/turnos") : null
    ]);
    if (YS.shell.actual !== "inicio") return;

    const cuenta = (lista, filtro = () => true) => Array.isArray(lista) ? lista.filter(filtro).length : null;
    const hoy = new Date();
    const nUrgentes = cuenta(chats, ch => ch.estado === "Urgente");
    const tarjetaChats = (filtro, etiqueta, valor, pie, peligro) => `
        <button class="ui-stat${valor ? (peligro ? " ui-stat--danger" : " ui-stat--warn") : ""}" type="button" onclick="irAChatsFiltrados('${filtro}')">
            <span class="ui-stat-etiqueta">${YS.icono("chat")}${YS.esc(etiqueta)}</span>
            <span class="ui-stat-valor">${valor ?? "—"}</span>
            <span class="ui-stat-pie">${YS.esc(pie)}</span>
        </button>`;
    c.innerHTML = `
        <div class="ui-titulo-seccion texto-chico ui-muted">Para revisar</div>
        <div class="ui-stats">
            ${conChats ? tarjetaChats("noLeidas", "Chats sin leer", cuenta(chats, ch => ch.noLeidos > 0), "Conversaciones con mensajes nuevos") : ""}
            ${conChats ? tarjetaChats("derivadas", "Chats escalados", cuenta(chats, ch => ch.estado === "DerivadaHumano" || ch.estado === "Urgente"),
                nUrgentes ? `${nUrgentes} urgente${nUrgentes === 1 ? "" : "s"}` : "Esperan a una persona del equipo", nUrgentes > 0) : ""}
            ${tarjetaInicio("casosderivaciones", "Derivaciones por confirmar", "flechas", cuenta(derivaciones), "Casos con grupo y profesional sugeridos", true)}
            ${tarjetaInicio("casos", "Casos sin asignar", "portapapeles", cuenta(casos), "Todavía sin profesional", true)}
        </div>
        ${conAgendas ? `
        <div class="ui-titulo-seccion texto-chico ui-muted">Hoy</div>
        <div class="ui-stats">
            ${tarjetaInicio("turnos", "Turnos de hoy", "reloj", cuenta(turnos, t => YS.fecha.mismoDia(t.fecha, hoy) && t.idEstadoTurno !== 3), "Sin contar los cancelados")}
        </div>` : ""}`;
}

function saludar() {
    YS.shell.usuario.then(u => {
        if (YS.shell.actual === "inicio")
            YS.shell.encabezado({ titulo: u.nombre ? `Hola, ${u.nombre}` : "Inicio", subtitulo: YS.capitalizar(YS.fecha.larga(new Date())) });
    });
}

function tarjetaInicio(seccion, etiqueta, icono, valor, pie, alerta = false) {
    return `
        <a class="ui-stat${alerta && valor ? " ui-stat--warn" : ""}" href="#${seccion}">
            <span class="ui-stat-etiqueta">${YS.icono(icono)}${YS.esc(etiqueta)}</span>
            <span class="ui-stat-valor">${valor ?? "—"}</span>
            <span class="ui-stat-pie">${YS.esc(pie)}</span>
        </a>`;
}

// Un usuario del módulo Profesional sin Admisión ni auditor no tiene nada en esta pantalla:
// vuelve a su panel (por ejemplo, si le quitaron el privilegio y tenía guardado el link).
function sinSecciones() {
    if (MODULO === "profesional") { location.replace(YS.rutas.profesional); return; }
    document.getElementById("contenido").innerHTML = YS.vacio({
        icono: "escudo", titulo: "No tenés secciones de gestión",
        texto: "Admisión y Auditoría se habilitan con el privilegio o el permiso correspondiente."
    });
}

// ══════════════════════════════════════════════════════════════
// FASE 2: Grupos, Reglas de derivación, Casos y Derivaciones
// ══════════════════════════════════════════════════════════════
const TONO_CASO = { Nuevo: "neutral", Derivado: "info", Asignado: "ok", "En atención": "ok", Cerrado: "neutral" };
function chipCaso(estado) { return `<span class="ui-chip ui-chip--${TONO_CASO[estado] || "neutral"}">${YS.esc(estado)}</span>`; }
const TEXTO_ORIGEN_DERIV = { Regla: "Regla de derivación", Manual: "Elegido a mano", Bot: "Bot" };
const TEXTO_MOTIVO_HIST = {
    AutomaticaPorCarga: "Automática (menor carga)", AutomaticaAleatoria: "Automática (sorteo entre empatados)",
    Manual: "Asignación manual", SolicitudCambio: "Solicitud de cambio de profesional", SinDisponibles: "Nadie disponible"
};
function datoCasoHtml(etiqueta, html) {
    return `<div class="ui-dato"><span class="etiqueta-dato">${YS.esc(etiqueta)}</span><span class="valor-dato">${html}</span></div>`;
}

// ── GRUPOS ───────────────────────────────────────────────────
async function renderGrupos(c) {
    const [grupos, profesionales] = await Promise.all([YS.api("/grupos"), YS.api("/profesionales")]);
    if (YS.shell.actual !== "grupos") return;
    window._grupos = grupos; window._profesionalesGrupos = profesionales;
    YS.shell.encabezado({ subtitulo: `${grupos.length} grupo${grupos.length === 1 ? "" : "s"}` });
    YS.shell.acciones([
        { texto: "Nuevo grupo", icono: "mas", alClick: () => abrirModalGrupo() },
        { texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab("grupos") }
    ]);
    c.innerHTML = `
        <div class="split-view">
            <section class="split-col" aria-labelledby="tituloGrupos">
                <div class="split-header"><span id="tituloGrupos">Grupos</span> <span class="split-badge" id="badgeGrupos"></span></div>
                <div class="split-list" id="listaGrupos"></div>
            </section>
            <section class="split-col" aria-labelledby="tituloMiembros">
                <div class="split-header"><span id="tituloMiembros">Miembros</span> <span class="split-badge" id="badgeMiembros"></span></div>
                <div class="split-list" id="listaMiembros"></div>
            </section>
        </div>
        <div class="split-info" id="grupoInfo" aria-live="polite"></div>`;
    renderListaGrupos();
}

function renderListaGrupos() {
    const grupos = window._grupos;
    document.getElementById("badgeGrupos").textContent = grupos.length;
    document.getElementById("listaGrupos").innerHTML = grupos.map(g => `
        <button type="button" class="usr-card${g.activo ? "" : " usr-card-inactivo"}" id="grupo-${g.idGrupo}" onclick="seleccionarGrupo(${g.idGrupo})">
            ${YS.avatar(g.nombre, g.idGrupo, "sm")}
            <span class="usr-textos"><span class="usr-nombre">${YS.esc(g.nombre)}</span><span class="usr-sub">${g.activo ? "Activo" : "Inactivo"}</span></span>
        </button>`).join("") || `<div class="split-vacio">${YS.vacio({ icono: "pacientes", titulo: "No hay grupos todavía", texto: "Creá el primero para empezar a derivar casos.", accion: { texto: "Nuevo grupo", onclick: "abrirModalGrupo()" } })}</div>`;
    if (window._grupoSeleccionado && grupos.some(g => g.idGrupo === window._grupoSeleccionado)) seleccionarGrupo(window._grupoSeleccionado);
    else {
        document.getElementById("listaMiembros").innerHTML = "";
        document.getElementById("badgeMiembros").textContent = "";
        document.getElementById("grupoInfo").innerHTML = '<span class="ui-muted">Elegí un grupo para ver y gestionar sus miembros.</span>';
    }
}

async function seleccionarGrupo(id) {
    window._grupoSeleccionado = id;
    document.querySelectorAll("#listaGrupos .usr-card").forEach(el => el.classList.toggle("seleccionado", el.id === `grupo-${id}`));
    const g = window._grupos.find(x => x.idGrupo === id);
    const miembros = await YS.api(`/grupos/${id}/miembros`).catch(() => []);
    if (window._grupoSeleccionado !== id || YS.shell.actual !== "grupos") return;

    document.getElementById("badgeMiembros").textContent = miembros.length;
    document.getElementById("listaMiembros").innerHTML = miembros.map(p => `
        <div class="usr-card" style="cursor:default">
            ${YS.avatar(YS.nombreCompleto(p), "p" + p.idProfesional, "sm")}
            <span class="usr-textos"><span class="usr-nombre">${YS.esc(YS.nombreCompleto(p))}</span><span class="usr-sub">${YS.esc(p.especialidad || "")}</span></span>
            <button type="button" class="ui-btn ui-btn--sm ui-btn--ghost ui-btn--icono btn-icono-peligro" title="Quitar del grupo" aria-label="Quitar a ${YS.esc(YS.nombreCompleto(p))} del grupo" onclick="quitarMiembroGrupo(${id}, ${p.idProfesional})">${YS.icono("cerrar")}</button>
        </div>`).join("") || `<div class="split-vacio">${YS.vacio({ icono: "pacientes", titulo: "Sin miembros", texto: "Agregá profesionales para que puedan recibir casos de este grupo." })}</div>`;

    const disponibles = window._profesionalesGrupos.filter(p => p.estado === "Activo" && !miembros.some(m => m.idProfesional === p.idProfesional));
    const opciones = disponibles.map(p => `<option value="${p.idProfesional}">${YS.esc(YS.nombreCompleto(p))}${p.especialidad ? " — " + YS.esc(p.especialidad) : ""}</option>`).join("");
    document.getElementById("grupoInfo").innerHTML = `
        <div class="split-info-texto"><strong>${YS.esc(g.nombre)}</strong>${g.descripcion ? " — " + YS.esc(g.descripcion) : ""}</div>
        <div class="split-info-acciones">
            ${campoSelect("selectAgregarMiembro", "Agregar profesional", opcionesConElegir(opciones, "No hay más profesionales activos para agregar"))}
            <button class="ui-btn ui-btn--sm" onclick="agregarMiembroGrupo(${id})">Agregar</button>
            <button class="ui-btn ui-btn--sm" onclick="abrirModalGrupo(${id})">Editar</button>
            <button class="ui-btn ui-btn--sm ${g.activo ? "ui-btn--danger" : "ui-btn--ok"}" onclick="cambiarActivoGrupo(${id}, ${!g.activo})">${g.activo ? "Desactivar" : "Activar"}</button>
        </div>`;
}

async function agregarMiembroGrupo(idGrupo) {
    const sel = document.getElementById("selectAgregarMiembro");
    const idProf = parseInt(sel?.value);
    if (!idProf) { toast("Elegí un profesional para agregar.", "error"); return; }
    try {
        const res = await YS.fetch(`/grupos/${idGrupo}/miembros`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ idProfesional: idProf }) });
        if (res.ok || res.status === 204) { toast("Profesional agregado al grupo."); seleccionarGrupo(idGrupo); }
        else toast(await YS.leerMensaje(res, "No se pudo agregar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

async function quitarMiembroGrupo(idGrupo, idProfesional) {
    if (!await confirmar("¿Quitar a este profesional del grupo? No afecta los casos que ya tiene asignados.", { titulo: "Quitar del grupo", textoConfirmar: "Quitar", peligro: true })) return;
    try {
        const res = await YS.fetch(`/grupos/${idGrupo}/miembros/${idProfesional}`, { method: "DELETE" });
        if (res.ok || res.status === 204) { toast("Profesional quitado del grupo."); seleccionarGrupo(idGrupo); }
        else toast(await YS.leerMensaje(res, "No se pudo quitar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

async function cambiarActivoGrupo(id, activo) {
    const verbo = activo ? "activar" : "desactivar";
    if (!await confirmar(`¿${YS.capitalizar(verbo)} este grupo?`, { titulo: YS.capitalizar(verbo) + " grupo", textoConfirmar: YS.capitalizar(verbo) })) return;
    try {
        const res = await YS.fetch(`/grupos/${id}/${verbo}`, { method: "PUT" });
        if (res.ok || res.status === 204) { toast(`Grupo ${activo ? "activado" : "desactivado"}.`); mostrarTab("grupos"); }
        else toast(await YS.leerMensaje(res, "No se pudo guardar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

function abrirModalGrupo(id) {
    const g = id ? window._grupos.find(x => x.idGrupo === id) : null;
    abrirModalCaso(g ? "Editar grupo" : "Nuevo grupo", g ? "Guardar" : "Crear", `
        <div class="ui-form-grid">
            <div class="ui-campo ui-campo--ancho"><label for="campoGrupoNombre">Nombre</label>
                <input type="text" id="campoGrupoNombre" class="ui-input" value="${YS.esc(g?.nombre || "")}"></div>
            <div class="ui-campo ui-campo--ancho"><label for="campoGrupoDescripcion">Descripción (opcional)</label>
                <textarea id="campoGrupoDescripcion" class="ui-textarea" rows="2">${YS.esc(g?.descripcion || "")}</textarea></div>
        </div>`, () => guardarGrupo(id));
}

async function guardarGrupo(id) {
    const input = document.getElementById("campoGrupoNombre");
    const nombre = input.value.trim();
    if (!nombre) { YS.form.marcar(input, "El nombre es obligatorio."); input.focus(); return; }
    const descripcion = document.getElementById("campoGrupoDescripcion").value.trim() || null;
    try {
        const res = await YS.fetch(id ? `/grupos/${id}` : "/grupos", {
            method: id ? "PUT" : "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ nombre, descripcion })
        });
        if (res.ok || res.status === 201) { cerrarModalCaso(); toast(id ? "Grupo guardado." : "Grupo creado."); mostrarTab("grupos"); }
        else toast(await YS.leerMensaje(res, "No se pudo guardar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

// ── REGLAS DE DERIVACIÓN ─────────────────────────────────────
async function renderReglasDerivacion(c) {
    const [reglas, especialidades, grupos] = await Promise.all([YS.api("/reglasderivacion"), YS.api("/especialidades"), YS.api("/grupos")]);
    if (YS.shell.actual !== "reglasderivacion") return;
    window._reglasActuales = reglas; window._reglasEspecialidades = especialidades; window._reglasGrupos = grupos;
    const esp = new Map(especialidades.map(e => [e.idEspecialidad, e.nombre]));
    const grp = new Map(grupos.map(g => [g.idGrupo, g.nombre]));

    YS.shell.encabezado({ subtitulo: `${reglas.length} regla${reglas.length === 1 ? "" : "s"}` });
    YS.shell.acciones([
        { texto: "Nueva regla", icono: "mas", alClick: () => abrirModalRegla() },
        { texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab("reglasderivacion") }
    ]);

    YS.tabla(c, {
        filas: reglas, unidad: ["regla", "reglas"], placeholder: "Buscar por especialidad o grupo",
        buscar: r => [esp.get(r.idEspecialidad), grp.get(r.idGrupoDestino)].join(" "),
        filtros: [
            { id: "activas", texto: "Activas", pasa: r => r.activa },
            { id: "todas", texto: "Todas", pasa: () => true },
            { id: "inactivas", texto: "Inactivas", pasa: r => !r.activa }
        ],
        ordenInicial: { col: 2, desc: true },
        vacio: { icono: "flechas", titulo: "No hay reglas de derivación", texto: "Sin una regla, un caso nuevo queda sin grupo hasta que alguien lo derive a mano.", accion: { texto: "Nueva regla", onclick: "abrirModalRegla()" } },
        columnas: [
            { titulo: "Si la especialidad solicitada es…", html: r => `<span class="principal">${celda(esp.get(r.idEspecialidad))}</span>`, orden: r => YS.normalizar(esp.get(r.idEspecialidad) || "") },
            { titulo: "…el grupo es", html: r => `<span class="principal">${celda(grp.get(r.idGrupoDestino))}</span>`, orden: r => YS.normalizar(grp.get(r.idGrupoDestino) || "") },
            { titulo: "Prioridad", html: r => `<span class="ui-num">${r.prioridad}</span>`, orden: r => r.prioridad },
            { titulo: "Estado", html: r => r.activa ? '<span class="ui-chip ui-chip--ok">Activa</span>' : '<span class="ui-chip ui-chip--neutral">Inactiva</span>', orden: r => r.activa ? 1 : 0 },
            { titulo: "", clase: "acciones", html: r => `
                <button class="ui-btn ui-btn--sm ui-btn--ghost ui-btn--icono" title="Editar" aria-label="Editar regla" onclick="abrirModalRegla(${r.idRegla})">${YS.icono("lapiz")}</button>
                <button class="ui-btn ui-btn--sm" onclick="cambiarActivaRegla(${r.idRegla}, ${!r.activa})">${r.activa ? "Desactivar" : "Activar"}</button>` }
        ]
    });
}

function abrirModalRegla(id) {
    const r = id ? window._reglasActuales.find(x => x.idRegla === id) : null;
    const opcEsp = window._reglasEspecialidades.filter(e => e.estado === "Activo" || r?.idEspecialidad === e.idEspecialidad)
        .map(e => `<option value="${e.idEspecialidad}"${e.idEspecialidad === r?.idEspecialidad ? " selected" : ""}>${YS.esc(e.nombre)}</option>`).join("");
    const opcGrp = window._reglasGrupos.filter(g => g.activo || r?.idGrupoDestino === g.idGrupo)
        .map(g => `<option value="${g.idGrupo}"${g.idGrupo === r?.idGrupoDestino ? " selected" : ""}>${YS.esc(g.nombre)}</option>`).join("");
    abrirModalCaso(r ? "Editar regla" : "Nueva regla", r ? "Guardar" : "Crear", `
        <div class="ui-form-grid">
            ${campoSelect("campoReglaEspecialidad", "Si la especialidad solicitada es…", opcEsp || '<option value="">No hay especialidades activas</option>')}
            ${campoSelect("campoReglaGrupo", "…el grupo es", opcGrp || '<option value="">No hay grupos activos — creá uno primero</option>')}
            <div class="ui-campo"><label for="campoReglaPrioridad">Prioridad</label>
                <input type="text" id="campoReglaPrioridad" class="ui-input" inputmode="numeric" value="${r?.prioridad ?? 0}">
                <div class="ui-campo-ayuda">Si hay más de una regla activa para la misma especialidad, gana la de mayor prioridad.</div></div>
        </div>`, () => guardarRegla(id));
}

async function guardarRegla(id) {
    const idEspecialidad = parseInt(document.getElementById("campoReglaEspecialidad").value);
    const idGrupoDestino = parseInt(document.getElementById("campoReglaGrupo").value);
    const prioridadInput = document.getElementById("campoReglaPrioridad");
    const prioridad = parseInt(prioridadInput.value);
    if (!idEspecialidad || !idGrupoDestino) { toast("Elegí una especialidad y un grupo.", "error"); return; }
    if (!Number.isInteger(prioridad)) { YS.form.marcar(prioridadInput, "Tiene que ser un número entero."); prioridadInput.focus(); return; }
    const actual = id ? window._reglasActuales.find(x => x.idRegla === id) : null;
    try {
        const res = id
            ? await YS.fetch(`/reglasderivacion/${id}`, { method: "PUT", headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ idRegla: id, idEspecialidad, idGrupoDestino, prioridad, activa: actual?.activa ?? true }) })
            : await YS.fetch("/reglasderivacion", { method: "POST", headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ idEspecialidad, idGrupoDestino, prioridad }) });
        if (res.ok || res.status === 201) { cerrarModalCaso(); toast(id ? "Regla guardada." : "Regla creada."); mostrarTab("reglasderivacion"); }
        else toast(await YS.leerMensaje(res, "No se pudo guardar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

async function cambiarActivaRegla(id, activa) {
    try {
        const res = await YS.fetch(`/reglasderivacion/${id}/${activa ? "activar" : "desactivar"}`, { method: "PUT" });
        if (res.ok || res.status === 204) { toast(`Regla ${activa ? "activada" : "desactivada"}.`); mostrarTab("reglasderivacion"); }
        else toast(await YS.leerMensaje(res, "No se pudo guardar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

// -- ETIQUETAS DE CONVERSACIONES (chats) -----------------------
// Paso 5 (cierre de "Mejora de chats y centrado"): catalogo chico, Configurador unico que
// lo administra - crear, renombrar, desactivar. No usa el motor generico de renderTabla
// (campos/columnas/idCampo/guardar/eliminar): la API vive bajo /mensajeria, no en la raiz
// como el resto de esas tablas, y el patron de YS.tabla + funciones propias (igual que
// Grupos/Reglas de derivacion mas arriba) es mas simple que agregarle una excepcion de ruta
// al motor compartido.
const TONOS_ETIQUETA = ["marca", "warn", "danger", "neutral", "ok"];
const TEXTO_TONO_ETIQUETA = { marca: "Marca", warn: "Advertencia", danger: "Riesgo", neutral: "Neutro", ok: "Resuelto" };

async function renderEtiquetas(c) {
    const etiquetas = await YS.api("/mensajeria/etiquetas");
    if (YS.shell.actual !== "etiquetas") return;
    window._etiquetasConversacion = etiquetas;
    YS.shell.encabezado({ subtitulo: `${etiquetas.length} etiqueta${etiquetas.length === 1 ? "" : "s"}` });
    YS.shell.acciones([
        { texto: "Nueva etiqueta", icono: "mas", alClick: () => abrirModalEtiqueta() },
        { texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab("etiquetas") }
    ]);

    YS.tabla(c, {
        filas: etiquetas, unidad: ["etiqueta", "etiquetas"], placeholder: "Buscar por nombre",
        buscar: e => e.nombre,
        vacio: { icono: "etiqueta", titulo: "Todavía no hay etiquetas", texto: "Las etiquetas ayudan a clasificar conversaciones del equipo (por ejemplo, Riesgo o Seguimiento).", accion: { texto: "Nueva etiqueta", onclick: "abrirModalEtiqueta()" } },
        columnas: [
            { titulo: "Nombre", html: e => `<span class="principal">${celda(e.nombre)}</span>`, orden: e => YS.normalizar(e.nombre) },
            { titulo: "Tono", html: e => `<span class="ui-chip ui-chip--${e.tono}">${YS.esc(TEXTO_TONO_ETIQUETA[e.tono] || e.tono)}</span>`, orden: e => e.tono },
            { titulo: "", clase: "acciones", html: e => `
                <button class="ui-btn ui-btn--sm ui-btn--ghost ui-btn--icono" title="Editar" aria-label="Editar etiqueta" onclick="abrirModalEtiqueta(${e.idEtiqueta})">${YS.icono("lapiz")}</button>
                <button class="ui-btn ui-btn--sm ui-btn--ghost ui-btn--icono btn-icono-peligro" title="Desactivar" aria-label="Desactivar etiqueta" onclick="desactivarEtiqueta(${e.idEtiqueta})">${YS.icono("papelera")}</button>` }
        ]
    });
}

function abrirModalEtiqueta(id) {
    const e = id ? window._etiquetasConversacion.find(x => x.idEtiqueta === id) : null;
    abrirModalCaso(e ? "Editar etiqueta" : "Nueva etiqueta", e ? "Guardar" : "Crear", `
        <div class="ui-form-grid">
            <div class="ui-campo ui-campo--ancho"><label for="campoEtiquetaNombre">Nombre</label>
                <input type="text" id="campoEtiquetaNombre" class="ui-input" maxlength="40" value="${YS.esc(e?.nombre || "")}"></div>
            <div class="ui-campo ui-campo--ancho"><label for="campoEtiquetaTono">Tono</label>
                <select id="campoEtiquetaTono" class="ui-select">
                    ${TONOS_ETIQUETA.map(t => `<option value="${t}"${(e?.tono || "neutral") === t ? " selected" : ""}>${YS.esc(TEXTO_TONO_ETIQUETA[t])}</option>`).join("")}
                </select></div>
        </div>`, () => guardarEtiqueta(id));
}

async function guardarEtiqueta(id) {
    const input = document.getElementById("campoEtiquetaNombre");
    const nombre = input.value.trim();
    if (!nombre) { YS.form.marcar(input, "El nombre es obligatorio."); input.focus(); return; }
    const tono = document.getElementById("campoEtiquetaTono").value;
    try {
        const res = await YS.fetch(id ? `/mensajeria/etiquetas/${id}` : "/mensajeria/etiquetas", {
            method: id ? "PUT" : "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ nombre, tono })
        });
        if (res.ok || res.status === 201) { cerrarModalCaso(); toast(id ? "Etiqueta guardada." : "Etiqueta creada."); mostrarTab("etiquetas"); }
        else toast(await YS.leerMensaje(res, "No se pudo guardar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

async function desactivarEtiqueta(id) {
    if (!await confirmar("Ya no va a poder asignarse a conversaciones nuevas (las que ya la tengan la conservan). ¿Desactivar esta etiqueta?", { titulo: "Desactivar etiqueta", textoConfirmar: "Desactivar", peligro: true })) return;
    try {
        const res = await YS.fetch(`/mensajeria/etiquetas/${id}`, { method: "DELETE" });
        if (res.ok || res.status === 204) { toast("Etiqueta desactivada."); mostrarTab("etiquetas"); }
        else toast(await YS.leerMensaje(res, "No se pudo desactivar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

// ── WHATSAPP ───────────────────────────────────────────
// Polling con setTimeout que se reprograma solo (mismo patrón que bandeja.js de
// Pantalla_ChatSimulado - no hay nada parecido ya en este archivo): 3s mientras no está
// conectado (para que el QR aparezca/se refresque rápido), 30s de heartbeat ya conectado
// (solo para notar una caída sin recargar la pantalla). Se frena solo si se navega a otra
// sección (no hay un "on leave" propio de mostrarTab) o si la pestaña queda oculta; al
// volver a quedar visible, el listener de visibilitychange lo reactiva.
let _waTimer = null;
let _waEstabaConectado = false;

async function renderWhatsapp(c) {
    _waEstabaConectado = false;
    c.innerHTML = `
        <div class="ui-card">
            <div class="ui-card-header"><h3 class="ui-card-titulo">WhatsApp</h3></div>
            <div id="waEstado"></div>
        </div>`;
    await waPulso();
}

function waDetenerPolling() {
    clearTimeout(_waTimer);
    _waTimer = null;
}

async function waPulso() {
    waDetenerPolling();
    if (YS.shell.actual !== "whatsapp") return;
    if (document.hidden) return; // lo reactiva el listener de visibilitychange de abajo

    let e;
    try { e = await YS.api("/mensajeria/whatsapp/estado"); }
    catch { e = { servicioDisponible: false, conectado: false, numero: null, qrPendiente: false, qr: null }; }
    if (YS.shell.actual !== "whatsapp") return;

    // Transición a conectado: se avisa al servidor una sola vez (no en cada vuelta del
    // polling); el propio endpoint vuelve a verificar el estado real antes de auditar, así
    // que no hace falta esperar la respuesta para seguir mostrando la pantalla.
    if (e.conectado && !_waEstabaConectado)
        YS.fetch("/mensajeria/whatsapp/confirmar-vinculacion", { method: "POST" }).catch(() => {});
    _waEstabaConectado = e.conectado;

    const v = YS.whatsappEstado.vista(e);
    document.getElementById("waEstado").innerHTML = waHtml(e, v);
    waActualizarEnfriamiento();

    // Conectado: heartbeat lento. Necesita revisión del servidor: nada que cambie solo, alcanza con mirar de vez en cuando.
    // El resto (conectando, QR, hay que vincular): rápido, para que el QR aparezca y el cambio de estado se note enseguida.
    const espera = v.clave === "conectado" ? 30000 : v.clave === "atencion" ? 10000 : 3000;
    _waTimer = setTimeout(waPulso, espera);
}

document.addEventListener("visibilitychange", () => {
    if (!document.hidden && YS.shell.actual === "whatsapp" && !_waTimer) waPulso();
});

const WA_CHIP = { ok: "ui-chip--ok", warn: "ui-chip--warn", danger: "ui-chip--danger", info: "ui-chip--info", neutral: "ui-chip--neutral" };

// "Conectado desde ..." con el desde del servicio (desde cuándo está en ese estado; se reinicia si el servicio se reinicia).
function waDesdeTexto(desde) {
    const d = desde ? new Date(desde) : null;
    if (!d || isNaN(d)) return "";
    return d.toLocaleString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

function waHtml(e, v) {
    v = v || YS.whatsappEstado.vista(e);
    const chip = (texto) => `<span class="ui-chip ${WA_CHIP[v.tono] || "ui-chip--neutral"}">${YS.esc(texto)}</span>`;
    const detalle = v.detalle ? `<p class="ui-muted mt-12">${YS.esc(v.detalle)}</p>` : "";

    if (v.clave === "conectado") {
        const desde = waDesdeTexto(e.desde);
        return `
            ${chip(`Conectado (+${e.numero || "?"})`)}
            ${desde ? `<p class="ui-muted mt-12">Conectado desde ${YS.esc(desde)}</p>` : ""}
            <p class="mt-12"><button class="ui-btn ui-btn--danger-solido" type="button" onclick="waDesvincular()">Desvincular y cambiar número</button></p>`;
    }
    if (v.clave === "escanear") {
        if (!v.mostrarQr) return `${chip(v.titulo)}${detalle}`;
        return `${chip(v.titulo)}
            <div class="wa-qr-caja"><img src="${e.qr}" alt="Código QR para vincular WhatsApp" class="wa-qr"></div>
            <p>Abrí WhatsApp en el chip dedicado → <b>Dispositivos vinculados</b> → <b>Vincular un dispositivo</b>, y escaneá este código.</p>
            <p class="ui-alerta ui-alerta--warn">Quien escanee este código controla ese WhatsApp — usar solo el chip dedicado de la clínica.</p>
            <p><button class="ui-btn ui-btn--ghost ui-btn--sm" type="button" data-wa-enfria="¿El código no funciona? Generar uno nuevo" onclick="waPedirQr(true)">¿El código no funciona? Generar uno nuevo</button></p>`;
    }
    if (v.clave === "vincular") {
        return `${chip(v.titulo)}${detalle}
            <p class="mt-12"><button class="ui-btn ui-btn--primary" type="button" data-wa-enfria="Mostrar código QR" onclick="waPedirQr(false)">Mostrar código QR</button></p>`;
    }
    // no-disponible, conectando, atencion: solo informan; en "atencion" no hay botón (reintentar no lo arregla y el servicio no lo reintenta solo).
    return `${chip(v.titulo)}${detalle}`;
}

// Enfriamiento del botón de código QR (30 s, el mismo que aplica la API): los botones con data-wa-enfria muestran la cuenta regresiva.
let _waEnfriaHasta = 0;
let _waTick = null;

function waActualizarEnfriamiento() {
    const falta = Math.max(0, Math.ceil((_waEnfriaHasta - Date.now()) / 1000));
    document.querySelectorAll("[data-wa-enfria]").forEach(b => {
        b.disabled = falta > 0;
        b.textContent = falta > 0 ? `Podés pedir otro código en ${falta} s` : b.dataset.waEnfria;
    });
    clearInterval(_waTick);
    _waTick = null;
    if (falta > 0 && YS.shell.actual === "whatsapp") _waTick = setInterval(waActualizarEnfriamiento, 1000);
}

async function waDesvincular() {
    if (!await confirmar("Se borra la sesión de WhatsApp de este servidor y el número deja de estar vinculado: mientras tanto los mensajes no salen ni llegan. " +
        "Para volver a usarlo vas a tener que escanear un código QR nuevo (con el mismo número u otro).",
        { titulo: "Desvincular WhatsApp", textoConfirmar: "Desvincular", peligro: true })) return;
    try {
        const res = await YS.fetch("/mensajeria/whatsapp/desvincular", { method: "POST" });
        if (res.ok) { toast("Desvinculado. Generando un código QR nuevo..."); _waEnfriaHasta = Date.now() + 30000; }
        else toast(await YS.leerMensaje(res, "No se pudo completar la acción."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
    await waPulso();
}

// nuevo = true: reemplazar el código que está esperando escaneo ("Generar uno nuevo"); false: pedir uno cuando venció o se cerró la sesión.
async function waPedirQr(nuevo) {
    if (_waEnfriaHasta > Date.now()) return;
    _waEnfriaHasta = Date.now() + 30000; // se asume el enfriamiento ya, para que un doble clic no pida dos veces
    waActualizarEnfriamiento();
    try {
        const res = await YS.fetch("/mensajeria/whatsapp/vincular", { method: "POST", json: { nuevo: !!nuevo } });
        if (res.ok) toast(nuevo ? "Generando un código QR nuevo..." : "Pidiendo el código QR...");
        else {
            if (res.status === 429) {
                const d = await res.clone().json().catch(() => null);
                _waEnfriaHasta = Date.now() + ((d && d.reintentarEnSegundos) || 30) * 1000;
            } else _waEnfriaHasta = 0; // no se llegó a abrir ninguna ventana: se puede volver a intentar
            toast(await YS.leerMensaje(res, "No se pudo pedir el código QR."), "error");
        }
    } catch { _waEnfriaHasta = 0; toast("No se pudo conectar con el servidor.", "error"); }
    await waPulso();
}

// ── CASOS ────────────────────────────────────────────────────
async function renderCasos(c, params = {}) {
    const [casos, grupos, profesionales, motivosReasignacion] = await Promise.all(
        [YS.api("/casos"), YS.api("/grupos"), YS.api("/profesionales"), YS.api("/casos/motivos-reasignacion")]);
    if (YS.shell.actual !== "casos") return;
    window._casosActuales = casos; window._casosGrupos = grupos; window._casosProfesionales = profesionales;
    window._motivosReasignacion = motivosReasignacion;

    YS.shell.encabezado({ subtitulo: `${casos.length} caso${casos.length === 1 ? "" : "s"}` });
    YS.shell.acciones([{ texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab("casos") }]);

    const sinAsignar = casos.filter(cs => !cs.idProfesionalAsignado && cs.estado !== "Cerrado");
    const aviso = sinAsignar.length
        ? `<div class="ui-alerta ui-alerta--warn mb-12">${sinAsignar.length} caso${sinAsignar.length === 1 ? "" : "s"} sin asignar.
            <button class="ui-btn ui-btn--sm" type="button" onclick="document.querySelector('[data-filtro=sinasignar]')?.click()">Ver</button></div>`
        : "";

    c.innerHTML = `${aviso}<div class="casos-layout"><div id="casosTabla"></div><aside class="casos-detalle" id="casosDetalle">${YS.vacio({ icono: "portapapeles", texto: "Elegí un caso para ver el detalle." })}</aside></div>`;

    YS.tabla(document.getElementById("casosTabla"), {
        filas: casos, unidad: ["caso", "casos"], placeholder: "Buscar por paciente, grupo o profesional",
        buscar: cs => [cs.nombrePaciente, cs.nombreGrupo, cs.nombreProfesionalAsignado].join(" "),
        filtros: [
            { id: "activos", texto: "Activos", pasa: cs => cs.estado !== "Cerrado" },
            { id: "todos", texto: "Todos", pasa: () => true },
            { id: "sinasignar", texto: "Sin asignar", pasa: cs => !cs.idProfesionalAsignado && cs.estado !== "Cerrado" },
            { id: "cerrados", texto: "Cerrados", pasa: cs => cs.estado === "Cerrado" }
        ],
        ordenInicial: { col: 4, desc: true },
        vacio: { icono: "portapapeles", titulo: "No hay casos todavía", texto: "Se crean solos cuando se manda un informe de admisión." },
        columnas: [
            { titulo: "Paciente", orden: cs => YS.normalizar(cs.nombrePaciente),
              html: cs => `<span class="ui-flex">${YS.avatar(cs.nombrePaciente, cs.idPaciente, "sm")}<span class="principal">${YS.esc(cs.nombrePaciente)}</span></span>` },
            { titulo: "Grupo", html: cs => celda(cs.nombreGrupo), orden: cs => cs.nombreGrupo || "" },
            { titulo: "Profesional", html: cs => celda(cs.nombreProfesionalAsignado), orden: cs => cs.nombreProfesionalAsignado || "" },
            { titulo: "Estado", html: cs => chipCaso(cs.estado), orden: cs => cs.estado },
            { titulo: "Creado", html: cs => `<span class="ui-num">${YS.esc(YS.fecha.corta(cs.fechaCreacion))}</span>`, orden: cs => cs.fechaCreacion },
            { titulo: "", clase: "acciones", html: cs => `<button class="ui-btn ui-btn--sm" onclick="verDetalleCaso(${cs.idCaso})">Ver</button>` }
        ]
    });

    const idCaso = parseInt(params.caso);
    if (idCaso) verDetalleCaso(idCaso);
}

async function verDetalleCaso(id) {
    if (location.hash.replace("#", "").split("?")[0] === "casos") location.hash = `#casos?caso=${id}`;
    const panel = document.getElementById("casosDetalle");
    if (!panel) return;
    panel.innerHTML = YS.cargando();
    const [detalle, historial, derivaciones] = await Promise.all([
        YS.api(`/casos/${id}`).catch(() => null), YS.api(`/casos/${id}/historial`).catch(() => []),
        YS.api(`/casos/${id}/derivaciones`).catch(() => [])
    ]);
    if (YS.shell.actual !== "casos" || !document.getElementById("casosDetalle")) return;
    if (!detalle) { panel.innerHTML = YS.vacio({ icono: "alerta", texto: "No se pudo cargar este caso." }); return; }
    window._casoDetalleActual = detalle;

    const profNombre = idp => YS.nombreCompleto(window._casosProfesionales.find(p => p.idProfesional === idp)) || `#${idp}`;
    const histTexto = h => `${TEXTO_MOTIVO_HIST[h.motivo] || h.motivo}${h.idProfesional ? " — " + YS.esc(profNombre(h.idProfesional)) : ""}`;
    const notas = derivaciones.filter(d => d.nota);

    panel.innerHTML = `
        <h3 class="tarjeta-titulo">${YS.esc(detalle.nombrePaciente)}</h3>
        <div class="ui-datos">
            ${datoCasoHtml("Estado", chipCaso(detalle.estado))}
            ${datoCasoHtml("Grupo", celda(detalle.nombreGrupo))}
            ${datoCasoHtml("Profesional asignado", celda(detalle.nombreProfesionalAsignado))}
            ${datoCasoHtml("Origen", celda(detalle.origen))}
            ${datoCasoHtml("Creado", fechaHoraCelda(detalle.fechaCreacion))}
        </div>
        <div class="split-info-acciones mt-12">
            <a class="ui-btn ui-btn--sm" href="#usuarios?paciente=${detalle.idPaciente}">Ver ficha</a>
            ${detalle.idGrupoActual ? `<button class="ui-btn ui-btn--sm" onclick="abrirModalReasignarCaso(${id})">Reasignar</button>` : ""}
        </div>
        <h4 class="tarjeta-titulo mt-16">Historial</h4>
        ${historial.length ? `<div>${historial.map(h => `<div class="item-historia">${fechaHoraCelda(h.fechaHora)} · ${histTexto(h)}</div>`).join("")}</div>`
            : YS.vacio({ icono: "lista", texto: "Todavía no hay asignaciones registradas." })}
        ${notas.length ? `<h4 class="tarjeta-titulo mt-16">Notas de reasignación</h4>
            <div>${notas.map(d => `<div class="item-historia">${fechaHoraCelda(d.fechaCreacion)} · ${YS.esc(d.nota)}</div>`).join("")}</div>` : ""}`;
}

const TEXTO_CATEGORIA_REASIGNACION = {
    CargaEquilibrada: "Carga equilibrada", PedidoDelPaciente: "Pedido del paciente",
    CambioDeEspecialidad: "Cambio de especialidad", ProfesionalNoDisponible: "Profesional no disponible", Otro: "Otro"
};

function abrirModalReasignarCaso(idCaso) {
    const detalle = window._casoDetalleActual;
    const opciones = window._casosProfesionales.filter(p => p.estado === "Activo")
        .map(p => `<option value="${p.idProfesional}"${p.idProfesional === detalle.idProfesionalAsignado ? " selected" : ""}>${YS.esc(YS.nombreCompleto(p))}${p.especialidad ? " — " + YS.esc(p.especialidad) : ""}</option>`).join("");
    const opcionesMotivo = (window._motivosReasignacion || []).map(m => `<option value="${m}">${YS.esc(TEXTO_CATEGORIA_REASIGNACION[m] || m)}</option>`).join("");
    abrirModalCaso("Reasignar caso", "Reasignar", `
        <p class="modal-texto">Paciente: <strong>${YS.esc(detalle.nombrePaciente)}</strong></p>
        <div class="ui-form-grid">
            ${campoSelect("campoReasignarProfesional", "Nuevo profesional", opcionesConElegir(opciones, "No hay profesionales activos"))}
            ${campoSelect("campoReasignarMotivo", "Motivo", opcionesMotivo)}
            <div class="ui-campo ui-campo--ancho"><label for="campoReasignarNota">Nota (opcional)</label>
                <input type="text" id="campoReasignarNota" class="ui-input" maxlength="140" placeholder="No va a auditoría ni a logs">
                <div class="ui-campo-ayuda">Hasta 140 caracteres. Es solo para el equipo, no queda en el registro de auditoría.</div></div>
        </div>`, () => confirmarReasignarCaso(idCaso));
}

async function confirmarReasignarCaso(idCaso) {
    const idProfesional = parseInt(document.getElementById("campoReasignarProfesional").value);
    if (!idProfesional) { toast("Elegí un profesional.", "error"); return; }
    const motivoCategoria = document.getElementById("campoReasignarMotivo").value;
    if (!motivoCategoria) { toast("Elegí un motivo.", "error"); return; }
    const nota = document.getElementById("campoReasignarNota").value.trim() || null;
    try {
        const res = await YS.fetch(`/casos/${idCaso}/reasignar`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ idProfesional, motivoCategoria, nota }) });
        if (res.ok || res.status === 204) { cerrarModalCaso(); toast("Caso reasignado."); mostrarTab("casos", { caso: idCaso }); }
        else toast(await YS.leerMensaje(res, "No se pudo reasignar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

// ── DERIVACIONES (cola de CasoDerivacion pendientes) ─────────
async function renderCasosDerivaciones(c) {
    const [pendientes, grupos, profesionales] = await Promise.all([YS.api("/casosderivaciones/pendientes"), YS.api("/grupos"), YS.api("/profesionales")]);
    if (YS.shell.actual !== "casosderivaciones") return;
    window._derivPendientes = pendientes; window._derivGrupos = grupos; window._derivProfesionales = profesionales;
    if (!encabezadoListado("casosderivaciones", pendientes.length, "pendientes")) return;

    YS.tabla(c, {
        filas: pendientes, unidad: ["pendiente", "pendientes"], placeholder: "Buscar por paciente o grupo",
        buscar: d => [d.nombrePaciente, d.nombreGrupo, d.nombreProfesionalSugerido].join(" "),
        vacio: { icono: "flechas", titulo: "No hay derivaciones pendientes", texto: "Las admisiones con una regla que coincide aparecen acá para confirmar." },
        columnas: [
            { titulo: "Paciente", orden: d => YS.normalizar(d.nombrePaciente),
              html: d => `<span class="ui-flex">${YS.avatar(d.nombrePaciente, d.idCaso, "sm")}<span class="principal">${YS.esc(d.nombrePaciente)}</span></span>` },
            { titulo: "Grupo sugerido", html: d => celda(d.nombreGrupo), orden: d => d.nombreGrupo || "" },
            { titulo: "Profesional sugerido", html: d => celda(d.nombreProfesionalSugerido), orden: d => d.nombreProfesionalSugerido || "" },
            { titulo: "Cómo se sugirió", html: d => `${celda(TEXTO_ORIGEN_DERIV[d.origen] || d.origen)}${d.motivo && TEXTO_MOTIVO_HIST[d.motivo] ? `<br><span class="texto-chico ui-muted">${YS.esc(TEXTO_MOTIVO_HIST[d.motivo])}</span>` : ""}` },
            { titulo: "Fecha", html: d => `<span class="ui-num">${YS.esc(YS.fecha.corta(d.fechaCreacion))}</span>`, orden: d => d.fechaCreacion },
            { titulo: "", clase: "acciones", html: d => `
                ${d.idProfesionalSugerido ? `<button class="ui-btn ui-btn--sm ui-btn--ok" onclick="confirmarDerivacionCaso(${d.idCasoDerivacion})">Confirmar</button>` : ""}
                <button class="ui-btn ui-btn--sm" onclick="abrirModalCorregirDerivacion(${d.idCasoDerivacion})">Corregir</button>
                <button class="ui-btn ui-btn--sm ui-btn--danger" onclick="rechazarDerivacionCaso(${d.idCasoDerivacion})">Rechazar</button>` }
        ]
    });
}

async function confirmarDerivacionCaso(id) {
    if (!await confirmar("¿Confirmar esta derivación tal cual la sugirió el sistema?", { titulo: "Confirmar derivación", textoConfirmar: "Confirmar" })) return;
    try {
        const res = await YS.fetch(`/casosderivaciones/${id}/confirmar`, { method: "PUT" });
        if (res.ok || res.status === 204) { toast("Derivación confirmada."); mostrarTab("casosderivaciones"); }
        else toast(await YS.leerMensaje(res, "No se pudo confirmar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

async function rechazarDerivacionCaso(id) {
    if (!await confirmar("¿Rechazar esta derivación? El caso queda derivado al grupo, pero sin profesional.", { titulo: "Rechazar derivación", textoConfirmar: "Rechazar", peligro: true })) return;
    try {
        const res = await YS.fetch(`/casosderivaciones/${id}/rechazar`, { method: "PUT" });
        if (res.ok || res.status === 204) { toast("Derivación rechazada."); mostrarTab("casosderivaciones"); }
        else toast(await YS.leerMensaje(res, "No se pudo rechazar."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

function abrirModalCorregirDerivacion(id) {
    const d = window._derivPendientes.find(x => x.idCasoDerivacion === id);
    const opcGrp = window._derivGrupos.filter(g => g.activo).map(g => `<option value="${g.idGrupo}"${g.idGrupo === d.idGrupo ? " selected" : ""}>${YS.esc(g.nombre)}</option>`).join("");
    const opcProf = window._derivProfesionales.filter(p => p.estado === "Activo").map(p => `<option value="${p.idProfesional}"${p.idProfesional === d.idProfesionalSugerido ? " selected" : ""}>${YS.esc(YS.nombreCompleto(p))}</option>`).join("");
    abrirModalCaso("Corregir derivación", "Guardar", `
        <p class="modal-texto">Paciente: <strong>${YS.esc(d.nombrePaciente)}</strong></p>
        <div class="ui-form-grid">
            ${campoSelect("campoCorregirGrupo", "Grupo", opcGrp)}
            ${campoSelect("campoCorregirProfesional", "Profesional", opcionesConElegir(opcProf, "No hay profesionales activos"))}
        </div>`, () => confirmarCorregirDerivacion(id));
}

async function confirmarCorregirDerivacion(id) {
    const idGrupo = parseInt(document.getElementById("campoCorregirGrupo").value) || null;
    const idProfesional = parseInt(document.getElementById("campoCorregirProfesional").value) || null;
    try {
        const res = await YS.fetch(`/casosderivaciones/${id}/corregir`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ idGrupo, idProfesional }) });
        if (res.ok || res.status === 204) { cerrarModalCaso(); toast("Derivación corregida."); mostrarTab("casosderivaciones"); }
        else toast(await YS.leerMensaje(res, "No se pudo corregir."), "error");
    } catch { toast("No se pudo conectar con el servidor.", "error"); }
}

// ══════════════════════════════════════════════════════════════
// REGISTRAR USUARIO DEL EQUIPO (18/10)
// ══════════════════════════════════════════════════════════════
// El Configurador da de alta a quien va a trabajar con el sistema. Si es Profesional, elige
// Profesional o Profesional auditor, puede agregarle Admisión, y carga matrícula y
// especialidad. Le llega un mail para confirmar la cuenta y elegir su contraseña. Las reglas
// las vuelve a aplicar el servidor (Services/AltaEquipo.cs).
function cuilValido(cuil) {
    const d = String(cuil || "").replace(/\D/g, "");
    if (d.length !== 11 || !["20", "23", "24", "27"].includes(d.slice(0, 2))) return false;
    const pesos = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
    const suma = pesos.reduce((s, p, i) => s + p * Number(d[i]), 0);
    const r = 11 - (suma % 11);
    return (r === 11 ? 0 : r === 10 ? -1 : r) === Number(d[10]);
}

function sugerirUsuario(nombre, apellido) {
    const limpio = t => YS.normalizar(String(t || "").trim().split(/\s+/)[0] || "").replace(/[^a-z0-9]/g, "");
    return [limpio(nombre), limpio(apellido)].filter(Boolean).join(".");
}

async function renderAltaUsuario(c) {
    let opciones = { especialidades: [] };
    try { opciones = await YS.api("/usuarios/equipo/opciones"); } catch (e) { toast(e.message, "error"); }
    if (YS.shell.actual !== "altausuario") return;
    YS.shell.encabezado({ subtitulo: "Le llega un mail para confirmar su cuenta y elegir la contraseña. Hasta entonces no puede ingresar." });
    const esp = opciones.especialidades.map(e => `<option value="${e.idEspecialidad}">${YS.esc(e.nombre)}</option>`).join("");
    c.innerHTML = `
        <form id="formAlta" class="tarjeta alta-usuario" novalidate>
            <h3 class="tarjeta-titulo">Datos de la persona</h3>
            <div class="ui-form-grid">
                <div class="ui-campo"><label for="altaNombre">Nombre</label><input id="altaNombre" class="ui-input" autocomplete="off" maxlength="100"></div>
                <div class="ui-campo"><label for="altaApellido">Apellido</label><input id="altaApellido" class="ui-input" autocomplete="off" maxlength="100"></div>
                <div class="ui-campo"><label for="altaCuil">CUIL</label><input id="altaCuil" class="ui-input" inputmode="numeric" placeholder="20-12345678-6" maxlength="13">
                    <div class="ui-campo-ayuda">11 dígitos; se controla el dígito verificador.</div></div>
                <div class="ui-campo"><label for="altaTelefono">Teléfono de contacto</label><input id="altaTelefono" class="ui-input" inputmode="tel" placeholder="351 555-1234" maxlength="30"></div>
                <div class="ui-campo"><label for="altaMail">Email</label><input id="altaMail" class="ui-input" type="email" maxlength="100">
                    <div class="ui-campo-ayuda">Ahí le llega la invitación para confirmar la cuenta.</div></div>
                <div class="ui-campo"><label for="altaUsuario">Usuario</label><input id="altaUsuario" class="ui-input" autocomplete="off" maxlength="50">
                    <div class="ui-campo-ayuda">Con este usuario va a ingresar. Se sugiere nombre.apellido.</div></div>
            </div>

            <h3 class="tarjeta-titulo mt-16">Rol</h3>
            <div class="alta-roles">
                <fieldset class="editor-permisos-modulo">
                    <legend><label class="ui-check"><input type="checkbox" id="altaRolProfesional"> <span class="texto-fuerte">Profesional</span></label></legend>
                    <div id="altaBloqueProfesional" class="oculto">
                        <div class="alta-opciones">
                            <label class="alta-opcion"><input type="radio" name="altaTipoProf" value="profesional" checked>
                                <span><span class="texto-fuerte">Profesional</span><span class="texto-chico ui-muted">Ve solo los pacientes que atiende.</span></span></label>
                            <label class="alta-opcion"><input type="radio" name="altaTipoProf" value="auditor">
                                <span><span class="texto-fuerte">Profesional auditor</span><span class="texto-chico ui-muted">Ve todos los pacientes, los reportes de atención y los registros (logs) de accesos clínicos.</span></span></label>
                        </div>
                        <label class="ui-check mt-8"><input type="checkbox" id="altaAdmision"> Privilegio: Admisión
                            <span class="texto-chico ui-muted">(informes de admisión, primera entrevista, asignaciones y solicitudes de cambio)</span></label>
                        <div class="ui-form-grid mt-12">
                            <div class="ui-campo"><label for="altaMatricula">Matrícula</label><input id="altaMatricula" class="ui-input" maxlength="50" placeholder="MP 1234"></div>
                            <div class="ui-campo"><label for="altaJurisdiccion">Jurisdicción de la matrícula</label><input id="altaJurisdiccion" class="ui-input" maxlength="60" value="Córdoba"></div>
                            <div class="ui-campo"><label for="altaEspecialidad">Especialidad principal</label>
                                <select id="altaEspecialidad" class="ui-select"><option value="">Elegí la especialidad</option>${esp}</select></div>
                        </div>
                    </div>
                </fieldset>
                <fieldset class="editor-permisos-modulo">
                    <legend><label class="ui-check"><input type="checkbox" id="altaRolCrm"> <span class="texto-fuerte">CRM y atención al cliente</span></label></legend>
                    <div id="altaBloqueCrm" class="oculto">
                        <label class="ui-check"><input type="checkbox" id="altaAgendas"> Privilegio: Gestión de agendas
                            <span class="texto-chico ui-muted">(turnos y horarios de todos los profesionales; nunca historias clínicas)</span></label>
                    </div>
                </fieldset>
                <fieldset class="editor-permisos-modulo">
                    <legend><label class="ui-check"><input type="checkbox" id="altaRolConfigurador"> <span class="texto-fuerte">Configurador</span></label></legend>
                    <div class="texto-chico ui-muted">Usuarios, permisos, configuración y auditoría administrativa. Sin acceso clínico.</div>
                </fieldset>
            </div>
            <div id="altaErrorRol" class="ui-alerta ui-alerta--danger mt-8 oculto" role="alert"></div>
            <div class="ui-flex mt-16">
                <button type="submit" class="ui-btn ui-btn--primary" id="altaGuardar">${YS.icono("usuario-mas")}Registrar y enviar invitación</button>
            </div>
        </form>
        <div id="altaResultado"></div>`;

    const $ = id => document.getElementById(id);
    const sync = () => {
        $("altaBloqueProfesional").classList.toggle("oculto", !$("altaRolProfesional").checked);
        $("altaBloqueCrm").classList.toggle("oculto", !$("altaRolCrm").checked);
        $("altaErrorRol").classList.add("oculto");
    };
    ["altaRolProfesional", "altaRolCrm", "altaRolConfigurador"].forEach(id => $(id).addEventListener("change", sync));
    let usuarioEditado = false;
    $("altaUsuario").addEventListener("input", () => { usuarioEditado = true; });
    const sugerir = () => { if (!usuarioEditado) $("altaUsuario").value = sugerirUsuario($("altaNombre").value, $("altaApellido").value); };
    $("altaNombre").addEventListener("input", sugerir);
    $("altaApellido").addEventListener("input", sugerir);
    $("formAlta").addEventListener("submit", e => { e.preventDefault(); guardarAltaUsuario(); });
    $("altaNombre").focus();
}

async function guardarAltaUsuario() {
    const $ = id => document.getElementById(id);
    const esProf = $("altaRolProfesional").checked;
    const reglas = [
        { id: "altaNombre", requerido: "Completá el nombre." },
        { id: "altaApellido", requerido: "Completá el apellido." },
        { id: "altaCuil", requerido: "Completá el CUIL.", validar: v => cuilValido(v) ? null : "El CUIL no es válido (revisá el último dígito)." },
        { id: "altaTelefono", requerido: "Completá un teléfono de contacto.", patron: /^[0-9+()\- ]{6,30}$/, mensajePatron: "Solo números, espacios, guiones o +." },
        { id: "altaMail", requerido: "Completá el email.", patron: /^[^@\s]+@[^@\s]+\.[^@\s]+$/, mensajePatron: "El email no es válido." },
        { id: "altaUsuario", requerido: "Completá el usuario.", patron: /^[a-z0-9._-]{3,50}$/, mensajePatron: "En minúsculas, sin espacios: letras, números, punto o guion." }
    ];
    if (esProf) reglas.push(
        { id: "altaMatricula", requerido: "Completá la matrícula." },
        { id: "altaJurisdiccion", requerido: "Indicá la jurisdicción." },
        { id: "altaEspecialidad", requerido: "Elegí la especialidad principal." });
    const datosOk = YS.form.validar(reglas);

    const permisos = [];
    const privilegios = [];
    if (esProf) {
        const tipo = document.querySelector('input[name="altaTipoProf"]:checked').value;
        permisos.push(tipo);
        if ($("altaAdmision").checked) privilegios.push({ permiso: tipo, privilegio: "admision" });
    }
    if ($("altaRolCrm").checked) {
        permisos.push("crm");
        if ($("altaAgendas").checked) privilegios.push({ permiso: "crm", privilegio: "gestion_agendas" });
    }
    if ($("altaRolConfigurador").checked) permisos.push("configurador");
    const errorRol = $("altaErrorRol");
    if (!permisos.length) { errorRol.textContent = "Elegí al menos un rol: sin rol, la persona no ve nada al ingresar."; errorRol.classList.remove("oculto"); }
    if (!datosOk || !permisos.length) return;

    const boton = $("altaGuardar");
    boton.classList.add("cargando");
    try {
        const res = await YS.fetch("/usuarios/equipo", { method: "POST", json: {
            nombre: $("altaNombre").value, apellido: $("altaApellido").value, cuil: $("altaCuil").value,
            telefono: $("altaTelefono").value, mail: $("altaMail").value, username: $("altaUsuario").value,
            permisos, privilegios,
            matricula: esProf ? $("altaMatricula").value : null,
            jurisdiccionMatricula: esProf ? $("altaJurisdiccion").value : null,
            idEspecialidad: esProf ? parseInt($("altaEspecialidad").value) || null : null
        } });
        if (!res.ok) { toast(await YS.leerMensaje(res, "No se pudo registrar el usuario."), "error"); return; }
        const r = await res.json();
        $("formAlta").classList.add("oculto");
        mostrarResultadoInvitacion($("altaResultado"), r, true);
    } catch (e) { toast(e.message, "error"); }
    finally { boton.classList.remove("cargando"); }
}

// Muestra si salió el mail y, en desarrollo, el link cuando no salió.
function mostrarResultadoInvitacion(cont, r, esAlta) {
    const vence = new Date(r.vence).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "short" });
    cont.innerHTML = `
        <div class="tarjeta">
            <div class="ui-alerta ui-alerta--${r.mailEnviado ? "ok" : "warn"} ui-alerta--bloque">
                <strong>${esAlta ? `Usuario ${YS.esc(r.username)} registrado.` : "Invitación generada."}</strong>
                <p class="mb-0 mt-8">${r.mailEnviado
                    ? `Le enviamos la invitación${r.mail ? " a " + YS.esc(r.mail) : ""}. El link vence el ${YS.esc(vence)}.`
                    : `${YS.esc(r.motivoMail || "No se pudo enviar el mail.")} La cuenta quedó creada y pendiente de confirmar.`}</p>
            </div>
            ${r.linkActivacion ? `
            <p class="mt-12 mb-4 texto-chico ui-muted">Solo en desarrollo: link de la invitación (vence el ${YS.esc(vence)}). Abrilo en otra ventana o mandáselo a la persona.</p>
            <div class="clave-temporal"><code class="link-invitacion">${YS.esc(r.linkActivacion)}</code>
                <button class="ui-btn ui-btn--sm" type="button" id="btnCopiarLink">Copiar</button></div>` : ""}
            ${esAlta ? `<div class="ui-flex mt-16">
                <button class="ui-btn ui-btn--primary" type="button" onclick="mostrarTab('altausuario')">Registrar otro</button>
                <a class="ui-btn" href="#permisos">Ver usuarios y permisos</a></div>` : ""}
        </div>`;
    document.getElementById("btnCopiarLink")?.addEventListener("click", () => YS.copiar(r.linkActivacion, "Link copiado"));
}

async function reenviarInvitacion(idUsuario) {
    const u = (window._ultimosAccesos || []).find(x => x.idUsuario === idUsuario);
    if (!await confirmar(`¿Reenviar la invitación a ${u?.username || "este usuario"}? El link anterior deja de servir.`,
        { titulo: "Reenviar invitación", textoConfirmar: "Reenviar" })) return;
    try {
        const res = await YS.fetch(`/usuarios/${idUsuario}/invitacion`, { method: "POST" });
        if (!res.ok) { toast(await YS.leerMensaje(res, "No se pudo reenviar la invitación."), "error"); return; }
        const r = await res.json();
        const cuerpo = YS.el("div");
        mostrarResultadoInvitacion(cuerpo, r, false);
        YS.modal({ titulo: "Invitación", cuerpo, acciones: [{ texto: "Listo", tipo: "primary", foco: true, alClick: c => c.cerrar() }] });
    } catch (e) { toast(e.message, "error"); }
}

// ══════════════════════════════════════════════════════════════
// PRIVILEGIOS (18/10): a quién se le agrega cada uno
// ══════════════════════════════════════════════════════════════
async function renderPrivilegios(c) {
    const datos = await YS.api("/privilegios");
    if (YS.shell.actual !== "privilegios") return;
    YS.shell.encabezado({ subtitulo: "Un privilegio se suma a un rol. Los cambios rigen desde el próximo ingreso de cada persona." });
    YS.shell.acciones([{ texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab("privilegios") }]);
    const persona = u => `<span class="principal">${YS.esc(u.username)}</span>${u.nombreCompleto ? `<span class="secundario">${YS.esc(u.nombreCompleto)}</span>` : ""}`;
    c.innerHTML = datos.map(p => `
        <section class="tarjeta privilegio" aria-labelledby="priv-${YS.esc(p.clave)}">
            <div class="ui-entre">
                <h3 class="tarjeta-titulo mb-0" id="priv-${YS.esc(p.clave)}">${YS.esc(p.nombre)}</h3>
                <span class="texto-chico ui-muted">Se agrega a: ${p.permisos.map(x => YS.esc(x.nombre)).join(", ")}</span>
            </div>
            ${p.descripcion ? `<p class="texto-suave mt-8">${YS.esc(p.descripcion)}</p>` : ""}
            <h4 class="mt-12 mb-8">Lo tienen (${p.loTienen.length})</h4>
            ${p.loTienen.length ? `<ul class="privilegio-lista">${p.loTienen.map(u => `
                <li><span class="privilegio-persona">${persona(u)}</span><span class="ui-chip ui-chip--neutral">${YS.esc(u.permiso)}</span>
                    ${u.estado === "Pendiente" ? '<span class="ui-chip ui-chip--warn">Sin confirmar</span>' : ""}
                    <button class="ui-btn ui-btn--sm ui-btn--danger" type="button" onclick="quitarPrivilegio(${u.idUsuario}, '${YS.esc(p.clave)}', '${YS.esc(p.nombre)}')">Quitar</button></li>`).join("")}</ul>`
                : '<p class="ui-muted texto-chico">Nadie todavía.</p>'}
            <div class="privilegio-agregar mt-12">
                ${p.candidatos.length ? `
                <div class="ui-campo"><label for="cand-${YS.esc(p.clave)}">Agregar a</label>
                    <select id="cand-${YS.esc(p.clave)}" class="ui-select"><option value="">Elegí a quién</option>${p.candidatos.map(u =>
                        `<option value="${u.idUsuario}">${YS.esc(u.username)}${u.nombreCompleto ? " — " + YS.esc(u.nombreCompleto) : ""} (${YS.esc(u.permiso)})</option>`).join("")}</select></div>
                <button class="ui-btn ui-btn--primary" type="button" onclick="agregarPrivilegio('${YS.esc(p.clave)}')">Agregar</button>`
                : `<p class="ui-muted texto-chico mb-0">No hay a quién agregarlo: todos los que tienen ${p.permisos.map(x => YS.esc(x.nombre)).join(" o ")} ya lo tienen, o no hay usuarios con ese rol.</p>`}
            </div>
        </section>`).join("") || YS.vacio({ icono: "candado", titulo: "No hay privilegios configurados" });
}

async function agregarPrivilegio(clave) {
    const sel = document.getElementById("cand-" + clave);
    const id = parseInt(sel?.value);
    if (!id) { YS.form.marcar(sel, "Elegí a quién se lo agregás."); return; }
    try {
        const res = await YS.fetch(`/usuarios/${id}/privilegios`, { method: "POST", json: { privilegio: clave } });
        if (!res.ok) { toast(await YS.leerMensaje(res, "No se pudo agregar el privilegio."), "error"); return; }
        toast("Privilegio agregado. Rige desde el próximo ingreso de esa persona.");
        mostrarTab("privilegios");
    } catch (e) { toast(e.message, "error"); }
}

async function quitarPrivilegio(idUsuario, clave, nombre) {
    if (!await confirmar(`¿Quitarle el privilegio ${nombre}?`, { titulo: "Quitar privilegio", textoConfirmar: "Quitar", peligro: true })) return;
    try {
        const res = await YS.fetch(`/usuarios/${idUsuario}/privilegios/${encodeURIComponent(clave)}`, { method: "DELETE" });
        if (!res.ok) { toast(await YS.leerMensaje(res, "No se pudo quitar el privilegio."), "error"); return; }
        toast("Privilegio quitado.", "warn");
        mostrarTab("privilegios");
    } catch (e) { toast(e.message, "error"); }
}

// ══════════════════════════════════════════════════════════════
// ETAPA 3: PERMISOS POR USUARIO (Configurador › Usuarios › Permisos)
// ══════════════════════════════════════════════════════════════
// Módulo (rol) → permisos → privilegios, como los definió la organización. Las reglas las
// vuelve a aplicar el servidor (Services/AccesosEdicion.cs); acá solo se ayuda a no
// equivocarse. Cada cambio queda en la auditoría administrativa.
async function renderPermisos(c) {
    const datos = await YS.api("/usuarios/accesos");
    window._ultimosAccesos = datos;
    if (YS.shell.actual !== "permisos") return;
    YS.shell.acciones([
        { texto: "Registrar usuario", icono: "usuario-mas", tipo: "primary", alClick: () => { location.hash = "#altausuario"; } },
        { texto: "Actualizar", icono: "refrescar", alClick: () => mostrarTab("permisos") }
    ]);
    YS.shell.encabezado({ subtitulo: `${datos.length} usuario${datos.length === 1 ? "" : "s"} del equipo · los cambios rigen desde el próximo ingreso de cada persona` });

    YS.tabla(c, {
        filas: datos, unidad: ["usuario", "usuarios"], placeholder: "Buscar por usuario, nombre, permiso o matrícula",
        buscar: u => [u.username, u.nombreCompleto, u.permisos, u.privilegios, u.matricula, u.especialidad].join(" "),
        filtros: [
            { id: "todos", texto: "Todos", pasa: () => true },
            { id: "sin", texto: "Sin permisos", pasa: u => u.sinPermisos },
            { id: "pendientes", texto: "Sin confirmar", pasa: u => u.estado === "Pendiente" }
        ],
        vacio: { icono: "escudo", titulo: "No hay usuarios del equipo", texto: "Las cuentas de paciente no llevan permisos y no aparecen acá." },
        columnas: [
            { titulo: "Usuario", orden: u => YS.normalizar(u.username || ""),
              html: u => `<span class="ui-flex">${YS.avatar(u.nombreCompleto || u.username, u.idUsuario, "sm")}<span><span class="principal">${YS.esc(u.username)}</span><span class="secundario">${YS.esc(u.nombreCompleto || u.matricula ? [u.nombreCompleto, u.matricula ? "Mat. " + u.matricula : ""].filter(Boolean).join(" · ") : "")}</span></span></span>` },
            { titulo: "Permisos", html: u => u.sinPermisos ? '<span class="ui-chip ui-chip--warn">Sin permisos</span>' : celda(u.permisos), orden: u => u.permisos || "" },
            { titulo: "Privilegios", html: u => celda(u.privilegios), orden: u => u.privilegios || "" },
            { titulo: "Cuenta", html: u => chipEstadoCuenta(u.estado), orden: u => u.estado || "" },
            { titulo: "", clase: "acciones", html: u => (u.estado === "Pendiente"
                ? `<button class="ui-btn ui-btn--sm" type="button" onclick="reenviarInvitacion(${u.idUsuario})">Reenviar invitación</button> ` : "")
                + `<button class="ui-btn ui-btn--sm" type="button" onclick="abrirEditorPermisos(${u.idUsuario})">Editar permisos</button>` }
        ]
    });
}

async function abrirEditorPermisos(idUsuario) {
    let datos;
    try { datos = await YS.api(`/usuarios/${idUsuario}/accesos`); }
    catch (e) { toast(e.message, "error"); return; }
    const fila = (window._ultimosAccesos || []).find(u => u.idUsuario === idUsuario);
    const tiene = new Set(datos.permisos);
    const tienePriv = new Set(datos.privilegios.map(p => p.privilegio));
    const cuerpo = YS.el("div", { class: "editor-permisos" });

    if (datos.esPaciente) {
        cuerpo.innerHTML = '<p class="ui-alerta ui-alerta--info"><span>Es una cuenta de paciente: no lleva permisos del equipo.</span></p>';
        YS.modal({ titulo: "Permisos", cuerpo, tamano: "chico", acciones: [{ texto: "Cerrar", alClick: c => c.cerrar() }] });
        return;
    }

    const avisos = [];
    if (!datos.tieneFichaProfesional) avisos.push("Profesional y Profesional auditor necesitan una ficha de profesional con matrícula vinculada a esta cuenta (RN-17).");
    if (datos.esUnoMismo) avisos.push("Es tu propia cuenta: no podés quitarte el permiso de Configurador.");
    cuerpo.innerHTML = (avisos.length ? `<div class="ui-alerta ui-alerta--info mb-12"><span>${avisos.map(YS.esc).join("<br>")}</span></div>` : "") +
        datos.modulos.map(m => {
            // Privilegios del módulo, una sola vez aunque los admitan dos permisos (Admisión
            // vale para Profesional y para Profesional auditor).
            const privilegios = [];
            m.permisos.forEach(p => p.privilegios.forEach(v => { if (!privilegios.some(x => x.clave === v.clave)) privilegios.push(v); }));
            const permisos = m.permisos.map(p => {
                const requiereFicha = p.clave === "profesional" || p.clave === "auditor";
                const bloqueado = (requiereFicha && !datos.tieneFichaProfesional && !tiene.has(p.clave))
                    || (datos.esUnoMismo && p.clave === "configurador" && tiene.has(p.clave));
                return `<label class="ui-check"><input type="checkbox" data-permiso="${YS.esc(p.clave)}" data-modulo="${YS.esc(m.clave)}"
                    ${tiene.has(p.clave) ? "checked" : ""} ${bloqueado ? "disabled" : ""}> ${YS.esc(p.nombre)}</label>`;
            }).join("");
            const privs = privilegios.map(v => `
                <label class="ui-check" title="${YS.esc(v.descripcion || "")}"><input type="checkbox" data-privilegio="${YS.esc(v.clave)}" data-modulo="${YS.esc(m.clave)}"
                    ${tienePriv.has(v.clave) ? "checked" : ""}> Privilegio: ${YS.esc(v.nombre)}</label>
                ${v.descripcion ? `<div class="texto-chico ui-muted editor-permisos-desc">${YS.esc(v.descripcion)}</div>` : ""}`).join("");
            return `
                <fieldset class="editor-permisos-modulo">
                    <legend class="texto-fuerte">${YS.esc(m.nombre)}</legend>
                    <div class="texto-chico ui-muted mb-8">${YS.esc(m.descripcion || "")}</div>
                    <div class="editor-permisos-fila">${permisos}</div>
                    ${privs ? `<div class="editor-permisos-privilegios">${privs}</div>` : ""}
                </fieldset>`;
        }).join("") +
        '<p class="texto-chico ui-muted mt-8">Los cambios rigen desde el próximo ingreso (o cambio de módulo) de esta persona, y quedan en la auditoría administrativa.</p>';

    // Un privilegio solo se puede marcar si hay un permiso del módulo que lo admite.
    const sincronizar = () => datos.modulos.forEach(m => {
        const marcados = [...cuerpo.querySelectorAll(`input[data-permiso][data-modulo="${m.clave}"]:checked`)].map(i => i.dataset.permiso);
        cuerpo.querySelectorAll(`input[data-privilegio][data-modulo="${m.clave}"]`).forEach(i => {
            const admite = m.permisos.some(p => marcados.includes(p.clave) && p.privilegios.some(v => v.clave === i.dataset.privilegio));
            i.disabled = !admite;
            if (!admite) i.checked = false;
        });
    });
    cuerpo.addEventListener("change", sincronizar);
    sincronizar();

    YS.modal({
        titulo: `Permisos de ${fila?.username || "#" + idUsuario}`, cuerpo,
        acciones: [
            { texto: "Cancelar" },
            { texto: "Guardar", tipo: "primary", foco: true, alClick: async ctrl => {
                const permisos = [...cuerpo.querySelectorAll("input[data-permiso]:checked")].map(i => i.dataset.permiso);
                const privilegios = [];
                cuerpo.querySelectorAll("input[data-privilegio]:checked").forEach(i => {
                    const m = datos.modulos.find(x => x.clave === i.dataset.modulo);
                    // Va sobre el primer permiso marcado del módulo que lo admite.
                    const sobre = m.permisos.find(p => permisos.includes(p.clave) && p.privilegios.some(v => v.clave === i.dataset.privilegio));
                    if (sobre) privilegios.push({ permiso: sobre.clave, privilegio: i.dataset.privilegio });
                });
                if (!permisos.length && !await confirmar("Sin ningún permiso, esta persona no va a ver nada al ingresar. ¿Guardar igual?",
                    { titulo: "Usuario sin permisos", textoConfirmar: "Guardar sin permisos", peligro: true })) return;
                try {
                    const res = await YS.fetch(`/usuarios/${idUsuario}/accesos`, { method: "PUT", json: { permisos, privilegios } });
                    if (!res.ok) { toast(await YS.leerMensaje(res, "No se pudieron guardar los permisos."), "error"); return; }
                    ctrl.cerrar(true);
                    toast("Permisos guardados. Rigen desde el próximo ingreso de esta persona.");
                    mostrarTab("permisos");
                } catch (e) { toast(e.message, "error"); }
            } }
        ]
    });
}

YS.shell.iniciar({
    rol: MODULO, pagina: "gestion", navegar: (id, params) => mostrarTab(id, params), inicial: ATERRIZAJE,
    // Chats como seccion propia en CRM y en el Configurador que trae el permiso crm; el profesional usa el cajon de siempre.
    chats: CON_CHATS
        ? { modo: "pagina", contenedor: document.getElementById("vistaChatsCRM"), alPulsarBoton: () => { location.hash = "#chats"; } }
        : undefined
});
