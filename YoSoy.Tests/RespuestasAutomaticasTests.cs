using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// Las respuestas automáticas solo salen dentro de una conversación que la persona empezó.
//
// Para no parecer un bot ante WhatsApp, un mensaje AUTOMÁTICO (origen "Bot" o "Sistema") es una respuesta, y una respuesta solo se manda si la persona ya
// escribió en esa conversación. La regla vive en ServicioMensajes, el único punto por el que sale todo, así que ningún camino nuevo se la salta sin
// declararlo: las excepciones son el equipo ("Humano": decide una persona) y los avisos de turno por evento (OpcionesEnvio.IniciaElNegocio).
//
//  · ServicioMensajes, en memoria: con y sin mensajes entrantes, para cada origen y cada opción.
//  · La aplicación entera contra una base de pruebas: el asistente contesta (la persona escribió), cerrar y reabrir una conversación mandan su aviso
//    solo si la persona escribió, y el equipo puede responder.
public class RespuestasAutomaticasTests
{
    private sealed class MensajesEnMemoria : IMensajeChatDAO
    {
        public readonly List<MensajeChat> Todos = new();
        public int Insertar(MensajeChat m) { m.IdMensaje = Todos.Count + 1; Todos.Add(m); return m.IdMensaje; }
        public void ActualizarEstadoEnvio(int idMensaje, string estado) => Todos.Single(x => x.IdMensaje == idMensaje).EstadoEnvio = estado;
        public List<MensajeChat> ObtenerPorConversacion(int idConversacion, int desdeIdMensaje) => new();
        public List<MensajeChat> ObtenerAnteriores(int idConversacion, int? antesDeIdMensaje, int cantidad) => new();
        public bool TieneMensajeEntrante(int idConversacion) => Todos.Any(m => m.IdConversacion == idConversacion && m.Direccion == "Entrante");
    }

    private sealed class CanalQueRegistra : IMessageChannel
    {
        public string Nombre => "Prueba";
        public readonly List<(string Telefono, string Texto, OpcionesEnvio Opciones)> Envios = new();

        public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto) => EnviarAsync(telefonoDestino, texto, OpcionesEnvio.Ninguna);

        public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto, OpcionesEnvio opciones)
        {
            Envios.Add((telefonoDestino, texto, opciones));
            return Task.FromResult(new ResultadoEnvio(true, "x"));
        }
    }

    private sealed class Arnes
    {
        public readonly MensajesEnMemoria Mensajes = new();
        public readonly CanalQueRegistra Canal = new();
        public readonly LogsCapturados Logs = new();
        public readonly ServicioMensajes Servicio;
        public readonly Conversacion Conversacion = new() { IdConversacion = 7, Telefono = "1155550001", Canal = "Prueba" };
        public readonly Conversacion Otra = new() { IdConversacion = 8, Telefono = "1166667777", Canal = "Prueba" };

        public Arnes() =>
            Servicio = new ServicioMensajes(Mensajes, Canal, new RelojFijo(Escenario.Jueves0900), Logs.CreateLogger<ServicioMensajes>());
    }

    // ───────────── Sin conversación que la persona haya empezado ─────────────

    [Theory]
    [InlineData("Bot")]
    [InlineData("Sistema")]
    public async Task Un_mensaje_automatico_en_una_conversacion_que_la_persona_no_empezo_no_se_guarda_ni_se_manda(string origen)
    {
        var a = new Arnes();

        var m = await a.Servicio.EnviarAsync(a.Conversacion, "El equipo de YoSoy cerró esta conversación.", origen);

        Assert.Empty(a.Canal.Envios);                                                       // no llega al canal
        Assert.Empty(a.Mensajes.Todos);                                                     // y no queda en el historial como si hubiera salido
        Assert.Equal("Error", m.EstadoEnvio);
        Assert.Equal(ServicioMensajes.CodigoSinConversacionIniciada, m.CodigoDeError);
        Assert.Equal(0, m.IdMensaje);                                                       // nunca se insertó
    }

    [Theory]
    [InlineData("Bot")]
    [InlineData("Sistema")]
    public async Task El_optIn_solo_no_alcanza_para_un_mensaje_automatico(string origen)
    {
        var a = new Arnes();

        await a.Servicio.EnviarAsync(a.Conversacion, "hola", origen, new OpcionesEnvio(OptIn: true));

        Assert.Empty(a.Canal.Envios);        // aceptar los avisos no es escribir: la excepción es IniciaElNegocio, y solo para los avisos de turno
        Assert.Empty(a.Mensajes.Todos);
    }

    [Fact]
    public async Task Lo_que_la_clinica_mando_antes_no_cuenta_como_que_la_persona_escribio()
    {
        var a = new Arnes();
        await a.Servicio.EnviarAsync(a.Conversacion, "Tu turno quedó confirmado.", "Sistema", new OpcionesEnvio(IniciaElNegocio: true));   // un aviso: sale
        Assert.Single(a.Canal.Envios);

        var m = await a.Servicio.EnviarAsync(a.Conversacion, "El equipo de YoSoy reabrió esta conversación.", "Sistema");

        Assert.Single(a.Canal.Envios);                                                     // el aviso no se convierte en una conversación de la persona
        Assert.Equal(ServicioMensajes.CodigoSinConversacionIniciada, m.CodigoDeError);
        Assert.Single(a.Mensajes.Todos);
    }

    [Fact]
    public async Task Solo_cuenta_lo_que_la_persona_escribio_en_esa_conversacion()
    {
        var a = new Arnes();
        a.Servicio.RegistrarEntrante(a.Otra, "hola");                                        // escribió en OTRA conversación

        var m = await a.Servicio.EnviarAsync(a.Conversacion, "respuesta", "Bot");

        Assert.Empty(a.Canal.Envios);
        Assert.Equal(ServicioMensajes.CodigoSinConversacionIniciada, m.CodigoDeError);
    }

    // ───────────── Con una conversación que la persona empezó ─────────────

    [Theory]
    [InlineData("Bot")]
    [InlineData("Sistema")]
    public async Task Si_la_persona_escribio_en_la_conversacion_el_mensaje_automatico_sale(string origen)
    {
        var a = new Arnes();
        a.Servicio.RegistrarEntrante(a.Conversacion, "hola");

        var m = await a.Servicio.EnviarAsync(a.Conversacion, "respuesta", origen);

        var envio = Assert.Single(a.Canal.Envios);
        Assert.Equal(("1155550001", "respuesta"), (envio.Telefono, envio.Texto));
        Assert.Equal("Enviado", m.EstadoEnvio);
        Assert.Null(m.CodigoDeError);
        Assert.Equal(new[] { "Entrante", "Saliente" }, a.Mensajes.Todos.Select(x => x.Direccion));
    }

    [Fact]
    public async Task Una_sola_vez_que_escriba_alcanza_para_todas_las_respuestas_que_siguen()
    {
        var a = new Arnes();
        a.Servicio.RegistrarEntrante(a.Conversacion, "hola");

        for (var i = 1; i <= 6; i++) await a.Servicio.EnviarAsync(a.Conversacion, $"respuesta {i}", "Bot");

        Assert.Equal(6, a.Canal.Envios.Count);
    }

    // ───────────── Las excepciones, una por una ─────────────

    [Fact]
    public async Task El_equipo_puede_responder_aunque_la_persona_no_haya_escrito()
    {
        var a = new Arnes();

        var m = await a.Servicio.EnviarAsync(a.Conversacion, "Hola, te escribimos del consultorio.", "Humano");

        Assert.Single(a.Canal.Envios);           // lo escribe una persona del equipo y decide ella; el servicio de WhatsApp igual exige consentimiento
        Assert.Equal("Enviado", m.EstadoEnvio);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Un_aviso_de_turno_por_evento_sale_sin_conversacion_previa_con_o_sin_optIn(bool optIn)
    {
        var a = new Arnes();

        var m = await a.Servicio.EnviarAsync(a.Conversacion, "Tu turno quedó confirmado.", "Sistema", new OpcionesEnvio(OptIn: optIn, IniciaElNegocio: true));

        Assert.Equal(optIn, Assert.Single(a.Canal.Envios).Opciones.OptIn);
        Assert.Equal("Enviado", m.EstadoEnvio);
    }

    [Fact]
    public async Task El_bloqueo_se_anota_en_el_log_sin_el_telefono_ni_el_texto()
    {
        var a = new Arnes();

        await a.Servicio.EnviarAsync(a.Conversacion, "texto privado del mensaje", "Sistema");

        var linea = Assert.Single(a.Logs.Lineas);
        Assert.Contains("Information|", linea);
        Assert.Contains("conversación 7", linea);
        Assert.Contains("Sistema", linea);
        Assert.DoesNotContain("1155550001", linea);
        Assert.DoesNotContain("texto privado", linea);
    }

    // ───────────── Quién puede saltearse la regla ─────────────

    [Fact]
    public void Solo_NotificadorTurnos_declara_que_inicia_el_negocio()
    {
        // La excepción es una decisión de producto (los avisos de turno por evento: ver docs/PROGRESO_WHATSAPP_SOLO_BAILEYS.md, roles y permisos): que otro
        // código la use, a propósito o por costumbre, tiene que hacer fallar esta prueba y obligar a pensarlo.
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        var archivos = Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}YoSoy.Tests{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}publish-vps{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("IniciaElNegocio: true"))
            .Select(f => Path.GetFileName(f))
            .ToList();

        Assert.Equal(new[] { "NotificadorTurnos.cs" }, archivos);
    }
}

// La aplicación entera, con el canal de WhatsApp reemplazado por uno que anota cada envío (no sale nada de la máquina) y una base de pruebas: lo que
// el asistente contesta, y los avisos que mandan cerrar y reabrir una conversación.
[Trait("Categoria", "Integracion")]
public class RespuestasAutomaticasEndpointsTests : IAsyncLifetime
{
    private sealed class CanalQueRegistra : IMessageChannel
    {
        public string Nombre => "Simulado";
        public readonly List<(string Telefono, string Texto)> Envios = new();
        public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto)
        {
            lock (Envios) Envios.Add((telefonoDestino, texto));
            return Task.FromResult(new ResultadoEnvio(true, "x"));
        }
        public List<(string Telefono, string Texto)> A(string telefono) { lock (Envios) return Envios.Where(e => e.Telefono == telefono).ToList(); }
    }

    private readonly CanalQueRegistra _canal = new();
    private DatosDePrueba _datos = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _crm = null!, _sinToken = null!;

    public Task InitializeAsync()
    {
        _datos = new DatosDePrueba();
        var idUsuarioCrm = _datos.Usuario();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(servicios =>
        {
            foreach (var d in servicios.Where(x => x.ServiceType == typeof(IMessageChannel)).ToList()) servicios.Remove(d);
            servicios.AddSingleton<IMessageChannel>(_canal);
        }));
        var emisor = _factory.Services.GetRequiredService<JwtEmisorService>();
        _crm = _factory.CreateClient();
        _crm.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", emisor.Emitir(idUsuarioCrm, Roles.Crm));
        _sinToken = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        _datos.Dispose();
        return Task.CompletedTask;
    }

    private static string TelefonoNuevo() => "11" + Random.Shared.Next(10_000_000, 99_999_999);

    // Una conversación que armó un aviso de turno: solo hay un mensaje saliente, la persona nunca escribió.
    private int ConversacionDeUnAviso(string telefono)
    {
        var id = Convert.ToInt32(DatosDePrueba.Escalar(
            "INSERT INTO Conversacion (Telefono, Canal, Estado, PasoActual) VALUES (@t, 'Simulado', 'DerivadaHumano', 'Menu'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@t", telefono)));
        _datos.AlLimpiar("Conversacion", "Telefono", telefono);
        DatosDePrueba.Ejecutar(
            "INSERT INTO MensajeChat (IdConversacion, Direccion, Origen, Texto, EstadoEnvio) VALUES (@c, 'Saliente', 'Sistema', N'Tu turno quedó confirmado.', 'Enviado')",
            ("@c", id));
        return id;
    }

    private static int AvisosDelSistema(int idConversacion) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c AND Origen = 'Sistema' AND Texto LIKE N'El equipo de YoSoy%'", ("@c", idConversacion));

    private static string Estado(int idConversacion) =>
        (string)DatosDePrueba.Escalar("SELECT Estado FROM Conversacion WHERE IdConversacion = @c", ("@c", idConversacion))!;

    // ───────────── El asistente contesta: la persona escribió ─────────────

    [Fact]
    public async Task El_asistente_le_contesta_a_quien_le_escribe_aunque_la_conversacion_sea_nueva()
    {
        var telefono = TelefonoNuevo();
        _datos.AlLimpiar("Conversacion", "Telefono", telefono);

        var r = await _sinToken.PostAsJsonAsync("/mensajeria/simulado/entrante", new { telefono, texto = "hola" });

        r.EnsureSuccessStatusCode();
        var envios = _canal.A(telefono);
        Assert.NotEmpty(envios);                                                                   // el menú del asistente salió por el canal
        Assert.Equal(envios.Count, DatosDePrueba.Contar(
            "SELECT COUNT(*) FROM MensajeChat m JOIN Conversacion c ON c.IdConversacion = m.IdConversacion " +
            "WHERE c.Telefono = @t AND m.Origen = 'Bot' AND m.EstadoEnvio = 'Enviado'", ("@t", telefono)));
    }

    // ───────────── Cerrar y reabrir ─────────────

    [Fact]
    public async Task Cerrar_y_reabrir_una_conversacion_que_la_persona_empezo_mandan_su_aviso()
    {
        var telefono = TelefonoNuevo();
        _datos.AlLimpiar("Conversacion", "Telefono", telefono);
        (await _sinToken.PostAsJsonAsync("/mensajeria/simulado/entrante", new { telefono, texto = "hola" })).EnsureSuccessStatusCode();
        var idConversacion = Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdConversacion FROM Conversacion WHERE Telefono = @t", ("@t", telefono)));
        var antes = _canal.A(telefono).Count;

        var cierre = await _crm.PutAsync($"/mensajeria/conversaciones/{idConversacion}/cerrar", null);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal("Cerrada", Estado(idConversacion));
        Assert.Equal(1, AvisosDelSistema(idConversacion));
        Assert.Contains("cerró esta conversación", Assert.Single(_canal.A(telefono).Skip(antes)).Texto);

        var reapertura = await _crm.PutAsync($"/mensajeria/conversaciones/{idConversacion}/reabrir", null);
        Assert.Equal(HttpStatusCode.OK, reapertura.StatusCode);
        Assert.Equal("DerivadaHumano", Estado(idConversacion));
        Assert.Equal(2, AvisosDelSistema(idConversacion));
        Assert.Contains("reabrió esta conversación", _canal.A(telefono).Last().Texto);
    }

    [Fact]
    public async Task Cerrar_y_reabrir_una_conversacion_que_armo_un_aviso_cambian_el_estado_pero_no_mandan_nada()
    {
        // Otra persona SÍ escribió, en otra conversación: no tiene que contar para esta.
        var otroTelefono = TelefonoNuevo();
        _datos.AlLimpiar("Conversacion", "Telefono", otroTelefono);
        (await _sinToken.PostAsJsonAsync("/mensajeria/simulado/entrante", new { telefono = otroTelefono, texto = "hola" })).EnsureSuccessStatusCode();

        var telefono = TelefonoNuevo();
        var idConversacion = ConversacionDeUnAviso(telefono);

        var cierre = await _crm.PutAsync($"/mensajeria/conversaciones/{idConversacion}/cerrar", null);
        var cuerpo = await cierre.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);                                         // el equipo cierra y la acción funciona
        Assert.Equal("Cerrada", Estado(idConversacion));
        Assert.Equal("Cerrada", cuerpo.GetProperty("estado").GetString());

        var reapertura = await _crm.PutAsync($"/mensajeria/conversaciones/{idConversacion}/reabrir", null);
        Assert.Equal(HttpStatusCode.OK, reapertura.StatusCode);
        Assert.Equal("DerivadaHumano", Estado(idConversacion));

        Assert.Empty(_canal.A(telefono));                                                           // pero a la persona no le llega nada
        Assert.Equal(0, AvisosDelSistema(idConversacion));                                          // ni queda un aviso que no salió
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c", ("@c", idConversacion)));   // solo el aviso de turno de antes
    }

    // ───────────── El equipo ─────────────

    [Fact]
    public async Task El_equipo_puede_responder_en_una_conversacion_que_armo_un_aviso()
    {
        var telefono = TelefonoNuevo();
        var idConversacion = ConversacionDeUnAviso(telefono);

        var r = await _crm.PostAsJsonAsync($"/mensajeria/conversaciones/{idConversacion}/responder", new { texto = "Hola, te escribimos del consultorio." });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Hola, te escribimos del consultorio.", Assert.Single(_canal.A(telefono)).Texto);
    }
}
