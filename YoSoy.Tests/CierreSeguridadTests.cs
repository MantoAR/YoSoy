using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// Cierre de seguridad: matriz de rol para los grupos más sensibles tocados por este
// trabajo (historia clínica por pertenencia, agendas, turnos, reportes) + que la
// auditoría de apertura de datos clínicos queda con ids, nunca contenido. No repite la
// matriz completa (eso la corre GuardiaEndpointsTests, por reflexión); esto prueba la
// pertenencia y la identidad-del-token donde antes no había ningún chequeo.
// Corre contra una base de datos y crea sus propios datos (DatosDePrueba): el paciente con su historia
// clínica, un profesional asociado a él y otro que no, y los usuarios de cada uno. Antes suponía los
// profesionales reales 1 y 2, el paciente 1 y los usuarios 1 a 4 de la base de desarrollo.
[Trait("Categoria", "Integracion")]
public class CierreSeguridadTests : IAsyncLifetime
{
    private int IdProfesionalAsociado;   // asociado al paciente
    private int IdProfesionalAjeno;      // no asociado al paciente
    private int IdPaciente;
    private int _usuarioAsociado;

    private DatosDePrueba _datos = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _asociado = null!, _ajeno = null!, _configurador = null!, _sinToken = null!;
    private int _usuarioPaciente;

    public Task InitializeAsync()
    {
        _datos = new DatosDePrueba();
        IdPaciente = _datos.Paciente();
        IdProfesionalAsociado = _datos.Profesional();
        IdProfesionalAjeno = _datos.Profesional();
        _datos.PacienteProfesional(IdPaciente, IdProfesionalAsociado);
        _datos.HistoriaClinica(IdPaciente);

        _usuarioAsociado = _datos.UsuarioDeProfesional(IdProfesionalAsociado);
        var usuarioAjeno = _datos.UsuarioDeProfesional(IdProfesionalAjeno);
        var usuarioConfigurador = _datos.Usuario();
        _usuarioPaciente = _datos.UsuarioDePaciente(IdPaciente);

        _factory = new WebApplicationFactory<Program>();
        var emisor = _factory.Services.GetRequiredService<JwtEmisorService>();
        _asociado = ClienteCon(emisor.Emitir(_usuarioAsociado, Roles.Profesional, idProfesional: IdProfesionalAsociado));
        _ajeno = ClienteCon(emisor.Emitir(usuarioAjeno, Roles.Profesional, idProfesional: IdProfesionalAjeno));
        _configurador = ClienteCon(emisor.Emitir(usuarioConfigurador, Roles.Configurador));
        _sinToken = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        _datos.Dispose();   // lo propio y, en cascada, la auditoría y los turnos que cuelgan de ello
        return Task.CompletedTask;
    }

    private HttpClient ClienteCon(string token) => DatosDePrueba.Cliente(_factory, token);

    [Fact]
    public async Task Historia_clinica_sin_token_da_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _sinToken.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task Historia_clinica_profesional_asociado_la_ve() =>
        Assert.Equal(HttpStatusCode.OK, (await _asociado.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task Historia_clinica_profesional_ajeno_recibe_404_no_403() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _ajeno.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task Historia_clinica_configurador_la_ve_igual() =>
        Assert.Equal(HttpStatusCode.OK, (await _configurador.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task Apertura_de_historia_clinica_queda_auditada_solo_con_ids()
    {
        await _asociado.GetAsync($"/pacientes/{IdPaciente}/historiaclinica");

        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT TOP 1 IdUsuario, Detalle FROM LogAuditoria WHERE Entidad = 'HistoriaClinica' AND IdEntidad = @Id ORDER BY FechaHora DESC", con);
        cmd.Parameters.AddWithValue("@Id", IdPaciente);
        using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Equal(_usuarioAsociado, r.GetInt32(0));        // Usuario del profesional asociado
        Assert.DoesNotContain("Transcripcion", r.GetString(1));
        Assert.DoesNotContain("Observaciones", r.GetString(1));
    }

    [Fact]
    public async Task Agenda_profesional_no_puede_borrar_la_de_otro()
    {
        var idAgenda = _datos.Agenda(IdProfesionalAsociado, "Lunes", "09:00", "10:00");

        var respuesta = await _ajeno.DeleteAsync($"/agendas/{idAgenda}");
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Agenda WHERE IdAgenda = @Id", ("@Id", idAgenda)));   // sigue ahí

        var propia = await _asociado.DeleteAsync($"/agendas/{idAgenda}");
        Assert.Equal(HttpStatusCode.NoContent, propia.StatusCode);
    }

    [Fact]
    public async Task Turno_solicitar_ignora_el_idPaciente_del_cuerpo_y_usa_el_del_token()
    {
        // El profesional tiene que tener agenda ese día y el estado "Pendiente" tiene que existir: así la
        // solicitud SE CREA (antes, en una base sin agenda, la regla la rechazaba y la prueba no verificaba nada).
        var fecha = DateTime.Now.AddDays(10);
        _datos.Agenda(IdProfesionalAsociado, DatosDePrueba.NombreDelDia(fecha), "09:00", "13:00");
        _datos.EstadoTurno("Pendiente");
        _datos.AlLimpiar("Turno", "IdPaciente", IdPaciente);

        var pacienteToken = ClienteCon(_factory.Services.GetRequiredService<JwtEmisorService>()
            .Emitir(_usuarioPaciente, Roles.Paciente, idPaciente: IdPaciente));

        // Pide un turno a nombre de OTRO paciente (idPaciente=999 en el cuerpo): tiene que crearse a nombre
        // del paciente del token, nunca del 999.
        var respuesta = await pacienteToken.PostAsJsonAsync("/turnos/solicitar", new
        {
            idPaciente = 999, idProfesional = IdProfesionalAsociado,
            fecha = fecha.ToString("yyyy-MM-dd"), hora = "11:00:00", motivo = "Prueba cierre de seguridad"
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = await respuesta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(IdPaciente, creado.GetProperty("idPaciente").GetInt32());
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = 999"));
    }

    [Fact]
    public async Task Reportes_atenciones_sin_token_da_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _sinToken.GetAsync("/reportes/atenciones")).StatusCode);

    [Fact]
    public async Task Reportes_atenciones_profesional_no_puede_pedir_el_alcance_de_otro() =>
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _asociado.GetAsync($"/reportes/atenciones?idProfesional={IdProfesionalAjeno}")).StatusCode);

    [Fact]
    public async Task Headers_de_seguridad_salen_en_cualquier_respuesta_no_solo_en_lo_publico()
    {
        var respuesta = await _sinToken.GetAsync("/capacidades");
        Assert.Equal("nosniff", respuesta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", respuesta.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("SAMEORIGIN", respuesta.Headers.GetValues("X-Frame-Options").Single());
    }
}
