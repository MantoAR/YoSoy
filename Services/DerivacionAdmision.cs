/// <summary>
/// Antes de derivar a un paciente (PUT /informes-admision/{id}/derivar): lo normal hoy es que el formulario esté ACEPTADO por la recepcionista (estado «Aprobado»: no hay entrevista).
/// Lo que sigue es el camino anterior, con entrevista, que se conserva para los informes que ya estaban en curso: la entrevista de admisión tiene que estar hecha:
///  - programada: la admisión está "En entrevista", o "Rechazado" (el profesional anterior lo devolvió después de una
///    entrevista que ya se hizo, y se lo deriva a otro);
///  - con su horario ya llegado (FechaEntrevista, hora de pared de la clínica);
///  - con las observaciones de la entrevista cargadas: son lo que lee el profesional al que se deriva.
/// </summary>
public static class DerivacionAdmision
{
    /// <summary>null si se puede derivar; si no, (código, mensaje) para responder 409.</summary>
    public static (string Codigo, string Mensaje)? Validar(string? estado, DateTime? fechaEntrevista, string? observaciones, DateTime ahora)
    {
        // Recorrido actual: la entrevista de admisión se reemplazó por la revisión del formulario. Un formulario aceptado se puede asignar, sin entrevista ni observaciones.
        if (estado == EstadoInforme.Aprobado) return null;
        if (estado is EstadoInforme.Pendiente or EstadoInforme.Devuelto)
            return ("FORMULARIO_SIN_ACEPTAR", estado == EstadoInforme.Devuelto
                ? "El formulario está devuelto al paciente: tiene que completarlo de nuevo y la recepción aceptarlo antes de asignar un profesional."
                : "Primero hay que revisar y aceptar el formulario del paciente.");
        if (estado == EstadoInforme.OtroRecurso)
            return ("ESTADO", "Este ingreso se cerró con «otro recurso»: no se puede asignar un profesional.");
        if (estado is not ("En entrevista" or "Rechazado"))
            return ("ESTADO", "Esta admisión ya fue derivada o resuelta.");
        if (fechaEntrevista is not DateTime fecha)
            return ("ENTREVISTA_SIN_PROGRAMAR", "Primero hay que programar y realizar la entrevista de admisión.");
        if (fecha > ahora)
            return ("ENTREVISTA_NO_REALIZADA",
                $"La entrevista es el {fecha:dd/MM/yyyy} a las {fecha:HH:mm} hs: se puede derivar después de realizarla.");
        if (string.IsNullOrWhiteSpace(observaciones))
            return ("SIN_OBSERVACIONES", "Cargá las observaciones de la entrevista antes de derivar.");
        return null;
    }
}
