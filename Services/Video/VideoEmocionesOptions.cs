// Sección "Video:Emociones" (appsettings.json y variables de entorno Video__Emociones__*): consentimiento, cifrado y
// retención de la lectura de emociones de la sala de video (PR 6). Lo NO secreto tiene su valor por defecto acá. La clave
// de cifrado NUNCA va en appsettings.json: solo por variable de entorno (el env file del servicio, ver
// docs/DESPLIEGUE_VIDEO.md) o user-secrets en desarrollo.
//
// Nada de esto hace fallar el arranque: sin clave de cifrado la API levanta igual y la lectura de emociones queda
// "no disponible" (la videollamada anda igual, sin análisis). Un valor numérico fuera de rango se acota.
public class VideoEmocionesOptions
{
    // Base64 de 32 bytes (AES-256). Con ella se cifra lo que se guarda en SesionClinica.HistorialEmociones. Si se
    // pierde, lo guardado no se puede volver a leer: guardar una copia FUERA del servidor y del repositorio.
    public string? ClaveCifrado { get; set; }

    // Identificador de la clave, va en cada dato cifrado ("ys1.<IdClave>.<...>"). Sirve para saber con qué clave se
    // cifró cuando algún día haya que rotarla.
    public string IdClave { get; set; } = "k1";

    // Qué versión del texto del consentimiento se muestra (archivo Services/Video/Consentimiento/<versión>.txt).
    // Una versión publicada no se edita: un texto nuevo es una versión nueva.
    public string VersionTexto { get; set; } = "v0-provisorio";

    // Cuánto tiempo se conservan las emociones guardadas. PROVISORIO: el plazo lo tiene que definir el equipo legal
    // (puede ser que formen parte de la historia clínica; ver docs/VIDEOLLAMADAS.md). 0 = no se purga nunca.
    public int RetencionDias { get; set; } = 3650;

    // La purga arranca DESHABILITADA. Sin este flag solo deja en el log cuántas filas purgaría: no modifica nada.
    public bool PurgaHabilitada { get; set; }
    public int PurgaCadaHoras { get; set; } = 24;
    public int PurgaLote { get; set; } = 500;

    // Lo que la API le dice a la pantalla del profesional: cada cuántos segundos preguntar si sigue el permiso, y
    // cuánto vale una respuesta. Si pasa ese tiempo sin una respuesta, la pantalla frena el análisis (falla cerrada).
    public int ConsultaCadaSegundos { get; set; } = 3;
    public int VigenciaSegundos { get; set; } = 10;

    // Holgura de relojes (el del navegador contra el del servidor) al validar la ventana de lo que se guarda.
    public int ToleranciaSegundos { get; set; } = 5;

    // Tope de muestras por segundo que se acepta (la pantalla toma una cada ~0,5 s): una cantidad imposible se rechaza.
    public int MuestrasMaxPorSegundo { get; set; } = 4;

    // Una persona sin fecha de nacimiento cargada (y sin tutor) no se puede comprobar que sea mayor de edad: por
    // defecto no se le ofrece el análisis. Con true se la trata como adulta (útil en una base de desarrollo).
    public bool PermitirEdadDesconocida { get; set; }

    // Pedidos por minuto y por IP real a los endpoints de consentimiento y de permiso (política "video-consentimiento").
    // La pantalla del profesional pregunta cada 3 s: 20 por minuto por persona; 240 sobra para una oficina entera.
    public int LimitePorMinuto { get; set; } = 240;
}

// Las siete expresiones que devuelve el modelo (face-api) y cómo se llaman en pantalla. Mismo orden y mismos textos
// que el EMOCIONES de PANTALLAS/Pantalla_Profesional/sesion_video.html y que lo que ya se guardó desde siempre.
public static class EmocionesCatalogo
{
    public static readonly IReadOnlyDictionary<string, string> Etiquetas = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["neutral"] = "Neutral",
        ["happy"] = "Felicidad",
        ["sad"] = "Tristeza",
        ["angry"] = "Enojo",
        ["fearful"] = "Miedo",
        ["surprised"] = "Sorpresa",
        ["disgusted"] = "Disgusto"
    };
}
