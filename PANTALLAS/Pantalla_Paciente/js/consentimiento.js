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

        mostrarMarca(estado.marcaProvisoria);
        if (estado.firmado) { mostrarFirmado(estado.fechaFirma, estado.versionFirmada); return; }
        versionTexto = estado.versionTexto;
        // Menor de edad: lo firma un tutor. Si el texto cambió de versión, hay que volver a firmarlo.
        const tutores = estado.esMenor ? (estado.tutores || []) : [];
        $("bloqueTutor").classList.toggle("oculto", tutores.length === 0);
        $("selTutor").innerHTML = tutores.map(t => `<option value="${t.idTutor}">${YS.esc(t.nombre)}${t.vinculo ? " (" + YS.esc(t.vinculo) + ")" : ""}</option>`).join("");
        if (estado.motivo === "version-anterior") error("El texto del consentimiento cambió desde la última vez que lo firmaste. Leelo y volvé a firmarlo para seguir.");
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

$("btnFirmar").addEventListener("click", firmar);
$("btnReintentar").addEventListener("click", cargar);
cargar();
