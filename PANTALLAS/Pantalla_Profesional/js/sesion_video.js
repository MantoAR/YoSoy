// Sala de video del profesional (y del Configurador en la entrevista de admisión).
//
// Circuito de atención (octubre 2026):
// - La sala la abre el servidor (POST /salas o generar-sala del turno) y queda registrada
//   con su paciente: con solo el código, la API sabe qué consentimientos mirar.
// - Interruptores de análisis de expresiones (RN-10) y transcripción (RN-15): deshabilitados
//   sin consentimiento del paciente; el valor por defecto sale de la configuración del
//   profesional; cada cambio queda registrado y el paciente ve un indicador.
// - Evolución por enfoque (shared/evolucion.js): borrador, firma inmutable (RN-11) y modos
//   manual, dictado, transcripción o dictado + transcripción.
// Todas las llamadas usan YS.fetch / YS.api (shared/ui.js), que mandan el JWT.

const SIGNAL = (location.protocol.startsWith('http') ? `${location.protocol}//${location.hostname}` : 'http://localhost') + ':3001';
const EMOCIONES = {
    neutral:   { label: 'Neutral',   color: '#888780', emoji: '😐' },
    happy:     { label: 'Felicidad', color: '#639922', emoji: '😄' },
    sad:       { label: 'Tristeza',  color: '#378ADD', emoji: '😢' },
    angry:     { label: 'Enojo',     color: '#E24B4A', emoji: '😠' },
    fearful:   { label: 'Miedo',     color: '#7F77DD', emoji: '😨' },
    surprised: { label: 'Sorpresa',  color: '#EF9F27', emoji: '😲' },
    disgusted: { label: 'Disgusto',  color: '#D4537E', emoji: '🤢' }
};
const $ = id => document.getElementById(id);
const toast = (m, t = 'ok') => YS.toast(m, t);

// ── Estado ─────────────────────────────────────────────────────────────
let sala = (YS.param('sala') || '').trim().toUpperCase();
let idPaciente = parseInt(YS.param('idPaciente')) || null;
let idTurno = parseInt(YS.param('idTurno')) || null;
let infoSala = null;
let config = { enfoque: 'General', iaExpresionesPorDefecto: false, transcripcionPorDefecto: false, modoEvolucionPorDefecto: 'Manual' };
let motor = 'Ninguno';
let funciones = { IaExpresiones: { consentida: false, activa: false }, Transcripcion: { consentida: false, activa: false } };

let pc, stream, socket, detectionTimer, timerInterval, pollTimer, actividadTimer, sessionStart;
let enSala = false;
let muestras = 0, totales = {}, historial = [], ultimaEmo = null;
let iaCorriendo = false, iaUsada = false, faceApiPromesa = null;
let recMedico = null, recPaciente = null, recDictado = null;
let micMedicoActivo = false, micPacienteActivo = false, dictando = false;
let transcripcionLineas = [];

let evoForm = null, idEvolucion = null, evolucionFirmada = false, cambiosSinGuardar = false;
let sesionClinicaGuardada = false;

const esProfesional = YS.sesion.rol() === 'profesional';
const esAdmision = () => infoSala?.origen === 'Admision';

// ── Arranque ───────────────────────────────────────────────────────────
(async function iniciar() {
    $('btn-copiar').addEventListener('click', () => YS.copiar(sala, 'Código copiado'));
    $('btn-mic-medico').addEventListener('click', () => toggleMic('medico'));
    $('btn-mic-paciente').addEventListener('click', () => toggleMic('paciente'));
    $('btn-limpiar').addEventListener('click', limpiarTranscripcion);
    $('btn-dictar').addEventListener('click', toggleDictado);
    $('btn-volcar').addEventListener('click', volcarTranscripcion);
    $('btn-finalizar').addEventListener('click', finalizarSesion);
    $('btn-borrador').addEventListener('click', () => guardarEvolucion(false));
    $('btn-firmar').addEventListener('click', () => guardarEvolucion(true));
    $('btn-exportar').addEventListener('click', exportarResumen);
    $('sw-ia').addEventListener('change', e => cambiarFuncion('IaExpresiones', e.target.checked));
    $('sw-tr').addEventListener('change', e => cambiarFuncion('Transcripcion', e.target.checked));
    window.addEventListener('beforeunload', e => {
        if (evoForm && cambiosSinGuardar && !evolucionFirmada) { e.preventDefault(); e.returnValue = ''; }
    });

    try { await YS.evolucion.cargar(); }
    catch { $('evolucion-form').replaceChildren(YS.el('p', { class: 'ui-alerta ui-alerta--danger', text: 'No se pudieron cargar las plantillas de evolución.' })); }

    if (esProfesional) {
        try { config = await YS.api(`/profesionales/${YS.sesion.idProfesional()}/configuracion-sesion`); } catch { /* valores por defecto */ }
    }

    if (sala) {
        $('campo-paciente').classList.add('oculto');
        $('ayuda-conexion').textContent = 'Revisá los datos de la sala y entrá cuando estés listo.';
        if (await resolverSala()) prepararEntrada();
    } else {
        await cargarPacientes();
        $('btn-crear').onclick = crearSala;
    }
})();

async function cargarPacientes() {
    const sel = $('sel-paciente');
    try {
        const lista = await YS.api(`/profesionales/${YS.sesion.idProfesional()}/pacientes`);
        if (!lista.length) { sel.innerHTML = '<option value="">Sin pacientes asignados</option>'; return; }
        sel.replaceChildren(YS.el('option', { value: '', text: '-- Seleccioná el paciente --' }),
            ...lista.map(p => YS.el('option', { value: p.idPaciente, text: `${p.nombre} ${p.apellido ?? ''}`.trim() })));
        if (idPaciente) sel.value = String(idPaciente);
    } catch {
        sel.innerHTML = '<option value="">Error al cargar pacientes</option>';
    }
}

// Sesión directa: el servidor genera y registra el código, y le avisa al paciente.
async function crearSala() {
    const id = parseInt($('sel-paciente').value);
    if (!id) { toast('Elegí el paciente de la sesión.', 'warn'); return; }
    $('btn-crear').disabled = true;
    try {
        const r = await YS.fetch('/salas', { method: 'POST', json: { idPaciente: id } });
        if (!r.ok) { toast(await YS.leerMensaje(r, 'No se pudo abrir la sala.'), 'error'); return; }
        sala = (await r.json()).codigoSala;
        idPaciente = id;
        $('sel-paciente').disabled = true;
        if (await resolverSala()) prepararEntrada();
    } finally {
        $('btn-crear').disabled = false;
    }
}

async function resolverSala() {
    try {
        infoSala = await YS.api(`/salas/${encodeURIComponent(sala)}`);
    } catch (e) {
        $('estado-conexion').textContent = e.status === 404
            ? 'No encontramos esa sala, o no está asociada a un paciente tuyo.'
            : 'No se pudo consultar la sala: ' + e.message;
        $('btn-crear').disabled = true;
        return false;
    }
    idPaciente = infoSala.idPaciente;
    idTurno = infoSala.idTurno ?? idTurno;
    motor = infoSala.motorTranscripcion || 'Ninguno';
    aplicarFunciones(infoSala.funciones);

    const chip = (ok, texto) => YS.el('span', { class: 'ui-chip ' + (ok ? 'ui-chip--ok' : 'ui-chip--neutral'), text: texto });
    $('resumen-sala').replaceChildren(
        YS.el('div', {}, YS.el('strong', { text: infoSala.nombrePaciente || 'Paciente' }),
            ' · ' + (infoSala.origen === 'Admision' ? 'Entrevista de admisión' : infoSala.origen === 'Turno' ? 'Turno' : 'Sesión directa')),
        YS.el('div', { class: 'chips' },
            chip(funciones.IaExpresiones.consentida, funciones.IaExpresiones.consentida ? 'Aceptó el análisis de expresiones' : 'Sin consentimiento para IA'),
            chip(funciones.Transcripcion.consentida, funciones.Transcripcion.consentida ? 'Aceptó la transcripción' : 'Sin consentimiento para transcripción')));
    $('resumen-sala').classList.remove('oculto');
    $('sala-codigo-display').textContent = sala;
    $('sala-box').classList.remove('oculto');
    return true;
}

function prepararEntrada() {
    $('btn-crear').textContent = '▶ Entrar a la sala';
    $('btn-crear').onclick = entrarASala;
    $('btn-crear').disabled = false;
    $('estado-conexion').textContent = '';
}

// ── Entrar ─────────────────────────────────────────────────────────────
async function entrarASala() {
    try {
        stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: true });
    } catch {
        $('estado-conexion').textContent = 'No se pudo acceder a la cámara o al micrófono.';
        return;
    }
    $('pantalla-conexion').classList.add('oculto');
    $('pantalla-sesion').classList.remove('oculto');
    $('estado-txt').textContent = 'Esperando al paciente...';
    $('vid-propio').srcObject = stream;
    enSala = true;
    aplicarFunciones([]);   // habilita los interruptores ahora que la sala está abierta

    montarEvolucion();

    socket = io(SIGNAL);
    socket.emit('join-room', { roomId: sala, role: 'profesional' });
    socket.on('funciones-cambio', refrescarFunciones);
    socket.on('pedido-apagado', ({ funcion }) => {
        toast(`El paciente pidió apagar ${funcion === 'IaExpresiones' ? 'el análisis de expresiones' : 'la transcripción'}.`, 'warn');
        refrescarFunciones();
    });

    pc = new RTCPeerConnection({ iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] });
    stream.getTracks().forEach(t => pc.addTrack(t, stream));
    pc.ontrack = e => {
        $('vid-paciente').srcObject = e.streams[0];
        $('estado-txt').textContent = '🟢 Sesión activa';
        if (!sessionStart) { sessionStart = Date.now(); iniciarTimer(); }
        if (funciones.IaExpresiones.activa && !iaCorriendo) iniciarIA();
    };
    pc.onicecandidate = e => { if (e.candidate) socket.emit('ice-candidate', { roomId: sala, candidate: e.candidate }); };
    socket.on('offer', async ({ offer }) => {
        await pc.setRemoteDescription(offer);
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        socket.emit('answer', { roomId: sala, answer });
    });
    socket.on('ice-candidate', async ({ candidate }) => { try { await pc.addIceCandidate(candidate); } catch { } });
    socket.on('user-left', () => { $('estado-txt').textContent = '⚠ El paciente se desconectó.'; });

    // Valores por defecto del profesional: solo si el paciente dio el consentimiento.
    if (esProfesional) {
        if (config.iaExpresionesPorDefecto && funciones.IaExpresiones.consentida && !funciones.IaExpresiones.activa)
            await cambiarFuncion('IaExpresiones', true, { silencioso: true });
        if (config.transcripcionPorDefecto && funciones.Transcripcion.consentida && !funciones.Transcripcion.activa)
            await cambiarFuncion('Transcripcion', true, { silencioso: true });
    }

    // El paciente puede revocar o pedir que se apague durante la sesión: se consulta seguido.
    pollTimer = setInterval(refrescarFunciones, 10000);
    // Con la sala abierta adentro del panel, la actividad acá no la ve el panel: se le avisa
    // para que no cierre la sesión por inactividad en medio de la consulta (js/sesion.js).
    actividadTimer = setInterval(() => { try { parent.postMessage('ys-actividad', location.origin); } catch { } }, 60000);
}

// ── Interruptores ──────────────────────────────────────────────────────
async function refrescarFunciones() {
    try {
        const info = await YS.api(`/salas/${encodeURIComponent(sala)}`);
        aplicarFunciones(info.funciones);
    } catch { /* se reintenta en el próximo ciclo */ }
}

async function cambiarFuncion(funcion, activa, { silencioso = false } = {}) {
    const r = await YS.fetch(`/salas/${encodeURIComponent(sala)}/funciones`, { method: 'PUT', json: { funcion, activa } });
    if (!r.ok) {
        if (!silencioso) toast(await YS.leerMensaje(r, 'No se pudo cambiar la función.'), 'error');
        await refrescarFunciones();
        return;
    }
    aplicarFunciones((await r.json()).funciones);
    socket?.emit('funciones-cambio', { roomId: sala });
    if (!silencioso) toast(`${funcion === 'IaExpresiones' ? 'Análisis de expresiones' : 'Transcripción'} ${activa ? 'encendida' : 'apagada'}. El paciente lo ve en su pantalla.`, activa ? 'info' : 'ok');
}

function aplicarFunciones(lista) {
    for (const f of lista || []) funciones[f.funcion] = { consentida: f.consentida, activa: f.activa };
    const ia = funciones.IaExpresiones, tr = funciones.Transcripcion;

    $('sw-ia').disabled = !ia.consentida || !enSala;
    $('sw-ia').checked = ia.activa;
    $('ayuda-ia').textContent = !ia.consentida ? 'El paciente no dio su consentimiento: no se puede encender.'
        : ia.activa ? 'Encendido. El paciente ve el aviso.' : 'Apagado. El paciente lo aceptó.';
    $('sw-tr').disabled = !tr.consentida || !enSala || motor === 'Ninguno';
    $('sw-tr').checked = tr.activa;
    $('ayuda-tr').textContent = motor === 'Ninguno' ? 'La transcripción no está habilitada en esta organización.'
        : !tr.consentida ? 'El paciente no dio su consentimiento: no se puede encender.'
        : tr.activa ? 'Encendida. El paciente ve el aviso.' : 'Apagada. El paciente la aceptó.';
    $('aviso-motor').classList.toggle('oculto', motor !== 'NavegadorDemo');

    if (ia.activa && !iaCorriendo && enSala) iniciarIA();
    if (!ia.activa && iaCorriendo) detenerIA();

    const puedeMic = tr.activa && motor === 'NavegadorDemo';
    $('btn-mic-medico').disabled = !puedeMic;
    $('btn-mic-paciente').disabled = !puedeMic;
    if (!puedeMic) detenerMics();
    $('transcripcion-vacia')?.replaceChildren(document.createTextNode(puedeMic
        ? 'Encendé un micrófono para empezar a transcribir.' : 'La transcripción está apagada.'));
    actualizarHerramientas();
}

// ── IA de expresiones (face-api se carga recién cuando se enciende) ────
function cargarFaceApi() {
    if (faceApiPromesa) return faceApiPromesa;
    faceApiPromesa = new Promise((ok, mal) => {
        const s = document.createElement('script');
        s.src = 'face-api.min.js';
        s.onload = ok; s.onerror = mal;
        document.head.append(s);
    }).then(() => Promise.all([
        faceapi.nets.tinyFaceDetector.loadFromUri('../models'),
        faceapi.nets.faceLandmark68TinyNet.loadFromUri('../models'),
        faceapi.nets.faceExpressionNet.loadFromUri('../models')
    ]));
    faceApiPromesa.catch(() => { faceApiPromesa = null; });
    return faceApiPromesa;
}

async function iniciarIA() {
    iaCorriendo = true;
    $('ia-apagada').textContent = 'Cargando el modelo de análisis…';
    try { await cargarFaceApi(); }
    catch {
        iaCorriendo = false;
        $('ia-apagada').textContent = 'No se pudo cargar el modelo de análisis.';
        return;
    }
    if (!funciones.IaExpresiones.activa) { iaCorriendo = false; return; }   // se apagó mientras cargaba
    iaUsada = true;
    $('ia-apagada').classList.add('oculto');
    $('emo-bars').classList.remove('oculto');
    $('emo-live').classList.remove('oculto');
    iniciarDeteccion($('vid-paciente'));
}

function detenerIA() {
    iaCorriendo = false;
    clearTimeout(detectionTimer);
    const canvas = $('overlay');
    canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height);
    $('emo-bars').classList.add('oculto');
    $('emo-live').classList.add('oculto');
    $('ia-apagada').textContent = 'El análisis de expresiones está apagado.';
    $('ia-apagada').classList.remove('oculto');
}

function iniciarDeteccion(video) {
    const canvas = $('overlay');
    async function detectar() {
        if (!iaCorriendo) return;
        if (!video || video.readyState < 2) { detectionTimer = setTimeout(detectar, 500); return; }
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        const opts = new faceapi.TinyFaceDetectorOptions({ inputSize: 224, scoreThreshold: 0.5 });
        const result = await faceapi.detectSingleFace(video, opts).withFaceLandmarks(true).withFaceExpressions();
        if (!iaCorriendo) return;
        const ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        if (result) {
            const { box } = result.detection;
            ctx.strokeStyle = '#185FA5';
            ctx.lineWidth = 2;
            ctx.strokeRect(box.x, box.y, box.width, box.height);
            const exps = result.expressions;
            muestras++;
            for (const [k, v] of Object.entries(exps)) totales[k] = (totales[k] || 0) + v;
            for (const k of Object.keys(EMOCIONES)) {
                const pct = Math.round((exps[k] || 0) * 100);
                $(`b-${k}`).style.width = pct + '%';
                $(`v-${k}`).textContent = pct + '%';
            }
            $('stat-muestras').textContent = muestras;
            const dom = Object.entries(exps).sort((a, b) => b[1] - a[1])[0];
            $('emo-live').textContent = `${EMOCIONES[dom[0]].emoji} ${EMOCIONES[dom[0]].label}`;
            let dk = 'neutral', dv = 0;
            for (const [k, v] of Object.entries(totales)) { if (v > dv) { dk = k; dv = v; } }
            $('stat-dom').textContent = `${EMOCIONES[dk].emoji} ${EMOCIONES[dk].label}`;
            if (dom[0] !== ultimaEmo) {
                ultimaEmo = dom[0];
                const t = transcurrido();
                historial.push({ tiempo: t, emocion: dom[0], label: EMOCIONES[dom[0]].label, intensidad: Math.round(dom[1] * 100) });
                const row = YS.el('div', { class: 'tl-row' },
                    YS.el('span', { class: 'tl-dot', style: `background:${EMOCIONES[dom[0]].color}` }),
                    YS.el('span', { class: 'tl-time', text: t }),
                    YS.el('span', { text: `${EMOCIONES[dom[0]].label} (${Math.round(dom[1] * 100)}%)` }));
                $('tl').append(row);
                $('tl').scrollTop = $('tl').scrollHeight;
            }
        }
        detectionTimer = setTimeout(detectar, 500);
    }
    clearTimeout(detectionTimer);
    detectionTimer = setTimeout(detectar, 500);
}

function transcurrido() {
    const ms = sessionStart ? Date.now() - sessionStart : 0;
    return `${String(Math.floor(ms / 60000)).padStart(2, '0')}:${String(Math.floor((ms % 60000) / 1000)).padStart(2, '0')}`;
}

function iniciarTimer() {
    timerInterval = setInterval(() => { $('stat-dur').textContent = transcurrido(); }, 1000);
}

// ── Transcripción y dictado (motor de demo del navegador) ──────────────
function crearReconocedor(alResultado, alTerminar) {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) { toast('Este navegador no tiene reconocimiento de voz. Usá Chrome o Edge.', 'warn'); return null; }
    const rec = new SR();
    rec.lang = 'es-AR';
    rec.continuous = true;
    rec.interimResults = false;
    rec.onresult = e => {
        for (let i = e.resultIndex; i < e.results.length; i++)
            if (e.results[i].isFinal) { const t = e.results[i][0].transcript.trim(); if (t) alResultado(t); }
    };
    rec.onerror = e => { if (e.error !== 'no-speech') toast(`Error del micrófono: ${e.error}`, 'error'); };
    rec.onend = alTerminar;
    return rec;
}

function toggleMic(quien) {
    const activo = quien === 'medico' ? micMedicoActivo : micPacienteActivo;
    const btn = $(quien === 'medico' ? 'btn-mic-medico' : 'btn-mic-paciente');
    if (!activo) {
        if (!funciones.Transcripcion.activa) return;
        const rec = crearReconocedor(t => agregarLinea(quien, t), () => {
            // El navegador corta solo cada tanto: se reinicia mientras siga activa.
            const sigue = quien === 'medico' ? micMedicoActivo : micPacienteActivo;
            if (sigue && funciones.Transcripcion.activa) try { rec.start(); } catch { }
        });
        if (!rec) return;
        rec.start();
        if (quien === 'medico') { recMedico = rec; micMedicoActivo = true; } else { recPaciente = rec; micPacienteActivo = true; }
        btn.classList.add('activo');
        btn.textContent = quien === 'medico' ? '⏹ Detener mi mic' : '⏹ Detener mic paciente';
    } else {
        if (quien === 'medico') { micMedicoActivo = false; recMedico?.stop(); } else { micPacienteActivo = false; recPaciente?.stop(); }
        btn.classList.remove('activo');
        btn.textContent = quien === 'medico' ? '🎤 Mi micrófono' : '🎤 Micrófono paciente';
    }
}

function detenerMics() {
    if (micMedicoActivo) toggleMic('medico');
    if (micPacienteActivo) toggleMic('paciente');
}

function agregarLinea(quien, texto) {
    $('transcripcion-vacia')?.remove();
    const hora = new Date().toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit', hour12: false });
    transcripcionLineas.push({ quien, hora, texto });
    $('transcripcion-box').append(YS.el('div', { class: quien === 'medico' ? 'msg-medico' : 'msg-paciente' },
        YS.el('span', { class: 'msg-label', text: quien === 'medico' ? 'Profesional' : 'Paciente' }), `[${hora}] ${texto}`));
    $('transcripcion-box').scrollTop = $('transcripcion-box').scrollHeight;
    actualizarHerramientas();
}

function limpiarTranscripcion() {
    transcripcionLineas = [];
    $('transcripcion-box').replaceChildren(YS.el('div', { class: 'transcripcion-vacia', id: 'transcripcion-vacia',
        text: funciones.Transcripcion.activa ? 'Encendé un micrófono para empezar a transcribir.' : 'La transcripción está apagada.' }));
    actualizarHerramientas();
}

function textoTranscripcion() {
    return transcripcionLineas.map(l => `${l.quien === 'medico' ? 'Profesional' : 'Paciente'}: ${l.texto}`).join('\n');
}

// Dictado: el profesional dicta su propia nota en el campo que tenga seleccionado. No
// necesita consentimiento del paciente (es la voz del profesional), pero usa el mismo motor.
function toggleDictado() {
    if (!evoForm) return;
    if (!dictando) {
        recDictado = crearReconocedor(t => { evoForm.insertarEnCampoActivo(t); cambiosSinGuardar = true; },
            () => { if (dictando) try { recDictado.start(); } catch { } });
        if (!recDictado) return;
        recDictado.start();
        dictando = true;
        $('btn-dictar').textContent = '⏹ Dejar de dictar';
        $('btn-dictar').classList.add('ui-btn--primary');
    } else {
        dictando = false;
        recDictado?.stop();
        $('btn-dictar').textContent = '🎙 Dictar en el campo seleccionado';
        $('btn-dictar').classList.remove('ui-btn--primary');
    }
}

function volcarTranscripcion() {
    if (evoForm?.insertarTranscripcion(textoTranscripcion())) {
        cambiosSinGuardar = true;
        toast('Se pasó la transcripción al borrador. Revisala y resumila antes de firmar.', 'info');
    }
}

function actualizarHerramientas() {
    const modo = evoForm?.modo() || 'Manual';
    const usaDictado = modo === 'Dictado' || modo === 'DictadoTranscripcion';
    const usaTransc = modo === 'Transcripcion' || modo === 'DictadoTranscripcion';
    $('btn-dictar').classList.toggle('oculto', !usaDictado);
    $('btn-dictar').disabled = !usaDictado || motor !== 'NavegadorDemo' || evolucionFirmada;
    if (!usaDictado && dictando) toggleDictado();
    $('btn-volcar').classList.toggle('oculto', !usaTransc);
    $('btn-volcar').disabled = !usaTransc || transcripcionLineas.length === 0 || evolucionFirmada;
}

// ── Evolución ──────────────────────────────────────────────────────────
function montarEvolucion() {
    if (esAdmision() || !esProfesional) {
        $('evolucion-admision').classList.remove('oculto');
        $('evolucion-form').classList.add('oculto');
        $('evo-herramientas').classList.add('oculto');
        $('btn-borrador').classList.add('oculto');
        $('btn-firmar').classList.add('oculto');
        return;
    }
    if (!YS.evolucion.plantilla('General')) return;   // no cargaron las plantillas
    const cont = $('evolucion-form');
    evoForm = YS.evolucion.formulario(cont, {
        enfoque: config.enfoque,
        modo: config.modoEvolucionPorDefecto,
        transcripcionDisponible: motor !== 'Ninguno' && funciones.Transcripcion.consentida,
        dictadoDisponible: motor === 'NavegadorDemo'
    });
    cont.addEventListener('input', () => { cambiosSinGuardar = true; });
    cont.addEventListener('evo:modo', actualizarHerramientas);
    actualizarHerramientas();
}

async function guardarEvolucion(firmar) {
    if (!evoForm || evolucionFirmada) return;
    if (firmar && !await YS.confirmar({
        titulo: 'Firmar evolución',
        mensaje: 'Una vez firmada no se puede modificar: las correcciones se agregan como addendum. ¿Firmar ahora?',
        textoConfirmar: 'Firmar'
    })) return;

    const estado = $('evolucion-estado');
    const cuerpo = { ...evoForm.leer(), idPaciente, idTurno, codigoSala: sala, firmar };
    const botones = [$('btn-borrador'), $('btn-firmar')];
    botones.forEach(b => b.disabled = true);
    try {
        const r = idEvolucion
            ? await YS.fetch(`/evoluciones/${idEvolucion}/borrador`, { method: 'PUT', json: cuerpo })
            : await YS.fetch('/evoluciones/por-enfoque', { method: 'POST', json: cuerpo });
        if (!r.ok) {
            estado.textContent = await YS.leerMensaje(r, 'No se pudo guardar la evolución.');
            estado.className = 'evolucion-estado ui-alerta ui-alerta--danger';
            return;
        }
        const data = await r.json();
        idEvolucion = data.idEvolucion;
        cambiosSinGuardar = false;
        const hora = new Date().toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit', hour12: false });
        if (data.estado === 'Firmada') {
            evolucionFirmada = true;
            $('evolucion-form').classList.add('bloqueada');
            estado.textContent = `Evolución firmada a las ${hora}. Para corregirla, agregá un addendum desde la historia clínica.`;
            estado.className = 'evolucion-estado evolucion-estado--firmada';
            if ((data.alertas || []).includes('PHQ9'))
                toast('PHQ-9 con ítem 9 positivo: evaluá el riesgo y, si corresponde, activá el protocolo de crisis.', 'warn');
            if (dictando) toggleDictado();
            actualizarHerramientas();
            await guardarSesionClinica(cuerpo.tipoCierre);
        } else {
            estado.textContent = `Borrador guardado a las ${hora}.`;
            estado.className = 'evolucion-estado';
        }
    } catch {
        estado.textContent = 'No se pudo conectar con el servidor. Tu texto sigue acá: probá de nuevo.';
        estado.className = 'evolucion-estado ui-alerta ui-alerta--danger';
    } finally {
        botones.forEach(b => b.disabled = evolucionFirmada);
    }
}

// Registro de la sesión (para el reporte de atenciones y el resumen de emociones). El
// servidor descarta las emociones o la transcripción si no hay consentimiento vigente.
async function guardarSesionClinica(tipoCierre) {
    if (sesionClinicaGuardada || !esProfesional || esAdmision()) return;
    const n = muestras || 1;
    const promedio = {};
    for (const [k, v] of Object.entries(totales)) promedio[EMOCIONES[k]?.label || k] = Math.round(v / n * 100);
    const r = await YS.fetch('/sesiones-clinicas', {
        method: 'POST',
        json: {
            idPaciente, idTurno, idProfesional: YS.sesion.idProfesional(),
            transcripcion: transcripcionLineas.length ? textoTranscripcion() : null,
            notasEvolucion: null,
            historialEmociones: iaUsada && muestras ? JSON.stringify(promedio) : null,
            tipoCierre
        }
    }).catch(() => null);
    sesionClinicaGuardada = !!r?.ok;
    if (!sesionClinicaGuardada) toast('La evolución quedó firmada, pero no se pudo registrar la sesión para el reporte.', 'warn');
}

// ── Finalizar ──────────────────────────────────────────────────────────
async function finalizarSesion() {
    clearTimeout(detectionTimer);
    clearInterval(timerInterval);
    clearInterval(pollTimer);
    clearInterval(actividadTimer);
    detenerMics();
    if (dictando) toggleDictado();
    // Las funciones encendidas se apagan y queda registrado.
    for (const f of ['IaExpresiones', 'Transcripcion'])
        if (funciones[f].activa) await cambiarFuncion(f, false, { silencioso: true });
    iaCorriendo = false;
    enSala = false;
    stream?.getTracks().forEach(t => t.stop());
    pc?.close();
    socket?.disconnect();
    aplicarFunciones([]);
    $('btn-finalizar').disabled = true;
    $('btn-exportar').disabled = false;
    $('estado-txt').textContent = evoForm && !evolucionFirmada
        ? 'Videollamada finalizada. Completá y firmá la evolución.'
        : 'Videollamada finalizada.';
}

// Resumen descargable: duración y emociones. No incluye la transcripción ni la evolución
// (esas quedan en el sistema, con sus controles de acceso).
function exportarResumen() {
    const n = muestras || 1;
    const resumen = {
        fecha: new Date().toLocaleDateString('es-AR'),
        sala,
        duracion: transcurrido(),
        analisisDeExpresiones: iaUsada ? {
            muestras,
            promedio: Object.fromEntries(Object.entries(totales).map(([k, v]) => [EMOCIONES[k]?.label || k, Math.round(v / n * 100) + '%'])),
            cambios: historial
        } : 'No se usó'
    };
    const a = YS.el('a', {
        href: URL.createObjectURL(new Blob([JSON.stringify(resumen, null, 2)], { type: 'application/json' })),
        download: `sesion_${new Date().toISOString().slice(0, 10)}.json`
    });
    a.click();
}
