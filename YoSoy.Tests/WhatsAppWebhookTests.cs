using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

// POST /mensajeria/whatsapp/entrante: webhook real de whatsapp-service. Doble candado
// propio (no depende de ChatSimuladoHabilitado/SoloLocal) — secreto compartido Y pinta de
// pedido local (EsLocal, mismo criterio ya probado en AccesoMensajeriaTests para
// /capacidades). Las dos fallas devuelven 404, nunca 401/403, para no confirmar que el
// endpoint existe ni cuál de las dos cosas falló.
[Trait("Categoria", "Integracion")]
public class WhatsAppWebhookTests
{
    private const string SecretoDePrueba = "secreto-de-prueba-123";

    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mensajeria:WhatsApp:Secreto"] = SecretoDePrueba
            })));

    [Fact]
    public async Task Secreto_correcto_y_pedido_local_responde_2xx_y_delega_al_bot()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Shared-Secret", SecretoDePrueba);

        // Teléfono inexistente a propósito: lo único que importa acá es que el pedido
        // pase los dos candados y llegue al pipeline real de BotReservasService (que ya
        // tiene sus propios tests) — no se valida el contenido de la respuesta del bot.
        // Igual crea una Conversacion real (BotReservasService.ProcesarAsync la persiste),
        // así que se limpia en el finally como el resto de este arnés.
        var marca = "549" + Guid.NewGuid().ToString("N")[..10];
        try
        {
            var r = await client.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono = marca, texto = "hola" });
            Assert.True(r.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)r.StatusCode}");
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            var telefonoNormalizado = Telefono.Normalizar(marca);
            using (var del = new SqlCommand(
                "DELETE FROM MensajeChat WHERE IdConversacion IN (SELECT IdConversacion FROM Conversacion WHERE Telefono = @t)", con))
            { del.Parameters.AddWithValue("@t", telefonoNormalizado); await del.ExecuteNonQueryAsync(); }
            using (var del = new SqlCommand("DELETE FROM Conversacion WHERE Telefono = @t", con))
            { del.Parameters.AddWithValue("@t", telefonoNormalizado); await del.ExecuteNonQueryAsync(); }
        }
    }

    [Fact]
    public async Task Secreto_incorrecto_responde_404()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Shared-Secret", "no-es-el-secreto");

        var r = await client.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono = "5491112345678", texto = "hola" });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Sin_secreto_responde_404()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var r = await client.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono = "5491112345678", texto = "hola" });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Secreto_correcto_pero_con_pinta_de_pedido_remoto_responde_404()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Shared-Secret", SecretoDePrueba);
        // Mismo truco que en AccesoMensajeriaTests: un X-Forwarded-For presente alcanza
        // para que EsLocal() trate el pedido como remoto, haya pasado o no por un proxy real.
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        var r = await client.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono = "5491112345678", texto = "hola" });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
