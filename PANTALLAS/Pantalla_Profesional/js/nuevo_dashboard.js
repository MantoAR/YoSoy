const API = 'http://localhost:5000';
const idProfesional = parseInt(localStorage.getItem('idProfesional'));
const idUsuario     = parseInt(localStorage.getItem('idUsuario'));

if (!idProfesional) window.location.href = '/pantallas/index.html';

function activarTab(nombre) {
    document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('activo'));
    document.getElementById('tab-' + nombre).classList.add('activo');
    const c = document.getElementById('contenido');
    c.innerHTML = '<div class="spinner">Cargando...</div>';
    const tabs = { inicio, agenda, pacientes, sesion, perfil }; if (nombre === 'reportes') { reportes(); } else { tabs[nombre](); }
}

function toast(msg, tipo = 'ok') {
    let cont = document.getElementById('toast-container');
    if (!cont) { cont = document.createElement('div'); cont.id = 'toast-container'; document.body.appendChild(cont); }
    const iconos = { ok: '✔', error: '✖', warn: '⚠' };
    const t = document.createElement('div');
    t.className = `toast toast-${tipo}`;
    t.innerHTML = `<span class="toast-icono">${iconos[tipo] ?? 'ℹ'}</span><span>${msg}</span>`;
    cont.appendChild(t);
    setTimeout(() => { t.classList.add('saliendo'); t.addEventListener('animationend', () => t.remove()); }, 3500);
}

async function inicio() {
    const c = document.getElementById('contenido');
    let prof = null;
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}`);
        prof = await r.json();
    } catch {}

    // Turnos de hoy confirmados
    let turnosHoy = [];
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}/turnos-confirmados`);
        const todos = await r.json();
        const hoy = new Date().toLocaleDateString('es-AR', { day:'2-digit', month:'2-digit', year:'numeric' });
        turnosHoy = todos.filter(t => t.fecha === hoy);
    } catch {}

    // Próximo turno
    let turnos = [];
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}/turnos`);
        turnos = await r.json();
    } catch {}

    const ahora = new Date();
    const proximos = turnos
        .filter(t => new Date(t.fecha) >= ahora && t.idEstadoTurno !== 3)
        .sort((a, b) => new Date(a.fecha) - new Date(b.fecha));
    const proximo = proximos[0];

    let pacNombre = '—';
    if (proximo) {
        try {
            const r = await fetch(`${API}/pacientes/${proximo.idPaciente}`);
            const p = await r.json();
            pacNombre = `${p.nombre ?? ''} ${p.apellido ?? ''}`.trim();
        } catch {}
    }

    const fecha = proximo ? new Date(proximo.fecha) : null;
    const hora  = proximo ? (proximo.hora?.substring(0, 5) ?? '—') : null;

    // HTML de sesiones de hoy
    const sesionesHoyHtml = turnosHoy.length ? `
        <div class="titulo-seccion" style="margin-bottom:12px">📅 Sesiones de hoy</div>
        <div class="turnos-lista">
            ${turnosHoy.map(t => `
            <div class="turno-item" style="align-items:flex-start;flex-direction:column;gap:10px;padding:16px">
                <div style="display:flex;align-items:center;gap:12px;width:100%">
                    <div class="turno-fecha-bloque">
                        <div class="turno-fecha-dia" style="font-size:22px">${t.hora}</div>
                        <div class="turno-fecha-mes">hs</div>
                    </div>
                    <div class="turno-info" style="flex:1">
                        <div class="turno-paciente">${t.nombrePaciente}</div>
                        ${t.motivo ? `<div class="turno-meta">${t.motivo}</div>` : ''}
                    </div>
                    ${t.codigoSala
                        ? `<div style="display:flex;flex-direction:column;gap:6px;align-items:flex-end">
                            <span style="background:#d4edda;color:#0f5132;border-radius:20px;padding:3px 12px;font-size:12px;font-weight:600">✅ Sala lista</span>
                            <code style="background:#f0f4ff;color:#185FA5;border-radius:6px;padding:4px 10px;font-size:13px;font-weight:700">${t.codigoSala}</code>
                           </div>`
                        : `<button onclick="generarSalaDesdeInicio(${t.idTurno})" 
                               style="background:linear-gradient(to right,#185FA5,#6A5ACD);color:white;border:none;padding:8px 18px;border-radius:8px;cursor:pointer;font-weight:600;font-size:13px">
                               📹 Iniciar sesión
                           </button>`
                    }
                </div>
                ${t.codigoSala ? `
                <div style="width:100%;display:flex;gap:8px;align-items:center;background:#f0f4ff;border-radius:8px;padding:10px 14px">
                    <span style="font-size:12px;color:#666;flex:1">Compartí este código con el paciente:</span>
                    <code style="font-size:15px;font-weight:700;color:#185FA5;letter-spacing:2px">${t.codigoSala}</code>
                    <button onclick="copiarCodigo('${t.codigoSala}')" 
                        style="background:white;border:1px solid #185FA5;color:#185FA5;padding:5px 12px;border-radius:6px;cursor:pointer;font-size:12px">
                        📋 Copiar
                    </button>
                    <button onclick="activarTab('sesion')"
                        style="background:#185FA5;color:white;border:none;padding:5px 14px;border-radius:6px;cursor:pointer;font-size:12px;font-weight:600">
                        Entrar a la sala →
                    </button>
                </div>` : ''}
            </div>`).join('')}
        </div>` : '';

    c.innerHTML = `
        <div class="titulo-seccion">Bienvenido, ${prof?.nombre ?? 'Profesional'}</div>

        ${sesionesHoyHtml ? `<div class="card" style="margin-bottom:16px;border-left:4px solid #185FA5">${sesionesHoyHtml}</div>` : ''}

        <div class="card" style="margin-bottom:16px">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
                <div class="titulo-seccion" style="margin-bottom:0">Próximo turno</div>
                <button onclick="citaUrgente()" 
                    style="background:linear-gradient(to right,#e53935,#b71c1c);color:white;border:none;padding:8px 16px;border-radius:8px;cursor:pointer;font-size:13px;font-weight:600">
                    ⚡ Cita urgente
                </button>
            </div>
            ${proximo ? `
            <div class="turno-proximo">
                <div class="turno-dato">
                    <span class="etiqueta">Fecha</span>
                    <span class="valor">${fecha.toLocaleDateString('es-AR', { weekday:'long', day:'numeric', month:'long' })}</span>
                </div>
                <div class="turno-dato">
                    <span class="etiqueta">Hora</span>
                    <span class="valor">${hora}</span>
                </div>
                <div class="turno-dato">
                    <span class="etiqueta">Paciente</span>
                    <span class="valor">${pacNombre}</span>
                </div>
            </div>` : '<div class="sin-turnos">No tenés turnos próximos.</div>'}
        </div>

        <div class="card" style="margin-bottom:16px;border-left:4px solid #28a745">
            <div class="titulo-seccion" style="margin-bottom:16px;color:#0f5132">👥 Pacientes hoy</div>
            <div id="pacientes-hoy-contenido"><div class="spinner">Cargando...</div></div>
        </div>

        <div class="card" id="card-pendientes">
            <div class="titulo-seccion" style="margin-bottom:16px">Turnos pendientes de confirmación</div>
            <div id="pendientes-contenido"><div class="spinner">Cargando...</div></div>
        </div>

        <!-- Panel cita urgente -->
        <div id="panel-urgente" style="display:none">
            <div class="card" style="margin-top:16px;border-left:4px solid #e53935">
                <div class="titulo-seccion" style="margin-bottom:12px;color:#b71c1c">⚡ Cita urgente</div>
                <p style="font-size:13px;color:#666;margin-bottom:14px">
                    Seleccioná el paciente, generá la sala al instante y el código le llega por notificación.
                </p>
                <div style="display:flex;gap:12px;flex-wrap:wrap;align-items:flex-end">
                    <div style="flex:1;min-width:200px">
                        <label style="font-size:12px;font-weight:600;color:#6A5ACD;display:block;margin-bottom:4px">Paciente</label>
                        <select id="urgente-paciente" 
                            style="width:100%;padding:10px;border:2px solid #e0d4f5;border-radius:10px;font-size:14px">
                            <option value="">Cargando...</option>
                        </select>
                    </div>
                    <button onclick="confirmarCitaUrgente()"
                        style="background:linear-gradient(to right,#e53935,#b71c1c);color:white;border:none;padding:10px 20px;border-radius:8px;cursor:pointer;font-weight:600;font-size:14px;height:44px">
                        Generar sala y notificar
                    </button>
                    <button onclick="document.getElementById('panel-urgente').style.display='none'"
                        style="background:#eee;color:#555;border:none;padding:10px 16px;border-radius:8px;cursor:pointer;font-size:13px;height:44px">
                        Cancelar
                    </button>
                </div>
                <div id="urgente-resultado" style="margin-top:12px"></div>
            </div>
        </div>
    `;

    // Cargar pendientes
    document.getElementById('pendientes-contenido').innerHTML = await renderTurnosPendientes();
    // Cargar pacientes de hoy
    document.getElementById('pacientes-hoy-contenido').innerHTML = await renderPacientesHoy();


    // Cargar pacientes para cita urgente
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}/pacientes`);
        const pacs = await r.json();
        const sel = document.getElementById('urgente-paciente');
        if (sel) {
            sel.innerHTML = pacs.length
                ? `<option value="">Seleccioná un paciente</option>` + pacs.map(p => `<option value="${p.idPaciente}">${p.nombre ?? ''} ${p.apellido ?? ''}</option>`).join('')
                : `<option value="">Sin pacientes asignados</option>`;
        }
    } catch {}
}

function copiarCodigo(codigo) {
    navigator.clipboard.writeText(codigo).then(() => toast('Código copiado al portapapeles'));
}

async function generarSalaDesdeInicio(idTurno) {
    try {
        const r = await fetch(`${API}/turnos/${idTurno}/generar-sala`, { method: 'PUT' });
        if (r.ok) {
            const data = await r.json();
            toast(`Sala generada: ${data.codigoSala}. El paciente fue notificado.`);
            activarTab('inicio');
        } else toast('Error al generar la sala.', 'error');
    } catch { toast('Error de conexión.', 'error'); }
}

function citaUrgente() {
    const panel = document.getElementById('panel-urgente');
    panel.style.display = panel.style.display === 'none' ? 'block' : 'none';
    panel.scrollIntoView({ behavior: 'smooth' });
}

async function confirmarCitaUrgente() {
    const idPac = parseInt(document.getElementById('urgente-paciente').value);
    if (!idPac) { toast('Seleccioná un paciente.', 'warn'); return; }

    const resultado = document.getElementById('urgente-resultado');
    resultado.innerHTML = '<div class="spinner">Generando sala...</div>';

    try {
        const ahora = new Date();
        const fechaStr = ahora.toISOString().split('T')[0];
        const horaStr = ahora.toTimeString().substring(0, 8);

        const rTurno = await fetch(`${API}/turnos/profesional-solicitar`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                idPaciente: idPac,
                idProfesional: idProfesional,
                fecha: fechaStr,
                hora: horaStr,
                motivo: 'Cita urgente'
            })
        });

        if (!rTurno.ok) { toast('Error al crear el turno urgente.', 'error'); return; }

        const rTurnos = await fetch(`${API}/profesionales/${idProfesional}/turnos`);
        const turnos = await rTurnos.json();
        const turnoUrgente = turnos
            .filter(t => t.idPaciente === idPac)
            .sort((a, b) => b.idTurno - a.idTurno)[0];

        if (!turnoUrgente) { toast('Error al obtener el turno.', 'error'); return; }

        await fetch(`${API}/turnos/${turnoUrgente.idTurno}/confirmar-paciente`, { method: 'PUT' });
        const rSala = await fetch(`${API}/turnos/${turnoUrgente.idTurno}/generar-sala`, { method: 'PUT' });
        const data = await rSala.json();

        toast(`Sala generada: ${data.codigoSala}`);

        // Ir a sesión con el paciente preseleccionado
        document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('activo'));
        document.getElementById('tab-sesion').classList.add('activo');
        sesion(idPac);

    } catch { toast('Error de conexión.', 'error'); resultado.innerHTML = ''; }
}

async function renderTurnosPendientes() {
    let turnos = [];
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}/turnos`);
        turnos = await r.json();
    } catch { return '<div class="sin-turnos">Error al cargar.</div>'; }

    const pendientes = turnos.filter(t => t.idEstadoTurno === 1);
    if (!pendientes.length) return '<div class="sin-turnos">Sin turnos pendientes.</div>';

    const pacientes = {};
    await Promise.all(pendientes.map(async t => {
        try {
            const r = await fetch(`${API}/pacientes/${t.idPaciente}`);
            pacientes[t.idPaciente] = await r.json();
        } catch {}
    }));

    return `<div class="turnos-lista">` + pendientes.map(t => {
        const fecha = new Date(t.fecha);
        const p = pacientes[t.idPaciente];
        return `
        <div class="turno-item">
            <div class="turno-fecha-bloque">
                <div class="turno-fecha-dia">${fecha.getDate()}</div>
                <div class="turno-fecha-mes">${fecha.toLocaleString('es-AR', {month:'short'})}</div>
            </div>
            <div class="turno-info">
                <div class="turno-paciente">${p ? `${p.nombre ?? ''} ${p.apellido ?? ''}`.trim() : 'Paciente #' + t.idPaciente}</div>
                <div class="turno-meta">${t.hora?.substring(0,5) ?? ''}${t.motivo ? ' · ' + t.motivo : ''}</div>
            </div>
            <span class="badge-estado badge-pendiente">Pendiente</span>
            <button class="btn-confirmar" onclick="confirmarTurno(${t.idTurno})">Confirmar</button>
        </div>`;
    }).join('') + `</div>`;
}

async function renderPacientesHoy() {
    let turnos = [];
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}/turnos`);
        turnos = await r.json();
    } catch { return '<div class="sin-turnos">Error al cargar.</div>'; }
    
    const hoy = new Date();
    const hoyStr = `${hoy.getFullYear()}-${String(hoy.getMonth()+1).padStart(2,'0')}-${String(hoy.getDate()).padStart(2,'0')}`;
    const turnosHoy = turnos.filter(t => {
        if (!t.fecha) return false;
        const fechaTurno = t.fecha.substring(0, 10);
        return fechaTurno === hoyStr && t.idEstadoTurno !== 3;

    }).sort((a, b) => (a.hora ?? '').localeCompare(b.hora ?? ''));

    if (!turnosHoy.length) return '<div class="sin-turnos">No tenés pacientes programados para hoy.</div>';

    // Enriquecer con nombre del paciente
    const pacientes = {};
    await Promise.all(turnosHoy.map(async t => {
        try {
            const r = await fetch(`${API}/pacientes/${t.idPaciente}`);
            pacientes[t.idPaciente] = await r.json();
        } catch {}
    }));

    const estadoBadge = {
        1: 'background:#fff3cd;color:#856404',
        2: 'background:#d1e7dd;color:#0f5132',
        4: 'background:#e2e3e5;color:#41464b'
    };
    const estadoLabel = { 1: 'Pendiente', 2: 'Confirmado', 4: 'Realizado' };

    return `
        <div class="tabla-wrapper">
            <table>
                <thead>
                    <tr>
                        <th>Hora</th>
                        <th>Paciente</th>
                        <th>Motivo</th>
                        <th>Estado</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    ${turnosHoy.map(t => {
                        const p = pacientes[t.idPaciente];
                        const nombre = p ? `${p.nombre ?? ''} ${p.apellido ?? ''}`.trim() : 'Paciente #' + t.idPaciente;
                        const hora = t.hora?.substring(0, 5) ?? '—';
                        const estado = estadoLabel[t.idEstadoTurno] ?? '—';
                        const badge = estadoBadge[t.idEstadoTurno] ?? 'background:#eee;color:#555';
                        return `
                        <tr>
                            <td><strong>${hora} hs</strong></td>
                            <td>${nombre}</td>
                            <td>${t.motivo ?? '—'}</td>
                            <td><span style="${badge};border-radius:20px;padding:3px 12px;font-size:12px;font-weight:600">${estado}</span></td>
                            <td>
                                <button onclick="irASala(${t.idTurno}, ${t.idPaciente})"
                                    style="background:linear-gradient(to right,#185FA5,#6A5ACD);color:white;border:none;padding:7px 16px;border-radius:8px;cursor:pointer;font-size:13px;font-weight:600">
                                    📹 Ir a sala
                                </button>
                            </td>
                        </tr>`;
                    }).join('')}
                </tbody>
            </table>
        </div>`;
}

async function irASala(idTurno, idPaciente) {

    //localStorage.setItem('sesion_idPaciente', idPaciente); //con esto le genera la sala directo al paciente

    // Si el turno no tiene sala, generarla primero
    try {
        const r = await fetch(`${API}/turnos/${idTurno}`);
        const turno = await r.json();

        if (!turno.codigoSala) {
            // Confirmar y generar sala
            await fetch(`${API}/turnos/${idTurno}/confirmar-paciente`, { method: 'PUT' });
            const rSala = await fetch(`${API}/turnos/${idTurno}/generar-sala`, { method: 'PUT' });
            if (rSala.ok) {
                const data = await rSala.json();
                toast(`Sala generada: ${data.codigoSala}. El paciente fue notificado.`);
            }
        } else {
            toast(`Sala ya generada: ${turno.codigoSala}`);
        }
    } catch { toast('Error al generar la sala.', 'error'); }


    document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('activo'));
    document.getElementById('tab-sesion').classList.add('activo');
    sesion(idPaciente);
}

async function confirmarTurno(id) {
    try {
        const turno = await (await fetch(`${API}/turnos/${id}`)).json();
        turno.idEstadoTurno = 2;
        const r = await fetch(`${API}/turnos/${id}`, { method: 'PUT', headers: {'Content-Type':'application/json'}, body: JSON.stringify(turno) });
        if (r.ok) { toast('Turno confirmado'); activarTab('inicio'); }
        else toast('Error al confirmar', 'error');
    } catch { toast('Error al confirmar', 'error'); }
}

async function agenda() {
    const c = document.getElementById('contenido');

    let slots = [];
    try { const r = await fetch(`${API}/profesionales/${idProfesional}/agenda`); slots = await r.json(); } catch {}

    const orden = ['Lunes','Martes','Miércoles','Jueves','Viernes','Sábado','Domingo'];
    slots.sort((a, b) => orden.indexOf(a.dia) - orden.indexOf(b.dia));

    c.innerHTML = `
        <div class="titulo-seccion">Mi Agenda</div>

        <div class="card" style="margin-bottom:20px">
            <div class="cal-nav">
                <button onclick="cambiarMesProfesional(-1)">&#8249;</button>
                <span class="cal-mes" id="calTituloProfesional"></span>
                <button onclick="cambiarMesProfesional(1)">&#8250;</button>
            </div>
            <div class="cal-grid" id="calGridProfesional"></div>
        </div>
        <div class="panel-turno-prof" id="panelTurnoProfesional" style="margin-bottom:20px"></div>

        <div class="card">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
                <div class="titulo-seccion" style="margin-bottom:0">Mis horarios de trabajo</div>
                <button class="btn-ver" onclick="abrirFormNuevoHorario()">+ Agregar día</button>
            </div>

            <div id="slots-lista">
                ${!slots.length ? '<div class="sin-turnos">No tenés horarios configurados.</div>' : `
                <div class="tabla-wrapper">
                    <table>
                        <thead><tr><th>Día</th><th>Desde</th><th>Hasta</th><th>Intervalo</th><th>Turnos/día</th><th>Disponible</th><th></th></tr></thead>
                        <tbody>
                            ${slots.map(s => `
                            <tr id="slot-row-${s.idAgenda}">
                                <td>${s.dia}</td>
                                <td>${fmtHora(s.horaInicio)}</td>
                                <td>${fmtHora(s.horaFin)}</td>
                                <td><span class="badge-disponible ${s.disponible ? 'si' : 'no'}" style="font-size:11px">${s.disponible ? 'Sí' : 'No'}</span></td>
                                <td>
                                    <button class="btn-ver" onclick="editarHorario(${s.idAgenda},'${s.dia}','${fmtHora(s.horaInicio)}','${fmtHora(s.horaFin)}',${s.disponible})">Editar</button>
                                    <button class="btn-ver" style="margin-left:6px;background:linear-gradient(to right,#e53935,#b71c1c)" onclick="eliminarHorario(${s.idAgenda})">Eliminar</button>
                                </td>
                            </tr>`).join('')}
                        </tbody>
                    </table>
                </div>`}
            </div>

            <div id="form-horario" style="display:none;margin-top:20px;padding-top:20px;border-top:1px solid #f0eaff;max-width:480px">
                <div style="font-size:14px;font-weight:bold;color:#6A5ACD;margin-bottom:14px" id="form-horario-titulo">Agregar horario</div>
                <input type="hidden" id="fh-id">
                <div style="display:flex;gap:12px;flex-wrap:wrap">
                    <div class="campo" style="flex:1;min-width:140px">
                        <label>Día</label>
                        <select id="fh-dia" style="height:44px;padding:0 14px;border:2px solid #e0d4f5;border-radius:10px;font-size:15px;width:100%;background:white">
                            <option>Lunes</option><option>Martes</option><option>Miércoles</option>
                            <option>Jueves</option><option>Viernes</option><option>Sábado</option><option>Domingo</option>
                        </select>
                    </div>
                    <div class="campo" style="flex:1;min-width:120px">
                        <label>Hora inicio</label>
                        <input type="time" id="fh-inicio" style="height:44px;padding:0 14px;border:2px solid #e0d4f5;border-radius:10px;font-size:15px;width:100%;outline:none">
                    </div>
                    <div class="campo" style="flex:1;min-width:120px">
                        <label>Hora fin</label>
                        <input type="time" id="fh-fin" style="height:44px;padding:0 14px;border:2px solid #e0d4f5;border-radius:10px;font-size:15px;width:100%;outline:none">
                    </div>
                    <div class="campo" style="min-width:120px">
                        <label>Disponible</label>
                        <select id="fh-disponible" style="height:44px;padding:0 14px;border:2px solid #e0d4f5;border-radius:10px;font-size:15px;width:100%;background:white">
                            <option value="true">Sí</option>
                            <option value="false">No</option>
                        </select>
                    </div>

                    <div class="campo" style="flex:1;min-width:140px">
                        <label>Intervalo de atención</label>
                        <select id="fh-intervalo" style="height:44px;padding:0 14px;border:2px solid #e0d4f5;border-radius:10px;font-size:15px;width:100%;background:white" onchange="actualizarPreviewNuevo()">
                            <option value="15">15 minutos</option>
                            <option value="20">20 minutos</option>
                            <option value="30" selected>30 minutos</option>
                            <option value="45">45 minutos</option>
                            <option value="60">60 minutos</option>
                            <option value="90">90 minutos</option>
                        
                        </select>
                    </div>

                <div id="fh-preview" style="font-size:13px;color:#6A5ACD;margin-top:8px"></div>
                <div style="display:flex;gap:10px;margin-top:14px">
                    <button class="btn-guardar-perfil" style="flex:1" onclick="guardarHorario()">Guardar</button>
                    <button style="flex:1;height:48px;border:2px solid #e0d4f5;border-radius:10px;background:white;color:#555;font-size:15px;cursor:pointer" onclick="cerrarFormHorario()">Cancelar</button>
                </div>
                <div id="fh-msg" style="font-size:13px;margin-top:8px"></div>
            </div>
        </div>
    `;

    await renderCalendario();

    setTimeout(() => {
        ['fh-inicio','fh-fin','fh-intervalo'].forEach(id => {
            document.getElementById(id)?.addEventListener('change', actualizarPreviewNuevo);
        });
    }, 100);
}

function abrirFormNuevoHorario() {
    document.getElementById('fh-id').value = '';
    document.getElementById('fh-dia').value = 'Lunes';
    document.getElementById('fh-inicio').value = '';
    document.getElementById('fh-fin').value = '';
    document.getElementById('fh-intervalo').value = '30';
    document.getElementById('fh-disponible').value = 'true';
    document.getElementById('form-horario-titulo').textContent = 'Agregar horario';
    document.getElementById('fh-msg').textContent = '';
    document.getElementById('fh-preview').textContent = '';
    document.getElementById('form-horario').style.display = 'block';
    document.getElementById('form-horario').scrollIntoView({ behavior: 'smooth' });
}

function editarHorario(id, dia, inicio, fin, disponible, intervalo = 30) {
    document.getElementById('fh-id').value = id;
    document.getElementById('fh-dia').value = dia;
    document.getElementById('fh-inicio').value = inicio;
    document.getElementById('fh-fin').value = fin;
    document.getElementById('fh-disponible').value = disponible ? 'true' : 'false';
    document.getElementById('fh-intervalo').value = intervalo;
    document.getElementById('form-horario-titulo').textContent = 'Editar horario';
    document.getElementById('fh-msg').textContent = '';
    document.getElementById('form-horario').style.display = 'block';
    document.getElementById('form-horario').scrollIntoView({ behavior: 'smooth' });
    actualizarPreviewNuevo();
}

function cerrarFormHorario() {
    document.getElementById('form-horario').style.display = 'none';
}

async function guardarHorario() {
    const msg      = document.getElementById('fh-msg');
    const id       = document.getElementById('fh-id').value;
    const dia      = document.getElementById('fh-dia').value;
    const inicio   = document.getElementById('fh-inicio').value;
    const fin      = document.getElementById('fh-fin').value;
    const disp     = document.getElementById('fh-disponible').value === 'true';
    const intervalo = parseInt(document.getElementById('fh-intervalo')?.value ?? 30);

    if (!inicio || !fin) { msg.style.color='#e74c3c'; msg.textContent='Completá hora inicio y fin.'; return; }
    if (inicio >= fin)   { msg.style.color='#e74c3c'; msg.textContent='La hora inicio debe ser menor a la hora fin.'; return; }

    const body = {
        idAgenda: id ? parseInt(id) : 0,
        idProfesional,
        dia,
        horaInicio: inicio + ':00',
        horaFin: fin + ':00',
        disponible: disp,
        intervaloMinutos: intervalo
    };

    const url    = id ? `${API}/agendas/${id}` : `${API}/agendas`;
    const method = id ? 'PUT' : 'POST';

    try {
        const r = await fetch(url, { method, headers: {'Content-Type':'application/json'}, body: JSON.stringify(body) });
        if (r.ok) { toast(id ? 'Horario actualizado' : 'Horario agregado'); activarTab('agenda'); }
        else { msg.style.color='#e74c3c'; msg.textContent='Error al guardar.'; }
    } catch { msg.style.color='#e74c3c'; msg.textContent='Error de conexión.'; }
}

async function eliminarHorario(id) {
    const ok = await confirmar('¿Eliminar este horario?');
    if (!ok) return;
    try {
        const r = await fetch(`${API}/agendas/${id}`, { method:'DELETE' });
        if (r.ok) { toast('Horario eliminado', 'warn'); activarTab('agenda'); }
        else toast('Error al eliminar', 'error');
    } catch { toast('Error de conexión', 'error'); }
}

async function responderTurno(id, aceptar) {
    try {
        const turno = await (await fetch(`${API}/turnos/${id}`)).json();
        turno.idEstadoTurno = aceptar ? 2 : 3;
        const r = await fetch(`${API}/turnos/${id}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body:JSON.stringify(turno) });
        if (r.ok) { toast(aceptar ? 'Turno aceptado' : 'Turno rechazado', aceptar ? 'ok' : 'warn'); activarTab('agenda'); }
        else toast('Error al responder', 'error');
    } catch { toast('Error de conexión', 'error'); }
}

async function crearTurnoProfesional() {
    const msg = document.getElementById('nt-msg');
    const idPac = document.getElementById('nt-paciente').value;
    const fecha = document.getElementById('nt-fecha').value;
    const hora  = document.getElementById('nt-hora').value;
    const modalidad = document.getElementById('nt-modalidad').value;
    const motivo    = document.getElementById('nt-motivo').value;

    if (!idPac || !fecha || !hora) { msg.style.color='#e74c3c'; msg.textContent='Completá paciente, fecha y hora.'; return; }

    try {
        const r = await fetch(`${API}/turnos/profesional-solicitar`, {
            method: 'POST',
            headers: {'Content-Type':'application/json'},
            body: JSON.stringify({ idPaciente:parseInt(idPac), idProfesional, fecha, hora:hora+':00', modalidad, motivo: motivo||null })
        });
        if (r.ok) { toast('Turno enviado al paciente'); activarTab('agenda'); }
        else { msg.style.color='#e74c3c'; msg.textContent='Error al crear el turno.'; }
    } catch { msg.style.color='#e74c3c'; msg.textContent='Error de conexión.'; }
}

async function renderTodosLosTurnos() {
    let turnos = [];
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}/turnos`);
        turnos = await r.json();
    } catch { return '<div class="sin-turnos">Error al cargar.</div>'; }

    const futuros = turnos
        .filter(t => new Date(t.fecha) >= new Date() && t.idEstadoTurno !== 3)
        .sort((a, b) => new Date(a.fecha) - new Date(b.fecha));

    if (!futuros.length) return '<div class="sin-turnos">Sin turnos próximos.</div>';

    const pacientes = {};
    await Promise.all(futuros.map(async t => {
        try { const r = await fetch(`${API}/pacientes/${t.idPaciente}`); pacientes[t.idPaciente] = await r.json(); } catch {}
    }));

    const estadoBadge = { 1:'badge-pendiente', 2:'badge-confirmado', 3:'badge-cancelado', 4:'badge-realizado' };
    const estadoLabel = { 1:'Pendiente', 2:'Confirmado', 3:'Cancelado', 4:'Realizado' };

    return `<div class="turnos-lista">` + futuros.map(t => {
        const fecha = new Date(t.fecha);
        const p = pacientes[t.idPaciente];
        return `
        <div class="turno-item">
            <div class="turno-fecha-bloque">
                <div class="turno-fecha-dia">${fecha.getDate()}</div>
                <div class="turno-fecha-mes">${fecha.toLocaleString('es-AR', {month:'short'})}</div>
            </div>
            <div class="turno-info">
                <div class="turno-paciente">${p ? `${p.nombre ?? ''} ${p.apellido ?? ''}`.trim() : 'Paciente #' + t.idPaciente}</div>
                <div class="turno-meta">${t.hora?.substring(0,5) ?? ''}${t.motivo ? ' · ' + t.motivo : ''}</div>
            </div>
            <span class="badge-estado ${estadoBadge[t.idEstadoTurno] ?? ''}">${estadoLabel[t.idEstadoTurno] ?? '—'}</span>
        </div>`;
    }).join('') + `</div>`;
}

function fmtHora(h) {
    if (!h) return '—';
    return h.substring(0, 5);
}

async function pacientes() {
    const c = document.getElementById('contenido');
    let lista = [];
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}/pacientes`);
        lista = await r.json();
    } catch {}

    c.innerHTML = `
        <div class="titulo-seccion">Mis Pacientes</div>
        <div class="card">
            ${!lista.length ? '<div class="sin-turnos">No tenés pacientes asignados.</div>' : `
            <div class="tabla-wrapper">
                <table>
                    <thead><tr>
                        <th>Nombre</th>
                        <th>DNI</th>
                        <th>Mail</th>
                        <th>Teléfono</th>
                        <th></th>
                    </tr></thead>
                    <tbody>
                        ${lista.map(p => `
                        <tr>
                            <td>${[p.nombre, p.apellido].filter(Boolean).join(' ')}</td>
                            <td>${p.dni ?? '—'}</td>
                            <td>${p.mail ?? '—'}</td>
                            <td>${p.telefono ?? '—'}</td>
                            <td>
                                <button class="btn-ver" onclick="verHistoria(${p.idPaciente}, '${escHtml([p.nombre, p.apellido].filter(Boolean).join(' '))}')">Historia</button>
                                <button class="btn-sesion" onclick="activarTab('sesion')">Sesión</button>
                            </td>
                        </tr>`).join('')}
                    </tbody>
                </table>
            </div>`}
        </div>

        <div class="modal-overlay" id="modalHistoria">
            <div class="modal-box">
                <div class="modal-header">
                    <span class="modal-titulo" id="modalTitulo">Historia Clínica</span>
                    <button class="modal-cerrar" onclick="cerrarModal()">✕</button>
                </div>
                <div class="modal-body" id="modalBody"></div>
            </div>
        </div>
    `;
}

function escHtml(s) { return s.replace(/'/g, "\\'"); }

function cerrarModal() {
    document.getElementById('modalHistoria')?.classList.remove('visible');
}

async function verHistoria(idPaciente, nombrePaciente) {
    document.getElementById('modalTitulo').textContent = `Historia Clínica — ${nombrePaciente}`;
    document.getElementById('modalBody').innerHTML = '<div class="spinner">Cargando...</div>';
    document.getElementById('modalHistoria').classList.add('visible');

        let paciente = null, historia = null, evoluciones = [], sesiones = [];
    try { paciente = await (await fetch(`${API}/pacientes/${idPaciente}`)).json(); } catch {}
    try { historia = await (await fetch(`${API}/pacientes/${idPaciente}/historiaclinica`)).json(); } catch {}
    if (historia?.idHistoria) {
        try { evoluciones = await (await fetch(`${API}/historiasclinicas/${historia.idHistoria}/evoluciones`)).json(); } catch {}
    }
    try { sesiones = await (await fetch(`${API}/pacientes/${idPaciente}/sesiones-clinicas`)).json(); } catch {}

    document.getElementById('modalBody').innerHTML = `
        <div class="modal-tabs">
            <button class="modal-tab activo" id="mtab-datos" onclick="modalTab('datos')">Datos del paciente</button>
            <button class="modal-tab" id="mtab-evoluciones" onclick="modalTab('evoluciones')">Evoluciones</button>
            <button class="modal-tab" id="mtab-sesiones" onclick="modalTab('sesiones')">Sesiones clínicas</button>
        </div>
        <div id="mpanel-datos">${renderDatosPaciente(paciente)}</div>
        <div id="mpanel-evoluciones" style="display:none">${renderEvoluciones(evoluciones)}</div>
        <div id="mpanel-sesiones" style="display:none">${renderSesionesClinicas(sesiones)}</div>
    `;
}

function modalTab(nombre) {
    ['datos','evoluciones','sesiones'].forEach(t => {
        document.getElementById('mtab-' + t)?.classList.toggle('activo', t === nombre);
        const p = document.getElementById('mpanel-' + t);
        if (p) p.style.display = t === nombre ? 'block' : 'none';
    });
}


function renderSesionesClinicas(lista) {
    return lista.map(s => {
        let emociones = '';
        try {
            const emo = JSON.parse(s.historialEmociones || '{}');
            emociones = Object.entries(emo).map(([k,v]) =>
                '<span style="display:inline-block;background:#f0f0f0;border-radius:6px;padding:3px 10px;font-size:12px;margin:2px">' + k + ': <strong>' + v + '%</strong></span>'
            ).join('');
        } catch {}
        return '<div class="evolucion-item">' +
            '<div class="evolucion-fecha">' +
                new Date(s.fechaHora).toLocaleDateString('es-AR', {weekday:'long',day:'numeric',month:'long',year:'numeric'}) +
                ' &nbsp;·&nbsp; ' + new Date(s.fechaHora).toLocaleTimeString('es-AR',{hour:'2-digit',minute:'2-digit'}) +
                (s.tipoCierre ? ' &nbsp;<span style="background:#e8d5ff;color:#6A5ACD;border-radius:10px;padding:2px 10px;font-size:11px;font-weight:600">' + s.tipoCierre + '</span>' : '') +
            '</div>' +
            (emociones ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Promedio de emociones</div><div style="margin-top:4px">' + emociones + '</div></div>' : '') +
            (s.transcripcion ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Transcripción</div><div class="evolucion-bloque-texto" style="white-space:pre-line;max-height:180px;overflow-y:auto;font-size:12px">' + s.transcripcion + '</div></div>' : '') +
            (s.notasEvolucion ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Notas del médico</div><div class="evolucion-bloque-texto">' + s.notasEvolucion + '</div></div>' : '') +
        '</div>';
    }).join('');
}

function renderDatosPaciente(p) {
    if (!p) return '<div class="sin-datos">Sin datos.</div>';
    return `
        <div class="dato-fila">
            <div class="dato-item"><span class="dato-label">Nombre</span><span class="dato-valor">${[p.nombre, p.apellido].filter(Boolean).join(' ') || '—'}</span></div>
            <div class="dato-item"><span class="dato-label">DNI</span><span class="dato-valor">${p.dni ?? '—'}</span></div>
        </div>
        <div class="dato-fila">
            <div class="dato-item"><span class="dato-label">Fecha de nacimiento</span><span class="dato-valor">${p.fechaNacimiento ? new Date(p.fechaNacimiento).toLocaleDateString('es-AR') : '—'}</span></div>
            <div class="dato-item"><span class="dato-label">Estado</span><span class="dato-valor">${p.estado ?? '—'}</span></div>
        </div>
        <div class="dato-fila">
            <div class="dato-item"><span class="dato-label">Mail</span><span class="dato-valor">${p.mail ?? '—'}</span></div>
            <div class="dato-item"><span class="dato-label">Teléfono</span><span class="dato-valor">${p.telefono ?? '—'}</span></div>
        </div>
        <div class="dato-fila">
            <div class="dato-item"><span class="dato-label">Domicilio</span><span class="dato-valor">${p.domicilio ?? '—'}</span></div>
        </div>
    `;
}

function renderEvoluciones(lista) {
    if (!lista?.length) return '<div class="sin-datos">Sin evoluciones registradas.</div>';
    return lista.map(e => `
        <div class="evolucion-item">
            <div class="evolucion-fecha">${new Date(e.fecha).toLocaleDateString('es-AR', { weekday:'long', day:'numeric', month:'long', year:'numeric' })}</div>
            ${e.diagnostico ? `<div class="evolucion-bloque"><div class="evolucion-bloque-label">Diagnóstico</div><div class="evolucion-bloque-texto">${e.diagnostico}</div></div>` : ''}
            ${e.observaciones ? `<div class="evolucion-bloque"><div class="evolucion-bloque-label">Observaciones / Transcripción IA</div><div class="evolucion-bloque-texto">${e.observaciones}</div></div>` : ''}
            ${e.planTratamiento ? `<div class="evolucion-bloque"><div class="evolucion-bloque-label">Emociones / Plan</div><div class="evolucion-bloque-texto">${e.planTratamiento}</div></div>` : ''}
        </div>
    `).join('');
}

function sesion(idPaciente = null) {
    const src = idPaciente 
        ? `sesion_video.html?idPaciente=${idPaciente}` 
        : 'sesion_video.html';
    document.getElementById('contenido').innerHTML = `
        <div class="titulo-seccion">Sesión de video</div>
        <div class="sesion-iframe-wrapper">
            <iframe src="${src}" allow="camera; microphone; display-capture"></iframe>
        </div>
    `;
}


async function perfil() {
    const c = document.getElementById('contenido');
    let prof = null, especialidades = [];
    try { prof = await (await fetch(`${API}/profesionales/${idProfesional}`)).json(); } catch {}
    try { especialidades = await (await fetch(`${API}/especialidades`)).json(); } catch {}

    const fn = prof?.fechaNacimiento ? prof.fechaNacimiento.substring(0, 10) : '';

    c.innerHTML = `
        <div class="titulo-seccion">Mi Perfil</div>
        <div class="card">
            <div class="perfil-form">
                <div class="campo"><label>Nombre</label><input id="pf-nombre" value="${prof?.nombre ?? ''}"></div>
                <div class="campo"><label>Apellido</label><input id="pf-apellido" value="${prof?.apellido ?? ''}"></div>
                <div class="campo"><label>Segundo nombre</label><input id="pf-segundoNombre" value="${prof?.segundoNombre ?? ''}"></div>
                <div class="campo"><label>DNI</label><input id="pf-dni" value="${prof?.dni ?? ''}"></div>
                <div class="campo"><label>Fecha de nacimiento</label><input type="date" id="pf-fechaNacimiento" value="${fn}"></div>
                <div class="campo"><label>Mail</label><input id="pf-mail" value="${prof?.mail ?? ''}"></div>
                <div class="campo"><label>Teléfono</label><input id="pf-telefono" value="${prof?.telefono ?? ''}"></div>
                <div class="campo"><label>Provincia</label><input id="pf-provincia" value="${prof?.provincia ?? ''}"></div>
                <div class="campo"><label>Matrícula</label><input id="pf-matricula" value="${prof?.matricula ?? ''}" disabled></div>
                <div class="campo">
                    <label>Especialidad</label>
                    <select id="pf-especialidad">
                        ${especialidades.map(e => `<option value="${e.idEspecialidad}" ${e.idEspecialidad === prof?.idEspecialidad ? 'selected' : ''}>${e.nombre ?? e.descripcion ?? e.idEspecialidad}</option>`).join('')}
                    </select>
                </div>
                <button class="btn-guardar-perfil" onclick="guardarPerfil()">Guardar cambios</button>
                <div class="msg-ok" id="msg-perfil-ok">Perfil actualizado correctamente.</div>
            </div>
        </div>
    `;
    window._profActual = prof;
}

async function guardarPerfil() {
    const prof = {
        ...window._profActual,
        nombre:          document.getElementById('pf-nombre').value,
        apellido:        document.getElementById('pf-apellido').value,
        segundoNombre:   document.getElementById('pf-segundoNombre').value,
        dni:             document.getElementById('pf-dni').value,
        fechaNacimiento: document.getElementById('pf-fechaNacimiento').value || null,
        mail:            document.getElementById('pf-mail').value,
        telefono:        document.getElementById('pf-telefono').value,
        provincia:       document.getElementById('pf-provincia').value,
        idEspecialidad:  parseInt(document.getElementById('pf-especialidad').value),
    };
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(prof)
        });
        if (r.ok) {
            const ok = document.getElementById('msg-perfil-ok');
            ok.style.display = 'block';
            setTimeout(() => ok.style.display = 'none', 3000);
            toast('Perfil actualizado');
        } else { toast('Error al guardar', 'error'); }
    } catch { toast('Error de conexión', 'error'); }
}

async function responderTurnoDesdeCal(id, aceptar) {
    try {
        const turno = await (await fetch(`${API}/turnos/${id}`)).json();
        turno.idEstadoTurno = aceptar ? 2 : 3;
        const r = await fetch(`${API}/turnos/${id}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body:JSON.stringify(turno) });
        if (r.ok) { toast(aceptar ? 'Turno aceptado' : 'Turno rechazado', aceptar ? 'ok' : 'warn'); await renderCalendario(); mostrarPanelDiaProfesional(_diaSeleccionadoProf); }
        else toast('Error', 'error');
    } catch { toast('Error de conexión', 'error'); }
}

let _diaSeleccionadoProf = null;

let _diaAgendaDisponible = false;

async function cerrarSesion() {
    const confirmado = await confirmar('¿Cerrar sesión?');
    if (!confirmado) return;
    localStorage.clear();
    window.location.href = '/pantallas/index.html';
}

function confirmar(msg) {
    return new Promise(resolve => {
        const overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.45);z-index:9999;display:flex;align-items:center;justify-content:center';
        overlay.innerHTML = `
            <div style="background:white;border-radius:16px;padding:32px 28px;max-width:340px;width:90%;text-align:center">
                <p style="font-size:15px;color:#444;margin-bottom:20px">${msg}</p>
                <div style="display:flex;gap:10px;justify-content:center">
                    <button id="conf-si" style="background:#e53935;color:white;border:none;padding:10px 24px;border-radius:8px;cursor:pointer;font-size:14px;font-weight:bold">Sí</button>
                    <button id="conf-no" style="background:#eee;color:#555;border:none;padding:10px 24px;border-radius:8px;cursor:pointer;font-size:14px">No</button>
                </div>
            </div>`;
        document.body.appendChild(overlay);
        overlay.querySelector('#conf-si').onclick = () => { overlay.remove(); resolve(true); };
        overlay.querySelector('#conf-no').onclick = () => { overlay.remove(); resolve(false); };
    });
}

const MESES      = ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];
const DIAS       = ["Lun","Mar","Mié","Jue","Vie","Sáb","Dom"];
const DIA_DB_NUM = { "lunes":1,"martes":2,"miercoles":3,"jueves":4,"viernes":5,"sabado":6,"domingo":0 };
function normalizarP(s) { return (s ?? "").toLowerCase().normalize("NFD").replace(/\p{Diacritic}/gu,""); }
let calAnio = new Date().getFullYear();
let calMes  = new Date().getMonth();
let turnosCalendarioProfesional = [];
let _pacientesCalMap = {};
let _modoReprogramarProf = false;
let _idTurnoReprogProf   = null;
let _reprogFechaSeleccionadaProf = null;
let _agendaReprogProf    = [];

async function renderCalendario() {
    try {
        const r = await fetch(`${API}/profesionales/${idProfesional}/turnos`);
        turnosCalendarioProfesional = await r.json();
    } catch { turnosCalendarioProfesional = []; }
    await dibujarCalendarioProfesional();
}

async function cambiarMesProfesional(delta) {
    calMes += delta;
    if (calMes > 11) { calMes = 0; calAnio++; }
    if (calMes < 0)  { calMes = 11; calAnio--; }
    if (!_modoReprogramarProf)
        document.getElementById('panelTurnoProfesional')?.classList.remove('visible');
    await dibujarCalendarioProfesional();
}

async function dibujarCalendarioProfesional() {
    const titulo = document.getElementById('calTituloProfesional');
    const grid   = document.getElementById('calGridProfesional');
    if (!titulo || !grid) return;

    titulo.textContent = `${MESES[calMes]} ${calAnio}`;
    const hoy = new Date();
    const primerDia  = new Date(calAnio, calMes, 1);
    const diasEnMes  = new Date(calAnio, calMes + 1, 0).getDate();
    const inicioCelda = (primerDia.getDay() + 6) % 7;

    const turnosPorDia = {};
    turnosCalendarioProfesional.forEach(t => {
        const f = new Date(t.fecha);
        if (f.getFullYear() === calAnio && f.getMonth() === calMes) {
            const d = f.getDate();
            if (!turnosPorDia[d]) turnosPorDia[d] = [];
            turnosPorDia[d].push(t);
        }
    });

    const autoconfirmarPromesas = [];

    let html = DIAS.map(d => `<div class="cal-header">${d}</div>`).join('');
    for (let i = 0; i < inicioCelda; i++) html += `<div class="cal-dia vacio"></div>`;

    for (let dia = 1; dia <= diasEnMes; dia++) {
        const tieneT        = !!turnosPorDia[dia];
        const esHoy         = dia === hoy.getDate() && calMes === hoy.getMonth() && calAnio === hoy.getFullYear();
        const esPasado      = new Date(calAnio, calMes, dia) < new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate());
        const fechaD        = new Date(calAnio, calMes, dia);
        const esSeleccionado = _reprogFechaSeleccionadaProf &&
            fechaD.toDateString() === _reprogFechaSeleccionadaProf.toDateString();

        let clases, onclick;
        if (_modoReprogramarProf) {
            const enAgenda = _agendaReprogProf.some(a => DIA_DB_NUM[normalizarP(a.dia)] === fechaD.getDay());
            const ocupado  = tieneT && turnosPorDia[dia].some(t => t.idEstadoTurno !== 3 && t.idEstadoTurno !== 4);
            const valido   = !esPasado && enAgenda && !ocupado;
            clases  = ['cal-dia', esPasado ? 'pasado' : valido ? 'reprog-valido' : 'reprog-invalido', esSeleccionado ? 'reprog-seleccionado' : ''].join(' ').trim();
            onclick = valido ? `onclick="seleccionarDiaReprogProf(${dia})"` : '';
        } else {
            const soloCancelado = tieneT && turnosPorDia[dia].every(t => t.idEstadoTurno === 3);
            const mePidieron    = tieneT && turnosPorDia[dia].some(t => t.idEstadoTurno === 1 && t.creadoPor === 'paciente');
            const yoPedi        = tieneT && turnosPorDia[dia].some(t => t.idEstadoTurno === 1 && t.creadoPor === 'profesional');

            if (mePidieron && yoPedi) {
                turnosPorDia[dia].filter(t => t.idEstadoTurno === 1).forEach(t => {
                    autoconfirmarPromesas.push(
                        fetch(`${API}/turnos/${t.idTurno}`).then(r => r.json()).then(turno => {
                            turno.idEstadoTurno = 2;
                            return fetch(`${API}/turnos/${t.idTurno}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body:JSON.stringify(turno) });
                        }).catch(() => {})
                    );
                });
            }

            const claseT = tieneT
                ? (soloCancelado ? 'con-turno-cancelado' : (mePidieron && !yoPedi) ? 'con-turno-pendiente-ellos' : (yoPedi && !mePidieron) ? 'con-turno-pendiente-yo' : 'con-turno')
                : (esPasado ? 'pasado' : 'futuro');
            clases  = ['cal-dia', claseT, esHoy ? 'hoy' : ''].join(' ').trim();
            onclick = !esPasado || tieneT ? `onclick="mostrarPanelDiaProfesional(${dia})"` : '';
        }
        html += `<div class="${clases}" ${onclick}>${dia}</div>`;
    }
    grid.innerHTML = html;

    if (autoconfirmarPromesas.length) {
        await Promise.all(autoconfirmarPromesas);
        await renderCalendario();
    }
}

async function mostrarPanelDiaProfesional(dia) {
    _diaSeleccionadoProf = dia;
    document.querySelectorAll('.cal-dia').forEach(el => el.classList.remove('seleccionado'));
    document.querySelectorAll('.cal-dia').forEach(el => {
        if (el.textContent.trim() === String(dia)) el.classList.add('seleccionado');
    });

    const turnosDia = turnosCalendarioProfesional.filter(t => {
        const f = new Date(t.fecha);
        return f.getFullYear() === calAnio && f.getMonth() === calMes && f.getDate() === dia;
    });

    const panel = document.getElementById('panelTurnoProfesional');
    panel.classList.add('visible');

    await Promise.all(turnosDia.map(async t => {
        if (!_pacientesCalMap[t.idPaciente]) {
            try { _pacientesCalMap[t.idPaciente] = await (await fetch(`${API}/pacientes/${t.idPaciente}`)).json(); } catch {}
        }
    }));

    const estadoLabel = { 1:'Pendiente', 2:'Confirmado', 3:'Cancelado', 4:'Realizado' };
    const estadoColor = { 1:'#fff3cd|#856404', 2:'#d1e7dd|#0f5132', 3:'#f8d7da|#842029', 4:'#e2e3e5|#41464b' };
    const fechaStr    = `${calAnio}-${String(calMes+1).padStart(2,'0')}-${String(dia).padStart(2,'0')}`;
    const tieneActivo = turnosDia.some(t => t.idEstadoTurno !== 3 && t.idEstadoTurno !== 4);

    const turnosHtml = turnosDia.map(t => {
        const p = _pacientesCalMap[t.idPaciente];
        const [bg, fg] = (estadoColor[t.idEstadoTurno] ?? '#e2e3e5|#41464b').split('|');
        const nombre = p ? [p.nombre, p.apellido].filter(Boolean).join(' ') : 'Paciente #' + t.idPaciente;
        const puedeAccionar = t.idEstadoTurno !== 3 && t.idEstadoTurno !== 4;
        const esPendientePaciente = t.idEstadoTurno === 1 && t.creadoPor === 'paciente';
        return `
        <div class="panel-top${t.idEstadoTurno === 3 ? ' panel-cancelado' : ''}">
            <div class="panel-hora-bloque">
                <div class="panel-hora">${fmtHora(t.hora)}</div>
                <div class="panel-hora-label">hs</div>
            </div>
            <div class="panel-info-principal">
                <div class="panel-profesional">${nombre}</div>
            </div>
            <span class="panel-estado-badge" style="background:${bg};color:${fg}">${estadoLabel[t.idEstadoTurno] ?? '—'}</span>
        </div>
        ${t.motivo ? `<div class="panel-chips"><div class="chip chip-wide"><span class="chip-label">Motivo</span><span class="chip-val">${t.motivo}</span></div></div>` : ''}
        ${esPendientePaciente ? `
            <div class="panel-acciones">
                <button class="btn-accion btn-guardar" onclick="responderTurnoDesdeCal(${t.idTurno}, true)">Aceptar</button>
                <button class="btn-ver" style="background:linear-gradient(to right,#e53935,#b71c1c)" onclick="responderTurnoDesdeCal(${t.idTurno}, false)">Rechazar</button>
            </div>` : ''}
        ${puedeAccionar && !esPendientePaciente ? `
            <div class="panel-acciones">
                <button class="btn-accion btn-cancelar" onclick="cancelarTurnoProf(${t.idTurno})">Cancelar turno</button>
                <button class="btn-accion btn-reprogramar" id="btnReprogramarProf" onclick="abrirFormReprogramarProf(${t.idTurno})">Reprogramar</button>
            </div>
            <div class="form-reprogramar" id="formReprogramarProf">
                <p class="reprog-instruccion">Seleccioná el nuevo día en el calendario</p>
                <div id="reprogDetallesProf" style="display:none">
                    <div class="reprog-dia-seleccionado" id="reprogDiaLabelProf"></div>
                    <div class="form-fila" style="margin-top:12px">
                        <div class="form-grupo" style="flex:1">
                            <label>Hora</label>
                            <select id="reprogHoraProf" class="turno-input"></select>
                        </div>
                        <div class="form-grupo" style="flex:1">
                            <label>Motivo</label>
                            <input type="text" id="reprogMotivoProf" class="turno-input" placeholder="Motivo del cambio...">
                        </div>
                    </div>
                    <button class="btn-accion btn-guardar" style="margin-top:12px" onclick="confirmarReprogramarProf(${t.idTurno})">Confirmar reprogramación</button>
                </div>
                <button class="btn-accion btn-cancelar" style="margin-top:10px" onclick="cancelarModoReprogramarProf()">Cancelar reprogramación</button>
            </div>` : ''}
        <div class="panel-separador"></div>`;
    }).join('');

    let agendaProf = [];
    try { agendaProf = await fetch(`${API}/profesionales/${idProfesional}/agenda`).then(r => r.json()); } catch {}
    const diaSem     = new Date(fechaStr + 'T12:00:00').getDay();
    const tieneSlot  = agendaProf.some(a => a.disponible && DIA_DB_NUM[normalizarP(a.dia)] === diaSem);

    const solicitudHtml = (!tieneActivo && tieneSlot) ? `
        <div class="panel-solicitar-bloque">
            <button class="btn-solicitar-dia" onclick="abrirFormSolicitudProf('${fechaStr}')">+ Solicitar turno para este día</button>
        </div>
        <div class="form-solicitar-dia" id="formSolicitudProf">
            <div class="form-fila">
                <div class="form-grupo" style="flex:1">
                    <label>Paciente</label>
                    <select id="solProfPac" class="turno-input" onchange="cargarHorasSolicitudProf('${fechaStr}')">
                        <option value="">Cargando...</option>
                    </select>
                </div>
                <div class="form-grupo">
                    <label>Hora</label>
                    <select id="solProfHora" class="turno-input" disabled><option value="">Elegí un paciente</option></select>
                </div>
            </div>
            <div class="form-grupo" style="margin-top:8px">
                <label>Motivo (opcional)</label>
                <input type="text" id="solProfMotivo" class="turno-input" placeholder="Escribí el motivo...">
            </div>
            <button class="btn-accion btn-guardar" style="margin-top:12px" onclick="confirmarSolicitudDiaProf('${fechaStr}')">Enviar turno al paciente</button>
        </div>` : '';

    panel.innerHTML = (turnosDia.length ? turnosHtml : `<div class="sin-turnos" style="margin-bottom:10px">Sin turnos el ${dia} de ${MESES[calMes]}.</div>`) + solicitudHtml;

    if (!tieneActivo) cargarPacientesSolicitudProf(fechaStr);
}

async function abrirFormSolicitudProf(fechaStr) {
    document.getElementById('formSolicitudProf')?.classList.toggle('visible');
}

async function cargarPacientesSolicitudProf(fechaStr) {
    const fechaDate = new Date(fechaStr + 'T12:00:00').toDateString();
    const yaOcupado = turnosCalendarioProfesional.some(t =>
        new Date(t.fecha).toDateString() === fechaDate &&
        t.idEstadoTurno !== 3 && t.idEstadoTurno !== 4
    );
    const sel = document.getElementById('solProfPac');
    if (!sel) return;
    if (yaOcupado) {
        sel.innerHTML = `<option value="">Ya tenés un turno ese día</option>`;
        sel.disabled = true;
        return;
    }
    const pacientes = await fetch(`${API}/profesionales/${idProfesional}/pacientes`).then(r => r.json()).catch(() => []);
    sel.innerHTML = pacientes.length
        ? `<option value="">Seleccioná un paciente</option>` + pacientes.map(p => `<option value="${p.idPaciente}">${p.nombre}${p.apellido ? ' '+p.apellido : ''}</option>`).join('')
        : `<option value="">Sin pacientes asignados</option>`;
    sel.disabled = !pacientes.length;
}

async function cargarHorasSolicitudProf(fechaStr) {
    const sel  = document.getElementById('solProfHora');
    if (!sel) return;
    const fecha  = new Date(fechaStr + 'T12:00:00');
    const diaSem = fecha.getDay();

    let agenda = [];
    try { agenda = await fetch(`${API}/profesionales/${idProfesional}/agenda`).then(r => r.json()); } catch {}
    const slot = agenda.find(a => a.disponible && DIA_DB_NUM[normalizarP(a.dia)] === diaSem);

    if (!slot) { sel.innerHTML = `<option value="">Sin disponibilidad ese día</option>`; sel.disabled = true; return; }

    const [hI, mI] = slot.horaInicio.substring(0,5).split(':').map(Number);
    const [hF, mF] = slot.horaFin.substring(0,5).split(':').map(Number);
    const opciones = [];
    let cur = hI * 60 + mI;
    while (cur + 60 <= hF * 60 + mF) {
        const hh = String(Math.floor(cur/60)).padStart(2,'0');
        const mm = String(cur%60).padStart(2,'0');
        opciones.push(`<option value="${hh}:${mm}">${hh}:${mm}</option>`);
        cur += 60;
    }
    sel.innerHTML  = opciones.length ? opciones.join('') : `<option value="">Sin horarios</option>`;
    sel.disabled   = !opciones.length;
}

async function confirmarSolicitudDiaProf(fechaStr) {
    const idPac    = parseInt(document.getElementById('solProfPac')?.value);
    const hora     = document.getElementById('solProfHora')?.value;
    const motivo = document.getElementById('solProfMotivo')?.value || null;

    if (!idPac) { toast('Seleccioná un paciente.', 'warn'); return; }
    if (!hora)  { toast('Seleccioná un horario.',  'warn'); return; }

    const res = await fetch(`${API}/turnos/profesional-solicitar`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            idPaciente:    idPac,
            idProfesional: idProfesional,
            fecha:         new Date(fechaStr + 'T12:00:00').toISOString(),
            hora:          hora + ':00',
            motivo
        })
    });

    if (res.ok) {
        toast('Turno enviado. El paciente deberá confirmarlo.');
        await renderCalendario();
        mostrarPanelDiaProfesional(_diaSeleccionadoProf);
    } else toast('Error al solicitar el turno.', 'error');
}

async function cancelarTurnoProf(id) {
    if (!await confirmar('¿Cancelar este turno?')) return;
    await fetch(`${API}/turnos/${id}/cancelar`, { method: 'PUT' });
    await renderCalendario();
    mostrarPanelDiaProfesional(_diaSeleccionadoProf);
}

async function abrirFormReprogramarProf(idTurno) {
    const form = document.getElementById('formReprogramarProf');
    if (!form) return;
    const abriendo = !form.classList.contains('visible');
    form.classList.toggle('visible');
    if (!abriendo) { cancelarModoReprogramarProf(); return; }

    _idTurnoReprogProf = idTurno;
    _reprogFechaSeleccionadaProf = null;
    document.getElementById('reprogDetallesProf').style.display = 'none';

    try {
        const agenda = await fetch(`${API}/profesionales/${idProfesional}/agenda`).then(r => r.json());
        _agendaReprogProf = agenda.filter(a => a.disponible);
    } catch { _agendaReprogProf = []; }

    _modoReprogramarProf = true;
    await dibujarCalendarioProfesional();
}

function seleccionarDiaReprogProf(dia) {
    _reprogFechaSeleccionadaProf = new Date(calAnio, calMes, dia);
    dibujarCalendarioProfesional();

    const slot = _agendaReprogProf.find(a => DIA_DB_NUM[normalizarP(a.dia)] === _reprogFechaSeleccionadaProf.getDay());
    const label = _reprogFechaSeleccionadaProf.toLocaleDateString('es-AR', { weekday:'long', day:'numeric', month:'long' });
    document.getElementById('reprogDiaLabelProf').textContent = label;
    document.getElementById('reprogDetallesProf').style.display = 'block';

    const sel = document.getElementById('reprogHoraProf');
    if (!sel) return;
    if (slot) {
        const [hI, mI] = slot.horaInicio.substring(0,5).split(':').map(Number);
        const [hF, mF] = slot.horaFin.substring(0,5).split(':').map(Number);
        const opciones = [];
        let cur = hI * 60 + mI;
        while (cur + 60 <= hF * 60 + mF) {
            const hh = String(Math.floor(cur/60)).padStart(2,'0');
            const mm = String(cur%60).padStart(2,'0');
            opciones.push(`<option value="${hh}:${mm}">${hh}:${mm}</option>`);
            cur += 60;
        }
        sel.innerHTML = opciones.length ? opciones.join('') : `<option value="">Sin horarios</option>`;
    } else {
        sel.innerHTML = `<option value="">Sin disponibilidad</option>`;
    }
}

async function confirmarReprogramarProf(idTurno) {
    if (!_reprogFechaSeleccionadaProf) { toast('Seleccioná un día en el calendario.', 'warn'); return; }
    const hora   = document.getElementById('reprogHoraProf')?.value;
    const motivo = document.getElementById('reprogMotivoProf')?.value || null;
    if (!hora) { toast('Seleccioná un horario.', 'warn'); return; }

    const turno = await fetch(`${API}/turnos/${idTurno}`).then(r => r.json());
    const fecha = _reprogFechaSeleccionadaProf;
    turno.fecha = new Date(fecha.getFullYear(), fecha.getMonth(), fecha.getDate(), 12).toISOString();
    turno.hora  = hora + ':00';
    if (motivo) turno.motivo = motivo;

    const r = await fetch(`${API}/turnos/${idTurno}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(turno)
    });

    if (r.ok) {
        toast('Turno reprogramado.');
        cancelarModoReprogramarProf();
        await renderCalendario();
        mostrarPanelDiaProfesional(_reprogFechaSeleccionadaProf.getDate());
    } else toast('Error al reprogramar.', 'error');
}

function cancelarModoReprogramarProf() {
    _modoReprogramarProf = false;
    _idTurnoReprogProf   = null;
    _reprogFechaSeleccionadaProf = null;
    document.getElementById('formReprogramarProf')?.classList.remove('visible');
    dibujarCalendarioProfesional();
}

activarTab('inicio');

async function reportes() {
    const cont = document.getElementById('contenido');
    cont.innerHTML =
        '<div class="titulo-seccion">Reportes</div>' +
        '<div class="card">' +
        '<h3 style="margin-bottom:20px;color:#6A5ACD">Mis Atenciones</h3>' +
        '<div style="margin-bottom:20px">' +
        '<button onclick="descargarReporte()" style="padding:9px 20px;background:#6A5ACD;color:white;border:none;border-radius:8px;font-size:14px;cursor:pointer;font-weight:bold">Descargar Excel</button>' +
        '</div>' +
        '<p style="font-size:13px;color:#888">Descarga el reporte de tus propias atenciones.</p>' +
        '</div>';
}

async function descargarReporte() {
    const url = API + '/reportes/atenciones?idProfesional=' + idProfesional;
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
    const blob = new Blob(['' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'mis_atenciones.csv';
    link.click();
}

// Mis Pacientes v2: asignaciones nuevas, confirmar y derivar
async function pacientes() {
    const c = document.getElementById("contenido");
    let lista = [];
    try {
        const r = await fetch(API + "/profesionales/" + idProfesional + "/pacientes-detalle");
        lista = await r.json();
    } catch(e) {}

    const filas = lista.map(function(p) {
        const esNuevo = p.estadoAsignacion === "Nueva";
        const nombreEsc = (p.nombre || "").split(String.fromCharCode(39)).join("").split(String.fromCharCode(34)).join("");
        let acciones = '<button class="btn-ver" onclick="verHistoria(' + p.idPaciente + ', &quot;' + nombreEsc + '&quot;)">Historia</button> ' +
            '<button class="btn-sesion" onclick="activarTab(&quot;sesion&quot;)">Sesion</button>';
        if (esNuevo) {
            acciones = '<button class="btn-ver" style="background:linear-gradient(to right,#0d6efd,#084298)" onclick="verEvolucionAdmision(' + p.idPaciente + ')">Motivo asignacion</button> ' +
                '<button class="btn-ver" style="background:linear-gradient(to right,#198754,#0f5132)" onclick="confirmarNuevoPaciente(' + p.idPaciente + ')">Confirmar</button> ' +
                '<button class="btn-ver" style="background:linear-gradient(to right,#e53935,#b71c1c)" onclick="derivarNuevoPaciente(' + p.idPaciente + ')">Derivar</button>';
        }
        return '<tr>' +
            '<td>' + p.nombre + (esNuevo ? ' <span style="background:#0d6efd;color:white;border-radius:8px;padding:2px 10px;font-size:11px;font-weight:bold;margin-left:6px">NUEVO</span>' : "") + '</td>' +
            '<td>' + (p.dni || "-") + '</td>' +
            '<td>' + (p.mail || "-") + '</td>' +
            '<td>' + (p.telefono || "-") + '</td>' +
            '<td>' + acciones + '</td>' +
            '</tr>';
    }).join("");

    c.innerHTML = '<div class="titulo-seccion">Mis Pacientes</div>' +
        '<div class="card">' +
        (lista.length === 0 ? '<div class="sin-turnos">No tenes pacientes asignados.</div>' :
        '<div class="tabla-wrapper"><table><thead><tr><th>Nombre</th><th>DNI</th><th>Mail</th><th>Telefono</th><th></th></tr></thead><tbody>' + filas + '</tbody></table></div>') +
        '</div>' +
        '<div id="panelNuevoPaciente"></div>' +
        '<div class="modal-overlay" id="modalHistoria">' +
        '<div class="modal-box">' +
        '<div class="modal-header">' +
        '<span class="modal-titulo" id="modalTitulo">Historia Clinica</span>' +
        '<button class="modal-cerrar" onclick="cerrarModal()">X</button>' +
        '</div>' +
        '<div class="modal-body" id="modalBody"></div>' +
        '</div>' +
        '</div>';
}

async function verEvolucionAdmision(idPaciente) {
    let historia = null, evoluciones = [];
    try { historia = await (await fetch(API + "/pacientes/" + idPaciente + "/historiaclinica")).json(); } catch(e) {}
    if (historia !== null && historia.idHistoria !== undefined) {
        try { evoluciones = await (await fetch(API + "/historiasclinicas/" + historia.idHistoria + "/evoluciones")).json(); } catch(e) {}
    }
    const deAdmision = evoluciones.filter(function(e) { return ((e.diagnostico || "").toLowerCase().indexOf("admis") > -1); });
    const panel = document.getElementById("panelNuevoPaciente");
    if (deAdmision.length === 0) {
        panel.innerHTML = '<div class="card" style="margin-top:16px"><p style="color:#888">No hay evolucion de admision registrada.</p></div>';
        return;
    }
    panel.innerHTML = '<div class="card" style="margin-top:16px;border-left:6px solid #0d6efd">' +
        '<h3 style="color:#0d6efd;margin-bottom:12px">Evolucion del Equipo de Admision</h3>' +
        deAdmision.map(function(e) {
            return '<div style="background:#f0f6ff;border-radius:10px;padding:14px;margin-bottom:10px">' +
                '<div style="font-weight:bold;margin-bottom:6px">' + new Date(e.fecha).toLocaleDateString("es-AR", { day:"numeric", month:"long", year:"numeric" }) + '</div>' +
                (e.observaciones ? '<div style="font-size:14px;margin-bottom:6px">' + e.observaciones + '</div>' : "") +
                (e.planTratamiento ? '<div style="font-size:13px;color:#084298;font-weight:600">' + e.planTratamiento + '</div>' : "") +
                '</div>';
        }).join("") +
        '</div>';
    panel.scrollIntoView({ behavior: "smooth" });
}

function confirmarNuevoPaciente(idPaciente) {
    const panel = document.getElementById("panelNuevoPaciente");
    panel.innerHTML = '<div class="card" style="margin-top:16px;border-left:6px solid #198754">' +
        '<h3 style="color:#0f5132;margin-bottom:12px">Proponer primer turno</h3>' +
        '<p style="font-size:13px;color:#888;margin-bottom:12px">Elegi una fecha estimativa. Al paciente le llegara como turno pendiente y podra confirmarlo o reprogramarlo segun tu agenda.</p>' +
        '<div style="display:flex;gap:12px;flex-wrap:wrap;align-items:flex-end">' +
        '<div><label style="font-size:12px;font-weight:bold;color:#0f5132;display:block">Fecha</label><input type="date" id="npFecha" style="padding:8px;border:2px solid #badbcc;border-radius:8px"></div>' +
        '<div><label style="font-size:12px;font-weight:bold;color:#0f5132;display:block">Hora</label><input type="time" id="npHora" style="padding:8px;border:2px solid #badbcc;border-radius:8px"></div>' +
        '<button onclick="enviarPrimerTurno(' + idPaciente + ')" style="padding:9px 20px;background:#198754;color:white;border:none;border-radius:8px;cursor:pointer;font-weight:bold">Enviar propuesta</button>' +
        '</div></div>';
    panel.scrollIntoView({ behavior: "smooth" });
}

async function enviarPrimerTurno(idPaciente) {
    const fecha = document.getElementById("npFecha").value;
    const hora  = document.getElementById("npHora").value;
    if (fecha === "" || hora === "") { toast("Completa fecha y hora.", "warn"); return; }
    try {
        const r = await fetch(API + "/turnos/profesional-solicitar", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ idPaciente: idPaciente, idProfesional: idProfesional, fecha: fecha, hora: hora + ":00", motivo: "Primer turno - derivacion de admision" })
        });
        if (r.ok) {
            await fetch(API + "/asignaciones/estado", {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ idPaciente: idPaciente, idProfesional: idProfesional, estado: "Confirmada" })
            });
            toast("Propuesta enviada. El paciente podra confirmar o reprogramar el turno.");
            pacientes();
        } else toast("Error al crear el turno.", "error");
    } catch(e) { toast("Error de conexion.", "error"); }
}

async function derivarNuevoPaciente(idPaciente) {
    const motivo = prompt("Indica el motivo por el cual no vas a atender a este paciente:");
    if (motivo === null || motivo.trim() === "") return;
    try {
        const r = await fetch(API + "/informes-admision/rechazar", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ idPaciente: idPaciente, idProfesional: idProfesional, motivo: motivo })
        });
        if (r.ok) { toast("El caso volvio al equipo de admision para re-derivar.", "warn"); pacientes(); }
        else toast("Error al derivar.", "error");
    } catch(e) { toast("Error de conexion.", "error"); }
}

function calcularTurnos(s) {
    if (!s.horaInicio || !s.horaFin) return '—';
    const [hI, mI] = fmtHora(s.horaInicio).split(':').map(Number);
    const [hF, mF] = fmtHora(s.horaFin).split(':').map(Number);
    const minTotales = (hF * 60 + mF) - (hI * 60 + mI);
    const intervalo = s.intervaloMinutos ?? 30;
    return minTotales > 0 ? Math.floor(minTotales / intervalo) : 0;
}

function actualizarPreviewNuevo() {
    const inicio    = document.getElementById('fh-inicio')?.value;
    const fin       = document.getElementById('fh-fin')?.value;
    const intervalo = parseInt(document.getElementById('fh-intervalo')?.value ?? 30);
    const preview   = document.getElementById('fh-preview');
    if (!preview) return;
    if (!inicio || !fin) { preview.textContent = ''; return; }
    const [hI, mI] = inicio.split(':').map(Number);
    const [hF, mF] = fin.split(':').map(Number);
    const minTotales = (hF * 60 + mF) - (hI * 60 + mI);
    if (minTotales <= 0) { preview.textContent = '⚠ La hora fin debe ser mayor a la hora inicio.'; return; }
    const cantidad = Math.floor(minTotales / intervalo);
    preview.textContent = `✅ Con este horario podés atender ${cantidad} paciente${cantidad !== 1 ? 's' : ''} por día.`;
}

// Evoluciones v2: las de admision se muestran en azul
function renderEvoluciones(lista) {
    if (lista === null || lista === undefined || lista.length === 0) return '<div class="sin-datos">Sin evoluciones registradas.</div>';
    return lista.map(function(e) {
        const esAdmision = ((e.diagnostico || "").toLowerCase().indexOf("admis") > -1);
        const estilo = esAdmision ? ' style="border-left:5px solid #0d6efd;background:#f0f6ff;border-radius:8px;padding:12px"' : "";
        return '<div class="evolucion-item"' + estilo + '>' +
            (esAdmision ? '<div style="color:#0d6efd;font-weight:bold;font-size:12px;text-transform:uppercase;letter-spacing:1px;margin-bottom:4px">Inicio del proceso - Admision</div>' : "") +
            '<div class="evolucion-fecha">' + new Date(e.fecha).toLocaleDateString("es-AR", { weekday:"long", day:"numeric", month:"long", year:"numeric" }) + '</div>' +
            (e.diagnostico ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Diagnostico</div><div class="evolucion-bloque-texto">' + e.diagnostico + '</div></div>' : "") +
            (e.observaciones ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Observaciones</div><div class="evolucion-bloque-texto">' + e.observaciones + '</div></div>' : "") +
            (e.planTratamiento ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">' + (esAdmision ? "Derivacion" : "Plan") + '</div><div class="evolucion-bloque-texto">' + e.planTratamiento + '</div></div>' : "") +
            '</div>';
    }).join("");
}

// Panel de turnos confirmados pendientes de generar sala
async function cargarTurnosConfirmados() {
    let lista = [];
    try { lista = await (await fetch(API + "/profesionales/" + idProfesional + "/turnos-confirmados")).json(); } catch(e) { return; }
    const sinSala = lista.filter(function(t) { return t.codigoSala === "" || t.codigoSala === null; });
    if (sinSala.length === 0) return;

    let cont = document.getElementById("panel-confirmados");
    if (cont === null) {
        cont = document.createElement("div");
        cont.id = "panel-confirmados";
        cont.style.cssText = "position:fixed;bottom:20px;right:20px;z-index:9990;max-width:360px;display:flex;flex-direction:column;gap:10px";
        document.body.appendChild(cont);
    }
    cont.innerHTML = sinSala.map(function(t) {
        return '<div style="background:white;border-left:5px solid #198754;border-radius:10px;padding:14px 16px;box-shadow:0 4px 16px rgba(0,0,0,.15)">' +
            '<div style="font-weight:bold;color:#0f5132;margin-bottom:4px">Turno confirmado</div>' +
            '<div style="font-size:13px;color:#444;margin-bottom:8px">' + t.nombrePaciente + ' - ' + t.fecha + ' ' + t.hora + ' hs</div>' +
            '<button onclick="generarSalaTurno(' + t.idTurno + ')" style="padding:7px 16px;background:#198754;color:white;border:none;border-radius:6px;cursor:pointer;font-size:13px;font-weight:bold">Generar sala</button>' +
            '</div>';
    }).join("");
}

async function generarSalaTurno(idTurno) {
    try {
        const r = await fetch(API + "/turnos/" + idTurno + "/generar-sala", { method: "PUT" });
        if (r.ok) {
            const resp = await r.json();
            toast("Sala generada: " + resp.codigoSala + ". El paciente fue notificado.");
            const cont = document.getElementById("panel-confirmados");
            if (cont !== null) cont.innerHTML = "";
            cargarTurnosConfirmados();
        } else toast("Error al generar la sala.", "error");
    } catch(e) { toast("Error de conexion.", "error"); }
}

cargarTurnosConfirmados();
