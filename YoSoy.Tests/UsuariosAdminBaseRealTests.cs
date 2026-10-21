using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Fase 2, PR 2: la pantalla Usuarios del Configurador contra la base de pruebas, de punta a punta con la aplicación real: la lista única,
// restablecer la contraseña (la clave una sola vez, nunca en logs ni en la auditoría), el cambio obligatorio al primer ingreso (el token
// de cambio solo sirve en su ruta), la asignación de profesionales a un paciente y la autorización de todo lo nuevo.
[Trait("Categoria", "Integracion")]
public class UsuariosAdminBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly LogsCapturados _logs = new();
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly int _idAdmin;
    private readonly HttpClient _admin;

    public UsuariosAdminBaseRealTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(_logs)));
        _idAdmin = _d.Usuario();
        _admin = DatosDePrueba.Cliente(_fabrica, _idAdmin, Roles.Configurador);
    }

    public void Dispose()
    {
        _fabrica.Dispose();
        _d.Dispose();   // la auditoría es de solo agregar: sus filas se quedan, con la marca de esta prueba
    }

    // ───────────── Ayudas ─────────────

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return texto.Length == 0 ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }

    private async Task<(HttpStatusCode Estado, JsonElement J)> Lista(string consulta)
    {
        var r = await _admin.GetAsync("/usuarios/lista?" + consulta);
        return (r.StatusCode, await Json(r));
    }

    private static string[] Usernames(JsonElement j) =>
        j.GetProperty("filas").EnumerateArray().Select(f => f.GetProperty("username").GetString() ?? "(sin cuenta)").ToArray();

    private JsonElement Fila(JsonElement j, int idUsuario) =>
        j.GetProperty("filas").EnumerateArray().Single(f => f.GetProperty("idUsuario").ValueKind == JsonValueKind.Number && f.GetProperty("idUsuario").GetInt32() == idUsuario);

    private async Task<(HttpResponseMessage R, JsonElement J)> Ingresar(int idUsuario, string? clave = null, string? modulo = null)
    {
        var r = await _fabrica.CreateClient().PostAsJsonAsync("/login", new { username = _d.Username(idUsuario), password = clave ?? DatosDePrueba.ClaveDePrueba, modulo });
        return (r, await Json(r));
    }

    private async Task<string> Restablecer(int idUsuario)
    {
        var r = await _admin.PostAsync($"/usuarios/{idUsuario}/restablecer-password", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await Json(r)).GetProperty("contrasenaTemporal").GetString()!;
    }

    private async Task<(HttpResponseMessage R, JsonElement J)> CambiarClave(string tokenCambio, string? nueva, string? modulo = null)
    {
        var c = DatosDePrueba.Cliente(_fabrica, tokenCambio);
        var r = await c.PostAsJsonAsync("/sesion/cambiar-password", new { passwordNueva = nueva, modulo });
        return (r, await Json(r));
    }

    private int UsuarioConPermisos(params string[] permisos)
    {
        var idUsuario = _d.Usuario();
        foreach (var permiso in permisos)
            DatosDePrueba.Ejecutar(@"
                IF NOT EXISTS (SELECT 1 FROM UsuarioRol ur JOIN Permiso p ON p.IdRol = ur.IdRol WHERE ur.IdUsuario = @u AND p.Clave = @p)
                    INSERT INTO UsuarioRol (IdUsuario, IdRol) SELECT @u, IdRol FROM Permiso WHERE Clave = @p;
                INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) SELECT @u, IdRol, IdPermiso FROM Permiso WHERE Clave = @p;",
                ("@u", idUsuario), ("@p", permiso));
        return idUsuario;
    }

    private static List<(string Accion, int? IdUsuario, string Entidad, int? IdEntidad, string? Detalle, string Categoria)> Auditoria(string accion, int idEntidad, string entidad = "Usuario")
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT Accion, IdUsuario, Entidad, IdEntidad, Detalle, Categoria FROM LogAuditoria WHERE Accion = @a AND Entidad = @e AND IdEntidad = @i ORDER BY IdLog", con);
        cmd.Parameters.AddWithValue("@a", accion);
        cmd.Parameters.AddWithValue("@e", entidad);
        cmd.Parameters.AddWithValue("@i", idEntidad);
        using var rd = cmd.ExecuteReader();
        var lista = new List<(string, int?, string, int?, string?, string)>();
        while (rd.Read())
            lista.Add(((string)rd[0], rd[1] as int?, (string)rd[2], rd[3] as int?, rd[4] as string, (string)rd[5]));
        return lista;
    }

    private static (bool Debe, int Intentos, DateTime? Bloqueado, string Hash) Estado(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT DebeCambiarPassword, IntentosFallidos, BloqueadoHasta, PasswordHash FROM Usuario WHERE IdUsuario = @i", con);
        cmd.Parameters.AddWithValue("@i", idUsuario);
        using var rd = cmd.ExecuteReader();
        Assert.True(rd.Read());
        return ((bool)rd[0], (int)rd[1], rd[2] as DateTime?, (string)rd[3]);
    }

    private static string Hash(string clave) => RestablecerPassword.Hash(clave);

    // ═════════════ La lista única ═════════════

    [Fact]
    public async Task La_lista_une_pacientes_profesionales_y_equipo_con_su_tipo_y_su_ficha()
    {
        var idPac = _d.Paciente("Paciente Lista " + _d.Marca);
        var uPac = _d.UsuarioDePaciente(idPac);
        var idProf = _d.Profesional();
        var uProf = _d.UsuarioDeProfesional(idProf);
        var uEquipo = _d.Usuario();

        var (estado, j) = await Lista("q=" + _d.Marca + "&cantidad=100");

        Assert.Equal(HttpStatusCode.OK, estado);
        var tipos = j.GetProperty("filas").EnumerateArray().ToDictionary(f => f.GetProperty("idUsuario").GetInt32(), f => f.GetProperty("tipo").GetString());
        Assert.Equal("paciente", tipos[uPac]);
        Assert.Equal("profesional", tipos[uProf]);
        Assert.Equal("equipo", tipos[uEquipo]);
        Assert.True(Fila(j, uEquipo).GetProperty("tieneCuenta").GetBoolean());

        var fp = Fila(j, uPac);
        Assert.Equal("Paciente Lista " + _d.Marca, fp.GetProperty("nombre").GetString());
        Assert.Equal(idPac, fp.GetProperty("idPaciente").GetInt32());
        Assert.False(string.IsNullOrEmpty(fp.GetProperty("dni").GetString()));

        var fr = Fila(j, uProf);
        Assert.Equal(idProf, fr.GetProperty("idProfesional").GetInt32());
        Assert.StartsWith("Profesional", fr.GetProperty("nombre").GetString());
        Assert.StartsWith(_d.Marca, fr.GetProperty("matricula").GetString());
    }

    [Fact]
    public async Task La_lista_no_trae_ni_hash_ni_datos_clinicos()
    {
        var uPac = _d.UsuarioDePaciente(_d.Paciente());
        var r = await _admin.GetAsync("/usuarios/lista?q=" + _d.Marca);
        var texto = await r.Content.ReadAsStringAsync();
        Assert.Contains(_d.Username(uPac), texto);
        Assert.DoesNotContain(DatosDePrueba.HashDeLaClaveDePrueba, texto, StringComparison.OrdinalIgnoreCase);
        foreach (var prohibido in new[] { "passwordHash", "hash", "historia", "diagnostico", "turno", "evolucion", "telefono", "domicilio" })
            Assert.DoesNotContain(prohibido, texto, StringComparison.OrdinalIgnoreCase);
        var propiedades = JsonDocument.Parse(texto).RootElement.GetProperty("filas")[0].EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "bloqueado", "bloqueadoHasta", "debeCambiarPassword", "dni", "esUnoMismo", "especialidad", "estado", "idPaciente", "idProfesional",
            "idUsuario", "linkPublicoActivo", "mail", "matricula", "nombre", "tieneCuenta", "tipo", "username" }, propiedades);
    }

    [Fact]
    public async Task Filtra_por_tipo()
    {
        var uPac = _d.UsuarioDePaciente(_d.Paciente());
        var uProf = _d.UsuarioDeProfesional(_d.Profesional());
        var uEq = _d.Usuario();

        var (_, soloPac) = await Lista($"q={_d.Marca}&tipo=paciente");
        Assert.Contains(_d.Username(uPac), Usernames(soloPac));
        Assert.DoesNotContain(_d.Username(uProf), Usernames(soloPac));
        Assert.DoesNotContain(_d.Username(uEq), Usernames(soloPac));
        var (_, soloProf) = await Lista($"q={_d.Marca}&tipo=profesional");
        Assert.Contains(_d.Username(uProf), Usernames(soloProf));
        Assert.DoesNotContain(_d.Username(uPac), Usernames(soloProf));
        var (_, soloEquipo) = await Lista($"q={_d.Marca}&tipo=equipo");
        Assert.Contains(_d.Username(uEq), Usernames(soloEquipo));
        Assert.DoesNotContain(_d.Username(uProf), Usernames(soloEquipo));
        Assert.All(soloEquipo.GetProperty("filas").EnumerateArray(), f => Assert.Equal("equipo", f.GetProperty("tipo").GetString()));
    }

    [Fact]
    public async Task Filtra_por_estado_y_marca_la_cuenta_bloqueada_y_la_que_tiene_que_cambiar_la_clave()
    {
        var activo = _d.Usuario();
        var inactivo = _d.Usuario("Inactivo");
        var pendiente = _d.Usuario("Pendiente");
        var bloqueado = _d.Usuario();
        var conClaveTemporal = _d.Usuario();
        DatosDePrueba.Ejecutar("UPDATE Usuario SET IntentosFallidos = 3, BloqueadoHasta = DATEADD(MINUTE, 10, GETUTCDATE()) WHERE IdUsuario = @i", ("@i", bloqueado));
        DatosDePrueba.Ejecutar("UPDATE Usuario SET DebeCambiarPassword = 1 WHERE IdUsuario = @i", ("@i", conClaveTemporal));

        string U(int id) => _d.Username(id);
        var (_, activos) = await Lista($"q={_d.Marca}&estado=activo&cantidad=100");
        Assert.Equal(new[] { U(activo), U(bloqueado), U(conClaveTemporal) }.OrderBy(x => x), Usernames(activos).OrderBy(x => x).Where(u => u != U(_idAdmin)));
        Assert.Equal(new[] { U(inactivo) }, Usernames((await Lista($"q={_d.Marca}&estado=inactivo")).J));
        Assert.Equal(new[] { U(pendiente) }, Usernames((await Lista($"q={_d.Marca}&estado=pendiente")).J));
        var (_, bloq) = await Lista($"q={_d.Marca}&estado=bloqueado");
        Assert.Equal(new[] { U(bloqueado) }, Usernames(bloq));
        Assert.True(Fila(bloq, bloqueado).GetProperty("bloqueado").GetBoolean());
        Assert.Equal(JsonValueKind.String, Fila(bloq, bloqueado).GetProperty("bloqueadoHasta").ValueKind);

        var (_, todos) = await Lista($"q={_d.Marca}&cantidad=100");
        Assert.False(Fila(todos, activo).GetProperty("bloqueado").GetBoolean());
        Assert.True(Fila(todos, conClaveTemporal).GetProperty("debeCambiarPassword").GetBoolean());
        Assert.False(Fila(todos, activo).GetProperty("debeCambiarPassword").GetBoolean());
        Assert.Equal("Pendiente", Fila(todos, pendiente).GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Un_bloqueo_vencido_ya_no_cuenta_como_bloqueado()
    {
        var id = _d.Usuario();
        DatosDePrueba.Ejecutar("UPDATE Usuario SET IntentosFallidos = 3, BloqueadoHasta = DATEADD(MINUTE, -5, GETUTCDATE()) WHERE IdUsuario = @i", ("@i", id));
        var (_, j) = await Lista($"q={_d.Username(id)}");
        Assert.False(Fila(j, id).GetProperty("bloqueado").GetBoolean());
        Assert.Empty(Usernames((await Lista($"q={_d.Username(id)}&estado=bloqueado")).J));
    }

    [Fact]
    public async Task Dar_de_baja_la_ficha_de_un_paciente_o_profesional_cuenta_como_Inactivo_en_la_lista()
    {
        // Dar de baja a un paciente o a un profesional baja su ficha, no su usuario (DELETE /pacientes/{id}): la lista lo tiene que mostrar.
        var idPac = _d.Paciente();
        var uPac = _d.UsuarioDePaciente(idPac);
        var idProf = _d.Profesional();
        var uProf = _d.UsuarioDeProfesional(idProf);
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Estado = 'Inactivo' WHERE IdPaciente = @i", ("@i", idPac));
        DatosDePrueba.Ejecutar("UPDATE Profesional SET Estado = 'Inactivo' WHERE IdProfesional = @i", ("@i", idProf));

        var (_, inactivos) = await Lista($"q={_d.Marca}&estado=inactivo&cantidad=100");
        Assert.Equal(new[] { _d.Username(uPac), _d.Username(uProf) }.OrderBy(x => x), Usernames(inactivos).OrderBy(x => x));
        Assert.Equal("Inactivo", Fila(inactivos, uPac).GetProperty("estado").GetString());
        var (_, activos) = await Lista($"q={_d.Marca}&estado=activo&cantidad=100");
        Assert.DoesNotContain(_d.Username(uPac), Usernames(activos));
        Assert.DoesNotContain(_d.Username(uProf), Usernames(activos));
    }

    [Fact]
    public async Task La_lista_dice_si_el_profesional_tiene_el_link_publico_activo()
    {
        var idProf = _d.Profesional();
        var u = _d.UsuarioDeProfesional(idProf);
        var idPac = _d.Paciente();
        var uPac = _d.UsuarioDePaciente(idPac);
        DatosDePrueba.Ejecutar("UPDATE Profesional SET LinkPublicoActivo = 1 WHERE IdProfesional = @i", ("@i", idProf));
        var (_, j) = await Lista($"q={_d.Marca}&cantidad=100");
        Assert.True(Fila(j, u).GetProperty("linkPublicoActivo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Fila(j, uPac).GetProperty("linkPublicoActivo").ValueKind);   // solo aplica a profesionales
    }

    [Fact]
    public async Task Los_pacientes_y_profesionales_sin_cuenta_estan_en_su_propio_filtro()
    {
        var pacSin = _d.Paciente("Sin Cuenta " + _d.Marca);
        var profSin = _d.Profesional();
        var profPendiente = _d.Profesional(estado: "Pendiente");
        var profInactivoSin = _d.Profesional(estado: "Inactivo");
        var idPacConCuenta = _d.Paciente();
        _d.UsuarioDePaciente(idPacConCuenta);

        // La lista de cuentas no los trae...
        var (_, cuentas) = await Lista($"q={_d.Marca}&cantidad=100");
        Assert.DoesNotContain(cuentas.GetProperty("filas").EnumerateArray(), f => f.TryGetProperty("idPaciente", out var p) && p.ValueKind == JsonValueKind.Number && p.GetInt32() == pacSin);

        // ...el filtro "sin-cuenta" sí, y solo a ellos.
        var (_, sin) = await Lista($"q={_d.Marca}&estado=sin-cuenta&cantidad=100");
        var filas = sin.GetProperty("filas").EnumerateArray().ToList();
        Assert.All(filas, f =>
        {
            Assert.False(f.GetProperty("tieneCuenta").GetBoolean());
            Assert.Equal(JsonValueKind.Null, f.GetProperty("idUsuario").ValueKind);
            Assert.Equal(JsonValueKind.Null, f.GetProperty("username").ValueKind);
        });
        Assert.Contains(filas, f => f.GetProperty("tipo").GetString() == "paciente" && f.GetProperty("idPaciente").GetInt32() == pacSin);
        Assert.Contains(filas, f => f.GetProperty("tipo").GetString() == "profesional" && f.GetProperty("idProfesional").GetInt32() == profSin);
        Assert.Contains(filas, f => f.GetProperty("idProfesional").ValueKind == JsonValueKind.Number && f.GetProperty("idProfesional").GetInt32() == profInactivoSin);
        // El profesional que todavía es una solicitud no es un usuario por crear: está en Solicitudes de registro.
        Assert.DoesNotContain(filas, f => f.GetProperty("idProfesional").ValueKind == JsonValueKind.Number && f.GetProperty("idProfesional").GetInt32() == profPendiente);
        // Quien ya tiene usuario no está en "sin cuenta".
        Assert.DoesNotContain(filas, f => f.GetProperty("idPaciente").ValueKind == JsonValueKind.Number && f.GetProperty("idPaciente").GetInt32() == idPacConCuenta);
        // Y el tipo se combina con el filtro.
        var (_, soloPacientes) = await Lista($"q={_d.Marca}&estado=sin-cuenta&tipo=paciente&cantidad=100");
        Assert.All(soloPacientes.GetProperty("filas").EnumerateArray(), f => Assert.Equal("paciente", f.GetProperty("tipo").GetString()));
        Assert.Empty((await Lista($"q={_d.Marca}&estado=sin-cuenta&tipo=equipo")).J.GetProperty("filas").EnumerateArray());
    }

    [Fact]
    public async Task Busca_por_usuario_nombre_DNI_matricula_y_mail_sin_distinguir_mayusculas()
    {
        var idPac = _d.Paciente("Zoilo Quintanilla " + _d.Marca);
        var uPac = _d.UsuarioDePaciente(idPac);
        var dni = (string)DatosDePrueba.Escalar("SELECT Dni FROM Paciente WHERE IdPaciente = @i", ("@i", idPac))!;
        var idProf = _d.Profesional();
        var uProf = _d.UsuarioDeProfesional(idProf);
        var matricula = (string)DatosDePrueba.Escalar("SELECT Matricula FROM Profesional WHERE IdProfesional = @i", ("@i", idProf))!;
        var mail = (string)DatosDePrueba.Escalar("SELECT Mail FROM Usuario WHERE IdUsuario = @i", ("@i", uProf))!;

        Assert.Contains(_d.Username(uPac), Usernames((await Lista("q=" + _d.Username(uPac).ToUpperInvariant())).J));       // usuario
        Assert.Equal(new[] { _d.Username(uPac) }, Usernames((await Lista($"q=quintanilla+{_d.Marca}")).J));                  // nombre, dos palabras en cualquier orden
        Assert.Equal(new[] { _d.Username(uPac) }, Usernames((await Lista($"q={_d.Marca}+ZOILO")).J));
        Assert.Contains(_d.Username(uPac), Usernames((await Lista("q=" + dni)).J));                                             // DNI
        Assert.Contains(_d.Username(uProf), Usernames((await Lista("q=" + matricula)).J));                                      // matrícula
        Assert.Contains(_d.Username(uProf), Usernames((await Lista("q=" + Uri.EscapeDataString(mail))).J));                     // mail
        Assert.Empty(Usernames((await Lista("q=zzzNoExisteNadieAsi" + _d.Marca)).J));
    }

    [Fact]
    public async Task Los_comodines_de_LIKE_se_buscan_literales()
    {
        _d.Usuario();
        var (_, conPorcentaje) = await Lista("q=" + Uri.EscapeDataString("%"));
        Assert.Equal(0, conPorcentaje.GetProperty("total").GetInt32());
        var (_, conGuion) = await Lista("q=" + Uri.EscapeDataString("____________") + "&cantidad=100");
        Assert.Equal(0, conGuion.GetProperty("total").GetInt32());
        var (estado, _) = await Lista("q=" + Uri.EscapeDataString("[a-z]") + "&cantidad=100");
        Assert.Equal(HttpStatusCode.OK, estado);
        // Una comilla no rompe nada (los valores van como parámetros).
        var (comilla, _) = await Lista("q=" + Uri.EscapeDataString("o'brien'; DROP TABLE Usuario;--"));
        Assert.Equal(HttpStatusCode.OK, comilla);
        Assert.True(DatosDePrueba.Contar("SELECT COUNT(*) FROM Usuario") > 0);
    }

    [Fact]
    public async Task Pagina_en_el_servidor_con_total_y_cantidad_de_paginas()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => _d.Usuario()).ToList();
        var vistos = new List<string>();
        for (var pagina = 1; pagina <= 3; pagina++)
        {
            var (estado, j) = await Lista($"q={_d.Marca}&cantidad=2&pagina={pagina}");
            Assert.Equal(HttpStatusCode.OK, estado);
            Assert.Equal(6, j.GetProperty("total").GetInt32());          // los 5 más el administrador de la prueba
            Assert.Equal(3, j.GetProperty("paginas").GetInt32());
            Assert.Equal(pagina, j.GetProperty("pagina").GetInt32());
            Assert.Equal(2, j.GetProperty("cantidad").GetInt32());
            vistos.AddRange(Usernames(j));
        }
        Assert.Equal(6, vistos.Distinct().Count());                       // sin repetir ni saltear
        Assert.Equal(ids.Select(_d.Username).Append(_d.Username(_idAdmin)).OrderBy(x => x), vistos.OrderBy(x => x));

        // Una página que no existe vuelve a la primera.
        var (_, afuera) = await Lista($"q={_d.Marca}&cantidad=2&pagina=99");
        Assert.Equal(1, afuera.GetProperty("pagina").GetInt32());
        Assert.Equal(2, afuera.GetProperty("filas").GetArrayLength());
    }

    [Theory]
    [InlineData("tipo=admin"), InlineData("estado=borrado"), InlineData("pagina=0"), InlineData("cantidad=0"), InlineData("cantidad=101")]
    public async Task Los_filtros_invalidos_dan_400_con_un_mensaje(string consulta)
    {
        var (estado, j) = await Lista(consulta);
        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.False(string.IsNullOrWhiteSpace(j.GetProperty("mensaje").GetString()));
    }

    [Fact]
    public async Task La_fila_propia_se_marca_para_que_la_pantalla_no_ofrezca_restablecerse()
    {
        var (_, j) = await Lista("q=" + _d.Username(_idAdmin));
        Assert.True(Fila(j, _idAdmin).GetProperty("esUnoMismo").GetBoolean());
        var otro = _d.Usuario();
        var (_, k) = await Lista("q=" + _d.Username(otro));
        Assert.False(Fila(k, otro).GetProperty("esUnoMismo").GetBoolean());
    }

    // ═════════════ Restablecer la contraseña ═════════════

    [Fact]
    public async Task Restablecer_devuelve_la_clave_una_sola_vez_sin_cache_y_deja_la_cuenta_lista_para_el_cambio()
    {
        var id = _d.Usuario();
        DatosDePrueba.Ejecutar("UPDATE Usuario SET IntentosFallidos = 3, BloqueadoHasta = DATEADD(MINUTE, 10, GETUTCDATE()) WHERE IdUsuario = @i", ("@i", id));

        var r = await _admin.PostAsync($"/usuarios/{id}/restablecer-password", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("no-store", r.Headers.CacheControl!.ToString());
        var j = await Json(r);
        var clave = j.GetProperty("contrasenaTemporal").GetString()!;
        Assert.Null(PoliticaContrasena.Validar(clave));
        Assert.Equal(_d.Username(id), j.GetProperty("username").GetString());
        Assert.Equal(id, j.GetProperty("idUsuario").GetInt32());

        var (debe, intentos, bloqueado, hash) = Estado(id);
        Assert.True(debe);
        Assert.Equal(0, intentos);
        Assert.Null(bloqueado);                                      // desbloquea
        Assert.Equal(Hash(clave), hash, ignoreCase: true);          // el mismo hash del sistema

        // Otra vez: otra clave (la anterior no se puede volver a ver), también sin cache.
        var otra = await _admin.PostAsync($"/usuarios/{id}/restablecer-password", null);
        Assert.NotEqual(clave, (await Json(otra)).GetProperty("contrasenaTemporal").GetString());
    }

    [Fact]
    public async Task Despues_de_restablecer_la_clave_vieja_no_entra_y_la_temporal_pide_elegir_una_nueva_sin_dar_sesion()
    {
        var id = _d.Usuario();
        var clave = await Restablecer(id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Ingresar(id, DatosDePrueba.ClaveDePrueba)).R.StatusCode);

        var (r, j) = await Ingresar(id, clave);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(j.GetProperty("requiereCambioPassword").GetBoolean());
        Assert.Equal(id, j.GetProperty("idUsuario").GetInt32());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("tokenCambioClave").GetString()));
        foreach (var propiedad in new[] { "token", "modulo", "modulos", "rol", "permisos", "tokenSeleccion", "requiereModulo" })
            Assert.False(j.TryGetProperty(propiedad, out _), $"no debería traer {propiedad}");
    }

    [Fact]
    public async Task Restablecer_aplica_a_pacientes_profesionales_y_equipo_y_todos_tienen_que_cambiar_la_clave()
    {
        var uPac = _d.UsuarioDePaciente(_d.Paciente());
        var uProf = _d.UsuarioDeProfesional(_d.Profesional());
        var uEquipo = UsuarioConPermisos("configurador");
        foreach (var id in new[] { uPac, uProf, uEquipo })
        {
            var clave = await Restablecer(id);
            var (r, j) = await Ingresar(id, clave);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.True(j.GetProperty("requiereCambioPassword").GetBoolean());
        }
    }

    [Fact]
    public async Task Nunca_sobre_uno_mismo()
    {
        var antes = Estado(_idAdmin);
        var r = await _admin.PostAsync($"/usuarios/{_idAdmin}/restablecer-password", null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("RESTABLECER_PROPIA", (await Json(r)).GetProperty("codigo").GetString());
        Assert.DoesNotContain("contrasenaTemporal", await r.Content.ReadAsStringAsync());
        Assert.Equal(antes, Estado(_idAdmin));
        Assert.Empty(Auditoria("Restablecimiento de contraseña", _idAdmin));
    }

    [Fact]
    public async Task Una_cuenta_que_no_existe_o_no_esta_activa_no_se_restablece()
    {
        var inactivo = _d.Usuario("Inactivo");
        var pendiente = _d.Usuario("Pendiente");
        var rInexistente = await _admin.PostAsync("/usuarios/2000000000/restablecer-password", null);
        Assert.Equal(HttpStatusCode.NotFound, rInexistente.StatusCode);
        foreach (var id in new[] { inactivo, pendiente })
        {
            var antes = Estado(id);
            var r = await _admin.PostAsync($"/usuarios/{id}/restablecer-password", null);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.DoesNotContain("contrasenaTemporal", await r.Content.ReadAsStringAsync());
            Assert.Equal(antes, Estado(id));   // la transacción se deshizo
            Assert.Empty(Auditoria("Restablecimiento de contraseña", id));
        }
    }

    [Fact]
    public async Task Restablecer_queda_en_la_auditoria_administrativa_con_quien_y_a_quien_pero_sin_la_clave()
    {
        var id = _d.Usuario();
        var clave = await Restablecer(id);

        var filas = Auditoria("Restablecimiento de contraseña", id);
        var fila = Assert.Single(filas);
        Assert.Equal(_idAdmin, fila.IdUsuario);                        // quién lo hizo
        Assert.Equal(id, fila.IdEntidad);                              // a quién
        Assert.Equal("Administrativa", fila.Categoria);
        Assert.Contains(_d.Username(id), fila.Detalle!);               // usuario afectado
        Assert.DoesNotContain(clave, fila.Detalle!);
        Assert.DoesNotContain(Hash(clave), fila.Detalle!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task La_clave_temporal_nunca_aparece_en_los_logs_ni_en_ningun_registro_de_auditoria()
    {
        var id = UsuarioConPermisos("configurador");
        var clave = await Restablecer(id);
        var (_, ingreso) = await Ingresar(id, clave);                 // pasa por el ingreso con la clave
        var nueva = "Nueva-" + Guid.NewGuid().ToString("N")[..10];
        var (cambio, _) = await CambiarClave(ingreso.GetProperty("tokenCambioClave").GetString()!, nueva);
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);

        foreach (var secreto in new[] { clave, nueva, Hash(clave), Hash(nueva) })
        {
            Assert.DoesNotContain(secreto, _logs.Todo, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Detalle LIKE @s OR Accion LIKE @s", ("@s", "%" + secreto + "%")));
        }
    }

    [Fact]
    public async Task No_se_cierran_las_sesiones_ya_abiertas_de_esa_cuenta_limite_conocido()
    {
        // No hay revocación de tokens: la sesión que esa persona ya tenía sigue valiendo hasta que venza (8 horas). Está documentado en
        // docs/DEPLOY_USUARIOS_PRODUCCION.md; esta prueba fija el comportamiento para que un cambio futuro sea consciente.
        var id = _d.Usuario();
        var tokenViejo = _fabrica.Services.GetRequiredService<JwtEmisorService>().Emitir(id, Roles.Configurador);
        await Restablecer(id);
        var r = await DatosDePrueba.Cliente(_fabrica, tokenViejo).GetAsync("/sesion/yo");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Restablecer_tiene_limite_de_pedidos()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("UsuariosAdmin:RestablecerPorDiezMinutos", "3"));
        var cliente = DatosDePrueba.Cliente(fabrica, _idAdmin, Roles.Configurador);
        var id = _d.Usuario();
        var estados = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) estados.Add((await cliente.PostAsync($"/usuarios/{id}/restablecer-password", null)).StatusCode);
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests }, estados);
    }

    // ═════════════ Cambio obligatorio al primer ingreso ═════════════

    [Fact]
    public async Task El_token_de_cambio_solo_sirve_en_su_ruta()
    {
        var id = UsuarioConPermisos("configurador");
        var tokenCambio = _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirCambioClave(id);
        var c = DatosDePrueba.Cliente(_fabrica, tokenCambio);

        foreach (var ruta in new[] { "/sesion/yo", "/usuarios/lista", "/usuarios/accesos", "/roles", "/mensajeria/conversaciones" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/sesion/modulo", new { modulo = "configurador" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync($"/usuarios/{_d.Usuario()}/restablecer-password", null)).StatusCode);
        // Y en su ruta sí se lo acepta (aunque acá la cuenta no tenga nada pendiente: 409, no 401).
        var enSuRuta = await c.PostAsJsonAsync("/sesion/cambiar-password", new { passwordNueva = "UnaClaveNueva9" });
        Assert.Equal(HttpStatusCode.Conflict, enSuRuta.StatusCode);
    }

    [Fact]
    public async Task Las_demas_sesiones_no_pueden_usar_la_ruta_de_cambio()
    {
        var id = UsuarioConPermisos("configurador");
        var normal = DatosDePrueba.Cliente(_fabrica, id, Roles.Configurador);
        var r = await normal.PostAsJsonAsync("/sesion/cambiar-password", new { passwordNueva = "UnaClaveNueva9" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var seleccion = DatosDePrueba.Cliente(_fabrica, _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirSeleccion(id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await seleccion.PostAsJsonAsync("/sesion/cambiar-password", new { passwordNueva = "UnaClaveNueva9" })).StatusCode);
        var sinSesion = await _fabrica.CreateClient().PostAsJsonAsync("/sesion/cambiar-password", new { passwordNueva = "UnaClaveNueva9" });
        Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);
        Assert.False(Estado(id).Debe);
    }

    [Fact]
    public async Task El_cambio_valida_la_politica_exige_una_clave_distinta_y_despues_entrega_la_sesion_normal()
    {
        var id = UsuarioConPermisos("configurador");
        var temporal = await Restablecer(id);
        var (_, ingreso) = await Ingresar(id, temporal);
        var tokenCambio = ingreso.GetProperty("tokenCambioClave").GetString()!;

        // Política: al menos 6 caracteres.
        var (corta, jCorta) = await CambiarClave(tokenCambio, "abc");
        Assert.Equal(HttpStatusCode.BadRequest, corta.StatusCode);
        Assert.Equal("CLAVE_INVALIDA", jCorta.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await CambiarClave(tokenCambio, null)).R.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CambiarClave(tokenCambio, new string('x', PoliticaContrasena.Maximo + 1))).R.StatusCode);
        // Distinta de la temporal.
        var (igual, jIgual) = await CambiarClave(tokenCambio, temporal);
        Assert.Equal(HttpStatusCode.BadRequest, igual.StatusCode);
        Assert.Equal("CLAVE_IGUAL_A_LA_TEMPORAL", jIgual.GetProperty("codigo").GetString());
        Assert.True(Estado(id).Debe);                                  // nada se guardó

        // Una clave buena: entrega la sesión por el mismo camino que el ingreso.
        var nueva = "MiClaveNueva-" + Guid.NewGuid().ToString("N")[..6];
        var (ok, j) = await CambiarClave(tokenCambio, nueva);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("no-store", ok.Headers.CacheControl!.ToString());
        Assert.Equal("configurador", j.GetProperty("modulo").GetString());
        Assert.Equal("configurador", j.GetProperty("rol").GetString());
        Assert.Equal(id, j.GetProperty("idUsuario").GetInt32());
        Assert.Contains("configurador", j.GetProperty("modulos").EnumerateArray().Select(m => m.GetProperty("clave").GetString()));
        var sesion = DatosDePrueba.Cliente(_fabrica, j.GetProperty("token").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await sesion.GetAsync("/usuarios/lista")).StatusCode);

        var (debe, intentos, bloqueado, hash) = Estado(id);
        Assert.False(debe);
        Assert.Equal((0, null), (intentos, bloqueado));
        Assert.Equal(Hash(nueva), hash, ignoreCase: true);

        // La clave nueva entra directo; la temporal ya no.
        var (entra, jEntra) = await Ingresar(id, nueva);
        Assert.Equal(HttpStatusCode.OK, entra.StatusCode);
        Assert.False(jEntra.TryGetProperty("requiereCambioPassword", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Ingresar(id, temporal)).R.StatusCode);
    }

    [Fact]
    public async Task El_cambio_queda_auditado_sin_secretos_y_el_mismo_token_no_sirve_dos_veces()
    {
        var id = UsuarioConPermisos("configurador");
        var temporal = await Restablecer(id);
        var tokenCambio = (await Ingresar(id, temporal)).J.GetProperty("tokenCambioClave").GetString()!;
        var nueva = "OtraClave-" + Guid.NewGuid().ToString("N")[..6];

        Assert.Equal(HttpStatusCode.OK, (await CambiarClave(tokenCambio, nueva)).R.StatusCode);

        var fila = Assert.Single(Auditoria("Cambio de contraseña", id));
        Assert.Equal(id, fila.IdUsuario);
        Assert.Equal("Administrativa", fila.Categoria);
        Assert.DoesNotContain(nueva, fila.Detalle!);
        Assert.DoesNotContain(temporal, fila.Detalle!);
        Assert.DoesNotContain(Hash(nueva), fila.Detalle!, StringComparison.OrdinalIgnoreCase);

        // El token todavía no venció (5 minutos), pero la marca ya bajó: no se puede volver a cambiar la clave con él.
        var (segunda, j) = await CambiarClave(tokenCambio, "TercerClave-123");
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("CAMBIO_NO_REQUERIDO", j.GetProperty("codigo").GetString());
        Assert.Equal(Hash(nueva), Estado(id).Hash, ignoreCase: true);
    }

    [Fact]
    public async Task Un_paciente_cambia_su_clave_y_entra_como_paciente()
    {
        var idPac = _d.Paciente();
        var uPac = _d.UsuarioDePaciente(idPac);
        var temporal = await Restablecer(uPac);
        var tokenCambio = (await Ingresar(uPac, temporal, "paciente")).J.GetProperty("tokenCambioClave").GetString()!;

        var (r, j) = await CambiarClave(tokenCambio, "ClavePaciente-" + Guid.NewGuid().ToString("N")[..6], "paciente");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("paciente", j.GetProperty("rol").GetString());
        Assert.Equal(idPac, j.GetProperty("idPaciente").GetInt32());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Quien_tiene_dos_modulos_elige_despues_del_cambio_igual_que_en_el_ingreso()
    {
        var id = UsuarioConPermisos("configurador", "crm");
        var tokenCambio = (await Ingresar(id, await Restablecer(id))).J.GetProperty("tokenCambioClave").GetString()!;

        var (r, j) = await CambiarClave(tokenCambio, "DosModulos-" + Guid.NewGuid().ToString("N")[..6]);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(j.GetProperty("requiereModulo").GetBoolean());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("tokenSeleccion").GetString()));
        Assert.False(j.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task La_pestania_elegida_en_el_ingreso_se_respeta_despues_del_cambio_y_la_que_no_tiene_se_rechaza()
    {
        var id = UsuarioConPermisos("configurador", "crm");
        var tokenCambio = (await Ingresar(id, await Restablecer(id), "crm")).J.GetProperty("tokenCambioClave").GetString()!;
        var (r, j) = await CambiarClave(tokenCambio, "ConPestania-" + Guid.NewGuid().ToString("N")[..6], "crm");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("crm", j.GetProperty("modulo").GetString());

        var sinPermiso = UsuarioConPermisos("configurador");
        var token2 = (await Ingresar(sinPermiso, await Restablecer(sinPermiso))).J.GetProperty("tokenCambioClave").GetString()!;
        var (r2, j2) = await CambiarClave(token2, "SinEseModulo-" + Guid.NewGuid().ToString("N")[..6], "profesional");
        Assert.Equal(HttpStatusCode.Forbidden, r2.StatusCode);
        Assert.Equal("MODULO_NO_HABILITADO", j2.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Sin_permisos_la_clave_igual_se_cambia_y_el_ingreso_responde_SIN_PERMISOS_como_siempre()
    {
        var id = _d.Usuario();                                         // sin ningún permiso
        var tokenCambio = (await Ingresar(id, await Restablecer(id))).J.GetProperty("tokenCambioClave").GetString()!;
        var (r, j) = await CambiarClave(tokenCambio, "SinPermisos-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("SIN_PERMISOS", j.GetProperty("codigo").GetString());
        Assert.False(Estado(id).Debe);                                 // la clave quedó elegida: no se pierde el cambio
    }

    [Fact]
    public async Task El_bloqueo_y_la_verificacion_van_primero_una_clave_mala_o_una_cuenta_bloqueada_no_reciben_el_token()
    {
        var id = UsuarioConPermisos("configurador");
        var temporal = await Restablecer(id);

        var (mala, jMala) = await Ingresar(id, "no-es-la-clave");
        Assert.Equal(HttpStatusCode.Unauthorized, mala.StatusCode);
        Assert.False(jMala.ValueKind == JsonValueKind.Object && jMala.TryGetProperty("tokenCambioClave", out _));
        Assert.Equal(1, Estado(id).Intentos);                          // cuenta como intento fallido, como siempre

        for (var i = 0; i < 2; i++) await Ingresar(id, "sigue-mal");
        var (bloqueada, jBloqueada) = await Ingresar(id, temporal);    // 3 intentos: con la temporal correcta, ahora bloqueado
        Assert.Equal((HttpStatusCode)423, bloqueada.StatusCode);
        Assert.False(jBloqueada.TryGetProperty("tokenCambioClave", out _));
    }

    [Fact]
    public async Task Una_cuenta_inactiva_con_el_cambio_pendiente_no_entra()
    {
        var id = _d.Usuario();
        var temporal = await Restablecer(id);
        DatosDePrueba.Ejecutar("UPDATE Usuario SET Estado = 'Inactivo' WHERE IdUsuario = @i", ("@i", id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Ingresar(id, temporal)).R.StatusCode);
        // Y si el token ya estaba en la mano cuando la dieron de baja, tampoco sirve.
        var token = _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirCambioClave(id);
        var (r, _) = await CambiarClave(token, "ClaveDeBaja-12345");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True(Estado(id).Debe);
    }

    [Fact]
    public async Task El_login_de_siempre_no_cambia_para_quien_no_tiene_el_cambio_pendiente()
    {
        var id = UsuarioConPermisos("configurador");
        var (r, j) = await Ingresar(id);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("configurador", j.GetProperty("modulo").GetString());
        Assert.False(j.TryGetProperty("requiereCambioPassword", out _));
        Assert.False(string.IsNullOrEmpty(j.GetProperty("token").GetString()));
    }

    // ═════════════ Asignar profesionales a un paciente ═════════════

    private (int IdPaciente, int IdProfesional1, int IdProfesional2) PacienteYDosProfesionales()
    {
        var pac = _d.Paciente();
        var esp = _d.Especialidad();
        return (pac, _d.Profesional(esp), _d.Profesional(esp));
    }

    private Task<HttpResponseMessage> Asignar(int idPaciente, params int[] ids) =>
        _admin.PostAsJsonAsync($"/usuarios/pacientes/{idPaciente}/profesionales", new { idProfesionales = ids });

    private Task<HttpResponseMessage> Quitar(int idPaciente, params int[] ids) =>
        _admin.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/usuarios/pacientes/{idPaciente}/profesionales") { Content = JsonContent.Create(new { idProfesionales = ids }) });

    private static int Asignaciones(int idPaciente) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @p", ("@p", idPaciente));

    [Fact]
    public async Task Asigna_varios_profesionales_de_una_vez_y_respeta_el_UNIQUE()
    {
        var (pac, p1, p2) = PacienteYDosProfesionales();
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pac);

        var r = await Asignar(pac, p1, p2);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Json(r);
        Assert.Equal(2, j.GetProperty("asignados").GetInt32());
        Assert.Equal(0, j.GetProperty("sinCambio").GetInt32());
        Assert.Equal(2, Asignaciones(pac));

        // Repetir (o repetir un id dentro del pedido) no duplica ni da error.
        var otra = await Json(await Asignar(pac, p1, p1, p2));
        Assert.Equal(0, otra.GetProperty("asignados").GetInt32());
        Assert.Equal(2, otra.GetProperty("sinCambio").GetInt32());
        Assert.Equal(2, Asignaciones(pac));
    }

    private static string? EstadoDeAsignacion(int idPaciente, int idProfesional) =>
        DatosDePrueba.Escalar("SELECT EstadoAsignacion FROM PacienteProfesional WHERE IdPaciente = @p AND IdProfesional = @r", ("@p", idPaciente), ("@r", idProfesional)) as string;

    [Fact]
    public async Task La_asignacion_del_Configurador_queda_Confirmada_y_la_de_Admision_sigue_vacia()
    {
        var (pac, p1, _) = PacienteYDosProfesionales();
        var pacAdmision = _d.Paciente();
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pac);
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pacAdmision);

        await Asignar(pac, p1);
        new PacienteProfesionalDAO().Asignar(pacAdmision, p1);          // lo que ejecuta POST /pacientes/{id}/profesionales (Admisión): no cambia

        Assert.Equal("Confirmada", EstadoDeAsignacion(pac, p1));
        Assert.Null(EstadoDeAsignacion(pacAdmision, p1));
    }

    [Fact]
    public async Task Si_ya_habia_una_fila_con_otro_estado_la_asignacion_del_Configurador_la_confirma()
    {
        var pac = _d.Paciente();
        var esp = _d.Especialidad();
        var (pVacio, pNueva, pRechazada, pConfirmada) = (_d.Profesional(esp), _d.Profesional(esp), _d.Profesional(esp), _d.Profesional(esp));
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pac);
        DatosDePrueba.Ejecutar(@"INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, EstadoAsignacion) VALUES
            (@p, @a, NULL), (@p, @b, 'Nueva'), (@p, @c, 'Rechazada'), (@p, @d, 'Confirmada')",
            ("@p", pac), ("@a", pVacio), ("@b", pNueva), ("@c", pRechazada), ("@d", pConfirmada));

        var j = await Json(await Asignar(pac, pVacio, pNueva, pRechazada, pConfirmada));

        Assert.Equal(3, j.GetProperty("asignados").GetInt32());          // las tres que no estaban confirmadas
        Assert.Equal(1, j.GetProperty("sinCambio").GetInt32());          // la que ya lo estaba
        foreach (var p in new[] { pVacio, pNueva, pRechazada, pConfirmada }) Assert.Equal("Confirmada", EstadoDeAsignacion(pac, p));
        Assert.Equal(4, Asignaciones(pac));                                // ninguna fila duplicada
        Assert.Equal(3, Auditoria("Asignación de profesional", pac, "Paciente").Count);
    }

    [Fact]
    public async Task El_KPI_de_pacientes_activos_y_la_lista_de_reportes_cuentan_al_paciente_asignado_por_el_Configurador()
    {
        var pac = _d.Paciente();
        var pacAdmision = _d.Paciente();
        var idProf = _d.Profesional();
        var uProf = _d.UsuarioDeProfesional(idProf);
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pac);
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pacAdmision);
        var clienteProf = DatosDePrueba.Cliente(_fabrica, uProf, Roles.Profesional, idProfesional: idProf);

        async Task<int> PacientesActivos() =>
            (await Json(await clienteProf.GetAsync($"/reportes/profesional/{idProf}/kpis"))).GetProperty("pacientesActivos").GetInt32();
        async Task<int[]> ListaDeReportes() => (await Json(await clienteProf.GetAsync($"/reportes/profesional/{idProf}/lista-pacientes")))
            .EnumerateArray().Select(x => x.GetProperty("idPaciente").GetInt32()).ToArray();

        Assert.Equal(0, await PacientesActivos());
        await Asignar(pac, idProf);
        Assert.Equal(1, await PacientesActivos());
        Assert.Contains(pac, await ListaDeReportes());
        // Contraste: la asignación de Admisión (estado vacío) no cuenta en ese reporte; no es parte de este cambio.
        new PacienteProfesionalDAO().Asignar(pacAdmision, idProf);
        Assert.Equal(1, await PacientesActivos());
        Assert.DoesNotContain(pacAdmision, await ListaDeReportes());
    }

    [Fact]
    public async Task Rechaza_que_el_Configurador_se_asigne_a_si_mismo_y_no_asigna_nada_del_pedido()
    {
        var (pac, p1, _) = PacienteYDosProfesionales();
        var miFicha = _d.Profesional();
        _d.AlLimpiar("UsuarioProfesional", "IdUsuario", _idAdmin);
        DatosDePrueba.Ejecutar("INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion) VALUES (@u, @p, GETDATE())", ("@u", _idAdmin), ("@p", miFicha));

        var r = await Asignar(pac, p1, miFicha);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("AUTO_ASIGNACION", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal(0, Asignaciones(pac));                            // ni siquiera el otro profesional del pedido
        // A otros profesionales sí puede.
        Assert.Equal(HttpStatusCode.OK, (await Asignar(pac, p1)).StatusCode);
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pac);
    }

    [Fact]
    public async Task Exige_paciente_y_profesionales_activos_y_es_todo_o_nada()
    {
        var (pac, p1, _) = PacienteYDosProfesionales();
        var profInactivo = _d.Profesional(estado: "Inactivo");
        var profPendiente = _d.Profesional(estado: "Pendiente");
        var pacInactivo = _d.Paciente(estado: "Inactivo");

        var r1 = await Asignar(pac, p1, profInactivo, profPendiente);
        Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);
        Assert.Equal("PROFESIONAL_NO_ACTIVO", (await Json(r1)).GetProperty("codigo").GetString());
        Assert.Equal(0, Asignaciones(pac));                            // el activo del pedido tampoco se asignó

        Assert.Equal(HttpStatusCode.Conflict, (await Asignar(pacInactivo, p1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Asignar(pac, 2000000000)).StatusCode);       // un profesional que no existe
        Assert.Equal(HttpStatusCode.NotFound, (await Asignar(2000000000, p1)).StatusCode);        // un paciente que no existe
        Assert.Equal(0, Asignaciones(pacInactivo));
    }

    [Fact]
    public async Task Quita_uno_o_varios_y_lo_que_no_estaba_asignado_no_es_error()
    {
        var (pac, p1, p2) = PacienteYDosProfesionales();
        var p3 = _d.Profesional();
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pac);
        await Asignar(pac, p1, p2, p3);

        var r = await Quitar(pac, p1, p2, 2000000000);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Json(r);
        Assert.Equal(2, j.GetProperty("quitados").GetInt32());
        Assert.Equal(1, j.GetProperty("sinCambio").GetInt32());
        Assert.Equal(1, Asignaciones(pac));
        Assert.Equal(p3, Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdProfesional FROM PacienteProfesional WHERE IdPaciente = @p", ("@p", pac))));

        // Un profesional dado de baja se puede quitar.
        DatosDePrueba.Ejecutar("UPDATE Profesional SET Estado = 'Inactivo' WHERE IdProfesional = @p", ("@p", p3));
        Assert.Equal(HttpStatusCode.OK, (await Quitar(pac, p3)).StatusCode);
        Assert.Equal(0, Asignaciones(pac));
        Assert.Equal(HttpStatusCode.NotFound, (await Quitar(2000000000, p1)).StatusCode);
    }

    [Fact]
    public async Task Asignar_y_quitar_quedan_en_la_auditoria_administrativa()
    {
        var (pac, p1, p2) = PacienteYDosProfesionales();
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pac);
        await Asignar(pac, p1, p2);
        await Asignar(pac, p1);                                        // sin cambio: no registra de nuevo
        await Quitar(pac, p2);

        var alta = Auditoria("Asignación de profesional", pac, "Paciente");
        Assert.Equal(2, alta.Count);
        Assert.All(alta, f => { Assert.Equal(_idAdmin, f.IdUsuario); Assert.Equal("Administrativa", f.Categoria); });
        Assert.Contains(alta, f => f.Detalle!.Contains("#" + p1));
        Assert.Contains(alta, f => f.Detalle!.Contains("#" + p2));
        var baja = Assert.Single(Auditoria("Desasignación de profesional", pac, "Paciente"));
        Assert.Contains("#" + p2, baja.Detalle!);
        Assert.Equal("Administrativa", baja.Categoria);
    }

    [Fact]
    public async Task Los_asignados_se_leen_solo_con_nombre_y_matricula_sin_datos_de_contacto_ni_clinicos()
    {
        var (pac, p1, p2) = PacienteYDosProfesionales();
        _d.AlLimpiar("PacienteProfesional", "IdPaciente", pac);
        await Asignar(pac, p1);

        var r = await _admin.GetAsync($"/usuarios/pacientes/{pac}/profesionales");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var texto = await r.Content.ReadAsStringAsync();
        var j = JsonDocument.Parse(texto).RootElement;
        Assert.Equal(new[] { p1 }, j.GetProperty("asignados").EnumerateArray().Select(a => a.GetProperty("idProfesional").GetInt32()));
        Assert.Contains(p2, j.GetProperty("disponibles").EnumerateArray().Select(a => a.GetProperty("idProfesional").GetInt32()));
        var propiedades = j.GetProperty("asignados")[0].EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "especialidad", "idProfesional", "matricula", "nombre" }, propiedades);
        foreach (var prohibido in new[] { "mail", "telefono", "historia", "diagnostico", "dni" })
            Assert.DoesNotContain("\"" + prohibido, texto, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/usuarios/pacientes/2000000000/profesionales")).StatusCode);
    }

    [Fact]
    public async Task Los_pedidos_mal_armados_dan_400()
    {
        var (pac, _, _) = PacienteYDosProfesionales();
        Assert.Equal(HttpStatusCode.BadRequest, (await Asignar(pac)).StatusCode);                                         // lista vacía
        Assert.Equal(HttpStatusCode.BadRequest, (await Asignar(pac, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Asignar(pac, Enumerable.Range(1, 51).ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync($"/usuarios/pacientes/{pac}/profesionales", new { otraCosa = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsync($"/usuarios/pacientes/{pac}/profesionales", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Quitar(pac)).StatusCode);
    }

    [Fact]
    public async Task Los_endpoints_de_Admision_siguen_siendo_solo_de_Admision()
    {
        var (pac, p1, _) = PacienteYDosProfesionales();
        var r = await _admin.PostAsJsonAsync($"/pacientes/{pac}/profesionales", new { idProfesional = p1 });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.DeleteAsync($"/pacientes/{pac}/profesionales/{p1}")).StatusCode);
        Assert.Equal(0, Asignaciones(pac));
    }

    // ═════════════ Dar de baja la ficha baja también al usuario ═════════════

    private static string EstadoUsuario(int idUsuario) => (string)DatosDePrueba.Escalar("SELECT Estado FROM Usuario WHERE IdUsuario = @i", ("@i", idUsuario))!;
    private static string EstadoFicha(string tabla, string columna, int id) => (string)DatosDePrueba.Escalar($"SELECT Estado FROM {tabla} WHERE {columna} = @i", ("@i", id))!;

    private void DarPermiso(int idUsuario, string permiso) => DatosDePrueba.Ejecutar(@"
        INSERT INTO UsuarioRol (IdUsuario, IdRol) SELECT @u, IdRol FROM Permiso WHERE Clave = @p;
        INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) SELECT @u, IdRol, IdPermiso FROM Permiso WHERE Clave = @p;", ("@u", idUsuario), ("@p", permiso));

    [Fact]
    public async Task Dar_de_baja_a_un_paciente_baja_tambien_a_su_usuario_y_el_ingreso_responde_como_a_un_inactivo()
    {
        var idPac = _d.Paciente();
        var uPac = _d.UsuarioDePaciente(idPac);
        Assert.Equal(HttpStatusCode.OK, (await Ingresar(uPac, modulo: "paciente")).R.StatusCode);   // antes de la baja entra

        var r = await _admin.DeleteAsync($"/pacientes/{idPac}");

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal("Inactivo", EstadoFicha("Paciente", "IdPaciente", idPac));
        Assert.Equal("Inactivo", EstadoUsuario(uPac));
        var (ingreso, _) = await Ingresar(uPac, modulo: "paciente");
        Assert.Equal(HttpStatusCode.Unauthorized, ingreso.StatusCode);                              // igual que un usuario inactivo
        // Auditoría administrativa: la baja de la ficha y la del usuario, con quién lo hizo.
        var ficha = Assert.Single(Auditoria("Baja Paciente", idPac, "Paciente"));
        Assert.Equal((_idAdmin, "Administrativa"), (ficha.IdUsuario, ficha.Categoria));
        var usuario = Assert.Single(Auditoria("Baja Usuario", uPac));
        Assert.Equal((_idAdmin, "Administrativa"), (usuario.IdUsuario, usuario.Categoria));
        Assert.Contains($"paciente #{idPac}", usuario.Detalle!);
        var (_, lista) = await Lista($"q={_d.Username(uPac)}");
        Assert.Equal("Inactivo", Fila(lista, uPac).GetProperty("estado").GetString());
    }

    [Fact]
    public async Task La_baja_de_un_profesional_baja_su_usuario_y_conserva_sus_asignaciones_como_hoy()
    {
        var idProf = _d.Profesional();
        var uProf = _d.UsuarioDeProfesional(idProf);
        DarPermiso(uProf, "profesional");
        var pac1 = _d.Paciente();
        var pac2 = _d.Paciente();
        _d.PacienteProfesional(pac1, idProf);
        _d.PacienteProfesional(pac2, idProf);
        Assert.Equal(HttpStatusCode.OK, (await Ingresar(uProf, modulo: "profesional")).R.StatusCode);

        var r = await _admin.DeleteAsync($"/profesionales/{idProf}");

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal("Inactivo", EstadoFicha("Profesional", "IdProfesional", idProf));
        Assert.Equal("Inactivo", EstadoUsuario(uProf));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Ingresar(uProf, modulo: "profesional")).R.StatusCode);
        // Lo demás sigue como hoy: las asignaciones no se tocan y el permiso del usuario tampoco.
        Assert.Equal(1, Asignaciones(pac1));
        Assert.Equal(1, Asignaciones(pac2));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM UsuarioPermiso WHERE IdUsuario = @u", ("@u", uProf)));
        Assert.Single(Auditoria("Baja Profesional", idProf, "Profesional"));
        Assert.Single(Auditoria("Baja Usuario", uProf));
    }

    [Fact]
    public async Task La_baja_no_toca_usuarios_pendientes_ni_registra_de_nuevo_a_uno_que_ya_estaba_inactivo_ni_falla_si_no_hay_usuario()
    {
        var pacSinUsuario = _d.Paciente();
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/pacientes/{pacSinUsuario}")).StatusCode);
        Assert.Equal("Inactivo", EstadoFicha("Paciente", "IdPaciente", pacSinUsuario));
        Assert.Single(Auditoria("Baja Paciente", pacSinUsuario, "Paciente"));

        var idProf = _d.Profesional();
        var uPendiente = _d.UsuarioDeProfesional(idProf);
        DatosDePrueba.Ejecutar("UPDATE Usuario SET Estado = 'Pendiente' WHERE IdUsuario = @i", ("@i", uPendiente));
        await _admin.DeleteAsync($"/profesionales/{idProf}");
        Assert.Equal("Pendiente", EstadoUsuario(uPendiente));
        Assert.Empty(Auditoria("Baja Usuario", uPendiente));

        var idPac = _d.Paciente();
        var uYaInactivo = _d.UsuarioDePaciente(idPac);
        DatosDePrueba.Ejecutar("UPDATE Usuario SET Estado = 'Inactivo' WHERE IdUsuario = @i", ("@i", uYaInactivo));
        await _admin.DeleteAsync($"/pacientes/{idPac}");
        Assert.Empty(Auditoria("Baja Usuario", uYaInactivo));

        // Un id que no existe sigue respondiendo 204, como antes.
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync("/pacientes/2000000000")).StatusCode);
    }

    [Fact]
    public async Task Dar_de_alta_la_ficha_reactiva_el_usuario_de_forma_simetrica()
    {
        var idPac = _d.Paciente();
        var uPac = _d.UsuarioDePaciente(idPac);
        var idProf = _d.Profesional();
        var uProf = _d.UsuarioDeProfesional(idProf);
        DarPermiso(uProf, "profesional");
        await _admin.DeleteAsync($"/pacientes/{idPac}");
        await _admin.DeleteAsync($"/profesionales/{idProf}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Ingresar(uPac, modulo: "paciente")).R.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsync($"/pacientes/{idPac}/alta", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsync($"/profesionales/{idProf}/alta", null)).StatusCode);

        Assert.Equal(("Activo", "Activo"), (EstadoFicha("Paciente", "IdPaciente", idPac), EstadoUsuario(uPac)));
        Assert.Equal(("Activo", "Activo"), (EstadoFicha("Profesional", "IdProfesional", idProf), EstadoUsuario(uProf)));
        Assert.Equal(HttpStatusCode.OK, (await Ingresar(uPac, modulo: "paciente")).R.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Ingresar(uProf, modulo: "profesional")).R.StatusCode);
        var react = Assert.Single(Auditoria("Reactivación Usuario", uPac));
        Assert.Equal((_idAdmin, "Administrativa"), (react.IdUsuario, react.Categoria));
        Assert.Single(Auditoria("Alta Paciente", idPac, "Paciente"));
        Assert.Single(Auditoria("Alta Profesional", idProf, "Profesional"));
        // Un id que no existe sigue dando 404 y no deja registros.
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PutAsync("/pacientes/2000000000/alta", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PutAsync("/profesionales/2000000000/alta", null)).StatusCode);
    }

    [Fact]
    public async Task Un_usuario_pendiente_no_se_reactiva_con_el_alta_de_la_ficha()
    {
        var idPac = _d.Paciente(estado: "Inactivo");
        var u = _d.UsuarioDePaciente(idPac);
        DatosDePrueba.Ejecutar("UPDATE Usuario SET Estado = 'Pendiente' WHERE IdUsuario = @i", ("@i", u));
        await _admin.PutAsync($"/pacientes/{idPac}/alta", null);
        Assert.Equal("Activo", EstadoFicha("Paciente", "IdPaciente", idPac));
        Assert.Equal("Pendiente", EstadoUsuario(u));
    }

    [Fact]
    public async Task Solo_el_Configurador_da_de_baja_o_de_alta_y_el_resto_no_cambia_nada()
    {
        var idPac = _d.Paciente();
        var uPac = _d.UsuarioDePaciente(idPac);
        var crm = DatosDePrueba.Cliente(_fabrica, _d.Usuario(), Roles.Crm);
        Assert.Equal(HttpStatusCode.Forbidden, (await crm.DeleteAsync($"/pacientes/{idPac}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().DeleteAsync($"/pacientes/{idPac}")).StatusCode);
        Assert.Equal(("Activo", "Activo"), (EstadoFicha("Paciente", "IdPaciente", idPac), EstadoUsuario(uPac)));
    }

    // ═════════════ Autorización de todo lo nuevo ═════════════

    public static IEnumerable<object[]> Rutas() => new[]
    {
        new object[] { "GET", "/usuarios/lista" },
        new object[] { "POST", "/usuarios/{u}/restablecer-password" },
        new object[] { "GET", "/usuarios/pacientes/{p}/profesionales" },
        new object[] { "POST", "/usuarios/pacientes/{p}/profesionales" },
        new object[] { "DELETE", "/usuarios/pacientes/{p}/profesionales" },
    };

    private HttpRequestMessage Pedido(string metodo, string ruta, int idUsuario, int idPaciente) =>
        new(new HttpMethod(metodo), ruta.Replace("{u}", idUsuario.ToString()).Replace("{p}", idPaciente.ToString()))
        { Content = metodo == "GET" ? null : JsonContent.Create(new { idProfesionales = new[] { 1 } }) };

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task CRM_Profesional_Paciente_y_otros_modulos_reciben_403(string metodo, string ruta)
    {
        var objetivo = _d.Usuario();
        var pac = _d.Paciente();
        var emisor = _fabrica.Services.GetRequiredService<JwtEmisorService>();
        var tokens = new Dictionary<string, string>
        {
            ["crm"] = emisor.Emitir(_d.Usuario(), Roles.Crm),
            ["profesional"] = emisor.Emitir(_d.Usuario(), Roles.Profesional, idProfesional: _d.Profesional()),
            ["auditor"] = emisor.Emitir(_d.Usuario(), Roles.Auditor),
            ["paciente"] = emisor.Emitir(_d.Usuario(), Roles.Paciente, idPaciente: pac),
            ["facturacion"] = emisor.Emitir(_d.Usuario(), Roles.Facturacion),
        };
        foreach (var (nombre, token) in tokens)
        {
            var r = await DatosDePrueba.Cliente(_fabrica, token).SendAsync(Pedido(metodo, ruta, objetivo, pac));
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"{nombre}: {metodo} {ruta} dio {(int)r.StatusCode}");
        }
        Assert.Equal(0, Asignaciones(pac));
        Assert.False(Estado(objetivo).Debe);
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task Sin_sesion_o_con_un_token_que_no_es_de_trabajo_reciben_401(string metodo, string ruta)
    {
        var objetivo = _d.Usuario();
        var pac = _d.Paciente();
        var emisor = _fabrica.Services.GetRequiredService<JwtEmisorService>();
        var clientes = new[]
        {
            _fabrica.CreateClient(),
            DatosDePrueba.Cliente(_fabrica, emisor.EmitirSeleccion(_idAdmin)),
            DatosDePrueba.Cliente(_fabrica, emisor.EmitirCambioClave(_idAdmin)),
        };
        foreach (var c in clientes)
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Pedido(metodo, ruta, objetivo, pac))).StatusCode);
        Assert.False(Estado(objetivo).Debe);
    }
}
