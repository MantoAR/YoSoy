using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// Pantalla de Leads de la vista única: GET /mensajeria/leads. Un lead es cualquier conversación; es una vista de solo lectura sobre las mismas
// conversaciones que la bandeja, con respuesta reducida (sin texto de mensajes ni paso del bot), búsqueda, filtros y paginación en el servidor.
public class LeadsUnidadTests
{
    private static ConversacionResumen C(int id, string telefono, int? idPaciente = null, string? nombre = null, string? contacto = null, string estado = "Activa", params int[] etiquetas) =>
        new() { IdConversacion = id, Telefono = telefono, IdPaciente = idPaciente, NombrePaciente = nombre, NombreContacto = contacto, Estado = estado, Canal = "WhatsApp",
                IdsEtiquetas = etiquetas.ToList(), FechaUltimaActividad = new DateTime(2026, 10, 20), UltimoMensaje = "TEXTO SECRETO", PasoActual = "MenuPrincipal" };

    private static FiltroLeads F(string? q = null, string? tipo = null, string? estado = null, int? etiqueta = null, int? pagina = null, int? cantidad = null) =>
        Leads.Normalizar(q, tipo, estado, etiqueta, pagina, cantidad).Filtro!;

    [Fact]
    public void Sin_filtros_son_todas_las_conversaciones_de_a_25()
    {
        var (f, error) = Leads.Normalizar(null, null, null, null, null, null);
        Assert.Null(error);
        Assert.Equal(("todos", null, 1, 25), (f!.Tipo, f.Estado, f.Pagina, f.Cantidad));
    }

    [Theory]
    [InlineData("admin", null, null, null, null)]
    [InlineData(null, "Borrada", null, null, null)]
    [InlineData(null, null, 0, null, null)]
    [InlineData(null, null, null, 0, null)]
    [InlineData(null, null, null, null, 101)]
    public void Rechaza_lo_que_no_es_un_valor_conocido(string? tipo, string? estado, int? etiqueta, int? pagina, int? cantidad)
    {
        var (f, error) = Leads.Normalizar(null, tipo, estado, etiqueta, pagina, cantidad);
        Assert.Null(f);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("activa", "Activa"), InlineData("DERIVADAHUMANO", "DerivadaHumano"), InlineData("urgente", "Urgente"), InlineData("Cerrada", "Cerrada")]
    public void El_estado_se_reconoce_sin_mirar_mayusculas(string pedido, string esperado) => Assert.Equal(esperado, F(estado: pedido).Estado);

    [Fact]
    public void Todos_los_estados_o_ninguno_no_filtran()
    {
        Assert.Null(F(estado: "todos").Estado);
        Assert.Null(F(estado: "  ").Estado);
    }

    [Fact]
    public void El_tipo_separa_pacientes_de_personas_sin_identificar()
    {
        var lista = new[] { C(1, "1111", 5, "Ana Paz"), C(2, "2222"), C(3, "3333", 6, "Luis Gil") };
        Assert.Equal(new[] { 1, 3 }, Leads.Filtrar(lista, F(tipo: "paciente")).Filas.Select(x => x.IdConversacion));
        Assert.Equal(new[] { 2 }, Leads.Filtrar(lista, F(tipo: "sin-identificar")).Filas.Select(x => x.IdConversacion));
        Assert.All(Leads.Filtrar(lista, F()).Filas, x => Assert.Equal(x.IdConversacion == 2 ? "sin-identificar" : "paciente", x.Tipo));
    }

    [Fact]
    public void Busca_por_nombre_contacto_o_telefono_sin_mayusculas_ni_tildes_y_cada_palabra_tiene_que_estar()
    {
        var lista = new[] { C(1, "5491155556666", 5, "María Pérez"), C(2, "5491100001111", null, null, "juan wa"), C(3, "5491122223333", 6, "Luis Gil") };
        int[] Ids(string q) => Leads.Filtrar(lista, F(q)).Filas.Select(x => x.IdConversacion).ToArray();
        Assert.Equal(new[] { 1 }, Ids("maria"));
        Assert.Equal(new[] { 1 }, Ids("PEREZ maría"));
        Assert.Equal(new[] { 2 }, Ids("JUAN"));                    // el nombre del perfil de WhatsApp
        Assert.Equal(new[] { 3 }, Ids("+54 9 11 2222"));           // el teléfono, con signos y espacios
        Assert.Equal(new[] { 1 }, Ids("5556666"));
        Assert.Empty(Ids("maria gil"));                            // las dos palabras tienen que coincidir
        Assert.Empty(Ids("zzz"));
        Assert.Equal(3, Ids("").Length);
    }

    [Fact]
    public void Filtra_por_etiqueta_y_pagina_en_orden()
    {
        var lista = Enumerable.Range(1, 7).Select(i => C(i, "1000" + i, null, null, null, "Activa", i % 2 == 0 ? new[] { 9 } : Array.Empty<int>())).ToList();
        Assert.Equal(new[] { 2, 4, 6 }, Leads.Filtrar(lista, F(etiqueta: 9)).Filas.Select(x => x.IdConversacion));
        var (total, pagina2) = Leads.Filtrar(lista, F(pagina: 2, cantidad: 3));
        Assert.Equal(7, total);
        Assert.Equal(new[] { 4, 5, 6 }, pagina2.Select(x => x.IdConversacion));
    }

    [Fact]
    public void La_fila_no_trae_el_texto_de_los_mensajes_ni_el_paso_del_bot()
    {
        var fila = Leads.Filtrar(new[] { C(1, "1111", 5, "Ana Paz") }, F()).Filas.Single();
        var json = JsonSerializer.Serialize(fila);
        Assert.DoesNotContain("SECRETO", json);
        Assert.DoesNotContain("MenuPrincipal", json);
        Assert.Equal(new[] { "canal", "estado", "fechaUltimaActividad", "idConversacion", "idsEtiquetas", "noLeidos", "nombre", "nombreContacto", "telefono", "tipo" },
            JsonDocument.Parse(JsonSerializer.Serialize(fila, new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal));
    }
}

[Trait("Categoria", "Integracion")]
public class LeadsBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly WebApplicationFactory<Program> _fabrica = new();

    public void Dispose() { _fabrica.Dispose(); _d.Dispose(); }

    private int Conversacion(string telefono, int? idPaciente = null, string estado = "Activa", string canal = "WhatsApp")
    {
        var id = Convert.ToInt32(DatosDePrueba.Escalar(@"INSERT INTO Conversacion (Telefono, IdPaciente, Canal, Estado) VALUES (@t, @p, @c, @e); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@t", telefono), ("@p", idPaciente), ("@c", canal), ("@e", estado)));
        _d.AlLimpiar("Conversacion", "IdConversacion", id);
        return id;
    }

    private string Telefono() => "1155" + Random.Shared.Next(100000, 999999) + Random.Shared.Next(10, 99);

    private HttpClient Cliente(string? token)
    {
        var c = _fabrica.CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private string Token(string modulo, params string[] roles)
    {
        var emisor = _fabrica.Services.GetRequiredService<JwtEmisorService>();
        return emisor.EmitirModulo(_d.Usuario(), new SesionModulo(modulo, roles, Array.Empty<string>(), null));
    }

    private string Administracion => Token("administracion", "configurador", "crm");

    private async Task<(HttpStatusCode Estado, JsonElement J, string Texto)> Leads_(string consulta, string? token = null)
    {
        var r = await Cliente(token ?? Administracion).GetAsync("/mensajeria/leads" + consulta);
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, texto.Length == 0 ? default : JsonDocument.Parse(texto).RootElement.Clone(), texto);
    }

    private static int[] Ids(JsonElement j) => j.GetProperty("filas").EnumerateArray().Select(f => f.GetProperty("idConversacion").GetInt32()).ToArray();

    [Fact]
    public async Task Un_lead_es_cualquier_conversacion_con_su_tipo_y_solo_lo_necesario_para_listar()
    {
        var idPac = _d.Paciente("Paciente " + _d.Marca);
        var tel = Telefono();
        var telSin = Telefono();
        var cPac = Conversacion(tel, idPac);
        var cSin = Conversacion(telSin);
        DatosDePrueba.Ejecutar("INSERT INTO MensajeChat (IdConversacion, Direccion, Origen, Texto) VALUES (@c, 'Entrante', 'Paciente', N'TEXTO-QUE-NO-DEBE-SALIR-" + _d.Marca + "')", ("@c", cPac));

        var (estado, j, texto) = await Leads_("?q=" + _d.Marca);
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(new[] { cPac }, Ids(j));
        var fila = j.GetProperty("filas")[0];
        Assert.Equal("paciente", fila.GetProperty("tipo").GetString());
        Assert.Equal("Paciente " + _d.Marca, fila.GetProperty("nombre").GetString());
        Assert.Equal(tel, fila.GetProperty("telefono").GetString());                                   // el teléfono completo
        Assert.Equal(1, fila.GetProperty("noLeidos").GetInt32());
        Assert.DoesNotContain("TEXTO-QUE-NO-DEBE-SALIR", texto);                                       // nunca el texto de los mensajes
        Assert.Equal(new[] { "canal", "estado", "fechaUltimaActividad", "idConversacion", "idsEtiquetas", "noLeidos", "nombre", "nombreContacto", "telefono", "tipo" },
            fila.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal));      // ni paso del bot, ni ids de paciente ni de profesional

        var (_, jSin, _) = await Leads_("?q=" + telSin);
        Assert.Equal(new[] { cSin }, Ids(jSin));
        Assert.Equal("sin-identificar", jSin.GetProperty("filas")[0].GetProperty("tipo").GetString());
        Assert.Equal(JsonValueKind.Null, jSin.GetProperty("filas")[0].GetProperty("nombre").ValueKind);
    }

    [Fact]
    public async Task Filtra_por_tipo_estado_y_etiqueta()
    {
        var marca = _d.Marca;
        var idPac = _d.Paciente("Paciente " + marca);
        var tel = Telefono();
        var cActiva = Conversacion(tel, idPac);
        var cUrgente = Conversacion(Telefono(), idPac, "Urgente", "Simulado");
        var cCerrada = Conversacion(Telefono(), idPac, "Cerrada", "Telegram");
        var idEtiqueta = Convert.ToInt32(DatosDePrueba.Escalar("INSERT INTO EtiquetaConversacion (Nombre, Tono) VALUES (@n, 'ok'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@n", marca)));
        _d.AlLimpiar("ConversacionEtiqueta", "IdEtiqueta", idEtiqueta);
        _d.AlLimpiar("EtiquetaConversacion", "IdEtiqueta", idEtiqueta);
        DatosDePrueba.Ejecutar("INSERT INTO ConversacionEtiqueta (IdConversacion, IdEtiqueta) VALUES (@c, @e)", ("@c", cUrgente), ("@e", idEtiqueta));

        Assert.Equal(new[] { cActiva, cCerrada, cUrgente }.OrderBy(x => x), Ids((await Leads_($"?q={marca}&tipo=paciente")).J).OrderBy(x => x));
        Assert.Empty(Ids((await Leads_($"?q={marca}&tipo=sin-identificar")).J));
        Assert.Equal(new[] { cUrgente }, Ids((await Leads_($"?q={marca}&estado=Urgente")).J));
        Assert.Equal(new[] { cCerrada }, Ids((await Leads_($"?q={marca}&estado=cerrada")).J));
        Assert.Equal(new[] { cUrgente }, Ids((await Leads_($"?q={marca}&idEtiqueta={idEtiqueta}")).J));
        var conEtiqueta = (await Leads_($"?q={marca}&idEtiqueta={idEtiqueta}")).J.GetProperty("filas")[0];
        Assert.Equal(new[] { idEtiqueta }, conEtiqueta.GetProperty("idsEtiquetas").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(3, (await Leads_($"?q={marca}&estado=todos")).J.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Busca_por_nombre_con_tildes_y_por_telefono()
    {
        var idPac = _d.Paciente("Zoé Ñandú " + _d.Marca);
        var tel = Telefono();
        var c = Conversacion(tel, idPac);
        Assert.Equal(new[] { c }, Ids((await Leads_($"?q=zoe+nandu+{_d.Marca}")).J));
        Assert.Contains(c, Ids((await Leads_("?q=" + tel[2..9])).J));
        Assert.Empty(Ids((await Leads_("?q=zzzNadie" + _d.Marca)).J));
    }

    [Fact]
    public async Task Pagina_en_el_servidor_y_una_pagina_que_no_existe_vuelve_a_la_primera()
    {
        var idPac = _d.Paciente("Paciente " + _d.Marca);
        var creadas = Enumerable.Range(0, 5).Select(_ => Conversacion(Telefono(), idPac, "Activa", "Canal" + Guid.NewGuid().ToString("N")[..8])).ToList();
        var vistos = new List<int>();
        for (var pagina = 1; pagina <= 3; pagina++)
        {
            var (estado, j, _) = await Leads_($"?q={_d.Marca}&cantidad=2&pagina={pagina}");
            Assert.Equal(HttpStatusCode.OK, estado);
            Assert.Equal((5, 3, pagina, 2), (j.GetProperty("total").GetInt32(), j.GetProperty("paginas").GetInt32(), j.GetProperty("pagina").GetInt32(), j.GetProperty("cantidad").GetInt32()));
            vistos.AddRange(Ids(j));
        }
        Assert.Equal(creadas.OrderBy(x => x), vistos.OrderBy(x => x));    // sin repetir ni saltear
        var (_, afuera, _) = await Leads_($"?q={_d.Marca}&cantidad=2&pagina=99");
        Assert.Equal(1, afuera.GetProperty("pagina").GetInt32());
        Assert.Equal(2, afuera.GetProperty("filas").GetArrayLength());
    }

    [Theory]
    [InlineData("?tipo=admin"), InlineData("?estado=Borrada"), InlineData("?pagina=0"), InlineData("?cantidad=0"), InlineData("?cantidad=101"), InlineData("?idEtiqueta=0")]
    public async Task Los_filtros_invalidos_dan_400_con_un_mensaje(string consulta)
    {
        var (estado, j, _) = await Leads_(consulta);
        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.False(string.IsNullOrWhiteSpace(j.GetProperty("mensaje").GetString()));
    }

    [Fact]
    public async Task Leer_la_lista_de_leads_no_deja_registro_de_auditoria_igual_que_la_de_conversaciones()
    {
        var idAdmin = _d.Usuario();
        var token = _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(idAdmin, new SesionModulo("administracion", new[] { "configurador", "crm" }, Array.Empty<string>(), null));
        int Filas() => DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE IdUsuario = @u", ("@u", idAdmin));
        var antes = Filas();

        Assert.Equal(HttpStatusCode.OK, (await Cliente(token).GetAsync("/mensajeria/conversaciones")).StatusCode);
        Assert.Equal(antes, Filas());                                        // /conversaciones no audita la lectura de la lista
        Assert.Equal(HttpStatusCode.OK, (await Cliente(token).GetAsync("/mensajeria/leads?cantidad=5")).StatusCode);
        Assert.Equal(antes, Filas());                                        // Leads tampoco: queda alineado
    }

    [Fact]
    public async Task Autorizacion_Administracion_y_CRM_leen_los_demas_no()
    {
        var emisor = _fabrica.Services.GetRequiredService<JwtEmisorService>();
        var casos = new (string Nombre, string? Token, HttpStatusCode Esperado)[]
        {
            ("administración", Administracion, HttpStatusCode.OK),
            ("crm", Token("crm", "crm"), HttpStatusCode.OK),       // la API lo admite (es el mismo rol que ya lee /conversaciones); el menú de Leads existe solo en Administración
            ("configurador", Token("configurador", "configurador"), HttpStatusCode.Forbidden),
            ("profesional", emisor.Emitir(_d.Usuario(), Roles.Profesional, idProfesional: _d.Profesional()), HttpStatusCode.Forbidden),
            ("paciente", emisor.Emitir(_d.Usuario(), Roles.Paciente, idPaciente: _d.Paciente()), HttpStatusCode.Forbidden),
            ("facturación", emisor.Emitir(_d.Usuario(), Roles.Facturacion), HttpStatusCode.Forbidden),
            ("sin sesión", null, HttpStatusCode.Unauthorized),
        };
        foreach (var (nombre, token, esperado) in casos)
        {
            var r = await Cliente(token).GetAsync("/mensajeria/leads?cantidad=1");
            Assert.True(r.StatusCode == esperado, $"{nombre}: esperaba {(int)esperado} y dio {(int)r.StatusCode}");
        }
    }
}

// La pantalla de Leads (PANTALLAS/Pantalla_Configurador/js/leads.js): solo lectura y cableada solo en Administración.
public class LeadsPantallaTests
{
    private static readonly string Raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz, "PANTALLAS" }.Concat(partes).ToArray()));
    private static string Leads => Leer("Pantalla_Configurador", "js", "leads.js");

    [Fact]
    public void La_pantalla_es_de_solo_lectura_y_pide_todo_al_servidor()
    {
        var js = Leads;
        Assert.Contains("/mensajeria/leads?", js);
        foreach (var parametro in new[] { "tipo", "estado", "idEtiqueta", "pagina", "cantidad" }) Assert.Contains(parametro, js);
        // No se crean ni se borran leads: ningún pedido que no sea de lectura.
        Assert.DoesNotMatch(@"method:\s*""(POST|PUT|DELETE|PATCH)""", js);
        Assert.DoesNotContain("localStorage", js);
        Assert.DoesNotContain("console.", js);
        Assert.DoesNotContain("ultimoMensaje", js);                 // nunca el texto de los mensajes
        Assert.Contains("YS.bandeja.abrir(idConversacion)", js);    // al hacer clic abre la conversación en Chats
    }

    [Fact]
    public void Esta_cableada_solo_en_el_panel_de_Administracion()
    {
        var html = Leer("Pantalla_Configurador", "dashboard.html");
        Assert.True(html.IndexOf("src=\"js/leads.js\"", StringComparison.Ordinal) is var iLeads && iLeads > 0
            && iLeads < html.IndexOf("src=\"js/dashboard.js\"", StringComparison.Ordinal), "leads.js tiene que cargarse antes que dashboard.js");
        var dashboard = Leer("Pantalla_Configurador", "js", "dashboard.js");
        Assert.Contains("if (tab === \"leads\")        return await renderLeads(contenido);", dashboard);
        var secciones = System.Text.RegularExpressions.Regex.Match(dashboard, @"const SECCIONES_POR_MODULO = \{.*?\n\};", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(secciones, "\"leads\"").Count);   // una sola vez: la lista de administracion
        Assert.Matches(@"administracion: \[""chats"", ""leads""", secciones);   // y está en la de Administración, en segundo lugar, después de Chats
        // Y el menú: el ítem existe una sola vez, en el grupo de chats de Administración.
        var shell = Leer("shared", "shell.js");
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(shell, @"id: ""leads""").Count);
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void leads_js_compila()
    {
        var (codigo, _, error) = NodeDelServicioDeWhatsApp.Ejecutar(new[] { "-e", "const vm = require('vm'); try { new vm.Script(process.env.CODIGO); } catch (e) { process.stderr.write(String(e)); process.exit(1); }" },
            new Dictionary<string, string> { ["CODIGO"] = Leads });
        Assert.True(codigo == 0, "leads.js no compila: " + error);
    }
}
