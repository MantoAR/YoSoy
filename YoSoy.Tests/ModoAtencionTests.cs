using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Mensajeria:ModoAtencion: quién atiende lo que la gente le escribe al número de WhatsApp.
//  · "Asistente" (por defecto, y lo que la aplicación hace hoy): el asistente de reservas contesta solo.
//  · "Personal": el chat es para el personal (el asistente no contesta; ver AtencionPersonalTests).
// Un valor que no existe frena el arranque (decide si el asistente contesta o no: no puede caer en silencio en uno de los dos), y /capacidades le dice a la
// pantalla en cuál está. Mensajeria:Personal:RespuestaDeEmergencia viene apagada.
//
// Ninguna prueba abre una conexión ni toca la base: solo se arma la aplicación y se lee la configuración.
public class ModoAtencionTests
{
    private const string ClaveJwt = "clave-de-prueba-del-modo-de-atencion-0123456789-abcdef";

    // UseSetting (no ConfigureAppConfiguration): igual que CanalActivoTests, para que llegue antes de que se arme el contenedor.
    private static WebApplicationFactory<Program> Fabrica(string? modo = null, string? chatsHabilitados = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", ClaveJwt);
            if (modo is not null) b.UseSetting("Mensajeria:ModoAtencion", modo);
            if (chatsHabilitados is not null) b.UseSetting("Mensajeria:ChatSimuladoHabilitado", chatsHabilitados);
        });

    private static MensajeriaOptions OpcionesDe(WebApplicationFactory<Program> fabrica) =>
        fabrica.Services.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue;

    // ───────────── Los valores ─────────────

    [Fact]
    public void Sin_configurar_nada_el_modo_es_Asistente_y_el_mensaje_de_emergencia_esta_apagado()
    {
        var op = new MensajeriaOptions();

        Assert.Equal("Asistente", op.ModoAtencion);
        Assert.False(op.EsModoPersonal);
        Assert.False(op.Personal.RespuestaDeEmergencia);
    }

    [Theory]
    [InlineData("Personal", true)]
    [InlineData("personal", true)]
    [InlineData("PERSONAL", true)]
    [InlineData("  Personal  ", true)]
    [InlineData("Asistente", false)]
    [InlineData("asistente", false)]
    public void EsModoPersonal_no_distingue_mayusculas(string valor, bool esperado) =>
        Assert.Equal(esperado, new MensajeriaOptions { ModoAtencion = valor }.EsModoPersonal);

    [Theory]
    [InlineData("Asistente", true)]
    [InlineData("Personal", true)]
    [InlineData("personal", true)]
    [InlineData("asistente", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Otro", false)]
    [InlineData("Asistentes", false)]
    [InlineData("Simulado", false)]
    [InlineData(null, false)]
    public void Solo_Asistente_y_Personal_son_modos_validos(string? valor, bool esperado) =>
        Assert.Equal(esperado, ModosAtencion.EsValido(valor));

    [Fact]
    public void Un_valor_invalido_no_cuenta_como_modo_Personal()
    {
        // Por si algo lo lee sin validar: lo que no es "Personal" nunca apaga al asistente.
        Assert.False(new MensajeriaOptions { ModoAtencion = "Personl" }.EsModoPersonal);
        Assert.False(new MensajeriaOptions { ModoAtencion = "" }.EsModoPersonal);
    }

    // ───────────── appsettings.json ─────────────

    [Fact]
    public void El_appsettings_del_repositorio_deja_el_modo_Asistente_y_la_emergencia_apagada()
    {
        // El valor por defecto en el archivo no cambia lo que hace la aplicación publicada: sin Mensajeria__ModoAtencion en el servidor, todo sigue igual.
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var mensajeria = json.RootElement.GetProperty("Mensajeria");

        Assert.Equal("Asistente", mensajeria.GetProperty("ModoAtencion").GetString());
        Assert.False(mensajeria.GetProperty("Personal").GetProperty("RespuestaDeEmergencia").GetBoolean());
    }

    // ───────────── La aplicación ─────────────

    [Fact]
    public void La_aplicacion_arranca_en_modo_Asistente_si_no_se_configura_nada()
    {
        using var fabrica = Fabrica();

        var op = OpcionesDe(fabrica);

        Assert.Equal("Asistente", op.ModoAtencion);
        Assert.False(op.EsModoPersonal);
        Assert.False(op.Personal.RespuestaDeEmergencia);
    }

    [Theory]
    [InlineData("Personal")]
    [InlineData("personal")]
    public void Con_Mensajeria_ModoAtencion_Personal_la_aplicacion_atiende_en_modo_Personal(string valor)
    {
        using var fabrica = Fabrica(valor);

        Assert.True(OpcionesDe(fabrica).EsModoPersonal);
    }

    [Theory]
    [InlineData("Otro")]
    [InlineData("Personl")]
    [InlineData("Simulado")]
    [InlineData("")]
    public void Un_modo_que_no_existe_frena_el_arranque_y_el_mensaje_dice_cuales_son(string valor)
    {
        using var fabrica = Fabrica(valor);

        var ex = Record.Exception(() => fabrica.Services);

        // Vale también para "" (una variable Mensajeria__ModoAtencion= vacía): el valor por defecto solo se usa si la clave no está.
        Assert.NotNull(ex);
        var mensajes = string.Join(" | ", Cadena(ex!).Select(e => e.Message));
        Assert.Contains("Mensajeria:ModoAtencion", mensajes);
        Assert.Contains($"\"{valor}\"", mensajes);            // y dice cuál fue el valor que no sirvió
        Assert.Contains("\"Asistente\"", mensajes);
        Assert.Contains("\"Personal\"", mensajes);
    }

    // ───────────── /capacidades ─────────────

    [Fact]
    public async Task Capacidades_dice_que_el_modo_es_asistente_por_defecto()
    {
        using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();

        var r = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");

        Assert.True(r.GetProperty("mensajeria").GetBoolean());
        Assert.Equal("asistente", r.GetProperty("modoAtencion").GetString());
    }

    [Fact]
    public async Task Capacidades_dice_que_el_modo_es_personal()
    {
        using var fabrica = Fabrica("Personal");
        using var cliente = fabrica.CreateClient();

        var r = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");

        Assert.True(r.GetProperty("mensajeria").GetBoolean());
        Assert.Equal("personal", r.GetProperty("modoAtencion").GetString());
    }

    [Fact]
    public async Task Con_los_chats_apagados_capacidades_no_informa_el_modo()
    {
        using var fabrica = Fabrica("Personal", chatsHabilitados: "false");
        using var cliente = fabrica.CreateClient();

        var r = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");

        Assert.False(r.GetProperty("mensajeria").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("modoAtencion").ValueKind);
    }

    [Fact]
    public async Task Un_pedido_por_proxy_ve_los_chats_como_remotos_y_no_se_le_informa_el_modo()
    {
        using var fabrica = Fabrica("Personal");
        using var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        var r = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");

        Assert.False(r.GetProperty("mensajeria").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("modoAtencion").ValueKind);
    }

    private static IEnumerable<Exception> Cadena(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!) yield return e;
    }
}
