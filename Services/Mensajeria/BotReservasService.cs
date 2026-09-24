using Microsoft.Extensions.Options;

public record RespuestaBot(int IdConversacion, List<string> Respuestas);

/// <summary>
/// Chatbot de reservas: menú guiado determinístico con una máquina de estados por
/// conversación (Conversacion.PasoActual + ContextoBot en DatosContexto).
/// Orden de cada mensaje entrante:
///   1. Seguridad clínica (frases de riesgo) — siempre primero, en cualquier estado.
///   2. Si la conversación está en manos de una persona (DerivadaHumano/Urgente), el bot calla.
///   3. Identificación del paciente (teléfono y, si no coincide, DNI).
///   4. Timeout por inactividad → vuelve al menú.
///   5. Intenciones globales (menú, persona, pedir/ver/cancelar turno, preguntas).
///   6. Lo que corresponda al paso actual.
/// Las reglas de turnos no están acá: las aplica TurnoReglasService.
/// </summary>
public class BotReservasService
{
    public const string TextoMenu =
        "¿En qué te puedo ayudar? Respondé con el número:\n" +
        "1) Pedir turno\n" +
        "2) Ver mis turnos\n" +
        "3) Cancelar un turno\n" +
        "4) Preguntas frecuentes\n" +
        "5) Hablar con una persona\n\n" +
        "En cualquier momento escribí 0 o \"menu\" para volver acá.";

    private const string PieMenu = "¿Algo más? Escribí 0 para ver el menú.";
    private const string PedirNumero = "Respondé con el número de una opción, o 0 para volver al menú.";
    private const int MaxIntentosDni = 3;

    private readonly IConversacionDAO _conversaciones;
    private readonly IServicioMensajes _mensajes;
    private readonly IDatosMensajeria _datos;
    private readonly IPreguntaFrecuenteDAO _faq;
    private readonly IRecordatorioDAO _recordatorios;
    private readonly TurnoReglasService _reglas;
    private readonly INotificadorTurnos _notificador;
    private readonly IInterpreteIntencion _interprete;
    private readonly FiltroSeguridad _filtro;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly TimeProvider _reloj;

    public BotReservasService(
        IConversacionDAO conversaciones, IServicioMensajes mensajes, IDatosMensajeria datos,
        IPreguntaFrecuenteDAO faq, IRecordatorioDAO recordatorios, TurnoReglasService reglas,
        INotificadorTurnos notificador, IInterpreteIntencion interprete, FiltroSeguridad filtro,
        IOptionsMonitor<MensajeriaOptions> opciones, TimeProvider reloj)
    {
        _conversaciones = conversaciones;
        _mensajes       = mensajes;
        _datos          = datos;
        _faq            = faq;
        _recordatorios  = recordatorios;
        _reglas         = reglas;
        _notificador    = notificador;
        _interprete     = interprete;
        _filtro         = filtro;
        _opciones       = opciones;
        _reloj          = reloj;
    }

    private MensajeriaOptions Op => _opciones.CurrentValue;

    public async Task<RespuestaBot> ProcesarAsync(string telefono, string texto, string canal)
    {
        var tel = Telefono.Normalizar(telefono);
        if (tel.Length < 6) throw new ArgumentException("El teléfono no es válido.");
        texto = (texto ?? "").Trim();
        if (texto.Length == 0) throw new ArgumentException("El mensaje está vacío.");

        var c = _conversaciones.ObtenerOCrear(tel, canal);
        _mensajes.RegistrarEntrante(c, texto);

        var ahora = _reloj.GetLocalNow().DateTime;
        var ctx   = ContextoBot.Leer(c.DatosContexto);
        var r     = new List<string>();

        Procesar(c, ctx, texto, ahora, r);

        c.DatosContexto        = ctx.Serializar();
        c.FechaUltimaActividad = ahora;
        _conversaciones.Guardar(c);

        foreach (var respuesta in r)
            await _mensajes.EnviarAsync(c, respuesta, "Bot");

        return new RespuestaBot(c.IdConversacion, r);
    }

    private void Procesar(Conversacion c, ContextoBot ctx, string texto, DateTime ahora, List<string> r)
    {
        // 1. Seguridad clínica: antes de interpretar nada.
        if (_filtro.EsRiesgo(texto))
        {
            c.Estado     = EstadoConversacion.Urgente;
            c.PasoActual = c.IdPaciente is null ? PasoBot.Inicio : PasoBot.Menu;
            ctx.LimpiarFlujo();
            r.Add(_filtro.MensajeEmergencia());
            _notificador.AvisarUrgencia(c, texto);
            return;
        }

        // 2. Una persona del equipo tiene la conversación: el bot no responde.
        if (c.Estado is EstadoConversacion.DerivadaHumano or EstadoConversacion.Urgente) return;

        if (c.Estado == EstadoConversacion.Cerrada)
        {
            c.Estado     = EstadoConversacion.Activa;
            c.PasoActual = c.IdPaciente is null ? PasoBot.Inicio : PasoBot.Menu;
            ctx.LimpiarFlujo();
        }

        var intencion = _interprete.Interpretar(texto);

        // 3. Identificación.
        if (c.IdPaciente is null)
        {
            Identificar(c, ctx, texto, intencion, r);
            return;
        }

        // 4. Timeout: el siguiente mensaje arranca desde el menú. La respuesta a un
        //    recordatorio puede llegar horas después, así que no expira por tiempo.
        if (c.PasoActual is not (PasoBot.Inicio or PasoBot.Menu or PasoBot.RespuestaRecordatorio)
            && ahora - c.FechaUltimaActividad > TimeSpan.FromMinutes(Op.TimeoutConversacionMinutos))
        {
            ctx.LimpiarFlujo();
            c.PasoActual = PasoBot.Menu;
            r.Add("Pasó un rato sin actividad, así que volvimos al comienzo.\n\n" + TextoMenu);
            return;
        }

        // 5. Intenciones que valen desde cualquier paso.
        switch (intencion.Tipo)
        {
            case TipoIntencion.VolverAlMenu:
            case TipoIntencion.Saludo:
                IrAlMenu(c, ctx, r, intencion.Tipo == TipoIntencion.Saludo ? "¡Hola!" : null);
                return;
            case TipoIntencion.HablarConPersona: Derivar(c, ctx, r);            return;
            case TipoIntencion.PedirTurno:       IniciarPedirTurno(c, ctx, r);  return;
            case TipoIntencion.VerTurnos:        VerTurnos(c, ctx, ahora, r);   return;
            case TipoIntencion.CancelarTurno:    IniciarCancelar(c, ctx, ahora, r); return;
            case TipoIntencion.Faq:              MostrarFaq(c, ctx, r);         return;
        }

        // 6. Según el paso actual.
        switch (c.PasoActual)
        {
            case PasoBot.ElegirEspecialidad:         ElegirEspecialidad(c, ctx, texto, intencion, r); break;
            case PasoBot.ElegirProfesional:          ElegirProfesional(c, ctx, texto, intencion, r); break;
            case PasoBot.ElegirHorario:              ElegirHorario(c, ctx, texto, intencion, r); break;
            case PasoBot.ConfirmarTurno:             ConfirmarTurno(c, ctx, texto, intencion, r); break;
            case PasoBot.ElegirTurnoCancelar:        ElegirTurnoCancelar(c, ctx, texto, intencion, ahora, r); break;
            case PasoBot.ConfirmarCancelacion:       ConfirmarCancelacion(c, ctx, texto, intencion, r); break;
            case PasoBot.ConfirmarAvisoNoAsistencia: ConfirmarAvisoNoAsistencia(c, ctx, texto, intencion, r); break;
            case PasoBot.Faq:                        ElegirFaq(c, ctx, texto, intencion, r); break;
            case PasoBot.RespuestaRecordatorio:      ResponderRecordatorio(c, ctx, texto, intencion, ahora, r); break;
            default:                                 OpcionMenu(c, ctx, texto, intencion, ahora, r); break;
        }
    }

    // ── Identificación ──────────────────────────────────────────

    private void Identificar(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, List<string> r)
    {
        if (intencion.Tipo == TipoIntencion.HablarConPersona ||
            (c.PasoActual == PasoBot.IdentificarDni && intencion is { Tipo: TipoIntencion.OpcionNumerica, Numero: 5 }))
        {
            Derivar(c, ctx, r);
            return;
        }

        if (c.PasoActual != PasoBot.IdentificarDni)
        {
            var candidatos = _datos.PacientesPorTelefono(c.Telefono);
            if (candidatos.Count == 1)
            {
                Vincular(c, ctx, candidatos[0], intencion, r);
                return;
            }
            // Ninguno, o más de un paciente con el mismo número: se pide el DNI.
            c.PasoActual = PasoBot.IdentificarDni;
            r.Add("¡Hola! Soy el asistente virtual de turnos de YoSoy. " +
                  "No encontré tu número entre nuestros pacientes. ¿Me decís tu DNI? (solo los números)");
            return;
        }

        if (ctx.IntentosDni >= MaxIntentosDni)
        {
            r.Add(TextoNoRegistrado());
            return;
        }

        var dni = new string(texto.Where(char.IsDigit).ToArray());
        if (dni.Length is < 7 or > 8)
        {
            r.Add("Necesito tu DNI, solo los números (por ejemplo 30123456). Si preferís, escribí 5 para hablar con una persona.");
            return;
        }

        var paciente = _datos.PacienteActivoPorDni(dni);
        if (paciente is null)
        {
            ctx.IntentosDni++;
            r.Add(TextoNoRegistrado());
            return;
        }
        Vincular(c, ctx, paciente, new Intencion(TipoIntencion.Desconocido), r);
    }

    // El bot no crea pacientes: el alta se hace en la web.
    private string TextoNoRegistrado() =>
        "No encontré un paciente registrado con ese DNI. Para atenderte por acá primero tenés que " +
        $"registrarte en la web: {Op.UrlRegistroWeb}\n" +
        "Si querés, escribí 5 para hablar con una persona o probá de nuevo con tu DNI.";

    private void Vincular(Conversacion c, ContextoBot ctx, Paciente p, Intencion intencion, List<string> r)
    {
        c.IdPaciente    = p.IdPaciente;
        c.PasoActual    = PasoBot.Menu;
        ctx.IntentosDni = 0;
        ctx.LimpiarFlujo();
        r.Add($"¡Hola, {Formato.PrimerNombre(p.Nombre)}! Soy el asistente virtual de turnos de YoSoy.");

        // Si el primer mensaje ya decía qué quería ("hola, quiero pedir un turno"), se sigue directo.
        switch (intencion.Tipo)
        {
            case TipoIntencion.PedirTurno:    IniciarPedirTurno(c, ctx, r); break;
            case TipoIntencion.VerTurnos:     VerTurnos(c, ctx, Ahora, r); break;
            case TipoIntencion.CancelarTurno: IniciarCancelar(c, ctx, Ahora, r); break;
            case TipoIntencion.Faq:           MostrarFaq(c, ctx, r); break;
            default:                          r.Add(TextoMenu); break;
        }
    }

    private DateTime Ahora => _reloj.GetLocalNow().DateTime;

    // ── Menú ────────────────────────────────────────────────────

    private void IrAlMenu(Conversacion c, ContextoBot ctx, List<string> r, string? saludo = null)
    {
        ctx.LimpiarFlujo();
        c.PasoActual = PasoBot.Menu;
        r.Add(saludo is null ? TextoMenu : $"{saludo} {TextoMenu}");
    }

    private void OpcionMenu(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, DateTime ahora, List<string> r)
    {
        c.PasoActual = PasoBot.Menu;
        switch (intencion is { Tipo: TipoIntencion.OpcionNumerica } ? intencion.Numero : null)
        {
            case 1: IniciarPedirTurno(c, ctx, r); break;
            case 2: VerTurnos(c, ctx, ahora, r); break;
            case 3: IniciarCancelar(c, ctx, ahora, r); break;
            case 4: MostrarFaq(c, ctx, r); break;
            case 5: Derivar(c, ctx, r); break;
            default: NoEntendido(texto, r, TextoMenu); break;
        }
    }

    // Nunca se responde algo clínico: se aclara que lo trabaja el profesional.
    private void NoEntendido(string texto, List<string> r, string ayuda)
    {
        r.Add(_filtro.EsConsultaClinica(texto)
            ? FiltroSeguridad.MensajeConsultaClinica
            : "No te entendí. " + ayuda);
    }

    private void Derivar(Conversacion c, ContextoBot ctx, List<string> r)
    {
        c.Estado     = EstadoConversacion.DerivadaHumano;
        c.PasoActual = c.IdPaciente is null ? PasoBot.Inicio : PasoBot.Menu;
        ctx.LimpiarFlujo();
        r.Add("Listo, le pasamos tu conversación a una persona del equipo de YoSoy, que te va a " +
              "responder por este mismo chat. Mientras tanto, el asistente automático queda en pausa.");
        _notificador.AvisarDerivacion(c);
    }

    // ── Pedir turno ─────────────────────────────────────────────

    private void IniciarPedirTurno(Conversacion c, ContextoBot ctx, List<string> r)
    {
        ctx.LimpiarFlujo();
        var profesionales = _datos.ProfesionalesDelPaciente(c.IdPaciente!.Value);

        if (profesionales.Count == 1)
        {
            OfrecerHorarios(c, ctx, profesionales[0].IdProfesional, profesionales[0].Nombre, r);
            return;
        }
        if (profesionales.Count > 1)
        {
            OfrecerProfesionales(c, ctx, profesionales, "¿Con cuál de tus profesionales querés el turno?", r);
            return;
        }

        // Sin profesionales asignados: se elige por especialidad.
        var especialidades = _datos.EspecialidadesConAgenda();
        if (especialidades.Count == 0)
        {
            c.PasoActual = PasoBot.Menu;
            r.Add("En este momento no hay profesionales con agenda disponible. Escribí 5 si querés hablar con una persona.");
            return;
        }
        ctx.Opciones        = especialidades.Select(e => e.IdEspecialidad).ToList();
        ctx.NombresOpciones = especialidades.Select(e => e.Nombre).ToList();
        c.PasoActual = PasoBot.ElegirEspecialidad;
        r.Add("¿Para qué especialidad querés el turno?\n" + ListaNumerada(ctx.NombresOpciones) + "\n\n" + PedirNumero);
    }

    private void ElegirEspecialidad(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, List<string> r)
    {
        if (!Elegido(ctx, intencion, out var i)) { NoEntendido(texto, r, PedirNumero); return; }
        var profesionales = _datos.ProfesionalesConAgenda(ctx.Opciones[i]);
        if (profesionales.Count == 0)
        {
            r.Add("No encontré profesionales disponibles en esa especialidad. Elegí otra, o escribí 5 para hablar con una persona.");
            return;
        }
        if (profesionales.Count == 1)
        {
            OfrecerHorarios(c, ctx, profesionales[0].IdProfesional, profesionales[0].Nombre, r);
            return;
        }
        OfrecerProfesionales(c, ctx, profesionales, "¿Con qué profesional querés el turno?", r);
    }

    private void OfrecerProfesionales(Conversacion c, ContextoBot ctx, List<ProfesionalResumen> lista, string pregunta, List<string> r)
    {
        ctx.Opciones        = lista.Select(p => p.IdProfesional).ToList();
        ctx.NombresOpciones = lista.Select(p => p.Nombre).ToList();
        c.PasoActual = PasoBot.ElegirProfesional;
        r.Add(pregunta + "\n" +
              ListaNumerada(lista.Select(p => $"{p.Nombre} — {p.Especialidad}")) + "\n\n" + PedirNumero);
    }

    private void ElegirProfesional(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, List<string> r)
    {
        if (!Elegido(ctx, intencion, out var i)) { NoEntendido(texto, r, PedirNumero); return; }
        OfrecerHorarios(c, ctx, ctx.Opciones[i], ctx.NombresOpciones[i], r);
    }

    private void OfrecerHorarios(Conversacion c, ContextoBot ctx, int idProfesional, string nombre, List<string> r)
    {
        var horarios = _reglas.HorariosLibres(c.IdPaciente!.Value, idProfesional, Op.DiasBusquedaHorarios, Op.MaxHorariosOfrecidos);
        ctx.LimpiarFlujo();
        if (horarios.Count == 0)
        {
            c.PasoActual = PasoBot.Menu;
            r.Add($"No encontré horarios libres con {nombre} en los próximos {Op.DiasBusquedaHorarios} días. " +
                  "Escribí 5 para hablar con una persona o 0 para volver al menú.");
            return;
        }
        ctx.IdProfesional     = idProfesional;
        ctx.NombreProfesional = nombre;
        ctx.Horarios          = horarios;
        c.PasoActual = PasoBot.ElegirHorario;
        r.Add($"Estos son los próximos horarios disponibles con {nombre}:\n" +
              ListaNumerada(horarios.Select(Formato.FechaHora)) + "\n\n" +
              "Respondé con el número del horario que prefieras, o 0 para volver al menú.");
    }

    private void ElegirHorario(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, List<string> r)
    {
        if (intencion.Tipo != TipoIntencion.OpcionNumerica || intencion.Numero < 1 || intencion.Numero > ctx.Horarios.Count)
        {
            NoEntendido(texto, r, PedirNumero);
            return;
        }
        ctx.HorarioElegido = ctx.Horarios[intencion.Numero!.Value - 1];
        c.PasoActual = PasoBot.ConfirmarTurno;
        r.Add($"Vas a pedir un turno virtual con {ctx.NombreProfesional} el {Formato.FechaHora(ctx.HorarioElegido.Value)}. ¿Confirmás?\n" +
              "1) Sí, pedir el turno\n2) No, ver otros horarios");
    }

    private void ConfirmarTurno(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, List<string> r)
    {
        if (ctx.IdProfesional is null || ctx.HorarioElegido is null) { IrAlMenu(c, ctx, r); return; }
        if (EsNo(intencion))
        {
            OfrecerHorarios(c, ctx, ctx.IdProfesional!.Value, ctx.NombreProfesional!, r);
            return;
        }
        if (!EsSi(intencion)) { NoEntendido(texto, r, "Respondé 1 para confirmar o 2 para ver otros horarios."); return; }

        var idPaciente = c.IdPaciente!.Value;
        var idProf     = ctx.IdProfesional!.Value;
        var nombre     = ctx.NombreProfesional!;
        var momento    = ctx.HorarioElegido!.Value;

        var resultado = _reglas.Solicitar(idPaciente, idProf, momento, "Solicitado por chat");
        if (!resultado.Regla.Ok)
        {
            var motivo = resultado.Regla.Mensaje;
            if (resultado.Regla.Codigo == "RN-02") motivo += $" Podés firmarlo en la web: {Op.UrlConsentimiento}";
            r.Add($"No pude registrar el turno: {motivo}");
            // Si el horario dejó de servir, se ofrecen los que siguen libres.
            if (resultado.Regla.Codigo is "RN-01" or "RN-05") OfrecerHorarios(c, ctx, idProf, nombre, r);
            else IrAlMenu(c, ctx, r);
            return;
        }

        var texto2 = $"¡Listo! Pediste un turno con {nombre} para el {Formato.FechaHora(momento)}. " +
                     "Queda pendiente hasta que tu profesional lo confirme; te vamos a avisar por acá.";
        if (!_reglas.TieneConsentimiento(idPaciente))
            texto2 += "\n\nImportante: antes de tu primera sesión vas a tener que firmar el consentimiento " +
                      $"informado. Podés hacerlo en la web: {Op.UrlConsentimiento}";
        r.Add(texto2 + "\n\n" + PieMenu);

        _notificador.TurnoSolicitado(resultado.IdTurno!.Value, respondidoPorChat: true);
        ctx.LimpiarFlujo();
        c.PasoActual = PasoBot.Menu;
    }

    // ── Ver turnos ──────────────────────────────────────────────

    private void VerTurnos(Conversacion c, ContextoBot ctx, DateTime ahora, List<string> r)
    {
        ctx.LimpiarFlujo();
        c.PasoActual = PasoBot.Menu;
        var turnos = _datos.TurnosProximos(c.IdPaciente!.Value, ahora);
        if (turnos.Count == 0)
        {
            r.Add("No tenés turnos próximos. Escribí 1 si querés pedir uno, o 0 para ver el menú.");
            return;
        }
        r.Add("Tus próximos turnos:\n" +
              string.Join("\n", turnos.Select(t =>
                  $"• {Formato.FechaHora(t.Momento)} con {t.NombreProfesional} ({t.Modalidad ?? "Virtual"}) — {t.Estado}")) +
              "\n\n" + PieMenu);
    }

    // ── Cancelar ────────────────────────────────────────────────

    private void IniciarCancelar(Conversacion c, ContextoBot ctx, DateTime ahora, List<string> r)
    {
        ctx.LimpiarFlujo();
        var turnos = _datos.TurnosProximos(c.IdPaciente!.Value, ahora);
        if (turnos.Count == 0)
        {
            c.PasoActual = PasoBot.Menu;
            r.Add("No tenés turnos próximos para cancelar. " + PieMenu);
            return;
        }
        ctx.Opciones = turnos.Select(t => t.IdTurno).ToList();
        c.PasoActual = PasoBot.ElegirTurnoCancelar;
        r.Add("¿Qué turno querés cancelar?\n" +
              ListaNumerada(turnos.Select(t => $"{Formato.FechaHora(t.Momento)} con {t.NombreProfesional} — {t.Estado}")) +
              "\n\n" + PedirNumero);
    }

    private void ElegirTurnoCancelar(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, DateTime ahora, List<string> r)
    {
        if (!Elegido(ctx, intencion, out var i)) { NoEntendido(texto, r, PedirNumero); return; }
        var t = _datos.ObtenerTurnoAviso(ctx.Opciones[i]);
        if (t is null || t.IdPaciente != c.IdPaciente) { IrAlMenu(c, ctx, r); return; }

        ctx.Opciones.Clear();
        ctx.IdTurno = t.IdTurno;
        ctx.NombreProfesional = t.NombreProfesional;

        // RN-05: con menos de 24 h no se cancela; se ofrece avisarle al profesional.
        if (t.Momento < ahora.AddHours(TurnoReglasService.HorasMinimasAnticipacion))
        {
            c.PasoActual = PasoBot.ConfirmarAvisoNoAsistencia;
            r.Add($"Faltan menos de {TurnoReglasService.HorasMinimasAnticipacion} horas para ese turno, así que ya no se puede " +
                  $"cancelar por este medio. ¿Querés que le avisemos a {t.NombreProfesional} que no vas a poder asistir?\n" +
                  "1) Sí, avisar\n2) No");
            return;
        }
        c.PasoActual = PasoBot.ConfirmarCancelacion;
        r.Add($"¿Confirmás que querés cancelar el turno del {Formato.FechaHora(t.Momento)} con {t.NombreProfesional}?\n" +
              "1) Sí, cancelar\n2) No");
    }

    private void ConfirmarCancelacion(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, List<string> r)
    {
        if (ctx.IdTurno is null) { IrAlMenu(c, ctx, r); return; }
        if (EsNo(intencion)) { r.Add("Perfecto, no cancelamos nada. " + PieMenu); ctx.LimpiarFlujo(); c.PasoActual = PasoBot.Menu; return; }
        if (!EsSi(intencion)) { NoEntendido(texto, r, "Respondé 1 para cancelar o 2 para dejarlo como está."); return; }

        var idTurno   = ctx.IdTurno!.Value;
        var resultado = _reglas.Cancelar(idTurno, c.IdPaciente!.Value);
        if (resultado.Ok)
        {
            r.Add("Listo, cancelamos tu turno. " + PieMenu);
            _notificador.TurnoCancelado(idTurno, respondidoPorChat: true);
        }
        else r.Add($"No pude cancelar el turno: {resultado.Mensaje} " + PieMenu);
        ctx.LimpiarFlujo();
        c.PasoActual = PasoBot.Menu;
    }

    private void ConfirmarAvisoNoAsistencia(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, List<string> r)
    {
        if (ctx.IdTurno is null) { IrAlMenu(c, ctx, r); return; }
        if (EsNo(intencion)) { r.Add("De acuerdo, no avisamos nada. " + PieMenu); ctx.LimpiarFlujo(); c.PasoActual = PasoBot.Menu; return; }
        if (!EsSi(intencion)) { NoEntendido(texto, r, "Respondé 1 para avisar o 2 para no avisar."); return; }

        _notificador.AvisarNoAsistencia(ctx.IdTurno!.Value);
        r.Add($"Listo, le avisamos a {ctx.NombreProfesional} que no vas a poder asistir. " +
              "El turno sigue registrado porque faltan menos de 24 horas. " + PieMenu);
        ctx.LimpiarFlujo();
        c.PasoActual = PasoBot.Menu;
    }

    // ── Respuesta a un recordatorio (1 = asisto, 2 = no puedo) ──

    private void ResponderRecordatorio(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, DateTime ahora, List<string> r)
    {
        var t = ctx.IdTurno is int id ? _datos.ObtenerTurnoAviso(id) : null;
        var vigente = t != null && t.IdPaciente == c.IdPaciente && t.Momento > ahora
                      && t.Estado is "Pendiente" or "Confirmado";

        if (vigente && EsSi(intencion))
        {
            _recordatorios.RegistrarRespuesta(t!.IdTurno, "Turno24h", "AsistenciaConfirmada", ahora);
            r.Add($"¡Gracias! Registramos que vas a asistir al turno del {Formato.FechaHora(t.Momento)}. " + PieMenu);
            ctx.LimpiarFlujo();
            c.PasoActual = PasoBot.Menu;
            return;
        }

        if (vigente && EsNo(intencion))
        {
            _recordatorios.RegistrarRespuesta(t!.IdTurno, "Turno24h", "NoAsistira", ahora);
            var cancelacion = _reglas.Cancelar(t.IdTurno, c.IdPaciente!.Value);
            if (cancelacion.Ok)
            {
                r.Add("Listo, cancelamos tu turno. " + PieMenu);
                _notificador.TurnoCancelado(t.IdTurno, respondidoPorChat: true);
            }
            else
            {
                // RN-05: faltan menos de 24 h, no se cancela; se le avisa al profesional.
                _notificador.AvisarNoAsistencia(t.IdTurno);
                r.Add($"Le avisamos a {t.NombreProfesional} que no vas a poder asistir. " +
                      "Como faltan menos de 24 horas, el turno no se puede cancelar por este medio. " + PieMenu);
            }
            ctx.LimpiarFlujo();
            c.PasoActual = PasoBot.Menu;
            return;
        }

        // Cualquier otra cosa (o un recordatorio vencido) se trata como una opción del menú.
        ctx.LimpiarFlujo();
        OpcionMenu(c, ctx, texto, intencion, ahora, r);
    }

    // ── Preguntas frecuentes ────────────────────────────────────

    private void MostrarFaq(Conversacion c, ContextoBot ctx, List<string> r)
    {
        ctx.LimpiarFlujo();
        var preguntas = _faq.ObtenerActivas();
        if (preguntas.Count == 0)
        {
            c.PasoActual = PasoBot.Menu;
            r.Add("Por ahora no hay preguntas frecuentes cargadas. " + PieMenu);
            return;
        }
        ctx.Opciones = preguntas.Select(p => p.IdPregunta).ToList();
        c.PasoActual = PasoBot.Faq;
        r.Add("Preguntas frecuentes:\n" + ListaNumerada(preguntas.Select(p => p.Pregunta)) +
              "\n\nRespondé con el número para ver la respuesta, o 0 para volver al menú.");
    }

    private void ElegirFaq(Conversacion c, ContextoBot ctx, string texto, Intencion intencion, List<string> r)
    {
        if (!Elegido(ctx, intencion, out var i)) { NoEntendido(texto, r, PedirNumero); return; }
        var pregunta = _faq.ObtenerActivas().FirstOrDefault(p => p.IdPregunta == ctx.Opciones[i]);
        if (pregunta is null) { MostrarFaq(c, ctx, r); return; }
        r.Add($"{pregunta.Pregunta}\n{pregunta.Respuesta}\n\n¿Otra pregunta? Escribí su número, o 0 para volver al menú.");
    }

    // ── Utilidades ──────────────────────────────────────────────

    private static bool Elegido(ContextoBot ctx, Intencion intencion, out int indice)
    {
        indice = (intencion.Numero ?? 0) - 1;
        return intencion.Tipo == TipoIntencion.OpcionNumerica && indice >= 0 && indice < ctx.Opciones.Count;
    }

    private static bool EsSi(Intencion i) =>
        i.Tipo == TipoIntencion.Afirmacion || i is { Tipo: TipoIntencion.OpcionNumerica, Numero: 1 };

    private static bool EsNo(Intencion i) =>
        i.Tipo == TipoIntencion.Negacion || i is { Tipo: TipoIntencion.OpcionNumerica, Numero: 2 };

    private static string ListaNumerada(IEnumerable<string> items) =>
        string.Join("\n", items.Select((x, i) => $"{i + 1}) {x}"));
}
