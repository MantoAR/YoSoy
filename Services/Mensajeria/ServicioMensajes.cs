public interface IServicioMensajes
{
    MensajeChat RegistrarEntrante(Conversacion c, string texto);
    Task<MensajeChat> EnviarAsync(Conversacion c, string texto, string origen);

    // Con opciones: OptIn (para el canal: el paciente aceptó recibir WhatsApp) e IniciaElNegocio (para la regla de las respuestas automáticas, abajo).
    // Quien implementa solo el de arriba (los dobles de prueba) las ignora.
    Task<MensajeChat> EnviarAsync(Conversacion c, string texto, string origen, OpcionesEnvio opciones) => EnviarAsync(c, texto, origen);
}

// Único punto por el que entra y sale un mensaje: primero lo guarda en
// MensajeChat (historial para cualquier canal) y después lo entrega por el
// canal activo, dejando registrado si se envió o dio error.
//
// UNA REGLA, para no parecer un bot ante WhatsApp: un mensaje AUTOMÁTICO (origen "Bot" o "Sistema") es una respuesta, y una respuesta solo sale
// dentro de una conversación en la que la persona ya escribió. Si nunca escribió (por ejemplo, la conversación la armó un aviso de turno y alguien
// del equipo la cierra o la reabre) no se manda nada, no se guarda y se devuelve un error con el código "sin_conversacion_iniciada". Las
// excepciones, a propósito y una por una: el equipo ("Humano": decide una persona) y los avisos que declaran OpcionesEnvio.IniciaElNegocio (los
// avisos de turno por evento, que acotan el consentimiento y whatsapp-service). Vale igual para cualquier canal: lo que se prueba es lo que corre.
public class ServicioMensajes : IServicioMensajes
{
    /// <summary>El código del error cuando se frena un mensaje automático porque la persona nunca escribió en la conversación.</summary>
    public const string CodigoSinConversacionIniciada = "sin_conversacion_iniciada";

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

        // Una respuesta automática sin conversación que la persona haya empezado: no se guarda ni se manda (el estado "Error" es del resultado, no de una fila).
        if (EsAutomatico(origen) && !opciones.IniciaElNegocio && !_mensajes.TieneMensajeEntrante(c.IdConversacion))
        {
            _log.LogInformation("No se manda el mensaje automático ({Origen}) de la conversación {IdConversacion}: la persona todavía no escribió en ella.",
                origen, c.IdConversacion);
            m.EstadoEnvio = "Error";
            m.CodigoDeError = CodigoSinConversacionIniciada;
            return m;
        }

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

    // "Bot" y "Sistema" salen solos; "Humano" lo escribe una persona del equipo; "Paciente" es lo que entra.
    private static bool EsAutomatico(string origen) => origen is "Bot" or "Sistema";

    private static string Recortar(string texto) =>
        texto.Length > 4000 ? texto[..4000] : texto;
}
