using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Piezas compartidas por los tests de video (PR 3: POST /video/acceso, token de sala y credenciales ICE).

// Turnos e informes de admisión en memoria: las reglas (pertenencia, estado, ventana) se prueban sin base.
public class FakeOrigenSala : IOrigenSalaRepositorio
{
    public readonly Dictionary<int, TurnoParaSala> Turnos = new();
    public readonly Dictionary<int, AdmisionParaSala> Admisiones = new();

    public TurnoParaSala? ObtenerTurno(int idTurno) => Turnos.GetValueOrDefault(idTurno);
    public AdmisionParaSala? ObtenerAdmision(int idInforme) => Admisiones.GetValueOrDefault(idInforme);
}

/// <summary>
/// La aceptación de la videollamada en memoria. Por defecto NO se exige (Exigida = false: como una base sin la migración 28), así que las pruebas de la sala de antes siguen igual;
/// las que prueban la aceptación la prenden con Exigida = true.
/// </summary>
public sealed class FakeAceptacionVideo : IAceptacionVideollamadaRepositorio
{
    public bool Exigida;
    public readonly List<AceptacionVideoFila> Filas = new();
    private int _siguiente = 1;

    public bool Disponible() => Exigida;

    public AceptacionVideoFila? Ultima(string origen, int idOrigen, int idPaciente)
    {
        lock (Filas) return Filas.Where(f => f.Origen == origen && f.IdOrigen == idOrigen && f.IdPaciente == idPaciente).OrderByDescending(f => f.IdAceptacion).FirstOrDefault();
    }

    public AceptacionVideoFila Insertar(int idUsuario, int idPaciente, string origen, int idOrigen, string versionTexto, string hashTexto, DateTime decididoUtc)
    {
        lock (Filas)
        {
            var f = new AceptacionVideoFila(_siguiente++, idUsuario, idPaciente, origen, idOrigen, versionTexto, decididoUtc, null);
            Filas.Add(f);
            return f;
        }
    }

    public IReadOnlyList<AceptacionVideoFila> VigentesDelPaciente(int idPaciente, DateTime desdeUtc)
    {
        lock (Filas) return Filas.Where(f => f.IdPaciente == idPaciente && f.RevocadoUtc is null && f.DecididoUtc >= desdeUtc).OrderByDescending(f => f.DecididoUtc).ToList();
    }

    public bool Revocar(int idAceptacion, DateTime revocadoUtc, int idUsuario)
    {
        lock (Filas)
        {
            var i = Filas.FindIndex(f => f.IdAceptacion == idAceptacion && f.RevocadoUtc is null);
            if (i < 0) return false;
            Filas[i] = Filas[i] with { RevocadoUtc = revocadoUtc };
            return true;
        }
    }
}

public class FakeAuditoriaVideo : IAuditoriaVideo
{
    public record Registro(int IdUsuario, string Origen, int IdOrigen, string Rol, ResultadoAcceso Resultado);

    private readonly List<Registro> _registros = new();
    public List<Registro> Registros { get { lock (_registros) return _registros.ToList(); } }

    public void Registrar(int idUsuario, string origen, int idOrigen, string rol, ResultadoAcceso resultado)
    {
        lock (_registros) _registros.Add(new Registro(idUsuario, origen, idOrigen, rol, resultado));
    }
}

// Reloj fijo cuya zona "local" (la del servidor) no es UTC: para probar cómo se interpretan Fecha y Hora.
public sealed class RelojConZona : RelojFijo
{
    private readonly TimeZoneInfo _zona;
    public RelojConZona(DateTime ahoraUtc, TimeZoneInfo zona) : base(ahoraUtc) => _zona = zona;
    public override TimeZoneInfo LocalTimeZone => _zona;
}

// Recoge todo lo que se loguea, para comprobar qué queda en los logs (y, sobre todo, qué NO queda).
public sealed class LogsCapturados : ILoggerProvider
{
    private readonly List<string> _lineas = new();
    public List<string> Lineas { get { lock (_lineas) return _lineas.ToList(); } }
    public string Todo => string.Join("\n", Lineas);

    public ILogger CreateLogger(string categoryName) => new Canal(this, categoryName);
    public ILogger<T> CreateLogger<T>() => new Canal<T>(this, typeof(T).FullName!);
    public void Dispose() { }

    private void Agregar(string linea) { lock (_lineas) _lineas.Add(linea); }

    private class Canal : ILogger
    {
        private readonly LogsCapturados _dueno;
        private readonly string _categoria;
        public Canal(LogsCapturados dueno, string categoria) { _dueno = dueno; _categoria = categoria; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _dueno.Agregar($"{logLevel}|{_categoria}|{formatter(state, exception)}{(exception is null ? "" : "|" + exception)}");
    }

    private sealed class Canal<T> : Canal, ILogger<T>
    {
        public Canal(LogsCapturados dueno, string categoria) : base(dueno, categoria) { }
    }
}

// La aplicación real (WebApplicationFactory) con lo justo reemplazado: el repositorio de turnos y la auditoría
// por dobles en memoria, y AccesoSalaService con un reloj fijo. NO se reemplaza el TimeProvider global: lo usa
// también el recordatorio de turnos, que con una fecha de 2031 no tiene nada que hacer pero no es de este test.
public sealed class VideoApp : IDisposable
{
    // Con acentos a propósito: el secreto se usa como UTF-8 de punta a punta.
    public const string SecretoSenal = "señalización-secreta-de-prueba-0123456789";
    public const string SecretoTurn = "turn-secreto-de-prueba-ABCDEFGHIJKLMNOP-0123";

    public const int IdTurno = 1, IdInforme = 5;
    public const string SalaDelTurno = "turno-1-sala-de-prueba";
    public const string SalaDeLaAdmision = "admision-5-sala-de-prueba";

    // Personas del escenario. El JWT de la API lleva el rol y los ids; el servidor no consulta nada más.
    public const int UsuarioPaciente = 31, IdPaciente = 301;                  // paciente del turno 1 y del informe 5
    public const int UsuarioOtroPaciente = 32, IdOtroPaciente = 302;
    public const int UsuarioProfesional = 21, IdProfesional = 201;            // profesional del turno 1
    public const int UsuarioOtroProfesional = 22, IdOtroProfesional = 202;
    public const int UsuarioConfigurador = 11;
    // Etapa 2 de roles: quien hace la entrevista de admisión es un profesional con el privilegio Admisión (antes, el Configurador).
    public const int UsuarioAdmision = 23, IdAdmision = 203;

    // Martes 4/3/2031 10:00 (UTC, la zona del reloj de prueba): el turno de referencia empieza justo "ahora".
    // La fecha es lejana a propósito: nada de la base real puede coincidir.
    public static readonly DateTime Inicio = new(2031, 3, 4, 10, 0, 0);

    public readonly FakeOrigenSala Origen = new();
    public readonly FakeAuditoriaVideo Auditoria = new();
    public readonly FakeAceptacionVideo Aceptaciones = new();
    public readonly LogsCapturados Logs = new();
    public readonly RelojFijo Reloj;
    public readonly WebApplicationFactory<Program> Factory;

    public VideoApp(Dictionary<string, string?>? config = null, RelojFijo? reloj = null,
        bool repositorioReal = false, bool auditoriaReal = false, Action<IServiceCollection>? servicios = null)
    {
        Reloj = reloj ?? new RelojFijo(Inicio);
        Origen.Turnos[IdTurno] = new TurnoParaSala(IdTurno, IdPaciente, IdProfesional, "Confirmado",
            Inicio.Date, Inicio.TimeOfDay, SalaDelTurno, 30);
        Origen.Admisiones[IdInforme] = new AdmisionParaSala(IdInforme, IdPaciente, "En entrevista", Inicio, SalaDeLaAdmision);

        var ajustes = new Dictionary<string, string?>
        {
            ["Video:SignalingSecreto"] = SecretoSenal,
            ["Video:TurnSecreto"] = SecretoTurn,
            ["Video:Ice:StunUrls:0"] = "stun:stun.prueba.test:3478",
            ["Video:Ice:TurnUrls:0"] = "turn:turn.prueba.test:3478?transport=udp",
            ["Video:Ice:TurnUrls:1"] = "turn:turn.prueba.test:3478?transport=tcp",
            // Alto a propósito para que ningún test se tope con el límite sin querer. Los de rate limit lo bajan.
            ["Video:LimitePorMinuto"] = "1000"
        };
        if (config is not null) foreach (var (clave, valor) in config) ajustes[clave] = valor;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(ajustes));
            b.ConfigureLogging(l => l.AddProvider(Logs));
            b.ConfigureTestServices(s =>
            {
                if (!repositorioReal)
                {
                    s.RemoveAll<IOrigenSalaRepositorio>();
                    s.AddSingleton<IOrigenSalaRepositorio>(Origen);
                }
                if (!auditoriaReal)
                {
                    s.RemoveAll<IAuditoriaVideo>();
                    s.AddSingleton<IAuditoriaVideo>(Auditoria);
                }
                // La aceptación de la videollamada: en memoria y sin exigir (como una base sin la migración 28), salvo que la prueba la prenda.
                s.RemoveAll<IAceptacionVideollamadaRepositorio>();
                s.AddSingleton<IAceptacionVideollamadaRepositorio>(Aceptaciones);
                s.RemoveAll<AccesoSalaService>();
                s.AddSingleton(sp => new AccesoSalaService(sp.GetRequiredService<IOrigenSalaRepositorio>(), Reloj,
                    sp.GetRequiredService<IOptionsMonitor<VideoOptions>>(), sp.GetRequiredService<ILogger<AccesoSalaService>>()));
                servicios?.Invoke(s);
            });
        });
    }

    public static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    public string JwtKey => Factory.Services.GetRequiredService<IConfiguration>()["Jwt:Key"]!;

    public HttpClient SinSesion() => Factory.CreateClient();

    public HttpClient Cliente(int idUsuario, string rol, int? idPaciente = null, int? idProfesional = null) =>
        ConToken(Factory.Services.GetRequiredService<JwtEmisorService>().Emitir(idUsuario, rol, idPaciente, idProfesional));

    public HttpClient ConToken(string jwt)
    {
        var c = Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return c;
    }

    public HttpClient Paciente() => Cliente(UsuarioPaciente, Roles.Paciente, idPaciente: IdPaciente);
    public HttpClient OtroPaciente() => Cliente(UsuarioOtroPaciente, Roles.Paciente, idPaciente: IdOtroPaciente);
    public HttpClient Profesional() => Cliente(UsuarioProfesional, Roles.Profesional, idProfesional: IdProfesional);
    public HttpClient OtroProfesional() => Cliente(UsuarioOtroProfesional, Roles.Profesional, idProfesional: IdOtroProfesional);
    public HttpClient Configurador() => Cliente(UsuarioConfigurador, Roles.Configurador);
    public HttpClient Admision() => ConToken(Factory.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(UsuarioAdmision,
        new SesionModulo(Modulos.Profesional, new[] { Roles.Profesional }, new[] { Privilegios.Admision }, IdAdmision)));

    public static Task<HttpResponseMessage> Acceder(HttpClient cliente, string origen, int id) =>
        cliente.PostAsJsonAsync("/video/acceso", new { origen, id });

    /// <summary>El cuerpo como JSON; si la respuesta no trae cuerpo (un 401, por ejemplo), un JsonElement vacío (default).</summary>
    public static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(texto)) return default;
        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.Clone();
    }

    public void Dispose() => Factory.Dispose();
}

// Verifica un token de sala como lo exige signaling-server/README.md ("Token de sala"), pero SIN la librería que
// lo emitió: parte el JWT, comprueba el algoritmo del encabezado y recalcula la firma HMAC-SHA256 con el secreto
// en UTF-8. Si el formato del token cambiara, esto lo vería aunque una librería lo siguiera aceptando.
public static class TokenDeSala
{
    // Los mismos juegos de caracteres que RE_SALA y RE_SUB de signaling-server/token.js.
    public static readonly Regex ReSala = new("^[A-Za-z0-9_.:-]{1,100}$");
    public static readonly Regex ReSub = new("^[A-Za-z0-9_.:-]{1,64}$");
    public static readonly Regex ReFormaJwt = new(@"^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$");

    /// <summary>Devuelve los claims si el token está bien firmado con ese secreto; si no, falla el test.</summary>
    public static JsonElement Verificar(string token, string secreto)
    {
        Assert.Matches(ReFormaJwt, token);
        var partes = token.Split('.');

        using (var cabecera = JsonDocument.Parse(DecodificarBase64Url(partes[0])))
            Assert.Equal("HS256", cabecera.RootElement.GetProperty("alg").GetString());

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secreto));
        var firma = hmac.ComputeHash(Encoding.ASCII.GetBytes(partes[0] + "." + partes[1]));
        Assert.True(CryptographicOperations.FixedTimeEquals(firma, DecodificarBase64Url(partes[2])),
            "La firma del token no es HMAC-SHA256 del secreto en UTF-8.");

        using var payload = JsonDocument.Parse(DecodificarBase64Url(partes[1]));
        return payload.RootElement.Clone();
    }

    public static byte[] DecodificarBase64Url(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
    }

    public static DateTimeOffset Exp(JsonElement claims) => DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64());
}
