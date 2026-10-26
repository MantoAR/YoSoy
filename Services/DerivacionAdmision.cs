/// <summary>
/// Antes de derivar a un paciente (PUT /informes-admision/{id}/derivar) la entrevista de admisión tiene que estar hecha:
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
        if (estado == "Pendiente")
            return ("ENTREVISTA_SIN_PROGRAMAR", "Primero hay que programar y realizar la entrevista de admisión.");
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
