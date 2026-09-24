public class Recordatorio
{
    public int IdRecordatorio { get; set; }
    public int IdTurno { get; set; }
    public string Tipo { get; set; } = "";        // 'Turno24h', 'TurnoSolicitado', 'TurnoConfirmado', ...
    public string Canal { get; set; } = "";       // 'Chat', 'Email', 'App'
    public DateTime FechaProgramada { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public string Estado { get; set; } = "Pendiente";
    public int Intentos { get; set; }
    public string? Error { get; set; }
    public string? Respuesta { get; set; }
    public DateTime? FechaRespuesta { get; set; }
}
