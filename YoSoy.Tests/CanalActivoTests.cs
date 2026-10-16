using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// Mensajeria:CanalActivo: "Simulado" (por defecto, no sale nada de la máquina) y WhatsApp por whatsapp-service con los dos nombres que
// existen, "Baileys" y "WhatsAppWebJs". Los dos registran el MISMO canal HTTP (CanalWhatsAppWebJs): la API no sabe qué librería usa
// whatsapp-service, solo habla su interfaz HTTP. Cualquier otro valor frena el arranque diciendo cuáles son los válidos.
//
// Ninguna prueba abre una conexión: el canal solo se registra en el contenedor de DI, nadie le manda nada a whatsapp-service.
public class CanalActivoTests
{
    private const string ClaveJwt = "clave-de-prueba-del-canal-activo-0123456789-abcdef";

    // UseSetting (no ConfigureAppConfiguration): AddMensajeria lee Mensajeria:CanalActivo de forma SÍNCRONA mientras se arma el host,
    // antes de que ConfigureAppConfiguration llegue a mezclarse (ver WhatsAppSecretoHeaderTests).
    private static WebApplicationFactory<Program> Fabrica(string? canalActivo) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", ClaveJwt);
            b.UseSetting("Mensajeria:WhatsApp:Secreto", "secreto-de-prueba-del-canal");
            if (canalActivo is not null) b.UseSetting("Mensajeria:CanalActivo", canalActivo);
        });

    [Fact]
    public void Sin_configurar_nada_el_canal_es_el_Simulado_igual_que_siempre()
    {
        using var fabrica = Fabrica(null);

        var canal = fabrica.Services.GetRequiredService<IMessageChannel>();

        Assert.IsType<CanalSimulado>(canal);
        Assert.Equal("Simulado", canal.Nombre);
    }

    [Fact]
    public void Simulado_explicito_tambien_registra_el_canal_simulado()
    {
        using var fabrica = Fabrica("Simulado");

        Assert.IsType<CanalSimulado>(fabrica.Services.GetRequiredService<IMessageChannel>());
    }

    [Theory]
    [InlineData("Baileys")]
    [InlineData("WhatsAppWebJs")]
    public void Los_dos_nombres_de_WhatsApp_registran_el_mismo_canal_HTTP(string valor)
    {
        using var fabrica = Fabrica(valor);

        var canal = fabrica.Services.GetRequiredService<IMessageChannel>();

        Assert.IsType<CanalWhatsAppWebJs>(canal);
        // Las conversaciones se guardan con este nombre: es el mismo con cualquiera de los dos, así cambiar de uno a otro no
        // parte en dos el historial de un paciente.
        Assert.Equal("WhatsApp", canal.Nombre);
        // Es un singleton: quien lo inyecta (NotificadorTurnos, BotReservasService) recibe siempre la misma instancia.
        Assert.Same(canal, fabrica.Services.GetRequiredService<IMessageChannel>());
    }

    [Theory]
    [InlineData("Baileys")]
    [InlineData("WhatsAppWebJs")]
    [InlineData("Simulado")]
    public void La_pantalla_de_administracion_de_WhatsApp_se_registra_con_cualquier_canal(string valor)
    {
        // Tiene que poder usarse para vincular el número ANTES de pasar el canal a WhatsApp.
        using var fabrica = Fabrica(valor);

        Assert.NotNull(fabrica.Services.GetRequiredService<WhatsAppAdminService>());
    }

    [Theory]
    [InlineData("WhatsApp")]
    [InlineData("baileys")]
    [InlineData("Oficial")]
    [InlineData("")]
    public void Un_valor_que_no_existe_frena_el_arranque_y_el_mensaje_lista_los_validos(string valor)
    {
        using var fabrica = Fabrica(valor);

        var ex = Record.Exception(() => fabrica.Services);

        // Vale también para "" (una variable Mensajeria__CanalActivo= vacía): el valor por defecto solo se usa si la clave no está.
        Assert.NotNull(ex);
        var mensajes = string.Join(" | ", Cadena(ex!).Select(e => e.Message));
        Assert.Contains("Mensajeria:CanalActivo", mensajes);
        Assert.Contains($"\"{valor}\"", mensajes);
        Assert.Contains("\"Simulado\"", mensajes);
        Assert.Contains("\"Baileys\"", mensajes);
        Assert.Contains("\"WhatsAppWebJs\"", mensajes);
    }

    private static IEnumerable<Exception> Cadena(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!) yield return e;
    }
}
