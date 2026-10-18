using System.Net.Http.Json;
using System.Text.Json.Serialization;

// Canal real: llama a whatsapp-service (Node) por HTTP, solo en loopback (ver whatsapp-service/README.md). Sirve igual para los
// dos backends del servicio (whatsapp-web.js y Baileys): la interfaz HTTP es la misma, así que Mensajeria:CanalActivo acepta
// "WhatsAppWebJs" y "Baileys" y registra esta misma clase (el nombre quedó por ser el primero que existió). Cualquier falla — timeout, 5xx, conexión
// rechazada, o un 503 porque la cola de whatsapp-service está llena — se traduce acá en
// ResultadoEnvio(false, ...), nunca una excepción sin capturar: el resto de la cadena de
// manejo de errores (ServicioMensajes, NotificadorTurnos.Seguro, MaxIntentosEnvio) ya la
// contiene, no hace falta duplicar nada de eso en este canal.
//
// Exito:true en la respuesta de whatsapp-service significa "aceptado en su cola de
// envío", no "entregado" — mismo criterio laxo que ya usa CanalSimulado (tampoco
// confirma que un humano lo leyó).
public class CanalWhatsAppWebJs : IMessageChannel
{
    private readonly HttpClient _http;
    private readonly ILogger<CanalWhatsAppWebJs> _log;

    public CanalWhatsAppWebJs(HttpClient http, ILogger<CanalWhatsAppWebJs> log)
    {
        _http = http;
        _log  = log;
    }

    public string Nombre => "WhatsApp";

    public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto) =>
        EnviarAsync(telefonoDestino, texto, OpcionesEnvio.Ninguna);

    public async Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto, OpcionesEnvio opciones)
    {
        try
        {
            // "optIn": true viaja SOLO cuando el paciente tiene una aceptación vigente (ConsentimientoWhatsApp): whatsapp-service cuenta únicamente
            // el valor true exacto y, sin él, decide por su cuenta (le manda solo a quien le escribió primero al número de la clínica).
            // Nunca se manda optIn:false: la ausencia es lo mismo y no deja dudas de que "no" no es una constancia.
            var respuesta = opciones.OptIn
                ? await _http.PostAsJsonAsync("enviar", new { telefono = telefonoDestino, texto, optIn = true })
                : await _http.PostAsJsonAsync("enviar", new { telefono = telefonoDestino, texto });
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaEnviar>();

            if (!respuesta.IsSuccessStatusCode || cuerpo is null || !cuerpo.Exito)
            {
                var error = cuerpo?.Error ?? $"whatsapp-service respondió {(int)respuesta.StatusCode}.";
                _log.LogWarning("whatsapp-service no aceptó el envío: {Error}", error);
                return new ResultadoEnvio(false, Error: error, Codigo: cuerpo?.Codigo);
            }

            return new ResultadoEnvio(true, IdExterno: cuerpo.IdExterno);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(ex, "No se pudo contactar a whatsapp-service.");
            return new ResultadoEnvio(false, Error: "No se pudo contactar al servicio de WhatsApp.");
        }
    }

    // JsonPropertyName explícito en todos: el deserializador por default de
    // System.Text.Json es sensible a mayúsculas (comprobado a mano) — sin esto,
    // "exito"/"idExterno" en minúscula de whatsapp-service nunca pisan estas
    // propiedades en PascalCase y EnviarAsync trataría cualquier envío exitoso
    // como un fallo en silencio. "codigo" solo viene cuando whatsapp-service rechaza el pedido
    // (sin_consentimiento, baja, telefono_invalido, tope_diario...).
    private record RespuestaEnviar(
        [property: JsonPropertyName("exito")] bool Exito,
        [property: JsonPropertyName("idExterno")] string? IdExterno,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("codigo")] string? Codigo = null);
}
