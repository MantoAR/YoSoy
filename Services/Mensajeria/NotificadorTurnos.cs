using Microsoft.Extensions.Options;

/// <summary>
/// Servicio único de avisos automáticos:
///  - Por evento (solicitud recibida, turno agendado por el profesional, confirmado,
///    cancelado): chat (canal activo) + campanita del paciente (Notificacion).
///  - Recordatorio 24 h (lo dispara RecordatorioBackgroundService): chat + mail + campanita.
///  - Avisos internos (urgencia, derivación, paciente que no asistirá): NotificacionInterna
///    y, para el profesional, mail si tiene uno cargado.
/// Cada envío a un paciente queda registrado en Recordatorio; el índice único
/// (IdTurno, Tipo, Canal) evita mandar dos veces el mismo aviso.
/// Ningún método público lanza: los errores se registran en el log y en Recordatorio.
/// Los estados se resuelven por Descripcion, nunca por id.
/// </summary>
public class NotificadorTurnos : INotificadorTurnos
{
    private static readonly string[] CanalesEvento       = { "Chat", "App" };
    public  static readonly string[] CanalesRecordatorio = { "Chat", "Email", "App" };

    private readonly IDatosMensajeria _datos;
    private readonly IRecordatorioDAO _recordatorios;
    private readonly INotificacionInternaDAO _internas;
    private readonly IConversacionDAO _conversaciones;
    private readonly IServicioMensajes _mensajes;
    private readonly IMessageChannel _canal;
    private readonly ITurnoRepositorio _turnos;
    private readonly IServiceProvider _servicios;
    private readonly IConfiguration _config;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<NotificadorTurnos> _log;

    public NotificadorTurnos(
        IDatosMensajeria datos, IRecordatorioDAO recordatorios, INotificacionInternaDAO internas,
        IConversacionDAO conversaciones, IServicioMensajes mensajes, IMessageChannel canal,
        ITurnoRepositorio turnos, IServiceProvider servicios, IConfiguration config,
        IOptionsMonitor<MensajeriaOptions> opciones, TimeProvider reloj, ILogger<NotificadorTurnos> log)
    {
        _datos          = datos;
        _recordatorios  = recordatorios;
        _internas       = internas;
        _conversaciones = conversaciones;
        _mensajes       = mensajes;
        _canal          = canal;
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

    public void TurnoSolicitado(int idTurno, bool respondidoPorChat = false) => Seguro(nameof(TurnoSolicitado), () =>
        AvisarPaciente(idTurno, "TurnoSolicitado", CanalesEvento, respondidoPorChat,
            "Solicitud de turno recibida",
            t => $"Recibimos tu solicitud de turno con {t.NombreProfesional} para el {Formato.FechaHora(t.Momento)}. " +
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
                t => $"{t.NombreProfesional} te propuso un turno para el {Formato.FechaHora(t.Momento)}. " +
                     $"Para confirmarlo, ingresá a tu panel en la web: {Op.UrlIngresoTurno}");
        });

    public void TurnoConfirmado(int idTurno) => Seguro(nameof(TurnoConfirmado), () =>
        AvisarPaciente(idTurno, "TurnoConfirmado", CanalesEvento, false,
            "Turno confirmado",
            t => $"Tu turno con {t.NombreProfesional} del {Formato.FechaHora(t.Momento)} quedó confirmado.",
            estadoEsperado: "Confirmado"));

    public void TurnoCancelado(int idTurno, bool respondidoPorChat = false) => Seguro(nameof(TurnoCancelado), () =>
        AvisarPaciente(idTurno, "TurnoCancelado", CanalesEvento, respondidoPorChat,
            "Turno cancelado",
            t => $"Tu turno con {t.NombreProfesional} del {Formato.FechaHora(t.Momento)} fue cancelado. " +
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
            t => TextoRecordatorio(t) + "\n\nRespondé 1 para confirmar tu asistencia o 2 si no podés asistir.",
            estadoEsperado: "Confirmado",
            textoOtrosCanales: t => TextoRecordatorio(t) +
                "\n\nSi no vas a poder asistir, avisanos respondiendo el mensaje del chat o desde la web.",
            esRecordatorio: true));

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

    private void AvisarPaciente(int idTurno, string tipo, string[] canales, bool respondidoPorChat, string titulo,
        Func<TurnoAviso, string> texto, string? estadoEsperado = null,
        Func<TurnoAviso, string>? textoOtrosCanales = null, bool esRecordatorio = false)
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
                var cuerpo = canal == "Chat" ? texto(t) : (textoOtrosCanales ?? texto)(t);
                (estado, error) = canal switch
                {
                    "Chat" when respondidoPorChat => ("Omitido", "El bot ya se lo respondió en la conversación."),
                    "Chat"  => EnviarChat(t, cuerpo, esRecordatorio),
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

    private (string, string?) EnviarChat(TurnoAviso t, string texto, bool esRecordatorio)
    {
        var telefono = Telefono.Normalizar(t.TelefonoPaciente);
        if (telefono.Length < 6) return ("Omitido", "El paciente no tiene teléfono cargado.");

        var c = _conversaciones.ObtenerOCrear(telefono, _canal.Nombre);
        c.IdPaciente ??= t.IdPaciente;

        // Después de un recordatorio, el "1" o "2" que conteste el paciente se
        // interpreta como respuesta a este turno. Si una persona tiene la
        // conversación, no se le cambia el estado.
        if (esRecordatorio && c.Estado is EstadoConversacion.Activa or EstadoConversacion.Cerrada)
        {
            var ctx = ContextoBot.Leer(c.DatosContexto);
            ctx.LimpiarFlujo();
            ctx.IdTurno     = t.IdTurno;
            c.Estado        = EstadoConversacion.Activa;
            c.PasoActual    = PasoBot.RespuestaRecordatorio;
            c.DatosContexto = ctx.Serializar();
        }
        c.FechaUltimaActividad = Ahora;
        _conversaciones.Guardar(c);

        var m = _mensajes.EnviarAsync(c, texto, "Sistema").GetAwaiter().GetResult();
        return m.EstadoEnvio == "Enviado" ? ("Enviado", null) : ("Error", "El canal no pudo entregar el mensaje.");
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
