// Consentimiento informado para atención por telemedicina (RN-02).
// Usa la misma sesión que el panel del paciente (localStorage.idPaciente).
const API = location.protocol.startsWith("http") ? location.origin : "http://localhost:5000";
const idPaciente = localStorage.getItem("idPaciente");
const $ = id => document.getElementById(id);

let versionTexto = null;

function mostrar(id) {
    ["cargando", "sinConexion", "sinSesion", "yaFirmado", "paraFirmar"].forEach(s => $(s).classList.toggle("oculto", s !== id));
}

function error(msg) {
    $("error").textContent = msg;
    $("error").classList.toggle("oculto", !msg);
}

// "sin-validar" es el placeholder de Consentimiento:VersionTexto (appsettings.json) hasta que
// se cargue el texto legal definitivo: no es una versión real, así que no se la muestra.
function versionEsReal(v) {
    return !!v && v !== "sin-validar";
}

function fechaHora(valor) {
    const d = new Date(valor);
    const fecha = d.toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" });
    const hora  = d.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });
    return `${fecha} a las ${hora}`;
}

// El texto legal todavía no está validado por el equipo legal: la marca viene de la API (Consentimiento:MarcaProvisoria) y se quita dejándola vacía.
function mostrarMarca(texto) {
    ["marcaProvisoria", "marcaProvisoriaFirmado"].forEach(id => { $(id).textContent = texto || ""; $(id).classList.toggle("oculto", !texto); });
}

// recienFirmado: la persona acaba de firmar en esta pantalla. Se le confirma con un título propio y lo que le habilita, y
// el foco va al botón para volver (sin redirección automática: la confirmación tiene que poder leerse).
function mostrarFirmado(fechaFirma, version, recienFirmado = false, venceEl = null) {
    $("yaFirmado").querySelector(".firmado-icono").innerHTML = YS.icono("check");
    $("tituloFirmado").textContent = recienFirmado ? "Consentimiento firmado" : "Ya firmaste este consentimiento";
    $("detalleFirma").textContent = `Lo firmaste el ${fechaHora(fechaFirma)} hs` + (versionEsReal(version) ? ` (versión del texto: ${version})` : "") +
        (venceEl ? `. Vale hasta el ${new Date(venceEl).toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" })}.` : ".");
    $("ayudaFirmado").textContent = recienFirmado
        ? "Listo: ya podés confirmar tus turnos. Guardamos la fecha y la hora de tu firma."
        : "No hace falta volver a firmarlo. Si ya no querés darlo, podés revocarlo.";
    mostrar("yaFirmado");
    if (recienFirmado) {
        window.scrollTo({ top: 0, behavior: "smooth" });
        $("btnVolver").focus({ preventScroll: true });
    }
}

async function cargar() {
    if (!idPaciente) { mostrar("sinSesion"); return; }
    mostrar("cargando");
    error("");
    try {
        const [rPac, rEstado] = await Promise.all([
            YS.fetch(`/pacientes/${idPaciente}`),
            YS.fetch(`/pacientes/${idPaciente}/consentimiento-telemedicina`)
        ]);
        // 401/404: la sesión no corresponde a un paciente; otro error: el servidor falló
        const sinSesion = r => r.status === 401 || r.status === 404;
        if (sinSesion(rPac) || sinSesion(rEstado)) { mostrar("sinSesion"); return; }
        if (!rPac.ok || !rEstado.ok) { mostrar("sinConexion"); return; }
        const p = await rPac.json();
        const estado = await rEstado.json();
        $("paciente").textContent = `Paciente: ${p.nombre} · DNI ${p.dni}`;

        mostrarMarca(estado.marcaProvisoria);
        if (estado.firmado) { mostrarFirmado(estado.fechaFirma, estado.versionFirmada, false, estado.venceEl); return; }
        versionTexto = estado.versionTexto;
        // Menor de edad: lo firma un tutor. Si el texto cambió de versión, hay que volver a firmarlo.
        const tutores = estado.esMenor ? (estado.tutores || []) : [];
        $("bloqueTutor").classList.toggle("oculto", tutores.length === 0);
        $("selTutor").innerHTML = tutores.map(t => `<option value="${t.idTutor}">${YS.esc(t.nombre)}${t.vinculo ? " (" + YS.esc(t.vinculo) + ")" : ""}</option>`).join("");
        if (estado.motivo === "vencido") error("Tu consentimiento venció (pasó más de un año desde que lo firmaste). Leelo y firmalo de nuevo para seguir.");
        else if (estado.motivo === "version-anterior") error("El texto del consentimiento cambió desde la última vez que lo firmaste. Leelo y volvé a firmarlo para seguir.");
        else if (estado.esMenor && tutores.length === 0) error("Tu ficha no tiene cargado a ningún tutor. Pedile a Admisión que lo cargue para poder firmar.");
        $("versionTexto").textContent = versionTexto;
        $("lineaVersion").classList.toggle("oculto", !versionEsReal(versionTexto));
        mostrar("paraFirmar");
    } catch {
        mostrar("sinConexion");
    }
}

async function firmar() {
    error("");
    const btn = $("btnFirmar");
    btn.disabled = true;
    try {
        const r = await YS.fetch(`/pacientes/${idPaciente}/consentimiento-telemedicina`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ acepto: true, version: versionTexto, idTutor: $("bloqueTutor").classList.contains("oculto") ? null : Number($("selTutor").value) })
        });
        const data = await r.json().catch(() => ({}));
        if (r.status === 201) { mostrarFirmado(data.fechaFirma, data.versionFirmada, true); return; }
        if (r.status === 409 && data.fechaFirma) { mostrarFirmado(data.fechaFirma); return; }   // ya estaba firmado
        if (r.status === 409) { error(data.mensaje); await cargar(); return; }                // cambió el texto
        error(data.mensaje || "No se pudo registrar la firma. Probá de nuevo.");
    } catch {
        error("No pudimos conectarnos con el servidor. Probá de nuevo en unos minutos.");
    } finally {
        btn.disabled = false;
    }
}

// ─── Mis sesiones: revocar la videollamada o las emociones desde el perfil ───
// La API es la autoridad: acá solo se la pide y se la pinta. Revocar el video corta también las emociones y avisa al profesional (igual que desde la sala).
async function cargarSesiones() {
    const seccion = $("misSesiones");
    let lista = [];
    try { lista = await YS.api("/video/aceptaciones-vigentes", { sinRedireccion: true }); } catch { lista = []; }
    seccion.classList.toggle("oculto", !Array.isArray(lista) || lista.length === 0);
    $("errorSesiones").classList.add("oculto");
    const ul = $("listaSesiones");
    ul.replaceChildren(...(Array.isArray(lista) ? lista : []).map(s => {
        const li = document.createElement("li");
        li.className = "sesion-aceptada";
        const nombre = s.origen === "admision" ? "Entrevista de admisión" : "Sesión";
        const t = document.createElement("span");
        t.textContent = `${nombre} #${s.id} · aceptaste la videollamada el ${fechaHora(s.desdeUtc)} · análisis de emociones: ${s.emocionesAceptadas ? "aceptado" : "no aceptado"}`;
        li.appendChild(t);
        const botones = document.createElement("span");
        botones.className = "acciones";
        const sesion = { origen: s.origen, id: s.id };
        const video = document.createElement("button");
        video.type = "button"; video.className = "ui-btn ui-btn--danger ui-btn--sm"; video.textContent = "Dejar de usar el video";
        video.addEventListener("click", () => revocarSesion(() => YS.emociones.revocarVideo(sesion), "Dejar de usar el video", "Se va a cortar el video de esa sesión y tu profesional va a recibir el aviso."));
        botones.appendChild(video);
        if (s.emocionesAceptadas) {
            const emo = document.createElement("button");
            emo.type = "button"; emo.className = "ui-btn ui-btn--sm"; emo.textContent = "Dejar de compartir mis expresiones";
            emo.addEventListener("click", () => revocarSesion(() => YS.emociones.revocar(sesion), "Dejar de compartir mis expresiones", "Se deja de analizar y se elimina lo que se hubiera guardado de esa sesión."));
            botones.appendChild(emo);
        }
        li.appendChild(botones);
        return li;
    }));
}

async function revocarSesion(operacion, titulo, mensaje) {
    if (!await YS.confirmar({ titulo, mensaje, textoConfirmar: titulo, peligro: true })) return;
    try { await operacion(); } catch (err) { const e = $("errorSesiones"); e.textContent = err.message; e.classList.remove("oculto"); return; }
    await cargarSesiones();
}

// Revocar con constancia: la fila no se borra (queda quién firmó, cuándo y cuándo revocó). Mientras esté revocado no se asigna profesional ni se confirman turnos.
async function revocar() {
    const ok = await YS.confirmar({
        titulo: "Revocar mi consentimiento",
        mensaje: "Si lo revocás, no vamos a poder asignarte un profesional ni confirmar tus turnos hasta que vuelvas a firmarlo. Tus turnos ya confirmados no se cancelan solos: se le avisa a tu profesional. ¿Querés revocarlo?",
        textoConfirmar: "Sí, revocar", textoCancelar: "No, mantenerlo", peligro: true
    });
    if (!ok) return;
    error("");
    const btn = $("btnRevocar");
    btn.disabled = true;
    try {
        const r = await YS.fetch(`/pacientes/${idPaciente}/consentimiento-telemedicina/revocar`, {
            method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ confirmo: true })
        });
        const data = await r.json().catch(() => ({}));
        if (!r.ok) { error(data.mensaje || "No se pudo revocar el consentimiento. Probá de nuevo."); return; }
        YS.toast(data.mensaje || "Revocaste tu consentimiento.", "info", 6000);
        await cargar();
    } catch {
        error("No pudimos conectarnos con el servidor. Probá de nuevo en unos minutos.");
    } finally {
        btn.disabled = false;
    }
}

$("btnRevocar").addEventListener("click", revocar);
$("btnFirmar").addEventListener("click", firmar);
$("btnReintentar").addEventListener("click", cargar);
cargar();
cargarSesiones();
