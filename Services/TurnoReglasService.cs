using Microsoft.Extensions.Options;

public record ResultadoRegla(bool Ok, string Codigo = "", string Mensaje = "")
{
    public static readonly ResultadoRegla Valido = new(true);
    public static ResultadoRegla Falla(string codigo, string mensaje) => new(false, codigo, mensaje);
}

public record ResultadoSolicitud(ResultadoRegla Regla, int? IdTurno);

/// <summary>
/// Reglas de negocio de turnos (Encuentro 5), en un solo lugar del backend:
///  RN-01  Disponibilidad real de agenda: el horario cae dentro de la franja de una
///         fila de Agenda disponible para ese día, alineado a los slots de
///         IntervaloMinutos, en el futuro y sin otro turno que no esté Cancelado
///         ni Rechazado.
///  RN-02  Consentimiento firmado. Se exige al CONFIRMAR; al solicitar solo si
///         Mensajeria:ExigirConsentimientoParaSolicitar = true.
///  RN-05  Anticipación mínima de 24 h para pedir y para cancelar.
///  RN-07  Un paciente no puede tener dos turnos Pendientes o Confirmados con el
///         mismo profesional el mismo día.
/// Hoy lo usa el bot. Los endpoints web existentes (POST /turnos/solicitar,
/// PUT /turnos/{id}/cancelar) todavía NO pasan por acá: pendiente de otra rama.
///
/// Zona horaria: Turno.Fecha y Turno.Hora se guardan como hora local sin zona.
/// Se compara contra la hora local del servidor (TimeProvider.GetLocalNow),
/// que se asume en Argentina (UTC-3). Si el servidor corriera en otra zona,
/// hay que fijar TZ o convertir explícitamente.
/// </summary>
public class TurnoReglasService
{
    public const int HorasMinimasAnticipacion = 24;

    private readonly ITurnoRepositorio _repo;
    private readonly TimeProvider _reloj;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;

    public TurnoReglasService(ITurnoRepositorio repo, TimeProvider reloj, IOptionsMonitor<MensajeriaOptions> opciones)
    {
        _repo     = repo;
        _reloj    = reloj;
        _opciones = opciones;
    }

    public DateTime Ahora => _reloj.GetLocalNow().DateTime;

    /// <summary>
    /// Próximos horarios que cumplen RN-01, RN-05 y RN-07 para ese paciente y profesional,
    /// recorriendo como máximo <paramref name="dias"/> días desde hoy.
    /// </summary>
    public List<DateTime> HorariosLibres(int idPaciente, int idProfesional, int dias, int maximo)
    {
        var ahora   = Ahora;
        var minimo  = ahora.AddHours(HorasMinimasAnticipacion);
        var agenda  = _repo.AgendaDe(idProfesional).Where(a => a.Disponible).ToList();
        var result  = new List<DateTime>();

        for (var d = 0; d < dias && result.Count < maximo; d++)
        {
            var fecha  = ahora.Date.AddDays(d);
            var franjas = agenda.Where(a => DiaDeLaSemana(a.Dia) == fecha.DayOfWeek).ToList();
            if (franjas.Count == 0) continue;
            if (_repo.ContarTurnosActivos(idPaciente, idProfesional, fecha) > 0) continue; // RN-07

            var ocupadas = _repo.HorasOcupadas(idProfesional, fecha);
            foreach (var slot in Slots(franjas).OrderBy(s => s))
            {
                var momento = fecha + slot;
                if (momento < minimo || ocupadas.Contains(slot)) continue;
                result.Add(momento);
                if (result.Count == maximo) break;
            }
        }
        return result;
    }

    public ResultadoRegla ValidarSolicitud(int idPaciente, int idProfesional, DateTime momento)
    {
        var ahora = Ahora;

        // RN-01: en el futuro y dentro de una franja de agenda, alineado al intervalo.
        if (momento <= ahora)
            return ResultadoRegla.Falla("RN-01", "Ese horario ya pasó.");
        var franjas = _repo.AgendaDe(idProfesional)
            .Where(a => a.Disponible && DiaDeLaSemana(a.Dia) == momento.DayOfWeek).ToList();
        if (!Slots(franjas).Contains(momento.TimeOfDay))
            return ResultadoRegla.Falla("RN-01", "Ese horario no está dentro de la agenda del profesional.");

        // RN-05
        if (momento < ahora.AddHours(HorasMinimasAnticipacion))
            return ResultadoRegla.Falla("RN-05", $"Los turnos se piden con al menos {HorasMinimasAnticipacion} horas de anticipación.");

        // RN-01: libre
        if (_repo.HorasOcupadas(idProfesional, momento.Date).Contains(momento.TimeOfDay))
            return ResultadoRegla.Falla("RN-01", "Ese horario ya está ocupado.");

        // RN-07
        if (_repo.ContarTurnosActivos(idPaciente, idProfesional, momento.Date) > 0)
            return ResultadoRegla.Falla("RN-07", "Ya tenés un turno con ese profesional ese día.");

        // RN-02 (solo si se configuró el modo estricto)
        if (_opciones.CurrentValue.ExigirConsentimientoParaSolicitar && !_repo.TieneConsentimiento(idPaciente))
            return ResultadoRegla.Falla("RN-02", "Antes de pedir un turno tenés que firmar el consentimiento informado.");

        return ResultadoRegla.Valido;
    }

    /// <summary>
    /// Valida y crea el turno en estado Pendiente, igual que POST /turnos/solicitar.
    /// </summary>
    public ResultadoSolicitud Solicitar(int idPaciente, int idProfesional, DateTime momento, string? motivo)
    {
        var regla = ValidarSolicitud(idPaciente, idProfesional, momento);
        if (!regla.Ok) return new ResultadoSolicitud(regla, null);

        var idPendiente = _repo.IdEstado("Pendiente")
            ?? throw new InvalidOperationException("No existe el estado 'Pendiente' en EstadoTurno.");

        var id = _repo.InsertarSiLibre(new Turno
        {
            IdPaciente    = idPaciente,
            IdProfesional = idProfesional,
            IdEstadoTurno = idPendiente,
            Fecha         = momento.Date,
            Hora          = momento.TimeOfDay,
            Modalidad     = "Virtual",
            Motivo        = motivo!   // Turno.Motivo admite NULL en la base
        }, "paciente");

        return id is null
            ? new ResultadoSolicitud(ResultadoRegla.Falla("RN-01", "Ese horario se acaba de ocupar."), null)
            : new ResultadoSolicitud(ResultadoRegla.Valido, id);
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

    // Slots de todas las franjas: desde HoraInicio, cada IntervaloMinutos, mientras
    // el turno completo entre antes de HoraFin (misma cuenta que cargarHorasSolicitud en la web).
    private static IEnumerable<TimeSpan> Slots(IEnumerable<Agenda> franjas)
    {
        foreach (var a in franjas)
        {
            if (a.HoraInicio is null || a.HoraFin is null) continue;
            var intervalo = TimeSpan.FromMinutes(a.IntervaloMinutos > 0 ? a.IntervaloMinutos : 30);
            for (var h = a.HoraInicio.Value; h + intervalo <= a.HoraFin.Value; h += intervalo)
                yield return h;
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
