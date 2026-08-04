const fs = require('fs');
const { execSync } = require('child_process');

// ══════════════════════════════════════════════════════════════
// 1. PROFESIONAL: redefinir pacientes() con NUEVO + confirmar + derivar
//    y renderEvoluciones() con azul para admision.
//    (En JS la segunda declaracion de una funcion pisa la primera,
//     asi que se agregan al final sin tocar el codigo existente.)
// ══════════════════════════════════════════════════════════════
const profPath = 'D:/YoSoy/PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js';
let prof = fs.readFileSync(profPath, 'utf8');

if (prof.indexOf('confirmarNuevoPaciente') === -1) {
    const fn = [
'',
'// Mis Pacientes v2: asignaciones nuevas, confirmar y derivar',
'async function pacientes() {',
'    const c = document.getElementById("contenido");',
'    let lista = [];',
'    try {',
'        const r = await fetch(API + "/profesionales/" + idProfesional + "/pacientes-detalle");',
'        lista = await r.json();',
'    } catch(e) {}',
'',
'    const filas = lista.map(function(p) {',
'        const esNuevo = p.estadoAsignacion === "Nueva";',
'        const nombreEsc = (p.nombre || "").split(String.fromCharCode(39)).join("").split(String.fromCharCode(34)).join("");',
'        let acciones = \'<button class="btn-ver" onclick="verHistoria(\' + p.idPaciente + \', &quot;\' + nombreEsc + \'&quot;)">Historia</button> \' +',
'            \'<button class="btn-sesion" onclick="activarTab(&quot;sesion&quot;)">Sesion</button>\';',
'        if (esNuevo) {',
'            acciones = \'<button class="btn-ver" style="background:linear-gradient(to right,#0d6efd,#084298)" onclick="verEvolucionAdmision(\' + p.idPaciente + \')">Motivo asignacion</button> \' +',
'                \'<button class="btn-ver" style="background:linear-gradient(to right,#198754,#0f5132)" onclick="confirmarNuevoPaciente(\' + p.idPaciente + \')">Confirmar</button> \' +',
'                \'<button class="btn-ver" style="background:linear-gradient(to right,#e53935,#b71c1c)" onclick="derivarNuevoPaciente(\' + p.idPaciente + \')">Derivar</button>\';',
'        }',
'        return \'<tr>\' +',
'            \'<td>\' + p.nombre + (esNuevo ? \' <span style="background:#0d6efd;color:white;border-radius:8px;padding:2px 10px;font-size:11px;font-weight:bold;margin-left:6px">NUEVO</span>\' : "") + \'</td>\' +',
'            \'<td>\' + (p.dni || "-") + \'</td>\' +',
'            \'<td>\' + (p.mail || "-") + \'</td>\' +',
'            \'<td>\' + (p.telefono || "-") + \'</td>\' +',
'            \'<td>\' + acciones + \'</td>\' +',
'            \'</tr>\';',
'    }).join("");',
'',
'    c.innerHTML = \'<div class="titulo-seccion">Mis Pacientes</div>\' +',
'        \'<div class="card">\' +',
'        (lista.length === 0 ? \'<div class="sin-turnos">No tenes pacientes asignados.</div>\' :',
'        \'<div class="tabla-wrapper"><table><thead><tr><th>Nombre</th><th>DNI</th><th>Mail</th><th>Telefono</th><th></th></tr></thead><tbody>\' + filas + \'</tbody></table></div>\') +',
'        \'</div>\' +',
'        \'<div id="panelNuevoPaciente"></div>\' +',
'        \'<div class="modal-overlay" id="modalHistoria">\' +',
'        \'<div class="modal-box">\' +',
'        \'<div class="modal-header">\' +',
'        \'<span class="modal-titulo" id="modalTitulo">Historia Clinica</span>\' +',
'        \'<button class="modal-cerrar" onclick="cerrarModal()">X</button>\' +',
'        \'</div>\' +',
'        \'<div class="modal-body" id="modalBody"></div>\' +',
'        \'</div>\' +',
'        \'</div>\';',
'}',
'',
'async function verEvolucionAdmision(idPaciente) {',
'    let historia = null, evoluciones = [];',
'    try { historia = await (await fetch(API + "/pacientes/" + idPaciente + "/historiaclinica")).json(); } catch(e) {}',
'    if (historia !== null && historia.idHistoria !== undefined) {',
'        try { evoluciones = await (await fetch(API + "/historiasclinicas/" + historia.idHistoria + "/evoluciones")).json(); } catch(e) {}',
'    }',
'    const deAdmision = evoluciones.filter(function(e) { return ((e.diagnostico || "").toLowerCase().indexOf("admis") > -1); });',
'    const panel = document.getElementById("panelNuevoPaciente");',
'    if (deAdmision.length === 0) {',
'        panel.innerHTML = \'<div class="card" style="margin-top:16px"><p style="color:#888">No hay evolucion de admision registrada.</p></div>\';',
'        return;',
'    }',
'    panel.innerHTML = \'<div class="card" style="margin-top:16px;border-left:6px solid #0d6efd">\' +',
'        \'<h3 style="color:#0d6efd;margin-bottom:12px">Evolucion del Equipo de Admision</h3>\' +',
'        deAdmision.map(function(e) {',
'            return \'<div style="background:#f0f6ff;border-radius:10px;padding:14px;margin-bottom:10px">\' +',
'                \'<div style="font-weight:bold;margin-bottom:6px">\' + new Date(e.fecha).toLocaleDateString("es-AR", { day:"numeric", month:"long", year:"numeric" }) + \'</div>\' +',
'                (e.observaciones ? \'<div style="font-size:14px;margin-bottom:6px">\' + e.observaciones + \'</div>\' : "") +',
'                (e.planTratamiento ? \'<div style="font-size:13px;color:#084298;font-weight:600">\' + e.planTratamiento + \'</div>\' : "") +',
'                \'</div>\';',
'        }).join("") +',
'        \'</div>\';',
'    panel.scrollIntoView({ behavior: "smooth" });',
'}',
'',
'function confirmarNuevoPaciente(idPaciente) {',
'    const panel = document.getElementById("panelNuevoPaciente");',
'    panel.innerHTML = \'<div class="card" style="margin-top:16px;border-left:6px solid #198754">\' +',
'        \'<h3 style="color:#0f5132;margin-bottom:12px">Proponer primer turno</h3>\' +',
'        \'<p style="font-size:13px;color:#888;margin-bottom:12px">Elegi una fecha estimativa. Al paciente le llegara como turno pendiente y podra confirmarlo o reprogramarlo segun tu agenda.</p>\' +',
'        \'<div style="display:flex;gap:12px;flex-wrap:wrap;align-items:flex-end">\' +',
'        \'<div><label style="font-size:12px;font-weight:bold;color:#0f5132;display:block">Fecha</label><input type="date" id="npFecha" style="padding:8px;border:2px solid #badbcc;border-radius:8px"></div>\' +',
'        \'<div><label style="font-size:12px;font-weight:bold;color:#0f5132;display:block">Hora</label><input type="time" id="npHora" style="padding:8px;border:2px solid #badbcc;border-radius:8px"></div>\' +',
'        \'<button onclick="enviarPrimerTurno(\' + idPaciente + \')" style="padding:9px 20px;background:#198754;color:white;border:none;border-radius:8px;cursor:pointer;font-weight:bold">Enviar propuesta</button>\' +',
'        \'</div></div>\';',
'    panel.scrollIntoView({ behavior: "smooth" });',
'}',
'',
'async function enviarPrimerTurno(idPaciente) {',
'    const fecha = document.getElementById("npFecha").value;',
'    const hora  = document.getElementById("npHora").value;',
'    if (fecha === "" || hora === "") { toast("Completa fecha y hora.", "warn"); return; }',
'    try {',
'        const r = await fetch(API + "/turnos/profesional-solicitar", {',
'            method: "POST",',
'            headers: { "Content-Type": "application/json" },',
'            body: JSON.stringify({ idPaciente: idPaciente, idProfesional: idProfesional, fecha: fecha, hora: hora + ":00", motivo: "Primer turno - derivacion de admision" })',
'        });',
'        if (r.ok) {',
'            await fetch(API + "/asignaciones/estado", {',
'                method: "PUT",',
'                headers: { "Content-Type": "application/json" },',
'                body: JSON.stringify({ idPaciente: idPaciente, idProfesional: idProfesional, estado: "Confirmada" })',
'            });',
'            toast("Propuesta enviada. El paciente podra confirmar o reprogramar el turno.");',
'            pacientes();',
'        } else toast("Error al crear el turno.", "error");',
'    } catch(e) { toast("Error de conexion.", "error"); }',
'}',
'',
'async function derivarNuevoPaciente(idPaciente) {',
'    const motivo = prompt("Indica el motivo por el cual no vas a atender a este paciente:");',
'    if (motivo === null || motivo.trim() === "") return;',
'    try {',
'        const r = await fetch(API + "/informes-admision/rechazar", {',
'            method: "PUT",',
'            headers: { "Content-Type": "application/json" },',
'            body: JSON.stringify({ idPaciente: idPaciente, idProfesional: idProfesional, motivo: motivo })',
'        });',
'        if (r.ok) { toast("El caso volvio al equipo de admision para re-derivar.", "warn"); pacientes(); }',
'        else toast("Error al derivar.", "error");',
'    } catch(e) { toast("Error de conexion.", "error"); }',
'}',
'',
'// Evoluciones v2: las de admision se muestran en azul',
'function renderEvoluciones(lista) {',
'    if (lista === null || lista === undefined || lista.length === 0) return \'<div class="sin-datos">Sin evoluciones registradas.</div>\';',
'    return lista.map(function(e) {',
'        const esAdmision = ((e.diagnostico || "").toLowerCase().indexOf("admis") > -1);',
'        const estilo = esAdmision ? \' style="border-left:5px solid #0d6efd;background:#f0f6ff;border-radius:8px;padding:12px"\' : "";',
'        return \'<div class="evolucion-item"\' + estilo + \'>\' +',
'            (esAdmision ? \'<div style="color:#0d6efd;font-weight:bold;font-size:12px;text-transform:uppercase;letter-spacing:1px;margin-bottom:4px">Inicio del proceso - Admision</div>\' : "") +',
'            \'<div class="evolucion-fecha">\' + new Date(e.fecha).toLocaleDateString("es-AR", { weekday:"long", day:"numeric", month:"long", year:"numeric" }) + \'</div>\' +',
'            (e.diagnostico ? \'<div class="evolucion-bloque"><div class="evolucion-bloque-label">Diagnostico</div><div class="evolucion-bloque-texto">\' + e.diagnostico + \'</div></div>\' : "") +',
'            (e.observaciones ? \'<div class="evolucion-bloque"><div class="evolucion-bloque-label">Observaciones</div><div class="evolucion-bloque-texto">\' + e.observaciones + \'</div></div>\' : "") +',
'            (e.planTratamiento ? \'<div class="evolucion-bloque"><div class="evolucion-bloque-label">\' + (esAdmision ? "Derivacion" : "Plan") + \'</div><div class="evolucion-bloque-texto">\' + e.planTratamiento + \'</div></div>\' : "") +',
'            \'</div>\';',
'    }).join("");',
'}',
''
    ].join('\n');
    prof = prof + fn;
    fs.writeFileSync(profPath, prof, 'utf8');
    console.log('1. Mis Pacientes v2 + evolucion azul agregados al profesional OK');
} else {
    console.log('1. Ya existia en el profesional');
}

try {
    execSync('node --check "' + profPath + '"', { stdio: 'pipe' });
    console.log('1b. Sintaxis profesional OK');
} catch(e) {
    console.log('1b. ERROR sintaxis:', e.stderr.toString().split('\n').slice(0, 3).join('\n'));
}

// ══════════════════════════════════════════════════════════════
// 2. CONFIGURADOR: estado Rechazado en la bandeja + re-derivar
// ══════════════════════════════════════════════════════════════
const confPath = 'D:/YoSoy/PANTALLAS/Pantalla_Configurador/js/dashboard.js';
let conf = fs.readFileSync(confPath, 'utf8');

// 2a. Color del badge Rechazado
const coloresViejo = 'const colores = { "Pendiente": "#fff3cd|#856404", "En entrevista": "#cfe2ff|#084298", "Derivado": "#d1e7dd|#0f5132" };';
const coloresNuevo = 'const colores = { "Pendiente": "#fff3cd|#856404", "En entrevista": "#cfe2ff|#084298", "Derivado": "#d1e7dd|#0f5132", "Rechazado": "#f8d7da|#842029" };';
if (conf.indexOf('"Rechazado": "#f8d7da|#842029"') > -1) {
    console.log('2a. Color Rechazado ya existia');
} else if (conf.indexOf(coloresViejo) > -1) {
    conf = conf.split(coloresViejo).join(coloresNuevo);
    console.log('2a. Color Rechazado agregado OK');
} else {
    console.log('2a. ERROR: no se encontro el objeto de colores');
}

// 2b. Permitir re-derivar cuando esta Rechazado
const entViejo = 'const esEntrevista = a.estado === "En entrevista";';
const entNuevo = 'const esEntrevista = a.estado === "En entrevista" || a.estado === "Rechazado";';
if (conf.indexOf(entNuevo) > -1) {
    console.log('2b. Re-derivar en Rechazado ya existia');
} else if (conf.indexOf(entViejo) > -1) {
    conf = conf.replace(entViejo, entNuevo);
    console.log('2b. Re-derivar habilitado para Rechazado OK');
} else {
    console.log('2b. ERROR: no se encontro esEntrevista');
}

// 2c. Mostrar el motivo del rechazo en el panel de derivar
const h4Ancla = '\x27<h4 style="color:#6A5ACD;margin-bottom:12px">Derivar al profesional</h4>\x27 +';
const h4Nuevo = '\x27<h4 style="color:#6A5ACD;margin-bottom:12px">Derivar al profesional</h4>\x27 +\n            (a.estado === "Rechazado" ? \x27<p style="background:#f8d7da;color:#842029;padding:10px 14px;border-radius:8px;font-size:13px;margin-bottom:10px"><strong>Rechazado por el profesional anterior:</strong> \x27 + (a.motivoRechazo || "Sin motivo") + \x27</p>\x27 : \x27\x27) +';
if (conf.indexOf('Rechazado por el profesional anterior') > -1) {
    console.log('2c. Motivo del rechazo ya se mostraba');
} else if (conf.indexOf(h4Ancla) > -1) {
    conf = conf.replace(h4Ancla, h4Nuevo);
    console.log('2c. Motivo del rechazo agregado al panel OK');
} else {
    console.log('2c. ERROR: no se encontro el h4 del panel de derivar');
}

fs.writeFileSync(confPath, conf, 'utf8');
try {
    execSync('node --check "' + confPath + '"', { stdio: 'pipe' });
    console.log('2d. Sintaxis configurador OK');
} catch(e) {
    console.log('2d. ERROR sintaxis:', e.stderr.toString().split('\n').slice(0, 3).join('\n'));
}