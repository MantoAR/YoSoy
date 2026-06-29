const fs = require('fs');

// ── 1. CONFIGURADOR: agregar tab Reportes al HTML ──────────────────
const htmlPath = 'D:/YoSoy/PANTALLAS/Pantalla_Configurador/dashboard.html';
let html = fs.readFileSync(htmlPath, 'utf8');

if (!html.includes("mostrarTab('reportes')")) {
    html = html.replace(
        "onclick=\"mostrarTab('turnos')\">Turnos</li>",
        "onclick=\"mostrarTab('reportes')\">Reportes</li>\n                    <li onclick=\"mostrarTab('turnos')\">Turnos</li>"
    );
    fs.writeFileSync(htmlPath, html, 'utf8');
    console.log('1. Tab Reportes agregado al HTML del configurador OK');
} else {
    console.log('1. Tab Reportes ya existe en HTML');
}

// ── 2. CONFIGURADOR: agregar funcion reportes al JS ────────────────
const jsPath = 'D:/YoSoy/PANTALLAS/Pantalla_Configurador/js/dashboard.js';
let js = fs.readFileSync(jsPath, 'utf8');

if (!js.includes('async function reportes()')) {
    const funcionReportes = `
async function reportes() {
    const c = document.getElementById('contenido');
    c.innerHTML = '<div class="spinner">Cargando...</div>';

    let profesionales = [];
    try {
        const r = await fetch(API + '/profesionales');
        profesionales = await r.json();
    } catch(e) {}

    const opts = profesionales
        .filter(function(p) { return p.estado === 'Activo'; })
        .map(function(p) { return '<option value="' + p.idProfesional + '">' + p.nombre + '</option>'; })
        .join('');

    c.innerHTML =
        '<h2 style="margin-bottom:20px">Reportes</h2>' +
        '<div style="background:white;border-radius:12px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,.08)">' +
        '<h3 style="margin-bottom:16px;color:#6A5ACD">Atenciones por Profesional</h3>' +
        '<div style="display:flex;gap:12px;align-items:flex-end;margin-bottom:20px;flex-wrap:wrap">' +
        '<div>' +
        '<label style="display:block;font-size:12px;font-weight:bold;color:#6A5ACD;margin-bottom:4px">Filtrar por profesional</label>' +
        '<select id="filtroProf" style="padding:8px 12px;border:2px solid #e0d4f5;border-radius:8px;font-size:14px;min-width:220px">' +
        '<option value="">Todos los profesionales</option>' +
        opts +
        '</select>' +
        '</div>' +
        '<button onclick="descargarReporteConf()" style="padding:9px 20px;background:#6A5ACD;color:white;border:none;border-radius:8px;font-size:14px;cursor:pointer;font-weight:bold">Descargar Excel</button>' +
        '</div>' +
        '<p style="font-size:13px;color:#888">Selecciona un profesional o deja en blanco para descargar todos.</p>' +
        '</div>';
}

async function descargarReporteConf() {
    const idProf = document.getElementById('filtroProf').value;
    const url = API + '/reportes/atenciones' + (idProf ? '?idProfesional=' + idProf : '');
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
        .join('\\n');
    const blob = new Blob(['\\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'atenciones_por_profesional.csv';
    link.click();
}
`;
    js = js + funcionReportes;
    fs.writeFileSync(jsPath, js, 'utf8');
    console.log('2. Funciones reportes agregadas al JS del configurador OK');
} else {
    console.log('2. Funciones ya existen en JS del configurador');
}

// ── 3. CONFIGURADOR: agregar caso reportes al mostrarTab ───────────
js = fs.readFileSync(jsPath, 'utf8');
if (!js.includes("case 'reportes'") && !js.includes('"reportes": reportes')) {
    js = js.replace(
        "async function mostrarTab(tab) {",
        "async function mostrarTab(tab) {\n    if (tab === 'reportes') { reportes(); return; }"
    );
    fs.writeFileSync(jsPath, js, 'utf8');
    console.log('3. Case reportes agregado a mostrarTab OK');
} else {
    console.log('3. Case reportes ya existe');
}

// ── 4. PROFESIONAL: restringir a sus propias atenciones ───────────
const profJsPath = 'D:/YoSoy/PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js';
let profJs = fs.readFileSync(profJsPath, 'utf8');

const reportesProfViejo = `async function reportes() {
    const cont = document.getElementById('contenido');
    let profesionales = [];
    try {
        const r = await fetch(API + '/profesionales');
        profesionales = await r.json();
    } catch(e) {}

    const opts = profesionales
        .filter(function(p) { return p.estado === 'Activo'; })
        .map(function(p) { return '<option value="' + p.idProfesional + '">' + p.nombre + ' ' + p.apellido + '</option>'; })
        .join('');

    cont.innerHTML =
        '<div class="titulo-seccion">Reportes</div>' +
        '<div class="card">' +
        '<h3 style="margin-bottom:20px;color:#6A5ACD">Atenciones por Profesional</h3>' +
        '<div style="display:flex;gap:12px;align-items:flex-end;margin-bottom:20px;flex-wrap:wrap">' +
        '<div>' +
        '<label style="display:block;font-size:12px;font-weight:bold;color:#6A5ACD;margin-bottom:4px">Filtrar por profesional</label>' +
        '<select id="filtroProf" style="padding:8px 12px;border:2px solid #e0d4f5;border-radius:8px;font-size:14px;min-width:220px">' +
        '<option value="">Todos los profesionales</option>' +
        opts +
        '</select>' +
        '</div>' +
        '<button onclick="descargarReporte()" style="padding:9px 20px;background:#6A5ACD;color:white;border:none;border-radius:8px;font-size:14px;cursor:pointer;font-weight:bold">Descargar Excel</button>' +
        '</div>' +
        '<p style="font-size:13px;color:#888">Selecciona un profesional o deja en blanco para descargar todos.</p>' +
        '</div>';
}`;

const reportesProfNuevo = `async function reportes() {
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
}`;

if (profJs.includes("Todos los profesionales")) {
    profJs = profJs.replace(reportesProfViejo, reportesProfNuevo);
    fs.writeFileSync(profJsPath, profJs, 'utf8');
    console.log('4. Reportes del profesional restringido a sus propias atenciones OK');
} else {
    console.log('4. Ya estaba restringido o no se encontro el bloque');
}

// ── 5. PROFESIONAL: fix descargarReporte para usar idProfesional ──
profJs = fs.readFileSync(profJsPath, 'utf8');
const descViejo = "    const idProf = document.getElementById('filtroProf').value;\n    const url = API + '/reportes/atenciones' + (idProf ? '?idProfesional=' + idProf : '');";
const descNuevo = "    const url = API + '/reportes/atenciones?idProfesional=' + idProfesional;";

if (profJs.includes(descViejo)) {
    profJs = profJs.replace(descViejo, descNuevo);
    fs.writeFileSync(profJsPath, profJs, 'utf8');
    console.log('5. descargarReporte corregido para usar idProfesional OK');
} else {
    console.log('5. descargarReporte ya estaba correcto o no se encontro');
}

console.log('\nVerificando sintaxis...');
try {
    require('child_process').execSync('node --check ' + profJsPath, {stdio: 'pipe'});
    console.log('nuevo_dashboard.js: sin errores de sintaxis');
} catch(e) {
    console.log('ERROR en nuevo_dashboard.js:', e.stderr.toString());
}