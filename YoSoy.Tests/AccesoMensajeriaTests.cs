using System.Net;
using Microsoft.AspNetCore.Http;

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
}
