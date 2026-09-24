public class Conversacion
{
    public int IdConversacion { get; set; }
    public string Telefono { get; set; } = "";
    public int? IdPaciente { get; set; }
    public string Canal { get; set; } = "Simulado";
    public string PasoActual { get; set; } = "Inicio";
    public string? DatosContexto { get; set; }
    public string Estado { get; set; } = "Activa";
    public DateTime FechaUltimaActividad { get; set; }
    public DateTime FechaCreacion { get; set; }
}
