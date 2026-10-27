const API = YS.API;   // mismo origen que la página (shared/ui.js)
const idProfesional = parseInt(localStorage.getItem('idProfesional'));
const idUsuario     = parseInt(localStorage.getItem('idUsuario'));

if (!idProfesional) window.location.href = '/pantallas/index.html';

// Cada sección del menú (shared/shell.js) se dibuja en #contenido.
function activarTab(nombre) {
    const tabs = { inicio, agenda, horarios, pacientes, casos, sesion, reportes, perfil };
    if (!tabs[nombre]) nombre = 'inicio';
    YS.shell.marcar(nombre);
    document.getElementById('contenido').innerHTML = YS.cargando();
    tabs[nombre]();
}

// Avisos del sistema de diseño (shared/ui.js)
function toast(msg, tipo = 'ok') {
    YS.toast(msg, tipo);
}

// ── Inicio: tablero del día ─────────────────────────────────
// Momento real del turno (fecha + hora) para ordenar y saber cuál es el próximo.
function momentoTurno(t) {
    const d = YS.fecha.parse(t.fecha);
    const [h, m] = fmtHora(t.hora).split(':').map(Number);
    return d ? new Date(d.getFullYear(), d.getMonth(), d.getDate(), h || 0, m || 0) : new Date(0);
}

// Datos de los pacientes que falten en el caché compartido con la agenda (uno por id).
async function cargarPacientes(ids) {
    await Promise.all([...new Set(ids)].filter(id => !_pacientesCalMap[id]).map(async id => {
        try { _pacientesCalMap[id] = await YS.api(`/pacientes/${id}`); } catch {}
    }));
}

function nombrePaciente(id) {
    const p = _pacientesCalMap[id];
    return (p && YS.nombreCompleto(p)) || 'Paciente #' + id;
}

async function inicio() {
    const c = document.getElementById('contenido');
    c.innerHTML = YS.esqueleto.bloques(4) + YS.esqueleto.lista(3);
    const pedir = (ruta, porDefecto) => YS.api(ruta).catch(() => porDefecto);
    const conAdmision = YS.sesion.tienePrivilegio('admision');
    const [prof, confirmados, turnos, asignados, admisiones] = await Promise.all([
        pedir(`/profesionales/${idProfesional}`, null),
        pedir(`/profesionales/${idProfesional}/turnos-confirmados`, []),
        pedir(`/profesionales/${idProfesional}/turnos`, []),
        pedir(`/profesionales/${idProfesional}/pacientes-detalle`, []),
        conAdmision ? pedir('/informes-admision-detalle', []) : []
    ]);
    const ahora = new Date();
    const deHoy      = turnos.filter(t => YS.fecha.esHoy(t.fecha) && t.idEstadoTurno !== 3);
    const pendientes = turnos.filter(t => t.idEstadoTurno === 1);
    const pedidos    = pendientes.filter(t => t.creadoPor !== 'profesional');
    const proximo    = turnos.filter(t => t.idEstadoTurno !== 3 && t.idEstadoTurno !== 4 && momentoTurno(t) >= ahora)
                             .sort((a, b) => momentoTurno(a) - momentoTurno(b))[0];
    const nuevos     = asignados.filter(p => p.estadoAsignacion === 'Nueva').length;
    const sesionesHoy = confirmados.filter(t => t.fecha === YS.fecha.corta(ahora));
    await cargarPacientes([...deHoy, ...pendientes, ...(proximo ? [proximo] : [])].map(t => t.idPaciente));

    if (YS.shell.actual !== 'inicio') return;   // el usuario ya cambió de sección
    YS.shell.encabezado({ titulo: `Hola, ${prof?.nombre ?? 'Profesional'}`, subtitulo: YS.capitalizar(YS.fecha.larga(new Date())) });
    YS.shell.acciones([{ texto: 'Cita urgente', icono: 'alerta', tipo: 'danger', alClick: citaUrgente }]);

    const plural = (n, uno, varios) => `${n} ${n === 1 ? uno : varios}`;
    // Quien hace admisión ve primero cuántos pacientes esperan su entrevista y cuántos están en entrevista para derivar
    // (las admisiones están en la página de gestión; cada tarjeta abre su solapa).
    const admPendientes = admisiones.filter(a => ['Pendiente', 'En entrevista', 'Rechazado'].includes(a.estado)).length;   // los dos últimos son del camino viejo de la entrevista: se revisan igual
    const admEnEntrevista = admisiones.filter(a => a.estado === 'Aprobado' && a.asignacionEstado === 'Por resolver').length;   // asignaciones para resolver
    const tarjetaAdmision = conAdmision ? `
            <a class="ui-stat${admPendientes ? ' ui-stat--warn' : ''}" href="${YS.rutas.gestion}#admisiones?filtro=pendientes">
                <span class="ui-stat-etiqueta">${YS.icono('portapapeles')}Formularios por revisar</span>
                <span class="ui-stat-valor">${admPendientes}</span>
                <span class="ui-stat-pie">${admPendientes ? plural(admPendientes, 'formulario «Sobre mí» para revisar', 'formularios «Sobre mí» para revisar') : 'No hay formularios para revisar'}</span>
            </a>
            <a class="ui-stat${admEnEntrevista ? ' ui-stat--warn' : ''}" href="${YS.rutas.gestion}#admisiones?filtro=resolver">
                <span class="ui-stat-etiqueta">${YS.icono('pacientes')}Asignaciones para resolver</span>
                <span class="ui-stat-valor">${admEnEntrevista}</span>
                <span class="ui-stat-pie">${admEnEntrevista ? 'Sin propuesta o rechazada: asignalas a mano' : 'Ninguna por resolver'}</span>
            </a>` : '';
    c.innerHTML = `
        <div class="ui-stats">${tarjetaAdmision}
            <a class="ui-stat" href="#agenda">
                <span class="ui-stat-etiqueta">${YS.icono('agenda')}Turnos de hoy</span>
                <span class="ui-stat-valor">${deHoy.length}</span>
                <span class="ui-stat-pie">${plural(deHoy.filter(t => t.idEstadoTurno === 2).length, 'confirmado', 'confirmados')} · ${plural(deHoy.filter(t => t.idEstadoTurno === 1).length, 'pendiente', 'pendientes')}</span>
            </a>
            <button class="ui-stat${pedidos.length ? ' ui-stat--warn' : ''}" type="button" onclick="document.getElementById('card-pendientes').scrollIntoView({ behavior: 'smooth' })">
                <span class="ui-stat-etiqueta">${YS.icono('reloj')}Para confirmar</span>
                <span class="ui-stat-valor">${pedidos.length}</span>
                <span class="ui-stat-pie">Pedidos de pacientes${pendientes.length > pedidos.length ? ` · ${plural(pendientes.length - pedidos.length, 'propuesto', 'propuestos')} por vos` : ''}</span>
            </button>
            <a class="ui-stat${nuevos ? ' ui-stat--marca' : ''}" href="#pacientes">
                <span class="ui-stat-etiqueta">${YS.icono('pacientes')}Pacientes</span>
                <span class="ui-stat-valor">${asignados.length}</span>
                <span class="ui-stat-pie">${nuevos ? plural(nuevos, 'asignación nueva', 'asignaciones nuevas') : 'Asignados a vos'}</span>
            </a>
            <a class="ui-stat" href="#agenda">
                <span class="ui-stat-etiqueta">${YS.icono('video')}Próximo turno</span>
                <span class="ui-stat-valor">${proximo ? fmtHora(proximo.hora) : '—'}</span>
                <span class="ui-stat-pie">${proximo ? `${YS.esc(YS.capitalizar(YS.fecha.larga(proximo.fecha)))} · ${YS.esc(nombrePaciente(proximo.idPaciente))}` : 'No tenés turnos próximos'}</span>
            </a>
        </div>

        ${sesionesHoy.length ? `<div class="card card--acento-info">${htmlSesionesHoy(sesionesHoy)}</div>` : ''}

        <div class="inicio-columnas">
            <div class="card">
                <div class="titulo-seccion titulo-seccion--tarjeta">Pacientes de hoy</div>
                <div id="pacientes-hoy-contenido">${renderPacientesHoy(turnos)}</div>
            </div>
            <div class="card" id="card-pendientes">
                <div class="titulo-seccion titulo-seccion--tarjeta">Turnos pendientes de confirmación</div>
                <div id="pendientes-contenido">${renderTurnosPendientes(turnos)}</div>
            </div>
        </div>

        <!-- Panel cita urgente (citaUrgente() lo muestra u oculta) -->
        <div id="panel-urgente" class="oculto">
            <div class="card card--acento-danger">
                <div class="titulo-seccion titulo-seccion--tarjeta titulo-seccion--danger">Cita urgente</div>
                <p class="texto-suave mb-12">
                    Seleccioná el paciente, generá la sala al instante y el código le llega por notificación.
                </p>
                <div class="form-fila form-fila--abajo">
                    <div class="ui-campo campo--ancho">
                        <label for="urgente-paciente">Paciente</label>
                        <select id="urgente-paciente" class="ui-select">
                            <option value="">Cargando…</option>
                        </select>
                    </div>
                    <button class="ui-btn btn-urgente" id="btnUrgente" onclick="confirmarCitaUrgente()">Generar sala y notificar</button>
                    <button class="ui-btn" onclick="cerrarCitaUrgente()">Cancelar</button>
                </div>
                <div id="urgente-resultado" class="mt-12"></div>
            </div>
        </div>
    `;

    // Pacientes para la cita urgente
    try {
        const pacs = await YS.api(`/profesionales/${idProfesional}/pacientes`);
        const sel = document.getElementById('urgente-paciente');
        if (sel) {
            sel.innerHTML = pacs.length
                ? `<option value="">Seleccioná un paciente</option>` + pacs.map(p => `<option value="${p.idPaciente}">${YS.esc(YS.nombreCompleto(p))}</option>`).join('')
                : `<option value="">Sin pacientes asignados</option>`;
        }
    } catch {}
}

// Sesiones de hoy ya confirmadas: generar la sala o compartir el código
function htmlSesionesHoy(turnosHoy) {
    return `
        <div class="titulo-seccion titulo-seccion--tarjeta">Sesiones de hoy</div>
        <div class="turnos-lista">
            ${turnosHoy.map(t => `
            <div class="turno-item sesion-hoy">
                <div class="sesion-hoy-fila">
                    <div class="turno-fecha-bloque turno-fecha-bloque--hora">
                        <div class="turno-fecha-dia">${YS.esc(t.hora)}</div>
                        <div class="turno-fecha-mes">hs</div>
                    </div>
                    <div class="turno-info">
                        <div class="turno-paciente">${YS.esc(t.nombrePaciente)}</div>
                        ${t.motivo ? `<div class="turno-meta">${YS.esc(t.motivo)}</div>` : ''}
                    </div>
                    ${t.codigoSala
                        ? `<div class="sala-estado">
                            <span class="ui-chip ui-chip--ok">Sala lista</span>
                            <code class="codigo-sala">${YS.esc(t.codigoSala)}</code>
                           </div>`
                        : `<button class="ui-btn ui-btn--primary" onclick="generarSalaDesdeInicio(${t.idTurno})">Iniciar sesión</button>`
                    }
                </div>
                ${t.codigoSala ? `
                <div class="sala-codigo-fila">
                    <span class="texto-suave">Compartí este código con el paciente:</span>
                    <code class="codigo-sala codigo-sala--grande">${YS.esc(t.codigoSala)}</code>
                    <button class="ui-btn ui-btn--sm" onclick="copiarCodigo('${YS.esc(t.codigoSala)}')">Copiar</button>
                    <button class="ui-btn ui-btn--sm ui-btn--primary" onclick="entrarASesion(${t.idTurno}, ${t.idPaciente})">Entrar a la sala →</button>
                </div>` : ''}
            </div>`).join('')}
        </div>`;
}

function copiarCodigo(codigo) {
    navigator.clipboard.writeText(codigo).then(() => toast('Código copiado al portapapeles'));
}

async function generarSalaDesdeInicio(idTurno) {
    try {
        const r = await YS.fetch(`/turnos/${idTurno}/generar-sala`, { method: 'PUT' });
        if (r.ok) {
            const data = await r.json();
            toast(`Sala generada: ${data.codigoSala}. El paciente fue notificado.`);
            activarTab('inicio');
        } else toast('Error al generar la sala.', 'error');
    } catch { toast('Error de conexión.', 'error'); }
}

function citaUrgente() {
    const panel = document.getElementById('panel-urgente');
    const abrir = panel.classList.contains('oculto');
    panel.classList.toggle('oculto', !abrir);
    if (abrir) { panel.scrollIntoView({ behavior: 'smooth' }); document.getElementById('urgente-paciente').focus(); }
}

function cerrarCitaUrgente() {
    document.getElementById('panel-urgente').classList.add('oculto');
}

async function confirmarCitaUrgente() {
    const idPac = parseInt(document.getElementById('urgente-paciente').value);
    if (!idPac) { YS.form.marcar('urgente-paciente', 'Elegí el paciente.'); document.getElementById('urgente-paciente').focus(); return; }

    const resultado = document.getElementById('urgente-resultado');
    const boton = document.getElementById('btnUrgente');
    resultado.innerHTML = YS.cargando('Generando la sala…');
    boton.disabled = true;

    try {
        const ahora = new Date();
        // Fecha y hora locales (toISOString da la fecha en UTC: después de las 21 era "mañana")
        const fechaStr = YS.fecha.aISO(ahora);
        const horaStr = ahora.toTimeString().substring(0, 8);

        const rTurno = await YS.fetch(`/turnos/profesional-solicitar`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                idPaciente: idPac,
                idProfesional: idProfesional,
                fecha: fechaStr,
                hora: horaStr,
                motivo: 'Cita urgente',
                esUrgente: true
            })
        });

        if (!rTurno.ok) { toast(await YS.leerMensaje(rTurno, 'No se pudo crear el turno urgente.'), 'error'); resultado.innerHTML = ''; return; }

        const rTurnos = await YS.fetch(`/profesionales/${idProfesional}/turnos`);
        const turnos = await rTurnos.json();
        const turnoUrgente = turnos
            .filter(t => t.idPaciente === idPac)
            .sort((a, b) => b.idTurno - a.idTurno)[0];

        if (!turnoUrgente) { toast('Se creó el turno, pero no se pudo recuperarlo para generar la sala.', 'error'); resultado.innerHTML = ''; return; }

        // Sin consentimiento (RN-02) el turno no se confirma, pero la sesión sigue: se avisa, como en irASala()
        const rConf = await YS.fetch(`/turnos/${turnoUrgente.idTurno}/confirmar-paciente`, { method: 'PUT' });
        const sinConsentimiento = rConf.status === 409;
        const rSala = await YS.fetch(`/turnos/${turnoUrgente.idTurno}/generar-sala`, { method: 'PUT' });
        if (!rSala.ok) { toast(await YS.leerMensaje(rSala, 'No se pudo generar la sala.'), 'error'); resultado.innerHTML = ''; return; }
        const data = await rSala.json();

        toast(`Sala generada: ${data.codigoSala}`);

        // Ir a sesión con el paciente preseleccionado
        YS.shell.marcar('sesion');
        sesion(idPac, turnoUrgente.idTurno);
        if (sinConsentimiento) bannerSinConsentimiento();

    } catch { toast('No se pudo conectar con el servidor.', 'error'); resultado.innerHTML = ''; }
    finally { boton.disabled = false; }
}

// Pendientes de confirmación: lo que pidió el paciente y lo que propuso el profesional
function renderTurnosPendientes(turnos) {
    const pendientes = turnos.filter(t => t.idEstadoTurno === 1).sort((a, b) => momentoTurno(a) - momentoTurno(b));
    if (!pendientes.length) return YS.vacio({ icono: 'check', titulo: 'Nada para confirmar', texto: 'Cuando un paciente pida un turno lo vas a ver acá.' });

    return `<div class="turnos-lista">` + pendientes.map(t => {
        const fecha = YS.fecha.parse(t.fecha);
        return `
        <div class="turno-item">
            <div class="turno-fecha-bloque">
                <div class="turno-fecha-dia">${fecha.getDate()}</div>
                <div class="turno-fecha-mes">${fecha.toLocaleString('es-AR', {month:'short'})}</div>
            </div>
            <div class="turno-info">
                <div class="turno-paciente">${YS.esc(nombrePaciente(t.idPaciente))}</div>
                <div class="turno-meta">${fmtHora(t.hora)}${t.motivo ? ' · ' + YS.esc(t.motivo) : ''}</div>
            </div>
            ${t.creadoPor === 'profesional'
                ? '<span class="badge-estado estado-propuesto" title="Lo propusiste vos: espera la confirmación del paciente">Propuesto</span>'
                : '<span class="badge-estado badge-pendiente">Pendiente</span>'}
            <button class="btn-confirmar" onclick="${t.creadoPor === 'profesional' ? 'confirmarPropuestaPropia' : 'confirmarTurno'}(${t.idTurno})">Confirmar</button>
        </div>`;
    }).join('') + `</div>`;
}

// Turnos de hoy (sin los cancelados), con acceso a la sala
function renderPacientesHoy(turnos) {
    const turnosHoy = turnos
        .filter(t => YS.fecha.esHoy(t.fecha) && t.idEstadoTurno !== 3)
        .sort((a, b) => fmtHora(a.hora).localeCompare(fmtHora(b.hora)));
    if (!turnosHoy.length) return YS.vacio({ icono: 'agenda', titulo: 'No tenés pacientes para hoy', texto: 'Los turnos del día aparecen acá con el acceso a la sala.' });

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
                    ${turnosHoy.map(t => `
                        <tr>
                            <td><strong>${fmtHora(t.hora)} hs</strong></td>
                            <td>${YS.esc(nombrePaciente(t.idPaciente))}</td>
                            <td>${YS.esc(t.motivo ?? '—')}</td>
                            <td><span class="badge-estado estado-${t.idEstadoTurno}">${estadoLabel[t.idEstadoTurno] ?? '—'}</span></td>
                            <td class="celda-acciones">
                                <button class="ui-btn ui-btn--sm ui-btn--primary" onclick="irASala(${t.idTurno}, ${t.idPaciente})">Ir a sala</button>
                            </td>
                        </tr>`).join('')}
                </tbody>
            </table>
        </div>`;
}

async function irASala(idTurno, idPaciente) {

    //localStorage.setItem('sesion_idPaciente', idPaciente); //con esto le genera la sala directo al paciente

    // Si el turno no tiene sala, generarla primero
    let sinConsentimiento = false;
    try {
        const r = await YS.fetch(`/turnos/${idTurno}`);
        const turno = await r.json();

        if (!turno.codigoSala) {
            // Confirmar y generar sala. Sin consentimiento (RN-02) el turno no se confirma,
            // pero la sesión no se bloquea: se avisa al profesional.
            const rConf = await YS.fetch(`/turnos/${idTurno}/confirmar-paciente`, { method: 'PUT' });
            sinConsentimiento = rConf.status === 409;
            const rSala = await YS.fetch(`/turnos/${idTurno}/generar-sala`, { method: 'PUT' });
            if (rSala.ok) {
                const data = await rSala.json();
                toast(`Sala generada: ${data.codigoSala}. El paciente fue notificado.`);
            }
        } else {
            toast(`Sala ya generada: ${turno.codigoSala}`);
        }
    } catch { toast('Error al generar la sala.', 'error'); }


    YS.shell.marcar('sesion');
    sesion(idPaciente, idTurno);
    if (sinConsentimiento) bannerSinConsentimiento();
}

// Un turno que propuso el profesional lo confirma el paciente. Confirmarlo desde acá sigue siendo
// posible (como antes), pero ahora se pregunta: el paciente todavía no respondió.
async function confirmarPropuestaPropia(id) {
    if (!await confirmar('Este turno lo propusiste vos y el paciente todavía no lo confirmó. ¿Confirmarlo igual?',
                         { titulo: 'Confirmar sin respuesta del paciente', textoConfirmar: 'Confirmar igual', textoCancelar: 'Esperar al paciente' })) return;
    await confirmarTurno(id);
}

async function confirmarTurno(id) {
    try {
        const turno = await (await YS.fetch(`/turnos/${id}`)).json();
        turno.idEstadoTurno = 2;
        const r = await YS.fetch(`/turnos/${id}`, { method: 'PUT', headers: {'Content-Type':'application/json'}, body: JSON.stringify(turno) });
        if (r.ok) { toast('Turno confirmado'); activarTab('inicio'); }
        else if (r.status === 409) await avisoConsentimiento(r);
        else toast('Error al confirmar', 'error');
    } catch { toast('Error al confirmar', 'error'); }
}

async function agenda() {
    const c = document.getElementById('contenido');

    let slots = [];
    try { const r = await YS.fetch(`/profesionales/${idProfesional}/agenda`); slots = await r.json(); } catch {}

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
            ${!slots.length ? YS.vacio({
                icono: 'reloj', clase: 'agenda-sin-horarios',
                titulo: 'Todavía no cargaste tus horarios de trabajo',
                texto: 'Sin horarios, el calendario no sabe qué días y franjas ofrecerte turnos a tus pacientes.',
                accion: { texto: 'Mis horarios de trabajo', onclick: "activarTab('horarios')" }
            }) : ""}
            <!-- Arranca en la vista elegida (por defecto Semana) para que no aparezca el mes un instante -->
            <div id="vistaMesProf"${_vistaAgendaProf === 'mes' ? '' : ' class="oculto"'}><div class="cal-grid" id="calGridProfesional"></div></div>
            <div id="vistaSemanaProf"${_vistaAgendaProf === 'mes' ? ' class="oculto"' : ''}></div>
            <div class="agenda-leyenda">
                <span class="ui-chip ui-chip--warn">Pendiente</span>
                <span class="ui-chip ui-chip--marca">Propuesto por mí</span>
                <span class="ui-chip ui-chip--ok">Confirmado</span>
                <span class="ui-chip ui-chip--neutral">Cancelado</span>
            </div>
        </div>
        <div class="panel-turno-prof" id="panelTurnoProfesional"></div>
    `;

    await renderCalendario();
}

// Mis horarios de trabajo (antes, la segunda tarjeta de la agenda)
// ── Mis horarios de trabajo ─────────────────────────────────
// Resumen arriba; a la izquierda "Tu semana" (las franjas de cada día, que se tocan para editar) y a la derecha el
// calendario del mes con los horarios libres de cada día. Al editar o agregar, el formulario ocupa el lugar del calendario.
const HORARIOS_DIAS_CALENDARIO = 90;   // lo que devuelve como máximo /profesionales/{id}/horarios-libres

const minutosDe = h => { const [hh, mm] = fmtHora(h).split(':').map(Number); return (hh || 0) * 60 + (mm || 0); };
const horasTexto = min => { const h = min / 60; return (Number.isInteger(h) ? h : h.toFixed(1).replace('.', ',')) + ' h'; };
const ddmm = v => { const [a, m, d] = fechaISO(v).split('-'); return `${d}/${m}`; };
const horaDeMinutos = min => `${String(Math.floor(min / 60)).padStart(2, '0')}:${String(min % 60).padStart(2, '0')}`;

// ¿Vale la franja ese día? (fechas de inicio y de fin inclusive; vacías = sin límite), igual que TurnoReglasService.VigenteEl.
function franjaVigenteEl(s, iso) {
    const desde = fechaISO(s.fechaInicio), hasta = fechaISO(s.fechaFin);
    return (!desde || iso >= desde) && (!hasta || iso <= hasta);
}

// Los horarios de una franja (cada IntervaloMinutos, mientras el turno entero entre antes de la hora de fin), en "HH:mm".
function horariosDeFranja(s) {
    const intervalo = s.intervaloMinutos > 0 ? s.intervaloMinutos : 30;
    const lista = [];
    for (let m = minutosDe(s.horaInicio); m + intervalo <= minutosDe(s.horaFin); m += intervalo) lista.push(horaDeMinutos(m));
    return lista;
}

let _horCal = null;   // estado del calendario: mes a la vista, libres por día y día elegido

async function horarios() {
    const c = document.getElementById('contenido');

    let slots = [], libres = [];
    await Promise.all([
        YS.fetch(`/profesionales/${idProfesional}/agenda`).then(r => r.json()).then(x => { slots = x; }).catch(() => {}),
        YS.fetch(`/profesionales/${idProfesional}/horarios-libres?dias=${HORARIOS_DIAS_CALENDARIO}`).then(r => r.json()).then(x => { libres = x; }).catch(() => {})
    ]);

    // La base guarda el día con distintas formas ('lunes', 'Lunes', 'miercoles'):
    // se ordena por el número de día, no por el texto.
    slots.sort((a, b) => YS.dias.orden(a.dia) - YS.dias.orden(b.dia) || fmtHora(a.horaInicio).localeCompare(fmtHora(b.horaInicio)));
    _agendaSlotsProf = slots;
    if (YS.shell.actual !== 'horarios') return;

    YS.shell.acciones([{ texto: 'Agregar horario', icono: 'mas', tipo: 'primary', alClick: () => abrirFormNuevoHorario() }]);

    const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
    const hoyISO = YS.fecha.aISO(hoy);
    const conAdmision = YS.sesion.tienePrivilegio('admision') || slots.some(s => s.esAdmision);
    const validas = slots.filter(s => s.horaInicio && s.horaFin && YS.dias.orden(s.dia) < 7);
    const activas = validas.filter(s => s.disponible && (!fechaISO(s.fechaFin) || fechaISO(s.fechaFin) >= hoyISO));
    const suma = lista => lista.reduce((t, s) => ({ min: t.min + minutosDe(s.horaFin) - minutosDe(s.horaInicio), n: t.n + (calcularTurnos(s) || 0) }), { min: 0, n: 0 });
    const turnos = suma(activas.filter(s => !s.esAdmision));
    const admision = suma(activas.filter(s => s.esAdmision));

    // Libres por día ("HH:mm" de cada uno), para el resumen de 14 días y para el calendario.
    const libresPorDia = new Map();
    libres.forEach(iso => {
        const d = new Date(iso), k = YS.fecha.aISO(d);
        if (!libresPorDia.has(k)) libresPorDia.set(k, new Set());
        libresPorDia.get(k).add(YS.fecha.hora(d));
    });
    _horCal = { anio: hoy.getFullYear(), mes: hoy.getMonth(), validas, libresPorDia, elegido: null };

    let total14 = 0, libres14 = 0;
    for (let i = 0; i < 14; i++) {
        const d = new Date(hoy); d.setDate(d.getDate() + i);
        const dia = diaDelCalendario(d);
        total14 += dia.total; libres14 += dia.libres;
    }

    // El próximo cambio de la agenda: una franja que empieza o que termina.
    const cambio = validas.flatMap(s => [
        fechaISO(s.fechaInicio) > hoyISO ? { iso: fechaISO(s.fechaInicio), s, texto: `Empieza el ${ddmm(s.fechaInicio)}` } : null,
        fechaISO(s.fechaFin) >= hoyISO ? { iso: fechaISO(s.fechaFin), s, texto: `Termina el ${ddmm(s.fechaFin)}` } : null
    ]).filter(Boolean).sort((a, b) => a.iso.localeCompare(b.iso))[0];

    const tarjeta = (etiqueta, valor, pie, tono = '') => `
        <div class="ui-stat${tono ? ' ui-stat--' + tono : ''}">
            <span class="ui-stat-etiqueta">${etiqueta}</span>
            <span class="ui-stat-valor">${valor}</span>
            <span class="ui-stat-pie">${pie}</span>
        </div>`;

    c.innerHTML = `
        ${slots.length ? `<div class="ui-stats">
            ${tarjeta(`${YS.icono('pacientes')}Atención de pacientes`, `${horasTexto(turnos.min)} <span class="horario-stat-unidad">por semana</span>`, `${turnos.n} turno${turnos.n === 1 ? '' : 's'}`)}
            ${conAdmision ? tarjeta(`${YS.icono('portapapeles')}Entrevistas de admisión`, `${horasTexto(admision.min)} <span class="horario-stat-unidad">por semana</span>`, `${admision.n} entrevista${admision.n === 1 ? '' : 's'}`) : ''}
            ${tarjeta(`${YS.icono('agenda')}Libres en los próximos 14 días`, libres14, total14 ? `de ${total14} turnos · ${Math.round(libres14 / total14 * 100)} % libre` : 'Sin turnos en esos días')}
            ${cambio
                ? tarjeta(`${YS.icono('reloj')}Próximo cambio`, `<span class="horario-stat-texto">${YS.esc(YS.dias.nombre(cambio.s.dia))} ${fmtHora(cambio.s.horaInicio)} a ${fmtHora(cambio.s.horaFin)}</span>`, YS.esc(cambio.texto), 'warn')
                : tarjeta(`${YS.icono('reloj')}Próximo cambio`, '—', 'Ninguna franja empieza ni termina')}
        </div>` : ''}

        <div class="horarios-columnas">
            <section class="card" aria-labelledby="horarios-semana-titulo">
                <div class="titulo-seccion titulo-seccion--tarjeta" id="horarios-semana-titulo">Tu semana</div>
                ${slots.length ? '' : '<p class="texto-suave mb-12">Todavía no cargaste horarios. Tocá el + de un día para agregar en qué horario atendés.</p>'}
                ${htmlSemanaLista(validas, hoyISO)}
                <div class="horarios-leyenda">
                    <span><span class="horario-muestra horario-muestra--turnos"></span>Turnos de pacientes</span>
                    ${conAdmision ? '<span><span class="horario-muestra horario-muestra--admision"></span>Admisión</span>' : ''}
                    <span><span class="horario-muestra horario-muestra--fuera"></span>Todavía no empieza, vencido o no disponible</span>
                </div>
                ${slots.length ? `<details class="horarios-lista">
                    <summary>Ver los horarios como tabla</summary>
                    ${htmlListaHorarios(slots)}
                </details>` : ''}
            </section>

            <div class="horarios-lateral">
                <section class="card horario-panel oculto" id="horario-panel" aria-label="Franja elegida">${htmlFormHorario()}</section>
                <section class="card" id="horarios-calendario" aria-labelledby="horarios-cal-titulo">
                    <div class="horarios-cal-cabecera">
                        <div class="titulo-seccion titulo-seccion--tarjeta titulo-seccion--sin-margen" id="horarios-cal-titulo">Así te ven los pacientes</div>
                        <div class="horarios-cal-nav">
                            <button type="button" class="ui-btn ui-btn--sm ui-btn--ghost" id="horarios-cal-anterior" onclick="moverMesHorarios(-1)" aria-label="Mes anterior">&#8249;</button>
                            <span id="horarios-cal-mes" aria-live="polite"></span>
                            <button type="button" class="ui-btn ui-btn--sm ui-btn--ghost" id="horarios-cal-siguiente" onclick="moverMesHorarios(1)" aria-label="Mes siguiente">&#8250;</button>
                        </div>
                    </div>
                    <div class="horarios-cal" id="horarios-cal-grilla"></div>
                    <div id="horarios-cal-detalle" class="horarios-cal-detalle" aria-live="polite"></div>
                </section>
            </div>
        </div>`;

    ['fh-inicio','fh-fin','fh-intervalo'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', actualizarPreviewNuevo);
    });
    dibujarCalendarioHorarios();
    if (!slots.length) abrirFormNuevoHorario();
}

// "Tu semana": una fila por día con sus franjas como botones; un día sin franjas tiene + para agregar una ese día.
function htmlSemanaLista(validas, hoyISO) {
    return `<ul class="horarios-semana-lista">${YS.dias.nombres.map((_, i) => (i + 1) % 7).map(numDia => {
        const nombre = YS.dias.nombres[numDia];
        const delDia = validas.filter(s => YS.dias.numero(s.dia) === numDia);
        const franjas = delDia.map(s => {
            const desdeF = fechaISO(s.fechaInicio), hastaF = fechaISO(s.fechaFin);
            const fuera = !s.disponible || (desdeF && desdeF > hoyISO) || (hastaF && hastaF < hoyISO);
            const vig = !s.disponible ? 'no disponible'
                      : hastaF && hastaF < hoyISO ? `venció el ${ddmm(hastaF)}`
                      : desdeF && desdeF > hoyISO ? `desde el ${ddmm(desdeF)}`
                      : hastaF ? `hasta el ${ddmm(hastaF)}` : '';
            const cant = calcularTurnos(s);
            const detalle = s.esAdmision ? 'admisión' : `${cant} turno${cant === 1 ? '' : 's'}`;
            const clase = ['horario-franja', s.esAdmision ? 'horario-franja--admision' : 'horario-franja--turnos', fuera ? 'horario-franja--fuera' : ''].join(' ').trim();
            return `<button type="button" class="${clase}" data-id="${s.idAgenda}" onclick="editarHorarioPorId(${s.idAgenda})"
                        aria-label="Editar ${YS.esc(nombre)} de ${fmtHora(s.horaInicio)} a ${fmtHora(s.horaFin)}, ${YS.esc(detalle)}${vig ? ', ' + YS.esc(vig) : ''}">
                        ${fmtHora(s.horaInicio)}–${fmtHora(s.horaFin)} · ${YS.esc(detalle)}${vig ? ` · ${YS.esc(vig)}` : ''}
                    </button>`;
        }).join('');
        return `<li class="horarios-semana-dia${delDia.length ? '' : ' horarios-semana-dia--libre'}">
            <span class="horarios-semana-nombre">${YS.esc(nombre)}</span>
            <span class="horarios-semana-franjas">${franjas || '<span class="texto-suave">No atendés</span>'}</span>
            <button type="button" class="ui-btn ui-btn--sm ui-btn--ghost horarios-semana-mas" onclick="abrirFormNuevoHorario('${nombre}')" aria-label="Agregar un horario el ${YS.esc(nombre.toLowerCase())}">+</button>
        </li>`;
    }).join('')}</ul>`;
}

// Un día del calendario: sus horarios de turnos (de las franjas que valen ese día), cuántos siguen libres y si atiende admisión.
function diaDelCalendario(d) {
    const iso = YS.fecha.aISO(d);
    const franjas = _horCal.validas.filter(s => s.disponible && YS.dias.numero(s.dia) === d.getDay() && franjaVigenteEl(s, iso));
    const horas = [...new Set(franjas.filter(s => !s.esAdmision).flatMap(horariosDeFranja))].sort();
    const libres = _horCal.libresPorDia.get(iso) || new Set();
    return { iso, horas, total: horas.length, libres: horas.filter(h => libres.has(h)).length, libresSet: libres, admision: franjas.some(s => s.esAdmision) };
}

function dibujarCalendarioHorarios() {
    const grilla = document.getElementById('horarios-cal-grilla');
    if (!grilla || !_horCal) return;
    const { anio, mes } = _horCal;
    const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
    const limite = new Date(hoy); limite.setDate(limite.getDate() + (_horCal.dias || HORARIOS_DIAS_CALENDARIO) - 1);
    const diaPropuesto = _horCal.propuesto ? YS.fecha.aISO(_horCal.propuesto) : null;

    document.getElementById('horarios-cal-mes').textContent = YS.capitalizar(new Date(anio, mes, 1).toLocaleDateString('es-AR', { month: 'long', year: 'numeric' }));
    document.getElementById('horarios-cal-anterior').disabled = anio === hoy.getFullYear() && mes === hoy.getMonth();
    document.getElementById('horarios-cal-siguiente').disabled = anio === limite.getFullYear() && mes === limite.getMonth();

    const primero = new Date(anio, mes, 1);
    const enMes = new Date(anio, mes + 1, 0).getDate();
    let html = ['L', 'M', 'M', 'J', 'V', 'S', 'D'].map(x => `<span class="horarios-cal-encabezado" aria-hidden="true">${x}</span>`).join('');
    for (let i = 0; i < (primero.getDay() + 6) % 7; i++) html += '<span></span>';
    for (let n = 1; n <= enMes; n++) {
        const d = new Date(anio, mes, n);
        const dia = diaDelCalendario(d);
        const pasado = d < hoy, fuera = d > limite;
        const clases = ['horarios-cal-dia',
            pasado || fuera ? 'horarios-cal-dia--pasado' : dia.total ? 'horarios-cal-dia--turnos' : dia.admision ? 'horarios-cal-dia--admision' : '',
            d.getTime() === hoy.getTime() ? 'horarios-cal-dia--hoy' : '',
            diaPropuesto === dia.iso ? 'horarios-cal-dia--propuesta' : '',
            _horCal.elegido === dia.iso ? 'horarios-cal-dia--elegido' : ''].filter(Boolean).join(' ');
        const pie = pasado || fuera ? '' : diaPropuesto === dia.iso ? 'propuesto'
                  : dia.total ? `${dia.libres} libre${dia.libres === 1 ? '' : 's'}` : dia.admision ? 'adm' : '';
        const etiqueta = `${n}${pie ? ', ' + (pie === 'adm' ? 'admisión' : pie) : ''}`;
        html += pasado || fuera || (!dia.total && !dia.admision)
            ? `<span class="${clases}"><span class="horarios-cal-num">${n}</span></span>`
            : `<button type="button" class="${clases}" onclick="elegirDiaHorarios('${dia.iso}')" aria-label="${YS.esc(etiqueta)}"><span class="horarios-cal-num">${n}</span><span class="horarios-cal-pie">${pie}</span></button>`;
    }
    grilla.innerHTML = html;
    dibujarDetalleDiaHorarios();
}

function moverMesHorarios(delta) {
    const d = new Date(_horCal.anio, _horCal.mes + delta, 1);
    _horCal.anio = d.getFullYear(); _horCal.mes = d.getMonth(); _horCal.elegido = null;
    dibujarCalendarioHorarios();
}

function elegirDiaHorarios(iso) {
    _horCal.elegido = _horCal.elegido === iso ? null : iso;
    dibujarCalendarioHorarios();
}

// Debajo del calendario: los horarios del día elegido, con los tomados marcados.
function dibujarDetalleDiaHorarios() {
    const cont = document.getElementById('horarios-cal-detalle');
    if (!cont) return;
    if (!_horCal.elegido) { cont.innerHTML = '<p class="texto-suave texto-chico mb-0">Tocá un día para ver sus horarios.</p>'; return; }
    const d = YS.fecha.parse(_horCal.elegido);
    const dia = diaDelCalendario(d);
    const ahora = new Date();
    const propuesta = _horCal.propuesto && YS.fecha.aISO(_horCal.propuesto) === dia.iso ? YS.fecha.hora(_horCal.propuesto) : null;
    const chips = dia.horas.map(h => {
        const [hh, mm] = h.split(':').map(Number);
        const paso = new Date(d.getFullYear(), d.getMonth(), d.getDate(), hh, mm) <= ahora;
        return h === propuesta ? `<span class="horarios-hora horarios-hora--propuesta" title="El horario que se le propone al paciente">${h} · propuesto</span>`
             : paso ? `<span class="horarios-hora horarios-hora--pasada">${h}</span>`
             : dia.libresSet.has(h) ? `<span class="horarios-hora">${h}</span>`
             : `<span class="horarios-hora horarios-hora--tomada">${h} tomado</span>`;
    }).join('');
    const tomados = dia.horas.length - dia.libres;
    cont.innerHTML = `
        <div class="texto-fuerte mb-4">${YS.esc(YS.capitalizar(YS.fecha.larga(d)))}</div>
        ${dia.total ? `<div class="texto-suave texto-chico mb-8">${dia.libres} libre${dia.libres === 1 ? '' : 's'} de ${dia.total}${tomados ? ` · ${tomados} ocupado${tomados === 1 ? '' : 's'} o ya pasado${tomados === 1 ? '' : 's'}` : ''}</div>
        <div class="horarios-horas-dia">${chips}</div>` : ''}
        ${dia.admision ? `<p class="texto-suave texto-chico ${dia.total ? 'mt-8 ' : ''}mb-0">Ese día tenés franjas de admisión: ahí se programan solas las entrevistas, no se ofrecen turnos.</p>` : ''}`;
}

function htmlListaHorarios(slots) {
    return `<div class="tabla-wrapper mt-8">
        <table>
            <thead><tr><th>Día</th><th>Hora inicio</th><th>Hora fin</th><th>Vigencia</th><th>Intervalo</th><th>Turnos/día</th><th>Uso</th><th>Disponible</th><th></th></tr></thead>
            <tbody>
                ${slots.map(s => `
                <tr id="slot-row-${s.idAgenda}">
                    <td>${YS.esc(YS.dias.nombre(s.dia))}</td>
                    <td>${fmtHora(s.horaInicio)}</td>
                    <td>${fmtHora(s.horaFin)}</td>
                    <td>${vigenciaHtml(s)}</td>
                    <td>${s.intervaloMinutos ?? 30} min</td>
                    <td>${calcularTurnos(s)}</td>
                    <td>${s.esAdmision ? '<span class="ui-chip ui-chip--info">Admisión</span>' : '<span class="ui-chip ui-chip--neutral">Turnos</span>'}</td>
                    <td><span class="badge-disponible ${s.disponible ? 'si' : 'no'}">${s.disponible ? 'Sí' : 'No'}</span></td>
                    <td class="celda-acciones">
                        <button class="btn-ver" onclick="editarHorarioPorId(${s.idAgenda})">Editar</button>
                        <button class="btn-peligro" onclick="eliminarHorario(${s.idAgenda})">Eliminar</button>
                    </td>
                </tr>`).join('')}
            </tbody>
        </table>
    </div>`;
}

// El formulario de la franja: ocupa el lugar del calendario mientras se edita o se agrega (mostrarFormHorario).
function htmlFormHorario() {
    return `
        <div id="form-horario" class="form-horario oculto">
            <div class="form-horario-titulo" id="form-horario-titulo">Agregar horario</div>
            <input type="hidden" id="fh-id">
            <div class="form-fila">
                <div class="ui-campo">
                    <label for="fh-dia">Día</label>
                    <select id="fh-dia" class="ui-select">
                        <option>Lunes</option><option>Martes</option><option>Miércoles</option>
                        <option>Jueves</option><option>Viernes</option><option>Sábado</option><option>Domingo</option>
                    </select>
                </div>
                <div class="ui-campo">
                    <label for="fh-disponible">Disponible</label>
                    <select id="fh-disponible" class="ui-select">
                        <option value="true">Sí</option>
                        <option value="false">No</option>
                    </select>
                </div>
                <div class="ui-campo">
                    <label for="fh-inicio">Hora inicio</label>
                    <input type="time" id="fh-inicio" class="ui-input">
                </div>
                <div class="ui-campo">
                    <label for="fh-fin">Hora fin</label>
                    <input type="time" id="fh-fin" class="ui-input">
                </div>
                <div class="ui-campo">
                    <label for="fh-fecha-inicio">Fecha de inicio</label>
                    <input type="date" id="fh-fecha-inicio" class="ui-input" aria-describedby="fh-vigencia-ayuda">
                </div>
                <div class="ui-campo">
                    <label for="fh-fecha-fin">Fecha de fin</label>
                    <input type="date" id="fh-fecha-fin" class="ui-input" aria-describedby="fh-vigencia-ayuda">
                </div>
                <!-- Solo con el privilegio Admisión: una franja de admisión no se ofrece a los pacientes; ahí se programan
                     solas, una detrás de otra, las primeras entrevistas que agendás. -->
                <div class="ui-campo${YS.sesion.tienePrivilegio('admision') ? '' : ' oculto'}">
                    <label for="fh-uso">Uso</label>
                    <select id="fh-uso" class="ui-select">
                        <option value="turnos">Turnos de pacientes</option>
                        <option value="admision">Entrevistas de admisión</option>
                    </select>
                </div>
                <div class="ui-campo">
                    <label for="fh-intervalo">Intervalo de atención</label>
                    <select id="fh-intervalo" class="ui-select">
                        <option value="15">15 minutos</option>
                        <option value="20">20 minutos</option>
                        <option value="30" selected>30 minutos</option>
                        <option value="45">45 minutos</option>
                        <option value="60">60 minutos</option>
                        <option value="90">90 minutos</option>
                    </select>
                </div>
            </div>
            <p id="fh-vigencia-ayuda" class="texto-suave texto-chico mt-8 mb-0">Fechas de inicio y fin: desde y hasta qué día vale este horario. Si las dejás vacías, vale siempre.</p>
            <div id="fh-preview" class="form-preview" aria-live="polite"></div>
            <div class="form-acciones">
                <button class="ui-btn ui-btn--primary" id="fh-guardar" onclick="guardarHorario()">Guardar</button>
                <button class="ui-btn oculto" id="fh-duplicar" onclick="duplicarHorario()">Duplicar a otro día</button>
                <button class="ui-btn ui-btn--danger oculto" id="fh-eliminar" onclick="eliminarHorario(document.getElementById('fh-id').value)">Eliminar</button>
                <button class="ui-btn" onclick="cerrarFormHorario()">Cancelar</button>
            </div>
        </div>`;
}

function editarHorarioPorId(id) {
    const s = (_agendaSlotsProf || []).find(x => x.idAgenda === id);
    if (s) editarHorario(s.idAgenda, s.dia, fmtHora(s.horaInicio), fmtHora(s.horaFin), s.disponible, s.intervaloMinutos ?? 30, !!s.esAdmision);
}

// La franja que se está editando pasa a ser una nueva, con los mismos datos: solo falta elegir el día.
function duplicarHorario() {
    document.getElementById('fh-id').value = '';
    document.getElementById('form-horario-titulo').textContent = 'Agregar horario (copia)';
    document.getElementById('fh-duplicar').classList.add('oculto');
    document.getElementById('fh-eliminar').classList.add('oculto');
    document.querySelectorAll('.horario-franja--elegida').forEach(b => b.classList.remove('horario-franja--elegida'));
    document.getElementById('fh-dia').focus();
    toast('Elegí el día de la copia y guardá.', 'info');
}

// dia: el del + de "Tu semana" ('Miércoles'); sin él, lunes.
function abrirFormNuevoHorario(dia = 'Lunes') {
    document.getElementById('fh-id').value = '';
    document.getElementById('fh-dia').value = dia;
    document.getElementById('fh-inicio').value = '';
    document.getElementById('fh-fin').value = '';
    document.getElementById('fh-fecha-inicio').value = YS.fecha.aISO(new Date());
    document.getElementById('fh-fecha-fin').value = '';
    document.getElementById('fh-intervalo').value = '30';
    document.getElementById('fh-disponible').value = 'true';
    document.getElementById('fh-uso').value = 'turnos';
    document.getElementById('form-horario-titulo').textContent = 'Agregar horario';
    document.getElementById('fh-preview').textContent = '';
    mostrarFormHorario();
    document.getElementById('fh-inicio').focus();
}

function editarHorario(id, dia, inicio, fin, disponible, intervalo = 30, esAdmision = false) {
    document.getElementById('fh-id').value = id;
    // Las opciones del select son 'Lunes'…'Domingo'; si la base guardó 'miercoles', sin
    // esto el select quedaba en la primera opción y al guardar el horario pasaba a lunes.
    document.getElementById('fh-dia').value = YS.dias.nombre(dia);
    document.getElementById('fh-inicio').value = inicio;
    document.getElementById('fh-fin').value = fin;
    const slot = (_agendaSlotsProf || []).find(x => x.idAgenda === id);
    document.getElementById('fh-fecha-inicio').value = fechaISO(slot?.fechaInicio);
    document.getElementById('fh-fecha-fin').value = fechaISO(slot?.fechaFin);
    document.getElementById('fh-disponible').value = disponible ? 'true' : 'false';
    document.getElementById('fh-intervalo').value = intervalo;
    document.getElementById('fh-uso').value = esAdmision ? 'admision' : 'turnos';
    document.getElementById('form-horario-titulo').textContent = 'Editar horario';
    mostrarFormHorario();
    actualizarPreviewNuevo();
}

// El formulario ocupa el lugar del calendario; Guardar o Cancelar lo devuelven.
function mostrarFormHorario() {
    const form = document.getElementById('form-horario');
    YS.form.limpiarTodo(form);
    form.classList.remove('oculto');
    document.getElementById('horario-panel')?.classList.remove('oculto');
    document.getElementById('horarios-calendario')?.classList.add('oculto');
    // Duplicar y Eliminar son de una franja que ya existe.
    const id = document.getElementById('fh-id').value;
    document.getElementById('fh-duplicar').classList.toggle('oculto', !id);
    document.getElementById('fh-eliminar').classList.toggle('oculto', !id);
    document.querySelectorAll('.horario-franja').forEach(b => b.classList.toggle('horario-franja--elegida', !!id && b.dataset.id === id));
    document.getElementById('horario-panel')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    document.getElementById('fh-dia').focus();
}

function cerrarFormHorario() {
    document.getElementById('form-horario').classList.add('oculto');
    document.getElementById('horario-panel')?.classList.add('oculto');
    document.getElementById('horarios-calendario')?.classList.remove('oculto');
    document.querySelectorAll('.horario-franja--elegida').forEach(b => b.classList.remove('horario-franja--elegida'));
}

async function guardarHorario() {
    const id       = document.getElementById('fh-id').value;
    const dia      = document.getElementById('fh-dia').value;
    const inicio   = document.getElementById('fh-inicio').value;
    const fin      = document.getElementById('fh-fin').value;
    const disp     = document.getElementById('fh-disponible').value === 'true';
    const intervalo = parseInt(document.getElementById('fh-intervalo')?.value ?? 30);
    const desde    = document.getElementById('fh-fecha-inicio').value;
    const hasta    = document.getElementById('fh-fecha-fin').value;

    const valido = YS.form.validar([
        { id: 'fh-inicio', requerido: 'Elegí la hora de inicio.' },
        { id: 'fh-fin', requerido: 'Elegí la hora de fin.', validar: v => inicio && v && v <= inicio ? 'Tiene que ser después de la hora de inicio.' : null },
        { id: 'fh-fecha-fin', validar: v => desde && v && v < desde ? 'No puede ser anterior a la fecha de inicio.' : null }
    ]);
    if (!valido) return;

    const body = {
        idAgenda: id ? parseInt(id) : 0,
        idProfesional,
        dia,
        horaInicio: inicio + ':00',
        horaFin: fin + ':00',
        disponible: disp,
        intervaloMinutos: intervalo,
        esAdmision: document.getElementById('fh-uso').value === 'admision',
        fechaInicio: desde || null,
        fechaFin: hasta || null
    };

    const url    = id ? `${API}/agendas/${id}` : `${API}/agendas`;
    const method = id ? 'PUT' : 'POST';

    const boton = document.getElementById('fh-guardar');
    boton.disabled = true;
    try {
        const r = await YS.fetch(url, { method, headers: {'Content-Type':'application/json'}, body: JSON.stringify(body) });
        if (r.ok) { toast(id ? 'Horario actualizado' : 'Horario agregado'); activarTab('horarios'); }
        else toast(await YS.leerMensaje(r, 'No se pudo guardar el horario.'), 'error');
    } catch { toast('No se pudo conectar con el servidor.', 'error'); }
    finally { boton.disabled = false; }
}

async function eliminarHorario(id) {
    const ok = await confirmar('¿Eliminar este horario? Los turnos ya dados no cambian.', { titulo: 'Eliminar horario', textoConfirmar: 'Eliminar', peligro: true });
    if (!ok) return;
    try {
        const r = await YS.fetch(`/agendas/${id}`, { method:'DELETE' });
        if (r.ok) { toast('Horario eliminado', 'warn'); activarTab('horarios'); }
        else toast(await YS.leerMensaje(r, 'No se pudo eliminar el horario.'), 'error');
    } catch { toast('No se pudo conectar con el servidor.', 'error'); }
}

// Vigencia de una franja (migración 27): "Siempre", "Desde 01/11/2026", "Hasta 31/12/2026" o las dos; vencida si ya terminó.
const fechaISO = v => v ? String(v).substring(0, 10) : '';
function vigenciaHtml(s) {
    const dmy = v => { const [a, m, d] = fechaISO(v).split('-'); return `${d}/${m}/${a}`; };
    const desde = fechaISO(s.fechaInicio), hasta = fechaISO(s.fechaFin);
    if (!desde && !hasta) return '<span class="ui-muted">Siempre</span>';
    const texto = desde && hasta ? `${dmy(desde)} al ${dmy(hasta)}` : desde ? `Desde ${dmy(desde)}` : `Hasta ${dmy(hasta)}`;
    const hoy = YS.fecha.aISO(new Date());
    const chip = hasta && hasta < hoy ? ' <span class="ui-chip ui-chip--neutral">Vencido</span>'
               : desde && desde > hoy ? ' <span class="ui-chip ui-chip--info">Todavía no empieza</span>' : '';
    return `<span class="ui-num">${YS.esc(texto)}</span>${chip}`;
}

function fmtHora(h) {
    if (!h) return '—';
    return h.substring(0, 5);
}

// ── HISTORIA CLÍNICA ────────────────────────────────────────
// Modal del sistema (se cierra con Esc o con clic afuera) con tres pestañas.
async function verHistoria(idPaciente) {
    const cuerpo = YS.el('div', { class: 'hc', html: YS.cargando('Cargando la historia clínica…') });
    const m = YS.modal({
        titulo: 'Historia clínica', cuerpo, tamano: 'grande', clase: 'modal-historia',
        acciones: [
            { texto: 'Abrir sesión de video', alClick: c => { c.cerrar(); abrirSesionCon(idPaciente); } },
            { texto: 'Cerrar', tipo: 'primary', foco: true, alClick: c => c.cerrar() }
        ]
    });
    const [paciente, historia, sesiones] = await Promise.all([
        YS.api(`/pacientes/${idPaciente}`).catch(() => null),
        YS.api(`/pacientes/${idPaciente}/historiaclinica`, { permitir404: true }).catch(() => null),
        YS.api(`/pacientes/${idPaciente}/sesiones-clinicas`).catch(() => [])
    ]);
    const evoluciones = historia?.idHistoria
        ? await YS.api(`/historiasclinicas/${historia.idHistoria}/evoluciones`).catch(() => [])
        : [];
    if (m.cerrado) return;
    if (paciente) m.elemento.querySelector('.ui-modal-titulo').textContent = `Historia clínica · ${YS.nombreCompleto(paciente)}`;

    const pestanas = [
        { id: 'datos', texto: 'Datos del paciente', html: renderDatosPaciente(paciente) },
        { id: 'evoluciones', texto: 'Evoluciones', n: evoluciones.length, html: renderEvoluciones(evoluciones) },
        { id: 'sesiones', texto: 'Sesiones clínicas', n: sesiones.length, html: renderSesionesClinicas(sesiones) }
    ];
    cuerpo.innerHTML = `
        <div class="ui-tabs" role="tablist" aria-label="Historia clínica">
            ${pestanas.map((p, i) => `<button type="button" class="ui-tab${i ? '' : ' activo'}" role="tab" id="hc-tab-${p.id}"
                aria-controls="hc-panel-${p.id}" aria-selected="${!i}" data-tab="${p.id}">${p.texto}${p.n != null
                ? ` <span class="ui-contador ui-contador--suave">${p.n}</span>` : ''}</button>`).join('')}
        </div>
        ${pestanas.map((p, i) => `<div class="hc-panel${i ? ' oculto' : ''}" role="tabpanel" id="hc-panel-${p.id}" aria-labelledby="hc-tab-${p.id}">${p.html}</div>`).join('')}`;
    cuerpo.querySelector('.ui-tabs').addEventListener('click', e => {
        const b = e.target.closest('[data-tab]');
        if (!b) return;
        cuerpo.querySelectorAll('.ui-tab').forEach(t => {
            t.classList.toggle('activo', t === b);
            t.setAttribute('aria-selected', t === b);
        });
        cuerpo.querySelectorAll('.hc-panel').forEach(p => p.classList.toggle('oculto', p.id !== 'hc-panel-' + b.dataset.tab));
    });
}

// Sesión de video con el paciente ya elegido (desde Mis pacientes o desde la historia clínica)
function abrirSesionCon(idPaciente) {
    YS.shell.marcar('sesion');
    sesion(idPaciente);
}

// Sesión de video de un turno que ya tiene sala (desde "Sesiones de hoy")
function entrarASesion(idTurno, idPaciente) {
    YS.shell.marcar('sesion');
    sesion(idPaciente, idTurno);
}

function renderSesionesClinicas(lista) {
    if (!lista?.length) return YS.vacio({ icono: 'video', texto: 'Todavía no hay sesiones clínicas registradas.' });
    return lista.map(s => {
        let emociones = '';
        try {
            const emo = JSON.parse(s.historialEmociones || '{}');
            emociones = Object.entries(emo).map(([k,v]) =>
                '<span class="ui-chip ui-chip--neutral ui-chip--sin-punto chip-emocion">' + YS.esc(k) + ': <strong>' + YS.esc(v) + '%</strong></span>'
            ).join('');
        } catch {}
        return '<div class="evolucion-item">' +
            '<div class="evolucion-fecha">' +
                new Date(s.fechaHora).toLocaleDateString('es-AR', {weekday:'long',day:'numeric',month:'long',year:'numeric'}) +
                ' &nbsp;·&nbsp; ' + new Date(s.fechaHora).toLocaleTimeString('es-AR',{hour:'2-digit',minute:'2-digit'}) +
                (s.tipoCierre ? ' &nbsp;<span class="ui-chip ui-chip--info ui-chip--sin-punto">' + YS.esc(s.tipoCierre) + '</span>' : '') +
            '</div>' +
            (emociones ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Promedio de emociones</div><div class="flex-gap mt-8">' + emociones + '</div></div>' : '') +
            (s.transcripcion ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Transcripción</div><div class="evolucion-bloque-texto transcripcion">' + YS.esc(s.transcripcion) + '</div></div>' : '') +
            (s.notasEvolucion ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Notas del médico</div><div class="evolucion-bloque-texto">' + YS.esc(s.notasEvolucion) + '</div></div>' : '') +
        '</div>';
    }).join('');
}

function renderDatosPaciente(p) {
    if (!p) return YS.vacio({ icono: 'usuario', texto: 'No se pudieron cargar los datos del paciente.' });
    const dato = (etiqueta, valor) => `
        <div class="ui-dato"><span class="ui-dato-etiqueta">${etiqueta}</span>
            <span class="ui-dato-valor${valor ? '' : ' vacio'}">${YS.esc(valor || '—')}</span></div>`;
    return `<div class="ui-datos">
        ${dato('Nombre', YS.nombreCompleto(p))}
        ${dato('DNI', p.dni)}
        ${dato('Fecha de nacimiento', p.fechaNacimiento ? YS.fecha.corta(p.fechaNacimiento) : '')}
        ${dato('Estado', p.estado)}
        ${dato('Mail', p.mail)}
        ${dato('Teléfono', p.telefono)}
        ${dato('Domicilio', p.domicilio)}
    </div>`;
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
    const cuerpo = YS.el('div', { id: 'avisoConsentimiento' }, YS.el('p', { class: 'mb-12', text: mensaje }));
    if (url) {
        const input = YS.el('input', { class: 'ui-input', id: 'linkConsentimiento', readonly: true, value: url });
        cuerpo.append(
            YS.el('label', { class: 'ui-label', for: 'linkConsentimiento', text: 'Compartile al paciente este link para que lo firme desde su panel:' }),
            YS.el('div', { class: 'dialogo-link mt-8' }, input,
                YS.el('button', { class: 'ui-btn', type: 'button', text: 'Copiar', on: { click: () => { input.select(); YS.copiar(url, 'Link copiado'); } } })));
    }
    YS.modal({
        titulo: 'No se puede confirmar el turno', cuerpo, rol: 'alertdialog', enfocarCampo: false,
        acciones: [{ texto: 'Entendido', tipo: 'primary', foco: true, alClick: c => c.cerrar() }]
    });
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

// La sala se abre dentro de un iframe. idTurno le dice a qué sesión entrar (la sala la genera el turno) e idPaciente deja
// elegido al paciente para la historia clínica. Sin idTurno, la sala deja elegir entre los turnos que ya tienen sala.
function sesion(idPaciente = null, idTurno = null) {
    const params = new URLSearchParams();
    if (idTurno) params.set('turno', idTurno);
    if (idPaciente) params.set('idPaciente', idPaciente);
    const consulta = params.toString();
    const src = 'sesion_video.html' + (consulta ? '?' + consulta : '');
    document.getElementById('contenido').innerHTML = `
        <div class="sesion-iframe-wrapper">
            <iframe src="${YS.esc(src)}" allow="camera; microphone; display-capture"></iframe>
        </div>
    `;
}


// Las mismas reglas que el registro (Pantalla_Usuario/Usuario/js/app.js)
const RE_DNI   = /^\d{7,9}$/;
const RE_MAIL  = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const RE_TEL   = /^[\d\s+()-]{6,20}$/;
// Un campo opcional que estaba vacío (null) y sigue vacío se manda como null, no como ""
const opcional = (valor, original) => valor === '' && original == null ? null : valor;

async function perfil() {
    const c = document.getElementById('contenido');
    let prof;
    try { prof = await YS.api(`/profesionales/${idProfesional}`); }
    catch (e) { c.innerHTML = YS.errorCarga({ texto: e.message, reintentar: "activarTab('perfil')" }); return; }
    const especialidades = await YS.api('/especialidades').catch(() => []);
    if (YS.shell.actual !== 'perfil') return;
    window._profActual = prof;

    const fn = prof?.fechaNacimiento ? prof.fechaNacimiento.substring(0, 10) : '';
    const campo = (id, etiqueta, valor, extra = '') => `
        <div class="ui-campo"><label for="${id}">${etiqueta}</label>
            <input class="ui-input" id="${id}" value="${YS.esc(valor ?? '')}"${extra}></div>`;
    c.innerHTML = `
        <div class="card perfil-form">
            <form id="formPerfil" novalidate>
                <div class="ui-form-grid">
                    ${campo('pf-nombre', 'Nombre', prof?.nombre, ' autocomplete="given-name" maxlength="60"')}
                    ${campo('pf-apellido', 'Apellido', prof?.apellido, ' autocomplete="family-name" maxlength="60"')}
                    ${campo('pf-segundoNombre', 'Segundo nombre', prof?.segundoNombre, ' autocomplete="additional-name" maxlength="60"')}
                    ${campo('pf-dni', 'DNI', prof?.dni, ' inputmode="numeric" maxlength="9"')}
                    <div class="ui-campo"><label for="pf-fechaNacimiento">Fecha de nacimiento</label>
                        <input class="ui-input" type="date" id="pf-fechaNacimiento" value="${fn}" max="${YS.fecha.aISO(new Date())}"></div>
                    ${campo('pf-mail', 'Mail', prof?.mail, ' type="email" autocomplete="email" maxlength="100"')}
                    ${campo('pf-telefono', 'Teléfono', prof?.telefono, ' type="tel" autocomplete="tel" maxlength="20"')}
                    ${campo('pf-provincia', 'Provincia', prof?.provincia, ' maxlength="60"')}
                    <div class="ui-campo"><label for="pf-matricula">Matrícula</label>
                        <input class="ui-input" id="pf-matricula" value="${YS.esc(prof?.matricula ?? '')}" disabled>
                        <div class="ui-campo-ayuda">La cambia el equipo de YoSoy.</div></div>
                    <div class="ui-campo"><label for="pf-especialidad">Especialidad</label>
                        <select id="pf-especialidad" class="ui-select">
                            ${especialidades.map(e => `<option value="${e.idEspecialidad}" ${e.idEspecialidad === prof?.idEspecialidad ? 'selected' : ''}>${YS.esc(e.nombre ?? e.descripcion ?? e.idEspecialidad)}</option>`).join('')}
                        </select></div>
                </div>
                <div class="ui-form-acciones">
                    <button type="submit" class="ui-btn ui-btn--primary" id="btnGuardarPerfil">Guardar cambios</button>
                </div>
            </form>
        </div>
        <div class="card mt-16">
            <h3>Link público de reserva</h3>
            ${prof?.linkPublicoActivo ? `
                <p class="texto-suave mb-12">Cualquiera que tenga este link puede ver tus próximos horarios libres y pedir un turno. Si lo apagás, deja de funcionar al instante.</p>
                <div class="dialogo-link mb-12">
                    <input class="ui-input" id="linkPublicoUrl" readonly value="${YS.esc(urlLinkPublico(prof.slug))}">
                    <button class="ui-btn" type="button" onclick="document.getElementById('linkPublicoUrl').select(); YS.copiar(urlLinkPublico('${prof.slug}'), 'Link copiado')">Copiar</button>
                </div>
                <button class="ui-btn ui-btn--danger" type="button" onclick="toggleLinkPublico(false)">Apagar link</button>
            ` : `
                <p class="texto-suave mb-12">Activalo para tener una página pública con tus horarios libres, para que un paciente pida turno sin necesidad de que lo contactes antes.</p>
                <button class="ui-btn ui-btn--primary" type="button" onclick="toggleLinkPublico(true)">Activar link público</button>
            `}
        </div>`;
    document.getElementById('formPerfil').addEventListener('submit', e => { e.preventDefault(); guardarPerfil(); });
}

function urlLinkPublico(slug) { return `${location.origin}/reservar/${slug}`; }

async function toggleLinkPublico(activo) {
    try {
        const r = await YS.fetch(`/profesionales/${idProfesional}/link-publico`, {
            method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ activo })
        });
        if (r.ok) { toast(activo ? 'Link público activado' : 'Link público apagado'); activarTab('perfil'); }
        else toast(await YS.leerMensaje(r, 'No se pudo cambiar el link público.'), 'error');
    } catch {
        toast('No se pudo conectar con el servidor.', 'error');
    }
}

async function guardarPerfil() {
    const hoy = YS.fecha.aISO(new Date());
    const valido = YS.form.validar([
        { id: 'pf-nombre', requerido: 'Ingresá tu nombre.' },
        { id: 'pf-apellido', requerido: 'Ingresá tu apellido.' },
        { id: 'pf-dni', requerido: 'Ingresá tu DNI.', patron: RE_DNI, mensajePatron: 'El DNI tiene que tener entre 7 y 9 números, sin puntos.' },
        { id: 'pf-fechaNacimiento', validar: v => v && v > hoy ? 'La fecha de nacimiento no puede ser futura.' : null },
        { id: 'pf-mail', requerido: 'Ingresá tu mail: ahí te llegan los avisos.', patron: RE_MAIL, mensajePatron: 'Ingresá un mail válido.' },
        { id: 'pf-telefono', patron: RE_TEL, mensajePatron: 'Usá solo números, espacios, + o guiones.' }
    ]);
    if (!valido) return;

    const valor = id => document.getElementById(id).value.trim();
    const antes = window._profActual || {};
    const prof = {
        ...antes,
        nombre:          valor('pf-nombre'),
        apellido:        valor('pf-apellido'),
        segundoNombre:   opcional(valor('pf-segundoNombre'), antes.segundoNombre),
        dni:             valor('pf-dni'),
        fechaNacimiento: valor('pf-fechaNacimiento') || null,
        mail:            valor('pf-mail'),
        telefono:        opcional(valor('pf-telefono'), antes.telefono),
        provincia:       opcional(valor('pf-provincia'), antes.provincia),
        idEspecialidad:  parseInt(document.getElementById('pf-especialidad').value),
    };
    const boton = document.getElementById('btnGuardarPerfil');
    boton.disabled = true;
    try {
        const r = await YS.fetch(`/profesionales/${idProfesional}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(prof)
        });
        if (r.ok) { window._profActual = prof; toast('Tu perfil quedó guardado.'); }
        else toast(await YS.leerMensaje(r, 'No se pudieron guardar los cambios.'), 'error');
    } catch {
        toast('No se pudo conectar con el servidor.', 'error');
    } finally {
        boton.disabled = false;
    }
}

async function responderTurnoDesdeCal(id, aceptar) {
    try {
        const turno = await (await YS.fetch(`/turnos/${id}`)).json();
        turno.idEstadoTurno = aceptar ? 2 : 3;
        const r = await YS.fetch(`/turnos/${id}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body:JSON.stringify(turno) });
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

// Confirmaciones del sistema de diseño: confirmar(texto, { titulo, textoConfirmar, peligro })
function confirmar(msg, opciones = {}) {
    return YS.confirmar({ mensaje: msg, ...opciones });
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
        const r = await YS.fetch(`/profesionales/${idProfesional}/turnos`);
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
            // Pendientes que esperan al profesional: pedidos del paciente y los anteriores a la
            // columna CreadoPor (sin autor conocido).
            const pendienteParaMi = tieneT && turnosPorDia[dia].some(t => t.idEstadoTurno === 1 && t.creadoPor !== 'profesional');

            // Autoconfirmación ("si el paciente pidió y yo propuse el mismo día, se confirman todos
            // los pendientes de ese día"): nunca corrió, porque creadoPor no llegaba del backend.
            // Ahora que llega queda apagada a propósito: confirmaría turnos de pacientes distintos
            // del mismo día y escribe en la base solo por abrir el calendario. Para activarla hace
            // falta una decisión del equipo (ver CAMBIOS_AGUS.md).
            const AUTOCONFIRMAR = false;
            if (AUTOCONFIRMAR && mePidieron && yoPedi) {
                turnosPorDia[dia].filter(t => t.idEstadoTurno === 1).forEach(t => {
                    autoconfirmarPromesas.push(
                        YS.fetch(`/turnos/${t.idTurno}`).then(r => r.json()).then(turno => {
                            turno.idEstadoTurno = 2;
                            return YS.fetch(`/turnos/${t.idTurno}`, { method:'PUT', headers:{'Content-Type':'application/json'}, body:JSON.stringify(turno) });
                        }).catch(() => {})
                    );
                });
            }

            const claseT = tieneT
                ? (soloCancelado ? 'con-turno-cancelado' : pendienteParaMi ? 'con-turno-pendiente-ellos' : yoPedi ? 'con-turno-pendiente-yo' : 'con-turno')
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
            try { _pacientesCalMap[id] = await (await YS.fetch(`/pacientes/${id}`)).json(); } catch {}
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
            const propuesto = t.idEstadoTurno === 1 && t.creadoPor === 'profesional';
            html += `<button class="bloque-turno estado-${t.idEstadoTurno}${propuesto ? ' propuesto' : ''}" style="top:${y(inicio) + 1}px;height:${alto}px;${posicion}"
                         title="${fmtHora(t.hora)} · ${YS.esc(nombre)} · ${propuesto ? 'Propuesto, espera al paciente' : ESTADO_TURNO_LABEL[t.idEstadoTurno] ?? ''}"
                         onclick="abrirDiaDesdeSemanaProf(${d.getFullYear()},${d.getMonth()},${d.getDate()})">
                        <span class="bt-nombre"><span class="bt-hora">${fmtHora(t.hora)}</span> ${YS.esc(nombre)}</span>
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
            try { _pacientesCalMap[t.idPaciente] = await (await YS.fetch(`/pacientes/${t.idPaciente}`)).json(); } catch {}
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
        const esPropuestoPorMi    = t.idEstadoTurno === 1 && t.creadoPor === 'profesional';
        return `
        <div class="panel-top${t.idEstadoTurno === 3 ? ' panel-cancelado' : ''}">
            <div class="panel-hora-bloque">
                <div class="panel-hora">${fmtHora(t.hora)}</div>
                <div class="panel-hora-label">hs</div>
            </div>
            <div class="panel-info-principal">
                <div class="panel-profesional">${YS.esc(nombre)}</div>
            </div>
            <span class="panel-estado-badge estado-${t.idEstadoTurno}${esPropuestoPorMi ? ' estado-propuesto' : ''}">${esPropuestoPorMi ? 'Propuesto' : (estadoLabel[t.idEstadoTurno] ?? '—')}</span>
        </div>
        ${esPropuestoPorMi ? `<p class="texto-suave mb-12">Lo propusiste vos: queda pendiente hasta que el paciente lo confirme.</p>` : ''}
        ${esPendientePaciente ? `<p class="texto-suave mb-12">Lo pidió el paciente: aceptalo o rechazalo.</p>` : ''}
        ${t.motivo ? `<div class="panel-chips"><div class="chip chip-wide"><span class="chip-label">Motivo</span><span class="chip-val">${YS.esc(t.motivo)}</span></div></div>` : ''}
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
                <div id="reprogDetallesProf-${t.idTurno}" class="oculto">
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
    try { agendaProf = await YS.fetch(`/profesionales/${idProfesional}/agenda`).then(r => r.json()); } catch {}
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
    const pacientes = await YS.fetch(`/profesionales/${idProfesional}/pacientes`).then(r => r.json()).catch(() => []);
    sel.innerHTML = pacientes.length
        ? `<option value="">Seleccioná un paciente</option>` + pacientes.map(p => `<option value="${p.idPaciente}">${YS.esc(YS.nombreCompleto(p))}</option>`).join('')
        : `<option value="">Sin pacientes asignados</option>`;
    sel.disabled = !pacientes.length;
}

// Fecha y hora de hoy en Argentina (la zona de la clínica), no la del navegador: es contra lo que valida el servidor
// ("Ese horario ya pasó."). El servidor sigue siendo quien decide; esto solo evita ofrecer en el combo horarios de hoy que
// ya pasaron. Es la misma cuenta que usa Pantalla_Paciente/js/dashboard.js.
function ahoraEnArgentina() {
    const partes = new Intl.DateTimeFormat('en-CA', {
        timeZone: 'America/Argentina/Buenos_Aires', year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
    }).formatToParts(new Date());
    const v = tipo => partes.find(p => p.type === tipo).value;
    return { fecha: `${v('year')}-${v('month')}-${v('day')}`, minutos: Number(v('hour')) * 60 + Number(v('minute')) };
}

// Minutos del día que ya pasaron, si fechaStr (YYYY-MM-DD) es hoy en Argentina; -1 si es otro día (no hay nada pasado).
function minutosPasadosHoy(fechaStr) {
    const ahora = ahoraEnArgentina();
    return fechaStr === ahora.fecha ? ahora.minutos : -1;
}

async function cargarHorasSolicitudProf(fechaStr) {
    const sel  = document.getElementById('solProfHora');
    if (!sel) return;
    const fecha  = new Date(fechaStr + 'T12:00:00');
    const diaSem = fecha.getDay();

    let agenda = [];
    try { agenda = await YS.fetch(`/profesionales/${idProfesional}/agenda`).then(r => r.json()); } catch {}
    const slot = agenda.find(a => a.disponible && DIA_DB_NUM[normalizarP(a.dia)] === diaSem);

    if (!slot) { sel.innerHTML = `<option value="">Sin disponibilidad ese día</option>`; sel.disabled = true; return; }

    const [hI, mI] = slot.horaInicio.substring(0,5).split(':').map(Number);
    const [hF, mF] = slot.horaFin.substring(0,5).split(':').map(Number);
    // Sin anticipación mínima: lo único que no se ofrece es lo que ya pasó (solo cuenta si el día elegido es hoy).
    const minutosPasados = minutosPasadosHoy(fechaStr);
    const opciones = [];
    let cur = hI * 60 + mI;
    while (cur + 60 <= hF * 60 + mF) {
        const hh = String(Math.floor(cur/60)).padStart(2,'0');
        const mm = String(cur%60).padStart(2,'0');
        if (cur > minutosPasados) opciones.push(`<option value="${hh}:${mm}">${hh}:${mm}</option>`);
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

    const res = await YS.fetch(`/turnos/profesional-solicitar`, {
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
    } else toast(await YS.leerMensaje(res, 'Error al solicitar el turno.'), 'error');
}

async function cancelarTurnoProf(id) {
    if (!await confirmar('¿Cancelar este turno? El paciente lo va a ver cancelado en su panel.', { titulo: 'Cancelar turno', textoConfirmar: 'Cancelar turno', textoCancelar: 'Volver', peligro: true })) return;
    try {
        const r = await YS.fetch(`/turnos/${id}/cancelar`, { method: 'PUT' });
        if (r.ok) toast('Turno cancelado.', 'warn');
        else toast(await YS.leerMensaje(r, 'No se pudo cancelar el turno.'), 'error');
    } catch { toast('No se pudo conectar con el servidor.', 'error'); }
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
    document.getElementById(`reprogDetallesProf-${idTurno}`).classList.add('oculto');

    try {
        const agenda = await YS.fetch(`/profesionales/${idProfesional}/agenda`).then(r => r.json());
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
    document.getElementById(`reprogDetallesProf-${_idTurnoReprogProf}`).classList.remove('oculto');

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

    const turno = await YS.fetch(`/turnos/${idTurno}`).then(r => r.json());
    const fecha = _reprogFechaSeleccionadaProf;
    turno.fecha = new Date(fecha.getFullYear(), fecha.getMonth(), fecha.getDate(), 12).toISOString();
    turno.hora  = hora + ':00';
    if (motivo) turno.motivo = motivo;

    const r = await YS.fetch(`/turnos/${idTurno}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(turno)
    });

    if (r.ok) {
        toast('Turno reprogramado.');
        cancelarModoReprogramarProf();   // limpia _reprogFechaSeleccionadaProf: se usa la copia
        await renderCalendario();
        mostrarPanelDiaProfesional(fecha.getDate());
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
    cont.innerHTML = `
        <div class="card reporte-card">
            <div class="titulo-seccion titulo-seccion--tarjeta">Mis atenciones</div>
            <p class="texto-suave mb-12">Descargá el detalle de tus atenciones (paciente, fecha, hora y tipo de cierre) en un archivo que se abre con Excel.</p>
            <button class="ui-btn ui-btn--primary" onclick="descargarReporte()">${YS.icono('descargar')}Descargar Excel</button>
        </div>`;
}

async function descargarReporte() {
    const url = API + '/reportes/atenciones?idProfesional=' + idProfesional;
    let datos = [];
    try {
        const r = await YS.fetch(url);
        datos = await r.json();
    } catch(e) {
        toast('No se pudieron obtener tus atenciones. Probá de nuevo en un rato.', 'error');
        return;
    }
    if (!datos.length) {
        toast('Todavía no tenés atenciones registradas.', 'warn');
        return;
    }
    const encabezado = ['Nombre y Apellido', 'DNI', 'NHC', 'Fecha', 'Hora', 'Tipo de Cierre', 'Profesional'];
    const filas = datos.map(function(d) {
        return [d.nombre, d.dni, d.nhc || '', d.fecha, d.hora, d.tipoCierre || '', d.nombreProfesional];
    });
    const csvContent = [encabezado].concat(filas)
        .map(function(row) { return row.map(function(v) { return '"' + String(v).replace(/"/g, '""') + '"'; }).join(','); })
        .join('\n');
    // El BOM hace que Excel lea el archivo como UTF-8 (sin él, "García" sale como "GarcÃ­a")
    const blob = new Blob(['\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'mis_atenciones.csv';
    link.click();
}

// ── MIS PACIENTES ───────────────────────────────────────────
// Los pacientes asignados. Las asignaciones nuevas (vienen de admisión) se confirman
// proponiendo un primer turno o se devuelven a admisión.
async function pacientes() {
    const c = document.getElementById('contenido');
    let lista;
    try { lista = await YS.api(`/profesionales/${idProfesional}/pacientes-detalle`); }
    catch (e) {
        if (YS.shell.actual === 'pacientes') c.innerHTML = YS.errorCarga({ texto: e.message, reintentar: "activarTab('pacientes')" });
        return;
    }
    if (YS.shell.actual !== 'pacientes') return;
    window._misPacientes = lista;
    const nuevos = lista.filter(p => p.estadoAsignacion === 'Nueva').length;
    YS.shell.encabezado({ subtitulo: `${lista.length} paciente${lista.length === 1 ? '' : 's'}` +
        (nuevos ? ` · ${nuevos} asignación${nuevos === 1 ? '' : 'es'} nueva${nuevos === 1 ? '' : 's'}` : '') });
    YS.shell.acciones([{ texto: 'Actualizar', icono: 'refrescar', alClick: () => activarTab('pacientes') }]);

    c.innerHTML = '<div id="tablaPacientes"></div><div id="panelNuevoPaciente"></div>';
    YS.tabla(document.getElementById('tablaPacientes'), {
        filas: lista, unidad: ['paciente', 'pacientes'], placeholder: 'Buscar por nombre, DNI, mail o teléfono',
        buscar: p => [p.nombre, p.dni, p.mail, p.telefono].join(' '),
        filtros: [
            { id: 'todos', texto: 'Todos', pasa: () => true },
            { id: 'nuevos', texto: 'Asignaciones nuevas', pasa: p => p.estadoAsignacion === 'Nueva' }
        ],
        ordenInicial: { col: 0 },
        vacio: { icono: 'pacientes', titulo: 'Todavía no tenés pacientes asignados', texto: 'Te los asigna el equipo de admisión.' },
        columnas: [
            { titulo: 'Paciente', orden: p => YS.normalizar(p.nombre),
              html: p => `<span class="ui-flex">${YS.avatar(p.nombre, p.idPaciente, 'sm')}<span class="principal">${YS.esc(p.nombre)}</span>` +
                  (p.estadoAsignacion === 'Nueva' ? '<span class="ui-chip ui-chip--info">Nuevo</span>' : '') + '</span>' },
            { titulo: 'DNI', html: p => YS.esc(p.dni || '—'), orden: p => p.dni || '' },
            { titulo: 'Mail', html: p => YS.esc(p.mail || '—') },
            { titulo: 'Teléfono', html: p => YS.esc(p.telefono || '—') },
            { titulo: '', clase: 'acciones', html: p => p.estadoAsignacion === 'Nueva'
                ? `<button class="ui-btn ui-btn--sm" onclick="verEvolucionAdmision(${p.idPaciente})">Motivo de la asignación</button>
                   <button class="ui-btn ui-btn--sm ui-btn--ok" onclick="confirmarNuevoPaciente(${p.idPaciente})">Confirmar</button>
                   <button class="ui-btn ui-btn--sm ui-btn--danger" onclick="derivarNuevoPaciente(${p.idPaciente})">Devolver a admisión</button>`
                : `<button class="ui-btn ui-btn--sm" onclick="verHistoria(${p.idPaciente})">Historia clínica</button>
                   <button class="ui-btn ui-btn--sm" onclick="abrirSesionCon(${p.idPaciente})">${YS.icono('video')}Sesión</button>` }
        ]
    });
}

// ── Mis casos (Fase 2) ───────────────────────────────────────
async function casos() {
    const c = document.getElementById('contenido');
    let lista;
    try { lista = await YS.api(`/profesionales/${idProfesional}/casos`); }
    catch (e) {
        if (YS.shell.actual === 'casos') c.innerHTML = YS.errorCarga({ texto: e.message, reintentar: "activarTab('casos')" });
        return;
    }
    if (YS.shell.actual !== 'casos') return;
    YS.shell.encabezado({ subtitulo: `${lista.length} caso${lista.length === 1 ? '' : 's'}` });
    YS.shell.acciones([{ texto: 'Actualizar', icono: 'refrescar', alClick: () => activarTab('casos') }]);

    const tono = { Nuevo: 'neutral', Derivado: 'info', Asignado: 'ok', 'En atención': 'ok', Cerrado: 'neutral' };
    YS.tabla(c, {
        filas: lista, unidad: ['caso', 'casos'], placeholder: 'Buscar por paciente o grupo',
        buscar: cs => [cs.nombrePaciente, cs.nombreGrupo].join(' '),
        filtros: [
            { id: 'activos', texto: 'Activos', pasa: cs => cs.estado !== 'Cerrado' },
            { id: 'todos', texto: 'Todos', pasa: () => true }
        ],
        ordenInicial: { col: 2, desc: true },
        vacio: { icono: 'portapapeles', titulo: 'Todavía no tenés casos asignados', texto: 'Te los asigna el sistema o CRM cuando entra una admisión.' },
        columnas: [
            { titulo: 'Paciente', orden: cs => YS.normalizar(cs.nombrePaciente),
              html: cs => `<span class="ui-flex">${YS.avatar(cs.nombrePaciente, cs.idPaciente, 'sm')}<span class="principal">${YS.esc(cs.nombrePaciente)}</span></span>` },
            { titulo: 'Grupo', html: cs => YS.esc(cs.nombreGrupo || '—') },
            { titulo: 'Estado', html: cs => `<span class="ui-chip ui-chip--${tono[cs.estado] || 'neutral'}">${YS.esc(cs.estado)}</span>`, orden: cs => cs.estado },
            { titulo: '', clase: 'acciones', html: cs => `
                <button class="ui-btn ui-btn--sm" onclick="verHistoria(${cs.idPaciente})">Historia clínica</button>
                <button class="ui-btn ui-btn--sm" onclick="abrirSesionCon(${cs.idPaciente})">${YS.icono('video')}Sesión</button>` }
        ]
    });
}

const nombreDeMiPaciente = id => (window._misPacientes || []).find(p => p.idPaciente === id)?.nombre ?? null;

function cerrarPanelNuevo() {
    document.getElementById('panelNuevoPaciente').innerHTML = '';
}

function cabeceraPanelNuevo(titulo) {
    return `<div class="ui-entre mb-12">
        <div class="titulo-seccion titulo-seccion--tarjeta titulo-seccion--sin-margen">${YS.esc(titulo)}</div>
        <button type="button" class="ui-btn ui-btn--sm ui-btn--ghost" onclick="cerrarPanelNuevo()">Cerrar</button>
    </div>`;
}

async function verEvolucionAdmision(idPaciente) {
    const panel = document.getElementById('panelNuevoPaciente');
    const nombre = nombreDeMiPaciente(idPaciente);
    panel.innerHTML = `<div class="card mt-16">${YS.cargando()}</div>`;
    let evoluciones = [];
    try {
        const historia = await YS.api(`/pacientes/${idPaciente}/historiaclinica`, { permitir404: true });
        if (historia?.idHistoria) evoluciones = await YS.api(`/historiasclinicas/${historia.idHistoria}/evoluciones`);
    } catch (e) {
        panel.innerHTML = `<div class="card mt-16">${YS.errorCarga({ texto: e.message, reintentar: `verEvolucionAdmision(${idPaciente})` })}</div>`;
        return;
    }
    const deAdmision = evoluciones.filter(e => (e.diagnostico || '').toLowerCase().includes('admis'));
    panel.innerHTML = `<div class="card card--acento-info mt-16">
        ${cabeceraPanelNuevo('Evolución del equipo de admisión' + (nombre ? ' · ' + nombre : ''))}
        ${deAdmision.length ? deAdmision.map(e => `
            <div class="caja-suave mb-12">
                <div class="texto-fuerte mb-8">${YS.esc(YS.fecha.larga(e.fecha))}</div>
                ${e.observaciones ? `<div class="mb-8">${YS.esc(e.observaciones)}</div>` : ''}
                ${e.planTratamiento ? `<div class="texto-accent texto-fuerte">${YS.esc(e.planTratamiento)}</div>` : ''}
            </div>`).join('')
            : YS.vacio({ icono: 'portapapeles', texto: 'El equipo de admisión no dejó una evolución para este paciente.' })}
    </div>`;
    panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

// El primer turno no se elige: la API toma el primer horario libre de tu agenda (así no quedan huecos entre turnos).
// Se muestra con el calendario del mes de Mis horarios de trabajo ("Así te ven los pacientes"), con ese día ya elegido.
const DIAS_PRIMER_TURNO = 60;   // TurnoReglasService.DiasPrimerTurno: los días que trae "libres"
const fechaHoraPropuesta = d => YS.fecha.separador(d) + ' a las ' + YS.fecha.hora(d) + ' hs';

async function confirmarNuevoPaciente(idPaciente) {
    const panel = document.getElementById('panelNuevoPaciente');
    const nombre = nombreDeMiPaciente(idPaciente);
    const cabecera = cabeceraPanelNuevo('Proponer el primer turno' + (nombre ? ' · ' + nombre : ''));
    panel.innerHTML = `<div class="card card--acento-ok mt-16">${cabecera}${YS.cargando()}</div>`;
    panel.scrollIntoView({ behavior: 'smooth', block: 'start' });

    let datos, franjas;
    try {
        [datos, franjas] = await Promise.all([
            YS.api(`/asignaciones/${idPaciente}/primer-turno`),
            YS.api(`/profesionales/${idProfesional}/agenda`).catch(() => [])
        ]);
    } catch (e) {
        panel.innerHTML = `<div class="card mt-16">${cabecera}${YS.errorCarga({ texto: e.message, reintentar: `confirmarNuevoPaciente(${idPaciente})` })}</div>`;
        return;
    }
    if (!document.getElementById('panelNuevoPaciente')) return;

    if (!datos.propuesto) {
        panel.innerHTML = `<div class="card card--acento-info mt-16">${cabecera}
            ${YS.vacio({ icono: 'reloj', texto: 'No tenés horarios libres en tu agenda. Agregá horarios en Mis horarios de trabajo para poder proponer el primer turno.' })}
            <div class="ui-flex mt-12"><button class="ui-btn ui-btn--primary" onclick="activarTab('horarios')">Ir a Mis horarios de trabajo</button></div>
        </div>`;
        return;
    }

    const propuesto = new Date(datos.propuesto);
    const libresPorDia = new Map();
    datos.libres.forEach(iso => {
        const d = new Date(iso), k = YS.fecha.aISO(d);
        if (!libresPorDia.has(k)) libresPorDia.set(k, new Set());
        libresPorDia.get(k).add(YS.fecha.hora(d));
    });
    const validas = franjas.filter(s => s.horaInicio && s.horaFin && YS.dias.orden(s.dia) < 7);
    // El mes y el día del turno propuesto quedan a la vista; se recorren los días en que se busca el primer turno.
    _horCal = { anio: propuesto.getFullYear(), mes: propuesto.getMonth(), validas, libresPorDia, elegido: YS.fecha.aISO(propuesto),
                propuesto, dias: DIAS_PRIMER_TURNO };

    panel.innerHTML = `<div class="card card--acento-ok mt-16">${cabecera}
        <p class="ui-alerta ui-alerta--info mb-12"><span>Se le propone el <strong>${YS.esc(fechaHoraPropuesta(propuesto))}</strong>: tu primer horario libre. Los primeros turnos se asignan solos, uno detrás de otro, para no dejar huecos en tu agenda.</span></p>
        <section class="horarios-propuesta" id="horarios-calendario" aria-labelledby="horarios-cal-titulo">
            <div class="horarios-cal-cabecera">
                <div class="titulo-seccion titulo-seccion--tarjeta titulo-seccion--sin-margen" id="horarios-cal-titulo">Así te ven los pacientes</div>
                <div class="horarios-cal-nav">
                    <button type="button" class="ui-btn ui-btn--sm ui-btn--ghost" id="horarios-cal-anterior" onclick="moverMesHorarios(-1)" aria-label="Mes anterior">&#8249;</button>
                    <span id="horarios-cal-mes" aria-live="polite"></span>
                    <button type="button" class="ui-btn ui-btn--sm ui-btn--ghost" id="horarios-cal-siguiente" onclick="moverMesHorarios(1)" aria-label="Mes siguiente">&#8250;</button>
                </div>
            </div>
            <div class="horarios-cal" id="horarios-cal-grilla"></div>
            <div class="horarios-leyenda">
                <span><span class="horario-muestra horario-muestra--propuesta"></span>Turno propuesto</span>
                <span><span class="horario-muestra horario-muestra--turnos"></span>Días con turnos</span>
            </div>
            <div id="horarios-cal-detalle" class="horarios-cal-detalle mb-12" aria-live="polite"></div>
        </section>
        <p class="texto-suave mb-12">Al paciente le llega como turno pendiente y puede confirmarlo o reprogramarlo según tu agenda.</p>
        <button class="ui-btn ui-btn--primary" id="npEnviar" onclick="enviarPrimerTurno(${idPaciente})">Enviar propuesta</button>
    </div>`;
    dibujarCalendarioHorarios();
    document.getElementById('npEnviar').focus();
}

async function enviarPrimerTurno(idPaciente) {
    const boton = document.getElementById('npEnviar');
    boton.disabled = true;
    try {
        const r = await YS.fetch(`/asignaciones/${idPaciente}/primer-turno`, { method: 'POST' });
        if (!r.ok) {
            toast(await YS.leerMensaje(r, 'No se pudo crear el turno.'), 'error');
            if (r.status === 409) confirmarNuevoPaciente(idPaciente);   // el horario pudo ocuparse: se recalcula la grilla
            return;
        }
        const d = await r.json();
        toast(`Propuesta enviada para el ${fechaHoraPropuesta(new Date(d.fecha))}. El paciente puede confirmar o reprogramar el turno.`);
        pacientes();
    } catch {
        toast('No se pudo conectar con el servidor.', 'error');
    } finally {
        boton.disabled = false;
    }
}

async function derivarNuevoPaciente(idPaciente) {
    const nombre = nombreDeMiPaciente(idPaciente);
    const motivo = await YS.pedirTexto({
        titulo: 'Devolver a admisión',
        mensaje: `${nombre ? nombre + ' vuelve' : 'El paciente vuelve'} al equipo de admisión para que lo asignen a otro profesional.`,
        etiqueta: 'Por qué no vas a atender a este paciente', multilinea: true, maximo: 500,
        mensajeRequerido: 'Contale al equipo el motivo.', textoConfirmar: 'Devolver a admisión', peligro: true
    });
    if (!motivo) return;
    try {
        const r = await YS.fetch("/informes-admision/rechazar", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ idPaciente: idPaciente, idProfesional: idProfesional, motivo: motivo })
        });
        if (r.ok) { toast('El caso volvió al equipo de admisión para asignarlo de nuevo.', 'warn'); pacientes(); }
        else toast(await YS.leerMensaje(r, 'No se pudo devolver el caso.'), 'error');
    } catch { toast('No se pudo conectar con el servidor.', 'error'); }
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
    if (lista === null || lista === undefined || lista.length === 0) return YS.vacio({ icono: 'documento', texto: 'Todavía no hay evoluciones registradas.' });
    return lista.map(function(e) {
        const esAdmision = ((e.diagnostico || "").toLowerCase().indexOf("admis") > -1);
        return '<div class="evolucion-item' + (esAdmision ? ' evolucion-item--admision' : '') + '">' +
            (esAdmision ? '<div class="evolucion-etiqueta-admision">Inicio del proceso - Admision</div>' : "") +
            '<div class="evolucion-fecha">' + new Date(e.fecha).toLocaleDateString("es-AR", { weekday:"long", day:"numeric", month:"long", year:"numeric" }) + '</div>' +
            (e.diagnostico ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Diagnostico</div><div class="evolucion-bloque-texto">' + YS.esc(e.diagnostico) + '</div></div>' : "") +
            (e.observaciones ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">Observaciones</div><div class="evolucion-bloque-texto">' + YS.esc(e.observaciones) + '</div></div>' : "") +
            (e.planTratamiento ? '<div class="evolucion-bloque"><div class="evolucion-bloque-label">' + (esAdmision ? "Derivacion" : "Plan") + '</div><div class="evolucion-bloque-texto">' + YS.esc(e.planTratamiento) + '</div></div>' : "") +
            '</div>';
    }).join("");
}

// Los turnos confirmados sin sala se avisan en la campana de la barra (shared/shell.js).
// Si se genera una sala desde ahí con el Inicio abierto, se actualiza la lista de hoy.
document.addEventListener('ys:sala-generada', () => { if (YS.shell.actual === 'inicio') activarTab('inicio'); });

YS.shell.iniciar({ rol: 'profesional', navegar: id => activarTab(id) });
