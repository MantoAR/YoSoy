const API = "http://localhost:5000";
// Escape de HTML para datos que cargan los usuarios. En Cambios-Agus vive en shared/ui.js (YS.esc);
// en main no existe esa capa, así que el parche de XSS define lo mínimo que usa.
window.YS = window.YS || {};
YS.esc = YS.esc || (s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]));

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
let _agendaProfCalendario = [];
let _idProfCalendario = null;

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
    else if (id === "sesion")        c.innerHTML = renderSesion();
    else if (id === "admision")      renderAdmision(c);
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

    // Cargar profesionales del paciente
    let profesionales = [];
    try {
        profesionales = await fetch(`${API}/pacientes/${idPaciente}/mis-profesionales`).then(r => r.json());
    } catch {}

    c.innerHTML = `
        <div class="titulo-seccion">Mi Agenda</div>

        ${profesionales.length ? `
        <div class="card" style="margin-bottom:12px;padding:14px 16px">
            <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap">
                <label style="font-size:13px;font-weight:600;color:#6A5ACD;white-space:nowrap">Ver disponibilidad de:</label>
                <select id="selProfCalendario" onchange="cambiarProfCalendario()" 
                    style="flex:1;min-width:200px;padding:8px 12px;border:2px solid #e0d4f5;border-radius:10px;font-size:14px;outline:none;background:white">
                    <option value="">— Sin filtro —</option>
                    ${profesionales.map(p => `<option value="${p.idProfesional}">${YS.esc(p.nombre)} — ${YS.esc(p.especialidad)}</option>`).join("")}
                </select>
                <div id="diasAtencion" style="font-size:12px;color:#6A5ACD;font-weight:500"></div>
            </div>
        </div>` : ""}

        <div class="card">
            <div class="cal-nav">
                <button onclick="cambiarMes(-1)">&#8249;</button>
                <span class="cal-mes" id="calTitulo"></span>
                <button onclick="cambiarMes(1)">&#8250;</button>
            </div>
            <div class="cal-grid" id="calGrid"></div>
            <div style="display:flex;gap:16px;flex-wrap:wrap;margin-top:12px;padding-top:12px;border-top:1px solid #f0eaff;font-size:12px;color:#666">
                <span><span style="display:inline-block;width:12px;height:12px;border-radius:3px;background:#0d6efd;margin-right:4px;vertical-align:middle"></span>Mis turnos</span>
                <span><span style="display:inline-block;width:12px;height:12px;border-radius:3px;background:#d4edda;border:1px solid #28a745;margin-right:4px;vertical-align:middle"></span>Días disponibles del profesional</span>
                <span><span style="display:inline-block;width:12px;height:12px;border-radius:3px;background:#f0f0f0;margin-right:4px;vertical-align:middle"></span>Sin disponibilidad</span>
            </div>
        </div>
        <div class="panel-turno" id="panelTurno"></div>
    `;

    dibujarCalendario();
}

async function cambiarProfCalendario() {
    const sel = document.getElementById("selProfCalendario");
    _idProfCalendario = parseInt(sel.value) || null;
    _agendaProfCalendario = [];

    const diasDiv = document.getElementById("diasAtencion");

    if (!_idProfCalendario) {
        diasDiv.textContent = "";
        dibujarCalendario();
        return;
    }

    try {
        const agenda = await fetch(`${API}/profesionales/${_idProfCalendario}/agenda`).then(r => r.json());
        _agendaProfCalendario = agenda.filter(a => a.disponible);
    } catch {}

    if (_agendaProfCalendario.length) {
        const dias = _agendaProfCalendario.map(a => a.dia).join(", ");
        diasDiv.textContent = `Atiende los: ${dias}`;
    } else {
        diasDiv.textContent = "Sin días configurados";
    }

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

            // Si hay profesional seleccionado, resaltar días disponibles
            let esDisponibleProf = false;
            if (_idProfCalendario && _agendaProfCalendario.length) {
                esDisponibleProf = _agendaProfCalendario.some(
                    a => DIA_DB_NUM[normalizar(a.dia)] === fechaD.getDay()
                );
            }

            let claseT;
            if (tieneT) {
                claseT = soloCancelado ? "con-turno-cancelado" : pendiente ? "con-turno-pendiente" : "con-turno";
            } else if (esPasado) {
                claseT = "pasado";
            } else if (_idProfCalendario && esDisponibleProf) {
                claseT = "dia-disponible-prof";
            } else if (_idProfCalendario && !esDisponibleProf) {
                claseT = "dia-no-disponible-prof";
            } else {
                claseT = "futuro";
            }

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
                    <div class="panel-profesional">${YS.esc(t.nombreProfesional)}</div>
                    <div class="panel-especialidad">${YS.esc(t.especialidad)}</div>
                </div>
                <span class="panel-estado-badge" style="background:${estadoBg};color:${estadoTxt}">${YS.esc(t.estadoDescripcion)}</span>
            </div>
            <div class="panel-chips">
                <div class="chip"><span class="chip-label">Modalidad</span><span class="chip-val">${YS.esc(t.modalidad ?? "-")}</span></div>
                ${t.motivo ? `<div class="chip chip-wide"><span class="chip-label">Motivo</span><span class="chip-val">${YS.esc(t.motivo)}</span></div>` : ""}
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
        ? `<option value="">Seleccioná un profesional</option>` + profs.map(p => `<option value="${p.idProfesional}">${YS.esc(p.nombre)} — ${YS.esc(p.especialidad)}</option>`).join("")
        : `<option value="">Sin profesionales asignados</option>`;
}

function normalizar(s) {
    return (s ?? "").toLowerCase().normalize("NFD").replace(/\p{Diacritic}/gu, "");
}

async function cargarHorasSolicitud(fechaStr) {
    const idProf = parseInt(document.getElementById("solProf").value);
    const sel    = document.getElementById("solHora");
    if (!idProf) { sel.innerHTML = `<option value="">Elegí un profesional</option>`; sel.disabled = true; return; }

    const fecha  = new Date(fechaStr + "T12:00:00");
    const diaSem = fecha.getDay();
    const agenda = await fetch(`${API}/profesionales/${idProf}/agenda`).then(r => r.json()).catch(() => []);
    const slot   = agenda.find(a => a.disponible && DIA_DB_NUM[normalizar(a.dia)] === diaSem);

    if (!slot) { sel.innerHTML = `<option value="">Sin disponibilidad ese día</option>`; sel.disabled = true; return; }

    const [hI, mI] = slot.horaInicio.substring(0,5).split(":").map(Number);
    const [hF, mF] = slot.horaFin.substring(0,5).split(":").map(Number);

    // Usar el intervalo configurado por el médico (por defecto 30 min)
    const intervalo = slot.intervaloMinutos ?? 30;

    // Obtener turnos ya ocupados ese día para ese profesional
    let turnosOcupados = [];
    try {
        const r = await fetch(`${API}/turnos/ocupados?idProfesional=${idProf}&fecha=${fechaStr}`);
        if (r.ok) turnosOcupados = await r.json();
    } catch {}

    const opciones = [];
    let cur = hI * 60 + mI;
    while (cur + intervalo <= hF * 60 + mF) {
        const hh = String(Math.floor(cur/60)).padStart(2,"0");
        const mm = String(cur%60).padStart(2,"0");
        const horaStr = `${hh}:${mm}`;
        const ocupado = turnosOcupados.some(t => t.hora?.substring(0,5) === horaStr);
        if (!ocupado) {
            opciones.push(`<option value="${horaStr}">${horaStr}</option>`);
        }
        cur += intervalo;
    }
    sel.innerHTML = opciones.length ? opciones.join("") : `<option value="">Sin horarios disponibles</option>`;
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
        window._misProfesionales = lista;

        if (!lista.length) {
            c.innerHTML = `<div class="titulo-seccion">Mis Profesionales</div><div class="card"><p class="sin-turnos">No tenés profesionales asignados aún.</p></div>`;
            return;
        }

        const cards = lista.map(p => `
            <div class="prof-card">
                <div class="prof-nombre">${YS.esc(p.nombre)}</div>
                <div class="prof-especialidad">${YS.esc(p.especialidad)}</div>
                ${p.mail     ? `<div class="prof-dato">${YS.esc(p.mail)}</div>`     : ""}
                ${p.telefono ? `<div class="prof-dato">${YS.esc(p.telefono)}</div>` : ""}
                <button class="btn-solicitar-cambio" onclick="abrirModalCambio(${p.idProfesional}, ${p.idEspecialidad})">Solicitar cambio de profesional</button>
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

function abrirModalCambio(idProfOrigen, idEsp, nombreProf = null) {
    _cambioIdProfOrigen = idProfOrigen;
    nombreProf ??= (window._misProfesionales || []).find(p => p.idProfesional === idProfOrigen)?.nombre ?? "tu profesional";
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
                <div class="campo"><label>Nombre</label><input type="text" id="pNombre" value="${YS.esc(p.nombre ?? "")}"></div>
                <div class="campo"><label>DNI</label><input type="text" id="pDni" value="${YS.esc(p.dni ?? "")}"></div>
                <div class="campo"><label>Fecha de Nacimiento</label><input type="date" id="pFechaNac" value="${fn}"></div>
                <div class="campo"><label>Domicilio</label><input type="text" id="pDomicilio" value="${YS.esc(p.domicilio ?? "")}"></div>
                <div class="campo"><label>Teléfono</label><input type="text" id="pTelefono" value="${YS.esc(p.telefono ?? "")}"></div>
                <div class="campo"><label>Email</label><input type="text" id="pMail" value="${YS.esc(p.mail ?? "")}"></div>
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

function renderSesion() {
    return '<div style="padding:30px;max-width:500px;margin:0 auto">' +
        '<h2 style="margin-bottom:20px">Iniciar sesion de video</h2>' +
        '<p style="margin-bottom:15px">Ingresa el codigo de sala que te dio tu profesional:</p>' +
        '<input type="text" id="codigoSala" placeholder="Codigo de sala" style="width:100%;padding:10px;font-size:16px;border:1px solid #ccc;border-radius:6px;margin-bottom:15px">' +
        '<button onclick="entrarSesion()" style="width:100%;padding:12px;background:#4a90d9;color:white;border:none;border-radius:6px;font-size:16px;cursor:pointer">Entrar a la sesion</button>' +
        '</div>';
}

function entrarSesion() {
    var codigo = document.getElementById('codigoSala').value.trim();
    if (codigo === '') { alert('Ingresa el codigo de sala.'); return; }
    window.location.href = 'sesion_video.html?sala=' + encodeURIComponent(codigo) + '&idPaciente=' + idPaciente;
}

async function renderAdmision(c) {
    // Ver si ya tiene informe enviado
    try {
        const r = await fetch(API + "/pacientes/" + idPaciente + "/informe-admision");
        if (r.ok) {
            const inf = await r.json();
            const colores = { "Pendiente": "#fff3cd|#856404", "En entrevista": "#cfe2ff|#084298", "Derivado": "#d1e7dd|#0f5132" };
            const par = (colores[inf.estado] || "#e2e3e5|#41464b").split("|");
            c.innerHTML = '<div class="titulo-seccion">Admision</div>' +
                '<div class="card" style="max-width:600px">' +
                '<h3 style="color:#6A5ACD;margin-bottom:16px">Tu informe de admision ya fue enviado</h3>' +
                '<p style="margin-bottom:10px">Estado actual: <span style="background:' + par[0] + ';color:' + par[1] + ';border-radius:10px;padding:4px 14px;font-weight:bold">' + inf.estado + '</span></p>' +
                '<p style="font-size:13px;color:#888">Cuando el equipo de admision revise tu informe, te contactaremos para coordinar la primera entrevista.</p>' +
                '</div>';
            return;
        }
    } catch(e) {}

    // No tiene informe: mostrar formulario
    let especialidades = [];
    try { especialidades = await (await fetch(API + "/especialidades")).json(); } catch(e) {}
    const opts = especialidades
        .filter(function(e) { return e.estado === "Activo"; })
        .map(function(e) { return '<option value="' + e.idEspecialidad + '">' + YS.esc(e.nombre) + '</option>'; })
        .join("");

    const inputStyle = "width:100%;padding:9px 12px;border:2px solid #e0d4f5;border-radius:8px;font-size:14px;margin-top:4px";
    const labelStyle = "display:block;font-size:12px;font-weight:bold;color:#6A5ACD;margin-top:14px";

    c.innerHTML = '<div class="titulo-seccion">Informe de Admision</div>' +
        '<div class="card" style="max-width:700px">' +
        '<p style="font-size:13px;color:#888;margin-bottom:6px">Completa este formulario para que el equipo de admision pueda conocerte y asignarte el profesional adecuado.</p>' +

        '<label style="' + labelStyle + '">Especialidad solicitada *</label>' +
        '<select id="admEspecialidad" style="' + inputStyle + '"><option value="">Selecciona una especialidad</option>' + opts + '</select>' +

        '<label style="' + labelStyle + '">Motivo de consulta *</label>' +
        '<textarea id="admMotivo" rows="3" style="' + inputStyle + '" placeholder="Describe brevemente el motivo por el cual solicitas atencion"></textarea>' +

        '<label style="' + labelStyle + '">Has recibido tratamiento anteriormente?</label>' +
        '<select id="admTratAnterior" style="' + inputStyle + '" onchange="document.getElementById(\'admTratDetalle\').style.display = this.value === \'true\' ? \'block\' : \'none\'"><option value="false">No</option><option value="true">Si</option></select>' +
        '<input type="text" id="admTratDetalle" style="' + inputStyle + ';display:none" placeholder="Indica cual">' +

        '<label style="' + labelStyle + '">Actualmente recibes medicacion?</label>' +
        '<select id="admMedicacion" style="' + inputStyle + '" onchange="document.getElementById(\'admMedDetalle\').style.display = this.value === \'true\' ? \'block\' : \'none\'"><option value="false">No</option><option value="true">Si</option></select>' +
        '<input type="text" id="admMedDetalle" style="' + inputStyle + ';display:none" placeholder="Cual?">' +

        '<h4 style="color:#6A5ACD;margin-top:24px">Conociendote mejor</h4>' +
        '<p style="font-size:12px;color:#999">Estas preguntas nos ayudan a brindarte una atencion personalizada.</p>' +

        '<label style="' + labelStyle + '">Gustos e intereses</label>' +
        '<textarea id="admGustos" rows="3" style="' + inputStyle + '" placeholder="Actividades que disfrutas, hobbies, musica, peliculas o libros, deportes, que te ayuda a relajarte, algo que quieras aprender"></textarea>' +

        '<label style="' + labelStyle + '">Preferencias durante la atencion</label>' +
        '<textarea id="admPreferencias" rows="3" style="' + inputStyle + '" placeholder="Como preferis que se dirijan a vos, algo que te genere ansiedad en una consulta, apoyos que necesites"></textarea>' +

        '<label style="' + labelStyle + '">Tipo de entrevista preferida</label>' +
        '<select id="admTipoEntrevista" style="' + inputStyle + '"><option value="Sin preferencia">Sin preferencia</option><option value="Individual">Individual</option><option value="Familiar">Familiar</option><option value="Grupal">Grupal</option></select>' +

        '<label style="' + labelStyle + '">Red de apoyo</label>' +
        '<textarea id="admRedApoyo" rows="2" style="' + inputStyle + '" placeholder="Con quien convivis? Contas con familiares o personas de confianza que te acompanen?"></textarea>' +

        '<label style="' + labelStyle + '">Que esperas lograr con el tratamiento?</label>' +
        '<textarea id="admObjetivos" rows="2" style="' + inputStyle + '"></textarea>' +

        '<button onclick="enviarAdmision()" style="width:100%;margin-top:20px;padding:12px;background:#6A5ACD;color:white;border:none;border-radius:8px;font-size:15px;cursor:pointer;font-weight:bold">Enviar informe de admision</button>' +
        '</div>';
}

async function enviarAdmision() {
    const idEsp  = parseInt(document.getElementById("admEspecialidad").value);
    const motivo = document.getElementById("admMotivo").value.trim();
    if (isNaN(idEsp) || idEsp === 0) { toast("Selecciona una especialidad.", "warn"); return; }
    if (motivo === "") { toast("Completa el motivo de consulta.", "warn"); return; }

    const body = {
        idPaciente:                 parseInt(idPaciente),
        idEspecialidad:             idEsp,
        motivoConsulta:             motivo,
        tratamientoAnterior:        document.getElementById("admTratAnterior").value === "true",
        tratamientoAnteriorDetalle: document.getElementById("admTratDetalle").value || null,
        recibeMedicacion:           document.getElementById("admMedicacion").value === "true",
        medicacionDetalle:          document.getElementById("admMedDetalle").value || null,
        gustosIntereses:            document.getElementById("admGustos").value || null,
        preferenciasAtencion:       document.getElementById("admPreferencias").value || null,
        tipoEntrevista:             document.getElementById("admTipoEntrevista").value,
        redApoyo:                   document.getElementById("admRedApoyo").value || null,
        objetivosPaciente:          document.getElementById("admObjetivos").value || null,
        estado:                     "Pendiente"
    };

    try {
        const r = await fetch(API + "/informes-admision", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
        });
        if (r.ok) {
            toast("Informe enviado correctamente. El equipo de admision te contactara.");
            mostrarSeccion("admision");
        } else toast("Error al enviar el informe.", "error");
    } catch(e) { toast("Error de conexion.", "error"); }
}

async function cargarNotificaciones() {
    let notifs = [];
    try { notifs = await (await fetch(API + "/pacientes/" + idPaciente + "/notificaciones")).json(); } catch(e) { return; }
    const noLeidas = notifs.filter(function(n) { return n.leida === false; });
    if (noLeidas.length === 0) return;

    let cont = document.getElementById("notif-container");
    if (cont === null) {
        cont = document.createElement("div");
        cont.id = "notif-container";
        cont.style.cssText = "position:fixed;top:70px;right:20px;z-index:9998;max-width:380px;display:flex;flex-direction:column;gap:10px";
        document.body.appendChild(cont);
    }
    cont.innerHTML = "";

    noLeidas.forEach(function(n) {
        const card = document.createElement("div");
        card.style.cssText = "background:white;border-left:5px solid #0d6efd;border-radius:10px;padding:14px 16px;box-shadow:0 4px 16px rgba(0,0,0,.15)";
        let botones = '<button onclick="marcarLeida(' + n.idNotificacion + ')" style="padding:6px 12px;background:#eee;color:#555;border:none;border-radius:6px;cursor:pointer;font-size:12px">Cerrar</button>';
        if (n.link !== null && n.link !== "") {
            botones = '<button onclick="unirseEntrevista(\'' + n.link + '\', ' + n.idNotificacion + ')" style="padding:6px 14px;background:#0d6efd;color:white;border:none;border-radius:6px;cursor:pointer;font-size:12px;font-weight:bold;margin-right:8px">Unirse a la entrevista</button>' + botones;
        }
        card.innerHTML = '<div style="font-weight:bold;color:#0d6efd;margin-bottom:4px">' + n.titulo + '</div>' +
            '<div style="font-size:13px;color:#444;margin-bottom:10px">' + n.mensaje + '</div>' +
            '<div>' + botones + '</div>';
        cont.appendChild(card);
    });
}

async function marcarLeida(idNotif) {
    try { await fetch(API + "/notificaciones/" + idNotif + "/leida", { method: "PUT" }); } catch(e) {}
    cargarNotificaciones();
    const cont = document.getElementById("notif-container");
    if (cont !== null) cont.innerHTML = "";
    cargarNotificaciones();
}

async function unirseEntrevista(codigoSala, idNotif) {
    try { await fetch(API + "/notificaciones/" + idNotif + "/leida", { method: "PUT" }); } catch(e) {}
    window.location.href = "sesion_video.html?sala=" + encodeURIComponent(codigoSala) + "&idPaciente=" + idPaciente;
}

cargarNotificaciones();

async function confirmarTurnoPaciente(idTurno) {
    if (!await confirmar("Confirmar este turno?")) return;
    try {
        const r = await fetch(API + "/turnos/" + idTurno + "/confirmar-paciente", { method: "PUT" });
        if (r.ok) {
            toast("Turno confirmado. El profesional generara la sala para ese dia.");
            const res = await fetch(API + "/pacientes/" + idPaciente + "/turnos-detalle");
            turnosCalendario = await res.json();
            dibujarCalendario();
            document.getElementById("panelTurno").classList.remove("visible");
        } else if (r.status === 409) {
            // RN-02: falta firmar el consentimiento de telemedicina.
            if (await confirmar("Antes de confirmar el turno tenés que firmar el consentimiento informado para atención por telemedicina. ¿Querés firmarlo ahora?"))
                location.href = "consentimiento.html";
        } else toast("Error al confirmar.", "error");
    } catch(e) { toast("Error de conexion.", "error"); }
}

// Casilla de notificaciones (campanita con historial)
async function initCampanita() {
    if (document.getElementById("campanita-notif") !== null) return;
    const btn = document.createElement("div");
    btn.id = "campanita-notif";
    btn.style.cssText = "position:fixed;top:16px;right:20px;z-index:9997;cursor:pointer;background:white;width:44px;height:44px;border-radius:50%;box-shadow:0 2px 8px rgba(0,0,0,.2);display:flex;align-items:center;justify-content:center;font-size:20px";
    btn.innerHTML = "&#128276;<span id=\"campanita-badge\" style=\"position:absolute;top:-2px;right:-2px;background:#e53935;color:white;border-radius:50%;min-width:18px;height:18px;font-size:11px;display:none;align-items:center;justify-content:center;font-weight:bold;padding:0 4px\"></span>";
    btn.onclick = abrirCasillaNotificaciones;
    document.body.appendChild(btn);
    refrescarBadgeNotif();
}

async function refrescarBadgeNotif() {
    let notifs = [];
    try { notifs = await (await fetch(API + "/pacientes/" + idPaciente + "/notificaciones")).json(); } catch(e) { return; }
    const noLeidas = notifs.filter(function(n) { return n.leida === false; });
    const badge = document.getElementById("campanita-badge");
    if (badge === null) return;
    if (noLeidas.length > 0) { badge.textContent = noLeidas.length; badge.style.display = "flex"; }
    else badge.style.display = "none";
}

async function abrirCasillaNotificaciones() {
    let notifs = [];
    try { notifs = await (await fetch(API + "/pacientes/" + idPaciente + "/notificaciones")).json(); } catch(e) {}

    let overlay = document.getElementById("casilla-overlay");
    if (overlay === null) {
        overlay = document.createElement("div");
        overlay.id = "casilla-overlay";
        overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.4);z-index:9999;display:flex;justify-content:flex-end";
        overlay.onclick = function(e) { if (e.target === overlay) overlay.remove(); };
        document.body.appendChild(overlay);
    }

    const items = notifs.length === 0 ? '<p style="color:#888;padding:20px">No tenes notificaciones.</p>' :
        notifs.map(function(n) {
            const link = (n.link !== null && n.link !== "") ?
                '<button onclick="unirseDesdeCasilla(\'' + n.link + '\', ' + n.idNotificacion + ')" style="margin-top:8px;padding:6px 14px;background:#0d6efd;color:white;border:none;border-radius:6px;cursor:pointer;font-size:12px;font-weight:bold">Unirse / Ver sala</button>' : "";
            return '<div style="background:' + (n.leida ? "#f8f8f8" : "#eef5ff") + ';border-left:4px solid ' + (n.leida ? "#ccc" : "#0d6efd") + ';border-radius:8px;padding:12px 14px;margin-bottom:10px">' +
                '<div style="font-weight:bold;color:#333;margin-bottom:4px">' + n.titulo + '</div>' +
                '<div style="font-size:13px;color:#555">' + n.mensaje + '</div>' +
                '<div style="font-size:11px;color:#aaa;margin-top:4px">' + n.fecha + '</div>' +
                link +
                '</div>';
        }).join("");

    overlay.innerHTML = '<div style="background:white;width:380px;max-width:90%;height:100%;overflow-y:auto;padding:20px;box-shadow:-4px 0 20px rgba(0,0,0,.15)">' +
        '<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">' +
        '<h3 style="margin:0;color:#6A5ACD">Notificaciones</h3>' +
        '<button onclick="document.getElementById(\'casilla-overlay\').remove()" style="background:none;border:none;font-size:22px;cursor:pointer;color:#999">&times;</button>' +
        '</div>' + items + '</div>';

    // marcar todas como leidas
    for (const n of notifs) {
        if (n.leida === false) { try { await fetch(API + "/notificaciones/" + n.idNotificacion + "/leida", { method: "PUT" }); } catch(e) {} }
    }
    refrescarBadgeNotif();
}

async function unirseDesdeCasilla(codigoSala, idNotif) {
    try { await fetch(API + "/notificaciones/" + idNotif + "/leida", { method: "PUT" }); } catch(e) {}
    window.location.href = "sesion_video.html?sala=" + encodeURIComponent(codigoSala) + "&idPaciente=" + idPaciente;
}

initCampanita();
