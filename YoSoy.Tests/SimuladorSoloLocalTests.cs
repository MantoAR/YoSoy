using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

// El simulador del paciente (POST /mensajeria/simulado/entrante y GET /mensajeria/simulado/conversacion) no tiene sesión ni secreto: simula un mensaje que ENTRA, como el webhook.
// Su único candado es la propia máquina, y vale SIEMPRE (el mismo del webhook, EsLocal), no solo con Mensajeria:SoloLocal. Con SoloLocal=false (la bandeja del Configurador vista desde
// el sitio, como en producción) el resto de /mensajeria se abre a la red, pero el simulador sigue respondiendo 404 a un pedido remoto. Antes quedaba abierto: cualquiera desde internet
// creaba conversaciones en la bandeja y podía preguntar por un teléfono si esa persona ya había hablado con la clínica.
//
// Un pedido remoto se arma como lo ven la aplicación y las demás pruebas: con X-Forwarded-For (lo que agrega Caddy) o CF-Connecting-IP (cloudflared). Ninguna prueba abre una conexión ni
// toca la base: el POST se pide con un teléfono vacío (400 "el teléfono no es válido": el pedido llegó al código y no escribió nada) y el GET con un doble de prueba de las conversaciones.
public class SimuladorSoloLocalTests
{
    private const string ClaveJwt = "clave-de-prueba-del-simulador-solo-local-0123456789-abcdef";
    private const string Telefono = "1155550001";

    // UseSetting (no ConfigureAppConfiguration): igual que CanalActivoTests y ModoAtencionTests, para que llegue antes de que se arme el contenedor.
    private static WebApplicationFactory<Program> Fabrica(string soloLocal, FakeConversaciones? conversaciones = null, string? chatsHabilitados = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", ClaveJwt);
            b.UseSetting("Mensajeria:SoloLocal", soloLocal);
            if (chatsHabilitados is not null) b.UseSetting("Mensajeria:ChatSimuladoHabilitado", chatsHabilitados);
            if (conversaciones is not null) b.ConfigureTestServices(s => s.AddSingleton<IConversacionDAO>(conversaciones));
        });

    // Una conversación que el simulador "ya creó" para ese teléfono, con el canal que usa la aplicación sin configurar nada (Simulado).
    private static FakeConversaciones ConUnaConversacion()
    {
        var conversaciones = new FakeConversaciones(new RelojFijo(new DateTime(2026, 10, 19, 12, 0, 0)));
        conversaciones.ObtenerOCrear(Telefono, "Simulado");
        return conversaciones;
    }

    private static Task<HttpResponseMessage> Entrante(HttpClient cliente) =>
        cliente.PostAsJsonAsync("/mensajeria/simulado/entrante", new { telefono = "", texto = "" });

    private static Task<HttpResponseMessage> Conversacion(HttpClient cliente) =>
        cliente.GetAsync("/mensajeria/simulado/conversacion?telefono=" + Telefono);

    // ───────────── Con SoloLocal=false (producción): un pedido remoto no entra ─────────────

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("CF-Connecting-IP")]
    public async Task Con_SoloLocal_false_un_pedido_remoto_no_puede_crear_conversaciones(string encabezado)
    {
        using var fabrica = Fabrica("false", ConUnaConversacion());
        using var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add(encabezado, "203.0.113.7");

        // La premisa: con SoloLocal=false la mensajería ESTÁ disponible para este pedido remoto (así el Configurador ve Chats desde el sitio)...
        var capacidades = await cliente.GetFromJsonAsync<JsonElement>("/capacidades");
        Assert.True(capacidades.GetProperty("mensajeria").GetBoolean());

        // ...y aun así el simulador no responde: 404, no 400 (que sería haber llegado al código, donde crea la conversación).
        using var r = await Entrante(cliente);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("CF-Connecting-IP")]
    public async Task Con_SoloLocal_false_un_pedido_remoto_no_puede_preguntar_por_un_telefono(string encabezado)
    {
        // El doble TIENE una conversación para ese teléfono: si el pedido llegara al código, contestaría 200 con su id y su estado.
        using var fabrica = Fabrica("false", ConUnaConversacion());
        using var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add(encabezado, "203.0.113.7");

        using var r = await Conversacion(cliente);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ───────────── El simulador local sigue funcionando (desarrollo, la propia máquina) ─────────────

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task Un_pedido_de_la_propia_maquina_sigue_llegando_al_simulador(string soloLocal)
    {
        using var fabrica = Fabrica(soloLocal, ConUnaConversacion());
        using var cliente = fabrica.CreateClient();

        using var entrante = await Entrante(cliente);
        using var conversacion = await Conversacion(cliente);

        // 400 con el teléfono vacío = el pedido llegó al código y este contestó "el teléfono no es válido" (no escribió nada).
        Assert.Equal(HttpStatusCode.BadRequest, entrante.StatusCode);
        Assert.Equal(HttpStatusCode.OK, conversacion.StatusCode);
        var c = await conversacion.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, c.GetProperty("idConversacion").GetInt32());
        Assert.Equal("Activa", c.GetProperty("estado").GetString());
    }

    // ───────────── Lo que ya cerraba el grupo sigue cerrado ─────────────

    [Fact]
    public async Task Con_SoloLocal_true_un_pedido_remoto_sigue_recibiendo_404()
    {
        using var fabrica = Fabrica("true", ConUnaConversacion());
        using var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        using var entrante = await Entrante(cliente);
        using var conversacion = await Conversacion(cliente);

        Assert.Equal(HttpStatusCode.NotFound, entrante.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, conversacion.StatusCode);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task Con_el_chat_apagado_el_simulador_da_404_aunque_el_pedido_sea_de_la_propia_maquina(string soloLocal)
    {
        using var fabrica = Fabrica(soloLocal, ConUnaConversacion(), chatsHabilitados: "false");
        using var cliente = fabrica.CreateClient();

        using var entrante = await Entrante(cliente);
        using var conversacion = await Conversacion(cliente);

        Assert.Equal(HttpStatusCode.NotFound, entrante.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, conversacion.StatusCode);
    }

    // ───────────── Que una ruta nueva no quede abierta ─────────────

    [Fact]
    public async Task Toda_ruta_de_mensajeria_sin_sesion_contesta_404_a_un_pedido_remoto_aun_con_SoloLocal_false()
    {
        // Recorre la tabla de rutas de la aplicación: hoy son el simulador (dos rutas) y el webhook de whatsapp-service. Una ruta anónima nueva de /mensajeria que no cierre con la
        // propia máquina rompe esta prueba en vez de quedar abierta a internet en cuanto se prenda SoloLocal=false. (Se mira AllowAnonymous a mano: las rutas del grupo heredan los
        // metadatos de autorización del grupo aunque lo tengan, y ReflexionEndpoints las daría por protegidas.)
        using var fabrica = Fabrica("false", new FakeConversaciones(new RelojFijo(new DateTime(2026, 10, 19, 12, 0, 0))));
        var anonimas = fabrica.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(f => f.Endpoints).OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? "").StartsWith("/mensajeria", StringComparison.Ordinal)
                        && (e.Metadata.GetMetadata<IAllowAnonymous>() is not null || !e.Metadata.OfType<IAuthorizeData>().Any()))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new List<string> { "*" })
                .Select(metodo => (Metodo: metodo, Ruta: e.RoutePattern.RawText!)))
            .ToList();
        Assert.True(anonimas.Count >= 3, "Esperaba al menos el simulador (2 rutas) y el webhook entre las rutas de /mensajeria sin sesión; hay " + anonimas.Count);

        using var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");
        var abiertas = new List<string>();
        foreach (var e in anonimas)
        {
            Assert.DoesNotContain("{", e.Ruta);   // una ruta con parámetros: hay que decir acá cómo se arma el pedido
            using var pedido = e.Metodo == "GET"
                ? new HttpRequestMessage(HttpMethod.Get, e.Ruta + "?telefono=" + Telefono)
                : new HttpRequestMessage(new HttpMethod(e.Metodo), e.Ruta) { Content = JsonContent.Create(new { telefono = "", texto = "" }) };
            using var r = await cliente.SendAsync(pedido);
            if (r.StatusCode != HttpStatusCode.NotFound) abiertas.Add($"{e.Metodo} {e.Ruta} contestó {(int)r.StatusCode}");
        }

        Assert.True(abiertas.Count == 0, "Rutas de /mensajeria sin sesión que le contestan a un pedido remoto con SoloLocal=false (tienen que dar 404):\n  " + string.Join("\n  ", abiertas));
    }
}
