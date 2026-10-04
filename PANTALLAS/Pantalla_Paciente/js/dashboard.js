const API = YS.API;   // mismo origen que la página (shared/ui.js)

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
// Cada sección del menú (shared/shell.js) se dibuja en #contenido.
function mostrarSeccion(id) {
    const secciones = ["inicio", "agenda", "profesionales", "perfil", "sesion", "admision"];
    if (!secciones.includes(id)) id = "inicio";
    YS.shell.marcar(id);

    const c = document.getElementById("contenido");
    c.innerHTML = YS.cargando();

    if      (id === "inicio")        renderInicio(c);
    else if (id === "agenda")        renderAgenda(c);
    else if (id === "profesionales") renderProfesionales(c);
    else if (id === "perfil")        renderPerfil(c);
    else if (id === "sesion")        c.innerHTML = renderSesion();
    else if (id === "admision")      renderAdmision(c);
}

async function cerrarSesion() {
    await YS.sesion.cerrar();
}

// ── INICIO ─────────────────────────────────────────────────
async function renderInicio(c) {
    YS.shell.usuario.then(u => {
        if (YS.shell.actual === "inicio" && u.nombre)
            YS.shell.encabezado({ titulo: `Hola, ${u.nombre.split(" ")[0]}`, subtitulo: YS.capitalizar(YS.fecha.larga(new Date())) });
    });
    try {
        const res = await fetch(`${API}/pacientes/${idPaciente}/proximo-turno`);
        if (YS.shell.actual !== "inicio") return;   // el usuario ya cambió de sección

        if (res.status === 404) {
            c.innerHTML = `
                <div class="card">
                    <p class="sin-turnos">No tenés turnos próximos.</p>
                </div>`;
            return;
        }

        const t = await res.json();
        const fecha = new Date(t.fecha).toLocaleDateString("es-AR", { weekday:"long", day:"numeric", month:"long", year:"numeric" });
        const hora  = t.hora.substring(0,5);

        c.innerHTML = `
            <div class="card proximo-turno">
                <p class="proximo-etiqueta">Próximo turno</p>
                <div class="proximo-fecha">${fecha}</div>
                <div class="proximo-hora">${hora} hs</div>
                <div class="proximo-datos">
                    <div>
                        <div class="etiqueta">Profesional</div>
                        <div class="valor">${t.nombreProfesional}</div>
                    </div>
                    <div>
                        <div class="etiqueta">Especialidad</div>
                        <div class="valor">${t.especialidad}</div>
                    </div>
                    <div>
                        <div class="etiqueta">Modalidad</div>
                        <div class="valor">${t.modalidad ?? "-"}</div>
                    </div>
                    <div>
                        <div class="etiqueta">Estado</div>
                        <div class="valor"><span class="panel-estado-badge estado-${t.estadoDescripcion}">${t.estadoDescripcion}</span></div>
                    </div>
                </div>
            </div>`;
    } catch {
        c.innerHTML = `<p class="texto-error">Error al cargar los datos.</p>`;
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

    if (YS.shell.actual !== "agenda") return;
    c.innerHTML = `
        ${profesionales.length ? `
        <div class="card">
            <div class="filtro-prof">
                <label for="selProfCalendario">Ver disponibilidad de:</label>
                <select id="selProfCalendario" class="turno-input" onchange="cambiarProfCalendario()">
                    <option value="">— Sin filtro —</option>
                    ${profesionales.map(p => `<option value="${p.idProfesional}">${p.nombre} — ${p.especialidad}</option>`).join("")}
                </select>
                <div id="diasAtencion" class="dias-atencion"></div>
            </div>
        </div>` : ""}

        <div class="card">
            <div class="cal-nav">
                <button onclick="cambiarMes(-1)">&#8249;</button>
                <span class="cal-mes" id="calTitulo"></span>
                <button onclick="cambiarMes(1)">&#8250;</button>
            </div>
            <div class="cal-grid" id="calGrid"></div>
            <div class="cal-leyenda">
                <span class="ui-chip ui-chip--info">Mis turnos</span>
                <span class="ui-chip ui-chip--warn">Pendiente</span>
                <span class="ui-chip ui-chip--ok">Días disponibles del profesional</span>
                <span class="ui-chip ui-chip--neutral">Sin disponibilidad</span>
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
                <span class="panel-estado-badge estado-${t.estadoDescripcion}">${t.estadoDescripcion}</span>
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
                    <div class="form-fila mt-12">
                        <div class="form-grupo form-grupo--ancho">
                            <label>Hora</label>
                            <select id="reprogHora" class="turno-input"></select>
                        </div>
                        <div class="form-grupo form-grupo--ancho">
                            <label>Motivo</label>
                            <input type="text" id="reprogMotivo" class="turno-input" placeholder="Motivo del cambio...">
                        </div>
                    </div>
                    <button class="btn-accion btn-guardar mt-12" onclick="confirmarReprogramar(${t.idTurno})">Confirmar reprogramación</button>
                </div>
                <button class="btn-accion btn-cancelar mt-12" onclick="cancelarModoReprogramar()">Cancelar reprogramación</button>
            </div>` : ""}
            <div class="panel-separador"></div>`;
    }

    const tieneActivo = turnosDia.some(t => t.estadoDescripcion !== "Cancelado" && t.estadoDescripcion !== "Realizado");

    html += !tieneActivo ? `<div class="panel-solicitar-bloque">
        <button class="btn-solicitar-dia" onclick="abrirFormSolicitud('${fechaStr}')">+ Solicitar turno para este día</button>
    </div>
    <div class="form-solicitar-dia" id="formSolicitar">
        <div class="form-fila">
            <div class="form-grupo form-grupo--ancho">
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
        <div class="form-grupo mt-8">
            <label>Motivo (opcional)</label>
            <input type="text" id="solMotivo" class="turno-input" placeholder="Escribí el motivo...">
        </div>
        <button class="btn-accion btn-guardar mt-12" onclick="confirmarSolicitudDia('${fechaStr}')">Confirmar solicitud</button>
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

        if (!lista.length) {
            c.innerHTML = `<div class="card"><p class="sin-turnos">No tenés profesionales asignados aún.</p></div>`;
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

        c.innerHTML = `<div class="prof-lista">${cards}</div>

            <!-- Modal solicitar turno -->
            <div class="modal-overlay-cambio" id="modalTurnoOverlay" onclick="cerrarModalTurno()"></div>
            <div class="modal-cambio" id="modalTurno">
                <div class="modal-cambio-header">
                    <span id="modalTurnoTitulo">Solicitar turno</span>
                    <button class="modal-cerrar-x" onclick="cerrarModalTurno()" aria-label="Cerrar">✕</button>
                </div>
                <div id="modalTurnoBody" class="modal-cuerpo"></div>
                <div class="modal-pie">
                    <button class="btn-mc-cancelar" onclick="cerrarModalTurno()">Cancelar</button>
                    <button class="btn-mc-enviar" onclick="enviarSolicitudTurno()">Solicitar</button>
                </div>
            </div>

            <!-- Modal solicitar cambio -->
            <div class="modal-overlay-cambio" id="modalCambioOverlay" onclick="cerrarModalCambio()"></div>
            <div class="modal-cambio" id="modalCambio">
                <div class="modal-cambio-header">
                    <span id="modalCambioTitulo">Solicitar cambio de profesional</span>
                    <button class="modal-cerrar-x" onclick="cerrarModalCambio()" aria-label="Cerrar">✕</button>
                </div>
                <div id="modalCambioBody" class="modal-cuerpo"></div>
                <div class="modal-pie">
                    <button class="btn-mc-cancelar" onclick="cerrarModalCambio()">Cancelar</button>
                    <button class="btn-mc-enviar" onclick="enviarSolicitudCambio()">Enviar solicitud</button>
                </div>
            </div>`;
    } catch {
        c.innerHTML = `<p class="texto-error">Error al cargar.</p>`;
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
            `<p class="texto-suave">Este profesional no tiene días disponibles en su agenda.</p>`;
        document.getElementById("modalTurnoOverlay").classList.add("visible");
        document.getElementById("modalTurno").classList.add("visible");
        return;
    }

    const diasDisponibles = _agendaProf.map(a => a.dia).join(", ");

    document.getElementById("modalTurnoBody").innerHTML = `
        <p class="texto-suave mb-12">Días disponibles: <strong>${diasDisponibles}</strong></p>
        <div class="modal-campos">
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
                <textarea id="turnoMotivo" rows="2" class="turno-input"></textarea>
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
        <p class="texto-suave mb-12">Tu solicitud será revisada por el equipo. Te avisaremos cuando sea procesada.</p>
        <div>
            <label class="turno-label" for="motivoCambio">Motivo</label>
            <textarea id="motivoCambio" rows="3" class="turno-input" placeholder="Contanos por qué querés cambiar de profesional..."></textarea>
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
        c.innerHTML = `<p class="texto-error">Error al cargar.</p>`;
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

function renderSesion() {
    return '<div class="card sesion-entrada">' +
        '<div class="titulo-tarjeta">Iniciar sesion de video</div>' +
        '<p class="texto-suave mb-12">Ingresa el codigo de sala que te dio tu profesional:</p>' +
        '<input type="text" id="codigoSala" class="turno-input" placeholder="Codigo de sala">' +
        '<button class="btn-primario btn-bloque" onclick="entrarSesion()">Entrar a la sesion</button>' +
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
            c.innerHTML = '<div class="card card--angosta">' +
                '<div class="titulo-tarjeta">Tu informe de admision ya fue enviado</div>' +
                '<p class="mb-12">Estado actual: <span class="panel-estado-badge estado-' + String(inf.estado).replace(/\s+/g, '-') + '">' + inf.estado + '</span></p>' +
                '<p class="texto-suave">Cuando el equipo de admision revise tu informe, te contactaremos para coordinar la primera entrevista.</p>' +
                '</div>';
            return;
        }
    } catch(e) {}

    // No tiene informe: mostrar formulario
    let especialidades = [];
    try { especialidades = await (await fetch(API + "/especialidades")).json(); } catch(e) {}
    const opts = especialidades
        .filter(function(e) { return e.estado === "Activo"; })
        .map(function(e) { return '<option value="' + e.idEspecialidad + '">' + e.nombre + '</option>'; })
        .join("");


    c.innerHTML = '<div class="card card--angosta form-admision">' +
        '<p class="texto-suave">Completa este formulario para que el equipo de admision pueda conocerte y asignarte el profesional adecuado.</p>' +

        '<label class="ui-label">Especialidad solicitada *</label>' +
        '<select id="admEspecialidad" class="campo-texto"><option value="">Selecciona una especialidad</option>' + opts + '</select>' +

        '<label class="ui-label">Motivo de consulta *</label>' +
        '<textarea id="admMotivo" rows="3" class="campo-texto" placeholder="Describe brevemente el motivo por el cual solicitas atencion"></textarea>' +

        '<label class="ui-label">Has recibido tratamiento anteriormente?</label>' +
        '<select id="admTratAnterior" class="campo-texto" onchange="document.getElementById(\'admTratDetalle\').style.display = this.value === \'true\' ? \'block\' : \'none\'"><option value="false">No</option><option value="true">Si</option></select>' +
        '<input type="text" id="admTratDetalle" class="campo-texto" style="display:none" placeholder="Indica cual">' +

        '<label class="ui-label">Actualmente recibes medicacion?</label>' +
        '<select id="admMedicacion" class="campo-texto" onchange="document.getElementById(\'admMedDetalle\').style.display = this.value === \'true\' ? \'block\' : \'none\'"><option value="false">No</option><option value="true">Si</option></select>' +
        '<input type="text" id="admMedDetalle" class="campo-texto" style="display:none" placeholder="Cual?">' +

        '<h4>Conociendote mejor</h4>' +
        '<p class="texto-suave">Estas preguntas nos ayudan a brindarte una atencion personalizada.</p>' +

        '<label class="ui-label">Gustos e intereses</label>' +
        '<textarea id="admGustos" rows="3" class="campo-texto" placeholder="Actividades que disfrutas, hobbies, musica, peliculas o libros, deportes, que te ayuda a relajarte, algo que quieras aprender"></textarea>' +

        '<label class="ui-label">Preferencias durante la atencion</label>' +
        '<textarea id="admPreferencias" rows="3" class="campo-texto" placeholder="Como preferis que se dirijan a vos, algo que te genere ansiedad en una consulta, apoyos que necesites"></textarea>' +

        '<label class="ui-label">Tipo de entrevista preferida</label>' +
        '<select id="admTipoEntrevista" class="campo-texto"><option value="Sin preferencia">Sin preferencia</option><option value="Individual">Individual</option><option value="Familiar">Familiar</option><option value="Grupal">Grupal</option></select>' +

        '<label class="ui-label">Red de apoyo</label>' +
        '<textarea id="admRedApoyo" rows="2" class="campo-texto" placeholder="Con quien convivis? Contas con familiares o personas de confianza que te acompanen?"></textarea>' +

        '<label class="ui-label">Que esperas lograr con el tratamiento?</label>' +
        '<textarea id="admObjetivos" rows="2" class="campo-texto"></textarea>' +

        '<button class="btn-primario btn-bloque" onclick="enviarAdmision()">Enviar informe de admision</button>' +
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
        cont.className = "notif-flotantes";
        document.body.appendChild(cont);
    }
    cont.innerHTML = "";

    noLeidas.forEach(function(n) {
        const card = document.createElement("div");
        card.className = "notif-flotante";
        let botones = '<button class="ui-btn ui-btn--sm" onclick="marcarLeida(' + n.idNotificacion + ')">Cerrar</button>';
        if (n.link !== null && n.link !== "") {
            botones = '<button class="ui-btn ui-btn--sm ui-btn--primary" onclick="unirseEntrevista(\'' + n.link + '\', ' + n.idNotificacion + ')">Unirse a la entrevista</button>' + botones;
        }
        card.innerHTML = '<div class="notif-titulo">' + n.titulo + '</div>' +
            '<div class="notif-mensaje">' + n.mensaje + '</div>' +
            '<div class="notif-botones">' + botones + '</div>';
        cont.appendChild(card);
    });
}

async function marcarLeida(idNotif) {
    try { await fetch(API + "/notificaciones/" + idNotif + "/leida", { method: "PUT" }); } catch(e) {}
    YS.shell.campana.refrescar();
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

// La campanita y su casilla de notificaciones ahora son la campana de la barra superior
// (shared/shell.js): al abrirla marca las notificaciones como leídas, igual que antes.
// Si se leyeron desde ahí, se sacan los avisos flotantes.
document.addEventListener("ys:avisos-leidos", () => document.getElementById("notif-container")?.remove());

YS.shell.iniciar({ rol: "paciente", navegar: id => mostrarSeccion(id) });
