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
    t.innerHTML = `<span class="toast-icono">${iconos[tipo] ?? "ℹ"}</span><span>${msg}</span>`;
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

const V = window.Validaciones;

V.conectarLimpieza(["nombre","dni","email","domicilio","fechaNacimiento","telefono"]);

document.getElementById("formRegistro")
?.addEventListener("submit", async function(e){

    e.preventDefault();

    const ok = V.validarCampos([
        { id: "nombre",          validar: v => V.requerido(v, "El nombre") },
        { id: "dni",             validar: v => V.requerido(v, "El DNI") || V.dni(v) },
        { id: "email",           validar: v => V.requerido(v, "El email") || V.email(v) },
        { id: "domicilio",       validar: v => V.requerido(v, "El domicilio") },
        { id: "fechaNacimiento", validar: v => V.requerido(v, "La fecha de nacimiento") || V.fechaNoFutura(v, "La fecha de nacimiento") },
        { id: "telefono",        validar: v => V.telefono(v) },
    ]);
    if (!ok) return;

    const fechaNacimiento = document.getElementById("fechaNacimiento").value;

    const fecha = new Date(fechaNacimiento);
    const hoy = new Date();
    let edad = hoy.getFullYear() - fecha.getFullYear();
    const mes = hoy.getMonth() - fecha.getMonth();
    if (mes < 0 || (mes === 0 && hoy.getDate() < fecha.getDate())) edad--;

    if (edad < 18) {
        document.getElementById("seccionTutor").style.display = "block";
        toast("Paciente menor de edad. Debe completar los datos del tutor responsable.", "warn");
    }

    const paciente = {
        nombre:          document.getElementById("nombre").value.trim(),
        dni:             document.getElementById("dni").value.trim(),
        fechaNacimiento: fechaNacimiento,
        domicilio:       document.getElementById("domicilio").value.trim(),
        telefono:        document.getElementById("telefono").value.trim(),
        mail:            document.getElementById("email").value.trim()
    };

    try {
        const respuesta = await fetch("/pacientes", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(paciente)
        });

        if (respuesta.ok) {
            toast("Registro enviado. Te avisaremos por mail cuando tu cuenta sea aprobada.");
        } else {
            toast("Error al registrar paciente", "error");
        }
    } catch (error) {
        console.error(error);
        toast("No se pudo conectar con la API", "error");
    }

});

function rechazar(){
    toast("Debe aceptar el consentimiento para continuar", "warn");
    setTimeout(() => window.location.href = "index.html", 2000);
}

