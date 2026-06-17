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
            <label style="display:flex;align-items:center;gap:8px;font-size:13px;color:#555;cursor:pointer">
                <input type="checkbox" id="chkDadosDeBaja" onchange="toggleDadosDeBaja()"
                    style="width:16px;height:16px;cursor:pointer">
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
        </div>`).join("") || `<div style="padding:12px;color:#aaa;font-size:13px">Sin resultados.</div>`;

    document.getElementById("listaProfesionales").innerHTML = profesionales.map(p => `
        <div class="usr-card${mostrarBaja ? " usr-card-inactivo" : ""}" id="prof-${p.idProfesional}" onclick="seleccionarProfesional(${p.idProfesional})">
            <div class="usr-nombre">${p.nombre ?? "-"}${p.apellido ? " " + p.apellido : ""}</div>
            <div class="usr-sub">Mat: ${p.matricula ?? "-"} &bull; ${p.estado ?? "-"}</div>
        </div>`).join("") || `<div style="padding:12px;color:#aaa;font-size:13px">Sin resultados.</div>`;

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
        <p style="color:#555;margin-bottom:16px">Asignar profesional a: <strong>${pac?.nombre}</strong></p>
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
        <p style="color:#555;margin-bottom:16px">Cambio para: <strong>${pac?.nombre}</strong></p>
        <div class="campo-modal" style="margin-bottom:14px">
            <label>Profesional actual</label>
            <select id="selectProfOrigen" class="input-modal" onchange="cargarProfesionalesDestino()">
                ${opcionesOrigen}
            </select>
        </div>
        <div class="campo-modal" style="margin-bottom:14px">
            <label>Nuevo profesional</label>
            <select id="selectProfDestino" class="input-modal"><option>Cargando...</option></select>
        </div>
        <div class="campo-modal">
            <label>Motivo</label>
            <textarea id="motivoCambioDir" rows="2" class="input-modal" style="height:auto;padding:10px"></textarea>
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
        <p style="color:#555;margin-bottom:16px">Seleccioná el profesional que atenderá al paciente:</p>
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
                ${SOLO_LECTURA.includes(tab) ? '<span class="badge bg-secondary ms-2" style="font-size:11px">Solo lectura</span>' : ""}
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
