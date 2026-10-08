public class BotReservasServiceTests
{
    private readonly Escenario _e = new();
    private const string Juan = Escenario.TelJuan;
    private const string Desconocido = "+54 9 351 000-0000";
    private static DateTime Dia(int d, int h, int m = 0) => new(2026, 9, d, h, m, 0);

    // ── Identificación ──────────────────────────────────────

    [Fact]
    public async Task Reconoce_al_paciente_por_telefono_en_formato_WhatsApp()
    {
        var r = await _e.Enviar("+54 9 11 5555-0001", "hola");
        Assert.Contains("¡Hola, Juan!", r);
        Assert.Contains("1) Pedir turno", r);
        Assert.Equal(1, _e.ConversacionDe(Juan).IdPaciente);
    }

    [Fact]
    public async Task Telefono_desconocido_pide_DNI_y_si_no_existe_manda_a_registrarse_sin_crear_pacientes()
    {
        Assert.Contains("DNI", await _e.Enviar(Desconocido, "hola"));
        var r = await _e.Enviar(Desconocido, "99.999.999");
        Assert.Contains("https://yosoy.test/registro", r);
        Assert.Equal(2, _e.Db.Pacientes.Count);
        Assert.Null(_e.ConversacionDe(Desconocido).IdPaciente);
    }

    [Fact]
    public async Task DNI_registrado_vincula_la_conversacion()
    {
        await _e.Enviar(Desconocido, "hola");
        Assert.Contains("¡Hola, Ana!", await _e.Enviar(Desconocido, "31000002"));
        Assert.Equal(2, _e.ConversacionDe(Desconocido).IdPaciente);
    }

    [Fact]
    public async Task Despues_de_3_DNIs_fallidos_no_sigue_probando()
    {
        await _e.Enviar(Desconocido, "hola");
        foreach (var dni in new[] { "11111111", "22222222", "33333333" }) await _e.Enviar(Desconocido, dni);
        var r = await _e.Enviar(Desconocido, "31000002");   // DNI válido, pero ya agotó los intentos
        Assert.Contains("Ya no puedo seguir probando", r);
        Assert.Null(_e.ConversacionDe(Desconocido).IdPaciente);
    }

    // ── Pedir turno ─────────────────────────────────────────

    [Fact]
    public async Task Flujo_completo_de_pedir_turno_crea_un_turno_Pendiente()
    {
        await _e.Enviar(Juan, "hola");
        var horarios = await _e.Enviar(Juan, "1");
        Assert.Contains("1) viernes 25/09 a las 09:00", horarios);
        Assert.Contains("¿Confirmás?", await _e.Enviar(Juan, "2"));
        var r = await _e.Enviar(Juan, "1");

        Assert.Contains("¡Listo!", r);
        var turno = Assert.Single(_e.Db.Turnos);
        Assert.Equal(Dia(25, 9, 30), turno.Fecha + turno.Hora);
        Assert.Equal("Pendiente", _e.Db.EstadoDe(turno.IdTurno));
        Assert.Contains($"TurnoSolicitado:{turno.IdTurno}:True", _e.Notificador.Eventos);
        // Sin consentimiento firmado: igual se crea (RN-02 se exige al confirmar) pero se avisa.
        Assert.Contains("https://yosoy.test/consentimiento", r);
    }

    [Fact]
    public async Task Con_consentimiento_firmado_no_aparece_el_aviso()
    {
        _e.Db.ConConsentimiento.Add(1);
        await _e.Enviar(Juan, "quiero pedir un turno");
        await _e.Enviar(Juan, "1");
        Assert.DoesNotContain("consentimiento", await _e.Enviar(Juan, "si"));
    }

    [Fact]
    public async Task Modo_estricto_de_consentimiento_no_crea_el_turno_y_manda_el_link()
    {
        _e.Opciones.CurrentValue.ExigirConsentimientoParaSolicitar = true;
        await _e.Enviar(Juan, "quiero pedir un turno");
        await _e.Enviar(Juan, "1");
        var r = await _e.Enviar(Juan, "1");
        Assert.Contains("No pude registrar el turno", r);
        Assert.Contains("https://yosoy.test/consentimiento", r);
        Assert.Empty(_e.Db.Turnos);
    }

    [Fact]
    public async Task Si_el_horario_se_ocupa_antes_de_confirmar_lo_rechaza_y_ofrece_otros()
    {
        await _e.Enviar(Juan, "quiero pedir un turno");
        await _e.Enviar(Juan, "1");                                        // elige viernes 09:00
        _e.Db.AgregarTurno(2, 10, Dia(25, 9), BaseEnMemoria.Pendiente);    // otro paciente se lo gana
        var r = await _e.Enviar(Juan, "1");
        Assert.Contains("ya está ocupado", r);
        Assert.Contains("1) viernes 25/09 a las 09:30", r);
        Assert.Single(_e.Db.Turnos);
    }

    [Fact]
    public async Task Sin_profesionales_asignados_elige_por_especialidad()
    {
        await _e.Enviar(Desconocido, "hola");
        await _e.Enviar(Desconocido, "31000002");                          // Ana, sin profesionales
        var r = await _e.Enviar(Desconocido, "1");
        Assert.Contains("¿Para qué especialidad", r);
        Assert.Contains("1) Psicología", r);
        Assert.Contains("horarios disponibles con Santiago Manto", await _e.Enviar(Desconocido, "1"));
    }

    // ── Ver y cancelar ──────────────────────────────────────

    [Fact]
    public async Task Ver_mis_turnos_lista_los_proximos_activos()
    {
        _e.Db.AgregarTurno(1, 10, Dia(28, 10), BaseEnMemoria.Confirmado);
        _e.Db.AgregarTurno(1, 10, Dia(29, 10), BaseEnMemoria.Cancelado);
        await _e.Enviar(Juan, "hola");
        var r = await _e.Enviar(Juan, "2");
        Assert.Contains("lunes 28/09 a las 10:00 con Santiago Manto (Virtual) — Confirmado", r);
        Assert.DoesNotContain("29/09", r);
    }

    [Fact]
    public async Task Cancelar_con_mas_de_24_horas()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(28, 10), BaseEnMemoria.Pendiente);
        await _e.Enviar(Juan, "hola");
        await _e.Enviar(Juan, "3");
        Assert.Contains("¿Confirmás que querés cancelar", await _e.Enviar(Juan, "1"));
        Assert.Contains("cancelamos tu turno", await _e.Enviar(Juan, "1"));
        Assert.Equal("Cancelado", _e.Db.EstadoDe(t.IdTurno));
        Assert.Contains($"TurnoCancelado:{t.IdTurno}:True", _e.Notificador.Eventos);
    }

    [Fact]
    public async Task Cancelar_con_menos_de_24_horas_no_cancela_y_ofrece_avisar_al_profesional()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(24, 16), BaseEnMemoria.Confirmado);
        await _e.Enviar(Juan, "cancelar");
        Assert.Contains("Faltan menos de 24 horas", await _e.Enviar(Juan, "1"));
        Assert.Contains("le avisamos a Santiago Manto", await _e.Enviar(Juan, "1"));
        Assert.Equal("Confirmado", _e.Db.EstadoDe(t.IdTurno));
        Assert.Contains($"NoAsistencia:{t.IdTurno}", _e.Notificador.Eventos);
    }

    // ── Seguridad clínica y derivación ──────────────────────

    [Fact]
    public async Task Frase_de_riesgo_corta_el_flujo_marca_Urgente_avisa_y_el_bot_calla()
    {
        await _e.Enviar(Juan, "quiero pedir un turno");                    // en medio de un flujo
        var r = await _e.Enviar(Juan, "La verdad es que NO QUIERO VIVIR más");
        Assert.Contains("[COMPLETAR Y VALIDAR]", r);
        Assert.Contains("guardia", r);
        Assert.Equal(EstadoConversacion.Urgente, _e.ConversacionDe(Juan).Estado);
        Assert.Contains($"Urgencia:{_e.ConversacionDe(Juan).IdConversacion}", _e.Notificador.Eventos);

        Assert.Equal("", await _e.Enviar(Juan, "1"));                      // sin respuesta del bot
        Assert.Empty(_e.Db.Turnos);
    }

    [Fact]
    public async Task La_frase_de_riesgo_se_detecta_incluso_antes_de_identificar_al_paciente()
    {
        var r = await _e.Enviar(Desconocido, "pienso en el suicidio");
        Assert.Contains("[COMPLETAR Y VALIDAR]", r);
        Assert.Equal(EstadoConversacion.Urgente, _e.ConversacionDe(Desconocido).Estado);
    }

    [Fact]
    public async Task Hablar_con_una_persona_deriva_y_el_bot_deja_de_responder()
    {
        await _e.Enviar(Juan, "hola");
        Assert.Contains("una persona del equipo", await _e.Enviar(Juan, "5"));
        Assert.Equal(EstadoConversacion.DerivadaHumano, _e.ConversacionDe(Juan).Estado);
        Assert.Contains($"Derivacion:{_e.ConversacionDe(Juan).IdConversacion}", _e.Notificador.Eventos);
        Assert.Equal("", await _e.Enviar(Juan, "hola?"));
    }

    [Fact]
    public async Task Una_consulta_clinica_no_se_responde()
    {
        await _e.Enviar(Juan, "hola");
        var r = await _e.Enviar(Juan, "qué hago con la ansiedad a la noche?");
        Assert.Equal(FiltroSeguridad.MensajeConsultaClinica, r);
    }

    // ── Navegación ──────────────────────────────────────────

    [Fact]
    public async Task Cero_vuelve_al_menu_desde_cualquier_paso()
    {
        await _e.Enviar(Juan, "quiero pedir un turno");
        Assert.Contains("1) Pedir turno", await _e.Enviar(Juan, "0"));
        Assert.Equal(PasoBot.Menu, _e.ConversacionDe(Juan).PasoActual);
    }

    [Fact]
    public async Task Tras_el_timeout_el_siguiente_mensaje_arranca_desde_el_menu()
    {
        await _e.Enviar(Juan, "quiero pedir un turno");                    // queda eligiendo horario
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        var r = await _e.Enviar(Juan, "1");
        Assert.Contains("volvimos al comienzo", r);
        Assert.Empty(_e.Db.Turnos);
    }

    // ── Respuestas a recordatorios ──────────────────────────

    [Fact]
    public async Task Recordatorio_responder_1_registra_la_asistencia()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(25, 8), BaseEnMemoria.Confirmado);
        _e.SimularRecordatorio(Juan, t.IdTurno);
        _e.Reloj.Avanzar(TimeSpan.FromHours(3));                           // no expira por timeout
        Assert.Contains("vas a asistir", await _e.Enviar(Juan, "1"));
        Assert.Contains((t.IdTurno, "Turno24h", "AsistenciaConfirmada"), _e.Recordatorios.Respuestas);
    }

    [Fact]
    public async Task Recordatorio_responder_2_con_menos_de_24_horas_avisa_al_profesional_sin_cancelar()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(25, 8), BaseEnMemoria.Confirmado);
        _e.SimularRecordatorio(Juan, t.IdTurno);
        Assert.Contains("Le avisamos a Santiago Manto", await _e.Enviar(Juan, "2"));
        Assert.Equal("Confirmado", _e.Db.EstadoDe(t.IdTurno));
        Assert.Contains($"NoAsistencia:{t.IdTurno}", _e.Notificador.Eventos);
        Assert.Contains((t.IdTurno, "Turno24h", "NoAsistira"), _e.Recordatorios.Respuestas);
    }

    [Fact]
    public async Task Recordatorio_responder_2_con_mas_de_24_horas_cancela()
    {
        var t = _e.Db.AgregarTurno(1, 10, Dia(26, 10), BaseEnMemoria.Confirmado);
        _e.SimularRecordatorio(Juan, t.IdTurno);
        Assert.Contains("cancelamos tu turno", await _e.Enviar(Juan, "2"));
        Assert.Equal("Cancelado", _e.Db.EstadoDe(t.IdTurno));
        Assert.Contains($"TurnoCancelado:{t.IdTurno}:True", _e.Notificador.Eventos);
    }

    [Fact]
    public async Task Mensaje_vacio_o_telefono_invalido_se_rechazan()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _e.Bot.ProcesarAsync("123", "hola", "Simulado"));
        await Assert.ThrowsAsync<ArgumentException>(() => _e.Bot.ProcesarAsync(Juan, "   ", "Simulado"));
    }
}
