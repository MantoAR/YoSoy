public class BloqueosAgenda
{
    public int IdBloqueo { get; set; }
    public int IdProfesional { get; set; }
    public int? IdAgenda { get; set; }
    public DateTime FechaDesde { get; set; }
    public DateTime FechaHasta { get; set; }
    public TimeSpan? HoraDesde { get; set; }
    public TimeSpan? HoraHasta { get; set; }
    public string Motivo { get; set; }
    public string Observaciones { get; set; }
    public string Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
}