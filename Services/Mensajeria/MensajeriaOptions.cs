// Sección "Mensajeria" de appsettings.json. Solo valores NO secretos:
// cualquier token futuro (Meta, etc.) va por dotnet user-secrets o variables de entorno.
public class MensajeriaOptions
{
    public const string Seccion = "Mensajeria";

    // Canal por el que salen los mensajes: "Simulado" (por defecto, no sale nada de la máquina) o "Baileys" (WhatsApp a través de
    // whatsapp-service, que usa Baileys: el único motor de WhatsApp). "WhatsAppWebJs" ya no existe: la aplicación no arranca con ese valor.
    public string CanalActivo { get; set; } = "Simulado";

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
