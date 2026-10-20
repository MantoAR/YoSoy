using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

// Antes de este arreglo, POST /profesionales sin X-Usuario-Id (como lo llama registro_profesional.html,
// que todavía no tiene sesión) respondía 500: el log de auditoría intentaba guardar IdUsuario = 0 y
// chocaba con la clave foránea a Usuario. Ver migrations/alter_logauditoria_idusuario_nullable.sql.
// Corre contra una base de datos (la misma que usa la app, Conexion.cs); crea su propia especialidad y
// borra sus propias filas al final.
[Trait("Categoria", "Integracion")]
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

        using var datos = new DatosDePrueba();   // la especialidad es propia de la prueba
        var idEspecialidad = datos.Especialidad();

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
            mail = marca.ToLowerInvariant() + "@example.com",
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

            // La auditoría es de solo agregar (migración 19): el registro de este alta queda.
            using var cmdDelProf = new SqlCommand("DELETE FROM Profesional WHERE Matricula = @m", con);
            cmdDelProf.Parameters.AddWithValue("@m", marca);
            await cmdDelProf.ExecuteNonQueryAsync();

            using var cmdCheck = new SqlCommand("SELECT COUNT(*) FROM Profesional WHERE Matricula = @m", con);
            cmdCheck.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await cmdCheck.ExecuteScalarAsync())!);
        }
    }

    // QA exploratorio (docs/QA_INFORME.md, QA-04): la respuesta de este mismo registro
    // devolvía idProfesional: 0 en vez del id real recién generado — la fila se crea bien
    // en la base, pero el valor que viaja en el cuerpo de la respuesta no se actualiza.
    [Fact]
    public async Task Registro_publico_devuelve_el_idProfesional_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Password"] = " " })));
        using var client = factory.CreateClient();

        using var datos = new DatosDePrueba();   // la especialidad es propia de la prueba
        var idEspecialidad = datos.Especialidad();

        var respuesta = await client.PostAsJsonAsync("/profesionales", new
        {
            nombre = "Prueba",
            apellido = marca,
            dni = marca,
            sexo = "Otro",
            fechaNacimiento = "1990-01-01",
            matricula = marca,
            idEspecialidad,
            provincia = "Buenos Aires",
            estado = "Pendiente",
            mail = marca.ToLowerInvariant() + "@example.com",
            telefono = "1100000000",
        });

        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");

            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idProfesional").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdProfesional FROM Profesional WHERE Matricula = @m", con);
            cmd.Parameters.AddWithValue("@m", marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idProfesional en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();

            // La auditoría es de solo agregar (migración 19): lo que la API deja en LogAuditoria queda, como en producción.
            using var cmdDelProf = new SqlCommand("DELETE FROM Profesional WHERE Matricula = @m", con);
            cmdDelProf.Parameters.AddWithValue("@m", marca);
            await cmdDelProf.ExecuteNonQueryAsync();

            using var cmdCheck = new SqlCommand("SELECT COUNT(*) FROM Profesional WHERE Matricula = @m", con);
            cmdCheck.Parameters.AddWithValue("@m", marca);
            Assert.Equal(0, (int)(await cmdCheck.ExecuteScalarAsync())!);
        }
    }
}
