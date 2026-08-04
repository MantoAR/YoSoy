const API = 'http://localhost:5000';
const idProfesional = localStorage.getItem('idProfesional');

// ─── ARRANQUE ────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    mostrarSeccion('inicio');
});

// ─── NAVEGACIÓN ──────────────────────────────────────────
function mostrarSeccion(id) {
    document.querySelectorAll('.tab-btn').forEach(btn =>
        btn.classList.toggle('activo', btn.dataset.sec === id));

    const c = document.getElementById('contenido');
    c.innerHTML = `<div class="spinner">Cargando...</div>`;

    if      (id === 'inicio')    renderInicio(c);
    else if (id === 'agenda')    renderAgenda(c);
    else if (id === 'pacientes') renderPacientes(c);
    else if (id === 'perfil')    renderPerfil(c);
    else if (id === 'sesion')    c.innerHTML = `
        <iframe src="../Pantalla_Profesional/sesion_video.html"
            style="width:100%;height:calc(100vh - 120px);border:none;">
        </iframe>`;
}

async function cerrarSesion() {
    if (await confirmar('¿Cerrar sesión?')) {
        localStorage.clear();
        window.location.href = '../index.html';
    }
}

// ─── INICIO ──────────────────────────────────────────────
async function renderInicio(c) {
    try {
        const res = await fetch(`${API}/profesionales/${idProfesional}`);
        const p = await res.json();
        c.innerHTML = `
            <div class="seccion">
                <h2>Bienvenido, ${p.nombre} ${p.apellido}</h2>
                <div class="cards-grid">
                    <div class="card-stat" onclick="mostrarSeccion('agenda')">
                        <div class="card-stat-icon">📅</div>
                        <div class="card-stat-label">Mi Agenda</div>
                    </div>
                    <div class="card-stat" onclick="mostrarSeccion('pacientes')">
                        <div class="card-stat-icon">👥</div>
                        <div class="card-stat-label">Mis Pacientes</div>
                    </div>
                    <div class="card-stat" onclick="mostrarSeccion('sesion')">
                        <div class="card-stat-icon">📹</div>
                        <div class="card-stat-label">Iniciar Sesión</div>
                    </div>
                </div>
            </div>`;
    } catch {
        c.innerHTML = `<div class="seccion"><p>Error cargando datos.</p></div>`;
    }
}

// ─── AGENDA ──────────────────────────────────────────────
async function renderAgenda(c) {
    let agenda = [];
    try {
        const res = await fetch(`${API}/profesionales/${idProfesional}/agenda`);
        agenda = await res.json();
    } catch {}

    c.innerHTML = `
        <div class="seccion">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
                <h2 style="margin:0">Mis horarios de trabajo</h2>
                <button class="btn-chico" style="background:#185FA5;color:white;padding:8px 16px;border:none;border-radius:8px;cursor:pointer" onclick="mostrarFormAgenda()">+ Agregar día</button>
            </div>

            <table class="tabla" id="tablaAgenda">
                <thead>
                    <tr><th>Día</th><th>Desde</th><th>Hasta</th><th>Intervalo</th><th>Turnos/día</th><th>Disponible</th><th></th></tr>
                </thead>
                <tbody>
                    ${agenda.length ? agenda.map(a => {
                        const inicio = a.horaInicio?.substring(0,5) ?? '--:--';
                        const fin    = a.horaFin?.substring(0,5) ?? '--:--';
                        const intervalo = a.intervaloMinutos ?? 30;
                        // Calcular cantidad de turnos
                        let turnos = '—';
                        if (a.horaInicio && a.horaFin) {
                            const [hI, mI] = inicio.split(':').map(Number);
                            const [hF, mF] = fin.split(':').map(Number);
                            const minTotales = (hF*60+mF) - (hI*60+mI);
                            turnos = minTotales > 0 ? Math.floor(minTotales / intervalo) : 0;
                        }
                        return `<tr>
                            <td>${a.dia}</td>
                            <td>${inicio}</td>
                            <td>${fin}</td>
                            <td>${intervalo} min</td>
                            <td><strong>${turnos}</strong> turnos</td>
                            <td><span style="background:${a.disponible?'#d1e7dd':'#f8d7da'};color:${a.disponible?'#0f5132':'#842029'};padding:2px 10px;border-radius:20px;font-size:12px">${a.disponible?'Sí':'No'}</span></td>
                            <td style="display:flex;gap:6px">
                                <button class="btn-chico" style="background:#185FA5;color:white" onclick="editarAgenda(${a.idAgenda})">Editar</button>
                                <button class="btn-chico" style="background:#A32D2D;color:white" onclick="eliminarAgenda(${a.idAgenda})">Eliminar</button>
                            </td>
                        </tr>`;
                    }).join('') : '<tr><td colspan="7" style="text-align:center;color:#999">No configuraste horarios todavía.</td></tr>'}
                </tbody>
            </table>

            <div id="formAgenda" style="display:none;margin-top:20px;background:#f8f5ff;border-radius:12px;padding:20px">
                <h3 style="margin-bottom:16px" id="formAgendaTitulo">Agregar horario</h3>
                <input type="hidden" id="agendaId">
                <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px;margin-bottom:12px">
                    <div>
                        <label style="font-size:12px;font-weight:600;color:#6A5ACD;text-transform:uppercase">Día</label>
                        <select id="agendaDia" class="turno-input" style="width:100%;margin-top:4px;padding:10px;border:1px solid #ddd;border-radius:8px">
                            <option>Lunes</option><option>Martes</option><option>Miércoles</option>
                            <option>Jueves</option><option>Viernes</option><option>Sábado</option><option>Domingo</option>
                        </select>
                    </div>
                    <div>
                        <label style="font-size:12px;font-weight:600;color:#6A5ACD;text-transform:uppercase">Hora inicio</label>
                        <input type="time" id="agendaInicio" style="width:100%;margin-top:4px;padding:10px;border:1px solid #ddd;border-radius:8px">
                    </div>
                    <div>
                        <label style="font-size:12px;font-weight:600;color:#6A5ACD;text-transform:uppercase">Hora fin</label>
                        <input type="time" id="agendaFin" style="width:100%;margin-top:4px;padding:10px;border:1px solid #ddd;border-radius:8px">
                    </div>
                </div>
                <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:16px">
                    <div>
                        <label style="font-size:12px;font-weight:600;color:#6A5ACD;text-transform:uppercase">Intervalo de atención</label>
                        <select id="agendaIntervalo" style="width:100%;margin-top:4px;padding:10px;border:1px solid #ddd;border-radius:8px">
                            <option value="15">15 minutos</option>
                            <option value="20">20 minutos</option>
                            <option value="30" selected>30 minutos</option>
                            <option value="45">45 minutos</option>
                            <option value="60">60 minutos</option>
                            <option value="90">90 minutos</option>
                        </select>
                    </div>
                    <div>
                        <label style="font-size:12px;font-weight:600;color:#6A5ACD;text-transform:uppercase">Disponible</label>
                        <select id="agendaDisponible" style="width:100%;margin-top:4px;padding:10px;border:1px solid #ddd;border-radius:8px">
                            <option value="true">Sí</option>
                            <option value="false">No</option>
                        </select>
                    </div>
                </div>
                <div id="agendaPreview" style="font-size:13px;color:#6A5ACD;margin-bottom:12px"></div>
                <div style="display:flex;gap:8px">
                    <button onclick="guardarAgenda()" style="background:#185FA5;color:white;border:none;padding:10px 24px;border-radius:8px;cursor:pointer;font-weight:500">Guardar</button>
                    <button onclick="document.getElementById('formAgenda').style.display='none'" style="background:#eee;color:#555;border:none;padding:10px 24px;border-radius:8px;cursor:pointer">Cancelar</button>
                </div>
            </div>
        </div>`;

    // Preview en tiempo real de cantidad de turnos
    ['agendaInicio','agendaFin','agendaIntervalo'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', actualizarPreview);
    });
}

function actualizarPreview() {
    const inicio    = document.getElementById('agendaInicio').value;
    const fin       = document.getElementById('agendaFin').value;
    const intervalo = parseInt(document.getElementById('agendaIntervalo').value);
    const preview   = document.getElementById('agendaPreview');
    if (!inicio || !fin || !intervalo) { preview.textContent = ''; return; }
    const [hI,mI] = inicio.split(':').map(Number);
    const [hF,mF] = fin.split(':').map(Number);
    const minTotales = (hF*60+mF)-(hI*60+mI);
    if (minTotales <= 0) { preview.textContent = '⚠ La hora fin debe ser mayor a la hora inicio.'; return; }
    const cantidad = Math.floor(minTotales / intervalo);
    preview.textContent = `✅ Con este horario podés atender ${cantidad} paciente${cantidad !== 1 ? 's' : ''} por día.`;
}

function mostrarFormAgenda() {
    document.getElementById('formAgendaTitulo').textContent = 'Agregar horario';
    document.getElementById('agendaId').value = '';
    document.getElementById('agendaDia').value = 'Lunes';
    document.getElementById('agendaInicio').value = '';
    document.getElementById('agendaFin').value = '';
    document.getElementById('agendaIntervalo').value = '30';
    document.getElementById('agendaDisponible').value = 'true';
    document.getElementById('agendaPreview').textContent = '';
    document.getElementById('formAgenda').style.display = 'block';
}

async function editarAgenda(id) {
    const res = await fetch(`${API}/agenda/${id}`);
    const a   = await res.json();
    document.getElementById('formAgendaTitulo').textContent = 'Editar horario';
    document.getElementById('agendaId').value          = a.idAgenda;
    document.getElementById('agendaDia').value         = a.dia;
    document.getElementById('agendaInicio').value      = a.horaInicio?.substring(0,5) ?? '';
    document.getElementById('agendaFin').value         = a.horaFin?.substring(0,5) ?? '';
    document.getElementById('agendaIntervalo').value   = a.intervaloMinutos ?? 30;
    document.getElementById('agendaDisponible').value  = String(a.disponible);
    document.getElementById('formAgenda').style.display = 'block';
    actualizarPreview();
}

async function guardarAgenda() {
    const id         = document.getElementById('agendaId').value;
    const body = {
        idProfesional:    parseInt(idProfesional),
        dia:              document.getElementById('agendaDia').value,
        horaInicio:       document.getElementById('agendaInicio').value + ':00',
        horaFin:          document.getElementById('agendaFin').value + ':00',
        intervaloMinutos: parseInt(document.getElementById('agendaIntervalo').value),
        disponible:       document.getElementById('agendaDisponible').value === 'true'
    };
    const url    = id ? `${API}/agenda/${id}` : `${API}/agenda`;
    const method = id ? 'PUT' : 'POST';
    const res    = await fetch(url, { method, headers: {'Content-Type':'application/json'}, body: JSON.stringify(body) });
    if (res.ok) {
        renderAgenda(document.getElementById('contenido'));
    } else {
        alert('Error al guardar el horario.');
    }
}

async function eliminarAgenda(id) {
    if (!await confirmar('¿Eliminar este horario?')) return;
    await fetch(`${API}/agenda/${id}`, { method: 'DELETE' });
    renderAgenda(document.getElementById('contenido'));
}

// ─── PACIENTES ───────────────────────────────────────────
async function renderPacientes(c) {
    try {
        const res = await fetch(`${API}/profesionales/${idProfesional}/pacientes`);
        const pacientes = await res.json();
        if (!pacientes.length) {
            c.innerHTML = `<div class="seccion"><p>No tenés pacientes asignados.</p></div>`;
            return;
        }
        const filas = pacientes.map(p => `
            <tr>
                <td>${p.nombre} ${p.apellido}</td>
                <td>${p.dni}</td>
                <td>${p.mail}</td>
                <td>${p.telefono ?? '—'}</td>
            </tr>`).join('');
        c.innerHTML = `
            <div class="seccion">
                <h2>Mis Pacientes</h2>
                <table class="tabla">
                    <thead><tr><th>Nombre</th><th>DNI</th><th>Email</th><th>Teléfono</th></tr></thead>
                    <tbody>${filas}</tbody>
                </table>
            </div>`;
    } catch {
        c.innerHTML = `<div class="seccion"><p>Error cargando pacientes.</p></div>`;
    }
}

// ─── PERFIL ──────────────────────────────────────────────
async function renderPerfil(c) {
    try {
        const res = await fetch(`${API}/profesionales/${idProfesional}`);
        const p = await res.json();
        c.innerHTML = `
            <div class="seccion">
                <h2>Mi Perfil</h2>
                <div class="perfil-grid">
                    <div class="perfil-row"><span class="perfil-label">Nombre</span><span>${p.nombre} ${p.apellido}</span></div>
                    <div class="perfil-row"><span class="perfil-label">DNI</span><span>${p.dni}</span></div>
                    <div class="perfil-row"><span class="perfil-label">Email</span><span>${p.mail}</span></div>
                    <div class="perfil-row"><span class="perfil-label">Especialidad</span><span>${p.especialidad ?? '—'}</span></div>
                    <div class="perfil-row"><span class="perfil-label">Teléfono</span><span>${p.telefono ?? '—'}</span></div>
                    <div class="perfil-row"><span class="perfil-label">Estado</span><span>${p.estado}</span></div>
                </div>
            </div>`;
    } catch {
        c.innerHTML = `<div class="seccion"><p>Error cargando perfil.</p></div>`;
    }
}

// ─── HELPERS ─────────────────────────────────────────────
function confirmar(msg) {
    return new Promise(resolve => {
        const overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.45);z-index:9999;display:flex;align-items:center;justify-content:center';
        overlay.innerHTML = `
            <div style="background:white;border-radius:16px;padding:32px 28px;max-width:340px;width:90%;text-align:center">
                <p style="font-size:15px;color:#444;margin-bottom:20px">${msg}</p>
                <div style="display:flex;gap:10px;justify-content:center">
                    <button id="conf-si" style="background:#A32D2D;color:white;border:none;padding:10px 24px;border-radius:8px;cursor:pointer;font-size:14px">Sí</button>
                    <button id="conf-no" style="background:#eee;color:#555;border:none;padding:10px 24px;border-radius:8px;cursor:pointer;font-size:14px">No</button>
                </div>
            </div>`;
        document.body.appendChild(overlay);
        overlay.querySelector('#conf-si').onclick = () => { overlay.remove(); resolve(true); };
        overlay.querySelector('#conf-no').onclick = () => { overlay.remove(); resolve(false); };
    });
}