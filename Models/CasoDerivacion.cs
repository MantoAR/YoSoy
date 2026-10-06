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
    // Nota corta opcional de una reasignación manual (máx. 140): nunca se copia a
    // auditoría ni a logs. Ver AsignacionService.CATEGORIAS_MOTIVO_REASIGNACION.
    public string? Nota { get; set; }
    public int? IdCreadoPor { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaResolucion { get; set; }
}
