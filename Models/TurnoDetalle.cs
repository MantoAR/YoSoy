public class TurnoDetalle
{
    public int IdTurno { get; set; }
    public int IdPaciente { get; set; }
    public int IdProfesional { get; set; }
    public string NombreProfesional { get; set; }
    public string Especialidad { get; set; }
    public int IdEstadoTurno { get; set; }
    public string EstadoDescripcion { get; set; }
    public DateTime Fecha { get; set; }
    public TimeSpan Hora { get; set; }
    public string? Modalidad { get; set; }
    public string? Motivo { get; set; }
    public string? MailProfesional { get; set; }
    public string? TelefonoProfesional { get; set; }
}
