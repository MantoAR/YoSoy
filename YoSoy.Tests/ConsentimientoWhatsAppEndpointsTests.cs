using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Los endpoints del consentimiento de WhatsApp con la aplicación REAL (WebApplicationFactory, JWT de verdad, limitador y headers de Program.cs) y
// dobles en memoria para la base y el reloj. Con la base real hay una pasada aparte (ConsentimientoWhatsAppBaseRealTests). Las personas son las de
// VideoApp: el paciente 301 / usuario 31, otro paciente 302, un profesional y el Configurador.
public class ConsentimientoWhatsAppEndpointsTests
{
    private const string Texto = "/consentimiento-whatsapp/texto";
    private const string Estado = "/consentimiento-whatsapp";
    private const string Aceptar = "/consentimiento-whatsapp/aceptar";
    private const string Revocar = "/consentimiento-whatsapp/revocar";
    private const string HashDeV0 = "ef9676eb31a07d4f2da1dd8fd2890593dbac39e295a43c74ffb1dba004cf63ef";

    private sealed class App : IDisposable
    {
        public readonly VideoApp Video;
        public readonly FakeConsentimientoWhatsAppRepo Repo = new();
        public readonly FakeAuditoriaConsentimientoWhatsApp Auditoria = new();

        /// <param name="llave">Mensajeria:AvisosPorWhatsApp. Prendida por defecto: estas pruebas son del consentimiento encendido; las del apagado la ponen en false.</param>
        public App(Dictionary<string, string?>? config = null, bool llave = true)
        {
            config = new Dictionary<string, string?>(config ?? new()) { ["Mensajeria:AvisosPorWhatsApp"] = llave ? "true" : "false" };
            Repo.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaWhatsApp("11 5555-0001", new DateTime(1990, 5, 5), false);
            Repo.Pacientes[VideoApp.IdOtroPaciente] = new DatosPacienteParaWhatsApp("351 412-3456", new DateTime(1985, 1, 1), false);

            Video = new VideoApp(config, servicios: s =>
            {
                s.RemoveAll<IConsentimientoWhatsAppRepositorio>();
                s.AddSingleton<IConsentimientoWhatsAppRepositorio>(Repo);
                s.RemoveAll<IAuditoriaConsentimientoWhatsApp>();
                s.AddSingleton<IAuditoriaConsentimientoWhatsApp>(Auditoria);
                // Las reglas con el reloj de prueba (4/3/2031 10:00), no con el del sistema.
                s.RemoveAll<ConsentimientoWhatsAppService>();
                s.AddSingleton(sp => new ConsentimientoWhatsAppService(Repo, sp.GetRequiredService<ITextoConsentimientoWhatsApp>(), Auditoria,
                    sp.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>(), Video!.Reloj, sp.GetRequiredService<ILogger<ConsentimientoWhatsAppService>>()));
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

    // ───────────── Autenticación ─────────────

    [Fact]
    public async Task Sin_sesion_el_estado_aceptar_y_revocar_dan_401_y_no_registran_nada()
    {
        using var app = new App();
        var c = app.Video.SinSesion();

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(Estado)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync(Aceptar, new { version = "v0-provisorio" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync(Revocar, null)).StatusCode);
        Assert.Empty(app.Repo.Filas);
    }

    [Fact]
    public async Task Un_profesional_y_el_configurador_reciben_403_en_los_tres_y_no_pueden_aceptar_por_nadie()
    {
        using var app = new App();

        foreach (var cliente in new[] { app.Video.Profesional(), app.Video.Configurador() })
        {
            var (rGet, get) = await Pedir(cliente, HttpMethod.Get, Estado);
            var (rAcepta, acepta) = await Pedir(cliente, HttpMethod.Post, Aceptar, new { version = "v0-provisorio", idPaciente = VideoApp.IdPaciente });
            var (rRevoca, revoca) = await Pedir(cliente, HttpMethod.Post, Revocar);

            foreach (var (r, j) in new[] { (rGet, get), (rAcepta, acepta), (rRevoca, revoca) })
            {
                Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
                Assert.Equal("solo-pacientes", j.GetProperty("codigo").GetString());
            }
        }
        Assert.Empty(app.Repo.Filas);
        Assert.Empty(app.Auditoria.Registros);
    }

    // ───────────── El texto (sin sesión) ─────────────

    [Fact]
    public async Task El_texto_es_publico_no_se_cachea_y_trae_la_version_provisoria()
    {
        using var app = new App();

        var (r, j) = await Pedir(app.Video.SinSesion(), HttpMethod.Get, Texto);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        Assert.Equal(new[] { "disponible", "provisorio", "texto", "titulo", "version" }, Nombres(j));
        Assert.True(j.GetProperty("disponible").GetBoolean());
        Assert.Equal("v0-provisorio", j.GetProperty("version").GetString());
        Assert.True(j.GetProperty("provisorio").GetBoolean());
        Assert.Equal("Avisos de tus turnos por WhatsApp", j.GetProperty("titulo").GetString());
        Assert.Equal(7, j.GetProperty("texto").GetArrayLength());
        Assert.Contains("respondé BAJA", j.GetProperty("texto").EnumerateArray().Select(p => p.GetString()).Aggregate((a, b) => a + " " + b));
        Assert.Empty(app.Repo.Filas);
    }

    [Fact]
    public async Task Si_la_version_configurada_no_existe_el_texto_dice_que_no_esta_disponible()
    {
        using var app = new App(new() { ["Mensajeria:ConsentimientoWhatsApp:VersionTexto"] = "no-existe" });

        var (r, j) = await Pedir(app.Video.SinSesion(), HttpMethod.Get, Texto);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(j.GetProperty("disponible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("texto").ValueKind);
    }

    // ───────────── Contrato del paciente ─────────────

    [Fact]
    public async Task El_paciente_ve_su_estado_con_el_texto_y_nada_viene_aceptado_por_defecto()
    {
        using var app = new App();

        var (r, j) = await Pedir(app.Video.Paciente(), HttpMethod.Get, Estado);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        Assert.Equal(new[] { "aceptadoUtc", "contacto", "disponible", "estado", "motivo", "provisorio", "revocadoUtc", "texto", "titulo", "version", "versionAceptada" }, Nombres(j));
        Assert.Equal(JsonValueKind.Null, j.GetProperty("contacto").ValueKind);   // sin número de la clínica configurado, no hay link
        Assert.True(j.GetProperty("disponible").GetBoolean());
        Assert.Equal("sin-decision", j.GetProperty("estado").GetString());
        Assert.Equal("v0-provisorio", j.GetProperty("version").GetString());
        Assert.True(j.GetProperty("provisorio").GetBoolean());
        Assert.Equal(7, j.GetProperty("texto").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("aceptadoUtc").ValueKind);
        Assert.Empty(app.Repo.Filas);   // mirar no registra nada
    }

    [Fact]
    public async Task Aceptar_y_revocar_de_punta_a_punta_con_lo_que_queda_registrado()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();

        var (rAcepta, acepta) = await Pedir(paciente, HttpMethod.Post, Aceptar, new { version = "v0-provisorio" });

        Assert.Equal(HttpStatusCode.OK, rAcepta.StatusCode);
        Assert.Equal("no-store", rAcepta.Headers.CacheControl?.ToString());
        Assert.Equal("acepta", acepta.GetProperty("estado").GetString());
        Assert.Equal("v0-provisorio", acepta.GetProperty("versionAceptada").GetString());
        Assert.NotEqual(JsonValueKind.Null, acepta.GetProperty("aceptadoUtc").ValueKind);
        var fila = Assert.Single(app.Repo.Filas);
        Assert.Equal((VideoApp.IdPaciente, VideoApp.UsuarioPaciente, "1155550001", "perfil", "v0-provisorio", HashDeV0),
            (fila.IdPaciente, fila.IdUsuario, fila.Telefono, fila.Origen, fila.VersionTexto, fila.HashTexto));
        Assert.Equal(VideoApp.Inicio, fila.AceptadoUtc);

        Assert.Equal("acepta", (await Pedir(paciente, HttpMethod.Get, Estado)).J.GetProperty("estado").GetString());

        var (rRevoca, revoca) = await Pedir(paciente, HttpMethod.Post, Revocar);
        Assert.Equal(HttpStatusCode.OK, rRevoca.StatusCode);
        Assert.Equal("revocado", revoca.GetProperty("estado").GetString());
        Assert.NotEqual(JsonValueKind.Null, revoca.GetProperty("revocadoUtc").ValueKind);
        Assert.Equal("paciente", Assert.Single(app.Repo.Filas).MotivoRevocacion);
        Assert.Equal(new[] { "acepta", "revoca" }, app.Auditoria.Registros.Select(a => a.Accion));
        Assert.All(app.Auditoria.Registros, a => Assert.Equal(VideoApp.UsuarioPaciente, a.IdUsuario));
    }

    [Fact]
    public async Task Aceptar_dos_veces_o_revocar_dos_veces_es_inofensivo()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();

        await Pedir(paciente, HttpMethod.Post, Aceptar, new { version = "v0-provisorio" });
        Assert.Equal(HttpStatusCode.OK, (await Pedir(paciente, HttpMethod.Post, Aceptar, new { version = "v0-provisorio" })).R.StatusCode);
        Assert.Single(app.Repo.Filas);

        await Pedir(paciente, HttpMethod.Post, Revocar);
        Assert.Equal(HttpStatusCode.OK, (await Pedir(paciente, HttpMethod.Post, Revocar)).R.StatusCode);
        Assert.Equal(2, app.Auditoria.Registros.Count);
    }

    // ───────────── Quién es: del token, nunca del pedido ─────────────

    [Fact]
    public async Task El_cuerpo_no_puede_decir_de_quien_es_ni_para_que_numero_ni_con_que_texto()
    {
        using var app = new App();

        // El paciente 301 manda en el cuerpo otro paciente, otro usuario, otro teléfono y otro hash.
        var (r, _) = await Pedir(app.Video.Paciente(), HttpMethod.Post, Aceptar, new
        {
            version = "v0-provisorio", idPaciente = VideoApp.IdOtroPaciente, idUsuario = VideoApp.UsuarioOtroPaciente,
            telefono = "1100000000", hash = "0000", origen = "registro"
        });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var fila = Assert.Single(app.Repo.Filas);
        Assert.Equal((VideoApp.IdPaciente, VideoApp.UsuarioPaciente, "1155550001", "perfil", HashDeV0),
            (fila.IdPaciente, fila.IdUsuario, fila.Telefono, fila.Origen, fila.HashTexto));
    }

    [Fact]
    public async Task Cada_paciente_ve_y_cambia_solo_lo_suyo()
    {
        using var app = new App();
        await Pedir(app.Video.Paciente(), HttpMethod.Post, Aceptar, new { version = "v0-provisorio" });

        var otro = app.Video.OtroPaciente();
        Assert.Equal("sin-decision", (await Pedir(otro, HttpMethod.Get, Estado)).J.GetProperty("estado").GetString());
        await Pedir(otro, HttpMethod.Post, Revocar);                                                // no toca lo del 301
        Assert.Equal("acepta", (await Pedir(app.Video.Paciente(), HttpMethod.Get, Estado)).J.GetProperty("estado").GetString());

        await Pedir(otro, HttpMethod.Post, Aceptar, new { version = "v0-provisorio" });
        Assert.Equal(new[] { VideoApp.IdPaciente, VideoApp.IdOtroPaciente }, app.Repo.Filas.Select(f => f.IdPaciente).OrderBy(i => i));
    }

    // ───────────── Pedidos mal armados y conflictos ─────────────

    [Fact]
    public async Task Sin_cuerpo_o_sin_version_da_409_version_desactualizada_y_un_cuerpo_roto_da_400()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();

        var rVacio = await paciente.PostAsync(Aceptar, null);
        var (rSinVersion, sinVersion) = await Pedir(paciente, HttpMethod.Post, Aceptar, new { });
        var roto = await paciente.PostAsync(Aceptar, new StringContent("{no es json", Encoding.UTF8, "application/json"));
        var (rViejo, viejo) = await Pedir(paciente, HttpMethod.Post, Aceptar, new { version = "v-anterior" });

        // Sin cuerpo en absoluto, el framework puede contestar 400/415 o entregarlo como "sin versión" (409): lo que importa es que no acepta.
        Assert.Contains((int)rVacio.StatusCode, new[] { 400, 409, 415 });
        Assert.Equal("version-desactualizada", sinVersion.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, roto.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rViejo.StatusCode);
        Assert.Equal("version-desactualizada", viejo.GetProperty("codigo").GetString());
        Assert.Empty(app.Repo.Filas);
    }

    [Theory]
    [InlineData(null, "telefono-invalido")]
    [InlineData("5555-0001", "telefono-invalido")]
    [InlineData("+34 612 345 678", "telefono-invalido")]
    public async Task Sin_un_celular_argentino_valido_da_409_y_no_registra_nada(string? telefono, string codigo)
    {
        using var app = new App();
        app.Repo.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaWhatsApp(telefono, new DateTime(1990, 5, 5), false);

        var (r, j) = await Pedir(app.Video.Paciente(), HttpMethod.Post, Aceptar, new { version = "v0-provisorio" });
        var estado = (await Pedir(app.Video.Paciente(), HttpMethod.Get, Estado)).J;

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(codigo, j.GetProperty("codigo").GetString());
        Assert.False(string.IsNullOrWhiteSpace(j.GetProperty("mensaje").GetString()));
        Assert.False(estado.GetProperty("disponible").GetBoolean());
        Assert.Equal(codigo, estado.GetProperty("motivo").GetString());
        Assert.Empty(app.Repo.Filas);
    }

    [Fact]
    public async Task Un_menor_recibe_no_disponible_y_409_al_aceptar()
    {
        using var app = new App();
        app.Repo.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaWhatsApp("11 5555-0001", new DateTime(2018, 2, 2), false);

        var (_, estado) = await Pedir(app.Video.Paciente(), HttpMethod.Get, Estado);
        var (r, j) = await Pedir(app.Video.Paciente(), HttpMethod.Post, Aceptar, new { version = "v0-provisorio" });

        Assert.False(estado.GetProperty("disponible").GetBoolean());
        Assert.Equal("menor-de-edad", estado.GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("menor-de-edad", j.GetProperty("codigo").GetString());
        Assert.Empty(app.Repo.Filas);
    }

    [Fact]
    public async Task Cambiar_el_telefono_en_el_perfil_hace_que_el_consentimiento_diga_telefono_cambiado()
    {
        using var app = new App();
        var paciente = app.Video.Paciente();
        await Pedir(paciente, HttpMethod.Post, Aceptar, new { version = "v0-provisorio" });

        app.Repo.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaWhatsApp("351 412-3456", new DateTime(1990, 5, 5), false);

        var (_, estado) = await Pedir(paciente, HttpMethod.Get, Estado);
        Assert.Equal("telefono-cambiado", estado.GetProperty("estado").GetString());
        Assert.True(estado.GetProperty("disponible").GetBoolean());
    }

    [Fact]
    public async Task Si_la_version_configurada_no_existe_no_se_puede_aceptar_y_se_dice()
    {
        using var app = new App(new() { ["Mensajeria:ConsentimientoWhatsApp:VersionTexto"] = "no-existe" });

        var (rEstado, estado) = await Pedir(app.Video.Paciente(), HttpMethod.Get, Estado);
        var (r, j) = await Pedir(app.Video.Paciente(), HttpMethod.Post, Aceptar, new { version = "no-existe" });

        Assert.Equal(HttpStatusCode.OK, rEstado.StatusCode);
        Assert.Equal("no-disponible", estado.GetProperty("estado").GetString());
        Assert.Equal("no-configurado", estado.GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("no-configurado", j.GetProperty("codigo").GetString());
        Assert.Empty(app.Repo.Filas);
    }

    // ───────────── Limitador ─────────────

    [Fact]
    public async Task Pasadas_30_consultas_por_minuto_se_rechaza_con_429_el_texto_incluido()
    {
        using var app = new App();
        var c = app.Video.SinSesion();

        for (var i = 0; i < 30; i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Texto)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync(Texto)).StatusCode);
    }
}
