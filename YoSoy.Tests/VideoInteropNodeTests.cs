using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Interoperabilidad con el signaling REAL: un token emitido por la API (.NET) lo tiene que aceptar
// signaling-server/token.js (verificarToken, el mismo código que usa server.js en el handshake) y lo tiene que
// rechazar en los casos que dice signaling-server/README.md. Se corre con el node de la máquina, en la carpeta del
// signaling, con el token y el secreto por variables de entorno (no por argumentos ni por archivos).
//
// Si no hay node, o no están instaladas las dependencias del signaling (npm ci en signaling-server/), estos tests
// quedan OMITIDOS (no pasan en falso) y el contrato sigue cubierto por TokenSalaServiceTests, que verifica el
// formato con las reglas del README sin ninguna librería. Lo único que no cubre esa versión es que jsonwebtoken lo
// acepte de verdad, que es lo que hace esta clase.
public sealed class FactConNodeAttribute : FactAttribute
{
    public FactConNodeAttribute()
    {
        var motivo = NodeSignaling.NoDisponible();
        if (motivo is not null) Skip = motivo;
    }
}

public sealed class TheoryConNodeAttribute : TheoryAttribute
{
    public TheoryConNodeAttribute()
    {
        var motivo = NodeSignaling.NoDisponible();
        if (motivo is not null) Skip = motivo;
    }
}

public static class NodeSignaling
{
    public static readonly string Carpeta = Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "signaling-server");
    private static readonly Lazy<string?> Motivo = new(CalcularMotivo);

    /// <summary>null si se puede verificar con node; si no, por qué se omiten los tests.</summary>
    public static string? NoDisponible() => Motivo.Value;

    private static string? CalcularMotivo()
    {
        try
        {
            if (!File.Exists(Path.Combine(Carpeta, "token.js"))) return "No está signaling-server/token.js.";
            if (!Directory.Exists(Path.Combine(Carpeta, "node_modules", "jsonwebtoken")))
                return "Faltan las dependencias del signaling: correr npm ci en signaling-server/.";
            var (codigo, _, _) = Ejecutar(new[] { "--version" }, new Dictionary<string, string>(), 15000);
            return codigo == 0 ? null : "node respondió con error al pedirle la versión.";
        }
        catch (Exception e)
        {
            return $"node no está disponible en esta máquina ({e.GetType().Name}).";
        }
    }

    public static (int Codigo, string Salida, string Error) Ejecutar(string[] args, Dictionary<string, string> entorno, int milisegundos = 30000)
    {
        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = Carpeta,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (clave, valor) in entorno) psi.Environment[clave] = valor;

        using var proceso = Process.Start(psi)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var error = proceso.StandardError.ReadToEndAsync();
        if (!proceso.WaitForExit(milisegundos))
        {
            try { proceso.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("node no terminó a tiempo.");
        }
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, error.Result);
    }

    // Lo mismo que hace server.js en el handshake: verificarToken(token, secreto). Imprime solo el resultado.
    private const string Script = """
        const { verificarToken } = require('./token');
        try {
            const r = verificarToken(process.env.TOKEN_SALA, process.env.SECRETO_SALA);
            console.log(JSON.stringify({ ok: true, ...r }));
        } catch (e) {
            console.log(JSON.stringify({ ok: false, codigo: e.codigo || null, nombre: e.name }));
        }
        """;

    /// <summary>{ ok, sala, rol, sub, exp } si el signaling acepta el token; { ok:false, codigo, nombre } si no.</summary>
    public static JsonElement Verificar(string token, string secreto)
    {
        var (codigo, salida, error) = Ejecutar(new[] { "-e", Script },
            new Dictionary<string, string> { ["TOKEN_SALA"] = token, ["SECRETO_SALA"] = secreto });
        Assert.True(codigo == 0, "node falló al verificar el token: " + error);
        using var doc = JsonDocument.Parse(salida.Trim());
        return doc.RootElement.Clone();
    }
}

public class VideoInteropNodeTests
{
    private const string JwtKey = "clave-de-login-de-la-api-para-las-pruebas-0123";

    private static TokenSalaService Servicio(string secreto = VideoApp.SecretoSenal)
    {
        var opciones = new OpcionesFijas<VideoOptions>();
        opciones.CurrentValue.SignalingSecreto = secreto;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = JwtKey }).Build();
        return new TokenSalaService(opciones, config);
    }

    // El camino completo: login → POST /video/acceso → token → signaling real.
    [FactConNode]
    public async Task Un_token_emitido_por_la_api_lo_acepta_el_signaling_real_con_los_dos_roles_de_la_sala()
    {
        using var app = new VideoApp();

        foreach (var (cliente, rolEsperado, sub) in new[]
        {
            (app.Paciente(), "paciente", "31"),
            (app.Profesional(), "profesional", "21")
        })
        {
            var r = await VideoApp.Acceder(cliente, "turno", VideoApp.IdTurno);
            var j = await VideoApp.Json(r);
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);

            var v = NodeSignaling.Verificar(j.GetProperty("token").GetString()!, VideoApp.SecretoSenal);

            Assert.True(v.GetProperty("ok").GetBoolean(), "El signaling rechazó un token de la API: " + v.GetRawText());
            Assert.Equal(VideoApp.SalaDelTurno, v.GetProperty("sala").GetString());
            Assert.Equal(rolEsperado, v.GetProperty("rol").GetString());
            Assert.Equal(sub, v.GetProperty("sub").GetString());
            // El exp que ve el signaling (y con el que corta el socket) es el que la API devuelve como expiraUtc.
            Assert.Equal(j.GetProperty("expiraUtc").GetDateTimeOffset().ToUnixTimeSeconds(), v.GetProperty("exp").GetInt64());
        }
    }

    [FactConNode]
    public async Task Un_token_de_una_admision_tambien_lo_acepta_el_signaling_real()
    {
        using var app = new VideoApp();
        var j = await VideoApp.Json(await VideoApp.Acceder(app.Admision(), "admision", VideoApp.IdInforme));

        var v = NodeSignaling.Verificar(j.GetProperty("token").GetString()!, VideoApp.SecretoSenal);

        Assert.True(v.GetProperty("ok").GetBoolean(), v.GetRawText());
        Assert.Equal(VideoApp.SalaDeLaAdmision, v.GetProperty("sala").GetString());
        Assert.Equal("profesional", v.GetProperty("rol").GetString());
    }

    // El contrato admite estos juegos de caracteres para sala y sub (RE_SALA, RE_SUB de token.js) y un exp numérico.
    [TheoryConNode]
    [InlineData("a", Roles.Paciente, "1")]
    [InlineData("Turno_1.2:3-4", Roles.Profesional, "2147483647")]
    [InlineData("turno-1-0123456789-abcdefghij", Roles.Paciente, "user.name:42")]
    public void El_signaling_acepta_los_juegos_de_caracteres_del_contrato(string sala, string rol, string sub)
    {
        var token = Servicio().Emitir(sala, rol, sub, DateTimeOffset.UtcNow.AddMinutes(30));

        var v = NodeSignaling.Verificar(token, VideoApp.SecretoSenal);

        Assert.True(v.GetProperty("ok").GetBoolean(), v.GetRawText());
        Assert.Equal(sala, v.GetProperty("sala").GetString());
        Assert.Equal(rol, v.GetProperty("rol").GetString());
        Assert.Equal(sub, v.GetProperty("sub").GetString());
    }

    [FactConNode]
    public void El_signaling_acepta_una_sala_de_100_caracteres_el_limite_del_contrato()
    {
        var sala = new string('s', 100);
        var token = Servicio().Emitir(sala, Roles.Paciente, "1", DateTimeOffset.UtcNow.AddMinutes(30));

        var v = NodeSignaling.Verificar(token, VideoApp.SecretoSenal);

        Assert.True(v.GetProperty("ok").GetBoolean(), v.GetRawText());
    }

    // ---------- Lo que el signaling tiene que rechazar ----------

    [FactConNode]
    public void El_signaling_rechaza_un_token_firmado_con_otro_secreto()
    {
        var token = Servicio().Emitir("sala-1", Roles.Paciente, "1", DateTimeOffset.UtcNow.AddMinutes(30));

        // Mismo largo y mismos acentos salvo un carácter: tiene que ser otra firma.
        var otro = VideoApp.SecretoSenal.Remove(VideoApp.SecretoSenal.Length - 1) + "X";
        var v = NodeSignaling.Verificar(token, otro);

        Assert.False(v.GetProperty("ok").GetBoolean());
        Assert.Equal("token-invalido", v.GetProperty("codigo").GetString());
    }

    [FactConNode]
    public void El_signaling_rechaza_un_token_ya_vencido_con_un_codigo_propio()
    {
        var token = Servicio().Emitir("sala-1", Roles.Paciente, "1", DateTimeOffset.UtcNow.AddMinutes(-5));

        var v = NodeSignaling.Verificar(token, VideoApp.SecretoSenal);

        Assert.False(v.GetProperty("ok").GetBoolean());
        Assert.Equal("token-vencido", v.GetProperty("codigo").GetString());
    }

    // Un login de la API (firmado con Jwt:Key, sin sala ni rol de sala) no sirve para entrar a una sala, ni con el
    // secreto de la señalización ni —aunque los secretos fueran el mismo— por sus claims.
    [FactConNode]
    public void El_signaling_rechaza_un_jwt_de_login_de_la_api()
    {
        using var app = new VideoApp();
        var login = app.Factory.Services.GetRequiredService<JwtEmisorService>()
            .Emitir(VideoApp.UsuarioPaciente, Roles.Paciente, idPaciente: VideoApp.IdPaciente);

        var conSecretoDeSenalizacion = NodeSignaling.Verificar(login, VideoApp.SecretoSenal);
        var conJwtKey = NodeSignaling.Verificar(login, app.JwtKey);

        Assert.False(conSecretoDeSenalizacion.GetProperty("ok").GetBoolean());
        Assert.Equal("token-invalido", conSecretoDeSenalizacion.GetProperty("codigo").GetString());
        Assert.False(conJwtKey.GetProperty("ok").GetBoolean());
        Assert.Equal("claims-invalidos", conJwtKey.GetProperty("codigo").GetString());
    }

    // Al revés: el token de sala, ni con la clave de login se acepta como sesión (ver VideoAccesoEndpointsTests); y
    // el signaling tampoco acepta un rol que no sea de los dos asientos.
    [FactConNode]
    public void El_signaling_rechaza_un_rol_que_no_es_de_los_dos_asientos()
    {
        var token = Servicio().Emitir("sala-1", Roles.Configurador, "1", DateTimeOffset.UtcNow.AddMinutes(30));

        var v = NodeSignaling.Verificar(token, VideoApp.SecretoSenal);

        Assert.False(v.GetProperty("ok").GetBoolean());
        Assert.Equal("claims-invalidos", v.GetProperty("codigo").GetString());
    }
}
