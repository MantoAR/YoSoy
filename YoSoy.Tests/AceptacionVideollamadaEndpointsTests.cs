using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// Los endpoints de la aceptación de la videollamada y su efecto en la sala (POST /video/acceso) y en el permiso del profesional, con la aplicación REAL (WebApplicationFactory, JWT de
// verdad) y dobles en memoria para la base. El turno de referencia es el 1 de VideoApp (4/3/2031 10:00).
public class AceptacionVideollamadaEndpointsTests
{
    private sealed class App : IDisposable
    {
        public readonly VideoApp Video;
        public readonly FakeConsentimientoRepo Repo = new();
        public readonly FakeAuditoriaEmociones Auditoria = new();
        public readonly FakeNotificacionInterna Avisos = new();

        public App(bool exigida = true)
        {
            Repo.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(1990, 5, 5), false);
            var ajustes = new Dictionary<string, string?> { ["Video:Emociones:ClaveCifrado"] = EscenarioEmociones.ClaveDePrueba };
            Video = new VideoApp(ajustes, servicios: s =>
            {
                s.RemoveAll<IConsentimientoVideoRepositorio>();
                s.AddSingleton<IConsentimientoVideoRepositorio>(Repo);
                s.RemoveAll<IAuditoriaEmociones>();
                s.AddSingleton<IAuditoriaEmociones>(Auditoria);
                s.RemoveAll<INotificacionInternaDAO>();
                s.AddSingleton<INotificacionInternaDAO>(Avisos);
                s.RemoveAll<IAuditoriaAceptacionVideo>();
                s.AddSingleton<IAuditoriaAceptacionVideo>(new FakeAuditoriaAceptacion());
                s.RemoveAll<ConsentimientoVideoService>();
                s.AddSingleton(sp => new ConsentimientoVideoService(Repo, sp.GetRequiredService<IOrigenSalaRepositorio>(),
                    sp.GetRequiredService<AccesoSalaService>(), sp.GetRequiredService<ITextoConsentimientoVideo>(),
                    sp.GetRequiredService<IEmocionesCifrado>(), Auditoria, Video!.Reloj, sp.GetRequiredService<IOptionsMonitor<VideoOptions>>(),
                    sp.GetRequiredService<IAceptacionVideollamadaRepositorio>()));
            });
            Video.Aceptaciones.Exigida = exigida;
        }

        public void Dispose() => Video.Dispose();
    }

    private static async Task<(HttpResponseMessage R, JsonElement J)> Pedir(HttpClient c, HttpMethod metodo, string url, object? cuerpo = null)
    {
        using var req = new HttpRequestMessage(metodo, url);
        if (cuerpo is not null) req.Content = JsonContent.Create(cuerpo);
        var r = await c.SendAsync(req);
        return (r, await VideoApp.Json(r));
    }

    private const string Url = "/video/aceptacion?origen=turno&id=1";
    private const string UrlPermiso = "/video/emociones/permiso?origen=turno&id=1";
    private static readonly string Version = TextoAceptacionVideollamada.Version;

    private static Task<(HttpResponseMessage R, JsonElement J)> Aceptar(HttpClient paciente) =>
        Pedir(paciente, HttpMethod.Put, "/video/aceptacion", new { origen = "turno", id = 1, version = Version });

    [Fact]
    public async Task Sin_sesion_los_tres_endpoints_dan_401()
    {
        using var app = new App();
        var c = app.Video.SinSesion();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pedir(c, HttpMethod.Get, Url)).R.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pedir(c, HttpMethod.Put, "/video/aceptacion", new { origen = "turno", id = 1, version = Version })).R.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pedir(c, HttpMethod.Post, "/video/aceptacion/revocar", new { origen = "turno", id = 1 })).R.StatusCode);
    }

    [Fact]
    public async Task El_paciente_no_entra_a_la_sala_hasta_aceptar_y_el_profesional_si()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();

        var (rPendiente, pendiente) = await Pedir(paciente, HttpMethod.Post, "/video/acceso", new { origen = "turno", id = 1 });
        Assert.Equal(HttpStatusCode.Conflict, rPendiente.StatusCode);
        Assert.Equal("aceptacion-video-pendiente", pendiente.GetProperty("codigo").GetString());
        Assert.False(pendiente.TryGetProperty("token", out _));   // no se emite token

        // El profesional entra igual: la aceptación es del paciente.
        Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(app.Video.Profesional(), "turno", 1)).StatusCode);

        var (rAcepta, acepta) = await Aceptar(paciente);
        Assert.Equal(HttpStatusCode.OK, rAcepta.StatusCode);
        Assert.Equal("acepta", acepta.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(paciente, "turno", 1)).StatusCode);
    }

    [Fact]
    public async Task Sin_la_migracion_el_paciente_entra_como_siempre()
    {
        using var app = new App(exigida: false);
        Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(app.Video.Paciente(), "turno", 1)).StatusCode);
    }

    [Fact]
    public async Task Revocar_cierra_el_video_avisa_al_profesional_y_el_siguiente_acceso_se_rechaza()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();
        await Aceptar(paciente);
        Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(paciente, "turno", 1)).StatusCode);

        var (r, j) = await Pedir(paciente, HttpMethod.Post, "/video/aceptacion/revocar", new { origen = "turno", id = 1 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("revocada", j.GetProperty("estado").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await VideoApp.Acceder(paciente, "turno", 1)).StatusCode);
        var aviso = Assert.Single(app.Avisos.Enviadas);
        Assert.Equal((VideoApp.IdProfesional, AceptacionVideollamadaService.TipoAvisoRevocacion), (aviso.IdProfesional, aviso.Tipo));

        // El profesional lo ve en su próxima consulta del permiso.
        var (_, permiso) = await Pedir(app.Video.Profesional(), HttpMethod.Get, UrlPermiso);
        Assert.Equal("revocada", permiso.GetProperty("video").GetProperty("estado").GetString());
        Assert.NotEqual(JsonValueKind.Null, permiso.GetProperty("video").GetProperty("revocadoUtc").ValueKind);
        Assert.False(permiso.GetProperty("permitido").GetBoolean());
    }

    [Fact]
    public async Task El_indicador_del_profesional_refleja_video_y_emociones_por_separado()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();
        var profesional = app.Video.Profesional();
        var (_, texto) = await Pedir(paciente, HttpMethod.Get, "/video/consentimiento?origen=turno&id=1");
        var versionEmociones = texto.GetProperty("version").GetString();

        var (_, p0) = await Pedir(profesional, HttpMethod.Get, UrlPermiso);
        Assert.Equal("pendiente", p0.GetProperty("video").GetProperty("estado").GetString());

        // Solo el video: video ✓, emociones ✗.
        await Aceptar(paciente);
        var (_, p1) = await Pedir(profesional, HttpMethod.Get, UrlPermiso);
        Assert.Equal("acepta", p1.GetProperty("video").GetProperty("estado").GetString());
        Assert.True(p1.GetProperty("video").GetProperty("exigido").GetBoolean());
        Assert.False(p1.GetProperty("permitido").GetBoolean());
        Assert.Equal("sin-decision", p1.GetProperty("estado").GetString());

        // Y las emociones: video ✓, emociones ✓.
        var (rEmo, _) = await Pedir(paciente, HttpMethod.Put, "/video/consentimiento", new { origen = "turno", id = 1, decision = "acepta", version = versionEmociones });
        Assert.Equal(HttpStatusCode.OK, rEmo.StatusCode);
        var (_, p2) = await Pedir(profesional, HttpMethod.Get, UrlPermiso);
        Assert.True(p2.GetProperty("permitido").GetBoolean());
        Assert.Equal("acepta", p2.GetProperty("video").GetProperty("estado").GetString());
    }

    [Fact]
    public async Task El_servidor_rechaza_activar_las_emociones_sin_haber_aceptado_el_video()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();
        var (_, texto) = await Pedir(paciente, HttpMethod.Get, "/video/consentimiento?origen=turno&id=1");

        var (r, j) = await Pedir(paciente, HttpMethod.Put, "/video/consentimiento",
            new { origen = "turno", id = 1, decision = "acepta", version = texto.GetProperty("version").GetString() });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("aceptacion-video-pendiente", j.GetProperty("codigo").GetString());
        Assert.Empty(app.Repo.Filas);

        var (_, permiso) = await Pedir(app.Video.Profesional(), HttpMethod.Get, UrlPermiso);
        Assert.False(permiso.GetProperty("permitido").GetBoolean());
    }

    [Fact]
    public async Task Otro_paciente_otro_profesional_y_el_Configurador_reciben_el_mismo_404_que_un_id_inexistente()
    {
        using var app = new App();
        var inexistente = await (await app.Video.Paciente().GetAsync("/video/aceptacion?origen=turno&id=999")).Content.ReadAsStringAsync();
        foreach (var c in new[] { app.Video.OtroPaciente(), app.Video.Profesional(), app.Video.Configurador() })
        {
            var r = await c.GetAsync(Url);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal(inexistente, await r.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await Aceptar(c)).R.StatusCode);
        }
        Assert.Empty(app.Video.Aceptaciones.Filas);
    }

    [Fact]
    public async Task Un_pedido_mal_armado_da_400_y_la_respuesta_no_se_cachea()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();
        Assert.Equal(HttpStatusCode.BadRequest, (await Pedir(paciente, HttpMethod.Get, "/video/aceptacion?origen=turno&id=abc")).R.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Pedir(paciente, HttpMethod.Put, "/video/aceptacion", new { origen = "otro", id = 1, version = Version })).R.StatusCode);
        var (r, j) = await Pedir(paciente, HttpMethod.Get, Url);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        Assert.Equal("pendiente", j.GetProperty("estado").GetString());
        Assert.Equal(Version, j.GetProperty("version").GetString());
        Assert.False(string.IsNullOrWhiteSpace(j.GetProperty("marcaProvisoria").GetString()));
    }

    [Fact]
    public async Task Quien_hace_la_admision_no_necesita_aceptar_y_el_paciente_si_en_una_admision()
    {
        using var app = new App();
        Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(app.Video.Admision(), "admision", 5)).StatusCode);
        var paciente = app.Video.Paciente();
        Assert.Equal(HttpStatusCode.Conflict, (await VideoApp.Acceder(paciente, "admision", 5)).StatusCode);
        var (r, _) = await Pedir(paciente, HttpMethod.Put, "/video/aceptacion", new { origen = "admision", id = 5, version = Version });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(paciente, "admision", 5)).StatusCode);
    }
}
