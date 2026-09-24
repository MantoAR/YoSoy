using Microsoft.Extensions.Options;

// Seguridad clínica, validada con una psicóloga: ante riesgo de vida lo que
// corresponde es derivar a guardia o a emergencias, no contener por la plataforma.
// El bot tampoco da consejos clínicos, diagnósticos ni contención emocional.
// Las listas de palabras están en configuración (Mensajeria:PalabrasRiesgo y
// Mensajeria:PalabrasClinicas), no en el código.
public class FiltroSeguridad
{
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;

    public FiltroSeguridad(IOptionsMonitor<MensajeriaOptions> opciones) => _opciones = opciones;

    // Coincidencia por contenido (no por palabra completa) a propósito: "suicid"
    // cubre "suicidio" y "suicidarme". Ante la duda es preferible un falso positivo.
    public bool EsRiesgo(string texto) => Contiene(texto, _opciones.CurrentValue.PalabrasRiesgo);

    public bool EsConsultaClinica(string texto) => Contiene(texto, _opciones.CurrentValue.PalabrasClinicas);

    // Mensaje fijo y breve. Los teléfonos salen de Mensajeria:LineasEmergencia
    // y tienen que ser completados y validados por el equipo antes de usarse.
    public string MensajeEmergencia()
    {
        var lineas = _opciones.CurrentValue.LineasEmergencia
            .Where(l => !string.IsNullOrWhiteSpace(l.Nombre) || !string.IsNullOrWhiteSpace(l.Telefono))
            .Select(l => $"• {l.Nombre}: {l.Telefono}");
        return "Lo que escribiste es importante y necesita atención inmediata. " +
               "Este chat no puede ayudarte en una urgencia: por favor buscá ayuda ahora.\n\n" +
               string.Join("\n", lineas) +
               "\n\nSi estás en peligro, andá a la guardia más cercana o llamá a emergencias. " +
               "Ya le avisamos al equipo de YoSoy.";
    }

    public const string MensajeConsultaClinica =
        "No puedo responder consultas sobre síntomas, medicación o tratamientos: " +
        "eso lo trabaja tu profesional en la sesión. " +
        "Si querés, escribí 1 para pedir un turno o 5 para hablar con una persona.";

    private static bool Contiene(string texto, List<string> frases)
    {
        var t = TextoNormalizado.Normalizar(texto);
        if (t.Length == 0) return false;
        return frases
            .Select(TextoNormalizado.Normalizar)
            .Where(f => f.Length > 0)
            .Any(f => t.Contains(f));
    }
}
