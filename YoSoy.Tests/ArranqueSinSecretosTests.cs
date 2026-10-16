using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// La aplicación con TODOS los secretos opcionales vacíos, como queda con un appsettings.json sin credenciales y sin
// secretos de usuario: arranca y responde, y cada función que necesita su secreto lo dice sin mostrar ningún valor.
// Lo único que no se tolera es la falta de Jwt:Key (sin ella no hay con qué firmar un login: a propósito).
// Los valores de estas pruebas son de relleno; ninguno es un secreto real.
public class ArranqueSinSecretosTests
{
    private const string ClaveJwt = "clave-de-prueba-para-el-arranque-sin-secretos-0123456789";

    // Los secretos que el código lee y que appsettings.json no trae (o trae vacíos). Ver docs/DESARROLLO_LOCAL.md.
    private static readonly string[] SecretosOpcionales =
    {
        "Email:From", "Email:Password", "Video:SignalingSecreto", "Video:TurnSecreto",
        "Video:Emociones:ClaveCifrado", "Mensajeria:WhatsApp:Secreto"
    };

    private static WebApplicationFactory<Program> Fabrica(string claveJwt, LogsCapturados? logs = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) =>
            {
                var valores = SecretosOpcionales.ToDictionary(k => k, _ => (string?)"");
                valores["Jwt:Key"] = claveJwt;
                c.AddInMemoryCollection(valores);
            });
            if (logs is not null) b.ConfigureLogging(l => l.AddProvider(logs));
        });

    [Fact]
    public async Task Con_todos_los_secretos_opcionales_vacios_la_aplicacion_arranca_y_responde()
    {
        var logs = new LogsCapturados();
        using var fabrica = Fabrica(ClaveJwt, logs);

        var r = await fabrica.CreateClient().GetAsync("/capacidades");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public void Cada_funcion_sin_su_secreto_lo_dice_por_su_nombre_y_no_muestra_valores()
    {
        using var fabrica = Fabrica(ClaveJwt);
        var servicios = fabrica.Services;

        Assert.False(servicios.GetRequiredService<EmailService>().Configurado);

        var emociones = servicios.GetRequiredService<IEmocionesCifrado>().ProblemaDeConfiguracion();
        Assert.Contains("Video:Emociones:ClaveCifrado", emociones);

        var senalizacion = servicios.GetRequiredService<TokenSalaService>().ProblemaDeConfiguracion();
        Assert.Contains("Video__SignalingSecreto", senalizacion);
        Assert.DoesNotContain(ClaveJwt, senalizacion + emociones);
    }

    // La API levanta sin los secretos de video: solo /video/acceso contesta "no disponible" (503) y deja el motivo en el log.
    [Fact]
    public async Task El_video_sin_secreto_contesta_503_y_deja_el_motivo_en_el_log_sin_valores()
    {
        var logs = new LogsCapturados();
        using var fabrica = Fabrica(ClaveJwt, logs);
        var jwt = fabrica.Services.GetRequiredService<JwtEmisorService>().Emitir(31, Roles.Paciente, idPaciente: 301);
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var r = await cliente.PostAsJsonAsync("/video/acceso", new { origen = "turno", id = 1 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal("video-no-disponible", cuerpo.RootElement.GetProperty("codigo").GetString());
        Assert.Contains(logs.Lineas, l => l.StartsWith("Warning|") && l.Contains("Video: /video/acceso deshabilitado")
                                           && l.Contains("no está configurado"));
        Assert.DoesNotContain(ClaveJwt, logs.Todo);
    }

    // Jwt:Key es la única que no se tolera vacía, y el error dice cuál falta.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("demasiado-corta")]
    public void Sin_una_Jwt_Key_valida_la_aplicacion_no_arranca_y_el_error_nombra_la_clave(string claveJwt)
    {
        using var fabrica = Fabrica(claveJwt);

        var ex = Record.Exception(() => fabrica.Services);

        Assert.NotNull(ex);
        var mensajes = string.Join(" | ", Cadena(ex!).Select(e => e.Message));
        Assert.Contains("Jwt:Key", mensajes);
    }

    private static IEnumerable<Exception> Cadena(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!) yield return e;
    }
}
