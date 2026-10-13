using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// fix-ids-cero-endpoints, Grupo 2 (personas): los altas de Tutor, Paciente y Usuario
// devuelven el id en 0 porque sus DAOs.Insertar son void y nunca leen el id generado.
public class IdsCeroPersonasTests
{
    private static HttpClient ClienteCon(WebApplicationFactory<Program> factory, int idUsuario, string rol)
    {
        var c = factory.CreateClient();
        var token = factory.Services.GetRequiredService<JwtEmisorService>().Emitir(idUsuario, rol);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Tutor_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteCon(factory, 1, Roles.Configurador);

        var respuesta = await client.PostAsJsonAsync("/tutores", new { nombre = marca, dni = marca, vinculo = "Padre", contacto = "1100000000" });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idTutor").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdTutor FROM Tutor WHERE Dni = @m", con);
            cmd.Parameters.AddWithValue("@m", marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idTutor en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using (var del = new SqlCommand("DELETE FROM Tutor WHERE Dni = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using var chk = new SqlCommand("SELECT COUNT(*) FROM Tutor WHERE Dni = @m", con);
            chk.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Paciente_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        var mail = marca.ToLowerInvariant() + "@example.com"; // único por corrida: ahora también es Usuario.Mail (UNIQUE).
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(); // POST /pacientes es público (registro), sin token.

        var respuesta = await client.PostAsJsonAsync("/pacientes", new { nombre = "Prueba", dni = marca, mail, usuario = marca, contrasena = "ClaveDePrueba123" });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idPaciente").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdPaciente FROM Paciente WHERE Dni = @m", con);
            cmd.Parameters.AddWithValue("@m", marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idPaciente en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using (var del = new SqlCommand(
                "DELETE FROM UsuarioPaciente WHERE IdPaciente IN (SELECT IdPaciente FROM Paciente WHERE Dni = @m)", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using (var del = new SqlCommand("DELETE FROM Usuario WHERE Username = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using (var del = new SqlCommand("DELETE FROM Paciente WHERE Dni = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using var chk = new SqlCommand("SELECT COUNT(*) FROM Paciente WHERE Dni = @m", con);
            chk.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Usuario_alta_devuelve_id_real_no_cero()
    {
        var marca = "xtest" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteCon(factory, 1, Roles.Configurador);

        var respuesta = await client.PostAsJsonAsync("/usuarios", new { username = marca, passwordHash = "ClaveTemporal123", mail = marca + "@example.com" });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idUsuario").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdUsuario FROM Usuario WHERE Username = @m", con);
            cmd.Parameters.AddWithValue("@m", marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idUsuario en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using (var del = new SqlCommand("DELETE FROM LogAuditoria WHERE Entidad = 'Usuario' AND Detalle = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using (var del = new SqlCommand("DELETE FROM Usuario WHERE Username = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using var chk = new SqlCommand("SELECT COUNT(*) FROM Usuario WHERE Username = @m", con);
            chk.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
        }
    }
}
