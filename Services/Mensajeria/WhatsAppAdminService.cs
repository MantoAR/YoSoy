using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

// Cliente admin hacia whatsapp-service: estado (con el QR adentro), pedir un código QR y desvincular, para la
// pantalla de WhatsApp del Configurador. Deliberadamente NO es CanalBaileys (que
// implementa IMessageChannel y solo se registra cuando Mensajeria:CanalActivo ya es
// "Baileys", ver MensajeriaServiceCollectionExtensions) — esta pantalla tiene que
// funcionar incluso con el canal todavía en "Simulado" (el default), porque su propósito es
// justamente vincular el número ANTES de activar ese canal. Se registra sin condición.
//
// Cualquier falla de red (timeout, conexión rechazada) se traduce en EstadoWhatsApp.NoDisponible,
// false o null, nunca una excepción sin capturar — mismo criterio que CanalBaileys.
public class WhatsAppAdminService
{
    private readonly HttpClient _http;
    private readonly ILogger<WhatsAppAdminService> _log;

    public WhatsAppAdminService(HttpClient http, ILogger<WhatsAppAdminService> log)
    {
        _http = http;
        _log = log;
    }

    /// <summary>
    /// El estado que informa whatsapp-service (GET /estado, con secreto): conectado, número, QR y el par estado/motivo con el que la pantalla
    /// distingue "conectando" de "hay que vincular". Con <paramref name="conDesde"/> (por defecto) pide además GET /salud (sin secreto, solo
    /// loopback) para saber desde cuándo está en ese estado y cuántos intentos de reconexión lleva; si /salud falla, esos dos datos quedan en null.
    /// </summary>
    public async Task<EstadoWhatsApp> ObtenerEstadoAsync(bool conDesde = true)
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

            DateTimeOffset? desde = null;
            int? intentos = null;
            if (conDesde)
            {
                var salud = await LeerSaludAsync();
                desde = salud?.Desde;
                intentos = salud?.IntentosDeReconexion;
            }
            return new EstadoWhatsApp(true, cuerpo.Conectado, cuerpo.Numero, cuerpo.QrPendiente, cuerpo.Qr, cuerpo.Estado, cuerpo.Motivo, desde, intentos);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogError(ex, "No se pudo contactar a whatsapp-service.");
            return EstadoWhatsApp.NoDisponible;
        }
    }

    // /salud responde 503 cuando no está conectado, con el mismo cuerpo: se lee igual. Cualquier falla acá no tumba el estado.
    private async Task<RespuestaSalud?> LeerSaludAsync()
    {
        try
        {
            using var respuesta = await _http.GetAsync("salud");
            return await respuesta.Content.ReadFromJsonAsync<RespuestaSalud>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            _log.LogWarning("whatsapp-service /salud no se pudo leer ({Tipo}).", ex.GetType().Name);
            return null;
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

    /// <summary>
    /// Pide una ventana de vinculación por QR (POST /vincular de whatsapp-service): idempotente de su lado (si ya hay un QR esperando, no abre otra).
    /// Devuelve null si no se pudo contactar al servicio; si el servicio contestó que no corresponde (409), el resultado trae Exito en false y el motivo.
    /// </summary>
    public async Task<ResultadoVinculacion?> VincularAsync()
    {
        try
        {
            using var respuesta = await _http.PostAsync("vincular", null);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaVincular>();
            if (cuerpo is null) return null;
            return new ResultadoVinculacion(cuerpo.Exito && respuesta.IsSuccessStatusCode, cuerpo.YaAbierta, cuerpo.Codigo, cuerpo.Error, cuerpo.Estado, cuerpo.Motivo, cuerpo.QrPendiente);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            _log.LogError(ex, "No se pudo contactar a whatsapp-service para pedir un código QR.");
            return null;
        }
    }

    // JsonPropertyName explícito: el deserializador por default de System.Text.Json es sensible a mayúsculas (ver CanalBaileys) — sin esto,
    // los nombres en minúscula de whatsapp-service no pisarían estas propiedades en PascalCase.
    private record RespuestaEstado(
        [property: JsonPropertyName("conectado")] bool Conectado,
        [property: JsonPropertyName("numero")] string? Numero,
        [property: JsonPropertyName("qrPendiente")] bool QrPendiente,
        [property: JsonPropertyName("qr")] string? Qr,
        [property: JsonPropertyName("estado")] string? Estado = null,
        [property: JsonPropertyName("motivo")] string? Motivo = null);

    private record RespuestaSalud(
        [property: JsonPropertyName("desde")] DateTimeOffset? Desde = null,
        [property: JsonPropertyName("intentosDeReconexion")] int? IntentosDeReconexion = null);

    private record RespuestaVincular(
        [property: JsonPropertyName("exito")] bool Exito,
        [property: JsonPropertyName("yaAbierta")] bool YaAbierta = false,
        [property: JsonPropertyName("codigo")] string? Codigo = null,
        [property: JsonPropertyName("error")] string? Error = null,
        [property: JsonPropertyName("estado")] string? Estado = null,
        [property: JsonPropertyName("motivo")] string? Motivo = null,
        [property: JsonPropertyName("qrPendiente")] bool QrPendiente = false);
}

// ServicioDisponible:false cuando no se pudo contactar a whatsapp-service en absoluto —
// distinto de Conectado:false (el servicio respondió, pero WhatsApp no está vinculado).
// Estado/Motivo son los del servicio (detenido, iniciando, conectado, reconectando, requiere_vincular, requiere_atencion; motivo: esperando_escaneo,
// qr_vencido, sesion_cerrada...): con ellos la pantalla distingue "Conectando…" de "hay que vincular". Desde: desde cuándo está en ese estado.
public record EstadoWhatsApp(bool ServicioDisponible, bool Conectado, string? Numero, bool QrPendiente, string? Qr,
    string? Estado = null, string? Motivo = null, DateTimeOffset? Desde = null, int? IntentosDeReconexion = null)
{
    public static readonly EstadoWhatsApp NoDisponible = new(false, false, null, false, null);
}

public record ResultadoVinculacion(bool Exito, bool YaAbierta, string? Codigo, string? Error, string? Estado, string? Motivo, bool QrPendiente);

/// <summary>
/// Enfriamiento de los pedidos de código QR nuevo: no se abre otra ventana de vinculación antes de <see cref="Segundos"/> desde la anterior (aunque
/// el pedido venga de otra pestaña u otro Configurador). Pedir QR en bucle contra WhatsApp es lo que puede terminar en un bloqueo del número.
/// </summary>
public class EnfriamientoQr
{
    public const int Segundos = 30;
    private readonly TimeProvider _reloj;
    private readonly object _candado = new();
    private DateTimeOffset? _ultimo;

    public EnfriamientoQr(TimeProvider reloj) => _reloj = reloj;

    /// <summary>Cuántos segundos faltan para poder pedir otro (0 si ya se puede).</summary>
    public int SegundosRestantes()
    {
        lock (_candado)
        {
            if (_ultimo is null) return 0;
            var falta = _ultimo.Value.AddSeconds(Segundos) - _reloj.GetUtcNow();
            return falta <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(falta.TotalSeconds);
        }
    }

    public void Registrar()
    {
        lock (_candado) _ultimo = _reloj.GetUtcNow();
    }
}
