// Caso con nombres resueltos, para las pantallas de Casos y "Mis casos".
public class CasoDetalle
{
    public int IdCaso { get; set; }
    public int IdPaciente { get; set; }
    public string NombrePaciente { get; set; }
    public string Estado { get; set; }
    public int? IdGrupoActual { get; set; }
    public string? NombreGrupo { get; set; }
    public int? IdProfesionalAsignado { get; set; }
    public string? NombreProfesionalAsignado { get; set; }
    public string Origen { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int? IdInforme { get; set; }
    // El paciente figura derivado a psiquiatría (migración 34). Se completa en el endpoint; no sale de la tabla Caso.
    public bool Derivado { get; set; }
}
