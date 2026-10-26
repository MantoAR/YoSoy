public class SesionClinica
{
    public int IdSesion { get; set; }
    public int? IdTurno { get; set; }
    public int IdPaciente { get; set; }
    public int IdProfesional { get; set; }
    public DateTime FechaHora { get; set; }
    public string? Transcripcion { get; set; }
    public string? NotasEvolucion { get; set; }
    // Desde el PR 6 lo guardado acá es el dato CIFRADO ("ys1.<clave>.<...>"); lo anterior es JSON en claro. Se lee
    // siempre a través de VistaSesionesClinicas, nunca se devuelve tal cual.
    public string? HistorialEmociones { get; set; }
    public string? TipoCierre { get; set; }

    // Con qué consentimiento (ConsentimientoVideo) se guardaron las emociones; null si no hay o son de antes del PR 6.
    public int? IdConsentimientoVideo { get; set; }
    // Cuándo (UTC) y por qué ("revocacion" | "retencion") se anularon las emociones guardadas.
    public DateTime? EmocionesAnuladasUtc { get; set; }
    public string? EmocionesAnuladasMotivo { get; set; }

    // Migración 26 (cierre de sesión). Solo se escriben desde el cierre; las lecturas de siempre no las traen.
    // La clave con la que la sala identificó este cierre: repetirlo con la misma clave no duplica nada.
    public Guid? ClaveCierre { get; set; }
    // Cuándo (UTC) este cierre pasó el turno a Realizado; null si no tocó el turno.
    public DateTime? TurnoRealizadoUtc { get; set; }
    // La evolución de la historia clínica a la que el profesional vinculó esta sesión (a mano); null si no hay vínculo. Solo la completan las lecturas.
    public int? IdEvolucion { get; set; }
}
