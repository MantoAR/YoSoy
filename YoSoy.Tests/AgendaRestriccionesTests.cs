// Migración 33 (agenda y matrícula, rama AAA1): feriados, bloqueos y vencimiento de matrícula sacan horarios de la agenda.
// Mismo escenario que el resto (Escenario.cs): jueves 24/09/2026 09:00; el profesional 10 atiende de lunes a viernes de
// 09:00 a 12:00, cada 30 minutos. Las restricciones se inyectan con un repositorio que vuelve a implementar
// ITurnoRepositorio.Restricciones (el resto de los tests usa la implementación por defecto: sin restricciones).
public class AgendaRestriccionesTests
{
    private sealed class RepoConRestricciones : FakeTurnoRepositorio, ITurnoRepositorio
    {
        public RestriccionesAgenda R = RestriccionesAgenda.Ninguna;
        public RepoConRestricciones(BaseEnMemoria db) : base(db) { }
        public RestriccionesAgenda Restricciones(int idProfesional, DateTime desde, DateTime hasta) => R;
    }

    private readonly Escenario _e = new();
    private readonly RepoConRestricciones _repo;
    private readonly TurnoReglasService _reglas;

    public AgendaRestriccionesTests()
    {
        _repo = new RepoConRestricciones(_e.Db);
        _reglas = new TurnoReglasService(_repo, _e.Reloj, _e.Opciones, _e.Consentimiento);
    }

    private static readonly DateTime Viernes25 = new(2026, 9, 25), Lunes28 = new(2026, 9, 28), Martes29 = new(2026, 9, 29), Miercoles30 = new(2026, 9, 30);

    [Fact]
    public void Sin_restricciones_el_calculo_no_cambia()
    {
        var libres = _reglas.HorariosLibresDelProfesional(10, 7, 500);
        Assert.Contains(Viernes25.AddHours(9), libres);
        Assert.Contains(Lunes28.AddHours(11.5), libres);
    }

    [Fact]
    public void Un_feriado_no_ofrece_ningun_horario_ese_dia_y_no_se_puede_pedir()
    {
        _repo.R = new RestriccionesAgenda(new[] { Viernes25 }, Array.Empty<FranjaBloqueada>(), null);

        var libres = _reglas.HorariosLibresDelProfesional(10, 7, 500);
        Assert.DoesNotContain(libres, h => h.Date == Viernes25);
        Assert.Contains(libres, h => h.Date == Lunes28);

        var r = _reglas.ValidarSolicitud(1, 10, Viernes25.AddHours(10));
        Assert.False(r.Ok);
        Assert.Equal("RN-08", r.Codigo);
        Assert.Contains("feriado", r.Mensaje);
        Assert.Equal(6, _reglas.HorasNoDisponibles(10, Viernes25).Count);   // 09:00 a 11:30
    }

    [Fact]
    public void Un_bloqueo_saca_los_horarios_que_se_superponen_aunque_sea_en_parte()
    {
        // 10:15 a 10:45 pisa el turno de las 10:00 (dura 30 min) y el de las 10:30.
        _repo.R = new RestriccionesAgenda(Array.Empty<DateTime>(),
            new[] { new FranjaBloqueada(Lunes28.AddHours(10.25), Lunes28.AddHours(10.75), "Capacitación") }, null);

        var libres = _reglas.HorariosLibresDelProfesional(10, 7, 500).Where(h => h.Date == Lunes28).Select(h => h.TimeOfDay).ToList();
        Assert.Equal(new[] { new TimeSpan(9, 0, 0), new TimeSpan(9, 30, 0), new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0) }, libres);
        Assert.Equal("RN-08", _reglas.ValidarSolicitud(1, 10, Lunes28.AddHours(10)).Codigo);
        Assert.True(_reglas.ValidarSolicitud(1, 10, Lunes28.AddHours(11)).Ok);
    }

    [Fact]
    public void Desde_el_vencimiento_de_la_matricula_no_se_ofrece_nada_y_el_dia_anterior_si()
    {
        _repo.R = new RestriccionesAgenda(Array.Empty<DateTime>(), Array.Empty<FranjaBloqueada>(), Miercoles30);

        var libres = _reglas.HorariosLibresDelProfesional(10, 30, 500);
        Assert.Contains(libres, h => h.Date == Martes29);
        Assert.DoesNotContain(libres, h => h.Date >= Miercoles30);

        Assert.True(_reglas.ValidarSolicitud(1, 10, Martes29.AddHours(11.5)).Ok);
        var r = _reglas.ValidarSolicitud(1, 10, Miercoles30.AddHours(9));
        Assert.Equal("RN-08", r.Codigo);
        Assert.Contains("matrícula", r.Mensaje);
    }

    [Fact]
    public void Con_la_matricula_vencida_tampoco_se_puede_iniciar_una_cita_urgente()
    {
        _repo.R = new RestriccionesAgenda(Array.Empty<DateTime>(), Array.Empty<FranjaBloqueada>(), new DateTime(2026, 9, 24));
        var r = _reglas.SolicitarUrgente(1, 10, _e.Reloj.Ahora.AddMinutes(5), null);
        Assert.False(r.Regla.Ok);
        Assert.Null(r.IdTurno);
    }

    [Fact]
    public void Un_feriado_o_un_bloqueo_no_frenan_una_cita_urgente()
    {
        _repo.R = new RestriccionesAgenda(new[] { _e.Reloj.Ahora.Date },
            new[] { new FranjaBloqueada(_e.Reloj.Ahora, _e.Reloj.Ahora.AddHours(2), "Personal") }, null);
        Assert.True(_reglas.SolicitarUrgente(1, 10, _e.Reloj.Ahora.AddMinutes(5), null).Regla.Ok);
    }

    [Fact]
    public void Reprogramar_a_un_horario_bloqueado_da_el_motivo()
    {
        _repo.R = new RestriccionesAgenda(new[] { Viernes25 }, Array.Empty<FranjaBloqueada>(), null);
        Assert.NotNull(_reglas.MotivoNoDisponible(10, Viernes25.AddHours(9)));
        Assert.Null(_reglas.MotivoNoDisponible(10, Lunes28.AddHours(9)));
    }

    [Theory]
    [InlineData(null, "Sin vencimiento cargado")]
    [InlineData("2026-09-24", "Vencida")]      // vence hoy: desde hoy no se atiende
    [InlineData("2026-09-01", "Vencida")]
    [InlineData("2026-09-25", "Por vencer")]
    [InlineData("2026-10-24", "Por vencer")]   // justo 30 días
    [InlineData("2026-10-25", "Vigente")]
    public void Estado_de_la_matricula_segun_el_vencimiento(string? vence, string esperado)
    {
        DateTime? v = vence is null ? null : DateTime.Parse(vence, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(esperado, EstadoMatricula.De(v, new DateTime(2026, 9, 24)));
    }

    [Theory]
    [InlineData("licencia", "Licencia")]
    [InlineData("CAPACITACION", "Capacitación")]
    [InlineData("vacaciones ", "Vacaciones")]
    [InlineData("inventado", null)]
    [InlineData(null, null)]
    public void El_motivo_de_bloqueo_se_toma_del_catalogo(string? texto, string? esperado) =>
        Assert.Equal(esperado, MotivosBloqueo.Normalizar(texto));
}
