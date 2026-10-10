using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

// Recuperación de contraseña autoservicio: reemplaza la aprobación manual del Configurador
// (token de un solo uso por mail, vence a los 30 minutos, respuesta siempre genérica).
// Corre contra la base local real; cada prueba crea su propio usuario y lo borra al final.
public class RecuperacionPasswordTests
{
    private static WebApplicationFactory<Program> FactorySinMailReal() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Password"] = " " })));

    private static async Task<(int idUsuario, string username)> CrearUsuarioDePrueba(SqlConnection con, string? mail)
    {
        var marca = "xtest" + Guid.NewGuid().ToString("N")[..10];
        var hashInicial = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("ClaveOriginal123")));
        using var cmd = new SqlCommand(
            "INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@U, @H, 'Activo', @M); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@U", marca);
        cmd.Parameters.AddWithValue("@H", hashInicial);
        cmd.Parameters.AddWithValue("@M", (object?)mail ?? DBNull.Value);
        var id = (int)(await cmd.ExecuteScalarAsync())!;
        return (id, marca);
    }

    private static async Task BorrarUsuarioDePrueba(SqlConnection con, int idUsuario)
    {
        using (var d1 = new SqlCommand("DELETE FROM RecuperacionPassword WHERE IdUsuario = @Id", con))
        { d1.Parameters.AddWithValue("@Id", idUsuario); await d1.ExecuteNonQueryAsync(); }
        using (var d2 = new SqlCommand("DELETE FROM Usuario WHERE IdUsuario = @Id", con))
        { d2.Parameters.AddWithValue("@Id", idUsuario); await d2.ExecuteNonQueryAsync(); }
    }

    [Fact]
    public async Task Responde_igual_exista_o_no_el_usuario()
    {
        using var factory = FactorySinMailReal();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var (idUsuario, username) = await CrearUsuarioDePrueba(con, "xtest@example.com");
        try
        {
            using var client = factory.CreateClient();
            var r1 = await client.PostAsJsonAsync("/recuperacion-password", new { username });
            var r2 = await client.PostAsJsonAsync("/recuperacion-password", new { username = "no-existe-" + Guid.NewGuid().ToString("N")[..8] });

            Assert.Equal(r1.StatusCode, r2.StatusCode);
            var cuerpo1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
            var cuerpo2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(cuerpo1.GetProperty("mensaje").GetString(), cuerpo2.GetProperty("mensaje").GetString());
        }
        finally { await BorrarUsuarioDePrueba(con, idUsuario); }
    }

    [Fact]
    public async Task Sin_mail_responde_igual_pero_no_crea_ningun_token()
    {
        using var factory = FactorySinMailReal();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var (idUsuario, username) = await CrearUsuarioDePrueba(con, null);
        try
        {
            using var client = factory.CreateClient();
            var r = await client.PostAsJsonAsync("/recuperacion-password", new { username });
            Assert.True(r.IsSuccessStatusCode);

            using var cmd = new SqlCommand("SELECT COUNT(*) FROM RecuperacionPassword WHERE IdUsuario = @Id", con);
            cmd.Parameters.AddWithValue("@Id", idUsuario);
            Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
        }
        finally { await BorrarUsuarioDePrueba(con, idUsuario); }
    }

    [Fact]
    public async Task Token_valido_cambia_la_contrasena_y_sirve_una_sola_vez()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var (idUsuario, _) = await CrearUsuarioDePrueba(con, "xtest@example.com");
        try
        {
            var tokenCrudo = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tokenCrudo)));
            new RecuperacionPasswordDAO().CrearToken(idUsuario, tokenHash, DateTime.Now.AddMinutes(30));

            using var client = factory.CreateClient();
            var r1 = await client.PostAsJsonAsync("/recuperacion-password/reset", new { token = tokenCrudo, nuevaPassword = "NuevaClave456" });
            Assert.True(r1.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)r1.StatusCode}");

            var hashEsperado = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("NuevaClave456")));
            using var cmd = new SqlCommand("SELECT PasswordHash FROM Usuario WHERE IdUsuario = @Id", con);
            cmd.Parameters.AddWithValue("@Id", idUsuario);
            Assert.Equal(hashEsperado, (string)(await cmd.ExecuteScalarAsync())!);

            // Un solo uso: reintentar el mismo token tiene que fallar, no volver a cambiar nada.
            var r2 = await client.PostAsJsonAsync("/recuperacion-password/reset", new { token = tokenCrudo, nuevaPassword = "OtraClave789" });
            Assert.False(r2.IsSuccessStatusCode);
        }
        finally { await BorrarUsuarioDePrueba(con, idUsuario); }
    }

    [Fact]
    public async Task Token_vencido_no_sirve()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var (idUsuario, _) = await CrearUsuarioDePrueba(con, "xtest@example.com");
        try
        {
            var tokenCrudo = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tokenCrudo)));
            new RecuperacionPasswordDAO().CrearToken(idUsuario, tokenHash, DateTime.Now.AddMinutes(-5)); // ya vencido

            using var client = factory.CreateClient();
            var r = await client.PostAsJsonAsync("/recuperacion-password/reset", new { token = tokenCrudo, nuevaPassword = "NuevaClave456" });
            Assert.False(r.IsSuccessStatusCode);
        }
        finally { await BorrarUsuarioDePrueba(con, idUsuario); }
    }

    [Fact]
    public async Task Pedir_un_token_nuevo_invalida_el_anterior()
    {
        using var factory = FactorySinMailReal();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var (idUsuario, username) = await CrearUsuarioDePrueba(con, "xtest@example.com");
        try
        {
            var token1 = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var hash1 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token1)));
            new RecuperacionPasswordDAO().CrearToken(idUsuario, hash1, DateTime.Now.AddMinutes(30));

            using var client = factory.CreateClient();
            await client.PostAsJsonAsync("/recuperacion-password", new { username }); // pide uno nuevo

            using var cmd = new SqlCommand("SELECT Estado FROM RecuperacionPassword WHERE Token = @T", con);
            cmd.Parameters.AddWithValue("@T", hash1);
            Assert.Equal("Reemplazada", (string)(await cmd.ExecuteScalarAsync())!);
        }
        finally { await BorrarUsuarioDePrueba(con, idUsuario); }
    }

    [Fact]
    public async Task Despues_de_tres_pedidos_recientes_no_crea_mas_filas()
    {
        using var factory = FactorySinMailReal();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var (idUsuario, username) = await CrearUsuarioDePrueba(con, "xtest@example.com");
        try
        {
            using var client = factory.CreateClient();
            for (int i = 0; i < 4; i++)
                await client.PostAsJsonAsync("/recuperacion-password", new { username });

            using var cmd = new SqlCommand("SELECT COUNT(*) FROM RecuperacionPassword WHERE IdUsuario = @Id", con);
            cmd.Parameters.AddWithValue("@Id", idUsuario);
            Assert.Equal(3, (int)(await cmd.ExecuteScalarAsync())!);
        }
        finally { await BorrarUsuarioDePrueba(con, idUsuario); }
    }

    [Fact]
    public async Task Los_endpoints_viejos_de_aprobacion_manual_ya_no_existen()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PutAsync("/recuperacion-password/1/aceptar", null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PutAsync("/recuperacion-password/1/rechazar", null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/recuperacion-password/pendientes")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/recuperacion-password/estado/cualquiera")).StatusCode);
    }
}
