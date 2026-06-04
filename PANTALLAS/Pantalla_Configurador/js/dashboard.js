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
    turnos:          ["idTurno", "idPaciente", "idProfesional", "idEstadoTurno", "fecha", "hora", "modalidad"],
    evoluciones:     ["idEvolucion", "idHistoria", "idProfesional", "fecha", "diagnostico"],
    eventoscrisis:   ["idEvento", "idPaciente", "fecha", "nivelRiesgo", "acciones"],
    roles:           ["idRol", "nombre", "estado"],
    especialidades:  ["idEspecialidad", "nombre", "estado"],
    coberturas:      ["idCobertura", "idPaciente", "idObraSocial", "nroAfiliado", "planCobertura", "estado"],
    interaccionesia: ["idInteraccion", "idPaciente", "fecha", "consulta"],
};

const ETIQUETAS = {
    idTurno: "Turno", idAgenda: "Agenda", idEvolucion: "Evolución", idEvento: "Evento",
    idRol: "Rol", idEspecialidad: "Especialidad", idCobertura: "Cobertura", idInteraccion: "Interacción",
    idPaciente: "Paciente", idProfesional: "Profesional",
    idEstadoTurno: "Estado", idObraSocial: "Obra social", idHistoria: "Historia clínica",
    fecha: "Fecha", hora: "Hora", horaInicio: "Hora inicio", horaFin: "Hora fin",
    dia: "Día", modalidad: "Modalidad", motivo: "Motivo", disponible: "Disponible",
    nombre: "Nombre", apellido: "Apellido", estado: "Estado", descripcion: "Descripción",
    diagnostico: "Diagnóstico", nivelRiesgo: "Nivel de riesgo", acciones: "Acciones",
    nroAfiliado: "N° afiliado", planCobertura: "Plan", consulta: "Consulta", respuesta: "Respuesta",
    mail: "Mail", matricula: "Matrícula", provincia: "Provincia", fechaSolicitud: "Fecha de solicitud",
    dni: "DNI", username: "Usuario",
};

function etiqueta(col) {
    return ETIQUETAS[col] ?? col;
}

const LOOKUPS = { pacientes: {}, profesionales: {}, estados: {}, obras: {} };
let _lookupsCargados = false;

async function cargarLookups() {
    if (_lookupsCargados) return;
    const get = url => fetch(`${API}${url}`).then(r => r.json()).catch(() => []);
    const [pacientes, profesionales, estados, obras] = await Promise.all([
        get("/pacientes"), get("/profesionales"), get("/estadosturnos"), get("/obrassociales"),
    ]);
    pacientes.forEach(p => LOOKUPS.pacientes[p.idPaciente] = p.nombre ?? `#${p.idPaciente}`);
    profesionales.forEach(p => LOOKUPS.profesionales[p.idProfesional] =
        `${p.nombre ?? ""}${p.apellido ? " " + p.apellido : ""}`.trim() || `#${p.idProfesional}`);
    estados.forEach(e => LOOKUPS.estados[e.idEstadoTurno] = e.descripcion ?? `#${e.idEstadoTurno}`);
    obras.forEach(o => LOOKUPS.obras[o.idObraSocial] = o.nombre ?? `#${o.idObraSocial}`);
    _lookupsCargados = true;
}

const RESOLVERS = {
    idPaciente:    v => LOOKUPS.pacientes[v]     ?? `#${v}`,
    idProfesional: v => LOOKUPS.profesionales[v] ?? `#${v}`,
    idEstadoTurno: v => LOOKUPS.estados[v]       ?? `#${v}`,
    idObraSocial:  v => LOOKUPS.obras[v]         ?? `#${v}`,
};

const PK = new Set(["idTurno", "idAgenda", "idEvolucion", "idEvento", "idRol", "idEspecialidad", "idCobertura", "idInteraccion"]);

function formatearCelda(col, valor) {
    if (valor === null || valor === undefined || valor === "") return "-";
    if (RESOLVERS[col]) return RESOLVERS[col](valor);
    if (PK.has(col)) return `<span class="celda-id">#${valor}</span>`;
    if (col === "fecha") return new Date(valor).toLocaleDateString("es-AR");
    if (col === "hora" || col === "horaInicio" || col === "horaFin")
        return typeof valor === "string" ? valor.slice(0, 5) : valor;
    if (col === "disponible")
        return valor === true || valor === "true" || valor === 1
            ? `<span class="estado-badge estado-aceptada">Sí</span>`
            : `<span class="estado-badge estado-rechazada">No</span>`;
    return String(valor);
}

function valorCelda(col, valor) {
    if (RESOLVERS[col]) return RESOLVERS[col](valor);
    if (col === "fecha" && valor) return new Date(valor).toLocaleDateString("es-AR");
    return valor ?? "";
}

const titulos = {
    usuarios:        "Usuarios",
    solicitudes:     "Solicitudes de Registro",
    derivaciones:    "Solicitudes de Cambio de Profesional",
    turnos:          "Turnos",
    agendas:         "Agendas",
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

// ── AGENDA COMPLETA ──────────────────────────────────────────

const MOTIVOS_BLOQUEO = [
    "Cambio de adscripción","Capacitación","Comisión oficial","Comisión sindical",
    "Defunción","Día festivo","Docencia","Doctor enfermo",
    "Doctor estudios de postgrado","Doctor haciendo clase universidad",
    "Doctor reunión colegio de doctores","Educación continua","Incapacidad",
    "Licencia con goce de sueldo","Licencia sin goce de sueldo","Otra",
    "Prestación laboral","Renuncia","Vacaciones"
];

const DIAS_SEMANA = ["Lunes","Martes","Miercoles","Jueves","Viernes","Sabado","Domingo"];

async function renderAgendas(c) {
    const [agendas, servicios, profesionales] = await Promise.all([
        fetch(`${API}/agendas`).then(r => r.json()).catch(() => []),
        fetch(`${API}/servicios`).then(r => r.json()).catch(() => []),
        fetch(`${API}/profesionales`).then(r => r.json()).catch(() => [])
    ]);

    window._servicios     = servicios;
    window._profesionales = profesionales;

    const mapaServicios = {};
    servicios.forEach(s => mapaServicios[s.idServicio] = s.nombre);

    c.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">Agendas</div>
            <button class="btn btn-guardar btn-sm" onclick="abrirFormAgenda(null)">+ Nueva agenda</button>
        </div>
        <div class="table-responsive">
            <table class="table table-hover tabla-datos">
                <thead><tr>
                    <th>#</th><th>Código</th><th>Descripción</th>
                    <th>Servicio</th><th>Fecha inicio</th><th>Fecha fin</th><th>Acciones</th>
                </tr></thead>
                <tbody>
                ${agendas.length ? agendas.map(a => `
                    <tr>
                        <td><span class="celda-id">#${a.idAgenda}</span></td>
                        <td>${a.codigo ?? '-'}</td>
                        <td>${a.descripcion ?? '-'}</td>
                        <td>${mapaServicios[a.idServicio] ?? '-'}</td>
                        <td>${a.fechaInicio ? new Date(a.fechaInicio).toLocaleDateString('es-AR') : '-'}</td>
                        <td>${a.fechaFin    ? new Date(a.fechaFin).toLocaleDateString('es-AR')    : '-'}</td>
                        <td class="col-acciones">
                            <button class="btn-tabla btn-tabla-editar" onclick="abrirFormAgenda(${a.idAgenda})">Editar</button>
                            <button class="btn-tabla btn-tabla-eliminar" onclick="eliminarAgenda(${a.idAgenda})">Eliminar</button>
                        </td>
                    </tr>`).join('') : `<tr><td colspan="7" class="tabla-sin-resultados">No hay agendas cargadas.</td></tr>`}
                </tbody>
            </table>
        </div>`;
}

async function abrirFormAgenda(idAgenda) {
    const c = document.getElementById('contenido');
    c.innerHTML = `<div class="text-center mt-5"><div class="spinner-border text-primary"></div></div>`;

    const [servicios, profesionales, tiposProfesional] = await Promise.all([
        fetch(`${API}/servicios`).then(r => r.json()).catch(() => []),
        fetch(`${API}/profesionales`).then(r => r.json()).catch(() => []),
        fetch(`${API}/tiposprofesional`).then(r => r.json()).catch(() => [])
    ]);

    let agenda = { idAgenda: null, codigo: '', descripcion: '', fechaInicio: '', fechaFin: '', idServicio: '' };
    let propietarios = [];
    let horarios     = [];

    if (idAgenda) {
        const [a, p, h] = await Promise.all([
            fetch(`${API}/agendas/${idAgenda}`).then(r => r.json()).catch(() => null),
            fetch(`${API}/agendas/${idAgenda}/propietarios`).then(r => r.json()).catch(() => []),
            fetch(`${API}/agendas/${idAgenda}/horarios`).then(r => r.json()).catch(() => [])
        ]);
        if (a) agenda = a;
        propietarios = p;
        horarios     = h;
    }

    window._tiposProfesional  = tiposProfesional;
    window._profesionalesForm = profesionales;

    c.innerHTML = `
        <div class="d-flex align-items-center gap-3 mb-3">
            <button class="btn btn-sm btn-outline-secondary" onclick="renderAgendas(document.getElementById('contenido'))">← Volver</button>
            <div class="titulo-seccion mb-0">${idAgenda ? 'Editar agenda' : 'Nueva agenda'}</div>
        </div>

        <!-- CABECERA -->
        <div class="card mb-3 p-3">
            <div class="fw-bold mb-2" style="font-size:13px;color:#888;text-transform:uppercase;letter-spacing:.05em">Datos de la agenda</div>
            <div class="row g-2">
                <div class="col-md-3">
                    <label class="form-label">Código</label>
                    <input type="text" id="ag-codigo" class="form-control form-control-sm" value="${agenda.codigo ?? ''}">
                </div>
                <div class="col-md-5">
                    <label class="form-label">Descripción</label>
                    <input type="text" id="ag-descripcion" class="form-control form-control-sm" value="${agenda.descripcion ?? ''}">
                </div>
                <div class="col-md-4">
                    <label class="form-label">Servicio</label>
                    <select id="ag-servicio" class="form-select form-select-sm">
                        <option value="">-- Seleccioná --</option>
                        ${servicios.map(s => `<option value="${s.idServicio}" ${agenda.idServicio == s.idServicio ? 'selected' : ''}>${s.nombre}</option>`).join('')}
                    </select>
                </div>
                <div class="col-md-3">
                    <label class="form-label">Fecha inicio</label>
                    <input type="date" id="ag-finicio" class="form-control form-control-sm" value="${agenda.fechaInicio ? String(agenda.fechaInicio).slice(0,10) : ''}">
                </div>
                <div class="col-md-3">
                    <label class="form-label">Fecha fin</label>
                    <input type="date" id="ag-ffin" class="form-control form-control-sm" value="${agenda.fechaFin ? String(agenda.fechaFin).slice(0,10) : ''}">
                </div>
            </div>
        </div>

        <!-- PROPIETARIOS -->
        <div class="card mb-3 p-3">
            <div class="d-flex justify-content-between align-items-center mb-2">
                <div class="fw-bold" style="font-size:13px;color:#888;text-transform:uppercase;letter-spacing:.05em">Propietarios</div>
                <button class="btn btn-sm btn-outline-primary" onclick="agregarPropietario()">+ Agregar</button>
            </div>
            <div id="lista-propietarios">
                ${propietarios.length ? propietarios.map((p,i) => htmlPropietario(p, i, profesionales, tiposProfesional)).join('') : '<p style="font-size:13px;color:#aaa">Sin propietarios.</p>'}
            </div>
        </div>

        <!-- HORARIOS -->
        <div class="card mb-3 p-3">
            <div class="d-flex justify-content-between align-items-center mb-2">
                <div class="fw-bold" style="font-size:13px;color:#888;text-transform:uppercase;letter-spacing:.05em">Horarios</div>
                <button class="btn btn-sm btn-outline-primary" onclick="agregarHorario()">+ Agregar</button>
            </div>
            <div id="lista-horarios">
                ${horarios.length ? horarios.map((h,i) => htmlHorario(h, i)).join('') : '<p style="font-size:13px;color:#aaa">Sin horarios.</p>'}
            </div>
        </div>

        <!-- BLOQUEOS (solo si la agenda ya existe) -->
        ${idAgenda ? `
        <div class="card mb-3 p-3">
            <div class="d-flex justify-content-between align-items-center mb-2">
                <div class="fw-bold" style="font-size:13px;color:#888;text-transform:uppercase;letter-spacing:.05em">Bloqueos</div>
                <button class="btn btn-sm btn-outline-danger" onclick="abrirModalBloqueo(${idAgenda})">+ Agregar bloqueo</button>
            </div>
            <div id="lista-bloqueos"><p style="font-size:13px;color:#aaa">Cargando...</p></div>
        </div>` : ''}

        <div class="d-flex gap-2 mb-4">
            <button class="btn btn-guardar" onclick="guardarAgenda(${idAgenda ?? 'null'})">Guardar agenda</button>
            <button class="btn btn-secondary btn-sm" onclick="renderAgendas(document.getElementById('contenido'))">Cancelar</button>
        </div>`;

    if (idAgenda) cargarBloqueosAgenda(idAgenda);
}

function htmlPropietario(p, i, profesionales, tiposProfesional) {
    const optsProf = profesionales
        .filter(pr => pr.estado === 'Activo')
        .map(pr => `<option value="${pr.idProfesional}" ${p.idProfesional == pr.idProfesional ? 'selected' : ''}>${pr.nombre} ${pr.apellido ?? ''}</option>`).join('');
    const optsTipo = tiposProfesional
        .map(t => `<option value="${t.idTipoProfesional}" ${p.idTipoProfesional == t.idTipoProfesional ? 'selected' : ''}>${t.nombre}</option>`).join('');

    return `
        <div class="row g-2 align-items-center mb-2" id="prop-row-${i}">
            <div class="col-md-4">
                <select class="form-select form-select-sm prop-profesional" data-idx="${i}">
                    <option value="">-- Profesional --</option>${optsProf}
                </select>
            </div>
            <div class="col-md-3">
                <select class="form-select form-select-sm prop-tipo" data-idx="${i}">
                    <option value="">-- Tipo --</option>${optsTipo}
                </select>
            </div>
            <div class="col-md-3 d-flex align-items-center gap-2">
                <input type="checkbox" id="prop-urgente-${i}" class="prop-urgente" data-idx="${i}" ${p.puedeCitaUrgente ? 'checked' : ''}>
                <label for="prop-urgente-${i}" style="font-size:13px;margin:0">Cita urgente</label>
            </div>
            <div class="col-md-2">
                <button class="btn btn-sm btn-outline-danger w-100" onclick="quitarPropietario(${i})">Quitar</button>
            </div>
        </div>`;
}

function htmlHorario(h, i) {
    return `
        <div class="border rounded p-2 mb-2" id="hor-row-${i}">
            <div class="row g-2 mb-2">
                <div class="col-md-2">
                    <label class="form-label" style="font-size:12px">Código</label>
                    <input type="text" class="form-control form-control-sm hor-codigo" data-idx="${i}" value="${h.codigo ?? ''}">
                </div>
                <div class="col-md-4">
                    <label class="form-label" style="font-size:12px">Descripción</label>
                    <input type="text" class="form-control form-control-sm hor-descripcion" data-idx="${i}" value="${h.descripcion ?? ''}">
                </div>
                <div class="col-md-2">
                    <label class="form-label" style="font-size:12px">Hora inicio</label>
                    <input type="time" class="form-control form-control-sm hor-inicio" data-idx="${i}" value="${h.horaInicio ? String(h.horaInicio).slice(0,5) : ''}">
                </div>
                <div class="col-md-2">
                    <label class="form-label" style="font-size:12px">Hora fin</label>
                    <input type="time" class="form-control form-control-sm hor-fin" data-idx="${i}" value="${h.horaFin ? String(h.horaFin).slice(0,5) : ''}">
                </div>
                <div class="col-md-1">
                    <label class="form-label" style="font-size:12px">Intervalo</label>
                    <input type="number" class="form-control form-control-sm hor-intervalo" data-idx="${i}" value="${h.intervaloMinutos ?? 30}" min="5" max="120">
                </div>
                <div class="col-md-1">
                    <label class="form-label" style="font-size:12px">Máx. citas</label>
                    <input type="number" class="form-control form-control-sm hor-maxcitas" data-idx="${i}" value="${h.maxCitas ?? 1}" min="1">
                </div>
            </div>
            <div class="row g-2 mb-2">
                <div class="col-md-3">
                    <label class="form-label" style="font-size:12px">Fecha inicio</label>
                    <input type="date" class="form-control form-control-sm hor-finicio" data-idx="${i}" value="${h.fechaInicio ? String(h.fechaInicio).slice(0,10) : ''}">
                </div>
                <div class="col-md-3">
                    <label class="form-label" style="font-size:12px">Fecha fin</label>
                    <input type="date" class="form-control form-control-sm hor-ffin" data-idx="${i}" value="${h.fechaFin ? String(h.fechaFin).slice(0,10) : ''}">
                </div>
                <div class="col-md-6 d-flex align-items-end gap-3 pb-1">
                    ${DIAS_SEMANA.map(d => `
                        <div class="d-flex flex-column align-items-center">
                            <span style="font-size:11px">${d.slice(0,3)}</span>
                            <input type="checkbox" class="hor-dia" data-dia="${d.toLowerCase()}" data-idx="${i}"
                                ${h[d.toLowerCase()] ? 'checked' : ''}>
                        </div>`).join('')}
                </div>
            </div>
            <div class="d-flex align-items-center gap-3">
                <div class="d-flex align-items-center gap-2">
                    <input type="checkbox" id="hor-festivos-${i}" class="hor-festivos" data-idx="${i}" ${h.trabajeFestivos ? 'checked' : ''}>
                    <label for="hor-festivos-${i}" style="font-size:13px;margin:0">Trabaja festivos</label>
                </div>
                <button class="btn btn-sm btn-outline-danger ms-auto" onclick="quitarHorario(${i})">Quitar horario</button>
            </div>
        </div>`;
}

function agregarPropietario() {
    const lista = document.getElementById('lista-propietarios');
    const idx = lista.querySelectorAll('[id^="prop-row-"]').length;
    const profesionales    = window._profesionalesForm ?? [];
    const tiposProfesional = window._tiposProfesional ?? [];
    lista.innerHTML = lista.innerHTML.replace('<p style="font-size:13px;color:#aaa">Sin propietarios.</p>', '');
    lista.insertAdjacentHTML('beforeend', htmlPropietario({}, idx, profesionales, tiposProfesional));
}

function quitarPropietario(i) {
    document.getElementById(`prop-row-${i}`)?.remove();
}

function agregarHorario() {
    const lista = document.getElementById('lista-horarios');
    const idx = lista.querySelectorAll('[id^="hor-row-"]').length;
    lista.innerHTML = lista.innerHTML.replace('<p style="font-size:13px;color:#aaa">Sin horarios.</p>', '');
    lista.insertAdjacentHTML('beforeend', htmlHorario({}, idx));
}

function quitarHorario(i) {
    document.getElementById(`hor-row-${i}`)?.remove();
}

function leerPropietarios() {
    const rows = document.querySelectorAll('[id^="prop-row-"]');
    return [...rows].map(row => {
        return {
            idProfesional:     parseInt(row.querySelector('.prop-profesional').value),
            idTipoProfesional: parseInt(row.querySelector('.prop-tipo').value),
            puedeCitaUrgente:  row.querySelector('.prop-urgente').checked
        };
    }).filter(p => p.idProfesional && p.idTipoProfesional);
}

function leerHorarios() {
    const rows = document.querySelectorAll('[id^="hor-row-"]');
    return [...rows].map(row => {
        const dias = {};
        row.querySelectorAll('.hor-dia').forEach(cb => dias[cb.dataset.dia] = cb.checked);
        return {
            codigo:           row.querySelector('.hor-codigo').value || null,
            descripcion:      row.querySelector('.hor-descripcion').value || null,
            horaInicio:       row.querySelector('.hor-inicio').value,
            horaFin:          row.querySelector('.hor-fin').value,
            fechaInicio:      row.querySelector('.hor-finicio').value,
            fechaFin:         row.querySelector('.hor-ffin').value || null,
            intervaloMinutos: parseInt(row.querySelector('.hor-intervalo').value) || 30,
            maxCitas:         parseInt(row.querySelector('.hor-maxcitas').value) || 1,
            trabajeFestivos:  row.querySelector('.hor-festivos').checked,
            ...dias
        };
    }).filter(h => h.horaInicio && h.horaFin && h.fechaInicio);
}

async function guardarAgenda(idAgenda) {
    const body = {
        codigo:      document.getElementById('ag-codigo').value || null,
        descripcion: document.getElementById('ag-descripcion').value || null,
        idServicio:  parseInt(document.getElementById('ag-servicio').value) || null,
        fechaInicio: document.getElementById('ag-finicio').value || null,
        fechaFin:    document.getElementById('ag-ffin').value || null,
        disponible:  true
    };

    if (!body.descripcion) { toast('La descripción es obligatoria.', 'warn'); return; }

    let idAgendaFinal = idAgenda;

    if (idAgenda) {
        const res = await fetch(`${API}/agendas/${idAgenda}`, {
            method: 'PUT', headers: auditorHeaders(), body: JSON.stringify({ ...body, idAgenda })
        });
        if (!res.ok) { toast('Error al actualizar la agenda.', 'error'); return; }
    } else {
        const res = await fetch(`${API}/agendas`, {
            method: 'POST', headers: auditorHeaders(), body: JSON.stringify(body)
        });
        if (!res.ok) { toast('Error al crear la agenda.', 'error'); return; }
        const data = await res.json();
        idAgendaFinal = data.idAgenda;
    }

    const propietarios = leerPropietarios();
    for (const p of propietarios) {
        await fetch(`${API}/agendas/${idAgendaFinal}/propietarios`, {
            method: 'POST', headers: auditorHeaders(), body: JSON.stringify(p)
        });
    }

    const horarios = leerHorarios();
    for (const h of horarios) {
        await fetch(`${API}/agendas/${idAgendaFinal}/horarios`, {
            method: 'POST', headers: auditorHeaders(), body: JSON.stringify(h)
        });
    }

    toast('Agenda guardada correctamente.');
    renderAgendas(document.getElementById('contenido'));
}

async function eliminarAgenda(id) {
    if (!await confirmar('¿Eliminar esta agenda?')) return;
    const res = await fetch(`${API}/agendas/${id}`, { method: 'DELETE', headers: auditorHeaders() });
    if (res.ok || res.status === 204) { toast('Agenda eliminada.'); renderAgendas(document.getElementById('contenido')); }
    else toast('Error al eliminar.', 'error');
}

// ── BLOQUEOS ─────────────────────────────────────────────────

async function cargarBloqueosAgenda(idAgenda) {
    const agenda = await fetch(`${API}/agendas/${idAgenda}`).then(r => r.json()).catch(() => null);
    if (!agenda) return;
    const idProf = agenda.idProfesional;
    if (!idProf) { document.getElementById('lista-bloqueos').innerHTML = '<p style="font-size:13px;color:#aaa">Sin profesional asignado.</p>'; return; }
    const bloqueos = await fetch(`${API}/profesionales/${idProf}/bloqueos`).then(r => r.json()).catch(() => []);
    const lista = document.getElementById('lista-bloqueos');
    if (!lista) return;

    lista.innerHTML = bloqueos.length ? `
        <table class="table table-sm tabla-datos">
            <thead><tr><th>Desde</th><th>Hasta</th><th>Motivo</th><th>Observaciones</th><th></th></tr></thead>
            <tbody>
            ${bloqueos.map(b => `
                <tr>
                    <td>${new Date(b.fechaDesde).toLocaleDateString('es-AR')}</td>
                    <td>${new Date(b.fechaHasta).toLocaleDateString('es-AR')}</td>
                    <td>${b.motivo}</td>
                    <td>${b.observaciones ?? '-'}</td>
                    <td><button class="btn-tabla btn-tabla-eliminar" onclick="eliminarBloqueo(${b.idBloqueo}, ${idAgenda})">Quitar</button></td>
                </tr>`).join('')}
            </tbody>
        </table>` : '<p style="font-size:13px;color:#aaa">Sin bloqueos registrados.</p>';
}

async function abrirModalBloqueo(idAgenda) {
    const agenda = await fetch(`${API}/agendas/${idAgenda}`).then(r => r.json()).catch(() => null);
    const idProf = agenda?.idProfesional;
    if (!idProf) { toast('Primero guardá la agenda con un propietario.', 'warn'); return; }

    const optsMotivo = MOTIVOS_BLOQUEO.map(m => `<option value="${m}">${m}</option>`).join('');

    document.getElementById('modalDerivBody').innerHTML = `
        <div class="row g-2">
            <div class="col-6">
                <label class="form-label">Desde</label>
                <input type="date" id="blq-desde" class="form-control form-control-sm">
            </div>
            <div class="col-6">
                <label class="form-label">Hasta</label>
                <input type="date" id="blq-hasta" class="form-control form-control-sm">
            </div>
            <div class="col-12">
                <label class="form-label">Motivo</label>
                <select id="blq-motivo" class="form-select form-select-sm">
                    <option value="">-- Seleccioná --</option>${optsMotivo}
                </select>
            </div>
            <div class="col-12">
                <label class="form-label">Observaciones</label>
                <textarea id="blq-obs" class="form-control form-control-sm" rows="2"></textarea>
            </div>
        </div>`;

    document.getElementById('modalDerivPanel').querySelector('.modal-header-custom span').textContent = 'Nuevo bloqueo';
    document.getElementById('modalDerivOverlay').classList.add('visible');
    document.getElementById('modalDerivPanel').classList.add('visible');

    window._bloqueoAgendaId      = idAgenda;
    window._bloqueoIdProfesional = idProf;
    window._modoBloqueo  = true;
    window._modoDirecto  = false;
    window._modoAsignar  = false;
}

async function eliminarBloqueo(idBloqueo, idAgenda) {
    if (!await confirmar('¿Eliminar este bloqueo?')) return;
    await fetch(`${API}/bloqueos/${idBloqueo}`, { method: 'DELETE', headers: auditorHeaders() });
    toast('Bloqueo eliminado.');
    cargarBloqueosAgenda(idAgenda);
}

// ── TAB ROUTING ──────────────────────────────────────────────
async function mostrarTab(tab) {
    tabActual = tab;
    marcarActivo(tab);

    const contenido = document.getElementById("contenido");
    contenido.innerHTML = `<div class="text-center mt-5"><div class="spinner-border text-primary"></div></div>`;

    try {
        if (tab === "usuarios")          return await renderUsuarios(contenido);
        if (tab === "solicitudes")        return await renderSolicitudes(contenido);
        if (tab === "derivaciones")       return await renderDerivaciones(contenido);
        if (tab === "auditoria")          return await renderAuditoria(contenido);
        if (tab === "recuperacion")       return await renderRecuperacion(contenido);
        if (tab === "agendas")            return await renderAgendas(contenido);
        if (tab === "nuevo-profesional")  return await renderNuevoProfesional(contenido);

        await cargarLookups();
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
    const profesionales = window._profesionales.filter(p => mostrarBaja ? p.estado === "Inactivo" : p.estado === "Activo");

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

    const cols = ["nombre", "apellido", "dni", "mail", "matricula", "provincia", "fechaSolicitud"];
    const headers = cols.map(col => `<th>${etiqueta(col)}</th>`).join("") + "<th>Acciones</th>";
    const filas = datos.map(row => {
        const celdas = cols.map(c => {
            if (c === "fechaSolicitud") return `<td>${row[c] ? new Date(row[c]).toLocaleDateString("es-AR") : "-"}</td>`;
            return `<td>${row[c] ?? "-"}</td>`;
        }).join("");
        return `<tr>${celdas}<td>
            <button class="btn-ok" onclick="aprobarSolicitud(${row.idProfesional})">Aprobar</button>
            <button class="btn-ko" onclick="rechazarSolicitud(${row.idProfesional})">Rechazar</button>
        </td></tr>`;
    }).join("");

    c.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">${titulos["solicitudes"]} <span class="badge bg-secondary">${datos.length}</span></div>
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('solicitudes')">Actualizar</button>
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
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('derivaciones')">Actualizar</button>
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
    window._modoBloqueo = false;
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
    window._modoBloqueo = false;
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
    window._modoBloqueo = false;
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
    window._modoBloqueo = false;
}

async function confirmarAceptarDeriv() {

    if (window._modoBloqueo) {
        const desde  = document.getElementById('blq-desde').value;
        const hasta  = document.getElementById('blq-hasta').value;
        const motivo = document.getElementById('blq-motivo').value;
        const obs    = document.getElementById('blq-obs').value || null;

        if (!desde || !hasta || !motivo) { toast('Completá todos los campos obligatorios.', 'warn'); return; }

        await fetch(`${API}/profesionales/${window._bloqueoIdProfesional}/bloqueos`, {
            method: 'POST',
            headers: auditorHeaders(),
            body: JSON.stringify({ idAgenda: window._bloqueoAgendaId, fechaDesde: desde, fechaHasta: hasta, motivo, observaciones: obs })
        });
        cerrarModalDeriv();
        toast('Bloqueo registrado.');
        cargarBloqueosAgenda(window._bloqueoAgendaId);
        return;
    }

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
let _tabla = { tab: null, cols: [], datos: [], busqueda: "", orden: { col: null, dir: 1 } };

function renderTabla(titulo, cols, datos, tab, contenido) {
    _tabla = { tab, cols, datos: datos ?? [], busqueda: "", orden: { col: null, dir: 1 } };

    if (!datos || datos.length === 0) {
        contenido.innerHTML = `<div class="titulo-seccion">${titulo}</div>
            <div class="alert alert-info">No hay registros para mostrar.</div>`;
        return;
    }

    contenido.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">${titulo} <span class="badge bg-secondary">${datos.length}</span>
                ${SOLO_LECTURA.includes(tab) ? '<span class="badge bg-secondary ms-2" style="font-size:11px">Solo lectura</span>' : ""}
            </div>
            <div class="tabla-acciones">
                <input type="text" id="buscadorTabla" class="tabla-buscador" placeholder="Buscar..."
                    oninput="filtrarTabla(this.value)">
                <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('${tab}')">Actualizar</button>
            </div>
        </div>
        <div class="table-responsive">
            <table class="table table-hover tabla-datos">
                <thead><tr id="tablaHead"></tr></thead>
                <tbody id="tablaBody"></tbody>
            </table>
        </div>`;

    pintarTabla();
}

function pintarTabla() {
    const { tab, cols, orden } = _tabla;
    const editable = !SOLO_LECTURA.includes(tab);

    const head = document.getElementById("tablaHead");
    head.innerHTML = cols.map(c => {
        const flecha = orden.col === c ? (orden.dir === 1 ? " ▲" : " ▼") : "";
        return `<th class="th-ordenable" onclick="ordenarTabla('${c}')">${etiqueta(c)}${flecha}</th>`;
    }).join("") + (editable ? "<th>Acciones</th>" : "");

    let filas = _tabla.datos.slice();

    const q = _tabla.busqueda.trim().toLowerCase();
    if (q) {
        filas = filas.filter(row =>
            cols.some(c => String(valorCelda(c, row[c])).toLowerCase().includes(q)));
    }

    if (orden.col) {
        const c = orden.col;
        filas.sort((a, b) => {
            const va = valorCelda(c, a[c]), vb = valorCelda(c, b[c]);
            const na = parseFloat(va), nb = parseFloat(vb);
            const cmp = (!isNaN(na) && !isNaN(nb))
                ? na - nb
                : String(va).localeCompare(String(vb), "es", { sensitivity: "base" });
            return cmp * orden.dir;
        });
    }

    const body = document.getElementById("tablaBody");
    if (!filas.length) {
        body.innerHTML = `<tr><td colspan="${cols.length + (editable ? 1 : 0)}" class="tabla-sin-resultados">Sin resultados.</td></tr>`;
        return;
    }

    body.innerHTML = filas.map(row => {
        const id = row[idCampo[tab]];
        const celdas = cols.map(c => {
            const num = (PK.has(c) || ["hora", "horaInicio", "horaFin", "nroAfiliado"].includes(c)) ? " class=\"col-num\"" : "";
            return `<td${num}>${formatearCelda(c, row[c])}</td>`;
        }).join("");
        const acciones = editable ? `<td class="col-acciones">
            <button class="btn-tabla btn-tabla-editar" onclick='abrirModal(${JSON.stringify(row)})'>Editar</button>
            <button class="btn-tabla btn-tabla-eliminar" onclick="eliminar('${tab}', ${id})">Eliminar</button>
        </td>` : "";
        return `<tr>${celdas}${acciones}</tr>`;
    }).join("");
}

function filtrarTabla(valor) {
    _tabla.busqueda = valor;
    pintarTabla();
}

function ordenarTabla(col) {
    if (_tabla.orden.col === col) _tabla.orden.dir *= -1;
    else _tabla.orden = { col, dir: 1 };
    pintarTabla();
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
            <label>${etiqueta(c)}</label>
            ${campoModal(c, val)}
        </div>`;
    }).join("");

    document.getElementById("modalOverlay").classList.add("visible");
    document.getElementById("modalPanel").classList.add("visible");
}

function campoModal(c, val) {
    const opcionesSelect = mapa => Object.entries(mapa)
        .map(([id, txt]) => `<option value="${id}" ${String(id) === String(val) ? "selected" : ""}>${txt}</option>`).join("");

    if (c === "idPaciente")    return `<select id="field_${c}" class="input-modal">${opcionesSelect(LOOKUPS.pacientes)}</select>`;
    if (c === "idProfesional") return `<select id="field_${c}" class="input-modal">${opcionesSelect(LOOKUPS.profesionales)}</select>`;
    if (c === "idEstadoTurno") return `<select id="field_${c}" class="input-modal">${opcionesSelect(LOOKUPS.estados)}</select>`;
    if (c === "idObraSocial")  return `<select id="field_${c}" class="input-modal">${opcionesSelect(LOOKUPS.obras)}</select>`;
    if (c === "estado")        return `<select id="field_${c}" class="input-modal">
        <option ${val === "Activo" ? "selected" : ""}>Activo</option>
        <option ${val === "Inactivo" ? "selected" : ""}>Inactivo</option></select>`;
    if (c === "disponible")    return `<select id="field_${c}" class="input-modal">
        <option value="true"  ${(val === true || val === "true") ? "selected" : ""}>Sí</option>
        <option value="false" ${(val === false || val === "false") ? "selected" : ""}>No</option></select>`;
    if (c === "fecha")  return `<input type="date" id="field_${c}" value="${String(val).slice(0, 10)}" class="input-modal">`;
    if (c === "hora" || c === "horaInicio" || c === "horaFin")
        return `<input type="time" id="field_${c}" value="${String(val).slice(0, 5)}" class="input-modal">`;
    return `<input type="text" id="field_${c}" value="${val}" class="input-modal">`;
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
        <button class="btn-reintentar" onclick="mostrarTab('${tab}')">Reintentar</button>
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
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('auditoria')">Actualizar</button>
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
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('recuperacion')">Actualizar</button>
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
// ── NUEVO PROFESIONAL ─────────────────────────────────────
async function renderNuevoProfesional(c) {
    const [especialidades, tiposProfesional] = await Promise.all([
        fetch(`${API}/especialidades`).then(r => r.json()).catch(() => []),
        fetch(`${API}/tiposprofesional`).then(r => r.json()).catch(() => [])
    ]);

    c.innerHTML = `
        <div class="titulo-seccion">Nuevo Profesional</div>
        <div class="card" style="max-width:700px">
            <div class="row g-3">
                <div class="col-md-6">
                    <label class="form-label">Nombre *</label>
                    <input type="text" id="np-nombre" class="form-control form-control-sm" placeholder="Nombre">
                </div>
                <div class="col-md-6">
                    <label class="form-label">Apellido</label>
                    <input type="text" id="np-apellido" class="form-control form-control-sm" placeholder="Apellido">
                </div>
                <div class="col-md-6">
                    <label class="form-label">Matrícula *</label>
                    <input type="text" id="np-matricula" class="form-control form-control-sm" placeholder="MP12345">
                </div>
                <div class="col-md-6">
                    <label class="form-label">Especialidad *</label>
                    <select id="np-especialidad" class="form-select form-select-sm">
                        <option value="">-- Seleccioná --</option>
                        ${especialidades.map(e => `<option value="${e.idEspecialidad}">${e.nombre}</option>`).join('')}
                    </select>
                </div>
                <div class="col-md-6">
                    <label class="form-label">Email *</label>
                    <input type="email" id="np-mail" class="form-control form-control-sm" placeholder="correo@ejemplo.com">
                </div>
                <div class="col-md-6">
                    <label class="form-label">Teléfono</label>
                    <input type="text" id="np-telefono" class="form-control form-control-sm" placeholder="3511234567">
                </div>
                <div class="col-md-6">
                    <label class="form-label">Provincia</label>
                    <input type="text" id="np-provincia" class="form-control form-control-sm" placeholder="Córdoba">
                </div>
                <div class="col-12">
                    <label class="form-label">Tipos de profesional</label>
                    <div style="display:flex;flex-wrap:wrap;gap:10px;margin-top:4px">
                        ${tiposProfesional.map(t => `
                            <label style="display:flex;align-items:center;gap:6px;font-size:13px;cursor:pointer">
                                <input type="checkbox" class="np-tipo" value="${t.idTipoProfesional}">
                                ${t.nombre}
                            </label>`).join('')}
                    </div>
                </div>
                <div class="col-12" style="margin-top:8px">
                    <button class="btn btn-guardar" onclick="guardarNuevoProfesional()">Crear profesional y enviar email de activación</button>
                </div>
            </div>
        </div>`;
}

async function guardarNuevoProfesional() {
    const nombre      = document.getElementById('np-nombre').value.trim();
    const apellido    = document.getElementById('np-apellido').value.trim();
    const matricula   = document.getElementById('np-matricula').value.trim();
    const idEsp       = parseInt(document.getElementById('np-especialidad').value);
    const mail        = document.getElementById('np-mail').value.trim();
    const telefono    = document.getElementById('np-telefono').value.trim();
    const provincia   = document.getElementById('np-provincia').value.trim();
    const tipos       = [...document.querySelectorAll('.np-tipo:checked')].map(cb => parseInt(cb.value));

    if (!nombre)    { toast('El nombre es obligatorio.', 'warn'); return; }
    if (!matricula) { toast('La matrícula es obligatoria.', 'warn'); return; }
    if (!idEsp)     { toast('Seleccioná una especialidad.', 'warn'); return; }
    if (!mail)      { toast('El email es obligatorio.', 'warn'); return; }

    // 1. Crear el profesional
    const resProf = await fetch(`${API}/profesionales`, {
        method: 'POST',
        headers: auditorHeaders(),
        body: JSON.stringify({ nombre, apellido, matricula, idEspecialidad: idEsp, mail, telefono, provincia, estado: 'Pendiente' })
    });

    if (resProf.status === 409) { toast('Ya existe un profesional con esa matrícula.', 'error'); return; }
    if (!resProf.ok) { toast('Error al crear el profesional.', 'error'); return; }

    const prof = await resProf.json();

    // 2. Asignar tipos de profesional
    for (const idTipo of tipos) {
        await fetch(`${API}/profesionales/${prof.idProfesional}/tipos`, {
            method: 'POST',
            headers: auditorHeaders(),
            body: JSON.stringify({ idTipoProfesional: idTipo })
        });
    }

    // 3. Aprobar y enviar email de activación
    const resAprob = await fetch(`${API}/profesionales/${prof.idProfesional}/aprobar`, {
        method: 'PUT',
        headers: auditorHeaders()
    });

    if (resAprob.ok) {
        toast(`Profesional creado. Se envió el email de activación a ${mail}.`);
        mostrarTab('usuarios');
    } else {
        toast('Profesional creado pero hubo un error al enviar el email.', 'warn');
        mostrarTab('usuarios');
    }
}
