public class CasoDerivacion
{
    public int IdCasoDerivacion { get; set; }
    public int IdCaso { get; set; }
    public int IdGrupo { get; set; }
    public int? IdProfesionalSugerido { get; set; }
    public int? IdProfesionalAsignado { get; set; }
    public string Origen { get; set; }
    public string Estado { get; set; }
    public string? Motivo { get; set; }
    public int? IdCreadoPor { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaResolucion { get; set; }
}
