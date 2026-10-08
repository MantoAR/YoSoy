using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

// Antes de este arreglo, POST /profesionales sin X-Usuario-Id (como lo llama registro_profesional.html,
// que todavía no tiene sesión) respondía 500: el log de auditoría intentaba guardar IdUsuario = 0 y
// chocaba con la clave foránea a Usuario. Ver migrations/alter_logauditoria_idusuario_nullable.sql.
// Corre contra la base local de desarrollo (la misma que usa la app, Conexion.cs); borra sus propias
// filas al final.
public class RegistroPublicoProfesionalTests
{
    [Fact]
    public async Task Registro_publico_sin_sesion_responde_2xx_y_queda_la_auditoria_sin_usuario()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Password"] = " " })));
        using var client = factory.CreateClient();

        var idEspecialidad = ObtenerPrimeraEspecialidad();

        // Sin X-Usuario-Id a propósito: así llega la solicitud pública, sin sesión.
        var respuesta = await client.PostAsJsonAsync("/profesionales", new
        {
            nombre = "Prueba",
            apellido = marca,
            segundoNombre = "",
            segundoApellido = "",
            dni = marca,
            sexo = "Otro",
            fechaNacimiento = "1990-01-01",
            matricula = marca,
            idEspecialidad,
            provincia = "Buenos Aires",
            origenContacto = "Test",
            estado = "Pendiente",
            mail = "xtest@example.com",
            telefono = "1100000000",
            fechaSolicitud = DateTime.Now,
        });

        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");

            using var con = Conexion.ObtenerConexion();
            con.Open();

            using var cmdProf = new SqlCommand("SELECT COUNT(*) FROM Profesional WHERE Matricula = @m", con);
            cmdProf.Parameters.AddWithValue("@m", marca);
            Assert.Equal(1, (int)(await cmdProf.ExecuteScalarAsync())!);

            using var cmdLog = new SqlCommand(@"
                SELECT COUNT(*) FROM LogAuditoria
                WHERE Entidad = 'Profesional' AND Accion = 'Alta Profesional'
                  AND Detalle LIKE @d AND IdUsuario IS NULL", con);
            cmdLog.Parameters.AddWithValue("@d", "%" + marca);
            Assert.Equal(1, (int)(await cmdLog.ExecuteScalarAsync())!);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();

            using var cmdDelLog = new SqlCommand("DELETE FROM LogAuditoria WHERE Entidad = 'Profesional' AND Detalle LIKE @d", con);
            cmdDelLog.Parameters.AddWithValue("@d", "%" + marca);
            await cmdDelLog.ExecuteNonQueryAsync();

            using var cmdDelProf = new SqlCommand("DELETE FROM Profesional WHERE Matricula = @m", con);
            cmdDelProf.Parameters.AddWithValue("@m", marca);
            await cmdDelProf.ExecuteNonQueryAsync();

            using var cmdCheck = new SqlCommand("SELECT COUNT(*) FROM Profesional WHERE Matricula = @m", con);
            cmdCheck.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await cmdCheck.ExecuteScalarAsync())!);
        }
    }

    private static int ObtenerPrimeraEspecialidad()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT TOP 1 IdEspecialidad FROM Especialidad", con);
        return (int)cmd.ExecuteScalar()!;
    }
}
