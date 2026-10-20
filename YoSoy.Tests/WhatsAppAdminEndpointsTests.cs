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
    }

    // ── Estados, pedido de código QR y aviso de la bandeja (Fase 2 de WhatsApp) ─────────────────────────────────────────────

    // whatsapp-service falso que contesta según la ruta (estado, salud, vincular, desvincular) y anota qué se le pidió.
    private sealed record Respuestas(string Estado, string Salud = """{"estado":"x","desde":"2026-10-20T15:30:00.000Z","intentosDeReconexion":0}""",
        string Vincular = """{"exito":true,"estado":"requiere_vincular","motivo":"esperando_escaneo","qrPendiente":false}""");

    private static ServidorFalso ServicioFalso(Respuestas r, System.Collections.Concurrent.ConcurrentQueue<string> pedidos) =>
        new() { Responder = req =>
        {
            var ruta = req.Url!.AbsolutePath;
            pedidos.Enqueue($"{req.HttpMethod} {ruta}");
            return ruta switch
            {
                "/estado" => (200, r.Estado),
                "/salud" => (200, r.Salud),
                "/vincular" => (200, r.Vincular),
                "/desvincular" => (200, """{"exito":true}"""),
                _ => (404, "{}")
            };
        } };

    private const string EstadoQrVencido = """{"conectado":false,"numero":null,"qrPendiente":false,"qr":null,"estado":"requiere_vincular","motivo":"qr_vencido"}""";
    private const string EstadoEsperandoEscaneo = """{"conectado":false,"numero":null,"qrPendiente":true,"qr":"data:image/png;base64,xx","estado":"requiere_vincular","motivo":"esperando_escaneo"}""";

    private static async Task<(int Cantidad, string? Detalle)> AuditoriaDe(int idUsuario, string accion)
    {
        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT COUNT(*), MAX(Detalle) FROM LogAuditoria WHERE Entidad = 'WhatsApp' AND Accion = @A AND IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@A", accion);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    [Fact]
    public async Task Estado_trae_el_estado_el_motivo_y_desde_cuando_del_servicio()
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas(EstadoQrVencido,
            Salud: """{"estado":"requiere_vincular","desde":"2026-10-20T15:30:00.000Z","intentosDeReconexion":4}"""), pedidos);
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        var cliente = ClienteCon(factory, emisor.Emitir(1, Roles.Configurador));

        var r = await cliente.GetAsync("/mensajeria/whatsapp/estado");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j.GetProperty("servicioDisponible").GetBoolean());
        Assert.Equal("requiere_vincular", j.GetProperty("estado").GetString());
        Assert.Equal("qr_vencido", j.GetProperty("motivo").GetString());
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero), j.GetProperty("desde").GetDateTimeOffset());
        Assert.Equal(4, j.GetProperty("intentosDeReconexion").GetInt32());
    }

    [Fact]
    public async Task Estado_sigue_andando_si_solo_falla_salud()
    {
        using var servidor = new ServidorFalso { Responder = req => req.Url!.AbsolutePath == "/salud" ? (500, "no") : (200, EstadoQrVencido) };
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        var cliente = ClienteCon(factory, emisor.Emitir(1, Roles.Configurador));

        var r = await cliente.GetAsync("/mensajeria/whatsapp/estado");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j.GetProperty("servicioDisponible").GetBoolean());
        Assert.Equal("qr_vencido", j.GetProperty("motivo").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, j.GetProperty("desde").ValueKind);
    }

    [Fact]
    public async Task Vincular_sin_token_da_401_y_profesional_o_CRM_dan_403()
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas(EstadoQrVencido), pedidos);
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/mensajeria/whatsapp/vincular", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClienteCon(factory, emisor.Emitir(2, Roles.Profesional, idProfesional: 1)).PostAsync("/mensajeria/whatsapp/vincular", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClienteCon(factory, emisor.Emitir(3, Roles.Crm)).PostAsync("/mensajeria/whatsapp/vincular", null)).StatusCode);
        Assert.Empty(pedidos); // ni siquiera llegó al servicio
    }

    [Fact]
    public async Task Vincular_en_qr_vencido_abre_la_ventana_y_audita_sin_numero_ni_qr()
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas(EstadoQrVencido), pedidos);
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var idAdmin = datos.Usuario();
        var cliente = ClienteCon(factory, emisor.Emitir(idAdmin, Roles.Configurador));

        var r = await cliente.PostAsync("/mensajeria/whatsapp/vincular", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        Assert.Contains("POST /vincular", pedidos);
        var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j.GetProperty("exito").GetBoolean());
        Assert.False(j.GetProperty("yaAbierta").GetBoolean());
        var (cantidad, detalle) = await AuditoriaDe(idAdmin, "Pedido de código QR de WhatsApp");
        Assert.Equal(1, cantidad);
        Assert.DoesNotContain("data:image", detalle);
    }

    [Fact]
    public async Task Vincular_dos_veces_seguidas_respeta_el_enfriamiento_de_30_segundos()
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas(EstadoQrVencido), pedidos);
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var cliente = ClienteCon(factory, emisor.Emitir(datos.Usuario(), Roles.Configurador));

        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsync("/mensajeria/whatsapp/vincular", null)).StatusCode);
        var segunda = await cliente.PostAsync("/mensajeria/whatsapp/vincular", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, segunda.StatusCode);
        var j = System.Text.Json.JsonDocument.Parse(await segunda.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("enfriamiento", j.GetProperty("codigo").GetString());
        var falta = j.GetProperty("reintentarEnSegundos").GetInt32();
        Assert.InRange(falta, 1, EnfriamientoQr.Segundos);
        Assert.Equal(1, pedidos.Count(p => p == "POST /vincular")); // el segundo no llegó al servicio
    }

    [Fact]
    public async Task Vincular_con_un_codigo_ya_esperando_escaneo_no_abre_otro_ni_gasta_el_enfriamiento()
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas(EstadoEsperandoEscaneo), pedidos);
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var cliente = ClienteCon(factory, emisor.Emitir(datos.Usuario(), Roles.Configurador));

        for (var i = 0; i < 3; i++)
        {
            var r = await cliente.PostAsync("/mensajeria/whatsapp/vincular", null);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
            Assert.True(j.GetProperty("yaAbierta").GetBoolean());
        }
        Assert.DoesNotContain("POST /vincular", pedidos);
        Assert.DoesNotContain("POST /desvincular", pedidos);
    }

    [Fact]
    public async Task Vincular_con_nuevo_reemplaza_el_codigo_que_esperaba_escaneo_audita_y_despues_enfria()
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas(EstadoEsperandoEscaneo), pedidos);
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var idAdmin = datos.Usuario();
        var cliente = ClienteCon(factory, emisor.Emitir(idAdmin, Roles.Configurador));

        var r = await cliente.PostAsJsonAsync("/mensajeria/whatsapp/vincular", new { nuevo = true });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("POST /desvincular", pedidos);
        Assert.Equal(1, (await AuditoriaDe(idAdmin, "Código QR nuevo de WhatsApp")).Cantidad);
        var otra = await cliente.PostAsJsonAsync("/mensajeria/whatsapp/vincular", new { nuevo = true });
        Assert.Equal(HttpStatusCode.TooManyRequests, otra.StatusCode);
        Assert.Equal(1, pedidos.Count(p => p == "POST /desvincular"));
    }

    [Fact]
    public async Task Vincular_con_nuevo_no_desvincula_si_entre_medio_se_escaneo_el_codigo_y_ya_esta_conectado()
    {
        // La primera lectura del estado dice "esperando escaneo"; para la segunda (justo antes de desvincular) ya escanearon y está conectado.
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var lecturas = 0;
        using var servidor = new ServidorFalso { Responder = req =>
        {
            var ruta = req.Url!.AbsolutePath;
            pedidos.Enqueue($"{req.HttpMethod} {ruta}");
            if (ruta == "/estado")
                return (200, Interlocked.Increment(ref lecturas) == 1 ? EstadoEsperandoEscaneo
                    : """{"conectado":true,"numero":"5491112345678","qrPendiente":false,"qr":null,"estado":"conectado","motivo":null}""");
            return (200, """{"exito":true}""");
        } };
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var idAdmin = datos.Usuario();
        var cliente = ClienteCon(factory, emisor.Emitir(idAdmin, Roles.Configurador));

        var r = await cliente.PostAsJsonAsync("/mensajeria/whatsapp/vincular", new { nuevo = true });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ya_vinculado", j.GetProperty("codigo").GetString());
        Assert.Equal(2, lecturas);                               // se leyó de nuevo
        Assert.DoesNotContain("POST /desvincular", pedidos);     // y no se cerró la sesión recién vinculada
        Assert.Equal(0, (await AuditoriaDe(idAdmin, "Código QR nuevo de WhatsApp")).Cantidad);
        // y no gastó el enfriamiento: un pedido que no abrió nada no frena al siguiente
        Assert.Equal(0, factory.Services.GetRequiredService<EnfriamientoQr>().SegundosRestantes());
    }

    [Theory]
    [InlineData("""{"conectado":true,"numero":"5491112345678","qrPendiente":false,"qr":null,"estado":"conectado","motivo":null}""", "ya_vinculado")]
    [InlineData("""{"conectado":false,"numero":null,"qrPendiente":false,"qr":null,"estado":"requiere_atencion","motivo":"cuenta_restringida"}""", "requiere_atencion")]
    [InlineData("""{"conectado":false,"numero":null,"qrPendiente":false,"qr":null,"estado":"reconectando","motivo":null}""", "en_curso")]
    [InlineData("""{"conectado":false,"numero":null,"qrPendiente":false,"qr":null,"estado":"iniciando","motivo":null}""", "en_curso")]
    public async Task Vincular_donde_un_codigo_no_corresponde_da_409_y_no_toca_al_servicio(string estado, string codigoEsperado)
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas(estado), pedidos);
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var cliente = ClienteCon(factory, emisor.Emitir(datos.Usuario(), Roles.Configurador));

        var r = await cliente.PostAsJsonAsync("/mensajeria/whatsapp/vincular", new { nuevo = true });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(codigoEsperado, j.GetProperty("codigo").GetString());
        Assert.DoesNotContain("POST /vincular", pedidos);
        Assert.DoesNotContain("POST /desvincular", pedidos); // con un número vinculado, "nuevo" jamás lo desvincula
    }

    [Fact]
    public async Task Vincular_con_el_servicio_caido_da_503_sin_excepcion_y_no_audita()
    {
        var puerto = ServidorFalso.PuertoLibre();
        using var factory = Factory($"http://127.0.0.1:{puerto}/");
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var idAdmin = datos.Usuario();
        var cliente = ClienteCon(factory, emisor.Emitir(idAdmin, Roles.Configurador));

        var r = await cliente.PostAsync("/mensajeria/whatsapp/vincular", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("servicio_no_disponible", j.GetProperty("codigo").GetString());
        Assert.Equal(0, (await AuditoriaDe(idAdmin, "Pedido de código QR de WhatsApp")).Cantidad);
    }

    [Fact]
    public async Task Vincular_tiene_limite_de_pedidos_por_IP()
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas("""{"conectado":true,"numero":"5491112345678","qrPendiente":false,"qr":null,"estado":"conectado"}"""), pedidos);
        using var factory = Factory(servidor.BaseUrl);
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var datos = new DatosDePrueba();
        var token = emisor.Emitir(datos.Usuario(), Roles.Configurador);

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Conflict, (await ClienteCon(factory, token).PostAsync("/mensajeria/whatsapp/vincular", null)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await ClienteCon(factory, token).PostAsync("/mensajeria/whatsapp/vincular", null)).StatusCode);
    }

    // La bandeja de Chats (módulo CRM) solo necesita saber si WhatsApp está conectado: ni número ni QR.
    private static WebApplicationFactory<Program> FactoryConCanal(string urlDelServicio, string canal) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // UseSetting y no ConfigureAppConfiguration: AddMensajeria lee CanalActivo mientras se arma el host (ver CanalActivoTests).
            b.UseSetting("Mensajeria:CanalActivo", canal);
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mensajeria:WhatsApp:Secreto"] = SecretoDePrueba,
                ["Mensajeria:WhatsAppServiceUrl"] = urlDelServicio
            }));
        });

    [Fact]
    public async Task Conexion_de_la_bandeja_la_ve_el_CRM_sin_numero_ni_qr_y_los_demas_no()
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas(EstadoEsperandoEscaneo), pedidos);
        using var factory = FactoryConCanal(servidor.BaseUrl, "Baileys");
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/mensajeria/conexion-whatsapp")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClienteCon(factory, emisor.Emitir(2, Roles.Profesional, idProfesional: 1)).GetAsync("/mensajeria/conexion-whatsapp")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClienteCon(factory, emisor.Emitir(1, Roles.Configurador)).GetAsync("/mensajeria/conexion-whatsapp")).StatusCode);

        var r = await ClienteCon(factory, emisor.Emitir(3, Roles.Crm)).GetAsync("/mensajeria/conexion-whatsapp");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var texto = await r.Content.ReadAsStringAsync();
        var j = System.Text.Json.JsonDocument.Parse(texto).RootElement;
        Assert.True(j.GetProperty("aplica").GetBoolean());
        Assert.False(j.GetProperty("conectado").GetBoolean());
        Assert.Equal("esperando_escaneo", j.GetProperty("motivo").GetString());
        Assert.DoesNotContain("qr", texto.Replace("qrPendiente", ""), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("numero", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:image", texto);
    }

    [Fact]
    public async Task Conexion_de_la_bandeja_con_el_canal_simulado_dice_que_no_aplica_y_no_llama_al_servicio()
    {
        var pedidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var servidor = ServicioFalso(new Respuestas(EstadoQrVencido), pedidos);
        using var factory = FactoryConCanal(servidor.BaseUrl, "Simulado");
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();

        var r = await ClienteCon(factory, emisor.Emitir(3, Roles.Crm)).GetAsync("/mensajeria/conexion-whatsapp");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.False(j.GetProperty("aplica").GetBoolean());
        Assert.Empty(pedidos);
    }

    [Fact]
    public async Task Conexion_de_la_bandeja_con_el_servicio_caido_responde_sin_excepcion()
    {
        var puerto = ServidorFalso.PuertoLibre();
        using var factory = FactoryConCanal($"http://127.0.0.1:{puerto}/", "Baileys");
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();

        var r = await ClienteCon(factory, emisor.Emitir(3, Roles.Crm)).GetAsync("/mensajeria/conexion-whatsapp");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.False(j.GetProperty("servicioDisponible").GetBoolean());
    }

    private record RespuestaEstadoDeTest(bool ServicioDisponible, bool Conectado, string? Numero, bool QrPendiente, string? Qr);
}
