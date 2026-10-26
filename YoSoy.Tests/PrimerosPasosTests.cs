// «Primeros pasos»: el estado de cada paso se calcula con datos reales. Lógica pura (sin base de datos): cada combinación importante.
public class PrimerosPasosTests
{
    private static readonly DateTime Ahora = new(2026, 10, 26, 9, 0, 0);

    private static EstadoTratamiento Firmado => new(true, ConsentimientoTratamiento.Ok, false, new DateTime(2026, 9, 1, 10, 30, 0), "v1", null);
    private static EstadoTratamiento SinFirma => new(false, ConsentimientoTratamiento.SinFirma, false, null, null, null);
    private static InformePaso Informe(string estado, string? aclaracion = null, string? motivo = null) => new(estado, aclaracion, motivo);
    private static ProfesionalPaso Prof => new(7, "Dra. Ana Ficticia", "Psicología clínica");
    private static TurnoPaso Turno(string estado, DateTime inicio, string creadoPor = "profesional", bool confirmadoPaciente = false, string? sala = null) =>
        new(1, inicio, estado, confirmadoPaciente, creadoPor, sala);

    private static ResultadoPrimerosPasos Calcular(EstadoTratamiento c, InformePaso? i = null, ProfesionalPaso? p = null, params TurnoPaso[] turnos) =>
        PrimerosPasos.Calcular(new DatosPrimerosPasos(c, i, p, turnos), Ahora);

    private static string Estado(ResultadoPrimerosPasos r, string clave) => r.Pasos.Single(x => x.Clave == clave).Estado;

    [Fact]
    public void Son_siete_pasos_en_el_orden_del_recorrido()
    {
        var r = Calcular(SinFirma);
        Assert.Equal(new[] { "registro", "consentimiento", "formulario", "revision", "profesional", "turno", "sesion" }, r.Pasos.Select(x => x.Clave));
    }

    [Fact]
    public void Paciente_recien_registrado_su_unico_boton_es_firmar_el_consentimiento()
    {
        var r = Calcular(SinFirma);
        Assert.Equal(PasoEstado.Hecho, Estado(r, "registro"));
        Assert.Equal(PasoEstado.Actual, Estado(r, "consentimiento"));
        Assert.Equal(PasoEstado.Actual, Estado(r, "formulario"));   // también falta, pero el botón principal es uno solo
        Assert.Equal("consentimiento", r.PasoActual);
        Assert.Equal(AccionTipo.FirmarConsentimiento, r.Principal!.Tipo);
        Assert.False(r.Cerrado);
    }

    [Fact]
    public void Firmado_y_sin_formulario_el_boton_es_completar_sobre_mi()
    {
        var r = Calcular(Firmado);
        Assert.Equal(PasoEstado.Hecho, Estado(r, "consentimiento"));
        Assert.Equal("formulario", r.PasoActual);
        Assert.Equal(AccionTipo.CompletarFormulario, r.Principal!.Tipo);
        Assert.Equal(PasoEstado.Pendiente, Estado(r, "revision"));
    }

    [Fact]
    public void Formulario_enviado_queda_en_revision_y_no_hay_boton()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Pendiente));
        Assert.Equal(PasoEstado.Hecho, Estado(r, "formulario"));
        Assert.Equal(PasoEstado.Espera, Estado(r, "revision"));
        Assert.Null(r.Principal);   // le toca al equipo
        Assert.Equal("revision", r.PasoActual);
    }

    [Theory]
    [InlineData("En entrevista")]
    [InlineData("Rechazado")]
    public void Los_estados_viejos_de_la_entrevista_se_leen_como_en_revision(string estadoViejo)
    {
        var r = Calcular(Firmado, Informe(estadoViejo));
        Assert.Equal(PasoEstado.Espera, Estado(r, "revision"));
        Assert.DoesNotContain("entrevista", string.Join(" ", r.Pasos.Select(x => x.Titulo + x.Detalle)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formulario_devuelto_muestra_la_aclaracion_y_el_boton_para_completarlo()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Devuelto, aclaracion: "Falta el motivo de consulta"));
        Assert.Equal("formulario", r.PasoActual);
        Assert.Contains("Falta el motivo de consulta", r.Pasos.Single(x => x.Clave == "formulario").Detalle);
        Assert.Equal(AccionTipo.CompletarFormulario, r.Principal!.Tipo);
        Assert.Equal(PasoEstado.Pendiente, Estado(r, "revision"));
    }

    [Fact]
    public void Formulario_aceptado_sin_profesional_espera_la_asignacion()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Aprobado));
        Assert.Equal(PasoEstado.Hecho, Estado(r, "revision"));
        Assert.Equal(PasoEstado.Espera, Estado(r, "profesional"));
        Assert.Null(r.Principal);
    }

    [Fact]
    public void Con_profesional_asignado_se_muestran_su_nombre_y_su_especialidad()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Derivado), Prof);
        var paso = r.Pasos.Single(x => x.Clave == "profesional");
        Assert.Equal(PasoEstado.Hecho, paso.Estado);
        Assert.Contains("Dra. Ana Ficticia", paso.Detalle);
        Assert.Contains("Psicología clínica", paso.Detalle);
        Assert.Equal("turno", r.PasoActual);
        Assert.Equal(AccionTipo.VerTurnos, r.Principal!.Tipo);
    }

    [Fact]
    public void Turno_propuesto_por_el_profesional_el_boton_es_confirmarlo()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Derivado), Prof, Turno("Pendiente", Ahora.AddDays(3)));
        Assert.Equal(AccionTipo.ConfirmarTurno, r.Principal!.Tipo);
        Assert.Equal(1, r.Principal.IdTurno);
    }

    [Fact]
    public void Turno_pedido_por_el_paciente_espera_la_confirmacion_del_profesional()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Derivado), Prof, Turno("Pendiente", Ahora.AddDays(3), creadoPor: "paciente"));
        Assert.Equal(PasoEstado.Espera, Estado(r, "turno"));
        Assert.Null(r.Principal);
    }

    [Fact]
    public void Turno_cancelado_no_cuenta()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Derivado), Prof, Turno("Cancelado", Ahora.AddDays(3)));
        Assert.Equal(PasoEstado.Actual, Estado(r, "turno"));
        Assert.Equal(AccionTipo.VerTurnos, r.Principal!.Tipo);
    }

    [Fact]
    public void Turno_confirmado_lejano_la_sesion_todavia_no_esta_habilitada()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Derivado), Prof, Turno("Confirmado", Ahora.AddHours(5), "paciente", true, "SALA1"));
        Assert.Equal(PasoEstado.Hecho, Estado(r, "turno"));
        Assert.Equal("sesion", r.PasoActual);
        Assert.Equal(AccionTipo.EntrarSesion, r.Principal!.Tipo);
        Assert.False(r.Sesion!.Habilitada);
        Assert.Equal(Ahora.AddHours(5).AddMinutes(-10), r.Sesion.HabilitaDesde);
    }

    [Theory]
    [InlineData(11, false)]   // faltan 11 minutos
    [InlineData(10, true)]    // justo 10 minutos antes: se habilita
    [InlineData(2, true)]
    [InlineData(-5, true)]    // ya empezó
    public void La_sesion_se_habilita_diez_minutos_antes_con_la_hora_del_servidor(int minutosHastaElTurno, bool habilitada)
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Derivado), Prof, Turno("Confirmado", Ahora.AddMinutes(minutosHastaElTurno), "paciente", true, "S"));
        Assert.Equal(habilitada, r.Sesion!.Habilitada);
    }

    [Fact]
    public void Con_la_sesion_realizada_estan_los_siete_pasos_hechos()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.Derivado), Prof, Turno("Realizado", Ahora.AddDays(-2), "paciente", true));
        Assert.Equal(7, r.Hechos);
        Assert.Null(r.PasoActual);
        Assert.Null(r.Principal);
        Assert.Null(r.Sesion);
    }

    [Fact]
    public void Otro_recurso_cierra_el_ingreso_con_el_motivo_y_sin_boton()
    {
        var r = Calcular(Firmado, Informe(EstadoInforme.OtroRecurso, motivo: "Necesita atención psiquiátrica de urgencia"));
        Assert.True(r.Cerrado);
        Assert.Equal("Necesita atención psiquiátrica de urgencia", r.MotivoCierre);
        Assert.Null(r.Principal);
        Assert.Equal(PasoEstado.Cerrado, Estado(r, "revision"));
        Assert.Contains("Necesita atención psiquiátrica de urgencia", r.Pasos.Single(x => x.Clave == "revision").Detalle);
    }

    [Fact]
    public void Quien_ya_tiene_profesional_sin_formulario_no_necesita_la_admision()
    {
        var r = Calcular(Firmado, null, Prof);
        Assert.Equal(PasoEstado.Hecho, Estado(r, "formulario"));
        Assert.Equal(PasoEstado.Hecho, Estado(r, "revision"));
        Assert.Equal("turno", r.PasoActual);
    }

    [Fact]
    public void Si_vence_o_se_revoca_el_consentimiento_vuelve_a_ser_el_unico_boton_aunque_ya_tenga_profesional()
    {
        var vencido = new EstadoTratamiento(false, ConsentimientoTratamiento.Vencido, false, new DateTime(2025, 9, 1), "v1", null);
        var r = Calcular(vencido, Informe(EstadoInforme.Derivado), Prof, Turno("Confirmado", Ahora.AddDays(2), "paciente", true));
        Assert.Equal("consentimiento", r.PasoActual);
        Assert.Equal(AccionTipo.FirmarConsentimiento, r.Principal!.Tipo);
        Assert.Contains("venció", r.Pasos.Single(x => x.Clave == "consentimiento").Detalle);
        // El resto de los pasos conserva su estado real.
        Assert.Equal(PasoEstado.Hecho, Estado(r, "profesional"));
        Assert.Equal(PasoEstado.Hecho, Estado(r, "turno"));
    }

    [Fact]
    public void Nunca_hay_mas_de_un_boton_principal()
    {
        var r = Calcular(SinFirma, Informe(EstadoInforme.Devuelto, "x"), Prof, Turno("Pendiente", Ahora.AddDays(1)));
        Assert.Equal(AccionTipo.FirmarConsentimiento, r.Principal!.Tipo);
        Assert.Equal(1, r.Pasos.Count(p => p.Accion is not null && p.Clave == r.PasoActual));
    }
}

public class ConsentimientoVigenciaAnualTests
{
    private static readonly DateTime Hoy = new(2026, 10, 26, 12, 0, 0);
    private static FirmaTratamiento Firma(DateTime f, string? version = "v1", int? tutor = null) => new(f, version, tutor);

    [Fact]
    public void Una_firma_de_hace_menos_de_doce_meses_vale()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma(Hoy.AddMonths(-11)) }, "v1", false, false, Hoy, 12);
        Assert.True(e.Vigente);
    }

    [Fact]
    public void Una_firma_de_hace_mas_de_doce_meses_vence()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma(Hoy.AddMonths(-12).AddDays(-1)) }, "v1", false, false, Hoy, 12);
        Assert.False(e.Vigente);
        Assert.Equal(ConsentimientoTratamiento.Vencido, e.Motivo);
    }

    [Fact]
    public void Justo_a_los_doce_meses_ya_no_vale()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma(Hoy.AddMonths(-12)) }, "v1", false, false, Hoy, 12);
        Assert.False(e.Vigente);
    }

    [Fact]
    public void Sin_vigencia_configurada_no_vence()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma(Hoy.AddYears(-5)) }, "v1", false, false, Hoy, 0);
        Assert.True(e.Vigente);
    }

    [Fact]
    public void Si_hay_una_firma_vieja_y_otra_reciente_vale_la_reciente()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma(Hoy.AddMonths(-1)), Firma(Hoy.AddMonths(-20)) }, "v1", false, false, Hoy, 12);
        Assert.True(e.Vigente);
    }

    [Fact]
    public void El_cambio_de_version_del_texto_sigue_exigiendo_firmar_de_nuevo()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma(Hoy.AddDays(-3), "v1") }, "v2", false, false, Hoy, 12);
        Assert.False(e.Vigente);
        Assert.Equal(ConsentimientoTratamiento.VersionAnterior, e.Motivo);
    }

    [Fact]
    public void La_vigencia_por_defecto_es_de_doce_meses_y_esta_documentada_como_configurable()
    {
        Assert.Equal(12, new ConsentimientoOptions().VigenciaMeses);
        Assert.Equal(12, ConsentimientoTratamiento.VigenciaMesesPorDefecto);
    }
}

public class PreferenciasAdmisionTests
{
    private static readonly IReadOnlySet<int> Activos = new HashSet<int> { 1, 2, 3 };

    [Fact]
    public void Sin_preferencias_es_valido()
        => Assert.Null(PreferenciasAdmision.Validar(null, null, Activos));

    [Fact]
    public void Horario_valido()
        => Assert.Null(PreferenciasAdmision.Validar(new() { Dias = new() { "lunes", "jueves" }, Franja = "tarde" }, null, Activos));

    [Theory]
    [InlineData("funday", "tarde")]
    [InlineData("lunes", "madrugada")]
    [InlineData("lunes", null)]
    public void Horario_invalido(string dia, string? franja)
        => Assert.NotNull(PreferenciasAdmision.Validar(new() { Dias = new() { dia }, Franja = franja }, null, Activos));

    [Fact]
    public void Horario_sin_dias_o_con_dias_repetidos_es_invalido()
    {
        Assert.NotNull(PreferenciasAdmision.Validar(new() { Dias = new(), Franja = "tarde" }, null, Activos));
        Assert.NotNull(PreferenciasAdmision.Validar(new() { Dias = new() { "lunes", "lunes" }, Franja = "tarde" }, null, Activos));
    }

    [Fact]
    public void Profesionales_validos_uno_varios_o_me_da_igual()
    {
        Assert.Null(PreferenciasAdmision.Validar(null, new() { Ids = new() { 2 } }, Activos));
        Assert.Null(PreferenciasAdmision.Validar(null, new() { Ids = new() { 1, 3 } }, Activos));
        Assert.Null(PreferenciasAdmision.Validar(null, new() { Cualquiera = true }, Activos));
    }

    [Fact]
    public void Profesionales_invalidos()
    {
        Assert.NotNull(PreferenciasAdmision.Validar(null, new() { Ids = new() { 99 } }, Activos));                       // no existe o no está activo
        Assert.NotNull(PreferenciasAdmision.Validar(null, new() { Ids = new() { 1, 1 } }, Activos));                     // repetido
        Assert.NotNull(PreferenciasAdmision.Validar(null, new() { Cualquiera = true, Ids = new() { 1 } }, Activos));     // me da igual + uno puntual
        Assert.NotNull(PreferenciasAdmision.Validar(null, new() { Cualquiera = false, Ids = new() }, Activos));          // ni uno ni "me da igual"
        var muchos = Enumerable.Range(1, PreferenciasAdmision.MaxProfesionales + 1).ToList();
        Assert.NotNull(PreferenciasAdmision.Validar(null, new() { Ids = muchos }, muchos.ToHashSet()));
    }

    [Fact]
    public void Se_guardan_estructuradas_para_cruzarlas_con_las_agendas()
    {
        var json = PreferenciasAdmision.Serializar(new HorarioPreferidoAdmision { Dias = new() { "martes" }, Franja = "manana" });
        var leido = PreferenciasAdmision.Leer(json)!.Value;
        Assert.Equal("manana", leido.GetProperty("franja").GetString());
        Assert.Equal("martes", leido.GetProperty("dias")[0].GetString());
        Assert.Equal((8, 12), PreferenciasAdmision.Horas("manana"));
        Assert.Null(PreferenciasAdmision.Leer("esto no es json"));
    }
}

public class DerivacionDesdeFormularioAceptadoTests
{
    [Fact]
    public void Un_formulario_aceptado_se_puede_asignar_sin_entrevista_ni_observaciones()
        => Assert.Null(DerivacionAdmision.Validar(EstadoInforme.Aprobado, null, null, DateTime.Now));

    [Theory]
    [InlineData(EstadoInforme.Pendiente)]
    [InlineData(EstadoInforme.Devuelto)]
    public void Sin_aceptar_no_se_asigna(string estado)
        => Assert.Equal("FORMULARIO_SIN_ACEPTAR", DerivacionAdmision.Validar(estado, null, null, DateTime.Now)!.Value.Codigo);

    [Fact]
    public void Otro_recurso_no_se_asigna()
        => Assert.Equal("ESTADO", DerivacionAdmision.Validar(EstadoInforme.OtroRecurso, null, null, DateTime.Now)!.Value.Codigo);
}
