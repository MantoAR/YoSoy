public class HistorialAsignacion
{
    public int IdHistorial { get; set; }
    public int IdCaso { get; set; }
    public int? IdProfesional { get; set; }
    public string Motivo { get; set; }
    public int? IdRealizadoPor { get; set; }
    public DateTime FechaHora { get; set; }
}
