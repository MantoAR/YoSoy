using System.Net.Http.Json;
using System.Text.Json.Serialization;

// Cliente admin hacia whatsapp-service: estado (con el QR adentro) y desvincular, para la
// pantalla de WhatsApp del Configurador. Deliberadamente NO es CanalWhatsAppWebJs (que
// implementa IMessageChannel y solo se registra cuando Mensajeria:CanalActivo ya es
// "WhatsAppWebJs", ver MensajeriaServiceCollectionExtensions) — esta pantalla tiene que
// funcionar incluso con el canal todavía en "Simulado" (el default), porque su propósito es
// justamente vincular el número ANTES de activar ese canal. Se registra sin condición.
//
// Cualquier falla de red (timeout, conexión rechazada) se traduce en EstadoWhatsApp.NoDisponible
// o false, nunca una excepción sin capturar — mismo criterio que CanalWhatsAppWebJs.
public class WhatsAppAdminService
{
    private readonly HttpClient _http;
    private readonly ILogger<WhatsAppAdminService> _log;

    public WhatsAppAdminService(HttpClient http, ILogger<WhatsAppAdminService> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<EstadoWhatsApp> ObtenerEstadoAsync()
    {
        try
        {
            var respuesta = await _http.GetAsync("estado");
            if (!respuesta.IsSuccessStatusCode)
            {
                _log.LogWarning("whatsapp-service /estado respondió {Status}.", (int)respuesta.StatusCode);
                return EstadoWhatsApp.NoDisponible;
            }
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaEstado>();
            if (cuerpo is null) return EstadoWhatsApp.NoDisponible;
            return new EstadoWhatsApp(true, cuerpo.Conectado, cuerpo.Numero, cuerpo.QrPendiente, cuerpo.Qr);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(ex, "No se pudo contactar a whatsapp-service.");
            return EstadoWhatsApp.NoDisponible;
        }
    }

    public async Task<bool> DesvincularAsync()
    {
        try
        {
            var respuesta = await _http.PostAsync("desvincular", null);
            return respuesta.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(ex, "No se pudo contactar a whatsapp-service para desvincular.");
            return false;
        }
    }

    // JsonPropertyName explícito en los cuatro: el deserializador por default de
    // System.Text.Json es sensible a mayúsculas (ver CanalWhatsAppWebJs) — sin esto, los
    // nombres en minúscula de whatsapp-service no pisarían estas propiedades en PascalCase.
    private record RespuestaEstado(
        [property: JsonPropertyName("conectado")] bool Conectado,
        [property: JsonPropertyName("numero")] string? Numero,
        [property: JsonPropertyName("qrPendiente")] bool QrPendiente,
        [property: JsonPropertyName("qr")] string? Qr);
}

// ServicioDisponible:false cuando no se pudo contactar a whatsapp-service en absoluto —
// distinto de Conectado:false (el servicio respondió, pero WhatsApp no está vinculado).
public record EstadoWhatsApp(bool ServicioDisponible, bool Conectado, string? Numero, bool QrPendiente, string? Qr)
{
    public static readonly EstadoWhatsApp NoDisponible = new(false, false, null, false, null);
}
