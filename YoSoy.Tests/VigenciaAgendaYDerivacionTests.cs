// Fecha de inicio y de fin de las franjas de la agenda (migración 27) y derivar solo con la entrevista de admisión hecha.
// Reloj del escenario: jueves 24/09/2026 09:00. El profesional 10 atiende de lunes a viernes de 9 a 12, cada 30 minutos.
public class VigenciaAgendaYDerivacionTests
{
    private readonly Escenario _e = new();
    private static DateTime Dia(int d, int h, int m = 0) => new(2026, 9, d, h, m, 0);   // septiembre 2026

    private void SoloConVigencia(DateTime? desde, DateTime? hasta)
    {
        foreach (var a in _e.Db.Agenda) { a.FechaInicio = desde; a.FechaFin = hasta; }
    }

    // ── Vigencia ─────────────────────────────────────────────

    [Fact]
    public void Sin_fechas_la_franja_vale_siempre()
        => Assert.Equal(Dia(24, 9, 30), _e.Reglas.PrimerHorarioLibre(1, 10));

    [Fact]
    public void Antes_de_la_fecha_de_inicio_no_se_ofrece()
    {
        SoloConVigencia(Dia(28, 0), null);   // empieza el lunes 28
        Assert.Equal(Dia(28, 9), _e.Reglas.PrimerHorarioLibre(1, 10));
        Assert.False(_e.Reglas.ValidarSolicitud(1, 10, Dia(25, 9)).Ok);
    }

    [Fact]
    public void Despues_de_la_fecha_de_fin_no_se_ofrece()
    {
        SoloConVigencia(null, Dia(25, 0));   // termina el viernes 25, inclusive
        var libres = _e.Reglas.HorariosLibresDelProfesional(10, 14, 500);
        Assert.Contains(Dia(25, 11, 30), libres);
        Assert.DoesNotContain(libres, h => h.Date > Dia(25, 0));
        Assert.False(_e.Reglas.ValidarSolicitud(1, 10, Dia(28, 9)).Ok);
    }

    [Fact]
    public void Una_agenda_vencida_no_cuenta_para_derivar()
    {
        SoloConVigencia(null, Dia(23, 0));   // terminó ayer
        Assert.False(_e.Reglas.TieneAgendaDeTurnos(10));
        Assert.Null(_e.Reglas.PrimerHorarioLibre(1, 10));
    }

    [Fact]
    public void Una_agenda_que_empieza_mas_adelante_si_cuenta_para_derivar()
    {
        SoloConVigencia(Dia(28, 0), null);
        Assert.True(_e.Reglas.TieneAgendaDeTurnos(10));
    }

    // ── Derivar solo con la entrevista hecha ──────────────────

    private static readonly DateTime Ahora = Dia(24, 9);

    [Fact]
    public void Con_la_entrevista_hecha_y_observaciones_se_puede_derivar()
        => Assert.Null(DerivacionAdmision.Validar("En entrevista", Dia(23, 15), "Motivo de consulta: ansiedad.", Ahora));

    [Fact]
    public void Sin_programar_no_se_puede_derivar()
        // Ya no hay entrevista que programar: un formulario «Pendiente» espera la revisión de la recepcionista (primeros pasos). La entrevista sin programar sigue igual para los estados viejos.
        => Assert.Equal("FORMULARIO_SIN_ACEPTAR", DerivacionAdmision.Validar("Pendiente", null, "x", Ahora)?.Codigo);

    [Fact]
    public void Un_informe_viejo_en_entrevista_sin_fecha_sigue_pidiendo_programarla()
        => Assert.Equal("ENTREVISTA_SIN_PROGRAMAR", DerivacionAdmision.Validar("En entrevista", null, "x", Ahora)?.Codigo);

    [Fact]
    public void Antes_del_horario_de_la_entrevista_no_se_puede_derivar()
    {
        var r = DerivacionAdmision.Validar("En entrevista", Dia(24, 15), "x", Ahora);
        Assert.Equal("ENTREVISTA_NO_REALIZADA", r?.Codigo);
        Assert.Contains("24/09/2026 a las 15:00", r?.Mensaje);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Sin_observaciones_no_se_puede_derivar(string? observaciones)
        => Assert.Equal("SIN_OBSERVACIONES", DerivacionAdmision.Validar("En entrevista", Dia(23, 15), observaciones, Ahora)?.Codigo);

    [Fact]
    public void Un_rechazado_se_puede_volver_a_derivar()
        => Assert.Null(DerivacionAdmision.Validar("Rechazado", Dia(20, 10), "Ya entrevistado.", Ahora));

    [Fact]
    public void Un_derivado_no_se_vuelve_a_derivar()
        => Assert.Equal("ESTADO", DerivacionAdmision.Validar("Derivado", Dia(20, 10), "x", Ahora)?.Codigo);
}
