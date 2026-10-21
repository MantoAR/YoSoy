/// <summary>Evalúa fechas sin zona, guardadas en la hora local de la clínica.</summary>
public sealed class VentanaDeConversacion(TimeProvider reloj)
{
    public const string MensajeVencida = "Pasaron más de 24 h desde el último mensaje de esta persona. Para cuidar el número del centro, solo se puede responder dentro de las 24 h. Podrás escribirle de nuevo cuando ella escriba.";
    public const string MensajeNuncaEscribio = "Esta persona todavía no escribió por este número. El centro no puede iniciar la conversación.";

    public EstadoVentanaDeConversacion Evaluar(DateTime? ultimoEntrante)
    {
        if (ultimoEntrante is null) return new(false, "nunca_escribio", 0, null);

        var transcurrido = reloj.GetLocalNow().DateTime - ultimoEntrante.Value;
        var restante = TimeSpan.FromHours(24) - transcurrido;
        return new(restante > TimeSpan.Zero, restante > TimeSpan.Zero ? null : "vencida",
            (long)Math.Clamp(Math.Ceiling(restante.TotalSeconds), 0, 86400),
            (long)Math.Max(0, Math.Floor(transcurrido.TotalSeconds)));
    }
}

public sealed record EstadoVentanaDeConversacion(
    bool Abierta, string? Motivo, long SegundosRestantes, long? UltimoEntranteHaceSegundos)
{
    public string? Mensaje => Motivo switch
    {
        "vencida" => VentanaDeConversacion.MensajeVencida,
        "nunca_escribio" => VentanaDeConversacion.MensajeNuncaEscribio,
        _ => null
    };
}
