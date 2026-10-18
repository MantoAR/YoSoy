using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// GET /mensajeria/whatsapp/estado, POST /mensajeria/whatsapp/desvincular y
// POST /mensajeria/whatsapp/confirmar-vinculacion: solo rol Configurador (no Profesional),
// mismo patrón de CierreSeguridadTests para 401/403/200. whatsapp-service se simula con el
// mismo ServidorFalso (HttpListener local) que CanalBaileysTests —
// Mensajeria:WhatsAppServiceUrl apunta ahí en vez del puerto 3002 real: confirma que
// WhatsAppAdminService lee la URL de configuración (a diferencia de CanalBaileys, que
// la tiene fija en el código) y por lo tanto es testeable sin tocar el puerto real.
// El usuario Configurador de las pruebas de auditoría es PROPIO de cada prueba (DatosDePrueba): antes se
// usaba el usuario 1 de la base de desarrollo, y sin él el INSERT de LogAuditoria (clave foránea a Usuario)
// falla y la auditoría queda vacía.
[Trait("Categoria", "Integracion")]
public class WhatsAppAdminEndpointsTests
{
    private const string SecretoDePrueba = "secreto-admin-de-prueba";

    private sealed class ServidorFalso : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        public string BaseUrl { get; }
        public Func<HttpListenerRequest, (int Status, string Cuerpo)> Responder { get; set; }
            = _ => (200, """{"conectado":false,"numero":null,"qrPendiente":false,"qr":null}""");

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

    private static WebApplicationFactory<Program> Factory(string whatsAppServiceUrl) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mensajeria:WhatsApp:Secreto"] = SecretoDePrueba,
                ["Mensajeria:WhatsAppServiceUrl"] = whatsAppServiceUrl
            })));

    private static HttpClient ClienteCon(WebApplicationFactory<Program> factory, string token)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task LimpiarAuditoria(string accion, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var cmd = new SqlCommand("DELETE FROM LogAuditoria WHERE Entidad = 'WhatsApp' AND Accion = @Accion AND IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Accion", accion);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Estado_sin_token_da_401()
    {
        using var servidor = new ServidorFalso();
        using var factory = Factory(servidor.BaseUrl);

        var r = await factory.CreateClient().GetAsync("/mensajeria/whatsapp/estado");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Estado_con_rol_profesional_da_403()
    {
        using var servidor = new ServidorFalso();
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        var cliente = ClienteCon(factory, emisor.Emitir(2, Roles.Profesional, idProfesional: 1));

        var r = await cliente.GetAsync("/mensajeria/whatsapp/estado");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Estado_con_rol_configurador_devuelve_el_estado_real_incluido_el_qr()
    {
        using var servidor = new ServidorFalso { Responder = _ =>
            (200, """{"conectado":true,"numero":"5491112345678","qrPendiente":false,"qr":null}""") };
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        var cliente = ClienteCon(factory, emisor.Emitir(1, Roles.Configurador));

        var r = await cliente.GetAsync("/mensajeria/whatsapp/estado");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        var cuerpo = await r.Content.ReadFromJsonAsync<RespuestaEstadoDeTest>();
        Assert.True(cuerpo!.ServicioDisponible);
        Assert.True(cuerpo.Conectado);
        Assert.Equal("5491112345678", cuerpo.Numero);
    }

    [Fact]
    public async Task Estado_con_whatsapp_service_caido_devuelve_servicioDisponible_false_sin_error()
    {
        // Puerto loopback real que nadie escucha: connection refused real, no simulado.
        var puerto = ServidorFalso.PuertoLibre();
        using var factory = Factory($"http://127.0.0.1:{puerto}/");
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        var cliente = ClienteCon(factory, emisor.Emitir(1, Roles.Configurador));

        var r = await cliente.GetAsync("/mensajeria/whatsapp/estado");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await r.Content.ReadFromJsonAsync<RespuestaEstadoDeTest>();
        Assert.False(cuerpo!.ServicioDisponible);
    }

    [Fact]
    public async Task Desvincular_sin_token_da_401()
    {
        using var servidor = new ServidorFalso();
        using var factory = Factory(servidor.BaseUrl);

        var r = await factory.CreateClient().PostAsync("/mensajeria/whatsapp/desvincular", null);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Desvincular_con_rol_profesional_da_403()
    {
        using var servidor = new ServidorFalso();
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        var cliente = ClienteCon(factory, emisor.Emitir(2, Roles.Profesional, idProfesional: 1));

        var r = await cliente.PostAsync("/mensajeria/whatsapp/desvincular", null);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Desvincular_con_rol_configurador_responde_ok_y_audita_el_numero_desvinculado()
    {
        using var servidor = new ServidorFalso { Responder = req =>
            req.HttpMethod == "POST" ? (200, """{"exito":true}""") : (200, """{"conectado":true,"numero":"5491112345678","qrPendiente":false,"qr":null}""") };
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var idAdmin = datos.Usuario();
        var cliente = ClienteCon(factory, emisor.Emitir(idAdmin, Roles.Configurador));

        try
        {
            var r = await cliente.PostAsync("/mensajeria/whatsapp/desvincular", null);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            using var con = Conexion.ObtenerConexion();
            await con.OpenAsync();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 IdUsuario, Detalle FROM LogAuditoria WHERE Entidad = 'WhatsApp' AND Accion = 'Desvinculación de WhatsApp' AND IdUsuario = @Id ORDER BY FechaHora DESC", con);
            cmd.Parameters.AddWithValue("@Id", idAdmin);
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(idAdmin, reader.GetInt32(0));
            Assert.Contains("5491112345678", reader.GetString(1));
        }
        finally
        {
            await LimpiarAuditoria("Desvinculación de WhatsApp", idAdmin);
        }
    }

    [Fact]
    public async Task Desvincular_cuando_whatsapp_service_no_responde_da_503_y_no_audita()
    {
        var puerto = ServidorFalso.PuertoLibre();
        using var factory = Factory($"http://127.0.0.1:{puerto}/");
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var idAdmin = datos.Usuario();
        var cliente = ClienteCon(factory, emisor.Emitir(idAdmin, Roles.Configurador));

        var r = await cliente.PostAsync("/mensajeria/whatsapp/desvincular", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = 'WhatsApp' AND Accion = 'Desvinculación de WhatsApp' AND IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idAdmin);
        Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ConfirmarVinculacion_no_audita_si_el_estado_real_no_esta_conectado()
    {
        using var servidor = new ServidorFalso { Responder = _ =>
            (200, """{"conectado":false,"numero":null,"qrPendiente":true,"qr":"data:image/png;base64,xx"}""") };
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var idAdmin = datos.Usuario();
        var cliente = ClienteCon(factory, emisor.Emitir(idAdmin, Roles.Configurador));

        var r = await cliente.PostAsync("/mensajeria/whatsapp/confirmar-vinculacion", null);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = 'WhatsApp' AND Accion = 'Vinculación de WhatsApp' AND IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idAdmin);
        Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ConfirmarVinculacion_audita_si_el_estado_real_esta_conectado()
    {
        using var servidor = new ServidorFalso { Responder = _ =>
            (200, """{"conectado":true,"numero":"5491112345678","qrPendiente":false,"qr":null}""") };
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var idAdmin = datos.Usuario();
        var cliente = ClienteCon(factory, emisor.Emitir(idAdmin, Roles.Configurador));

        try
        {
            var r = await cliente.PostAsync("/mensajeria/whatsapp/confirmar-vinculacion", null);
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

            using var con = Conexion.ObtenerConexion();
            await con.OpenAsync();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 IdUsuario, Detalle FROM LogAuditoria WHERE Entidad = 'WhatsApp' AND Accion = 'Vinculación de WhatsApp' AND IdUsuario = @Id ORDER BY FechaHora DESC", con);
            cmd.Parameters.AddWithValue("@Id", idAdmin);
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(idAdmin, reader.GetInt32(0));
            Assert.Contains("5491112345678", reader.GetString(1));
        }
        finally
        {
            await LimpiarAuditoria("Vinculación de WhatsApp", idAdmin);
        }
    }

    // Partición por IP (no por el header Authorization crudo, ver Program.cs): 10 pedidos
    // en 5 minutos para la política "whatsapp-admin". El pedido 11 desde la misma IP tiene
    // que rechazarse (429); un X-Forwarded-For con otra IP, confiado porque la conexión real
    // en TestServer es loopback (mismo KnownProxies que ya usa esta app), tiene que seguir
    // teniendo su propio balde intacto.
    [Fact]
    public async Task Politica_whatsapp_admin_rechaza_pasado_el_limite_por_ip_y_otra_ip_tiene_balde_propio()
    {
        using var servidor = new ServidorFalso { Responder = _ => (200, """{"exito":true}""") };
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var idAdmin = datos.Usuario();
        var token = emisor.Emitir(idAdmin, Roles.Configurador);

        try
        {
            HttpResponseMessage? ultima = null;
            for (var i = 0; i < 10; i++)
            {
                var cliente = ClienteCon(factory, token);
                ultima = await cliente.PostAsync("/mensajeria/whatsapp/desvincular", null);
                Assert.NotEqual(HttpStatusCode.TooManyRequests, ultima.StatusCode);
            }

            var cliente11 = ClienteCon(factory, token);
            var r11 = await cliente11.PostAsync("/mensajeria/whatsapp/desvincular", null);
            Assert.Equal(HttpStatusCode.TooManyRequests, r11.StatusCode);

            var clienteOtraIp = ClienteCon(factory, token);
            clienteOtraIp.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
            var rOtraIp = await clienteOtraIp.PostAsync("/mensajeria/whatsapp/desvincular", null);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, rOtraIp.StatusCode);
        }
        finally
        {
            await LimpiarAuditoria("Desvinculación de WhatsApp", idAdmin);
        }
    }

    private record RespuestaEstadoDeTest(bool ServicioDisponible, bool Conectado, string? Numero, bool QrPendiente, string? Qr);
}
