// Las reglas del consentimiento de los avisos por WhatsApp, sin base ni HTTP: estados, teléfono, menores, texto, quién puede hacer qué, la
// BAJA y lo que NO queda en los logs ni en la auditoría. Con la base real hay una pasada aparte (ConsentimientoWhatsAppBaseRealTests).
public class ConsentimientoWhatsAppServiceTests
{
    private static EstadoConsentimientoWhatsApp Estado(ResultadoConsentimientoWhatsApp r)
    {
        Assert.True(r.Ok, r.Error?.Codigo);
        return r.Estado!;
    }

    private static ErrorDeConsentimiento Falla(ResultadoConsentimientoWhatsApp r)
    {
        Assert.False(r.Ok);
        return r.Error!;
    }

    // ───────────── Sin decisión: nada viene aceptado ─────────────

    [Fact]
    public void Un_paciente_que_nunca_decidio_esta_sin_decision_y_ve_el_texto_para_leerlo()
    {
        var e = new EscenarioWhatsApp();

        var estado = Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente()));

        Assert.True(estado.Disponible);
        Assert.Equal("sin-decision", estado.Estado);
        Assert.Null(estado.Motivo);
        Assert.Equal("v1", estado.Version);
        Assert.Equal("Avisos por WhatsApp", estado.Titulo);
        Assert.Equal(new[] { "Primero.", "Segundo." }, estado.Texto);
        Assert.Null(estado.AceptadoUtc);
        Assert.Null(estado.RevocadoUtc);
        Assert.Null(estado.VersionAceptada);
        Assert.Empty(e.Repo.Filas);   // mirar no registra nada
    }

    [Fact]
    public void Sin_decision_no_hay_optIn_y_no_se_bloquea_nada()
    {
        var e = new EscenarioWhatsApp();

        Assert.Equal(PermisoEnvioWhatsApp.SinDecision, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));
    }

    // ───────────── Aceptar ─────────────

    [Fact]
    public void Aceptar_guarda_quien_para_que_numero_con_que_texto_y_cuando()
    {
        var e = new EscenarioWhatsApp();

        var estado = Estado(e.Aceptar());

        var fila = Assert.Single(e.Repo.Filas);
        Assert.Equal(EscenarioWhatsApp.IdPaciente, fila.IdPaciente);
        Assert.Equal(EscenarioWhatsApp.IdUsuario, fila.IdUsuario);                 // el usuario del propio paciente
        Assert.Equal(EscenarioWhatsApp.NumeroDelPaciente, fila.Telefono);          // el número nacional de 10 dígitos
        Assert.Equal("perfil", fila.Origen);
        Assert.Equal("v1", fila.VersionTexto);
        Assert.Equal(e.Textos.Texto!.Hash, fila.HashTexto);                        // el SHA-256 del texto exacto que se mostró
        Assert.Equal(e.Reloj.Ahora, fila.AceptadoUtc);
        Assert.Null(fila.RevocadoUtc);
        Assert.Null(fila.MotivoRevocacion);

        Assert.Equal("acepta", estado.Estado);
        Assert.Equal(e.Reloj.Ahora, estado.AceptadoUtc);
        Assert.Equal("v1", estado.VersionAceptada);
        Assert.Equal(PermisoEnvioWhatsApp.ConConsentimiento, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));
        Assert.Equal(new[] { "acepta" }, e.Auditoria.Registros.Select(r => r.Accion));
    }

    [Fact]
    public void Aceptar_dos_veces_lo_mismo_no_agrega_otra_fila_ni_otra_auditoria()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Reloj.Avanzar(TimeSpan.FromHours(1));

        var segunda = Estado(e.Aceptar());

        Assert.Single(e.Repo.Filas);
        Assert.Single(e.Auditoria.Registros);
        Assert.Equal(new DateTime(2031, 3, 4, 10, 0, 0), segunda.AceptadoUtc);   // la fecha sigue siendo la de la primera
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v0-provisorio")]       // una versión vieja
    [InlineData("V1")]                  // las versiones se comparan exactas
    [InlineData("v1 ")]
    public void Aceptar_sin_la_version_vigente_del_texto_se_rechaza(string? version)
    {
        var e = new EscenarioWhatsApp();

        var error = Falla(e.Servicio.Aceptar(EscenarioWhatsApp.Paciente(), version));

        Assert.Equal(TipoErrorConsentimiento.Conflicto, error.Tipo);
        Assert.Equal("version-desactualizada", error.Codigo);
        Assert.Empty(e.Repo.Filas);
    }

    [Fact]
    public void Si_no_hay_texto_configurado_no_se_puede_aceptar_y_se_dice()
    {
        var e = new EscenarioWhatsApp();
        e.Textos.Texto = null;

        var estado = Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente()));
        var error = Falla(e.Servicio.Aceptar(EscenarioWhatsApp.Paciente(), "v1"));

        Assert.False(estado.Disponible);
        Assert.Equal("no-disponible", estado.Estado);
        Assert.Equal("no-configurado", estado.Motivo);
        Assert.Null(estado.Texto);
        Assert.Equal("no-configurado", error.Codigo);
        Assert.Empty(e.Repo.Filas);
    }

    // ───────────── El teléfono ─────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("5555-0001")]            // sin código de área
    [InlineData("1155550")]
    [InlineData("+34 612 345 678")]      // de otro país
    public void Sin_un_celular_argentino_valido_no_se_puede_aceptar(string? telefono)
    {
        var e = new EscenarioWhatsApp();
        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] = new DatosPacienteParaWhatsApp(telefono, new DateTime(1990, 5, 5), false);

        var estado = Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente()));
        var error = Falla(e.Aceptar());

        Assert.False(estado.Disponible);
        Assert.Equal("telefono-invalido", estado.Motivo);
        Assert.Equal("sin-decision", estado.Estado);
        Assert.Equal(TipoErrorConsentimiento.Conflicto, error.Tipo);
        Assert.Equal("telefono-invalido", error.Codigo);
        Assert.Empty(e.Repo.Filas);
    }

    [Fact]
    public void El_mismo_numero_escrito_de_otra_forma_sigue_siendo_el_mismo_consentimiento()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();

        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] = new DatosPacienteParaWhatsApp("+54 9 11 5555-0001", new DateTime(1990, 5, 5), false);
        Assert.Equal("acepta", Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente())).Estado);
        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] = new DatosPacienteParaWhatsApp("011 15 5555-0001", new DateTime(1990, 5, 5), false);
        Assert.Equal("acepta", Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente())).Estado);

        Assert.Equal(PermisoEnvioWhatsApp.ConConsentimiento, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, "011 15 5555-0001"));
        Assert.Single(e.Repo.Filas);
    }

    [Fact]
    public void Si_cambia_el_telefono_la_aceptacion_deja_de_valer_hasta_que_vuelva_a_aceptar()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();

        // El paciente edita su perfil: otro celular. Nadie tocó el consentimiento, pero ya no es para este número.
        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] = new DatosPacienteParaWhatsApp("351 412-3456", new DateTime(1990, 5, 5), false);
        var estado = Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente()));
        Assert.Equal("telefono-cambiado", estado.Estado);
        Assert.True(estado.Disponible);                            // puede aceptar para el número nuevo
        Assert.Equal(PermisoEnvioWhatsApp.SinDecision, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, "351 412-3456"));

        // Vuelve a aceptar: la fila vieja se cierra (por el cambio de número) y se abre una nueva con el número nuevo.
        e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        Assert.Equal("acepta", Estado(e.Aceptar()).Estado);
        Assert.Equal(2, e.Repo.Filas.Count);
        Assert.Equal("telefono-cambiado", e.Repo.Filas[0].MotivoRevocacion);
        Assert.Equal(e.Reloj.Ahora, e.Repo.Filas[0].RevocadoUtc);
        Assert.Equal("3514123456", e.Repo.Filas[1].Telefono);
        Assert.Null(e.Repo.Filas[1].RevocadoUtc);
        Assert.Equal(PermisoEnvioWhatsApp.ConConsentimiento, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, "351 412-3456"));
    }

    [Fact]
    public void Si_borra_el_telefono_queda_telefono_cambiado_y_no_puede_aceptar_pero_si_revocar()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] = new DatosPacienteParaWhatsApp(null, new DateTime(1990, 5, 5), false);

        var estado = Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente()));
        Assert.Equal("telefono-cambiado", estado.Estado);
        Assert.False(estado.Disponible);
        Assert.Equal("telefono-invalido", estado.Motivo);
        Assert.Equal(PermisoEnvioWhatsApp.SinDecision, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, null));

        Assert.Equal("revocado", Estado(e.Servicio.Revocar(EscenarioWhatsApp.Paciente())).Estado);
    }

    // ───────────── El texto ─────────────

    [Fact]
    public void Si_se_publica_un_texto_nuevo_las_aceptaciones_viejas_dejan_de_valer_hasta_que_se_vuelva_a_aceptar()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Textos.Texto = FakeTextoConsentimientoWhatsApp.Version("v2", "Texto nuevo.");

        var estado = Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente()));
        Assert.Equal("texto-desactualizado", estado.Estado);
        Assert.Equal("v2", estado.Version);                         // la que tiene que leer ahora
        Assert.Equal("v1", estado.VersionAceptada);                 // la que había aceptado
        Assert.Equal(PermisoEnvioWhatsApp.SinDecision, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));

        // Con la versión vieja no puede (leyó otro texto); con la nueva, sí, y la vieja queda cerrada por el texto nuevo.
        Assert.Equal("version-desactualizada", Falla(e.Servicio.Aceptar(EscenarioWhatsApp.Paciente(), "v1")).Codigo);
        Assert.Equal("acepta", Estado(e.Servicio.Aceptar(EscenarioWhatsApp.Paciente(), "v2")).Estado);
        Assert.Equal(2, e.Repo.Filas.Count);
        Assert.Equal("texto-nuevo", e.Repo.Filas[0].MotivoRevocacion);
        Assert.Equal("v2", e.Repo.Filas[1].VersionTexto);
        Assert.Equal(e.Textos.Texto.Hash, e.Repo.Filas[1].HashTexto);
        Assert.NotEqual(e.Repo.Filas[0].HashTexto, e.Repo.Filas[1].HashTexto);   // cada aceptación guarda el hash de SU texto
    }

    [Fact]
    public void Si_el_texto_deja_de_estar_disponible_una_aceptacion_vigente_no_da_optIn()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Textos.Texto = null;   // una configuración rota: no hay forma de saber qué texto era el vigente

        Assert.Equal(PermisoEnvioWhatsApp.SinDecision, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));
    }

    // ───────────── Revocar ─────────────

    [Fact]
    public void Revocar_cierra_la_aceptacion_con_fecha_y_motivo_y_ya_no_se_manda_nada()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Reloj.Avanzar(TimeSpan.FromDays(2));

        var estado = Estado(e.Servicio.Revocar(EscenarioWhatsApp.Paciente()));

        Assert.Equal("revocado", estado.Estado);
        Assert.Equal(e.Reloj.Ahora, estado.RevocadoUtc);
        Assert.Equal(new DateTime(2031, 3, 4, 10, 0, 0), estado.AceptadoUtc);   // la aceptación original queda
        var fila = Assert.Single(e.Repo.Filas);
        Assert.Equal("paciente", fila.MotivoRevocacion);
        Assert.Equal(PermisoEnvioWhatsApp.Revocado, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));
        Assert.Equal(new[] { "acepta", "revoca" }, e.Auditoria.Registros.Select(r => r.Accion));
    }

    [Fact]
    public void Revocado_sigue_bloqueando_aunque_despues_cambie_el_telefono_o_el_texto()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Servicio.Revocar(EscenarioWhatsApp.Paciente());

        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] = new DatosPacienteParaWhatsApp("351 412-3456", new DateTime(1990, 5, 5), false);
        e.Textos.Texto = FakeTextoConsentimientoWhatsApp.Version("v2");

        Assert.Equal(PermisoEnvioWhatsApp.Revocado, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, "351 412-3456"));
    }

    [Fact]
    public void Revocar_de_nuevo_o_sin_haber_aceptado_no_hace_nada_ni_cambia_la_fecha()
    {
        var e = new EscenarioWhatsApp();

        // Sin haber aceptado nunca.
        Assert.Equal("sin-decision", Estado(e.Servicio.Revocar(EscenarioWhatsApp.Paciente())).Estado);
        Assert.Empty(e.Repo.Filas);
        Assert.Empty(e.Auditoria.Registros);

        // Y revocar dos veces.
        e.Aceptar();
        e.Servicio.Revocar(EscenarioWhatsApp.Paciente());
        var fecha = e.Repo.Filas[0].RevocadoUtc;
        e.Reloj.Avanzar(TimeSpan.FromHours(5));
        Assert.Equal("revocado", Estado(e.Servicio.Revocar(EscenarioWhatsApp.Paciente())).Estado);
        Assert.Equal(fecha, e.Repo.Filas[0].RevocadoUtc);
        Assert.Equal(2, e.Auditoria.Registros.Count);   // acepta + una sola revocación
    }

    [Fact]
    public void Despues_de_revocar_puede_volver_a_aceptar_y_queda_todo_el_historial()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Reloj.Avanzar(TimeSpan.FromDays(1));
        e.Servicio.Revocar(EscenarioWhatsApp.Paciente());
        e.Reloj.Avanzar(TimeSpan.FromDays(1));

        var estado = Estado(e.Aceptar());

        Assert.Equal("acepta", estado.Estado);
        Assert.Equal(2, e.Repo.Filas.Count);
        Assert.Equal("paciente", e.Repo.Filas[0].MotivoRevocacion);   // la primera sigue revocada
        Assert.Null(e.Repo.Filas[1].RevocadoUtc);
        Assert.Null(estado.RevocadoUtc);                              // el estado es el de la fila más nueva
        Assert.Equal(PermisoEnvioWhatsApp.ConConsentimiento, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));
    }

    // ───────────── Menores ─────────────

    [Fact]
    public void Un_menor_de_18_anios_no_puede_aceptar_solo()
    {
        var e = new EscenarioWhatsApp();
        var hoy = DateOnly.FromDateTime(e.Reloj.GetLocalNow().DateTime);
        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] =
            new DatosPacienteParaWhatsApp(EscenarioWhatsApp.TelefonoDelPaciente, hoy.AddYears(-17).ToDateTime(TimeOnly.MinValue), false);

        var estado = Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente()));
        var error = Falla(e.Aceptar());

        Assert.False(estado.Disponible);
        Assert.Equal("menor-de-edad", estado.Motivo);
        Assert.Equal("menor-de-edad", error.Codigo);
        Assert.Empty(e.Repo.Filas);
    }

    [Fact]
    public void Quien_cumple_18_hoy_ya_puede_y_quien_los_cumple_manana_todavia_no()
    {
        var e = new EscenarioWhatsApp();
        var hoy = DateOnly.FromDateTime(e.Reloj.GetLocalNow().DateTime);

        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] =
            new DatosPacienteParaWhatsApp(EscenarioWhatsApp.TelefonoDelPaciente, hoy.AddYears(-18).ToDateTime(TimeOnly.MinValue), false);
        Assert.True(Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente())).Disponible);

        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] =
            new DatosPacienteParaWhatsApp(EscenarioWhatsApp.TelefonoDelPaciente, hoy.AddYears(-18).AddDays(1).ToDateTime(TimeOnly.MinValue), false);
        Assert.Equal("menor-de-edad", Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente())).Motivo);
    }

    [Fact]
    public void Con_un_tutor_cargado_se_lo_trata_como_menor_aunque_la_fecha_diga_otra_cosa()
    {
        var e = new EscenarioWhatsApp();
        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] = new DatosPacienteParaWhatsApp(EscenarioWhatsApp.TelefonoDelPaciente, new DateTime(1960, 1, 1), true);

        Assert.Equal("menor-de-edad", Falla(e.Aceptar()).Codigo);
    }

    [Fact]
    public void Sin_fecha_de_nacimiento_y_sin_tutor_se_deja_aceptar()
    {
        var e = new EscenarioWhatsApp();
        e.Repo.Pacientes[EscenarioWhatsApp.IdPaciente] = new DatosPacienteParaWhatsApp(EscenarioWhatsApp.TelefonoDelPaciente, null, false);

        Assert.Equal("acepta", Estado(e.Aceptar()).Estado);
    }

    // ───────────── Quién puede hacer qué ─────────────

    [Fact]
    public void Un_profesional_o_el_configurador_no_pueden_ver_aceptar_ni_revocar_por_un_paciente()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        var antes = e.Repo.Filas.ToList();

        foreach (var yo in new[] { EscenarioWhatsApp.Profesional(), EscenarioWhatsApp.Configurador() })
        {
            Assert.Equal(TipoErrorConsentimiento.SoloPacientes, Falla(e.Servicio.Obtener(yo)).Tipo);
            Assert.Equal(TipoErrorConsentimiento.SoloPacientes, Falla(e.Servicio.Aceptar(yo, "v1")).Tipo);
            Assert.Equal(TipoErrorConsentimiento.SoloPacientes, Falla(e.Servicio.Revocar(yo)).Tipo);
        }
        Assert.Equal(antes, e.Repo.Filas);
        Assert.Single(e.Auditoria.Registros);
    }

    [Fact]
    public void Un_paciente_sin_id_de_paciente_en_la_sesion_no_hace_nada()
    {
        var e = new EscenarioWhatsApp();
        var sinPaciente = new Identidad(31, Roles.Paciente, null, null);

        Assert.Equal(TipoErrorConsentimiento.SoloPacientes, Falla(e.Servicio.Aceptar(sinPaciente, "v1")).Tipo);
        Assert.Empty(e.Repo.Filas);
    }

    [Fact]
    public void Cada_paciente_toca_solo_lo_suyo()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();   // el 301

        var otro = EscenarioWhatsApp.Paciente(EscenarioWhatsApp.IdOtroUsuario, EscenarioWhatsApp.IdOtroPaciente);
        Assert.Equal("sin-decision", Estado(e.Servicio.Obtener(otro)).Estado);          // lo del 301 no se le ve
        e.Servicio.Revocar(otro);                                                       // y revocar lo suyo no toca lo del 301
        Assert.Equal("acepta", Estado(e.Servicio.Obtener(EscenarioWhatsApp.Paciente())).Estado);

        e.Servicio.Aceptar(otro, "v1");
        Assert.Equal(2, e.Repo.Filas.Count);
        Assert.Equal(EscenarioWhatsApp.IdOtroUsuario, e.Repo.Filas.Single(f => f.IdPaciente == EscenarioWhatsApp.IdOtroPaciente).IdUsuario);
        Assert.Equal("3514123456", e.Repo.Filas.Single(f => f.IdPaciente == EscenarioWhatsApp.IdOtroPaciente).Telefono);
    }

    [Fact]
    public void Un_paciente_que_no_existe_da_no_encontrado()
    {
        var e = new EscenarioWhatsApp();

        var error = Falla(e.Servicio.Aceptar(EscenarioWhatsApp.Paciente(99, 999), "v1"));

        Assert.Equal(TipoErrorConsentimiento.NoEncontrado, error.Tipo);
        Assert.Empty(e.Repo.Filas);
    }

    // ───────────── Carreras ─────────────

    [Fact]
    public void Si_otro_pedido_igual_abrio_la_aceptacion_un_instante_antes_se_contesta_el_estado_real_sin_error()
    {
        var e = new EscenarioWhatsApp();
        e.Repo.SimularCarrera = true;

        var estado = Estado(e.Aceptar());

        Assert.Equal("acepta", estado.Estado);                  // el estado real: la aceptación del otro pedido, que es igual
        Assert.Single(e.Repo.Filas);                            // una sola fila, no dos
        Assert.Empty(e.Auditoria.Registros);                    // y este pedido no audita una aceptación que no registró él
    }

    // ───────────── BAJA por WhatsApp ─────────────

    [Fact]
    public void La_baja_revoca_a_todos_los_que_aceptaron_ese_numero_y_a_nadie_mas()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();                                                                     // 301, 1155550001
        e.Repo.Pacientes[EscenarioWhatsApp.IdOtroPaciente] = new DatosPacienteParaWhatsApp("011 15 5555-0001", new DateTime(1980, 1, 1), false);
        e.Aceptar(EscenarioWhatsApp.Paciente(EscenarioWhatsApp.IdOtroUsuario, EscenarioWhatsApp.IdOtroPaciente));   // 302, el MISMO celular
        e.Repo.Pacientes[303] = new DatosPacienteParaWhatsApp("351 412-3456", new DateTime(1980, 1, 1), false);
        e.Aceptar(EscenarioWhatsApp.Paciente(33, 303));                                  // 303, otro celular
        e.Reloj.Avanzar(TimeSpan.FromDays(3));

        var cuantas = e.Servicio.RegistrarBaja("1155550001");

        Assert.Equal(2, cuantas);
        Assert.Equal(PermisoEnvioWhatsApp.Revocado, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));
        Assert.Equal(PermisoEnvioWhatsApp.Revocado, e.Servicio.Evaluar(EscenarioWhatsApp.IdOtroPaciente, "011 15 5555-0001"));
        Assert.Equal(PermisoEnvioWhatsApp.ConConsentimiento, e.Servicio.Evaluar(303, "351 412-3456"));
        Assert.All(e.Repo.Filas.Where(f => f.IdPaciente != 303), f =>
        {
            Assert.Equal("baja-whatsapp", f.MotivoRevocacion);
            Assert.Equal(e.Reloj.Ahora, f.RevocadoUtc);
        });
        // Auditada, sin usuario (no hay una sesión detrás) y sin el teléfono.
        var bajas = e.Auditoria.Registros.Where(r => r.Accion == "baja").ToList();
        Assert.Equal(2, bajas.Count);
        Assert.All(bajas, b => Assert.Null(b.IdUsuario));
    }

    [Fact]
    public void La_baja_de_un_numero_sin_aceptaciones_no_hace_nada_y_una_aceptacion_cerrada_no_se_toca()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Servicio.Revocar(EscenarioWhatsApp.Paciente());
        var fecha = e.Repo.Filas[0].RevocadoUtc;
        e.Reloj.Avanzar(TimeSpan.FromDays(1));

        Assert.Equal(0, e.Servicio.RegistrarBaja("1155550001"));    // ya estaba revocada
        Assert.Equal(0, e.Servicio.RegistrarBaja("3514123456"));    // nadie aceptó este número
        Assert.Equal(fecha, e.Repo.Filas[0].RevocadoUtc);
        Assert.Equal("paciente", e.Repo.Filas[0].MotivoRevocacion); // y sigue valiendo el motivo original
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("abc")]
    [InlineData("1111111111")]
    public void La_baja_de_algo_que_no_es_un_telefono_argentino_no_toca_la_base(string telefono)
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();

        Assert.Equal(0, e.Servicio.RegistrarBaja(telefono));
        Assert.Null(e.Repo.Filas[0].RevocadoUtc);
    }

    [Fact]
    public void Si_la_base_falla_la_baja_no_lanza_y_el_log_no_trae_el_telefono()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Repo.Falla = new InvalidOperationException("se cayó la base de 1155550001");

        var cuantas = e.Servicio.RegistrarBaja("1155550001");

        Assert.Equal(0, cuantas);
        Assert.Contains(e.Logs.Lineas, l => l.StartsWith("Warning|"));
        Assert.DoesNotContain("1155550001", e.Logs.Todo);
        Assert.DoesNotContain("se cayó", e.Logs.Todo);          // ni el mensaje de la excepción: solo su tipo
    }

    [Fact]
    public void Evaluar_con_la_base_caida_lanza_para_que_el_aviso_no_salga_sin_saber_si_hay_consentimiento()
    {
        var e = new EscenarioWhatsApp();
        e.Repo.Falla = new InvalidOperationException("sin base");

        Assert.Throws<InvalidOperationException>(() => e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));
    }

    // ───────────── Registro ─────────────

    [Fact]
    public void El_registro_valida_el_telefono_la_edad_y_el_texto_antes_de_crear_la_cuenta()
    {
        var e = new EscenarioWhatsApp();
        var nacimiento = new DateTime(1990, 5, 5);

        Assert.Null(e.Servicio.ValidarAceptacionEnRegistro("11 5555-0001", nacimiento, "v1"));

        var telefono = e.Servicio.ValidarAceptacionEnRegistro("5555-0001", nacimiento, "v1")!;
        Assert.Equal(TipoErrorConsentimiento.SolicitudInvalida, telefono.Tipo);
        Assert.Equal("telefono-invalido", telefono.Codigo);

        var menor = e.Servicio.ValidarAceptacionEnRegistro("11 5555-0001", new DateTime(2018, 2, 2), "v1")!;
        Assert.Equal(TipoErrorConsentimiento.SolicitudInvalida, menor.Tipo);
        Assert.Equal("menor-de-edad", menor.Codigo);

        var texto = e.Servicio.ValidarAceptacionEnRegistro("11 5555-0001", nacimiento, "v0")!;
        Assert.Equal(TipoErrorConsentimiento.Conflicto, texto.Tipo);
        Assert.Equal("version-desactualizada", texto.Codigo);
        Assert.Equal("version-desactualizada", e.Servicio.ValidarAceptacionEnRegistro("11 5555-0001", nacimiento, null)!.Codigo);

        e.Textos.Texto = null;
        Assert.Equal("no-configurado", e.Servicio.ValidarAceptacionEnRegistro("11 5555-0001", nacimiento, "v1")!.Codigo);
        Assert.Empty(e.Repo.Filas);   // validar no registra nada
    }

    [Fact]
    public void Aceptar_en_el_registro_deja_el_origen_registro_y_a_nombre_del_usuario_recien_creado()
    {
        var e = new EscenarioWhatsApp();

        var ok = e.Servicio.AceptarEnRegistro(310, 410, "011 15 5555-0001", "v1");

        Assert.True(ok);
        var fila = Assert.Single(e.Repo.Filas);
        Assert.Equal((310, 410, "1155550001", "registro", "v1"), (fila.IdPaciente, fila.IdUsuario, fila.Telefono, fila.Origen, fila.VersionTexto));
        Assert.Equal(e.Textos.Texto!.Hash, fila.HashTexto);
        Assert.Equal(410, Assert.Single(e.Auditoria.Registros).IdUsuario);
    }

    [Theory]
    [InlineData("v1", "5555-0001")]       // teléfono que no sirve
    [InlineData("v0", "11 5555-0001")]    // texto que no es el vigente
    [InlineData(null, "11 5555-0001")]    // sin versión
    public void Aceptar_en_el_registro_con_algo_que_no_sirve_no_registra_nada(string? version, string telefono)
    {
        var e = new EscenarioWhatsApp();

        Assert.False(e.Servicio.AceptarEnRegistro(310, 410, telefono, version));
        Assert.Empty(e.Repo.Filas);
    }

    [Fact]
    public void Aceptar_en_el_registro_no_lanza_si_la_base_falla_y_el_log_no_trae_el_telefono()
    {
        var e = new EscenarioWhatsApp();
        e.Repo.Falla = new InvalidOperationException("sin base para 11 5555-0001");

        Assert.False(e.Servicio.AceptarEnRegistro(310, 410, "11 5555-0001", "v1"));
        Assert.Contains(e.Logs.Lineas, l => l.StartsWith("Warning|"));
        Assert.DoesNotContain("5555", e.Logs.Todo);
    }

    // ───────────── Privacidad ─────────────

    [Fact]
    public void Ni_el_log_ni_la_auditoria_traen_el_telefono_ni_el_texto()
    {
        var e = new EscenarioWhatsApp();
        e.Aceptar();
        e.Servicio.Revocar(EscenarioWhatsApp.Paciente());
        e.Aceptar();
        e.Servicio.RegistrarBaja("1155550001");
        e.Servicio.AceptarEnRegistro(310, 410, "011 15 5555-0001", "v1");
        e.Repo.Falla = new InvalidOperationException("boom");
        e.Servicio.RegistrarBaja("1155550001");

        var auditoria = string.Join("\n", e.Auditoria.Registros.Select(r => r.Detalle));
        foreach (var salida in new[] { e.Logs.Todo, auditoria })
        {
            Assert.DoesNotContain("5555", salida);
            Assert.DoesNotContain("Primero", salida);                      // el texto del consentimiento
            Assert.DoesNotMatch(@"\d{8,}", salida);                        // ningún número largo
        }
    }
}
