using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

// Punto de control del módulo de mensajería (MensajeriaEndpoints.Disponible): lo usan el
// filtro del grupo /mensajeria (404) y /capacidades (que decide si se ve el botón Chats).
public class AccesoMensajeriaTests
{
    private static MensajeriaOptions Op(bool habilitado, bool soloLocal) =>
        new() { ChatSimuladoHabilitado = habilitado, SoloLocal = soloLocal };

    private static HttpContext Pedido(string ip = "127.0.0.1", string? encabezadoProxy = null)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (encabezadoProxy != null) http.Request.Headers[encabezadoProxy] = "203.0.113.7";
        return http;
    }

    [Fact]
    public void Apagado_no_esta_disponible_ni_desde_la_propia_maquina() =>
        Assert.False(MensajeriaEndpoints.Disponible(Op(false, true), Pedido()));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Encendido_y_pedido_local_esta_disponible(string ip) =>
        Assert.True(MensajeriaEndpoints.Disponible(Op(true, true), Pedido(ip)));

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("CF-Connecting-IP")]
    public void Con_SoloLocal_lo_que_llega_por_proxy_no_esta_disponible(string encabezado) =>
        Assert.False(MensajeriaEndpoints.Disponible(Op(true, true), Pedido("127.0.0.1", encabezado)));

    [Fact]
    public void Con_SoloLocal_otra_maquina_no_esta_disponible() =>
        Assert.False(MensajeriaEndpoints.Disponible(Op(true, true), Pedido("192.168.0.20")));

    [Fact]
    public void Sin_SoloLocal_acepta_otras_maquinas() =>
        Assert.True(MensajeriaEndpoints.Disponible(Op(true, false), Pedido("192.168.0.20")));

    // Pruebas de arriba: Disponible()/EsLocal() en aislamiento, con un HttpContext armado a
    // mano. Esta corre por el pipeline real (UseForwardedHeaders incluido, agregado junto con
    // fix/recuperacion-ajustes) para confirmar que un pedido público detrás de Caddy sigue
    // contando como remoto después de ese cambio, verificado antes a mano con curl contra el
    // server real: sin X-Forwarded-For, local; con X-Forwarded-For (lo que pone Caddy) o
    // CF-Connecting-IP, remoto — en los dos casos, aunque UseForwardedHeaders confíe en el
    // peer loopback que usa WebApplicationFactory internamente.
    [Fact]
    public async Task Capacidades_por_el_pipeline_real_pedido_local_lo_ve_habilitado()
    {
        using var factory = FactoryConHabilitado();
        using var client = factory.CreateClient();

        var r = await client.GetFromJsonAsync<JsonElement>("/capacidades");
        Assert.True(r.GetProperty("mensajeria").GetBoolean());
    }

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("CF-Connecting-IP")]
    public async Task Capacidades_por_el_pipeline_real_pedido_via_proxy_lo_ve_remoto(string encabezado)
    {
        using var factory = FactoryConHabilitado();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(encabezado, "203.0.113.7");

        var r = await client.GetFromJsonAsync<JsonElement>("/capacidades");
        Assert.False(r.GetProperty("mensajeria").GetBoolean());
    }

    private static WebApplicationFactory<Program> FactoryConHabilitado() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mensajeria:ChatSimuladoHabilitado"] = "true",
                ["Mensajeria:SoloLocal"] = "true"
            })));
}
