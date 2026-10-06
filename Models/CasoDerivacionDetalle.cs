// CasoDerivacion con nombres resueltos, para la cola de pendientes de confirmar.
public class CasoDerivacionDetalle
{
    public int IdCasoDerivacion { get; set; }
    public int IdCaso { get; set; }
    public string NombrePaciente { get; set; }
    public int IdGrupo { get; set; }
    public string NombreGrupo { get; set; }
    public int? IdProfesionalSugerido { get; set; }
    public string? NombreProfesionalSugerido { get; set; }
    public int? IdProfesionalAsignado { get; set; }
    public string? NombreProfesionalAsignado { get; set; }
    public string Origen { get; set; }
    public string Estado { get; set; }
    public string? Motivo { get; set; }
    public DateTime FechaCreacion { get; set; }
}
