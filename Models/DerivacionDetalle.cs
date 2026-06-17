public class DerivacionDetalle
{
    public int IdDerivacion { get; set; }
    public int IdPaciente { get; set; }
    public string NombrePaciente { get; set; }
    public int IdProfesionalOrigen { get; set; }
    public string NombreProfesionalOrigen { get; set; }
    public int IdProfesionalDestino { get; set; }
    public string NombreProfesionalDestino { get; set; }
    public int IdEspecialidad { get; set; }
    public string Especialidad { get; set; }
    public DateTime Fecha { get; set; }
    public string? Motivo { get; set; }
    public string Estado { get; set; }
}
