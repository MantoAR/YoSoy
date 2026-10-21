using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// D2 del chat para el personal (con el modelo de roles de la etapa 2): los chats son SOLO del módulo CRM (atención al cliente). Los profesionales atienden, no
// contestan mensajes: no ven ningún chat ni reciben sus notificaciones. Un Profesional, un paciente o un Configurador SIN el módulo CRM reciben 403 en cada ruta
// de /mensajeria, y sin sesión 401. Quien tiene los dos módulos (Configurador y CRM) entra con el token del módulo CRM. La única excepción de los 403 para el
// Configurador es la vinculación del número de WhatsApp (/mensajeria/whatsapp/estado, desvincular, confirmar-vinculacion): no es leer ni contestar chats, es una
// tarea de administración, y por eso es del Configurador (y el CRM, a su vez, no la hace).
//
// Estas pruebas no miran una lista escrita a mano: leen la tabla de rutas de la aplicación (ReflexionEndpoints, la misma que usa GuardiaEndpointsTests), así que una
// ruta nueva dentro de /mensajeria que quede abierta a un profesional rompe la prueba en vez de pasar en silencio. Las únicas rutas de /mensajeria que no piden sesión
// son las que no tienen una persona del otro lado: el simulador y el webhook de whatsapp-service.
//
// ChatsSoloCrmTests no toca la base. ChatsSoloCrmBaseRealTests (abajo) comprueba, contra la base de pruebas, que el CRM sí entra y que
// lo que no era de los chats (la campanita de turnos del profesional) sigue andando.
public class ChatsSoloCrmTests
{
    private const string ClaveJwt = "clave-de-prueba-de-los-chats-solo-crm-0123456789";

    // Las únicas rutas de /mensajeria sin sesión: no hay una persona del otro lado (ver GuardiaEndpointsTests.Publicos).
    private static readonly HashSet<(string Metodo, string Ruta)> SinSesion = new()
    {
        ("POST", "/mensajeria/simulado/entrante"),
        ("GET", "/mensajeria/simulado/conversacion"),
        ("POST", "/mensajeria/whatsapp/entrante")
    };

    // Un id que no existe: si una ruta quedara abierta por error, el pedido de la prueba no toca ninguna fila real.
    private const string IdInexistente = "2147483647";

    private static WebApplicationFactory<Program> Fabrica() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Jwt:Key", ClaveJwt));

    private static List<InfoEndpoint> RutasDeChats(WebApplicationFactory<Program> fabrica) =>
        ReflexionEndpoints.Listar(fabrica)
            .Where(e => e.Ruta.StartsWith("/mensajeria", StringComparison.OrdinalIgnoreCase) && !SinSesion.Contains((e.Metodo, e.Ruta)))
            .ToList();

    private static HttpClient ClienteCon(WebApplicationFactory<Program> fabrica, string? token)
    {
        var c = fabrica.CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    // Pide cada ruta con ese cliente y devuelve las que NO contestaron lo esperado. La autorización corre antes que el cuerpo y que los ids, así que alcanza con
    // un pedido vacío contra un id que no existe.
    private static async Task<List<string>> RecorrerLasRutas(WebApplicationFactory<Program> fabrica, HttpClient cliente, HttpStatusCode esperado)
    {
        var distintas = new List<string>();
        foreach (var ruta in RutasDeChats(fabrica))
        {
            var url = Regex.Replace(ruta.Ruta, @"\{[^}]+\}", IdInexistente);
            using var pedido = new HttpRequestMessage(new HttpMethod(ruta.Metodo), url);
            if (ruta.Metodo is "POST" or "PUT" or "DELETE") pedido.Content = JsonContent.Create(new { });
            using var respuesta = await cliente.SendAsync(pedido);
            if (respuesta.StatusCode != esperado) distintas.Add($"{ruta.Metodo} {ruta.Ruta} -> {(int)respuesta.StatusCode}");
        }
        return distintas;
    }

    [Fact]
    public void Toda_ruta_de_chats_exige_sesion()
    {
        using var fabrica = Fabrica();

        var rutas = RutasDeChats(fabrica);

        // Que la lista no esté vacía por un cambio en cómo se leen las rutas: hoy hay unas treinta y cuatro.
        Assert.True(rutas.Count >= 30, $"Se esperaban al menos 30 rutas de /mensajeria y hay {rutas.Count}: ¿cambió la forma de listarlas?");
        var mal = rutas.Where(r => !r.RequiereAuth).ToList();
        Assert.True(mal.Count == 0,
            "Estas rutas de /mensajeria no piden sesión (y no están en la lista de las que no tienen una persona del otro lado):\n" +
            string.Join("\n", mal.Select(r => $"  {r.Metodo} {r.Ruta}")));
    }

    [Fact]
    public void Las_rutas_de_whatsapp_del_Configurador_tambien_estan_en_la_lista()
    {
        // El grupo /mensajeria/whatsapp (estado, QR, desvincular) es de otro grupo de rutas: la prueba de arriba tiene que alcanzarlo también.
        using var fabrica = Fabrica();

        var rutas = RutasDeChats(fabrica).Select(r => $"{r.Metodo} {r.Ruta}").ToList();

        Assert.Contains("GET /mensajeria/whatsapp/estado", rutas);
        Assert.Contains("POST /mensajeria/whatsapp/desvincular", rutas);
        Assert.Contains("POST /mensajeria/whatsapp/confirmar-vinculacion", rutas);
        Assert.Contains("GET /mensajeria/conversaciones", rutas);
        Assert.Contains("POST /mensajeria/conversaciones/{id}/responder", rutas);
    }

    [Fact]
    public async Task Un_Profesional_recibe_403_en_cada_ruta_de_chats()
    {
        using var fabrica = Fabrica();
        var emisor = fabrica.Services.GetRequiredService<JwtEmisorService>();
        using var profesional = ClienteCon(fabrica, emisor.Emitir(1, Roles.Profesional, idProfesional: 1));

        var distintas = await RecorrerLasRutas(fabrica, profesional, HttpStatusCode.Forbidden);

        Assert.True(distintas.Count == 0, "Un Profesional tiene que recibir 403 en toda ruta de /mensajeria (D2). Contestaron otra cosa:\n  " + string.Join("\n  ", distintas));
    }

    [Fact]
    public async Task Un_paciente_recibe_403_en_cada_ruta_de_chats()
    {
        using var fabrica = Fabrica();
        var emisor = fabrica.Services.GetRequiredService<JwtEmisorService>();
        using var paciente = ClienteCon(fabrica, emisor.Emitir(1, Roles.Paciente, idPaciente: 1));

        var distintas = await RecorrerLasRutas(fabrica, paciente, HttpStatusCode.Forbidden);

        Assert.True(distintas.Count == 0, "Un paciente tiene que recibir 403 en toda ruta de /mensajeria. Contestaron otra cosa:\n  " + string.Join("\n  ", distintas));
    }

    // La vinculación del número de WhatsApp es de administración (Configurador), no de los chats: no es del CRM.
    private static bool EsDeVinculacion(string ruta) => ruta.StartsWith("/mensajeria/whatsapp/", StringComparison.OrdinalIgnoreCase);

    // Pide cada ruta y devuelve las que contestaron distinto de lo esperado (403 o no) según el grupo al que pertenecen.
    private static async Task<List<string>> RecorrerPorGrupo(WebApplicationFactory<Program> fabrica, HttpClient cliente, Func<string, bool> esperaProhibido)
    {
        var distintas = new List<string>();
        foreach (var ruta in RutasDeChats(fabrica))
        {
            var url = Regex.Replace(ruta.Ruta, @"\{[^}]+\}", IdInexistente);
            using var pedido = new HttpRequestMessage(new HttpMethod(ruta.Metodo), url);
            if (ruta.Metodo is "POST" or "PUT" or "DELETE") pedido.Content = JsonContent.Create(new { });
            using var respuesta = await cliente.SendAsync(pedido);
            var prohibido = respuesta.StatusCode == HttpStatusCode.Forbidden;
            if (esperaProhibido(ruta.Ruta) != prohibido || respuesta.StatusCode == HttpStatusCode.Unauthorized)
                distintas.Add($"{ruta.Metodo} {ruta.Ruta} -> {(int)respuesta.StatusCode}");
        }
        return distintas;
    }

    [Fact]
    public async Task Un_Configurador_sin_el_modulo_CRM_recibe_403_en_cada_ruta_de_chats()
    {
        using var fabrica = Fabrica();
        var emisor = fabrica.Services.GetRequiredService<JwtEmisorService>();
        using var configurador = ClienteCon(fabrica, emisor.Emitir(1, Roles.Configurador));

        // 403 en todo menos en la vinculación de WhatsApp (que es suya).
        var distintas = await RecorrerPorGrupo(fabrica, configurador, ruta => !EsDeVinculacion(ruta));

        Assert.True(distintas.Count == 0, "El Configurador sin CRM tiene que recibir 403 en los chats (y poder usar la vinculación de WhatsApp). Distinto:\n  " + string.Join("\n  ", distintas));
    }

    [Fact]
    public async Task Un_usuario_CRM_entra_a_los_chats_pero_no_a_la_vinculacion_de_WhatsApp()
    {
        using var fabrica = Fabrica();
        var emisor = fabrica.Services.GetRequiredService<JwtEmisorService>();
        using var crm = ClienteCon(fabrica, emisor.Emitir(1, Roles.Crm));

        // Ninguna ruta de chats da 403 (puede dar 400/404 por el id inexistente, que es lo esperado) y la vinculación sí: 403.
        var distintas = await RecorrerPorGrupo(fabrica, crm, ruta => EsDeVinculacion(ruta));

        Assert.True(distintas.Count == 0, "El CRM tiene que entrar a toda ruta de chats y recibir 403 en la vinculación de WhatsApp. Distinto:\n  " + string.Join("\n  ", distintas));
    }

    [Fact]
    public async Task Quien_tiene_Configurador_y_CRM_entra_a_los_chats_con_el_token_del_modulo_CRM_y_no_con_el_del_Configurador()
    {
        using var fabrica = Fabrica();
        var emisor = fabrica.Services.GetRequiredService<JwtEmisorService>();
        using var comoCrm = ClienteCon(fabrica, emisor.EmitirModulo(1, new SesionModulo(Modulos.Crm, new[] { Roles.Crm }, Array.Empty<string>(), null)));
        using var comoConfigurador = ClienteCon(fabrica, emisor.EmitirModulo(1, new SesionModulo(Modulos.Configurador, new[] { Roles.Configurador }, Array.Empty<string>(), null)));

        // El mismo usuario: trabajando como CRM atiende los chats; trabajando como Configurador, no (cada módulo es un token distinto).
        Assert.NotEqual(HttpStatusCode.Forbidden, (await comoCrm.GetAsync("/mensajeria/conversaciones")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await comoConfigurador.GetAsync("/mensajeria/conversaciones")).StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_se_recibe_401_en_cada_ruta_de_chats()
    {
        using var fabrica = Fabrica();
        using var sinSesion = ClienteCon(fabrica, null);

        var distintas = await RecorrerLasRutas(fabrica, sinSesion, HttpStatusCode.Unauthorized);

        Assert.True(distintas.Count == 0, "Sin sesión tiene que dar 401 en toda ruta de /mensajeria. Contestaron otra cosa:\n  " + string.Join("\n  ", distintas));
    }

    [Fact]
    public async Task Un_Profesional_no_pasa_ni_con_un_token_de_otro_profesional_ni_con_los_ids_de_su_propia_conversacion()
    {
        // El 403 no depende de qué se pida ni de a quién pertenezca: se decide por el rol, antes de mirar la conversación (no es un 404 de "no la ves").
        using var fabrica = Fabrica();
        var emisor = fabrica.Services.GetRequiredService<JwtEmisorService>();
        using var profesional = ClienteCon(fabrica, emisor.Emitir(7, Roles.Profesional, idProfesional: 7));

        foreach (var ruta in new[] { "/mensajeria/conversaciones", "/mensajeria/conversaciones/1", "/mensajeria/conversaciones/1/mensajes", "/mensajeria/conversaciones/1/paciente",
                                     "/mensajeria/notificaciones-internas?destinatario=Administrador", "/mensajeria/etiquetas", "/mensajeria/respuestas-rapidas" })
        {
            using var r = await profesional.GetAsync(ruta);
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"GET {ruta} -> {(int)r.StatusCode}");
        }
    }

    [Fact]
    public async Task El_simulador_y_el_webhook_no_cambian_siguen_sin_pedir_sesion_de_usuario()
    {
        // D2 no toca lo que no tiene una persona del otro lado: sin JWT, el simulador contesta (con el modulo disponible) y el webhook sigue cerrado por el secreto.
        using var fabrica = Fabrica();
        using var sinSesion = ClienteCon(fabrica, null);

        using var webhook = await sinSesion.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono = "5491155550001", texto = "hola" });
        Assert.Equal(HttpStatusCode.NotFound, webhook.StatusCode);   // sin secreto compartido: el mismo 404 de siempre, no un 401 ni un 403

        using var estado = await sinSesion.GetAsync("/mensajeria/simulado/conversacion?telefono=1100000000");
        Assert.NotEqual(HttpStatusCode.Unauthorized, estado.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, estado.StatusCode);
    }

    // ───────────── La pantalla ─────────────
    // El servidor ya contesta 403; la pantalla del Profesional además no ofrece nada de chats: ni el botón Chats ni el cajón ni sus avisos.

    private static string Pantalla(params string[] partes) =>
        File.ReadAllText(Path.Combine(new[] { VideoAccesoEndpointsTests.RaizDelRepositorio(), "PANTALLAS" }.Concat(partes).ToArray()));

    [Fact]
    public void El_shell_solo_carga_los_chats_para_el_modulo_crm()
    {
        var shell = Pantalla("shared", "shell.js");

        // La condición que decide quién tiene el botón Chats y la bandeja: el módulo "crm" (o el Configurador que además trae el permiso crm en la sesión,
        // el menú CRM del panel de gestión). Ni el profesional ni el Configurador sin CRM: lo decide grupoDeChats(rol).
        Assert.Matches(@"const grupoChats = grupoDeChats\(cfg\.rol\);\s*if \(grupoChats\) \{\s*capacidades = YS\.api\(""/capacidades""\)", shell);
        Assert.Matches(@"function grupoDeChats\(rol\) \{\s*if \(rol === ""crm""\) return GRUPO_CHATS;\s*(//[^
]*\s*)?if \(rol === ""administracion""\) return ITEMS_CHATS_ADMINISTRACION;\s*if \(rol === ""configurador"" && YS\.sesion\.tienePermiso\(""crm""\)\) return GRUPO_CRM_CONFIGURADOR;\s*return null;\s*\}", shell);
        Assert.DoesNotContain(@"cfg.rol === ""crm"" || cfg.rol === ""profesional""", shell);
        Assert.DoesNotMatch(@"cfg\.rol === ""profesional""\)\s*\{\s*capacidades", shell);
        Assert.DoesNotMatch(@"cfg\.rol === ""configurador""\)\s*\{\s*capacidades", shell);
        Assert.DoesNotMatch(@"rol === ""profesional"".*return GRUPO_", shell);
    }

    [Theory]
    [InlineData("/tomar")]
    [InlineData("/devolver")]
    [InlineData("/asignar")]
    public void La_bandeja_ya_no_llama_a_tomar_devolver_ni_asignar(string ruta) =>
        Assert.DoesNotContain(ruta, Pantalla("Pantalla_ChatSimulado", "js", "bandeja.js"));

    [Fact]
    public void La_bandeja_ya_no_tiene_el_filtro_Mias_ni_el_rol_profesional()
    {
        var bandeja = Pantalla("Pantalla_ChatSimulado", "js", "bandeja.js");

        Assert.DoesNotContain("soloMias", bandeja);
        Assert.DoesNotContain("data-mias", bandeja);
        Assert.DoesNotContain("idProfesional()", bandeja);
        Assert.DoesNotContain(@"rol === ""profesional""", bandeja);
    }

    [Fact]
    public void La_bandeja_dibuja_los_nodos_de_cada_mensaje_con_append_y_no_con_appendChild()
    {
        // Node.appendChild recibe UN solo nodo: con appendChild(...nodos) solo entraba el primero (el separador de día) y la burbuja del primer mensaje
        // de cada día no se dibujaba al abrir el hilo. append() acepta varios.
        var bandeja = Pantalla("Pantalla_ChatSimulado", "js", "bandeja.js");

        Assert.DoesNotContain("appendChild(...", bandeja);
        Assert.Contains("m.append(...nodosMensaje(", bandeja);
    }

    [Theory]
    [InlineData("Conversación marcada como resuelta.")]
    [InlineData("Conversación reabierta.")]
    public void Cerrar_y_reabrir_piden_una_vuelta_completa_de_la_lista(string aviso)
    {
        // Una vuelta incremental (?desdeMensaje=) solo trae las conversaciones con mensajes nuevos; cerrar o reabrir cambia el estado sin mensaje nuevo
        // (en el modo Personal ni siquiera se manda el aviso "Sistema"), así que sin la vuelta completa el cambio tardaba hasta 20 segundos en verse.
        var bandeja = Pantalla("Pantalla_ChatSimulado", "js", "bandeja.js");

        Assert.Matches(Regex.Escape(aviso) + @"[^\n]*\n\s*est\.vueltasSinCambios = 0;\s*await pulso\(true\);", bandeja);
    }

    [Fact]
    public void Ninguna_pantalla_del_Profesional_pide_nada_de_mensajeria()
    {
        // La campanita de turnos del profesional es de turnos (/profesionales/{id}/turnos-confirmados); nada de lo suyo pasa por /mensajeria.
        var carpeta = Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "PANTALLAS", "Pantalla_Profesional");
        foreach (var archivo in Directory.EnumerateFiles(carpeta, "*.*", SearchOption.AllDirectories).Where(a => a.EndsWith(".js") || a.EndsWith(".html")))
            Assert.False(File.ReadAllText(archivo).Contains("/mensajeria"), $"{Path.GetRelativePath(carpeta, archivo)} pide algo de /mensajeria");
    }
}

// Contra la base de pruebas: el CRM entra, y la campanita de turnos del profesional (que no es de los chats) sigue andando.
[Trait("Categoria", "Integracion")]
public class ChatsSoloCrmBaseRealTests : IAsyncLifetime
{
    private DatosDePrueba _datos = null!;
    private WebApplicationFactory<Program> _fabrica = null!;
    private HttpClient _crm = null!, _configurador = null!, _profesional = null!, _sinToken = null!;
    private int _idProfesional, _idPaciente;
    private string _nombreDelPaciente = null!;

    public Task InitializeAsync()
    {
        _datos = new DatosDePrueba();
        // Un apellido propio: la auditoría de apertura tiene que dejar solo ids, y el nombre no puede aparecer en el detalle.
        _nombreDelPaciente = "Juan Garcíaprueba " + _datos.Marca;
        _idPaciente = _datos.Paciente(_nombreDelPaciente);
        _idProfesional = _datos.Profesional();
        var usuarioCrm = _datos.Usuario();
        var usuarioConfigurador = _datos.Usuario();
        var usuarioProfesional = _datos.UsuarioDeProfesional(_idProfesional);

        _fabrica = new WebApplicationFactory<Program>();
        var emisor = _fabrica.Services.GetRequiredService<JwtEmisorService>();
        _crm = Con(emisor.Emitir(usuarioCrm, Roles.Crm));
        _configurador = Con(emisor.Emitir(usuarioConfigurador, Roles.Configurador));
        _profesional = Con(emisor.Emitir(usuarioProfesional, Roles.Profesional, idProfesional: _idProfesional));
        _sinToken = _fabrica.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _fabrica.Dispose();
        _datos.Dispose();   // las filas propias y, en cascada, las conversaciones que cuelgan de ellas
        return Task.CompletedTask;
    }

    private HttpClient Con(string token)
    {
        var c = _fabrica.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static string TelefonoNuevo() => "11" + Random.Shared.Next(10_000_000, 99_999_999);

    // Una conversación de prueba, armada como la arma el simulador (la persona escribe) y limpiada al terminar.
    private async Task<int> NuevaConversacion(string estado, int? idPaciente = null)
    {
        var telefono = TelefonoNuevo();
        _datos.AlLimpiar("Conversacion", "Telefono", telefono);
        (await _sinToken.PostAsJsonAsync("/mensajeria/simulado/entrante", new { telefono, texto = "hola, prueba de chats solo crm" })).EnsureSuccessStatusCode();
        var id = Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdConversacion FROM Conversacion WHERE Telefono = @t", ("@t", telefono)));
        DatosDePrueba.Ejecutar("UPDATE Conversacion SET Estado = @e, IdPaciente = @p WHERE IdConversacion = @i", ("@e", estado), ("@p", idPaciente), ("@i", id));
        return id;   // la auditoría que deje el CRM queda (es de solo agregar)
    }

    [Fact]
    public async Task El_CRM_entra_a_las_rutas_de_lectura_de_los_chats_y_el_Configurador_sin_CRM_no()
    {
        foreach (var ruta in new[] { "/mensajeria/conversaciones", "/mensajeria/etiquetas", "/mensajeria/respuestas-rapidas",
                                     "/mensajeria/notificaciones-internas?destinatario=Administrador", "/mensajeria/preguntas-frecuentes" })
        {
            using var r = await _crm.GetAsync(ruta);
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"GET {ruta} -> {(int)r.StatusCode}");
            using var sinCrm = await _configurador.GetAsync(ruta);
            Assert.True(sinCrm.StatusCode == HttpStatusCode.Forbidden, $"GET {ruta} como Configurador sin CRM -> {(int)sinCrm.StatusCode}");
        }
    }

    [Fact]
    public async Task El_CRM_ve_cualquier_conversacion_y_un_Profesional_no_ve_ninguna()
    {
        var sinPaciente = await NuevaConversacion(EstadoConversacion.Activa);
        var escalada = await NuevaConversacion(EstadoConversacion.DerivadaHumano);
        var urgente = await NuevaConversacion(EstadoConversacion.Urgente);
        var delPaciente = await NuevaConversacion(EstadoConversacion.Activa, _idPaciente);

        foreach (var id in new[] { sinPaciente, escalada, urgente, delPaciente })
        {
            using var comoCrm = await _crm.GetAsync($"/mensajeria/conversaciones/{id}");
            Assert.Equal(HttpStatusCode.OK, comoCrm.StatusCode);
            // Ni siquiera la que está escalada y sin dueño (antes la veía cualquier profesional) ni la de un paciente suyo: 403 por el rol, no un 404.
            using var comoProfesional = await _profesional.GetAsync($"/mensajeria/conversaciones/{id}");
            Assert.Equal(HttpStatusCode.Forbidden, comoProfesional.StatusCode);
            using var mensajes = await _profesional.GetAsync($"/mensajeria/conversaciones/{id}/mensajes");
            Assert.Equal(HttpStatusCode.Forbidden, mensajes.StatusCode);
        }
    }

    [Fact]
    public async Task Un_Profesional_no_puede_contestar_ni_cerrar_ni_tomar_una_conversacion_escalada()
    {
        var id = await NuevaConversacion(EstadoConversacion.DerivadaHumano);

        using var responder = await _profesional.PostAsJsonAsync($"/mensajeria/conversaciones/{id}/responder", new { texto = "hola" });
        using var cerrar = await _profesional.PutAsync($"/mensajeria/conversaciones/{id}/cerrar", null);
        using var tomar = await _profesional.PutAsync($"/mensajeria/conversaciones/{id}/tomar", null);

        Assert.Equal(HttpStatusCode.Forbidden, responder.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cerrar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, tomar.StatusCode);
        // Y no pasó nada: sigue escalada, sin dueño y sin ninguna respuesta del equipo.
        Assert.Equal("DerivadaHumano", DatosDePrueba.Escalar("SELECT Estado FROM Conversacion WHERE IdConversacion = @i", ("@i", id)));
        Assert.Null(DatosDePrueba.Escalar("SELECT IdProfesionalAsignado FROM Conversacion WHERE IdConversacion = @i", ("@i", id)));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @i AND Origen = 'Humano'", ("@i", id)));
    }

    [Fact]
    public async Task Los_avisos_de_una_conversacion_los_ve_el_CRM_y_un_Profesional_recibe_403()
    {
        var id = await NuevaConversacion(EstadoConversacion.DerivadaHumano);
        DatosDePrueba.Ejecutar(@"INSERT INTO NotificacionInterna (Destinatario, IdConversacion, Tipo, Titulo, Mensaje)
                                 VALUES ('Administrador', @i, 'DerivacionHumano', 'Aviso de prueba', 'Mensaje de prueba')", ("@i", id));

        var comoCrm = await _crm.GetFromJsonAsync<JsonElement[]>("/mensajeria/notificaciones-internas?destinatario=Administrador");
        Assert.Contains(comoCrm!, n => n.TryGetProperty("idConversacion", out var v) && v.ValueKind == JsonValueKind.Number && v.GetInt32() == id);

        using var comoProfesional = await _profesional.GetAsync("/mensajeria/notificaciones-internas?destinatario=Administrador");
        Assert.Equal(HttpStatusCode.Forbidden, comoProfesional.StatusCode);
    }

    [Fact]
    public async Task La_apertura_queda_auditada_solo_con_ids_sin_el_nombre_del_paciente()
    {
        var id = await NuevaConversacion(EstadoConversacion.Activa, _idPaciente);

        (await _crm.GetAsync($"/mensajeria/conversaciones/{id}/paciente")).EnsureSuccessStatusCode();

        var detalle = (string?)DatosDePrueba.Escalar(
            "SELECT TOP 1 Detalle FROM LogAuditoria WHERE Entidad = 'Conversacion' AND IdEntidad = @i AND Accion = N'Apertura de conversación de chat' ORDER BY IdLog DESC", ("@i", id));
        Assert.NotNull(detalle);
        Assert.Contains("#" + id, detalle);
        Assert.Contains($"paciente #{_idPaciente}", detalle);
        Assert.DoesNotContain("García", detalle);
        Assert.DoesNotContain(_nombreDelPaciente, detalle);
    }

    [Fact]
    public async Task Lo_que_no_es_de_los_chats_sigue_andando_para_el_Profesional_su_campanita_de_turnos()
    {
        // La campanita "Avisos" del profesional (turnos confirmados que esperan sala) NO es de los chats: no pasa por /mensajeria y D2 no la toca.
        using var r = await _profesional.GetAsync($"/profesionales/{_idProfesional}/turnos-confirmados");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
