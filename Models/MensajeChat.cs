public class MensajeChat
{
    public int IdMensaje { get; set; }
    public int IdConversacion { get; set; }
    public string Direccion { get; set; } = "";   // 'Entrante' / 'Saliente'
    public string Origen { get; set; } = "";      // 'Paciente', 'Bot', 'Humano', 'Sistema'
    public string Texto { get; set; } = "";
    public DateTime FechaHora { get; set; }
    public string EstadoEnvio { get; set; } = "Pendiente";

    // NO se guarda ni sale en las respuestas: es lo que dijo el canal al rechazar este envío (por ejemplo "sin_consentimiento" o "baja",
    // de whatsapp-service). Lo usa NotificadorTurnos para distinguir un aviso que la persona no aceptó de una falla.
    [System.Text.Json.Serialization.JsonIgnore]
    public string? CodigoDeError { get; set; }
}
