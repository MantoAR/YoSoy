// Sección "Video:Transcripcion" (appsettings.json y variables de entorno Video__Transcripcion__*): consentimiento,
// grabación y transcripción local de la sesión de video. Lo NO secreto tiene su valor por defecto acá.
//
// Habilitada arranca en false a propósito: sin ella, la grabación y la transcripción quedan "no disponibles" (la
// videollamada anda igual, sin grabarse). Modelo, RutaPython, RutaScript y HilosCpu son del worker de transcripción
// (fuera de alcance de esta etapa, que solo sube y encola el audio) y están acá para no tener que agregar la sección
// de nuevo más adelante.
public class VideoTranscripcionOptions
{
    public bool Habilitada { get; set; }

    // Carpeta del servidor donde se guardan los fragmentos de audio subidos, un subdirectorio por transcripción
    // (IdTranscripcion). Nunca se expone por HTTP: solo la lee el worker de transcripción.
    public string CarpetaAudio { get; set; } = "/opt/yosoy/grabaciones";

    // Tope de bytes de UN fragmento subido a /video/grabacion/fragmento (un rechazo, no trunca).
    public long MaxBytesFragmento { get; set; } = 2L * 1024 * 1024;

    // Tope de bytes acumulados de UNA pista (profesional o paciente) de la grabación.
    public long MaxBytesPista { get; set; } = 300L * 1024 * 1024;

    // Tope de duración de la grabación, a partir del InicioMs de la secuencia 0 de cada pista.
    public int MaxMinutosGrabacion { get; set; } = 120;

    // Antigüedad a partir de la cual una grabación en estado "grabando" sin ningún fragmento nuevo se considera
    // huérfana (la sesión se cerró sin que el cliente avisara). Lo usa el saneamiento del worker, no esta etapa.
    public int HorasGrabacionHuerfana { get; set; } = 24;

    public string Modelo { get; set; } = "small";
    public string? RutaPython { get; set; }
    public string? RutaScript { get; set; }
    public int HilosCpu { get; set; } = 1;

    // Qué versión del texto del consentimiento de grabación se muestra (archivo
    // Services/Video/Consentimiento/<versión>.txt). Una versión publicada no se edita: un texto nuevo es una versión nueva.
    public string VersionTexto { get; set; } = "v0-provisorio-grabacion";

    // Mismo esquema que VideoEmocionesOptions: cada cuántos segundos la pantalla pregunta si sigue el permiso de
    // grabación, y cuánto vale una respuesta.
    public int ConsultaCadaSegundos { get; set; } = 3;
    public int VigenciaSegundos { get; set; } = 10;

    // Pedidos por minuto y por IP real a los endpoints de consentimiento/permiso de grabación y al de subida de
    // fragmentos (políticas "video-consentimiento" y "video-grabacion").
    public int LimitePorMinuto { get; set; } = 240;
}
