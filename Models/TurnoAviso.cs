// Datos de un turno con lo necesario para avisarle al paciente o al profesional.
public class TurnoAviso
{
    public int IdTurno { get; set; }
    public int IdPaciente { get; set; }
    public string NombrePaciente { get; set; } = "";
    public string? TelefonoPaciente { get; set; }
    public string? MailPaciente { get; set; }
    public int IdProfesional { get; set; }
    public string NombreProfesional { get; set; } = "";
    public string? MailProfesional { get; set; }
    public DateTime Fecha { get; set; }
    public TimeSpan Hora { get; set; }
    public string? Modalidad { get; set; }
    public string Estado { get; set; } = "";
    public string? CodigoSala { get; set; }

    public DateTime Momento => Fecha.Date + Hora;
}
