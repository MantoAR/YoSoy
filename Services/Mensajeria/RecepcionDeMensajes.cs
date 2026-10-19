using Microsoft.Extensions.Options;

/// <summary>
/// Por donde entra lo que la gente le escribe a la clínica (el webhook de whatsapp-service y el simulador): elige quién lo atiende según Mensajeria:ModoAtencion, que se
/// puede cambiar sin reiniciar.
///  · "Asistente" (por defecto): BotReservasService, el asistente de reservas de siempre.
///  · "Personal": AtencionPersonalService, el chat para el personal (el asistente no contesta).
/// </summary>
public class RecepcionDeMensajes
{
    private readonly BotReservasService _asistente;
    private readonly AtencionPersonalService _personal;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;

    public RecepcionDeMensajes(BotReservasService asistente, AtencionPersonalService personal, IOptionsMonitor<MensajeriaOptions> opciones)
    {
        _asistente = asistente;
        _personal  = personal;
        _opciones  = opciones;
    }

    public Task<RespuestaBot> ProcesarAsync(string telefono, string texto, string canal) =>
        _opciones.CurrentValue.EsModoPersonal
            ? _personal.ProcesarAsync(telefono, texto, canal)
            : _asistente.ProcesarAsync(telefono, texto, canal);
}
