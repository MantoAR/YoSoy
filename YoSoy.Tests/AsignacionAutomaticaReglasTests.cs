// La regla de elección de la propuesta automática de profesional y horario (Services/AsignacionAutomatica.cs), sin base de datos: cada preferencia, cada relajación y cada desempate
// con candidatos armados a mano. Y cómo la usan los primeros pasos del paciente (el paso «Te asignamos un profesional»).
public class AsignacionAutomaticaReglasTests
{
    // Una semana de referencia: lunes 2 de noviembre de 2026 es lunes.
    private static DateTime Lunes(int hora = 10, int minuto = 0) => new(2026, 11, 2, hora, minuto, 0);
    private static DateTime Martes(int hora = 10, int minuto = 0) => new(2026, 11, 3, hora, minuto, 0);
    private static DateTime Miercoles(int hora = 10) => new(2026, 11, 4, hora, 0, 0);

    private static CandidatoPropuesta Cand(int id, int activos, params DateTime[] horarios) => new(id, "Profesional " + id, "Psicología", activos, horarios);
    private static PreferenciasDelPaciente Prefs(string[]? dias = null, string? franja = null, int[]? ids = null) =>
        new(dias ?? Array.Empty<string>(), franja, ids is null || ids.Length == 0, ids ?? Array.Empty<int>());

    [Fact]
    public void Sin_preferencias_elige_el_horario_libre_mas_proximo_de_cualquier_profesional()
    {
        var e = AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 0, Martes(9), Martes(10)), Cand(2, 0, Lunes(15), Martes(9)) }, PreferenciasDelPaciente.Ninguna);
        Assert.Equal(2, e!.IdProfesional);
        Assert.Equal(Lunes(15), e.Momento);
    }

    [Fact]
    public void Si_dos_horarios_empatan_gana_el_profesional_con_menos_pacientes_y_despues_el_de_menor_id()
    {
        var porCarga = AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 5, Lunes()), Cand(2, 2, Lunes()), Cand(3, 7, Lunes()) }, PreferenciasDelPaciente.Ninguna);
        Assert.Equal(2, porCarga!.IdProfesional);

        var porId = AsignacionAutomaticaReglas.Elegir(new[] { Cand(9, 3, Lunes()), Cand(4, 3, Lunes()), Cand(6, 3, Lunes()) }, PreferenciasDelPaciente.Ninguna);
        Assert.Equal(4, porId!.IdProfesional);
    }

    [Fact]
    public void Con_profesionales_preferidos_prueba_primero_solo_con_ellos_aunque_otro_tenga_un_horario_mas_cercano()
    {
        var e = AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 0, Lunes(9)), Cand(2, 9, Miercoles()) }, Prefs(ids: new[] { 2 }));
        Assert.Equal(2, e!.IdProfesional);
        Assert.Equal(Miercoles(), e.Momento);
        Assert.DoesNotContain("no tenían disponibilidad", e.Motivo);
    }

    [Fact]
    public void Si_ningun_preferido_tiene_horario_amplia_a_todos_y_lo_anota()
    {
        var e = AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 0, Lunes(9)), Cand(2, 0) }, Prefs(ids: new[] { 2 }));
        Assert.Equal(1, e!.IdProfesional);
        Assert.Contains("Los profesionales elegidos no tenían disponibilidad.", e.Motivo);
    }

    [Fact]
    public void Los_dias_y_la_franja_filtran_los_horarios()
    {
        // Martes a la mañana existe (9) y a la tarde (15); el paciente pidió martes por la tarde.
        var e = AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 0, Lunes(15), Martes(9), Martes(15), Miercoles(15)) }, Prefs(new[] { "martes" }, "tarde"));
        Assert.Equal(Martes(15), e!.Momento);
        Assert.DoesNotContain("relaj", e.Motivo);
    }

    [Fact]
    public void Sin_horarios_en_esa_franja_relaja_primero_la_franja_y_lo_anota()
    {
        var e = AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 0, Lunes(15), Martes(9), Miercoles(15)) }, Prefs(new[] { "martes" }, "tarde"));
        Assert.Equal(Martes(9), e!.Momento);   // el día se respeta, la franja no
        Assert.Contains("Se relajó la franja horaria preferida.", e.Motivo);
    }

    [Fact]
    public void Si_tampoco_hay_ese_dia_relaja_los_dias_y_lo_anota()
    {
        var e = AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 0, Lunes(9), Miercoles(15)) }, Prefs(new[] { "martes" }, "tarde"));
        Assert.Equal(Lunes(9), e!.Momento);
        Assert.Contains("Se relajaron los días y la franja horaria preferidos.", e.Motivo);
    }

    [Fact]
    public void Solo_con_dias_y_sin_franja_se_relajan_los_dias()
    {
        var e = AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 0, Lunes(9)) }, Prefs(new[] { "viernes" }, "cualquiera"));
        Assert.Equal(Lunes(9), e!.Momento);
        Assert.Contains("Se relajaron los días preferidos.", e.Motivo);
    }

    [Fact]
    public void Me_da_igual_y_los_campos_vacios_no_filtran()
    {
        var sinNada = PreferenciasDelPaciente.Desde(null, null);
        Assert.Empty(sinNada.Dias);
        Assert.True(sinNada.CualquierProfesional);

        var meDaIgual = PreferenciasDelPaciente.Desde("""{"dias":[],"franja":"cualquiera"}""", """{"cualquiera":true,"ids":[]}""");
        Assert.True(meDaIgual.CualquierProfesional);
        var e = AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 0, Miercoles(20)) }, meDaIgual);
        Assert.Equal(Miercoles(20), e!.Momento);
        Assert.DoesNotContain("relaj", e.Motivo);
    }

    [Fact]
    public void Las_preferencias_guardadas_se_leen_y_un_texto_roto_cuenta_como_sin_preferencias()
    {
        var p = PreferenciasDelPaciente.Desde("""{"dias":["martes","jueves"],"franja":"manana"}""", """{"cualquiera":false,"ids":[3,5]}""");
        Assert.Equal(new[] { "martes", "jueves" }, p.Dias);
        Assert.Equal("manana", p.Franja);
        Assert.False(p.CualquierProfesional);
        Assert.Equal(new[] { 3, 5 }, p.IdsProfesionales);

        var roto = PreferenciasDelPaciente.Desde("{no es json", "[");
        Assert.Empty(roto.Dias);
        Assert.True(roto.CualquierProfesional);
    }

    [Fact]
    public void Sin_candidatos_con_horarios_no_hay_propuesta()
    {
        Assert.Null(AsignacionAutomaticaReglas.Elegir(Array.Empty<CandidatoPropuesta>(), PreferenciasDelPaciente.Ninguna));
        Assert.Null(AsignacionAutomaticaReglas.Elegir(new[] { Cand(1, 0), Cand(2, 0) }, Prefs(new[] { "lunes" }, "tarde", new[] { 1 })));
    }

    [Fact]
    public void El_horario_se_cuenta_para_el_paciente_con_dia_y_hora_claros()
    {
        Assert.Equal("el lunes 02/11 a las 10:30 hs", AsignacionAutomaticaReglas.Cuando(Lunes(10, 30)));
        Assert.Equal("el miércoles 04/11 a las 09:00 hs", AsignacionAutomaticaReglas.Cuando(Miercoles(9)));
    }

    // ───────────── Vencimiento de la propuesta (sin anticipación de 24 h: puede haber turnos para dentro de pocos minutos) ─────────────

    [Fact]
    public void El_vencimiento_de_un_turno_lejano_es_el_menor_entre_48_horas_y_dos_horas_antes_del_turno()
    {
        var creacion = new DateTime(2026, 11, 3, 23, 16, 0);
        var turno = new DateTime(2026, 11, 4, 8, 15, 0);   // turno el miércoles 08:15, propuesta ~9 h antes
        Assert.Equal(new DateTime(2026, 11, 4, 6, 15, 0), AsignacionAutomaticaReglas.Vencimiento(creacion, turno));
        Assert.Equal(creacion.AddHours(48), AsignacionAutomaticaReglas.Vencimiento(creacion, creacion.AddDays(6)));
        Assert.Equal(creacion.AddHours(48), AsignacionAutomaticaReglas.Vencimiento(creacion, creacion.AddHours(50)));   // justo en el empate
        Assert.Equal(2, AsignacionAutomaticaReglas.HorasAntesDelTurnoParaCerrar);
    }

    [Theory]
    [InlineData(16)] [InlineData(20)] [InlineData(29)] [InlineData(30)] [InlineData(31)] [InlineData(45)] [InlineData(60)] [InlineData(119)]
    [InlineData(134)] [InlineData(135)] [InlineData(136)] [InlineData(180)] [InlineData(600)] [InlineData(2880)] [InlineData(5000)]
    public void El_vencimiento_nunca_es_anterior_a_la_creacion_ni_posterior_al_inicio(int minutosHastaElTurno)
    {
        var creacion = new DateTime(2026, 11, 3, 10, 0, 0);
        var turno = creacion.AddMinutes(minutosHastaElTurno);
        var v = AsignacionAutomaticaReglas.Vencimiento(creacion, turno);
        Assert.True(v >= creacion.AddMinutes(AsignacionAutomaticaReglas.MinutosMinimosDeVigencia), $"dura menos de 15 min: {v}");
        Assert.True(v < turno, $"vence después de empezar el turno: {v} vs {turno}");
        Assert.True(v <= creacion.AddHours(AsignacionAutomaticaReglas.HorasParaResponder));
    }

    [Theory]
    [InlineData(130, 115)]   // turno a 2 h 10: «inicio − 15 min» (no se aplica el − 2 h, quedaría a 10 min de la creación, menos del mínimo)
    [InlineData(60, 45)]     // turno en 1 hora: vence 15 min antes del turno
    [InlineData(30, 15)]     // turno en 30 min: justo el mínimo de 15 min desde la creación
    [InlineData(20, 15)]     // turno en 20 min: «inicio − 15» sería a los 5 min; vale el mínimo de 15 min, que sigue siendo antes del inicio
    [InlineData(16, 15)]
    [InlineData(135, 15)]    // el empate: turno a 2 h 15 → «inicio − 2 h» cae justo a los 15 min de la creación
    public void Con_un_turno_cercano_vale_hasta_15_minutos_antes_pero_al_menos_15_minutos_desde_la_creacion(int minutosHastaElTurno, int minutosDeVigenciaEsperados)
    {
        var creacion = new DateTime(2026, 11, 3, 10, 0, 0);
        Assert.Equal(creacion.AddMinutes(minutosDeVigenciaEsperados), AsignacionAutomaticaReglas.Vencimiento(creacion, creacion.AddMinutes(minutosHastaElTurno)));
    }

    [Fact]
    public void El_primer_horario_permitido_es_estrictamente_posterior_a_ahora_y_respeta_el_colchon_y_la_clave()
    {
        var ahora = new DateTime(2026, 11, 3, 10, 0, 0);
        Assert.Equal(AsignacionAutomaticaReglas.AnticipacionMinimaMinutosPorDefecto, 0);
        Assert.Equal(ahora.AddMinutes(15), AsignacionAutomaticaReglas.PrimerHorarioPermitido(ahora));                       // por defecto: solo el colchón
        Assert.True(AsignacionAutomaticaReglas.PrimerHorarioPermitido(ahora) > ahora);
        Assert.Equal(ahora.AddMinutes(15), AsignacionAutomaticaReglas.PrimerHorarioPermitido(ahora, 5));                    // una anticipación menor al colchón no lo baja
        Assert.Equal(ahora.AddMinutes(90), AsignacionAutomaticaReglas.PrimerHorarioPermitido(ahora, 90));                   // configurable (AsignacionAutomatica:AnticipacionMinimaMinutos)
        Assert.Equal(ahora.AddMinutes(15), AsignacionAutomaticaReglas.PrimerHorarioPermitido(ahora, -30));                  // un valor negativo no adelanta nada
        Assert.Equal(ahora.AddHours(24), AsignacionAutomaticaReglas.PrimerHorarioPermitido(ahora, 0, 24 * 60));             // la de un pedido del paciente, si algún día se define
        Assert.Equal("AsignacionAutomatica:AnticipacionMinimaMinutos", AsignacionAutomaticaReglas.ClaveAnticipacionMinimaMinutos);
    }

    [Fact]
    public void No_se_puede_confirmar_con_el_plazo_vencido_ni_con_el_turno_ya_empezado()
    {
        var ahora = new DateTime(2026, 11, 3, 10, 0, 0);
        Assert.False(AsignacionAutomaticaReglas.NoSePuedeConfirmar(ahora.AddHours(5), ahora.AddHours(30), ahora));        // vigente
        Assert.True(AsignacionAutomaticaReglas.NoSePuedeConfirmar(ahora, ahora.AddHours(30), ahora));                    // el plazo vence ahora mismo
        Assert.True(AsignacionAutomaticaReglas.NoSePuedeConfirmar(ahora.AddHours(40), ahora, ahora));                    // plazo viejo, el turno ya empezó
        Assert.True(AsignacionAutomaticaReglas.NoSePuedeConfirmar(ahora.AddHours(40), ahora.AddMinutes(-1), ahora));
        Assert.False(AsignacionAutomaticaReglas.NoSePuedeConfirmar(ahora.AddMinutes(10), ahora.AddMinutes(25), ahora));  // turno cercano: el plazo corto ya trae el margen, no se pide otro
        Assert.False(AsignacionAutomaticaReglas.NoSePuedeConfirmar(ahora.AddHours(40), ahora.AddHours(1), ahora));       // un turno en 1 hora, con plazo vigente, se puede confirmar
    }

    [Fact]
    public void Las_cifras_sin_validar_estan_en_un_solo_lugar_y_son_las_pedidas()
    {
        Assert.Equal(0, AsignacionAutomaticaReglas.AnticipacionMinimaMinutosPorDefecto);
        Assert.Equal(15, AsignacionAutomaticaReglas.MinutosColchon);
        Assert.Equal(30, AsignacionAutomaticaReglas.DiasHorizonte);
        Assert.Equal(48, AsignacionAutomaticaReglas.HorasParaResponder);
        Assert.Equal(3, AsignacionAutomaticaReglas.MaxPropuestas);
        Assert.Equal(10, AsignacionAutomaticaReglas.MaxHorariosAlternativos);
    }

    // ───────────── Los primeros pasos del paciente ─────────────

    private static readonly DateTime Ahora = new(2026, 11, 1, 9, 0, 0);
    private static EstadoTratamiento Vigente => new(true, ConsentimientoTratamiento.Ok, false, Ahora.AddDays(-1), null, null);
    private static InformePaso Aprobado => new(EstadoInforme.Aprobado, null, null);

    private static PropuestaVista Propuesta(bool puedeCambiar = true) =>
        new(7, 3, "Marta Pérez", "Psicología", Lunes(10), Ahora.AddHours(48), puedeCambiar ? 1 : 3, puedeCambiar);

    [Fact]
    public void Con_una_propuesta_pendiente_el_paso_cinco_le_toca_al_paciente_y_dice_a_quien_y_cuando()
    {
        var r = PrimerosPasos.Calcular(new DatosPrimerosPasos(Vigente, Aprobado, null, Array.Empty<TurnoPaso>(), true, Propuesta()), Ahora);
        var paso = r.Pasos.Single(p => p.Clave == PasoClave.Profesional);
        Assert.Equal(PasoEstado.Actual, paso.Estado);
        Assert.Contains("Te propusimos a Marta Pérez (Psicología) el lunes 02/11 a las 10:00 hs", paso.Detalle);
        Assert.Equal(AccionTipo.ResponderPropuesta, paso.Accion!.Tipo);
        Assert.Equal(PasoClave.Profesional, r.PasoActual);
        Assert.Equal(AccionTipo.ResponderPropuesta, r.Principal!.Tipo);
    }

    [Fact]
    public void Con_la_asignacion_para_resolver_el_paciente_ve_el_mensaje_honesto_y_sin_boton()
    {
        var r = PrimerosPasos.Calcular(new DatosPrimerosPasos(Vigente, Aprobado, null, Array.Empty<TurnoPaso>(), true, null, AsignacionPorResolver: true), Ahora);
        var paso = r.Pasos.Single(p => p.Clave == PasoClave.Profesional);
        Assert.Equal(PasoEstado.Espera, paso.Estado);
        Assert.Equal("Estamos buscando la mejor opción para vos, te avisamos apenas esté lista.", paso.Detalle);
        Assert.Null(paso.Accion);
        Assert.Null(r.Principal);
    }

    [Fact]
    public void Sin_propuesta_ni_caso_para_resolver_el_paso_sigue_como_siempre()
    {
        var r = PrimerosPasos.Calcular(new DatosPrimerosPasos(Vigente, Aprobado, null, Array.Empty<TurnoPaso>()), Ahora);
        Assert.Equal("Estamos eligiendo el profesional adecuado para vos. Te avisamos apenas esté asignado.", r.Pasos.Single(p => p.Clave == PasoClave.Profesional).Detalle);
    }

    [Fact]
    public void Una_propuesta_no_cambia_nada_si_el_formulario_todavia_no_fue_aceptado_o_ya_hay_profesional()
    {
        var pendiente = PrimerosPasos.Calcular(new DatosPrimerosPasos(Vigente, new(EstadoInforme.Pendiente, null, null), null, Array.Empty<TurnoPaso>(), true, Propuesta()), Ahora);
        Assert.Equal(PasoEstado.Pendiente, pendiente.Pasos.Single(p => p.Clave == PasoClave.Profesional).Estado);

        var conProfesional = PrimerosPasos.Calcular(new DatosPrimerosPasos(Vigente, new(EstadoInforme.Derivado, null, null), new ProfesionalPaso(3, "Marta Pérez", "Psicología"),
            Array.Empty<TurnoPaso>(), true, Propuesta()), Ahora);
        Assert.Equal(PasoEstado.Hecho, conProfesional.Pasos.Single(p => p.Clave == PasoClave.Profesional).Estado);
    }
}
