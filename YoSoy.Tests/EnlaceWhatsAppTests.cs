using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// El link wa.me (opción B del consentimiento): el paciente le escribe PRIMERO al número de la clínica. El número sale de la configuración y
// nada está escrito en el código ni en las pantallas; sin número, no hay link. Lo que sale va a la URL de otra aplicación: se arma siempre
// con el texto codificado y acotado.
public class EnlaceWhatsAppTests
{
    // ───────────── El número ─────────────

    [Theory]
    [InlineData("+54 9 11 5555-0000", "5491155550000")]
    [InlineData("011 5555-0000", "5491155550000")]
    [InlineData("011 15 5555-0000", "5491155550000")]
    [InlineData("(011) 5555-0000", "5491155550000")]
    [InlineData("1155550000", "5491155550000")]
    [InlineData("5491155550000", "5491155550000")]
    [InlineData("+5491155550000", "5491155550000")]
    [InlineData("0351 15 412-3456", "5493514123456")]
    [InlineData("  +54 351 412 3456  ", "5493514123456")]
    public void Un_celular_argentino_queda_como_lo_espera_wa_me_54_9_area_y_abonado(string configurado, string esperado)
    {
        Assert.Equal(esperado, EnlaceWhatsApp.NumeroParaEnlace(configurado));
    }

    [Theory]
    [InlineData("+598 99 123 456", "59899123456")]       // Uruguay
    [InlineData("+1 415 555 2671", "14155552671")]       // Estados Unidos
    [InlineData("+34 612 345 678", "34612345678")]       // España
    public void Un_numero_de_otro_pais_solo_vale_si_viene_con_mas(string configurado, string esperado)
    {
        Assert.Equal(esperado, EnlaceWhatsApp.NumeroParaEnlace(configurado));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("5555-0000")]                  // sin código de área
    [InlineData("598 99 123 456")]             // de otro país pero sin "+": no se adivina
    [InlineData("+0 123 456 789")]             // el código de país no empieza con 0
    [InlineData("+12345")]                     // demasiado corto
    [InlineData("+1234567890123456")]          // demasiado largo
    [InlineData("+54 9 11 5555-000a")]         // con una letra
    [InlineData("javascript:alert(1)")]
    [InlineData("https://wa.me/5491155550000")]
    public void Lo_que_no_es_un_numero_utilizable_no_da_link(string? configurado)
    {
        Assert.Null(EnlaceWhatsApp.NumeroParaEnlace(configurado));
        Assert.Null(EnlaceWhatsApp.Construir(configurado, "Hola"));
    }

    // ───────────── El link ─────────────

    [Fact]
    public void El_link_lleva_el_numero_y_un_texto_de_inicio_neutro_por_defecto()
    {
        Assert.Equal("Hola", EnlaceWhatsApp.TextoPorDefecto);
        Assert.Equal("https://wa.me/5491155550000?text=Hola", EnlaceWhatsApp.Construir("+54 9 11 5555-0000", EnlaceWhatsApp.TextoPorDefecto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("\r\n\t")]
    public void Sin_texto_de_inicio_el_link_va_sin_mensaje_precargado(string? texto)
    {
        Assert.Equal("https://wa.me/5491155550000", EnlaceWhatsApp.Construir("11 5555-0000", texto));
    }

    [Theory]
    [InlineData("Hola, quiero un turno", "Hola%2C%20quiero%20un%20turno")]
    [InlineData("¡Hola! ¿Cómo están?", "%C2%A1Hola%21%20%C2%BFC%C3%B3mo%20est%C3%A1n%3F")]
    [InlineData("Hola & chau", "Hola%20%26%20chau")]                  // el & no abre otro parámetro
    [InlineData("Hola #1", "Hola%20%231")]                              // el # no corta la URL
    [InlineData("Hola\r\nBCC: otro@x.com", "Hola%20BCC%3A%20otro%40x.com")]   // sin saltos de línea
    [InlineData("  Hola   mundo  ", "Hola%20mundo")]                    // sin espacios de más
    [InlineData("Hola 😀", "Hola%20%F0%9F%98%80")]
    public void El_texto_va_codificado_y_limpio(string texto, string codificado)
    {
        Assert.Equal($"https://wa.me/5491155550000?text={codificado}", EnlaceWhatsApp.Construir("11 5555-0000", texto));
    }

    [Fact]
    public void El_texto_tiene_un_tope_de_200_caracteres()
    {
        var link = EnlaceWhatsApp.Construir("11 5555-0000", new string('a', 500))!;

        var texto = Uri.UnescapeDataString(link[(link.IndexOf("?text=", StringComparison.Ordinal) + 6)..]);
        Assert.Equal(200, texto.Length);
    }

    [Theory]
    [InlineData("Hola")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("'; DROP TABLE Paciente;--")]
    [InlineData("%0d%0aSet-Cookie: x=1")]
    [InlineData("https://sitio-malo.example/?a=b&c=d#e")]
    [InlineData("ñandú 🦆 \u202e")]
    public void El_link_siempre_tiene_la_forma_de_wa_me_y_solo_caracteres_seguros(string texto)
    {
        var link = EnlaceWhatsApp.Construir("+54 9 11 5555-0000", texto)!;

        Assert.Matches(@"^https://wa\.me/\d{8,15}(\?text=[A-Za-z0-9%._~-]*)?$", link);
    }

    // ───────────── El servicio según la configuración ─────────────

    private static (ContactoWhatsApp Contacto, OpcionesFijas Opciones, LogsCapturados Logs) Armar(bool llave = true)
    {
        var opciones = new OpcionesFijas();
        opciones.CurrentValue.AvisosPorWhatsApp = llave;   // el link es parte de los avisos por WhatsApp: estas pruebas son con la llave prendida (con la apagada no hay link)
        var logs = new LogsCapturados();
        return (new ContactoWhatsApp(opciones, logs.CreateLogger<ContactoWhatsApp>()), opciones, logs);
    }

    [Fact]
    public void Sin_numero_configurado_no_hay_link_ni_aviso_en_el_log()
    {
        var (contacto, _, logs) = Armar();

        Assert.Null(contacto.Enlace());
        Assert.Empty(logs.Lineas);
    }

    [Fact]
    public void Con_un_numero_valido_hay_link_y_los_cambios_de_configuracion_se_ven_sin_reiniciar()
    {
        var (contacto, opciones, _) = Armar();

        opciones.CurrentValue.WhatsAppNumeroClinica = "+54 9 11 5555-0000";
        Assert.Equal("https://wa.me/5491155550000?text=Hola", contacto.Enlace());

        opciones.CurrentValue.WhatsAppTextoInicio = "Buen día";
        Assert.Equal("https://wa.me/5491155550000?text=Buen%20d%C3%ADa", contacto.Enlace());

        opciones.CurrentValue.WhatsAppNumeroClinica = "";
        Assert.Null(contacto.Enlace());
    }

    [Fact]
    public void Un_numero_que_no_sirve_no_da_link_y_avisa_una_sola_vez_sin_poner_el_valor_en_el_log()
    {
        var (contacto, opciones, logs) = Armar();
        opciones.CurrentValue.WhatsAppNumeroClinica = "numero-raro-5555-0000";

        for (var i = 0; i < 5; i++) Assert.Null(contacto.Enlace());

        Assert.Single(logs.Lineas, l => l.StartsWith("Warning|"));
        Assert.DoesNotContain("numero-raro", logs.Todo);
        Assert.DoesNotContain("5555", logs.Todo);
    }

    [Fact]
    public void Por_defecto_la_configuracion_no_trae_ningun_numero_y_el_texto_es_Hola()
    {
        var o = new MensajeriaOptions();

        Assert.Equal("", o.WhatsAppNumeroClinica);
        Assert.Equal("Hola", o.WhatsAppTextoInicio);
    }

    // ───────────── Nada de números escritos en el código ni en las pantallas ─────────────

    [Fact]
    public void Ningun_archivo_del_producto_trae_un_link_wa_me_con_un_numero_escrito()
    {
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        var propios = new[] { "Services", "Endpoints", "DAOs", "Models", "PANTALLAS", "whatsapp-service" }
            .Select(c => Path.Combine(raiz, c)).Where(Directory.Exists)
            .SelectMany(c => Directory.EnumerateFiles(c, "*", SearchOption.AllDirectories))
            .Where(f => new[] { ".cs", ".js", ".html", ".json", ".css", ".txt" }.Contains(Path.GetExtension(f))
                        && !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}")
                        && !f.EndsWith("package-lock.json"))
            .Concat(new[] { Path.Combine(raiz, "appsettings.json") });

        var conNumero = propios.Where(f => Regex.IsMatch(File.ReadAllText(f), @"wa\.me/\+?\d")).ToList();

        Assert.True(conNumero.Count == 0, "Hay un link wa.me con un número escrito (tiene que salir de Mensajeria:WhatsAppNumeroClinica):\n" + string.Join("\n", conNumero));
    }

    [Fact]
    public void Las_pantallas_no_arman_links_de_WhatsApp_a_mano_los_recibe_de_la_API()
    {
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        var pantallas = Directory.EnumerateFiles(Path.Combine(raiz, "PANTALLAS"), "*", SearchOption.AllDirectories)
            .Where(f => new[] { ".js", ".html" }.Contains(Path.GetExtension(f)) && !f.Contains($"{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"wa\.me/|api\.whatsapp\.com/send|web\.whatsapp\.com/send", RegexOptions.IgnoreCase))
            .Select(Path.GetFileName).ToList();

        Assert.True(pantallas.Count == 0, "Estas pantallas arman un link de WhatsApp por su cuenta (tiene que venir de la API, contacto.enlace):\n" + string.Join("\n", pantallas));
    }
}

// Lo que dicen los endpoints del consentimiento sobre el contacto: con la aplicación real y la configuración por servidor.
public class EnlaceWhatsAppEndpointsTests
{
    private sealed class App : IDisposable
    {
        public readonly VideoApp Video;
        public readonly FakeConsentimientoWhatsAppRepo Repo = new();

        /// <param name="llave">Mensajeria:AvisosPorWhatsApp. Prendida por defecto: estas pruebas son del link con el consentimiento encendido.</param>
        public App(string? numero, string? texto = null, bool llave = true)
        {
            Repo.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaWhatsApp("11 5555-0001", new DateTime(1990, 5, 5), false);
            var config = new Dictionary<string, string?> { ["Mensajeria:AvisosPorWhatsApp"] = llave ? "true" : "false" };
            if (numero is not null) config["Mensajeria:WhatsAppNumeroClinica"] = numero;
            if (texto is not null) config["Mensajeria:WhatsAppTextoInicio"] = texto;
            Video = new VideoApp(config, servicios: s =>
            {
                s.RemoveAll<IConsentimientoWhatsAppRepositorio>();
                s.AddSingleton<IConsentimientoWhatsAppRepositorio>(Repo);
                s.RemoveAll<IAuditoriaConsentimientoWhatsApp>();
                s.AddSingleton<IAuditoriaConsentimientoWhatsApp>(new FakeAuditoriaConsentimientoWhatsApp());
                s.RemoveAll<ConsentimientoWhatsAppService>();
                s.AddSingleton(sp => new ConsentimientoWhatsAppService(Repo, sp.GetRequiredService<ITextoConsentimientoWhatsApp>(),
                    new FakeAuditoriaConsentimientoWhatsApp(), sp.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>(), Video!.Reloj,
                    sp.GetRequiredService<ILogger<ConsentimientoWhatsAppService>>()));
            });
        }

        public void Dispose() => Video.Dispose();
    }

    private static async Task<JsonElement> Estado(HttpClient c) => await VideoApp.Json(await c.GetAsync("/consentimiento-whatsapp"));

    [Fact]
    public async Task Con_un_numero_configurado_el_estado_trae_el_link_para_escribirle_a_la_clinica()
    {
        using var app = new App("+54 9 11 5555-0000");

        var j = await Estado(app.Video.Paciente());

        Assert.Equal("https://wa.me/5491155550000?text=Hola", j.GetProperty("contacto").GetProperty("enlace").GetString());
        Assert.Equal(new[] { "enlace" }, j.GetProperty("contacto").EnumerateObject().Select(p => p.Name));   // y nada más: ni el número suelto
    }

    [Fact]
    public async Task El_texto_de_inicio_se_configura_por_servidor()
    {
        using var app = new App("011 5555-0000", "Buen día");

        var j = await Estado(app.Video.Paciente());

        Assert.Equal("https://wa.me/5491155550000?text=Buen%20d%C3%ADa", j.GetProperty("contacto").GetProperty("enlace").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es un numero")]
    public async Task Sin_numero_o_con_uno_que_no_sirve_el_contacto_es_null_y_las_pantallas_no_muestran_nada(string? numero)
    {
        using var app = new App(numero);

        var j = await Estado(app.Video.Paciente());

        Assert.True(j.TryGetProperty("contacto", out var contacto));
        Assert.Equal(JsonValueKind.Null, contacto.ValueKind);
    }

    [Fact]
    public async Task Aceptar_y_revocar_tambien_devuelven_el_contacto()
    {
        using var app = new App("+54 9 11 5555-0000");
        var paciente = app.Video.Paciente();

        var acepta = await VideoApp.Json(await paciente.PostAsJsonAsync("/consentimiento-whatsapp/aceptar", new { version = "v0-provisorio" }));
        var revoca = await VideoApp.Json(await paciente.PostAsync("/consentimiento-whatsapp/revocar", null));

        Assert.Equal("https://wa.me/5491155550000?text=Hola", acepta.GetProperty("contacto").GetProperty("enlace").GetString());
        Assert.Equal("https://wa.me/5491155550000?text=Hola", revoca.GetProperty("contacto").GetProperty("enlace").GetString());
    }

    [Fact]
    public async Task El_texto_publico_del_registro_no_trae_el_contacto_ni_el_numero_de_la_clinica()
    {
        using var app = new App("+54 9 11 5555-0000");

        var respuesta = await app.Video.SinSesion().GetAsync("/consentimiento-whatsapp/texto");
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain("wa.me", cuerpo);
        Assert.DoesNotContain("5491155550000", cuerpo);
        Assert.False(JsonDocument.Parse(cuerpo).RootElement.TryGetProperty("contacto", out _));
    }

    [Fact]
    public async Task Los_errores_no_traen_el_contacto()
    {
        using var app = new App("+54 9 11 5555-0000");

        var r = await app.Video.Profesional().GetAsync("/consentimiento-whatsapp");
        var cuerpo = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.DoesNotContain("wa.me", cuerpo);
    }
}
