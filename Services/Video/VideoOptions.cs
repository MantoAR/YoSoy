// Sección "Video" de appsettings.json y de las variables de entorno (Video__*). Lo NO secreto
// tiene su valor por defecto acá y se puede pisar en appsettings.json. Los dos secretos
// (SignalingSecreto y TurnSecreto) NUNCA van en appsettings.json: solo por variable de entorno
// (el env file del servicio, ver docs/VIDEOLLAMADAS.md) o user-secrets en desarrollo.
//
// Nada de esto hace fallar el arranque de la API: el video es opcional hasta que se configure.
// Un secreto que falta o es débil deshabilita /video/acceso con un 503 (ver TokenSalaService) y un
// valor numérico fuera de rango se acota (ver Acotar* en AccesoSalaService y TurnCredentialsService).
public class VideoOptions
{
    public const string Seccion = "Video";

    // Mismo valor que SIGNALING_SECRETO del signaling-server: con él se firma el token de sala (HS256).
    public string? SignalingSecreto { get; set; }
    // Mismo valor que static-auth-secret de coturn: con él se firman las credenciales TURN efímeras.
    public string? TurnSecreto { get; set; }

    public VideoVentanaOptions Ventana { get; set; } = new();
    public VideoTokenOptions Token { get; set; } = new();
    public VideoTurnOptions Turn { get; set; } = new();
    public VideoIceOptions Ice { get; set; } = new();
    public VideoAdmisionOptions Admision { get; set; } = new();

    // Consentimiento, cifrado y retención de la lectura de emociones (PR 6). Ver VideoEmocionesOptions.
    public VideoEmocionesOptions Emociones { get; set; } = new();

    // Consentimiento y grabación para la transcripción local de la sesión. Ver VideoTranscripcionOptions.
    public VideoTranscripcionOptions Transcripcion { get; set; } = new();

    // Duración de un turno que no cae en ninguna franja de la agenda del profesional. Es el mismo 30 que
    // usa el resto del sistema cuando una franja no tiene intervalo (ver TurnoReglasService.Slots).
    public int DuracionPorDefectoMinutos { get; set; } = 30;

    // Zona horaria en la que están escritas Turno.Fecha / Turno.Hora y InformeAdmision.FechaEntrevista.
    // Vacía = la de la clínica, la MISMA que usan las reglas de turnos: Turnos:ZonaHoraria o, si tampoco está,
    // America/Argentina/Buenos_Aires. Ya no depende de la zona del servidor, así que no hace falta completarla.
    // Si se completa y existe, manda para la ventana de la sala y, cuando Turnos:ZonaHoraria no está, también define
    // la zona de la clínica (es el valor de siempre: no cambia nada para quien ya lo tiene). Si no existe, se ignora.
    public string? ZonaHoraria { get; set; }

    // Pedidos por minuto y por IP real a /video/acceso (política "video").
    public int LimitePorMinuto { get; set; } = 20;
}

public class VideoVentanaOptions
{
    public int MinutosAntes { get; set; } = 10;
    public int MinutosDespues { get; set; } = 30;
}

public class VideoTokenOptions
{
    // Techo de vida del token de sala (el exp es el menor entre el fin de la ventana y ahora + esto).
    public int MaxMinutos { get; set; } = 120;
}

public class VideoTurnOptions
{
    // Vida de las credenciales TURN. Nunca menos que la ventana de la sesión ni que el token de sala.
    public int TtlMinutos { get; set; } = 120;
}

public class VideoIceOptions
{
    // Vacías por defecto A PROPÓSITO: una lista con valores en el código se SUMARÍA a la de la
    // configuración en vez de reemplazarla (así enlaza .NET las listas). Los respaldos están en
    // TurnCredentialsService.StunPorDefecto.
    public List<string> StunUrls { get; set; } = new();
    public List<string> TurnUrls { get; set; } = new();
}

public class VideoAdmisionOptions
{
    // La entrevista de admisión no tiene duración propia en la base: se asume lo mismo que un turno.
    public int DuracionMinutos { get; set; } = 30;
}
