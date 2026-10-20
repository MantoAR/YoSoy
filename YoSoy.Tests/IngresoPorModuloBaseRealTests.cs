using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

// El ingreso por módulos de la etapa 2 de roles y permisos, de punta a punta contra la base de pruebas: POST /login, el token de selección, POST /sesion/modulo
// y lo que cada token puede (o no) hacer. Incluye el caso del administrador de producción: un usuario con los DOS módulos, Configurador y CRM, que elige uno al
// ingresar y cambia al otro sin volver a ingresar.
[Trait("Categoria", "Integracion")]
public class IngresoPorModuloBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _datos = new();
    private readonly WebApplicationFactory<Program> _fabrica = new();

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

    [Fact]
    public async Task El_administrador_con_Configurador_y_CRM_elige_modulo_al_ingresar_y_cambia_sin_volver_a_ingresar()
    {
        var admin = UsuarioConPermisos("configurador", "crm");

        // 1) Ingresa: con dos módulos el servidor no decide, pide elegir y da un token que solo sirve para eso.
        var (r, j) = await Ingresar(admin);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(j.GetProperty("requiereModulo").GetBoolean());
        Assert.Equal(new[] { "configurador", "crm" },
            j.GetProperty("modulos").EnumerateArray().Select(m => m.GetProperty("clave").GetString()!).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        var seleccion = j.GetProperty("tokenSeleccion").GetString()!;
        Assert.False(j.TryGetProperty("token", out _));   // todavía no hay token de trabajo

        // El token de selección no abre nada más que la elección del módulo.
        Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(seleccion, "/mensajeria/conversaciones"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Pedir(seleccion, "/usuarios/accesos"));

        // 2) Elige CRM: atiende los chats y no administra usuarios.
        var (rCrm, jCrm) = await ElegirModulo(seleccion, "crm");
        Assert.Equal(HttpStatusCode.OK, rCrm.StatusCode);
        Assert.Equal("crm", jCrm.GetProperty("modulo").GetString());
        var tokenCrm = jCrm.GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, await Pedir(tokenCrm, "/mensajeria/conversaciones"));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(tokenCrm, "/usuarios/accesos"));

        // 3) Cambia a Configurador con el token de CRM (sin volver a ingresar): administra usuarios y ya no atiende los chats.
        var (rConf, jConf) = await ElegirModulo(tokenCrm, "configurador");
        Assert.Equal(HttpStatusCode.OK, rConf.StatusCode);
        Assert.Equal("configurador", jConf.GetProperty("modulo").GetString());
        var tokenConfigurador = jConf.GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, await Pedir(tokenConfigurador, "/usuarios/accesos"));
        Assert.Equal(HttpStatusCode.Forbidden, await Pedir(tokenConfigurador, "/mensajeria/conversaciones"));

        // 4) Y vuelve a CRM.
        var (rVuelta, _) = await ElegirModulo(tokenConfigurador, "crm");
        Assert.Equal(HttpStatusCode.OK, rVuelta.StatusCode);
    }

    [Fact]
    public async Task Con_la_pestania_elegida_en_el_ingreso_entra_directo_a_ese_modulo_y_se_rechaza_la_que_no_tiene()
    {
        var admin = UsuarioConPermisos("configurador", "crm");

        var (rCrm, jCrm) = await Ingresar(admin, "crm");
        Assert.Equal(HttpStatusCode.OK, rCrm.StatusCode);
        Assert.Equal("crm", jCrm.GetProperty("modulo").GetString());
        Assert.Equal(HttpStatusCode.OK, await Pedir(jCrm.GetProperty("token").GetString()!, "/mensajeria/conversaciones"));

        var (rProf, jProf) = await Ingresar(admin, "profesional");
        Assert.Equal(HttpStatusCode.Forbidden, rProf.StatusCode);
        Assert.Equal("MODULO_NO_HABILITADO", jProf.GetProperty("codigo").GetString());

        var (rPaciente, jPaciente) = await Ingresar(admin, "paciente");
        Assert.Equal(HttpStatusCode.Forbidden, rPaciente.StatusCode);
        Assert.Equal("ROL_INCORRECTO", jPaciente.GetProperty("codigo").GetString());
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
    public async Task Un_permiso_que_el_Configurador_quito_deja_de_valer_al_cambiar_de_modulo()
    {
        var admin = UsuarioConPermisos("configurador", "crm");
        var (_, j) = await Ingresar(admin, "configurador");
        var token = j.GetProperty("token").GetString()!;

        DatosDePrueba.Ejecutar("DELETE FROM UsuarioPermiso WHERE IdUsuario = @u AND IdPermiso = (SELECT IdPermiso FROM Permiso WHERE Clave = 'crm')", ("@u", admin));

        var (r, _) = await ElegirModulo(token, "crm");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
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
