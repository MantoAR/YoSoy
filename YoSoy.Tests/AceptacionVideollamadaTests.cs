// La aceptación de la videollamada (migración 28) y su relación con la lectura de emociones, sin base: reglas del servicio con datos en memoria. El turno de referencia es el 1
// (ver EscenarioEmociones). Las pruebas HTTP de punta a punta están en AceptacionVideollamadaEndpointsTests; las de la base real, en AceptacionVideollamadaBaseRealTests.
public class AceptacionVideollamadaTests
{
    private static EscenarioEmociones Exigiendo()
    {
        var e = new EscenarioEmociones();
        e.Video.Exigida = true;
        return e;
    }

    private static string Version => TextoAceptacionVideollamada.Version;

    [Fact]
    public void Sin_la_migracion_no_se_exige_nada()
    {
        var e = new EscenarioEmociones();   // Exigida = false
        Assert.True(e.Aceptacion.Aceptada("turno", 1, EscenarioEmociones.IdPaciente));
        var r = e.Aceptacion.Obtener(e.Paciente, "turno", 1);
        Assert.True(r.Ok);
        Assert.False(r.Estado!.Exigida);
        Assert.Equal("acepta", r.Estado.Estado);
        // Y no escribe nada si se llama igual.
        Assert.True(e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version).Ok);
        Assert.Empty(e.Video.Filas);
    }

    [Fact]
    public void Exigida_la_sesion_empieza_pendiente_con_el_texto_la_version_y_la_marca_provisoria()
    {
        var e = Exigiendo();
        Assert.False(e.Aceptacion.Aceptada("turno", 1, EscenarioEmociones.IdPaciente));

        var r = e.Aceptacion.Obtener(e.Paciente, "turno", 1);
        Assert.True(r.Ok);
        Assert.Equal("pendiente", r.Estado!.Estado);
        Assert.True(r.Estado.Exigida);
        Assert.Equal(Version, r.Estado.Version);
        Assert.Equal("Antes de empezar la sesión", r.Estado.Titulo);
        Assert.NotEmpty(r.Estado.Texto);
        Assert.Equal("Texto provisorio, pendiente de validación legal", r.Estado.MarcaProvisoria);
        Assert.Empty(e.Video.Filas);   // pedir el texto no acepta nada
    }

    [Fact]
    public void La_marca_provisoria_se_puede_quitar_por_configuracion()
    {
        var e = Exigiendo();
        e.OpcionesConsentimiento.CurrentValue.MarcaProvisoria = "";
        Assert.Null(e.Aceptacion.Obtener(e.Paciente, "turno", 1).Estado!.MarcaProvisoria);
    }

    [Fact]
    public void Aceptar_guarda_quien_cuando_y_la_version_y_audita()
    {
        var e = Exigiendo();
        var r = e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);

        Assert.True(r.Ok);
        Assert.Equal("acepta", r.Estado!.Estado);
        var fila = Assert.Single(e.Video.Filas);
        Assert.Equal((EscenarioEmociones.UsuarioPaciente, EscenarioEmociones.IdPaciente, "turno", 1, Version), (fila.IdUsuario, fila.IdPaciente, fila.Origen, fila.IdOrigen, fila.VersionTexto));
        Assert.Equal(e.Ahora, fila.DecididoUtc);
        Assert.Null(fila.RevocadoUtc);
        Assert.True(e.Aceptacion.Aceptada("turno", 1, EscenarioEmociones.IdPaciente));
        var auditoria = Assert.Single(e.AuditoriaVideo.Registros);
        Assert.Equal(("acepta", Version), (auditoria.Decision, auditoria.Version));
    }

    [Fact]
    public void Aceptar_dos_veces_no_agrega_otra_fila()
    {
        var e = Exigiendo();
        e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);
        Assert.True(e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version).Ok);
        Assert.Single(e.Video.Filas);
        Assert.Single(e.AuditoriaVideo.Registros);
    }

    [Fact]
    public void Aceptar_sobre_un_texto_que_no_es_el_vigente_se_rechaza()
    {
        var e = Exigiendo();
        var r = e.Aceptacion.Aceptar(e.Paciente, "turno", 1, "otra-version");
        Assert.False(r.Ok);
        Assert.Equal("version-desactualizada", r.Codigo);
        Assert.Empty(e.Video.Filas);
    }

    [Fact]
    public void Solo_decide_el_paciente_de_la_sesion()
    {
        var e = Exigiendo();
        foreach (var quien in new[] { e.OtroPaciente, e.Profesional, e.Configurador })
        {
            var r = e.Aceptacion.Aceptar(quien, "turno", 1, Version);
            Assert.False(r.Ok);
            Assert.Equal(TipoResultado.NoEncontrada, r.Tipo);   // mismo 404 que «no existe»
        }
        Assert.Empty(e.Video.Filas);
        Assert.Equal(TipoResultado.NoEncontrada, e.Aceptacion.Revocar(e.OtroPaciente, "turno", 1).Tipo);
        Assert.Equal(TipoResultado.NoEncontrada, e.Aceptacion.Aceptar(e.Paciente, "turno", 999, Version).Tipo);
    }

    [Fact]
    public void Revocar_marca_la_fila_avisa_al_profesional_una_sola_vez_y_audita()
    {
        var e = Exigiendo();
        e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);
        e.Reloj.Avanzar(TimeSpan.FromMinutes(5));

        var r = e.Aceptacion.Revocar(e.Paciente, "turno", 1);
        Assert.True(r.Ok);
        Assert.Equal("revocada", r.Estado!.Estado);
        Assert.Equal(e.Ahora, e.Video.Filas[0].RevocadoUtc);
        Assert.False(e.Aceptacion.Aceptada("turno", 1, EscenarioEmociones.IdPaciente));

        var aviso = Assert.Single(e.Avisos.Enviadas);
        Assert.Equal(("Profesional", EscenarioEmociones.IdProfesional, 1, AceptacionVideollamadaService.TipoAvisoRevocacion),
            (aviso.Destinatario, aviso.IdProfesional, aviso.IdTurno, aviso.Tipo));
        Assert.Contains("revocó la aceptación de la videollamada", aviso.Mensaje);
        Assert.Equal(new[] { "acepta", "revoca" }, e.AuditoriaVideo.Registros.Select(x => x.Decision).ToArray());

        // Repetirla no hace daño ni repite el aviso.
        Assert.True(e.Aceptacion.Revocar(e.Paciente, "turno", 1).Ok);
        Assert.Single(e.Avisos.Enviadas);
        Assert.Equal(2, e.AuditoriaVideo.Registros.Count);
    }

    [Fact]
    public void Revocar_sin_haber_aceptado_no_avisa_a_nadie()
    {
        var e = Exigiendo();
        Assert.True(e.Aceptacion.Revocar(e.Paciente, "turno", 1).Ok);
        Assert.Empty(e.Avisos.Enviadas);
        Assert.Empty(e.Video.Filas);
    }

    [Fact]
    public void Despues_de_revocar_se_puede_volver_a_aceptar()
    {
        var e = Exigiendo();
        e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);
        e.Aceptacion.Revocar(e.Paciente, "turno", 1);
        Assert.True(e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version).Ok);
        Assert.Equal(2, e.Video.Filas.Count);
        Assert.True(e.Aceptacion.Aceptada("turno", 1, EscenarioEmociones.IdPaciente));
    }

    [Fact]
    public void En_una_admision_el_aviso_va_a_Admision_y_no_a_un_profesional_puntual()
    {
        var e = Exigiendo();
        e.Aceptacion.Aceptar(e.Paciente, "admision", 5, Version);
        e.Aceptacion.Revocar(e.Paciente, "admision", 5);
        var aviso = Assert.Single(e.Avisos.Enviadas);
        Assert.Equal("Administrador", aviso.Destinatario);
        Assert.Null(aviso.IdProfesional);
    }

    // ───────────── Lectura de emociones encima del video ─────────────

    [Fact]
    public void Las_emociones_no_se_pueden_aceptar_sin_haber_aceptado_el_video()
    {
        var e = Exigiendo();
        var r = e.Consentimiento.Decidir(e.Paciente, "turno", 1, "acepta", e.Texto.Version);
        Assert.False(r.Ok);
        Assert.Equal("aceptacion-video-pendiente", r.Codigo);
        Assert.Empty(e.Repo.Filas);

        e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);
        Assert.True(e.Consentimiento.Decidir(e.Paciente, "turno", 1, "acepta", e.Texto.Version).Ok);
    }

    [Fact]
    public void Rechazar_las_emociones_no_exige_el_video_aceptado()
    {
        var e = Exigiendo();
        Assert.True(e.Consentimiento.Decidir(e.Paciente, "turno", 1, "rechaza", e.Texto.Version).Ok);
    }

    [Fact]
    public void El_permiso_del_profesional_exige_el_video_aceptado_y_informa_el_estado_de_cada_cosa()
    {
        var e = Exigiendo();
        // Nada aceptado todavía.
        var p0 = e.Consentimiento.Permiso(e.Profesional, "turno", 1).Permiso!;
        Assert.False(p0.Permitido);
        Assert.Equal(("pendiente", true), (p0.VideoEstado, p0.VideoExigido));

        // Video sí, emociones no: el indicador dice video ✓ emociones ✗.
        e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);
        var p1 = e.Consentimiento.Permiso(e.Profesional, "turno", 1).Permiso!;
        Assert.False(p1.Permitido);
        Assert.Equal(("acepta", "sin-decision"), (p1.VideoEstado, p1.Estado));

        // Las dos: se puede analizar.
        e.Consentimiento.Decidir(e.Paciente, "turno", 1, "acepta", e.Texto.Version);
        var p2 = e.Consentimiento.Permiso(e.Profesional, "turno", 1).Permiso!;
        Assert.True(p2.Permitido);
        Assert.Equal(("acepta", "acepta"), (p2.VideoEstado, p2.Estado));
    }

    [Fact]
    public void Revocar_el_video_corta_las_emociones_y_descarta_lo_acumulado()
    {
        var e = Exigiendo();
        e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);
        e.Consentimiento.Decidir(e.Paciente, "turno", 1, "acepta", e.Texto.Version);
        e.Repo.SesionesConEmociones = 1;   // ya había emociones guardadas de esta sesión
        Assert.True(e.Consentimiento.Permiso(e.Profesional, "turno", 1).Permiso!.Permitido);

        e.Aceptacion.Revocar(e.Paciente, "turno", 1);

        var p = e.Consentimiento.Permiso(e.Profesional, "turno", 1).Permiso!;
        Assert.False(p.Permitido);
        Assert.Equal("revocada", p.VideoEstado);
        Assert.NotNull(p.VideoRevocadoUtc);
        // Las emociones aceptadas quedaron revocadas y lo guardado de la sesión se anuló.
        Assert.Equal("revocado", ConsentimientoVideoService.EstadoDe(e.Repo.Ultimo("turno", 1, EscenarioEmociones.IdPaciente)));
        var anulacion = Assert.Single(e.Repo.Anulaciones);
        Assert.Equal(("turno", 1, "revocacion"), (anulacion.Origen, anulacion.IdOrigen, anulacion.Motivo));
        Assert.Equal(0, e.Repo.SesionesConEmociones);
    }

    [Fact]
    public void Mis_consentimientos_lista_las_sesiones_con_video_aceptado_y_si_tambien_hay_emociones()
    {
        var e = Exigiendo();
        Assert.Empty(e.Aceptacion.VigentesDe(e.Paciente));

        e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);
        var soloVideo = Assert.Single(e.Aceptacion.VigentesDe(e.Paciente));
        Assert.Equal(("turno", 1, false), (soloVideo.Origen, soloVideo.IdOrigen, soloVideo.EmocionesAceptadas));

        e.Consentimiento.Decidir(e.Paciente, "turno", 1, "acepta", e.Texto.Version);
        Assert.True(Assert.Single(e.Aceptacion.VigentesDe(e.Paciente)).EmocionesAceptadas);

        // Solo las del propio paciente; el resto de las personas no ve nada.
        Assert.Empty(e.Aceptacion.VigentesDe(e.OtroPaciente));
        Assert.Empty(e.Aceptacion.VigentesDe(e.Profesional));

        // Revocar desde el perfil usa lo mismo que desde la sala.
        e.Aceptacion.Revocar(e.Paciente, "turno", 1);
        Assert.Empty(e.Aceptacion.VigentesDe(e.Paciente));
    }

    [Fact]
    public void Las_aceptaciones_viejas_no_aparecen_en_Mis_consentimientos()
    {
        var e = Exigiendo();
        e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);
        e.Reloj.Avanzar(TimeSpan.FromDays(31));
        Assert.Empty(e.Aceptacion.VigentesDe(e.Paciente));
        Assert.Single(e.Aceptacion.VigentesDe(e.Paciente, dias: 60));
    }

    [Fact]
    public void La_guardia_descarta_las_emociones_de_una_sesion_sin_video_aceptado()
    {
        var e = Exigiendo();
        e.Aceptacion.Aceptar(e.Paciente, "turno", 1, Version);
        e.Consentimiento.Decidir(e.Paciente, "turno", 1, "acepta", e.Texto.Version);
        e.Reloj.Avanzar(TimeSpan.FromMinutes(30));
        var inicio = EscenarioEmociones.Inicio;

        Assert.True(e.Guardia.Evaluar(e.Profesional, EscenarioEmociones.IdPaciente, e.Emociones(inicio, inicio.AddMinutes(30)), false).Aprobada);

        // Un cliente alterado que insiste en guardar con el video revocado: el servidor las descarta.
        e.Video.Filas[0] = e.Video.Filas[0] with { RevocadoUtc = e.Ahora };
        var ev = e.Guardia.Evaluar(e.Profesional, EscenarioEmociones.IdPaciente, e.Emociones(inicio, inicio.AddMinutes(30)), false);
        Assert.False(ev.Aprobada);
        Assert.Equal("video-no-aceptado", ev.Motivo);
        Assert.Contains("videollamada", GuardiaEmocionesService.MensajeDeDescarte(ev.Motivo));
    }
}
