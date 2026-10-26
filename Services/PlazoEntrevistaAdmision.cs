/// <summary>
/// La primera entrevista de admisión se programa dentro de las 24 horas de recibida la solicitud del paciente
/// (InformeAdmision.FechaCreacion). Las fechas son de pared, en la hora de la clínica: FechaCreacion la pone la base
/// (GETDATE() en el servidor, que está en la zona de la clínica) y la entrevista llega como "yyyy-MM-ddTHH:mm".
///
/// Si las 24 horas ya pasaron sin programarla, no se traba la admisión: se la puede programar cuanto antes, como
/// máximo dentro de las 24 horas siguientes a ahora, y la grilla la marca como fuera de plazo.
/// </summary>
public static class PlazoEntrevistaAdmision
{
    public static readonly TimeSpan Plazo = TimeSpan.FromHours(24);

    // Margen para el reloj del navegador y lo que tarda en apretar "Programar".
    private static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(5);

    public static bool Vencido(DateTime recibida, DateTime ahora) => ahora > recibida + Plazo;

    /// <summary>La última fecha y hora en que se puede programar la entrevista.</summary>
    public static DateTime Limite(DateTime recibida, DateTime ahora) =>
        Vencido(recibida, ahora) ? ahora + Plazo : recibida + Plazo;

    /// <summary>null si la fecha sirve; si no, el mensaje para quien la programa.</summary>
    public static string? Validar(DateTime recibida, DateTime entrevista, DateTime ahora)
    {
        if (entrevista < ahora - Tolerancia) return "Esa fecha y hora ya pasaron.";
        var limite = Limite(recibida, ahora);
        if (entrevista <= limite) return null;
        var hasta = $"{limite:dd/MM/yyyy} a las {limite:HH:mm} hs";
        return Vencido(recibida, ahora)
            ? $"Ya pasaron las 24 horas desde que llegó la solicitud: programá la entrevista cuanto antes, como máximo el {hasta}."
            : $"La entrevista tiene que ser dentro de las 24 horas de recibida la solicitud: como máximo el {hasta}.";
    }
}
