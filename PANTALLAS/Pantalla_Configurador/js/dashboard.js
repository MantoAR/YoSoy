const API = "http://localhost:5000";

const SOLO_LECTURA = ["evoluciones", "eventoscrisis"];

const campos = {
    usuarios:        ["username", "mail", "estado"],
    pacientes:       ["nombre", "dni", "fechaNacimiento", "domicilio", "telefono", "mail", "estado"],
    profesionales:   ["nombre", "matricula", "idEspecialidad", "estado", "mail", "telefono"],
    tutores:         ["nombre", "dni", "vinculo", "contacto"],
    turnos:          ["idPaciente", "idProfesional", "idEstadoTurno", "fecha", "hora", "modalidad", "motivo"],
    agendas:         ["idProfesional", "dia", "horaInicio", "horaFin", "disponible"],
    derivaciones:    ["idPaciente", "idProfesionalOrigen", "idProfesionalDestino", "fecha", "motivo", "estado"],
    roles:           ["nombre", "estado"],
    especialidades:  ["nombre", "estado"],
    coberturas:      ["idPaciente", "idObraSocial", "nroAfiliado", "planCobertura", "estado"],
    interaccionesia: ["idPaciente", "fecha", "consulta", "respuesta"],
};

const idCampo = {
    usuarios:        "idUsuario",
    pacientes:       "idPaciente",
    profesionales:   "idProfesional",
    tutores:         "idTutor",
    turnos:          "idTurno",
    agendas:         "idAgenda",
    derivaciones:    "idDerivacion",
    roles:           "idRol",
    especialidades:  "idEspecialidad",
    coberturas:      "idCobertura",
    interaccionesia: "idInteraccion",
};

const columnas = {
    usuarios:        ["idUsuario", "username", "mail", "estado"],
    pacientes:       ["idPaciente", "nombre", "dni", "telefono", "estado", "HC"],
    profesionales:   ["idProfesional", "nombre", "matricula", "estado"],
    tutores:         ["idTutor", "nombre", "dni", "vinculo", "contacto"],
    turnos:          ["idTurno", "idPaciente", "idProfesional", "fecha", "hora", "modalidad"],
    agendas:         ["idAgenda", "idProfesional", "dia", "horaInicio", "horaFin", "disponible"],
    derivaciones:    ["idDerivacion", "idPaciente", "idProfesionalOrigen", "idProfesionalDestino", "fecha", "estado"],
    evoluciones:     ["idEvolucion", "idHistoria", "idProfesional", "fecha", "diagnostico"],
    eventoscrisis:   ["idEvento", "idPaciente", "fecha", "nivelRiesgo", "acciones"],
    roles:           ["idRol", "nombre", "estado"],
    especialidades:  ["idEspecialidad", "nombre", "estado"],
    coberturas:      ["idCobertura", "idPaciente", "nroAfiliado", "planCobertura", "estado"],
    interaccionesia: ["idInteraccion", "idPaciente", "fecha", "consulta"],
};

const titulos = {
    usuarios:        "Usuarios",
    pacientes:       "Pacientes",
    profesionales:   "Profesionales",
    tutores:         "Tutores",
    turnos:          "Turnos",
    agendas:         "Agenda",
    derivaciones:    "Derivaciones",
    evoluciones:     "Evoluciones",
    eventoscrisis:   "Eventos de Crisis",
    roles:           "Roles",
    especialidades:  "Especialidades",
    coberturas:      "Coberturas",
    interaccionesia: "Interacciones IA",
};

let tabActual = null;
let datosActuales = [];

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

async function mostrarTab(tab) {
    tabActual = tab;
    marcarActivo(tab);

    const contenido = document.getElementById("contenido");
    contenido.innerHTML = `<div class="text-center mt-5"><div class="spinner-border text-primary"></div></div>`;

    try {
        let datos = await fetch(`${API}/${tab}`).then(r => r.json());

        if (tab === "pacientes") {
            datos = await Promise.all(datos.map(async p => {
                const res = await fetch(`${API}/pacientes/${p.idPaciente}/historiaclinica`);
                p.HC = res.ok ? "✅ Sí" : "❌ No";
                return p;
            }));
        }

        datosActuales = datos;
        renderTabla(titulos[tab], columnas[tab], datos, tab);

    } catch (e) {
        contenido.innerHTML = `
            <div class="error-api">
                <div class="error-icono">⚠️</div>
                <div class="error-titulo">No se puede conectar con la API</div>
                <div class="error-desc">El servidor no está disponible en <strong>${API}</strong>.<br>Iniciá la API y volvé a intentarlo.</div>
                <button class="btn-reintentar" onclick="mostrarTab('${tabActual}')">↺ Reintentar</button>
            </div>`;
    }
}

function renderTabla(titulo, cols, datos, tab) {
    const contenido = document.getElementById("contenido");
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
        const acciones = editable ? `
            <td>
                <button class="btn-accion btn-editar" onclick='abrirModal(${JSON.stringify(row)})'>✏️</button>
                <button class="btn-accion btn-eliminar" onclick="eliminar('${tab}', ${id})">🗑️</button>
            </td>` : "";
        return `<tr>${celdas}${acciones}</tr>`;
    }).join("");

    const badgeSL = SOLO_LECTURA.includes(tab)
        ? `<span class="badge bg-secondary ms-2" style="font-size:11px">Solo lectura</span>` : "";

    contenido.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div class="titulo-seccion mb-0">${titulo} <span class="badge bg-secondary" style="font-size:13px">${datos.length}</span>${badgeSL}</div>
            <button class="btn btn-sm btn-outline-secondary" onclick="mostrarTab('${tab}')">↺ Actualizar</button>
        </div>
        <div class="table-responsive">
            <table class="table table-hover tabla-datos">
                <thead><tr>${headers}</tr></thead>
                <tbody>${filas}</tbody>
            </table>
        </div>

        <div class="modal-overlay" id="modalOverlay" onclick="cerrarModal()"></div>
        <div class="modal-panel" id="modalPanel">
            <div class="modal-header-custom">
                <span id="modalTitulo">Editar</span>
                <button class="btn-cerrar-modal" onclick="cerrarModal()">✕</button>
            </div>
            <div class="modal-body-custom" id="modalBody"></div>
            <div class="modal-footer-custom">
                <button class="btn btn-secondary" onclick="cerrarModal()">Cancelar</button>
                <button class="btn btn-guardar" onclick="guardar()">Guardar cambios</button>
            </div>
        </div>`;
}

let filaEnEdicion = null;

function abrirModal(row) {
    filaEnEdicion = row;
    const tab = tabActual;
    const camposTab = campos[tab];

    document.getElementById("modalTitulo").textContent = `Editar — ${titulos[tab]}`;

    const body = document.getElementById("modalBody");
    body.innerHTML = camposTab.map(c => {
        const val = row[c] ?? "";
        return `
            <div class="campo-modal">
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
    const camposTab = campos[tab];
    const id = filaEnEdicion[idCampo[tab]];

    const body = { ...filaEnEdicion };
    camposTab.forEach(c => {
        const val = document.getElementById(`field_${c}`)?.value;
        if (val !== undefined) body[c] = val;
    });

    try {
        const res = await fetch(`${API}/${tab}/${id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
        });

        if (res.ok) {
            cerrarModal();
            mostrarTab(tab);
        } else {
            alert("Error al guardar. Verificá los datos.");
        }
    } catch {
        alert("No se pudo conectar con la API.");
    }
}

async function eliminar(tab, id) {
    if (!confirm("¿Eliminar este registro?")) return;

    try {
        const res = await fetch(`${API}/${tab}/${id}`, { method: "DELETE" });
        if (res.ok || res.status === 204) {
            mostrarTab(tab);
        } else {
            alert("No se pudo eliminar.");
        }
    } catch {
        alert("No se pudo conectar con la API.");
    }
}

function cerrarSesion() {
    if (confirm("¿Cerrar sesión?")) window.location.href = "index.html";
}
