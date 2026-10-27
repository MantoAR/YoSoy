using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Se OMITE si la base no tiene la migración 35 (ausencias, abandono, alta estructurada).</summary>
public sealed class FactConBaseDeAusenciasAttribute : FactAttribute
{
    public FactConBaseDeAusenciasAttribute()
    {
        if (Convert.ToInt32(DatosDePrueba.Escalar("SELECT CASE WHEN OBJECT_ID('dbo.PropuestaAbandono', 'U') IS NOT NULL AND OBJECT_ID('dbo.ControlSeguimiento', 'U') IS NOT NULL THEN 1 ELSE 0 END")) != 1)
            Skip = "La base no tiene la migración migrations/add_ausencias_alta.sql.";
    }
}

// Ausencias con umbrales (migración 35) contra SQL Server de verdad y la aplicación completa: 15 minutos de tolerancia, aviso a los 10, alerta a la 2.ª ausencia consecutiva y PROPUESTA (no abandono
// automático) a la 3.ª, que el profesional confirma o descarta. Derivados y en alta no generan alertas. Nadie recibe mails: el servicio de correo es un espía.
[Trait("Categoria", "Integracion")]
public class AusenciasBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly EmailServiceEspia _mails = new();
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly List<int> _turnos = new();
    private static readonly object Confirma = new { confirma = true };

    public AusenciasBaseRealTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(_mails); }));
    }

    public void Dispose()
    {
        DatosDePrueba.Ejecutar("DELETE FROM NotificacionInterna WHERE Mensaje LIKE @m", ("@m", "%" + _d.Marca + "%"));
        foreach (var t in _turnos) DatosDePrueba.Ejecutar("DELETE FROM AvisoSeguimiento WHERE Clave LIKE @c", ("@c", $"%:turno:{t}"));
        _fabrica.Dispose();
        _d.Dispose();
    }

    // ───────────── Armado ─────────────

    private DateTime Ahora => _fabrica.Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime;

    private HttpClient ComoProfesional(int idProfesional) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(idProfesional), Roles.Profesional, idProfesional: idProfesional);
    private HttpClient ComoPaciente(int idPaciente) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(idPaciente), Roles.Paciente, idPaciente: idPaciente);
    private HttpClient ComoCrm() => DatosDePrueba.Cliente(_fabrica, _d.Usuario(), Roles.Crm);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }

    /// <summary>Un profesional y un paciente a su cargo, con un caso «Asignado».</summary>
    private (int Prof, int Pac, int Caso) Escenario(string estadoCaso = "Asignado")
    {
        var prof = _d.Profesional(_d.Especialidad(), nombre: "Prof " + _d.Marca);
        var pac = _d.Paciente("Paciente " + _d.Marca, mail: $"{_d.Marca}@example.test");
        _d.PacienteProfesional(pac, prof);
        _d.AlLimpiar("DerivacionPsiquiatrica", "IdPaciente", pac);
        _d.AlLimpiar("Turno", "IdPaciente", pac);
        var caso = Convert.ToInt32(DatosDePrueba.Escalar(
            "INSERT INTO Caso (IdPaciente, Estado, IdProfesionalAsignado, Origen) VALUES (@p, @e, @m, N'Admision'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@p", pac), ("@e", estadoCaso), ("@m", prof)));
        _d.AlLimpiar("Caso", "IdCaso", caso);
        return (prof, pac, caso);
    }

    /// <summary>Un turno que empezó hace tantos minutos (puede cruzar la medianoche: la fecha sale del instante).</summary>
    private int TurnoHace(int pac, int prof, string estado, int minutos)
    {
        var inicio = Ahora.AddMinutes(-minutos);
        var id = _d.Turno(pac, prof, estado, inicio.Date, new TimeSpan(inicio.Hour, inicio.Minute, 0));
        _turnos.Add(id);
        return id;
    }

    /// <summary>Un turno de días atrás (para armar el historial de asistencia).</summary>
    private int TurnoDiasAtras(int pac, int prof, string estado, int dias)
    {
        var id = _d.Turno(pac, prof, estado, Ahora.Date.AddDays(-dias), TimeSpan.FromHours(10));
        _turnos.Add(id);
        return id;
    }

    private static string EstadoDelTurno(int id) => (string)DatosDePrueba.Escalar("SELECT et.Descripcion FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @t", ("@t", id))!;
    private static string EstadoDelCaso(int id) => (string)DatosDePrueba.Escalar("SELECT Estado FROM Caso WHERE IdCaso = @c", ("@c", id))!;
    private int Avisos(string tipo, string destinatario) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Tipo = @t AND Destinatario = @d AND Mensaje LIKE @m", ("@t", tipo), ("@d", destinatario), ("@m", "%" + _d.Marca + "%"));
    private static int Auditorias(string entidad, int id, string accion) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Entidad = @e AND IdEntidad = @i AND Accion = @a", ("@e", entidad), ("@i", id), ("@a", accion));
    private static int Propuestas(int caso, string? estado = null) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM PropuestaAbandono WHERE IdCaso = @c AND (@e IS NULL OR Estado = @e)", ("@c", caso), ("@e", estado));

    private static Task<HttpResponseMessage> Marcar(HttpClient c, int turno, object? cuerpo = null) => c.PostAsJsonAsync($"/turnos/{turno}/marcar-ausente", cuerpo ?? Confirma);
    private static Task<HttpResponseMessage> Seguimiento(HttpClient c, int prof) => c.GetAsync($"/profesionales/{prof}/seguimiento");

    // ───────────── La tolerancia de 15 minutos ─────────────

    [FactConBaseDeAusencias]
    public async Task Antes_de_los_15_minutos_no_se_puede_marcar_ausente_y_no_cambia_nada()
    {
        var (prof, pac, _) = Escenario();
        var turno = TurnoHace(pac, prof, "Confirmado", 5);

        var r = await Marcar(ComoProfesional(prof), turno);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("demasiado-pronto", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal("Confirmado", EstadoDelTurno(turno));
        Assert.Equal(0, Auditorias("Turno", turno, "Turno marcado como ausente"));
    }

    [FactConBaseDeAusencias]
    public async Task El_estado_de_ausencia_habilita_el_boton_recien_a_los_15_minutos()
    {
        var (prof, pac, _) = Escenario();
        var temprano = TurnoHace(pac, prof, "Confirmado", 5);
        var tarde = TurnoHace(pac, prof, "Confirmado", 20);

        var a = await Json(await ComoProfesional(prof).GetAsync($"/turnos/{temprano}/ausencia"));
        var b = await Json(await ComoProfesional(prof).GetAsync($"/turnos/{tarde}/ausencia"));

        Assert.False(a.GetProperty("puedeMarcar").GetBoolean());
        Assert.True(b.GetProperty("puedeMarcar").GetBoolean());
        Assert.False(b.GetProperty("pacienteConectado").GetBoolean());
    }

    [FactConBaseDeAusencias]
    public async Task Sin_confirmacion_explicita_no_se_marca()
    {
        var (prof, pac, _) = Escenario();
        var turno = TurnoHace(pac, prof, "Confirmado", 20);

        var r = await Marcar(ComoProfesional(prof), turno, new { });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Confirmado", EstadoDelTurno(turno));
    }

    [FactConBaseDeAusencias]
    public async Task Pasados_15_minutos_el_profesional_lo_marca_con_auditoria_y_repetirlo_no_duplica_nada()
    {
        var (prof, pac, _) = Escenario();
        var turno = TurnoHace(pac, prof, "Confirmado", 20);
        var c = ComoProfesional(prof);

        var r1 = await Marcar(c, turno);
        var r2 = await Marcar(c, turno);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.False((await Json(r1)).GetProperty("repetido").GetBoolean());
        Assert.True((await Json(r2)).GetProperty("repetido").GetBoolean());
        Assert.Equal("Ausente", EstadoDelTurno(turno));
        Assert.Equal(1, Auditorias("Turno", turno, "Turno marcado como ausente"));
    }

    [FactConBaseDeAusencias]
    public async Task Si_el_paciente_ya_pidio_entrar_a_la_sala_no_se_puede_marcar_ausente()
    {
        var (prof, pac, _) = Escenario();
        var turno = TurnoHace(pac, prof, "Confirmado", 20);
        AusenciasDAO.RegistrarConexion(turno, "Paciente");
        AusenciasDAO.RegistrarConexion(turno, "Paciente");   // repetirlo no falla ni duplica

        var r = await Marcar(ComoProfesional(prof), turno);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("paciente-conectado", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal("Confirmado", EstadoDelTurno(turno));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM TurnoConexion WHERE IdTurno = @t AND Rol = N'Paciente'", ("@t", turno)));
    }

    [FactConBaseDeAusencias]
    public async Task Un_turno_Pendiente_no_se_marca_ausente_no_cuenta_como_turno_confirmado()
    {
        var (prof, pac, _) = Escenario();
        var turno = TurnoHace(pac, prof, "Pendiente", 20);

        var r = await Marcar(ComoProfesional(prof), turno);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("estado-no-permitido", (await Json(r)).GetProperty("codigo").GetString());
    }

    // ───────────── Aviso temprano (10 minutos) ─────────────

    [FactConBaseDeAusencias]
    public async Task A_los_10_minutos_avisa_una_sola_vez_por_turno_aunque_se_consulte_muchas_veces()
    {
        var (prof, pac, _) = Escenario();
        var turno = TurnoHace(pac, prof, "Confirmado", 12);
        var c = ComoProfesional(prof);

        await Seguimiento(c, prof);
        await Seguimiento(c, prof);
        new AusenciasDAO().EvaluarAvisosTempranos(Ahora);   // y la pasada del servicio de fondo

        Assert.Equal(1, Avisos("ausencia-temprana", "Profesional"));
        Assert.Contains("todavía no se conectó", (string)DatosDePrueba.Escalar("SELECT TOP 1 Titulo FROM NotificacionInterna WHERE Tipo = N'ausencia-temprana' AND IdTurno = @t", ("@t", turno))!);
    }

    [FactConBaseDeAusencias]
    public async Task Antes_de_los_10_minutos_no_avisa_y_si_el_paciente_ya_entro_tampoco()
    {
        var (prof, pac, _) = Escenario();
        TurnoHace(pac, prof, "Confirmado", 5);
        var conectado = TurnoHace(pac, prof, "Confirmado", 30);
        AusenciasDAO.RegistrarConexion(conectado, "Paciente");

        await Seguimiento(ComoProfesional(prof), prof);

        Assert.Equal(0, Avisos("ausencia-temprana", "Profesional"));
    }

    [FactConBaseDeAusencias]
    public async Task Un_paciente_derivado_o_en_alta_no_genera_el_aviso_temprano()
    {
        var (prof, pac, _) = Escenario();
        TurnoHace(pac, prof, "Confirmado", 12);
        DatosDePrueba.Ejecutar("INSERT INTO DerivacionPsiquiatrica (IdPaciente, IdProfesional) VALUES (@p, @m)", ("@p", pac), ("@m", prof));
        var (prof2, pac2, caso2) = Escenario("En alta");
        TurnoHace(pac2, prof2, "Confirmado", 12);

        await Seguimiento(ComoProfesional(prof), prof);
        await Seguimiento(ComoProfesional(prof2), prof2);

        Assert.Equal(0, Avisos("ausencia-temprana", "Profesional"));
    }

    // ───────────── 2.ª y 3.ª ausencia consecutiva ─────────────

    [FactConBaseDeAusencias]
    public async Task La_primera_ausencia_no_alerta()
    {
        var (prof, pac, _) = Escenario();
        var turno = TurnoHace(pac, prof, "Confirmado", 20);

        await Marcar(ComoProfesional(prof), turno);

        Assert.Equal(0, Avisos("ausencia-segunda", "Profesional"));
        Assert.Equal(0, Avisos("ausencia-segunda", "Administrador"));
    }

    [FactConBaseDeAusencias]
    public async Task La_segunda_ausencia_consecutiva_avisa_al_profesional_y_a_recepcion_con_el_nombre_sin_duplicar()
    {
        var (prof, pac, caso) = Escenario();
        TurnoDiasAtras(pac, prof, "Ausente", 7);
        var turno = TurnoHace(pac, prof, "Confirmado", 20);
        var c = ComoProfesional(prof);

        var r = await Marcar(c, turno);
        await Marcar(c, turno);   // doble clic

        Assert.True((await Json(r)).GetProperty("alertaSegundaAusencia").GetBoolean());
        Assert.Equal(1, Avisos("ausencia-segunda", "Profesional"));
        Assert.Equal(1, Avisos("ausencia-segunda", "Administrador"));
        Assert.Contains("Paciente " + _d.Marca, (string)DatosDePrueba.Escalar("SELECT TOP 1 Mensaje FROM NotificacionInterna WHERE Tipo = N'ausencia-segunda' AND Destinatario = N'Administrador' AND Mensaje LIKE @m", ("@m", "%" + _d.Marca + "%"))!);
        Assert.Equal(0, Propuestas(caso));
        Assert.Equal("Asignado", EstadoDelCaso(caso));
    }

    [FactConBaseDeAusencias]
    public async Task La_tercera_crea_una_propuesta_y_NO_pasa_al_paciente_a_abandono_ni_cancela_turnos()
    {
        var (prof, pac, caso) = Escenario();
        TurnoDiasAtras(pac, prof, "Ausente", 14);
        TurnoDiasAtras(pac, prof, "Ausente", 7);
        var futuro = _d.Turno(pac, prof, "Confirmado", Ahora.Date.AddDays(3), TimeSpan.FromHours(10));
        var turno = TurnoHace(pac, prof, "Confirmado", 20);

        var r = await Json(await Marcar(ComoProfesional(prof), turno));

        Assert.True(r.GetProperty("propuestaAbandono").GetBoolean());
        Assert.Equal(1, Propuestas(caso, "Pendiente"));
        Assert.Equal("Asignado", EstadoDelCaso(caso));            // el sistema propone: no decide
        Assert.Equal("Confirmado", EstadoDelTurno(futuro));        // y no cancela turnos
        Assert.Equal(0, Avisos("ausencia-segunda", "Profesional"));   // a la 3.ª no se repite la alerta de la 2.ª
    }

    [FactConBaseDeAusencias]
    public async Task Una_sesion_realizada_entre_medio_reinicia_el_conteo()
    {
        var (prof, pac, caso) = Escenario();
        TurnoDiasAtras(pac, prof, "Ausente", 21);
        TurnoDiasAtras(pac, prof, "Realizado", 14);
        var turno = TurnoHace(pac, prof, "Confirmado", 20);

        var r = await Json(await Marcar(ComoProfesional(prof), turno));

        Assert.Equal(1, r.GetProperty("ausenciasConsecutivas").GetInt32());
        Assert.False(r.GetProperty("alertaSegundaAusencia").GetBoolean());
        Assert.Equal(0, Avisos("ausencia-segunda", "Profesional"));
        Assert.Equal(0, Propuestas(caso));
    }

    [FactConBaseDeAusencias]
    public async Task Un_paciente_en_alta_o_derivado_se_marca_pero_no_genera_alertas_ni_propuesta()
    {
        var (prof, pac, caso) = Escenario("En alta");
        TurnoDiasAtras(pac, prof, "Ausente", 14);
        TurnoDiasAtras(pac, prof, "Ausente", 7);
        var turno = TurnoHace(pac, prof, "Confirmado", 20);
        var (prof2, pac2, caso2) = Escenario();
        TurnoDiasAtras(pac2, prof2, "Ausente", 7);
        DatosDePrueba.Ejecutar("INSERT INTO DerivacionPsiquiatrica (IdPaciente, IdProfesional) VALUES (@p, @m)", ("@p", pac2), ("@m", prof2));
        var turno2 = TurnoHace(pac2, prof2, "Confirmado", 20);

        var r1 = await Marcar(ComoProfesional(prof), turno);
        var r2 = await Marcar(ComoProfesional(prof2), turno2);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("Ausente", EstadoDelTurno(turno));
        Assert.Equal(0, Propuestas(caso));
        Assert.Equal(0, Avisos("ausencia-segunda", "Profesional") + Avisos("ausencia-segunda", "Administrador"));
    }

    // ───────────── Confirmar y descartar la propuesta ─────────────

    private async Task<(int Prof, int Pac, int Caso, int Propuesta, int Futuro)> ConPropuesta()
    {
        var (prof, pac, caso) = Escenario();
        TurnoDiasAtras(pac, prof, "Ausente", 14);
        TurnoDiasAtras(pac, prof, "Ausente", 7);
        var futuro = _d.Turno(pac, prof, "Confirmado", Ahora.Date.AddDays(3), TimeSpan.FromHours(10));
        var turno = TurnoHace(pac, prof, "Confirmado", 20);
        var r = await Json(await Marcar(ComoProfesional(prof), turno));
        return (prof, pac, caso, r.GetProperty("idPropuesta").GetInt32(), futuro);
    }

    [FactConBaseDeAusencias]
    public async Task Confirmar_la_propuesta_pasa_el_caso_a_abandono_con_auditoria_idempotente_y_avisa_a_recepcion_sin_cancelar_turnos()
    {
        var (prof, pac, caso, propuesta, futuro) = await ConPropuesta();
        var c = ComoProfesional(prof);

        var sinConfirmar = await c.PostAsJsonAsync($"/propuestas-abandono/{propuesta}/confirmar", new { });
        var r1 = await c.PostAsJsonAsync($"/propuestas-abandono/{propuesta}/confirmar", Confirma);
        var r2 = await c.PostAsJsonAsync($"/propuestas-abandono/{propuesta}/confirmar", Confirma);

        Assert.Equal(HttpStatusCode.BadRequest, sinConfirmar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.True((await Json(r2)).GetProperty("repetido").GetBoolean());
        Assert.Equal("Abandono", EstadoDelCaso(caso));
        Assert.Equal(1, Propuestas(caso, "Confirmada"));
        Assert.Equal(1, Auditorias("Caso", caso, "Caso pasa a Abandono"));
        Assert.Equal(1, Auditorias("PropuestaAbandono", propuesta, "Propuesta de abandono confirmada"));
        Assert.Equal("Confirmado", EstadoDelTurno(futuro));        // no se cancela solo
        Assert.Equal(1, Avisos("ausencia-abandono", "Administrador"));
        Assert.Contains("1 turno", (string)DatosDePrueba.Escalar("SELECT TOP 1 Mensaje FROM NotificacionInterna WHERE Tipo = N'ausencia-abandono' AND Mensaje LIKE @m", ("@m", "%" + _d.Marca + "%"))!);
    }

    [FactConBaseDeAusencias]
    public async Task Descartar_con_motivo_no_cambia_nada_del_paciente_y_sin_motivo_se_rechaza()
    {
        var (prof, pac, caso, propuesta, futuro) = await ConPropuesta();
        var c = ComoProfesional(prof);

        var sinMotivo = await c.PostAsJsonAsync($"/propuestas-abandono/{propuesta}/descartar", new { motivo = "  " });
        var r1 = await c.PostAsJsonAsync($"/propuestas-abandono/{propuesta}/descartar", new { motivo = "Avisó que está de viaje. [TEST]" });
        var r2 = await c.PostAsJsonAsync($"/propuestas-abandono/{propuesta}/descartar", new { motivo = "otra vez" });
        var confirmarDespues = await c.PostAsJsonAsync($"/propuestas-abandono/{propuesta}/confirmar", Confirma);

        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.True((await Json(r2)).GetProperty("repetido").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, confirmarDespues.StatusCode);   // una descartada no se puede confirmar
        Assert.Equal("Asignado", EstadoDelCaso(caso));
        Assert.Equal(1, Propuestas(caso, "Descartada"));
        Assert.Equal(1, Auditorias("PropuestaAbandono", propuesta, "Propuesta de abandono descartada"));
        Assert.Equal(0, Avisos("ausencia-abandono", "Administrador"));
    }

    [FactConBaseDeAusencias]
    public async Task Si_despues_de_la_propuesta_hubo_una_sesion_realizada_ya_no_corresponde_confirmarla()
    {
        var (prof, pac, caso, propuesta, futuro) = await ConPropuesta();
        DatosDePrueba.Ejecutar("UPDATE Turno SET IdEstadoTurno = @r WHERE IdTurno = (SELECT IdTurnoDisparador FROM PropuestaAbandono WHERE IdPropuestaAbandono = @i)", ("@r", _d.EstadoTurno("Realizado")), ("@i", propuesta));

        var r = await ComoProfesional(prof).PostAsJsonAsync($"/propuestas-abandono/{propuesta}/confirmar", Confirma);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("ya-no-corresponde", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal("Asignado", EstadoDelCaso(caso));
    }

    [FactConBaseDeAusencias]
    public async Task La_campanita_muestra_la_propuesta_con_los_turnos_por_delante()
    {
        var (prof, pac, caso, propuesta, futuro) = await ConPropuesta();

        var j = await Json(await Seguimiento(ComoProfesional(prof), prof));

        var p = Assert.Single(j.GetProperty("propuestasAbandono").EnumerateArray());
        Assert.Equal(propuesta, p.GetProperty("idPropuesta").GetInt32());
        Assert.Equal(3, p.GetProperty("ausencias").GetInt32());
        Assert.Single(p.GetProperty("turnosFuturos").EnumerateArray());
    }

    // ───────────── Permisos por rol ─────────────

    [FactConBaseDeAusencias]
    public async Task Solo_el_profesional_del_turno_puede_marcar_y_resolver_las_propuestas()
    {
        var (prof, pac, caso, propuesta, futuro) = await ConPropuesta();
        var ajeno = _d.Profesional(_d.Especialidad(), nombre: "Ajeno " + _d.Marca);
        var turno = TurnoHace(pac, prof, "Confirmado", 30);

        Assert.Equal(HttpStatusCode.NotFound, (await Marcar(ComoProfesional(ajeno), turno)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ComoProfesional(ajeno).PostAsJsonAsync($"/propuestas-abandono/{propuesta}/confirmar", Confirma)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ComoProfesional(ajeno).PostAsJsonAsync($"/propuestas-abandono/{propuesta}/descartar", new { motivo = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Marcar(ComoPaciente(pac), turno)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Marcar(ComoCrm(), turno)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ComoCrm().PostAsJsonAsync($"/propuestas-abandono/{propuesta}/confirmar", Confirma)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ComoPaciente(pac).PostAsJsonAsync($"/propuestas-abandono/{propuesta}/confirmar", Confirma)).StatusCode);
        Assert.Equal("Confirmado", EstadoDelTurno(turno));
        Assert.Equal("Asignado", EstadoDelCaso(caso));
        Assert.Equal(HttpStatusCode.Forbidden, (await Seguimiento(ComoProfesional(ajeno), prof)).StatusCode);   // ni ver el seguimiento de otro
    }
}

// Las reglas puras (sin base): umbrales, conteo de consecutivas, cuándo corresponde cada aviso.
public class AusenciasReglasTests
{
    private static readonly DateTime Inicio = new(2026, 11, 2, 10, 0, 0);

    [Fact]
    public void Los_umbrales_son_15_10_2_y_3()
    {
        Assert.Equal(15, AusenciasReglas.ToleranciaMarcarMinutos);
        Assert.Equal(10, AusenciasReglas.AvisoTempranoMinutos);
        Assert.Equal(2, AusenciasReglas.AusenciasParaAlerta);
        Assert.Equal(3, AusenciasReglas.AusenciasParaProponerAbandono);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(90, true)]
    public void Se_puede_marcar_desde_los_15_minutos(int minutos, bool esperado) =>
        Assert.Equal(esperado, AusenciasReglas.PuedeMarcarse(Inicio, Inicio.AddMinutes(minutos)));

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(60, true)]
    [InlineData(181, false)]
    public void El_aviso_temprano_corre_desde_los_10_minutos_y_no_para_siempre(int minutos, bool esperado) =>
        Assert.Equal(esperado, AusenciasReglas.CorrespondeAvisoTemprano(Inicio, Inicio.AddMinutes(minutos)));

    [Fact]
    public void Las_consecutivas_cortan_en_la_primera_sesion_realizada()
    {
        Assert.Equal(0, AusenciasReglas.ContarConsecutivas(Array.Empty<string>()));
        Assert.Equal(2, AusenciasReglas.ContarConsecutivas(new[] { "Ausente", "Ausente", "Realizado", "Ausente" }));
        Assert.Equal(0, AusenciasReglas.ContarConsecutivas(new[] { "Realizado", "Ausente", "Ausente" }));
        Assert.Equal(3, AusenciasReglas.ContarConsecutivas(new[] { "Ausente", "Ausente", "Ausente" }));
    }

    [Theory]
    [InlineData("En alta", true)]
    [InlineData("Abandono", true)]
    [InlineData("Asignado", false)]
    [InlineData("En atención", false)]
    public void En_alta_y_en_abandono_silencian_las_alertas(string estado, bool silencia) => Assert.Equal(silencia, AusenciasReglas.CasoSilencia(estado));

    [Fact]
    public void Solo_un_caso_asignado_o_en_atencion_se_puede_pasar_a_abandono()
    {
        Assert.True(AusenciasReglas.CasoSePuedeAbandonar("Asignado"));
        Assert.True(AusenciasReglas.CasoSePuedeAbandonar("En atención"));
        Assert.False(AusenciasReglas.CasoSePuedeAbandonar("En alta"));
        Assert.False(AusenciasReglas.CasoSePuedeAbandonar("Cerrado"));
    }
}
