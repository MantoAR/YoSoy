public class Agenda
{
    public int IdAgenda { get; set; }
    public int IdProfesional { get; set; }
    public string Dia { get; set; }
    public TimeSpan? HoraInicio { get; set; }
    public TimeSpan? HoraFin { get; set; }
    public bool Disponible { get; set; }
    public string Codigo { get; set; }
    public string Descripcion { get; set; }
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public int? IdServicio { get; set; }
}