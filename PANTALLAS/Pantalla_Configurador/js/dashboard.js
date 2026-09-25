const API = "http://localhost:5000";

function toast(msg, tipo = "ok", duracion = 3500) {
    let cont = document.getElementById("toast-container");
    if (!cont) {
        cont = document.createElement("div");
        cont.id = "toast-container";
        document.body.appendChild(cont);
    }
    const iconos = { ok: "✔", error: "✖", warn: "⚠" };
    const t = document.createElement("div");
    t.className = `toast toast-${tipo}`;
    t.innerHTML = `<span class="toast-icono">${iconos[tipo] ?? "ℹ"}</span><span>${msg}</span>`;
    cont.appendChild(t);
    setTimeout(() => {
        t.classList.add("saliendo");
        t.addEventListener("animationend", () => t.remove());
    }, duracion);
}

function confirmar(msg) {
    return new Promise(resolve => {
        let overlay = document.getElementById("confirmOverlay");
        if (!overlay) {
            overlay = document.createElement("div");
            overlay.id = "confirmOverlay";
            overlay.innerHTML = `
                <div class="confirm-box">
                    <p class="confirm-msg" id="confirmMsg"></p>
                    <div class="confirm-btns">
                        <button class="confirm-btn-no" id="confirmNo">Cancelar</button>
                        <button class="confirm-btn-si" id="confirmSi">Aceptar</button>
                    </div>
                </div>`;
            document.body.appendChild(overlay);
        }
        document.getElementById("confirmMsg").textContent = msg;
        overlay.classList.add("visible");
        const cerrar = (val) => { overlay.classList.remove("visible"); resolve(val); };
        document.getElementById("confirmSi").onclick = () => cerrar(true);
        document.getElementById("confirmNo").onclick = () => cerrar(false);
    });
}

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

// ── SIDEBAR ──────────────────────────────────────────────────
function toggleGrupo(id) {
    const lista = document.getElementById(`grupo-${id}`);
    const flecha = document.getElementById(`flecha-${id}`);
    lista.classList.toggle("oculto");
    flecha.textContent = lista.classList.contains("oculto") ? "▶" : "▼";
}

function marcarActivo(tab) {
    document.querySelectorAll(".grupo-items li").forEach(li => {
        li.classList.toggle("activo", li.getAttribute("onclick")?.includes(`'${tab}'`));
    });
}

// ── TAB ROUTING ──────────────────────────────────────────────
async function mostrarTab(tab) {
    if (tab === 'reportes') { reportes(); return; }
    if (tab === 'admisiones') { admisiones(); return; }
    tabActual = tab;
    marcarActivo(tab);

    const contenido = document.getElementById("contenido");
    contenido.innerHTML = `<div class="text-center mt-5"><div class="spinner-border text-primary"></div></div>`;

    try {
        if (tab === "usuarios")     return await renderUsuarios(contenido);
        if (tab === "solicitudes")  return await renderSolicitudes(contenido);
        if (tab === "derivaciones") return await renderDerivaciones(contenido);
        if (tab === "auditoria")    return await renderAuditoria(contenido);
        if (tab === "recuperacion") return await renderRecuperacion(contenido);

        const datos = await fetch(`${API}/${tab}`).then(r => r.json());
        datosActuales = datos;
        renderTabla(titulos[tab], columnas[tab], datos, tab, contenido);

    } catch {
        contenido.innerHTML = errorHtml(tab);
    }
}

// ── USUARIOS ─────────────────────────────────────────────────
async function renderUsuarios(c) {
    const [pacientes, profesionales] = await Promise.all([
        fetch(`${API}/pacientes`).then(r => r.json()),
        fetch(`${API}/profesionales`).then(r => r.json()),
    ]);

    const pacProfs = {};
    const profPacs = {};
    await Promise.all(pacientes.map(async p => {
        const profs = await fetch(`${API}/pacientes/${p.idPaciente}/mis-profesionales`).then(r => r.json()).catch(() => []);
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

    c.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">${titulos["usuarios"]}</div>
            <label class="check-baja">
                <input type="checkbox" id="chkDadosDeBaja" onchange="toggleDadosDeBaja()">
                Mostrar dados de baja
            </label>
        </div>
        <div class="split-view">
            <div class="split-col">
                <div class="split-header">Pacientes <span class="split-badge" id="badgePacientes"></span></div>
                <div class="split-list" id="listaPacientes"></div>
            </div>
            <div class="split-col">
                <div class="split-header">Profesionales <span class="split-badge" id="badgeProfesionales"></span></div>
                <div class="split-list" id="listaProfesionales"></div>
            </div>
        </div>
        <div class="split-info" id="splitInfo"></div>`;

    renderCardsUsuarios();
}

function renderCardsUsuarios() {
    const mostrarBaja = window._mostrarDadosDeBaja;
    const pacientes = window._pacientes.filter(p => mostrarBaja ? p.estado === "Inactivo" : p.estado !== "Inactivo");
    const profesionales = window._profesionales.filter(p => mostrarBaja ? p.estado === "Inactivo" : p.estado !== "Inactivo");

    document.getElementById("badgePacientes").textContent = pacientes.length;
    document.getElementById("badgeProfesionales").textContent = profesionales.length;

    document.getElementById("listaPacientes").innerHTML = pacientes.map(p => `
        <div class="usr-card${mostrarBaja ? " usr-card-inactivo" : ""}" id="pac-${p.idPaciente}" onclick="seleccionarPaciente(${p.idPaciente})">
            <div class="usr-nombre">${p.nombre ?? "-"}</div>
            <div class="usr-sub">DNI: ${p.dni ?? "-"} &bull; ${p.estado ?? "-"}</div>
        </div>`).join("") || `<div class="split-vacio">Sin resultados.</div>`;

    document.getElementById("listaProfesionales").innerHTML = profesionales.map(p => `
        <div class="usr-card${mostrarBaja ? " usr-card-inactivo" : ""}" id="prof-${p.idProfesional}" onclick="seleccionarProfesional(${p.idProfesional})">
            <div class="usr-nombre">${p.nombre ?? "-"}${p.apellido ? " " + p.apellido : ""}</div>
            <div class="usr-sub">Mat: ${p.matricula ?? "-"} &bull; ${p.estado ?? "-"}</div>
        </div>`).join("") || `<div class="split-vacio">Sin resultados.</div>`;

    document.getElementById("splitInfo").innerHTML = "";
}

function toggleDadosDeBaja() {
    window._mostrarDadosDeBaja = document.getElementById("chkDadosDeBaja").checked;
    window._pacSeleccionado = null;
    window._profSeleccionado = null;
    renderCardsUsuarios();
}

function seleccionarPaciente(idPac) {
    window._pacSeleccionado = idPac;
    window._profSeleccionado = null;

    document.querySelectorAll(".usr-card").forEach(el => el.classList.remove("seleccionado", "relacionado"));
    document.getElementById(`pac-${idPac}`)?.classList.add("seleccionado");

    const relacionados = window._pacProfs[idPac] ?? new Set();
    document.querySelectorAll("#listaProfesionales .usr-card").forEach(el => {
        const idProf = parseInt(el.id.replace("prof-", ""));
        if (relacionados.has(idProf)) el.classList.add("relacionado");
    });

    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    const nombres = [...relacionados].map(idP => {
        const pro = window._profesionales.find(p => p.idProfesional === idP);
        return pro ? (pro.nombre + (pro.apellido ? " " + pro.apellido : "")) : idP;
    });

    const esInactivo = pac?.estado === "Inactivo";
    const btnAccion = esInactivo
        ? `<button class="btn-cambio-alta" onclick="darDeAltaPaciente(${idPac})">Dar de alta</button>`
        : `<button class="btn-cambio-baja" onclick="darDeBajaPaciente(${idPac})">Dar de baja</button>`;
    const btnCambio = (!esInactivo && relacionados.size)
        ? `<button class="btn-cambio-prof" onclick="abrirModalCambioDirecto(${idPac})">Cambiar profesional</button>`
        : "";
    const btnAsignar = !esInactivo
        ? `<button class="btn-cambio-prof" onclick="abrirModalAsignar(${idPac})">Asignar profesional</button>`
        : "";

    document.getElementById("splitInfo").innerHTML = relacionados.size
        ? `<strong>${pac?.nombre}</strong> tiene asignados: ${nombres.join(", ")}. ${btnCambio} ${btnAsignar} ${btnAccion}`
        : `<strong>${pac?.nombre}</strong> no tiene profesionales asignados. ${btnAsignar} ${btnAccion}`;
}

function seleccionarProfesional(idProf) {
    document.querySelectorAll(".usr-card").forEach(el => el.classList.remove("seleccionado", "relacionado"));
    document.getElementById(`prof-${idProf}`)?.classList.add("seleccionado");

    const relacionados = window._profPacs[idProf] ?? new Set();
    document.querySelectorAll("#listaPacientes .usr-card").forEach(el => {
        const idPac = parseInt(el.id.replace("pac-", ""));
        if (relacionados.has(idPac)) el.classList.add("relacionado");
    });

    const prof = window._profesionales.find(p => p.idProfesional === idProf);
    const nombres = [...relacionados].map(idPac => {
        const pac = window._pacientes.find(p => p.idPaciente === idPac);
        return pac?.nombre ?? idPac;
    });
    const esInactivoProf = prof?.estado === "Inactivo";
    const btnAccionProf = esInactivoProf
        ? `<button class="btn-cambio-alta" onclick="darDeAltaProfesional(${idProf})">Dar de alta</button>`
        : `<button class="btn-cambio-baja" onclick="darDeBajaProfesional(${idProf})">Dar de baja</button>`;

    document.getElementById("splitInfo").innerHTML = relacionados.size
        ? `<strong>${prof?.nombre}${prof?.apellido ? " " + prof.apellido : ""}</strong> atiende a: ${nombres.join(", ")}. ${btnAccionProf}`
        : `<strong>${prof?.nombre}</strong> no tiene turnos registrados. ${btnAccionProf}`;
}

async function darDeBajaPaciente(idPac) {
    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    if (!await confirmar(`¿Dar de baja a ${pac?.nombre ?? "este paciente"}?`)) return;
    const res = await fetch(`${API}/pacientes/${idPac}`, { method: "DELETE", headers: auditorHeaders() });
    if (res.ok || res.status === 204) { toast("Paciente dado de baja."); mostrarTab("usuarios"); }
    else toast("Error al dar de baja.", "error");
}

async function darDeAltaPaciente(idPac) {
    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    if (!await confirmar(`¿Dar de alta a ${pac?.nombre ?? "este paciente"}?`)) return;
    const res = await fetch(`${API}/pacientes/${idPac}/alta`, { method: "PUT", headers: auditorHeaders() });
    if (res.ok) { toast("Paciente dado de alta."); mostrarTab("usuarios"); }
    else toast("Error al dar de alta.", "error");
}

async function darDeBajaProfesional(idProf) {
    const prof = window._profesionales.find(p => p.idProfesional === idProf);
    const nombre = prof ? `${prof.nombre}${prof.apellido ? " " + prof.apellido : ""}` : "este profesional";
    if (!await confirmar(`¿Dar de baja a ${nombre}?`)) return;
    const res = await fetch(`${API}/profesionales/${idProf}`, { method: "DELETE", headers: auditorHeaders() });
    if (res.ok || res.status === 204) { toast("Profesional dado de baja."); mostrarTab("usuarios"); }
    else toast("Error al dar de baja.", "error");
}

async function darDeAltaProfesional(idProf) {
    const prof = window._profesionales.find(p => p.idProfesional === idProf);
    const nombre = prof ? `${prof.nombre}${prof.apellido ? " " + prof.apellido : ""}` : "este profesional";
    if (!await confirmar(`¿Dar de alta a ${nombre}?`)) return;
    const res = await fetch(`${API}/profesionales/${idProf}/alta`, { method: "PUT", headers: auditorHeaders() });
    if (res.ok) { toast("Profesional dado de alta."); mostrarTab("usuarios"); }
    else toast("Error al dar de alta.", "error");
}

// ── SOLICITUDES DE REGISTRO ──────────────────────────────────
async function renderSolicitudes(c) {
    const datos = await fetch(`${API}/profesionales/solicitudes`).then(r => r.json());

    if (!datos.length) {
        c.innerHTML = `<div class="titulo-seccion">${titulos["solicitudes"]}</div>
            <div class="alert alert-info">No hay solicitudes pendientes.</div>`;
        return;
    }

    const cols = ["idProfesional", "nombre", "apellido", "dni", "mail", "matricula", "provincia", "fechaSolicitud"];
    const headers = cols.map(col => `<th>${col}</th>`).join("") + "<th>Acciones</th>";
    const filas = datos.map(row => {
        const celdas = cols.map(c => `<td>${row[c] ?? "-"}</td>`).join("");
        return `<tr>${celdas}<td>
            <button class="btn-ok" onclick="aprobarSolicitud(${row.idProfesional})">Aprobar</button>
            <button class="btn-ko" onclick="rechazarSolicitud(${row.idProfesional})">Rechazar</button>
        </td></tr>`;
    }).join("");

    c.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">${titulos["solicitudes"]} <span class="badge bg-secondary">${datos.length}</span></div>
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('solicitudes')">↺ Actualizar</button>
        </div>
        <div class="table-responsive">
            <table class="table table-hover tabla-datos">
                <thead><tr>${headers}</tr></thead>
                <tbody>${filas}</tbody>
            </table>
        </div>`;
}

function auditorHeaders() {
    return { "Content-Type": "application/json", "X-Usuario-Id": localStorage.getItem("idUsuario") ?? "0" };
}

async function aprobarSolicitud(id) {
    if (!await confirmar("¿Aprobar esta solicitud?")) return;
    await fetch(`${API}/profesionales/${id}/aprobar`, { method: "PUT", headers: auditorHeaders() });
    toast("Solicitud aprobada.");
    mostrarTab("solicitudes");
}

async function rechazarSolicitud(id) {
    if (!await confirmar("¿Rechazar esta solicitud?")) return;
    await fetch(`${API}/profesionales/${id}/rechazar`, { method: "PUT", headers: auditorHeaders() });
    toast("Solicitud rechazada.", "warn");
    mostrarTab("solicitudes");
}

// ── SOLICITUDES DE CAMBIO ────────────────────────────────────
async function renderDerivaciones(c) {
    const datos = await fetch(`${API}/derivaciones/detalle`).then(r => r.json());

    if (!datos.length) {
        c.innerHTML = `<div class="titulo-seccion">${titulos["derivaciones"]}</div>
            <div class="alert alert-info">No hay solicitudes de cambio.</div>`;
        return;
    }

    const filas = datos.map(d => {
        const fecha = new Date(d.fecha).toLocaleDateString("es-AR");
        const acciones = d.estado === "Pendiente" ? `
            <button class="btn-ok" onclick="abrirModalDeriv(${d.idDerivacion}, ${d.idEspecialidad}, ${d.idProfesionalOrigen})">Aceptar</button>
            <button class="btn-ko" onclick="rechazarDerivacion(${d.idDerivacion})">Rechazar</button>` : "-";
        return `<tr>
            <td>${d.idDerivacion}</td>
            <td>${d.nombrePaciente}</td>
            <td>${d.nombreProfesionalOrigen}</td>
            <td>${d.nombreProfesionalDestino}</td>
            <td>${d.especialidad}</td>
            <td>${fecha}</td>
            <td>${d.motivo ?? "-"}</td>
            <td><span class="estado-badge estado-${d.estado.toLowerCase()}">${d.estado}</span></td>
            <td>${acciones}</td>
        </tr>`;
    }).join("");

    c.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">${titulos["derivaciones"]} <span class="badge bg-secondary">${datos.length}</span></div>
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('derivaciones')">↺ Actualizar</button>
        </div>
        <div class="table-responsive">
            <table class="table table-hover tabla-datos">
                <thead><tr>
                    <th>#</th><th>Paciente</th><th>Prof. Actual</th><th>Prof. Solicitado</th>
                    <th>Especialidad</th><th>Fecha</th><th>Motivo</th><th>Estado</th><th>Acciones</th>
                </tr></thead>
                <tbody>${filas}</tbody>
            </table>
        </div>`;
}

// ── ASIGNAR PROFESIONAL DIRECTO ──────────────────────────────
async function abrirModalAsignar(idPac) {
    window._cambioPaciente = idPac;
    window._modoDirecto = false;
    window._modoAsignar = true;
    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    const yaAsignados = window._pacProfs[idPac] ?? new Set();

    const opciones = window._profesionales
        .filter(p => p.estado === "Activo" && !yaAsignados.has(p.idProfesional))
        .map(p => `<option value="${p.idProfesional}">${p.nombre}${p.apellido ? " " + p.apellido : ""} — ${p.especialidad ?? ""}</option>`)
        .join("");

    document.getElementById("modalDerivBody").innerHTML = `
        <p class="modal-texto">Asignar profesional a: <strong>${pac?.nombre}</strong></p>
        <div class="campo-modal">
            <label>Profesional</label>
            <select id="selectProfAsignar" class="input-modal">
                ${opciones || '<option value="">Todos los profesionales ya están asignados</option>'}
            </select>
        </div>`;

    document.getElementById("modalDerivOverlay").classList.add("visible");
    document.getElementById("modalDerivPanel").classList.add("visible");
}

// ── CAMBIO DIRECTO ───────────────────────────────────────────
async function abrirModalCambioDirecto(idPac) {
    window._cambioPaciente = idPac;
    const pac = window._pacientes.find(p => p.idPaciente === idPac);
    const relacionados = [...(window._pacProfs[idPac] ?? new Set())];

    const opcionesOrigen = relacionados.map(idP => {
        const pro = window._profesionales.find(p => p.idProfesional === idP);
        const nombre = pro ? (pro.nombre + (pro.apellido ? " " + pro.apellido : "")) : idP;
        return `<option value="${idP}" data-esp="${pro?.idEspecialidad ?? 0}">${nombre}</option>`;
    }).join("");

    document.getElementById("modalDerivBody").innerHTML = `
        <p class="modal-texto">Cambio para: <strong>${pac?.nombre}</strong></p>
        <div class="campo-modal">
            <label>Profesional actual</label>
            <select id="selectProfOrigen" class="input-modal" onchange="cargarProfesionalesDestino()">
                ${opcionesOrigen}
            </select>
        </div>
        <div class="campo-modal">
            <label>Nuevo profesional</label>
            <select id="selectProfDestino" class="input-modal"><option>Cargando...</option></select>
        </div>
        <div class="campo-modal">
            <label>Motivo</label>
            <textarea id="motivoCambioDir" rows="2" class="input-modal"></textarea>
        </div>`;

    document.getElementById("modalDerivOverlay").classList.add("visible");
    document.getElementById("modalDerivPanel").classList.add("visible");
    window._derivIdActual = null;
    window._modoDirecto = true;

    await cargarProfesionalesDestino();
}

async function cargarProfesionalesDestino() {
    const sel = document.getElementById("selectProfOrigen");
    const idEsp = parseInt(sel.options[sel.selectedIndex]?.dataset.esp ?? 0);
    const idOrigen = parseInt(sel.value);

    if (!idEsp) return;
    const profs = await fetch(`${API}/profesionales/activos/especialidad/${idEsp}`).then(r => r.json());
    const opciones = profs
        .filter(p => p.idProfesional !== idOrigen)
        .map(p => `<option value="${p.idProfesional}">${p.nombre}${p.apellido ? " " + p.apellido : ""}</option>`)
        .join("");
    document.getElementById("selectProfDestino").innerHTML = opciones || '<option value="">Sin profesionales disponibles</option>';
}

let _derivIdActual = null;

async function abrirModalDeriv(idDeriv, idEsp, idProfOrigen) {
    window._modoDirecto = false;
    _derivIdActual = idDeriv;

    const profs = await fetch(`${API}/profesionales/activos/especialidad/${idEsp}`).then(r => r.json());
    const opciones = profs
        .filter(p => p.idProfesional !== idProfOrigen)
        .map(p => `<option value="${p.idProfesional}">${p.nombre}${p.apellido ? " " + p.apellido : ""} — Mat. ${p.matricula ?? "-"}</option>`)
        .join("");

    document.getElementById("modalDerivBody").innerHTML = `
        <p class="modal-texto">Seleccioná el profesional que atenderá al paciente:</p>
        <div class="campo-modal">
            <label>Profesional destino</label>
            <select id="selectProfDeriv" class="input-modal">
                ${opciones || '<option value="">Sin profesionales disponibles</option>'}
            </select>
        </div>`;

    document.getElementById("modalDerivOverlay").classList.add("visible");
    document.getElementById("modalDerivPanel").classList.add("visible");
}

function cerrarModalDeriv() {
    document.getElementById("modalDerivOverlay").classList.remove("visible");
    document.getElementById("modalDerivPanel").classList.remove("visible");
    _derivIdActual = null;
    window._modoDirecto = false;
    window._modoAsignar = false;
}

async function confirmarAceptarDeriv() {
    if (window._modoAsignar) {
        const idProf = parseInt(document.getElementById("selectProfAsignar").value);
        if (!idProf) { toast("Seleccioná un profesional.", "warn"); return; }
        const res = await fetch(`${API}/pacientes/${window._cambioPaciente}/profesionales`, {
            method: "POST",
            headers: auditorHeaders(),
            body: JSON.stringify({ idProfesional: idProf })
        });
        cerrarModalDeriv();
        if (res.ok) { toast("Profesional asignado correctamente."); mostrarTab("usuarios"); }
        else toast("Error al asignar.", "error");
        return;
    }

    if (window._modoDirecto) {
        const idOrigen  = parseInt(document.getElementById("selectProfOrigen").value);
        const idDestino = parseInt(document.getElementById("selectProfDestino").value);
        const motivo    = document.getElementById("motivoCambioDir").value || null;
        if (!idDestino) { toast("Seleccioná un profesional destino.", "warn"); return; }

        const res = await fetch(`${API}/derivaciones/cambio-directo`, {
            method: "POST",
            headers: auditorHeaders(),
            body: JSON.stringify({
                idPaciente:           window._cambioPaciente,
                idProfesionalOrigen:  idOrigen,
                idProfesionalDestino: idDestino,
                motivo
            })
        });
        cerrarModalDeriv();
        if (res.ok) {
            toast("Cambio realizado. El profesional fue asignado correctamente.");
            mostrarTab("usuarios");
        } else toast("Error al realizar el cambio.", "error");
        return;
    }

    // Aceptar solicitud de derivación pendiente
    const idProf = parseInt(document.getElementById("selectProfDeriv").value);
    if (!idProf) { toast("Seleccioná un profesional.", "warn"); return; }

    const res = await fetch(`${API}/derivaciones/${_derivIdActual}/aceptar`, {
        method: "PUT",
        headers: auditorHeaders(),
        body: JSON.stringify({ idProfesionalDestino: idProf })
    });

    cerrarModalDeriv();
    if (res.ok) {
        toast("Solicitud aceptada. El profesional fue asignado correctamente.");
        mostrarTab("derivaciones");
    } else toast("Error al aceptar la solicitud.", "error");
}

async function rechazarDerivacion(id) {
    if (!await confirmar("¿Rechazar esta solicitud de cambio?")) return;
    await fetch(`${API}/derivaciones/${id}/rechazar`, { method: "PUT", headers: auditorHeaders() });
    toast("Solicitud rechazada.", "warn");
    mostrarTab("derivaciones");
}

// ── TABLA GENÉRICA ───────────────────────────────────────────
function renderTabla(titulo, cols, datos, tab, contenido) {
    const editable = !SOLO_LECTURA.includes(tab);

    if (!datos || datos.length === 0) {
        contenido.innerHTML = `<div class="titulo-seccion">${titulo}</div>
            <div class="alert alert-info">No hay registros para mostrar.</div>`;
        return;
    }

    const headers = cols.map(c => `<th>${c}</th>`).join("") + (editable ? "<th>Acciones</th>" : "");

    const filas = datos.map(row => {
        const id = row[idCampo[tab]];
        const celdas = cols.map(c => `<td>${row[c] ?? "-"}</td>`).join("");
        const acciones = editable ? `<td>
            <button class="btn-accion btn-editar" onclick='abrirModal(${JSON.stringify(row)})'>✏️</button>
            <button class="btn-accion btn-eliminar" onclick="eliminar('${tab}', ${id})">🗑️</button>
        </td>` : "";
        return `<tr>${celdas}${acciones}</tr>`;
    }).join("");

    contenido.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">${titulo} <span class="badge bg-secondary">${datos.length}</span>
                ${SOLO_LECTURA.includes(tab) ? '<span class="badge bg-secondary ms-2">Solo lectura</span>' : ""}
            </div>
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('${tab}')">↺ Actualizar</button>
        </div>
        <div class="table-responsive">
            <table class="table table-hover tabla-datos">
                <thead><tr>${headers}</tr></thead>
                <tbody>${filas}</tbody>
            </table>
        </div>`;
}

// ── MODAL EDICIÓN ────────────────────────────────────────────
function abrirModal(row) {
    filaEnEdicion = row;
    const tab = tabActual;

    document.getElementById("modalTitulo").textContent = `Editar — ${titulos[tab]}`;

    const body = document.getElementById("modalBody");
    body.innerHTML = (campos[tab] ?? []).map(c => {
        const val = row[c] ?? "";
        return `<div class="campo-modal">
            <label>${c}</label>
            <input type="text" id="field_${c}" value="${val}" class="input-modal">
        </div>`;
    }).join("");

    document.getElementById("modalOverlay").classList.add("visible");
    document.getElementById("modalPanel").classList.add("visible");
}

function cerrarModal() {
    document.getElementById("modalOverlay").classList.remove("visible");
    document.getElementById("modalPanel").classList.remove("visible");
    filaEnEdicion = null;
}

async function guardar() {
    const tab = tabActual;
    const id = filaEnEdicion[idCampo[tab]];
    const body = { ...filaEnEdicion };

    (campos[tab] ?? []).forEach(c => {
        const val = document.getElementById(`field_${c}`)?.value;
        if (val !== undefined) body[c] = val;
    });

    try {
        const res = await fetch(`${API}/${tab}/${id}`, {
            method: "PUT",
            headers: auditorHeaders(),
            body: JSON.stringify(body)
        });
        if (res.ok) { cerrarModal(); mostrarTab(tab); }
        else toast("Error al guardar.", "error");
    } catch { toast("No se pudo conectar con la API.", "error"); }
}

async function eliminar(tab, id) {
    if (!await confirmar("¿Eliminar este registro?")) return;
    try {
        const res = await fetch(`${API}/${tab}/${id}`, { method: "DELETE", headers: auditorHeaders() });
        if (res.ok || res.status === 204) { toast("Registro eliminado."); mostrarTab(tab); }
        else toast("No se pudo eliminar.", "error");
    } catch { toast("No se pudo conectar con la API.", "error"); }
}

// ── UTILS ────────────────────────────────────────────────────
function errorHtml(tab) {
    return `<div class="error-api">
        <div class="error-titulo">No se puede conectar con la API</div>
        <div class="error-desc">El servidor no está disponible en <strong>${API}</strong>.</div>
        <button class="btn-reintentar" onclick="mostrarTab('${tab}')">↺ Reintentar</button>
    </div>`;
}

async function cerrarSesion() {
    if (await confirmar("¿Cerrar sesión?")) window.location.href = "../index.html";
}

async function renderAuditoria(c, pagina = 1) {
    const datos = await fetch(`${API}/auditoria?pagina=${pagina}&cantidad=50`).then(r => r.json());

    if (!datos.length) {
        c.innerHTML = `<div class="titulo-seccion">${titulos["auditoria"]}</div>
            <div class="alert alert-info">No hay registros de auditoría.</div>`;
        return;
    }

    const filas = datos.map(d => {
        const fecha = new Date(d.fechaHora).toLocaleString("es-AR");
        return `<tr>
            <td>${fecha}</td>
            <td>${d.accion}</td>
            <td>${d.entidad}</td>
            <td>${d.idEntidad ?? "-"}</td>
            <td>${d.detalle ?? "-"}</td>
            <td>${d.idUsuario}</td>
        </tr>`;
    }).join("");

    c.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">${titulos["auditoria"]} <span class="badge bg-secondary">${datos.length}</span></div>
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('auditoria')">↺ Actualizar</button>
        </div>
        <div class="table-responsive">
            <table class="table table-hover tabla-datos">
                <thead><tr>
                    <th>Fecha/Hora</th><th>Acción</th><th>Entidad</th>
                    <th>ID Entidad</th><th>Detalle</th><th>ID Usuario</th>
                </tr></thead>
                <tbody>${filas}</tbody>
            </table>
        </div>`;
}

async function renderRecuperacion(c) {
    const datos = await fetch(`${API}/recuperacion-password/pendientes`).then(r => r.json());

    if (!datos.length) {
        c.innerHTML = `<div class="titulo-seccion">${titulos["recuperacion"]}</div>
            <div class="alert alert-info">No hay solicitudes pendientes.</div>`;
        return;
    }

    const filas = datos.map(d => {
        const fecha = new Date(d.fechaSolicitud).toLocaleString("es-AR");
        return `<tr>
            <td>${d.username ?? "-"}</td>
            <td>${d.mail ?? "-"}</td>
            <td>${fecha}</td>
            <td>
                <button class="btn-ok" onclick="aceptarRecuperacion(${d.idRecuperacion})">Aceptar</button>
                <button class="btn-ko" onclick="rechazarRecuperacion(${d.idRecuperacion})">Rechazar</button>
            </td>
        </tr>`;
    }).join("");

    c.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">${titulos["recuperacion"]} <span class="badge bg-secondary">${datos.length}</span></div>
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('recuperacion')">↺ Actualizar</button>
        </div>
        <div class="table-responsive">
            <table class="table table-hover tabla-datos">
                <thead><tr>
                    <th>Usuario</th><th>Mail</th><th>Fecha Solicitud</th><th>Acciones</th>
                </tr></thead>
                <tbody>${filas}</tbody>
            </table>
        </div>`;
}

async function aceptarRecuperacion(id) {
    if (!await confirmar("¿Generar contraseña temporal para este usuario?")) return;
    const res = await fetch(`${API}/recuperacion-password/${id}/aceptar`, { method: "PUT" });
    if (!res.ok) { toast("Error al procesar la solicitud.", "error"); return; }
    const data = await res.json();
    toast(`Solicitud aceptada. Contraseña temporal: ${data.passwordTemporal}`, "ok", 8000);
    mostrarTab("recuperacion");
}

async function rechazarRecuperacion(id) {
    if (!await confirmar("¿Rechazar esta solicitud de recuperación?")) return;
    const res = await fetch(`${API}/recuperacion-password/${id}/rechazar`, { method: "PUT" });
    if (res.ok) { toast("Solicitud rechazada.", "warn"); mostrarTab("recuperacion"); }
    else toast("Error al rechazar.", "error");
}

async function reportes() {
    const c = document.getElementById('contenido');
    c.innerHTML = '<div class="spinner">Cargando...</div>';

    let profesionales = [];
    try {
        const r = await fetch(API + '/profesionales');
        profesionales = await r.json();
    } catch(e) {}

    const opts = profesionales
        .filter(function(p) { return p.estado === 'Activo'; })
        .map(function(p) { return '<option value="' + p.idProfesional + '">' + p.nombre + '</option>'; })
        .join('');

    c.innerHTML =
        '<h2>Reportes</h2>' +
        '<div class="tarjeta">' +
        '<h3 class="tarjeta-titulo">Atenciones por Profesional</h3>' +
        '<div class="fila-form">' +
        '<div>' +
        '<label for="filtroProf">Filtrar por profesional</label>' +
        '<select id="filtroProf">' +
        '<option value="">Todos los profesionales</option>' +
        opts +
        '</select>' +
        '</div>' +
        '<button class="btn-primario" onclick="descargarReporteConf()">Descargar Excel</button>' +
        '<button class="btn-exito" onclick="descargarReporteHtml()">Descargar Reporte HTML</button>' +
        '<button class="btn-accion" onclick="verEstadisticas()">Ver Estadisticas</button>' +
        '</div>' +
        '<p class="texto-suave">Selecciona un profesional o deja en blanco para ver todos.</p>' +
        '</div>' +
        '<div id="panelEstadisticas" class="tarjeta" style="display:none">' +
        '<div class="d-flex justify-content-between align-items-center mb-3">' +
        '<h3 class="tarjeta-titulo mb-0">Estadisticas</h3>' +
        '<button class="btn-exito" onclick="descargarGrafico()">Descargar Imagen</button>' +
        '</div>' +
        '<canvas id="graficoAtenciones" class="canvas-grafico"></canvas>' +
        '</div>';
}

async function verEstadisticas() {
    const idProf = document.getElementById('filtroProf').value;
    const url = API + '/reportes/atenciones' + (idProf ? '?idProfesional=' + idProf : '');
    let datos = [];
    try {
        const r = await fetch(url);
        datos = await r.json();
    } catch(e) {
        alert('Error al obtener los datos.');
        return;
    }
    if (!datos.length) {
        alert('No hay datos para mostrar.');
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
    panel.style.display = 'block';

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
    if (!canvas) { alert('Primero genera las estadisticas.'); return; }
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
        const r = await fetch(url);
        datos = await r.json();
    } catch(e) {
        alert('Error al obtener los datos.');
        return;
    }
    if (!datos.length) {
        alert('No hay datos para el filtro seleccionado.');
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
        res = await fetch(url, { headers: { "X-Usuario-Id": localStorage.getItem("idUsuario") ?? "0" } });
    } catch(e) {
        alert('Error al generar el reporte.');
        return;
    }
    if (res.status === 403) {
        let msg = 'Sin permiso para descargar este reporte.';
        try { const j = await res.json(); if (j && j.mensaje) msg = j.mensaje; } catch(e) {}
        alert(msg);
        return;
    }
    if (!res.ok) {
        alert('Error al generar el reporte.');
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
    c.innerHTML = '<div class="spinner">Cargando...</div>';
    try {
        const r = await fetch(API + "/informes-admision-detalle");
        _admisionesCache = await r.json();
    } catch(e) { _admisionesCache = []; }

    const badge = function(estado) {
        const colores = { "Pendiente": "#fff3cd|#856404", "En entrevista": "#cfe2ff|#084298", "Derivado": "#d1e7dd|#0f5132", "Rechazado": "#f8d7da|#842029" };
        const par = (colores[estado] || "#e2e3e5|#41464b").split("|");
        return '<span class="estado-badge estado-' + String(estado).replace(/\s+/g, '-') + '">' + estado + '</span>';
    };

    const filas = _admisionesCache.map(function(a) {
        return '<tr>' +
            '<td>' + a.nombrePaciente + '</td>' +
            '<td>' + a.dni + '</td>' +
            '<td>' + a.especialidad + '</td>' +
            '<td>' + a.fechaCreacion + '</td>' +
            '<td>' + badge(a.estado) + '</td>' +
            '<td><button class="btn btn-sm btn-outline-secondary" onclick="verAdmision(' + a.idInforme + ')">Ver</button></td>' +
            '</tr>';
    }).join("");

    c.innerHTML = '<h2>Admisiones</h2>' +
        '<div class="tarjeta">' +
        (_admisionesCache.length === 0 ? '<p class="texto-suave">No hay informes de admision.</p>' :
        '<table class="table"><thead><tr><th>Paciente</th><th>DNI</th><th>Especialidad</th><th>Fecha</th><th>Estado</th><th></th></tr></thead><tbody>' + filas + '</tbody></table>') +
        '</div>' +
        '<div id="detalleAdmision" class="mt-16"></div>';
}

function verAdmision(idInforme) {
    const a = _admisionesCache.find(function(x) { return x.idInforme === idInforme; });
    if (a === undefined) return;

    const campo = function(label, valor) {
        if (valor === null || valor === undefined || valor === "") return "";
        return '<div class="dato-admision"><span class="etiqueta-dato">' + label + '</span><span class="valor-dato">' + valor + '</span></div>';
    };

    const esEntrevista = a.estado === "En entrevista" || a.estado === "Rechazado";
    const esPendiente  = a.estado === "Pendiente";
    const esDerivado   = a.estado === "Derivado";

    let acciones = "";
    if (esPendiente) {
        acciones = '<div class="caja-accion">' +
            '<h4>Programar primera entrevista</h4>' +
            '<div class="fila-form">' +
            '<div class="campo-form"><label for="entFecha">Fecha</label><input type="date" id="entFecha"></div>' +
            '<div class="campo-form"><label for="entHora">Hora</label><input type="time" id="entHora"></div>' +
            '<button class="btn-primario" onclick="programarEntrevista(' + a.idInforme + ')">Programar</button>' +
            '</div></div>';
    }
    if (esEntrevista) {
        acciones = '<div class="caja-accion">' +
            '<h4>Derivar al profesional</h4>' +
            (a.estado === "Rechazado" ? '<p class="ui-alerta ui-alerta--danger mb-3"><span><strong>Rechazado por el profesional anterior:</strong> ' + (a.motivoRechazo || "Sin motivo") + '</span></p>' : '') +
            '<p class="texto-chico mb-1">Entrevista programada: ' + (a.fechaEntrevista || "-") + '</p>' +
            '<p class="mb-3">Codigo de sala: <strong class="codigo-sala">' + (a.codigoSala || "-") + '</strong> <button onclick="iniciarEntrevistaVideo(\'' + a.codigoSala + '\')" class="btn-exito ms-2">Iniciar entrevista</button></p>' +
            '<div class="campo-form mb-3"><label for="derObs">Observaciones de la entrevista</label>' +
            '<textarea id="derObs" rows="3">' + (a.observacionesEntrevista || "") + '</textarea></div>' +
            '<div class="campo-form"><label for="derProf">Profesional asignado</label></div>' +
            '<div class="fila-form">' +
            '<div class="campo-form campo-form--ancho"><select id="derProf"><option value="">Cargando...</option></select></div>' +
            '<button class="btn-primario" onclick="derivarAdmision(' + a.idInforme + ')">Derivar</button>' +
            '</div></div>';
    }
    if (esDerivado) {
        acciones = '<div class="ui-alerta ui-alerta--ok mt-16 d-block">' +
            '<strong>Derivado a: ' + (a.nombreProfesionalDerivado || "-") + '</strong>' +
            (a.observacionesEntrevista ? '<p class="mt-2 mb-0">' + a.observacionesEntrevista + '</p>' : "") +
            '</div>';
    }

    document.getElementById("detalleAdmision").innerHTML =
        '<div class="tarjeta">' +
        '<h3 class="tarjeta-titulo">Informe de ' + a.nombrePaciente + '</h3>' +
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
        '<button class="btn btn-sm btn-outline-secondary mt-2" onclick="verHCAdmision(' + a.idPaciente + ')">Ver Historia Clinica completa</button>' +
        acciones +
        '</div>';

    if (esEntrevista) cargarProfesionalesDerivacion();
    document.getElementById("detalleAdmision").scrollIntoView({ behavior: "smooth" });
}

async function cargarProfesionalesDerivacion() {
    let profs = [];
    try { profs = await (await fetch(API + "/profesionales")).json(); } catch(e) {}
    const sel = document.getElementById("derProf");
    if (sel === null) return;
    sel.innerHTML = '<option value="">Selecciona un profesional</option>' + profs
        .filter(function(p) { return p.estado === "Activo"; })
        .map(function(p) { return '<option value="' + p.idProfesional + '">' + p.nombre + " " + (p.apellido || "") + '</option>'; })
        .join("");
}

async function programarEntrevista(idInforme) {
    const fecha = document.getElementById("entFecha").value;
    const hora  = document.getElementById("entHora").value;
    if (fecha === "" || hora === "") { toast("Completa fecha y hora.", "warn"); return; }
    const r = await fetch(API + "/informes-admision/" + idInforme + "/entrevista", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fecha: fecha + "T" + hora + ":00", observaciones: null })
    });
    if (r.ok) { const resp = await r.json(); toast("Entrevista programada. Codigo de sala: " + resp.codigoSala + ". El paciente fue notificado."); admisiones(); }
    else toast("Error al programar.", "error");
}

async function derivarAdmision(idInforme) {
    const idProf = parseInt(document.getElementById("derProf").value);
    const obs    = document.getElementById("derObs").value || null;
    if (isNaN(idProf)) { toast("Selecciona un profesional.", "warn"); return; }
    const r = await fetch(API + "/informes-admision/" + idInforme + "/derivar", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ idProfesional: idProf, observaciones: obs })
    });
    if (r.ok) { toast("Paciente derivado. Ya aparece en Mis Pacientes del profesional."); admisiones(); }
    else toast("Error al derivar.", "error");
}

async function verHCAdmision(idPaciente) {
    let paciente = null, historia = null, evoluciones = [], sesiones = [];
    try { paciente = await (await fetch(API + "/pacientes/" + idPaciente)).json(); } catch(e) {}
    try { historia = await (await fetch(API + "/pacientes/" + idPaciente + "/historiaclinica")).json(); } catch(e) {}
    if (historia !== null && historia.idHistoria !== undefined) {
        try { evoluciones = await (await fetch(API + "/historiasclinicas/" + historia.idHistoria + "/evoluciones")).json(); } catch(e) {}
    }
    try { sesiones = await (await fetch(API + "/pacientes/" + idPaciente + "/sesiones-clinicas")).json(); } catch(e) {}

    const evHtml = evoluciones.map(function(e) {
        return '<div class="item-historia">' +
            '<strong>' + new Date(e.fecha).toLocaleDateString("es-AR") + '</strong>' +
            (e.diagnostico ? '<div><em>Diagnostico:</em> ' + e.diagnostico + '</div>' : "") +
            (e.observaciones ? '<div><em>Observaciones:</em> ' + e.observaciones + '</div>' : "") +
            '</div>';
    }).join("") || '<p class="texto-suave">Sin evoluciones.</p>';

    const sesHtml = sesiones.map(function(s) {
        return '<div class="item-historia item-historia--sesion">' +
            '<strong>' + new Date(s.fechaHora).toLocaleDateString("es-AR") + '</strong>' +
            (s.tipoCierre ? " - " + s.tipoCierre : "") +
            (s.notasEvolucion ? '<div>' + s.notasEvolucion + '</div>' : "") +
            '</div>';
    }).join("") || '<p class="texto-suave">Sin sesiones clinicas.</p>';

    document.getElementById("detalleAdmision").innerHTML +=
        '<div class="tarjeta mt-16">' +
        '<h3 class="tarjeta-titulo">Historia Clinica - ' + (paciente !== null ? paciente.nombre : "") + '</h3>' +
        '<h4 class="mt-3">Evoluciones</h4>' + evHtml +
        '<h4 class="mt-3">Sesiones clinicas</h4>' + sesHtml +
        '</div>';
}

async function iniciarEntrevistaVideo(codigoSala) {
    if (codigoSala === "" || codigoSala === "null" || codigoSala === "-") { toast("Este informe no tiene codigo de sala.", "warn"); return; }
    let idAdm = null;
    try {
        const profs = await (await fetch(API + "/profesionales")).json();
        const adm = profs.find(function(p) { return p.matricula === "ADM-001"; });
        if (adm !== undefined) idAdm = adm.idProfesional;
    } catch(e) {}
    if (idAdm === null) { toast("Falta crear el profesional admisionista (ADM-001).", "error"); return; }
    localStorage.setItem("idProfesional", idAdm);
    window.open("../Pantalla_Profesional/sesion_video.html?sala=" + encodeURIComponent(codigoSala), "_blank");
}
