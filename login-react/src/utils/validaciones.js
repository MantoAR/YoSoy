function parseFechaLocal(valor) {
    if (!valor) return null;
    const soloFecha = String(valor).slice(0, 10);
    const partes = soloFecha.split("-");
    if (partes.length !== 3) return null;
    const anio = parseInt(partes[0], 10);
    const mes = parseInt(partes[1], 10);
    const dia = parseInt(partes[2], 10);
    if (!anio || !mes || !dia) return null;
    return new Date(anio, mes - 1, dia);
}

export function requerido(valor, etiqueta) {
    if (valor === null || valor === undefined || String(valor).trim() === "")
        return (etiqueta || "Este campo") + " es obligatorio.";
    return null;
}

export function email(valor) {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(String(valor || "").trim())
        ? null
        : "Ingresá un email válido.";
}

export function dni(valor) {
    return /^\d{7,8}$/.test(String(valor || "").trim())
        ? null
        : "El DNI debe tener 7 u 8 dígitos.";
}

export function minLongitud(valor, n, etiqueta) {
    return String(valor || "").length < n
        ? (etiqueta || "Este campo") + " debe tener al menos " + n + " caracteres."
        : null;
}

export function coinciden(a, b, mensaje) {
    return a !== b ? (mensaje || "Los valores no coinciden.") : null;
}

export function telefono(valor) {
    const v = String(valor || "").trim();
    if (v === "") return null;
    if (!/^[\d\s+\-()]+$/.test(v) || v.length < 6 || v.length > 20)
        return "Ingresá un teléfono válido.";
    return null;
}

export function fechaNoFutura(valor, etiqueta) {
    const fecha = parseFechaLocal(valor);
    if (!fecha) return null;
    const hoy = new Date();
    hoy.setHours(0, 0, 0, 0);
    return fecha > hoy
        ? (etiqueta || "La fecha") + " no puede ser una fecha futura."
        : null;
}

export function anticipacionHoras(fechaISO, horaHHMM, horasMin) {
    const base = parseFechaLocal(fechaISO);
    if (!base) return null;
    const hm = String(horaHHMM || "").split(":");
    const h = parseInt(hm[0], 10) || 0;
    const m = parseInt(hm[1], 10) || 0;
    const objetivo = new Date(base.getFullYear(), base.getMonth(), base.getDate(), h, m, 0, 0);
    const limite = new Date(Date.now() + horasMin * 3600 * 1000);
    return objetivo < limite
        ? "Debés reservar con al menos " + horasMin + " horas de anticipación."
        : null;
}
