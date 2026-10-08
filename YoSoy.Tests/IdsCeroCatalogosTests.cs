using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// fix-ids-cero-endpoints, Grupo 1 (catálogos): los altas de Especialidad, Rol, EstadoTurno,
// TipoConsentimiento y ObraSocial devuelven el id en 0 porque sus DAOs.Insertar son void y
// nunca leen el id generado. Cada prueba crea una fila marcada, confirma el id real en la
// respuesta y la borra al final (verifica 0 filas colgadas).
public class IdsCeroCatalogosTests
{
    private static HttpClient ClienteConfigurador(WebApplicationFactory<Program> factory)
    {
        var c = factory.CreateClient();
        var token = factory.Services.GetRequiredService<JwtEmisorService>().Emitir(1, Roles.Configurador);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Especialidad_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/especialidades", new { nombre = marca });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idEspecialidad").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdEspecialidad FROM Especialidad WHERE Nombre = @m", con);
            cmd.Parameters.AddWithValue("@m", marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idEspecialidad en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using (var del = new SqlCommand("DELETE FROM Especialidad WHERE Nombre = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using var chk = new SqlCommand("SELECT COUNT(*) FROM Especialidad WHERE Nombre = @m", con);
            chk.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Rol_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/roles", new { nombre = marca });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idRol").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdRol FROM Rol WHERE Nombre = @m", con);
            cmd.Parameters.AddWithValue("@m", marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idRol en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using (var del = new SqlCommand("DELETE FROM Rol WHERE Nombre = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using var chk = new SqlCommand("SELECT COUNT(*) FROM Rol WHERE Nombre = @m", con);
            chk.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task EstadoTurno_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/estadosturnos", new { descripcion = marca });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idEstadoTurno").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = @m", con);
            cmd.Parameters.AddWithValue("@m", marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idEstadoTurno en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using (var del = new SqlCommand("DELETE FROM EstadoTurno WHERE Descripcion = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using var chk = new SqlCommand("SELECT COUNT(*) FROM EstadoTurno WHERE Descripcion = @m", con);
            chk.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task TipoConsentimiento_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/tiposconsentimiento", new { descripcion = marca });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idTipoConsentimiento").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdTipoConsentimiento FROM TipoConsentimiento WHERE Descripcion = @m", con);
            cmd.Parameters.AddWithValue("@m", marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idTipoConsentimiento en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using (var del = new SqlCommand("DELETE FROM TipoConsentimiento WHERE Descripcion = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using var chk = new SqlCommand("SELECT COUNT(*) FROM TipoConsentimiento WHERE Descripcion = @m", con);
            chk.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task ObraSocial_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/obrassociales", new { nombre = marca });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idObraSocial").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdObraSocial FROM ObraSocial WHERE Nombre = @m", con);
            cmd.Parameters.AddWithValue("@m", marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idObraSocial en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using (var del = new SqlCommand("DELETE FROM ObraSocial WHERE Nombre = @m", con))
            { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
            using var chk = new SqlCommand("SELECT COUNT(*) FROM ObraSocial WHERE Nombre = @m", con);
            chk.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
        }
    }
}
