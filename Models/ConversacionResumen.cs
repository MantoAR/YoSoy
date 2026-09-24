public class ConversacionResumen
{
    public int IdConversacion { get; set; }
    public string Telefono { get; set; } = "";
    public int? IdPaciente { get; set; }
    public string? NombrePaciente { get; set; }
    public string Canal { get; set; } = "";
    public string Estado { get; set; } = "";
    public string PasoActual { get; set; } = "";
    public DateTime FechaUltimaActividad { get; set; }
    public string? UltimoMensaje { get; set; }
    public DateTime? FechaUltimoMensaje { get; set; }
}
