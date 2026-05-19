public class Derivacion
{
    public int IdDerivacion { get; set; }
    public int IdPaciente { get; set; }
    public int IdProfesionalOrigen { get; set; }
    public int IdProfesionalDestino { get; set; }
    public DateTime Fecha { get; set; }
    public string Motivo { get; set; }
    public string Estado { get; set; }
}
