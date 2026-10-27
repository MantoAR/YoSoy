using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Se OMITE si la base no tiene la migración 34 (paciente derivado a psiquiatría).</summary>
public sealed class FactConBaseDeDerivadoAttribute : FactAttribute
{
    public FactConBaseDeDerivadoAttribute()
    {
        if (Convert.ToInt32(DatosDePrueba.Escalar("SELECT CASE WHEN OBJECT_ID('dbo.DerivacionPsiquiatrica', 'U') IS NOT NULL THEN 1 ELSE 0 END")) != 1)
            Skip = "La base no tiene la migración migrations/add_derivado_psiquiatra.sql.";
    }
}

/// <summary>Las dos migraciones: la 34 y la 32 (propuesta automática), para la prueba de la asignación automática.</summary>
public sealed class FactConBaseDeDerivadoYAsignacionAttribute : FactAttribute
{
    public FactConBaseDeDerivadoYAsignacionAttribute()
    {
        var motivo = new FactConBaseDeDerivadoAttribute().Skip ?? new FactConBaseDeAsignacionAttribute().Skip;
        if (motivo is not null) Skip = motivo;
    }
}

// «Derivado» a psiquiatría (migración 34), con datos propios en yosoy_test: confirmación, idempotencia, auditoría, bloqueo de turnos por todos los caminos, turnos futuros que NO se cancelan
// (y generan aviso), reapertura y permisos por rol. Nadie recibe mails: el servicio de correo es un espía.
[Trait("Categoria", "Integracion")]
public class DerivacionPsiquiatraBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly EmailServiceEspia _mails = new();
    private readonly WebApplicationFactory<Program> _fabrica;
    private static readonly object Confirma = new { confirma = true };
    private const string Bloqueo = "Este paciente fue derivado y no puede reservar turnos con este equipo.";

    public DerivacionPsiquiatraBaseRealTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(_mails); }));
    }

    public void Dispose()
    {
        DatosDePrueba.Ejecutar("DELETE FROM NotificacionInterna WHERE Mensaje LIKE @m", ("@m", "%" + _d.Marca + "%"));
        _fabrica.Dispose();
        _d.Dispose();
    }

    // ───────────── Armado ─────────────

    private DateTime Ahora => _fabrica.Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime;

    private HttpClient ComoProfesional(int idProfesional) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(idProfesional), Roles.Profesional, idProfesional: idProfesional);
    private HttpClient ComoPaciente(int idPaciente) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(idPaciente), Roles.Paciente, idPaciente: idPaciente);
    private HttpClient ComoCrm() => DatosDePrueba.Cliente(_fabrica, _d.Usuario(), Roles.Crm);

    private HttpClient ConAdmision(int idProfesional)
    {
        var token = _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(_d.UsuarioDeProfesional(idProfesional),
            new SesionModulo(Modulos.Profesional, new[] { Roles.Profesional }, new[] { Privilegios.Admision }, idProfesional));
        return DatosDePrueba.Cliente(_fabrica, token);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }

    /// <summary>Un profesional con agenda todos los días de 9 a 18 y un paciente a su cargo.</summary>
    private (int Prof, int Pac) Escenario(string? nombrePaciente = null)
    {
        var prof = _d.Profesional(_d.Especialidad(), nombre: "Prof " + _d.Marca);
        foreach (var dia in Enum.GetValues<DayOfWeek>())
            _d.Agenda(prof, DatosDePrueba.NombreDelDia(new DateTime(2026, 11, 1).AddDays((int)dia)), "09:00", "18:00");
        var pac = _d.Paciente(nombrePaciente ?? "Paciente " + _d.Marca, mail: $"{_d.Marca}@example.test");
        _d.PacienteProfesional(pac, prof);
        _d.AlLimpiar("DerivacionPsiquiatrica", "IdPaciente", pac);
        _d.AlLimpiar("Turno", "IdPaciente", pac);
        return (prof, pac);
    }

    private DateTime Manana10 => Ahora.Date.AddDays(3).AddHours(10);

    private static int Filas(int pac) => DatosDePrueba.Contar("SELECT COUNT(*) FROM DerivacionPsiquiatrica WHERE IdPaciente = @p", ("@p", pac));
    private static int Vigentes(int pac) => DatosDePrueba.Contar("SELECT COUNT(*) FROM DerivacionPsiquiatrica WHERE IdPaciente = @p AND ReabiertaUtc IS NULL", ("@p", pac));
    private static int Auditorias(int pac, string accion) => DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = N'Paciente' AND IdEntidad = @p AND Accion = @a", ("@p", pac), ("@a", accion));
    private static int Avisos(string destinatario, string marca) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Destinatario = @d AND Tipo = N'derivado-turnos-futuros' AND Mensaje LIKE @m", ("@d", destinatario), ("@m", "%" + marca + "%"));

    private Task<HttpResponseMessage> Derivar(HttpClient c, int pac, object? cuerpo = null) => c.PostAsJsonAsync($"/pacientes/{pac}/derivar-psiquiatra", cuerpo ?? Confirma);
    private Task<HttpResponseMessage> Reabrir(HttpClient c, int pac, object? cuerpo = null) => c.PostAsJsonAsync($"/pacientes/{pac}/reabrir-derivacion", cuerpo ?? Confirma);

    private Task<HttpResponseMessage> PedirTurno(HttpClient c, int prof, DateTime momento) =>
        c.PostAsJsonAsync("/turnos/solicitar", new { idProfesional = prof, fecha = momento.ToString("yyyy-MM-dd"), hora = momento.ToString("HH:mm:ss"), motivo = "[TEST] derivado" });

    // ───────────── Confirmación, idempotencia y auditoría ─────────────

    [FactConBaseDeDerivado]
    public async Task Sin_confirmacion_no_cambia_nada()
    {
        var (prof, pac) = Escenario();
        var r = await Derivar(ComoProfesional(prof), pac, new { });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("falta-confirmacion", (await Json(r)).GetProperty("codigo").GetString());
        var r2 = await Derivar(ComoProfesional(prof), pac, new { confirma = false });
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.Equal(0, Filas(pac));
        Assert.False(DerivacionPsiquiatraDAO.EstaDerivado(pac));
    }

    [FactConBaseDeDerivado]
    public async Task Derivar_cambia_el_estado_audita_quien_cuando_que_paciente_y_desde_que_turno_y_no_avisa_a_nadie_si_no_hay_turnos_futuros()
    {
        var (prof, pac) = Escenario();
        var turno = _d.Turno(pac, prof, "Realizado", Ahora.Date.AddDays(-1), TimeSpan.FromHours(10));

        var r = await Derivar(ComoProfesional(prof), pac, new { confirma = true, idTurno = turno });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await Json(r);
        Assert.Equal("Derivado", cuerpo.GetProperty("estado").GetString());
        Assert.False(cuerpo.GetProperty("repetido").GetBoolean());
        Assert.Equal(0, cuerpo.GetProperty("turnosFuturos").GetInt32());

        Assert.True(DerivacionPsiquiatraDAO.EstaDerivado(pac));
        Assert.Equal(1, Vigentes(pac));
        Assert.Equal(turno, Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdTurno FROM DerivacionPsiquiatrica WHERE IdPaciente = @p", ("@p", pac))));
        Assert.Equal(prof, Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdProfesional FROM DerivacionPsiquiatrica WHERE IdPaciente = @p", ("@p", pac))));
        Assert.NotNull(DatosDePrueba.Escalar("SELECT IdUsuario FROM DerivacionPsiquiatrica WHERE IdPaciente = @p", ("@p", pac)));   // quién
        Assert.Equal(1, Auditorias(pac, "Paciente derivado a psiquiatría"));                                                       // cuándo y qué: la fila de auditoría
        var detalle = (string)DatosDePrueba.Escalar("SELECT TOP 1 Detalle FROM LogAuditoria WHERE Entidad = N'Paciente' AND IdEntidad = @p AND Accion = N'Paciente derivado a psiquiatría'", ("@p", pac))!;
        Assert.Contains($"turno #{turno}", detalle);
        Assert.Contains($"paciente #{pac}", detalle);

        // Sin tareas, sin mails y sin avisos (no había turnos por delante).
        Assert.Empty(_mails.Avisos);
        Assert.Equal(0, Avisos("Administrador", _d.Marca) + Avisos("Profesional", _d.Marca));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = @p AND Fecha > CAST(GETDATE() AS DATE)", ("@p", pac)));
    }

    [FactConBaseDeDerivado]
    public async Task Es_idempotente_un_doble_clic_no_duplica_ni_audita_dos_veces()
    {
        var (prof, pac) = Escenario();
        var c = ComoProfesional(prof);
        Assert.Equal(HttpStatusCode.Created, (await Derivar(c, pac)).StatusCode);
        var segunda = await Derivar(c, pac);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.True((await Json(segunda)).GetProperty("repetido").GetBoolean());
        Assert.Equal(1, Filas(pac));
        Assert.Equal(1, Auditorias(pac, "Paciente derivado a psiquiatría"));
    }

    [FactConBaseDeDerivado]
    public async Task Dos_pedidos_a_la_vez_dejan_una_sola_derivacion()
    {
        var (prof, pac) = Escenario();
        var c1 = ComoProfesional(prof);
        var c2 = ComoProfesional(prof);
        var respuestas = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Derivar(i % 2 == 0 ? c1 : c2, pac)));
        Assert.All(respuestas, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, Filas(pac));
        Assert.Equal(1, Auditorias(pac, "Paciente derivado a psiquiatría"));
    }

    // ───────────── Permisos por rol ─────────────

    [FactConBaseDeDerivado]
    public async Task Solo_el_profesional_a_cargo_puede_derivar()
    {
        var (prof, pac) = Escenario();
        var otro = _d.Profesional(_d.Especialidad(), nombre: "Otro " + _d.Marca);

        Assert.Equal(HttpStatusCode.Forbidden, (await Derivar(ComoProfesional(otro), pac)).StatusCode);   // un profesional sin ese paciente
        var ajeno = await Derivar(ComoProfesional(otro), pac);
        Assert.Equal("paciente-ajeno", (await Json(ajeno)).GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await Derivar(ComoPaciente(pac), pac)).StatusCode);       // el paciente no
        Assert.Equal(HttpStatusCode.Forbidden, (await Derivar(ComoCrm(), pac)).StatusCode);               // CRM tampoco
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().PostAsJsonAsync($"/pacientes/{pac}/derivar-psiquiatra", Confirma)).StatusCode);   // sin sesión
        Assert.Equal(0, Filas(pac));

        Assert.Equal(HttpStatusCode.Created, (await Derivar(ComoProfesional(prof), pac)).StatusCode);
    }

    [FactConBaseDeDerivado]
    public async Task Derivar_a_un_paciente_que_no_existe_da_404()
    {
        var (prof, _) = Escenario();
        var r = await Derivar(ComoProfesional(prof), 2_000_000_000);
        Assert.True(r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, r.StatusCode.ToString());
    }

    [FactConBaseDeDerivado]
    public async Task Solo_recepcion_y_CRM_reabren_con_confirmacion_y_auditoria()
    {
        var (prof, pac) = Escenario();
        await Derivar(ComoProfesional(prof), pac);

        // Sin permiso: el profesional (aunque sea el que derivó), el paciente y quien no tiene sesión.
        Assert.Equal(HttpStatusCode.Forbidden, (await Reabrir(ComoProfesional(prof), pac)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Reabrir(ComoPaciente(pac), pac)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().PostAsJsonAsync($"/pacientes/{pac}/reabrir-derivacion", Confirma)).StatusCode);
        Assert.True(DerivacionPsiquiatraDAO.EstaDerivado(pac));

        // Con permiso pero sin confirmar: nada.
        var sinConfirmar = await Reabrir(ComoCrm(), pac, new { confirma = false });
        Assert.Equal(HttpStatusCode.BadRequest, sinConfirmar.StatusCode);
        Assert.True(DerivacionPsiquiatraDAO.EstaDerivado(pac));

        // CRM reabre.
        var r = await Reabrir(ComoCrm(), pac);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False((await Json(r)).GetProperty("repetido").GetBoolean());
        Assert.False(DerivacionPsiquiatraDAO.EstaDerivado(pac));
        Assert.Equal(1, Auditorias(pac, "Paciente derivado reabierto"));
        Assert.NotNull(DatosDePrueba.Escalar("SELECT IdUsuarioReapertura FROM DerivacionPsiquiatrica WHERE IdPaciente = @p", ("@p", pac)));
        Assert.Equal(1, Filas(pac));   // la constancia de la derivación queda

        // Idempotente: reabrir de nuevo no escribe ni audita.
        var otra = await Reabrir(ComoCrm(), pac);
        Assert.Equal(HttpStatusCode.OK, otra.StatusCode);
        Assert.True((await Json(otra)).GetProperty("repetido").GetBoolean());
        Assert.Equal(1, Auditorias(pac, "Paciente derivado reabierto"));
    }

    [FactConBaseDeDerivado]
    public async Task Recepcion_con_el_privilegio_de_admision_tambien_puede_reabrir()
    {
        var (prof, pac) = Escenario();
        var recepcion = _d.Profesional(_d.Especialidad(), nombre: "Recep " + _d.Marca);
        await Derivar(ComoProfesional(prof), pac);
        Assert.Equal(HttpStatusCode.OK, (await Reabrir(ConAdmision(recepcion), pac)).StatusCode);
        Assert.False(DerivacionPsiquiatraDAO.EstaDerivado(pac));
    }

    [FactConBaseDeDerivado]
    public async Task Tras_reabrir_puede_volver_a_reservar_y_tambien_a_ser_derivado_de_nuevo()
    {
        var (prof, pac) = Escenario();
        _d.EstadoTurno("Pendiente");   // el catálogo de la base de pruebas puede no traerlo
        await Derivar(ComoProfesional(prof), pac);
        Assert.Equal(HttpStatusCode.Conflict, (await PedirTurno(ComoPaciente(pac), prof, Manana10)).StatusCode);

        await Reabrir(ComoCrm(), pac);
        Assert.Equal(HttpStatusCode.Created, (await PedirTurno(ComoPaciente(pac), prof, Manana10)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await Derivar(ComoProfesional(prof), pac)).StatusCode);   // una segunda derivación es una fila nueva
        Assert.Equal(2, Filas(pac));
        Assert.Equal(1, Vigentes(pac));
    }

    // ───────────── Bloqueo de turnos por todos los caminos ─────────────

    [FactConBaseDeDerivado]
    public async Task Un_paciente_derivado_no_puede_pedir_turno_por_el_autoservicio()
    {
        var (prof, pac) = Escenario();
        await Derivar(ComoProfesional(prof), pac);
        var r = await PedirTurno(ComoPaciente(pac), prof, Manana10);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var cuerpo = await Json(r);
        Assert.Equal(Bloqueo, cuerpo.GetProperty("mensaje").GetString());
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = @p", ("@p", pac)));
    }

    [FactConBaseDeDerivado]
    public async Task Ni_recepcion_ni_el_profesional_le_pueden_agendar_un_turno_y_tampoco_una_cita_urgente()
    {
        var (prof, pac) = Escenario();
        await Derivar(ComoProfesional(prof), pac);
        var m = Manana10;

        // El profesional propone un turno futuro.
        var propone = await ComoProfesional(prof).PostAsJsonAsync("/turnos/profesional-solicitar",
            new { idPaciente = pac, idProfesional = prof, fecha = m.ToString("yyyy-MM-dd"), hora = m.ToString("HH:mm"), motivo = "[TEST] derivado", esUrgente = false });
        Assert.Equal(HttpStatusCode.Conflict, propone.StatusCode);
        Assert.Equal(Bloqueo, (await Json(propone)).GetProperty("mensaje").GetString());

        // Cita urgente (ahora mismo).
        var urgente = await ComoProfesional(prof).PostAsJsonAsync("/turnos/profesional-solicitar",
            new { idPaciente = pac, idProfesional = prof, fecha = (string?)null, hora = (string?)null, motivo = "[TEST] derivado", esUrgente = true });
        Assert.Equal(HttpStatusCode.Conflict, urgente.StatusCode);

        // Recepción / agendas (el CRM con gestión de agendas agenda en nombre de un profesional).
        var token = _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(_d.Usuario(),
            new SesionModulo(Modulos.Administracion, new[] { Roles.Crm }, new[] { Privilegios.GestionAgendas }, null));
        var agenda = await DatosDePrueba.Cliente(_fabrica, token).PostAsJsonAsync("/turnos/profesional-solicitar",
            new { idPaciente = pac, idProfesional = prof, fecha = m.ToString("yyyy-MM-dd"), hora = m.ToString("HH:mm"), motivo = "[TEST] derivado", esUrgente = false });
        Assert.Equal(HttpStatusCode.Conflict, agenda.StatusCode);

        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = @p", ("@p", pac)));
    }

    [FactConBaseDeDerivado]
    public async Task Reprogramar_un_turno_que_ya_tenia_tampoco_se_puede_pero_cancelarlo_o_editar_otros_datos_si()
    {
        var (prof, pac) = Escenario();
        var turno = _d.Turno(pac, prof, "Confirmado", Ahora.Date.AddDays(5), TimeSpan.FromHours(10));
        await Derivar(ComoProfesional(prof), pac);
        var idConfirmado = _d.EstadoTurno("Confirmado");
        var idCancelado = _d.EstadoTurno("Cancelado");
        var fecha = Ahora.Date.AddDays(5).ToString("yyyy-MM-dd");

        object Cuerpo(string hora, int estado, string motivo) => new { idTurno = turno, idPaciente = pac, idProfesional = prof, idEstadoTurno = estado, fecha, hora, modalidad = "Virtual", motivo };

        var reprograma = await ComoProfesional(prof).PutAsJsonAsync($"/turnos/{turno}", Cuerpo("11:00:00", idConfirmado, "[TEST] mismo"));
        Assert.Equal(HttpStatusCode.Conflict, reprograma.StatusCode);
        Assert.Equal(Bloqueo, (await Json(reprograma)).GetProperty("mensaje").GetString());
        Assert.Equal(TimeSpan.FromHours(10), (TimeSpan)DatosDePrueba.Escalar("SELECT Hora FROM Turno WHERE IdTurno = @t", ("@t", turno))!);   // no se movió

        // Mismo día y hora, otro dato: se puede (no es recibir un turno). Y cancelarlo, también.
        Assert.Equal(HttpStatusCode.OK, (await ComoProfesional(prof).PutAsJsonAsync($"/turnos/{turno}", Cuerpo("10:00:00", idConfirmado, "[TEST] otro motivo"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ComoProfesional(prof).PutAsJsonAsync($"/turnos/{turno}", Cuerpo("10:00:00", idCancelado, "[TEST] otro motivo"))).StatusCode);
    }

    [FactConBaseDeDerivado]
    public async Task El_link_publico_del_profesional_tampoco_deja_reservar()
    {
        var (prof, pac) = Escenario();
        var slug = "derivado-" + _d.Marca;
        DatosDePrueba.Ejecutar("UPDATE Profesional SET Slug = @s, LinkPublicoActivo = 1 WHERE IdProfesional = @p", ("@s", slug), ("@p", prof));
        await Derivar(ComoProfesional(prof), pac);
        var m = Manana10;
        var r = await ComoPaciente(pac).PostAsJsonAsync($"/public/profesionales/{slug}/reservar", new { idPaciente = pac, momento = m, motivo = "[TEST] derivado" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = @p", ("@p", pac)));
    }

    // ───────────── Asignación automática ─────────────

    private async Task<(int Paciente, int Informe, int Prof, HttpClient Recepcion)> FormularioAceptable(bool derivar)
    {
        var esp = _d.Especialidad();
        var prof = _d.Profesional(esp, nombre: "Cand " + _d.Marca);
        foreach (var dia in Enum.GetValues<DayOfWeek>())
            _d.Agenda(prof, DatosDePrueba.NombreDelDia(new DateTime(2026, 11, 1).AddDays((int)dia)), "09:00", "18:00");
        var recepcion = _d.Profesional(_d.Especialidad(), nombre: "Recep " + _d.Marca);
        var pac = _d.Paciente("Ana Gomez " + _d.Marca, mail: $"{_d.Marca}@example.test");
        DatosDePrueba.Ejecutar("UPDATE Paciente SET FechaNacimiento = @f, Telefono = N'11 5555-0001' WHERE IdPaciente = @p", ("@f", new DateTime(1990, 5, 20)), ("@p", pac));
        _d.ConsentimientoFirmado(pac);
        var informe = _d.InformeAdmision(pac, "Pendiente", null, null);
        DatosDePrueba.Ejecutar("UPDATE InformeAdmision SET IdEspecialidad = @e WHERE IdInforme = @i", ("@e", esp), ("@i", informe));
        _d.AlLimpiar("DerivacionPsiquiatrica", "IdPaciente", pac);
        _d.AlLimpiar("PropuestaAsignacion", "IdPaciente", pac);
        if (derivar)
        {
            _d.PacienteProfesional(pac, recepcion);
            Assert.Equal(HttpStatusCode.Created, (await Derivar(ComoProfesional(recepcion), pac)).StatusCode);
        }
        return (pac, informe, prof, ConAdmision(recepcion));
    }

    [FactConBaseDeDerivadoYAsignacion]
    public async Task La_asignacion_automatica_no_propone_a_un_paciente_derivado_y_lo_deja_para_resolver_con_el_motivo_claro()
    {
        var (pac, informe, _, recep) = await FormularioAceptable(derivar: true);
        var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", new { confirmo = true });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM PropuestaAsignacion WHERE IdInformeAdmision = @i", ("@i", informe)));
        Assert.Equal("Por resolver", DatosDePrueba.Escalar("SELECT AsignacionEstado FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe)) as string);
        Assert.Contains(Bloqueo, (string)DatosDePrueba.Escalar("SELECT AsignacionMotivo FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe))!);
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = @p", ("@p", pac)));
    }

    [FactConBaseDeDerivadoYAsignacion]
    public async Task Si_lo_derivan_despues_de_la_propuesta_confirmarla_se_rechaza_y_no_se_crea_turno_ni_vinculo()
    {
        var (pac, informe, prof, recep) = await FormularioAceptable(derivar: false);
        Assert.Equal(HttpStatusCode.OK, (await recep.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", new { confirmo = true })).StatusCode);
        var propuesta = Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdPropuesta FROM PropuestaAsignacion WHERE IdInformeAdmision = @i AND Estado = N'Pendiente'", ("@i", informe)));

        // Lo derivan (el vínculo con otro profesional lo habilita a derivar; la fila alcanza para el bloqueo).
        DatosDePrueba.Ejecutar("INSERT INTO DerivacionPsiquiatrica (IdPaciente, IdProfesional) VALUES (@p, @r)", ("@p", pac), ("@r", prof));

        var r = await ComoPaciente(pac).PostAsync($"/propuestas-asignacion/{propuesta}/aceptar", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("paciente-derivado", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = @p", ("@p", pac)));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @p AND IdProfesional = @r", ("@p", pac), ("@r", prof)));
        Assert.Equal("Cancelada", DatosDePrueba.Escalar("SELECT Estado FROM PropuestaAsignacion WHERE IdPropuesta = @i", ("@i", propuesta)) as string);
        Assert.Equal("Por resolver", DatosDePrueba.Escalar("SELECT AsignacionEstado FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe)) as string);
    }

    // ───────────── Turnos futuros: no se cancelan solos, se avisa ─────────────

    [FactConBaseDeDerivado]
    public async Task Los_turnos_futuros_no_se_cancelan_y_se_avisa_al_profesional_y_a_recepcion()
    {
        var (prof, pac) = Escenario("Paciente Futuro " + Guid.NewGuid().ToString("N")[..6]);
        var marca = _d.Marca;
        // El nombre del paciente lleva la marca de la prueba, así los avisos se limpian.
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Nombre = @n WHERE IdPaciente = @p", ("@n", "Pac " + marca), ("@p", pac));
        var t1 = _d.Turno(pac, prof, "Confirmado", Ahora.Date.AddDays(4), TimeSpan.FromHours(10));
        var t2 = _d.Turno(pac, prof, "Pendiente", Ahora.Date.AddDays(11), TimeSpan.FromHours(15));
        var viejo = _d.Turno(pac, prof, "Realizado", Ahora.Date.AddDays(-3), TimeSpan.FromHours(10));

        var r = await Derivar(ComoProfesional(prof), pac);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await Json(r);
        Assert.Equal(2, cuerpo.GetProperty("turnosFuturos").GetInt32());
        Assert.Contains("2 turnos por delante", cuerpo.GetProperty("mensaje").GetString());

        // Siguen como estaban: lo cancela una persona, no el sistema.
        string Estado(int t) => (string)DatosDePrueba.Escalar("SELECT et.Descripcion FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @t", ("@t", t))!;
        Assert.Equal("Confirmado", Estado(t1));
        Assert.Equal("Pendiente", Estado(t2));
        Assert.Equal("Realizado", Estado(viejo));

        // Un aviso a recepción y otro al profesional; ningún mail.
        Assert.Equal(1, Avisos("Administrador", marca));
        Assert.Equal(1, Avisos("Profesional", marca));
        Assert.Contains("No se cancelaron solos", (string)DatosDePrueba.Escalar("SELECT TOP 1 Mensaje FROM NotificacionInterna WHERE Destinatario = N'Profesional' AND IdProfesional = @r AND Tipo = N'derivado-turnos-futuros' AND Mensaje LIKE '%" + marca + "%'", ("@r", prof))!);
        Assert.Empty(_mails.Avisos);

        // Repetir no avisa de nuevo.
        await Derivar(ComoProfesional(prof), pac);
        Assert.Equal(1, Avisos("Administrador", marca));
    }

    [Fact]
    public void El_aviso_por_turnos_futuros_concuerda_en_singular_y_plural_y_no_lleva_datos_clinicos()
    {
        Assert.Equal("Ana fue derivado y todavía tiene 1 turno por delante. No se cancelaron solos: decidí si corresponde cancelarlos.", DerivacionPsiquiatraReglas.AvisoTurnosFuturos("Ana", 1));
        Assert.Contains("3 turnos por delante", DerivacionPsiquiatraReglas.AvisoTurnosFuturos("Ana", 3));
        Assert.Equal("Este paciente fue derivado y no puede reservar turnos con este equipo.", DerivacionPsiquiatraReglas.MensajeBloqueo);
    }

    [Fact]
    public void Los_codigos_de_rechazo_tienen_su_estado_y_un_mensaje_en_espanol()
    {
        foreach (var c in new[] { DerivacionPsiquiatraReglas.FaltaMigracion34, DerivacionPsiquiatraReglas.FaltaConfirmacion, DerivacionPsiquiatraReglas.PacienteInexistente,
                     DerivacionPsiquiatraReglas.PacienteAjeno, DerivacionPsiquiatraReglas.SesionNoCoincide, DerivacionPsiquiatraReglas.NoEstaDerivado })
            Assert.False(string.IsNullOrWhiteSpace(DerivacionPsiquiatraReglas.Mensaje(c)), c);
        Assert.Equal(404, DerivacionPsiquiatraReglas.Status(DerivacionPsiquiatraReglas.PacienteInexistente));
        Assert.Equal(403, DerivacionPsiquiatraReglas.Status(DerivacionPsiquiatraReglas.PacienteAjeno));
        Assert.Equal(400, DerivacionPsiquiatraReglas.Status(DerivacionPsiquiatraReglas.FaltaConfirmacion));
        Assert.Equal(409, DerivacionPsiquiatraReglas.Status(DerivacionPsiquiatraReglas.FaltaMigracion34));
    }
}
