using Microsoft.Extensions.Options;

/// <summary>
/// Por donde entra lo que la gente le escribe a la clínica (el webhook de whatsapp-service y el simulador): elige quién lo atiende según Mensajeria:ModoAtencion, que se
/// puede cambiar sin reiniciar.
///  · "Asistente" (por defecto): BotReservasService, el asistente de reservas de siempre. Con lo que no es texto (una foto, un audio) no hace nada: es lo mismo que antes,
///    cuando whatsapp-service ni se lo reenviaba.
///  · "Personal": AtencionPersonalService, el chat para el personal (el asistente no contesta). Guarda también las fotos y los audios, como un aviso del tipo.
///
/// Además descarta, en los dos modos, un mensaje que ya se atendió (mismo teléfono e id de WhatsApp): whatsapp-service reintenta si la API falla, y a veces la API sí lo había
/// atendido pero la respuesta se perdió. Un pedido sin id (el simulador, o la versión anterior de whatsapp-service) no pasa por acá.
/// </summary>
public class RecepcionDeMensajes
{
    private readonly BotReservasService _asistente;
    private readonly AtencionPersonalService _personal;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly EntrantesVistos _vistos;

    public RecepcionDeMensajes(BotReservasService asistente, AtencionPersonalService personal, IOptionsMonitor<MensajeriaOptions> opciones, EntrantesVistos vistos)
    {
        _asistente = asistente;
        _personal  = personal;
        _opciones  = opciones;
        _vistos    = vistos;
    }

    public async Task<RespuestaBot> ProcesarAsync(string telefono, string texto, string canal, DatosDelEntrante? datos = null)
    {
        var id = datos?.IdExterno;
        if (id is not null && !_vistos.Reservar(telefono, id))
            return new RespuestaBot(0, new List<string>(), Ignorado: "duplicado");

        try
        {
            if (_opciones.CurrentValue.EsModoPersonal) return await _personal.ProcesarAsync(telefono, texto, canal, datos);

            if (datos is { EsTexto: false }) return new RespuestaBot(0, new List<string>(), Ignorado: "no_textual");
            return await _asistente.ProcesarAsync(telefono, texto, canal);
        }
        catch
        {
            // Si no se pudo atender (un error, o un pedido inválido), el reintento tiene que poder entrar.
            if (id is not null) _vistos.Liberar(telefono, id);
            throw;
        }
    }
}
