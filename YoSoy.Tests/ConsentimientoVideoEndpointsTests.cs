using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// Los cuatro endpoints del consentimiento de la lectura de emociones, con la aplicación REAL (WebApplicationFactory, JWT de
// verdad, limitador y headers de Program.cs) y dobles en memoria para la base y el reloj. Con la base real hay una pasada en
// EmocionesBaseRealTests. El turno de referencia es el mismo que en VideoApp: el 1, de las 10:00 del 4/3/2031.
public class ConsentimientoVideoEndpointsTests
{
    // VideoApp con el consentimiento enchufado a la misma sala de prueba, a un reloj fijo y a un repositorio en memoria.
    private sealed class App : IDisposable
    {
        public readonly VideoApp Video;
        public readonly FakeConsentimientoRepo Repo = new();
        public readonly FakeAuditoriaEmociones Auditoria = new();

        public App(Dictionary<string, string?>? config = null)
        {
            Repo.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(1990, 5, 5), false);
            var ajustes = new Dictionary<string, string?> { ["Video:Emociones:ClaveCifrado"] = EscenarioEmociones.ClaveDePrueba };
            if (config is not null) foreach (var (k, v) in config) ajustes[k] = v;

            Video = new VideoApp(ajustes, servicios: s =>
            {
                s.RemoveAll<IConsentimientoVideoRepositorio>();
                s.AddSingleton<IConsentimientoVideoRepositorio>(Repo);
                s.RemoveAll<IAuditoriaEmociones>();
                s.AddSingleton<IAuditoriaEmociones>(Auditoria);
                // Las reglas con el reloj de la sala (4/3/2031 10:00), no con el del sistema.
                s.RemoveAll<ConsentimientoVideoService>();
                s.AddSingleton(sp => new ConsentimientoVideoService(Repo, sp.GetRequiredService<IOrigenSalaRepositorio>(),
                    sp.GetRequiredService<AccesoSalaService>(), sp.GetRequiredService<ITextoConsentimientoVideo>(),
                    sp.GetRequiredService<IEmocionesCifrado>(), Auditoria, Video!.Reloj, sp.GetRequiredService<IOptionsMonitor<VideoOptions>>()));
            });
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

    private static string[] Nombres(JsonElement j) => j.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private const string UrlConsentimiento = "/video/consentimiento?origen=turno&id=1";
    private const string UrlPermiso = "/video/emociones/permiso?origen=turno&id=1";

    private static async Task<string> Version(HttpClient paciente)
    {
        var (_, j) = await Pedir(paciente, HttpMethod.Get, UrlConsentimiento);
        return j.GetProperty("version").GetString()!;
    }

    // La respuesta entera (status, headers menos Date y cuerpo): para comparar un 404 con otro.
    private static async Task<string> Huella(HttpClient c, HttpMethod metodo, string url, object? cuerpo = null)
    {
        using var req = new HttpRequestMessage(metodo, url);
        if (cuerpo is not null) req.Content = JsonContent.Create(cuerpo);
        var r = await c.SendAsync(req);
        var cabeceras = r.Headers.Concat(r.Content.Headers)
            .Where(h => !h.Key.Equals("Date", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}").OrderBy(l => l, StringComparer.Ordinal);
        return $"{(int)r.StatusCode}\n{string.Join("\n", cabeceras)}\n\n{await r.Content.ReadAsStringAsync()}";
    }

    // ───────────── Autenticación ─────────────

    [Fact]
    public async Task Sin_sesion_los_cuatro_endpoints_dan_401()
    {
        using var app = new App();
        var c = app.Video.SinSesion();

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(UrlConsentimiento)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PutAsJsonAsync("/video/consentimiento", new { origen = "turno", id = 1, decision = "acepta", version = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/video/consentimiento/revocar", new { origen = "turno", id = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(UrlPermiso)).StatusCode);
        Assert.Empty(app.Repo.Filas);
    }

    // ───────────── Contrato ─────────────

    [Fact]
    public async Task El_paciente_ve_su_estado_y_el_texto_y_la_respuesta_no_se_cachea()
    {
        using var app = new App();

        var (r, j) = await Pedir(app.Video.Paciente(), HttpMethod.Get, UrlConsentimiento);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        Assert.Equal(new[] { "decididoUtc", "disponible", "estado", "motivo", "provisorio", "revocadoUtc", "texto", "titulo", "version" }, Nombres(j));
        Assert.True(j.GetProperty("disponible").GetBoolean());
        Assert.Equal("sin-decision", j.GetProperty("estado").GetString());
        Assert.Equal("v0-provisorio", j.GetProperty("version").GetString());
        Assert.True(j.GetProperty("provisorio").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(j.GetProperty("titulo").GetString()));
        Assert.True(j.GetProperty("texto").GetArrayLength() >= 5);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("decididoUtc").ValueKind);
    }

    [Fact]
    public async Task Aceptar_revocar_y_el_permiso_del_profesional_de_punta_a_punta()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();
        var profesional = app.Video.Profesional();
        var version = await Version(paciente);

        // Todavía nada: el profesional no tiene permiso.
        var (_, antes) = await Pedir(profesional, HttpMethod.Get, UrlPermiso);
        Assert.False(antes.GetProperty("permitido").GetBoolean());
        Assert.Equal("sin-decision", antes.GetProperty("estado").GetString());

        // El paciente acepta.
        var (rAcepta, acepta) = await Pedir(paciente, HttpMethod.Put, "/video/consentimiento",
            new { origen = "turno", id = 1, decision = "acepta", version });
        Assert.Equal(HttpStatusCode.OK, rAcepta.StatusCode);
        Assert.Equal("acepta", acepta.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, acepta.GetProperty("texto").ValueKind);   // en la respuesta de la decisión no vuelve el texto
        Assert.NotEqual(JsonValueKind.Null, acepta.GetProperty("decididoUtc").ValueKind);

        // Ahora sí, con todo lo que la pantalla necesita para vigilar.
        var (rPermiso, permiso) = await Pedir(profesional, HttpMethod.Get, UrlPermiso);
        Assert.Equal(HttpStatusCode.OK, rPermiso.StatusCode);
        Assert.Equal("no-store", rPermiso.Headers.CacheControl?.ToString());
        Assert.Equal(new[] { "ahoraUtc", "consultarCadaSegundos", "desdeUtc", "estado", "motivo", "permitido", "revocadoUtc", "version", "vigenciaSegundos" }, Nombres(permiso));
        Assert.True(permiso.GetProperty("permitido").GetBoolean());
        Assert.Equal("acepta", permiso.GetProperty("estado").GetString());
        Assert.Equal(3, permiso.GetProperty("consultarCadaSegundos").GetInt32());
        Assert.Equal(10, permiso.GetProperty("vigenciaSegundos").GetInt32());
        Assert.Equal(version, permiso.GetProperty("version").GetString());

        // El paciente revoca: el permiso se corta.
        var (rRevoca, revoca) = await Pedir(paciente, HttpMethod.Post, "/video/consentimiento/revocar", new { origen = "turno", id = 1 });
        Assert.Equal(HttpStatusCode.OK, rRevoca.StatusCode);
        Assert.Equal("revocado", revoca.GetProperty("estado").GetString());
        var (_, despues) = await Pedir(profesional, HttpMethod.Get, UrlPermiso);
        Assert.False(despues.GetProperty("permitido").GetBoolean());
        Assert.Equal("revocado", despues.GetProperty("estado").GetString());
        Assert.NotEqual(JsonValueKind.Null, despues.GetProperty("revocadoUtc").ValueKind);
        Assert.Equal(new[] { "consentimiento:acepta", "consentimiento:revoca" }, app.Auditoria.Registros.Select(r => r.Tipo).ToArray());
    }

    [Fact]
    public async Task El_configurador_consulta_el_permiso_de_una_admision()
    {
        using var app = new App();
        var (_, j) = await Pedir(app.Video.Configurador(), HttpMethod.Get, "/video/emociones/permiso?origen=admision&id=5");

        Assert.False(j.GetProperty("permitido").GetBoolean());
        Assert.Equal("sin-decision", j.GetProperty("estado").GetString());
    }

    // ───────────── Quién es parte: 404 uniforme ─────────────

    [Fact]
    public async Task Inexistente_y_ajeno_dan_exactamente_la_misma_respuesta()
    {
        using var app = new App();
        var version = await Version(app.Video.Paciente());
        var cuerpoDecision = new { origen = "turno", id = 1, decision = "acepta", version };
        var cuerpoInexistente = new { origen = "turno", id = 987, decision = "acepta", version };

        var referencia = await Huella(app.Video.Paciente(), HttpMethod.Put, "/video/consentimiento", cuerpoInexistente);
        Assert.StartsWith("404", referencia);

        // Otro paciente, un profesional y el Configurador intentando decidir por el paciente.
        Assert.Equal(referencia, await Huella(app.Video.OtroPaciente(), HttpMethod.Put, "/video/consentimiento", cuerpoDecision));
        Assert.Equal(referencia, await Huella(app.Video.Profesional(), HttpMethod.Put, "/video/consentimiento", cuerpoDecision));
        Assert.Equal(referencia, await Huella(app.Video.Configurador(), HttpMethod.Put, "/video/consentimiento", cuerpoDecision));
        Assert.Empty(app.Repo.Filas);

        // Y el permiso: ni otro profesional, ni un paciente.
        var permisoInexistente = await Huella(app.Video.Profesional(), HttpMethod.Get, "/video/emociones/permiso?origen=turno&id=987");
        Assert.StartsWith("404", permisoInexistente);
        Assert.Equal(permisoInexistente, await Huella(app.Video.OtroProfesional(), HttpMethod.Get, UrlPermiso));
        Assert.Equal(permisoInexistente, await Huella(app.Video.Paciente(), HttpMethod.Get, UrlPermiso));
    }

    // ───────────── Pedidos mal armados y conflictos ─────────────

    [Theory]
    [InlineData("/video/consentimiento")]
    [InlineData("/video/consentimiento?origen=turno")]
    [InlineData("/video/consentimiento?id=1")]
    [InlineData("/video/consentimiento?origen=otro&id=1")]
    [InlineData("/video/consentimiento?origen=turno&id=0")]
    [InlineData("/video/consentimiento?origen=turno&id=-3")]
    [InlineData("/video/emociones/permiso")]
    [InlineData("/video/emociones/permiso?origen=turno&id=abc")]
    public async Task Un_pedido_sin_sesion_valida_da_400_con_su_codigo(string url)
    {
        using var app = new App();
        var cliente = url.Contains("permiso") ? app.Video.Profesional() : app.Video.Paciente();

        var (r, j) = await Pedir(cliente, HttpMethod.Get, url);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("solicitud-invalida", j.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Un_cuerpo_roto_o_vacio_da_400_y_no_registra_nada()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();

        var vacio = await paciente.PutAsync("/video/consentimiento", new StringContent("", System.Text.Encoding.UTF8, "application/json"));
        var roto = await paciente.PutAsync("/video/consentimiento", new StringContent("{no es json", System.Text.Encoding.UTF8, "application/json"));
        var sinDecision = await Pedir(paciente, HttpMethod.Put, "/video/consentimiento", new { origen = "turno", id = 1, version = "v0-provisorio" });

        Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, roto.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinDecision.R.StatusCode);
        Assert.Empty(app.Repo.Filas);
    }

    [Fact]
    public async Task Version_vieja_y_rechazo_despues_de_aceptar_dan_409_con_su_codigo()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();
        var version = await Version(paciente);

        var (rVieja, vieja) = await Pedir(paciente, HttpMethod.Put, "/video/consentimiento", new { origen = "turno", id = 1, decision = "acepta", version = "v-anterior" });
        await Pedir(paciente, HttpMethod.Put, "/video/consentimiento", new { origen = "turno", id = 1, decision = "acepta", version });
        var (rRechazo, rechazo) = await Pedir(paciente, HttpMethod.Put, "/video/consentimiento", new { origen = "turno", id = 1, decision = "rechaza", version });

        Assert.Equal(HttpStatusCode.Conflict, rVieja.StatusCode);
        Assert.Equal("version-desactualizada", vieja.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Conflict, rRechazo.StatusCode);
        Assert.Equal("usar-revocar", rechazo.GetProperty("codigo").GetString());
        Assert.Single(app.Repo.Filas);
    }

    [Fact]
    public async Task Un_menor_recibe_no_disponible_y_el_profesional_el_motivo()
    {
        using var app = new App();
        app.Repo.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(2018, 2, 2), false);

        var (_, paciente) = await Pedir(app.Video.Paciente(), HttpMethod.Get, UrlConsentimiento);
        var (rDecidir, decidir) = await Pedir(app.Video.Paciente(), HttpMethod.Put, "/video/consentimiento",
            new { origen = "turno", id = 1, decision = "acepta", version = "v0-provisorio" });
        var (_, permiso) = await Pedir(app.Video.Profesional(), HttpMethod.Get, UrlPermiso);

        Assert.False(paciente.GetProperty("disponible").GetBoolean());
        Assert.Equal("no-disponible", paciente.GetProperty("estado").GetString());
        Assert.Equal("menor-de-edad", paciente.GetProperty("motivo").GetString());
        Assert.Equal(JsonValueKind.Null, paciente.GetProperty("texto").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, rDecidir.StatusCode);
        Assert.Equal("menor-de-edad", decidir.GetProperty("codigo").GetString());
        Assert.False(permiso.GetProperty("permitido").GetBoolean());
        Assert.Equal("menor-de-edad", permiso.GetProperty("motivo").GetString());
        Assert.Empty(app.Repo.Filas);
    }

    [Fact]
    public async Task La_sesion_que_ya_termino_dice_cuando_cerro()
    {
        using var app = new App();
        app.Video.Reloj.Avanzar(TimeSpan.FromHours(3));   // la sala cerró a las 11:00

        var (r, j) = await Pedir(app.Video.Paciente(), HttpMethod.Get, UrlConsentimiento);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("ya-termino", j.GetProperty("codigo").GetString());
        Assert.True(j.TryGetProperty("cerroUtc", out _));
    }

    // ───────────── Limitador ─────────────

    [Fact]
    public async Task Pasado_el_limite_por_minuto_se_rechaza_con_429()
    {
        using var app = new App(new() { ["Video:Emociones:LimitePorMinuto"] = "3" });
        var profesional = app.Video.Profesional();

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await profesional.GetAsync(UrlPermiso)).StatusCode);
        var cuarto = await profesional.GetAsync(UrlPermiso);

        Assert.Equal(HttpStatusCode.TooManyRequests, cuarto.StatusCode);
    }

    [Fact]
    public async Task El_limite_por_defecto_alcanza_para_preguntar_cada_tres_segundos_varias_personas_a_la_vez()
    {
        using var app = new App();
        var profesional = app.Video.Profesional();

        // 60 pedidos seguidos (3 minutos de una sesión a un pedido cada 3 s, ni la mitad de la cuota): ninguno se rechaza.
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await profesional.GetAsync(UrlPermiso)).StatusCode);
    }

    [Fact]
    public async Task El_limite_de_consentimiento_es_independiente_del_de_acceso_a_la_sala()
    {
        using var app = new App(new() { ["Video:LimitePorMinuto"] = "2", ["Video:Emociones:LimitePorMinuto"] = "50" });
        var profesional = app.Video.Profesional();

        // Se agota el de /video/acceso y el permiso sigue andando.
        for (var i = 0; i < 3; i++) await VideoApp.Acceder(profesional, "turno", 1);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await VideoApp.Acceder(profesional, "turno", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await profesional.GetAsync(UrlPermiso)).StatusCode);
    }
}
