using Microsoft.Extensions.Options;

/// <summary>
/// Servicio único de avisos automáticos:
///  - Por evento (solicitud recibida, turno agendado por el profesional, confirmado,
///    cancelado): chat (canal activo) + campanita del paciente (Notificacion).
///  - Recordatorio 24 h (lo dispara RecordatorioBackgroundService): mail + campanita. NUNCA por
///    WhatsApp: el uso saliente de WhatsApp está pausado y un recordatorio es lo programado y masivo.
///  - Avisos internos (urgencia, derivación, paciente que no asistirá): NotificacionInterna
///    y, para el profesional, mail si tiene uno cargado.
/// Cada envío a un paciente queda registrado en Recordatorio; el índice único
/// (IdTurno, Tipo, Canal) evita mandar dos veces el mismo aviso.
/// Ningún método público lanza: los errores se registran en el log y en Recordatorio.
/// Los estados se resuelven por Descripcion, nunca por id.
/// </summary>
public class NotificadorTurnos : INotificadorTurnos
{
    private static readonly string[] CanalesEvento = { "Chat", "App" };

    // El recordatorio de 24 h sale SOLO por mail y campanita: nunca por WhatsApp ("Chat"). Es una decisión de producto (el uso saliente de WhatsApp está pausado por el
    // riesgo de bloqueo del número, y un recordatorio es justo lo programado y masivo) y no hay ninguna clave de configuración que la cambie. Es una lista de solo lectura:
    // nadie le agrega "Chat" desde afuera. RecordatoriosSinWhatsAppTests lo vigila de punta a punta, con el canal de WhatsApp configurado.
    public static readonly IReadOnlyList<string> CanalesRecordatorio = new[] { "Email", "App" };

    private readonly IDatosMensajeria _datos;
    private readonly IRecordatorioDAO _recordatorios;
    private readonly INotificacionInternaDAO _internas;
    private readonly IConversacionDAO _conversaciones;
    private readonly IServicioMensajes _mensajes;
    private readonly IMessageChannel _canal;
    private readonly IPoliticaEnvioWhatsApp _politicaWhatsApp;
    private readonly ITurnoRepositorio _turnos;
    private readonly IServiceProvider _servicios;
    private readonly IConfiguration _config;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<NotificadorTurnos> _log;

    public NotificadorTurnos(
        IDatosMensajeria datos, IRecordatorioDAO recordatorios, INotificacionInternaDAO internas,
        IConversacionDAO conversaciones, IServicioMensajes mensajes, IMessageChannel canal, IPoliticaEnvioWhatsApp politicaWhatsApp,
        ITurnoRepositorio turnos, IServiceProvider servicios, IConfiguration config,
        IOptionsMonitor<MensajeriaOptions> opciones, TimeProvider reloj, ILogger<NotificadorTurnos> log)
    {
        _datos          = datos;
        _recordatorios  = recordatorios;
        _internas       = internas;
        _conversaciones = conversaciones;
        _mensajes       = mensajes;
        _canal          = canal;
        _politicaWhatsApp = politicaWhatsApp;
        _turnos         = turnos;
        _servicios      = servicios;
        _config         = config;
        _opciones       = opciones;
        _reloj          = reloj;
        _log            = log;
    }

    private DateTime Ahora => _reloj.GetLocalNow().DateTime;
    private MensajeriaOptions Op => _opciones.CurrentValue;

    // ── Eventos de turno ────────────────────────────────────────

    // Cada aviso tiene DOS textos: el del chat de WhatsApp (PlantillasWhatsApp: solo el día y la hora, nada clínico) y el de la campanita del panel
    // (con sesión, con el nombre del profesional). El chat no recibe nada del turno salvo su momento (ver PlantillasWhatsApp).
    public void TurnoSolicitado(int idTurno, bool respondidoPorChat = false) => Seguro(nameof(TurnoSolicitado), () =>
        AvisarPaciente(idTurno, "TurnoSolicitado", CanalesEvento, respondidoPorChat,
            "Solicitud de turno recibida",
            textoChat: t => PlantillasWhatsApp.SolicitudRecibida(t.Momento),
            textoApp: t => $"Recibimos tu solicitud de turno con {t.NombreProfesional} para el {Formato.FechaHora(t.Momento)}. " +
                           "Queda pendiente hasta que tu profesional la confirme."));

    // POST /turnos/solicitar no devuelve el id: se busca por sus datos.
    public void TurnoSolicitado(Turno t) => Seguro(nameof(TurnoSolicitado), () =>
    {
        var id = _datos.BuscarIdTurno(t.IdPaciente, t.IdProfesional, t.Fecha, t.Hora);
        if (id is null) { _log.LogWarning("No se encontró el turno recién solicitado para avisar al paciente."); return; }
        TurnoSolicitado(id.Value);
    });

    public void TurnoAgendadoPorProfesional(int idPaciente, int idProfesional, DateTime fecha, TimeSpan hora) =>
        Seguro(nameof(TurnoAgendadoPorProfesional), () =>
        {
            var id = _datos.BuscarIdTurno(idPaciente, idProfesional, fecha, hora);
            if (id is null) { _log.LogWarning("No se encontró el turno agendado por el profesional para avisar al paciente."); return; }
            AvisarPaciente(id.Value, "TurnoAgendado", CanalesEvento, false,
                "Tu profesional te propuso un turno",
                textoChat: t => PlantillasWhatsApp.TurnoPropuesto(t.Momento, Op.UrlIngresoTurno),
                textoApp: t => $"{t.NombreProfesional} te propuso un turno para el {Formato.FechaHora(t.Momento)}. " +
                               $"Para confirmarlo, ingresá a tu panel en la web: {Op.UrlIngresoTurno}");
        });

    public void TurnoConfirmado(int idTurno) => Seguro(nameof(TurnoConfirmado), () =>
        AvisarPaciente(idTurno, "TurnoConfirmado", CanalesEvento, false,
            "Turno confirmado",
            textoChat: t => PlantillasWhatsApp.TurnoConfirmado(t.Momento),
            textoApp: t => $"Tu turno con {t.NombreProfesional} del {Formato.FechaHora(t.Momento)} quedó confirmado.",
            estadoEsperado: "Confirmado"));

    public void TurnoCancelado(int idTurno, bool respondidoPorChat = false) => Seguro(nameof(TurnoCancelado), () =>
        AvisarPaciente(idTurno, "TurnoCancelado", CanalesEvento, respondidoPorChat,
            "Turno cancelado",
            textoChat: t => PlantillasWhatsApp.TurnoCancelado(t.Momento),
            textoApp: t => $"Tu turno con {t.NombreProfesional} del {Formato.FechaHora(t.Momento)} fue cancelado. " +
                           "Si querés pedir otro, escribí \"menu\" por acá o ingresá a la web.",
            estadoEsperado: "Cancelado"));

    // PUT /turnos/{id}: el profesional confirma o rechaza mandando el turno entero.
    public void CambioDeEstado(int idTurno, int idEstadoAnterior) => Seguro(nameof(CambioDeEstado), () =>
    {
        var anterior = _turnos.DescripcionEstado(idEstadoAnterior);
        var actual   = _datos.ObtenerTurnoAviso(idTurno)?.Estado;
        if (actual is null || actual == anterior) return;
        if (actual == "Confirmado") TurnoConfirmado(idTurno);
        else if (actual == "Cancelado") TurnoCancelado(idTurno);
    });

    // ── Recordatorio 24 h ───────────────────────────────────────

    public void EnviarRecordatorio24h(int idTurno) => Seguro(nameof(EnviarRecordatorio24h), () =>
        AvisarPaciente(idTurno, "Turno24h", CanalesRecordatorio, false,
            "Recordatorio de turno",
            textoChat: null,   // no hay texto de WhatsApp: el recordatorio no sale por ahí
            textoApp: t => TextoRecordatorio(t) +
                "\n\nSi no vas a poder asistir, avisanos desde la web.",
            estadoEsperado: "Confirmado"));

    // El del mail y la campanita (con sesión): lleva con quién es el turno y cómo es.
    private string TextoRecordatorio(TurnoAviso t) =>
        $"Recordatorio: tenés turno el {Formato.FechaHora(t.Momento)} con {t.NombreProfesional} " +
        $"({t.Modalidad ?? "Virtual"}).\nPara ingresar: {Op.UrlIngresoTurno}";

    // ── Avisos internos ─────────────────────────────────────────

    public void AvisarUrgencia(Conversacion c, string textoPaciente) => Seguro(nameof(AvisarUrgencia), () =>
    {
        var quien = QuienEs(c);
        var extracto = textoPaciente.Length > 300 ? textoPaciente[..300] + "…" : textoPaciente;
        _internas.Insertar(new NotificacionInterna
        {
            Destinatario   = "Administrador",
            IdConversacion = c.IdConversacion,
            Tipo           = "Urgencia",
            Titulo         = "URGENTE: posible situación de riesgo en el chat",
            Mensaje        = $"Conversación #{c.IdConversacion} ({quien}). Mensaje: «{extracto}». " +
                             "El bot respondió con el mensaje de emergencia y quedó en pausa."
        });
    });

    public void AvisarDerivacion(Conversacion c) => Seguro(nameof(AvisarDerivacion), () =>
        _internas.Insertar(new NotificacionInterna
        {
            Destinatario   = "Administrador",
            IdConversacion = c.IdConversacion,
            Tipo           = "DerivacionHumano",
            Titulo         = "Un paciente pidió hablar con una persona",
            Mensaje        = $"Conversación #{c.IdConversacion} ({QuienEs(c)}). El bot quedó en pausa hasta que se cierre la derivación."
        }));

    public void AvisarNoAsistencia(int idTurno) => Seguro(nameof(AvisarNoAsistencia), () =>
    {
        var t = _datos.ObtenerTurnoAviso(idTurno);
        if (t is null) return;
        var titulo  = "Un paciente avisó que no va a asistir";
        var mensaje = $"{t.NombrePaciente} avisó por chat que no va a poder asistir al turno del {Formato.FechaHora(t.Momento)}.";
        _internas.Insertar(new NotificacionInterna
        {
            Destinatario  = "Profesional",
            IdProfesional = t.IdProfesional,
            IdTurno       = t.IdTurno,
            Tipo          = "PacienteNoAsistira",
            Titulo        = titulo,
            Mensaje       = mensaje
        });
        // El mail sale en segundo plano para no demorar la respuesta del chat.
        if (!string.IsNullOrWhiteSpace(t.MailProfesional))
            _ = Task.Run(() => Seguro("MailNoAsistencia", () =>
            {
                var (estado, error) = EnviarMail(t.MailProfesional, "YoSoy – " + titulo, mensaje);
                if (estado == "Error") _log.LogWarning("No se pudo mandar el mail al profesional {IdProfesional}: {Error}", t.IdProfesional, error);
            }));
    });

    // ── Envío a pacientes ───────────────────────────────────────

    // textoChat: lo que sale por WhatsApp (siempre una plantilla de PlantillasWhatsApp), o null si el aviso no sale por WhatsApp. textoApp: lo que va a la
    // campanita del panel y al mail.
    private void AvisarPaciente(int idTurno, string tipo, IReadOnlyList<string> canales, bool respondidoPorChat, string titulo,
        Func<TurnoAviso, string>? textoChat, Func<TurnoAviso, string> textoApp, string? estadoEsperado = null)
    {
        var t = _datos.ObtenerTurnoAviso(idTurno);
        if (t is null) return;
        if (estadoEsperado != null && t.Estado != estadoEsperado) return;

        var ahora = Ahora;
        foreach (var canal in canales)
        {
            var idRecordatorio = _recordatorios.Reservar(idTurno, tipo, canal, ahora, Op.MaxIntentosEnvio);
            if (idRecordatorio is null) continue;   // ya enviado, omitido o sin intentos

            string estado; string? error;
            try
            {
                var cuerpo = canal == "Chat" ? textoChat?.Invoke(t) ?? "" : textoApp(t);
                (estado, error) = canal switch
                {
                    "Chat" when textoChat is null => ("Omitido", "Este aviso no sale por WhatsApp."),
                    "Chat" when respondidoPorChat => ("Omitido", "El bot ya se lo respondió en la conversación."),
                    "Chat"  => EnviarChat(t, cuerpo),
                    "Email" => string.IsNullOrWhiteSpace(t.MailPaciente)
                                   ? ("Omitido", "El paciente no tiene mail cargado.")
                                   : EnviarMail(t.MailPaciente, "YoSoy – " + titulo, cuerpo),
                    "App"   => Campanita(t, titulo, cuerpo),
                    _       => ("Omitido", $"Canal desconocido: {canal}")
                };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error enviando el aviso {Tipo} del turno {IdTurno} por {Canal}", tipo, idTurno, canal);
                (estado, error) = ("Error", ex.Message);
            }
            _recordatorios.MarcarResultado(idRecordatorio.Value, estado, error, ahora);
        }
    }

    private (string, string?) EnviarChat(TurnoAviso t, string texto)
    {
        // Consentimiento de WhatsApp (ConsentimientoWhatsApp): si el paciente revocó, no se manda nada, y ni se arma la conversación. Con una
        // aceptación vigente (de este número y de este texto) el servicio de WhatsApp recibe optIn:true; sin decisión, no recibe optIn y
        // decide él (solo le escribe a quien le escribió primero al número de la clínica).
        var permiso = _politicaWhatsApp.Evaluar(t.IdPaciente, t.TelefonoPaciente);
        if (permiso == PermisoEnvioWhatsApp.Revocado) return ("Omitido", "El paciente revocó los avisos por WhatsApp.");

        // El número nacional de 10 dígitos si se lo entiende como teléfono argentino ("011 15 5555-0001" es 1155550001); si no, lo de
        // siempre (los últimos 10 dígitos).
        var telefono = TelefonoArgentino.Normalizar(t.TelefonoPaciente) ?? Telefono.Normalizar(t.TelefonoPaciente);
        if (telefono.Length < 6) return ("Omitido", "El paciente no tiene teléfono cargado.");

        var c = _conversaciones.ObtenerOCrear(telefono, _canal.Nombre);
        c.IdPaciente ??= t.IdPaciente;

        c.FechaUltimaActividad = Ahora;
        _conversaciones.Guardar(c);

        // IniciaElNegocio: un aviso de turno no responde a nada que la persona haya escrito (a diferencia de las respuestas del asistente y los
        // avisos de cerrar y reabrir, que ServicioMensajes solo deja salir dentro de una conversación que ella empezó). Lo acota el consentimiento.
        var m = _mensajes.EnviarAsync(c, texto, "Sistema", new OpcionesEnvio(OptIn: permiso == PermisoEnvioWhatsApp.ConConsentimiento, IniciaElNegocio: true))
            .GetAwaiter().GetResult();
        if (m.EstadoEnvio == "Enviado") return ("Enviado", null);
        // whatsapp-service rechazó el aviso porque la persona no aceptó recibir WhatsApp (o pidió la baja): no es una falla ni se reintenta.
        if (m.CodigoDeError is "sin_consentimiento" or "baja")
            return ("Omitido", "La persona no aceptó recibir WhatsApp o pidió la baja.");
        return ("Error", "El canal no pudo entregar el mensaje.");
    }

    private (string, string?) Campanita(TurnoAviso t, string titulo, string texto)
    {
        // Link en null a propósito: en la campanita el link es un código de sala
        // y el panel del paciente lo muestra como botón "Unirse".
        _datos.CrearNotificacionPaciente(t.IdPaciente, titulo, texto, null);
        return ("Enviado", null);
    }

    // EmailService es opcional: si el correo no está configurado (falta Email:Host, Email:From o Email:Password) el aviso
    // se omite. EmailService deja en el log el aviso "Correo no configurado" con las claves que faltan.
    private (string, string?) EnviarMail(string destinatario, string asunto, string texto)
    {
        var servicio = _servicios.GetService<EmailService>();
        if (servicio is null) return ("Omitido", "EmailService no está registrado.");
        try
        {
            return servicio.EnviarAviso(destinatario, asunto, texto)
                ? ("Enviado", null)
                : ("Omitido", "EmailService no está configurado.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error enviando mail");
            return ("Error", ex.Message);
        }
    }

    private string QuienEs(Conversacion c)
    {
        if (c.IdPaciente is int id)
        {
            var p = _datos.ObtenerPaciente(id);
            if (p != null) return $"paciente {p.Nombre}, tel. {c.Telefono}";
        }
        return $"no identificado, tel. {c.Telefono}";
    }

    private void Seguro(string operacion, Action accion)
    {
        try { accion(); }
        catch (Exception ex) { _log.LogError(ex, "Falló el aviso {Operacion}; el flujo original sigue.", operacion); }
    }
}
