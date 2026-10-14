using System.Text.Json;

// La guardia de lo que se guarda: las emociones solo pasan con una aceptación vigente del paciente de ESA sesión, con una
// ventana dentro de ella y con cantidades posibles. Si algo no cierra, se descartan (la sesión se guarda igual) y el
// motivo queda dicho. Todo sin base: el escenario es el turno 1 (ver EscenarioEmociones).
public class GuardiaEmocionesServiceTests
{
    private static readonly DateTime T0 = EscenarioEmociones.Inicio;

    // Acepta a las 10:00 y deja el reloj a las 10:30: el profesional guarda al terminar la sesión.
    private static EscenarioEmociones ConAceptacion(int minutosDespues = 30)
    {
        var e = new EscenarioEmociones();
        e.Acepta();
        e.Reloj.Avanzar(TimeSpan.FromMinutes(minutosDespues));
        return e;
    }

    private static EvaluacionEmociones Evaluar(EscenarioEmociones e, EmocionesSesionRequest? req, Identidad? yo = null,
        int idPaciente = EscenarioEmociones.IdPaciente, bool formatoViejo = false) =>
        e.Guardia.Evaluar(yo ?? e.Profesional, idPaciente, req, formatoViejo);

    // ───────────── Lo que pasa ─────────────

    [Fact]
    public void Con_aceptacion_vigente_y_una_ventana_valida_se_aprueba_y_arma_el_dato_a_cifrar()
    {
        var e = ConAceptacion();

        var ev = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30), muestras: 600));

        Assert.True(ev.Aprobada);
        Assert.Equal(EvaluacionEmociones.Guardadas, ev.Estado);
        Assert.Null(ev.Motivo);
        Assert.Equal(e.Repo.Filas[0].IdConsentimientoVideo, ev.IdConsentimiento);
        Assert.Equal(600, ev.Muestras);

        using var doc = JsonDocument.Parse(ev.JsonPlano!);
        var raiz = doc.RootElement;
        Assert.Equal(1, raiz.GetProperty("v").GetInt32());
        Assert.Equal(600, raiz.GetProperty("muestras").GetInt32());
        Assert.Equal(e.Repo.Filas[0].IdConsentimientoVideo, raiz.GetProperty("consentimiento").GetInt32());
        Assert.Equal("v0-provisorio", raiz.GetProperty("textoVersion").GetString());
        Assert.Equal(T0, raiz.GetProperty("desdeUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(T0.AddMinutes(30), raiz.GetProperty("hastaUtc").GetDateTime().ToUniversalTime());
        var promedios = raiz.GetProperty("promedios");
        Assert.Equal(7, promedios.EnumerateObject().Count());
        Assert.Equal(60, promedios.GetProperty("neutral").GetInt32());
        Assert.Equal(20, promedios.GetProperty("sad").GetInt32());
    }

    [Fact]
    public void Lo_que_se_guarda_no_lleva_nada_mas_que_lo_esperado()
    {
        var e = ConAceptacion();

        var ev = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30)));

        using var doc = JsonDocument.Parse(ev.JsonPlano!);
        var claves = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "consentimiento", "desdeUtc", "hastaUtc", "muestras", "promedios", "textoVersion", "v" }, claves);
    }

    [Fact]
    public void Los_porcentajes_se_redondean_a_enteros()
    {
        var e = ConAceptacion();
        var req = e.Emociones(T0, T0.AddMinutes(30));
        req.Promedios = new Dictionary<string, double> { ["neutral"] = 60.5, ["sad"] = 39.4 };

        var ev = Evaluar(e, req);

        Assert.True(ev.Aprobada);
        using var doc = JsonDocument.Parse(ev.JsonPlano!);
        Assert.Equal(61, doc.RootElement.GetProperty("promedios").GetProperty("neutral").GetInt32());
        Assert.Equal(39, doc.RootElement.GetProperty("promedios").GetProperty("sad").GetInt32());
    }

    [Fact]
    public void El_configurador_puede_guardar_las_emociones_de_un_turno_y_de_una_admision()
    {
        var e = new EscenarioEmociones();
        e.Acepta("turno");
        e.Acepta("admision", EscenarioEmociones.IdInforme);
        e.Reloj.Avanzar(TimeSpan.FromMinutes(30));

        var turno = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30)), e.Configurador);
        var admision = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30), origen: "admision", id: EscenarioEmociones.IdInforme), e.Configurador);

        Assert.True(turno.Aprobada);
        Assert.True(admision.Aprobada);
        Assert.Equal("admision", admision.Origen);
    }

    // ───────────── Sin pedido o con el formato de antes ─────────────

    [Fact]
    public void Sin_emociones_en_el_pedido_no_hay_nada_que_evaluar()
    {
        var e = ConAceptacion();

        var ev = Evaluar(e, null);

        Assert.False(ev.Aprobada);
        Assert.Equal(EvaluacionEmociones.NoEnviadas, ev.Estado);
        Assert.Null(ev.Motivo);
        e.Guardia.Registrar(e.Profesional, ev, 77);
        Assert.DoesNotContain(e.Auditoria.Registros, r => r.Tipo is "guardadas" or "descartadas");
    }

    [Fact]
    public void El_campo_historialEmociones_de_antes_del_PR_6_se_ignora_y_se_avisa()
    {
        var e = ConAceptacion();

        var ev = Evaluar(e, null, formatoViejo: true);

        Assert.False(ev.Aprobada);
        Assert.Equal(EvaluacionEmociones.Descartadas, ev.Estado);
        Assert.Equal("formato-viejo", ev.Motivo);
    }

    // ───────────── El consentimiento ─────────────

    [Fact]
    public void Sin_decision_no_se_guarda()
    {
        var e = new EscenarioEmociones();
        e.Reloj.Avanzar(TimeSpan.FromMinutes(30));

        var ev = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30)));

        Assert.False(ev.Aprobada);
        Assert.Equal("sin-consentimiento", ev.Motivo);
    }

    [Fact]
    public void Un_rechazo_no_se_guarda()
    {
        var e = new EscenarioEmociones();
        e.Consentimiento.Decidir(e.Paciente, "turno", EscenarioEmociones.IdTurno, "rechaza", e.Texto.Version);
        e.Reloj.Avanzar(TimeSpan.FromMinutes(30));

        var ev = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30)));

        Assert.False(ev.Aprobada);
        Assert.Equal("rechazado", ev.Motivo);
    }

    [Fact]
    public void Una_aceptacion_revocada_no_se_guarda_ni_siquiera_lo_anterior_a_la_revocacion()
    {
        var e = new EscenarioEmociones();
        e.Acepta();
        e.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);
        e.Reloj.Avanzar(TimeSpan.FromMinutes(20));

        // La ventana está entera dentro de la aceptación (10:00 a 10:10), pero la persona ya revocó.
        var ev = Evaluar(e, e.Emociones(T0, T0.AddMinutes(10)));

        Assert.False(ev.Aprobada);
        Assert.Equal("revocado", ev.Motivo);
    }

    [Fact]
    public void Despues_de_revocar_y_volver_a_aceptar_solo_vale_una_ventana_posterior_a_la_nueva_aceptacion()
    {
        var e = new EscenarioEmociones();
        e.Acepta();                                           // 10:00
        e.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        e.Consentimiento.Revocar(e.Paciente, "turno", EscenarioEmociones.IdTurno);   // 10:10
        e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        e.Acepta();                                           // 10:15
        e.Reloj.Avanzar(TimeSpan.FromMinutes(15));            // 10:30

        var desdeLaPrimera = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30)));
        var desdeLaSegunda = Evaluar(e, e.Emociones(T0.AddMinutes(15), T0.AddMinutes(30)));

        Assert.Equal("ventana-anterior-al-consentimiento", desdeLaPrimera.Motivo);
        Assert.True(desdeLaSegunda.Aprobada);
        Assert.Equal(e.Repo.Filas[1].IdConsentimientoVideo, desdeLaSegunda.IdConsentimiento);
    }

    // ───────────── La ventana ─────────────

    [Fact]
    public void El_inicio_puede_adelantarse_a_la_aceptacion_hasta_la_tolerancia_y_no_mas()
    {
        var e = ConAceptacion();   // aceptó a las 10:00:00; tolerancia 5 s

        var justo = Evaluar(e, e.Emociones(T0.AddSeconds(-5), T0.AddMinutes(30)));
        var pasado = Evaluar(e, e.Emociones(T0.AddSeconds(-6), T0.AddMinutes(30)));

        Assert.True(justo.Aprobada);
        Assert.Equal("ventana-anterior-al-consentimiento", pasado.Motivo);
    }

    [Fact]
    public void El_final_puede_pasarse_del_reloj_del_servidor_hasta_la_tolerancia_y_no_mas()
    {
        var e = ConAceptacion();   // ahora = 10:30:00

        var justo = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30).AddSeconds(5)));
        var pasado = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30).AddSeconds(6)));

        Assert.True(justo.Aprobada);
        Assert.Equal("ventana-futura", pasado.Motivo);
    }

    [Fact]
    public void Una_ventana_invertida_vacia_o_de_mas_de_doce_horas_se_rechaza()
    {
        var e = ConAceptacion();
        var invertida = e.Emociones(T0.AddMinutes(30), T0);
        var vacia = e.Emociones(T0, T0);

        Assert.Equal("ventana-invalida", Evaluar(e, invertida).Motivo);
        Assert.Equal("ventana-invalida", Evaluar(e, vacia).Motivo);

        e.Reloj.Avanzar(TimeSpan.FromHours(13));
        Assert.Equal("ventana-invalida", Evaluar(e, e.Emociones(T0, T0.AddHours(13))).Motivo);
    }

    [Fact]
    public void Sin_fechas_la_ventana_es_invalida()
    {
        var e = ConAceptacion();
        var sinDesde = e.Emociones(T0, T0.AddMinutes(30));
        sinDesde.DesdeUtc = null;
        var sinHasta = e.Emociones(T0, T0.AddMinutes(30));
        sinHasta.HastaUtc = null;

        Assert.Equal("ventana-invalida", Evaluar(e, sinDesde).Motivo);
        Assert.Equal("ventana-invalida", Evaluar(e, sinHasta).Motivo);
    }

    // ───────────── Cantidades ─────────────

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void Una_cantidad_de_muestras_vacia_o_negativa_se_rechaza(int? muestras)
    {
        var e = ConAceptacion();
        var req = e.Emociones(T0, T0.AddMinutes(30));
        req.Muestras = muestras;

        Assert.Equal("muestras-invalidas", Evaluar(e, req).Motivo);
    }

    [Fact]
    public void No_se_aceptan_mas_muestras_de_las_posibles_en_la_ventana()
    {
        var e = ConAceptacion();   // 30 minutos = 1800 s; 4 por segundo + 5 = 7205

        var tope = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30), muestras: 7205));
        var imposible = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30), muestras: 7206));

        Assert.True(tope.Aprobada);
        Assert.Equal("muestras-invalidas", imposible.Motivo);
    }

    [Theory]
    [InlineData("ira", 100)]          // no es una de las siete
    [InlineData("neutral", -1)]
    [InlineData("neutral", 100.5)]
    [InlineData("neutral", 50)]       // suma 50
    [InlineData("neutral", double.NaN)]
    [InlineData("neutral", double.PositiveInfinity)]
    public void Los_porcentajes_tienen_que_ser_de_las_siete_expresiones_posibles_y_sumar_cerca_de_100(string clave, double valor)
    {
        var e = ConAceptacion();
        var req = e.Emociones(T0, T0.AddMinutes(30));
        req.Promedios = new Dictionary<string, double> { [clave] = valor };

        Assert.Equal("promedios-invalidos", Evaluar(e, req).Motivo);
    }

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, true)]
    [InlineData(100, true)]
    [InlineData(110, true)]
    [InlineData(111, false)]
    public void La_suma_de_los_porcentajes_puede_desviarse_un_poco_por_el_redondeo(double suma, bool valido)
    {
        var e = ConAceptacion();
        var req = e.Emociones(T0, T0.AddMinutes(30));
        req.Promedios = new Dictionary<string, double> { ["neutral"] = suma > 100 ? 100 : suma, ["sad"] = suma > 100 ? suma - 100 : 0 };

        Assert.Equal(valido, Evaluar(e, req).Aprobada);
    }

    [Fact]
    public void Sin_porcentajes_o_con_mas_de_siete_se_rechaza()
    {
        var e = ConAceptacion();
        var vacio = e.Emociones(T0, T0.AddMinutes(30));
        vacio.Promedios = new Dictionary<string, double>();
        var nulo = e.Emociones(T0, T0.AddMinutes(30));
        nulo.Promedios = null;
        var ocho = e.Emociones(T0, T0.AddMinutes(30));
        ocho.Promedios!["otra"] = 0;

        Assert.Equal("promedios-invalidos", Evaluar(e, vacio).Motivo);
        Assert.Equal("promedios-invalidos", Evaluar(e, nulo).Motivo);
        Assert.Equal("promedios-invalidos", Evaluar(e, ocho).Motivo);
    }

    // ───────────── La sesión y las personas ─────────────

    [Fact]
    public void El_paciente_del_pedido_tiene_que_ser_el_de_la_sesion_de_video()
    {
        var e = ConAceptacion();

        var ev = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30)), idPaciente: EscenarioEmociones.IdOtroPaciente);

        Assert.Equal("paciente-no-coincide", ev.Motivo);
    }

    [Fact]
    public void Solo_guarda_quien_atiende_la_sesion()
    {
        var e = ConAceptacion();
        var req = e.Emociones(T0, T0.AddMinutes(30));

        Assert.Equal("sesion-ajena", Evaluar(e, req, e.OtroProfesional).Motivo);
        Assert.Equal("sesion-ajena", Evaluar(e, req, e.Paciente).Motivo);
        Assert.Equal("sesion-ajena", Evaluar(e, e.Emociones(T0, T0.AddMinutes(30), origen: "admision", id: EscenarioEmociones.IdInforme), e.Profesional).Motivo);
        Assert.True(Evaluar(e, req, e.Profesional).Aprobada);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("turno", null)]
    [InlineData("turno", 0)]
    [InlineData("turno", -4)]
    [InlineData("llamada", 1)]
    [InlineData("turno", 999)]     // no existe
    [InlineData("admision", 999)]
    public void Una_sesion_de_video_que_no_se_puede_identificar_se_rechaza(string? origen, int? id)
    {
        var e = ConAceptacion();
        var req = e.Emociones(T0, T0.AddMinutes(30));
        req.Origen = origen;
        req.Id = id;

        Assert.Equal("sesion-invalida", Evaluar(e, req).Motivo);
    }

    [Fact]
    public void El_origen_se_lee_sin_importar_mayusculas_ni_tildes()
    {
        var e = new EscenarioEmociones();
        e.Acepta("admision", EscenarioEmociones.IdInforme);
        e.Reloj.Avanzar(TimeSpan.FromMinutes(30));
        var req = e.Emociones(T0, T0.AddMinutes(30), origen: "Admisión", id: EscenarioEmociones.IdInforme);

        var ev = Evaluar(e, req, e.Configurador);

        Assert.True(ev.Aprobada);
        Assert.Equal("admision", ev.Origen);
    }

    [Fact]
    public void A_un_menor_no_se_le_guardan_emociones_aunque_haya_una_aceptacion_registrada()
    {
        var e = ConAceptacion();
        e.Repo.Pacientes[EscenarioEmociones.IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(2016, 1, 1), false);

        Assert.Equal("menor-de-edad", Evaluar(e, e.Emociones(T0, T0.AddMinutes(30))).Motivo);
    }

    [Fact]
    public void Sin_clave_de_cifrado_no_se_guarda_nada()
    {
        var e = new EscenarioEmociones(conClave: false);
        e.Reloj.Avanzar(TimeSpan.FromMinutes(30));

        Assert.Equal("no-configurado", Evaluar(e, e.Emociones(T0, T0.AddMinutes(30))).Motivo);
    }

    // ───────────── Auditoría ─────────────

    [Fact]
    public void Registrar_deja_constancia_de_lo_guardado_y_de_lo_descartado_con_su_motivo()
    {
        var e = ConAceptacion();
        var buena = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30), muestras: 321));
        var mala = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30), muestras: 0));

        e.Guardia.Registrar(e.Profesional, buena, idSesion: 77);
        e.Guardia.Registrar(e.Profesional, mala, idSesion: 78);

        var guardadas = Assert.Single(e.Auditoria.Registros, r => r.Tipo == "guardadas");
        Assert.Equal((EscenarioEmociones.UsuarioProfesional, 77, 321), (guardadas.IdUsuario, guardadas.IdOrigen, guardadas.Cantidad));
        var descartadas = Assert.Single(e.Auditoria.Registros, r => r.Tipo == "descartadas");
        Assert.Equal("muestras-invalidas", descartadas.Detalle);
        Assert.Equal("turno", descartadas.Origen);
        Assert.Equal(EscenarioEmociones.IdTurno, descartadas.IdOrigen);
        Assert.Equal(78, descartadas.IdSesion);   // y la sesión clínica que se guardó sin las emociones
    }

    [Fact]
    public void Un_pedido_del_formato_viejo_se_audita_atado_a_la_sesion_clinica_porque_no_dice_cual_es_la_de_video()
    {
        var e = ConAceptacion();
        var viejo = e.Guardia.Evaluar(e.Profesional, EscenarioEmociones.IdPaciente, null, vinoElFormatoViejo: true);

        e.Guardia.Registrar(e.Profesional, viejo, idSesion: 79);

        var d = Assert.Single(e.Auditoria.Registros, r => r.Tipo == "descartadas");
        Assert.Equal("formato-viejo", d.Detalle);
        Assert.Null(d.IdOrigen);        // el pedido no decía cuál es la sesión de video…
        Assert.Equal(79, d.IdSesion);   // …y la auditoría queda atada a la sesión clínica guardada
    }

    [Fact]
    public void Si_la_escritura_no_guardo_las_emociones_por_una_revocacion_en_el_medio_se_cuenta_como_descartada()
    {
        var e = ConAceptacion();
        var aprobada = Evaluar(e, e.Emociones(T0, T0.AddMinutes(30), muestras: 100));
        Assert.True(aprobada.Aprobada);

        var guardada = GuardiaEmocionesService.TrasLaEscritura(aprobada, emocionesGuardadas: true);
        var revocadaEnElMedio = GuardiaEmocionesService.TrasLaEscritura(aprobada, emocionesGuardadas: false);
        e.Guardia.Registrar(e.Profesional, revocadaEnElMedio, idSesion: 90);

        Assert.Same(aprobada, guardada);                                  // lo normal: queda tal cual
        Assert.False(revocadaEnElMedio.Aprobada);
        Assert.Equal(EvaluacionEmociones.Descartadas, revocadaEnElMedio.Estado);
        Assert.Equal("revocado", revocadaEnElMedio.Motivo);
        Assert.Null(revocadaEnElMedio.JsonPlano);                         // no queda nada de lo medido
        var d = Assert.Single(e.Auditoria.Registros, r => r.Tipo == "descartadas");
        Assert.Equal(("revocado", EscenarioEmociones.IdTurno, 90), (d.Detalle, d.IdOrigen, d.IdSesion));
        Assert.DoesNotContain(e.Auditoria.Registros, r => r.Tipo == "guardadas");
    }

    [Fact]
    public void Los_mensajes_de_descarte_no_incluyen_datos_de_nadie()
    {
        foreach (var motivo in new[] { "sin-consentimiento", "rechazado", "revocado", "menor-de-edad", "edad-desconocida", "no-configurado",
                                       "formato-viejo", "ventana-invalida", "muestras-invalidas" })
        {
            var m = GuardiaEmocionesService.MensajeDeDescarte(motivo);
            Assert.False(string.IsNullOrWhiteSpace(m));
            Assert.DoesNotMatch(@"\d{3,}", m);   // ni ids ni números
        }
    }
}
