// Agenda, bloques 1 y 2 de la rama AAA1 (migración 33): lo que saca horarios de la agenda semanal aunque la franja
// exista. Tres cosas, en este orden:
//   1. Matrícula principal vencida: desde la fecha de vencimiento (inclusive) no se ofrece ningún turno. Se calcula, no se
//      guarda: al renovar la matrícula la agenda vuelve sola, sin que nadie tenga que "levantar" una suspensión.
//   2. Feriado cargado por el Configurador, salvo que el profesional haya marcado "trabajo este feriado".
//   3. Bloqueo del profesional (licencia, vacaciones, capacitación...), con fecha y hora de inicio y fin.
// Los turnos ya dados dentro de un bloqueo o después del vencimiento NO se cancelan solos: se listan para que el
// profesional (o quien gestiona agendas) los reprograme. Cancelar sin avisar a un paciente de salud mental es peor que
// dejar el turno a la vista para resolverlo.

public record FranjaBloqueada(DateTime Desde, DateTime Hasta, string Motivo);

public sealed class RestriccionesAgenda
{
    public static readonly RestriccionesAgenda Ninguna = new(Array.Empty<DateTime>(), Array.Empty<FranjaBloqueada>(), null);

    /// <summary>Días de feriado (sin hora) que el profesional NO trabaja.</summary>
    public IReadOnlySet<DateTime> Feriados { get; }
    public IReadOnlyList<FranjaBloqueada> Bloqueos { get; }
    /// <summary>Vencimiento de la matrícula principal (sin hora). Null = no cargado: no corta nada.</summary>
    public DateTime? VenceMatricula { get; }

    public RestriccionesAgenda(IEnumerable<DateTime> feriados, IEnumerable<FranjaBloqueada> bloqueos, DateTime? venceMatricula)
    {
        Feriados = feriados.Select(f => f.Date).ToHashSet();
        Bloqueos = bloqueos.ToList();
        VenceMatricula = venceMatricula?.Date;
    }

    public bool MatriculaVencida(DateTime fecha) => VenceMatricula is DateTime v && fecha.Date >= v;

    /// <summary>
    /// Por qué no se puede dar un turno que empieza en <paramref name="momento"/> y dura <paramref name="duracion"/>,
    /// o null si nada lo impide. El texto se le puede mostrar al paciente: no dice nada personal del profesional.
    /// </summary>
    public string? MotivoNoDisponible(DateTime momento, TimeSpan duracion)
    {
        if (MatriculaVencida(momento)) return "el profesional no tiene la matrícula vigente para esa fecha";
        if (Feriados.Contains(momento.Date)) return "es feriado";
        var fin = momento + (duracion > TimeSpan.Zero ? duracion : TimeSpan.FromMinutes(1));
        foreach (var b in Bloqueos)
            if (momento < b.Hasta && fin > b.Desde) return "el profesional no atiende en ese horario";
        return null;
    }
}

public static class MotivosBloqueo
{
    public static readonly string[] Todos = { "Licencia", "Vacaciones", "Capacitación", "Personal", "Otro" };
    public static string? Normalizar(string? motivo) =>
        Todos.FirstOrDefault(m => TextoNormalizado.Normalizar(m) == TextoNormalizado.Normalizar(motivo));
}

public static class EstadoMatricula
{
    public const int DiasDeAviso = 30;
    public const string Vigente = "Vigente", PorVencer = "Por vencer", Vencida = "Vencida", SinFecha = "Sin vencimiento cargado";

    public static string De(DateTime? vencimiento, DateTime hoy)
    {
        if (vencimiento is not DateTime v) return SinFecha;
        if (hoy.Date >= v.Date) return Vencida;
        return (v.Date - hoy.Date).TotalDays <= DiasDeAviso ? PorVencer : Vigente;
    }

    public static int? DiasRestantes(DateTime? vencimiento, DateTime hoy) =>
        vencimiento is DateTime v ? (int)(v.Date - hoy.Date).TotalDays : null;
}
