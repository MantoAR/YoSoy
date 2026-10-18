public interface IServicioMensajes
{
    MensajeChat RegistrarEntrante(Conversacion c, string texto);
    Task<MensajeChat> EnviarAsync(Conversacion c, string texto, string origen);

    // Con opciones para el canal (hoy, OptIn: el paciente aceptó recibir WhatsApp). Quien implementa solo el de arriba (los dobles de
    // prueba) las ignora.
    Task<MensajeChat> EnviarAsync(Conversacion c, string texto, string origen, OpcionesEnvio opciones) => EnviarAsync(c, texto, origen);
}

// Único punto por el que entra y sale un mensaje: primero lo guarda en
// MensajeChat (historial para cualquier canal) y después lo entrega por el
// canal activo, dejando registrado si se envió o dio error.
public class ServicioMensajes : IServicioMensajes
{
    private readonly IMensajeChatDAO _mensajes;
    private readonly IMessageChannel _canal;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ServicioMensajes> _log;

    public ServicioMensajes(IMensajeChatDAO mensajes, IMessageChannel canal, TimeProvider reloj, ILogger<ServicioMensajes> log)
    {
        _mensajes = mensajes;
        _canal    = canal;
        _reloj    = reloj;
        _log      = log;
    }

    public MensajeChat RegistrarEntrante(Conversacion c, string texto)
    {
        var m = new MensajeChat
        {
            IdConversacion = c.IdConversacion,
            Direccion      = "Entrante",
            Origen         = "Paciente",
            Texto          = Recortar(texto),
            FechaHora      = _reloj.GetLocalNow().DateTime,
            EstadoEnvio    = "Enviado"
        };
        _mensajes.Insertar(m);
        return m;
    }

    public Task<MensajeChat> EnviarAsync(Conversacion c, string texto, string origen) =>
        EnviarAsync(c, texto, origen, OpcionesEnvio.Ninguna);

    public async Task<MensajeChat> EnviarAsync(Conversacion c, string texto, string origen, OpcionesEnvio opciones)
    {
        var m = new MensajeChat
        {
            IdConversacion = c.IdConversacion,
            Direccion      = "Saliente",
            Origen         = origen,
            Texto          = Recortar(texto),
            FechaHora      = _reloj.GetLocalNow().DateTime,
            EstadoEnvio    = "Pendiente"
        };
        _mensajes.Insertar(m);

        ResultadoEnvio resultado;
        try
        {
            resultado = await _canal.EnviarAsync(c.Telefono, m.Texto, opciones);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error enviando el mensaje {IdMensaje} por el canal {Canal}", m.IdMensaje, _canal.Nombre);
            resultado = new ResultadoEnvio(false, Error: ex.Message);
        }

        m.EstadoEnvio = resultado.Exito ? "Enviado" : "Error";
        m.CodigoDeError = resultado.Exito ? null : resultado.Codigo;
        _mensajes.ActualizarEstadoEnvio(m.IdMensaje, m.EstadoEnvio);
        return m;
    }

    private static string Recortar(string texto) =>
        texto.Length > 4000 ? texto[..4000] : texto;
}
