const fs = require('fs');
const path = 'C:/Users/Araceli/YoSoy/PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js';
let c = fs.readFileSync(path, 'utf8');

// 1. Actualizar activarTab
c = c.replace(
    "({ inicio, agenda, pacientes, sesion, perfil })[nombre]();",
    "const tabs = { inicio, agenda, pacientes, sesion, perfil }; if (nombre === 'reportes') { reportes(); } else { tabs[nombre](); }"
);

// 2. Agregar funciones reportes y descargarReporte ANTES de la ultima linea
const funciones = `
async function reportes() {
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
}

async function descargarReporte() {
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
    const encabezado = ['Nombre', 'Apellido', 'DNI', 'NHC', 'Fecha', 'Hora', 'Tipo de Cierre', 'Profesional'];
    const filas = datos.map(function(d) {
        return [d.nombre, d.apellido, d.dni, d.nhc || '', d.fecha, d.hora, d.tipoCierre || '', d.nombreProfesional];
    });
    const csvContent = [encabezado].concat(filas)
        .map(function(row) { return row.map(function(v) { return '"' + String(v).replace(/"/g, '""') + '"'; }).join(','); })
        .join('\n');
    const blob = new Blob(['\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'atenciones_por_profesional.csv';
    link.click();
}

`;

c = c.replace("activarTab('inicio');", funciones + "activarTab('inicio');");
fs.writeFileSync(path, c, 'utf8');
console.log('Reportes agregado OK');

// Verificar
const verificar = fs.readFileSync(path, 'utf8');
const linea12 = verificar.split('\n')[11];
console.log('Linea 12:', linea12.trim());
const tieneReportes = verificar.includes('async function reportes()');
console.log('Tiene funcion reportes:', tieneReportes);