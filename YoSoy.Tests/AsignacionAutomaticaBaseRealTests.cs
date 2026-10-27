using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Se OMITE si la base no tiene la migración 32 (propuesta automática) o las que necesitan los primeros pasos.</summary>
public sealed class FactConBaseDeAsignacionAttribute : FactAttribute
{
    public FactConBaseDeAsignacionAttribute()
    {
        var motivo = new FactConBaseDePrimerosPasosAttribute().Skip;
        if (motivo is null && Convert.ToInt32(DatosDePrueba.Escalar(
                "SELECT CASE WHEN OBJECT_ID('dbo.PropuestaAsignacion', 'U') IS NOT NULL AND COL_LENGTH('dbo.InformeAdmision', 'AsignacionEstado') IS NOT NULL THEN 1 ELSE 0 END")) != 1)
            motivo = "La base no tiene la migración migrations/add_propuesta_asignacion.sql.";
        if (motivo is not null) Skip = motivo;
    }
}

// Propuesta automática de profesional y horario al aceptar un formulario, con datos propios en yosoy_test: la recepción acepta, el sistema propone, el paciente decide.
// Cada especialidad es nueva en cada prueba, así que los únicos candidatos son los profesionales que arma la propia prueba.
[Trait("Categoria", "Integracion")]
public class AsignacionAutomaticaBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly EmailServiceEspia _mails = new();
    private readonly WebApplicationFactory<Program> _fabrica;
    private static readonly object Confirmo = new { confirmo = true };

    public AsignacionAutomaticaBaseRealTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(_mails); }));
    }

    public void Dispose()
    {
        // Los avisos a la recepción no cuelgan de ninguna fila de la prueba: se borran por el nombre del paciente, que lleva la marca.
        DatosDePrueba.Ejecutar("DELETE FROM NotificacionInterna WHERE Mensaje LIKE @m", ("@m", "%" + _d.Marca + "%"));
        _fabrica.Dispose();
        _d.Dispose();
    }

    // ───────────── Armado ─────────────

    private DateTime Ahora => _fabrica.Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime;

    private HttpClient Recepcion(int idProfesional)
    {
        var u = _d.UsuarioDeProfesional(idProfesional);
        var token = _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(u,
            new SesionModulo(Modulos.Profesional, new[] { Roles.Profesional }, new[] { Privilegios.Admision }, idProfesional));
        return DatosDePrueba.Cliente(_fabrica, token);
    }

    private HttpClient Paciente(int idPaciente) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(idPaciente), Roles.Paciente, idPaciente: idPaciente);

    private HttpClient ProfesionalSinAdmision(int idProfesional) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(idProfesional), Roles.Profesional, idProfesional: idProfesional);

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>Un profesional de la especialidad con agenda de turnos todos los días de <paramref name="inicio"/> a <paramref name="fin"/>.</summary>
    private int Profesional(int esp, string nombre, string inicio = "09:00", string fin = "18:00", params DayOfWeek[] dias)
    {
        var p = _d.Profesional(esp, nombre: nombre);
        foreach (var dia in dias.Length == 0 ? Enum.GetValues<DayOfWeek>() : dias)
            _d.Agenda(p, DatosDePrueba.NombreDelDia(new DateTime(2026, 11, 1).AddDays((int)dia)), inicio, fin);
        return p;
    }

    private int ProfesionalSinAgenda(int esp, string nombre) => _d.Profesional(esp, nombre: nombre);

    /// <summary>El primer horario (grilla de 30 minutos) de esos días y esa franja, con la anticipación de 24 horas, ignorando todo lo demás.</summary>
    private DateTime Primero(DayOfWeek[] dias, string inicio, string fin, Func<DateTime, bool>? filtro = null)
    {
        var desde = Ahora.AddHours(AsignacionAutomaticaReglas.HorasAnticipacionMinima);
        for (var fecha = desde.Date; fecha <= desde.Date.AddDays(40); fecha = fecha.AddDays(1))
        {
            if (!dias.Contains(fecha.DayOfWeek)) continue;
            for (var h = TimeSpan.Parse(inicio); h + TimeSpan.FromMinutes(30) <= TimeSpan.Parse(fin); h += TimeSpan.FromMinutes(30))
                if (fecha + h >= desde && (filtro?.Invoke(fecha + h) ?? true)) return fecha + h;
        }
        throw new InvalidOperationException("no hay horario");
    }

    private static readonly DayOfWeek[] TodosLosDias = Enum.GetValues<DayOfWeek>();

    private (int Paciente, int Informe) NuevoFormulario(int esp, bool consentimiento = true, object? horario = null, object? profesionales = null, string estado = "Pendiente")
    {
        var pac = _d.Paciente("Ana Gomez " + _d.Marca, mail: $"{_d.Marca}@example.test");
        DatosDePrueba.Ejecutar("UPDATE Paciente SET FechaNacimiento = @f, Telefono = N'11 5555-0001' WHERE IdPaciente = @p", ("@f", new DateTime(1990, 5, 20)), ("@p", pac));
        if (consentimiento) _d.ConsentimientoFirmado(pac);
        var informe = _d.InformeAdmision(pac, estado, null, null);
        DatosDePrueba.Ejecutar("UPDATE InformeAdmision SET IdEspecialidad = @e, HorarioPreferido = @h, ProfesionalesPreferidos = @pp WHERE IdInforme = @i",
            ("@e", esp), ("@h", horario is null ? null : JsonSerializer.Serialize(horario)), ("@pp", profesionales is null ? null : JsonSerializer.Serialize(profesionales)), ("@i", informe));
        return (pac, informe);
    }

    private async Task<JsonElement> Aceptar(HttpClient recep, int informe)
    {
        var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", Confirmo);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private sealed record Prop(int Id, int Profesional, DateTime FechaHora, string Estado, int Nro, string? Motivo, DateTime ExpiraEn, DateTime? Respuesta, int? Turno);

    private static List<Prop> Propuestas(int informe)
    {
        var lista = new List<Prop>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT IdPropuesta, IdProfesional, FechaHora, Estado, NroPropuesta, Motivo, ExpiraEn, FechaRespuesta, IdTurno FROM PropuestaAsignacion WHERE IdInformeAdmision = @i ORDER BY IdPropuesta", con);
        cmd.Parameters.AddWithValue("@i", informe);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new((int)r["IdPropuesta"], (int)r["IdProfesional"], (DateTime)r["FechaHora"], (string)r["Estado"], Convert.ToInt32(r["NroPropuesta"]), r["Motivo"] as string,
                (DateTime)r["ExpiraEn"], r["FechaRespuesta"] as DateTime?, r["IdTurno"] as int?));
        return lista;
    }

    private static string? AsignacionEstado(int informe) => DatosDePrueba.Escalar("SELECT AsignacionEstado FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe)) as string;
    private static string? AsignacionMotivo(int informe) => DatosDePrueba.Escalar("SELECT AsignacionMotivo FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe)) as string;
    private static string EstadoInforme(int informe) => (string)DatosDePrueba.Escalar("SELECT Estado FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe))!;
    private static int Turnos(int pac, int prof) => DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = @p AND IdProfesional = @r", ("@p", pac), ("@r", prof));
    private static int Vinculos(int pac, int prof) => DatosDePrueba.Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @p AND IdProfesional = @r", ("@p", pac), ("@r", prof));
    private static int Auditorias(int informe, string accion) => DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = N'InformeAdmision' AND IdEntidad = @i AND Accion = @a", ("@i", informe), ("@a", accion));
    private static int AvisosRecepcion(int informe, string tipo) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Destinatario = N'Administrador' AND Tipo = @t AND Mensaje LIKE @m", ("@t", tipo), ("@m", $"%(formulario #{informe})"));

    private async Task<JsonElement> PrimerosPasos(int pac) => await Json(await Paciente(pac).GetAsync($"/pacientes/{pac}/primeros-pasos"));

    private static JsonElement Paso(JsonElement pp, string clave) => pp.GetProperty("pasos").EnumerateArray().Single(p => p.GetProperty("clave").GetString() == clave);

    // ───────────── Al aceptar el formulario ─────────────

    [FactConBaseDeAsignacion]
    public async Task Sin_preferencias_propone_el_primer_horario_libre_del_profesional_con_menos_pacientes_y_es_idempotente()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var b = Profesional(esp, "Beto");
        _d.PacienteProfesional(_d.Paciente("Otro " + _d.Marca), a);   // Alba ya tiene un paciente activo: a igual horario gana Beto
        var (pac, informe) = NuevoFormulario(esp);
        var recep = Recepcion(_d.Profesional());

        var r = await Aceptar(recep, informe);
        Assert.Equal("propuesta", r.GetProperty("asignacion").GetString());
        Assert.Contains("Le propusimos un profesional y un horario al paciente", r.GetProperty("mensaje").GetString());

        var p = Assert.Single(Propuestas(informe));
        Assert.Equal(b, p.Profesional);
        Assert.Equal(Primero(TodosLosDias, "09:00", "18:00"), p.FechaHora);
        Assert.Equal(PropuestaEstado.Pendiente, p.Estado);
        Assert.Equal(1, p.Nro);
        Assert.InRange((p.ExpiraEn - Ahora).TotalHours, 47.9, 48.1);
        Assert.Equal("Aprobado", EstadoInforme(informe));
        Assert.Equal("Propuesta", AsignacionEstado(informe));
        Assert.Equal(1, Auditorias(informe, "Se generó una propuesta de asignación"));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p AND Titulo = N'Te propusimos un profesional'", ("@p", pac)));
        Assert.Equal(0, Turnos(pac, b) + Vinculos(pac, b));   // el sistema propone: no crea turno ni vínculo

        // Lo que ve el paciente en sus primeros pasos.
        var pp = await PrimerosPasos(pac);
        var pr = pp.GetProperty("propuesta");
        Assert.Equal(p.Id, pr.GetProperty("idPropuesta").GetInt32());
        Assert.Equal(b, pr.GetProperty("idProfesional").GetInt32());
        Assert.True(pr.GetProperty("puedeCambiar").GetBoolean());
        Assert.Equal("responder-propuesta", pp.GetProperty("principal").GetProperty("tipo").GetString());
        Assert.Contains("Te propusimos a Beto", Paso(pp, "profesional").GetProperty("detalle").GetString());

        // Aceptar de nuevo no hace nada de más: no hay segunda propuesta.
        var otraVez = await recep.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", Confirmo);
        Assert.False((await Json(otraVez)).GetProperty("cambio").GetBoolean());
        Assert.Single(Propuestas(informe));
    }

    [FactConBaseDeAsignacion]
    public async Task Con_profesional_dias_y_franja_preferidos_los_respeta()
    {
        var esp = _d.Especialidad();
        Profesional(esp, "Alba");   // todos los días de 9 a 18: tiene horarios más cercanos, pero no es la elegida
        var b = Profesional(esp, "Beto", "14:00", "18:00", DayOfWeek.Tuesday);
        var (_, informe) = NuevoFormulario(esp, horario: new { dias = new[] { "martes" }, franja = "tarde" }, profesionales: new { cualquiera = false, ids = new[] { b } });

        await Aceptar(Recepcion(_d.Profesional()), informe);

        var p = Assert.Single(Propuestas(informe));
        Assert.Equal(b, p.Profesional);
        Assert.Equal(Primero(new[] { DayOfWeek.Tuesday }, "14:00", "18:00"), p.FechaHora);
        Assert.Contains("eligió el paciente", p.Motivo);
        Assert.DoesNotContain("relaj", p.Motivo);
    }

    [FactConBaseDeAsignacion]
    public async Task Sin_disponibilidad_para_lo_pedido_relaja_primero_la_franja_despues_los_dias_y_lo_anota()
    {
        var esp = _d.Especialidad();
        var b = Profesional(esp, "Beto", "09:00", "12:00", DayOfWeek.Tuesday);   // solo martes a la mañana
        var recep = Recepcion(_d.Profesional());

        // Pidió martes por la tarde: el martes se respeta y se relaja la franja.
        var (_, informeFranja) = NuevoFormulario(esp, horario: new { dias = new[] { "martes" }, franja = "tarde" });
        await Aceptar(recep, informeFranja);
        var pFranja = Assert.Single(Propuestas(informeFranja));
        Assert.Equal(Primero(new[] { DayOfWeek.Tuesday }, "09:00", "12:00"), pFranja.FechaHora);
        Assert.Contains("Se relajó la franja horaria preferida.", pFranja.Motivo);

        // Pidió jueves por la tarde: no hay jueves; se relajan los días y la franja. (Ese horario del martes ya tiene una propuesta pendiente: toma el siguiente.)
        var (_, informeDias) = NuevoFormulario(esp, horario: new { dias = new[] { "jueves" }, franja = "tarde" });
        await Aceptar(recep, informeDias);
        var pDias = Assert.Single(Propuestas(informeDias));
        Assert.Equal(b, pDias.Profesional);
        Assert.Equal(Primero(new[] { DayOfWeek.Tuesday }, "09:00", "12:00", m => m != pFranja.FechaHora), pDias.FechaHora);
        Assert.Contains("Se relajaron los días y la franja horaria preferidos.", pDias.Motivo);
    }

    [FactConBaseDeAsignacion]
    public async Task Si_los_profesionales_elegidos_no_tienen_horario_amplia_a_los_de_la_especialidad_y_lo_anota()
    {
        var esp = _d.Especialidad();
        var elegido = ProfesionalSinAgenda(esp, "Sin agenda");
        var a = Profesional(esp, "Alba");
        var (_, informe) = NuevoFormulario(esp, profesionales: new { cualquiera = false, ids = new[] { elegido } });

        await Aceptar(Recepcion(_d.Profesional()), informe);

        var p = Assert.Single(Propuestas(informe));
        Assert.Equal(a, p.Profesional);
        Assert.Contains("Los profesionales elegidos no tenían disponibilidad.", p.Motivo);
    }

    [FactConBaseDeAsignacion]
    public async Task Nunca_propone_profesionales_inactivos_ni_de_admision_ni_de_otra_especialidad_ni_horarios_de_admision()
    {
        var esp = _d.Especialidad();
        var inactivo = _d.Profesional(esp, estado: "Inactivo");
        foreach (var dia in TodosLosDias) _d.Agenda(inactivo, DatosDePrueba.NombreDelDia(new DateTime(2026, 11, 1).AddDays((int)dia)));
        var otraEsp = Profesional(_d.Especialidad(), "Otra especialidad");
        // Un profesional de la especialidad cuya única franja es de admisión (entrevistas): tampoco cuenta.
        var soloAdmision = _d.Profesional(esp, nombre: "Solo admision");
        var franja = _d.Agenda(soloAdmision, "Lunes");
        DatosDePrueba.Ejecutar("UPDATE Agenda SET EsAdmision = 1 WHERE IdAgenda = @a", ("@a", franja));
        var (_, informe) = NuevoFormulario(esp);

        var r = await Aceptar(Recepcion(_d.Profesional()), informe);

        Assert.Equal("por-resolver", r.GetProperty("asignacion").GetString());
        Assert.Empty(Propuestas(informe));
        Assert.NotEqual(0, otraEsp);
    }

    [FactConBaseDeAsignacion]
    public async Task La_vigencia_de_la_agenda_se_respeta_una_franja_vencida_o_que_todavia_no_empezo_no_se_propone()
    {
        var esp = _d.Especialidad();
        var p = _d.Profesional(esp, nombre: "Vigencia");
        var ayer = Ahora.Date.AddDays(-1);
        var antes = _d.Agenda(p, DatosDePrueba.NombreDelDia(Ahora.Date.AddDays(2)));   // vencida
        DatosDePrueba.Ejecutar("UPDATE Agenda SET FechaFin = @f WHERE IdAgenda = @a", ("@f", ayer), ("@a", antes));
        var luego = _d.Agenda(p, DatosDePrueba.NombreDelDia(Ahora.Date.AddDays(3)));   // empieza dentro de 10 días
        var inicio = Ahora.Date.AddDays(10);
        DatosDePrueba.Ejecutar("UPDATE Agenda SET FechaInicio = @f WHERE IdAgenda = @a", ("@f", inicio), ("@a", luego));
        var (_, informe) = NuevoFormulario(esp);

        await Aceptar(Recepcion(_d.Profesional()), informe);

        var prop = Assert.Single(Propuestas(informe));
        Assert.True(prop.FechaHora.Date >= inicio);
        Assert.Equal(Ahora.Date.AddDays(3).DayOfWeek, prop.FechaHora.DayOfWeek);
    }

    [FactConBaseDeAsignacion]
    public async Task Sin_horarios_libres_el_caso_va_a_recepcion_con_el_motivo_y_el_paciente_ve_el_mensaje_honesto()
    {
        var esp = _d.Especialidad();
        ProfesionalSinAgenda(esp, "Sin agenda");
        var (pac, informe) = NuevoFormulario(esp);
        var recep = Recepcion(_d.Profesional());

        var r = await Aceptar(recep, informe);

        Assert.Equal("por-resolver", r.GetProperty("asignacion").GetString());
        Assert.Contains("Asignaciones para resolver", r.GetProperty("mensaje").GetString());
        Assert.Empty(Propuestas(informe));
        Assert.Equal("Aprobado", EstadoInforme(informe));   // la aceptación de la recepción no se pierde
        Assert.Equal("Por resolver", AsignacionEstado(informe));
        Assert.Equal("Sin horarios libres para esa especialidad", AsignacionMotivo(informe));
        Assert.Equal(1, AvisosRecepcion(informe, "asignacion-por-resolver"));   // la campanita de la recepción

        var fila = (await Json(await recep.GetAsync("/informes-admision-detalle"))).EnumerateArray().Single(x => x.GetProperty("idInforme").GetInt32() == informe);
        Assert.Equal("Por resolver", fila.GetProperty("asignacionEstado").GetString());
        Assert.Equal("Sin horarios libres para esa especialidad", fila.GetProperty("asignacionMotivo").GetString());

        var pp = await PrimerosPasos(pac);
        Assert.True(pp.GetProperty("asignacionPorResolver").GetBoolean());
        Assert.Equal(JsonValueKind.Null, pp.GetProperty("propuesta").ValueKind);
        Assert.Equal("Estamos buscando la mejor opción para vos, te avisamos apenas esté lista.", Paso(pp, "profesional").GetProperty("detalle").GetString());
        Assert.Equal(JsonValueKind.Null, pp.GetProperty("principal").ValueKind);   // al paciente no le queda nada por hacer
    }

    [FactConBaseDeAsignacion]
    public async Task Con_el_consentimiento_revocado_no_propone_y_recepcion_lo_ve_con_el_motivo()
    {
        var esp = _d.Especialidad();
        Profesional(esp, "Alba");
        var (pac, informe) = NuevoFormulario(esp);   // firmó...
        var revocado = ConsentimientoRevocacion.Revocar(pac, "Ana " + _d.Marca, new ConsentimientoOptions().TipoRequerido, _d.UsuarioDePaciente(pac), Ahora);
        Assert.True(revocado.Revocadas > 0);          // ...y lo revocó

        var r = await Aceptar(Recepcion(_d.Profesional()), informe);

        Assert.Equal("por-resolver", r.GetProperty("asignacion").GetString());
        Assert.Empty(Propuestas(informe));
        Assert.Equal("Por resolver", AsignacionEstado(informe));
        Assert.Contains("consentimiento", AsignacionMotivo(informe)!);
        Assert.Equal(1, AvisosRecepcion(informe, "asignacion-por-resolver"));
    }

    [FactConBaseDeAsignacion]
    public async Task Sin_consentimiento_firmado_tampoco_propone()
    {
        var esp = _d.Especialidad();
        Profesional(esp, "Alba");
        var (_, informe) = NuevoFormulario(esp, consentimiento: false);

        await Aceptar(Recepcion(_d.Profesional()), informe);

        Assert.Empty(Propuestas(informe));
        Assert.Equal("Por resolver", AsignacionEstado(informe));
        Assert.Contains("no lo firmó o lo revocó", AsignacionMotivo(informe)!);
    }

    // ───────────── «Me sirve» ─────────────

    [FactConBaseDeAsignacion]
    public async Task Me_sirve_crea_un_solo_turno_y_el_vinculo_aunque_se_repita_o_se_haga_en_paralelo()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var (pac, informe) = NuevoFormulario(esp);
        await Aceptar(Recepcion(_d.Profesional()), informe);
        var p = Propuestas(informe).Single();
        var paciente = Paciente(pac);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => paciente.PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null)));
        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var cuerpos = await Task.WhenAll(respuestas.Select(Json));
        var idTurno = cuerpos[0].GetProperty("idTurno").GetInt32();
        Assert.All(cuerpos, c => Assert.Equal(idTurno, c.GetProperty("idTurno").GetInt32()));
        Assert.Contains(cuerpos, c => c.GetProperty("codigo").GetString() == "aceptada");

        Assert.Equal(1, Turnos(pac, a));
        Assert.Equal("Confirmado", (string)DatosDePrueba.Escalar("SELECT et.Descripcion FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @t", ("@t", idTurno))!);
        Assert.True((bool)DatosDePrueba.Escalar("SELECT ConfirmadoPaciente FROM Turno WHERE IdTurno = @t", ("@t", idTurno))!);
        Assert.Equal(p.FechaHora, (DateTime)DatosDePrueba.Escalar("SELECT CAST(Fecha AS DATETIME) + CAST(Hora AS DATETIME) FROM Turno WHERE IdTurno = @t", ("@t", idTurno))!);
        Assert.Equal(1, Vinculos(pac, a));
        Assert.Equal("Confirmada", (string)DatosDePrueba.Escalar("SELECT EstadoAsignacion FROM PacienteProfesional WHERE IdPaciente = @p AND IdProfesional = @r", ("@p", pac), ("@r", a))!);
        Assert.Equal("Derivado", EstadoInforme(informe));
        Assert.Equal(a, (int)DatosDePrueba.Escalar("SELECT IdProfesionalDerivado FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe))!);
        Assert.Null(AsignacionEstado(informe));
        var aceptada = Propuestas(informe).Single();
        Assert.Equal(PropuestaEstado.Aceptada, aceptada.Estado);
        Assert.Equal(idTurno, aceptada.Turno);
        Assert.NotNull(aceptada.Respuesta);
        Assert.Equal(1, Auditorias(informe, "El paciente aceptó la propuesta de asignación"));
        // El profesional se entera por la campanita, una sola vez.
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Destinatario = N'Profesional' AND IdProfesional = @r AND Tipo = N'asignacion-nueva' AND IdTurno = @t", ("@r", a), ("@t", idTurno)));

        // Y lo ve en su campanita (avisos clínicos del profesional).
        var avisos = await Json(await ProfesionalSinAdmision(a).GetAsync($"/profesionales/{a}/avisos-clinicos"));
        Assert.Contains(avisos.GetProperty("avisos").EnumerateArray(), x => x.GetProperty("titulo").GetString() == "Tenés un paciente nuevo con primer turno");

        // Los pasos 5 y 6 quedan hechos y el 7 es «Tu primera sesión».
        var pp = await PrimerosPasos(pac);
        Assert.Equal("hecho", Paso(pp, "profesional").GetProperty("estado").GetString());
        Assert.Equal("hecho", Paso(pp, "turno").GetProperty("estado").GetString());
        Assert.Equal("actual", Paso(pp, "sesion").GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, pp.GetProperty("propuesta").ValueKind);
    }

    [FactConBaseDeAsignacion]
    public async Task Si_el_horario_se_ocupa_entre_la_propuesta_y_la_confirmacion_se_rechaza_con_mensaje_claro_y_se_propone_otro()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var (pac, informe) = NuevoFormulario(esp);
        await Aceptar(Recepcion(_d.Profesional()), informe);
        var p = Propuestas(informe).Single();
        _d.Turno(_d.Paciente("Otro " + _d.Marca), a, "Confirmado", p.FechaHora.Date, p.FechaHora.TimeOfDay);   // alguien tomó ese horario por otro camino

        var r = await Paciente(pac).PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var cuerpo = await Json(r);
        Assert.Equal("horario-ocupado", cuerpo.GetProperty("codigo").GetString());
        Assert.Contains("Ese horario se acaba de ocupar. Te propusimos otro", cuerpo.GetProperty("mensaje").GetString());
        var nueva = cuerpo.GetProperty("propuesta");
        Assert.NotEqual(p.Id, nueva.GetProperty("idPropuesta").GetInt32());

        var todas = Propuestas(informe);
        Assert.Equal(2, todas.Count);
        Assert.Equal(PropuestaEstado.Cancelada, todas[0].Estado);
        Assert.Equal(PropuestaEstado.Pendiente, todas[1].Estado);
        Assert.NotEqual(p.FechaHora, todas[1].FechaHora);
        Assert.Equal(1, todas[1].Nro);   // no fue un rechazo del paciente: no gasta una de las tres propuestas
        Assert.Equal(0, Turnos(pac, a));
        Assert.Equal(0, Vinculos(pac, a));
        Assert.Equal("Propuesta", AsignacionEstado(informe));
    }

    [FactConBaseDeAsignacion]
    public async Task Si_el_consentimiento_se_revoca_entre_la_propuesta_y_la_confirmacion_no_se_asigna()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var (pac, informe) = NuevoFormulario(esp);
        await Aceptar(Recepcion(_d.Profesional()), informe);
        var p = Propuestas(informe).Single();
        ConsentimientoRevocacion.Revocar(pac, "Ana " + _d.Marca, new ConsentimientoOptions().TipoRequerido, _d.UsuarioDePaciente(pac), Ahora);

        var r = await Paciente(pac).PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("consentimiento-pendiente", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal(PropuestaEstado.Cancelada, Propuestas(informe).Single().Estado);
        Assert.Equal("Por resolver", AsignacionEstado(informe));
        Assert.Equal(0, Turnos(pac, a) + Vinculos(pac, a));
    }

    // ───────────── Rechazos ─────────────

    [FactConBaseDeAsignacion]
    public async Task Otro_horario_otro_profesional_y_al_tercer_rechazo_vuelve_a_recepcion()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var b = Profesional(esp, "Beto");
        _d.PacienteProfesional(_d.Paciente("Otro " + _d.Marca), b);   // Beto tiene un paciente más: la primera propuesta es de Alba
        var (pac, informe) = NuevoFormulario(esp);
        await Aceptar(Recepcion(_d.Profesional()), informe);
        var paciente = Paciente(pac);
        var p1 = Propuestas(informe).Single();
        Assert.Equal(a, p1.Profesional);

        // «Prefiero otro horario»: la lista es del MISMO profesional, hasta 10, sin el propuesto y en orden.
        var lista = await Json(await paciente.GetAsync($"/propuestas-asignacion/{p1.Id}/horarios"));
        var horarios = lista.GetProperty("horarios").EnumerateArray().Select(h => DateTime.Parse(h.GetProperty("fechaHora").GetString()!)).ToList();
        Assert.Equal(10, horarios.Count);
        Assert.DoesNotContain(p1.FechaHora, horarios);
        Assert.Equal(horarios.OrderBy(h => h), horarios);

        // Un horario que no está en la lista se rechaza con un mensaje claro.
        var fuera = await paciente.PostAsJsonAsync($"/propuestas-asignacion/{p1.Id}/rechazar", new { tipo = "otro-horario", fechaHora = Ahora.AddYears(1).ToString("yyyy-MM-ddTHH:mm:ss") });
        Assert.Equal(HttpStatusCode.Conflict, fuera.StatusCode);
        Assert.Equal(PropuestaEstado.Pendiente, Propuestas(informe).Single().Estado);

        var elegido = horarios[2];
        var r2 = await paciente.PostAsJsonAsync($"/propuestas-asignacion/{p1.Id}/rechazar", new { tipo = "otro-horario", fechaHora = elegido.ToString("yyyy-MM-ddTHH:mm:ss") });
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var p2 = Propuestas(informe).Last();
        Assert.Equal((a, elegido, 2, PropuestaEstado.Pendiente), (p2.Profesional, p2.FechaHora, p2.Nro, p2.Estado));

        // «Probar con otro profesional»: la siguiente propuesta es de otro, sin repetir a quien ya se rechazó.
        var r3 = await paciente.PostAsJsonAsync($"/propuestas-asignacion/{p2.Id}/rechazar", new { tipo = "otro-profesional" });
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        var p3 = Propuestas(informe).Last();
        Assert.Equal((b, 3, PropuestaEstado.Pendiente), (p3.Profesional, p3.Nro, p3.Estado));
        var pp = await PrimerosPasos(pac);
        Assert.False(pp.GetProperty("propuesta").GetProperty("puedeCambiar").GetBoolean());   // es la última

        // Con la tercera ya no hay lista de horarios; el tercer rechazo devuelve el caso a la recepción.
        Assert.Equal(HttpStatusCode.Conflict, (await paciente.GetAsync($"/propuestas-asignacion/{p3.Id}/horarios")).StatusCode);
        var r4 = await paciente.PostAsJsonAsync($"/propuestas-asignacion/{p3.Id}/rechazar", new { tipo = "ninguno" });
        Assert.Equal(HttpStatusCode.OK, r4.StatusCode);
        var cuerpo = await Json(r4);
        Assert.Equal("por-resolver", cuerpo.GetProperty("codigo").GetString());
        Assert.Equal("Estamos buscando la mejor opción para vos, te avisamos apenas esté lista.", cuerpo.GetProperty("mensaje").GetString());

        Assert.Equal(new[] { "Rechazada", "Rechazada", "Rechazada" }, Propuestas(informe).Select(x => x.Estado).ToArray());
        Assert.Equal("Aprobado", EstadoInforme(informe));
        Assert.Equal("Por resolver", AsignacionEstado(informe));
        Assert.Contains("tres propuestas", AsignacionMotivo(informe)!);
        Assert.Equal(1, AvisosRecepcion(informe, "asignacion-por-resolver"));
        Assert.Equal(3, Auditorias(informe, "El paciente rechazó la propuesta de asignación"));
        Assert.Equal(0, Turnos(pac, a) + Turnos(pac, b) + Vinculos(pac, a) + Vinculos(pac, b));

        var ultimo = await PrimerosPasos(pac);
        Assert.True(ultimo.GetProperty("asignacionPorResolver").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ultimo.GetProperty("propuesta").ValueKind);
        // Y ya no puede responder una propuesta que cerró.
        Assert.Equal(HttpStatusCode.Conflict, (await paciente.PostAsync($"/propuestas-asignacion/{p3.Id}/aceptar", null)).StatusCode);
    }

    [FactConBaseDeAsignacion]
    public async Task Si_al_probar_con_otro_profesional_no_quedan_candidatos_el_caso_vuelve_a_recepcion()
    {
        var esp = _d.Especialidad();
        Profesional(esp, "Alba");   // el único
        var (pac, informe) = NuevoFormulario(esp);
        await Aceptar(Recepcion(_d.Profesional()), informe);
        var p1 = Propuestas(informe).Single();

        var r = await Paciente(pac).PostAsJsonAsync($"/propuestas-asignacion/{p1.Id}/rechazar", new { tipo = "otro-profesional" });

        Assert.Equal("por-resolver", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal(PropuestaEstado.Rechazada, Propuestas(informe).Single().Estado);
        Assert.Equal("Por resolver", AsignacionEstado(informe));
        Assert.Equal("Sin horarios libres para esa especialidad", AsignacionMotivo(informe));
    }

    // ───────────── El horario reservado por una propuesta ─────────────

    [FactConBaseDeAsignacion]
    public async Task Una_propuesta_pendiente_bloquea_su_horario_para_otras_propuestas_y_otras_reservas_pero_no_para_su_paciente()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var recep = Recepcion(_d.Profesional());
        var (pac1, informe1) = NuevoFormulario(esp);
        var (pac2, informe2) = NuevoFormulario(esp);
        await Aceptar(recep, informe1);
        await Aceptar(recep, informe2);
        var s1 = Propuestas(informe1).Single().FechaHora;
        var s2 = Propuestas(informe2).Single().FechaHora;
        Assert.Equal(Primero(TodosLosDias, "09:00", "18:00", m => m != s1), s2);   // el segundo paciente recibe el horario siguiente

        var reglas = _fabrica.Services.GetRequiredService<TurnoReglasService>();
        var otro = _d.Paciente("Otro " + _d.Marca);
        var pedido = reglas.Solicitar(otro, a, s1, "x");
        Assert.False(pedido.Regla.Ok);
        Assert.Equal("RN-01", pedido.Regla.Codigo);
        Assert.Null(pedido.IdTurno);
        Assert.DoesNotContain(s1, reglas.HorariosLibresDelProfesional(a, 30, 3000));
        Assert.DoesNotContain(s1, reglas.HorariosLibres(otro, a, 30, 3000));
        Assert.Contains(s1, reglas.HorariosLibres(pac1, a, 30, 3000));   // su propio horario sí es suyo
        Assert.True(reglas.ValidarSolicitud(pac1, a, s1).Ok);

        // La base lo garantiza aunque dos pedidos lleguen a la vez: un mismo horario no tiene dos propuestas pendientes.
        var ex = Assert.ThrowsAny<SqlException>(() => DatosDePrueba.Ejecutar(@"
            INSERT INTO PropuestaAsignacion (IdInformeAdmision, IdPaciente, IdProfesional, FechaHora, Estado, NroPropuesta, ExpiraEn)
            VALUES (@i, @p, @r, @f, N'Pendiente', 1, DATEADD(HOUR, 48, GETDATE()))", ("@i", informe2), ("@p", pac2), ("@r", a), ("@f", s1)));
        Assert.True(ex.Number is 2601 or 2627);
        await Task.CompletedTask;
    }

    [FactConBaseDeAsignacion]
    public async Task Una_propuesta_vencida_libera_su_horario_y_el_paciente_pasa_a_recepcion_sin_proceso_en_segundo_plano()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var (pac, informe) = NuevoFormulario(esp);
        await Aceptar(Recepcion(_d.Profesional()), informe);
        var p = Propuestas(informe).Single();
        DatosDePrueba.Ejecutar("UPDATE PropuestaAsignacion SET ExpiraEn = @e WHERE IdPropuesta = @i", ("@e", Ahora.AddHours(-1)), ("@i", p.Id));

        // Se evalúa al consultar: el paciente ya no ve la propuesta.
        var pp = await PrimerosPasos(pac);
        Assert.Equal(JsonValueKind.Null, pp.GetProperty("propuesta").ValueKind);
        Assert.True(pp.GetProperty("asignacionPorResolver").GetBoolean());

        var vencida = Propuestas(informe).Single();
        Assert.Equal(PropuestaEstado.Vencida, vencida.Estado);
        Assert.NotNull(vencida.Respuesta);
        Assert.Equal("Por resolver", AsignacionEstado(informe));
        Assert.Contains("no respondió", AsignacionMotivo(informe)!);
        Assert.Equal(1, Auditorias(informe, "Venció la propuesta de asignación"));
        Assert.Equal(1, AvisosRecepcion(informe, "asignacion-por-resolver"));
        Assert.Contains(p.FechaHora, _fabrica.Services.GetRequiredService<TurnoReglasService>().HorariosLibresDelProfesional(a, 30, 3000));   // el horario volvió a estar libre

        // Y ya no se puede aceptar.
        var r = await Paciente(pac).PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("vencida", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal(0, Turnos(pac, a));
    }

    // ───────────── Permisos por rol ─────────────

    [FactConBaseDeAsignacion]
    public async Task Cada_paciente_solo_ve_y_responde_sus_propuestas_y_solo_el_paciente_puede_responder()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var (pac, informe) = NuevoFormulario(esp);
        var recep = Recepcion(_d.Profesional());
        await Aceptar(recep, informe);
        var p = Propuestas(informe).Single();
        var (ajeno, _) = NuevoFormulario(esp);

        // Otro paciente: no la ve ni la responde (404), y la propuesta sigue pendiente.
        var otro = Paciente(ajeno);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.GetAsync($"/propuestas-asignacion/{p.Id}/horarios")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.PostAsJsonAsync($"/propuestas-asignacion/{p.Id}/rechazar", new { tipo = "ninguno" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.GetAsync($"/pacientes/{pac}/primeros-pasos")).StatusCode);
        Assert.Equal(PropuestaEstado.Pendiente, Propuestas(informe).Single().Estado);

        // La recepción, un profesional común y quien no inició sesión no la responden por el paciente.
        Assert.Equal(HttpStatusCode.Forbidden, (await recep.PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ProfesionalSinAdmision(a).PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null)).StatusCode);

        // Y el paciente no puede pedir que la recepción recalcule ni asignar a mano.
        var propio = Paciente(pac);
        Assert.Equal(HttpStatusCode.Forbidden, (await propio.PostAsJsonAsync($"/informes-admision/{informe}/proponer-asignacion", Confirmo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await propio.PutAsJsonAsync($"/informes-admision/{informe}/derivar", new { idProfesional = a })).StatusCode);
        Assert.Equal(PropuestaEstado.Pendiente, Propuestas(informe).Single().Estado);
        Assert.Equal(0, Turnos(pac, a));
    }

    // ───────────── La recepción ─────────────

    [FactConBaseDeAsignacion]
    public async Task La_asignacion_manual_de_recepcion_cancela_la_propuesta_pendiente_y_libera_el_horario()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var manual = Profesional(esp, "Manual");
        var (pac, informe) = NuevoFormulario(esp);
        var recep = Recepcion(_d.Profesional());
        await Aceptar(recep, informe);
        var p = Propuestas(informe).Single();

        var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/derivar", new { idProfesional = manual });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

        var cancelada = Propuestas(informe).Single();
        Assert.Equal(PropuestaEstado.Cancelada, cancelada.Estado);
        Assert.Contains("a mano", cancelada.Motivo);
        Assert.Equal("Derivado", EstadoInforme(informe));
        Assert.Null(AsignacionEstado(informe));
        Assert.Equal(1, Vinculos(pac, manual));
        Assert.Equal(1, Auditorias(informe, "Se canceló la propuesta de asignación"));
        Assert.Contains(p.FechaHora, _fabrica.Services.GetRequiredService<TurnoReglasService>().HorariosLibresDelProfesional(a, 30, 3000));   // el horario queda libre

        // El paciente ya no puede aceptarla.
        var tarde = await Paciente(pac).PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null);
        Assert.Equal(HttpStatusCode.Conflict, tarde.StatusCode);
        Assert.Equal(0, Turnos(pac, a));
    }

    [FactConBaseDeAsignacion]
    public async Task La_recepcion_resuelve_a_mano_un_caso_para_resolver_y_el_listado_lo_marca()
    {
        var esp = _d.Especialidad();
        var sinAgenda = ProfesionalSinAgenda(esp, "Sin agenda");
        var (pac, informe) = NuevoFormulario(esp);
        var recep = Recepcion(_d.Profesional());
        await Aceptar(recep, informe);
        Assert.Equal("Por resolver", AsignacionEstado(informe));

        // Se asigna a un profesional con agenda (la asignación manual de siempre).
        var conAgenda = Profesional(esp, "Con agenda");
        Assert.Equal(HttpStatusCode.NoContent, (await recep.PutAsJsonAsync($"/informes-admision/{informe}/derivar", new { idProfesional = conAgenda })).StatusCode);
        Assert.Equal("Derivado", EstadoInforme(informe));
        Assert.Null(AsignacionEstado(informe));
        Assert.Equal(1, Vinculos(pac, conAgenda));
        Assert.NotEqual(0, sinAgenda);
    }

    [FactConBaseDeAsignacion]
    public async Task La_recepcion_puede_volver_a_calcular_la_propuesta_cuando_el_paciente_ya_firmo_y_sin_confirmacion_no_hace_nada()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var (pac, informe) = NuevoFormulario(esp, consentimiento: false);
        var recep = Recepcion(_d.Profesional());
        await Aceptar(recep, informe);
        Assert.Equal("Por resolver", AsignacionEstado(informe));

        // Todavía sin firmar: no propone.
        var sinFirma = await recep.PostAsJsonAsync($"/informes-admision/{informe}/proponer-asignacion", Confirmo);
        Assert.Equal(HttpStatusCode.Conflict, sinFirma.StatusCode);
        Assert.Empty(Propuestas(informe));

        _d.ConsentimientoFirmado(pac);
        Assert.Equal(HttpStatusCode.BadRequest, (await recep.PostAsJsonAsync($"/informes-admision/{informe}/proponer-asignacion", new { })).StatusCode);   // sin confirmación explícita
        Assert.Empty(Propuestas(informe));

        var r = await recep.PostAsJsonAsync($"/informes-admision/{informe}/proponer-asignacion", Confirmo);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("propuesta", (await Json(r)).GetProperty("codigo").GetString());
        var p = Assert.Single(Propuestas(informe));
        Assert.Equal(a, p.Profesional);
        Assert.Equal("Propuesta", AsignacionEstado(informe));

        // Repetirlo no suma otra propuesta.
        var otraVez = await recep.PostAsJsonAsync($"/informes-admision/{informe}/proponer-asignacion", Confirmo);
        Assert.Equal("ya-propuesta", (await Json(otraVez)).GetProperty("codigo").GetString());
        Assert.Single(Propuestas(informe));
    }

    [FactConBaseDeAsignacion]
    public async Task La_lista_de_la_recepcion_muestra_la_propuesta_pendiente_de_cada_formulario()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var (_, informe) = NuevoFormulario(esp);
        var recep = Recepcion(_d.Profesional());
        await Aceptar(recep, informe);

        var fila = (await Json(await recep.GetAsync("/informes-admision-detalle"))).EnumerateArray().Single(x => x.GetProperty("idInforme").GetInt32() == informe);

        Assert.Equal("Propuesta", fila.GetProperty("asignacionEstado").GetString());
        var pend = fila.GetProperty("propuestaPendiente");
        Assert.Contains("Alba", pend.GetProperty("profesional").GetString());
        Assert.Equal(1, pend.GetProperty("nro").GetInt32());
        Assert.Equal(a, Propuestas(informe).Single().Profesional);
    }

    // ───────────── Aviso a la recepción por cada formulario nuevo ─────────────

    [FactConBaseDeAsignacion]
    public async Task Cada_formulario_nuevo_avisa_a_la_recepcion_una_sola_vez_aunque_se_devuelva_y_se_reenvie()
    {
        var esp = _d.Especialidad();
        var pac = _d.Paciente("Ana Gomez " + _d.Marca, mail: $"{_d.Marca}@example.test");
        DatosDePrueba.Ejecutar("UPDATE Paciente SET FechaNacimiento = @f, Telefono = N'11 5555-0001' WHERE IdPaciente = @p", ("@f", new DateTime(1990, 5, 20)), ("@p", pac));
        var paciente = Paciente(pac);

        var creado = await paciente.PostAsJsonAsync("/informes-admision", new { idEspecialidad = esp, motivoConsulta = "Quiero empezar terapia", tratamientoAnterior = false, recibeMedicacion = false });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var informe = (await Json(creado)).GetProperty("idInforme").GetInt32();
        _d.AlLimpiar("InformeAdmision", "IdInforme", informe);
        Assert.Equal(1, AvisosRecepcion(informe, "formulario-nuevo"));

        // La recepción lo devuelve y el paciente lo reenvía: mientras el aviso siga sin leer, no se duplica; si ya lo leyó, vuelve a avisar.
        var recep = Recepcion(_d.Profesional());
        Assert.Equal(HttpStatusCode.OK, (await recep.PutAsJsonAsync($"/informes-admision/{informe}/devolver", new { confirmo = true, aclaracion = "Falta el motivo" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await paciente.PutAsJsonAsync($"/pacientes/{pac}/informe-admision", new { idEspecialidad = esp, motivoConsulta = "Ahora sí", tratamientoAnterior = false, recibeMedicacion = false })).StatusCode);
        Assert.Equal(1, AvisosRecepcion(informe, "formulario-nuevo"));

        DatosDePrueba.Ejecutar("UPDATE NotificacionInterna SET Leida = 1 WHERE Tipo = N'formulario-nuevo' AND Mensaje LIKE @m", ("@m", $"%(formulario #{informe})"));
        Assert.Equal(HttpStatusCode.OK, (await recep.PutAsJsonAsync($"/informes-admision/{informe}/devolver", new { confirmo = true, aclaracion = "Otra vez" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await paciente.PutAsJsonAsync($"/pacientes/{pac}/informe-admision", new { idEspecialidad = esp, motivoConsulta = "Y de nuevo", tratamientoAnterior = false, recibeMedicacion = false })).StatusCode);
        Assert.Equal(2, AvisosRecepcion(informe, "formulario-nuevo"));
    }

    // ───────────── Si la recepción cambia de opinión ─────────────

    [FactConBaseDeAsignacion]
    public async Task Si_la_recepcion_devuelve_el_formulario_o_lo_cierra_la_propuesta_pendiente_se_cancela_y_el_horario_queda_libre()
    {
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var recep = Recepcion(_d.Profesional());
        var reglas = _fabrica.Services.GetRequiredService<TurnoReglasService>();

        var (pacDevuelto, devuelto) = NuevoFormulario(esp);
        await Aceptar(recep, devuelto);
        var horario = Propuestas(devuelto).Single().FechaHora;
        Assert.Equal(HttpStatusCode.OK, (await recep.PutAsJsonAsync($"/informes-admision/{devuelto}/devolver", new { confirmo = true, aclaracion = "Falta aclarar el motivo" })).StatusCode);
        Assert.Equal(PropuestaEstado.Cancelada, Propuestas(devuelto).Single().Estado);
        Assert.Null(AsignacionEstado(devuelto));
        Assert.Contains(horario, reglas.HorariosLibresDelProfesional(a, 30, 3000));
        Assert.Equal(HttpStatusCode.Conflict, (await Paciente(pacDevuelto).PostAsync($"/propuestas-asignacion/{Propuestas(devuelto).Single().Id}/aceptar", null)).StatusCode);

        var (_, cerrado) = NuevoFormulario(esp);
        await Aceptar(recep, cerrado);
        Assert.Equal(HttpStatusCode.OK, (await recep.PutAsJsonAsync($"/informes-admision/{cerrado}/otro-recurso", new { confirmo = true, motivo = "Consulta por otra institución" })).StatusCode);
        Assert.Equal(PropuestaEstado.Cancelada, Propuestas(cerrado).Single().Estado);
        Assert.Null(AsignacionEstado(cerrado));
    }

    // ───────────── Varias a la vez ─────────────

    [FactConBaseDeAsignacion]
    public async Task Aceptar_y_confirmar_a_la_vez_para_varios_pacientes_no_se_traba_y_no_repite_ningun_horario()
    {
        // La auditoría se escribe con un bloqueo hasta el final de la transacción: si una transacción leyera por otra conexión DESPUÉS de escribir, varias a la vez se trabarían
        // hasta el tiempo agotado (30 s). Acá seis aceptaciones y seis confirmaciones simultáneas tienen que terminar enseguida, cada una con su propio horario.
        var esp = _d.Especialidad();
        var a = Profesional(esp, "Alba");
        var recep = Recepcion(_d.Profesional());
        var formularios = Enumerable.Range(0, 6).Select(_ => NuevoFormulario(esp)).ToList();
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        var aceptadas = await Task.WhenAll(formularios.Select(f => recep.PutAsJsonAsync($"/informes-admision/{f.Informe}/aceptar", Confirmo)));
        Assert.All(aceptadas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var propuestas = formularios.Select(f => Propuestas(f.Informe).Single()).ToList();
        Assert.All(propuestas, p => Assert.Equal(PropuestaEstado.Pendiente, p.Estado));
        Assert.Equal(6, propuestas.Select(p => p.FechaHora).Distinct().Count());   // ningún horario repetido

        var confirmadas = await Task.WhenAll(formularios.Zip(propuestas, (f, p) => Paciente(f.Paciente).PostAsync($"/propuestas-asignacion/{p.Id}/aceptar", null)));
        Assert.All(confirmadas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        reloj.Stop();

        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(25), $"tardaron {reloj.Elapsed.TotalSeconds:0} s: algo se trabó");
        Assert.Equal(6, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdProfesional = @r AND IdPaciente IN (SELECT IdPaciente FROM InformeAdmision WHERE IdProfesionalDerivado = @r)", ("@r", a)));
        Assert.All(formularios, f => Assert.Equal("Derivado", EstadoInforme(f.Informe)));
    }
}
