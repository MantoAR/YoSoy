public record ResultadoEnvio(bool Exito, string? IdExterno = null, string? Error = null);

// Canal por el que sale un mensaje hacia el paciente (chat simulado, WhatsApp,
// y a futuro Instagram/Facebook). El historial no es responsabilidad del canal:
// lo guarda ServicioMensajes en MensajeChat antes de entregar, sea cual sea el canal.
public interface IMessageChannel
{
    string Nombre { get; }
    Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto);
}
