public class MensajeChat
{
    public int IdMensaje { get; set; }
    public int IdConversacion { get; set; }
    public string Direccion { get; set; } = "";   // 'Entrante' / 'Saliente'
    public string Origen { get; set; } = "";      // 'Paciente', 'Bot', 'Humano', 'Sistema'
    public string Texto { get; set; } = "";
    public DateTime FechaHora { get; set; }
    public string EstadoEnvio { get; set; } = "Pendiente";
}
