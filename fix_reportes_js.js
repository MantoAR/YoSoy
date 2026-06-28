const fs = require('fs');
const path = 'C:/Users/Araceli/YoSoy/PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js';
let c = fs.readFileSync(path, 'utf8');

const funcionReportes = `
async function reportes() {
    const cont = document.getElementById('contenido');

    // Cargar lista de profesionales para el filtro
    let profesionales = [];
    try {
        const r = await fetch(API + '/profesionales');
        profesionales = await r.json();
    } catch(e) {}

    cont.innerHTML = \`
        <div class="titulo-seccion">Reportes</div>
        <div class="card">
            <h3 style="margin-bottom:20px;color:#6A5ACD">Atenciones por Profesional</h3>
            <div style="display:flex;gap:12px;align-items:flex-end;margin-bottom:20px;flex-wrap:wrap">
                <div>
                    <label style="display:block;font-size:12px;font-weight:bold;color:#6A5ACD;margin-bottom:4px">Filtrar por profesional</label>
                    <select id="filtroProf" style="padding:8px 12px;border:2px solid #e0d4f5;border-radius:8px;font-size:14px;min-width:220px">
                        <option value="">Todos los profesionales</option>
                        \${profesionales.filter(p => p.estado === 'Activo').map(p => '<option value="' + p.idProfesional + '">' + p.nombre + ' ' + p.apellido + '</option>').join('')}
                    </select>
                </div>
                <button onclick="descargarReporte()" style="padding:9px 20px;background:#6A5ACD;color:white;border:none;border-radius:8px;font-size:14px;cursor:pointer;font-weight:bold">
                    Descargar Excel
                </button>
            </div>
            <div id="previstaReporte" style="font-size:13px;color:#888">Seleccioná un profesional o dejá en blanco para descargar todos.</div>
        </div>
    \`;
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

    // Armar CSV
    const encabezado = ['Nombre', 'Apellido', 'DNI', 'NHC', 'Fecha', 'Hora', 'Tipo de Cierre', 'Profesional'];
    const filas = datos.map(d => [
        d.nombre,
        d.apellido,
        d.dni,
        d.nhc || '',
        d.fecha,
        d.hora,
        d.tipoCierre || '',
        d.nombreProfesional
    ]);

    const csvContent = [encabezado, ...filas]
        .map(row => row.map(v => '"' + String(v).replace(/"/g, '""') + '"').join(','))
        .join('\\n');

    const blob = new Blob(['\\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'atenciones_por_profesional.csv';
    link.click();
}
`;

// Agregar antes del activarTab('inicio') final
c = c.replace("activarTab('inicio');", funcionReportes + "\nactivarTab('inicio');");
fs.writeFileSync(path, c, 'utf8');
console.log('Funcion reportes agregada OK');