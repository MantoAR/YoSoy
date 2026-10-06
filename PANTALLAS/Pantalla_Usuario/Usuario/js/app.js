// YoSoy · Registro de paciente: acuerdo de atención (consentimiento crear_cuenta.html)
// y formulario de registro (registro.html). Usa shared/ui.js (YS) cuando está cargado.

function toast(msg, tipo = "ok", duracion = 3500) {
    if (window.YS) { YS.toast(msg, tipo, { duracion }); return; }
    // Páginas que no cargan shared/ui.js (la copia sin uso consentimiento_crear_cuenta.html)
    let cont = document.getElementById("toast-container");
    if (!cont) {
        cont = document.createElement("div");
        cont.id = "toast-container";
        document.body.appendChild(cont);
    }
    const t = document.createElement("div");
    t.className = `toast toast-${tipo}`;
    t.textContent = msg;
    cont.appendChild(t);
    setTimeout(() => t.remove(), duracion);
}

// "return" (Fase 3a, Paso 3: volver a la reserva por link público tras loguearse o
// registrarse) solo se reenvía tal cual acá — se valida una sola vez, en index.html,
// que es la única página de esta cadena que carga shared/ui.js sin condición.
function conRetorno(url) {
    const retorno = new URLSearchParams(location.search).get("return");
    return url + (retorno ? "?return=" + encodeURIComponent(retorno) : "");
}

function aceptar() {
    window.location.href = conRetorno("registro.html");
}

// registro.html: el botón de éxito ("Volver al ingreso") también tiene que llevar el
// return, para que el paciente recién registrado pueda volver derecho a la reserva.
const linkVolver = document.getElementById("linkVolverIngreso");
if (linkVolver) linkVolver.href = conRetorno("../../index.html");

function rechazar() {
    toast("Para registrarte tenés que aceptar el acuerdo de atención.", "warn");
    setTimeout(() => window.location.href = conRetorno("index.html"), 2000);
}

function calcularEdad(fechaNacimiento) {
    const fecha = new Date(fechaNacimiento + "T12:00:00");
    const hoy = new Date();
    let edad = hoy.getFullYear() - fecha.getFullYear();
    const mes = hoy.getMonth() - fecha.getMonth();
    if (mes < 0 || (mes === 0 && hoy.getDate() < fecha.getDate())) edad--;
    return edad;
}

document.getElementById("formRegistro")?.addEventListener("submit", async function (e) {
    e.preventDefault();
    const error = document.getElementById("errorRegistro");
    error.classList.add("oculto");

    const valido = YS.form.validar([
        { id: "nombre", requerido: "Ingresá tu nombre." },
        { id: "apellido", requerido: "Ingresá tu apellido." },
        { id: "dni", requerido: "Ingresá tu DNI.", patron: /^\d{7,9}$/, mensajePatron: "El DNI tiene que tener entre 7 y 9 números, sin puntos." },
        { id: "sexo", requerido: "Elegí una opción." },
        { id: "fechaNacimiento", requerido: "Ingresá tu fecha de nacimiento.",
          validar: v => v && new Date(v + "T12:00:00") > new Date() ? "La fecha no puede ser posterior a hoy." : null },
        { id: "email", requerido: "Ingresá tu email.", patron: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, mensajePatron: "Ingresá un email válido." },
        { id: "domicilio", requerido: "Ingresá la calle." },
        { id: "numero", requerido: "Ingresá el número." },
        { id: "provincia", requerido: "Ingresá la provincia." }
    ]);
    if (!valido) return;

    const fechaNacimiento = document.getElementById("fechaNacimiento").value;
    const edad = calcularEdad(fechaNacimiento);

    // Menor de edad: se muestra la sección del tutor (como antes, el registro sigue igual).
    if (edad < 18) {
        document.getElementById("seccionTutor").classList.remove("oculto");
        toast("Paciente menor de edad. Debe completar los datos del tutor responsable.", "warn");
    }

    const valor = id => document.getElementById(id).value;
    const paciente = {
        nombre: valor("nombre"),
        segundoNombre: valor("segundoNombre"),
        apellido: valor("apellido"),
        segundoApellido: valor("segundoApellido"),
        dni: valor("dni"),
        fechaNacimiento: fechaNacimiento,
        sexo: valor("sexo"),
        mail: valor("email"),
        mailValidado: false,
        telefono: valor("telefono"),
        domicilio: valor("domicilio"),
        numero: parseInt(valor("numero")),
        provincia: valor("provincia"),
        estado: "Activo",
        esMenorEdad: edad < 18,
        fechaAlta: new Date().toISOString()
    };

    const btn = document.getElementById("btnRegistrar");
    btn.classList.add("cargando");
    try {
        const respuesta = await YS.fetch("/pacientes", { method: "POST", json: paciente, sinRedireccion: true });
        if (respuesta.ok) {
            document.getElementById("formRegistro").classList.add("oculto");
            document.getElementById("registroListo").classList.remove("oculto");
            window.scrollTo({ top: 0 });
        } else {
            error.textContent = await YS.leerMensaje(respuesta, "No se pudo registrar al paciente. Revisá los datos e intentá de nuevo.");
            error.classList.remove("oculto");
        }
    } catch (err) {
        error.textContent = err.message || "No se pudo conectar con el servidor.";
        error.classList.remove("oculto");
    } finally {
        btn.classList.remove("cargando");
    }
});
