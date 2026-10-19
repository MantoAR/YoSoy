using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Con la llave Mensajeria:AvisosPorWhatsApp APAGADA (por defecto) el consentimiento de WhatsApp está dormido y NADA de él se ve: ni la casilla del
// registro, ni la tarjeta del perfil, ni el link wa.me, ni la sugerencia de después de un turno. Así no se promete un aviso que no va a llegar.
//
//  · El servicio: el texto y el estado salen como "no disponible" (motivo "avisos-apagados"), aceptar y revocar contestan conflicto, y nada de eso lee ni
//    escribe la tabla (el doble de la base tira una excepción si se la toca: es "la tabla que no existe").
//  · Los endpoints, con la aplicación real: lo mismo por HTTP, y aunque haya un número de la clínica configurado no hay link.
//  · Las pantallas, leyendo sus archivos: la tarjeta del perfil nace oculta y sigue oculta, no se arma ningún link ni sugerencia.
public class ConsentimientoWhatsAppApagadoServicioTests
{
    private static EscenarioWhatsApp Apagado()
    {
        var e = new EscenarioWhatsApp(llave: false);
        e.Repo.Falla = new InvalidOperationException("Invalid object name 'ConsentimientoWhatsApp'.");   // la migración 16 no se aplicó: tocar la tabla revienta
        return e;
    }

    [Fact]
    public void El_texto_no_esta_disponible_aunque_exista_y_asi_el_registro_no_ofrece_la_casilla()
    {
        var e = Apagado();

        var t = e.Servicio.Texto();

        Assert.False(t.Disponible);
        Assert.Null(t.Version);
        Assert.Null(t.Texto);
        Assert.False(e.Servicio.Habilitado);
    }

    [Fact]
    public void El_estado_dice_avisos_apagados_sin_texto_y_sin_leer_la_base()
    {
        var e = Apagado();

        var r = e.Servicio.Obtener(EscenarioWhatsApp.Paciente());

        Assert.True(r.Ok);
        var estado = r.Estado!;
        Assert.False(estado.Disponible);
        Assert.Equal(ConsentimientoWhatsAppService.NoDisponible, estado.Estado);
        Assert.Equal(ConsentimientoWhatsAppService.AvisosApagados, estado.Motivo);
        Assert.Equal("avisos-apagados", estado.Motivo);
        Assert.Null(estado.Version);
        Assert.Null(estado.Texto);
        Assert.Null(estado.Titulo);
        Assert.Null(estado.AceptadoUtc);
        Assert.Null(estado.RevocadoUtc);
    }

    [Fact]
    public void Aceptar_y_revocar_contestan_conflicto_y_no_tocan_ni_la_tabla_ni_la_auditoria()
    {
        var e = Apagado();

        var acepta = e.Servicio.Aceptar(EscenarioWhatsApp.Paciente(), e.Version);
        var revoca = e.Servicio.Revocar(EscenarioWhatsApp.Paciente());

        foreach (var r in new[] { acepta, revoca })
        {
            Assert.False(r.Ok);
            Assert.Equal(TipoErrorConsentimiento.Conflicto, r.Error!.Tipo);
            Assert.Equal("avisos-apagados", r.Error.Codigo);
        }
        Assert.Empty(e.Repo.Filas);
        Assert.Empty(e.Auditoria.Registros);
    }

    [Fact]
    public void Un_profesional_o_el_configurador_siguen_recibiendo_solo_pacientes_con_la_llave_apagada()
    {
        var e = Apagado();

        foreach (var yo in new[] { EscenarioWhatsApp.Profesional(), EscenarioWhatsApp.Configurador() })
        {
            Assert.Equal("solo-pacientes", e.Servicio.Obtener(yo).Error!.Codigo);
            Assert.Equal("solo-pacientes", e.Servicio.Aceptar(yo, e.Version).Error!.Codigo);
            Assert.Equal("solo-pacientes", e.Servicio.Revocar(yo).Error!.Codigo);
        }
    }

    [Fact]
    public void Prender_la_llave_enciende_el_consentimiento_sin_reiniciar_y_apagarla_lo_vuelve_a_dormir()
    {
        var e = new EscenarioWhatsApp(llave: false);

        Assert.False(e.Servicio.Texto().Disponible);

        e.Opciones.CurrentValue.AvisosPorWhatsApp = true;
        Assert.True(e.Servicio.Texto().Disponible);
        Assert.Equal(ConsentimientoWhatsAppService.SinDecision, e.Servicio.Obtener(EscenarioWhatsApp.Paciente()).Estado!.Estado);
        Assert.True(e.Aceptar().Ok);
        Assert.Single(e.Repo.Filas);

        e.Opciones.CurrentValue.AvisosPorWhatsApp = false;
        Assert.False(e.Servicio.Texto().Disponible);
        Assert.Equal("avisos-apagados", e.Servicio.Obtener(EscenarioWhatsApp.Paciente()).Estado!.Motivo);
        Assert.Single(e.Repo.Filas);            // lo que ya estaba registrado queda como estaba: apagar no borra ni revoca nada
        Assert.True(e.Repo.Filas[0].EstaAbierta);
    }

    // ───────────── El link wa.me ─────────────

    [Fact]
    public void Con_la_llave_apagada_no_hay_link_wa_me_aunque_haya_un_numero_configurado_y_el_log_no_avisa_nada()
    {
        var opciones = new OpcionesFijas();
        opciones.CurrentValue.WhatsAppNumeroClinica = "+54 9 11 5555-0000";
        var logs = new LogsCapturados();
        var contacto = new ContactoWhatsApp(opciones, logs.CreateLogger<ContactoWhatsApp>());

        Assert.Null(contacto.Enlace());

        opciones.CurrentValue.WhatsAppNumeroClinica = "numero-que-no-sirve";
        Assert.Null(contacto.Enlace());
        Assert.Empty(logs.Lineas);                                       // ni el aviso de "número que no sirve": con los avisos apagados no importa

        opciones.CurrentValue.AvisosPorWhatsApp = true;
        opciones.CurrentValue.WhatsAppNumeroClinica = "+54 9 11 5555-0000";
        Assert.Equal("https://wa.me/5491155550000?text=Hola", contacto.Enlace());   // y prendida, como siempre
    }
}

public class ConsentimientoWhatsAppApagadoEndpointsTests
{
    private const string Texto = "/consentimiento-whatsapp/texto";
    private const string Estado = "/consentimiento-whatsapp";
    private const string Aceptar = "/consentimiento-whatsapp/aceptar";
    private const string Revocar = "/consentimiento-whatsapp/revocar";

    private sealed class App : IDisposable
    {
        public readonly VideoApp Video;
        public readonly FakeConsentimientoWhatsAppRepo Repo = new();
        public readonly FakeAuditoriaConsentimientoWhatsApp Auditoria = new();

        /// <param name="llave">null = sin tocar la configuración (la aplicación real, que viene con la llave apagada); true / false = se escribe en la configuración.</param>
        public App(bool? llave = null, string? numeroDeLaClinica = "+54 9 11 5555-0000")
        {
            Repo.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaWhatsApp("11 5555-0001", new DateTime(1990, 5, 5), false);
            // La tabla no existe: cualquier lectura o escritura de la base tira.
            Repo.Falla = new InvalidOperationException("Invalid object name 'ConsentimientoWhatsApp'.");
            var config = new Dictionary<string, string?>();
            if (numeroDeLaClinica is not null) config["Mensajeria:WhatsAppNumeroClinica"] = numeroDeLaClinica;
            if (llave is { } valor) config["Mensajeria:AvisosPorWhatsApp"] = valor ? "true" : "false";
            Video = new VideoApp(config, servicios: s =>
            {
                s.RemoveAll<IConsentimientoWhatsAppRepositorio>();
                s.AddSingleton<IConsentimientoWhatsAppRepositorio>(Repo);
                s.RemoveAll<IAuditoriaConsentimientoWhatsApp>();
                s.AddSingleton<IAuditoriaConsentimientoWhatsApp>(Auditoria);
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

    [Fact]
    public async Task La_aplicacion_real_viene_con_los_avisos_apagados_y_el_texto_no_esta_disponible()
    {
        using var app = new App();          // sin tocar la llave: es lo que trae appsettings.json

        var (r, j) = await Pedir(app.Video.SinSesion(), HttpMethod.Get, Texto);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        Assert.False(j.GetProperty("disponible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("texto").ValueKind);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("titulo").ValueKind);
    }

    [Fact]
    public async Task El_estado_del_paciente_dice_avisos_apagados_y_no_trae_link_aunque_haya_un_numero_de_la_clinica()
    {
        using var app = new App(llave: false, numeroDeLaClinica: "+54 9 11 5555-0000");

        var (r, j) = await Pedir(app.Video.Paciente(), HttpMethod.Get, Estado);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);                     // y no 500: la tabla no se toca
        Assert.False(j.GetProperty("disponible").GetBoolean());
        Assert.Equal("no-disponible", j.GetProperty("estado").GetString());
        Assert.Equal("avisos-apagados", j.GetProperty("motivo").GetString());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("texto").ValueKind);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("contacto").ValueKind);   // ni el link para escribirle a la clínica
        Assert.DoesNotContain("wa.me", j.ToString());
    }

    [Theory]
    [InlineData("aceptar")]
    [InlineData("revocar")]
    public async Task Aceptar_y_revocar_dan_409_avisos_apagados_y_no_registran_nada(string que)
    {
        using var app = new App(llave: false);

        var (r, j) = que == "aceptar"
            ? await Pedir(app.Video.Paciente(), HttpMethod.Post, Aceptar, new { version = "v0-provisorio" })
            : await Pedir(app.Video.Paciente(), HttpMethod.Post, Revocar);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("avisos-apagados", j.GetProperty("codigo").GetString());
        Assert.Empty(app.Repo.Filas);
        Assert.Empty(app.Auditoria.Registros);
    }

    [Fact]
    public async Task Sin_sesion_sigue_el_401_y_un_profesional_el_403_con_la_llave_apagada()
    {
        using var app = new App(llave: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Video.SinSesion().GetAsync(Estado)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Video.SinSesion().PostAsync(Revocar, null)).StatusCode);
        var (r, j) = await Pedir(app.Video.Profesional(), HttpMethod.Get, Estado);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("solo-pacientes", j.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Prendida_la_llave_el_mismo_pedido_ofrece_el_texto_y_el_link()
    {
        using var app = new App(llave: true, numeroDeLaClinica: "+54 9 11 5555-0000");
        app.Repo.Falla = null;                                                                 // con la llave prendida la base se usa, y acá es el doble

        var (rTexto, texto) = await Pedir(app.Video.SinSesion(), HttpMethod.Get, Texto);
        var (rEstado, estado) = await Pedir(app.Video.Paciente(), HttpMethod.Get, Estado);

        Assert.True(texto.GetProperty("disponible").GetBoolean());
        Assert.Equal("sin-decision", estado.GetProperty("estado").GetString());
        Assert.Equal("https://wa.me/5491155550000?text=Hola", estado.GetProperty("contacto").GetProperty("enlace").GetString());
        Assert.Equal(HttpStatusCode.OK, rTexto.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rEstado.StatusCode);
    }
}

// La interfaz, leyendo los archivos de PANTALLAS/ (lo que hace en un navegador de verdad lo prueba el banco de Playwright, no versionado).
public class PantallasConsentimientoWhatsAppApagadoTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(partes).ToArray())).Replace("\r\n", "\n");

    private static string Modulo => Leer("PANTALLAS", "shared", "consentimiento-whatsapp.js");
    private static string Dashboard => Leer("PANTALLAS", "Pantalla_Paciente", "js", "dashboard.js");
    private static string RegistroJs => Leer("PANTALLAS", "Pantalla_Usuario", "Usuario", "js", "app.js");

    private static string FuncionRenderDelPerfil()
    {
        var js = Dashboard;
        var inicio = js.IndexOf("async function renderAvisosWhatsApp()", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "no encuentro renderAvisosWhatsApp");
        var fin = js.IndexOf("// Después de pedir o confirmar un turno", inicio, StringComparison.Ordinal);
        Assert.True(fin > inicio);
        return js[inicio..fin];
    }

    [Fact]
    public void La_tarjeta_del_perfil_nace_oculta_y_solo_se_muestra_si_la_API_la_ofrece()
    {
        var render = FuncionRenderDelPerfil();

        Assert.Contains(@"<div class=""card perfil-form oculto"" id=""avisosWhatsApp""", Dashboard);
        // Apagado: se vuelve a ocultar, se vacía y no se dibuja nada más (el return viene antes de armar la tarjeta).
        var apagado = render.IndexOf("if (YS.whatsapp.apagado(e))", StringComparison.Ordinal);
        var finRama = render.IndexOf("_estadoWhatsApp = e;", apagado, StringComparison.Ordinal);
        var armado = render.IndexOf("const partes = [", StringComparison.Ordinal);
        Assert.True(apagado > 0 && finRama > apagado && armado > finRama, "la rama de 'avisos apagados' tiene que ir antes de armar la tarjeta");
        var rama = render[apagado..finRama];
        Assert.Contains(@"tarjeta.classList.add(""oculto"")", rama);
        Assert.Contains("tarjeta.replaceChildren();", rama);
        Assert.Contains("return;", rama);
        // Solo después de eso se muestra (y antes de dibujarla).
        var mostrar = render.IndexOf(@"tarjeta.classList.remove(""oculto"")", finRama, StringComparison.Ordinal);
        Assert.True(mostrar > finRama && mostrar < armado, "la tarjeta se muestra recién cuando la API la ofrece");
    }

    [Fact]
    public void El_modulo_compartido_reconoce_el_motivo_y_no_arma_ni_el_link_ni_la_sugerencia()
    {
        var js = Modulo;

        Assert.Contains(@"const AVISOS_APAGADOS = ""avisos-apagados"";", js);
        Assert.Contains("return estado?.motivo === AVISOS_APAGADOS;", js);
        Assert.Matches(@"contacto\(estado\) \{\s+if \(YS\.whatsapp\.apagado\(estado\)\) return null;", js);          // sin link
        Assert.Contains("if (!estado || YS.whatsapp.apagado(estado) || estado.estado !== \"sin-decision\" || !estado.disponible) return false;", js);   // sin sugerencia
    }

    [Fact]
    public void El_registro_no_ofrece_la_casilla_si_la_API_no_manda_un_texto_disponible()
    {
        // El bloque nace oculto (otra prueba mira el HTML) y solo se muestra después de pedirle el texto a la API y recibirlo...
        Assert.Matches(@"textoWhatsApp = await YS\.whatsapp\.texto\(\);\s+if \(!textoWhatsApp\) return;", RegistroJs);
        // ...y la API, apagada, contesta disponible:false, que el módulo traduce en null.
        Assert.Contains("return t && t.disponible && t.version && Array.isArray(t.texto) ? t : null;", Modulo);
        // Sin texto no se manda nada de WhatsApp en el registro.
        Assert.Contains("const quiereWhatsApp = !!(textoWhatsApp && document.getElementById(\"aceptaWhatsApp\")?.checked);", RegistroJs);
    }

    [Fact]
    public void Ninguna_pantalla_del_paciente_ofrece_el_link_ni_la_sugerencia_por_su_cuenta()
    {
        // La sugerencia de después de un turno sale de un solo lugar (YS.whatsapp.sugerirTrasUnTurno), que respeta lo apagado.
        var llamadas = Regex.Matches(Dashboard, @"YS\.whatsapp\.sugerirTrasUnTurno\(").Count;
        Assert.Equal(1, llamadas);
        Assert.DoesNotContain("wa.me", Dashboard);
        Assert.DoesNotContain("wa.me", RegistroJs);
    }
}
