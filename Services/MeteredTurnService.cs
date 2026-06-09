using System.Text.Json;

public class MeteredTurnService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IceServer[]? _cache;
    private static DateTimeOffset _expira;

    private readonly string _apiKey;
    private readonly string _url;
    private readonly int _ttl;

    public MeteredTurnService(IConfiguration config)
    {
        var m = config.GetSection("Turn:Metered");
        _apiKey = !string.IsNullOrWhiteSpace(m["ApiKey"])
            ? m["ApiKey"]!
            : Environment.GetEnvironmentVariable("METERED_API_KEY") ?? "";
        _url = !string.IsNullOrWhiteSpace(m["Url"])
            ? m["Url"]!
            : Environment.GetEnvironmentVariable("METERED_TURN_URL") ?? "";
        _ttl = int.TryParse(m["CacheSegundos"], out var t) ? t : 3600;
    }

    public bool Configurado => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_url);

    public async Task<IceServer[]> ObtenerAsync()
    {
        if (!Configurado)
        {
            Console.WriteLine("TURN Metered deshabilitado: falta METERED_API_KEY o Turn:Metered:Url. La videollamada usa solo STUN hasta cargar la key.");
            return Array.Empty<IceServer>();
        }

        if (_cache != null && DateTimeOffset.UtcNow < _expira) return _cache;

        await Gate.WaitAsync();
        try
        {
            if (_cache != null && DateTimeOffset.UtcNow < _expira) return _cache;

            var separador = _url.Contains('?') ? "&" : "?";
            using var resp = await Http.GetAsync($"{_url}{separador}apiKey={Uri.EscapeDataString(_apiKey)}");
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"TURN Metered: el endpoint respondio {(int)resp.StatusCode}. Se sigue solo con STUN.");
                return _cache ?? Array.Empty<IceServer>();
            }

            var json = await resp.Content.ReadAsStringAsync();
            var crudos = JsonSerializer.Deserialize<MeteredServer[]>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<MeteredServer>();

            _cache = crudos
                .Where(s => !string.IsNullOrWhiteSpace(s.urls))
                .Select(s => new IceServer
                {
                    urls = new[] { s.urls! },
                    username = s.username ?? "",
                    credential = s.credential ?? ""
                })
                .ToArray();
            _expira = DateTimeOffset.UtcNow.AddSeconds(_ttl);
            Console.WriteLine($"TURN Metered: {_cache.Length} iceServers obtenidos del endpoint REST.");
            return _cache;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TURN Metered: error al obtener credenciales ({ex.Message}). Se sigue solo con STUN.");
            return _cache ?? Array.Empty<IceServer>();
        }
        finally
        {
            Gate.Release();
        }
    }

    private class MeteredServer
    {
        public string? urls { get; set; }
        public string? username { get; set; }
        public string? credential { get; set; }
    }
}
