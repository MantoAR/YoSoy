using Microsoft.Extensions.Options;

/// <summary>
/// El chat para el personal (Mensajeria:ModoAtencion = "Personal"): lo que la gente le escribe al número de WhatsApp de la clínica lo atiende una persona del equipo,
/// no el asistente. Por cada mensaje que entra:
///  1. lo guarda en el historial, como siempre;
///  2. NO contesta nada. Lo único que puede salir hacia el paciente es el mensaje de emergencia, y solo si se activó a mano (más abajo);
///  3. deja la conversación para el Configurador, que hace de recepción y es el único rol que ve los chats (decisión D2): "DerivadaHumano" (la pantalla la rotula "Para
///     atender") y sin dueño: los profesionales no ven chats, así que no hay a quién asignarla;
///  4. una frase de riesgo la marca "Urgente" y avisa en la campanita, igual que en el modo Asistente;
///  5. avisa "chat nuevo" en la campanita, una vez por tanda de mensajes: cuando es el primero de la conversación, cuando estaba cerrada o cuando pasó media hora sin
///     actividad. Los mensajes de una charla en curso no vuelven a avisar: para eso está el contador de no leídos de la bandeja.
///
/// El mensaje de emergencia (Mensajeria:Personal:RespuestaDeEmergencia, decisión D3) viene APAGADO: las líneas de Mensajeria:LineasEmergencia en appsettings.json todavía
/// dicen "[COMPLETAR Y VALIDAR]" y un paciente en crisis no puede recibir un teléfono que no existe. Apagado, la frase de riesgo igual marca la conversación como urgente y
/// avisa al Configurador: lo único que no sale es el mensaje al paciente. Y aunque se lo prenda, tampoco sale mientras alguna línea siga sin completar (resguardo de
/// <see cref="PuedeMandarLaEmergencia"/>). El paso manual para activarlo está en docs/DEPLOY.md.
///
/// Es una clase aparte de BotReservasService a propósito: el modo Asistente no cambia en nada (BotReservasService sigue siendo el de siempre) y RecepcionDeMensajes elige cuál
/// atiende según el valor del momento.
/// </summary>
public class AtencionPersonalService
{
    /// <summary>Cuánto silencio tiene que haber habido para que un mensaje nuevo vuelva a avisar en la campanita.</summary>
    public static readonly TimeSpan SilencioParaAvisar = TimeSpan.FromMinutes(30);

    private readonly IConversacionDAO _conversaciones;
    private readonly IServicioMensajes _mensajes;
    private readonly IMensajeChatDAO _historial;
    private readonly FiltroSeguridad _filtro;
    private readonly INotificadorTurnos _notificador;
    private readonly IBajaDeWhatsApp _bajas;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<AtencionPersonalService> _log;

    public AtencionPersonalService(
        IConversacionDAO conversaciones, IServicioMensajes mensajes, IMensajeChatDAO historial, FiltroSeguridad filtro, INotificadorTurnos notificador,
        IBajaDeWhatsApp bajas, IOptionsMonitor<MensajeriaOptions> opciones, TimeProvider reloj, ILogger<AtencionPersonalService> log)
    {
        _conversaciones = conversaciones;
        _mensajes       = mensajes;
        _historial      = historial;
        _filtro         = filtro;
        _notificador    = notificador;
        _bajas          = bajas;
        _opciones       = opciones;
        _reloj          = reloj;
        _log            = log;
    }

    private MensajeriaOptions Op => _opciones.CurrentValue;

    public async Task<RespuestaBot> ProcesarAsync(string telefono, string texto, string canal)
    {
        var tel = Telefono.Normalizar(telefono);
        if (tel.Length < 6) throw new ArgumentException("El teléfono no es válido.");
        texto = (texto ?? "").Trim();
        if (texto.Length == 0) throw new ArgumentException("El mensaje está vacío.");

        var c = _conversaciones.ObtenerOCrear(tel, canal);
        var ahora = _reloj.GetLocalNow().DateTime;

        // Si vuelve a avisar en la campanita se decide ANTES de guardar este mensaje, mirando cómo estaba la conversación.
        var esElPrimero = !_historial.TieneMensajeEntrante(c.IdConversacion);
        var avisar = esElPrimero || c.Estado == EstadoConversacion.Cerrada || ahora - c.FechaUltimaActividad >= SilencioParaAvisar;

        _mensajes.RegistrarEntrante(c, texto);

        // BAJA / STOP: igual que en el modo Asistente se revoca la aceptación de los avisos (si la llave está prendida; si no, no toca nada). A diferencia de aquel modo
        // el mensaje NO se esconde: el equipo tiene que ver que la persona pidió la baja (whatsapp-service ya dejó de escribirle y rechazaría cualquier respuesta).
        if (ComandoWhatsApp.EsBaja(texto)) _bajas.RegistrarBaja(tel);

        var respuestas = new List<string>();
        if (_filtro.EsRiesgo(texto))
        {
            c.Estado = EstadoConversacion.Urgente;
            var seManda = PuedeMandarLaEmergencia();
            if (seManda) respuestas.Add(_filtro.MensajeEmergencia());
            _notificador.AvisarUrgencia(c, texto, seRespondioAlPaciente: seManda);
        }
        else
        {
            // Una "Urgente" sigue urgente hasta que el equipo la resuelva: un mensaje común no la baja de estado.
            if (c.Estado is EstadoConversacion.Activa or EstadoConversacion.Cerrada) c.Estado = EstadoConversacion.DerivadaHumano;
            if (avisar) _notificador.AvisarChatNuevo(c);
        }

        c.FechaUltimaActividad = ahora;
        _conversaciones.Guardar(c);

        foreach (var respuesta in respuestas)
            await _mensajes.EnviarAsync(c, respuesta, "Bot");

        return new RespuestaBot(c.IdConversacion, respuestas);
    }

    /// <summary>
    /// ¿Se le manda el mensaje de emergencia a quien escribió una frase de riesgo? Solo si está activado (Mensajeria:Personal:RespuestaDeEmergencia) Y todas las líneas de
    /// Mensajeria:LineasEmergencia están completas. Con la llave prendida y una línea sin completar no se manda y el log lo avisa.
    /// </summary>
    private bool PuedeMandarLaEmergencia()
    {
        if (!Op.Personal.RespuestaDeEmergencia) return false;
        var lineas = Op.LineasEmergencia;
        if (lineas.Count > 0 && lineas.All(l => !LineaSinCompletar(l))) return true;
        _log.LogWarning("Mensajeria:Personal:RespuestaDeEmergencia está prendida pero las líneas de emergencia (Mensajeria:LineasEmergencia) están sin completar: " +
                        "no se le manda el mensaje de emergencia al paciente. El equipo recibe el aviso de urgencia igual.");
        return false;
    }

    /// <summary>Una línea sin teléfono, o que todavía dice "[COMPLETAR Y VALIDAR]" (en el nombre o en el teléfono), no se le puede dar a una persona en crisis.</summary>
    public static bool LineaSinCompletar(LineaEmergencia l) =>
        string.IsNullOrWhiteSpace(l.Telefono)
        || l.Telefono.Contains("COMPLETAR", StringComparison.OrdinalIgnoreCase)
        || (l.Nombre ?? "").Contains("COMPLETAR", StringComparison.OrdinalIgnoreCase);
}
