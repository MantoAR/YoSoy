const API = "http://localhost:5000";

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
                        <button class="confirm-btn-no"  id="confirmNo">Cancelar</button>
                        <button class="confirm-btn-si"  id="confirmSi">Aceptar</button>
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

const idPaciente = localStorage.getItem("idPaciente");
if (!idPaciente) window.location.href = "../index.html";

let calAnio  = new Date().getFullYear();
let calMes   = new Date().getMonth();
let turnosCalendario = [];
let turnoSeleccionado = null;

let _modoReprogramar = false;
let _idTurnoReprog   = null;
let _reprogAgenda    = [];
let _reprogFechaSeleccionada = null;

const MESES   = ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];
const DIAS    = ["Lun","Mar","Mié","Jue","Vie","Sáb","Dom"];
const DIAS_ES = { "Lunes":1,"Martes":2,"Miércoles":3,"Jueves":4,"Viernes":5,"Sábado":6,"Domingo":0 };
const DIA_DB_NUM = { "lunes":1,"martes":2,"miercoles":3,"jueves":4,"viernes":5,"sabado":6,"domingo":0 };

// ── NAVEGACIÓN ──────────────────────────────────────────────
function mostrarSeccion(id) {
    document.querySelectorAll(".tab-btn").forEach(btn =>
        btn.classList.toggle("activo", btn.dataset.sec === id));

    const c = document.getElementById("contenido");
    c.innerHTML = `<div class="spinner">Cargando...</div>`;

    if      (id === "inicio")        renderInicio(c);
    else if (id === "agenda")        renderAgenda(c);
    else if (id === "profesionales") renderProfesionales(c);
    else if (id === "perfil")        renderPerfil(c);
}

async function cerrarSesion() {
    if (await confirmar("¿Cerrar sesión?")) {
        localStorage.clear();
        window.location.href = "../index.html";
    }
}

// ── INICIO ─────────────────────────────────────────────────
async function renderInicio(c) {
    try {
        const res = await fetch(`${API}/pacientes/${idPaciente}/proximo-turno`);

        if (res.status === 404) {
            c.innerHTML = `
                <div class="titulo-seccion">Inicio</div>
                <div class="card">
                    <p class="sin-turnos">No tenés turnos próximos.</p>
                </div>`;
            return;
        }

        const t = await res.json();
        const fecha = new Date(t.fecha).toLocaleDateString("es-AR", { weekday:"long", day:"numeric", month:"long", year:"numeric" });
        const hora  = t.hora.substring(0,5);

        c.innerHTML = `
            <div class="titulo-seccion">Inicio</div>
            <div class="card" style="background:linear-gradient(135deg,#0d6efd,#6A5ACD);border-radius:20px;padding:32px;color:white;max-width:680px">
                <p style="font-size:11px;font-weight:bold;text-transform:uppercase;letter-spacing:1px;opacity:.75;margin-bottom:20px">Próximo turno</p>
                <div style="font-size:26px;font-weight:bold;margin-bottom:6px">${fecha}</div>
                <div style="font-size:40px;font-weight:bold;margin-bottom:24px;opacity:.95">${hora} hs</div>
                <div style="display:flex;gap:32px;flex-wrap:wrap;border-top:1px solid rgba(255,255,255,.25);padding-top:20px">
                    <div>
                        <div style="font-size:11px;text-transform:uppercase;letter-spacing:.5px;opacity:.7;margin-bottom:4px">Profesional</div>
                        <div style="font-size:16px;font-weight:600">${t.nombreProfesional}</div>
                    </div>
                    <div>
                        <div style="font-size:11px;text-transform:uppercase;letter-spacing:.5px;opacity:.7;margin-bottom:4px">Especialidad</div>
                        <div style="font-size:16px;font-weight:600">${t.especialidad}</div>
                    </div>
                    <div>
                        <div style="font-size:11px;text-transform:uppercase;letter-spacing:.5px;opacity:.7;margin-bottom:4px">Modalidad</div>
                        <div style="font-size:16px;font-weight:600">${t.modalidad ?? "-"}</div>
                    </div>
                    <div>
                        <div style="font-size:11px;text-transform:uppercase;letter-spacing:.5px;opacity:.7;margin-bottom:4px">Estado</div>
                        <div style="font-size:16px;font-weight:600">${t.estadoDescripcion}</div>
                    </div>
                </div>
            </div>`;
    } catch {
        c.innerHTML = `<div class="titulo-seccion">Inicio</div><p style="color:#e74c3c">Error al cargar los datos.</p>`;
    }
}

// ── AGENDA / CALENDARIO ────────────────────────────────────
async function renderAgenda(c) {
    try {
        const res = await fetch(`${API}/pacientes/${idPaciente}/turnos-detalle`);
        turnosCalendario = await res.json();
    } catch {
        turnosCalendario = [];
    }

    c.innerHTML = `
        <div class="titulo-seccion">Mi Agenda</div>
        <div class="card">
            <div class="cal-nav">
                <button onclick="cambiarMes(-1)">&#8249;</button>
                <span class="cal-mes" id="calTitulo"></span>
                <button onclick="cambiarMes(1)">&#8250;</button>
            </div>
            <div class="cal-grid" id="calGrid"></div>
        </div>
        <div class="panel-turno" id="panelTurno"></div>

`;

    dibujarCalendario();
}


function cambiarMes(delta) {
    calMes += delta;
    if (calMes > 11) { calMes = 0;  calAnio++; }
    if (calMes < 0)  { calMes = 11; calAnio--; }
    dibujarCalendario();
    if (!_modoReprogramar)
        document.getElementById("panelTurno").classList.remove("visible");
}

function dibujarCalendario() {
    document.getElementById("calTitulo").textContent = `${MESES[calMes]} ${calAnio}`;

    const grid = document.getElementById("calGrid");
    const hoy  = new Date();
    const primerDia  = new Date(calAnio, calMes, 1);
    const diasEnMes  = new Date(calAnio, calMes + 1, 0).getDate();
    const inicioCelda = (primerDia.getDay() + 6) % 7; // Lunes = 0

    const turnosPorDia = {};
    turnosCalendario.forEach(t => {
        const f = new Date(t.fecha);
        if (f.getFullYear() === calAnio && f.getMonth() === calMes) {
            const d = f.getDate();
            if (!turnosPorDia[d]) turnosPorDia[d] = [];
            turnosPorDia[d].push(t);
        }
    });

    let html = DIAS.map(d => `<div class="cal-header">${d}</div>`).join("");

    for (let i = 0; i < inicioCelda; i++)
        html += `<div class="cal-dia vacio"></div>`;

    for (let dia = 1; dia <= diasEnMes; dia++) {
        const tieneT    = !!turnosPorDia[dia];
        const pendiente = tieneT && turnosPorDia[dia].some(t => t.estadoDescripcion === "Pendiente");
        const esHoy     = dia === hoy.getDate() && calMes === hoy.getMonth() && calAnio === hoy.getFullYear();
        const esPasado  = new Date(calAnio, calMes, dia) < new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate());
        const fechaD    = new Date(calAnio, calMes, dia);
        const esSeleccionado = _reprogFechaSeleccionada &&
            fechaD.toDateString() === _reprogFechaSeleccionada.toDateString();

        let clases, onclick;
        if (_modoReprogramar) {
            const enAgenda  = _reprogAgenda.some(a => DIA_DB_NUM[normalizar(a.dia)] === fechaD.getDay());
            const ocupado   = tieneT && turnosPorDia[dia].some(t => t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado");
            const valido    = !esPasado && enAgenda && !ocupado;
            clases  = ["cal-dia", esPasado ? "pasado" : valido ? "reprog-valido" : "reprog-invalido", esSeleccionado ? "reprog-seleccionado" : ""].join(" ").trim();
            onclick = valido ? `onclick="seleccionarDiaReprog(${dia})"` : "";
        } else {
            const soloCancelado = tieneT && turnosPorDia[dia].every(t => t.estadoDescripcion === "Cancelado");
            const claseT = tieneT
                ? (soloCancelado ? "con-turno-cancelado" : pendiente ? "con-turno-pendiente" : "con-turno")
                : (esPasado ? "pasado" : "futuro");
            clases  = ["cal-dia", claseT, esHoy ? "hoy" : ""].join(" ").trim();
            onclick = !esPasado ? `onclick="mostrarPanelDia(${dia})"` : "";
        }
        html += `<div class="${clases}" ${onclick}>${dia}</div>`;
    }

    grid.innerHTML = html;
}

let _diaSeleccionado = null;

async function mostrarPanelDia(dia) {
    _diaSeleccionado = dia;
    document.querySelectorAll(".cal-dia").forEach(el => el.classList.remove("seleccionado"));
    document.querySelectorAll(".cal-dia").forEach(el => {
        if (el.textContent.trim() === String(dia)) el.classList.add("seleccionado");
    });
    const turnosDia = turnosCalendario.filter(t => {
        const f = new Date(t.fecha);
        return f.getFullYear() === calAnio && f.getMonth() === calMes && f.getDate() === dia;
    });

    const fechaStr = `${calAnio}-${String(calMes + 1).padStart(2,"0")}-${String(dia).padStart(2,"0")}`;
    const panel = document.getElementById("panelTurno");
    panel.classList.add("visible");

    let html = "";

    if (turnosDia.length) {
        turnoSeleccionado = turnosDia[0];
        const t = turnoSeleccionado;
        const hora = t.hora.substring(0, 5);
        const estadoColor = {
            "Confirmado": "#d1e7dd|#0f5132",
            "Pendiente":  "#fff3cd|#856404",
            "Cancelado":  "#f8d7da|#842029",
            "Realizado":  "#e2e3e5|#41464b",
        }[t.estadoDescripcion] ?? "#e2e3e5|#41464b";
        const [estadoBg, estadoTxt] = estadoColor.split("|");
        const puedeAccionar = t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado";

        html += `
            <div class="panel-top${t.estadoDescripcion === 'Cancelado' ? ' panel-cancelado' : ''}">
                <div class="panel-hora-bloque">
                    <div class="panel-hora">${hora}</div>
                    <div class="panel-hora-label">hs</div>
                </div>
                <div class="panel-info-principal">
                    <div class="panel-profesional">${t.nombreProfesional}</div>
                    <div class="panel-especialidad">${t.especialidad}</div>
                </div>
                <span class="panel-estado-badge" style="background:${estadoBg};color:${estadoTxt}">${t.estadoDescripcion}</span>
            </div>
            <div class="panel-chips">
                <div class="chip"><span class="chip-label">Modalidad</span><span class="chip-val">${t.modalidad ?? "-"}</span></div>
                ${t.motivo ? `<div class="chip chip-wide"><span class="chip-label">Motivo</span><span class="chip-val">${t.motivo}</span></div>` : ""}
            </div>
            ${puedeAccionar ? `
            <div class="panel-acciones">
                <button class="btn-accion btn-cancelar" onclick="cancelarTurno(${t.idTurno})">Cancelar turno</button>
                <button class="btn-accion btn-reprogramar" id="btnReprogramar" onclick="abrirFormReprogramar(${t.idTurno}, ${t.idProfesional})">Reprogramar</button>
            </div>
            <div class="form-reprogramar" id="formReprogramar">
                <p class="reprog-instruccion">Seleccioná el nuevo día en el calendario</p>
                <div id="reprogDetalles" style="display:none">
                    <div class="reprog-dia-seleccionado" id="reprogDiaLabel"></div>
                    <div class="form-fila" style="margin-top:12px">
                        <div class="form-grupo" style="flex:1">
                            <label>Hora</label>
                            <select id="reprogHora" class="turno-input"></select>
                        </div>
                        <div class="form-grupo" style="flex:1">
                            <label>Motivo</label>
                            <input type="text" id="reprogMotivo" class="turno-input" placeholder="Motivo del cambio...">
                        </div>
                    </div>
                    <button class="btn-accion btn-guardar" style="margin-top:12px" onclick="confirmarReprogramar(${t.idTurno})">Confirmar reprogramación</button>
                </div>
                <button class="btn-accion btn-cancelar" style="margin-top:10px" onclick="cancelarModoReprogramar()">Cancelar reprogramación</button>
            </div>` : ""}
            <div class="panel-separador"></div>`;
    }

    const tieneActivo = turnosDia.some(t => t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado");

    html += !tieneActivo ? `<div class="panel-solicitar-bloque">
        <button class="btn-solicitar-dia" onclick="abrirFormSolicitud('${fechaStr}')">+ Solicitar turno para este día</button>
    </div>
    <div class="form-solicitar-dia" id="formSolicitar">
        <div class="form-fila">
            <div class="form-grupo" style="flex:1">
                <label>Profesional</label>
                <select id="solProf" class="turno-input" onchange="cargarHorasSolicitud('${fechaStr}')">
                    <option value="">Cargando...</option>
                </select>
            </div>
            <div class="form-grupo">
                <label>Hora</label>
                <select id="solHora" class="turno-input" disabled><option value="">Elegí un profesional</option></select>
            </div>
        </div>
        <div class="form-grupo" style="margin-top:8px">
            <label>Motivo (opcional)</label>
            <input type="text" id="solMotivo" class="turno-input" placeholder="Escribí el motivo...">
        </div>
        <button class="btn-accion btn-guardar" style="margin-top:12px" onclick="confirmarSolicitudDia('${fechaStr}')">Confirmar solicitud</button>
    </div>` : "";

    panel.innerHTML = html;
    if (!tieneActivo) cargarProfesionalesSolicitud(fechaStr);
}

async function abrirFormSolicitud(fechaStr) {
    document.getElementById("formSolicitar").classList.toggle("visible");
}

async function cargarProfesionalesSolicitud(fechaStr) {
    // Si ya hay turno activo ese día, no permitir solicitar otro
    const fechaDate = new Date(fechaStr + "T12:00:00").toDateString();
    const yaOcupado = turnosCalendario.some(t =>
        new Date(t.fecha).toDateString() === fechaDate &&
        t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado"
    );
    const sel = document.getElementById("solProf");
    if (!sel) return;
    if (yaOcupado) {
        sel.innerHTML = `<option value="">Ya tenés un turno ese día</option>`;
        sel.disabled = true;
        document.querySelector(".btn-accion.btn-guardar[onclick*='confirmarSolicitudDia']")?.setAttribute("disabled", true);
        return;
    }
    const profs = await fetch(`${API}/pacientes/${idPaciente}/mis-profesionales`).then(r => r.json()).catch(() => []);
    sel.innerHTML = profs.length
        ? `<option value="">Seleccioná un profesional</option>` + profs.map(p => `<option value="${p.idProfesional}">${p.nombre} — ${p.especialidad}</option>`).join("")
        : `<option value="">Sin profesionales asignados</option>`;
}

function normalizar(s) {
    return (s ?? "").toLowerCase().normalize("NFD").replace(/\p{Diacritic}/gu, "");
}

async function cargarHorasSolicitud(fechaStr) {
    const idProf = parseInt(document.getElementById("solProf").value);
    const sel    = document.getElementById("solHora");
    if (!idProf) { sel.innerHTML = `<option value="">Elegí un profesional</option>`; sel.disabled = true; return; }

    const fecha     = new Date(fechaStr + "T12:00:00");
    const diaSem = fecha.getDay();
    const agenda = await fetch(`${API}/profesionales/${idProf}/agenda`).then(r => r.json()).catch(() => []);
    const slot   = agenda.find(a => a.disponible && DIA_DB_NUM[normalizar(a.dia)] === diaSem);

    if (!slot) { sel.innerHTML = `<option value="">Sin disponibilidad ese día</option>`; sel.disabled = true; return; }

    const [hI, mI] = slot.horaInicio.substring(0,5).split(":").map(Number);
    const [hF, mF] = slot.horaFin.substring(0,5).split(":").map(Number);
    const opciones = [];
    let cur = hI * 60 + mI;
    while (cur + 60 <= hF * 60 + mF) {
        const hh = String(Math.floor(cur/60)).padStart(2,"0");
        const mm = String(cur%60).padStart(2,"0");
        opciones.push(`<option value="${hh}:${mm}">${hh}:${mm}</option>`);
        cur += 60;
    }
    sel.innerHTML = opciones.length ? opciones.join("") : `<option value="">Sin horarios</option>`;
    sel.disabled  = !opciones.length;
}

async function confirmarSolicitudDia(fechaStr) {
    const idProf    = parseInt(document.getElementById("solProf").value);
    const hora      = document.getElementById("solHora").value;
    const modalidad = "Virtual";
    const motivo    = document.getElementById("solMotivo").value || null;

    if (!idProf) { toast("Seleccioná un profesional.", "warn"); return; }
    if (!hora)   { toast("Seleccioná un horario.", "warn"); return; }

    const res = await fetch(`${API}/turnos/solicitar`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            idPaciente:    parseInt(idPaciente),
            idProfesional: idProf,
            fecha:         new Date(fechaStr + "T12:00:00").toISOString(),
            hora:          hora + ":00",
            modalidad,
            motivo
        })
    });

    if (res.ok) {
        toast("Turno solicitado. El profesional lo confirmará.");
        const r = await fetch(`${API}/pacientes/${idPaciente}/turnos-detalle`);
        turnosCalendario = await r.json();
        dibujarCalendario();
        document.getElementById("panelTurno").classList.remove("visible");
    } else toast("Error al solicitar el turno.", "error");
}

function toggleReprogramar() {
    document.getElementById("formReprogramar")?.classList.toggle("visible");
}

async function cancelarTurno(id) {
    if (!await confirmar("¿Cancelar este turno?")) return;
    await fetch(`${API}/turnos/${id}/cancelar`, { method: "PUT" });
    const res = await fetch(`${API}/pacientes/${idPaciente}/turnos-detalle`);
    turnosCalendario = await res.json();
    dibujarCalendario();
    document.getElementById("panelTurno").classList.remove("visible");
}

// ── REPROGRAMAR ────────────────────────────────────────────
async function abrirFormReprogramar(idTurno, idProf) {
    const form = document.getElementById("formReprogramar");
    const abriendo = !form.classList.contains("visible");
    form.classList.toggle("visible");
    if (!abriendo) { cancelarModoReprogramar(); return; }

    _idTurnoReprog = idTurno;
    _reprogFechaSeleccionada = null;

    try {
        const agenda = await fetch(`${API}/profesionales/${idProf}/agenda`).then(r => r.json());
        _reprogAgenda = agenda.filter(a => a.disponible);
    } catch { _reprogAgenda = []; }

    _modoReprogramar = true;
    dibujarCalendario();
}

function seleccionarDiaReprog(dia) {
    _reprogFechaSeleccionada = new Date(calAnio, calMes, dia);
    dibujarCalendario();

    const slot = _reprogAgenda.find(a => DIA_DB_NUM[normalizar(a.dia)] === _reprogFechaSeleccionada.getDay());

    const label = _reprogFechaSeleccionada.toLocaleDateString("es-AR", { weekday:"long", day:"numeric", month:"long" });
    document.getElementById("reprogDiaLabel").textContent = label;

    const sel = document.getElementById("reprogHora");
    if (slot) {
        const [hI, mI] = slot.horaInicio.substring(0,5).split(":").map(Number);
        const [hF, mF] = slot.horaFin.substring(0,5).split(":").map(Number);
        const opciones = [];
        let cur = hI * 60 + mI;
        while (cur + 60 <= hF * 60 + mF) {
            const hh = String(Math.floor(cur/60)).padStart(2,"0");
            const mm = String(cur%60).padStart(2,"0");
            opciones.push(`<option value="${hh}:${mm}">${hh}:${mm}</option>`);
            cur += 60;
        }
        sel.innerHTML = opciones.join("") || `<option value="">Sin horarios</option>`;
    } else {
        sel.innerHTML = `<option value="">Sin horarios ese día</option>`;
    }

    document.getElementById("reprogDetalles").style.display = "block";
}

function cancelarModoReprogramar() {
    _modoReprogramar = false;
    _idTurnoReprog   = null;
    _reprogFechaSeleccionada = null;
    _reprogAgenda    = [];
    dibujarCalendario();
    document.getElementById("formReprogramar")?.classList.remove("visible");
}

async function confirmarReprogramar(idTurno) {
    const hora   = document.getElementById("reprogHora").value;
    const motivo = document.getElementById("reprogMotivo").value || null;
    if (!_reprogFechaSeleccionada) { toast("Seleccioná un día en el calendario.", "warn"); return; }
    if (!hora) { toast("Seleccioná un horario.", "warn"); return; }

    const fechaNueva = _reprogFechaSeleccionada;

    await fetch(`${API}/turnos/${idTurno}/cancelar`, { method: "PUT" });

    await fetch(`${API}/turnos/solicitar`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            idPaciente:    parseInt(idPaciente),
            idProfesional: turnoSeleccionado.idProfesional,
            fecha:         new Date(fechaNueva.getFullYear(), fechaNueva.getMonth(), fechaNueva.getDate(), 12).toISOString(),
            hora:          hora + ":00",
            modalidad:     "Virtual",
            motivo
        })
    });

    _modoReprogramar = false;
    _reprogFechaSeleccionada = null;
    _reprogAgenda = [];

    const res = await fetch(`${API}/pacientes/${idPaciente}/turnos-detalle`);
    turnosCalendario = await res.json();
    dibujarCalendario();
    document.getElementById("panelTurno").classList.remove("visible");
    toast("Turno reprogramado. Quedará pendiente de confirmación.");
}

// ── MIS PROFESIONALES ──────────────────────────────────────
async function renderProfesionales(c) {
    try {
        const lista = await fetch(`${API}/pacientes/${idPaciente}/mis-profesionales`).then(r => r.json());

        if (!lista.length) {
            c.innerHTML = `<div class="titulo-seccion">Mis Profesionales</div><div class="card"><p class="sin-turnos">No tenés profesionales asignados aún.</p></div>`;
            return;
        }

        const cards = lista.map(p => `
            <div class="prof-card">
                <div class="prof-nombre">${p.nombre}</div>
                <div class="prof-especialidad">${p.especialidad}</div>
                ${p.mail     ? `<div class="prof-dato">${p.mail}</div>`     : ""}
                ${p.telefono ? `<div class="prof-dato">${p.telefono}</div>` : ""}
                <button class="btn-solicitar-cambio" onclick="abrirModalCambio(${p.idProfesional}, ${p.idEspecialidad}, '${p.nombre.replace(/'/g, "\\'")}')">Solicitar cambio de profesional</button>
            </div>`).join("");

        c.innerHTML = `<div class="titulo-seccion">Mis Profesionales</div><div class="prof-lista">${cards}</div>

            <!-- Modal solicitar turno -->
            <div class="modal-overlay-cambio" id="modalTurnoOverlay" onclick="cerrarModalTurno()"></div>
            <div class="modal-cambio" id="modalTurno">
                <div class="modal-cambio-header">
                    <span id="modalTurnoTitulo">Solicitar turno</span>
                    <button onclick="cerrarModalTurno()" style="background:none;border:none;font-size:18px;cursor:pointer;color:#999">✕</button>
                </div>
                <div id="modalTurnoBody" style="padding:20px"></div>
                <div style="padding:0 20px 20px;display:flex;gap:10px;justify-content:flex-end">
                    <button class="btn-mc-cancelar" onclick="cerrarModalTurno()">Cancelar</button>
                    <button class="btn-mc-enviar" onclick="enviarSolicitudTurno()">Solicitar</button>
                </div>
            </div>

            <!-- Modal solicitar cambio -->
            <div class="modal-overlay-cambio" id="modalCambioOverlay" onclick="cerrarModalCambio()"></div>
            <div class="modal-cambio" id="modalCambio">
                <div class="modal-cambio-header">
                    <span id="modalCambioTitulo">Solicitar cambio de profesional</span>
                    <button onclick="cerrarModalCambio()" style="background:none;border:none;font-size:18px;cursor:pointer;color:#999">✕</button>
                </div>
                <div id="modalCambioBody" style="padding:20px"></div>
                <div style="padding:0 20px 20px;display:flex;gap:10px;justify-content:flex-end">
                    <button class="btn-mc-cancelar" onclick="cerrarModalCambio()">Cancelar</button>
                    <button class="btn-mc-enviar" onclick="enviarSolicitudCambio()">Enviar solicitud</button>
                </div>
            </div>`;
    } catch {
        c.innerHTML = `<div class="titulo-seccion">Mis Profesionales</div><p style="color:#e74c3c">Error al cargar.</p>`;
    }
}

// ── SOLICITAR TURNO ────────────────────────────────────────

let _turnoIdProf = null;
let _agendaProf  = [];

async function abrirModalTurno(idProf, nombreProf) {
    _turnoIdProf = idProf;
    document.getElementById("modalTurnoTitulo").textContent = `Solicitar turno — ${nombreProf}`;

    const agenda = await fetch(`${API}/profesionales/${idProf}/agenda`).then(r => r.json()).catch(() => []);
    _agendaProf  = agenda.filter(a => a.disponible);

    if (!_agendaProf.length) {
        document.getElementById("modalTurnoBody").innerHTML =
            `<p style="color:#888">Este profesional no tiene días disponibles en su agenda.</p>`;
        document.getElementById("modalTurnoOverlay").classList.add("visible");
        document.getElementById("modalTurno").classList.add("visible");
        return;
    }

    const diasDisponibles = _agendaProf.map(a => a.dia).join(", ");

    document.getElementById("modalTurnoBody").innerHTML = `
        <p style="font-size:13px;color:#888;margin-bottom:16px">Días disponibles: <strong>${diasDisponibles}</strong></p>
        <div style="display:flex;flex-direction:column;gap:14px">
            <div>
                <label class="turno-label">Fecha</label>
                <input type="date" id="turnoFecha" class="turno-input" onchange="actualizarHorasDisponibles()" min="${new Date().toISOString().split('T')[0]}">
            </div>
            <div>
                <label class="turno-label">Hora</label>
                <select id="turnoHora" class="turno-input"><option value="">Elegí una fecha primero</option></select>
            </div>
            <div>
                <label class="turno-label">Motivo (opcional)</label>
                <textarea id="turnoMotivo" rows="2" style="width:100%;border:2px solid #e0d4f5;border-radius:10px;padding:10px 12px;font-size:14px;outline:none;resize:none"></textarea>
            </div>
        </div>`;

    document.getElementById("modalTurnoOverlay").classList.add("visible");
    document.getElementById("modalTurno").classList.add("visible");
}

function actualizarHorasDisponibles() {
    const fechaStr = document.getElementById("turnoFecha").value;
    if (!fechaStr) return;

    const fecha  = new Date(fechaStr + "T12:00:00");
    const diaSem = fecha.getDay();
    const agenda = _agendaProf.find(a => DIA_DB_NUM[normalizar(a.dia)] === diaSem);
    const sel       = document.getElementById("turnoHora");

    if (!agenda) {
        sel.innerHTML = `<option value="">El profesional no atiende ese día</option>`;
        sel.disabled  = true;
        return;
    }

    const [hI, mI] = agenda.horaInicio.substring(0, 5).split(":").map(Number);
    const [hF, mF] = agenda.horaFin.substring(0, 5).split(":").map(Number);

    const opciones = [];
    let cur = hI * 60 + mI;
    const end = hF * 60 + mF;
    while (cur + 60 <= end) {
        const hh = String(Math.floor(cur / 60)).padStart(2, "0");
        const mm = String(cur % 60).padStart(2, "0");
        opciones.push(`<option value="${hh}:${mm}">${hh}:${mm}</option>`);
        cur += 60;
    }

    if (opciones.length) {
        sel.innerHTML = opciones.join("");
        sel.disabled  = false;
    } else {
        sel.innerHTML = `<option value="">Sin horarios disponibles</option>`;
        sel.disabled  = true;
    }
}

function cerrarModalTurno() {
    document.getElementById("modalTurnoOverlay").classList.remove("visible");
    document.getElementById("modalTurno").classList.remove("visible");
    _turnoIdProf = null;
}

async function enviarSolicitudTurno() {
    const idProf    = _turnoIdProf || parseInt(document.getElementById("turnoProf")?.value);
    const fecha     = document.getElementById("turnoFecha")?.value;
    const hora      = document.getElementById("turnoHora")?.value;
    const modalidad = "Virtual";
    const motivo    = document.getElementById("turnoMotivo")?.value || null;

    if (!idProf) { toast("Seleccioná un profesional.", "warn"); return; }
    if (!fecha)  { toast("Seleccioná una fecha.", "warn"); return; }
    if (!hora)   { toast("Seleccioná un horario.", "warn"); return; }

    const res = await fetch(`${API}/turnos/solicitar`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            idPaciente:    parseInt(idPaciente),
            idProfesional: idProf,
            fecha:         new Date(fecha + "T12:00:00").toISOString(),
            hora:          hora + ":00",
            modalidad,
            motivo
        })
    });

    cerrarModalTurno();
    if (res.ok) {
        toast("Turno solicitado correctamente. El profesional lo confirmará.");
        const r = await fetch(`${API}/pacientes/${idPaciente}/turnos-detalle`);
        turnosCalendario = await r.json();
        dibujarCalendario();
    } else toast("Error al solicitar el turno.", "error");
}

// ── SOLICITAR CAMBIO ────────────────────────────────────────
let _cambioIdProfOrigen = null;

function abrirModalCambio(idProfOrigen, idEsp, nombreProf) {
    _cambioIdProfOrigen = idProfOrigen;
    document.getElementById("modalCambioTitulo").textContent = `Solicitar cambio de: ${nombreProf}`;

    document.getElementById("modalCambioBody").innerHTML = `
        <p style="font-size:14px;color:#666;margin-bottom:14px">Tu solicitud será revisada por el equipo. Te avisaremos cuando sea procesada.</p>
        <div>
            <label style="font-size:12px;font-weight:bold;color:#6A5ACD;text-transform:uppercase;letter-spacing:.5px;display:block;margin-bottom:6px">Motivo</label>
            <textarea id="motivoCambio" rows="3" placeholder="Contanos por qué querés cambiar de profesional..." style="width:100%;border:2px solid #e0d4f5;border-radius:10px;padding:10px 12px;font-size:14px;outline:none;resize:none"></textarea>
        </div>`;

    document.getElementById("modalCambioOverlay").classList.add("visible");
    document.getElementById("modalCambio").classList.add("visible");
}

function cerrarModalCambio() {
    document.getElementById("modalCambioOverlay").classList.remove("visible");
    document.getElementById("modalCambio").classList.remove("visible");
    _cambioIdProfOrigen = null;
}

async function enviarSolicitudCambio() {
    const motivo = document.getElementById("motivoCambio").value || null;

    const res = await fetch(`${API}/derivaciones`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            idPaciente:           parseInt(idPaciente),
            idProfesionalOrigen:  _cambioIdProfOrigen,
            idProfesionalDestino: _cambioIdProfOrigen, // el configurador elige el destino al aceptar
            motivo
        })
    });

    cerrarModalCambio();
    if (res.ok) toast("Solicitud enviada correctamente. El configurador la revisará.");
    else toast("Error al enviar la solicitud.", "error");
}

// ── MI PERFIL ──────────────────────────────────────────────
async function renderPerfil(c) {
    try {
        const p = await fetch(`${API}/pacientes/${idPaciente}`).then(r => r.json());
        const fn = p.fechaNacimiento ? p.fechaNacimiento.substring(0, 10) : "";

        c.innerHTML = `
            <div class="titulo-seccion">Mi Perfil</div>
            <div class="card perfil-form">
                <div class="campo"><label>Nombre</label><input type="text" id="pNombre" value="${p.nombre ?? ""}"></div>
                <div class="campo"><label>DNI</label><input type="text" id="pDni" value="${p.dni ?? ""}"></div>
                <div class="campo"><label>Fecha de Nacimiento</label><input type="date" id="pFechaNac" value="${fn}"></div>
                <div class="campo"><label>Domicilio</label><input type="text" id="pDomicilio" value="${p.domicilio ?? ""}"></div>
                <div class="campo"><label>Teléfono</label><input type="text" id="pTelefono" value="${p.telefono ?? ""}"></div>
                <div class="campo"><label>Email</label><input type="text" id="pMail" value="${p.mail ?? ""}"></div>
                <button class="btn-guardar-perfil" onclick="guardarPerfil(${p.idPaciente})">Guardar cambios</button>
                <div class="msg-ok" id="msgOk">Cambios guardados correctamente.</div>
            </div>`;
    } catch {
        c.innerHTML = `<div class="titulo-seccion">Mi Perfil</div><p style="color:#e74c3c">Error al cargar.</p>`;
    }
}

async function guardarPerfil(id) {
    const body = {
        idPaciente:      id,
        nombre:          document.getElementById("pNombre").value,
        dni:             document.getElementById("pDni").value,
        fechaNacimiento: document.getElementById("pFechaNac").value || null,
        domicilio:       document.getElementById("pDomicilio").value,
        telefono:        document.getElementById("pTelefono").value,
        mail:            document.getElementById("pMail").value,
        estado:          "Activo"
    };

    const res = await fetch(`${API}/pacientes/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
    });

    if (res.ok) {
        const msg = document.getElementById("msgOk");
        msg.style.display = "block";
        setTimeout(() => msg.style.display = "none", 3000);
    } else {
        toast("Error al guardar los cambios.", "error");
    }
}

// ── INIT ────────────────────────────────────────────────────
mostrarSeccion("inicio");
