using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// Mejora de chats y centrado (A3): gates de rol de los endpoints de MensajeriaEndpoints. Con la D2 del chat para el personal
// (los profesionales no ven chats: ChatsSoloCrmTests prueba ruta por ruta que un Profesional recibe 403) esta clase deja
// los puntos más sensibles: un Profesional no puede asignar, ni escribir ni borrar notas, ni tocar las respuestas rápidas, y el
// CRM sí. Antes también cubría quién podía borrar la nota de otro profesional y las respuestas rápidas propias: ya no
// existen profesionales en los chats.
// Corre contra una base de datos; crea sus propios datos (los dos profesionales, sus usuarios y la
// conversación de prueba con teléfono único) y borra todo lo que crea al final. Antes suponía los
// profesionales 1 y 2 y los usuarios 1, 2 y 99 de la base de desarrollo.
[Trait("Categoria", "Integracion")]
public class ChatsGestionTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _crm = null!, _profesional = null!, _sinToken = null!;
    private DatosDePrueba _datos = null!;
    private int _idProfesional, _idOtroProfesional, _idUsuarioOtroProfesional;
    private int _idConversacion;
    private readonly string _telefono = "1199" + DateTime.Now.Ticks.ToString()[^7..];

    public async Task InitializeAsync()
    {
        _datos = new DatosDePrueba();
        _idProfesional = _datos.Profesional();
        _idOtroProfesional = _datos.Profesional();
        var idUsuarioCrm = _datos.Usuario();
        var idUsuarioProfesional = _datos.UsuarioDeProfesional(_idProfesional);
        _idUsuarioOtroProfesional = _datos.UsuarioDeProfesional(_idOtroProfesional);

        _factory = new WebApplicationFactory<Program>();
        var emisor = _factory.Services.GetRequiredService<JwtEmisorService>();

        _crm = ClienteCon(emisor.Emitir(idUsuarioCrm, Roles.Crm));
        _profesional = ClienteCon(emisor.Emitir(idUsuarioProfesional, Roles.Profesional, idProfesional: _idProfesional));
        _sinToken = _factory.CreateClient();

        var entrante = await _sinToken.PostAsJsonAsync("/mensajeria/simulado/entrante",
            new { telefono = _telefono, texto = "hola, prueba ChatsGestionTests" });
        entrante.EnsureSuccessStatusCode();
        var conv = await _sinToken.GetFromJsonAsync<JsonElement>($"/mensajeria/simulado/conversacion?telefono={_telefono}");
        _idConversacion = conv.GetProperty("idConversacion").GetInt32();

        // Escalada y sin asignar: visible para cualquier profesional + CRM (ver
        // VisibilidadConversacion). Recién creada por el bot queda "Activa" sin paciente
        // identificado - ahí solo entraría el CRM, y esta clase prueba permisos
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
        _datos.Dispose();   // los profesionales, los usuarios y lo que cuelga de ellos (notas, respuestas rápidas, auditoría)
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
            new { idProfesional = _idProfesional, motivoCategoria = "Otro" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Asignar_sin_token_responde_401()
    {
        var r = await _sinToken.PutAsJsonAsync($"/mensajeria/conversaciones/{_idConversacion}/asignar",
            new { idProfesional = _idProfesional, motivoCategoria = "Otro" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Reabrir_una_conversacion_que_no_esta_cerrada_responde_400()
    {
        var r = await _crm.PutAsync($"/mensajeria/conversaciones/{_idConversacion}/reabrir", null);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Las_notas_internas_son_solo_del_CRM_un_Profesional_no_las_escribe_ni_las_borra()
    {
        // El Profesional ni siquiera entra (D2): no puede escribir una nota.
        var negadaAlEscribir = await _profesional.PostAsJsonAsync($"/mensajeria/conversaciones/{_idConversacion}/notas", new { texto = "nota de prueba" });
        Assert.Equal(HttpStatusCode.Forbidden, negadaAlEscribir.StatusCode);

        // El CRM la escribe...
        var alta = await _crm.PostAsJsonAsync($"/mensajeria/conversaciones/{_idConversacion}/notas", new { texto = "nota de prueba" });
        alta.EnsureSuccessStatusCode();
        var idNota = (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idNota").GetInt32();

        // ...un Profesional no la puede borrar (ni lo intenta con otro token de profesional)...
        var otro = ClienteCon(_factory.Services.GetRequiredService<JwtEmisorService>()
            .Emitir(_idUsuarioOtroProfesional, Roles.Profesional, idProfesional: _idOtroProfesional));
        Assert.Equal(HttpStatusCode.Forbidden, (await _profesional.DeleteAsync($"/mensajeria/conversaciones/{_idConversacion}/notas/{idNota}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.DeleteAsync($"/mensajeria/conversaciones/{_idConversacion}/notas/{idNota}")).StatusCode);

        // ...y el CRM sí.
        var borrado = await _crm.DeleteAsync($"/mensajeria/conversaciones/{_idConversacion}/notas/{idNota}");
        Assert.Equal(HttpStatusCode.NoContent, borrado.StatusCode);
    }

    [Fact]
    public async Task Las_respuestas_rapidas_son_solo_del_CRM_y_siempre_globales()
    {
        var global = await _crm.PostAsJsonAsync("/mensajeria/respuestas-rapidas", new { titulo = "T1", texto = "Texto 1" });
        global.EnsureSuccessStatusCode();
        var creada = await global.Content.ReadFromJsonAsync<JsonElement>();
        var idGlobal = creada.GetProperty("idRespuestaRapida").GetInt32();

        try
        {
            // El CRM crea siempre una global (sin dueño): ya no hay profesionales con respuestas propias.
            Assert.Equal(JsonValueKind.Null, creada.GetProperty("idProfesional").ValueKind);

            // Un Profesional no crea una propia ni toca la global: 403 por el rol.
            Assert.Equal(HttpStatusCode.Forbidden, (await _profesional.PostAsJsonAsync("/mensajeria/respuestas-rapidas", new { titulo = "T2", texto = "Texto 2" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _profesional.PutAsJsonAsync($"/mensajeria/respuestas-rapidas/{idGlobal}", new { titulo = "x", texto = "y" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _profesional.DeleteAsync($"/mensajeria/respuestas-rapidas/{idGlobal}")).StatusCode);

            // El CRM sí la edita.
            var edita = await _crm.PutAsJsonAsync($"/mensajeria/respuestas-rapidas/{idGlobal}", new { titulo = "T1 editada", texto = "Texto 1 editado" });
            Assert.Equal(HttpStatusCode.OK, edita.StatusCode);
        }
        finally
        {
            await _crm.DeleteAsync($"/mensajeria/respuestas-rapidas/{idGlobal}");
            using var con = Conexion.ObtenerConexion();
            await con.OpenAsync();
            using var cmd = new SqlCommand("DELETE FROM RespuestaRapida WHERE IdRespuestaRapida = @A", con);
            cmd.Parameters.AddWithValue("@A", idGlobal);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
