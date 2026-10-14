// Reglas del consentimiento de la lectura de emociones, sin base: quién decide, estados, idempotencia, revocación, menores,
// ventana de la sala y el permiso que consulta la pantalla del profesional. El escenario (EscenarioEmociones) es el turno 1:
// paciente 301 / usuario 31, profesional 201; empieza 4/3/2031 10:00 UTC, dura 30 min: la sala abre 09:50 y cierra 11:00.
public class ConsentimientoVideoServiceTests
{
    // ───────────── Estado inicial y texto ─────────────

    [Fact]
    public void Sin_decision_el_paciente_ve_el_texto_con_su_version_y_nada_decidido()
    {
        var e = new EscenarioEmociones();

        var r = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno);

        Assert.True(r.Ok);
        var s = r.Estado!;
        Assert.True(s.Disponible);
        Assert.Equal(ConsentimientoVideoService.SinDecision, s.Estado);
        Assert.Equal("v0-provisorio", s.Version);
        Assert.True(s.Provisorio);
        Assert.False(string.IsNullOrWhiteSpace(s.Titulo));
        Assert.NotEmpty(s.Texto!);
        Assert.Null(s.DecididoUtc);
        Assert.Empty(e.Repo.Filas);   // mirar no registra nada
        Assert.Empty(e.Auditoria.Registros);
    }

    [Fact]
    public void Una_vez_decidido_ya_no_se_manda_el_texto_en_la_respuesta_de_la_decision()
    {
        var e = new EscenarioEmociones();

        var r = e.Acepta();

        Assert.Null(r.Estado!.Texto);
        Assert.Null(r.Estado.Titulo);
    }

    // ───────────── Aceptar y rechazar ─────────────

    [Fact]
    public void Aceptar_registra_la_decision_con_la_version_el_hash_y_la_hora_utc_y_la_audita()
    {
        var e = new EscenarioEmociones();

        var r = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "acepta", e.Texto.Version);

        Assert.True(r.Ok);
        Assert.Equal(ConsentimientoVideoService.Acepta, r.Estado!.Estado);
        var fila = Assert.Single(e.Repo.Filas);
        Assert.Equal(EscenarioEmociones.UsuarioPaciente, fila.IdUsuario);
        Assert.Equal(EscenarioEmociones.IdPaciente, fila.IdPaciente);
        Assert.Equal("turno", fila.Origen);
        Assert.Equal(EscenarioEmociones.IdTurno, fila.IdOrigen);
        Assert.Equal(e.Texto.Version, fila.VersionTexto);
        Assert.Equal(e.Texto.Hash, fila.HashTexto);
        Assert.Equal("acepta", fila.Decision);
        Assert.Equal(e.Ahora, fila.DecididoUtc);
        Assert.Null(fila.RevocadoUtc);
        var a = Assert.Single(e.Auditoria.Registros);
        Assert.Equal("consentimiento:acepta", a.Tipo);
        Assert.Equal(EscenarioEmociones.UsuarioPaciente, a.IdUsuario);
    }

    [Fact]
    public void Rechazar_se_registra_y_la_sesion_no_queda_con_permiso()
    {
        var e = new EscenarioEmociones();

        var r = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "rechaza", e.Texto.Version);

        Assert.True(r.Ok);
        Assert.Equal(ConsentimientoVideoService.Rechaza, r.Estado!.Estado);
        var permiso = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;
        Assert.False(permiso.Permitido);
        Assert.Equal(ConsentimientoVideoService.Rechaza, permiso.Estado);
        Assert.Equal("consentimiento:rechaza", Assert.Single(e.Auditoria.Registros).Tipo);
    }

    [Fact]
    public void Repetir_la_misma_decision_no_agrega_una_fila_ni_otra_auditoria()
    {
        var e = new EscenarioEmociones();
        e.Acepta();

        e.Acepta();
        e.Acepta();

        Assert.Single(e.Repo.Filas);
        Assert.Single(e.Auditoria.Registros);
    }

    [Fact]
    public void Cambiar_de_idea_de_rechazo_a_aceptacion_agrega_una_fila_y_la_ultima_manda()
    {
        var e = new EscenarioEmociones();
        e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "rechaza", e.Texto.Version);
        e.Reloj.Avanzar(TimeSpan.FromMinutes(2));

        var r = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "acepta", e.Texto.Version);

        Assert.True(r.Ok);
        Assert.Equal(2, e.Repo.Filas.Count);
        var permiso = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;
        Assert.True(permiso.Permitido);
        Assert.Equal(e.Repo.Filas[1].DecididoUtc, permiso.DesdeUtc);
    }

    [Fact]
    public void Despues_de_aceptar_rechazar_no_vale_hay_que_revocar()
    {
        var e = new EscenarioEmociones();
        e.Acepta();

        var r = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "rechaza", e.Texto.Version);

        Assert.Equal(TipoResultado.Conflicto, r.Tipo);
        Assert.Equal("usar-revocar", r.Codigo);
        Assert.Single(e.Repo.Filas);
        Assert.True(e.Repo.Filas[0].EsAceptacionVigente);
    }

    [Fact]
    public void La_decision_tiene_que_ser_sobre_la_version_del_texto_que_se_mostro()
    {
        var e = new EscenarioEmociones();

        var vieja = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "acepta", "v-anterior");
        var sinVersion = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "acepta", null);

        Assert.Equal("version-desactualizada", vieja.Codigo);
        Assert.Equal("version-desactualizada", sinVersion.Codigo);
        Assert.Equal(TipoResultado.Conflicto, vieja.Tipo);
        Assert.Empty(e.Repo.Filas);
    }

    [Fact]
    public void Si_el_texto_cambio_despues_de_una_decision_el_estado_trae_la_version_vigente_y_se_puede_volver_a_decidir()
    {
        var e = new EscenarioEmociones();
        // Una decisión de esta sesión tomada con una versión anterior del texto.
        e.Repo.Insertar(EscenarioEmociones.UsuarioPaciente, EscenarioEmociones.IdPaciente, "turno", EscenarioEmociones.IdTurno,
            "v-anterior", new string('a', 64), "rechaza", EscenarioEmociones.Inicio);

        var estado = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno).Estado!;
        var decidir = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "acepta", estado.Version);

        Assert.Equal(e.Texto.Version, estado.Version);   // la vigente, no la de la decisión anterior
        Assert.True(decidir.Ok, decidir.Codigo);         // y con ella la persona puede volver a decidir
        Assert.Equal(e.Texto.Version, e.Repo.Filas[^1].VersionTexto);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tal-vez")]
    [InlineData("revoca")]
    public void Una_decision_que_no_es_acepta_ni_rechaza_es_un_pedido_invalido(string? decision)
    {
        var e = new EscenarioEmociones();

        var r = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, decision, e.Texto.Version);

        Assert.Equal(TipoResultado.SolicitudInvalida, r.Tipo);
        Assert.Empty(e.Repo.Filas);
    }

    [Theory]
    [InlineData("ACEPTA")]
    [InlineData(" acepta ")]
    public void La_decision_se_lee_sin_importar_mayusculas_ni_espacios(string decision)
    {
        var e = new EscenarioEmociones();

        var r = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, decision, e.Texto.Version);

        Assert.True(r.Ok);
        Assert.Equal("acepta", Assert.Single(e.Repo.Filas).Decision);
    }

    // ───────────── Revocar ─────────────

    [Fact]
    public void Revocar_corta_el_permiso_anula_lo_guardado_y_audita_la_cantidad()
    {
        var e = new EscenarioEmociones();
        e.Acepta();
        e.Repo.SesionesConEmociones = 2;
        e.Reloj.Avanzar(TimeSpan.FromMinutes(5));

        var r = e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);

        Assert.True(r.Ok);
        Assert.Equal(ConsentimientoVideoService.Revocado, r.Estado!.Estado);
        var fila = Assert.Single(e.Repo.Filas);
        Assert.Equal(e.Ahora, fila.RevocadoUtc);
        // Anuló las emociones de ESA sesión, con el motivo "revocacion".
        var anulacion = Assert.Single(e.Repo.Anulaciones);
        Assert.Equal(("turno", EscenarioEmociones.IdTurno, EscenarioEmociones.IdPaciente, "revocacion"),
            (anulacion.Origen, anulacion.IdOrigen, anulacion.IdPaciente, anulacion.Motivo));
        // Y lo dejó asentado, con la cantidad.
        var auditoria = e.Auditoria.Registros.Last();
        Assert.Equal("consentimiento:revoca", auditoria.Tipo);
        Assert.Equal(2, auditoria.Cantidad);
        // El profesional ya no tiene permiso.
        var permiso = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;
        Assert.False(permiso.Permitido);
        Assert.Equal(ConsentimientoVideoService.Revocado, permiso.Estado);
        Assert.Equal(e.Ahora, permiso.RevocadoUtc);
    }

    [Fact]
    public void Revocar_dos_veces_no_duplica_la_auditoria_pero_reintenta_la_anulacion_por_si_se_corto()
    {
        var e = new EscenarioEmociones();
        e.Acepta();
        e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);
        var auditoriaAntes = e.Auditoria.Registros.Count;
        e.Repo.SesionesConEmociones = 0;

        var r = e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);

        Assert.True(r.Ok);
        Assert.Equal(ConsentimientoVideoService.Revocado, r.Estado!.Estado);
        Assert.Equal(auditoriaAntes, e.Auditoria.Registros.Count);   // nada nuevo que anular, nada nuevo que asentar
        Assert.Equal(2, e.Repo.Anulaciones.Count);                    // pero se volvió a intentar

        // Si en el reintento había algo (la primera vez se cortó a la mitad), se anula y se asienta.
        e.Repo.SesionesConEmociones = 1;
        e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);
        Assert.Equal(1, e.Auditoria.Registros.Last().Cantidad);
    }

    [Fact]
    public void Revocar_sin_haber_decidido_o_despues_de_rechazar_no_hace_nada()
    {
        var e = new EscenarioEmociones();

        var sinDecision = e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);
        e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "rechaza", e.Texto.Version);
        var conRechazo = e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);

        Assert.True(sinDecision.Ok);
        Assert.Equal(ConsentimientoVideoService.SinDecision, sinDecision.Estado!.Estado);
        Assert.True(conRechazo.Ok);
        Assert.Equal(ConsentimientoVideoService.Rechaza, conRechazo.Estado!.Estado);
        Assert.Empty(e.Repo.Anulaciones);
        Assert.Single(e.Auditoria.Registros);   // solo el rechazo
    }

    [Fact]
    public void Se_puede_volver_a_aceptar_despues_de_revocar_y_la_nueva_decision_tiene_su_propio_inicio()
    {
        var e = new EscenarioEmociones();
        e.Acepta();
        e.Reloj.Avanzar(TimeSpan.FromMinutes(3));
        e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);
        e.Reloj.Avanzar(TimeSpan.FromMinutes(2));

        e.Acepta();

        Assert.Equal(2, e.Repo.Filas.Count);
        var permiso = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;
        Assert.True(permiso.Permitido);
        Assert.Null(permiso.RevocadoUtc);
        Assert.Equal(e.Ahora, permiso.DesdeUtc);   // la ventana arranca en la aceptación nueva, no en la primera
    }

    [Fact]
    public void Se_puede_revocar_aunque_la_sala_ya_haya_cerrado_pero_no_decidir()
    {
        var e = new EscenarioEmociones();
        e.Acepta();
        e.Reloj.Avanzar(TimeSpan.FromHours(2));   // 12:00: la sala cerró a las 11:00

        var revocar = e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);
        var decidir = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "acepta", e.Texto.Version);
        var leer = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno);

        Assert.True(revocar.Ok);
        Assert.Equal(ConsentimientoVideoService.Revocado, revocar.Estado!.Estado);
        Assert.Equal(TipoResultado.Conflicto, decidir.Tipo);
        Assert.Equal("ya-termino", decidir.Codigo);
        Assert.NotNull(decidir.CerroUtc);
        Assert.Equal("ya-termino", leer.Codigo);
    }

    // ───────────── Quién es parte (404 uniforme) ─────────────

    [Fact]
    public void Solo_el_paciente_de_la_sesion_decide_y_el_resto_recibe_el_mismo_404()
    {
        var e = new EscenarioEmociones();
        var inexistente = e.Consentimiento.Decidir(e.Paciente, "turno", 999, "acepta", e.Texto.Version);

        var ajenos = new[]
        {
            e.Consentimiento.Decidir(e.OtroPaciente, "turno", EscenarioEmociones.IdTurno, "acepta", e.Texto.Version),
            e.Consentimiento.Decidir(e.Profesional, "turno", EscenarioEmociones.IdTurno, "acepta", e.Texto.Version),
            e.Consentimiento.Decidir(e.Configurador, "turno", EscenarioEmociones.IdTurno, "acepta", e.Texto.Version),
            e.Consentimiento.Decidir(e.OtroPaciente, "admision", EscenarioEmociones.IdInforme, "acepta", e.Texto.Version),
            e.Consentimiento.Obtener(e.OtroPaciente, "turno", EscenarioEmociones.IdTurno),
            e.Consentimiento.Revocar(e.OtroPaciente, "turno", EscenarioEmociones.IdTurno)
        };

        Assert.Equal(TipoResultado.NoEncontrada, inexistente.Tipo);
        foreach (var r in ajenos)
        {
            Assert.Equal(inexistente.Tipo, r.Tipo);
            Assert.Equal(inexistente.Codigo, r.Codigo);
            Assert.Equal(inexistente.Mensaje, r.Mensaje);
            Assert.Null(r.Estado);
        }
        Assert.Empty(e.Repo.Filas);
        Assert.Empty(e.Auditoria.Registros);
    }

    [Fact]
    public void Una_decision_sobre_un_turno_no_vale_para_otra_sesion_del_mismo_paciente()
    {
        var e = new EscenarioEmociones();
        e.Acepta("turno", EscenarioEmociones.IdTurno);

        var admision = e.Consentimiento.Obtener(e.Paciente, "admision", EscenarioEmociones.IdInforme).Estado!;
        var permisoAdmision = e.Consentimiento.Permiso(e.Configurador, "admision", EscenarioEmociones.IdInforme).Permiso!;

        Assert.Equal(ConsentimientoVideoService.SinDecision, admision.Estado);
        Assert.False(permisoAdmision.Permitido);
    }

    [Fact]
    public void El_paciente_puede_decidir_sobre_su_entrevista_de_admision()
    {
        var e = new EscenarioEmociones();

        e.Acepta("admision", EscenarioEmociones.IdInforme);

        var fila = Assert.Single(e.Repo.Filas);
        Assert.Equal("admision", fila.Origen);
        Assert.Equal(EscenarioEmociones.IdInforme, fila.IdOrigen);
        var auditoria = Assert.Single(e.Auditoria.Registros);
        Assert.Equal(("admision", EscenarioEmociones.IdInforme), (auditoria.Origen, auditoria.IdOrigen));
        // Quien hace la entrevista es el Configurador, en el asiento del profesional.
        Assert.True(e.Consentimiento.Permiso(e.Configurador, "admision", EscenarioEmociones.IdInforme).Permiso!.Permitido);
    }

    [Fact]
    public void La_sesion_tiene_que_estar_en_condiciones_de_tener_sala()
    {
        var e = new EscenarioEmociones();
        e.Origen.Turnos[EscenarioEmociones.IdTurno] = e.Origen.Turnos[EscenarioEmociones.IdTurno] with { Estado = "Cancelado" };

        var r = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno);

        Assert.Equal(TipoResultado.Conflicto, r.Tipo);
        Assert.Equal("estado-invalido", r.Codigo);
    }

    [Fact]
    public void Antes_de_que_abra_la_sala_se_puede_leer_y_decidir()
    {
        var e = new EscenarioEmociones();
        e.Reloj.Ahora = EscenarioEmociones.Inicio.AddMinutes(-40);   // 09:20: la sala abre 09:50

        var leer = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno);
        var decidir = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "acepta", e.Texto.Version);

        Assert.True(leer.Ok);
        Assert.True(decidir.Ok);
    }

    // ───────────── Menores y datos faltantes ─────────────

    // "Hoy" en el escenario es el martes 4/3/2031.
    [Theory]
    [InlineData("2013-03-05", true)]    // cumple 18 mañana: tiene 17
    [InlineData("2013-03-04", false)]   // cumple 18 exactamente hoy: es mayor
    [InlineData("2010-01-01", false)]
    [InlineData("2031-01-01", true)]    // un bebé
    [InlineData("2032-01-01", true)]    // fecha futura (dato mal cargado): se trata como menor
    public void Un_menor_de_18_no_tiene_disponible_el_analisis(string nacimiento, bool esMenor)
    {
        var e = new EscenarioEmociones();
        e.Repo.Pacientes[EscenarioEmociones.IdPaciente] = new DatosPacienteParaConsentimiento(DateTime.Parse(nacimiento), false);

        var s = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno).Estado!;

        Assert.Equal(!esMenor, s.Disponible);
        if (esMenor)
        {
            Assert.Equal(ConsentimientoVideoService.NoDisponible, s.Estado);
            Assert.Equal("menor-de-edad", s.Motivo);
            Assert.Null(s.Texto);   // ni se le muestra el texto
        }
    }

    [Fact]
    public void A_un_menor_no_se_le_puede_registrar_ni_aceptacion_ni_rechazo_y_el_profesional_no_tiene_permiso()
    {
        var e = new EscenarioEmociones();
        e.Repo.Pacientes[EscenarioEmociones.IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(2015, 6, 1), false);

        var acepta = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "acepta", e.Texto.Version);
        var rechaza = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "rechaza", e.Texto.Version);
        var permiso = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;

        Assert.Equal("menor-de-edad", acepta.Codigo);
        Assert.Equal(TipoResultado.Conflicto, acepta.Tipo);
        Assert.Equal("menor-de-edad", rechaza.Codigo);
        Assert.Empty(e.Repo.Filas);
        Assert.False(permiso.Permitido);
        Assert.Equal(ConsentimientoVideoService.NoDisponible, permiso.Estado);
        Assert.Equal("menor-de-edad", permiso.Motivo);
    }

    [Fact]
    public void Un_paciente_con_tutor_se_trata_como_menor_aunque_tenga_mas_de_18()
    {
        var e = new EscenarioEmociones();
        e.Repo.Pacientes[EscenarioEmociones.IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(1980, 1, 1), true);

        var s = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno).Estado!;

        Assert.False(s.Disponible);
        Assert.Equal("menor-de-edad", s.Motivo);
    }

    [Fact]
    public void Sin_fecha_de_nacimiento_no_se_ofrece_salvo_que_se_permita_a_proposito()
    {
        var e = new EscenarioEmociones();
        e.Repo.Pacientes[EscenarioEmociones.IdPaciente] = new DatosPacienteParaConsentimiento(null, false);

        var porDefecto = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno).Estado!;
        e.Opciones.CurrentValue.Emociones.PermitirEdadDesconocida = true;
        var permitido = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno).Estado!;

        Assert.False(porDefecto.Disponible);
        Assert.Equal("edad-desconocida", porDefecto.Motivo);
        Assert.True(permitido.Disponible);
    }

    [Fact]
    public void Un_paciente_que_se_volvio_menor_de_los_datos_deja_de_tener_permiso_aunque_haya_aceptado()
    {
        var e = new EscenarioEmociones();
        e.Acepta();
        // Se cargó un tutor después de aceptar: la regla se evalúa en cada consulta, no solo al aceptar.
        e.Repo.Pacientes[EscenarioEmociones.IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(1990, 5, 5), true);

        var permiso = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;

        Assert.False(permiso.Permitido);
        Assert.Equal("menor-de-edad", permiso.Motivo);
    }

    [Fact]
    public void Sin_clave_de_cifrado_no_hay_analisis_pero_la_llamada_no_depende_de_eso()
    {
        var e = new EscenarioEmociones(conClave: false);

        var paciente = e.Consentimiento.Obtener(e.Paciente, "turno", EscenarioEmociones.IdTurno).Estado!;
        var decidir = e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "acepta", e.Texto.Version);
        var permiso = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;

        Assert.False(paciente.Disponible);
        Assert.Equal("no-configurado", paciente.Motivo);
        Assert.Equal("no-configurado", decidir.Codigo);
        Assert.False(permiso.Permitido);
        Assert.Equal("no-configurado", permiso.Motivo);
        Assert.Empty(e.Repo.Filas);
    }

    // ───────────── El permiso del profesional ─────────────

    [Fact]
    public void El_permiso_dice_cada_cuanto_preguntar_y_cuanto_vale_la_respuesta()
    {
        var e = new EscenarioEmociones();
        e.Acepta();

        var p = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;

        Assert.True(p.Permitido);
        Assert.Equal(ConsentimientoVideoService.Acepta, p.Estado);
        Assert.Null(p.Motivo);
        Assert.Equal(3, p.ConsultarCadaSegundos);
        Assert.Equal(10, p.VigenciaSegundos);
        Assert.Equal(e.Ahora, p.AhoraUtc);       // el reloj del servidor, para calibrar el del navegador
        Assert.Equal(e.Ahora, p.DesdeUtc);
        Assert.Equal(e.Texto.Version, p.Version);
    }

    [Fact]
    public void Sin_decision_no_hay_permiso_y_no_es_un_error()
    {
        var e = new EscenarioEmociones();

        var r = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno);

        Assert.True(r.Ok);
        Assert.False(r.Permiso!.Permitido);
        Assert.Equal(ConsentimientoVideoService.SinDecision, r.Permiso.Estado);
    }

    [Fact]
    public void El_permiso_lo_consultan_el_profesional_de_la_sesion_y_el_configurador_y_nadie_mas()
    {
        var e = new EscenarioEmociones();
        e.Acepta();
        e.Acepta("admision", EscenarioEmociones.IdInforme);

        Assert.True(e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Ok);
        Assert.True(e.Consentimiento.Permiso(e.Configurador, "turno", EscenarioEmociones.IdTurno).Ok);
        Assert.True(e.Consentimiento.Permiso(e.Configurador, "admision", EscenarioEmociones.IdInforme).Ok);

        var inexistente = e.Consentimiento.Permiso(e.Profesional, "turno", 999);
        foreach (var r in new[]
        {
            e.Consentimiento.Permiso(e.OtroProfesional, "turno", EscenarioEmociones.IdTurno),
            e.Consentimiento.Permiso(e.Paciente, "turno", EscenarioEmociones.IdTurno),
            e.Consentimiento.Permiso(e.OtroPaciente, "turno", EscenarioEmociones.IdTurno),
            e.Consentimiento.Permiso(e.Profesional, "admision", EscenarioEmociones.IdInforme)   // el profesional no atiende admisiones
        })
        {
            Assert.Equal(TipoResultado.NoEncontrada, r.Tipo);
            Assert.Equal(inexistente.Tipo, r.Tipo);
            Assert.Equal(inexistente.Codigo, r.Codigo);
            Assert.Equal(inexistente.Mensaje, r.Mensaje);
            Assert.Null(r.Permiso);
        }
    }

    // Un valor fuera de rango se acota en vez de romper nada (mismo criterio que el resto de Video:*).
    [Theory]
    [InlineData(0, 0, 1, 2)]
    [InlineData(-5, -5, 1, 2)]
    [InlineData(500, 99999, 60, 300)]
    [InlineData(3, 10, 3, 10)]
    public void Los_tiempos_que_dice_el_permiso_se_acotan_a_un_rango_razonable(int cada, int vigencia, int esperadoCada, int esperadoVigencia)
    {
        var e = new EscenarioEmociones();
        e.Opciones.CurrentValue.Emociones.ConsultaCadaSegundos = cada;
        e.Opciones.CurrentValue.Emociones.VigenciaSegundos = vigencia;

        var p = e.Consentimiento.Permiso(e.Profesional, "turno", EscenarioEmociones.IdTurno).Permiso!;

        Assert.Equal(esperadoCada, p.ConsultarCadaSegundos);
        Assert.Equal(esperadoVigencia, p.VigenciaSegundos);
    }
}
