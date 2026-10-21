using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// Chats v2, Fase 1: vincular y desvincular una conversación con un paciente, las sugerencias por teléfono, el buscador, el interruptor de IA (global y por conversación) y "tomar
// control", contra una base de pruebas, con tokens reales de la aplicación. Lo que tiene que pasar:
//  · CRM puro y Administración entran; Configurador solo, Profesional, Paciente y Facturación reciben 403; sin sesión, 401 (el global de IA es al revés: Configurador sí, CRM no);
//  · nunca se vincula solo: leer el panel con una coincidencia por teléfono no cambia nada;
//  · vincular a un paciente que no existe, que está inactivo o a una conversación que ya tiene otro paciente falla con su código;
//  · la auditoría es ADMINISTRATIVA (no cae en la clínica), con ids y palabras fijas: sin número, sin nombre, sin DNI;
//  · una copia vieja de la conversación (un mensaje que entra, el asistente) no pisa un vínculo hecho a mano ni deshace una desvinculación (concurrencia real).
// Las que necesitan la columna de la migración 25 llevan Trait("Migracion", "25"): con la base en la forma de producción (sin ella) se filtran con 'Migracion!=25'.
[Trait("Categoria", "Integracion")]
public class ChatsVinculoBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Jwt:Key", "clave-de-prueba-chats-vinculo-0123456789-abcdef"));

    public void Dispose() { _fabrica.Dispose(); _d.Dispose(); }

    // ── Armado ──────────────────────────────────────────────────────────────────────────────
    private string TelefonoNuevo() => "1155" + Random.Shared.Next(100000, 999999);

    private int Conversacion(string? telefono = null, int? idPaciente = null, string estado = "Activa", string canal = "WhatsApp", string paso = "Inicio", string? contexto = null)
    {
        var id = Convert.ToInt32(DatosDePrueba.Escalar(
            @"INSERT INTO Conversacion (Telefono, IdPaciente, Canal, Estado, PasoActual, DatosContexto) VALUES (@t, @p, @c, @e, @s, @x); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@t", telefono ?? TelefonoNuevo()), ("@p", idPaciente), ("@c", canal), ("@e", estado), ("@s", paso), ("@x", contexto)));
        _d.AlLimpiar("Conversacion", "IdConversacion", id);
        return id;
    }

    private int Paciente(string nombre, string? dni = null, DateTime? nacimiento = null, string? telefono = null, string estado = "Activo")
    {
        var id = Convert.ToInt32(DatosDePrueba.Escalar(
            @"INSERT INTO Paciente (Nombre, Dni, FechaNacimiento, Telefono, Estado) VALUES (@n, @d, @f, @t, @e); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@n", nombre), ("@d", dni ?? Dni()), ("@f", nacimiento), ("@t", telefono), ("@e", estado)));
        _d.AlLimpiar("Paciente", "IdPaciente", id);
        return id;
    }

    private static int _dni = Random.Shared.Next(10000000, 90000000);
    private static string Dni() => Interlocked.Increment(ref _dni).ToString();

    private HttpClient Cliente(string? token)
    {
        var c = _fabrica.CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private string Token(string modulo, params string[] roles) =>
        _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(_d.Usuario(), new SesionModulo(modulo, roles, Array.Empty<string>(), null));

    private string Administracion => Token("administracion", "configurador", "crm");
    private string CrmPuro => Token("crm", "crm");
    private string ConfiguradorPuro => Token("configurador", "configurador");
    private string Profesional => _fabrica.Services.GetRequiredService<JwtEmisorService>().Emitir(_d.Usuario(), Roles.Profesional, idProfesional: _d.Profesional());
    private string PacienteToken => _fabrica.Services.GetRequiredService<JwtEmisorService>().Emitir(_d.Usuario(), Roles.Paciente, idPaciente: _d.Paciente());
    private string Facturacion => _fabrica.Services.GetRequiredService<JwtEmisorService>().Emitir(_d.Usuario(), Roles.Facturacion);

    private static int? IdPacienteDe(int idConversacion) => DatosDePrueba.Escalar("SELECT IdPaciente FROM Conversacion WHERE IdConversacion = @c", ("@c", idConversacion)) as int?;
    private static string? PasoDe(int idConversacion) => DatosDePrueba.Escalar("SELECT PasoActual FROM Conversacion WHERE IdConversacion = @c", ("@c", idConversacion)) as string;
    private static bool HayColumnaIa() => DatosDePrueba.Contar("SELECT CASE WHEN COL_LENGTH('dbo.Conversacion', 'IaActiva') IS NULL THEN 0 ELSE 1 END") == 1;

    private static int FilasDeAuditoria(string entidad, int idEntidad) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = @e AND IdEntidad = @i", ("@e", entidad), ("@i", idEntidad));

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return texto.Length == 0 ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }

    private static HttpRequestMessage Pedido(string metodo, string url, object? cuerpo = null)
    {
        var p = new HttpRequestMessage(new HttpMethod(metodo), url);
        if (cuerpo is not null) p.Content = JsonContent.Create(cuerpo);
        return p;
    }

    // ── Autorización ────────────────────────────────────────────────────────────────────────
    private const string IdInexistente = "2147483647";

    [Fact]
    public async Task Autorizacion_de_las_rutas_de_Chats_v2_con_tokens_reales()
    {
        // Rutas de /mensajeria: CRM puro y Administración entran (la respuesta ya no es 401/403); el resto, 403; sin sesión, 401. Contra un id que no existe: no se toca ninguna fila.
        var deChats = new (string Metodo, string Url, object? Cuerpo)[]
        {
            ("GET", "/mensajeria/pacientes/buscar?q=abc", null),
            ("PUT", $"/mensajeria/conversaciones/{IdInexistente}/vinculo", new { idPaciente = 1 }),
            ("DELETE", $"/mensajeria/conversaciones/{IdInexistente}/vinculo", null),
            ("PUT", $"/mensajeria/conversaciones/{IdInexistente}/ia", new { activa = true }),
            ("POST", $"/mensajeria/conversaciones/{IdInexistente}/tomar-control", new { }),
        };
        var quienes = new (string Nombre, string? Token, bool Entra)[]
        {
            ("administración", Administracion, true), ("crm", CrmPuro, true),
            ("configurador solo", ConfiguradorPuro, false), ("profesional", Profesional, false), ("paciente", PacienteToken, false), ("facturación", Facturacion, false),
        };
        var mal = new List<string>();
        foreach (var (metodo, url, cuerpo) in deChats)
        {
            foreach (var (nombre, token, entra) in quienes)
            {
                using var r = await Cliente(token).SendAsync(Pedido(metodo, url, cuerpo));
                var prohibido = r.StatusCode == HttpStatusCode.Forbidden;
                if (entra && (prohibido || r.StatusCode == HttpStatusCode.Unauthorized)) mal.Add($"{nombre} {metodo} {url} -> {(int)r.StatusCode} (debía entrar)");
                if (!entra && !prohibido) mal.Add($"{nombre} {metodo} {url} -> {(int)r.StatusCode} (debía ser 403)");
            }
            using var sin = await Cliente(null).SendAsync(Pedido(metodo, url, cuerpo));
            if (sin.StatusCode != HttpStatusCode.Unauthorized) mal.Add($"sin sesión {metodo} {url} -> {(int)sin.StatusCode} (debía ser 401)");
        }
        Assert.True(mal.Count == 0, string.Join("\n", mal));
    }

    [Fact]
    public async Task El_interruptor_global_de_IA_es_del_Configurador_no_del_CRM()
    {
        var quienes = new (string Nombre, string? Token, HttpStatusCode Leer, HttpStatusCode Cambiar)[]
        {
            // Cambiar con un cuerpo vacío: el que entra recibe 400 (falta "activa") y no se modifica nada.
            ("administración", Administracion, HttpStatusCode.OK, HttpStatusCode.BadRequest),
            ("configurador solo", ConfiguradorPuro, HttpStatusCode.OK, HttpStatusCode.BadRequest),
            ("crm", CrmPuro, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("profesional", Profesional, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("paciente", PacienteToken, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("facturación", Facturacion, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("sin sesión", null, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
        };
        foreach (var (nombre, token, leer, cambiar) in quienes)
        {
            using var g = await Cliente(token).GetAsync("/configuracion/chats-ia");
            Assert.True(g.StatusCode == leer, $"{nombre}: GET esperaba {(int)leer} y dio {(int)g.StatusCode}");
            using var p = await Cliente(token).SendAsync(Pedido("PUT", "/configuracion/chats-ia", new { }));
            Assert.True(p.StatusCode == cambiar, $"{nombre}: PUT esperaba {(int)cambiar} y dio {(int)p.StatusCode}");
        }
    }

    // ── Buscador ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task El_buscador_devuelve_lo_justo_para_estar_seguro_y_nada_mas()
    {
        var marca = "Qwxz" + Guid.NewGuid().ToString("N")[..6];
        var dni = Dni();
        var p = Paciente($"María Ñandú {marca}", dni, new DateTime(1990, 5, 17), "351 206-6471");
        var cliente = Cliente(CrmPuro);

        var porNombre = await cliente.GetAsync($"/mensajeria/pacientes/buscar?q={Uri.EscapeDataString("maria nandu " + marca.ToLowerInvariant())}");   // sin tildes ni mayúsculas
        Assert.Equal(HttpStatusCode.OK, porNombre.StatusCode);
        var texto = await porNombre.Content.ReadAsStringAsync();
        var lista = JsonDocument.Parse(texto).RootElement;
        Assert.Equal(1, lista.GetArrayLength());
        var fila = lista[0];
        Assert.Equal(p, fila.GetProperty("idPaciente").GetInt32());
        Assert.Equal($"María Ñandú {marca}", fila.GetProperty("nombre").GetString());
        Assert.Equal("•••" + dni[^3..], fila.GetProperty("dniParcial").GetString());
        Assert.Equal(1990, fila.GetProperty("anioNacimiento").GetInt32());
        Assert.Equal(0, fila.GetProperty("otrasConversaciones").GetInt32());
        // Solo esas cinco propiedades: ni DNI completo, ni teléfono, ni mail, ni domicilio, ni fecha de nacimiento completa, ni estado clínico.
        Assert.Equal(new[] { "anioNacimiento", "dniParcial", "idPaciente", "nombre", "otrasConversaciones" }, fila.EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToArray());
        Assert.DoesNotContain(dni, texto);
        Assert.DoesNotContain("206", texto);
        Assert.DoesNotContain("1990-05", texto);

        var porDni = await Json(await cliente.GetAsync($"/mensajeria/pacientes/buscar?q={dni[..2]}.{dni[2..5]}.{dni[5..]}"));   // con puntos
        Assert.Equal(p, porDni[0].GetProperty("idPaciente").GetInt32());
    }

    [Fact]
    public async Task El_buscador_no_trae_inactivos_exige_un_minimo_y_trata_los_comodines_como_texto()
    {
        var marca = "Qbaja" + Guid.NewGuid().ToString("N")[..6];
        Paciente("Ana " + marca, estado: "Inactivo");
        var cliente = Cliente(Administracion);

        Assert.Equal(0, (await Json(await cliente.GetAsync($"/mensajeria/pacientes/buscar?q={marca}"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync("/mensajeria/pacientes/buscar?q=ab")).StatusCode);        // muy corto
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync("/mensajeria/pacientes/buscar")).StatusCode);              // vacío
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync("/mensajeria/pacientes/buscar?q=12345")).StatusCode);      // un DNI incompleto
        Assert.Equal(0, (await Json(await cliente.GetAsync("/mensajeria/pacientes/buscar?q=%25%25%25"))).GetArrayLength());        // "%%%" no es un comodín
        Assert.Equal(0, (await Json(await cliente.GetAsync("/mensajeria/pacientes/buscar?q=___"))).GetArrayLength());
    }

    [Fact]
    public async Task El_buscador_avisa_cuantas_otras_conversaciones_tiene_el_paciente()
    {
        var marca = "Qotra" + Guid.NewGuid().ToString("N")[..6];
        var p = Paciente("Beatriz " + marca);
        var otra = Conversacion(idPaciente: p);
        var estaConv = Conversacion();
        var cliente = Cliente(CrmPuro);

        var sinContar = await Json(await cliente.GetAsync($"/mensajeria/pacientes/buscar?q={marca}"));
        Assert.Equal(1, sinContar[0].GetProperty("otrasConversaciones").GetInt32());
        var excluyendo = await Json(await cliente.GetAsync($"/mensajeria/pacientes/buscar?q={marca}&idConversacion={otra}"));   // mirando justo esa: no cuenta
        Assert.Equal(0, excluyendo[0].GetProperty("otrasConversaciones").GetInt32());
        Assert.NotEqual(otra, estaConv);
    }

    // ── Vincular ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Vincular_deja_el_paciente_reinicia_el_asistente_y_audita_solo_ids_en_la_auditoria_administrativa()
    {
        var p = Paciente("Carlos Vincular", telefono: "351 206-6471");
        var telefono = TelefonoNuevo();
        var c = Conversacion(telefono, paso: "ElegirHorario", contexto: "{\"Opciones\":[1,2],\"IdTurno\":7}");

        var r = await Cliente(Administracion).SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = p }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Json(r);
        Assert.True(j.GetProperty("exito").GetBoolean());
        Assert.False(j.GetProperty("sinCambios").GetBoolean());
        Assert.Equal(p, IdPacienteDe(c));
        Assert.Equal("Menu", PasoDe(c));                                                                                                       // el flujo del asistente, reiniciado
        Assert.Null(DatosDePrueba.Escalar("SELECT DatosContexto FROM Conversacion WHERE IdConversacion = @c", ("@c", c)));

        Assert.Equal(1, FilasDeAuditoria("ConversacionVinculo", c));
        var fila = Auditoria("ConversacionVinculo", c);
        Assert.Equal("Administrativa", fila.Categoria);
        Assert.Equal("administracion", fila.Espacio);
        Assert.Equal($"Conversación #{c}, paciente #{p}, origen busqueda", fila.Detalle);
        Assert.DoesNotContain(telefono, fila.Detalle);
        Assert.DoesNotContain("Carlos", fila.Detalle);
        // Cae en la auditoría ADMINISTRATIVA (la del Configurador) y no en la clínica: el mismo criterio que usa LogAuditoriaDAO.ObtenerPaginado.
        Assert.Equal(1, DatosDePrueba.Contar(
            $"SELECT COUNT(*) FROM LogAuditoria l WHERE l.Entidad = 'ConversacionVinculo' AND l.IdEntidad = @c AND NOT (l.Categoria = 'Clinica' OR l.Entidad IN ({LogAuditoriaDAO.EntidadesClinicas}))", ("@c", c)));
    }

    private static (string Categoria, string? Espacio, string Detalle) Auditoria(string entidad, int idEntidad)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT TOP 1 Categoria, Espacio, Detalle FROM LogAuditoria WHERE Entidad = @e AND IdEntidad = @i ORDER BY IdLog DESC", con);
        cmd.Parameters.AddWithValue("@e", entidad);
        cmd.Parameters.AddWithValue("@i", idEntidad);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        return ((string)r["Categoria"], r["Espacio"] as string, (string)r["Detalle"]);
    }

    [Fact]
    public async Task Vincular_al_mismo_paciente_otra_vez_no_cambia_nada_ni_repite_el_registro_y_a_otro_da_409()
    {
        var p1 = Paciente("Primero Vinculo");
        var p2 = Paciente("Segundo Vinculo");
        var c = Conversacion();
        var cliente = Cliente(CrmPuro);
        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = p1 }))).StatusCode);

        var otraVez = await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = p1 }));
        Assert.Equal(HttpStatusCode.OK, otraVez.StatusCode);
        Assert.True((await Json(otraVez)).GetProperty("sinCambios").GetBoolean());
        Assert.Equal(1, FilasDeAuditoria("ConversacionVinculo", c));

        var aOtro = await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = p2 }));
        Assert.Equal(HttpStatusCode.Conflict, aOtro.StatusCode);
        Assert.Equal("YA_VINCULADA", (await Json(aOtro)).GetProperty("codigo").GetString());
        Assert.Equal(p1, IdPacienteDe(c));                                                                                                     // el vínculo no se pisó
        Assert.Equal(1, FilasDeAuditoria("ConversacionVinculo", c));
    }

    [Fact]
    public async Task Vincular_falla_con_su_codigo_si_el_paciente_no_existe_esta_inactivo_o_si_la_conversacion_no_existe()
    {
        var inactivo = Paciente("Baja Vinculo", estado: "Inactivo");
        var activo = Paciente("Alta Vinculo");
        var c = Conversacion();
        var cliente = Cliente(Administracion);

        var noExiste = await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = 2147483000 }));
        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
        var baja = await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = inactivo }));
        Assert.Equal(HttpStatusCode.Conflict, baja.StatusCode);
        Assert.Equal("PACIENTE_INACTIVO", (await Json(baja)).GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{IdInexistente}/vinculo", new { idPaciente = activo }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = 0 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = -5 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = activo, origen = "magia" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { }))).StatusCode);

        Assert.Null(IdPacienteDe(c));                                                                                                          // ninguno de los intentos dejó nada
        Assert.Equal(0, FilasDeAuditoria("ConversacionVinculo", c));
    }

    [Fact]
    public async Task Un_paciente_que_ya_tiene_otra_conversacion_se_puede_vincular()
    {
        var p = Paciente("Dos Conversaciones");
        var primera = Conversacion(idPaciente: p);
        var segunda = Conversacion();

        var r = await Cliente(CrmPuro).SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{segunda}/vinculo", new { idPaciente = p }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(p, IdPacienteDe(primera));
        Assert.Equal(p, IdPacienteDe(segunda));
    }

    // ── Sugerencias por teléfono: nunca vinculan ───────────────────────────────────────────
    [Fact]
    public async Task El_panel_sugiere_por_telefono_con_las_dos_lecturas_del_numero_y_no_vincula_nada()
    {
        var telefono = TelefonoNuevo();                                   // 1155xxxxxx: 10 dígitos
        var area = telefono[..2]; var abonado = telefono[2..];
        var conQuince = Paciente("Con Quince Sugerencia", telefono: $"0{area} 15 {abonado[..4]}-{abonado[4..]}");     // "011 15 5555-0001": solo el número nacional lo entiende
        var conPrefijo = Paciente("Con Prefijo Sugerencia", telefono: $"+54 9 {area} {abonado[..4]}-{abonado[4..]}");
        var inactivo = Paciente("Inactivo Sugerencia", telefono: telefono, estado: "Inactivo");
        var otroNumero = Paciente("Otro Numero Sugerencia", telefono: "1166660000");
        var c = Conversacion(telefono);

        var panel = await Json(await Cliente(CrmPuro).GetAsync($"/mensajeria/conversaciones/{c}/paciente"));

        var sugeridos = panel.GetProperty("chat").GetProperty("sugerencias").EnumerateArray().Select(s => s.GetProperty("idPaciente").GetInt32()).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { conQuince, conPrefijo }.OrderBy(x => x).ToArray(), sugeridos);
        Assert.DoesNotContain(inactivo, sugeridos);
        Assert.DoesNotContain(otroNumero, sugeridos);
        Assert.Null(IdPacienteDe(c));                                                                                                           // leer una sugerencia NO vincula
        Assert.Equal(0, FilasDeAuditoria("ConversacionVinculo", c));
        Assert.Equal(JsonValueKind.Null, panel.GetProperty("paciente").ValueKind);
        Assert.Equal(TelefonoWhatsApp.Formatear(telefono), panel.GetProperty("chat").GetProperty("telefonoMostrar").GetString());
    }

    [Fact]
    public async Task La_sugerencia_encuentra_al_paciente_con_los_formatos_reales_de_carga_del_telefono()
    {
        // Producción tiene los teléfonos de los pacientes con símbolos. Todos son el mismo número que la conversación (351 206-6471).
        var formatos = new[] { "351-206-6471", "(351) 15 206-6471", "+54 9 351 206 6471", "0351 15 2066471" };
        var canal = "T" + Guid.NewGuid().ToString("N")[..12];            // (Telefono, Canal) es único: un canal propio evita chocar con otra prueba
        var c = Conversacion("3512066471", canal: canal);
        var pacientes = formatos.Select((f, i) => Paciente($"Formato {i} " + _d.Marca, telefono: f)).ToArray();
        var distinto = Paciente("Otro numero " + _d.Marca, telefono: "351-206-6472");

        var panel = await Json(await Cliente(CrmPuro).GetAsync($"/mensajeria/conversaciones/{c}/paciente"));

        var sugeridos = panel.GetProperty("chat").GetProperty("sugerencias").EnumerateArray().Select(s => s.GetProperty("idPaciente").GetInt32()).ToHashSet();
        foreach (var (p, formato) in pacientes.Zip(formatos)) Assert.True(sugeridos.Contains(p), $"el teléfono cargado como '{formato}' tendría que sugerirse");
        Assert.DoesNotContain(distinto, sugeridos);
        Assert.Null(IdPacienteDe(c));                                      // y ninguno quedó vinculado
    }

    [Fact]
    public async Task Vincular_con_origen_sugerencia_solo_vale_si_el_paciente_esta_entre_las_coincidencias_y_el_registro_lo_dice()
    {
        var telefono = TelefonoNuevo();
        var coincide = Paciente("Coincide Sugerencia", telefono: telefono);
        var noCoincide = Paciente("No Coincide Sugerencia", telefono: "1177770000");
        var c = Conversacion(telefono);
        var cliente = Cliente(CrmPuro);

        var mal = await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = noCoincide, origen = "sugerencia" }));
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
        Assert.Null(IdPacienteDe(c));

        var bien = await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = coincide, origen = "sugerencia" }));
        Assert.Equal(HttpStatusCode.OK, bien.StatusCode);
        Assert.Equal($"Conversación #{c}, paciente #{coincide}, origen sugerencia", Auditoria("ConversacionVinculo", c).Detalle);
    }

    [Fact]
    public async Task Una_conversacion_ya_vinculada_no_trae_sugerencias()
    {
        var telefono = TelefonoNuevo();
        var p = Paciente("Ya Vinculado Sugerencia", telefono: telefono);
        var c = Conversacion(telefono, idPaciente: p);

        var panel = await Json(await Cliente(CrmPuro).GetAsync($"/mensajeria/conversaciones/{c}/paciente"));

        Assert.Equal(0, panel.GetProperty("chat").GetProperty("sugerencias").GetArrayLength());
        Assert.Equal(p, panel.GetProperty("paciente").GetProperty("idPaciente").GetInt32());
    }

    // ── Desvincular ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Desvincular_deja_la_conversacion_sin_identificar_reinicia_el_asistente_conserva_las_notas_y_audita()
    {
        var p = Paciente("Elena Desvincular");
        var c = Conversacion(idPaciente: p, paso: "ConfirmarTurno", contexto: "{\"IdTurno\":9}");
        var cliente = Cliente(Administracion);
        Assert.Equal(HttpStatusCode.Created, (await cliente.SendAsync(Pedido("POST", $"/mensajeria/conversaciones/{c}/notas", new { texto = "nota que se conserva" }))).StatusCode);

        var r = await cliente.SendAsync(Pedido("DELETE", $"/mensajeria/conversaciones/{c}/vinculo"));

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Null(IdPacienteDe(c));
        Assert.Equal("Inicio", PasoDe(c));
        Assert.Null(DatosDePrueba.Escalar("SELECT DatosContexto FROM Conversacion WHERE IdConversacion = @c", ("@c", c)));
        Assert.Equal(1, (await Json(await cliente.GetAsync($"/mensajeria/conversaciones/{c}/notas"))).GetArrayLength());                      // las notas se conservan
        var fila = Auditoria("ConversacionVinculo", c);
        Assert.Equal("Administrativa", fila.Categoria);
        Assert.Equal($"Conversación #{c}, paciente #{p}" + (HayColumnaIa() ? ", IA apagada" : ""), fila.Detalle);
        Assert.DoesNotContain("Elena", fila.Detalle);

        var otraVez = await cliente.SendAsync(Pedido("DELETE", $"/mensajeria/conversaciones/{c}/vinculo"));
        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        Assert.Equal("SIN_VINCULO", (await Json(otraVez)).GetProperty("codigo").GetString());
        Assert.Equal(1, FilasDeAuditoria("ConversacionVinculo", c));                                                                           // el segundo intento no deja registro
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.SendAsync(Pedido("DELETE", $"/mensajeria/conversaciones/{IdInexistente}/vinculo"))).StatusCode);
    }

    [Fact]
    [Trait("Migracion", "25")]
    public async Task Desvincular_apaga_la_IA_de_la_conversacion_porque_su_contexto_era_el_de_ese_paciente()
    {
        var p = Paciente("Ia Desvincular");
        var c = Conversacion(idPaciente: p);
        DatosDePrueba.Ejecutar("UPDATE Conversacion SET IaActiva = 1 WHERE IdConversacion = @c", ("@c", c));

        Assert.Equal(HttpStatusCode.NoContent, (await Cliente(CrmPuro).SendAsync(Pedido("DELETE", $"/mensajeria/conversaciones/{c}/vinculo"))).StatusCode);

        Assert.Equal(0, DatosDePrueba.Contar("SELECT CAST(IaActiva AS INT) FROM Conversacion WHERE IdConversacion = @c", ("@c", c)));
        Assert.EndsWith(", IA apagada", Auditoria("ConversacionVinculo", c).Detalle);
    }

    // ── Interruptor de IA y tomar control ───────────────────────────────────────────────────
    [Fact]
    [Trait("Migracion", "25")]
    public async Task La_IA_por_conversacion_arranca_apagada_se_guarda_y_solo_es_efectiva_con_el_global_prendido()
    {
        var c = Conversacion();
        var crm = Cliente(CrmPuro);
        var configurador = Cliente(Administracion);
        try
        {
            Assert.Equal(0, DatosDePrueba.Contar("SELECT CAST(IaActiva AS INT) FROM Conversacion WHERE IdConversacion = @c", ("@c", c)));       // nace apagada
            var panel0 = (await Json(await crm.GetAsync($"/mensajeria/conversaciones/{c}/paciente"))).GetProperty("chat");
            Assert.True(panel0.GetProperty("ia").GetProperty("disponible").GetBoolean());
            Assert.False(panel0.GetProperty("ia").GetProperty("activa").GetBoolean());
            Assert.False(panel0.GetProperty("ia").GetProperty("global").GetBoolean());

            // Prendida en la conversación con el global apagado: se guarda, pero NO es efectiva.
            var r = await crm.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/ia", new { activa = true }));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var j = await Json(r);
            Assert.True(j.GetProperty("activa").GetBoolean());
            Assert.False(j.GetProperty("global").GetBoolean());
            Assert.False(j.GetProperty("efectiva").GetBoolean());
            Assert.Equal(1, DatosDePrueba.Contar("SELECT CAST(IaActiva AS INT) FROM Conversacion WHERE IdConversacion = @c", ("@c", c)));
            Assert.Equal($"Conversación #{c}", Auditoria("ConversacionIa", c).Detalle);

            // Con el global prendido pasa a ser efectiva; "quién responde" sigue diciendo Bot porque la IA todavía no existe.
            Assert.Equal(HttpStatusCode.OK, (await configurador.SendAsync(Pedido("PUT", "/configuracion/chats-ia", new { activa = true }))).StatusCode);
            var panel = (await Json(await crm.GetAsync($"/mensajeria/conversaciones/{c}/paciente"))).GetProperty("chat");
            Assert.True(panel.GetProperty("ia").GetProperty("efectiva").GetBoolean());
            Assert.False(panel.GetProperty("ia").GetProperty("implementada").GetBoolean());
            Assert.Equal("Bot", panel.GetProperty("quienResponde").GetString());

            // Apagar el global apaga la efectividad en todas, sin tocar el interruptor de cada conversación.
            Assert.Equal(HttpStatusCode.OK, (await configurador.SendAsync(Pedido("PUT", "/configuracion/chats-ia", new { activa = false }))).StatusCode);
            var panel2 = (await Json(await crm.GetAsync($"/mensajeria/conversaciones/{c}/paciente"))).GetProperty("chat");
            Assert.False(panel2.GetProperty("ia").GetProperty("efectiva").GetBoolean());
            Assert.True(panel2.GetProperty("ia").GetProperty("activa").GetBoolean());

            Assert.Equal(HttpStatusCode.BadRequest, (await crm.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/ia", new { }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await crm.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{IdInexistente}/ia", new { activa = true }))).StatusCode);
        }
        finally
        {
            await configurador.SendAsync(Pedido("PUT", "/configuracion/chats-ia", new { activa = false }));
        }
    }

    [Fact]
    public async Task El_interruptor_global_arranca_apagado_se_guarda_y_deja_registro_administrativo()
    {
        var configurador = Cliente(ConfiguradorPuro);
        try
        {
            var antes = await Json(await configurador.GetAsync("/configuracion/chats-ia"));
            Assert.False(antes.GetProperty("implementada").GetBoolean());
            var cantidad = DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = 'ConfiguracionOrganizacion' AND Accion LIKE 'IA global de chats%'");

            Assert.Equal(HttpStatusCode.OK, (await configurador.SendAsync(Pedido("PUT", "/configuracion/chats-ia", new { activa = true }))).StatusCode);
            Assert.True((await Json(await configurador.GetAsync("/configuracion/chats-ia"))).GetProperty("activa").GetBoolean());
            Assert.Equal("1", DatosDePrueba.Escalar("SELECT Valor FROM ConfiguracionOrganizacion WHERE Clave = 'Chats.IaActiva'") as string);
            Assert.Equal(cantidad + 1, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = 'ConfiguracionOrganizacion' AND Accion LIKE 'IA global de chats%'"));
            Assert.True(DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = 'ConfiguracionOrganizacion' AND Categoria = 'Administrativa' AND Detalle = 'Chats.IaActiva: apagada a prendida' AND FechaHora > DATEADD(MINUTE, -5, GETDATE())") >= 1);
            Assert.NotNull(DatosDePrueba.Escalar("SELECT IdUsuarioModifico FROM ConfiguracionOrganizacion WHERE Clave = 'Chats.IaActiva'"));         // queda quién lo cambió
        }
        finally
        {
            await configurador.SendAsync(Pedido("PUT", "/configuracion/chats-ia", new { activa = false }));
        }
        Assert.False((await Json(await configurador.GetAsync("/configuracion/chats-ia"))).GetProperty("activa").GetBoolean());
    }

    [Fact]
    public async Task Tomar_control_en_modo_Asistente_apaga_la_IA_y_saca_al_asistente_si_estaba_activo()
    {
        var activa = Conversacion(estado: "Activa");
        var urgente = Conversacion(estado: "Urgente");
        var cliente = Cliente(CrmPuro);
        if (HayColumnaIa()) DatosDePrueba.Ejecutar("UPDATE Conversacion SET IaActiva = 1 WHERE IdConversacion IN (@a, @u)", ("@a", activa), ("@u", urgente));

        var r = await cliente.SendAsync(Pedido("POST", $"/mensajeria/conversaciones/{activa}/tomar-control", new { }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Json(r);
        Assert.Equal("DerivadaHumano", j.GetProperty("estado").GetString());                                                                   // el mecanismo que ya frena al asistente
        Assert.Equal("Persona", j.GetProperty("quienResponde").GetString());
        Assert.Equal("DerivadaHumano", DatosDePrueba.Escalar("SELECT Estado FROM Conversacion WHERE IdConversacion = @c", ("@c", activa)) as string);
        Assert.Equal(HayColumnaIa(), j.GetProperty("iaDisponible").GetBoolean());
        if (HayColumnaIa()) Assert.Equal(0, DatosDePrueba.Contar("SELECT CAST(IaActiva AS INT) FROM Conversacion WHERE IdConversacion = @c", ("@c", activa)));
        Assert.Equal(1, FilasDeAuditoria("ConversacionIa", activa));

        // Una conversación urgente sigue urgente: tomar control no la baja de estado.
        var u = await Json(await cliente.SendAsync(Pedido("POST", $"/mensajeria/conversaciones/{urgente}/tomar-control", new { })));
        Assert.Equal("Urgente", u.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.SendAsync(Pedido("POST", $"/mensajeria/conversaciones/{IdInexistente}/tomar-control", new { }))).StatusCode);
    }

    [Fact]
    public async Task Tomar_control_en_modo_Personal_no_cambia_el_estado_porque_alli_no_hay_asistente()
    {
        using var personal = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", "clave-de-prueba-chats-vinculo-0123456789-abcdef");
            b.UseSetting("Mensajeria:ModoAtencion", "Personal");
        });
        var token = personal.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(_d.Usuario(), new SesionModulo("crm", new[] { "crm" }, Array.Empty<string>(), null));
        var cliente = personal.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var c = Conversacion(estado: "Activa");

        var j = await Json(await cliente.SendAsync(Pedido("POST", $"/mensajeria/conversaciones/{c}/tomar-control", new { })));

        Assert.Equal("Activa", j.GetProperty("estado").GetString());
        Assert.Equal("Persona", j.GetProperty("quienResponde").GetString());
    }

    private sealed class IaSinColumna : IIaConversacionDAO
    {
        public bool ColumnaDisponible() => false;
        public bool? Obtener(int idConversacion) => null;
        public bool Establecer(int idConversacion, bool activa, QuienAudita quien) => false;
        public ResultadoTomarControl? TomarControl(int idConversacion, bool pausarAsistente, QuienAudita quien) =>
            new(EstadoConversacion.Activa, false, false);
    }

    [Fact]
    public async Task Sin_la_migracion_25_el_interruptor_por_conversacion_responde_409_y_el_panel_dice_que_no_esta_disponible()
    {
        using var sinColumna = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", "clave-de-prueba-chats-vinculo-0123456789-abcdef");
            b.ConfigureServices(s =>
            {
                foreach (var d in s.Where(x => x.ServiceType == typeof(IIaConversacionDAO)).ToList()) s.Remove(d);
                s.AddSingleton<IIaConversacionDAO>(new IaSinColumna());
            });
        });
        var token = sinColumna.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(_d.Usuario(), new SesionModulo("crm", new[] { "crm" }, Array.Empty<string>(), null));
        var cliente = sinColumna.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var c = Conversacion();

        var r = await cliente.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/ia", new { activa = true }));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("MIGRACION_25", (await Json(r)).GetProperty("codigo").GetString());

        var ia = (await Json(await cliente.GetAsync($"/mensajeria/conversaciones/{c}/paciente"))).GetProperty("chat").GetProperty("ia");
        Assert.False(ia.GetProperty("disponible").GetBoolean());
        Assert.False(ia.GetProperty("activa").GetBoolean());
        Assert.False(ia.GetProperty("efectiva").GetBoolean());
    }

    // ── Concurrencia real: una copia vieja de la conversación no pisa el vínculo ────────────────────────────────
    private static ConversacionDAO Dao() => new();

    [Fact]
    public void Una_copia_vieja_que_no_toco_el_paciente_no_deshace_un_vinculo_hecho_en_el_medio()
    {
        var p = Paciente("Carrera Determinista");
        var c = Conversacion();
        var vieja = Dao().ObtenerPorId(c)!;                                                  // la lee, por ejemplo, un mensaje que entra
        Assert.Equal(ResultadoVinculo.Vinculada, new VinculoConversacionDAO().Vincular(c, p, "busqueda", new QuienAudita(_d.Usuario(), "crm")));

        vieja.FechaUltimaActividad = DateTime.Now;
        Dao().Guardar(vieja);                                                                // la guarda con su copia vieja (sin paciente)

        Assert.Equal(p, IdPacienteDe(c));
    }

    [Fact]
    public void Una_copia_vieja_con_paciente_no_deshace_una_desvinculacion_hecha_en_el_medio()
    {
        var p = Paciente("Carrera Desvinculo");
        var c = Conversacion(idPaciente: p);
        var vieja = Dao().ObtenerPorId(c)!;
        Assert.Equal(p, new VinculoConversacionDAO().Desvincular(c, new QuienAudita(_d.Usuario(), "crm")));

        vieja.FechaUltimaActividad = DateTime.Now;
        Dao().Guardar(vieja);

        Assert.Null(IdPacienteDe(c));
    }

    [Fact]
    public void El_asistente_sigue_pudiendo_identificar_a_alguien_pero_nunca_pisa_el_vinculo_de_una_persona()
    {
        var delAsistente = Paciente("Lo Identifica El Asistente");
        var aMano = Paciente("Lo Vincula Una Persona");
        var c1 = Conversacion();
        var c2 = Conversacion();

        var copia1 = Dao().ObtenerPorId(c1)!;
        copia1.IdPaciente = delAsistente;                                                    // el asistente identifica por DNI: el cambio SÍ se guarda
        Dao().Guardar(copia1);
        Assert.Equal(delAsistente, IdPacienteDe(c1));

        var copia2 = Dao().ObtenerPorId(c2)!;
        new VinculoConversacionDAO().Vincular(c2, aMano, "busqueda", new QuienAudita(_d.Usuario(), "crm"));
        copia2.IdPaciente = delAsistente;                                                    // el asistente, con su copia vieja, cree que sigue sin paciente
        Dao().Guardar(copia2);
        Assert.Equal(aMano, IdPacienteDe(c2));                                               // la persona gana
    }

    [Fact]
    public void Dos_personas_vinculando_a_la_vez_a_pacientes_distintos_dejan_uno_solo_y_un_solo_registro()
    {
        var usuario = _d.Usuario();
        for (var i = 0; i < 30; i++)
        {
            var p1 = Paciente("Simultaneo A " + i);
            var p2 = Paciente("Simultaneo B " + i);
            var c = Conversacion();
            using var barrera = new Barrier(2);
            ResultadoVinculo? r1 = null, r2 = null;
            var t1 = Task.Run(() => { barrera.SignalAndWait(); r1 = new VinculoConversacionDAO().Vincular(c, p1, "busqueda", new QuienAudita(usuario, "crm")); });
            var t2 = Task.Run(() => { barrera.SignalAndWait(); r2 = new VinculoConversacionDAO().Vincular(c, p2, "busqueda", new QuienAudita(usuario, "crm")); });
            Task.WaitAll(t1, t2);

            Assert.Single(new[] { r1, r2 }, r => r == ResultadoVinculo.Vinculada);
            Assert.Single(new[] { r1, r2 }, r => r == ResultadoVinculo.YaVinculadaAOtro);
            Assert.Equal(r1 == ResultadoVinculo.Vinculada ? p1 : p2, IdPacienteDe(c));
            Assert.Equal(1, FilasDeAuditoria("ConversacionVinculo", c));
        }
    }

    [Fact]
    public void Con_dos_hilos_a_la_vez_y_muchas_vueltas_el_vinculo_hecho_a_mano_siempre_sobrevive_a_los_guardados_viejos()
    {
        // Un hilo vincula y desvincula a mano; el otro es el asistente / el modo Personal guardando con copias que leyó ANTES. Al terminar cada vuelta, el estado tiene que ser el
        // de la última operación a mano, sea cual sea el orden en que se intercalaron los guardados viejos.
        var usuario = _d.Usuario();
        var dao = new VinculoConversacionDAO();
        var p = Paciente("Estres Concurrencia");
        for (var i = 0; i < 60; i++)
        {
            var c = Conversacion();
            var vieja = Dao().ObtenerPorId(c)!;              // todos los guardados del otro hilo salen de esta copia vieja (sin paciente)
            using var barrera = new Barrier(2);
            var guardados = Task.Run(() =>
            {
                barrera.SignalAndWait();
                for (var k = 0; k < 15; k++) { vieja.FechaUltimaActividad = DateTime.Now; Dao().Guardar(vieja); }
            });
            var aMano = Task.Run(() =>
            {
                barrera.SignalAndWait();
                Assert.Equal(ResultadoVinculo.Vinculada, dao.Vincular(c, p, "busqueda", new QuienAudita(usuario, "crm")));
            });
            Task.WaitAll(guardados, aMano);
            Assert.Equal(p, IdPacienteDe(c));                // con el Guardar anterior (a ciegas), una de estas vueltas lo dejaba en NULL

            // Y al revés: una copia con paciente que se sigue guardando mientras se desvincula.
            var conPaciente = Dao().ObtenerPorId(c)!;
            using var barrera2 = new Barrier(2);
            var guardados2 = Task.Run(() => { barrera2.SignalAndWait(); for (var k = 0; k < 15; k++) { conPaciente.FechaUltimaActividad = DateTime.Now; Dao().Guardar(conPaciente); } });
            var desvincula = Task.Run(() => { barrera2.SignalAndWait(); Assert.Equal(p, dao.Desvincular(c, new QuienAudita(usuario, "crm"))); });
            Task.WaitAll(guardados2, desvincula);
            Assert.Null(IdPacienteDe(c));
        }
    }

    [Fact]
    public async Task De_punta_a_punta_un_mensaje_que_entra_por_el_webhook_mientras_se_vincula_no_deshace_el_vinculo()
    {
        const string secreto = "secreto-de-prueba-del-vinculo-0123456789";
        using var personal = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", "clave-de-prueba-chats-vinculo-0123456789-abcdef");
            b.UseSetting("Mensajeria:WhatsApp:Secreto", secreto);
            b.UseSetting("Mensajeria:ModoAtencion", "Personal");
            b.ConfigureServices(s =>
            {
                foreach (var d in s.Where(x => x.ServiceType == typeof(IMessageChannel)).ToList()) s.Remove(d);
                s.AddSingleton<IMessageChannel>(new CanalMudo());
            });
        });
        var token = personal.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(_d.Usuario(), new SesionModulo("crm", new[] { "crm" }, Array.Empty<string>(), null));
        var crm = personal.CreateClient();
        crm.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var webhook = personal.CreateClient();
        webhook.DefaultRequestHeaders.Add("X-Shared-Secret", secreto);
        var p = Paciente("Estres Webhook");

        for (var i = 0; i < 25; i++)
        {
            var telefono = TelefonoNuevo();
            var c = Conversacion(telefono, canal: "Simulado");
            var mensajes = Enumerable.Range(0, 6).Select(n => webhook.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono, texto = $"mensaje {n}" })).ToList();
            var vincula = crm.SendAsync(Pedido("PUT", $"/mensajeria/conversaciones/{c}/vinculo", new { idPaciente = p }));
            await Task.WhenAll(mensajes.Append(vincula));

            Assert.Equal(HttpStatusCode.OK, (await vincula).StatusCode);
            Assert.All(mensajes, m => Assert.Equal(HttpStatusCode.OK, m.Result.StatusCode));
            Assert.Equal(p, IdPacienteDe(c));
            Assert.Equal(6, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c AND Direccion = 'Entrante'", ("@c", c)));   // y ningún mensaje se perdió
        }
    }

    private sealed class CanalMudo : IMessageChannel
    {
        public string Nombre => "Simulado";
        public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto) => Task.FromResult(new ResultadoEnvio(true, "x"));
    }
}
