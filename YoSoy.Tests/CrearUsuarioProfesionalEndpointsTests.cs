using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// POST /profesionales/{id}/usuario y GET /profesionales/sin-usuario contra una base real (la de Conexion.cs).
// Cada prueba crea sus propias filas (especialidad, profesional, un usuario "admin" para el JWT) y las borra al
// final: no depende de lo que haya cargado en la base.
[Trait("Categoria", "Integracion")]
public class CrearUsuarioProfesionalEndpointsTests
{
    private sealed class Mundo : IDisposable
    {
        public string Marca { get; } = "xtest" + Guid.NewGuid().ToString("N")[..10];
        public int IdEspecialidad { get; }
        public int IdAdmin { get; }
        public int IdProfesional { get; }
        public WebApplicationFactory<Program> Factory { get; }

        public Mundo(string estadoProfesional = "Activo")
        {
            IdEspecialidad = Escalar("INSERT INTO Especialidad (Nombre, Estado) VALUES (@a, N'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@a", Marca));
            IdAdmin = Escalar("INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@a, N'0', N'Activo', @m); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                ("@a", Marca + "adm"), ("@m", Marca + "adm@example.com"));
            IdProfesional = NuevoProfesional(estadoProfesional, "A");
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
                b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Password"] = " " })));
        }

        public int NuevoProfesional(string estado, string sufijo) => Escalar(
            @"INSERT INTO Profesional (Nombre, Apellido, Matricula, IdEspecialidad, Estado, Mail)
              VALUES (N'Prueba', @ap, @mat, @esp, @est, @mail); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@ap", Marca), ("@mat", "MP" + Marca + sufijo), ("@esp", IdEspecialidad), ("@est", estado),
            ("@mail", $"{Marca}{sufijo}@example.com".ToLowerInvariant()));

        public HttpClient Cliente(string? rol = null, int? idUsuario = null, int? idProfesional = null)
        {
            var c = Factory.CreateClient();
            if (rol is null) return c;
            var jwt = Factory.Services.GetRequiredService<JwtEmisorService>();
            var token = idProfesional is null
                ? jwt.Emitir(idUsuario ?? IdAdmin, rol)
                : jwt.Emitir(idUsuario ?? IdAdmin, rol, idProfesional: idProfesional);
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return c;
        }

        public HttpClient Configurador() => Cliente(Roles.Configurador);

        public int Cuenta(string sql, params (string, object)[] p) => Escalar(sql, p);

        public void Dispose()
        {
            Factory.Dispose();
            // La auditoría es de solo agregar (migración 19): lo que la API deja en LogAuditoria queda, como en producción.
            // El permiso "profesional" que recibe el usuario creado (etapa 2 de roles) cuelga del usuario: va antes.
            Ejecutar("DELETE FROM UsuarioPrivilegio WHERE IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Username LIKE @m)", ("@m", Marca + "%"));
            Ejecutar("DELETE FROM UsuarioPermiso WHERE IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Username LIKE @m)", ("@m", Marca + "%"));
            Ejecutar("DELETE FROM UsuarioRol WHERE IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Username LIKE @m)", ("@m", Marca + "%"));
            Ejecutar("DELETE FROM UsuarioProfesional WHERE IdProfesional IN (SELECT IdProfesional FROM Profesional WHERE Apellido = @m)", ("@m", Marca));
            // El administrador de la prueba actuó (figura en LogAuditoria, de solo agregar): ese usuario no se puede borrar y queda con su marca.
            Ejecutar("DELETE FROM Usuario WHERE Username LIKE @m AND NOT EXISTS (SELECT 1 FROM LogAuditoria l WHERE l.IdUsuario = Usuario.IdUsuario)", ("@m", Marca + "%"));
            Ejecutar("DELETE FROM Profesional WHERE Apellido = @m", ("@m", Marca));
            Ejecutar("DELETE FROM Especialidad WHERE IdEspecialidad = @i", ("@i", IdEspecialidad));
        }
    }

    private static int Escalar(string sql, params (string, object)[] parametros)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        return (int)cmd.ExecuteScalar()!;
    }

    private static void Ejecutar(string sql, params (string, object)[] parametros)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    [Fact]
    public async Task Crea_el_usuario_con_su_vinculo_y_la_auditoria_y_el_profesional_puede_entrar()
    {
        using var m = new Mundo();
        var username = m.Marca + "u";
        using var admin = m.Configurador();

        var r = await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", new { username, generarContrasena = true });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Contains("no-store", r.Headers.CacheControl?.ToString());
        var cuerpo = await Json(r);
        var temporal = cuerpo.GetProperty("contrasenaTemporal").GetString()!;
        var idUsuario = cuerpo.GetProperty("idUsuario").GetInt32();
        Assert.Equal(14, temporal.Length);
        Assert.Equal(username, cuerpo.GetProperty("username").GetString());
        Assert.Equal($"{m.Marca}A@example.com".ToLowerInvariant(), cuerpo.GetProperty("mail").GetString());

        // Usuario Activo con el hash de la temporal, y su vínculo
        Assert.Equal(1, m.Cuenta("SELECT COUNT(*) FROM Usuario WHERE IdUsuario = @i AND Estado = N'Activo' AND PasswordHash = @h",
            ("@i", idUsuario), ("@h", Sha256(temporal))));
        Assert.Equal(1, m.Cuenta("SELECT COUNT(*) FROM UsuarioProfesional WHERE IdUsuario = @i AND IdProfesional = @p",
            ("@i", idUsuario), ("@p", m.IdProfesional)));

        // Auditoría: las dos acciones, a nombre de quien la hizo y sin la contraseña en ningún detalle
        Assert.Equal(1, m.Cuenta("SELECT COUNT(*) FROM LogAuditoria WHERE Accion = N'Alta Usuario' AND IdEntidad = @i AND IdUsuario = @a AND Detalle = @u",
            ("@i", idUsuario), ("@a", m.IdAdmin), ("@u", username)));
        Assert.Equal(1, m.Cuenta("SELECT COUNT(*) FROM LogAuditoria WHERE Accion = N'Vinculación Usuario-Profesional' AND IdEntidad = @p AND IdUsuario = @a",
            ("@p", m.IdProfesional), ("@a", m.IdAdmin)));
        Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM LogAuditoria WHERE Detalle LIKE @t", ("@t", "%" + temporal + "%")));

        // Y el profesional entra con esa contraseña, como profesional de SU ficha
        using var anonimo = m.Cliente();
        var login = await anonimo.PostAsJsonAsync("/login", new { username, password = temporal });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var sesion = await Json(login);
        Assert.Equal("profesional", sesion.GetProperty("rol").GetString());
        Assert.Equal(m.IdProfesional, sesion.GetProperty("idProfesional").GetInt32());
    }

    [Fact]
    public async Task Con_una_contrasena_escrita_no_se_devuelve_y_el_profesional_entra_con_ella()
    {
        using var m = new Mundo();
        var username = m.Marca + "u";
        using var admin = m.Configurador();

        var r = await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario",
            new { username, contrasena = "ClaveEscrita123", mail = $"{m.Marca}.otro@example.com".ToLowerInvariant() });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await Json(r);
        Assert.Equal(JsonValueKind.Null, cuerpo.GetProperty("contrasenaTemporal").ValueKind);
        Assert.Equal($"{m.Marca}.otro@example.com".ToLowerInvariant(), cuerpo.GetProperty("mail").GetString());

        using var anonimo = m.Cliente();
        var login = await anonimo.PostAsJsonAsync("/login", new { username, password = "ClaveEscrita123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Un_segundo_pedido_para_el_mismo_profesional_es_409_y_no_crea_otro_usuario()
    {
        using var m = new Mundo();
        using var admin = m.Configurador();

        var primero = await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", new { username = m.Marca + "u1", contrasena = "ClaveEscrita123" });
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);

        var segundo = await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", new { username = m.Marca + "u2", contrasena = "ClaveEscrita123" });

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.Contains("ya tiene un usuario", (await Json(segundo)).GetProperty("mensaje").GetString());
        Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM Usuario WHERE Username = @u", ("@u", m.Marca + "u2")));
        Assert.Equal(1, m.Cuenta("SELECT COUNT(*) FROM UsuarioProfesional WHERE IdProfesional = @p", ("@p", m.IdProfesional)));
    }

    [Fact]
    public async Task Usuario_o_mail_repetidos_son_409_y_no_dejan_ninguna_fila_creada()
    {
        using var m = new Mundo();
        var otro = m.NuevoProfesional("Activo", "B");
        using var admin = m.Configurador();

        var base1 = await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", new { username = m.Marca + "u1", contrasena = "ClaveEscrita123" });
        Assert.Equal(HttpStatusCode.Created, base1.StatusCode);

        // Mismo nombre de usuario (aunque cambien las mayúsculas)
        var usuarioRepetido = await admin.PostAsJsonAsync($"/profesionales/{otro}/usuario",
            new { username = (m.Marca + "u1").ToUpperInvariant(), contrasena = "ClaveEscrita123" });
        Assert.Equal(HttpStatusCode.Conflict, usuarioRepetido.StatusCode);
        Assert.Contains("nombre de usuario", (await Json(usuarioRepetido)).GetProperty("mensaje").GetString());

        // Mismo mail: el del primer profesional
        var mailRepetido = await admin.PostAsJsonAsync($"/profesionales/{otro}/usuario",
            new { username = m.Marca + "u3", contrasena = "ClaveEscrita123", mail = $"{m.Marca}A@example.com".ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, mailRepetido.StatusCode);
        Assert.Contains("mail", (await Json(mailRepetido)).GetProperty("mensaje").GetString());

        Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM UsuarioProfesional WHERE IdProfesional = @p", ("@p", otro)));
        Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM Usuario WHERE Username = @u", ("@u", m.Marca + "u3")));
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Rechazado")]
    [InlineData("Inactivo")]
    public async Task Un_profesional_que_no_esta_activo_es_409_y_no_se_crea_nada(string estado)
    {
        using var m = new Mundo(estado);
        using var admin = m.Configurador();

        var r = await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", new { username = m.Marca + "u", contrasena = "ClaveEscrita123" });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM Usuario WHERE Username = @u", ("@u", m.Marca + "u")));
    }

    [Fact]
    public async Task Un_profesional_inexistente_es_404()
    {
        using var m = new Mundo();
        using var admin = m.Configurador();
        var r = await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional + 1_000_000}/usuario", new { username = m.Marca + "u", contrasena = "ClaveEscrita123" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Una_contrasena_corta_es_400_y_no_se_crea_nada()
    {
        using var m = new Mundo();
        using var admin = m.Configurador();
        var r = await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", new { username = m.Marca + "u", contrasena = "corta12" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM Usuario WHERE Username = @u", ("@u", m.Marca + "u")));
    }

    [Fact]
    public async Task Solo_el_Configurador_puede()
    {
        using var m = new Mundo();
        var cuerpo = new { username = m.Marca + "u", contrasena = "ClaveEscrita123" };

        using var anonimo = m.Cliente();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", cuerpo)).StatusCode);

        using var profesional = m.Cliente(Roles.Profesional, m.IdAdmin, m.IdProfesional);
        Assert.Equal(HttpStatusCode.Forbidden, (await profesional.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await profesional.GetAsync("/profesionales/sin-usuario")).StatusCode);

        Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM Usuario WHERE Username = @u", ("@u", m.Marca + "u")));
    }

    [Fact]
    public async Task Sin_usuario_lista_a_los_activos_sin_vinculo_y_deja_de_listar_al_que_ya_lo_tiene()
    {
        using var m = new Mundo();
        var pendiente = m.NuevoProfesional("Pendiente", "P");
        using var admin = m.Configurador();

        var antes = (await Json(await admin.GetAsync("/profesionales/sin-usuario"))).EnumerateArray().Select(e => e.GetInt32()).ToList();
        Assert.Contains(m.IdProfesional, antes);
        Assert.DoesNotContain(pendiente, antes);          // no está Activo

        await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", new { username = m.Marca + "u", contrasena = "ClaveEscrita123" });

        var despues = (await Json(await admin.GetAsync("/profesionales/sin-usuario"))).EnumerateArray().Select(e => e.GetInt32()).ToList();
        Assert.DoesNotContain(m.IdProfesional, despues);
    }

    [Fact]
    public async Task Si_falla_la_creacion_del_vinculo_no_queda_el_usuario_creado()
    {
        // El Usuario se inserta ANTES que el vínculo. Se hace fallar el segundo INSERT con un trigger temporal
        // (solo para el profesional de esta prueba) y se comprueba que el primero se deshace: sin esto, quedaría
        // un Usuario sin vínculo, que POST /login trata como configurador.
        using var m = new Mundo();
        var username = m.Marca + "u";
        var trigger = "trg_" + m.Marca;
        Ejecutar($@"CREATE TRIGGER dbo.[{trigger}] ON dbo.UsuarioProfesional AFTER INSERT AS
                    IF EXISTS (SELECT 1 FROM inserted WHERE IdProfesional = {m.IdProfesional}) THROW 50001, N'falla simulada', 1;");
        try
        {
            using var admin = m.Configurador();
            HttpStatusCode? status = null;
            try { status = (await admin.PostAsJsonAsync($"/profesionales/{m.IdProfesional}/usuario", new { username, contrasena = "ClaveEscrita123" })).StatusCode; }
            catch (Exception) { /* el servidor de prueba puede propagar la excepción en vez de responder 500 */ }
            Assert.NotEqual(HttpStatusCode.Created, status);

            Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM Usuario WHERE Username = @u", ("@u", username)));
            Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM UsuarioProfesional WHERE IdProfesional = @p", ("@p", m.IdProfesional)));
            Assert.Equal(0, m.Cuenta("SELECT COUNT(*) FROM LogAuditoria WHERE Accion = N'Alta Usuario' AND Detalle = @u", ("@u", username)));
        }
        finally
        {
            Ejecutar($"DROP TRIGGER IF EXISTS dbo.[{trigger}]");
        }
    }
}
