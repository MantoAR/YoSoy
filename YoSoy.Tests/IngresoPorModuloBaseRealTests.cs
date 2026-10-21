using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// El ingreso por módulos de la etapa 2 de roles y permisos, de punta a punta contra la base de pruebas: POST /login, el token de selección, POST /sesion/modulo
// y lo que cada token puede (o no) hacer. Incluye el caso del administrador de producción: un usuario con los DOS módulos, Configurador y CRM, que elige uno al
// ingresar y cambia al otro sin volver a ingresar.
[Trait("Categoria", "Integracion")]
public class IngresoPorModuloBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _datos = new();
    // whatsapp-service apunta a un puerto sin nada: lo que dependa de él (el estado, vincular) responde "no disponible" al instante y sin efectos.
    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Mensajeria:WhatsAppServiceUrl"] = "http://127.0.0.1:1/" })));

    public void Dispose()
    {
        _fabrica.Dispose();
        _datos.Dispose();   // los usuarios que ingresaron quedan en la auditoría (solo agregar) y se retienen con su marca
    }

    // Un usuario del equipo con esos permisos (claves de la tabla Permiso) y, si se piden, esos privilegios.
    private int UsuarioConPermisos(params string[] permisos) => UsuarioConPermisosYPrivilegios(permisos, Array.Empty<string>());

    private int UsuarioConPermisosYPrivilegios(string[] permisos, string[] privilegios)
    {
        var idUsuario = _datos.Usuario();
        foreach (var permiso in permisos)
        {
            DatosDePrueba.Ejecutar(@"
                IF NOT EXISTS (SELECT 1 FROM UsuarioRol ur JOIN Permiso p ON p.IdRol = ur.IdRol WHERE ur.IdUsuario = @u AND p.Clave = @p)
                    INSERT INTO UsuarioRol (IdUsuario, IdRol) SELECT @u, IdRol FROM Permiso WHERE Clave = @p;
                INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) SELECT @u, IdRol, IdPermiso FROM Permiso WHERE Clave = @p;",
                ("@u", idUsuario), ("@p", permiso));
        }
        foreach (var privilegio in privilegios)
            DatosDePrueba.Ejecutar(@"
                INSERT INTO UsuarioPrivilegio (IdUsuario, IdPermiso, IdPrivilegio)
                SELECT @u, pp.IdPermiso, pp.IdPrivilegio FROM PermisoPrivilegio pp JOIN Privilegio pv ON pv.IdPrivilegio = pp.IdPrivilegio
                JOIN UsuarioPermiso up ON up.IdPermiso = pp.IdPermiso AND up.IdUsuario = @u WHERE pv.Clave = @v",
                ("@u", idUsuario), ("@v", privilegio));
        return idUsuario;
    }

    private HttpClient Cliente(string? token = null)
    {
        var c = _fabrica.CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private async Task<(HttpResponseMessage R, JsonElement J)> Ingresar(int idUsuario, string? modulo = null, string? clave = null)
    {
        var r = await Cliente().PostAsJsonAsync("/login", new { username = _datos.Username(idUsuario), password = clave ?? DatosDePrueba.ClaveDePrueba, modulo });
        JsonElement j = default;
        var texto = await r.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(texto)) j = JsonDocument.Parse(texto).RootElement.Clone();
        return (r, j);
    }

    private async Task<(HttpResponseMessage R, JsonElement J)> ElegirModulo(string token, string modulo)
    {
        var r = await Cliente(token).PostAsJsonAsync("/sesion/modulo", new { modulo });
        JsonElement j = default;
        var texto = await r.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(texto)) j = JsonDocument.Parse(texto).RootElement.Clone();
        return (r, j);
    }

    private async Task<HttpStatusCode> Pedir(string token, string ruta) => (await Cliente(token).GetAsync(ruta)).StatusCode;

    // ═════════════ Vista única (PR 3): quien tiene Configurador Y CRM entra a Administración ═════════════

    private static string[] Claves(JsonElement j, string propiedad) =>
        j.GetProperty(propiedad).EnumerateArray().Select(m => m.ValueKind == JsonValueKind.Object ? m.GetProperty("clave").GetString()! : m.GetString()!).ToArray();

    private static string[] Roles_(string token) => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Claims
        .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role").Select(c => c.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static string[] ClaimsDe(string token, string tipo) => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Claims
        .Where(c => c.Type == tipo).Select(c => c.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private void DarPermisos(int idUsuario, params string[] permisos)
    {
        foreach (var permiso in permisos)
            DatosDePrueba.Ejecutar(@"
                IF NOT EXISTS (SELECT 1 FROM UsuarioRol ur JOIN Permiso p ON p.IdRol = ur.IdRol WHERE ur.IdUsuario = @u AND p.Clave = @p)
                    INSERT INTO UsuarioRol (IdUsuario, IdRol) SELECT @u, IdRol FROM Permiso WHERE Clave = @p;
                INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) SELECT @u, IdRol, IdPermiso FROM Permiso WHERE Clave = @p;",
                ("@u", idUsuario), ("@p", permiso));
    }

    private void DarPrivilegio(int idUsuario, string privilegio) => DatosDePrueba.Ejecutar(@"
        INSERT INTO UsuarioPrivilegio (IdUsuario, IdPermiso, IdPrivilegio)
        SELECT @u, pp.IdPermiso, pp.IdPrivilegio FROM PermisoPrivilegio pp JOIN Privilegio pv ON pv.IdPrivilegio = pp.IdPrivilegio
        JOIN UsuarioPermiso up ON up.IdPermiso = pp.IdPermiso AND up.IdUsuario = @u WHERE pv.Clave = @v", ("@u", idUsuario), ("@v", privilegio));

    // Un usuario del equipo con ficha de profesional (para darle también el módulo Profesional).
    private int UsuarioConFicha() => _datos.UsuarioDeProfesional(_datos.Profesional());

    private async Task<HttpStatusCode> Enviar(string? token, HttpMethod metodo, string ruta) =>
        (await Cliente(token).SendAsync(new HttpRequestMessage(metodo, ruta))).StatusCode;

    [Fact]
    public async Task El_administrador_con_Configurador_y_CRM_entra_directo_a_Administracion_sin_selector()
    {
        var admin = UsuarioConPermisosYPrivilegios(new[] { "configurador", "crm" }, new[] { "gestion_agendas" });

        var (r, j) = await Ingresar(admin);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(j.TryGetProperty("requiereModulo", out _));        // sin selector
        Assert.False(j.TryGetProperty("tokenSeleccion", out _));
        Assert.Equal("administracion", j.GetProperty("modulo").GetString());
        Assert.Equal("administracion", j.GetProperty("rol").GetString());
        Assert.Equal(new[] { "administracion" }, Claves(j, "modulos"));   // la unión es UNA sola opción
        Assert.Equal(new[] { "configurador", "crm" }, Claves(j, "permisos"));
        Assert.Equal(new[] { "gestion_agendas" }, Claves(j, "privilegios"));
        Assert.Equal("Administración", j.GetProperty("modulos")[0].GetProperty("nombre").GetString());

        // Los claims exactos del token: el módulo, los dos roles y el privilegio; ni ficha de profesional ni uso especial.
        var token = j.GetProperty("token").GetString()!;
        Assert.Equal(new[] { "administracion" }, ClaimsDe(token, "modulo"));
        Assert.Equal(new[] { "configurador", "crm" }, Roles_(token));
        Assert.Equal(new[] { "gestion_agendas" }, ClaimsDe(token, "privilegio"));
        Assert.Empty(ClaimsDe(token, "idProfesional"));
        Assert.Empty(ClaimsDe(token, "uso"));

        // Con ese único token hace todo lo de Configurador y todo lo de CRM (lo que antes pedía cambiar de token).
        Assert.Equal(HttpStatusCode.OK, await Pedir(token, "/mensajeria/conversaciones"));
        Assert.Equal(HttpStatusCode.OK, await Pedir(token, "/mensajeria/conexion-whatsapp"));
        Assert.Equal(HttpStatusCode.OK, await Pedir(token, "/usuarios/accesos"));
        Assert.Equal(HttpStatusCode.OK, await Pedir(token, "/usuarios/lista"));
        Assert.Equal(HttpStatusCode.OK, await Pedir(token, "/auditoria"));
        Assert.Equal(HttpStatusCode.OK, await Pedir(token, "/turnos"));    // Gestión de agendas (privilegio de CRM)
        Assert.NotEqual(HttpStatusCode.Forbidden, await Enviar(token, HttpMethod.Get, "/mensajeria/whatsapp/estado"));   // el QR sigue siendo del rol Configurador
        Assert.NotEqual(HttpStatusCode.Forbidden, await Enviar(token, HttpMethod.Post, "/mensajeria/whatsapp/vincular"));
        // ...y nada de Profesional ni de Admisión.
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(token, "/tutores"));
        Assert.Equal(HttpStatusCode.Forbidden, await Enviar(token, HttpMethod.Post, $"/pacientes/{_datos.Paciente()}/profesionales"));
    }

    [Fact]
    public async Task Pedir_crm_o_configurador_a_quien_tiene_la_union_da_la_sesion_de_Administracion()
    {
        var admin = UsuarioConPermisos("configurador", "crm");
        var (_, j) = await Ingresar(admin);
        var token = j.GetProperty("token").GetString()!;

        foreach (var pedido in new[] { "crm", "configurador", "administracion" })
        {
            var (r, jr) = await ElegirModulo(token, pedido);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("administracion", jr.GetProperty("modulo").GetString());
            Assert.Equal(new[] { "configurador", "crm" }, Roles_(jr.GetProperty("token").GetString()!));
            Assert.Equal(new[] { "administracion" }, Claves(jr, "modulos"));
        }
        var (rProf, jProf) = await ElegirModulo(token, "profesional");
        Assert.Equal(HttpStatusCode.Forbidden, rProf.StatusCode);
        Assert.Equal("MODULO_NO_HABILITADO", jProf.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await ElegirModulo(token, "facturacion")).R.StatusCode);
    }

    [Fact]
    public async Task Con_la_pestania_CRM_o_Configurador_el_administrador_entra_a_Administracion_y_la_que_no_tiene_se_rechaza()
    {
        var admin = UsuarioConPermisos("configurador", "crm");

        foreach (var pestania in new[] { "crm", "configurador", "administracion" })
        {
            var (r, j) = await Ingresar(admin, pestania);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("administracion", j.GetProperty("modulo").GetString());
            Assert.Equal(HttpStatusCode.OK, await Pedir(j.GetProperty("token").GetString()!, "/mensajeria/conversaciones"));
        }

        var (rProf, jProf) = await Ingresar(admin, "profesional");
        Assert.Equal(HttpStatusCode.Forbidden, rProf.StatusCode);
        Assert.Equal("MODULO_NO_HABILITADO", jProf.GetProperty("codigo").GetString());

        var (rPaciente, jPaciente) = await Ingresar(admin, "paciente");
        Assert.Equal(HttpStatusCode.Forbidden, rPaciente.StatusCode);
        Assert.Equal("ROL_INCORRECTO", jPaciente.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Pedir_Administracion_sin_tener_los_DOS_permisos_da_403_MODULO_NO_HABILITADO_con_la_pestania_y_con_sesion_modulo()
    {
        foreach (var permisos in new[] { new[] { "crm" }, new[] { "configurador" }, new[] { "facturacion" }, new[] { "crm", "facturacion" } })
        {
            var u = UsuarioConPermisos(permisos);
            var (r, j) = await Ingresar(u, "administracion");
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("MODULO_NO_HABILITADO", j.GetProperty("codigo").GetString());

            // Con una sesión que ya tiene (la suya, de un solo módulo): tampoco puede subir a Administración.
            var (_, jUno) = await Ingresar(u, permisos[0]);
            var (rSesion, jSesion) = await ElegirModulo(jUno.GetProperty("token").GetString()!, "administracion");
            Assert.Equal(HttpStatusCode.Forbidden, rSesion.StatusCode);
            Assert.Equal("MODULO_NO_HABILITADO", jSesion.GetProperty("codigo").GetString());
        }
    }

    [Fact]
    public async Task El_CRM_puro_ingresa_y_ve_solo_chats_exactamente_como_antes()
    {
        var recepcionista = UsuarioConPermisos("crm");

        foreach (var pestania in new string?[] { null, "crm" })
        {
            var (r, j) = await Ingresar(recepcionista, pestania);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.False(j.TryGetProperty("requiereModulo", out _));
            Assert.Equal("crm", j.GetProperty("modulo").GetString());
            Assert.Equal("crm", j.GetProperty("rol").GetString());
            Assert.Equal(new[] { "crm" }, Claves(j, "modulos"));
            Assert.Equal(new[] { "crm" }, Claves(j, "permisos"));
            Assert.Empty(j.GetProperty("privilegios").EnumerateArray());
            var token = j.GetProperty("token").GetString()!;
            Assert.Equal(new[] { "crm" }, ClaimsDe(token, "modulo"));
            Assert.Equal(new[] { "crm" }, Roles_(token));

            Assert.Equal(HttpStatusCode.OK, await Pedir(token, "/mensajeria/conversaciones"));
            Assert.Equal(HttpStatusCode.OK, await Pedir(token, "/mensajeria/conexion-whatsapp"));
            foreach (var ruta in new[] { "/usuarios/accesos", "/usuarios/lista", "/auditoria", "/mensajeria/whatsapp/estado", "/tutores" })
                Assert.Equal(HttpStatusCode.Forbidden, await Pedir(token, ruta));
            Assert.Equal(HttpStatusCode.Forbidden, await Enviar(token, HttpMethod.Post, "/mensajeria/whatsapp/vincular"));      // el QR no es del CRM
            Assert.Equal(HttpStatusCode.Forbidden, await Enviar(token, HttpMethod.Post, "/mensajeria/whatsapp/desvincular"));
        }
        // Las otras pestañas, rechazadas como siempre.
        foreach (var pestania in new[] { "configurador", "administracion", "profesional" })
        {
            var (r, j) = await Ingresar(recepcionista, pestania);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("MODULO_NO_HABILITADO", j.GetProperty("codigo").GetString());
        }
    }

    [Fact]
    public async Task El_Configurador_solo_sigue_igual_sin_chats()
    {
        var u = UsuarioConPermisos("configurador");
        var (r, j) = await Ingresar(u);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("configurador", j.GetProperty("modulo").GetString());
        Assert.Equal(new[] { "configurador" }, Claves(j, "modulos"));
        var token = j.GetProperty("token").GetString()!;
        Assert.Equal(new[] { "configurador" }, Roles_(token));
        Assert.Equal(HttpStatusCode.OK, await Pedir(token, "/usuarios/lista"));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(token, "/mensajeria/conversaciones"));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(token, "/mensajeria/conexion-whatsapp"));
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ((await Ingresar(u, "crm")).R.StatusCode, (await Ingresar(u, "administracion")).R.StatusCode));
    }

    [Fact]
    public async Task La_union_mas_Profesional_ofrece_dos_opciones_y_cada_token_lleva_solo_lo_suyo()
    {
        var u = UsuarioConFicha();
        DarPermisos(u, "configurador", "crm", "profesional");
        DarPrivilegio(u, "admision");   // un privilegio de Profesional: no puede viajar en el token de Administración

        var (r, j) = await Ingresar(u);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(j.GetProperty("requiereModulo").GetBoolean());
        Assert.Equal(new[] { "profesional", "administracion" }, Claves(j, "modulos"));
        var seleccion = j.GetProperty("tokenSeleccion").GetString()!;

        var (rAdmin, jAdmin) = await ElegirModulo(seleccion, "administracion");
        Assert.Equal(HttpStatusCode.OK, rAdmin.StatusCode);
        var tokenAdmin = jAdmin.GetProperty("token").GetString()!;
        Assert.Equal(new[] { "configurador", "crm" }, Roles_(tokenAdmin));       // ni profesional ni auditor
        Assert.Empty(ClaimsDe(tokenAdmin, "privilegio"));                        // la admisión no viaja en Administración
        Assert.Empty(ClaimsDe(tokenAdmin, "idProfesional"));
        Assert.Equal(new[] { "administracion", "profesional" }, Claves(jAdmin, "modulos").OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(tokenAdmin, "/tutores"));

        var (rProf, jProf) = await ElegirModulo(seleccion, "profesional");
        Assert.Equal(HttpStatusCode.OK, rProf.StatusCode);
        var tokenProf = jProf.GetProperty("token").GetString()!;
        Assert.Equal(new[] { "profesional" }, Roles_(tokenProf));
        Assert.Equal(new[] { "admision" }, ClaimsDe(tokenProf, "privilegio"));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(tokenProf, "/mensajeria/conversaciones"));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(tokenProf, "/usuarios/lista"));

        // Y desde la sesión de Administración se puede pasar a Profesional (y volver) sin volver a ingresar.
        Assert.Equal(HttpStatusCode.OK, (await ElegirModulo(tokenAdmin, "profesional")).R.StatusCode);
        Assert.Equal("administracion", (await ElegirModulo(tokenProf, "crm")).J.GetProperty("modulo").GetString());

        // Con la pestaña: Profesional entra a Profesional; CRM y Configurador, a Administración.
        Assert.Equal("profesional", (await Ingresar(u, "profesional")).J.GetProperty("modulo").GetString());
        Assert.Equal("administracion", (await Ingresar(u, "configurador")).J.GetProperty("modulo").GetString());
    }

    [Fact]
    public async Task La_union_mas_Facturacion_tambien_ofrece_dos_opciones()
    {
        var u = UsuarioConPermisos("configurador", "crm", "facturacion");
        var (_, j) = await Ingresar(u);
        Assert.True(j.GetProperty("requiereModulo").GetBoolean());
        Assert.Equal(new[] { "administracion", "facturacion" }, Claves(j, "modulos"));
        var (r, jAdmin) = await ElegirModulo(j.GetProperty("tokenSeleccion").GetString()!, "administracion");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(new[] { "configurador", "crm" }, Roles_(jAdmin.GetProperty("token").GetString()!));
    }

    [Fact]
    public async Task Un_permiso_que_el_Configurador_quito_deja_de_valer_para_la_vista_unica()
    {
        var admin = UsuarioConPermisos("configurador", "crm");
        var (_, j) = await Ingresar(admin);
        var tokenViejo = j.GetProperty("token").GetString()!;

        DatosDePrueba.Ejecutar("DELETE FROM UsuarioPermiso WHERE IdUsuario = @u AND IdPermiso = (SELECT IdPermiso FROM Permiso WHERE Clave = 'crm')", ("@u", admin));

        // Ya no hay unión: pedir Administración da 403 y pedir Configurador da la sesión de Configurador solo (sin chats).
        var (r, jr) = await ElegirModulo(tokenViejo, "administracion");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("MODULO_NO_HABILITADO", jr.GetProperty("codigo").GetString());
        var (rConf, jConf) = await ElegirModulo(tokenViejo, "configurador");
        Assert.Equal(HttpStatusCode.OK, rConf.StatusCode);
        Assert.Equal("configurador", jConf.GetProperty("modulo").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(jConf.GetProperty("token").GetString()!, "/mensajeria/conversaciones"));
        // El token que ya estaba emitido sigue valiendo hasta que venza (8 horas): no hay revocación de tokens (límite conocido, igual que antes).
        Assert.Equal(HttpStatusCode.OK, await Pedir(tokenViejo, "/mensajeria/conversaciones"));
    }

    [Fact]
    public async Task Los_tokens_viejos_de_un_solo_modulo_siguen_valiendo_con_lo_que_tenian_y_nada_mas()
    {
        var admin = UsuarioConPermisos("configurador", "crm");
        var emisor = _fabrica.Services.GetRequiredService<JwtEmisorService>();
        var viejoConfigurador = emisor.EmitirModulo(admin, new SesionModulo("configurador", new[] { "configurador" }, Array.Empty<string>(), null));
        var viejoCrm = emisor.EmitirModulo(admin, new SesionModulo("crm", new[] { "crm" }, Array.Empty<string>(), null));

        // Fallan de forma segura: son lo que eran. Quien tenía el token de Configurador no ve chats; el de CRM, no administra.
        Assert.Equal(HttpStatusCode.OK, await Pedir(viejoConfigurador, "/usuarios/lista"));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(viejoConfigurador, "/mensajeria/conversaciones"));
        Assert.Equal(HttpStatusCode.OK, await Pedir(viejoCrm, "/mensajeria/conversaciones"));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(viejoCrm, "/usuarios/lista"));
        Assert.Equal(HttpStatusCode.Forbidden, await Enviar(viejoCrm, HttpMethod.Post, "/mensajeria/whatsapp/vincular"));
        // Y con cualquiera de los dos se puede subir a Administración (el servidor vuelve a mirar la base).
        foreach (var viejo in new[] { viejoConfigurador, viejoCrm })
        {
            var (r, j) = await ElegirModulo(viejo, "administracion");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(HttpStatusCode.OK, await Pedir(j.GetProperty("token").GetString()!, "/usuarios/lista"));
        }
    }

    [Fact]
    public async Task Matriz_de_autorizacion_con_tokens_reales_la_sesion_unida_es_la_suma_y_nada_mas()
    {
        var unida = (await Ingresar(UsuarioConPermisosYPrivilegios(new[] { "configurador", "crm" }, new[] { "gestion_agendas" }))).J.GetProperty("token").GetString()!;
        var crm = (await Ingresar(UsuarioConPermisos("crm"))).J.GetProperty("token").GetString()!;
        var configurador = (await Ingresar(UsuarioConPermisos("configurador"))).J.GetProperty("token").GetString()!;
        var uProf = UsuarioConFicha();
        DarPermisos(uProf, "profesional");
        var profesional = (await Ingresar(uProf)).J.GetProperty("token").GetString()!;
        var emisor = _fabrica.Services.GetRequiredService<JwtEmisorService>();
        var paciente = emisor.Emitir(_datos.UsuarioDePaciente(_datos.Paciente()), Roles.Paciente, idPaciente: _datos.Paciente());
        var facturacion = emisor.Emitir(_datos.Usuario(), Roles.Facturacion);

        var quienes = new (string Nombre, string? Token)[]
        { ("unida", unida), ("crm", crm), ("configurador", configurador), ("profesional", profesional), ("paciente", paciente), ("facturacion", facturacion), ("sin sesión", null) };
        // "P" = pasa la autorización (cualquier respuesta que no sea 401 ni 403: ahí ya atiende el endpoint).
        // Orden: unida, crm, configurador, profesional, paciente, facturacion, sin sesión.
        var matriz = new (HttpMethod M, string Ruta, string[] Esperado)[]
        {
            (HttpMethod.Get,  "/mensajeria/conversaciones",        new[] { "200", "200", "403", "403", "403", "403", "401" }),
            (HttpMethod.Get,  "/mensajeria/conexion-whatsapp",     new[] { "200", "200", "403", "403", "403", "403", "401" }),
            (HttpMethod.Get,  "/mensajeria/etiquetas",             new[] { "200", "200", "403", "403", "403", "403", "401" }),
            (HttpMethod.Get,  "/usuarios/lista",                   new[] { "200", "403", "200", "403", "403", "403", "401" }),
            (HttpMethod.Get,  "/usuarios/accesos",                 new[] { "200", "403", "200", "403", "403", "403", "401" }),
            (HttpMethod.Get,  "/auditoria",                        new[] { "200", "403", "200", "403", "403", "403", "401" }),
            (HttpMethod.Get,  "/turnos",                           new[] { "200", "403", "403", "403", "403", "403", "401" }),   // solo CRM con Gestión de agendas
            (HttpMethod.Get,  "/mensajeria/whatsapp/estado",       new[] { "P",   "403", "P",   "403", "403", "403", "401" }),
            (HttpMethod.Post, "/mensajeria/whatsapp/vincular",     new[] { "P",   "403", "P",   "403", "403", "403", "401" }),
            (HttpMethod.Get,  "/tutores",                          new[] { "403", "403", "403", "P",   "403", "403", "401" }),
        };
        var fallas = new List<string>();
        foreach (var (metodo, ruta, esperado) in matriz)
            for (var i = 0; i < quienes.Length; i++)
            {
                var estado = await Enviar(quienes[i].Token, metodo, ruta);
                var real = ((int)estado).ToString();
                var ok = esperado[i] == "P" ? estado != HttpStatusCode.Unauthorized && estado != HttpStatusCode.Forbidden : esperado[i] == real;
                if (!ok) fallas.Add($"{metodo} {ruta} con {quienes[i].Nombre}: esperaba {esperado[i]} y dio {real}");
            }
        Assert.True(fallas.Count == 0, string.Join("\n", fallas));
    }

    [Fact]
    public async Task Un_usuario_con_un_solo_modulo_entra_directo_y_uno_sin_permisos_recibe_403_SIN_PERMISOS()
    {
        var soloConfigurador = UsuarioConPermisos("configurador");
        var (r, j) = await Ingresar(soloConfigurador);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(j.TryGetProperty("requiereModulo", out _));
        Assert.Equal("configurador", j.GetProperty("modulo").GetString());
        Assert.Equal("configurador", j.GetProperty("rol").GetString());

        var sinPermisos = _datos.Usuario();
        var (rSin, jSin) = await Ingresar(sinPermisos);
        Assert.Equal(HttpStatusCode.Forbidden, rSin.StatusCode);
        Assert.Equal("SIN_PERMISOS", jSin.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task El_privilegio_Gestion_de_agendas_viaja_en_el_token_de_CRM_y_sin_el_no_hay_turnos()
    {
        var conAgendas = UsuarioConPermisosYPrivilegios(new[] { "crm" }, new[] { "gestion_agendas" });
        var sinAgendas = UsuarioConPermisos("crm");

        var (_, jCon) = await Ingresar(conAgendas);
        var (_, jSin) = await Ingresar(sinAgendas);

        Assert.Contains(jCon.GetProperty("privilegios").EnumerateArray(), p => p.GetString() == "gestion_agendas");
        Assert.Empty(jSin.GetProperty("privilegios").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, await Pedir(jCon.GetProperty("token").GetString()!, "/turnos"));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(jSin.GetProperty("token").GetString()!, "/turnos"));
    }

    [Fact]
    public async Task El_bloqueo_por_intentos_sigue_siendo_el_de_PoliticaBloqueoLogin_con_el_ingreso_por_modulos()
    {
        var admin = UsuarioConPermisos("configurador", "crm");

        // Tres contraseñas incorrectas: las dos primeras 401, la tercera bloquea (423) con los minutos reales; después, ni la correcta entra.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Ingresar(admin, clave: "mal-1")).R.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Ingresar(admin, clave: "mal-2")).R.StatusCode);
        var (r3, j3) = await Ingresar(admin, clave: "mal-3");
        Assert.Equal((HttpStatusCode)423, r3.StatusCode);
        // 15 minutos (a veces 16: la fecha vuelve de la base redondeada a 3,33 ms y MinutosRestantes redondea hacia arriba; es de PoliticaBloqueoLogin, no de este ingreso).
        Assert.Matches(@"Intentá en 1[56] minutos", j3.GetProperty("mensaje").GetString());
        var (rBloqueado, _) = await Ingresar(admin);
        Assert.Equal((HttpStatusCode)423, rBloqueado.StatusCode);
        Assert.NotNull(DatosDePrueba.Escalar("SELECT BloqueadoHasta FROM Usuario WHERE IdUsuario = @u", ("@u", admin)));
    }

    [Fact]
    public async Task Cada_ingreso_y_cada_rechazo_quedan_en_la_auditoria_sin_la_contrasena()
    {
        var admin = UsuarioConPermisos("configurador");
        await Ingresar(admin, clave: "contrasena-equivocada-visible?");
        await Ingresar(admin);

        var filas = new List<string>();
        using (var con = Conexion.ObtenerConexion())
        {
            con.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT Accion + ' | ' + ISNULL(Detalle, '') FROM LogAuditoria WHERE IdUsuario = @u ORDER BY IdLog", con);
            cmd.Parameters.AddWithValue("@u", admin);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) filas.Add(rd.GetString(0));
        }
        Assert.Contains(filas, f => f.StartsWith("Ingreso fallido"));
        Assert.Contains(filas, f => f.StartsWith("Ingreso |") || f.StartsWith("Ingreso"));
        Assert.DoesNotContain(filas, f => f.Contains("contrasena-equivocada"));
    }
}
