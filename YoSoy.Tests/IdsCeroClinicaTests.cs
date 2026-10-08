using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// fix-ids-cero-endpoints, Grupo 4 (clínica): Consentimiento, HistoriaClinica, Evolucion,
// Derivacion, EventoCrisis, InteraccionIA y Cobertura devuelven el id en 0 — mismo patrón
// que los grupos anteriores. Todas autenticadas como Configurador: AutorizacionClinica.
// PuedePaciente devuelve true siempre para el admin (Services/AutorizacionClinica.cs), así
// que no hace falta un vínculo PacienteProfesional real para probar el alta en sí.
// Paciente 1, HistoriaClinica 1 (de Paciente 1), ObraSocial 1, TipoConsentimiento 1 y
// Profesional 1/2 son datos reales preexistentes de la base de desarrollo — solo se leen,
// nunca se modifican ni se borran.
public class IdsCeroClinicaTests
{
    private static HttpClient ClienteConfigurador(WebApplicationFactory<Program> factory)
    {
        var c = factory.CreateClient();
        var token = factory.Services.GetRequiredService<JwtEmisorService>().Emitir(1, Roles.Configurador);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<int> IdRealPor(string tabla, string columnaId, string columnaMarca, object marca)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand($"SELECT {columnaId} FROM {tabla} WHERE {columnaMarca} = @m", con);
        cmd.Parameters.AddWithValue("@m", marca);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task LimpiarYVerificar(string tabla, string columnaId, string columnaMarca, object marca)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using (var del = new SqlCommand($"DELETE FROM {tabla} WHERE {columnaMarca} = @m", con))
        { del.Parameters.AddWithValue("@m", marca); await del.ExecuteNonQueryAsync(); }
        using var chk = new SqlCommand($"SELECT COUNT(*) FROM {tabla} WHERE {columnaMarca} = @m", con);
        chk.Parameters.AddWithValue("@m", marca);
        Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Consentimiento_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/consentimientos", new
        { idPaciente = 1, idTipoConsentimiento = 1, version = marca, fechaFirma = DateTime.Now });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idConsentimiento").GetInt32();
            var idReal = await IdRealPor("Consentimiento", "IdConsentimiento", "Version", marca);
            Assert.True(idDevuelto > 0, $"idConsentimiento en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally { await LimpiarYVerificar("Consentimiento", "IdConsentimiento", "Version", marca); }
    }

    [Fact]
    public async Task HistoriaClinica_alta_devuelve_id_real_no_cero()
    {
        // HistoriaClinica tiene UNIQUE en IdPaciente (una sola historia por paciente) y los
        // pacientes reales de la base de desarrollo ya tienen la suya — se crea un paciente
        // descartable propio para esta prueba, en vez de pisar el de un paciente real.
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        int idPacientePrueba;
        using (var con = Conexion.ObtenerConexion())
        {
            con.Open();
            using var cmd = new SqlCommand(
                "INSERT INTO Paciente (Nombre, Dni, Estado) VALUES ('Prueba', @m, 'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
            cmd.Parameters.AddWithValue("@m", marca);
            idPacientePrueba = (int)(await cmd.ExecuteScalarAsync())!;
        }

        var respuesta = await client.PostAsJsonAsync("/historiasclinicas", new { idPaciente = idPacientePrueba, fechaApertura = DateTime.Now });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idHistoria").GetInt32();
            var idReal = await IdRealPor("HistoriaClinica", "IdHistoria", "IdPaciente", idPacientePrueba);
            Assert.True(idDevuelto > 0, $"idHistoria en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using (var del = new SqlCommand("DELETE FROM HistoriaClinica WHERE IdPaciente = @id", con))
            { del.Parameters.AddWithValue("@id", idPacientePrueba); await del.ExecuteNonQueryAsync(); }
            using (var del = new SqlCommand("DELETE FROM Paciente WHERE IdPaciente = @id", con))
            { del.Parameters.AddWithValue("@id", idPacientePrueba); await del.ExecuteNonQueryAsync(); }
            using var chk = new SqlCommand("SELECT COUNT(*) FROM Paciente WHERE IdPaciente = @id", con);
            chk.Parameters.AddWithValue("@id", idPacientePrueba);
            Assert.Equal(0, (int)(await chk.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Evolucion_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        // IdHistoria = 1 es la historia real (preexistente) de Paciente 1 — solo se lee.
        var respuesta = await client.PostAsJsonAsync("/evoluciones", new
        { idHistoria = 1, idProfesional = 1, fecha = DateTime.Now, diagnostico = marca });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idEvolucion").GetInt32();
            var idReal = await IdRealPor("Evolucion", "IdEvolucion", "Diagnostico", marca);
            Assert.True(idDevuelto > 0, $"idEvolucion en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally { await LimpiarYVerificar("Evolucion", "IdEvolucion", "Diagnostico", marca); }
    }

    [Fact]
    public async Task Derivacion_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/derivaciones", new
        { idPaciente = 1, idProfesionalOrigen = 1, idProfesionalDestino = 2, motivo = marca });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idDerivacion").GetInt32();
            var idReal = await IdRealPor("Derivacion", "IdDerivacion", "Motivo", marca);
            Assert.True(idDevuelto > 0, $"idDerivacion en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally { await LimpiarYVerificar("Derivacion", "IdDerivacion", "Motivo", marca); }
    }

    [Fact]
    public async Task EventoCrisis_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/eventoscrisis", new
        { idPaciente = 1, fecha = DateTime.Now, nivelRiesgo = "Bajo", acciones = marca });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idEvento").GetInt32();
            var idReal = await IdRealPor("EventoCrisis", "IdEvento", "Acciones", marca);
            Assert.True(idDevuelto > 0, $"idEvento en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally { await LimpiarYVerificar("EventoCrisis", "IdEvento", "Acciones", marca); }
    }

    [Fact]
    public async Task InteraccionIA_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/interaccionesia", new
        { idPaciente = 1, fecha = DateTime.Now, consulta = marca, respuesta = "r" });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idInteraccion").GetInt32();
            var idReal = await IdRealPor("InteraccionIA", "IdInteraccion", "Consulta", marca);
            Assert.True(idDevuelto > 0, $"idInteraccion en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally { await LimpiarYVerificar("InteraccionIA", "IdInteraccion", "Consulta", marca); }
    }

    [Fact]
    public async Task Cobertura_alta_devuelve_id_real_no_cero()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = ClienteConfigurador(factory);

        var respuesta = await client.PostAsJsonAsync("/coberturas", new
        { idPaciente = 1, idObraSocial = 1, nroAfiliado = marca, planCobertura = "Básico" });
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idDevuelto = cuerpo.GetProperty("idCobertura").GetInt32();
            var idReal = await IdRealPor("Cobertura", "IdCobertura", "NroAfiliado", marca);
            Assert.True(idDevuelto > 0, $"idCobertura en la respuesta tendría que ser > 0, llegó {idDevuelto}");
            Assert.Equal(idReal, idDevuelto);
        }
        finally { await LimpiarYVerificar("Cobertura", "IdCobertura", "NroAfiliado", marca); }
    }
}
