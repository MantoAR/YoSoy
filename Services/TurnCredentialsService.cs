using System.Security.Cryptography;
using System.Text;

public class TurnCredentialsService
{
    private readonly bool _habilitado;
    private readonly string _secret;
    private readonly string[] _urls;
    private readonly int _ttl;

    public TurnCredentialsService(IConfiguration config)
    {
        var t = config.GetSection("Turn");
        _habilitado = bool.TryParse(t["Habilitado"], out var h) && h;
        _secret = t["StaticAuthSecret"] ?? "";
        _urls = t.GetSection("Urls").Get<string[]>() ?? Array.Empty<string>();
        _ttl = int.TryParse(t["TtlSegundos"], out var ttl) ? ttl : 600;
    }

    public IceServer? GenerarIceServer(int idUsuario)
    {
        if (!_habilitado || _urls.Length == 0 || string.IsNullOrEmpty(_secret))
            return null;

        var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _ttl;
        var username = $"{expiry}:{idUsuario}";

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_secret));
        var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));

        return new IceServer { urls = _urls, username = username, credential = credential };
    }
}

public class IceServer
{
    public string[] urls { get; set; } = Array.Empty<string>();
    public string username { get; set; } = "";
    public string credential { get; set; } = "";
}
