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

// Copiado de crear-cuenta-profesional.html — único otro lugar del repo con un campo de
// contraseña con mostrar/ocultar.
function togglePass(id, boton) {
    const inp = document.getElementById(id);
    const mostrar = inp.type === "password";
    inp.type = mostrar ? "text" : "password";
    boton.setAttribute("aria-pressed", mostrar);
    boton.setAttribute("aria-label", mostrar ? "Ocultar contraseña" : "Mostrar contraseña");
}

function calcularEdad(fechaNacimiento) {
    const fecha = new Date(fechaNacimiento + "T12:00:00");
    const hoy = new Date();
    let edad = hoy.getFullYear() - fecha.getFullYear();
    const mes = hoy.getMonth() - fecha.getMonth();
    if (mes < 0 || (mes === 0 && hoy.getDate() < fecha.getDate())) edad--;
    return edad;
}

// ── Avisos por WhatsApp (opcional) ──────────────────────────────
// El texto y su versión los sirve la API (YS.whatsapp.texto): lo que se lee es lo que queda registrado. La casilla NUNCA viene tildada:
// se destilda a mano al cargar (por si el navegador recuerda un estado) y solo cuenta un tilde de la persona. Si no hay texto, no se ofrece.
let textoWhatsApp = null;

(async function prepararWhatsApp() {
    const bloque = document.getElementById("bloqueWhatsApp");
    const caja = document.getElementById("aceptaWhatsApp");
    if (!bloque || !caja || !window.YS?.whatsapp) return;
    caja.checked = false;
    textoWhatsApp = await YS.whatsapp.texto();
    if (!textoWhatsApp) return;
    YS.whatsapp.dibujarTexto(document.getElementById("textoWhatsApp"), textoWhatsApp);
    caja.checked = false;
    bloque.classList.remove("oculto");
})();

// Lo que se le dice a la persona cuando el registro salió bien, según lo que contestó la API sobre el consentimiento.
function avisoDeWhatsAppTrasRegistrarse(datos) {
    const c = datos?.consentimientoWhatsApp;
    if (!c?.pedido) return null;
    return c.estado === "acepta"
        ? "Activaste los avisos de tus turnos por WhatsApp. Podés cambiarlo cuando quieras desde tu perfil."
        : "Tu cuenta quedó creada, pero no pudimos guardar tu pedido de avisos por WhatsApp. Podés activarlos desde tu perfil.";
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
        { id: "provincia", requerido: "Ingresá la provincia." },
        { id: "usuario", requerido: "Elegí un nombre de usuario.", patron: /^[a-zA-Z0-9_.-]{3,50}$/, mensajePatron: "El usuario tiene que tener entre 3 y 50 caracteres (letras, números, puntos, guiones o guion bajo)." },
        { id: "pass1", requerido: "Ingresá una contraseña.", minimo: 6, mensajeMinimo: "La contraseña debe tener al menos 6 caracteres." },
        { id: "pass2", requerido: "Repetí la contraseña.",
          validar: v => v !== document.getElementById("pass1").value ? "Las contraseñas no coinciden." : null }
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
        fechaAlta: new Date().toISOString(),
        usuario: valor("usuario"),
        contrasena: valor("pass1")
    };

    // Avisos por WhatsApp: solo con la casilla tildada por la persona y con la versión del texto que ella leyó. El servidor lo valida de nuevo
    // (celular argentino válido, mayor de edad, texto vigente) ANTES de crear la cuenta; acá se evita un viaje inútil.
    const quiereWhatsApp = !!(textoWhatsApp && document.getElementById("aceptaWhatsApp")?.checked);
    paciente.aceptaWhatsApp = quiereWhatsApp;
    paciente.versionTextoWhatsApp = quiereWhatsApp ? textoWhatsApp.version : null;
    if (quiereWhatsApp) {
        if (!valor("telefono").trim()) {
            YS.form.marcar("telefono", "Para recibir avisos por WhatsApp cargá tu celular, con código de área.");
            return;
        }
        if (edad < 18) {
            error.textContent = YS.whatsapp.motivo("menor-de-edad") + " Podés registrarte igual, sin tildar esa opción.";
            error.classList.remove("oculto");
            return;
        }
    }

    const btn = document.getElementById("btnRegistrar");
    btn.classList.add("cargando");
    try {
        const respuesta = await YS.fetch("/pacientes", { method: "POST", json: paciente, sinRedireccion: true });
        if (respuesta.ok) {
            let aviso = null;
            try { aviso = avisoDeWhatsAppTrasRegistrarse(await respuesta.json()); } catch { /* sin cuerpo legible: no hay aviso */ }
            const avisoWhatsApp = document.getElementById("avisoWhatsAppRegistro");
            if (aviso && avisoWhatsApp) {
                avisoWhatsApp.textContent = aviso;
                avisoWhatsApp.classList.remove("oculto");
            }
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
