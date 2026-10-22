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
    // Etapa 2 de roles: el auditor es un profesional NO asociado al paciente, con el permiso auditor; CRM es un módulo sin acceso clínico.
    private HttpClient _auditor = null!, _crm = null!;
    private int _usuarioAuditor;

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
        _usuarioAuditor = usuarioAjeno;
        _auditor = ClienteCon(emisor.EmitirModulo(usuarioAjeno, new SesionModulo(Modulos.Profesional,
            new[] { Roles.Auditor, Roles.Profesional }, Array.Empty<string>(), IdProfesionalAjeno)));
        _crm = ClienteCon(emisor.Emitir(_datos.Usuario(), Roles.Crm));
        _sinToken = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        _datos.Dispose();   // lo propio y lo que cuelga de ello; la auditoría es de solo agregar: los usuarios que actuaron quedan (ver DatosDePrueba)
        return Task.CompletedTask;
    }

    private HttpClient ClienteCon(string token) => DatosDePrueba.Cliente(_factory, token);

    [Fact]
    public async Task Historia_clinica_sin_token_da_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _sinToken.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Fact]
    public async Task Historia_clinica_profesional_asociado_la_ve() =>
        Assert.Equal(HttpStatusCode.OK, (await _asociado.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    [Theory]
    [InlineData("Confirmada", true)]
    [InlineData("Nueva", true)]
    [InlineData("Activo", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Rechazada", false)]
    [InlineData("Cancelada", false)]
    public async Task El_estado_de_asignacion_restringe_la_lista_y_el_acceso_clinico(string? estado, bool permitido)
    {
        DatosDePrueba.Ejecutar(@"
            UPDATE PacienteProfesional SET EstadoAsignacion = @estado
            WHERE IdPaciente = @paciente AND IdProfesional = @profesional",
            ("@estado", estado), ("@paciente", IdPaciente), ("@profesional", IdProfesionalAsociado));

        var ids = new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(IdProfesionalAsociado);
        Assert.Equal(permitido, ids.Contains(IdPaciente));
        var pacientes = await _asociado.GetFromJsonAsync<List<Paciente>>($"/profesionales/{IdProfesionalAsociado}/pacientes");
        Assert.Equal(permitido, pacientes!.Any(p => p.IdPaciente == IdPaciente));
        Assert.Equal(permitido ? HttpStatusCode.OK : HttpStatusCode.NotFound,
            (await _asociado.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);
    }

    [Fact]
    public async Task Historia_clinica_profesional_ajeno_recibe_404_no_403() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _ajeno.GetAsync($"/pacientes/{IdPaciente}/historiaclinica")).StatusCode);

    // Roles y permisos (16/10): el Configurador deja de ver historias clínicas; las ve el auditor.
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
        var r = await _auditor.PostAsJsonAsync("/eventoscrisis", new { idPaciente = IdPaciente, nivelRiesgo = "Bajo", acciones = "prueba roles y permisos" });
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
            "SELECT TOP 1 Detalle, Categoria FROM LogAuditoria WHERE Entidad = 'HistoriaClinica' AND IdUsuario = @U ORDER BY IdLog DESC", con);
        cmd.Parameters.AddWithValue("@U", _usuarioAuditor);
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
    public async Task Reportes_atenciones_de_toda_la_organizacion_son_del_auditor()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _configurador.GetAsync("/reportes/atenciones")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _auditor.GetAsync("/reportes/atenciones")).StatusCode);
    }

    // Roles y permisos: estos reportes no pedían sesión (emociones de pacientes a la vista de cualquiera).
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
