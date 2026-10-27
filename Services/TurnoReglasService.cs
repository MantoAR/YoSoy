using Microsoft.Extensions.Options;

// Link: adónde mandar a la persona para resolverlo (por ahora solo RN-02: la página de firma).
public record ResultadoRegla(bool Ok, string Codigo = "", string Mensaje = "", string? Link = null)
{
    public static readonly ResultadoRegla Valido = new(true);
    public static ResultadoRegla Falla(string codigo, string mensaje, string? link = null) => new(false, codigo, mensaje, link);
}

public record ResultadoSolicitud(ResultadoRegla Regla, int? IdTurno);

/// <summary>
/// Reglas de negocio de turnos (Encuentro 5), en un solo lugar del backend:
///  RN-01  Disponibilidad real de agenda: el horario cae dentro de la franja de una
///         fila de Agenda disponible para ese día, alineado a los slots de
///         IntervaloMinutos, en el futuro (posterior a "ahora" en la zona horaria de
///         la clínica) y sin otro turno que no esté Cancelado ni Rechazado.
///  RN-02  Consentimiento informado de telemedicina vigente (tipo buscado por
///         Descripcion, Consentimiento:TipoRequerido). Se exige al CONFIRMAR un turno
///         (PUT /turnos/{id} cuando pasa a Confirmado y PUT /turnos/{id}/confirmar-paciente)
///         y al solicitar solo si Mensajeria:ExigirConsentimientoParaSolicitar = true.
///         Aplica hacia adelante: un turno que ya estaba Confirmado no se toca.
///  RN-05  Anticipación mínima de 24 h para CANCELAR. Para pedir un turno ya no hay
///         anticipación mínima: alcanza con que el horario sea posterior a "ahora"
///         (RN-01), así que se puede pedir uno para dentro de unos minutos.
///  RN-07  Un paciente no puede tener dos turnos Pendientes o Confirmados con el
///         mismo profesional el mismo día.
///  RN-08  (migración 33) No se ofrecen ni se aceptan turnos en un feriado que el
///         profesional no trabaja, dentro de un bloqueo suyo, ni desde el vencimiento de su
///         matrícula principal (ver Services/AgendaRestricciones.cs).
/// Lo usa el bot entero y, desde la Fase 3a, también los endpoints web de turnos
/// (POST /turnos/solicitar, PUT /turnos/{id}/cancelar cuando lo llama un paciente,
/// POST /turnos/profesional-solicitar) — antes insertaban/cancelaban directo, sin pasar
/// por ninguna regla. El único que sigue aparte a propósito es Cita urgente
/// (SolicitarUrgente): mismo motivo que el bot con la mensajería, no tiene sentido pedirle
/// RN-01 a algo que es "ahora mismo" por definición.
///
/// Zona horaria: Turno.Fecha y Turno.Hora se guardan como hora local sin zona: la de
/// la clínica. "Ahora" sale de TimeProvider.GetLocalNow() y el TimeProvider de la
/// aplicación es RelojDeLaClinica (Services/RelojDeLaClinica.cs), con la zona EXPLÍCITA
/// (Turnos:ZonaHoraria, por defecto America/Argentina/Buenos_Aires): no depende de la
/// zona del sistema operativo del servidor.
/// </summary>
public class TurnoReglasService
{
    /// <summary>
    /// RN-05: anticipación mínima para CANCELAR un turno por este medio (paciente web y bot).
    /// Pedir un turno ya no la exige: ver <see cref="ValidarSolicitud"/>.
    /// </summary>
    public const int HorasMinimasAnticipacion = 24;

    private readonly ITurnoRepositorio _repo;
    private readonly TimeProvider _reloj;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly IOptionsMonitor<ConsentimientoOptions> _consentimiento;

    public TurnoReglasService(ITurnoRepositorio repo, TimeProvider reloj, IOptionsMonitor<MensajeriaOptions> opciones,
        IOptionsMonitor<ConsentimientoOptions> consentimiento)
    {
        _repo           = repo;
        _reloj          = reloj;
        _opciones       = opciones;
        _consentimiento = consentimiento;
    }

    public string UrlConsentimiento => _consentimiento.CurrentValue.UrlFirma;

    public DateTime Ahora => _reloj.GetLocalNow().DateTime;

    /// <summary>
    /// Próximos horarios que cumplen RN-01 y RN-07 para ese paciente y profesional (los de
    /// hoy que todavía no pasaron incluidos: no hay anticipación mínima), recorriendo como
    /// máximo <paramref name="dias"/> días desde hoy.
    /// </summary>
    public List<DateTime> HorariosLibres(int idPaciente, int idProfesional, int dias, int maximo) =>
        CalcularHorariosLibres(idProfesional, dias, maximo, fecha => _repo.ContarTurnosActivos(idPaciente, idProfesional, fecha) == 0, idPaciente);

    /// <summary>
    /// Igual que <see cref="HorariosLibres"/> pero sin un paciente particular: no aplica
    /// RN-07 (no hay a quién chequearle turnos repetidos ese día). La usan la vista previa
    /// de "Mis horarios de trabajo" (Fase 3a, Paso 2) y la página pública de reserva
    /// (Fase 3a, Paso 3) — ninguna de las dos tiene un paciente identificado todavía.
    /// </summary>
    public List<DateTime> HorariosLibresDelProfesional(int idProfesional, int dias, int maximo) =>
        CalcularHorariosLibres(idProfesional, dias, maximo, _ => true);

    /// <summary>Días hacia adelante en los que se busca el primer turno de una asignación nueva.</summary>
    public const int DiasPrimerTurno = 60;

    /// <summary>
    /// Primer turno de una asignación nueva (Mis pacientes › Confirmar): el primer horario libre de la agenda del
    /// profesional para ese paciente, sin elegir. Como siempre se toma el más temprano, los primeros turnos quedan uno
    /// detrás de otro y no quedan huecos libres entre ellos. null si no hay ninguno en <see cref="DiasPrimerTurno"/> días.
    /// </summary>
    public DateTime? PrimerHorarioLibre(int idPaciente, int idProfesional) =>
        HorariosLibres(idPaciente, idProfesional, DiasPrimerTurno, 1) is [var primero, ..] ? primero : null;

    private List<DateTime> CalcularHorariosLibres(int idProfesional, int dias, int maximo, Func<DateTime, bool> pasaRN07, int? idPaciente = null)
    {
        var ahora   = Ahora;
        _repo.VencerPropuestas(ahora);   // una propuesta de asignación vencida libera su horario (migración 32)
        // Las franjas de admisión (migración 26) no se ofrecen a los pacientes: ahí van las entrevistas.
        var agenda  = _repo.AgendaDe(idProfesional).Where(a => a.Disponible && !a.EsAdmision).ToList();
        var result  = new List<DateTime>();
        // Migración 33: feriados, bloqueos y matrícula, una sola consulta para todo el rango.
        var restricciones = _repo.Restricciones(idProfesional, ahora.Date, ahora.Date.AddDays(Math.Max(dias, 1)));

        for (var d = 0; d < dias && result.Count < maximo; d++)
        {
            var fecha  = ahora.Date.AddDays(d);
            var franjas = agenda.Where(a => DiaDeLaSemana(a.Dia) == fecha.DayOfWeek && VigenteEl(a, fecha)).ToList();
            if (franjas.Count == 0) continue;
            if (restricciones.MatriculaVencida(fecha)) break;   // de acá en adelante, todo vencido
            if (!pasaRN07(fecha)) continue;

            // Ocupado = con un turno activo o con una propuesta de asignación pendiente de otro paciente (la propia no cuenta).
            var ocupadas = _repo.HorasOcupadas(idProfesional, fecha).Concat(_repo.HorasReservadasPorPropuestas(idProfesional, fecha, idPaciente, ahora)).ToHashSet();
            foreach (var (slot, duracion) in SlotsConDuracion(franjas).OrderBy(s => s.Hora))
            {
                var momento = fecha + slot;
                if (momento <= ahora || ocupadas.Contains(slot)) continue;   // mismo criterio que ValidarSolicitud (RN-01)
                if (restricciones.MotivoNoDisponible(momento, duracion) is not null) continue;   // RN-08
                result.Add(momento);
                if (result.Count == maximo) break;
            }
        }
        return result;
    }

    /// <summary>
    /// ¿Tiene ese profesional horarios de atención (franjas disponibles de turnos, no de admisión)? Sin ellos no se le
    /// puede derivar un paciente: no tendría dónde darle el primer turno.
    /// </summary>
    public bool TieneAgendaDeTurnos(int idProfesional) =>
        _repo.AgendaDe(idProfesional).Any(a => a.Disponible && !a.EsAdmision && a.HoraInicio.HasValue && a.HoraFin.HasValue && DiaDeLaSemana(a.Dia) is not null && NoVencida(a));

    /// <summary>¿Tiene ese profesional alguna franja disponible marcada para entrevistas de admisión?</summary>
    public bool TieneAgendaDeAdmision(int idProfesional) =>
        _repo.AgendaDe(idProfesional).Any(a => a.Disponible && a.EsAdmision && a.HoraInicio.HasValue && a.HoraFin.HasValue && DiaDeLaSemana(a.Dia) is not null && NoVencida(a));

    /// <summary>
    /// El primer horario libre de las franjas de admisión del profesional, posterior a ahora y no después de
    /// <paramref name="limite"/> (el plazo de 24 horas: PlazoEntrevistaAdmision). Libre = sin un turno activo ni otra
    /// entrevista de admisión de ese profesional a esa hora; así las entrevistas quedan una detrás de otra. null si no hay.
    /// </summary>
    public DateTime? PrimerHorarioDeAdmision(int idProfesional, DateTime limite)
    {
        var ahora = Ahora;
        var agenda = _repo.AgendaDe(idProfesional).Where(a => a.Disponible && a.EsAdmision).ToList();
        for (var fecha = ahora.Date; fecha <= limite.Date; fecha = fecha.AddDays(1))
        {
            var franjas = agenda.Where(a => DiaDeLaSemana(a.Dia) == fecha.DayOfWeek && VigenteEl(a, fecha)).ToList();
            if (franjas.Count == 0) continue;
            var ocupadas = _repo.HorasOcupadas(idProfesional, fecha).Concat(_repo.EntrevistasDeAdmision(idProfesional, fecha)).ToHashSet();
            foreach (var slot in Slots(franjas).Distinct().OrderBy(s => s))
            {
                var momento = fecha + slot;
                if (momento <= ahora || ocupadas.Contains(slot)) continue;
                if (momento > limite) return null;
                return momento;
            }
        }
        return null;
    }

    /// <summary>
    /// Migración 33: horarios de la agenda de ese día que no se pueden dar por feriado, bloqueo o matrícula vencida.
    /// Los usa GET /turnos/ocupados para que las pantallas que arman los horarios por su cuenta no los ofrezcan.
    /// </summary>
    public List<TimeSpan> HorasNoDisponibles(int idProfesional, DateTime fecha)
    {
        var franjas = _repo.AgendaDe(idProfesional)
            .Where(a => a.Disponible && DiaDeLaSemana(a.Dia) == fecha.DayOfWeek && VigenteEl(a, fecha)).ToList();
        if (franjas.Count == 0) return new();
        var restricciones = _repo.Restricciones(idProfesional, fecha.Date, fecha.Date);
        return SlotsConDuracion(franjas)
            .Where(s => restricciones.MotivoNoDisponible(fecha.Date + s.Hora, s.Duracion) is not null)
            .Select(s => s.Hora).Distinct().OrderBy(h => h).ToList();
    }

    /// <summary>Migración 33: por qué no se puede mover un turno a ese momento (reprogramación), o null.</summary>
    public string? MotivoNoDisponible(int idProfesional, DateTime momento) =>
        _repo.Restricciones(idProfesional, momento.Date, momento.Date).MotivoNoDisponible(momento, TimeSpan.FromMinutes(1));

    public ResultadoRegla ValidarSolicitud(int idPaciente, int idProfesional, DateTime momento)
    {
        var ahora = Ahora;

        // RN-01: en el futuro y dentro de una franja de agenda, alineado al intervalo.
        if (momento <= ahora)
            return ResultadoRegla.Falla("RN-01", "Ese horario ya pasó.");
        var franjas = _repo.AgendaDe(idProfesional)
            .Where(a => a.Disponible && !a.EsAdmision && DiaDeLaSemana(a.Dia) == momento.DayOfWeek && VigenteEl(a, momento)).ToList();
        if (!Slots(franjas).Contains(momento.TimeOfDay))
            return ResultadoRegla.Falla("RN-01", "Ese horario no está dentro de la agenda del profesional.");

        // RN-08 (migración 33): feriado, bloqueo del profesional o matrícula vencida.
        var duracion = SlotsConDuracion(franjas).Where(s => s.Hora == momento.TimeOfDay).Select(s => s.Duracion).FirstOrDefault();
        var motivo = _repo.Restricciones(idProfesional, momento.Date, momento.Date).MotivoNoDisponible(momento, duracion);
        if (motivo is not null)
            return ResultadoRegla.Falla("RN-08", $"Ese horario no está disponible: {motivo}.");

        // RN-01: libre (un turno activo, o una propuesta de asignación pendiente de otro paciente: la propia no cuenta)
        if (_repo.HorasOcupadas(idProfesional, momento.Date).Contains(momento.TimeOfDay)
            || _repo.HorasReservadasPorPropuestas(idProfesional, momento.Date, idPaciente, ahora).Contains(momento.TimeOfDay))
            return ResultadoRegla.Falla("RN-01", "Ese horario ya está ocupado.");

        // RN-07
        if (_repo.ContarTurnosActivos(idPaciente, idProfesional, momento.Date) > 0)
            return ResultadoRegla.Falla("RN-07", "Ya tenés un turno con ese profesional ese día.");

        // RN-02 (solo si se configuró el modo estricto)
        if (_opciones.CurrentValue.ExigirConsentimientoParaSolicitar && !TieneConsentimiento(idPaciente))
            return ResultadoRegla.Falla("RN-02", "Antes de pedir un turno tenés que firmar el consentimiento informado.", UrlConsentimiento);

        return ResultadoRegla.Valido;
    }

    /// <summary>
    /// Valida y crea el turno en estado Pendiente, igual que POST /turnos/solicitar.
    /// <paramref name="creadoPor"/> es "paciente" por defecto (el paciente pide, o el bot en
    /// su nombre); el profesional proponiendo un turno futuro (POST /turnos/profesional-solicitar
    /// sin Cita urgente) pasa "profesional" — es lo que ya distinguía "Pedido por" en la
    /// tabla de turnos antes de esta fase, no se inventa una categoría nueva.
    /// </summary>
    public ResultadoSolicitud Solicitar(int idPaciente, int idProfesional, DateTime momento, string? motivo, string creadoPor = "paciente")
    {
        _repo.VencerPropuestas(Ahora);   // una propuesta de asignación vencida libera su horario (migración 32)
        var regla = ValidarSolicitud(idPaciente, idProfesional, momento);
        if (!regla.Ok) return new ResultadoSolicitud(regla, null);

        var id = InsertarPendiente(idPaciente, idProfesional, momento, motivo, creadoPor);
        return id is null
            ? new ResultadoSolicitud(ResultadoRegla.Falla("RN-01", "Ese horario se acaba de ocupar."), null)
            : new ResultadoSolicitud(ResultadoRegla.Valido, id);
    }

    /// <summary>
    /// Cita urgente (POST /turnos/profesional-solicitar con esUrgente=true, Fase 3a Paso 1):
    /// el profesional inicia una sesión ahora mismo, así que no tiene sentido pedir RN-01
    /// (dentro de una franja futura) — es la naturaleza de
    /// "urgente", y así se comportaba antes de esta fase (sin ninguna regla). Lo único que
    /// se suma es la inserción a prueba de doble reserva: si el profesional ya tiene otra
    /// cosa exactamente a esa hora, o el paciente ya tiene otro turno activo ese mismo día
    /// con él, no se crea un duplicado (antes sí se hubiera creado).
    /// </summary>
    public ResultadoSolicitud SolicitarUrgente(int idPaciente, int idProfesional, DateTime momento, string? motivo)
    {
        // Migración 33: sin matrícula vigente no se atiende, tampoco de urgencia. Feriados y bloqueos no frenan una
        // urgencia: si el profesional decide atender, atiende.
        if (_repo.Restricciones(idProfesional, momento.Date, momento.Date).MatriculaVencida(momento))
            return new ResultadoSolicitud(ResultadoRegla.Falla("RN-08", "Tu matrícula no está vigente: no podés iniciar una cita."), null);
        var id = InsertarPendiente(idPaciente, idProfesional, momento, motivo, "profesional");
        return id is null
            ? new ResultadoSolicitud(ResultadoRegla.Falla("RN-07", "Ya hay un turno activo para ese profesional y paciente ese día, o el horario está ocupado."), null)
            : new ResultadoSolicitud(ResultadoRegla.Valido, id);
    }

    private int? InsertarPendiente(int idPaciente, int idProfesional, DateTime momento, string? motivo, string creadoPor)
    {
        var idPendiente = _repo.IdEstado("Pendiente")
            ?? throw new InvalidOperationException("No existe el estado 'Pendiente' en EstadoTurno.");
        return _repo.InsertarSiLibre(new Turno
        {
            IdPaciente    = idPaciente,
            IdProfesional = idProfesional,
            IdEstadoTurno = idPendiente,
            Fecha         = momento.Date,
            Hora          = momento.TimeOfDay,
            Modalidad     = "Virtual",
            Motivo        = motivo!   // Turno.Motivo admite NULL en la base
        }, creadoPor);
    }

    public bool TieneConsentimiento(int idPaciente) =>
        _repo.TieneConsentimiento(idPaciente, _consentimiento.CurrentValue.TipoRequerido, _consentimiento.CurrentValue.VersionTexto);

    /// <summary>
    /// RN-02 para un turno que va a pasar a Confirmado. Si ya estaba Confirmado no se
    /// valida (la regla aplica hacia adelante). Turno null = no hay nada que validar.
    /// </summary>
    public ResultadoRegla ValidarPasoAConfirmado(Turno? actual)
    {
        if (actual is null || _repo.DescripcionEstado(actual.IdEstadoTurno) == "Confirmado")
            return ResultadoRegla.Valido;
        return TieneConsentimiento(actual.IdPaciente)
            ? ResultadoRegla.Valido
            : ResultadoRegla.Falla("RN-02",
                "El paciente todavía no firmó el consentimiento informado para atención por telemedicina. " +
                "Tiene que firmarlo antes de que se confirme el turno.", UrlConsentimiento);
    }

    /// <summary>
    /// RN-02 para PUT /turnos/{id}, que recibe el turno entero: solo se valida si el
    /// cambio lo lleva a Confirmado. Rechazar, cancelar o editar otros datos no se frena.
    /// </summary>
    public ResultadoRegla ValidarCambioDeEstado(Turno? anterior, Turno nuevo)
    {
        if (anterior is null || _repo.DescripcionEstado(nuevo.IdEstadoTurno) != "Confirmado")
            return ResultadoRegla.Valido;
        return ValidarPasoAConfirmado(new Turno
        {
            IdTurno = anterior.IdTurno, IdPaciente = nuevo.IdPaciente, IdEstadoTurno = anterior.IdEstadoTurno
        });
    }

    public ResultadoRegla ValidarCancelacion(Turno t, int idPaciente)
    {
        if (t.IdPaciente != idPaciente)
            return ResultadoRegla.Falla("PERMISO", "Ese turno no es tuyo.");
        var estado = _repo.DescripcionEstado(t.IdEstadoTurno);
        if (estado is not ("Pendiente" or "Confirmado"))
            return ResultadoRegla.Falla("ESTADO", $"Ese turno no se puede cancelar porque está {estado?.ToLower() ?? "en otro estado"}.");
        if (t.Fecha.Date + t.Hora < Ahora.AddHours(HorasMinimasAnticipacion))
            return ResultadoRegla.Falla("RN-05", $"Faltan menos de {HorasMinimasAnticipacion} horas para el turno, así que ya no se puede cancelar por este medio.");
        return ResultadoRegla.Valido;
    }

    public ResultadoRegla Cancelar(int idTurno, int idPaciente)
    {
        var t = _repo.ObtenerTurno(idTurno);
        if (t is null) return ResultadoRegla.Falla("NO_EXISTE", "No encontré ese turno.");
        var regla = ValidarCancelacion(t, idPaciente);
        if (!regla.Ok) return regla;
        var idCancelado = _repo.IdEstado("Cancelado")
            ?? throw new InvalidOperationException("No existe el estado 'Cancelado' en EstadoTurno.");
        _repo.CambiarEstado(idTurno, idCancelado);
        return ResultadoRegla.Valido;
    }

    // Vigencia de la franja (migración 27): FechaInicio y FechaFin inclusive; null = sin límite de ese lado.
    public static bool VigenteEl(Agenda a, DateTime fecha) =>
        (a.FechaInicio is null || fecha.Date >= a.FechaInicio.Value.Date) && (a.FechaFin is null || fecha.Date <= a.FechaFin.Value.Date);

    // Una franja que ya terminó no cuenta como agenda (para derivar o programar entrevistas).
    private bool NoVencida(Agenda a) => a.FechaFin is null || a.FechaFin.Value.Date >= Ahora.Date;

    // Slots de todas las franjas: desde HoraInicio, cada IntervaloMinutos, mientras
    // el turno completo entre antes de HoraFin (misma cuenta que cargarHorasSolicitud en la web).
    internal static IEnumerable<TimeSpan> Slots(IEnumerable<Agenda> franjas)
    {
        foreach (var a in franjas)
        {
            if (a.HoraInicio is null || a.HoraFin is null) continue;
            var intervalo = TimeSpan.FromMinutes(a.IntervaloMinutos > 0 ? a.IntervaloMinutos : 30);
            for (var h = a.HoraInicio.Value; h + intervalo <= a.HoraFin.Value; h += intervalo)
                yield return h;
        }
    }

    // Lo mismo que Slots, con la duración de cada turno (el intervalo de su franja).
    private static IEnumerable<(TimeSpan Hora, TimeSpan Duracion)> SlotsConDuracion(IEnumerable<Agenda> franjas)
    {
        foreach (var a in franjas)
        {
            if (a.HoraInicio is null || a.HoraFin is null) continue;
            var intervalo = TimeSpan.FromMinutes(a.IntervaloMinutos > 0 ? a.IntervaloMinutos : 30);
            for (var h = a.HoraInicio.Value; h + intervalo <= a.HoraFin.Value; h += intervalo)
                yield return (h, intervalo);
        }
    }

    // Agenda.Dia se guarda como texto ('lunes', 'miercoles', ...), a veces con tilde.
    public static DayOfWeek? DiaDeLaSemana(string? dia) => TextoNormalizado.Normalizar(dia) switch
    {
        "lunes"     => DayOfWeek.Monday,
        "martes"    => DayOfWeek.Tuesday,
        "miercoles" => DayOfWeek.Wednesday,
        "jueves"    => DayOfWeek.Thursday,
        "viernes"   => DayOfWeek.Friday,
        "sabado"    => DayOfWeek.Saturday,
        "domingo"   => DayOfWeek.Sunday,
        _           => null
    };
}
