using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// El chat para el personal (Mensajeria:ModoAtencion = "Personal") por la aplicación entera, con una base de pruebas y el canal de WhatsApp reemplazado por uno que anota
// cada envío (no sale nada de la máquina). Lo que tiene que pasar, entrando por el webhook real de whatsapp-service (con su secreto) y por el simulador:
//  · el asistente NO contesta: nada sale por el canal;
//  · el mensaje queda guardado y la conversación "para atender" (DerivadaHumano), con un aviso "chat nuevo" en la campanita del Configurador;
//  · el Configurador la ve y le responde (eso sí sale por el canal), y cerrar o reabrir NO le mandan ningún texto automático a la persona;
//  · una frase de riesgo la marca Urgente y avisa, sin mandarle nada al paciente (el mensaje de emergencia viene apagado: decisión D3).
// El modo Asistente (el de siempre) lo cubren RespuestasAutomaticasEndpointsTests y las pruebas del asistente: acá solo se comprueba que el valor por defecto sigue siendo ese.
[Trait("Categoria", "Integracion")]
public class ModoPersonalEndpointsTests : IAsyncLifetime
{
    private const string Secreto = "secreto-de-prueba-del-modo-personal-0123456789";
    private const string ClaveJwt = "clave-de-prueba-del-modo-personal-0123456789-abcdef";

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
    private HttpClient _configurador = null!, _profesional = null!, _sinToken = null!, _webhook = null!;

    public Task InitializeAsync()
    {
        _datos = new DatosDePrueba();
        var idUsuarioConfigurador = _datos.Usuario();
        var idProfesional = _datos.Profesional();
        var idUsuarioProfesional = _datos.UsuarioDeProfesional(idProfesional);
        _factory = Fabrica("Personal", _canal);
        var emisor = _factory.Services.GetRequiredService<JwtEmisorService>();
        _configurador = ClienteCon(emisor.Emitir(idUsuarioConfigurador, Roles.Configurador));
        _profesional = ClienteCon(emisor.Emitir(idUsuarioProfesional, Roles.Profesional, idProfesional: idProfesional));
        _sinToken = _factory.CreateClient();
        _webhook = _factory.CreateClient();
        _webhook.DefaultRequestHeaders.Add("X-Shared-Secret", Secreto);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        _datos.Dispose();
        return Task.CompletedTask;
    }

    private static WebApplicationFactory<Program> Fabrica(string? modo, CanalQueRegistra canal) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", ClaveJwt);
            b.UseSetting("Mensajeria:WhatsApp:Secreto", Secreto);
            if (modo is not null) b.UseSetting("Mensajeria:ModoAtencion", modo);
            b.ConfigureServices(servicios =>
            {
                foreach (var d in servicios.Where(x => x.ServiceType == typeof(IMessageChannel)).ToList()) servicios.Remove(d);
                servicios.AddSingleton<IMessageChannel>(canal);
            });
        });

    private HttpClient ClienteCon(string token)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static string TelefonoNuevo() => "11" + Random.Shared.Next(10_000_000, 99_999_999);

    // El teléfono de WhatsApp es el internacional (549 + 10 dígitos); la conversación guarda los últimos 10.
    private string ConversacionNueva(out string telefonoWhatsApp)
    {
        var telefono = TelefonoNuevo();
        telefonoWhatsApp = "549" + telefono;
        _datos.AlLimpiar("Conversacion", "Telefono", telefono);   // y en cascada sus mensajes y avisos
        return telefono;
    }

    private static int IdDe(string telefono) =>
        Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdConversacion FROM Conversacion WHERE Telefono = @t", ("@t", telefono)));

    private static string Estado(int idConversacion) =>
        (string)DatosDePrueba.Escalar("SELECT Estado FROM Conversacion WHERE IdConversacion = @c", ("@c", idConversacion))!;

    private static int Avisos(int idConversacion, string tipo) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE IdConversacion = @c AND Tipo = @t", ("@c", idConversacion), ("@t", tipo));

    private async Task<JsonElement> Entra(HttpClient por, string ruta, string telefono, string texto)
    {
        var r = await por.PostAsJsonAsync(ruta, new { telefono, texto });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ───────────── Lo que entra por el webhook de whatsapp-service ─────────────

    [Fact]
    public async Task Un_mensaje_por_el_webhook_se_guarda_queda_para_atender_y_no_se_le_contesta_nada()
    {
        var telefono = ConversacionNueva(out var deWhatsApp);

        var r = await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "hola, quería sacar un turno");

        var id = IdDe(telefono);
        Assert.Equal(0, r.GetProperty("respuestas").GetArrayLength());
        Assert.Equal(id, r.GetProperty("idConversacion").GetInt32());
        Assert.Empty(_canal.A(telefono));                                    // ni al número de 10 dígitos...
        Assert.Empty(_canal.A(deWhatsApp));                                  // ...ni al de WhatsApp
        Assert.Equal("DerivadaHumano", Estado(id));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c", ("@c", id)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c AND Direccion = 'Entrante' AND Origen = 'Paciente' AND Texto = N'hola, quería sacar un turno'", ("@c", id)));
        Assert.Null(DatosDePrueba.Escalar("SELECT IdProfesionalAsignado FROM Conversacion WHERE IdConversacion = @c", ("@c", id)));
    }

    [Fact]
    public async Task El_aviso_de_chat_nuevo_llega_a_la_campanita_del_Configurador_y_no_lleva_el_texto_del_mensaje()
    {
        var telefono = ConversacionNueva(out var deWhatsApp);

        await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "mensaje secreto de prueba que no tiene que aparecer en el aviso");

        var id = IdDe(telefono);
        Assert.Equal(1, Avisos(id, "ChatNuevo"));
        var avisos = await _configurador.GetFromJsonAsync<JsonElement[]>("/mensajeria/notificaciones-internas?destinatario=Administrador");
        var aviso = Assert.Single(avisos!, n => n.TryGetProperty("idConversacion", out var v) && v.ValueKind == JsonValueKind.Number && v.GetInt32() == id);
        Assert.Equal("ChatNuevo", aviso.GetProperty("tipo").GetString());
        Assert.Equal("Administrador", aviso.GetProperty("destinatario").GetString());
        Assert.Contains($"#{id}", aviso.GetProperty("mensaje").GetString());
        Assert.DoesNotContain("mensaje secreto", aviso.GetProperty("mensaje").GetString());
        Assert.DoesNotContain("mensaje secreto", aviso.GetProperty("titulo").GetString());
    }

    [Fact]
    public async Task Una_charla_en_curso_deja_un_solo_aviso_y_todos_los_mensajes_guardados()
    {
        var telefono = ConversacionNueva(out var deWhatsApp);

        await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "hola");
        await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "¿hay alguien?");
        await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "necesito un turno para esta semana");

        var id = IdDe(telefono);
        Assert.Equal(1, Avisos(id, "ChatNuevo"));
        Assert.Equal(3, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c AND Direccion = 'Entrante'", ("@c", id)));
        Assert.Empty(_canal.A(telefono));
    }

    [Fact]
    public async Task El_Configurador_ve_la_conversacion_y_sus_mensajes_y_un_Profesional_no()
    {
        var telefono = ConversacionNueva(out var deWhatsApp);
        await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "hola");
        var id = IdDe(telefono);

        var lista = await _configurador.GetFromJsonAsync<JsonElement[]>("/mensajeria/conversaciones");
        var fila = Assert.Single(lista!, c => c.GetProperty("idConversacion").GetInt32() == id);
        Assert.Equal("DerivadaHumano", fila.GetProperty("estado").GetString());
        Assert.Equal(1, fila.GetProperty("noLeidos").GetInt32());
        var mensajes = await _configurador.GetFromJsonAsync<JsonElement[]>($"/mensajeria/conversaciones/{id}/mensajes");
        Assert.Equal("hola", Assert.Single(mensajes!).GetProperty("texto").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await _profesional.GetAsync($"/mensajeria/conversaciones/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _profesional.GetAsync("/mensajeria/conversaciones")).StatusCode);
    }

    [Fact]
    public async Task El_Configurador_le_responde_y_eso_si_sale_por_el_canal()
    {
        var telefono = ConversacionNueva(out var deWhatsApp);
        await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "hola");
        var id = IdDe(telefono);

        var r = await _configurador.PostAsJsonAsync($"/mensajeria/conversaciones/{id}/responder", new { texto = "Hola, te escribimos del consultorio. ¿En qué podemos ayudarte?" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Hola, te escribimos del consultorio. ¿En qué podemos ayudarte?", Assert.Single(_canal.A(telefono)).Texto);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c AND Origen = 'Humano' AND EstadoEnvio = 'Enviado'", ("@c", id)));
    }

    // ───────────── Cerrar y reabrir ─────────────

    [Fact]
    public async Task Cerrar_y_reabrir_cambian_el_estado_pero_no_le_mandan_ningun_texto_a_la_persona()
    {
        var telefono = ConversacionNueva(out var deWhatsApp);
        await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "hola");
        var id = IdDe(telefono);

        var cierre = await _configurador.PutAsync($"/mensajeria/conversaciones/{id}/cerrar", null);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal("Cerrada", Estado(id));

        var reapertura = await _configurador.PutAsync($"/mensajeria/conversaciones/{id}/reabrir", null);
        Assert.Equal(HttpStatusCode.OK, reapertura.StatusCode);
        Assert.Equal("DerivadaHumano", Estado(id));

        Assert.Empty(_canal.A(telefono));                                                       // a la persona no le llegó nada
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c AND Origen = 'Sistema'", ("@c", id)));   // ni queda un aviso que no salió
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c", ("@c", id)));                         // solo lo que escribió ella
    }

    [Fact]
    public async Task Si_la_persona_vuelve_a_escribir_despues_de_cerrada_vuelve_a_quedar_para_atender_y_avisa_de_nuevo()
    {
        var telefono = ConversacionNueva(out var deWhatsApp);
        await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "hola");
        var id = IdDe(telefono);
        (await _configurador.PutAsync($"/mensajeria/conversaciones/{id}/cerrar", null)).EnsureSuccessStatusCode();
        Assert.Equal("Cerrada", Estado(id));

        var r = await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "perdón, una cosa más");

        Assert.Equal("DerivadaHumano", Estado(id));
        Assert.Equal(0, r.GetProperty("respuestas").GetArrayLength());                          // el asistente no retoma desde el menú
        Assert.Empty(_canal.A(telefono));
        Assert.Equal(2, Avisos(id, "ChatNuevo"));                                                // cerrada, así que el equipo se entera otra vez
    }

    // ───────────── Frase de riesgo ─────────────

    [Fact]
    public async Task Una_frase_de_riesgo_marca_la_conversacion_urgente_y_avisa_sin_mandarle_nada_al_paciente()
    {
        var telefono = ConversacionNueva(out var deWhatsApp);

        var r = await Entra(_webhook, "/mensajeria/whatsapp/entrante", deWhatsApp, "no quiero vivir");

        var id = IdDe(telefono);
        Assert.Equal(0, r.GetProperty("respuestas").GetArrayLength());
        Assert.Empty(_canal.A(telefono));                                                       // el mensaje de emergencia viene apagado
        Assert.Equal("Urgente", Estado(id));
        Assert.Equal(1, Avisos(id, "Urgencia"));
        Assert.Equal(0, Avisos(id, "ChatNuevo"));
        var texto = (string)DatosDePrueba.Escalar("SELECT Mensaje FROM NotificacionInterna WHERE IdConversacion = @c AND Tipo = 'Urgencia'", ("@c", id))!;
        Assert.Contains("no se le mandó ningún mensaje", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("El bot respondió", texto);                                       // no se afirma algo que no pasó
    }

    // ───────────── El simulador entra por el mismo lado ─────────────

    [Fact]
    public async Task El_simulador_tambien_atiende_el_personal_y_el_asistente_no_contesta()
    {
        var telefono = ConversacionNueva(out _);

        var r = await Entra(_sinToken, "/mensajeria/simulado/entrante", telefono, "hola");

        var id = IdDe(telefono);
        Assert.Equal(0, r.GetProperty("respuestas").GetArrayLength());
        Assert.Empty(_canal.A(telefono));
        Assert.Equal("DerivadaHumano", Estado(id));
        Assert.Equal(1, Avisos(id, "ChatNuevo"));
    }

    // ───────────── El valor por defecto sigue siendo el modo Asistente ─────────────

    [Fact]
    public async Task Sin_configurar_el_modo_el_asistente_contesta_como_siempre()
    {
        var canal = new CanalQueRegistra();
        using var fabrica = Fabrica(null, canal);
        using var cliente = fabrica.CreateClient();
        var telefono = TelefonoNuevo();
        _datos.AlLimpiar("Conversacion", "Telefono", telefono);

        var r = await cliente.PostAsJsonAsync("/mensajeria/simulado/entrante", new { telefono, texto = "hola" });

        r.EnsureSuccessStatusCode();
        Assert.NotEmpty(canal.A(telefono));                                                     // el menú del asistente salió por el canal
        Assert.Equal(0, Avisos(IdDe(telefono), "ChatNuevo"));                                   // y no hay aviso de "chat nuevo": eso es del modo Personal
    }
}
