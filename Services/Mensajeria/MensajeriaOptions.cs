// Sección "Mensajeria" de appsettings.json. Solo valores NO secretos:
// cualquier token futuro (Meta, etc.) va por dotnet user-secrets o variables de entorno.
public class MensajeriaOptions
{
    public const string Seccion = "Mensajeria";

    // Canal por el que salen los mensajes: "Simulado" (por defecto, no sale nada de la máquina) o "Baileys" (WhatsApp a través de
    // whatsapp-service, que usa Baileys: el único motor de WhatsApp). "WhatsAppWebJs" ya no existe: la aplicación no arranca con ese valor.
    public string CanalActivo { get; set; } = "Simulado";

    // Quién atiende lo que la gente le escribe al número de WhatsApp de la clínica (ver ModosAtencion).
    //  · "Asistente" (por defecto, y lo que la aplicación hace hoy): el asistente de reservas contesta solo (menú, pide el DNI, turnos...).
    //  · "Personal": el chat es para el personal. El asistente NO contesta: cada mensaje se guarda y la conversación queda para el Configurador, el
    //    único rol que ve y contesta chats (los profesionales no ven ninguno). Una frase de riesgo marca la conversación como urgente y avisa en la
    //    campanita; el mensaje de emergencia al paciente es aparte (Personal.RespuestaDeEmergencia, apagado por defecto).
    // Se puede cambiar sin reiniciar la aplicación. Cualquier otro valor frena el arranque (ver AddMensajeria).
    public string ModoAtencion { get; set; } = ModosAtencion.Asistente;

    /// <summary>¿Se atiende en modo Personal? (sin distinguir mayúsculas)</summary>
    public bool EsModoPersonal => ModosAtencion.EsPersonal(ModoAtencion);

    // Lo que solo vale en modo Personal.
    public AtencionPersonalOptions Personal { get; set; } = new();

    // Los cuatro avisos de turno por evento (solicitud recibida, turno propuesto, confirmado y cancelado) POR WHATSAPP. APAGADO por defecto: el uso
    // saliente de WhatsApp está pausado por el riesgo de que bloqueen el número (es una API no oficial) y estos avisos son lo único que abre una
    // conversación sin que la persona haya escrito. Apagado:
    //  · no sale ninguno por WhatsApp (el mail y la campanita siguen igual);
    //  · TODO el consentimiento de WhatsApp queda dormido: no se ofrece en el registro ni en el perfil, no hay link wa.me ni sugerencia después de
    //    un turno, y la API no toca la tabla ConsentimientoWhatsApp (se puede publicar sin haber aplicado la migración 16).
    // No afecta a las respuestas del asistente a quien le escribe, ni a las del equipo. Para encenderlo: Mensajeria__AvisosPorWhatsApp=true, con la
    // migración 16 aplicada ANTES (docs/DEPLOY.md, "Consentimiento de WhatsApp").
    public bool AvisosPorWhatsApp { get; set; }

    // URL base de whatsapp-service para las rutas de administración (estado/desvincular,
    // ver WhatsAppAdminService) — siempre loopback, no es secreta. A diferencia del canal de
    // envío (CanalBaileys, URL fija en el código: solo se registra con CanalActivo "Baileys"),
    // esta sale de configuración para poder apuntar los tests a un listener
    // falso en vez del puerto 3002 real.
    public string WhatsAppServiceUrl { get; set; } = "http://127.0.0.1:3002/";

    public int RecordatorioHorasAntes { get; set; } = 24;
    public int IntervaloChequeoMinutos { get; set; } = 5;
    public int TimeoutConversacionMinutos { get; set; } = 30;
    public int MaxIntentosEnvio { get; set; } = 3;

    // Apaga todos los endpoints /mensajeria (chat simulado y administración).
    public bool ChatSimuladoHabilitado { get; set; }
    // Rechaza pedidos a /mensajeria que no vengan de la propia máquina.
    public bool SoloLocal { get; set; } = true;

    public string UrlRegistroWeb { get; set; } = "";
    public string UrlIngresoTurno { get; set; } = "";

    // Consentimiento de los avisos por WhatsApp (tabla ConsentimientoWhatsApp): qué texto se le muestra al paciente.
    public ConsentimientoWhatsAppOptions ConsentimientoWhatsApp { get; set; } = new();

    // El número de WhatsApp de la clínica (el que atiende whatsapp-service), para el link wa.me con el que un paciente puede escribirle primero.
    // VACÍO = no se muestra ningún link. Formato libre: "+54 9 11 5555-0000", "011 5555-0000"... (ver EnlaceWhatsApp). No es un secreto, pero tampoco
    // está escrito en el código ni en las pantallas: se configura por servidor (Mensajeria__WhatsAppNumeroClinica).
    public string WhatsAppNumeroClinica { get; set; } = "";

    // Lo que viene escrito cuando el paciente abre ese link. Neutro a propósito (no dice qué es la clínica ni para qué se escribe).
    public string WhatsAppTextoInicio { get; set; } = EnlaceWhatsApp.TextoPorDefecto;

    // RN-02: por defecto el bot crea el turno Pendiente aunque falte el
    // consentimiento (se exige al confirmar). true = comportamiento estricto.
    public bool ExigirConsentimientoParaSolicitar { get; set; }

    public int DiasBusquedaHorarios { get; set; } = 14;
    public int MaxHorariosOfrecidos { get; set; } = 5;

    // Palabras y frases que cortan el flujo y disparan el mensaje de emergencia.
    // Se comparan sin mayúsculas ni tildes. Obligatoria (se valida al arrancar).
    public List<string> PalabrasRiesgo { get; set; } = new();
    // Si el mensaje no se entiende y contiene alguna de estas, el bot aclara que
    // lo clínico lo trabaja el profesional (nunca responde consultas clínicas).
    public List<string> PalabrasClinicas { get; set; } = new();
    public List<LineaEmergencia> LineasEmergencia { get; set; } = new();
}

// Los dos modos de Mensajeria:ModoAtencion.
public static class ModosAtencion
{
    public const string Asistente = "Asistente";
    public const string Personal = "Personal";

    public static bool EsAsistente(string? valor) => string.Equals(valor?.Trim(), Asistente, StringComparison.OrdinalIgnoreCase);
    public static bool EsPersonal(string? valor) => string.Equals(valor?.Trim(), Personal, StringComparison.OrdinalIgnoreCase);
    public static bool EsValido(string? valor) => EsAsistente(valor) || EsPersonal(valor);
}

public class AtencionPersonalOptions
{
    // El mensaje de emergencia (las líneas de Mensajeria:LineasEmergencia) que se le manda a quien escribe una frase de riesgo, en modo Personal. APAGADO por defecto: las
    // líneas de appsettings.json todavía dicen "[COMPLETAR Y VALIDAR]" y un paciente en crisis no puede recibir un teléfono que no existe. Apagado, la frase de riesgo igual
    // marca la conversación como urgente y avisa al Configurador en la campanita: lo que no sale es el mensaje al paciente.
    // Aunque se ponga en true, tampoco sale mientras alguna línea esté sin completar (teléfono vacío o con el texto provisorio): el log lo avisa. Para activarlo, con líneas
    // oficiales y verificadas, ver docs/DEPLOY.md ("Mensaje de emergencia del chat para el personal").
    public bool RespuestaDeEmergencia { get; set; }
}

public class ConsentimientoWhatsAppOptions
{
    // Archivo Services/Mensajeria/Consentimiento/<versión>.txt (embebido). Una versión publicada NO se edita: un texto nuevo es una
    // versión nueva, y al cambiarla los avisos por WhatsApp se detienen hasta que cada paciente vuelva a aceptar.
    public string VersionTexto { get; set; } = "v0-provisorio";
}

public class LineaEmergencia
{
    public string Nombre { get; set; } = "";
    public string Telefono { get; set; } = "";
}
