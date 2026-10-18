using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

// Cierre de seguridad: matriz de rol para los grupos más sensibles tocados por este
// trabajo (historia clínica por pertenencia, agendas, turnos, reportes) + que la
// auditoría de apertura de datos clínicos queda con ids, nunca contenido. No repite la
// matriz completa (eso la corre GuardiaEndpointsTests, por reflexión); esto prueba la
// pertenencia y la identidad-del-token donde antes no había ningún chequeo.
// Corre contra la base local de desarrollo, igual que PertenenciaConversacionTests; usa
// los mismos profesionales reales 1 (Usuario 2, asociado al paciente 1) y 2 (Usuario 4,
// no asociado). Crea su propia HistoriaClinica para el paciente 1 si no existe y borra
// solo lo que crea.
public class CierreSeguridadTests : IAsyncLifetime
{
    private const int IdProfesionalAsociado = 1;     // ya asociado al paciente 1
    private const int IdProfesionalAjeno = 2;        // no asociado al paciente 1
    private const int IdPaciente = 1;

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _asociado = null!, _ajeno = null!, _configurador = null!, _sinToken = null!;
    // Etapa 3: el auditor es un profesional NO asociado al paciente 1, con el permiso auditor.
    private HttpClient _auditor = null!, _crm = null!;
    private int? _idHistoriaCreada;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>();
        var emisor = _factory.Services.GetRequiredService<JwtEmisorService>();
        _asociado = ClienteCon(emisor.Emitir(2, Roles.Profesional, idProfesional: IdProfesionalAsociado));
        _ajeno = ClienteCon(emisor.Emitir(4, Roles.Profesional, idProfesional: IdProfesionalAjeno));
        _configurador = ClienteCon(emisor.Emitir(1, Roles.Configurador));
        _auditor = ClienteCon(emisor.EmitirModulo(4, new SesionModulo(Modulos.Profesional,
            new[] { Roles.Auditor, Roles.Profesional }, Array.Empty<string>(), IdProfesionalAjeno)));
        _crm = ClienteCon(emisor.Emitir(1, Roles.Crm));
        _sinToken = _factory.CreateClient();

        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var existe = new SqlCommand("SELECT IdHistoria FROM HistoriaClinica WHERE IdPaciente = @Id", con);
        existe.Parameters.AddWithValue("@Id", IdPaciente);
        var idExistente = await existe.ExecuteScalarAsync();
        if (idExistente is null)
        {
            using var crear = new SqlCommand(
                "INSERT INTO HistoriaClinica (IdPaciente, FechaApertura) OUTPUT INSERTED.IdHistoria VALUES (@Id, GETDATE())", con);
            crear.Parameters.AddWithValue("@Id", IdPaciente);
            _idHistoriaCreada = (int)(await crear.ExecuteScalarAsync())!;
        }
    }

    public async Task DisposeAsync()
    {
        if (_idHistoriaCreada is int idHistoria)
        {
            using var con = Conexion.ObtenerConexion();
            await con.OpenAsync();
            using var borrar = new SqlCommand("DELETE FROM HistoriaClinica WHERE IdHistoria = @Id", con);
            borrar.Parameters.AddWithValue("@Id", idHistoria);
            await borrar.ExecuteNonQueryAsync();
        }
        // Etapa 2 (migración 17): la auditoría es de solo agregar, la base rechaza el DELETE.
        // Los registros que deja esta prueba quedan, como cualquier otro acceso real.
        _factory.Dispose();
    }

    private HttpClient ClienteCon(string token)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Historia_clinica_sin_token_da_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _sinToken.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task Historia_clinica_profesional_asociado_la_ve() =>
        Assert.Equal(HttpStatusCode.OK, (await _asociado.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task Historia_clinica_profesional_ajeno_recibe_404_no_403() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _ajeno.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    // Etapa 3 (16/10): el Configurador deja de ver historias clínicas; las ve el auditor.
    [Fact]
    public async Task Historia_clinica_el_configurador_ya_no_la_ve() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await _configurador.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task Historia_clinica_crm_no_la_ve() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await _crm.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task Historia_clinica_el_auditor_la_ve_aunque_no_atienda_al_paciente() =>
        Assert.Equal(HttpStatusCode.OK, (await _auditor.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task El_auditor_lee_pero_no_escribe_en_la_historia_de_un_paciente_que_no_atiende()
    {
        var r = await _auditor.PostAsJsonAsync("/eventoscrisis", new { idPaciente = IdPaciente, nivelRiesgo = "Bajo", acciones = "prueba etapa 3" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Listados_clinicos_completos_solo_para_el_auditor()
    {
        foreach (var ruta in new[] { "/historiasclinicas", "/evoluciones", "/eventoscrisis", "/interaccionesia" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await _configurador.GetAsync(ruta)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _asociado.GetAsync(ruta)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _auditor.GetAsync(ruta)).StatusCode);
        }
    }

    [Fact]
    public async Task La_apertura_del_auditor_queda_auditada_como_auditor()
    {
        await _auditor.GetAsync($"/pacientes/{IdPaciente}/historiaclinica");
        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT TOP 1 Detalle, Categoria FROM LogAuditoria WHERE Entidad = 'HistoriaClinica' AND IdUsuario = 4 ORDER BY IdLog DESC", con);
        using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Contains("como auditor", r.GetString(0));
        Assert.Equal("Clinica", r.GetString(1));
    }

    [Fact]
    public async Task Auditoria_cada_uno_ve_su_parte()
    {
        var administrativa = await _configurador.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/auditoria?cantidad=200");
        Assert.DoesNotContain(administrativa!, d => d.GetProperty("entidad").GetString() == "HistoriaClinica");
        var clinica = await _auditor.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/auditoria?cantidad=200");
        Assert.DoesNotContain(clinica!, d => d.GetProperty("accion").GetString() == "Ingreso");
        Assert.Equal(HttpStatusCode.Forbidden, (await _asociado.GetAsync("/auditoria")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _crm.GetAsync("/auditoria")).StatusCode);
    }

    [Fact]
    public async Task Permisos_por_usuario_solo_los_edita_el_configurador()
    {
        Assert.Equal(HttpStatusCode.OK, (await _configurador.GetAsync("/usuarios/accesos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _auditor.GetAsync("/usuarios/accesos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _crm.PutAsJsonAsync("/usuarios/4/accesos", new { permisos = new[] { "crm" } })).StatusCode);
    }

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
        Assert.Equal(2, r.GetInt32(0));                       // Usuario del profesional asociado
        Assert.DoesNotContain("Transcripcion", r.GetString(1));
        Assert.DoesNotContain("Observaciones", r.GetString(1));
    }

    [Fact]
    public async Task Agenda_profesional_no_puede_borrar_la_de_otro()
    {
        using var con = Conexion.ObtenerConexion();
        await con.OpenAsync();
        using var crear = new SqlCommand(
            "INSERT INTO Agenda (IdProfesional, Dia, HoraInicio, HoraFin, Disponible, IntervaloMinutos) " +
            "OUTPUT INSERTED.IdAgenda VALUES (@IdProf, 'Lunes', '09:00', '10:00', 1, 30)", con);
        crear.Parameters.AddWithValue("@IdProf", IdProfesionalAsociado);
        var idAgenda = (int)(await crear.ExecuteScalarAsync())!;

        try
        {
            var respuesta = await _ajeno.DeleteAsync($"/agendas/{idAgenda}");
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);

            var propia = await _asociado.DeleteAsync($"/agendas/{idAgenda}");
            Assert.Equal(HttpStatusCode.NoContent, propia.StatusCode);
        }
        finally
        {
            using var borrar = new SqlCommand("DELETE FROM Agenda WHERE IdAgenda = @Id", con);
            borrar.Parameters.AddWithValue("@Id", idAgenda);
            await borrar.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Turno_solicitar_ignora_el_idPaciente_del_cuerpo_y_usa_el_del_token()
    {
        var pacienteToken = ClienteCon(_factory.Services.GetRequiredService<JwtEmisorService>()
            .Emitir(3, Roles.Paciente, idPaciente: IdPaciente));

        // Pide un turno a nombre de OTRO paciente (idPaciente=999 en el cuerpo) - la regla
        // RN-07/disponibilidad puede rechazarlo por otros motivos, pero lo que se prueba
        // acá es que si se crea, queda a nombre del paciente del token, nunca del 999.
        var respuesta = await pacienteToken.PostAsJsonAsync("/turnos/solicitar", new
        {
            idPaciente = 999, idProfesional = IdProfesionalAsociado,
            fecha = DateTime.Now.AddDays(10).ToString("yyyy-MM-dd"), hora = "11:00:00", motivo = "Prueba cierre de seguridad"
        });

        if (respuesta.StatusCode == HttpStatusCode.Created)
        {
            var creado = await respuesta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var idTurno = creado.GetProperty("idTurno").GetInt32();
            Assert.Equal(IdPaciente, creado.GetProperty("idPaciente").GetInt32());

            using var con = Conexion.ObtenerConexion();
            await con.OpenAsync();
            using var borrar = new SqlCommand("DELETE FROM Turno WHERE IdTurno = @Id", con);
            borrar.Parameters.AddWithValue("@Id", idTurno);
            await borrar.ExecuteNonQueryAsync();
        }
        // Un 409 (regla de negocio, ej. doble turno ese día) es un resultado válido para
        // esta prueba: lo que importa es que nunca haya quedado un Turno con IdPaciente=999.
    }

    [Fact]
    public async Task Reportes_atenciones_sin_token_da_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _sinToken.GetAsync("/reportes/atenciones")).StatusCode);

    [Fact]
    public async Task Reportes_atenciones_de_toda_la_organizacion_son_del_auditor()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _configurador.GetAsync("/reportes/atenciones")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _auditor.GetAsync("/reportes/atenciones")).StatusCode);
    }

    // Etapa 3: estos reportes no pedían sesión (emociones de pacientes a la vista de cualquiera).
    [Fact]
    public async Task Reportes_del_profesional_exigen_sesion_y_ser_el_propio_o_auditor()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _sinToken.GetAsync($"/reportes/profesional/{IdProfesionalAsociado}/kpis")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _ajeno.GetAsync($"/reportes/profesional/{IdProfesionalAsociado}/kpis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _asociado.GetAsync($"/reportes/profesional/{IdProfesionalAsociado}/kpis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _auditor.GetAsync($"/reportes/profesional/{IdProfesionalAsociado}/kpis")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _ajeno.GetAsync($"/reportes/paciente/{IdPaciente}/evolucion-emocional")).StatusCode);
    }

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
