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

// recienFirmado: la persona acaba de firmar en esta pantalla. Se le confirma con un título propio y lo que le habilita, y
// el foco va al botón para volver (sin redirección automática: la confirmación tiene que poder leerse).
function mostrarFirmado(fechaFirma, version, recienFirmado = false) {
    $("yaFirmado").querySelector(".firmado-icono").innerHTML = YS.icono("check");
    $("tituloFirmado").textContent = recienFirmado ? "Consentimiento firmado" : "Ya firmaste este consentimiento";
    $("detalleFirma").textContent = `Lo firmaste el ${fechaHora(fechaFirma)}` + (versionEsReal(version) ? ` (versión del texto: ${version}).` : ".");
    $("ayudaFirmado").textContent = recienFirmado
        ? "Listo: ya podés confirmar tus turnos. Guardamos la fecha y la hora de tu firma."
        : "No hace falta volver a firmarlo. Si necesitás revocarlo o tenés dudas, hablalo con tu profesional.";
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

$("btnFirmar").addEventListener("click", firmar);
$("btnReintentar").addEventListener("click", cargar);
cargar();
