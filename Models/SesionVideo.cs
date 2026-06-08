public class SesionVideo
{
    public int IdSesion { get; set; }
    public int IdTurno { get; set; }
    public string RoomId { get; set; }
    public string Estado { get; set; }
    public DateTime? InicioReal { get; set; }
    public DateTime FinProgramado { get; set; }
    public DateTime? FinReal { get; set; }
    public int? DuracionReal { get; set; }
    public string? MotivoFin { get; set; }
    public DateTime FechaCreacion { get; set; }
}
