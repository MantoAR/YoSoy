using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// fix-ids-cero-endpoints, Grupo 3 (agenda): /agendas y /agenda (alias sin 's', mismo
// AgendaDAO) devuelven el id en 0 porque Insertar es void y nunca lee el id generado.
// IntervaloMinutos = 9999 es la marca de la fila de prueba (ningún horario real usa eso).
public class IdsCeroAgendaTests
{
    private const int Marca = 9999;

    private static HttpClient ClienteConfigurador(WebApplicationFactory<Program> factory)
    {
        var c = factory.CreateClient();
        var token = factory.Services.GetRequiredService<JwtEmisorService>().Emitir(1, Roles.Configurador);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task LimpiarYVerificar(SqlConnection con)
    {
        using (var del = new SqlCommand("DELETE FROM Agenda WHERE IntervaloMinutos = @m", con))
        { del.Parameters.AddWithValue("@m", Marca); await del.ExecuteNonQueryAsync(); }
        using var chk = new SqlCommand("SELECT COUNT(*) FROM Agenda WHERE IntervaloMinutos = @m", con);
        chk.Parameters.AddWithValue("@m", Marca);
        Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Agendas_alta_devuelve_id_real_no_cero()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/agendas", new
        {
            idProfesional = 1, dia = "jueves", horaInicio = "23:58:00", horaFin = "23:59:00",
            disponible = true, intervaloMinutos = Marca
        });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idAgenda").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdAgenda FROM Agenda WHERE IntervaloMinutos = @m", con);
            cmd.Parameters.AddWithValue("@m", Marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idAgenda en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            await LimpiarYVerificar(con);
        }
    }

    [Fact]
    public async Task Agenda_alias_alta_devuelve_id_real_no_cero()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/agenda", new
        {
            idProfesional = 1, dia = "jueves", horaInicio = "23:58:00", horaFin = "23:59:00",
            disponible = true, intervaloMinutos = Marca
        });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idAgenda").GetInt32();

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT IdAgenda FROM Agenda WHERE IntervaloMinutos = @m", con);
            cmd.Parameters.AddWithValue("@m", Marca);
            var idReal = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.True(idDevuelto > 0, $"idAgenda en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            await LimpiarYVerificar(con);
        }
    }
}
