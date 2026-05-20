public class CambioProfesionalRequest
{
    public int IdPaciente { get; set; }
    public int IdProfesionalOrigen { get; set; }
    public int IdProfesionalDestino { get; set; }
    public string? Motivo { get; set; }
}
