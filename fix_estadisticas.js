const fs = require('fs');
const path = 'D:/YoSoy/PANTALLAS/Pantalla_Configurador/js/dashboard.js';
let c = fs.readFileSync(path, 'utf8');

// Reemplazar la funcion reportes existente con la nueva que incluye estadisticas
const reportesViejo = `async function reportes() {
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
}`;

const reportesNuevo = `async function reportes() {
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
        '<div style="background:white;border-radius:12px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,.08);margin-bottom:20px">' +
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
        '<button onclick="verEstadisticas()" style="padding:9px 20px;background:#0d6efd;color:white;border:none;border-radius:8px;font-size:14px;cursor:pointer;font-weight:bold">Ver Estadisticas</button>' +
        '</div>' +
        '<p style="font-size:13px;color:#888">Selecciona un profesional o deja en blanco para ver todos.</p>' +
        '</div>' +
        '<div id="panelEstadisticas" style="display:none;background:white;border-radius:12px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,.08)">' +
        '<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">' +
        '<h3 style="color:#6A5ACD;margin:0">Estadisticas</h3>' +
        '<button onclick="descargarGrafico()" style="padding:7px 16px;background:#198754;color:white;border:none;border-radius:8px;font-size:13px;cursor:pointer;font-weight:bold">Descargar Imagen</button>' +
        '</div>' +
        '<canvas id="graficoAtenciones" style="max-height:400px"></canvas>' +
        '</div>';
}

async function verEstadisticas() {
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
        alert('No hay datos para mostrar.');
        return;
    }

    // Agrupar por profesional
    const conteo = {};
    datos.forEach(function(d) {
        const key = d.nombreProfesional;
        conteo[key] = (conteo[key] || 0) + 1;
    });
    const labels = Object.keys(conteo);
    const valores = Object.values(conteo);
    const colores = labels.map(function(_, i) {
        const palette = ['#6A5ACD','#0d6efd','#198754','#fd7e14','#dc3545','#20c997','#6610f2','#0dcaf0'];
        return palette[i % palette.length];
    });

    const panel = document.getElementById('panelEstadisticas');
    panel.style.display = 'block';

    // Destruir grafico anterior si existe
    if (window._chartAtenciones) {
        window._chartAtenciones.destroy();
    }

    // Cargar Chart.js si no esta cargado
    if (!window.Chart) {
        await new Promise(function(resolve) {
            const script = document.createElement('script');
            script.src = 'https://cdn.jsdelivr.net/npm/chart.js';
            script.onload = resolve;
            document.head.appendChild(script);
        });
    }

    const ctx = document.getElementById('graficoAtenciones').getContext('2d');
    window._chartAtenciones = new window.Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Cantidad de atenciones',
                data: valores,
                backgroundColor: colores,
                borderRadius: 8
            }]
        },
        options: {
            responsive: true,
            plugins: {
                legend: { display: false },
                title: {
                    display: true,
                    text: 'Atenciones por Profesional',
                    font: { size: 16 }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: { stepSize: 1 }
                }
            }
        }
    });

    panel.scrollIntoView({ behavior: 'smooth' });
}

function descargarGrafico() {
    const canvas = document.getElementById('graficoAtenciones');
    if (!canvas) { alert('Primero genera las estadisticas.'); return; }
    const link = document.createElement('a');
    link.href = canvas.toDataURL('image/png');
    link.download = 'estadisticas_atenciones.png';
    link.click();
}`;

if (c.includes('async function reportes()')) {
    c = c.replace(reportesViejo, reportesNuevo);
    fs.writeFileSync(path, c, 'utf8');
    console.log('Funcion reportes actualizada OK');
} else {
    console.log('ERROR: No se encontro la funcion reportes');
}

// Verificar sintaxis
try {
    require('child_process').execSync('node --check ' + path, {stdio: 'pipe'});
    console.log('Sintaxis: OK');
} catch(e) {
    console.log('ERROR sintaxis:', e.stderr.toString().split('\n').slice(0,3).join('\n'));
}