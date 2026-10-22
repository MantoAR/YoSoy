public interface IServicioMensajes
{
    MensajeChat RegistrarEntrante(Conversacion c, string texto);

    // Con lo que trae whatsapp-service además del texto (id, tipo, hora; ver DatosDelEntrante). Devuelve null si ese mensaje ya estaba guardado (un reintento). Quien implementa
    // solo el de arriba (los dobles de prueba) no ve esos datos.
    MensajeChat? RegistrarEntrante(Conversacion c, string texto, DatosDelEntrante datos) => RegistrarEntrante(c, texto);

    Task<MensajeChat> EnviarAsync(Conversacion c, string texto, string origen);

    // Con opciones: OptIn (para el canal: el paciente aceptó recibir WhatsApp) e IniciaElNegocio (para la regla de las respuestas automáticas, abajo).
    // Quien implementa solo el de arriba (los dobles de prueba) las ignora.
    Task<MensajeChat> EnviarAsync(Conversacion c, string texto, string origen, OpcionesEnvio opciones) => EnviarAsync(c, texto, origen);
}

// Único punto por el que entra y sale un mensaje: primero lo guarda en
// MensajeChat (historial para cualquier canal) y después lo entrega por el
// canal activo, dejando registrado si se envió o dio error.
//
// Humano y Sistema (cerrar/reabrir) solo responden dentro de las 24 h del último entrante real.
// Los avisos de turno (Sistema + IniciaElNegocio) se rigen por consentimiento y tope diario.
// El asistente (Bot) conserva su regla: solo responde a una conversación iniciada por la persona.
public class ServicioMensajes : IServicioMensajes
{
    /// <summary>El código del error cuando se frena un mensaje automático porque la persona nunca escribió en la conversación.</summary>
    public const string CodigoSinConversacionIniciada = "sin_conversacion_iniciada";
    public const string CodigoVentanaCerrada = "ventana_24h_cerrada";

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

    public MensajeChat? RegistrarEntrante(Conversacion c, string texto, DatosDelEntrante datos)
    {
        var m = new MensajeChat
        {
            IdConversacion = c.IdConversacion,
            Direccion      = "Entrante",
            Origen         = "Paciente",
            Texto          = Recortar(texto),
            // La hora real del mensaje, si es razonable (DatosDelEntrante); si no, la de ahora.
            FechaHora      = datos.FechaHora ?? _reloj.GetLocalNow().DateTime,
            EstadoEnvio    = "Enviado",
            Tipo           = datos.Tipo,
            IdExterno      = datos.IdExterno
        };
        return _mensajes.InsertarEntrante(m) is null ? null : m;
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

        // Antes de insertar: un rechazo no deja una fila fantasma. Humano nunca puede
        // saltarse la ventana, aunque alguien le pase IniciaElNegocio por error.
        if (origen == "Humano" || (origen == "Sistema" && !opciones.IniciaElNegocio))
        {
            var ventana = new VentanaDeConversacion(_reloj).Evaluar(_mensajes.UltimoEntrante(c.IdConversacion));
            if (!ventana.Abierta)
            {
                _log.LogInformation("No se manda el mensaje ({Origen}) de la conversación {IdConversacion}: ventana de 24 h cerrada ({Motivo}).",
                    origen, c.IdConversacion, ventana.Motivo);
                m.EstadoEnvio = "Error";
                m.CodigoDeError = CodigoVentanaCerrada;
                m.VentanaRechazada = ventana;
                return m;
            }
        }

        if (origen == "Bot" && !opciones.IniciaElNegocio && !_mensajes.TieneMensajeEntrante(c.IdConversacion))
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

    private static string Recortar(string texto) =>
        texto.Length > 4000 ? texto[..4000] : texto;
}
