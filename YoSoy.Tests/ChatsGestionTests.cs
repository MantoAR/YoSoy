using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// Mejora de chats y centrado (A3): gates de rol y pertenencia de los endpoints nuevos de
// MensajeriaEndpoints. La matriz completa (401/403/404/200 por endpoint) ya se corrió a
// mano con Playwright (docs/pruebas-ui-cambios-agus/p10_chats_a3.py, no versionado); esto
// deja los puntos más sensibles (quién puede asignar, quién puede borrar una nota de otro,
// quién puede tocar una respuesta rápida ajena) como prueba permanente de la batería.
// Corre contra la base local de desarrollo; crea su propia conversación de prueba
// (teléfono único) y borra todo lo que crea al final.
public class ChatsGestionTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _configurador = null!, _profesional = null!, _sinToken = null!;
    private int _idConversacion;
    private readonly string _telefono = "1199" + DateTime.Now.Ticks.ToString()[^7..];

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>();
        var emisor = _factory.Services.GetRequiredService<JwtEmisorService>();

        _configurador = ClienteCon(emisor.Emitir(1, Roles.Configurador));
        _profesional = ClienteCon(emisor.Emitir(2, Roles.Profesional, idProfesional: 1));
        _sinToken = _factory.CreateClient();

        var entrante = await _sinToken.PostAsJsonAsync("/mensajeria/simulado/entrante",
            new { telefono = _telefono, texto = "hola, prueba ChatsGestionTests" });
        entrante.EnsureSuccessStatusCode();
        var conv = await _sinToken.GetFromJsonAsync<JsonElement>($"/mensajeria/simulado/conversacion?telefono={_telefono}");
        _idConversacion = conv.GetProperty("idConversacion").GetInt32();

        // Escalada y sin asignar: visible para cualquier profesional + Configurador (ver
        // VisibilidadConversacion). Recién creada por el bot queda "Activa" sin paciente
        // identificado - ahí solo entraría el Configurador, y esta clase prueba permisos
        // de notas/asignación entre profesionales, no la pertenencia en sí (eso lo cubre
        // PertenenciaConversacionTests).
        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var cmd = new SqlCommand("UPDATE Conversacion SET Estado = 'DerivadaHumano' WHERE IdConversacion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", _idConversacion);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM NotaInternaConversacion WHERE IdConversacion = @Id",
            "DELETE FROM ConversacionEtiqueta WHERE IdConversacion = @Id",
            "DELETE FROM MensajeChat WHERE IdConversacion = @Id",
            "DELETE FROM Conversacion WHERE IdConversacion = @Id"
        })
        {
            using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Id", _idConversacion);
            await cmd.ExecuteNonQueryAsync();
        }
        _factory.Dispose();
    }

    private HttpClient ClienteCon(string token)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Asignar_sin_ser_administrador_responde_403()
    {
        var r = await _profesional.PutAsJsonAsync($"/mensajeria/conversaciones/{_idConversacion}/asignar",
            new { idProfesional = 1, motivoCategoria = "Otro" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Asignar_sin_token_responde_401()
    {
        var r = await _sinToken.PutAsJsonAsync($"/mensajeria/conversaciones/{_idConversacion}/asignar",
            new { idProfesional = 1, motivoCategoria = "Otro" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Reabrir_una_conversacion_que_no_esta_cerrada_responde_400()
    {
        var r = await _configurador.PutAsync($"/mensajeria/conversaciones/{_idConversacion}/reabrir", null);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Nota_interna_solo_la_borra_el_autor_o_el_administrador()
    {
        var alta = await _profesional.PostAsJsonAsync($"/mensajeria/conversaciones/{_idConversacion}/notas", new { texto = "nota de prueba" });
        alta.EnsureSuccessStatusCode();
        var idNota = (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idNota").GetInt32();

        // Otro profesional (no el autor) no puede borrarla.
        var otro = ClienteCon(_factory.Services.GetRequiredService<JwtEmisorService>().Emitir(99, Roles.Profesional, idProfesional: 2));
        var negado = await otro.DeleteAsync($"/mensajeria/conversaciones/{_idConversacion}/notas/{idNota}");
        Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);

        // El administrador sí, aunque no sea el autor.
        var borrado = await _configurador.DeleteAsync($"/mensajeria/conversaciones/{_idConversacion}/notas/{idNota}");
        Assert.Equal(HttpStatusCode.NoContent, borrado.StatusCode);
    }

    [Fact]
    public async Task Respuesta_rapida_global_y_propia_no_se_cruzan_entre_roles()
    {
        var global = await _configurador.PostAsJsonAsync("/mensajeria/respuestas-rapidas", new { titulo = "T1", texto = "Texto 1" });
        global.EnsureSuccessStatusCode();
        var idGlobal = (await global.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idRespuestaRapida").GetInt32();

        var propia = await _profesional.PostAsJsonAsync("/mensajeria/respuestas-rapidas", new { titulo = "T2", texto = "Texto 2" });
        propia.EnsureSuccessStatusCode();
        var idPropia = (await propia.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idRespuestaRapida").GetInt32();

        try
        {
            var profesionalTocaGlobal = await _profesional.PutAsJsonAsync($"/mensajeria/respuestas-rapidas/{idGlobal}", new { titulo = "x", texto = "y" });
            Assert.Equal(HttpStatusCode.Forbidden, profesionalTocaGlobal.StatusCode);

            var configuradorTocaPropia = await _configurador.PutAsJsonAsync($"/mensajeria/respuestas-rapidas/{idPropia}", new { titulo = "x", texto = "y" });
            Assert.Equal(HttpStatusCode.Forbidden, configuradorTocaPropia.StatusCode);
        }
        finally
        {
            await _configurador.DeleteAsync($"/mensajeria/respuestas-rapidas/{idGlobal}");
            await _profesional.DeleteAsync($"/mensajeria/respuestas-rapidas/{idPropia}");
            using var con = Conexion.ObtenerConexion();
            await con.OpenAsync();
            using var cmd = new SqlCommand("DELETE FROM RespuestaRapida WHERE IdRespuestaRapida IN (@A, @B)", con);
            cmd.Parameters.AddWithValue("@A", idGlobal);
            cmd.Parameters.AddWithValue("@B", idPropia);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
