using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

// CanalBaileys: sin librería de mocking HTTP (no hay ninguna en este repo, ver
// Fakes.cs para el patrón real — fakes a mano implementando las interfaces de la app).
// Para este canal, que si necesita HTTP real para probarse, un HttpListener local liviano
// simula whatsapp-service. Ningún test toca la base ni un WhatsApp real.
public class CanalBaileysTests
{
    // Simula las respuestas de whatsapp-service en un puerto loopback real, sin arrancar
    // Node ni nada externo al proceso de test.
    private sealed class ServidorFalso : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        public string BaseUrl { get; }
        public Func<HttpListenerRequest, (int Status, string Cuerpo)> Responder { get; set; }
            = _ => (200, """{"exito":true,"idExterno":"cola-1"}""");

        public ServidorFalso()
        {
            var puerto = PuertoLibre();
            BaseUrl = $"http://127.0.0.1:{puerto}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = EscucharAsync();
        }

        private async Task EscucharAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch (Exception) { break; } // el listener se cerró (Dispose)

                var (status, cuerpo) = Responder(ctx.Request);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(cuerpo);
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.OutputStream.Close();
            }
        }

        public static int PuertoLibre()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
        }
    }

    private static CanalBaileys CrearCanal(string baseUrl) =>
        new(new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) },
            NullLogger<CanalBaileys>.Instance);

    [Fact]
    public async Task Envio_exitoso_devuelve_resultado_con_idExterno()
    {
        using var servidor = new ServidorFalso { Responder = _ => (200, """{"exito":true,"idExterno":"cola-7"}""") };
        var canal = CrearCanal(servidor.BaseUrl);

        var r = await canal.EnviarAsync("5491112345678", "hola");

        Assert.True(r.Exito);
        Assert.Equal("cola-7", r.IdExterno);
    }

    [Fact]
    public async Task Numero_sin_whatsapp_devuelve_resultado_fallido_sin_excepcion()
    {
        using var servidor = new ServidorFalso { Responder = _ => (200, """{"exito":false,"error":"Ese número no tiene WhatsApp."}""") };
        var canal = CrearCanal(servidor.BaseUrl);

        var r = await canal.EnviarAsync("5491112345678", "hola");

        Assert.False(r.Exito);
        Assert.Equal("Ese número no tiene WhatsApp.", r.Error);
    }

    [Fact]
    public async Task Cola_llena_503_devuelve_resultado_fallido_sin_excepcion()
    {
        using var servidor = new ServidorFalso { Responder = _ => (503, """{"exito":false,"error":"Cola llena, probá de nuevo en unos minutos."}""") };
        var canal = CrearCanal(servidor.BaseUrl);

        var r = await canal.EnviarAsync("5491112345678", "hola");

        Assert.False(r.Exito);
        Assert.Contains("Cola llena", r.Error);
    }

    [Fact]
    public async Task Servicio_caido_devuelve_resultado_fallido_sin_excepcion()
    {
        // Puerto loopback real que nadie escucha: connection refused real, no simulado.
        var puerto = ServidorFalso.PuertoLibre();
        var canal = CrearCanal($"http://127.0.0.1:{puerto}/");

        var r = await canal.EnviarAsync("5491112345678", "hola");

        Assert.False(r.Exito);
        Assert.NotNull(r.Error);
    }
}
