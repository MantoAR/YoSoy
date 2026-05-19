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

        alert(
            "Paciente menor de edad. Debe completar los datos del tutor responsable."
        );
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
            "https://localhost:7167/api/Paciente",
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

            alert(
                "Paciente registrado correctamente"
            );

        }else{

            alert(
                "Error al registrar paciente"
            );
        }

    } catch(error){

        console.error(error);

        alert(
            "No se pudo conectar con la API"
        );
    }

});

function rechazar(){

    alert("Debe aceptar el consentimiento para continuar");

    window.location.href = "index.html";
}

