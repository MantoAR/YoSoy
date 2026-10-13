using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

// Servidores ICE para el RTCPeerConnection: STUN siempre y, si está configurado, TURN con credenciales
// efímeras al estilo "TURN REST API" de coturn (use-auth-secret):
//     username   = "{vencimientoUnix}:{sub}"
//     credential = Base64( HMAC-SHA1( secretoTurn, username ) )
// coturn las valida con el mismo secreto (static-auth-secret) sin ninguna base de datos. Las credenciales
// nunca se loguean ni se cachean.
public class TurnCredentialsService
{
    // Lo que usan hoy las pantallas. Con coturn propio se pisa con Video__Ice__StunUrls__0 y listo.
    public static readonly string[] StunPorDefecto = { "stun:stun.l.google.com:19302" };

    private readonly IOptionsMonitor<VideoOptions> _opciones;
    private readonly IConfiguration _config;
    private readonly ILogger<TurnCredentialsService> _log;

    public TurnCredentialsService(IOptionsMonitor<VideoOptions> opciones, IConfiguration config, ILogger<TurnCredentialsService> log)
    {
        _opciones = opciones; _config = config; _log = log;
    }

    /// <summary>
    /// Servidores ICE para este usuario. El vencimiento de las credenciales TURN es el MAYOR entre el exp del token
    /// de sala y ahora + TTL, y el TTL nunca es menor que la ventana de la sesión: así la relay no se corta antes que
    /// la señalización ni antes de que termine la sesión, aunque la llamada siga después de que el token venció.
    /// </summary>
    public ConfiguracionIce Generar(string sub, DateTimeOffset ahora, DateTimeOffset expiraToken, TimeSpan ventanaDeLaSesion)
    {
        var o = _opciones.CurrentValue;
        var stun = Limpias(o.Ice.StunUrls);
        var servidores = new List<IceServer> { new(stun.Length > 0 ? stun : StunPorDefecto) };

        var turn = Limpias(o.Ice.TurnUrls);
        if (turn.Length == 0) return new ConfiguracionIce(servidores, false);

        if (VideoSecretos.Problema("Video__TurnSecreto", o.TurnSecreto, _config["Jwt:Key"]) is { } problema)
        {
            // La respuesta sigue siendo válida (solo STUN); el motivo, para quien administra, va al log.
            _log.LogWarning("Video: no se entregan credenciales TURN. {Motivo}", problema);
            return new ConfiguracionIce(servidores, false);
        }

        var ttl = TimeSpan.FromMinutes(AccesoSalaService.Acotar(o.Turn.TtlMinutos, 1, 24 * 60));
        if (ttl < ventanaDeLaSesion) ttl = ventanaDeLaSesion;
        var vence = ahora + ttl;
        if (vence < expiraToken) vence = expiraToken;

        var username = $"{vence.ToUnixTimeSeconds()}:{sub}";
        servidores.Add(new IceServer(turn, username, Credencial(o.TurnSecreto!, username)));
        return new ConfiguracionIce(servidores, true);
    }

    /// <summary>Base64(HMAC-SHA1(secreto, username)), con el secreto como UTF-8 (lo que hace coturn con la cadena).</summary>
    public static string Credencial(string secreto, string username)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secreto));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
    }

    private static string[] Limpias(IEnumerable<string>? urls) =>
        (urls ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToArray();
}

/// <summary>Un elemento de iceServers del RTCPeerConnection. Los null se omiten: un username null llegaría al navegador como "null".</summary>
public record IceServer(
    string[] Urls,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Username = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Credential = null);

public record ConfiguracionIce(IReadOnlyList<IceServer> Servidores, bool TurnDisponible);
