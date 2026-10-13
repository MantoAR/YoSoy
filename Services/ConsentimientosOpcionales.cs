// Consentimientos opcionales del paciente (RN-10, RN-15). A diferencia del de telemedicina
// (RN-02, obligatorio para confirmar turnos), estos se firman y se revocan por separado, y
// ninguno condiciona la atención: solo habilitan una función.
//
// Cada tipo se guarda como una fila de TipoConsentimiento (la crea
// migrations/add_atencion_consentimientos_ia_evolucion.sql) y se busca por Descripcion,
// igual que el de telemedicina. La "clave" es lo que viaja en las URLs y en el frontend.
public record TipoConsentimientoOpcional(string Clave, string Descripcion, string Titulo, string Resumen, string Funcion);

public static class ConsentimientosOpcionales
{
    public const string ClaveIa = "ia-expresiones";
    public const string ClaveTranscripcion = "transcripcion";
    public const string ClaveWhatsapp = "whatsapp";

    public static readonly TipoConsentimientoOpcional Ia = new(
        ClaveIa,
        "Consentimiento Opcional - Análisis de expresiones faciales por IA",
        "Análisis de expresiones faciales por IA",
        "Durante la videollamada, un programa que corre en la computadora de tu profesional puede " +
        "estimar las expresiones de tu cara (por ejemplo, alegría o tristeza) para darle un dato de " +
        "apoyo. No hace diagnósticos ni reemplaza el criterio de tu profesional. No se graban ni se " +
        "envían imágenes: solo se guarda un resumen de las expresiones estimadas. Es una herramienta " +
        "experimental y puede equivocarse según la luz, la cámara o la persona. Cuando esté activa vas " +
        "a ver un aviso en pantalla, y podés pedir que se apague en cualquier momento.",
        FuncionesSesion.IaExpresiones);

    public static readonly TipoConsentimientoOpcional Transcripcion = new(
        ClaveTranscripcion,
        "Consentimiento Opcional - Transcripción de la sesión",
        "Transcripción de la sesión",
        "Tu profesional puede pasar a texto lo que se dice en la sesión para ayudarse a escribir la " +
        "evolución. El texto es un borrador que tu profesional revisa y corrige; la transcripción " +
        "completa no queda en tu historia clínica salvo que la organización lo haya configurado así y " +
        "solo por el plazo indicado. Cuando esté activa vas a ver un aviso en pantalla, y podés pedir " +
        "que se apague en cualquier momento.",
        FuncionesSesion.Transcripcion);

    public static readonly TipoConsentimientoOpcional Whatsapp = new(
        ClaveWhatsapp,
        "Consentimiento Opcional - Avisos por WhatsApp",
        "Avisos por WhatsApp",
        "Podés recibir por WhatsApp recordatorios de turnos y avisos administrativos. Nunca se envía " +
        "por ese medio información sobre tu salud ni el contenido de tus sesiones.",
        Funcion: "");

    public static readonly IReadOnlyList<TipoConsentimientoOpcional> Todos = new[] { Ia, Transcripcion, Whatsapp };

    public static TipoConsentimientoOpcional? PorClave(string? clave) =>
        Todos.FirstOrDefault(t => t.Clave == clave);

    public static TipoConsentimientoOpcional? PorFuncion(string funcion) =>
        Todos.FirstOrDefault(t => t.Funcion == funcion && funcion != "");
}

// Sección "Atencion" de appsettings.json.
public class AtencionOptions
{
    public const string Seccion = "Atencion";

    // Versión del texto de los consentimientos opcionales. Se guarda con cada firma; si
    // cambia, la firma anterior sigue valiendo para lo que dice, pero el paciente ve el texto
    // nuevo la próxima vez. Cambiarla al cargar los textos validados por el equipo.
    public string VersionTextoOpcionales { get; set; } = "sin-validar";

    // false (por defecto): la transcripción completa nunca se guarda; sirve solo para armar
    // el borrador de la evolución en el navegador del profesional.
    public bool ConservarTranscripcion { get; set; } = false;

    // Motor de transcripción. "NavegadorDemo" usa el reconocimiento de voz del navegador,
    // que en Chrome y Edge envía el audio a servidores externos: solo para la demo, nunca con
    // pacientes reales. "Ninguno" apaga la transcripción y el dictado. El servicio propio
    // (Whisper local) se agrega como un tercer valor.
    public string MotorTranscripcion { get; set; } = "NavegadorDemo";
}
