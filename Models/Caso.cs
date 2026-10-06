public class Caso
{
    public int IdCaso { get; set; }
    public int IdPaciente { get; set; }
    public string Estado { get; set; }
    public int? IdGrupoActual { get; set; }
    public int? IdProfesionalAsignado { get; set; }
    public string Origen { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int? IdInforme { get; set; }
}
