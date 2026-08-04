const fs = require('fs');
const { execSync } = require('child_process');

// ══════════════════════════════════════════════════════════════
// 1. CONFIGURADOR: mostrar codigo de sala + boton iniciar entrevista
// ══════════════════════════════════════════════════════════════
const confPath = 'D:/YoSoy/PANTALLAS/Pantalla_Configurador/js/dashboard.js';
let conf = fs.readFileSync(confPath, 'utf8');

const anclaEntrevista = "'<p style=\"font-size:12px;color:#888;margin-bottom:10px\">Entrevista programada: ' + (a.fechaEntrevista || \"-\") + '</p>' +";
const nuevoEntrevista = "'<p style=\"font-size:12px;color:#888;margin-bottom:6px\">Entrevista programada: ' + (a.fechaEntrevista || \"-\") + '</p>' +\n            '<p style=\"font-size:13px;margin-bottom:10px\">Codigo de sala: <strong style=\"color:#0d6efd;letter-spacing:1px\">' + (a.codigoSala || \"-\") + '</strong> <button onclick=\"iniciarEntrevistaVideo(\\'' + a.codigoSala + '\\')\" style=\"margin-left:10px;padding:6px 14px;background:#198754;color:white;border:none;border-radius:8px;cursor:pointer;font-weight:bold\">Iniciar entrevista</button></p>' +";

if (conf.indexOf('iniciarEntrevistaVideo') > -1) {
    console.log('1a. Codigo de sala ya estaba en el configurador');
} else if (conf.indexOf(anclaEntrevista) > -1) {
    conf = conf.replace(anclaEntrevista, nuevoEntrevista);
    console.log('1a. Codigo de sala + boton agregados al panel de entrevista OK');
} else {
    console.log('1a. ERROR: no se encontro el ancla del panel de entrevista');
}

// Toast con el codigo al programar
const anclaProgramar = 'if (r.ok) { toast("Entrevista programada."); admisiones(); }';
const nuevoProgramar = 'if (r.ok) { const resp = await r.json(); toast("Entrevista programada. Codigo de sala: " + resp.codigoSala + ". El paciente fue notificado."); admisiones(); }';
if (conf.indexOf('resp.codigoSala') > -1) {
    console.log('1b. Toast con codigo ya existia');
} else if (conf.indexOf(anclaProgramar) > -1) {
    conf = conf.replace(anclaProgramar, nuevoProgramar);
    console.log('1b. Toast con codigo de sala OK');
} else {
    console.log('1b. ERROR: no se encontro programarEntrevista');
}

// Funcion para iniciar la entrevista como admisionista
if (conf.indexOf('async function iniciarEntrevistaVideo') === -1) {
    conf = conf + [
'',
'async function iniciarEntrevistaVideo(codigoSala) {',
'    if (codigoSala === "" || codigoSala === "null" || codigoSala === "-") { toast("Este informe no tiene codigo de sala.", "warn"); return; }',
'    let idAdm = null;',
'    try {',
'        const profs = await (await fetch(API + "/profesionales")).json();',
'        const adm = profs.find(function(p) { return p.matricula === "ADM-001"; });',
'        if (adm !== undefined) idAdm = adm.idProfesional;',
'    } catch(e) {}',
'    if (idAdm === null) { toast("Falta crear el profesional admisionista (ADM-001).", "error"); return; }',
'    localStorage.setItem("idProfesional", idAdm);',
'    window.open("../Pantalla_Profesional/sesion_video.html?sala=" + encodeURIComponent(codigoSala), "_blank");',
'}',
''
    ].join('\n');
    console.log('1c. Funcion iniciarEntrevistaVideo agregada OK');
} else {
    console.log('1c. Funcion ya existia');
}

fs.writeFileSync(confPath, conf, 'utf8');
try {
    execSync('node --check "' + confPath + '"', { stdio: 'pipe' });
    console.log('1d. Sintaxis configurador OK');
} catch(e) {
    console.log('1d. ERROR sintaxis:', e.stderr.toString().split('\n').slice(0, 3).join('\n'));
}

// ══════════════════════════════════════════════════════════════
// 2. SESION_VIDEO del profesional: aceptar ?sala= en la URL
// ══════════════════════════════════════════════════════════════
const sesPath = 'D:/YoSoy/PANTALLAS/Pantalla_Profesional/sesion_video.html';
let ses = fs.readFileSync(sesPath, 'utf8');

const anclaSala = "sala = 'SALA-' + idProfesional + '-' + Date.now().toString(36).toUpperCase();";
const nuevoSala = "const salaURL = new URLSearchParams(window.location.search).get('sala');\n    sala = salaURL ? salaURL : ('SALA-' + idProfesional + '-' + Date.now().toString(36).toUpperCase());";

if (ses.indexOf('salaURL') > -1) {
    console.log('2. sesion_video ya aceptaba sala por URL');
} else if (ses.indexOf(anclaSala) > -1) {
    ses = ses.replace(anclaSala, nuevoSala);
    fs.writeFileSync(sesPath, ses, 'utf8');
    console.log('2. sesion_video acepta ?sala= en la URL OK');
} else {
    console.log('2. ERROR: no se encontro la generacion de sala en sesion_video.html');
}

// ══════════════════════════════════════════════════════════════
// 3. PACIENTE: banner de notificaciones no leidas
// ══════════════════════════════════════════════════════════════
const pacPath = 'D:/YoSoy/PANTALLAS/Pantalla_Paciente/js/dashboard.js';
let pac = fs.readFileSync(pacPath, 'utf8');

if (pac.indexOf('cargarNotificaciones') === -1) {
    pac = pac + [
'',
'async function cargarNotificaciones() {',
'    let notifs = [];',
'    try { notifs = await (await fetch(API + "/pacientes/" + idPaciente + "/notificaciones")).json(); } catch(e) { return; }',
'    const noLeidas = notifs.filter(function(n) { return n.leida === false; });',
'    if (noLeidas.length === 0) return;',
'',
'    let cont = document.getElementById("notif-container");',
'    if (cont === null) {',
'        cont = document.createElement("div");',
'        cont.id = "notif-container";',
'        cont.style.cssText = "position:fixed;top:70px;right:20px;z-index:9998;max-width:380px;display:flex;flex-direction:column;gap:10px";',
'        document.body.appendChild(cont);',
'    }',
'    cont.innerHTML = "";',
'',
'    noLeidas.forEach(function(n) {',
'        const card = document.createElement("div");',
'        card.style.cssText = "background:white;border-left:5px solid #0d6efd;border-radius:10px;padding:14px 16px;box-shadow:0 4px 16px rgba(0,0,0,.15)";',
'        let botones = \'<button onclick="marcarLeida(\' + n.idNotificacion + \')" style="padding:6px 12px;background:#eee;color:#555;border:none;border-radius:6px;cursor:pointer;font-size:12px">Cerrar</button>\';',
'        if (n.link !== null && n.link !== "") {',
'            botones = \'<button onclick="unirseEntrevista(\\\'\' + n.link + \'\\\', \' + n.idNotificacion + \')" style="padding:6px 14px;background:#0d6efd;color:white;border:none;border-radius:6px;cursor:pointer;font-size:12px;font-weight:bold;margin-right:8px">Unirse a la entrevista</button>\' + botones;',
'        }',
'        card.innerHTML = \'<div style="font-weight:bold;color:#0d6efd;margin-bottom:4px">\' + n.titulo + \'</div>\' +',
'            \'<div style="font-size:13px;color:#444;margin-bottom:10px">\' + n.mensaje + \'</div>\' +',
'            \'<div>\' + botones + \'</div>\';',
'        cont.appendChild(card);',
'    });',
'}',
'',
'async function marcarLeida(idNotif) {',
'    try { await fetch(API + "/notificaciones/" + idNotif + "/leida", { method: "PUT" }); } catch(e) {}',
'    cargarNotificaciones();',
'    const cont = document.getElementById("notif-container");',
'    if (cont !== null) cont.innerHTML = "";',
'    cargarNotificaciones();',
'}',
'',
'async function unirseEntrevista(codigoSala, idNotif) {',
'    try { await fetch(API + "/notificaciones/" + idNotif + "/leida", { method: "PUT" }); } catch(e) {}',
'    window.location.href = "sesion_video.html?sala=" + encodeURIComponent(codigoSala) + "&idPaciente=" + idPaciente;',
'}',
'',
'cargarNotificaciones();',
''
    ].join('\n');
    fs.writeFileSync(pacPath, pac, 'utf8');
    console.log('3. Notificaciones del paciente agregadas OK');
} else {
    console.log('3. Notificaciones ya existian');
}

try {
    execSync('node --check "' + pacPath + '"', { stdio: 'pipe' });
    console.log('3b. Sintaxis paciente OK');
} catch(e) {
    console.log('3b. ERROR sintaxis:', e.stderr.toString().split('\n').slice(0, 3).join('\n'));
}