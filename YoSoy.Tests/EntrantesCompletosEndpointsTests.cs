using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Los mensajes completos que entran por el webhook de whatsapp-service (contrato v2: tipo, id, ts y nombre además de telefono y texto), por la aplicación entera y la base de pruebas, con el
// canal de WhatsApp reemplazado por uno que anota cada envío (no sale nada de la máquina).
//
//  · EntrantesCompletosEndpointsTests (modo Personal, con la migración 17: se saltea contra una base sin ella con -Filtro 'Migracion!=17'): se guarda el id, el tipo y el nombre; un repetido
//    no se guarda dos veces ni aunque la API se reinicie; y el cuerpo de siempre sigue andando.
//  · EntrantesSinLasColumnasTests (los dos modos y SIN depender de que la base tenga la 17: corren igual contra yosoy_test y contra una base sin la migración, ver
//    Scripts\\crear-base-de-pruebas.ps1 -SinMigracion17): en el modo Asistente la API NO toca ninguna columna nueva ni pregunta por ellas; en el modo Personal sin ellas sigue guardando.
internal sealed class AppDeEntrantes : IDisposable
{
    public const string Secreto = "secreto-de-prueba-de-los-entrantes-0123456789";
    private const string ClaveJwt = "clave-de-prueba-de-los-entrantes-0123456789-abcdef";

    public sealed class Canal : IMessageChannel
    {
        public string Nombre => "Simulado";
        public readonly List<(string Telefono, string Texto)> Envios = new();
        public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto)
        {
            lock (Envios) Envios.Add((telefonoDestino, texto));
            return Task.FromResult(new ResultadoEnvio(true, "x"));
        }
        public int Cantidad { get { lock (Envios) return Envios.Count; } }
    }

    /// <summary>La consulta "¿tiene la base las columnas de la migración 17?" reemplazada por una respuesta fija, contando cuántas veces se preguntó.</summary>
    public sealed class Sonda : ISondaDeColumnasChatPersonal
    {
        private readonly bool _responde;
        private int _consultas;
        public Sonda(bool responde) => _responde = responde;
        public int Consultas => _consultas;
        public bool Existen() { Interlocked.Increment(ref _consultas); return _responde; }
    }

    public readonly Canal Envios = new();
    public readonly LogsCapturados Logs = new();
    public readonly Sonda? SondaFija;
    public readonly WebApplicationFactory<Program> Fabrica;
    public readonly HttpClient Webhook, CRM;
    private readonly DatosDePrueba _datos;

    public AppDeEntrantes(DatosDePrueba datos, string modo, bool? columnasExisten = null)
    {
        _datos = datos;
        if (columnasExisten is bool existen) SondaFija = new Sonda(existen);
        var idUsuarioCrm = datos.Usuario();
        Fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", ClaveJwt);
            b.UseSetting("Mensajeria:WhatsApp:Secreto", Secreto);
            b.UseSetting("Mensajeria:ModoAtencion", modo);
            b.ConfigureServices(servicios =>
            {
                foreach (var d in servicios.Where(x => x.ServiceType == typeof(IMessageChannel)).ToList()) servicios.Remove(d);
                servicios.AddSingleton<IMessageChannel>(Envios);
                servicios.AddSingleton<ILoggerProvider>(Logs);
                if (SondaFija is not null)
                {
                    foreach (var d in servicios.Where(x => x.ServiceType == typeof(ISondaDeColumnasChatPersonal)).ToList()) servicios.Remove(d);
                    servicios.AddSingleton<ISondaDeColumnasChatPersonal>(SondaFija);
                }
            });
        });
        var emisor = Fabrica.Services.GetRequiredService<JwtEmisorService>();
        CRM = Fabrica.CreateClient();
        CRM.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", emisor.Emitir(idUsuarioCrm, Roles.Crm));
        Webhook = Fabrica.CreateClient();
        Webhook.DefaultRequestHeaders.Add("X-Shared-Secret", Secreto);
    }

    public void Dispose() => Fabrica.Dispose();

    // El teléfono de WhatsApp es el internacional (549 + 10 dígitos); la conversación guarda los últimos 10.
    public string TelefonoNuevo(out string deWhatsApp)
    {
        var telefono = "11" + Random.Shared.Next(10_000_000, 99_999_999);
        deWhatsApp = "549" + telefono;
        _datos.AlLimpiar("Conversacion", "Telefono", telefono);   // y en cascada sus mensajes y avisos
        return telefono;
    }

    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Entra(object cuerpo)
    {
        var r = await Webhook.PostAsJsonAsync("/mensajeria/whatsapp/entrante", cuerpo);
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, texto.Length == 0 ? default : JsonDocument.Parse(texto).RootElement.Clone());
    }

    public async Task<JsonElement> Get(string ruta)
    {
        var r = await CRM.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    public static int IdDe(string telefono) =>
        Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdConversacion FROM Conversacion WHERE Telefono = @t", ("@t", telefono)));

    public static int Mensajes(int idConversacion) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c", ("@c", idConversacion));

    public static bool BaseTieneLaMigracion17() =>
        DatosDePrueba.Contar(@"SELECT CASE WHEN COL_LENGTH('dbo.MensajeChat', 'IdExterno') IS NOT NULL AND COL_LENGTH('dbo.MensajeChat', 'Tipo') IS NOT NULL
                                          AND COL_LENGTH('dbo.Conversacion', 'NombreContacto') IS NOT NULL THEN 1 ELSE 0 END") == 1;
}

[Trait("Categoria", "Integracion")]
[Trait("Migracion", "17")]   // necesita las columnas de la migración 17: se saltea con -Filtro 'Migracion!=17' contra una base sin ella
public class EntrantesCompletosEndpointsTests : IDisposable
{
    private readonly DatosDePrueba _datos = new();
    private readonly AppDeEntrantes _app;

    public EntrantesCompletosEndpointsTests() => _app = new AppDeEntrantes(_datos, ModosAtencion.Personal);

    public void Dispose()
    {
        _app.Dispose();
        _datos.Dispose();
    }

    private static string Tipo(int idMensaje) => (string)DatosDePrueba.Escalar("SELECT Tipo FROM MensajeChat WHERE IdMensaje = @i", ("@i", idMensaje))!;
    private static string? IdExterno(int idMensaje) => (string?)DatosDePrueba.Escalar("SELECT IdExterno FROM MensajeChat WHERE IdMensaje = @i", ("@i", idMensaje));
    private static string? Nombre(int idConversacion) => (string?)DatosDePrueba.Escalar("SELECT NombreContacto FROM Conversacion WHERE IdConversacion = @c", ("@c", idConversacion));

    private static string IdNuevo() => "3EB0" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    // ───────────── Una foto o un audio ─────────────

    [Fact]
    public async Task Una_foto_por_el_webhook_se_guarda_como_aviso_con_su_tipo_y_su_id_y_no_se_le_contesta_nada()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);
        var id = IdNuevo();

        var (estado, r) = await _app.Entra(new { telefono = deWhatsApp, texto = "[Imagen] mi receta", tipo = "imagen", id, nombre = "Juan P." });

        Assert.Equal(HttpStatusCode.OK, estado);
        var idConversacion = AppDeEntrantes.IdDe(telefono);
        var idMensaje = r.GetProperty("idMensaje").GetInt32();
        Assert.Equal(idConversacion, r.GetProperty("idConversacion").GetInt32());
        Assert.Equal(0, r.GetProperty("respuestas").GetArrayLength());
        Assert.False(r.TryGetProperty("ignorado", out _));
        Assert.Equal(("imagen", id), (Tipo(idMensaje), IdExterno(idMensaje)));
        Assert.Equal(0, _app.Envios.Cantidad);                                              // nada sale por el canal
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE IdConversacion = @c AND Tipo = 'ChatNuevo'", ("@c", idConversacion)));

        // El CRM ve el aviso del tipo y el texto, y NO el id interno.
        var mensajes = await _app.Get($"/mensajeria/conversaciones/{idConversacion}/mensajes");
        var m = Assert.Single(mensajes.EnumerateArray());
        Assert.Equal("imagen", m.GetProperty("tipo").GetString());
        Assert.Equal("[Imagen] mi receta", m.GetProperty("texto").GetString());
        Assert.False(m.TryGetProperty("idExterno", out _));
        Assert.DoesNotContain(id, m.GetRawText());
    }

    [Fact]
    public async Task Cada_tipo_se_guarda_con_su_tipo_y_uno_desconocido_se_guarda_como_otro()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);

        foreach (var tipo in new[] { "audio", "video", "documento", "sticker", "ubicacion", "contacto", "gif" })
            await _app.Entra(new { telefono = deWhatsApp, texto = $"[{tipo}]", tipo, id = IdNuevo() });

        var tipos = (await _app.Get($"/mensajeria/conversaciones/{AppDeEntrantes.IdDe(telefono)}/mensajes")).EnumerateArray().Select(m => m.GetProperty("tipo").GetString()).ToList();
        Assert.Equal(new[] { "audio", "video", "documento", "sticker", "ubicacion", "contacto", "otro" }, tipos);
    }

    [Fact]
    public async Task Un_texto_con_los_campos_nuevos_se_guarda_como_texto_y_con_su_id()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);
        var id = IdNuevo();

        var (_, r) = await _app.Entra(new { telefono = deWhatsApp, texto = "hola", tipo = "texto", id });

        var idMensaje = r.GetProperty("idMensaje").GetInt32();
        Assert.Equal(("texto", id), (Tipo(idMensaje), IdExterno(idMensaje)));
        Assert.Equal(1, AppDeEntrantes.Mensajes(AppDeEntrantes.IdDe(telefono)));
    }

    // ───────────── El mismo mensaje dos veces ─────────────

    [Fact]
    public async Task El_mismo_id_otra_vez_no_se_guarda_dos_veces_ni_avisa_otra_vez()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);
        var id = IdNuevo();

        await _app.Entra(new { telefono = deWhatsApp, texto = "hola", id });
        var (estado, repetido) = await _app.Entra(new { telefono = deWhatsApp, texto = "hola", id });

        Assert.Equal(HttpStatusCode.OK, estado);                                            // un 200: whatsapp-service no tiene nada que reintentar
        Assert.Equal("duplicado", repetido.GetProperty("ignorado").GetString());
        var idConversacion = AppDeEntrantes.IdDe(telefono);
        Assert.Equal(1, AppDeEntrantes.Mensajes(idConversacion));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE IdConversacion = @c AND Tipo = 'ChatNuevo'", ("@c", idConversacion)));
    }

    [Fact]
    public async Task Un_repetido_tampoco_entra_despues_de_reiniciar_la_API_porque_lo_frena_el_indice_de_la_base()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);
        var id = IdNuevo();
        await _app.Entra(new { telefono = deWhatsApp, texto = "hola", id });

        // Otra instancia de la aplicación = una API recién reiniciada: no recuerda nada en memoria.
        using var reiniciada = new AppDeEntrantes(_datos, ModosAtencion.Personal);
        var (_, repetido) = await reiniciada.Entra(new { telefono = deWhatsApp, texto = "hola", id });

        Assert.Equal("duplicado", repetido.GetProperty("ignorado").GetString());
        Assert.Equal(1, AppDeEntrantes.Mensajes(AppDeEntrantes.IdDe(telefono)));
    }

    [Fact]
    public async Task El_mismo_id_en_otra_conversacion_es_otro_mensaje_y_sin_id_no_se_descarta_nada()
    {
        var uno = _app.TelefonoNuevo(out var deUno);
        var otro = _app.TelefonoNuevo(out var deOtro);
        var id = IdNuevo();

        await _app.Entra(new { telefono = deUno, texto = "hola", id });
        await _app.Entra(new { telefono = deOtro, texto = "hola", id });                    // otro teléfono, mismo id: es otro mensaje
        await _app.Entra(new { telefono = deUno, texto = "sin id" });
        await _app.Entra(new { telefono = deUno, texto = "sin id" });

        Assert.Equal(3, AppDeEntrantes.Mensajes(AppDeEntrantes.IdDe(uno)));
        Assert.Equal(1, AppDeEntrantes.Mensajes(AppDeEntrantes.IdDe(otro)));
    }

    [Fact]
    public async Task Dos_pedidos_a_la_vez_con_el_mismo_id_dejan_un_solo_mensaje()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);
        var id = IdNuevo();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _app.Entra(new { telefono = deWhatsApp, texto = "hola", id })));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.Estado));
        Assert.Equal(1, AppDeEntrantes.Mensajes(AppDeEntrantes.IdDe(telefono)));
        Assert.Equal(7, respuestas.Count(r => r.Cuerpo.TryGetProperty("ignorado", out _)));
    }

    // ───────────── El nombre del perfil ─────────────

    [Fact]
    public async Task El_nombre_del_perfil_se_guarda_en_la_conversacion_y_sale_en_la_lista_y_en_el_panel()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);

        await _app.Entra(new { telefono = deWhatsApp, texto = "hola", id = IdNuevo(), nombre = "  Juan   Pérez 🌸 " });

        var idConversacion = AppDeEntrantes.IdDe(telefono);
        Assert.Equal("Juan Pérez 🌸", Nombre(idConversacion));                              // limpio: sin espacios de más
        var lista = await _app.Get("/mensajeria/conversaciones");
        var fila = lista.EnumerateArray().Single(c => c.GetProperty("idConversacion").GetInt32() == idConversacion);
        Assert.Equal("Juan Pérez 🌸", fila.GetProperty("nombreContacto").GetString());
        Assert.Equal(JsonValueKind.Null, fila.GetProperty("nombrePaciente").ValueKind);     // no hay paciente vinculado: el nombre es del perfil, no una identidad
        var panel = await _app.Get($"/mensajeria/conversaciones/{idConversacion}/paciente");
        Assert.Equal("Juan Pérez 🌸", panel.GetProperty("nombreContacto").GetString());
        Assert.Equal(JsonValueKind.Null, panel.GetProperty("paciente").ValueKind);
    }

    [Fact]
    public async Task Si_la_persona_cambia_su_nombre_de_perfil_se_actualiza_y_un_mensaje_sin_nombre_no_lo_borra()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);

        await _app.Entra(new { telefono = deWhatsApp, texto = "uno", id = IdNuevo(), nombre = "Juan" });
        await _app.Entra(new { telefono = deWhatsApp, texto = "dos", id = IdNuevo() });
        var idConversacion = AppDeEntrantes.IdDe(telefono);
        Assert.Equal("Juan", Nombre(idConversacion));

        await _app.Entra(new { telefono = deWhatsApp, texto = "tres", id = IdNuevo(), nombre = "Juan Pérez" });
        Assert.Equal("Juan Pérez", Nombre(idConversacion));
    }

    [Fact]
    public async Task Un_nombre_largo_o_con_caracteres_raros_se_acota_y_no_rechaza_el_mensaje()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);

        var (estado, _) = await _app.Entra(new { telefono = deWhatsApp, texto = "hola", id = IdNuevo(), nombre = "\u202e" + new string('a', 300) + "\n" });

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(new string('a', 100), Nombre(AppDeEntrantes.IdDe(telefono)));
    }

    [Fact]
    public async Task El_nombre_del_perfil_no_aparece_en_ningun_log_de_la_API()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);

        await _app.Entra(new { telefono = deWhatsApp, texto = "[Imagen] no quiero vivir", tipo = "imagen", id = IdNuevo(), nombre = "Zoraida Xylofona" });   // hasta con una urgencia
        await _app.Entra(new { telefono = deWhatsApp, texto = "hola", id = IdNuevo(), nombre = "Zoraida Xylofona" });

        Assert.DoesNotContain("Zoraida", _app.Logs.Todo);
        Assert.DoesNotContain("Xylofona", _app.Logs.Todo);
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Detalle LIKE '%Zoraida%'"));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Mensaje LIKE '%Zoraida%' OR Titulo LIKE '%Zoraida%'"));
        _ = telefono;
    }

    // ───────────── La hora ─────────────

    [Fact]
    public async Task La_hora_real_del_mensaje_se_usa_si_es_razonable_y_si_es_absurda_se_usa_la_del_servidor()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);
        var hace10Minutos = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        var (_, sinHora) = await _app.Entra(new { telefono = deWhatsApp, texto = "sin hora", id = IdNuevo() });
        var (_, conHora) = await _app.Entra(new { telefono = deWhatsApp, texto = "con hora", id = IdNuevo(), ts = hace10Minutos });
        var (_, futura) = await _app.Entra(new { telefono = deWhatsApp, texto = "del futuro", id = IdNuevo(), ts = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds() });
        var (_, antigua) = await _app.Entra(new { telefono = deWhatsApp, texto = "de hace una semana", id = IdNuevo(), ts = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds() });

        DateTime Hora(JsonElement r) => (DateTime)DatosDePrueba.Escalar("SELECT FechaHora FROM MensajeChat WHERE IdMensaje = @i", ("@i", r.GetProperty("idMensaje").GetInt32()))!;
        var servidor = Hora(sinHora);
        Assert.InRange((servidor - Hora(conHora)).TotalMinutes, 9.5, 10.5);                 // diez minutos antes que "ahora": la hora que trajo el mensaje
        Assert.InRange(Math.Abs((Hora(futura) - servidor).TotalSeconds), 0, 30);            // una hora futura o vieja se ignora: queda la del servidor
        Assert.InRange(Math.Abs((Hora(antigua) - servidor).TotalSeconds), 0, 30);
        _ = telefono;
    }

    // ───────────── El cuerpo de siempre ─────────────

    [Fact]
    public async Task El_cuerpo_de_siempre_sigue_andando_y_la_respuesta_es_la_de_siempre()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);

        var (estado, r) = await _app.Entra(new { telefono = deWhatsApp, texto = "hola" });

        Assert.Equal(HttpStatusCode.OK, estado);
        var idMensaje = r.GetProperty("idMensaje").GetInt32();
        Assert.Equal(("texto", (string?)null), (Tipo(idMensaje), IdExterno(idMensaje)));
        Assert.Equal(AppDeEntrantes.IdDe(telefono), r.GetProperty("idConversacion").GetInt32());
        Assert.False(r.TryGetProperty("ignorado", out _));
    }

    [Theory]
    [InlineData("tiene espacios")]
    [InlineData("con'comilla")]
    [InlineData("ñandú")]
    public async Task Un_id_raro_no_rechaza_el_mensaje_solo_no_se_guarda(string id)
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);

        var (estado, r) = await _app.Entra(new { telefono = deWhatsApp, texto = "hola", id });

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Null(IdExterno(r.GetProperty("idMensaje").GetInt32()));
        _ = telefono;
    }

    [Fact]
    public async Task Un_texto_vacio_o_un_telefono_invalido_siguen_dando_400_y_no_dejan_nada()
    {
        var telefono = _app.TelefonoNuevo(out var deWhatsApp);

        var (vacio, _) = await _app.Entra(new { telefono = deWhatsApp, texto = "   ", id = IdNuevo() });
        var (invalido, _) = await _app.Entra(new { telefono = "12", texto = "hola", id = IdNuevo() });

        Assert.Equal(HttpStatusCode.BadRequest, vacio);
        Assert.Equal(HttpStatusCode.BadRequest, invalido);
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Conversacion WHERE Telefono = @t", ("@t", telefono)));
    }

    [Fact]
    public async Task El_webhook_sigue_cerrado_sin_el_secreto_aunque_traiga_los_campos_nuevos()
    {
        _app.TelefonoNuevo(out var deWhatsApp);
        using var sinSecreto = _app.Fabrica.CreateClient();

        var r = await sinSecreto.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono = deWhatsApp, texto = "hola", tipo = "imagen", id = IdNuevo(), nombre = "Juan" });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}

// Lo que NO depende de que la base tenga la migración 17: corre igual contra yosoy_test y contra una base sin ella (Scripts\crear-base-de-pruebas.ps1 -SinMigracion17).
[Trait("Categoria", "Integracion")]
public class EntrantesSinLasColumnasTests : IDisposable
{
    private readonly DatosDePrueba _datos = new();

    public void Dispose() => _datos.Dispose();

    // ───────────── Modo Asistente: la API no toca nada de la migración 17 ─────────────

    [Fact]
    public async Task En_modo_Asistente_una_foto_no_se_atiende_no_crea_una_conversacion_y_no_se_le_contesta_nada()
    {
        using var app = new AppDeEntrantes(_datos, ModosAtencion.Asistente, columnasExisten: true);
        var telefono = app.TelefonoNuevo(out var deWhatsApp);

        var (estado, r) = await app.Entra(new { telefono = deWhatsApp, texto = "[Imagen] mi receta", tipo = "imagen", id = "3EB0AAAA", nombre = "Juan" });

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal("no_textual", r.GetProperty("ignorado").GetString());
        Assert.Equal(0, r.GetProperty("respuestas").GetArrayLength());
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Conversacion WHERE Telefono = @t", ("@t", telefono)));
        Assert.Equal(0, app.Envios.Cantidad);
    }

    [Fact]
    public async Task En_modo_Asistente_un_texto_con_los_campos_nuevos_lo_atiende_el_asistente_y_no_se_escribe_nada_de_la_17()
    {
        using var app = new AppDeEntrantes(_datos, ModosAtencion.Asistente, columnasExisten: true);   // ni aunque la base las tenga
        var telefono = app.TelefonoNuevo(out var deWhatsApp);

        var (estado, r) = await app.Entra(new { telefono = deWhatsApp, texto = "hola", tipo = "texto", id = "3EB0BBBB", ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), nombre = "Juan" });

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.True(r.GetProperty("respuestas").GetArrayLength() > 0);                      // el menú del asistente, como siempre
        Assert.False(r.TryGetProperty("ignorado", out _));
        Assert.False(r.TryGetProperty("idMensaje", out _));                                 // la respuesta es la de siempre
        var idConversacion = AppDeEntrantes.IdDe(telefono);
        Assert.True(AppDeEntrantes.Mensajes(idConversacion) >= 2);                          // lo que escribió la persona y lo que contestó el asistente
        Assert.True(app.Envios.Cantidad >= 1);                                              // y el asistente sí contesta por el canal

        // Si la base tiene las columnas, la API no las escribió.
        if (AppDeEntrantes.BaseTieneLaMigracion17())
        {
            Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c AND IdExterno IS NOT NULL", ("@c", idConversacion)));
            Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c AND Tipo <> 'texto'", ("@c", idConversacion)));
            Assert.Null(DatosDePrueba.Escalar("SELECT NombreContacto FROM Conversacion WHERE IdConversacion = @c", ("@c", idConversacion)));
        }
    }

    [Fact]
    public async Task En_modo_Asistente_la_API_ni_pregunta_por_las_columnas_nuevas_en_ningun_recorrido()
    {
        using var app = new AppDeEntrantes(_datos, ModosAtencion.Asistente, columnasExisten: true);
        var telefono = app.TelefonoNuevo(out var deWhatsApp);

        // El webhook (texto y foto), la lista, el hilo, el panel y el simulador: ninguno toca la 17 ni pregunta por ella.
        await app.Entra(new { telefono = deWhatsApp, texto = "hola", id = "3EB0CCCC", nombre = "Juan" });
        await app.Entra(new { telefono = deWhatsApp, texto = "[Audio]", tipo = "audio", id = "3EB0DDDD" });
        var idConversacion = AppDeEntrantes.IdDe(telefono);
        var lista = await app.Get("/mensajeria/conversaciones");
        var mensajes = await app.Get($"/mensajeria/conversaciones/{idConversacion}/mensajes");
        var panel = await app.Get($"/mensajeria/conversaciones/{idConversacion}/paciente");
        await app.Get("/capacidades");
        var telefonoSimulado = app.TelefonoNuevo(out _);
        var simulado = await app.CRM.PostAsJsonAsync("/mensajeria/simulado/entrante", new { telefono = telefonoSimulado, texto = "hola" });
        Assert.Equal(HttpStatusCode.OK, simulado.StatusCode);

        Assert.Equal(0, app.SondaFija!.Consultas);

        // Y las respuestas son las de siempre: sin los campos nuevos.
        Assert.DoesNotContain("nombreContacto", lista.GetRawText());
        Assert.DoesNotContain("\"tipo\"", mensajes.GetRawText());
        Assert.DoesNotContain("idExterno", mensajes.GetRawText());
        _ = panel;
    }

    // ───────────── Modo Personal sin las columnas (la base sin la 17, o todavía sin aplicar) ─────────────

    [Fact]
    public async Task En_modo_Personal_sin_las_columnas_los_mensajes_se_guardan_igual_y_el_log_avisa_que_falta_la_migracion()
    {
        using var app = new AppDeEntrantes(_datos, ModosAtencion.Personal, columnasExisten: false);   // como si la base no la tuviera
        var telefono = app.TelefonoNuevo(out var deWhatsApp);

        var (estado, foto) = await app.Entra(new { telefono = deWhatsApp, texto = "[Imagen] mi receta", tipo = "imagen", id = "3EB0EEEE", nombre = "Juan" });
        var (_, texto) = await app.Entra(new { telefono = deWhatsApp, texto = "hola", id = "3EB0FFFF" });
        var (_, repetido) = await app.Entra(new { telefono = deWhatsApp, texto = "hola", id = "3EB0FFFF" });

        Assert.Equal(HttpStatusCode.OK, estado);
        var idConversacion = AppDeEntrantes.IdDe(telefono);
        Assert.Equal(2, AppDeEntrantes.Mensajes(idConversacion));                           // la foto (como aviso) y el texto; el repetido lo frenó la memoria
        Assert.Equal("duplicado", repetido.GetProperty("ignorado").GetString());
        Assert.Equal(0, foto.GetProperty("respuestas").GetArrayLength());
        Assert.Equal(0, texto.GetProperty("respuestas").GetArrayLength());
        Assert.Equal(0, app.Envios.Cantidad);
        Assert.Equal("[Imagen] mi receta", (string)DatosDePrueba.Escalar("SELECT TOP 1 Texto FROM MensajeChat WHERE IdConversacion = @c ORDER BY IdMensaje", ("@c", idConversacion))!);

        // Sin las columnas: la lista y el hilo no traen nombre ni tipo, y el log lo dice (una vez, no por cada mensaje).
        var mensajes = await app.Get($"/mensajeria/conversaciones/{idConversacion}/mensajes");
        Assert.DoesNotContain("\"tipo\"", mensajes.GetRawText());
        var avisos = app.Logs.Lineas.Where(l => l.Contains("migración 17") && l.StartsWith("Error|")).ToList();
        Assert.Single(avisos);
        Assert.Contains("add_chat_personal_entrantes.sql", avisos[0]);
        Assert.DoesNotContain("Juan", app.Logs.Todo);
    }
}
