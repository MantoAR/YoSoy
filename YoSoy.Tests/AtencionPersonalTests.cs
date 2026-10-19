// El chat para el personal (Mensajeria:ModoAtencion = "Personal"): AtencionPersonalService y la puerta RecepcionDeMensajes, en memoria (sin base ni red).
//
// Se arman el ServicioMensajes REAL (con el historial en memoria y un canal que anota cada envío), el filtro de seguridad real y los dobles de siempre: lo que se prueba
// es lo que corre. Lo que tiene que pasar:
//  · cada mensaje se guarda y NO se le contesta nada al paciente (ni el menú, ni el pedido del DNI, ni un "no te entendí"): lo atiende una persona;
//  · la conversación queda para el Configurador ("DerivadaHumano") y se avisa "chat nuevo" en la campanita una vez por tanda de mensajes;
//  · una frase de riesgo la marca "Urgente" y avisa, pero el mensaje de emergencia al paciente viene APAGADO (decisión D3) y, aunque se lo prenda, no sale mientras
//    alguna línea de emergencia esté sin completar.
public class AtencionPersonalTests
{
    private const string Telefono1 = "11 5555-0001";
    private static readonly string Telefono1Normalizado = Telefono.Normalizar(Telefono1);

    private sealed class Arnes
    {
        public readonly RelojFijo Reloj = new(Escenario.Jueves0900);
        public readonly OpcionesFijas Opciones = new();
        public readonly FakeConversaciones Conversaciones;
        public readonly MensajesChatEnMemoria Historial = new();
        public readonly CanalEspia Canal = new();
        public readonly FakeNotificador Notificador = new();
        public readonly FakePoliticaWhatsApp Bajas = new();
        public readonly LogsCapturados Logs = new();
        public readonly AtencionPersonalService Personal;

        public Arnes()
        {
            var op = Opciones.CurrentValue;
            op.ModoAtencion      = ModosAtencion.Personal;
            op.PalabrasRiesgo    = new() { "suicid", "no quiero vivir" };
            op.PalabrasClinicas  = new() { "ansiedad" };
            op.LineasEmergencia  = new() { new LineaEmergencia { Nombre = "Emergencias", Telefono = "[COMPLETAR Y VALIDAR]" } };   // como en appsettings.json
            Conversaciones = new FakeConversaciones(Reloj);
            var mensajes = new ServicioMensajes(Historial, Canal, Reloj, Logs.CreateLogger<ServicioMensajes>());
            Personal = new AtencionPersonalService(Conversaciones, mensajes, Historial, new FiltroSeguridad(Opciones), Notificador, Bajas, Opciones, Reloj,
                Logs.CreateLogger<AtencionPersonalService>());
        }

        public Task<RespuestaBot> Escribe(string texto, string telefono = Telefono1) => Personal.ProcesarAsync(telefono, texto, "Prueba");

        public Conversacion Conversacion(string telefono = Telefono1) => Conversaciones.ObtenerPorTelefono(Telefono.Normalizar(telefono), "Prueba")!;

        public int Avisos(string tipo) => Notificador.Eventos.Count(e => e.StartsWith(tipo + ":"));

        public void PrendeLaEmergencia(params (string Nombre, string Telefono)[] lineas)
        {
            Opciones.CurrentValue.Personal.RespuestaDeEmergencia = true;
            Opciones.CurrentValue.LineasEmergencia = lineas.Select(l => new LineaEmergencia { Nombre = l.Nombre, Telefono = l.Telefono }).ToList();
        }
    }

    // ───────────── Se guarda y no se contesta ─────────────

    [Fact]
    public async Task Un_mensaje_nuevo_se_guarda_y_no_se_le_contesta_nada_al_paciente()
    {
        var a = new Arnes();

        var r = await a.Escribe("hola, quería consultar por un turno");

        Assert.Empty(r.Respuestas);
        Assert.Empty(a.Canal.Envios);                                           // nada sale por el canal
        var guardado = Assert.Single(a.Historial.Todos);                        // y solo está lo que escribió la persona
        Assert.Equal(("Entrante", "Paciente", "hola, quería consultar por un turno"), (guardado.Direccion, guardado.Origen, guardado.Texto));
        Assert.Equal(a.Conversacion().IdConversacion, r.IdConversacion);
    }

    [Theory]
    [InlineData("hola")]
    [InlineData("menu")]
    [InlineData("1")]
    [InlineData("quiero pedir un turno")]
    [InlineData("30123456")]                    // un DNI: el asistente lo pediría; acá nadie lo pide
    [InlineData("tengo mucha ansiedad")]        // una consulta clínica: el asistente aclararía que no responde eso; acá no contesta nadie
    [InlineData("asdf qwer")]                   // lo que el asistente no entiende
    public async Task El_asistente_no_contesta_nada_sea_lo_que_sea_que_escriba_la_persona(string texto)
    {
        var a = new Arnes();

        var r = await a.Escribe(texto);

        Assert.Empty(r.Respuestas);
        Assert.Empty(a.Canal.Envios);
        Assert.DoesNotContain(a.Historial.Todos, m => m.Direccion == "Saliente");
        Assert.Equal("Inicio", a.Conversacion().PasoActual);                    // el flujo del asistente ni se toca
        Assert.Null(a.Conversacion().DatosContexto);
        Assert.Null(a.Conversacion().IdPaciente);
    }

    [Fact]
    public async Task Todos_los_mensajes_de_la_charla_se_guardan_en_orden()
    {
        var a = new Arnes();

        await a.Escribe("hola");
        await a.Escribe("necesito hablar con alguien");
        await a.Escribe("gracias");

        Assert.Equal(new[] { "hola", "necesito hablar con alguien", "gracias" }, a.Historial.Todos.Select(m => m.Texto));
        Assert.All(a.Historial.Todos, m => Assert.Equal(("Entrante", "Paciente"), (m.Direccion, m.Origen)));
        Assert.Empty(a.Canal.Envios);
    }

    [Fact]
    public async Task Un_mensaje_largo_se_guarda_recortado_a_4000_como_siempre()
    {
        var a = new Arnes();

        await a.Escribe(new string('a', 5000));

        Assert.Equal(4000, Assert.Single(a.Historial.Todos).Texto.Length);
    }

    // ───────────── La conversación queda para el Configurador ─────────────

    [Fact]
    public async Task La_conversacion_queda_para_atender_y_sin_dueno()
    {
        var a = new Arnes();

        await a.Escribe("hola");

        var c = a.Conversacion();
        Assert.Equal(EstadoConversacion.DerivadaHumano, c.Estado);
        Assert.Null(c.IdProfesionalAsignado);                                   // los profesionales no ven chats: no hay a quién asignarla (D2)
        Assert.Equal(Escenario.Jueves0900, c.FechaUltimaActividad);
    }

    [Fact]
    public async Task Una_conversacion_cerrada_vuelve_a_quedar_para_atender_cuando_la_persona_escribe()
    {
        var a = new Arnes();
        await a.Escribe("hola");
        var c = a.Conversacion();
        c.Estado = EstadoConversacion.Cerrada;
        a.Conversaciones.Guardar(c);

        var r = await a.Escribe("perdón, una cosa más");

        Assert.Equal(EstadoConversacion.DerivadaHumano, a.Conversacion().Estado);
        Assert.Empty(r.Respuestas);                                             // el asistente NO retoma la charla desde el menú, como haría en el modo Asistente
        Assert.Empty(a.Canal.Envios);
    }

    [Fact]
    public async Task Una_conversacion_urgente_sigue_urgente_con_un_mensaje_comun()
    {
        var a = new Arnes();
        await a.Escribe("no quiero vivir");
        Assert.Equal(EstadoConversacion.Urgente, a.Conversacion().Estado);

        await a.Escribe("sigo acá");

        Assert.Equal(EstadoConversacion.Urgente, a.Conversacion().Estado);     // la resuelve el equipo, no un mensaje común
    }

    [Fact]
    public async Task Cada_telefono_tiene_su_propia_conversacion()
    {
        var a = new Arnes();

        await a.Escribe("hola", "11 5555-0001");
        await a.Escribe("hola", "11 5555-0002");

        Assert.Equal(2, a.Conversaciones.Todas.Count);
        Assert.Equal(2, a.Avisos("ChatNuevo"));
    }

    // ───────────── "Chat nuevo" en la campanita: una vez por tanda de mensajes ─────────────

    [Fact]
    public async Task El_primer_mensaje_avisa_chat_nuevo_en_la_campanita()
    {
        var a = new Arnes();

        await a.Escribe("hola");

        Assert.Equal(new[] { $"ChatNuevo:{a.Conversacion().IdConversacion}" }, a.Notificador.Eventos);
    }

    [Fact]
    public async Task Los_mensajes_de_una_charla_en_curso_no_vuelven_a_avisar()
    {
        var a = new Arnes();

        await a.Escribe("hola");
        a.Reloj.Avanzar(TimeSpan.FromMinutes(2));
        await a.Escribe("sigo acá");
        a.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        await a.Escribe("¿hay alguien?");
        a.Reloj.Avanzar(TimeSpan.FromMinutes(29));          // 29 minutos desde el anterior: todavía es la misma charla
        await a.Escribe("hola?");

        Assert.Equal(1, a.Avisos("ChatNuevo"));
    }

    [Fact]
    public async Task Despues_de_media_hora_sin_actividad_el_mensaje_vuelve_a_avisar()
    {
        var a = new Arnes();
        await a.Escribe("hola");

        a.Reloj.Avanzar(AtencionPersonalService.SilencioParaAvisar);   // justo media hora
        await a.Escribe("buenas, de nuevo");

        Assert.Equal(2, a.Avisos("ChatNuevo"));
    }

    [Fact]
    public async Task El_silencio_se_cuenta_desde_la_ultima_actividad_no_desde_el_primer_mensaje()
    {
        var a = new Arnes();
        await a.Escribe("hola");
        a.Reloj.Avanzar(TimeSpan.FromMinutes(20));
        await a.Escribe("sigo acá");                         // actividad a los 20 minutos
        a.Reloj.Avanzar(TimeSpan.FromMinutes(20));           // 40 desde el primero pero 20 desde el último

        await a.Escribe("ahí estoy");

        Assert.Equal(1, a.Avisos("ChatNuevo"));
    }

    [Fact]
    public async Task Una_conversacion_cerrada_vuelve_a_avisar_aunque_haya_pasado_poco_tiempo()
    {
        var a = new Arnes();
        await a.Escribe("hola");
        var c = a.Conversacion();
        c.Estado = EstadoConversacion.Cerrada;
        a.Conversaciones.Guardar(c);
        a.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        await a.Escribe("una cosa más");

        Assert.Equal(2, a.Avisos("ChatNuevo"));
    }

    [Fact]
    public async Task El_aviso_de_chat_nuevo_no_se_manda_si_el_mensaje_es_una_urgencia()
    {
        var a = new Arnes();

        await a.Escribe("no quiero vivir");

        Assert.Equal(0, a.Avisos("ChatNuevo"));                 // la urgencia ya avisa por su cuenta: no se duplica
        Assert.Equal(1, a.Avisos("UrgenciaSinRespuesta"));
    }

    // ───────────── Frase de riesgo y mensaje de emergencia (D3) ─────────────

    [Fact]
    public async Task Una_frase_de_riesgo_marca_urgente_y_avisa_pero_por_defecto_no_le_manda_nada_al_paciente()
    {
        var a = new Arnes();   // la emergencia viene apagada y las lineas dicen [COMPLETAR Y VALIDAR], como en appsettings.json

        var r = await a.Escribe("a veces pienso en el suicidio");

        Assert.Equal(EstadoConversacion.Urgente, a.Conversacion().Estado);
        Assert.Empty(r.Respuestas);
        Assert.Empty(a.Canal.Envios);
        Assert.Equal(new[] { $"UrgenciaSinRespuesta:{a.Conversacion().IdConversacion}" }, a.Notificador.Eventos);
        Assert.DoesNotContain(a.Logs.Lineas, l => l.Contains("sin completar"));      // apagada: no es un problema que avisar
    }

    [Fact]
    public async Task Con_la_emergencia_apagada_las_lineas_completas_no_alcanzan_para_que_salga()
    {
        var a = new Arnes();
        a.Opciones.CurrentValue.LineasEmergencia = new() { new LineaEmergencia { Nombre = "Emergencias", Telefono = "911" } };   // completas, pero la llave está apagada

        var r = await a.Escribe("no quiero vivir");

        Assert.Empty(r.Respuestas);
        Assert.Empty(a.Canal.Envios);
        Assert.Equal(1, a.Avisos("UrgenciaSinRespuesta"));
    }

    [Fact]
    public async Task Con_la_emergencia_prendida_y_las_lineas_completas_sale_el_mensaje_con_las_lineas()
    {
        var a = new Arnes();
        a.PrendeLaEmergencia(("Emergencias", "911"), ("Línea de salud mental", "0800-999-0091"));

        var r = await a.Escribe("no quiero vivir");

        var mensaje = Assert.Single(r.Respuestas);
        Assert.Contains("• Emergencias: 911", mensaje);
        Assert.Contains("• Línea de salud mental: 0800-999-0091", mensaje);
        Assert.Equal(mensaje, Assert.Single(a.Canal.Envios).Texto);             // y sale de verdad por el canal
        Assert.Equal(("Saliente", "Bot"), (a.Historial.Todos.Last().Direccion, a.Historial.Todos.Last().Origen));
        Assert.Equal(EstadoConversacion.Urgente, a.Conversacion().Estado);
        Assert.Equal(1, a.Avisos("Urgencia"));                                  // el aviso dice que SÍ se respondió
        Assert.Equal(0, a.Avisos("UrgenciaSinRespuesta"));
        Assert.DoesNotContain(a.Logs.Lineas, l => l.Contains("sin completar"));
    }

    [Theory]
    [InlineData("Emergencias", "[COMPLETAR Y VALIDAR]")]
    [InlineData("Emergencias", "[completar y validar]")]
    [InlineData("Emergencias", "")]
    [InlineData("Emergencias", "   ")]
    [InlineData("[COMPLETAR]", "911")]
    public async Task Con_la_emergencia_prendida_pero_una_linea_sin_completar_no_sale_y_el_log_lo_avisa(string nombre, string telefono)
    {
        var a = new Arnes();
        a.PrendeLaEmergencia(("Emergencias", "911"), (nombre, telefono));       // una línea completa y otra sin completar: alcanza con una mala

        var r = await a.Escribe("no quiero vivir");

        Assert.Empty(r.Respuestas);
        Assert.Empty(a.Canal.Envios);
        Assert.Equal(EstadoConversacion.Urgente, a.Conversacion().Estado);       // el equipo se entera igual
        Assert.Equal(1, a.Avisos("UrgenciaSinRespuesta"));
        Assert.Contains(a.Logs.Lineas, l => l.Contains("sin completar") && l.Contains("Mensajeria:LineasEmergencia"));
    }

    [Fact]
    public async Task Con_la_emergencia_prendida_y_sin_ninguna_linea_no_sale_y_el_log_lo_avisa()
    {
        var a = new Arnes();
        a.PrendeLaEmergencia();

        var r = await a.Escribe("no quiero vivir");

        Assert.Empty(r.Respuestas);
        Assert.Empty(a.Canal.Envios);
        Assert.Contains(a.Logs.Lineas, l => l.Contains("sin completar"));
    }

    [Fact]
    public async Task El_log_nunca_lleva_el_texto_del_paciente_ni_su_telefono()
    {
        var a = new Arnes();
        a.PrendeLaEmergencia(("Emergencias", "[COMPLETAR Y VALIDAR]"));

        await a.Escribe("no quiero vivir, me llamo Juan Pérez y mi teléfono es 1155550001");

        Assert.DoesNotContain("no quiero vivir", a.Logs.Todo);
        Assert.DoesNotContain("Juan", a.Logs.Todo);
        Assert.DoesNotContain("1155550001", a.Logs.Todo);
        Assert.DoesNotContain("5555", a.Logs.Todo);
    }

    [Theory]
    [InlineData("[COMPLETAR Y VALIDAR]", true)]
    [InlineData("[completar]", true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("911", false)]
    [InlineData("0800-999-0091", false)]
    [InlineData("+54 9 11 5555-0000", false)]
    public void Una_linea_sin_telefono_o_con_el_texto_provisorio_esta_sin_completar(string telefono, bool sinCompletar) =>
        Assert.Equal(sinCompletar, AtencionPersonalService.LineaSinCompletar(new LineaEmergencia { Nombre = "Emergencias", Telefono = telefono }));

    // ───────────── BAJA ─────────────

    [Fact]
    public async Task Un_pedido_de_BAJA_se_registra_y_el_equipo_lo_ve_como_cualquier_otro_mensaje()
    {
        var a = new Arnes();

        var r = await a.Escribe("BAJA");

        Assert.Equal(new[] { Telefono1Normalizado }, a.Bajas.Bajas);            // se revoca la aceptación de los avisos (si la llave está prendida)
        Assert.Empty(r.Respuestas);
        Assert.Empty(a.Canal.Envios);
        Assert.Equal("BAJA", Assert.Single(a.Historial.Todos).Texto);           // y queda en el historial
        Assert.Equal(EstadoConversacion.DerivadaHumano, a.Conversacion().Estado);
        Assert.Equal(1, a.Avisos("ChatNuevo"));                                 // a diferencia del modo Asistente, el equipo se entera
    }

    [Theory]
    [InlineData("el turno se dio de baja")]
    [InlineData("quiero darme de baja del turno del jueves")]
    public async Task Una_frase_que_solo_contiene_la_palabra_baja_no_es_un_pedido_de_baja(string texto)
    {
        var a = new Arnes();

        await a.Escribe(texto);

        Assert.Empty(a.Bajas.Bajas);
    }

    // ───────────── Entradas inválidas ─────────────

    [Theory]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData("abc")]
    public async Task Un_telefono_demasiado_corto_o_sin_numeros_es_un_ArgumentException(string telefono)
    {
        var a = new Arnes();

        await Assert.ThrowsAsync<ArgumentException>(() => a.Escribe("hola", telefono));

        Assert.Empty(a.Historial.Todos);
        Assert.Empty(a.Conversaciones.Todas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Un_mensaje_vacio_es_un_ArgumentException_y_no_deja_nada(string texto)
    {
        var a = new Arnes();

        await Assert.ThrowsAsync<ArgumentException>(() => a.Escribe(texto));

        Assert.Empty(a.Historial.Todos);
        Assert.Empty(a.Notificador.Eventos);
    }

    // ───────────── RecepcionDeMensajes: quién atiende, según el modo del momento ─────────────

    [Fact]
    public async Task La_puerta_elige_quien_atiende_segun_el_modo_y_se_puede_cambiar_sin_reiniciar()
    {
        var esc = new Escenario();                       // el asistente de siempre, con sus dobles
        esc.Opciones.CurrentValue.ModoAtencion = ModosAtencion.Asistente;
        var a = new Arnes();
        var personal = new AtencionPersonalService(a.Conversaciones, new ServicioMensajes(a.Historial, a.Canal, a.Reloj, a.Logs.CreateLogger<ServicioMensajes>()),
            a.Historial, new FiltroSeguridad(esc.Opciones), a.Notificador, a.Bajas, esc.Opciones, a.Reloj, a.Logs.CreateLogger<AtencionPersonalService>());
        var puerta = new RecepcionDeMensajes(esc.Bot, personal, esc.Opciones);

        // Asistente (el modo de siempre): contesta el asistente, con su menú.
        var comoAsistente = await puerta.ProcesarAsync("11 5555-0001", "hola", "Simulado");
        Assert.NotEmpty(comoAsistente.Respuestas);
        Assert.Empty(a.Historial.Todos);                // y el servicio del personal ni se enteró

        // El mismo objeto, otro valor (como un cambio del archivo en caliente): ahora atiende el personal.
        esc.Opciones.CurrentValue.ModoAtencion = ModosAtencion.Personal;
        var comoPersonal = await puerta.ProcesarAsync("11 5555-0002", "hola", "Prueba");
        Assert.Empty(comoPersonal.Respuestas);
        Assert.Single(a.Historial.Todos);

        // Y de vuelta.
        esc.Opciones.CurrentValue.ModoAtencion = ModosAtencion.Asistente;
        Assert.NotEmpty((await puerta.ProcesarAsync("11 5555-0003", "hola", "Simulado")).Respuestas);
    }

    [Fact]
    public async Task Un_valor_invalido_en_caliente_atiende_el_asistente_como_siempre()
    {
        var esc = new Escenario();
        esc.Opciones.CurrentValue.ModoAtencion = "Personl";    // un error de tipeo en el archivo: no apaga al asistente
        var a = new Arnes();
        var puerta = new RecepcionDeMensajes(esc.Bot, a.Personal, esc.Opciones);

        var r = await puerta.ProcesarAsync("11 5555-0001", "hola", "Simulado");

        Assert.NotEmpty(r.Respuestas);
        Assert.Empty(a.Historial.Todos);
    }
}
