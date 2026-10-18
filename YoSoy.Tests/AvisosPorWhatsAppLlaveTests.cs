using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// La llave Mensajeria:AvisosPorWhatsApp: los cuatro avisos de turno por evento (solicitud recibida, turno propuesto, confirmado y cancelado) NO salen por
// WhatsApp por defecto. El uso saliente de WhatsApp está pausado por el riesgo de bloqueo, y estos avisos son lo único que abre una conversación sin que la
// persona haya escrito.
//
// Con el NotificadorTurnos REAL, el ServicioMensajes real y un canal que anota cada envío (no sale nada de la máquina) se comprueba que:
//  · apagada (por defecto): no se manda nada, no se arma la conversación, ni siquiera se consulta el consentimiento (que es la tabla ConsentimientoWhatsApp), sea
//    cual sea la decisión del paciente; el aviso queda "Omitido" y la campanita sale igual, con su texto completo;
//  · prendida: el aviso sale como siempre, según el consentimiento (optIn solo con aceptación vigente, nada si revocó);
//  · se puede prender y apagar sin reiniciar (IOptionsMonitor).
public class AvisosPorWhatsAppLlaveTests
{
    private const string Url = "https://yosoy.test/ingreso";

    private sealed class Arnes
    {
        public readonly Escenario E = new();
        public readonly MensajesChatEnMemoria Mensajes = new();
        public readonly CanalEspia Canal = new();
        public readonly DatosConContactoDelPaciente Datos;
        public readonly RecordatoriosEspia Recordatorios = new();
        public readonly NotificadorTurnos Notificador;
        public readonly Turno Turno;

        public Arnes(int idEstado, bool llave, PermisoEnvioWhatsApp permiso = PermisoEnvioWhatsApp.SinDecision)
        {
            E.Opciones.CurrentValue.AvisosPorWhatsApp = llave;
            E.Opciones.CurrentValue.UrlIngresoTurno = Url;
            E.WhatsApp.Permisos[1] = permiso;
            E.Db.Profesionales.Single(p => p.IdProfesional == 10).Nombre = "Dra. Marta Quiroga";
            Turno = E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(2), idEstado);

            Datos = new DatosConContactoDelPaciente(new FakeDatos(E.Db), E.Db);
            var mensajes = new ServicioMensajes(Mensajes, Canal, E.Reloj, NullLogger<ServicioMensajes>.Instance);
            Notificador = new NotificadorTurnos(Datos, Recordatorios, new InternasQueNoHacenNada(), E.Conversaciones, mensajes, Canal, E.WhatsApp,
                new FakeTurnoRepositorio(E.Db), new ServiceCollection().BuildServiceProvider(), new ConfigurationBuilder().Build(), E.Opciones, E.Reloj,
                NullLogger<NotificadorTurnos>.Instance);
        }

        public DateTime MomentoDelTurno => Turno.Fecha.Date + Turno.Hora;
        public (string Canal, string Estado, string? Error) Chat => Recordatorios.Resultados.Single(r => r.Canal == "Chat");
    }

    // Cada aviso con el estado que necesita el turno.
    private static readonly (string Aviso, int IdEstado)[] Avisos =
    {
        ("solicitud", BaseEnMemoria.Pendiente),
        ("propuesta", BaseEnMemoria.Pendiente),
        ("confirmado", BaseEnMemoria.Confirmado),
        ("cancelado", BaseEnMemoria.Cancelado)
    };

    public static IEnumerable<object[]> AvisosYPermisos() =>
        from aviso in Avisos
        from permiso in Enum.GetValues<PermisoEnvioWhatsApp>()
        select new object[] { aviso.Aviso, aviso.IdEstado, permiso };

    private static void Disparar(Arnes a, string aviso)
    {
        switch (aviso)
        {
            case "solicitud": a.Notificador.TurnoSolicitado(a.Turno.IdTurno); break;
            case "propuesta": a.Notificador.TurnoAgendadoPorProfesional(1, 10, a.Turno.Fecha, a.Turno.Hora); break;
            case "confirmado": a.Notificador.TurnoConfirmado(a.Turno.IdTurno); break;
            case "cancelado": a.Notificador.TurnoCancelado(a.Turno.IdTurno); break;
            default: throw new ArgumentException(aviso);
        }
    }

    // ───────────── Apagada (por defecto) ─────────────

    [Theory]
    [MemberData(nameof(AvisosYPermisos))]
    public void Con_la_llave_apagada_no_sale_ninguno_de_los_cuatro_avisos_por_WhatsApp_sea_cual_sea_el_consentimiento(string aviso, int idEstado, PermisoEnvioWhatsApp permiso)
    {
        var a = new Arnes(idEstado, llave: false, permiso);

        Disparar(a, aviso);

        Assert.Empty(a.Canal.Envios);                       // nada llega al canal de WhatsApp
        Assert.Empty(a.Mensajes.Todos);                     // ni queda un mensaje en el chat
        Assert.Empty(a.E.Conversaciones.Todas);             // ni se armó la conversación
        Assert.Empty(a.E.WhatsApp.Consultas);               // y ni se consultó el consentimiento: la tabla ConsentimientoWhatsApp ni se toca
        Assert.Equal("Omitido", a.Chat.Estado);
        Assert.Contains("apagados", a.Chat.Error);
    }

    [Theory]
    [MemberData(nameof(AvisosYPermisos))]
    public void Con_la_llave_apagada_la_campanita_sale_igual_con_su_texto_completo(string aviso, int idEstado, PermisoEnvioWhatsApp permiso)
    {
        var a = new Arnes(idEstado, llave: false, permiso);

        Disparar(a, aviso);

        var campanita = Assert.Single(a.Datos.Campanita);
        Assert.Contains("Dra. Marta Quiroga", campanita.Mensaje);                          // la campanita no cambia: es el panel, con sesión
        Assert.Contains(Formato.FechaHora(a.MomentoDelTurno), campanita.Mensaje);
        Assert.Equal(("App", "Enviado", (string?)null), a.Recordatorios.Resultados.Single(r => r.Canal == "App"));
    }

    [Fact]
    public void Un_aviso_que_responde_el_asistente_queda_igual_de_omitido_con_la_llave_apagada()
    {
        var a = new Arnes(BaseEnMemoria.Pendiente, llave: false);

        a.Notificador.TurnoSolicitado(a.Turno.IdTurno, respondidoPorChat: true);

        Assert.Empty(a.Canal.Envios);
        Assert.Equal("Omitido", a.Chat.Estado);
        Assert.Single(a.Datos.Campanita);
    }

    // ───────────── Prendida: como hoy ─────────────

    [Fact]
    public void Con_la_llave_prendida_y_una_aceptacion_vigente_el_aviso_sale_con_optIn()
    {
        var a = new Arnes(BaseEnMemoria.Confirmado, llave: true, PermisoEnvioWhatsApp.ConConsentimiento);

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        var envio = Assert.Single(a.Canal.Envios);
        Assert.Equal(PlantillasWhatsApp.TurnoConfirmado(a.MomentoDelTurno), envio.Texto);
        Assert.True(envio.Opciones.OptIn);
        Assert.Equal("Enviado", a.Chat.Estado);
        Assert.Equal((1, Escenario.TelJuan), Assert.Single(a.E.WhatsApp.Consultas));       // y entonces sí se consulta el consentimiento
    }

    [Fact]
    public void Con_la_llave_prendida_sin_decision_sale_sin_optIn_y_decide_whatsapp_service()
    {
        var a = new Arnes(BaseEnMemoria.Confirmado, llave: true);

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.False(Assert.Single(a.Canal.Envios).Opciones.OptIn);
    }

    [Fact]
    public void Con_la_llave_prendida_si_el_paciente_revoco_no_sale_nada()
    {
        var a = new Arnes(BaseEnMemoria.Confirmado, llave: true, PermisoEnvioWhatsApp.Revocado);

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Empty(a.Canal.Envios);
        Assert.Equal("Omitido", a.Chat.Estado);
        Assert.Contains("revocó", a.Chat.Error);
    }

    [Fact]
    public void La_llave_se_prende_y_se_apaga_sin_reiniciar()
    {
        var a = new Arnes(BaseEnMemoria.Confirmado, llave: false, PermisoEnvioWhatsApp.ConConsentimiento);

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);
        Assert.Empty(a.Canal.Envios);

        a.E.Opciones.CurrentValue.AvisosPorWhatsApp = true;                                // IOptionsMonitor: el cambio se ve en el aviso siguiente
        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);
        Assert.Single(a.Canal.Envios);

        a.E.Opciones.CurrentValue.AvisosPorWhatsApp = false;
        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);
        Assert.Single(a.Canal.Envios);                                                      // no salió otro
    }

    [Fact]
    public void El_recordatorio_no_depende_de_la_llave_porque_nunca_sale_por_WhatsApp()
    {
        foreach (var llave in new[] { false, true })
        {
            var a = new Arnes(BaseEnMemoria.Confirmado, llave, PermisoEnvioWhatsApp.ConConsentimiento);

            a.Notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

            Assert.Empty(a.Canal.Envios);
            Assert.DoesNotContain(a.Recordatorios.Resultados, r => r.Canal == "Chat");
            Assert.Single(a.Datos.Campanita);
        }
    }

    // ───────────── La configuración ─────────────

    [Fact]
    public void Por_defecto_la_llave_esta_apagada_en_las_opciones_y_escrita_asi_en_appsettings()
    {
        Assert.False(new MensajeriaOptions().AvisosPorWhatsApp);

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var llave = json.RootElement.GetProperty("Mensajeria").GetProperty("AvisosPorWhatsApp");
        Assert.Equal(JsonValueKind.False, llave.ValueKind);                                 // escrita a propósito, no solo el valor por defecto del código
    }

    [Fact]
    public void La_variable_Mensajeria__AvisosPorWhatsApp_prende_la_llave()
    {
        // Así llega del .env del servidor: Mensajeria__AvisosPorWhatsApp=true es la clave Mensajeria:AvisosPorWhatsApp.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mensajeria:AvisosPorWhatsApp"] = "true" }).Build();

        Assert.True(config.GetSection(MensajeriaOptions.Seccion).Get<MensajeriaOptions>()!.AvisosPorWhatsApp);
        Assert.False(new ConfigurationBuilder().Build().GetSection(MensajeriaOptions.Seccion).Get<MensajeriaOptions>() is { AvisosPorWhatsApp: true });
    }

    [Fact]
    public void La_aplicacion_real_arranca_con_la_llave_apagada()
    {
        using var app = new VideoApp();

        var opciones = app.Factory.Services.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue;

        Assert.False(opciones.AvisosPorWhatsApp);
    }
}
