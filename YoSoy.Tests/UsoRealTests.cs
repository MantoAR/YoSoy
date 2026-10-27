using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// Ajustes de uso real (prompt 05): disponibilidad del paciente (misma función que la propuesta automática), estado de la sala en un solo lugar y turnos pasados.
// Contra SQL Server de verdad con datos propios. Nadie recibe mails: el servicio de correo es un espía.
[Trait("Categoria", "Integracion")]
public class DisponibilidadYSalaBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly EmailServiceEspia _mails = new();
    private readonly WebApplicationFactory<Program> _fabrica;

    public DisponibilidadYSalaBaseRealTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(_mails); }));
    }

    public void Dispose() { _fabrica.Dispose(); _d.Dispose(); }

    private DateTime Ahora => _fabrica.Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime;
    private HttpClient ComoProfesional(int id) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(id), Roles.Profesional, idProfesional: id);
    private HttpClient ComoPaciente(int id) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(id), Roles.Paciente, idPaciente: id);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }

    /// <summary>Un profesional con agenda todos los días de 9 a 18 (cada 30 minutos) y un paciente a su cargo.</summary>
    private (int Prof, int Pac) Escenario(bool conAgenda = true)
    {
        var prof = _d.Profesional(_d.Especialidad(), nombre: "Prof " + _d.Marca);
        if (conAgenda)
            foreach (var dia in Enum.GetValues<DayOfWeek>())
                _d.Agenda(prof, DatosDePrueba.NombreDelDia(new DateTime(2026, 11, 1).AddDays((int)dia)), "09:00", "18:00");
        var pac = _d.Paciente("Paciente " + _d.Marca, mail: $"{_d.Marca}@example.test");
        _d.PacienteProfesional(pac, prof);
        _d.AlLimpiar("DerivacionPsiquiatrica", "IdPaciente", pac);
        _d.AlLimpiar("Turno", "IdPaciente", pac);
        return (prof, pac);
    }

    private Task<HttpResponseMessage> Disponibilidad(HttpClient c, int pac, int prof, int? dias = null, int? ignorar = null) =>
        c.GetAsync($"/pacientes/{pac}/disponibilidad?idProfesional={prof}" + (dias is null ? "" : $"&dias={dias}") + (ignorar is null ? "" : $"&ignorarTurno={ignorar}"));

    // ───────────── Bloque A: disponibilidad ─────────────

    [Fact]
    public async Task Los_horarios_del_paciente_salen_de_la_misma_funcion_que_la_propuesta_automatica()
    {
        var (prof, pac) = Escenario();
        var asignacion = _fabrica.Services.GetRequiredService<AsignacionAutomaticaService>();

        var j = await Json(await Disponibilidad(ComoPaciente(pac), pac, prof, 14));

        var esperado = asignacion.HorariosLibres(prof, Ahora, null, null, DisponibilidadReglas.AnticipacionMinimaPedidoHoras, 14).Where(h => h > Ahora)
            .Select(h => h.ToString("yyyy-MM-ddTHH:mm")).OrderBy(x => x).ToList();
        var recibido = j.GetProperty("dias").EnumerateArray()
            .SelectMany(d => d.GetProperty("horas").EnumerateArray().Select(h => d.GetProperty("fecha").GetString() + "T" + h.GetString())).OrderBy(x => x).ToList();
        Assert.NotEmpty(recibido);
        Assert.Equal(esperado, recibido);
        Assert.Equal(recibido.Count, j.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("mensaje").ValueKind);
    }

    [Fact]
    public async Task Las_franjas_de_admision_y_las_vencidas_no_se_ofrecen_y_se_explica_por_que_no_hay_nada()
    {
        var (prof, pac) = Escenario(conAgenda: false);
        var hoy = Ahora.Date;
        var franja = _d.Agenda(prof, DatosDePrueba.NombreDelDia(hoy.AddDays(2)), "09:00", "12:00");
        DatosDePrueba.Ejecutar("UPDATE Agenda SET EsAdmision = 1 WHERE IdAgenda = @a", ("@a", franja));   // franja de entrevistas: no se ofrece a los pacientes
        var vencida = _d.Agenda(prof, DatosDePrueba.NombreDelDia(hoy.AddDays(3)), "09:00", "12:00");
        DatosDePrueba.Ejecutar("UPDATE Agenda SET FechaFin = @f WHERE IdAgenda = @a", ("@f", hoy.AddDays(-1)), ("@a", vencida));   // vigencia terminada (migración 30)

        var j = await Json(await Disponibilidad(ComoPaciente(pac), pac, prof, 14));

        Assert.Equal(0, j.GetProperty("total").GetInt32());
        Assert.Equal("sin-agenda", j.GetProperty("codigo").GetString());
        Assert.Contains("todavía no cargó horarios", j.GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task Sin_horarios_libres_en_los_proximos_dias_devuelve_un_mensaje_claro_y_no_un_calendario_muerto()
    {
        var (prof, pac) = Escenario(conAgenda: false);
        _d.Agenda(prof, DatosDePrueba.NombreDelDia(Ahora.Date.AddDays(5)), "09:00", "12:00");   // atiende, pero no en los próximos 2 días

        var j = await Json(await Disponibilidad(ComoPaciente(pac), pac, prof, 2));

        Assert.Equal(0, j.GetProperty("total").GetInt32());
        Assert.Equal("sin-horarios", j.GetProperty("codigo").GetString());
        Assert.Contains("no tiene horarios libres en los próximos 2 días", j.GetProperty("mensaje").GetString());
        Assert.Empty(j.GetProperty("dias").EnumerateArray());
    }

    [Fact]
    public async Task Para_hoy_mismo_el_rechazo_explica_el_motivo_en_vez_de_dejar_el_calendario_vacio()
    {
        var (prof, pac) = Escenario();
        var j = await Json(await Disponibilidad(ComoPaciente(pac), pac, prof));
        Assert.Contains("hoy", j.GetProperty("mensajeHoy").GetString());
        Assert.Contains("recepción", j.GetProperty("mensajeHoy").GetString());

        // Pedir un horario que ya pasó hoy: el mensaje dice qué hacer.
        var reglas = _fabrica.Services.GetRequiredService<TurnoReglasService>();
        var r = reglas.ValidarSolicitud(pac, prof, Ahora.AddHours(-1));
        Assert.False(r.Ok);
        Assert.StartsWith("Ese horario ya pasó.", r.Mensaje);
        Assert.Contains("recepción", r.Mensaje);
        Assert.Contains("Elegí otro día", reglas.ValidarSolicitud(pac, prof, Ahora.AddDays(-1)).Mensaje);
    }

    [Fact]
    public async Task Un_dia_con_turno_propio_no_se_ofrece_salvo_el_que_se_esta_reprogramando()
    {
        var (prof, pac) = Escenario();
        var manana = Ahora.Date.AddDays(2);
        var turno = _d.Turno(pac, prof, "Confirmado", manana, TimeSpan.FromHours(10));
        var c = ComoPaciente(pac);

        var sin = await Json(await Disponibilidad(c, pac, prof, 7));
        var reprogramando = await Json(await Disponibilidad(c, pac, prof, 7, turno));

        var dia = manana.ToString("yyyy-MM-dd");
        Assert.DoesNotContain(sin.GetProperty("dias").EnumerateArray(), d => d.GetProperty("fecha").GetString() == dia);      // RN-07
        Assert.Contains(reprogramando.GetProperty("dias").EnumerateArray(), d => d.GetProperty("fecha").GetString() == dia);  // el turno que se cancela no bloquea su día
    }

    [Fact]
    public async Task Un_turno_de_hoy_que_ya_paso_sin_sesion_no_impide_pedir_otro_el_mismo_dia_pero_uno_en_curso_con_sesion_si()
    {
        var (prof, pac) = Escenario();
        var inicio = Ahora.AddHours(-3);
        var perdido = _d.Turno(pac, prof, "Confirmado", inicio.Date, new TimeSpan(inicio.Hour, inicio.Minute, 0));   // empezó hace 3 horas y nadie entró
        var repo = _fabrica.Services.GetRequiredService<ITurnoRepositorio>();

        Assert.Equal(1, repo.ContarTurnosActivos(pac, prof, inicio.Date));            // la cuenta de siempre
        Assert.Equal(0, repo.ContarTurnosActivos(pac, prof, inicio.Date, Ahora));     // con el reloj de la clínica: ese horario se perdió

        var sesion = Convert.ToInt32(DatosDePrueba.Escalar("INSERT INTO SesionClinica (IdTurno, IdPaciente, IdProfesional, FechaHora) VALUES (@t, @p, @m, GETDATE()); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@t", perdido), ("@p", pac), ("@m", prof)));
        _d.AlLimpiar("SesionClinica", "IdSesion", sesion);
        Assert.Equal(1, repo.ContarTurnosActivos(pac, prof, inicio.Date, Ahora));     // con sesión sí hubo atención: cuenta
    }

    [Fact]
    public async Task Un_paciente_derivado_no_ve_horarios_y_el_motivo_se_explica()
    {
        if (!DerivacionPsiquiatraDAO.Disponible()) return;
        var (prof, pac) = Escenario();
        DatosDePrueba.Ejecutar("INSERT INTO DerivacionPsiquiatrica (IdPaciente, IdProfesional) VALUES (@p, @m)", ("@p", pac), ("@m", prof));

        var j = await Json(await Disponibilidad(ComoPaciente(pac), pac, prof));

        Assert.Equal("paciente-derivado", j.GetProperty("codigo").GetString());
        Assert.Equal(0, j.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Solo_el_propio_paciente_consulta_su_disponibilidad()
    {
        var (prof, pac) = Escenario();
        var otro = _d.Paciente("Otro " + _d.Marca);

        Assert.Equal(HttpStatusCode.Forbidden, (await Disponibilidad(ComoPaciente(otro), pac, prof)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Disponibilidad(ComoProfesional(prof), pac, prof)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ComoPaciente(pac).GetAsync($"/pacientes/{pac}/disponibilidad?idProfesional=0")).StatusCode);
    }

    // ───────────── Bloque B: estado de la sala ─────────────

    [Fact]
    public async Task El_estado_de_la_sala_lo_da_el_servidor_y_solo_para_los_turnos_propios()
    {
        var (prof, pac) = Escenario();
        var viejo = Ahora.AddHours(-3);
        var cerrado = _d.Turno(pac, prof, "Confirmado", viejo.Date, new TimeSpan(viejo.Hour, viejo.Minute, 0));
        var proximo = Ahora.AddHours(4);
        var futuro = _d.Turno(pac, prof, "Confirmado", proximo.Date, new TimeSpan(proximo.Hour, proximo.Minute, 0));
        var ajeno = _d.Profesional(_d.Especialidad(), nombre: "Ajeno " + _d.Marca);

        var j = await Json(await ComoProfesional(prof).GetAsync($"/salas/estado?ids={cerrado},{futuro},999999999"));
        var delAjeno = await Json(await ComoProfesional(ajeno).GetAsync($"/salas/estado?ids={cerrado}"));
        var delPaciente = await Json(await ComoPaciente(pac).GetAsync($"/salas/estado?ids={cerrado}"));

        var salas = j.GetProperty("salas").EnumerateArray().ToDictionary(s => s.GetProperty("idTurno").GetInt32(), s => s.GetProperty("fase").GetString());
        Assert.Equal("cerrada", salas[cerrado]);
        Assert.Equal("por-abrir", salas[futuro]);
        Assert.Equal(2, salas.Count);   // el id inexistente se omite
        Assert.Empty(delAjeno.GetProperty("salas").EnumerateArray());
        Assert.Equal("cerrada", delPaciente.GetProperty("salas")[0].GetProperty("fase").GetString());
    }

    [Fact]
    public async Task La_tarjeta_Proximo_turno_no_muestra_un_turno_que_ya_paso_ni_uno_ausente()
    {
        var (prof, pac) = Escenario();
        var viejo = Ahora.AddHours(-3);
        _d.Turno(pac, prof, "Confirmado", viejo.Date, new TimeSpan(viejo.Hour, viejo.Minute, 0));
        _d.Turno(pac, prof, "Ausente", viejo.Date, new TimeSpan(viejo.Hour, viejo.Minute, 0).Add(TimeSpan.FromMinutes(1)));
        var c = ComoPaciente(pac);

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/pacientes/{pac}/proximo-turno")).StatusCode);

        var luego = Ahora.AddHours(5);
        var futuro = _d.Turno(pac, prof, "Confirmado", luego.Date, new TimeSpan(luego.Hour, luego.Minute, 0));
        var j = await Json(await c.GetAsync($"/pacientes/{pac}/proximo-turno"));
        Assert.Equal(futuro, j.GetProperty("idTurno").GetInt32());
    }
}

/// Bloque C: el profesional agenda un turno para un paciente suyo. Queda Confirmado y avisa al paciente.
[Trait("Categoria", "Integracion")]
public class AgendarTurnoBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly EmailServiceEspia _mails = new();
    private readonly WebApplicationFactory<Program> _fabrica;

    public AgendarTurnoBaseRealTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(_mails); }));
    }

    public void Dispose() { _fabrica.Dispose(); _d.Dispose(); }

    private DateTime Ahora => _fabrica.Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime;
    private HttpClient ComoProfesional(int id) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(id), Roles.Profesional, idProfesional: id);
    private HttpClient ComoPaciente(int id) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(id), Roles.Paciente, idPaciente: id);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }

    private (int Prof, int Pac) Escenario(bool consentimiento = true)
    {
        var prof = _d.Profesional(_d.Especialidad(), nombre: "Prof " + _d.Marca);
        foreach (var dia in Enum.GetValues<DayOfWeek>())
            _d.Agenda(prof, DatosDePrueba.NombreDelDia(new DateTime(2026, 11, 1).AddDays((int)dia)), "09:00", "18:00");
        var pac = _d.Paciente("Paciente " + _d.Marca, mail: $"{_d.Marca}@example.test");
        _d.PacienteProfesional(pac, prof);
        if (consentimiento) _d.ConsentimientoFirmado(pac);
        _d.AlLimpiar("DerivacionPsiquiatrica", "IdPaciente", pac);
        _d.AlLimpiar("Turno", "IdPaciente", pac);
        _d.AlLimpiar("Notificacion", "IdPaciente", pac);
        return (prof, pac);
    }

    private object Pedido(int pac, DateTime momento) => new { idPaciente = pac, fecha = momento.ToString("yyyy-MM-dd"), hora = momento.ToString("HH:mm:ss"), motivo = "[TEST] agendado" };
    private DateTime Dentro(int dias, int hora = 10) => Ahora.Date.AddDays(dias).AddHours(hora);

    [Fact]
    public async Task El_profesional_agenda_un_turno_que_nace_Confirmado_con_aviso_al_paciente_y_auditoria()
    {
        var (prof, pac) = Escenario();
        var momento = Dentro(3);

        var r = await ComoProfesional(prof).PostAsJsonAsync("/turnos/agendar", Pedido(pac, momento));
        var j = await Json(r);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = j.GetProperty("idTurno").GetInt32();
        Assert.Equal("Confirmado", DatosDePrueba.Escalar("SELECT et.Descripcion FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @t", ("@t", id)));
        Assert.Equal("profesional", DatosDePrueba.Escalar("SELECT CreadoPor FROM Turno WHERE IdTurno = @t", ("@t", id)));
        Assert.Equal(true, DatosDePrueba.Escalar("SELECT ConfirmadoPaciente FROM Turno WHERE IdTurno = @t", ("@t", id)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p AND Titulo = N'Tu profesional te agendó un turno'", ("@p", pac)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = N'Turno' AND IdEntidad = @i AND Accion = N'Turno agendado por el profesional'", ("@i", id)));
        // No requiere aceptación: no figura entre las solicitudes del profesional.
        var solicitudes = await Json(await ComoProfesional(prof).GetAsync($"/profesionales/{prof}/solicitudes-turno"));
        Assert.DoesNotContain(solicitudes.EnumerateArray(), s => s.GetProperty("idTurno").GetInt32() == id);
    }

    [Fact]
    public async Task Agendar_dos_veces_lo_mismo_no_duplica_ni_el_turno_ni_el_aviso()
    {
        var (prof, pac) = Escenario();
        var momento = Dentro(3);
        var c = ComoProfesional(prof);

        var r1 = await c.PostAsJsonAsync("/turnos/agendar", Pedido(pac, momento));
        var r2 = await c.PostAsJsonAsync("/turnos/agendar", Pedido(pac, momento));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = @p", ("@p", pac)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p AND Titulo = N'Tu profesional te agendó un turno'", ("@p", pac)));
    }

    [Fact]
    public async Task Se_rechaza_a_un_paciente_ajeno_derivado_o_sin_consentimiento_y_un_horario_que_no_es_de_su_agenda_y_el_motivo_se_explica()
    {
        var (prof, pac) = Escenario();
        var ajeno = _d.Paciente("Ajeno " + _d.Marca);
        var c = ComoProfesional(prof);

        var rAjeno = await c.PostAsJsonAsync("/turnos/agendar", Pedido(ajeno, Dentro(3)));
        Assert.Equal(HttpStatusCode.Forbidden, rAjeno.StatusCode);
        Assert.Contains("no está a tu cargo", (await Json(rAjeno)).GetProperty("mensaje").GetString());

        var rHora = await c.PostAsJsonAsync("/turnos/agendar", Pedido(pac, Dentro(3, 22)));   // fuera de la franja de 9 a 18
        Assert.Equal(HttpStatusCode.Conflict, rHora.StatusCode);
        Assert.Contains("agenda", (await Json(rHora)).GetProperty("mensaje").GetString());

        var (prof2, pac2) = Escenario(consentimiento: false);
        var rSin = await ComoProfesional(prof2).PostAsJsonAsync("/turnos/agendar", Pedido(pac2, Dentro(3)));
        Assert.Equal(HttpStatusCode.Conflict, rSin.StatusCode);
        Assert.Contains("consentimiento", (await Json(rSin)).GetProperty("mensaje").GetString());
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente IN (@a, @b)", ("@a", pac2), ("@b", ajeno)));

        if (DerivacionPsiquiatraDAO.Disponible())
        {
            var (prof3, pac3) = Escenario();
            DatosDePrueba.Ejecutar("INSERT INTO DerivacionPsiquiatrica (IdPaciente, IdProfesional) VALUES (@p, @m)", ("@p", pac3), ("@m", prof3));
            var rDer = await ComoProfesional(prof3).PostAsJsonAsync("/turnos/agendar", Pedido(pac3, Dentro(3)));
            Assert.Equal(HttpStatusCode.Conflict, rDer.StatusCode);
            Assert.Contains("derivado", (await Json(rDer)).GetProperty("mensaje").GetString());
        }
    }

    [Fact]
    public async Task Solo_un_profesional_agenda_y_un_horario_ocupado_se_rechaza()
    {
        var (prof, pac) = Escenario();
        var otro = _d.Paciente("Otro " + _d.Marca);
        _d.PacienteProfesional(otro, prof);
        _d.ConsentimientoFirmado(otro);
        _d.AlLimpiar("Turno", "IdPaciente", otro);
        var momento = Dentro(3);

        Assert.Equal(HttpStatusCode.Forbidden, (await ComoPaciente(pac).PostAsJsonAsync("/turnos/agendar", Pedido(pac, momento))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ComoProfesional(prof).PostAsJsonAsync("/turnos/agendar", Pedido(pac, momento))).StatusCode);
        var ocupado = await ComoProfesional(prof).PostAsJsonAsync("/turnos/agendar", Pedido(otro, momento));
        Assert.Equal(HttpStatusCode.Conflict, ocupado.StatusCode);
        Assert.Contains("ocupado", (await Json(ocupado)).GetProperty("mensaje").GetString());
    }
}

// La regla de la sala y los primeros pasos, sin base.
public class EstadoDeSalaReglasTests
{
    private static readonly DateTime Inicio = new(2026, 11, 2, 10, 0, 0);
    private static readonly VideoOptions Opciones = new();   // 10 minutos antes, 30 después, 30 de duración por defecto

    [Theory]
    [InlineData(-11, "por-abrir")]
    [InlineData(-10, "abierta")]
    [InlineData(0, "abierta")]
    [InlineData(59, "abierta")]
    [InlineData(60, "cerrada")]
    [InlineData(300, "cerrada")]
    public void La_sala_esta_por_abrir_abierta_o_cerrada(int minutosDesdeElInicio, string esperado) =>
        Assert.Equal(esperado, EstadoDeSala.Nombre(EstadoDeSala.Evaluar(Inicio, null, Inicio.AddMinutes(minutosDesdeElInicio), Opciones).Fase));

    [Fact]
    public void La_duracion_de_la_franja_manda_sobre_la_por_defecto()
    {
        var (fase, _, hasta) = EstadoDeSala.Evaluar(Inicio, 60, Inicio.AddMinutes(75), Opciones);
        Assert.Equal(FaseSala.Abierta, fase);
        Assert.Equal(Inicio.AddMinutes(90), hasta);
    }

    private static readonly EstadoTratamiento Firmado = new(true, "", false, DateTime.Today, "1", null);

    private static ResultadoPrimerosPasos Calcular(DateTime ahora, params TurnoPaso[] turnos) =>
        PrimerosPasos.Calcular(new DatosPrimerosPasos(Firmado, new InformePaso(EstadoInforme.Derivado, null, null), new ProfesionalPaso(1, "Prof Ficticio", null), turnos), ahora);

    [Fact]
    public void El_paso_7_con_el_turno_pasado_y_sin_sesion_ofrece_reprogramar_y_no_entrar()
    {
        var ahora = new DateTime(2026, 11, 2, 15, 0, 0);
        var r = Calcular(ahora, new TurnoPaso(7, new DateTime(2026, 11, 2, 10, 0, 0), "Confirmado", true, "paciente", "SALA-1"));

        var paso = r.Pasos.Single(p => p.Clave == PasoClave.Sesion);
        Assert.Equal("Tu turno ya pasó", paso.Titulo);
        Assert.Equal(AccionTipo.Reprogramar, paso.Accion!.Tipo);
        Assert.Equal(7, paso.Accion.IdTurno);
        Assert.Equal(EstadoDeSala.Cerrada, r.Sesion!.Fase);
        Assert.False(r.Sesion.Habilitada);
        Assert.Contains("Reprogramalo", paso.Detalle);
    }

    [Fact]
    public void Con_un_turno_nuevo_vigente_el_paso_7_apunta_al_nuevo_y_no_al_pasado()
    {
        var ahora = new DateTime(2026, 11, 2, 15, 0, 0);
        var r = Calcular(ahora,
            new TurnoPaso(7, new DateTime(2026, 11, 2, 10, 0, 0), "Confirmado", true, "paciente", "SALA-1"),
            new TurnoPaso(8, new DateTime(2026, 11, 3, 10, 0, 0), "Confirmado", true, "paciente", "SALA-2"));

        var paso = r.Pasos.Single(p => p.Clave == PasoClave.Sesion);
        Assert.Equal(AccionTipo.EntrarSesion, paso.Accion!.Tipo);
        Assert.Equal(8, r.Sesion!.IdTurno);
        Assert.Equal(EstadoDeSala.PorAbrir, r.Sesion.Fase);
    }

    [Fact]
    public void Con_la_sala_abierta_se_puede_entrar_y_con_la_sesion_realizada_el_paso_esta_hecho()
    {
        var ahora = new DateTime(2026, 11, 2, 10, 5, 0);
        var abierta = Calcular(ahora, new TurnoPaso(7, new DateTime(2026, 11, 2, 10, 0, 0), "Confirmado", true, "paciente", "SALA-1"));
        Assert.True(abierta.Sesion!.Habilitada);
        Assert.Equal(EstadoDeSala.Abierta, abierta.Sesion.Fase);

        var hecha = Calcular(new DateTime(2026, 11, 2, 15, 0, 0), new TurnoPaso(7, new DateTime(2026, 11, 2, 10, 0, 0), "Realizado", true, "paciente", "SALA-1"));
        Assert.Equal(PasoEstado.Hecho, hecha.Pasos.Single(p => p.Clave == PasoClave.Sesion).Estado);
    }
}

// Pantallas leídas como texto: lo que no puede cambiar sin que alguien lo note.
public class UsoRealPantallasTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] ruta) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(ruta).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void El_calendario_del_paciente_ya_no_arma_los_horarios_solo_y_dice_por_que_no_hay_nada()
    {
        var js = Leer("PANTALLAS", "Pantalla_Paciente", "js", "dashboard.js");
        Assert.Contains("/pacientes/${idPaciente}/disponibilidad?idProfesional=", js);
        Assert.DoesNotContain("DIA_DB_NUM[normalizar(a.dia)]", js);          // ya no usa las franjas crudas
        Assert.DoesNotContain("/turnos/ocupados?idProfesional", js);
        Assert.Contains("No pudimos cargar los horarios", js);              // un error se dice, no deja el calendario vacío
        Assert.Contains("solAviso", js);
        Assert.Matches(@"function turnoYaPaso", js);
    }

    [Fact]
    public void El_inicio_del_profesional_deshabilita_la_sala_cerrada_con_el_motivo()
    {
        var js = Leer("PANTALLAS", "Pantalla_Profesional", "js", "nuevo_dashboard.js");
        Assert.Contains("/salas/estado?ids=", js);
        Assert.Contains("Sala cerrada", js);
        Assert.Contains("disabled aria-disabled=\"true\"", js);
        Assert.Contains("textoSalaCerrada", js);
    }

    [Fact]
    public void El_paso_7_del_paciente_ofrece_reprogramar_cuando_el_turno_ya_paso()
    {
        var pp = Leer("PANTALLAS", "Pantalla_Paciente", "js", "primeros_pasos.js");
        Assert.Contains("tipo === \"reprogramar\"", pp);
        Assert.Contains("reprogramarTurnoPasado", pp);
        Assert.Contains("s.fase === \"cerrada\"", pp);
        var dash = Leer("PANTALLAS", "Pantalla_Paciente", "js", "dashboard.js");
        Assert.Contains("turnoTerminado(x)", dash);   // «Próximo turno» no muestra un turno que ya pasó
    }
}
