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

function mostrarFirmado(fechaFirma, version) {
    $("yaFirmado").querySelector(".firmado-icono").innerHTML = YS.icono("check");
    $("detalleFirma").textContent = `Lo firmaste el ${fechaHora(fechaFirma)}` + (versionEsReal(version) ? ` (versión del texto: ${version}).` : ".");
    mostrar("yaFirmado");
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
        cargarOpcionales();

        if (estado.firmado) { mostrarFirmado(estado.fechaFirma, estado.versionFirmada); return; }
        versionTexto = estado.versionTexto;
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
            body: JSON.stringify({ acepto: true, version: versionTexto })
        });
        const data = await r.json().catch(() => ({}));
        if (r.status === 201) { mostrarFirmado(data.fechaFirma, data.versionFirmada); return; }
        if (r.status === 409 && data.fechaFirma) { mostrarFirmado(data.fechaFirma); return; }   // ya estaba firmado
        if (r.status === 409) { error(data.mensaje); await cargar(); return; }                // cambió el texto
        error(data.mensaje || "No se pudo registrar la firma. Probá de nuevo.");
    } catch {
        error("No pudimos conectarnos con el servidor. Probá de nuevo en unos minutos.");
    } finally {
        btn.disabled = false;
    }
}

// ── Consentimientos opcionales (RN-10 IA de expresiones, RN-15 transcripción, WhatsApp) ──
// Se firman y se revocan de a uno. El servidor nunca deja encender una función sin el
// consentimiento vigente, y si se revoca durante una sesión la función se apaga sola.
async function cargarOpcionales() {
    let lista;
    try {
        lista = await YS.api(`/pacientes/${idPaciente}/consentimientos-opcionales`);
    } catch {
        return;   // la sección queda oculta: el consentimiento principal no depende de esto
    }
    $("opcionales").classList.remove("oculto");
    $("opcionalesBorrador").classList.toggle("oculto", lista.some(c => versionEsReal(c.versionTexto)));
    const cont = $("listaOpcionales");
    cont.replaceChildren(...lista.map(tarjetaOpcional));
}

function tarjetaOpcional(c) {
    const estado = c.firmado
        ? `Lo aceptaste el ${fechaHora(c.fechaFirma)}.`
        : c.revocadoEl ? `Lo revocaste el ${fechaHora(c.revocadoEl)}.` : "Todavía no lo aceptaste.";
    const boton = c.firmado
        ? YS.el("button", { class: "ui-btn", type: "button", text: "Revocar", on: { click: () => revocarOpcional(c) } })
        : YS.el("button", { class: "ui-btn ui-btn--primary", type: "button", text: "Leí y acepto", on: { click: () => firmarOpcional(c) } });
    return YS.el("article", { class: "opcional", "aria-labelledby": "op-" + c.clave },
        YS.el("div", { class: "opcional-cabecera" },
            YS.el("h3", { id: "op-" + c.clave, text: c.titulo }),
            YS.el("span", { class: "ui-chip " + (c.firmado ? "ui-chip--ok" : "ui-chip--neutral"), text: c.firmado ? "Aceptado" : "No aceptado" })),
        YS.el("p", { class: "opcional-texto", text: c.resumen }),
        YS.el("p", { class: "opcional-estado", text: estado }),
        boton);
}

async function firmarOpcional(c) {
    error("");
    try {
        const r = await YS.fetch(`/pacientes/${idPaciente}/consentimientos-opcionales/${encodeURIComponent(c.clave)}`, {
            method: "POST", json: { acepto: true, version: c.versionTexto }
        });
        if (!r.ok && r.status !== 409) { error(await YS.leerMensaje(r, "No se pudo registrar tu aceptación.")); return; }
        if (r.status === 409) { const m = await YS.leerMensaje(r); if (!/ya estaba/i.test(m)) error(m); }
        else YS.toast(`Aceptaste: ${c.titulo}.`);
    } catch {
        error("No pudimos conectarnos con el servidor. Probá de nuevo en unos minutos.");
    }
    cargarOpcionales();
}

async function revocarOpcional(c) {
    const ok = await YS.confirmar({
        titulo: "Revocar consentimiento",
        mensaje: `Vas a revocar "${c.titulo}". Rige desde ahora: si estás en una sesión, la función se apaga. Podés volver a aceptarlo cuando quieras.`,
        textoConfirmar: "Revocar", peligro: true
    });
    if (!ok) return;
    error("");
    try {
        const r = await YS.fetch(`/pacientes/${idPaciente}/consentimientos-opcionales/${encodeURIComponent(c.clave)}/revocar`, { method: "POST" });
        if (r.ok) YS.toast(`Revocaste: ${c.titulo}.`);
        else error(await YS.leerMensaje(r, "No se pudo revocar."));
    } catch {
        error("No pudimos conectarnos con el servidor. Probá de nuevo en unos minutos.");
    }
    cargarOpcionales();
}

$("btnFirmar").addEventListener("click", firmar);
$("btnReintentar").addEventListener("click", cargar);
cargar();
