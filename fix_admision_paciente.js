const fs = require('fs');
const { execSync } = require('child_process');

// ── 1. HTML: agregar tab Admision ───────────────────────────
const htmlPath = 'D:/YoSoy/PANTALLAS/Pantalla_Paciente/dashboard.html';
let html = fs.readFileSync(htmlPath, 'utf8');

if (html.indexOf('admision') === -1) {
    // Insertar el tab despues del boton de Sesion (o de Perfil si no existe)
    if (html.indexOf('data-sec="sesion"') > -1) {
        html = html.replace(
            /(<button class="tab-btn" data-sec="sesion"[^>]*>[^<]*<\/button>)/,
            '$1\n        <button class="tab-btn" data-sec="admision" onclick="mostrarSeccion(\'admision\')">Admision</button>'
        );
    } else {
        html = html.replace(
            /(<button class="tab-btn" data-sec="perfil"[^>]*>[^<]*<\/button>)/,
            '$1\n        <button class="tab-btn" data-sec="admision" onclick="mostrarSeccion(\'admision\')">Admision</button>'
        );
    }
    fs.writeFileSync(htmlPath, html, 'utf8');
    console.log('1. Tab Admision agregado al HTML OK');
} else {
    console.log('1. Tab Admision ya existia');
}

// ── 2. JS: agregar caso en mostrarSeccion ───────────────────
const jsPath = 'D:/YoSoy/PANTALLAS/Pantalla_Paciente/js/dashboard.js';
let js = fs.readFileSync(jsPath, 'utf8');

if (js.indexOf('id === "admision"') === -1) {
    js = js.replace(
        'else if (id === "sesion")        c.innerHTML = renderSesion();',
        'else if (id === "sesion")        c.innerHTML = renderSesion();\n    else if (id === "admision")      renderAdmision(c);'
    );
    console.log('2. Caso admision agregado a mostrarSeccion OK');
} else {
    console.log('2. Caso admision ya existia');
}

// ── 3. JS: agregar funciones de admision al final ───────────
if (js.indexOf('async function renderAdmision') === -1) {
    const funciones = [
'',
'async function renderAdmision(c) {',
'    // Ver si ya tiene informe enviado',
'    try {',
'        const r = await fetch(API + "/pacientes/" + idPaciente + "/informe-admision");',
'        if (r.ok) {',
'            const inf = await r.json();',
'            const colores = { "Pendiente": "#fff3cd|#856404", "En entrevista": "#cfe2ff|#084298", "Derivado": "#d1e7dd|#0f5132" };',
'            const par = (colores[inf.estado] || "#e2e3e5|#41464b").split("|");',
'            c.innerHTML = \'<div class="titulo-seccion">Admision</div>\' +',
'                \'<div class="card" style="max-width:600px">\' +',
'                \'<h3 style="color:#6A5ACD;margin-bottom:16px">Tu informe de admision ya fue enviado</h3>\' +',
'                \'<p style="margin-bottom:10px">Estado actual: <span style="background:\' + par[0] + \';color:\' + par[1] + \';border-radius:10px;padding:4px 14px;font-weight:bold">\' + inf.estado + \'</span></p>\' +',
'                \'<p style="font-size:13px;color:#888">Cuando el equipo de admision revise tu informe, te contactaremos para coordinar la primera entrevista.</p>\' +',
'                \'</div>\';',
'            return;',
'        }',
'    } catch(e) {}',
'',
'    // No tiene informe: mostrar formulario',
'    let especialidades = [];',
'    try { especialidades = await (await fetch(API + "/especialidades")).json(); } catch(e) {}',
'    const opts = especialidades',
'        .filter(function(e) { return e.estado === "Activo"; })',
'        .map(function(e) { return \'<option value="\' + e.idEspecialidad + \'">\' + e.nombre + \'</option>\'; })',
'        .join("");',
'',
'    const inputStyle = "width:100%;padding:9px 12px;border:2px solid #e0d4f5;border-radius:8px;font-size:14px;margin-top:4px";',
'    const labelStyle = "display:block;font-size:12px;font-weight:bold;color:#6A5ACD;margin-top:14px";',
'',
'    c.innerHTML = \'<div class="titulo-seccion">Informe de Admision</div>\' +',
'        \'<div class="card" style="max-width:700px">\' +',
'        \'<p style="font-size:13px;color:#888;margin-bottom:6px">Completa este formulario para que el equipo de admision pueda conocerte y asignarte el profesional adecuado.</p>\' +',
'',
'        \'<label style="\' + labelStyle + \'">Especialidad solicitada *</label>\' +',
'        \'<select id="admEspecialidad" style="\' + inputStyle + \'"><option value="">Selecciona una especialidad</option>\' + opts + \'</select>\' +',
'',
'        \'<label style="\' + labelStyle + \'">Motivo de consulta *</label>\' +',
'        \'<textarea id="admMotivo" rows="3" style="\' + inputStyle + \'" placeholder="Describe brevemente el motivo por el cual solicitas atencion"></textarea>\' +',
'',
'        \'<label style="\' + labelStyle + \'">Has recibido tratamiento anteriormente?</label>\' +',
'        \'<select id="admTratAnterior" style="\' + inputStyle + \'" onchange="document.getElementById(\\\'admTratDetalle\\\').style.display = this.value === \\\'true\\\' ? \\\'block\\\' : \\\'none\\\'"><option value="false">No</option><option value="true">Si</option></select>\' +',
'        \'<input type="text" id="admTratDetalle" style="\' + inputStyle + \';display:none" placeholder="Indica cual">\' +',
'',
'        \'<label style="\' + labelStyle + \'">Actualmente recibes medicacion?</label>\' +',
'        \'<select id="admMedicacion" style="\' + inputStyle + \'" onchange="document.getElementById(\\\'admMedDetalle\\\').style.display = this.value === \\\'true\\\' ? \\\'block\\\' : \\\'none\\\'"><option value="false">No</option><option value="true">Si</option></select>\' +',
'        \'<input type="text" id="admMedDetalle" style="\' + inputStyle + \';display:none" placeholder="Cual?">\' +',
'',
'        \'<h4 style="color:#6A5ACD;margin-top:24px">Conociendote mejor</h4>\' +',
'        \'<p style="font-size:12px;color:#999">Estas preguntas nos ayudan a brindarte una atencion personalizada.</p>\' +',
'',
'        \'<label style="\' + labelStyle + \'">Gustos e intereses</label>\' +',
'        \'<textarea id="admGustos" rows="3" style="\' + inputStyle + \'" placeholder="Actividades que disfrutas, hobbies, musica, peliculas o libros, deportes, que te ayuda a relajarte, algo que quieras aprender"></textarea>\' +',
'',
'        \'<label style="\' + labelStyle + \'">Preferencias durante la atencion</label>\' +',
'        \'<textarea id="admPreferencias" rows="3" style="\' + inputStyle + \'" placeholder="Como preferis que se dirijan a vos, algo que te genere ansiedad en una consulta, apoyos que necesites"></textarea>\' +',
'',
'        \'<label style="\' + labelStyle + \'">Tipo de entrevista preferida</label>\' +',
'        \'<select id="admTipoEntrevista" style="\' + inputStyle + \'"><option value="Sin preferencia">Sin preferencia</option><option value="Individual">Individual</option><option value="Familiar">Familiar</option><option value="Grupal">Grupal</option></select>\' +',
'',
'        \'<label style="\' + labelStyle + \'">Red de apoyo</label>\' +',
'        \'<textarea id="admRedApoyo" rows="2" style="\' + inputStyle + \'" placeholder="Con quien convivis? Contas con familiares o personas de confianza que te acompanen?"></textarea>\' +',
'',
'        \'<label style="\' + labelStyle + \'">Que esperas lograr con el tratamiento?</label>\' +',
'        \'<textarea id="admObjetivos" rows="2" style="\' + inputStyle + \'"></textarea>\' +',
'',
'        \'<button onclick="enviarAdmision()" style="width:100%;margin-top:20px;padding:12px;background:#6A5ACD;color:white;border:none;border-radius:8px;font-size:15px;cursor:pointer;font-weight:bold">Enviar informe de admision</button>\' +',
'        \'</div>\';',
'}',
'',
'async function enviarAdmision() {',
'    const idEsp  = parseInt(document.getElementById("admEspecialidad").value);',
'    const motivo = document.getElementById("admMotivo").value.trim();',
'    if (isNaN(idEsp) || idEsp === 0) { toast("Selecciona una especialidad.", "warn"); return; }',
'    if (motivo === "") { toast("Completa el motivo de consulta.", "warn"); return; }',
'',
'    const body = {',
'        idPaciente:                 parseInt(idPaciente),',
'        idEspecialidad:             idEsp,',
'        motivoConsulta:             motivo,',
'        tratamientoAnterior:        document.getElementById("admTratAnterior").value === "true",',
'        tratamientoAnteriorDetalle: document.getElementById("admTratDetalle").value || null,',
'        recibeMedicacion:           document.getElementById("admMedicacion").value === "true",',
'        medicacionDetalle:          document.getElementById("admMedDetalle").value || null,',
'        gustosIntereses:            document.getElementById("admGustos").value || null,',
'        preferenciasAtencion:       document.getElementById("admPreferencias").value || null,',
'        tipoEntrevista:             document.getElementById("admTipoEntrevista").value,',
'        redApoyo:                   document.getElementById("admRedApoyo").value || null,',
'        objetivosPaciente:          document.getElementById("admObjetivos").value || null,',
'        estado:                     "Pendiente"',
'    };',
'',
'    try {',
'        const r = await fetch(API + "/informes-admision", {',
'            method: "POST",',
'            headers: { "Content-Type": "application/json" },',
'            body: JSON.stringify(body)',
'        });',
'        if (r.ok) {',
'            toast("Informe enviado correctamente. El equipo de admision te contactara.");',
'            mostrarSeccion("admision");',
'        } else toast("Error al enviar el informe.", "error");',
'    } catch(e) { toast("Error de conexion.", "error"); }',
'}',
''
    ].join('\n');

    js = js + funciones;
    console.log('3. Funciones renderAdmision y enviarAdmision agregadas OK');
} else {
    console.log('3. Funciones ya existian');
}

fs.writeFileSync(jsPath, js, 'utf8');

// ── 4. Verificar sintaxis ───────────────────────────────────
try {
    execSync('node --check "' + jsPath + '"', { stdio: 'pipe' });
    console.log('4. Sintaxis del dashboard.js: OK');
} catch(e) {
    console.log('4. ERROR de sintaxis:', e.stderr.toString().split('\n').slice(0, 3).join('\n'));
}