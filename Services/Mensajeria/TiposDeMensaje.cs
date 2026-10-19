/// <summary>
/// Los tipos de contenido de un mensaje de WhatsApp que guarda el chat: la lista cerrada de la restricción CK_MensajeChat_Tipo de la migración 17. Lo que no es texto
/// (una foto, un audio...) no se descarga ni se guarda: se guarda un AVISO del tipo en el texto del mensaje ("[Imagen]", con la leyenda a continuación si la trae) y la
/// persona del equipo mira el archivo en el teléfono del número (decisión D4).
/// </summary>
public static class TiposDeMensaje
{
    public const string Texto = "texto";
    public const string Audio = "audio";
    public const string Imagen = "imagen";
    public const string Video = "video";
    public const string Documento = "documento";
    public const string Sticker = "sticker";
    public const string Ubicacion = "ubicacion";
    public const string Contacto = "contacto";
    public const string Otro = "otro";

    /// <summary>Los nueve, en el orden de la restricción de la base.</summary>
    public static readonly IReadOnlyList<string> Todos = new[] { Texto, Audio, Imagen, Video, Documento, Sticker, Ubicacion, Contacto, Otro };

    /// <summary>Sin tipo (el cuerpo de la versión anterior de whatsapp-service) es texto; un tipo que no se conoce es "otro" (no se rechaza el mensaje por eso).</summary>
    public static string Normalizar(string? tipo)
    {
        var limpio = (tipo ?? "").Trim().ToLowerInvariant();
        if (limpio.Length == 0) return Texto;
        return Todos.Contains(limpio) ? limpio : Otro;
    }

    /// <summary>El aviso que ocupa el lugar del contenido que no se guarda (el mismo que arma whatsapp-service). Nada para el texto.</summary>
    public static string Aviso(string tipo) => tipo switch
    {
        Audio => "[Audio]",
        Imagen => "[Imagen]",
        Video => "[Video]",
        Documento => "[Documento]",
        Sticker => "[Sticker]",
        Ubicacion => "[Ubicación]",
        Contacto => "[Contacto]",
        Otro => "[Mensaje]",
        _ => ""
    };
}
