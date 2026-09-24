// Sección "Mensajeria" de appsettings.json. Solo valores NO secretos:
// cualquier token futuro (Meta, etc.) va por dotnet user-secrets o variables de entorno.
public class MensajeriaOptions
{
    public const string Seccion = "Mensajeria";

    // Canal por el que salen los mensajes. Hoy solo existe "Simulado".
    public string CanalActivo { get; set; } = "Simulado";

    public int RecordatorioHorasAntes { get; set; } = 24;
    public int IntervaloChequeoMinutos { get; set; } = 5;
    public int TimeoutConversacionMinutos { get; set; } = 30;
    public int MaxIntentosEnvio { get; set; } = 3;

    // Apaga todos los endpoints /mensajeria (chat simulado y administración).
    public bool ChatSimuladoHabilitado { get; set; }
    // Rechaza pedidos a /mensajeria que no vengan de la propia máquina.
    public bool SoloLocal { get; set; } = true;

    public string UrlRegistroWeb { get; set; } = "";
    public string UrlConsentimiento { get; set; } = "";
    public string UrlIngresoTurno { get; set; } = "";

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

public class LineaEmergencia
{
    public string Nombre { get; set; } = "";
    public string Telefono { get; set; } = "";
}
