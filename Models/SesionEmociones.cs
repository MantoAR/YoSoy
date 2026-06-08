public class SesionEmociones
{
    public int IdRegistro { get; set; }
    public int? IdSesion { get; set; }
    public int? IdProfesional { get; set; }
    public string? RoomId { get; set; }
    public int? DuracionSegundos { get; set; }
    public int? Muestras { get; set; }
    public string? Resumen { get; set; }
    public string? Historial { get; set; }
    public DateTime FechaRegistro { get; set; }
}
