const API = YS.API;   // mismo origen que la página (shared/ui.js)
const idProfesional = parseInt(localStorage.getItem('idProfesional'));
const idUsuario     = parseInt(localStorage.getItem('idUsuario'));

if (!idProfesional) window.location.href = '/pantallas/index.html';

// Cada sección del menú (shared/shell.js) se dibuja en #contenido.
function activarTab(nombre) {
    const tabs = { inicio, agenda, horarios, pacientes, sesion, reportes, perfil };
    if (!tabs[nombre]) nombre = 'inicio';
    YS.shell.marcar(nombre);
    document.getElementById('contenido').innerHTML = YS.cargando();
    tabs[nombre]();
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
        <div class="titulo-seccion titulo-seccion--tarjeta">Sesiones de hoy</div>
        <div class="turnos-lista">
            ${turnosHoy.map(t => `
            <div class="turno-item sesion-hoy">
                <div class="sesion-hoy-fila">
                    <div class="turno-fecha-bloque turno-fecha-bloque--hora">
                        <div class="turno-fecha-dia">${t.hora}</div>
                        <div class="turno-fecha-mes">hs</div>
                    </div>
                    <div class="turno-info">
                        <div class="turno-paciente">${t.nombrePaciente}</div>
                        ${t.motivo ? `<div class="turno-meta">${t.motivo}</div>` : ''}
                    </div>
                    ${t.codigoSala
                        ? `<div class="sala-estado">
                            <span class="ui-chip ui-chip--ok">Sala lista</span>
                            <code class="codigo-sala">${t.codigoSala}</code>
                           </div>`
                        : `<button class="ui-btn ui-btn--primary" onclick="generarSalaDesdeInicio(${t.idTurno})">Iniciar sesión</button>`
                    }
                </div>
                ${t.codigoSala ? `
                <div class="sala-codigo-fila">
                    <span class="texto-suave">Compartí este código con el paciente:</span>
                    <code class="codigo-sala codigo-sala--grande">${t.codigoSala}</code>
                    <button class="ui-btn ui-btn--sm" onclick="copiarCodigo('${t.codigoSala}')">Copiar</button>
                    <button class="ui-btn ui-btn--sm ui-btn--primary" onclick="activarTab('sesion')">Entrar a la sala →</button>
                </div>` : ''}
            </div>`).join('')}
        </div>` : '';

    if (YS.shell.actual !== 'inicio') return;   // el usuario ya cambió de sección
    YS.shell.encabezado({ titulo: `Hola, ${prof?.nombre ?? 'Profesional'}`, subtitulo: YS.capitalizar(YS.fecha.larga(new Date())) });
    YS.shell.acciones([{ texto: 'Cita urgente', icono: 'alerta', tipo: 'danger', alClick: citaUrgente }]);

    c.innerHTML = `
        ${sesionesHoyHtml ? `<div class="card card--acento-info">${sesionesHoyHtml}</div>` : ''}

        <div class="card">
            <div class="titulo-seccion titulo-seccion--tarjeta">Próximo turno</div>
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

        <div class="card">
            <div class="titulo-seccion titulo-seccion--tarjeta">Pacientes hoy</div>
            <div id="pacientes-hoy-contenido"><div class="spinner">Cargando...</div></div>
        </div>

        <div class="card" id="card-pendientes">
            <div class="titulo-seccion titulo-seccion--tarjeta">Turnos pendientes de confirmación</div>
            <div id="pendientes-contenido"><div class="spinner">Cargando...</div></div>
        </div>

        <!-- Panel cita urgente (citaUrgente() alterna style.display) -->
        <div id="panel-urgente" style="display:none">
            <div class="card card--acento-danger">
                <div class="titulo-seccion titulo-seccion--tarjeta titulo-seccion--danger">Cita urgente</div>
                <p class="texto-suave mb-12">
                    Seleccioná el paciente, generá la sala al instante y el código le llega por notificación.
                </p>
                <div class="form-fila form-fila--abajo">
                    <div class="campo campo--ancho">
                        <label for="urgente-paciente">Paciente</label>
                        <select id="urgente-paciente">
                            <option value="">Cargando...</option>
                        </select>
                    </div>
                    <button class="ui-btn btn-urgente" onclick="confirmarCitaUrgente()">Generar sala y notificar</button>
                    <button class="ui-btn" onclick="document.getElementById('panel-urgente').style.display='none'">Cancelar</button>
                </div>
                <div id="urgente-resultado" class="mt-12"></div>
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
        YS.shell.marcar('sesion');
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
                        return `
                        <tr>
                            <td><strong>${hora} hs</strong></td>
                            <td>${nombre}</td>
                            <td>${t.motivo ?? '—'}</td>
                            <td><span class="badge-estado estado-${t.idEstadoTurno}">${estado}</span></td>
                            <td class="celda-acciones">
                                <button class="ui-btn ui-btn--sm ui-btn--primary" onclick="irASala(${t.idTurno}, ${t.idPaciente})">Ir a sala</button>
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
    let sinConsentimiento = false;
    try {
        const r = await fetch(`${API}/turnos/${idTurno}`);
        const turno = await r.json();

        if (!turno.codigoSala) {
            // Confirmar y generar sala. Sin consentimiento (RN-02) el turno no se confirma,
            // pero la sesión no se bloquea: se avisa al profesional.
            const rConf = await fetch(`${API}/turnos/${idTurno}/confirmar-paciente`, { method: 'PUT' });
            sinConsentimiento = rConf.status === 409;
            const rSala = await fetch(`${API}/turnos/${idTurno}/generar-sala`, { method: 'PUT' });
            if (rSala.ok) {
                const data = await rSala.json();
                toast(`Sala generada: ${data.codigoSala}. El paciente fue notificado.`);
            }
        } else {
            toast(`Sala ya generada: ${turno.codigoSala}`);
        }
    } catch { toast('Error al generar la sala.', 'error'); }


    YS.shell.marcar('sesion');
    sesion(idPaciente);
    if (sinConsentimiento) bannerSinConsentimiento();
}

async function confirmarTurno(id) {
    try {
        const turno = await (await fetch(`${API}/turnos/${id}`)).json();
        turno.idEstadoTurno = 2;
        const r = await fetch(`${API}/turnos/${id}`, { method: 'PUT', headers: {'Content-Type':'application/json'}, body: JSON.stringify(turno) });
        if (r.ok) { toast('Turno confirmado'); activarTab('inicio'); }
        else if (r.status === 409) await avisoConsentimiento(r);
        else toast('Error al confirmar', 'error');
    } catch { toast('Error al confirmar', 'error'); }
}

async function agenda() {
    const c = document.getElementById('contenido');

    let slots = [];
    try { const r = await fetch(`${API}/profesionales/${idProfesional}/agenda`); slots = await r.json(); } catch {}

    // La base guarda el día con distintas formas ('lunes', 'Lunes', 'miercoles'):
    // se ordena por el número de día, no por el texto.
    slots.sort((a, b) => YS.dias.orden(a.dia) - YS.dias.orden(b.dia) || fmtHora(a.horaInicio).localeCompare(fmtHora(b.horaInicio)));
    _agendaSlotsProf = slots;
    if (YS.shell.actual !== 'agenda') return;

    c.innerHTML = `
        <div class="card">
            <div class="agenda-toolbar">
                <div class="agenda-nav">
                    <button class="btn-secundario" onclick="irAHoyProf()">Hoy</button>
                    <button class="btn-secundario" onclick="navegarAgendaProf(-1)" aria-label="Anterior">&#8249;</button>
                    <button class="btn-secundario" onclick="navegarAgendaProf(1)" aria-label="Siguiente">&#8250;</button>
                    <span class="agenda-titulo cal-mes" id="calTituloProfesional"></span>
                </div>
                <div class="ui-segmentado" role="group" aria-label="Vista de la agenda">
                    <button data-vista="dia"    onclick="cambiarVistaProf('dia')">Día</button>
                    <button data-vista="semana" onclick="cambiarVistaProf('semana')">Semana</button>
                    <button data-vista="mes"    onclick="cambiarVistaProf('mes')">Mes</button>
                </div>
            </div>
            <div class="ui-alerta ui-alerta--info agenda-aviso-reprog oculto" id="avisoReprogProf">
                Reprogramando: elegí el nuevo día en el calendario del mes.
            </div>
            <div id="vistaMesProf"><div class="cal-grid" id="calGridProfesional"></div></div>
            <div id="vistaSemanaProf" class="oculto"></div>
            <div class="agenda-leyenda">
                <span class="ui-chip ui-chip--warn">Pendiente</span>
                <span class="ui-chip ui-chip--info">Propuesto por mí</span>
                <span class="ui-chip ui-chip--ok">Confirmado</span>
                <span class="ui-chip ui-chip--neutral">Cancelado</span>
            </div>
        </div>
        <div class="panel-turno-prof" id="panelTurnoProfesional"></div>
    `;

    await renderCalendario();
}

// Mis horarios de trabajo (antes, la segunda tarjeta de la agenda)
async function horarios() {
    const c = document.getElementById('contenido');

    let slots = [];
    try { const r = await fetch(`${API}/profesionales/${idProfesional}/agenda`); slots = await r.json(); } catch {}

    // La base guarda el día con distintas formas ('lunes', 'Lunes', 'miercoles'):
    // se ordena por el número de día, no por el texto.
    slots.sort((a, b) => YS.dias.orden(a.dia) - YS.dias.orden(b.dia) || fmtHora(a.horaInicio).localeCompare(fmtHora(b.horaInicio)));
    _agendaSlotsProf = slots;
    if (YS.shell.actual !== 'horarios') return;

    YS.shell.acciones([{ texto: 'Agregar horario', icono: 'mas', tipo: 'primary', alClick: abrirFormNuevoHorario }]);

    c.innerHTML = `
        <div class="card">
            <div id="slots-lista">
                ${!slots.length ? YS.vacio({ icono: 'reloj', titulo: 'Todavía no cargaste horarios', texto: 'Agregá los días y las franjas en que atendés para que se puedan pedir turnos.', accion: { texto: 'Agregar horario', onclick: 'abrirFormNuevoHorario()' } }) : `
                <div class="tabla-wrapper">
                    <table>
                        <thead><tr><th>Día</th><th>Desde</th><th>Hasta</th><th>Intervalo</th><th>Turnos/día</th><th>Disponible</th><th></th></tr></thead>
                        <tbody>
                            ${slots.map(s => `
                            <tr id="slot-row-${s.idAgenda}">
                                <td>${YS.dias.nombre(s.dia)}</td>
                                <td>${fmtHora(s.horaInicio)}</td>
                                <td>${fmtHora(s.horaFin)}</td>
                                <td>${s.intervaloMinutos ?? 30} min</td>
                                <td>${calcularTurnos(s)}</td>
                                <td><span class="badge-disponible ${s.disponible ? 'si' : 'no'}">${s.disponible ? 'Sí' : 'No'}</span></td>
                                <td class="celda-acciones">
                                    <button class="btn-ver" onclick="editarHorario(${s.idAgenda},'${s.dia}','${fmtHora(s.horaInicio)}','${fmtHora(s.horaFin)}',${s.disponible},${s.intervaloMinutos ?? 30})">Editar</button>
                                    <button class="btn-peligro" onclick="eliminarHorario(${s.idAgenda})">Eliminar</button>
                                </td>
                            </tr>`).join('')}
                        </tbody>
                    </table>
                </div>`}
            </div>

            <div id="form-horario" class="form-horario">
                <div class="form-horario-titulo" id="form-horario-titulo">Agregar horario</div>
                <input type="hidden" id="fh-id">
                <div class="form-fila">
                    <div class="campo">
                        <label for="fh-dia">Día</label>
                        <select id="fh-dia">
                            <option>Lunes</option><option>Martes</option><option>Miércoles</option>
                            <option>Jueves</option><option>Viernes</option><option>Sábado</option><option>Domingo</option>
                        </select>
                    </div>
                    <div class="campo">
                        <label for="fh-inicio">Hora inicio</label>
                        <input type="time" id="fh-inicio">
                    </div>
                    <div class="campo">
                        <label for="fh-fin">Hora fin</label>
                        <input type="time" id="fh-fin">
                    </div>
                    <div class="campo">
                        <label for="fh-disponible">Disponible</label>
                        <select id="fh-disponible">
                            <option value="true">Sí</option>
                            <option value="false">No</option>
                        </select>
                    </div>
                    <div class="campo">
                        <label for="fh-intervalo">Intervalo de atención</label>
                        <select id="fh-intervalo">
                            <option value="15">15 minutos</option>
                            <option value="20">20 minutos</option>
                            <option value="30" selected>30 minutos</option>
                            <option value="45">45 minutos</option>
                            <option value="60">60 minutos</option>
                            <option value="90">90 minutos</option>
                        </select>
                    </div>
                </div>
                <div id="fh-preview" class="form-preview"></div>
                <div class="form-acciones">
                    <button class="btn-guardar-perfil" onclick="guardarHorario()">Guardar</button>
                    <button class="btn-secundario" onclick="cerrarFormHorario()">Cancelar</button>
                </div>
                <div id="fh-msg" class="form-mensaje"></div>
            </div>
        </div>
    `;

    ['fh-inicio','fh-fin','fh-intervalo'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', actualizarPreviewNuevo);
    });
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
    // Las opciones del select son 'Lunes'…'Domingo'; si la base guardó 'miercoles', sin
    // esto el select quedaba en la primera opción y al guardar el horario pasaba a lunes.
    document.getElementById('fh-dia').value = YS.dias.nombre(dia);
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
        if (r.ok) { toast(id ? 'Horario actualizado' : 'Horario agregado'); activarTab('horarios'); }
        else { msg.style.color='#e74c3c'; msg.textContent='Error al guardar.'; }
    } catch { msg.style.color='#e74c3c'; msg.textContent='Error de conexión.'; }
}

async function eliminarHorario(id) {
    const ok = await confirmar('¿Eliminar este horario?');
    if (!ok) return;
    try {
        const r = await fetch(`${API}/agendas/${id}`, { method:'DELETE' });
        if (r.ok) { toast('Horario eliminado', 'warn'); activarTab('horarios'); }
        else toast('Error al eliminar', 'error');
    } catch { toast('Error de conexión', 'error'); }
}

async function responderTurno(id, aceptar) {
    try {
        const turno = await (await fetch(`${API}/turnos/${id}`)).json();
        turno.idEstadoTurno = aceptar ? 2 : 3;
        const r = await fetch(`${API}/turnos/${id}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body:JSON.stringify(turno) });
        if (r.ok) { toast(aceptar ? 'Turno aceptado' : 'Turno rechazado', aceptar ? 'ok' : 'warn'); activarTab('agenda'); }
        else if (r.status === 409) await avisoConsentimiento(r);
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
                '<span class="ui-chip ui-chip--neutral ui-chip--sin-punto chip-emocion">' + k + ': <strong>' + v + '%</strong></span>'
            ).join('');
        } catch {}
        return '<div class="evolucion-item">' +
            '<div class="evolucion-fecha">' +
                new Date(s.fechaHora).toLocaleDateString('es-AR', {weekday:'long',day:'numeric',month:'long',year:'numeric'}) +
                ' &nbsp;·&nbsp; ' + new Date(s.fechaHora).toLocaleTimeString('es-AR',{hour:'2-digit',minute:'2-digit'}) +
                (s.tipoCierre ? ' &nbsp;<span class="ui-chip ui-chip--info ui-chip--sin-punto">' + s.tipoCierre + '</span>' : '') +
            '</div>' +
            (emociones ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Promedio de emociones</div><div class="flex-gap mt-8">' + emociones + '</div></div>' : '') +
            (s.transcripcion ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Transcripción</div><div class="evolucion-bloque-texto transcripcion">' + s.transcripcion + '</div></div>' : '') +
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

// ── RN-02: consentimiento de telemedicina ───────────────────
// El backend responde 409 { codigo: "RN-02", mensaje, urlConsentimiento } cuando se
// intenta confirmar un turno de un paciente sin el consentimiento firmado.
async function avisoConsentimiento(r) {
    const data = await r.json().catch(() => ({}));
    mostrarAvisoConsentimiento(
        data.mensaje || 'El paciente todavía no firmó el consentimiento informado para atención por telemedicina.',
        data.urlConsentimiento);
}

// Modal con el mensaje y el link para compartir con el paciente (el profesional no puede firmar por él).
function mostrarAvisoConsentimiento(mensaje, url) {
    document.getElementById('avisoConsentimiento')?.remove();
    const overlay = document.createElement('div');
    overlay.id = 'avisoConsentimiento';
    overlay.className = 'dialogo-overlay';
    const box = document.createElement('div');
    box.className = 'dialogo dialogo--ancho';
    box.setAttribute('role', 'alertdialog');
    const titulo = document.createElement('h3');
    titulo.className = 'dialogo-titulo';
    titulo.textContent = 'No se puede confirmar el turno';
    const texto = document.createElement('p');
    texto.className = 'dialogo-mensaje';
    texto.textContent = mensaje;
    box.append(titulo, texto);
    if (url) {
        const etiqueta = document.createElement('label');
        etiqueta.className = 'ui-label';
        etiqueta.textContent = 'Compartile al paciente este link para que lo firme desde su panel:';
        const fila = document.createElement('div');
        fila.className = 'dialogo-link';
        const input = document.createElement('input');
        input.className = 'ui-input';
        input.readOnly = true; input.value = url;
        const copiar = document.createElement('button');
        copiar.className = 'ui-btn';
        copiar.textContent = 'Copiar';
        copiar.onclick = () => { input.select(); navigator.clipboard?.writeText(url); copiar.textContent = 'Copiado'; };
        fila.append(input, copiar);
        box.append(etiqueta, fila);
    }
    const botones = document.createElement('div');
    botones.className = 'dialogo-botones';
    const ok = document.createElement('button');
    ok.className = 'ui-btn ui-btn--primary';
    ok.textContent = 'Entendido';
    ok.onclick = () => overlay.remove();
    botones.appendChild(ok);
    box.appendChild(botones);
    overlay.appendChild(box);
    document.body.appendChild(overlay);
    ok.focus();
}

// Aviso persistente en la vista de sesión: la sesión arranca igual, pero el turno
// queda sin confirmar hasta que el paciente firme.
function bannerSinConsentimiento() {
    const c = document.getElementById('contenido');
    if (!c || document.getElementById('bannerSinConsentimiento')) return;
    const b = document.createElement('div');
    b.id = 'bannerSinConsentimiento';
    b.className = 'ui-alerta mb-12';
    b.setAttribute('role', 'alert');
    b.textContent = 'Este paciente no tiene firmado el consentimiento informado para atención por telemedicina. ' +
                    'La sesión puede hacerse igual, pero el turno queda sin confirmar hasta que lo firme desde su panel.';
    c.prepend(b);
}

function sesion(idPaciente = null) {
    const src = idPaciente 
        ? `sesion_video.html?idPaciente=${idPaciente}` 
        : 'sesion_video.html';
    document.getElementById('contenido').innerHTML = `
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
        else if (r.status === 409) await avisoConsentimiento(r);
        else toast('Error', 'error');
    } catch { toast('Error de conexión', 'error'); }
}

let _diaSeleccionadoProf = null;

let _diaAgendaDisponible = false;

async function cerrarSesion() {
    await YS.sesion.cerrar();
}

function confirmar(msg) {
    return new Promise(resolve => {
        const overlay = document.createElement('div');
        overlay.className = 'dialogo-overlay';
        overlay.innerHTML = `
            <div class="dialogo" role="alertdialog">
                <p class="dialogo-mensaje">${msg}</p>
                <div class="dialogo-botones">
                    <button id="conf-no" class="ui-btn">No</button>
                    <button id="conf-si" class="ui-btn ui-btn--primary">Sí</button>
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
    await dibujarSemanaProfesional();
}

// ── Agenda: vistas Día / Semana / Mes ─────────────────────
// El mes se sigue dibujando siempre (dibujarCalendarioProfesional, con la misma lógica
// de siempre, incluida la autoconfirmación); cuando la vista es Día o Semana queda oculto
// y además se dibuja esta grilla con los mismos turnos. Al hacer clic en un turno o en un
// día se abre el mismo panel del día (mostrarPanelDiaProfesional) con las mismas acciones.
// Mientras se reprograma se muestra el mes, porque ahí se elige el nuevo día.
let _vistaAgendaProf = 'semana';
let _fechaRefProf    = new Date();
let _agendaSlotsProf = [];
const HORA_PX = 64;
const ESTADO_TURNO_LABEL = { 1: 'Pendiente', 2: 'Confirmado', 3: 'Cancelado', 4: 'Realizado' };

function inicioSemanaProf(d) {
    const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
    x.setDate(x.getDate() - ((x.getDay() + 6) % 7));
    return x;
}

function diasVisiblesProf() {
    if (_vistaAgendaProf === 'dia')
        return [new Date(_fechaRefProf.getFullYear(), _fechaRefProf.getMonth(), _fechaRefProf.getDate())];
    const lunes = inicioSemanaProf(_fechaRefProf);
    return Array.from({ length: 7 }, (_, i) => new Date(lunes.getFullYear(), lunes.getMonth(), lunes.getDate() + i));
}

function sincronizarMesProf() {
    calAnio = _fechaRefProf.getFullYear();
    calMes  = _fechaRefProf.getMonth();
}

async function cambiarVistaProf(vista) {
    if (vista !== 'mes' && _vistaAgendaProf === 'mes') {
        // Al pasar del mes a Día/Semana: el día de hoy si es este mes, si no el primero del mes.
        const hoy = new Date();
        _fechaRefProf = (calAnio === hoy.getFullYear() && calMes === hoy.getMonth()) ? hoy : new Date(calAnio, calMes, 1);
    }
    _vistaAgendaProf = vista;
    if (vista !== 'mes') sincronizarMesProf();
    await dibujarCalendarioProfesional();
}

async function navegarAgendaProf(delta) {
    if (_vistaAgendaProf === 'mes' || _modoReprogramarProf) { await cambiarMesProfesional(delta); return; }
    const paso = _vistaAgendaProf === 'dia' ? 1 : 7;
    _fechaRefProf = new Date(_fechaRefProf.getFullYear(), _fechaRefProf.getMonth(), _fechaRefProf.getDate() + delta * paso);
    sincronizarMesProf();
    document.getElementById('panelTurnoProfesional')?.classList.remove('visible');
    await dibujarCalendarioProfesional();
}

async function irAHoyProf() {
    _fechaRefProf = new Date();
    sincronizarMesProf();
    if (!_modoReprogramarProf) document.getElementById('panelTurnoProfesional')?.classList.remove('visible');
    await dibujarCalendarioProfesional();
}

// Clic en un turno o en la cabecera de un día de la grilla: mismo panel que en el mes.
async function abrirDiaDesdeSemanaProf(anio, mes, dia) {
    calAnio = anio; calMes = mes;
    _diaSeleccionadoProf = dia;
    await dibujarCalendarioProfesional();
    await mostrarPanelDiaProfesional(dia);
    await dibujarSemanaProfesional();   // marca el día abierto en la grilla
    document.getElementById('panelTurnoProfesional')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function actualizarVistaAgendaProf() {
    const mes     = document.getElementById('vistaMesProf');
    const semana  = document.getElementById('vistaSemanaProf');
    const titulo  = document.getElementById('calTituloProfesional');
    if (!mes || !semana || !titulo) return;
    const verMes = _vistaAgendaProf === 'mes' || _modoReprogramarProf;
    mes.classList.toggle('oculto', !verMes);
    semana.classList.toggle('oculto', verMes);
    document.getElementById('avisoReprogProf')?.classList.toggle('oculto', !_modoReprogramarProf);
    document.querySelectorAll('.ui-segmentado [data-vista]').forEach(b =>
        b.classList.toggle('activo', b.dataset.vista === (verMes ? 'mes' : _vistaAgendaProf)));
    if (verMes) return;   // el título del mes lo pone dibujarCalendarioProfesional
    const dias = diasVisiblesProf();
    if (_vistaAgendaProf === 'dia') {
        titulo.textContent = dias[0].toLocaleDateString('es-AR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
    } else {
        const a = dias[0], b = dias[6];
        const fmt = d => d.toLocaleDateString('es-AR', { day: 'numeric', month: 'short' }).replace('.', '');
        titulo.textContent = `${fmt(a)} – ${fmt(b)} ${b.getFullYear()}`;
    }
}

async function dibujarSemanaProfesional() {
    actualizarVistaAgendaProf();
    const cont = document.getElementById('vistaSemanaProf');
    if (!cont || cont.classList.contains('oculto')) return;

    const dias = diasVisiblesProf();
    const clave = d => `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
    const porDia = {};
    turnosCalendarioProfesional.forEach(t => {
        const f = new Date(t.fecha);
        (porDia[clave(f)] ??= []).push(t);
    });
    const visibles = dias.flatMap(d => porDia[clave(d)] ?? []);

    // Nombres de pacientes (mismo caché que usa el panel del día)
    await Promise.all([...new Set(visibles.map(t => t.idPaciente))].map(async id => {
        if (!_pacientesCalMap[id]) {
            try { _pacientesCalMap[id] = await (await fetch(`${API}/pacientes/${id}`)).json(); } catch {}
        }
    }));

    // Franja horaria: la agenda de trabajo y los turnos visibles (por defecto 8 a 20)
    const aMin = h => { const [hh, mm] = fmtHora(h).split(':').map(Number); return hh * 60 + (mm || 0); };
    const franjasDia = d => _agendaSlotsProf.filter(a => a.disponible && DIA_DB_NUM[normalizarP(a.dia)] === d.getDay() && a.horaInicio && a.horaFin);
    let desde = 8 * 60, hasta = 20 * 60;
    const inicios = [], fines = [];
    dias.forEach(d => franjasDia(d).forEach(a => { inicios.push(aMin(a.horaInicio)); fines.push(aMin(a.horaFin)); }));
    visibles.forEach(t => { inicios.push(aMin(t.hora)); fines.push(aMin(t.hora) + 60); });
    if (inicios.length) desde = Math.min(desde, Math.floor(Math.min(...inicios) / 60) * 60);
    if (fines.length)   hasta = Math.max(hasta, Math.ceil(Math.max(...fines) / 60) * 60);
    const alto = ((hasta - desde) / 60) * HORA_PX;
    const y = min => ((min - desde) / 60) * HORA_PX;

    const hoy = new Date();
    const hoySinHora = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate());
    const columnas = `56px repeat(${dias.length}, minmax(0, 1fr))`;

    const cabecera = dias.map(d => {
        const esHoy = d.getTime() === hoySinHora.getTime();
        const esPasado = d < hoySinHora;
        const tieneT = !!porDia[clave(d)];
        const sel = _diaSeleccionadoProf === d.getDate() && calMes === d.getMonth() && calAnio === d.getFullYear()
            && document.getElementById('panelTurnoProfesional')?.classList.contains('visible');
        const click = (!esPasado || tieneT) ? `onclick="abrirDiaDesdeSemanaProf(${d.getFullYear()},${d.getMonth()},${d.getDate()})"` : '';
        return `<button class="semana-col-cab${esHoy ? ' hoy' : ''}${esPasado && !tieneT ? ' pasado' : ''}${sel ? ' seleccionado' : ''}" ${click}>
                    <span class="dia-nombre">${DIAS[(d.getDay() + 6) % 7]}</span>
                    <span class="dia-numero">${d.getDate()}</span>
                </button>`;
    }).join('');

    let horas = '';
    for (let m = desde; m <= hasta; m += 60)
        if (m > desde) horas += `<div class="semana-hora" style="top:${y(m)}px">${String(m / 60).padStart(2, '0')}:00</div>`;

    const cols = dias.map(d => {
        let html = '';
        for (let m = desde; m < hasta; m += 30)
            html += `<div class="semana-linea${m % 60 ? ' media' : ''}" style="top:${y(m)}px"></div>`;
        // Fuera del horario de trabajo: rayado suave
        const fr = franjasDia(d).map(a => [aMin(a.horaInicio), aMin(a.horaFin)]).sort((a, b) => a[0] - b[0]);
        let cursor = desde;
        fr.forEach(([i, f]) => {
            if (i > cursor) html += `<div class="semana-fuera" style="top:${y(cursor)}px;height:${y(i) - y(cursor)}px"></div>`;
            cursor = Math.max(cursor, f);
        });
        if (cursor < hasta) html += `<div class="semana-fuera" style="top:${y(cursor)}px;height:${y(hasta) - y(cursor)}px"></div>`;
        // Turnos. Los que se superponen (por ejemplo uno cancelado y otro nuevo a la
        // misma hora) se reparten el ancho de la columna en carriles, como en cualquier calendario.
        const intervalo = franjasDia(d)[0]?.intervaloMinutos ?? 30;
        const items = (porDia[clave(d)] ?? [])
            .map(t => ({ t, i: aMin(t.hora), f: aMin(t.hora) + intervalo }))
            .sort((a, b) => a.i - b.i || a.t.idEstadoTurno - b.t.idEstadoTurno);
        let grupo = [], finGrupo = -1;
        const cerrarGrupo = () => {
            const carriles = [];
            grupo.forEach(it => {
                let c = carriles.findIndex(fin => fin <= it.i);
                if (c < 0) { c = carriles.length; carriles.push(0); }
                carriles[c] = it.f; it.carril = c;
            });
            grupo.forEach(it => it.carriles = carriles.length);
            grupo = []; finGrupo = -1;
        };
        items.forEach(it => {
            if (grupo.length && it.i >= finGrupo) cerrarGrupo();
            grupo.push(it); finGrupo = Math.max(finGrupo, it.f);
        });
        if (grupo.length) cerrarGrupo();
        items.forEach(({ t, i: inicio, carril, carriles }) => {
            const p = _pacientesCalMap[t.idPaciente];
            const nombre = p ? [p.nombre, p.apellido].filter(Boolean).join(' ') : 'Paciente #' + t.idPaciente;
            const alto = Math.max(22, (intervalo / 60) * HORA_PX - 3);
            const posicion = `left:calc(3px + (100% - 6px) * ${carril} / ${carriles});width:calc((100% - 6px) / ${carriles} - 2px)`;
            html += `<button class="bloque-turno estado-${t.idEstadoTurno}" style="top:${y(inicio) + 1}px;height:${alto}px;${posicion}"
                         title="${fmtHora(t.hora)} · ${nombre} · ${ESTADO_TURNO_LABEL[t.idEstadoTurno] ?? ''}"
                         onclick="abrirDiaDesdeSemanaProf(${d.getFullYear()},${d.getMonth()},${d.getDate()})">
                        <span class="bt-nombre"><span class="bt-hora">${fmtHora(t.hora)}</span> ${nombre}</span>
                    </button>`;
        });
        // Línea de "ahora"
        if (d.getTime() === hoySinHora.getTime()) {
            const ahora = hoy.getHours() * 60 + hoy.getMinutes();
            if (ahora >= desde && ahora <= hasta) html += `<div class="semana-ahora" style="top:${y(ahora)}px"></div>`;
        }
        return `<div class="semana-col${d.getTime() === hoySinHora.getTime() ? ' hoy' : ''}" style="height:${alto}px">${html}</div>`;
    }).join('');

    const scrollPrevio = cont.querySelector('.semana-cuerpo')?.scrollTop;
    cont.innerHTML = `
        <div class="semana">
            <div class="semana-cabecera" style="grid-template-columns:${columnas}"><div></div>${cabecera}</div>
            <div class="semana-cuerpo" style="grid-template-columns:${columnas}">
                <div class="semana-horas" style="height:${alto}px">${horas}</div>${cols}
            </div>
        </div>`;
    const cuerpo = cont.querySelector('.semana-cuerpo');
    if (scrollPrevio != null) cuerpo.scrollTop = scrollPrevio;
    else {
        // Primera vez: arrancar cerca del comienzo del horario de trabajo
        const primero = Math.min(...dias.flatMap(d => franjasDia(d).map(a => aMin(a.horaInicio))), hasta);
        cuerpo.scrollTop = Math.max(0, y(primero) - 12);
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
    const fechaStr    = `${calAnio}-${String(calMes+1).padStart(2,'0')}-${String(dia).padStart(2,'0')}`;
    const tieneActivo = turnosDia.some(t => t.idEstadoTurno !== 3 && t.idEstadoTurno !== 4);

    const turnosHtml = turnosDia.map(t => {
        const p = _pacientesCalMap[t.idPaciente];
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
            <span class="panel-estado-badge estado-${t.idEstadoTurno}">${estadoLabel[t.idEstadoTurno] ?? '—'}</span>
        </div>
        ${t.motivo ? `<div class="panel-chips"><div class="chip chip-wide"><span class="chip-label">Motivo</span><span class="chip-val">${t.motivo}</span></div></div>` : ''}
        ${esPendientePaciente ? `
            <div class="panel-acciones">
                <button class="btn-accion btn-guardar" onclick="responderTurnoDesdeCal(${t.idTurno}, true)">Aceptar</button>
                <button class="btn-peligro" onclick="responderTurnoDesdeCal(${t.idTurno}, false)">Rechazar</button>
            </div>` : ''}
        ${puedeAccionar && !esPendientePaciente ? `
            <div class="panel-acciones">
                <button class="btn-accion btn-cancelar" onclick="cancelarTurnoProf(${t.idTurno})">Cancelar turno</button>
                <button class="btn-accion btn-reprogramar" id="btnReprogramarProf-${t.idTurno}" onclick="abrirFormReprogramarProf(${t.idTurno})">Reprogramar</button>
            </div>
            <div class="form-reprogramar" id="formReprogramarProf-${t.idTurno}">
                <p class="reprog-instruccion">Seleccioná el nuevo día en el calendario</p>
                <div id="reprogDetallesProf-${t.idTurno}" style="display:none">
                    <div class="reprog-dia-seleccionado" id="reprogDiaLabelProf-${t.idTurno}"></div>
                    <div class="form-fila mt-12">
                        <div class="form-grupo form-grupo--ancho">
                            <label>Hora</label>
                            <select id="reprogHoraProf-${t.idTurno}" class="turno-input"></select>
                        </div>
                        <div class="form-grupo form-grupo--ancho">
                            <label>Motivo</label>
                            <input type="text" id="reprogMotivoProf-${t.idTurno}" class="turno-input" placeholder="Motivo del cambio...">
                        </div>
                    </div>
                    <button class="btn-accion btn-guardar mt-12" onclick="confirmarReprogramarProf(${t.idTurno})">Confirmar reprogramación</button>
                </div>
                <button class="btn-accion btn-cancelar mt-12" onclick="cancelarModoReprogramarProf()">Cancelar reprogramación</button>
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
                <div class="form-grupo form-grupo--ancho">
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
            <div class="form-grupo mt-8">
                <label>Motivo (opcional)</label>
                <input type="text" id="solProfMotivo" class="turno-input" placeholder="Escribí el motivo...">
            </div>
            <button class="btn-accion btn-guardar mt-12" onclick="confirmarSolicitudDiaProf('${fechaStr}')">Enviar turno al paciente</button>
        </div>` : '';

    panel.innerHTML = (turnosDia.length ? turnosHtml : `<div class="sin-turnos mb-12">Sin turnos el ${dia} de ${MESES[calMes]}.</div>`) + solicitudHtml;

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

// Con dos turnos activos el mismo día, cada uno tiene su formulario (ids con el idTurno).
async function abrirFormReprogramarProf(idTurno) {
    const form = document.getElementById(`formReprogramarProf-${idTurno}`);
    if (!form) return;
    const abriendo = !form.classList.contains('visible');
    document.querySelectorAll('.form-reprogramar.visible').forEach(f => { if (f !== form) f.classList.remove('visible'); });
    form.classList.toggle('visible');
    if (!abriendo) { cancelarModoReprogramarProf(); return; }

    _idTurnoReprogProf = idTurno;
    _reprogFechaSeleccionadaProf = null;
    document.getElementById(`reprogDetallesProf-${idTurno}`).style.display = 'none';

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
    document.getElementById(`reprogDiaLabelProf-${_idTurnoReprogProf}`).textContent = label;
    document.getElementById(`reprogDetallesProf-${_idTurnoReprogProf}`).style.display = 'block';

    const sel = document.getElementById(`reprogHoraProf-${_idTurnoReprogProf}`);
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
    const hora   = document.getElementById(`reprogHoraProf-${idTurno}`)?.value;
    const motivo = document.getElementById(`reprogMotivoProf-${idTurno}`)?.value || null;
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
    document.querySelectorAll('.form-reprogramar.visible').forEach(f => f.classList.remove('visible'));
    dibujarCalendarioProfesional();
}

async function reportes() {
    const cont = document.getElementById('contenido');
    cont.innerHTML =
        '<div class="card">' +
        '<div class="titulo-seccion titulo-seccion--tarjeta">Mis Atenciones</div>' +
        '<p class="texto-suave mb-12">Descarga el reporte de tus propias atenciones.</p>' +
        '<button class="ui-btn ui-btn--primary" onclick="descargarReporte()">Descargar Excel</button>' +
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
            acciones = '<button class="btn-ver" onclick="verEvolucionAdmision(' + p.idPaciente + ')">Motivo asignacion</button> ' +
                '<button class="btn-confirmar" onclick="confirmarNuevoPaciente(' + p.idPaciente + ')">Confirmar</button> ' +
                '<button class="btn-peligro" onclick="derivarNuevoPaciente(' + p.idPaciente + ')">Derivar</button>';
        }
        return '<tr>' +
            '<td>' + p.nombre + (esNuevo ? ' <span class="ui-chip ui-chip--info">Nuevo</span>' : "") + '</td>' +
            '<td>' + (p.dni || "-") + '</td>' +
            '<td>' + (p.mail || "-") + '</td>' +
            '<td>' + (p.telefono || "-") + '</td>' +
            '<td class="celda-acciones">' + acciones + '</td>' +
            '</tr>';
    }).join("");

    if (YS.shell.actual !== 'pacientes') return;
    c.innerHTML = '<div class="card">' +
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
        panel.innerHTML = '<div class="card mt-16"><p class="texto-suave">No hay evolucion de admision registrada.</p></div>';
        return;
    }
    panel.innerHTML = '<div class="card card--acento-info mt-16">' +
        '<div class="titulo-seccion titulo-seccion--tarjeta">Evolucion del Equipo de Admision</div>' +
        deAdmision.map(function(e) {
            return '<div class="caja-suave mb-12">' +
                '<div class="texto-fuerte mb-8">' + new Date(e.fecha).toLocaleDateString("es-AR", { day:"numeric", month:"long", year:"numeric" }) + '</div>' +
                (e.observaciones ? '<div class="mb-8">' + e.observaciones + '</div>' : "") +
                (e.planTratamiento ? '<div class="texto-accent texto-fuerte">' + e.planTratamiento + '</div>' : "") +
                '</div>';
        }).join("") +
        '</div>';
    panel.scrollIntoView({ behavior: "smooth" });
}

function confirmarNuevoPaciente(idPaciente) {
    const panel = document.getElementById("panelNuevoPaciente");
    panel.innerHTML = '<div class="card card--acento-ok mt-16">' +
        '<div class="titulo-seccion titulo-seccion--tarjeta">Proponer primer turno</div>' +
        '<p class="texto-suave mb-12">Elegi una fecha estimativa. Al paciente le llegara como turno pendiente y podra confirmarlo o reprogramarlo segun tu agenda.</p>' +
        '<div class="form-fila form-fila--abajo">' +
        '<div class="campo"><label for="npFecha">Fecha</label><input type="date" id="npFecha"></div>' +
        '<div class="campo"><label for="npHora">Hora</label><input type="time" id="npHora"></div>' +
        '<button class="ui-btn ui-btn--primary" onclick="enviarPrimerTurno(' + idPaciente + ')">Enviar propuesta</button>' +
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
        return '<div class="evolucion-item' + (esAdmision ? ' evolucion-item--admision' : '') + '">' +
            (esAdmision ? '<div class="evolucion-etiqueta-admision">Inicio del proceso - Admision</div>' : "") +
            '<div class="evolucion-fecha">' + new Date(e.fecha).toLocaleDateString("es-AR", { weekday:"long", day:"numeric", month:"long", year:"numeric" }) + '</div>' +
            (e.diagnostico ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Diagnostico</div><div class="evolucion-bloque-texto">' + e.diagnostico + '</div></div>' : "") +
            (e.observaciones ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Observaciones</div><div class="evolucion-bloque-texto">' + e.observaciones + '</div></div>' : "") +
            (e.planTratamiento ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">' + (esAdmision ? "Derivacion" : "Plan") + '</div><div class="evolucion-bloque-texto">' + e.planTratamiento + '</div></div>' : "") +
            '</div>';
    }).join("");
}

// Los turnos confirmados sin sala se avisan en la campana de la barra (shared/shell.js).
// Si se genera una sala desde ahí con el Inicio abierto, se actualiza la lista de hoy.
document.addEventListener('ys:sala-generada', () => { if (YS.shell.actual === 'inicio') activarTab('inicio'); });

YS.shell.iniciar({ rol: 'profesional', navegar: id => activarTab(id) });
