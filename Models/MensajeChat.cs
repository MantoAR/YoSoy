public class MensajeChat
{
    public int IdMensaje { get; set; }
    public int IdConversacion { get; set; }
    public string Direccion { get; set; } = "";   // 'Entrante' / 'Saliente'
    public string Origen { get; set; } = "";      // 'Paciente', 'Bot', 'Humano', 'Sistema'
    public string Texto { get; set; } = "";
    public DateTime FechaHora { get; set; }
    public string EstadoEnvio { get; set; } = "Pendiente";

    // Lo que agrega la migración 17 (chat de WhatsApp para el personal). Solo se leen y se escriben con ModoAtencion = "Personal" y la migración aplicada (ver
    // IColumnasChatPersonal): en el modo Asistente quedan en null y la respuesta JSON es la de siempre.
    //  · IdExterno: el id del mensaje en WhatsApp. Es un detalle interno (sirve para no guardar dos veces lo mismo): no sale en ninguna respuesta.
    //  · Tipo: texto, audio, imagen, video, documento, sticker, ubicacion, contacto u otro (TiposDeMensaje). Lo que no es texto trae un aviso en Texto: el archivo no se guarda.
    [System.Text.Json.Serialization.JsonIgnore]
    public string? IdExterno { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Tipo { get; set; }

    // NO se guarda ni sale en las respuestas: es lo que dijo el canal al rechazar este envío (por ejemplo "sin_consentimiento" o "baja",
    // de whatsapp-service). Lo usa NotificadorTurnos para distinguir un aviso que la persona no aceptó de una falla.
    [System.Text.Json.Serialization.JsonIgnore]
    public string? CodigoDeError { get; set; }
}
