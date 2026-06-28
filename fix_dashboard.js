const fs = require('fs');

const html = `<!DOCTYPE html>
<html lang="es">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>YoSoy - Panel Paciente</title>
    <link rel="stylesheet" href="css/dashboard.css">
</head>
<body>
    <nav class="navbar">
        <div class="navbar-brand">
            <img src="../Pantalla_Usuario/Usuario/img/logo_SINFONDO.png" alt="Logo">
            YoSoy <span class="badge-panel">Panel Paciente</span>
        </div>
        <button class="btn-cerrar" onclick="cerrarSesion()">Cerrar sesion</button>
    </nav>
    <div class="tabs-bar">
        <button class="tab-btn" data-sec="inicio" onclick="mostrarSeccion('inicio')">Inicio</button>
        <button class="tab-btn" data-sec="agenda" onclick="mostrarSeccion('agenda')">Mi Agenda</button>
        <button class="tab-btn" data-sec="profesionales" onclick="mostrarSeccion('profesionales')">Mis Profesionales</button>
        <button class="tab-btn" data-sec="perfil" onclick="mostrarSeccion('perfil')">Mi Perfil</button>
        <button class="tab-btn" data-sec="sesion" onclick="mostrarSeccion('sesion')">Sesion</button>
    </div>
    <main class="contenido" id="contenido"></main>
    <script src="../js/sesion.js"></script>
    <script src="js/dashboard.js"></script>
</body>
</html>`;

fs.writeFileSync('C:/Users/Araceli/YoSoy/PANTALLAS/Pantalla_Paciente/dashboard.html', html, 'utf8');
console.log('dashboard.html OK');

// Agregar funciones al JS
const jsPath = 'C:/Users/Araceli/YoSoy/PANTALLAS/Pantalla_Paciente/js/dashboard.js';
let js = fs.readFileSync(jsPath, 'utf8');

if (js.includes('function renderSesion')) {
    console.log('renderSesion ya existe en JS');
} else {
    const funciones = `
function renderSesion() {
    return '<div style="padding:30px;max-width:500px;margin:0 auto">' +
        '<h2 style="margin-bottom:20px">Iniciar sesion de video</h2>' +
        '<p style="margin-bottom:15px">Ingresa el codigo de sala que te dio tu profesional:</p>' +
        '<input type="text" id="codigoSala" placeholder="Codigo de sala" style="width:100%;padding:10px;font-size:16px;border:1px solid #ccc;border-radius:6px;margin-bottom:15px">' +
        '<button onclick="entrarSesion()" style="width:100%;padding:12px;background:#4a90d9;color:white;border:none;border-radius:6px;font-size:16px;cursor:pointer">Entrar a la sesion</button>' +
        '</div>';
}

function entrarSesion() {
    var codigo = document.getElementById('codigoSala').value.trim();
    if (!codigo) { alert('Ingresa el codigo de sala.'); return; }
    window.location.href = '../Pantalla_Paciente/sesion_video.html?sala=' + encodeURIComponent(codigo) + '&idPaciente=' + idPaciente;
}
`;
    js += funciones;
    fs.writeFileSync(jsPath, js, 'utf8');
    console.log('Funciones agregadas al JS OK');
}