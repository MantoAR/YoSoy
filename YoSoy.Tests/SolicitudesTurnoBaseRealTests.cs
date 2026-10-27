using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Se OMITE si la base no tiene la migración 36 (vencimiento de las solicitudes de turno).</summary>
public sealed class FactConBaseDeSolicitudesAttribute : FactAttribute
{
    public FactConBaseDeSolicitudesAttribute()
    {
        if (Convert.ToInt32(DatosDePrueba.Escalar("SELECT CASE WHEN COL_LENGTH('dbo.Turno', 'SolicitudExpiraEn') IS NOT NULL THEN 1 ELSE 0 END")) != 1)
            Skip = "La base no tiene la migración migrations/add_solicitud_turno_vencimiento.sql.";
    }
}

// Los turnos que el paciente pide por su cuenta (autoservicio, link público, bot) quedan PENDIENTES hasta que el profesional los acepta: reservan el horario, no habilitan la sala, el profesional los
// acepta o rechaza, y si no responde a tiempo vencen. Recepción y el profesional conservan su camino directo. Nadie recibe mails: el servicio de correo es un espía.
[Trait("Categoria", "Integracion")]
public class SolicitudesTurnoBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly EmailServiceEspia _mails = new();
    private readonly WebApplicationFactory<Program> _fabrica;
    private static readonly object Sin = new { };

    public SolicitudesTurnoBaseRealTests()
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

    private DateTime Ahora => _fabrica.Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime;
    private HttpClient ComoProfesional(int id) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(id), Roles.Profesional, idProfesional: id);
    private HttpClient ComoPaciente(int id) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(id), Roles.Paciente, idPaciente: id);
    private HttpClient ComoCrm() => DatosDePrueba.Cliente(_fabrica, _d.Usuario(), Roles.Crm);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }

    /// <summary>Un profesional con agenda todos los días de 9 a 18, y dos pacientes (con el consentimiento firmado, para poder confirmar).</summary>
    private (int Prof, int Pac, int Otro) Escenario()
    {
        var prof = _d.Profesional(_d.Especialidad(), nombre: "Prof " + _d.Marca);
        foreach (var dia in Enum.GetValues<DayOfWeek>())
            _d.Agenda(prof, DatosDePrueba.NombreDelDia(new DateTime(2026, 11, 1).AddDays((int)dia)), "09:00", "18:00");
        var pac = _d.Paciente("Paciente " + _d.Marca, mail: $"{_d.Marca}@example.test");
        var otro = _d.Paciente("Otro " + _d.Marca, mail: $"otro{_d.Marca}@example.test");
        foreach (var p in new[] { pac, otro })
        {
            _d.PacienteProfesional(p, prof);
            _d.ConsentimientoFirmado(p);
            _d.AlLimpiar("DerivacionPsiquiatrica", "IdPaciente", p);
            _d.AlLimpiar("Turno", "IdPaciente", p);
            _d.AlLimpiar("Notificacion", "IdPaciente", p);
        }
        return (prof, pac, otro);
    }

    private DateTime Dentro(int dias, int hora = 10) => Ahora.Date.AddDays(dias).AddHours(hora);

    private Task<HttpResponseMessage> Pedir(HttpClient paciente, int prof, DateTime momento) =>
        paciente.PostAsJsonAsync("/turnos/solicitar", new { idProfesional = prof, fecha = momento.ToString("yyyy-MM-dd"), hora = momento.ToString("HH:mm:ss"), motivo = "[TEST] solicitud" });

    private async Task<int> PedirYObtenerId(int pac, int prof, DateTime momento)
    {
        var r = await Pedir(ComoPaciente(pac), prof, momento);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await Json(r)).GetProperty("idTurno").GetInt32();
    }

    private static string Estado(int turno) => (string)DatosDePrueba.Escalar("SELECT et.Descripcion FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @t", ("@t", turno))!;
    private static int Auditorias(int turno, string accion) => DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = N'Turno' AND IdEntidad = @i AND Accion = @a", ("@i", turno), ("@a", accion));
    private static int AvisosAlPaciente(int pac, string titulo) => DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p AND Titulo = @t", ("@p", pac), ("@t", titulo));
    private int AvisosAlProfesional(string tipo) => DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Tipo = @t AND Mensaje LIKE @m", ("@t", tipo), ("@m", "%" + _d.Marca + "%"));

    // ───────────── La solicitud nace pendiente ─────────────

    [FactConBaseDeSolicitudes]
    public async Task Cuando_el_paciente_pide_un_turno_queda_pendiente_con_vencimiento_y_reserva_el_horario()
    {
        var (prof, pac, otro) = Escenario();
        var momento = Dentro(3);

        var turno = await PedirYObtenerId(pac, prof, momento);

        Assert.Equal("Pendiente", Estado(turno));
        Assert.Equal("paciente", DatosDePrueba.Escalar("SELECT CreadoPor FROM Turno WHERE IdTurno = @t", ("@t", turno)));
        var expira = (DateTime)DatosDePrueba.Escalar("SELECT SolicitudExpiraEn FROM Turno WHERE IdTurno = @t", ("@t", turno))!;
        Assert.InRange(expira, Ahora.AddHours(23), Ahora.AddHours(25));      // turno lejano: 24 hs
        // El horario queda reservado: otro paciente no lo puede tomar.
        var choque = await Pedir(ComoPaciente(otro), prof, momento);
        Assert.Equal(HttpStatusCode.Conflict, choque.StatusCode);
        // Y no aparece como horario libre en la disponibilidad del profesional.
        var libres = _fabrica.Services.GetRequiredService<TurnoReglasService>().HorariosLibresDelProfesional(prof, 10, 500);
        Assert.DoesNotContain(momento, libres);
    }

    [FactConBaseDeSolicitudes]
    public async Task La_solicitud_pendiente_no_cuenta_como_turno_confirmado_para_la_sala_ni_para_los_recordatorios()
    {
        var (prof, pac, _) = Escenario();
        var turno = await PedirYObtenerId(pac, prof, Dentro(3));

        // La sala exige un turno Confirmado (AccesoSalaService): un Pendiente no la abre.
        var sala = _fabrica.Services.GetRequiredService<AccesoSalaService>();
        var r = sala.Evaluar(new Identidad(_d.UsuarioDePaciente(pac), Roles.Paciente, pac, null), "turno", turno);
        Assert.False(r.Ok);
        // El recordatorio busca solo Confirmados.
        var ids = new RecordatorioDAO().TurnosConfirmadosSinRecordar(Ahora, Ahora.AddDays(30), NotificadorTurnos.CanalesRecordatorio, 3);
        Assert.DoesNotContain(turno, ids);
    }

    [FactConBaseDeSolicitudes]
    public async Task El_profesional_ve_la_solicitud_en_su_lista_y_en_la_campanita()
    {
        var (prof, pac, _) = Escenario();
        var turno = await PedirYObtenerId(pac, prof, Dentro(3));

        var lista = await Json(await ComoProfesional(prof).GetAsync($"/profesionales/{prof}/solicitudes-turno"));
        var campana = await Json(await ComoProfesional(prof).GetAsync($"/profesionales/{prof}/seguimiento"));

        Assert.Contains(lista.EnumerateArray(), s => s.GetProperty("idTurno").GetInt32() == turno);
        Assert.Contains(campana.GetProperty("solicitudes").EnumerateArray(), s => s.GetProperty("idTurno").GetInt32() == turno);
    }

    // ───────────── Aceptar ─────────────

    [FactConBaseDeSolicitudes]
    public async Task Aceptar_confirma_el_turno_audita_avisa_al_paciente_y_hacerlo_dos_veces_no_duplica_nada()
    {
        var (prof, pac, _) = Escenario();
        var turno = await PedirYObtenerId(pac, prof, Dentro(3));
        var c = ComoProfesional(prof);

        var r1 = await c.PostAsJsonAsync($"/turnos/{turno}/aceptar-solicitud", Sin);
        var r2 = await c.PostAsJsonAsync($"/turnos/{turno}/aceptar-solicitud", Sin);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.False((await Json(r1)).GetProperty("repetido").GetBoolean());
        Assert.True((await Json(r2)).GetProperty("repetido").GetBoolean());
        Assert.Equal("Confirmado", Estado(turno));
        Assert.Equal(true, DatosDePrueba.Escalar("SELECT ConfirmadoPaciente FROM Turno WHERE IdTurno = @t", ("@t", turno)));
        Assert.Equal(1, Auditorias(turno, "Solicitud de turno aceptada"));
        Assert.Equal(1, AvisosAlPaciente(pac, "Turno confirmado"));
    }

    [FactConBaseDeSolicitudes]
    public async Task Aceptar_respeta_el_consentimiento_y_el_bloqueo_del_paciente_derivado()
    {
        var (prof, pac, otro) = Escenario();
        var turno = await PedirYObtenerId(pac, prof, Dentro(3));
        if (DerivacionPsiquiatraDAO.Disponible()) DatosDePrueba.Ejecutar("INSERT INTO DerivacionPsiquiatrica (IdPaciente, IdProfesional) VALUES (@p, @m)", ("@p", pac), ("@m", prof));
        var conDerivacion = DerivacionPsiquiatraDAO.Disponible();

        var r = await ComoProfesional(prof).PostAsJsonAsync($"/turnos/{turno}/aceptar-solicitud", Sin);

        if (conDerivacion)
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Pendiente", Estado(turno));
            // Lo que sí puede hacer es rechazarla.
            Assert.Equal(HttpStatusCode.OK, (await ComoProfesional(prof).PostAsJsonAsync($"/turnos/{turno}/rechazar-solicitud", Sin)).StatusCode);
        }
        // Sin consentimiento firmado (RN-02) tampoco se puede confirmar.
        DatosDePrueba.Ejecutar("UPDATE Consentimiento SET Estado = N'Inactivo' WHERE IdPaciente = @p", ("@p", otro));
        var turno2 = await PedirYObtenerId(otro, prof, Dentro(4));
        var r2 = await ComoProfesional(prof).PostAsJsonAsync($"/turnos/{turno2}/aceptar-solicitud", Sin);
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        Assert.Equal("Pendiente", Estado(turno2));
    }

    // ───────────── Rechazar ─────────────

    [FactConBaseDeSolicitudes]
    public async Task Rechazar_libera_el_horario_avisa_al_paciente_con_el_motivo_y_es_idempotente()
    {
        var (prof, pac, otro) = Escenario();
        var momento = Dentro(3);
        var turno = await PedirYObtenerId(pac, prof, momento);
        var c = ComoProfesional(prof);

        var r1 = await c.PostAsJsonAsync($"/turnos/{turno}/rechazar-solicitud", new { motivo = "Ese día no atiendo. [TEST]" });
        var r2 = await c.PostAsJsonAsync($"/turnos/{turno}/rechazar-solicitud", new { motivo = "otra vez" });

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.True((await Json(r2)).GetProperty("repetido").GetBoolean());
        Assert.Equal("Cancelado", Estado(turno));
        Assert.Equal(1, Auditorias(turno, "Solicitud de turno rechazada"));
        Assert.Equal(1, AvisosAlPaciente(pac, "Tu solicitud de turno no fue aceptada"));
        var texto = (string)DatosDePrueba.Escalar("SELECT TOP 1 Mensaje FROM Notificacion WHERE IdPaciente = @p AND Titulo = N'Tu solicitud de turno no fue aceptada'", ("@p", pac))!;
        Assert.Contains("Ese día no atiendo. [TEST]", texto);
        Assert.Contains("elegir otro", texto);
        // El horario quedó libre: otro paciente lo puede pedir.
        Assert.Equal(HttpStatusCode.Created, (await Pedir(ComoPaciente(otro), prof, momento)).StatusCode);
        // Y una solicitud rechazada ya no se puede aceptar.
        var aceptar = await c.PostAsJsonAsync($"/turnos/{turno}/aceptar-solicitud", Sin);
        Assert.Equal(HttpStatusCode.Conflict, aceptar.StatusCode);
        Assert.Equal("solicitud-cerrada", (await Json(aceptar)).GetProperty("codigo").GetString());
    }

    // ───────────── Vencimiento ─────────────

    [FactConBaseDeSolicitudes]
    public async Task La_solicitud_sin_respuesta_a_tiempo_vence_libera_el_horario_y_avisa_a_ambos_una_sola_vez()
    {
        var (prof, pac, otro) = Escenario();
        var momento = Dentro(3);
        var turno = await PedirYObtenerId(pac, prof, momento);
        DatosDePrueba.Ejecutar("UPDATE Turno SET SolicitudExpiraEn = DATEADD(MINUTE, -5, GETDATE()) WHERE IdTurno = @t", ("@t", turno));

        var primera = SolicitudesTurnoDAO.VencerSolicitudes(Ahora);
        var segunda = SolicitudesTurnoDAO.VencerSolicitudes(Ahora);

        Assert.Equal(1, primera);
        Assert.Equal(0, segunda);                                           // idempotente
        Assert.Equal("Cancelado", Estado(turno));
        Assert.Equal(1, AvisosAlPaciente(pac, "Tu solicitud de turno venció"));
        Assert.Equal(1, AvisosAlProfesional("solicitud-vencida"));
        Assert.Equal(1, Auditorias(turno, "Solicitud de turno vencida"));
        Assert.Equal(HttpStatusCode.Created, (await Pedir(ComoPaciente(otro), prof, momento)).StatusCode);   // horario liberado
    }

    [FactConBaseDeSolicitudes]
    public async Task Una_solicitud_vencida_no_se_puede_aceptar_aunque_la_pasada_todavia_no_haya_corrido()
    {
        var (prof, pac, _) = Escenario();
        var turno = await PedirYObtenerId(pac, prof, Dentro(3));
        DatosDePrueba.Ejecutar("UPDATE Turno SET SolicitudExpiraEn = DATEADD(MINUTE, -5, GETDATE()) WHERE IdTurno = @t", ("@t", turno));

        var r = await ComoProfesional(prof).PostAsJsonAsync($"/turnos/{turno}/aceptar-solicitud", Sin);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("solicitud-cerrada", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal("Cancelado", Estado(turno));
    }

    [FactConBaseDeSolicitudes]
    public async Task Las_solicitudes_que_todavia_estan_a_tiempo_no_vencen()
    {
        var (prof, pac, _) = Escenario();
        var turno = await PedirYObtenerId(pac, prof, Dentro(3));

        Assert.Equal(0, SolicitudesTurnoDAO.VencerSolicitudes(Ahora));

        Assert.Equal("Pendiente", Estado(turno));
    }

    // ───────────── Lo que no requiere aceptación ─────────────

    [FactConBaseDeSolicitudes]
    public async Task El_turno_que_propone_el_profesional_o_recepcion_conserva_su_camino_y_no_es_una_solicitud()
    {
        var (prof, pac, _) = Escenario();
        var momento = Dentro(3);

        var r = await ComoProfesional(prof).PostAsJsonAsync("/turnos/profesional-solicitar", new { idPaciente = pac, fecha = momento.ToString("yyyy-MM-dd"), hora = momento.ToString("HH:mm:ss"), motivo = "[TEST] propuesto", esUrgente = false });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var turno = (await Json(r)).GetProperty("idTurno").GetInt32();
        Assert.Equal("profesional", DatosDePrueba.Escalar("SELECT CreadoPor FROM Turno WHERE IdTurno = @t", ("@t", turno)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdTurno = @t AND SolicitudExpiraEn IS NULL", ("@t", turno)));   // sin vencimiento
        var lista = await Json(await ComoProfesional(prof).GetAsync($"/profesionales/{prof}/solicitudes-turno"));
        Assert.DoesNotContain(lista.EnumerateArray(), s => s.GetProperty("idTurno").GetInt32() == turno);
        var aceptar = await ComoProfesional(prof).PostAsJsonAsync($"/turnos/{turno}/aceptar-solicitud", Sin);
        Assert.Equal(HttpStatusCode.Conflict, aceptar.StatusCode);
        Assert.Equal("no-es-solicitud", (await Json(aceptar)).GetProperty("codigo").GetString());
        Assert.Equal(0, SolicitudesTurnoDAO.VencerSolicitudes(Ahora.AddDays(30)));   // y nunca vence solo
        Assert.Equal("Pendiente", Estado(turno));
    }

    // ───────────── Permisos por rol ─────────────

    [FactConBaseDeSolicitudes]
    public async Task Solo_el_profesional_dueno_del_turno_acepta_o_rechaza()
    {
        var (prof, pac, _) = Escenario();
        var turno = await PedirYObtenerId(pac, prof, Dentro(3));
        var ajeno = _d.Profesional(_d.Especialidad(), nombre: "Ajeno " + _d.Marca);

        Assert.Equal(HttpStatusCode.NotFound, (await ComoProfesional(ajeno).PostAsJsonAsync($"/turnos/{turno}/aceptar-solicitud", Sin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ComoProfesional(ajeno).PostAsJsonAsync($"/turnos/{turno}/rechazar-solicitud", Sin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ComoPaciente(pac).PostAsJsonAsync($"/turnos/{turno}/aceptar-solicitud", Sin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ComoCrm().PostAsJsonAsync($"/turnos/{turno}/aceptar-solicitud", Sin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ComoProfesional(ajeno).GetAsync($"/profesionales/{prof}/solicitudes-turno")).StatusCode);
        Assert.Equal("Pendiente", Estado(turno));
    }

    [FactConBaseDeSolicitudes]
    public async Task El_motivo_de_rechazo_no_puede_ser_larguisimo()
    {
        var (prof, pac, _) = Escenario();
        var turno = await PedirYObtenerId(pac, prof, Dentro(3));

        var r = await ComoProfesional(prof).PostAsJsonAsync($"/turnos/{turno}/rechazar-solicitud", new { motivo = new string('x', 301) });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Pendiente", Estado(turno));
    }
}

// El vencimiento de una solicitud (puro, sin base): el menor entre 24 hs y el turno menos 2 hs, con un piso de 30 minutos y nunca después del turno.
public class SolicitudesTurnoReglasTests
{
    private static readonly DateTime Creada = new(2026, 11, 2, 9, 0, 0);

    [Fact]
    public void Un_turno_lejano_vence_a_las_24_horas() =>
        Assert.Equal(Creada.AddHours(24), SolicitudesTurnoReglas.Vencimiento(Creada, Creada.AddDays(10)));

    [Fact]
    public void Un_turno_en_menos_de_26_horas_vence_2_horas_antes_del_turno() =>
        Assert.Equal(Creada.AddHours(10), SolicitudesTurnoReglas.Vencimiento(Creada, Creada.AddHours(12)));

    [Fact]
    public void Un_turno_cercano_no_nace_vencido_pero_nunca_vence_despues_de_empezar()
    {
        Assert.Equal(Creada.AddMinutes(30), SolicitudesTurnoReglas.Vencimiento(Creada, Creada.AddHours(1)));       // piso de 30 minutos
        Assert.Equal(Creada.AddMinutes(20), SolicitudesTurnoReglas.Vencimiento(Creada, Creada.AddMinutes(20)));    // jamás después del inicio
    }
}
