// Escape de HTML para datos que cargan los usuarios. En Cambios-Agus vive en shared/ui.js (YS.esc);
// en main no existe esa capa, asi que el parche de XSS define lo minimo que usa.
window.YS = window.YS || {};
YS.esc = YS.esc || (s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]));
function toast(msg, tipo = "ok", duracion = 3500) {
    let cont = document.getElementById("toast-container");
    if (!cont) {
        cont = document.createElement("div");
        cont.id = "toast-container";
        document.body.appendChild(cont);
    }
    const iconos = { ok: "✔", error: "✖", warn: "⚠" };
    const t = document.createElement("div");
    t.className = `toast toast-${tipo}`;
    t.innerHTML = `<span class="toast-icono">${iconos[tipo] ?? "ℹ"}</span><span>${YS.esc(msg)}</span>`;
    cont.appendChild(t);
    setTimeout(() => {
        t.classList.add("saliendo");
        t.addEventListener("animationend", () => t.remove());
    }, duracion);
}

function aceptar(){
    window.location.href = "registro.html";
}

/*document.getElementById("formRegistro")
?.addEventListener("submit", function(e){

    e.preventDefault();

    const fechaNacimiento =
    document.getElementById("fechaNacimiento").value;

    const fecha = new Date(fechaNacimiento);

    const hoy = new Date();

    let edad =
    hoy.getFullYear() - fecha.getFullYear();

    const mes =
    hoy.getMonth() - fecha.getMonth();

    if(
        mes < 0 ||
        (mes === 0 && hoy.getDate() < fecha.getDate())
    ){
        edad--;
    }

    // =====================================
    // MENOR DE EDAD
    // =====================================

    if(edad < 18){

        const tutor =
        document.getElementById("seccionTutor");

        tutor.style.display = "block";

        alert(
            "Paciente menor de edad. Debe completar los datos del tutor responsable."
        );

        return;
    }

    // =====================================
    // MAYOR DE EDAD
    // =====================================

    alert(
        "Paciente registrado correctamente"
    );

}); */

document.getElementById("formRegistro")
?.addEventListener("submit", async function(e){

    e.preventDefault();

    const fechaNacimiento =
        document.getElementById("fechaNacimiento").value;

    const fecha = new Date(fechaNacimiento);

    const hoy = new Date();

    let edad =
        hoy.getFullYear() - fecha.getFullYear();

    const mes =
        hoy.getMonth() - fecha.getMonth();

    if(
        mes < 0 ||
        (mes === 0 && hoy.getDate() < fecha.getDate())
    ){
        edad--;
    }

    // =====================================
    // MENOR DE EDAD
    // =====================================

    if(edad < 18){

        const tutor =
            document.getElementById("seccionTutor");

        tutor.style.display = "block";

        toast("Paciente menor de edad. Debe completar los datos del tutor responsable.", "warn");
    }

    const paciente = {

        nombre:
            document.getElementById("nombre").value,

        segundoNombre:
            document.getElementById("segundoNombre").value,

        apellido:
            document.getElementById("apellido").value,

        segundoApellido:
            document.getElementById("segundoApellido").value,

        dni:
            document.getElementById("dni").value,

        fechaNacimiento:
            fechaNacimiento,

        sexo:
            document.getElementById("sexo").value,

        mail:
            document.getElementById("email").value,

        mailValidado: false,

        telefono:
            document.getElementById("telefono").value,

        domicilio:
            document.getElementById("domicilio").value,

        numero:
            parseInt(
                document.getElementById("numero").value
            ),

        provincia:
            document.getElementById("provincia").value,

        estado: "Activo",

        esMenorEdad: edad < 18,

        fechaAlta:
            new Date().toISOString()
    };

    try {

        const respuesta = await fetch(
            "http://localhost:5000/pacientes",
            {
                method: "POST",

                headers: {
                    "Content-Type":
                        "application/json"
                },

                body: JSON.stringify(paciente)
            }
        );

        if(respuesta.ok){
            toast("Paciente registrado correctamente");
        }else{
            toast("Error al registrar paciente", "error");
        }

    } catch(error){
        console.error(error);
        toast("No se pudo conectar con la API", "error");
    }

});

function rechazar(){
    toast("Debe aceptar el consentimiento para continuar", "warn");
    setTimeout(() => window.location.href = "index.html", 2000);
}

