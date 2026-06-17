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
    try {
        const res = await fetch(`${API}/turnos/profesional/${idProfesional}`);
        const turnos = await res.json();
        if (!turnos.length) {
            c.innerHTML = `<div class="seccion"><p>No tenés turnos programados.</p></div>`;
            return;
        }
        const filas = turnos.map(t => `
            <tr>
                <td>${new Date(t.fecha).toLocaleString('es-AR')}</td>
                <td>${t.paciente}</td>
                <td>${t.estado}</td>
                <td><button class="btn-chico" onclick="mostrarSeccion('sesion')">📹 Iniciar</button></td>
            </tr>`).join('');
        c.innerHTML = `
            <div class="seccion">
                <h2>Mi Agenda</h2>
                <table class="tabla">
                    <thead><tr><th>Fecha</th><th>Paciente</th><th>Estado</th><th></th></tr></thead>
                    <tbody>${filas}</tbody>
                </table>
            </div>`;
    } catch {
        c.innerHTML = `<div class="seccion"><p>Error cargando agenda.</p></div>`;
    }
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