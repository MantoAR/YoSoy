public class Agenda
{
    public int IdAgenda { get; set; }
    public int IdProfesional { get; set; }
    public string Dia { get; set; }
    public TimeSpan? HoraInicio { get; set; }
    public TimeSpan? HoraFin { get; set; }
    public bool Disponible { get; set; }
    public int IntervaloMinutos { get; set; } = 30; // NUEVO
    // Franja de entrevistas de admisión (migración 26): ahí no se ofrecen turnos a los pacientes; la API
    // programa sola las primeras entrevistas que agenda este profesional (TurnoReglasService.PrimerHorarioDeAdmision).
    public bool EsAdmision { get; set; }
    // Desde y hasta qué día vale la franja (migración 27). null = sin límite de ese lado.
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
}
