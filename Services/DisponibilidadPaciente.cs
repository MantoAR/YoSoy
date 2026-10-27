// Los horarios que ve el paciente al pedir un turno o reprogramar. Salen de UNA sola función, la misma que usa la propuesta automática de asignación
// (AsignacionAutomaticaService.HorariosLibres): franjas de atención (sin las de admisión), vigencia de cada franja, horarios ocupados y propuestas pendientes. Antes la pantalla del paciente
// los armaba sola, a partir de las franjas crudas del profesional: ignoraba las franjas de admisión y la vigencia, usaba horas redondas fijas al reprogramar y, si una consulta fallaba, dejaba el
// calendario vacío sin decir por qué. Detalle en docs/DISPONIBILIDAD.md.
public static class DisponibilidadReglas
{
    // TODO-VALIDAR: cuántas horas de anticipación se exigen para que el PACIENTE pida un turno. Hoy ninguna (se puede pedir para dentro de unos minutos, ver PR #26: se quitó la de 24 hs para pedir y se
    // conserva para cancelar). La propuesta automática de admisión tampoco (AsignacionAutomaticaReglas.AnticipacionMinimaMinutosPorDefecto = 0, más un colchón de 15 min que vale para las dos). Si se define una, el mensaje de hoy la nombra solo.
    public const int AnticipacionMinimaPedidoHoras = 0;

    public const int DiasPorDefecto = 30;
    public const int DiasMaximo = 60;

    public const string CodigoSinAgenda = "sin-agenda";
    public const string CodigoSinHorarios = "sin-horarios";
    public const string CodigoDerivado = "paciente-derivado";

    public static string MensajeSinAgenda =>
        "Tu profesional todavía no cargó horarios de atención. Escribinos o comunicate con recepción y te ayudamos a conseguir un turno.";

    public static string MensajeSinHorarios(int dias) =>
        $"Tu profesional no tiene horarios libres en los próximos {dias} días. Probá más adelante o escribinos y te avisamos cuando se libere uno.";

    /// <summary>Qué decir cuando el paciente mira HOY y no hay nada para elegir. Con anticipación mínima definida, la nombra.</summary>
    public static string MensajeHoy(int anticipacionHoras) => anticipacionHoras > 0
        ? $"Los turnos se piden con al menos {anticipacionHoras} horas de anticipación; para hoy mismo comunicate con recepción."
        : "Para hoy ya no quedan horarios libres. Si necesitás un turno hoy mismo, comunicate con recepción.";
}

public record DiaDisponible(string Fecha, List<string> Horas);

public record ResultadoDisponibilidad(List<DiaDisponible> Dias, int Total, string? Codigo, string? Mensaje, string MensajeHoy, int AnticipacionHoras);
