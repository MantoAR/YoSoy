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
}
