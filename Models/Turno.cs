public class Turno
{
    public int IdTurno { get; set; }
    public int IdPaciente { get; set; }
    public int IdProfesional { get; set; }
    public int IdEstadoTurno { get; set; }
    public DateTime Fecha { get; set; }
    public TimeSpan Hora { get; set; }
    public string Modalidad { get; set; }
    public string Motivo { get; set; }
    public string? CreadoPor { get; set; }
    public int? DuracionMinutos { get; set; }
}
