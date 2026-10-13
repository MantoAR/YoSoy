public class Evolucion
{
    public int IdEvolucion { get; set; }
    public int IdHistoria { get; set; }
    public int IdProfesional { get; set; }
    public int? IdTurno { get; set; }
    public DateTime Fecha { get; set; }
    public string Diagnostico { get; set; }
    public string Observaciones { get; set; }
    public string PlanTratamiento { get; set; }

    // Evolución por enfoque (migrations/add_atencion_consentimientos_ia_evolucion.sql).
    public string? TipoCierre { get; set; }
    public string? Enfoque { get; set; }
    public string? ModoRegistro { get; set; }
    public string? ContenidoJson { get; set; }
    public string? CodigoSala { get; set; }
    // 'Borrador' o 'Firmada'. Las anteriores a la migración quedan como 'Firmada'.
    public string Estado { get; set; } = "Firmada";
    public DateTime? FechaFirma { get; set; }
    public string? HashContenido { get; set; }
}
