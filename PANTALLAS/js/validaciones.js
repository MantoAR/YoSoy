(function () {
    function parseFechaLocal(valor) {
        if (!valor) return null;
        var soloFecha = String(valor).slice(0, 10);
        var partes = soloFecha.split("-");
        if (partes.length !== 3) return null;
        var anio = parseInt(partes[0], 10);
        var mes = parseInt(partes[1], 10);
        var dia = parseInt(partes[2], 10);
        if (!anio || !mes || !dia) return null;
        return new Date(anio, mes - 1, dia);
    }

    function requerido(valor, etiqueta) {
        if (valor === null || valor === undefined || String(valor).trim() === "")
            return (etiqueta || "Este campo") + " es obligatorio.";
        return null;
    }

    function email(valor) {
        return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(String(valor || "").trim())
            ? null
            : "Ingresá un email válido.";
    }

    function dni(valor) {
        return /^\d{7,8}$/.test(String(valor || "").trim())
            ? null
            : "El DNI debe tener 7 u 8 dígitos.";
    }

    function minLongitud(valor, n, etiqueta) {
        return String(valor || "").length < n
            ? (etiqueta || "Este campo") + " debe tener al menos " + n + " caracteres."
            : null;
    }

    function coinciden(a, b, mensaje) {
        return a !== b ? (mensaje || "Los valores no coinciden.") : null;
    }

    function telefono(valor) {
        var v = String(valor || "").trim();
        if (v === "") return null;
        if (!/^[\d\s+\-()]+$/.test(v) || v.length < 6 || v.length > 20)
            return "Ingresá un teléfono válido.";
        return null;
    }

    function fechaNoFutura(valor, etiqueta) {
        var fecha = parseFechaLocal(valor);
        if (!fecha) return null;
        var hoy = new Date();
        hoy.setHours(0, 0, 0, 0);
        return fecha > hoy
            ? (etiqueta || "La fecha") + " no puede ser una fecha futura."
            : null;
    }

    function anticipacionHoras(fechaISO, horaHHMM, horasMin) {
        var base = parseFechaLocal(fechaISO);
        if (!base) return null;
        var hm = String(horaHHMM || "").split(":");
        var h = parseInt(hm[0], 10) || 0;
        var m = parseInt(hm[1], 10) || 0;
        var objetivo = new Date(base.getFullYear(), base.getMonth(), base.getDate(), h, m, 0, 0);
        var limite = new Date(Date.now() + horasMin * 3600 * 1000);
        return objetivo < limite
            ? "Debés reservar con al menos " + horasMin + " horas de anticipación."
            : null;
    }

    function marcarError(id, mensaje) {
        var campo = document.getElementById(id);
        if (!campo) return;
        campo.classList.add("invalido");
        var existente = document.getElementById("err-" + id);
        if (existente) {
            if (mensaje) existente.textContent = mensaje;
            existente.style.display = "block";
            return;
        }
        var small = document.getElementById("val-error-" + id);
        if (!small) {
            small = document.createElement("small");
            small.className = "val-error";
            small.id = "val-error-" + id;
            campo.insertAdjacentElement("afterend", small);
        }
        small.textContent = mensaje || "";
    }

    function limpiarError(id) {
        var campo = document.getElementById(id);
        if (campo) campo.classList.remove("invalido");
        var existente = document.getElementById("err-" + id);
        if (existente) existente.style.display = "none";
        var small = document.getElementById("val-error-" + id);
        if (small) small.remove();
    }

    function validarCampos(reglas) {
        var valido = true;
        var primerError = null;
        reglas.forEach(function (regla) {
            var campo = document.getElementById(regla.id);
            var valor = campo ? campo.value : "";
            var error = regla.validar(valor);
            if (error) {
                marcarError(regla.id, error);
                if (!primerError) primerError = campo;
                valido = false;
            } else {
                limpiarError(regla.id);
            }
        });
        if (primerError) primerError.focus();
        return valido;
    }

    function conectarLimpieza(ids) {
        ids.forEach(function (id) {
            var campo = document.getElementById(id);
            if (campo) campo.addEventListener("input", function () { limpiarError(id); });
        });
    }

    window.Validaciones = {
        requerido: requerido,
        email: email,
        dni: dni,
        minLongitud: minLongitud,
        coinciden: coinciden,
        telefono: telefono,
        fechaNoFutura: fechaNoFutura,
        anticipacionHoras: anticipacionHoras,
        marcarError: marcarError,
        limpiarError: limpiarError,
        validarCampos: validarCampos,
        conectarLimpieza: conectarLimpieza
    };
})();
