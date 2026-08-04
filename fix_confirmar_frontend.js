const fs = require('fs');
const { execSync } = require('child_process');

// ══════════════════════════════════════════════════════════════
// 1. PACIENTE: boton Confirmar en turnos propuestos por el profesional
// ══════════════════════════════════════════════════════════════
const pacPath = 'D:/YoSoy/PANTALLAS/Pantalla_Paciente/js/dashboard.js';
let pac = fs.readFileSync(pacPath, 'utf8');

const anclaBotones = '<div class="panel-acciones">\n                <button class="btn-accion btn-cancelar" onclick="cancelarTurno(${t.idTurno})">Cancelar turno</button>';
const nuevoBotones = '<div class="panel-acciones">\n                ${(t.estadoDescripcion === "Pendiente" && t.confirmadoPaciente === false) ? `<button class="btn-accion btn-guardar" onclick="confirmarTurnoPaciente(${t.idTurno})">Confirmar</button>` : ""}\n                <button class="btn-accion btn-cancelar" onclick="cancelarTurno(${t.idTurno})">Cancelar turno</button>';

if (pac.indexOf('confirmarTurnoPaciente') > -1) {
    console.log('1a. Boton Confirmar ya existia en el paciente');
} else if (pac.indexOf(anclaBotones) > -1) {
    pac = pac.replace(anclaBotones, nuevoBotones);
    console.log('1a. Boton Confirmar agregado al turno del paciente OK');
} else {
    console.log('1a. ERROR: no se encontro el bloque de botones del turno');
}

// Funcion confirmarTurnoPaciente
if (pac.indexOf('async function confirmarTurnoPaciente') === -1) {
    pac = pac + [
'',
'async function confirmarTurnoPaciente(idTurno) {',
'    if (!await confirmar("Confirmar este turno?")) return;',
'    try {',
'        const r = await fetch(API + "/turnos/" + idTurno + "/confirmar-paciente", { method: "PUT" });',
'        if (r.ok) {',
'            toast("Turno confirmado. El profesional generara la sala para ese dia.");',
'            const res = await fetch(API + "/pacientes/" + idPaciente + "/turnos-detalle");',
'            turnosCalendario = await res.json();',
'            dibujarCalendario();',
'            document.getElementById("panelTurno").classList.remove("visible");',
'        } else toast("Error al confirmar.", "error");',
'    } catch(e) { toast("Error de conexion.", "error"); }',
'}',
''
    ].join('\n');
    console.log('1b. Funcion confirmarTurnoPaciente agregada OK');
} else {
    console.log('1b. Funcion ya existia');
}

fs.writeFileSync(pacPath, pac, 'utf8');
try {
    execSync('node --check "' + pacPath + '"', { stdio: 'pipe' });
    console.log('1c. Sintaxis paciente OK');
} catch(e) {
    console.log('1c. ERROR sintaxis:', e.stderr.toString().split('\n').slice(0, 3).join('\n'));
}

// ══════════════════════════════════════════════════════════════
// 2. PACIENTE: casilla de notificaciones (campanita en la navbar)
// ══════════════════════════════════════════════════════════════
// Se agrega una campanita flotante fija que abre el historial completo.
if (pac.indexOf('abrirCasillaNotificaciones') === -1) {
    let pac2 = fs.readFileSync(pacPath, 'utf8');
    pac2 = pac2 + [
'',
'// Casilla de notificaciones (campanita con historial)',
'async function initCampanita() {',
'    if (document.getElementById("campanita-notif") !== null) return;',
'    const btn = document.createElement("div");',
'    btn.id = "campanita-notif";',
'    btn.style.cssText = "position:fixed;top:16px;right:20px;z-index:9997;cursor:pointer;background:white;width:44px;height:44px;border-radius:50%;box-shadow:0 2px 8px rgba(0,0,0,.2);display:flex;align-items:center;justify-content:center;font-size:20px";',
'    btn.innerHTML = "&#128276;<span id=\\"campanita-badge\\" style=\\"position:absolute;top:-2px;right:-2px;background:#e53935;color:white;border-radius:50%;min-width:18px;height:18px;font-size:11px;display:none;align-items:center;justify-content:center;font-weight:bold;padding:0 4px\\"></span>";',
'    btn.onclick = abrirCasillaNotificaciones;',
'    document.body.appendChild(btn);',
'    refrescarBadgeNotif();',
'}',
'',
'async function refrescarBadgeNotif() {',
'    let notifs = [];',
'    try { notifs = await (await fetch(API + "/pacientes/" + idPaciente + "/notificaciones")).json(); } catch(e) { return; }',
'    const noLeidas = notifs.filter(function(n) { return n.leida === false; });',
'    const badge = document.getElementById("campanita-badge");',
'    if (badge === null) return;',
'    if (noLeidas.length > 0) { badge.textContent = noLeidas.length; badge.style.display = "flex"; }',
'    else badge.style.display = "none";',
'}',
'',
'async function abrirCasillaNotificaciones() {',
'    let notifs = [];',
'    try { notifs = await (await fetch(API + "/pacientes/" + idPaciente + "/notificaciones")).json(); } catch(e) {}',
'',
'    let overlay = document.getElementById("casilla-overlay");',
'    if (overlay === null) {',
'        overlay = document.createElement("div");',
'        overlay.id = "casilla-overlay";',
'        overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.4);z-index:9999;display:flex;justify-content:flex-end";',
'        overlay.onclick = function(e) { if (e.target === overlay) overlay.remove(); };',
'        document.body.appendChild(overlay);',
'    }',
'',
'    const items = notifs.length === 0 ? \'<p style="color:#888;padding:20px">No tenes notificaciones.</p>\' :',
'        notifs.map(function(n) {',
'            const link = (n.link !== null && n.link !== "") ?',
'                \'<button onclick="unirseDesdeCasilla(\\\'\' + n.link + \'\\\', \' + n.idNotificacion + \')" style="margin-top:8px;padding:6px 14px;background:#0d6efd;color:white;border:none;border-radius:6px;cursor:pointer;font-size:12px;font-weight:bold">Unirse / Ver sala</button>\' : "";',
'            return \'<div style="background:\' + (n.leida ? "#f8f8f8" : "#eef5ff") + \';border-left:4px solid \' + (n.leida ? "#ccc" : "#0d6efd") + \';border-radius:8px;padding:12px 14px;margin-bottom:10px">\' +',
'                \'<div style="font-weight:bold;color:#333;margin-bottom:4px">\' + n.titulo + \'</div>\' +',
'                \'<div style="font-size:13px;color:#555">\' + n.mensaje + \'</div>\' +',
'                \'<div style="font-size:11px;color:#aaa;margin-top:4px">\' + n.fecha + \'</div>\' +',
'                link +',
'                \'</div>\';',
'        }).join("");',
'',
'    overlay.innerHTML = \'<div style="background:white;width:380px;max-width:90%;height:100%;overflow-y:auto;padding:20px;box-shadow:-4px 0 20px rgba(0,0,0,.15)">\' +',
'        \'<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">\' +',
'        \'<h3 style="margin:0;color:#6A5ACD">Notificaciones</h3>\' +',
'        \'<button onclick="document.getElementById(\\\'casilla-overlay\\\').remove()" style="background:none;border:none;font-size:22px;cursor:pointer;color:#999">&times;</button>\' +',
'        \'</div>\' + items + \'</div>\';',
'',
'    // marcar todas como leidas',
'    for (const n of notifs) {',
'        if (n.leida === false) { try { await fetch(API + "/notificaciones/" + n.idNotificacion + "/leida", { method: "PUT" }); } catch(e) {} }',
'    }',
'    refrescarBadgeNotif();',
'}',
'',
'async function unirseDesdeCasilla(codigoSala, idNotif) {',
'    try { await fetch(API + "/notificaciones/" + idNotif + "/leida", { method: "PUT" }); } catch(e) {}',
'    window.location.href = "sesion_video.html?sala=" + encodeURIComponent(codigoSala) + "&idPaciente=" + idPaciente;',
'}',
'',
'initCampanita();',
''
    ].join('\n');
    fs.writeFileSync(pacPath, pac2, 'utf8');
    console.log('2. Casilla de notificaciones (campanita) agregada OK');
} else {
    console.log('2. Casilla ya existia');
}

try {
    execSync('node --check "' + pacPath + '"', { stdio: 'pipe' });
    console.log('2b. Sintaxis paciente OK');
} catch(e) {
    console.log('2b. ERROR sintaxis:', e.stderr.toString().split('\n').slice(0, 3).join('\n'));
}

// ══════════════════════════════════════════════════════════════
// 3. PROFESIONAL: seccion "Turnos confirmados" con boton Generar sala
//    Se agrega dentro de la pantalla de inicio como panel extra.
// ══════════════════════════════════════════════════════════════
const profPath = 'D:/YoSoy/PANTALLAS/Pantalla_Profesional/js/nuevo_dashboard.js';
let prof = fs.readFileSync(profPath, 'utf8');

if (prof.indexOf('generarSalaTurno') === -1) {
    prof = prof + [
'',
'// Panel de turnos confirmados pendientes de generar sala',
'async function cargarTurnosConfirmados() {',
'    let lista = [];',
'    try { lista = await (await fetch(API + "/profesionales/" + idProfesional + "/turnos-confirmados")).json(); } catch(e) { return; }',
'    const sinSala = lista.filter(function(t) { return t.codigoSala === "" || t.codigoSala === null; });',
'    if (sinSala.length === 0) return;',
'',
'    let cont = document.getElementById("panel-confirmados");',
'    if (cont === null) {',
'        cont = document.createElement("div");',
'        cont.id = "panel-confirmados";',
'        cont.style.cssText = "position:fixed;bottom:20px;right:20px;z-index:9990;max-width:360px;display:flex;flex-direction:column;gap:10px";',
'        document.body.appendChild(cont);',
'    }',
'    cont.innerHTML = sinSala.map(function(t) {',
'        return \'<div style="background:white;border-left:5px solid #198754;border-radius:10px;padding:14px 16px;box-shadow:0 4px 16px rgba(0,0,0,.15)">\' +',
'            \'<div style="font-weight:bold;color:#0f5132;margin-bottom:4px">Turno confirmado</div>\' +',
'            \'<div style="font-size:13px;color:#444;margin-bottom:8px">\' + t.nombrePaciente + \' - \' + t.fecha + \' \' + t.hora + \' hs</div>\' +',
'            \'<button onclick="generarSalaTurno(\' + t.idTurno + \')" style="padding:7px 16px;background:#198754;color:white;border:none;border-radius:6px;cursor:pointer;font-size:13px;font-weight:bold">Generar sala</button>\' +',
'            \'</div>\';',
'    }).join("");',
'}',
'',
'async function generarSalaTurno(idTurno) {',
'    try {',
'        const r = await fetch(API + "/turnos/" + idTurno + "/generar-sala", { method: "PUT" });',
'        if (r.ok) {',
'            const resp = await r.json();',
'            toast("Sala generada: " + resp.codigoSala + ". El paciente fue notificado.");',
'            const cont = document.getElementById("panel-confirmados");',
'            if (cont !== null) cont.innerHTML = "";',
'            cargarTurnosConfirmados();',
'        } else toast("Error al generar la sala.", "error");',
'    } catch(e) { toast("Error de conexion.", "error"); }',
'}',
'',
'cargarTurnosConfirmados();',
''
    ].join('\n');
    fs.writeFileSync(profPath, prof, 'utf8');
    console.log('3. Panel turnos confirmados + generar sala agregado al profesional OK');
} else {
    console.log('3. Ya existia en el profesional');
}

try {
    execSync('node --check "' + profPath + '"', { stdio: 'pipe' });
    console.log('3b. Sintaxis profesional OK');
} catch(e) {
    console.log('3b. ERROR sintaxis:', e.stderr.toString().split('\n').slice(0, 3).join('\n'));
}