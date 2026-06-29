const fs = require('fs');
const path = 'D:/YoSoy/PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js';
let c = fs.readFileSync(path, 'utf8');

// 1. Fix activarTab
c = c.replace(
    "({ inicio, agenda, pacientes, sesion, perfil })[nombre]();",
    "const tabs = { inicio, agenda, pacientes, sesion, perfil }; if (nombre === 'reportes') { reportes(); } else { tabs[nombre](); }"
);

// 2. Agregar funciones AL FINAL del archivo
const funciones = `
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
    const blob = new Blob(['\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'mis_atenciones.csv';
    link.click();
}
`;

// Agregar al final
c = c + funciones;

fs.writeFileSync(path, c, 'utf8');
console.log('Escrito OK');

// Verificaciones
const v = fs.readFileSync(path, 'utf8');
console.log('Funciones reportes:', (v.match(/async function reportes\(\)/g) || []).length);
console.log('activarTab correcto:', v.includes("if (nombre === 'reportes') { reportes(); }"));

// Verificar sintaxis
try {
    require('child_process').execSync('node --check ' + path, {stdio: 'pipe'});
    console.log('Sintaxis: OK');
} catch(e) {
    console.log('ERROR sintaxis:', e.stderr.toString());
}