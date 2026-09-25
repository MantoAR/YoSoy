public class TurnoReglasServiceTests
{
    private readonly Escenario _e = new();
    private static DateTime Dia(int d, int h, int m = 0) => new(2026, 9, d, h, m, 0);   // septiembre 2026

    [Fact]
    public void Horarios_libres_empiezan_24_horas_despues_como_minimo()
    {
        var horarios = _e.Reglas.HorariosLibres(1, 10, 14, 5);
        Assert.Equal(5, horarios.Count);
        Assert.Equal(Dia(25, 9), horarios[0]);                                     // viernes 09:00, justo 24 h
        Assert.All(horarios, h => Assert.True(h >= Escenario.Jueves0900.AddHours(24)));
    }

    [Fact]
    public void Horarios_libres_saltean_los_ocupados_pero_no_los_cancelados()
    {
        _e.Db.AgregarTurno(2, 10, Dia(25, 9), BaseEnMemoria.Confirmado);
        _e.Db.AgregarTurno(2, 10, Dia(25, 9, 30), BaseEnMemoria.Cancelado);
        var horarios = _e.Reglas.HorariosLibres(1, 10, 14, 3);
        Assert.Equal(new[] { Dia(25, 9, 30), Dia(25, 10), Dia(25, 10, 30) }, horarios);
    }

    [Fact]
    public void RN07_si_ya_tiene_turno_ese_dia_con_el_profesional_no_ofrece_ese_dia()
    {
        _e.Db.AgregarTurno(1, 10, Dia(25, 11), BaseEnMemoria.Pendiente);
        var horarios = _e.Reglas.HorariosLibres(1, 10, 14, 5);
        Assert.DoesNotContain(horarios, h => h.Date == Dia(25, 0).Date);
        Assert.Equal(Dia(28, 9), horarios[0]);                                     // lunes siguiente
    }

    [Theory]
    [InlineData(2026, 9, 23, 10, 0, "RN-01")]   // ya pasó
    [InlineData(2026, 9, 26, 10, 0, "RN-01")]   // sábado: sin agenda
    [InlineData(2026, 9, 28, 10, 15, "RN-01")]  // no alineado al intervalo de 30 min
    [InlineData(2026, 9, 28, 11, 45, "RN-01")]  // el turno terminaría después de las 12:00
    [InlineData(2026, 9, 24, 15, 0, "RN-01")]   // hoy, fuera de la franja
    [InlineData(2026, 9, 24, 11, 0, "RN-05")]   // hoy, dentro de la franja pero con menos de 24 h
    public void Validar_solicitud_rechaza_con_el_codigo_de_la_regla(int a, int mes, int d, int h, int min, string codigo)
    {
        var r = _e.Reglas.ValidarSolicitud(1, 10, new DateTime(a, mes, d, h, min, 0));
        Assert.False(r.Ok);
        Assert.Equal(codigo, r.Codigo);
    }

    [Fact]
    public void RN01_horario_ocupado_por_otro_paciente()
    {
        _e.Db.AgregarTurno(2, 10, Dia(28, 10), BaseEnMemoria.Pendiente);
        Assert.Equal("RN-01", _e.Reglas.ValidarSolicitud(1, 10, Dia(28, 10)).Codigo);
    }

    [Fact]
    public void RN07_un_solo_turno_activo_por_paciente_profesional_y_dia()
    {
        _e.Db.AgregarTurno(1, 10, Dia(28, 9), BaseEnMemoria.Confirmado);
        Assert.Equal("RN-07", _e.Reglas.ValidarSolicitud(1, 10, Dia(28, 11)).Codigo);
    }

    [Fact]
    public void RN02_solo_se_exige_al_solicitar_si_la_configuracion_lo_pide()
    {
        Assert.True(_e.Reglas.ValidarSolicitud(1, 10, Dia(28, 10)).Ok);

        _e.Opciones.CurrentValue.ExigirConsentimientoParaSolicitar = true;
        Assert.Equal("RN-02", _e.Reglas.ValidarSolicitud(1, 10, Dia(28, 10)).Codigo);

        _e.Db.ConConsentimiento.Add(1);
        Assert.True(_e.Reglas.ValidarSolicitud(1, 10, Dia(28, 10)).Ok);
    }

    [Fact]
    public void Solicitar_crea_el_turno_pendiente_resolviendo_el_estado_por_descripcion()
    {
        var r = _e.Reglas.Solicitar(1, 10, Dia(28, 10), "prueba");
        Assert.True(r.Regla.Ok);
        Assert.Equal("Pendiente", _e.Db.EstadoDe(r.IdTurno!.Value));
    }

    [Fact]
    public void Cancelar_con_mas_de_24_horas_pasa_a_Cancelado()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(28, 10), BaseEnMemoria.Confirmado);
        Assert.True(_e.Reglas.Cancelar(t.IdTurno, 1).Ok);
        Assert.Equal("Cancelado", _e.Db.EstadoDe(t.IdTurno));
    }

    [Fact]
    public void RN05_no_se_cancela_con_menos_de_24_horas()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(25, 8, 59), BaseEnMemoria.Confirmado);
        var r = _e.Reglas.Cancelar(t.IdTurno, 1);
        Assert.Equal("RN-05", r.Codigo);
        Assert.Equal("Confirmado", _e.Db.EstadoDe(t.IdTurno));
    }

    [Fact]
    public void No_se_cancela_un_turno_de_otro_paciente_ni_uno_ya_cancelado()
    {
        var ajeno = _e.Db.AgregarTurno(2, 10, Dia(28, 10), BaseEnMemoria.Pendiente);
        Assert.Equal("PERMISO", _e.Reglas.Cancelar(ajeno.IdTurno, 1).Codigo);

        var cancelado = _e.Db.AgregarTurno(1, 10, Dia(29, 10), BaseEnMemoria.Cancelado);
        Assert.Equal("ESTADO", _e.Reglas.Cancelar(cancelado.IdTurno, 1).Codigo);
    }

    // ── RN-02 al confirmar ──────────────────────────────────

    private Turno Nuevo(Turno anterior, int idEstado) => new()
    {
        IdTurno = anterior.IdTurno, IdPaciente = anterior.IdPaciente, IdProfesional = anterior.IdProfesional,
        IdEstadoTurno = idEstado, Fecha = anterior.Fecha, Hora = anterior.Hora
    };

    [Fact]
    public void RN02_no_se_confirma_sin_consentimiento_y_devuelve_el_link_para_firmarlo()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(28, 10), BaseEnMemoria.Pendiente);
        var r = _e.Reglas.ValidarCambioDeEstado(t, Nuevo(t, BaseEnMemoria.Confirmado));
        Assert.Equal("RN-02", r.Codigo);
        Assert.Equal("https://yosoy.test/consentimiento", r.Link);
        Assert.Equal("RN-02", _e.Reglas.ValidarPasoAConfirmado(t).Codigo);
    }

    [Fact]
    public void RN02_con_el_consentimiento_de_telemedicina_se_confirma()
    {
        _e.Db.ConConsentimiento.Add(1);
        var t = _e.Db.AgregarTurno(1, 10, Dia(28, 10), BaseEnMemoria.Pendiente);
        Assert.True(_e.Reglas.ValidarCambioDeEstado(t, Nuevo(t, BaseEnMemoria.Confirmado)).Ok);
        Assert.True(_e.Reglas.ValidarPasoAConfirmado(t).Ok);
    }

    [Fact]
    public void RN02_un_consentimiento_de_otro_tipo_no_alcanza()
    {
        _e.Db.ConOtroConsentimiento.Add(1);
        var t = _e.Db.AgregarTurno(1, 10, Dia(28, 10), BaseEnMemoria.Pendiente);
        Assert.Equal("RN-02", _e.Reglas.ValidarPasoAConfirmado(t).Codigo);
    }

    [Fact]
    public void RN02_rechazar_cancelar_o_editar_sin_confirmar_no_se_frena()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(28, 10), BaseEnMemoria.Pendiente);
        Assert.True(_e.Reglas.ValidarCambioDeEstado(t, Nuevo(t, BaseEnMemoria.Cancelado)).Ok);
        Assert.True(_e.Reglas.ValidarCambioDeEstado(t, Nuevo(t, BaseEnMemoria.Pendiente)).Ok);
    }

    [Fact]
    public void RN02_aplica_hacia_adelante_un_turno_ya_confirmado_no_se_toca()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(28, 10), BaseEnMemoria.Confirmado);   // dato viejo sin consentimiento
        Assert.True(_e.Reglas.ValidarCambioDeEstado(t, Nuevo(t, BaseEnMemoria.Confirmado)).Ok);
        Assert.True(_e.Reglas.ValidarPasoAConfirmado(t).Ok);
    }

    [Fact]
    public void RN02_modo_estricto_al_solicitar_usa_el_mismo_chequeo_y_link()
    {
        _e.Opciones.CurrentValue.ExigirConsentimientoParaSolicitar = true;
        _e.Db.ConOtroConsentimiento.Add(1);
        var r = _e.Reglas.ValidarSolicitud(1, 10, Dia(28, 10));
        Assert.Equal("RN-02", r.Codigo);
        Assert.Equal("https://yosoy.test/consentimiento", r.Link);
    }

    [Theory]
    [InlineData("lunes", DayOfWeek.Monday)]
    [InlineData("Miércoles", DayOfWeek.Wednesday)]
    [InlineData("miercoles", DayOfWeek.Wednesday)]
    [InlineData("SÁBADO", DayOfWeek.Saturday)]
    public void Dia_de_agenda_se_interpreta_con_o_sin_tilde(string dia, DayOfWeek esperado) =>
        Assert.Equal(esperado, TurnoReglasService.DiaDeLaSemana(dia));
}
