const API = (location.protocol.startsWith('http') && location.port !== '5500') ? location.origin : 'http://localhost:5000';

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
    else if (id === "buscar")        renderBuscarTurno(c);
    else if (id === "profesionales") renderProfesionales(c);
    else if (id === "perfil")        renderPerfil(c);
    else if (id === "sesion")        c.innerHTML = `<iframe src="../Pantalla_Paciente/sesion_video.html" allow="camera; microphone" style="width:100%;height:calc(100vh - 120px);border:none;"></iframe>`;  // NUEVO
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
            const pendientesHtml = await renderTurnosPendientesProfesional();
            c.innerHTML = `
                <div class="titulo-seccion">Inicio</div>
                <div class="card">
                    <p class="sin-turnos">No tenés turnos próximos.</p>
                </div>
                ${pendientesHtml}`;
            return;
        }

        const t = await res.json();
        const fecha = new Date(t.fecha).toLocaleDateString("es-AR", { weekday:"long", day:"numeric", month:"long", year:"numeric" });
        const hora  = t.hora.substring(0,5);

        const pendientesHtml = await renderTurnosPendientesProfesional();
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
                        <div style="font-size:11px;text-transform:uppercase;letter-spacing:.5px;opacity:.7;margin-bottom:4px">Estado</div>
                        <div style="font-size:16px;font-weight:600">${t.estadoDescripcion}</div>
                    </div>
                </div>
            </div>
            ${pendientesHtml}`;
    } catch {
        c.innerHTML = `<div class="titulo-seccion">Inicio</div><p style="color:#e74c3c">Error al cargar los datos.</p>`;
    }
}

async function renderTurnosPendientesProfesional() {
    let turnos = [];
    try {
        const r = await fetch(`${API}/pacientes/${idPaciente}/turnos`);
        turnos = await r.json();
    } catch { return ''; }

    const pendientes = turnos.filter(t => t.idEstadoTurno === 1 && t.creadoPor === 'profesional');
    if (!pendientes.length) return '';

    const profMap = {};
    await Promise.all(pendientes.map(async t => {
        if (!profMap[t.idProfesional]) {
            try { profMap[t.idProfesional] = await (await fetch(`${API}/profesionales/${t.idProfesional}`)).json(); } catch {}
        }
    }));

    const filas = pendientes.map(t => {
        const fecha = new Date(t.fecha);
        const p = profMap[t.idProfesional];
        return `
        <div style="display:flex;align-items:center;gap:16px;padding:14px;background:#faf7ff;border-radius:12px;border-left:4px solid #0d6efd;margin-bottom:10px;flex-wrap:wrap">
            <div style="background:linear-gradient(135deg,#0d6efd,#6A5ACD);color:white;border-radius:10px;padding:10px 14px;text-align:center;min-width:56px">
                <div style="font-size:20px;font-weight:bold;line-height:1">${fecha.getDate()}</div>
                <div style="font-size:11px;text-transform:uppercase">${fecha.toLocaleString('es-AR',{month:'short'})}</div>
            </div>
            <div style="flex:1">
                <div style="font-size:15px;font-weight:bold;color:#222">${p ? p.nombre+' '+(p.apellido??'') : 'Profesional #'+t.idProfesional}</div>
                <div style="font-size:13px;color:#888">${t.hora?.substring(0,5) ?? ''} · ${t.modalidad ?? ''}</div>
            </div>
            <button onclick="responderTurnoPaciente(${t.idTurno}, true)"  style="padding:6px 16px;border:none;border-radius:8px;background:#d1fae5;color:#065f46;font-weight:bold;cursor:pointer">Aceptar</button>
            <button onclick="responderTurnoPaciente(${t.idTurno}, false)" style="padding:6px 16px;border:none;border-radius:8px;background:#fee2e2;color:#991b1b;font-weight:bold;cursor:pointer">Rechazar</button>
        </div>`;
    }).join('');

    return `
        <div class="card" style="margin-top:20px">
            <p style="font-size:11px;font-weight:bold;text-transform:uppercase;letter-spacing:1px;color:#9D6BCF;margin-bottom:16px">Turnos propuestos por tu profesional</p>
            ${filas}
        </div>`;
}

async function responderTurnoPaciente(id, aceptar) {
    try {
        let res;
        if (aceptar) {
            res = await fetch(`${API}/turnos/${id}/confirmar?quien=paciente`, { method:'PUT' });
        } else {
            res = await fetch(`${API}/turnos/${id}/rechazar`, { method:'PUT' });
        }
        if (res.ok) { toast(aceptar ? 'Turno aceptado' : 'Turno rechazado', aceptar ? 'ok' : 'warn'); mostrarSeccion('agenda'); }
        else if (res.status === 400) { const e = await res.json().catch(() => null); toast(e?.mensaje ?? 'No se pudo confirmar', 'error'); }
        else toast('Error al responder', 'error');
    } catch { toast('Error de conexión', 'error'); }
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
            const yoPedi      = tieneT && turnosPorDia[dia].some(t => t.estadoDescripcion === "Pendiente" && t.creadoPor === 'paciente');
            const mePidieron  = tieneT && turnosPorDia[dia].some(t => t.estadoDescripcion === "Pendiente" && t.creadoPor === 'profesional');
            const claseT = tieneT
                ? (soloCancelado ? "con-turno-cancelado" : mePidieron ? "con-turno-pendiente-ellos" : yoPedi ? "con-turno-pendiente-yo" : "con-turno")
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
        turnoSeleccionado = turnosDia.find(t => t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado") ?? turnosDia[0];
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
            ${t.motivo ? `<div class="panel-chips"><div class="chip chip-wide"><span class="chip-label">Motivo</span><span class="chip-val">${t.motivo}</span></div></div>` : ""}
            ${t.estadoDescripcion === 'Pendiente' && t.creadoPor === 'profesional' ? `
            <div class="panel-acciones">
                <button class="btn-accion btn-guardar" onclick="responderTurnoPaciente(${t.idTurno}, true)">Aceptar turno</button>
                <button class="btn-accion btn-cancelar" onclick="responderTurnoPaciente(${t.idTurno}, false)">Rechazar</button>
            </div>` : ''}
            ${puedeAccionar && t.creadoPor !== 'profesional' ? `
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

    let hayDisponibilidad = false;
    if (!tieneActivo) {
        const diaSem = new Date(fechaStr + "T12:00:00").getDay();
        const profs  = await fetch(`${API}/pacientes/${idPaciente}/mis-profesionales`).then(r => r.json()).catch(() => []);
        const agendas = await Promise.all(profs.map(p =>
            fetch(`${API}/profesionales/${p.idProfesional}/agenda`).then(r => r.json()).catch(() => [])
        ));
        hayDisponibilidad = agendas.some(ag => ag.some(a => a.disponible && DIA_DB_NUM[normalizar(a.dia)] === diaSem));
    }

    html += (!tieneActivo && hayDisponibilidad) ? `<div class="panel-solicitar-bloque">
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
    if (!tieneActivo && hayDisponibilidad) cargarProfesionalesSolicitud(fechaStr);
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
    const idProf = parseInt(document.getElementById("solProf").value);
    const hora   = document.getElementById("solHora").value;
    const motivo = document.getElementById("solMotivo").value || null;

    if (!idProf) { toast("Seleccioná un profesional.", "warn"); return; }
    if (!hora)   { toast("Seleccioná un horario.", "warn"); return; }

    const errAnticip = window.Validaciones.anticipacionHoras(fechaStr, hora, 24);
    if (errAnticip) { toast(errAnticip, "warn"); return; }

    const res = await fetch(`${API}/turnos/solicitar`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            idPaciente:    parseInt(idPaciente),
            idProfesional: idProf,
            fecha:         new Date(fechaStr + "T12:00:00").toISOString(),
            hora:          hora + ":00",
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

    const idPac  = parseInt(idPaciente);
    const idProf = parseInt(_cambioIdProfOrigen);
    if (!(idPac > 0) || !(idProf > 0)) {
        toast("No se pudo identificar al paciente o al profesional. Cerrá y volvé a intentar.", "error");
        return;
    }

    const res = await fetch(`${API}/derivaciones`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            idPaciente:           idPac,
            idProfesionalOrigen:  idProf,
            idProfesionalDestino: idProf,
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
        window._pacienteActual = p;
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

        window.Validaciones.conectarLimpieza(["pNombre", "pDni", "pMail", "pDomicilio", "pFechaNac", "pTelefono"]);
    } catch {
        c.innerHTML = `<div class="titulo-seccion">Mi Perfil</div><p style="color:#e74c3c">Error al cargar.</p>`;
    }
}

async function guardarPerfil(id) {
    const V = window.Validaciones;
    const ok = V.validarCampos([
        { id: "pNombre",    validar: v => V.requerido(v, "El nombre") },
        { id: "pDni",       validar: v => V.requerido(v, "El DNI") || V.dni(v) },
        { id: "pMail",      validar: v => V.requerido(v, "El email") || V.email(v) },
        { id: "pDomicilio", validar: v => V.requerido(v, "El domicilio") },
        { id: "pFechaNac",  validar: v => V.requerido(v, "La fecha de nacimiento") || V.fechaNoFutura(v, "La fecha de nacimiento") },
        { id: "pTelefono",  validar: v => V.telefono(v) },
    ]);
    if (!ok) return;

    const estadoReal = window._pacienteActual?.estado;
    if (!estadoReal) { toast("No se pudo determinar el estado del paciente. Recargá e intentá de nuevo.", "error"); return; }

    const body = {
        idPaciente:      id,
        nombre:          document.getElementById("pNombre").value,
        dni:             document.getElementById("pDni").value,
        fechaNacimiento: document.getElementById("pFechaNac").value || null,
        domicilio:       document.getElementById("pDomicilio").value,
        telefono:        document.getElementById("pTelefono").value,
        mail:            document.getElementById("pMail").value,
        estado:          estadoReal
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

// ── BUSCAR TURNO ────────────────────────────────────────────
async function renderBuscarTurno(c) {
    const servicios = await fetch(`${API}/servicios`).then(r => r.json()).catch(() => []);

    c.innerHTML = `
        <div class="titulo-seccion">Buscar Turno</div>
        <div class="card">
            <p style="font-size:13px;color:#888;margin-bottom:16px">Seleccioná un servicio para ver los profesionales disponibles.</p>
            <div style="display:flex;flex-wrap:wrap;gap:12px" id="listaServicios">
                ${servicios.map(s => `
                    <button onclick="verProfesionalesServicio(${s.idServicio}, '${s.nombre.replace(/'/g,"\\'")}', this)"
                        style="padding:10px 20px;border:2px solid #e0d4f5;border-radius:12px;background:white;cursor:pointer;font-size:14px;color:#6A5ACD;font-weight:500;transition:all .2s">
                        ${s.nombre}
                    </button>`).join('')}
            </div>
        </div>
        <div id="resultadosBusqueda"></div>`;
}

async function verProfesionalesServicio(idServicio, nombreServicio, btn) {
    document.querySelectorAll('#listaServicios button').forEach(b => {
        b.style.background = 'white';
        b.style.color = '#6A5ACD';
        b.style.borderColor = '#e0d4f5';
    });
    btn.style.background = '#6A5ACD';
    btn.style.color = 'white';
    btn.style.borderColor = '#6A5ACD';

    const res = document.getElementById('resultadosBusqueda');
    res.innerHTML = `<div class="card"><p style="color:#888">Cargando profesionales...</p></div>`;

    const agendas = await fetch(`${API}/agendas`).then(r => r.json()).catch(() => []);
    const agendasServicio = agendas.filter(a => a.idServicio === idServicio);

    if (!agendasServicio.length) {
        res.innerHTML = `<div class="card"><p style="color:#888">No hay agendas disponibles para este servicio.</p></div>`;
        return;
    }

    const profIds = [...new Set(
        await Promise.all(agendasServicio.map(a =>
            fetch(`${API}/agendas/${a.idAgenda}/propietarios`).then(r => r.json()).catch(() => [])
        )).then(results => results.flat().map(p => p.idProfesional))
    )];

    if (!profIds.length) {
        res.innerHTML = `<div class="card"><p style="color:#888">No hay profesionales asignados a este servicio aún.</p></div>`;
        return;
    }

    const profesionales = await Promise.all(
        profIds.map(id => fetch(`${API}/profesionales/${id}`).then(r => r.json()).catch(() => null))
    ).then(lista => lista.filter(p => p && p.estado === 'Activo'));

    if (!profesionales.length) {
        res.innerHTML = `<div class="card"><p style="color:#888">No hay profesionales activos para este servicio.</p></div>`;
        return;
    }

    res.innerHTML = `
        <div class="titulo-seccion" style="margin-top:20px">${nombreServicio}</div>
        <div class="prof-lista">
            ${profesionales.map(p => `
                <div class="prof-card">
                    <div class="prof-nombre">${p.nombre}${p.apellido ? ' ' + p.apellido : ''}</div>
                    <div class="prof-especialidad">${p.especialidad ?? ''}</div>
                    ${p.mail     ? `<div class="prof-dato">${p.mail}</div>`     : ''}
                    ${p.telefono ? `<div class="prof-dato">${p.telefono}</div>` : ''}
                    <button class="btn-solicitar-cambio"
                        onclick="abrirSolicitudTurnoLibre(${p.idProfesional}, '${(p.nombre + ' ' + (p.apellido ?? '')).trim().replace(/'/g,"\\'")}')">
                        Solicitar turno
                    </button>
                </div>`).join('')}
        </div>`;
}

async function abrirSolicitudTurnoLibre(idProf, nombreProf) {
    const agenda = await fetch(`${API}/agendas`).then(r => r.json()).catch(() => []);
    const propietarios = await Promise.all(
        agenda.map(a => fetch(`${API}/agendas/${a.idAgenda}/propietarios`).then(r => r.json()).catch(() => []))
    );
    const agendaProf = agenda.filter((a, i) =>
        propietarios[i].some(p => p.idProfesional === idProf)
    );

    const horarios = (await Promise.all(
        agendaProf.map(a => fetch(`${API}/agendas/${a.idAgenda}/horarios`).then(r => r.json()).catch(() => []))
    )).flat().filter(h => h.estado === 'Activo');

    if (!horarios.length) {
        toast('Este profesional no tiene horarios cargados aún.', 'warn');
        return;
    }

    const diasDisponibles = [];
    if (horarios.some(h => h.lunes))     diasDisponibles.push('Lunes');
    if (horarios.some(h => h.martes))    diasDisponibles.push('Martes');
    if (horarios.some(h => h.miercoles)) diasDisponibles.push('Miércoles');
    if (horarios.some(h => h.jueves))    diasDisponibles.push('Jueves');
    if (horarios.some(h => h.viernes))   diasDisponibles.push('Viernes');
    if (horarios.some(h => h.sabado))    diasDisponibles.push('Sábado');
    if (horarios.some(h => h.domingo))   diasDisponibles.push('Domingo');

    window._turnoLibreHorarios = horarios;
    window._turnoLibreIdProf   = idProf;

    const hoy = new Date().toISOString().split('T')[0];

    const modalHtml = `
        <div class="modal-overlay-cambio visible" id="modalTurnoLibreOverlay" onclick="cerrarTurnoLibre()"></div>
        <div class="modal-cambio visible" id="modalTurnoLibre">
            <div class="modal-cambio-header">
                <span>Solicitar turno — ${nombreProf}</span>
                <button onclick="cerrarTurnoLibre()" style="background:none;border:none;font-size:18px;cursor:pointer;color:#999">✕</button>
            </div>
            <div style="padding:20px">
                <p style="font-size:13px;color:#888;margin-bottom:16px">Atiende los días: <strong>${diasDisponibles.join(', ')}</strong></p>
                <div style="display:flex;flex-direction:column;gap:14px">
                    <div>
                        <label class="turno-label">Fecha</label>
                        <input type="date" id="turnoLibreFecha" class="turno-input" min="${hoy}" onchange="cargarHorasTurnoLibre()">
                    </div>
                    <div>
                        <label class="turno-label">Hora</label>
                        <select id="turnoLibreHora" class="turno-input" disabled>
                            <option value="">Elegí una fecha primero</option>
                        </select>
                    </div>
                    <div>
                        <label class="turno-label">Motivo (opcional)</label>
                        <textarea id="turnoLibreMotivo" rows="2" style="width:100%;border:2px solid #e0d4f5;border-radius:10px;padding:10px 12px;font-size:14px;outline:none;resize:none"></textarea>
                    </div>
                </div>
            </div>
            <div style="padding:0 20px 20px;display:flex;gap:10px;justify-content:flex-end">
                <button class="btn-mc-cancelar" onclick="cerrarTurnoLibre()">Cancelar</button>
                <button class="btn-mc-enviar" onclick="confirmarTurnoLibre()">Solicitar turno</button>
            </div>
        </div>`;

    document.body.insertAdjacentHTML('beforeend', modalHtml);
}

function cargarHorasTurnoLibre() {
    const fechaStr = document.getElementById('turnoLibreFecha').value;
    const sel      = document.getElementById('turnoLibreHora');
    if (!fechaStr) return;

    const fecha  = new Date(fechaStr + 'T12:00:00');
    const diaSem = fecha.getDay();

    const MAP_DIA = { 0:'domingo', 1:'lunes', 2:'martes', 3:'miercoles', 4:'jueves', 5:'viernes', 6:'sabado' };
    const diaKey  = MAP_DIA[diaSem];

    const horario = window._turnoLibreHorarios.find(h => h[diaKey] === true);

    if (!horario) {
        sel.innerHTML = `<option value="">El profesional no atiende ese día</option>`;
        sel.disabled  = true;
        return;
    }

    const [hI, mI] = horario.horaInicio.substring(0,5).split(':').map(Number);
    const [hF, mF] = horario.horaFin.substring(0,5).split(':').map(Number);
    const intervalo = horario.intervaloMinutos || 30;

    const opciones = [];
    let cur = hI * 60 + mI;
    while (cur + intervalo <= hF * 60 + mF) {
        const hh = String(Math.floor(cur/60)).padStart(2,'0');
        const mm = String(cur%60).padStart(2,'0');
        opciones.push(`<option value="${hh}:${mm}">${hh}:${mm}</option>`);
        cur += intervalo;
    }

    sel.innerHTML = opciones.length ? opciones.join('') : `<option value="">Sin horarios disponibles</option>`;
    sel.disabled  = !opciones.length;
}

async function confirmarTurnoLibre() {
    const fecha  = document.getElementById('turnoLibreFecha').value;
    const hora   = document.getElementById('turnoLibreHora').value;
    const motivo = document.getElementById('turnoLibreMotivo').value || null;

    if (!fecha) { toast('Seleccioná una fecha.', 'warn'); return; }
    if (!hora)  { toast('Seleccioná un horario.', 'warn'); return; }

    const res = await fetch(`${API}/turnos/solicitar`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            idPaciente:    parseInt(idPaciente),
            idProfesional: window._turnoLibreIdProf,
            fecha:         new Date(fecha + 'T12:00:00').toISOString(),
            hora:          hora + ':00',
            modalidad:     'Virtual',
            motivo
        })
    });

    if (res.ok) {
        cerrarTurnoLibre();
        toast('Turno solicitado correctamente. El profesional lo confirmará.');
        mostrarSeccion('agenda');
    } else {
        try {
            const error = await res.json();
            cerrarTurnoLibre();
            toast(error.mensaje ?? 'Error al solicitar el turno.', 'error');
        } catch {
            cerrarTurnoLibre();
            toast('Error al solicitar el turno.', 'error');
        }
    }
}

function cerrarTurnoLibre() {
    document.getElementById('modalTurnoLibreOverlay')?.remove();
    document.getElementById('modalTurnoLibre')?.remove();
}
