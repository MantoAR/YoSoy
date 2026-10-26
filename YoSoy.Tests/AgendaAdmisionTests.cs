// Entrevistas de admisión con agenda propia (migración 26): la API programa sola la entrevista en el primer horario libre de
// las franjas de admisión de quien la programa, una detrás de otra, y esas franjas no se ofrecen a los pacientes como turnos.
// Reloj del escenario: jueves 24/09/2026 09:00. El profesional 10 atiende turnos de lunes a viernes de 9 a 12.
public class AgendaAdmisionTests
{
    private readonly Escenario _e = new();
    private readonly FakeTurnoRepositorio _repo;
    private readonly TurnoReglasService _reglas;
    private static DateTime Dia(int d, int h, int m = 0) => new(2026, 9, d, h, m, 0);   // septiembre 2026

    public AgendaAdmisionTests()
    {
        _repo = new FakeTurnoRepositorio(_e.Db);
        _reglas = new TurnoReglasService(_repo, _e.Reloj, _e.Opciones, _e.Consentimiento);
    }

    // Jueves y viernes de 14 a 16, cada 30 minutos.
    private void ConFranjasDeAdmision()
    {
        foreach (var dia in new[] { "jueves", "viernes" })
            _e.Db.Agenda.Add(new Agenda { IdProfesional = 10, Dia = dia, HoraInicio = new(14, 0, 0), HoraFin = new(16, 0, 0),
                                          Disponible = true, IntervaloMinutos = 30, EsAdmision = true });
    }

    [Fact]
    public void Sin_franjas_de_admision_no_tiene_agenda_ni_horario()
    {
        Assert.False(_reglas.TieneAgendaDeAdmision(10));
        Assert.Null(_reglas.PrimerHorarioDeAdmision(10, Dia(25, 9)));
    }

    [Fact]
    public void Una_franja_de_admision_no_disponible_no_cuenta()
    {
        _e.Db.Agenda.Add(new Agenda { IdProfesional = 10, Dia = "jueves", HoraInicio = new(14, 0, 0), HoraFin = new(16, 0, 0),
                                      Disponible = false, IntervaloMinutos = 30, EsAdmision = true });
        Assert.False(_reglas.TieneAgendaDeAdmision(10));
    }

    [Fact]
    public void Toma_el_primer_horario_libre_de_la_franja_de_admision()
    {
        ConFranjasDeAdmision();
        Assert.True(_reglas.TieneAgendaDeAdmision(10));
        Assert.Equal(Dia(24, 14), _reglas.PrimerHorarioDeAdmision(10, Dia(25, 9)));
    }

    [Fact]
    public void Las_entrevistas_quedan_una_detras_de_otra_y_saltean_los_turnos()
    {
        ConFranjasDeAdmision();
        _repo.Entrevistas.Add((10, Dia(24, 14)));                          // ya programada
        _e.Db.AgregarTurno(1, 10, Dia(24, 14, 30), BaseEnMemoria.Confirmado);   // un turno a esa hora la ocupa
        Assert.Equal(Dia(24, 15), _reglas.PrimerHorarioDeAdmision(10, Dia(25, 9)));
    }

    [Fact]
    public void Un_turno_cancelado_no_ocupa_el_horario()
    {
        ConFranjasDeAdmision();
        _e.Db.AgregarTurno(1, 10, Dia(24, 14), BaseEnMemoria.Cancelado);
        Assert.Equal(Dia(24, 14), _reglas.PrimerHorarioDeAdmision(10, Dia(25, 9)));
    }

    [Fact]
    public void Las_entrevistas_de_otro_profesional_no_ocupan_el_horario()
    {
        ConFranjasDeAdmision();
        _repo.Entrevistas.Add((99, Dia(24, 14)));
        Assert.Equal(Dia(24, 14), _reglas.PrimerHorarioDeAdmision(10, Dia(25, 9)));
    }

    [Fact]
    public void Si_el_primer_libre_cae_despues_del_limite_no_hay_horario()
    {
        ConFranjasDeAdmision();
        Assert.Null(_reglas.PrimerHorarioDeAdmision(10, Dia(24, 13)));         // el límite (24 h) vence antes de las 14
    }

    [Fact]
    public void Con_el_jueves_completo_pasa_al_viernes()
    {
        ConFranjasDeAdmision();
        foreach (var m in new[] { Dia(24, 14), Dia(24, 14, 30), Dia(24, 15), Dia(24, 15, 30) }) _repo.Entrevistas.Add((10, m));
        Assert.Equal(Dia(25, 14), _reglas.PrimerHorarioDeAdmision(10, Dia(25, 23)));
    }

    // ── Derivar: solo a quien tiene horarios de atención (franjas de turnos) ──

    [Fact]
    public void Con_franjas_de_turnos_se_le_puede_derivar() => Assert.True(_reglas.TieneAgendaDeTurnos(10));

    [Fact]
    public void Sin_agenda_no_se_le_puede_derivar() => Assert.False(_reglas.TieneAgendaDeTurnos(77));

    [Fact]
    public void Solo_con_franjas_de_admision_o_no_disponibles_tampoco()
    {
        _e.Db.Agenda.Add(new Agenda { IdProfesional = 77, Dia = "lunes", HoraInicio = new(9, 0, 0), HoraFin = new(12, 0, 0), Disponible = true, EsAdmision = true });
        _e.Db.Agenda.Add(new Agenda { IdProfesional = 77, Dia = "martes", HoraInicio = new(9, 0, 0), HoraFin = new(12, 0, 0), Disponible = false });
        _e.Db.Agenda.Add(new Agenda { IdProfesional = 77, Dia = null!, HoraInicio = null, HoraFin = null, Disponible = true });
        Assert.False(_reglas.TieneAgendaDeTurnos(77));
    }

    [Fact]
    public void Las_franjas_de_admision_no_se_ofrecen_ni_se_aceptan_como_turnos()
    {
        ConFranjasDeAdmision();
        Assert.DoesNotContain(_reglas.HorariosLibresDelProfesional(10, 2, 100), h => h.Hour >= 14);
        var r = _reglas.ValidarSolicitud(1, 10, Dia(24, 14));
        Assert.False(r.Ok);
        Assert.Equal("RN-01", r.Codigo);
    }
}
