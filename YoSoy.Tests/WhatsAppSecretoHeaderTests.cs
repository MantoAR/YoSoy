using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Regresión puntual: CanalWhatsAppWebJs llegó a mergearse (Etapa 1) sin enviar nunca el
// X-Shared-Secret que whatsapp-service exige en /enviar, /estado y /desvincular — un bug
// real, invisible para los tests existentes de esas clases porque construyen el HttpClient
// a mano (CrearCanal en CanalWhatsAppWebJsTests), sin pasar por el delegado de
// AddHttpClient<T> que es donde se agrega el header
// (MensajeriaServiceCollectionExtensions.AgregarSecretoWhatsApp). Este archivo prueba la
// registración real en el contenedor de DI (WebApplicationFactory + IHttpClientFactory),
// no la clase aislada, para que una regresión futura en ese delegado sí se note. No hace
// falta ningún pedido HTTP real: DefaultRequestHeaders ya está armado apenas se crea el
// HttpClient desde la fábrica.
public class WhatsAppSecretoHeaderTests
{
    private const string SecretoDePrueba = "secreto-header-de-prueba";

    // UseSetting (no ConfigureAppConfiguration): MensajeriaServiceCollectionExtensions.
    // AddMensajeria lee Mensajeria:CanalActivo de forma SÍNCRONA, una sola vez, para decidir
    // el switch de registración (a diferencia del secreto, que WhatsAppAdminService/
    // CanalWhatsAppWebJs leen recién al crear el HttpClient, ya con la app corriendo) — y esa
    // lectura pasa mientras se arma el host, antes de que ConfigureAppConfiguration llegue a
    // mezclarse (comprobado: con ConfigureAppConfiguration, CanalActivo seguía viendo
    // "Simulado" y CanalWhatsAppWebJs ni se registraba). UseSetting sí está disponible a
    // tiempo para ese caso.
    private static WebApplicationFactory<Program> Factory(string canalActivo) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mensajeria:CanalActivo", canalActivo);
            b.UseSetting("Mensajeria:WhatsApp:Secreto", SecretoDePrueba);
        });

    [Theory]
    [InlineData("WhatsAppWebJs")]
    [InlineData("Baileys")]
    public void CanalWhatsAppWebJs_agrega_el_header_X_Shared_Secret_con_el_valor_configurado(string canalActivo)
    {
        // CanalWhatsAppWebJs solo se registra en el contenedor cuando CanalActivo es
        // "WhatsAppWebJs" o "Baileys" (default "Simulado") — ver el switch en
        // MensajeriaServiceCollectionExtensions.
        using var factory = Factory(canalActivo);
        var fabrica = factory.Services.GetRequiredService<IHttpClientFactory>();

        var http = fabrica.CreateClient(nameof(CanalWhatsAppWebJs));

        Assert.True(http.DefaultRequestHeaders.TryGetValues("X-Shared-Secret", out var valores),
            "CanalWhatsAppWebJs no está mandando X-Shared-Secret — whatsapp-service rechazaría /enviar con 401.");
        Assert.Equal(SecretoDePrueba, Assert.Single(valores!));
    }

    [Fact]
    public void WhatsAppAdminService_agrega_el_header_X_Shared_Secret_con_el_valor_configurado()
    {
        // A diferencia de CanalWhatsAppWebJs, WhatsAppAdminService se registra sin
        // condición — con cualquier CanalActivo, incluido el default "Simulado".
        using var factory = Factory("Simulado");
        var fabrica = factory.Services.GetRequiredService<IHttpClientFactory>();

        var http = fabrica.CreateClient(nameof(WhatsAppAdminService));

        Assert.True(http.DefaultRequestHeaders.TryGetValues("X-Shared-Secret", out var valores),
            "WhatsAppAdminService no está mandando X-Shared-Secret — whatsapp-service rechazaría /estado y /desvincular con 401.");
        Assert.Equal(SecretoDePrueba, Assert.Single(valores!));
    }
}
