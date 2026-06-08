using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class JwtService
{
    private readonly byte[] _secret;
    private readonly string _issuer;

    public JwtService(IConfiguration config)
    {
        var jwt = config.GetSection("Jwt");
        var secreto = jwt["Secret"];
        if (string.IsNullOrWhiteSpace(secreto))
            throw new InvalidOperationException("Falta la configuración Jwt:Secret.");
        _secret = Encoding.UTF8.GetBytes(secreto);
        _issuer = jwt["Issuer"] ?? "yosoy";
    }

    public string GenerarLogin(int idUsuario, string rol, DateTime expiracion)
    {
        return Construir(new Dictionary<string, object>
        {
            ["iss"] = _issuer,
            ["idUsuario"] = idUsuario,
            ["rol"] = rol,
            ["tipo"] = "login",
            ["exp"] = new DateTimeOffset(expiracion).ToUnixTimeSeconds()
        });
    }

    public string GenerarSesion(int idUsuario, string rol, string roomId, int idTurno, int idSesion, DateTime expiracion)
    {
        return Construir(new Dictionary<string, object>
        {
            ["iss"] = _issuer,
            ["idUsuario"] = idUsuario,
            ["rol"] = rol,
            ["tipo"] = "sesion",
            ["roomId"] = roomId,
            ["idTurno"] = idTurno,
            ["idSesion"] = idSesion,
            ["exp"] = new DateTimeOffset(expiracion).ToUnixTimeSeconds()
        });
    }

    private string Construir(Dictionary<string, object> payload)
    {
        var header = new { alg = "HS256", typ = "JWT" };
        var h = B64(JsonSerializer.SerializeToUtf8Bytes(header));
        var p = B64(JsonSerializer.SerializeToUtf8Bytes(payload));
        var firma = B64(Firmar($"{h}.{p}"));
        return $"{h}.{p}.{firma}";
    }

    public Dictionary<string, JsonElement>? Validar(string token, int graciaSegundos = 0)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var partes = token.Split('.');
        if (partes.Length != 3) return null;

        var esperado = B64(Firmar($"{partes[0]}.{partes[1]}"));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(esperado), Encoding.UTF8.GetBytes(partes[2])))
            return null;

        Dictionary<string, JsonElement>? payload;
        try { payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(DeB64(partes[1])); }
        catch { return null; }
        if (payload == null) return null;

        if (payload.TryGetValue("exp", out var exp) &&
            exp.ValueKind == JsonValueKind.Number &&
            exp.GetInt64() + graciaSegundos < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return null;

        return payload;
    }

    private byte[] Firmar(string data)
    {
        using var hmac = new HMACSHA256(_secret);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string B64(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DeB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
