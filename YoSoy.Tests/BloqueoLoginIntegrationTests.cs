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
using Microsoft.Extensions.DependencyInjection.Extensions;

// Bloqueo del login contra una base real (la de Conexion.cs): el UPDATE de RegistrarIntentoFallido tiene que
// hacer lo mismo que PoliticaBloqueoLogin, y el flujo de POST /login (con un reloj controlado) tiene que
// desbloquear al vencer el bloqueo, después de un login exitoso y después de cambiar la contraseña.
// Cada prueba crea sus propias filas de Usuario y las borra al final.
[Trait("Categoria", "Integracion")]
public class BloqueoLoginIntegrationTests
{
    private const string Clave = "ClaveDePrueba123";
    private static readonly string HashClave = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Clave)));

    // Fecha fija y sin fracciones (datetime de SQL Server redondea a 3,33 ms).
    private static readonly DateTime Ahora = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Usuarios : IDisposable
    {
        private readonly List<string> _nombres = new();
        public string Nuevo(int intentos = 0, DateTime? bloqueadoHasta = null, string hash = "")
        {
            var username = "xtest" + Guid.NewGuid().ToString("N")[..12];
            _nombres.Add(username);
            Ejecutar(@"INSERT INTO Usuario (Username, PasswordHash, Estado, Mail, IntentosFallidos, BloqueadoHasta)
                       VALUES (@u, @h, N'Activo', @m, @i, @b)",
                ("@u", username), ("@h", hash.Length > 0 ? hash : HashClave), ("@m", username + "@example.com"),
                ("@i", intentos), ("@b", (object?)bloqueadoHasta ?? DBNull.Value));
            // Etapa 2 de roles: el equipo ingresa por los permisos que tiene asignados; un usuario sin ninguno recibe 403 SIN_PERMISOS (después de comprobar la
            // contraseña y de poner los intentos en cero). Estas pruebas son del bloqueo, no del acceso: el usuario tiene el permiso de Configurador.
            Ejecutar(@"INSERT INTO UsuarioRol (IdUsuario, IdRol) SELECT u.IdUsuario, p.IdRol FROM Usuario u, Permiso p WHERE u.Username = @u AND p.Clave = 'configurador';
                       INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) SELECT u.IdUsuario, p.IdRol, p.IdPermiso FROM Usuario u, Permiso p WHERE u.Username = @u AND p.Clave = 'configurador'",
                ("@u", username));
            return username;
        }

        public int Id(string username) =>
            (int)Escalar("SELECT IdUsuario FROM Usuario WHERE Username = @u", ("@u", username))!;

        public (int Intentos, DateTime? BloqueadoHasta) Estado(string username)
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IntentosFallidos, BloqueadoHasta FROM Usuario WHERE Username = @u", con);
            cmd.Parameters.AddWithValue("@u", username);
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            return ((int)r["IntentosFallidos"], r["BloqueadoHasta"] == DBNull.Value ? null : (DateTime?)r["BloqueadoHasta"]);
        }

        public void Dispose()
        {
            // Cada ingreso (y cada bloqueo) queda en la auditoría, que es de solo agregar (migración 19): el usuario que ingresó no se puede borrar y queda
            // con su marca en la base de pruebas.
            foreach (var n in _nombres) Ejecutar("DELETE FROM UsuarioPermiso WHERE IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Username = @u); DELETE FROM UsuarioRol WHERE IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Username = @u)", ("@u", n));
            foreach (var n in _nombres) Ejecutar("DELETE FROM Usuario WHERE Username = @u AND NOT EXISTS (SELECT 1 FROM LogAuditoria l WHERE l.IdUsuario = Usuario.IdUsuario)", ("@u", n));
        }
    }

    private static object? Escalar(string sql, params (string, object)[] p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }

    private static void Ejecutar(string sql, params (string, object)[] p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static WebApplicationFactory<Program> Fabrica(RelojFijo reloj) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Password"] = " " }));
            b.ConfigureServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(reloj);
            });
        });

    private static async Task<HttpResponseMessage> Login(HttpClient c, string username, string password) =>
        await c.PostAsJsonAsync("/login", new { username, password });

    private static async Task<string?> Mensaje(HttpResponseMessage r)
    {
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("mensaje", out var m) ? m.GetString() : null;
    }

    // ── El UPDATE de SQL == la política en C# ────────────────────────────────────────────────

    public static IEnumerable<object?[]> Escenarios() => new[]
    {
        new object?[] { 0, null },          // sin errores
        new object?[] { 1, null },
        new object?[] { 2, null },          // el próximo bloquea
        new object?[] { 3, 10 },            // bloqueo vigente (no debería llegar por el login, pero se cruzan pedidos)
        new object?[] { 3, -1 },            // venció hace un minuto
        new object?[] { 3, 0 },             // vence justo ahora
        new object?[] { 7, -600 },          // contador viejo que venía creciendo
        new object?[] { 5, null },          // fila vieja: contador alto y sin fecha
    };

    [Theory]
    [MemberData(nameof(Escenarios))]
    public void El_update_de_sql_hace_lo_mismo_que_la_politica(int intentos, int? bloqueoEnMinutos)
    {
        using var usuarios = new Usuarios();
        DateTime? bloqueadoHasta = bloqueoEnMinutos is null ? null : Ahora.AddMinutes(bloqueoEnMinutos.Value);
        var username = usuarios.Nuevo(intentos, bloqueadoHasta);

        var esperado = PoliticaBloqueoLogin.TrasIntentoFallido(intentos, bloqueadoHasta, Ahora);
        var devuelto = new UsuarioDAO().RegistrarIntentoFallido(usuarios.Id(username), Ahora);
        var guardado = usuarios.Estado(username);

        Assert.Equal(esperado.IntentosFallidos, devuelto.IntentosFallidos);
        Assert.Equal(esperado.BloqueadoHasta, devuelto.BloqueadoHasta);
        Assert.Equal(esperado.IntentosFallidos, guardado.Intentos);
        Assert.Equal(esperado.BloqueadoHasta, guardado.BloqueadoHasta);
    }

    // ── El flujo de /login ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Vencido_el_bloqueo_un_error_no_vuelve_a_bloquear_y_la_clave_correcta_deja_todo_en_cero()
    {
        using var usuarios = new Usuarios();
        var username = usuarios.Nuevo();
        var reloj = new RelojFijo(Ahora);
        using var fabrica = Fabrica(reloj);
        using var cliente = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(cliente, username, "mal")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(cliente, username, "mal")).StatusCode);
        var tercero = await Login(cliente, username, "mal");
        Assert.Equal((HttpStatusCode)423, tercero.StatusCode);
        Assert.Equal("Cuenta bloqueada temporalmente. Intentá en 15 minutos.", await Mensaje(tercero));

        // Mientras dura, ni la clave correcta entra, y el mensaje dice cuánto falta.
        reloj.Avanzar(TimeSpan.FromMinutes(10));
        var durante = await Login(cliente, username, Clave);
        Assert.Equal((HttpStatusCode)423, durante.StatusCode);
        Assert.Contains("5 minutos", await Mensaje(durante));

        // Vence: UN error no vuelve a bloquear (antes sí, para siempre) y el conteo arranca en 1.
        reloj.Avanzar(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(cliente, username, "mal")).StatusCode);
        Assert.Equal((1, (DateTime?)null), usuarios.Estado(username));

        // Con dos más sí vuelve a bloquear: el umbral sigue siendo 3.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(cliente, username, "mal")).StatusCode);
        Assert.Equal((HttpStatusCode)423, (await Login(cliente, username, "mal")).StatusCode);

        // Vence otra vez: la clave correcta entra y deja el conteo en cero.
        reloj.Avanzar(TimeSpan.FromMinutes(16));
        var bien = await Login(cliente, username, Clave);
        Assert.Equal(HttpStatusCode.OK, bien.StatusCode);
        Assert.Equal((0, (DateTime?)null), usuarios.Estado(username));
    }

    [Fact]
    public async Task Un_login_exitoso_pone_en_cero_los_errores_sin_bloqueo()
    {
        using var usuarios = new Usuarios();
        var username = usuarios.Nuevo();
        using var fabrica = Fabrica(new RelojFijo(Ahora));
        using var cliente = fabrica.CreateClient();

        await Login(cliente, username, "mal");
        await Login(cliente, username, "mal");
        Assert.Equal(2, usuarios.Estado(username).Intentos);

        Assert.Equal(HttpStatusCode.OK, (await Login(cliente, username, Clave)).StatusCode);
        Assert.Equal((0, (DateTime?)null), usuarios.Estado(username));

        // Y otra vez hacen falta tres para bloquear.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(cliente, username, "mal")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(cliente, username, "mal")).StatusCode);
        Assert.Equal((HttpStatusCode)423, (await Login(cliente, username, "mal")).StatusCode);
    }

    [Fact]
    public async Task Un_bloqueo_guardado_por_una_version_anterior_vence_con_el_mismo_criterio()
    {
        // Fila "vieja": 6 errores acumulados y el bloqueo ya vencido. Antes, el próximo error bloqueaba de nuevo.
        using var usuarios = new Usuarios();
        var username = usuarios.Nuevo(intentos: 6, bloqueadoHasta: Ahora.AddMinutes(-30));
        using var fabrica = Fabrica(new RelojFijo(Ahora));
        using var cliente = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(cliente, username, "mal")).StatusCode);
        Assert.Equal((1, (DateTime?)null), usuarios.Estado(username));
    }

    [Fact]
    public async Task El_Configurador_que_le_pone_una_clave_nueva_a_un_usuario_bloqueado_lo_desbloquea()
    {
        using var usuarios = new Usuarios();
        var admin = usuarios.Nuevo();
        var bloqueado = usuarios.Nuevo(intentos: 3, bloqueadoHasta: Ahora.AddMinutes(10));
        using var fabrica = Fabrica(new RelojFijo(Ahora));
        using var cliente = fabrica.CreateClient();
        Assert.Equal((HttpStatusCode)423, (await Login(cliente, bloqueado, Clave)).StatusCode);

        using var configurador = fabrica.CreateClient();
        var token = fabrica.Services.GetRequiredService<JwtEmisorService>().Emitir(usuarios.Id(admin), Roles.Configurador);
        configurador.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var put = await configurador.PutAsJsonAsync($"/usuarios/{usuarios.Id(bloqueado)}", new
        {
            username = bloqueado, passwordHash = "NuevaClave456", estado = "Activo", mail = bloqueado + "@example.com"
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal((0, (DateTime?)null), usuarios.Estado(bloqueado));

        Assert.Equal(HttpStatusCode.OK, (await Login(cliente, bloqueado, "NuevaClave456")).StatusCode);
    }
}
