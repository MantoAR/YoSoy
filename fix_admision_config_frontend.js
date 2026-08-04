const fs = require('fs');
const { execSync } = require('child_process');

// ── 1. HTML: item Admisiones en el menu Clinico ─────────────
const htmlPath = 'D:/YoSoy/PANTALLAS/Pantalla_Configurador/dashboard.html';
let html = fs.readFileSync(htmlPath, 'utf8');

if (html.indexOf("mostrarTab('admisiones')") === -1) {
    html = html.replace(
        "onclick=\"mostrarTab('reportes')\">Reportes</li>",
        "onclick=\"mostrarTab('admisiones')\">Admisiones</li>\n                    <li onclick=\"mostrarTab('reportes')\">Reportes</li>"
    );
    fs.writeFileSync(htmlPath, html, 'utf8');
    console.log('1. Item Admisiones agregado al menu OK');
} else {
    console.log('1. Item Admisiones ya existia');
}

// ── 2. JS: case en mostrarTab + funciones ───────────────────
const jsPath = 'D:/YoSoy/PANTALLAS/Pantalla_Configurador/js/dashboard.js';
let js = fs.readFileSync(jsPath, 'utf8');

if (js.indexOf("tab === 'admisiones'") === -1) {
    js = js.replace(
        "if (tab === 'reportes') { reportes(); return; }",
        "if (tab === 'reportes') { reportes(); return; }\n    if (tab === 'admisiones') { admisiones(); return; }"
    );
    console.log('2. Case admisiones agregado a mostrarTab OK');
} else {
    console.log('2. Case admisiones ya existia');
}

if (js.indexOf('async function admisiones') === -1) {
    const fn = [
'',
'let _admisionesCache = [];',
'',
'async function admisiones() {',
'    const c = document.getElementById("contenido");',
'    c.innerHTML = \'<div class="spinner">Cargando...</div>\';',
'    try {',
'        const r = await fetch(API + "/informes-admision-detalle");',
'        _admisionesCache = await r.json();',
'    } catch(e) { _admisionesCache = []; }',
'',
'    const badge = function(estado) {',
'        const colores = { "Pendiente": "#fff3cd|#856404", "En entrevista": "#cfe2ff|#084298", "Derivado": "#d1e7dd|#0f5132" };',
'        const par = (colores[estado] || "#e2e3e5|#41464b").split("|");',
'        return \'<span style="background:\' + par[0] + \';color:\' + par[1] + \';border-radius:10px;padding:3px 12px;font-size:12px;font-weight:bold">\' + estado + \'</span>\';',
'    };',
'',
'    const filas = _admisionesCache.map(function(a) {',
'        return \'<tr>\' +',
'            \'<td>\' + a.nombrePaciente + \'</td>\' +',
'            \'<td>\' + a.dni + \'</td>\' +',
'            \'<td>\' + a.especialidad + \'</td>\' +',
'            \'<td>\' + a.fechaCreacion + \'</td>\' +',
'            \'<td>\' + badge(a.estado) + \'</td>\' +',
'            \'<td><button class="btn btn-sm btn-outline-secondary" onclick="verAdmision(\' + a.idInforme + \')">Ver</button></td>\' +',
'            \'</tr>\';',
'    }).join("");',
'',
'    c.innerHTML = \'<h2 style="margin-bottom:20px">Admisiones</h2>\' +',
'        \'<div style="background:white;border-radius:12px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,.08)">\' +',
'        (_admisionesCache.length === 0 ? \'<p style="color:#888">No hay informes de admision.</p>\' :',
'        \'<table class="table"><thead><tr><th>Paciente</th><th>DNI</th><th>Especialidad</th><th>Fecha</th><th>Estado</th><th></th></tr></thead><tbody>\' + filas + \'</tbody></table>\') +',
'        \'</div>\' +',
'        \'<div id="detalleAdmision" style="margin-top:20px"></div>\';',
'}',
'',
'function verAdmision(idInforme) {',
'    const a = _admisionesCache.find(function(x) { return x.idInforme === idInforme; });',
'    if (a === undefined) return;',
'',
'    const campo = function(label, valor) {',
'        if (valor === null || valor === undefined || valor === "") return "";',
'        return \'<div style="margin-bottom:10px"><span style="font-size:11px;font-weight:bold;color:#6A5ACD;text-transform:uppercase;display:block">\' + label + \'</span><span style="font-size:14px">\' + valor + \'</span></div>\';',
'    };',
'',
'    const esEntrevista = a.estado === "En entrevista";',
'    const esPendiente  = a.estado === "Pendiente";',
'    const esDerivado   = a.estado === "Derivado";',
'',
'    let acciones = "";',
'    if (esPendiente) {',
'        acciones = \'<div style="background:#e7f1ff;border:2px solid #0d6efd;border-radius:12px;padding:20px;margin-top:16px">\' +',
'            \'<h4 style="color:#084298;margin-bottom:12px">Programar primera entrevista</h4>\' +',
'            \'<div style="display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end">\' +',
'            \'<div><label style="font-size:12px;font-weight:bold;color:#084298;display:block">Fecha</label><input type="date" id="entFecha" style="padding:8px;border:2px solid #b6d4fe;border-radius:8px"></div>\' +',
'            \'<div><label style="font-size:12px;font-weight:bold;color:#084298;display:block">Hora</label><input type="time" id="entHora" style="padding:8px;border:2px solid #b6d4fe;border-radius:8px"></div>\' +',
'            \'<button onclick="programarEntrevista(\' + a.idInforme + \')" style="padding:9px 20px;background:#0d6efd;color:white;border:none;border-radius:8px;cursor:pointer;font-weight:bold">Programar</button>\' +',
'            \'</div></div>\';',
'    }',
'    if (esEntrevista) {',
'        acciones = \'<div style="background:#f3efff;border:2px solid #6A5ACD;border-radius:12px;padding:20px;margin-top:16px">\' +',
'            \'<h4 style="color:#6A5ACD;margin-bottom:12px">Derivar al profesional</h4>\' +',
'            \'<p style="font-size:12px;color:#888;margin-bottom:10px">Entrevista programada: \' + (a.fechaEntrevista || "-") + \'</p>\' +',
'            \'<label style="font-size:12px;font-weight:bold;color:#6A5ACD;display:block">Observaciones de la entrevista</label>\' +',
'            \'<textarea id="derObs" rows="3" style="width:100%;padding:9px;border:2px solid #e0d4f5;border-radius:8px;margin-bottom:10px">\' + (a.observacionesEntrevista || "") + \'</textarea>\' +',
'            \'<label style="font-size:12px;font-weight:bold;color:#6A5ACD;display:block">Profesional asignado</label>\' +',
'            \'<div style="display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end">\' +',
'            \'<select id="derProf" style="flex:1;min-width:220px;padding:9px;border:2px solid #e0d4f5;border-radius:8px"><option value="">Cargando...</option></select>\' +',
'            \'<button onclick="derivarAdmision(\' + a.idInforme + \')" style="padding:9px 20px;background:#6A5ACD;color:white;border:none;border-radius:8px;cursor:pointer;font-weight:bold">Derivar</button>\' +',
'            \'</div></div>\';',
'    }',
'    if (esDerivado) {',
'        acciones = \'<div style="background:#d1e7dd;border-radius:12px;padding:16px;margin-top:16px">\' +',
'            \'<strong style="color:#0f5132">Derivado a: \' + (a.nombreProfesionalDerivado || "-") + \'</strong>\' +',
'            (a.observacionesEntrevista ? \'<p style="font-size:13px;margin-top:8px">\' + a.observacionesEntrevista + \'</p>\' : "") +',
'            \'</div>\';',
'    }',
'',
'    document.getElementById("detalleAdmision").innerHTML =',
'        \'<div style="background:white;border-radius:12px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,.08)">\' +',
'        \'<h3 style="color:#6A5ACD;margin-bottom:16px">Informe de \' + a.nombrePaciente + \'</h3>\' +',
'        \'<div style="display:grid;grid-template-columns:1fr 1fr;gap:6px 24px">\' +',
'        campo("DNI", a.dni) +',
'        campo("Fecha de nacimiento", a.fechaNacimiento) +',
'        campo("Especialidad solicitada", a.especialidad) +',
'        campo("Tipo de entrevista", a.tipoEntrevista) +',
'        campo("Motivo de consulta", a.motivoConsulta) +',
'        campo("Tratamiento anterior", a.tratamientoAnterior ? ("Si. " + (a.tratamientoAnteriorDetalle || "")) : "No") +',
'        campo("Medicacion actual", a.recibeMedicacion ? ("Si. " + (a.medicacionDetalle || "")) : "No") +',
'        campo("Gustos e intereses", a.gustosIntereses) +',
'        campo("Preferencias de atencion", a.preferenciasAtencion) +',
'        campo("Red de apoyo", a.redApoyo) +',
'        campo("Objetivos del paciente", a.objetivosPaciente) +',
'        \'</div>\' +',
'        \'<button class="btn btn-sm btn-outline-secondary" style="margin-top:10px" onclick="verHCAdmision(\' + a.idPaciente + \')">Ver Historia Clinica completa</button>\' +',
'        acciones +',
'        \'</div>\';',
'',
'    if (esEntrevista) cargarProfesionalesDerivacion();',
'    document.getElementById("detalleAdmision").scrollIntoView({ behavior: "smooth" });',
'}',
'',
'async function cargarProfesionalesDerivacion() {',
'    let profs = [];',
'    try { profs = await (await fetch(API + "/profesionales")).json(); } catch(e) {}',
'    const sel = document.getElementById("derProf");',
'    if (sel === null) return;',
'    sel.innerHTML = \'<option value="">Selecciona un profesional</option>\' + profs',
'        .filter(function(p) { return p.estado === "Activo"; })',
'        .map(function(p) { return \'<option value="\' + p.idProfesional + \'">\' + p.nombre + " " + (p.apellido || "") + \'</option>\'; })',
'        .join("");',
'}',
'',
'async function programarEntrevista(idInforme) {',
'    const fecha = document.getElementById("entFecha").value;',
'    const hora  = document.getElementById("entHora").value;',
'    if (fecha === "" || hora === "") { toast("Completa fecha y hora.", "warn"); return; }',
'    const r = await fetch(API + "/informes-admision/" + idInforme + "/entrevista", {',
'        method: "PUT",',
'        headers: { "Content-Type": "application/json" },',
'        body: JSON.stringify({ fecha: fecha + "T" + hora + ":00", observaciones: null })',
'    });',
'    if (r.ok) { toast("Entrevista programada."); admisiones(); }',
'    else toast("Error al programar.", "error");',
'}',
'',
'async function derivarAdmision(idInforme) {',
'    const idProf = parseInt(document.getElementById("derProf").value);',
'    const obs    = document.getElementById("derObs").value || null;',
'    if (isNaN(idProf)) { toast("Selecciona un profesional.", "warn"); return; }',
'    const r = await fetch(API + "/informes-admision/" + idInforme + "/derivar", {',
'        method: "PUT",',
'        headers: { "Content-Type": "application/json" },',
'        body: JSON.stringify({ idProfesional: idProf, observaciones: obs })',
'    });',
'    if (r.ok) { toast("Paciente derivado. Ya aparece en Mis Pacientes del profesional."); admisiones(); }',
'    else toast("Error al derivar.", "error");',
'}',
'',
'async function verHCAdmision(idPaciente) {',
'    let paciente = null, historia = null, evoluciones = [], sesiones = [];',
'    try { paciente = await (await fetch(API + "/pacientes/" + idPaciente)).json(); } catch(e) {}',
'    try { historia = await (await fetch(API + "/pacientes/" + idPaciente + "/historiaclinica")).json(); } catch(e) {}',
'    if (historia !== null && historia.idHistoria !== undefined) {',
'        try { evoluciones = await (await fetch(API + "/historiasclinicas/" + historia.idHistoria + "/evoluciones")).json(); } catch(e) {}',
'    }',
'    try { sesiones = await (await fetch(API + "/pacientes/" + idPaciente + "/sesiones-clinicas")).json(); } catch(e) {}',
'',
'    const evHtml = evoluciones.map(function(e) {',
'        return \'<div style="border-left:3px solid #6A5ACD;padding:8px 12px;margin-bottom:8px;background:#faf8ff">\' +',
'            \'<strong>\' + new Date(e.fecha).toLocaleDateString("es-AR") + \'</strong>\' +',
'            (e.diagnostico ? \'<div><em>Diagnostico:</em> \' + e.diagnostico + \'</div>\' : "") +',
'            (e.observaciones ? \'<div><em>Observaciones:</em> \' + e.observaciones + \'</div>\' : "") +',
'            \'</div>\';',
'    }).join("") || \'<p style="color:#888;font-size:13px">Sin evoluciones.</p>\';',
'',
'    const sesHtml = sesiones.map(function(s) {',
'        return \'<div style="border-left:3px solid #0d6efd;padding:8px 12px;margin-bottom:8px;background:#f5f9ff">\' +',
'            \'<strong>\' + new Date(s.fechaHora).toLocaleDateString("es-AR") + \'</strong>\' +',
'            (s.tipoCierre ? " - " + s.tipoCierre : "") +',
'            (s.notasEvolucion ? \'<div>\' + s.notasEvolucion + \'</div>\' : "") +',
'            \'</div>\';',
'    }).join("") || \'<p style="color:#888;font-size:13px">Sin sesiones clinicas.</p>\';',
'',
'    document.getElementById("detalleAdmision").innerHTML +=',
'        \'<div style="background:white;border-radius:12px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,.08);margin-top:16px">\' +',
'        \'<h3 style="color:#6A5ACD">Historia Clinica - \' + (paciente !== null ? paciente.nombre : "") + \'</h3>\' +',
'        \'<h4 style="margin-top:14px;font-size:14px">Evoluciones</h4>\' + evHtml +',
'        \'<h4 style="margin-top:14px;font-size:14px">Sesiones clinicas</h4>\' + sesHtml +',
'        \'</div>\';',
'}',
''
    ].join('\n');

    js = js + fn;
    console.log('3. Funciones de admisiones agregadas OK');
} else {
    console.log('3. Funciones ya existian');
}

fs.writeFileSync(jsPath, js, 'utf8');

try {
    execSync('node --check "' + jsPath + '"', { stdio: 'pipe' });
    console.log('4. Sintaxis del dashboard.js configurador: OK');
} catch(e) {
    console.log('4. ERROR de sintaxis:', e.stderr.toString().split('\n').slice(0, 3).join('\n'));
}