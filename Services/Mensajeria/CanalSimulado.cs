// Canal de prueba: la pantalla PANTALLAS/Pantalla_ChatSimulado lee los mensajes
// salientes directamente de MensajeChat (por polling). ServicioMensajes ya los
// guardó ahí antes de llamar a este canal, así que "entregar" es no hacer nada más.
public class CanalSimulado : IMessageChannel
{
    public string Nombre => "Simulado";

    public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto) =>
        Task.FromResult(new ResultadoEnvio(true));
}
