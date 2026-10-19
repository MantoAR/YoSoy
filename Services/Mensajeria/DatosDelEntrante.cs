using System.Text.RegularExpressions;

/// <summary>
/// Lo que trae un mensaje de whatsapp-service además del teléfono y el texto (el contrato v2 del webhook), ya limpio y acotado: el tipo de contenido, el id del mensaje en
/// WhatsApp, la hora a la que se escribió y el nombre del perfil de la persona. Todo es opcional: un cuerpo sin ninguno de estos campos (el de la versión anterior de
/// whatsapp-service) no genera nada de esto (<see cref="Desde"/> devuelve null) y se atiende exactamente como siempre.
///
/// Nada de lo que viene acá es de confianza (el webhook es local y con secreto, pero igual se acota) y nada rechaza el mensaje: un dato que no pasa el filtro se descarta
/// él solo (un id raro no se guarda, una hora absurda se reemplaza por la del servidor) y el mensaje entra. El nombre del perfil (decisión D5) se guarda en la conversación y
/// NUNCA va a un log ni a la auditoría.
/// </summary>
public sealed record DatosDelEntrante(string Tipo, string? IdExterno, DateTime? FechaHora, string? NombreContacto)
{
    public const int MaxId = 64;       // MensajeChat.IdExterno
    public const int MaxNombre = 100;  // Conversacion.NombreContacto

    /// <summary>Cuánto en el futuro puede estar la hora del mensaje (los relojes no coinciden del todo) antes de descartarla. Dentro de ese margen se usa la hora del servidor.</summary>
    public static readonly TimeSpan HolguraDelFuturo = TimeSpan.FromMinutes(1);

    /// <summary>Cuánto atrás (sin llegar): lo que llega con un día o más se muestra con la hora en que llegó a la API.</summary>
    public static readonly TimeSpan AntiguedadMaxima = TimeSpan.FromDays(1);

    private static readonly Regex IdValido = new(@"^[A-Za-z0-9_.\-]{1,64}$", RegexOptions.Compiled);
    // Controles y caracteres que invierten o esconden texto (marcas de dirección, aislamientos, espacios de ancho cero, BOM). NO se sacan el unión de ancho cero ni el
    // separador de variantes (U+200C/U+200D): hacen falta para los emojis compuestos y algunas escrituras.
    private static readonly Regex Invisibles = new(@"[\p{Cc}\u200B\u200E\u200F\u202A-\u202E\u2060-\u2064\u2066-\u2069\uFEFF]+", RegexOptions.Compiled);
    private static readonly Regex Espacios = new(@"\s+", RegexOptions.Compiled);

    public bool EsTexto => Tipo == TiposDeMensaje.Texto;

    /// <summary>Los datos del cuerpo del webhook, o null si no trae ninguno de los campos nuevos (el cuerpo de siempre).</summary>
    public static DatosDelEntrante? Desde(MensajeEntranteRequest req, TimeProvider reloj)
    {
        if (req.Tipo is null && req.Id is null && req.Ts is null && req.Nombre is null) return null;
        return new DatosDelEntrante(TiposDeMensaje.Normalizar(req.Tipo), LimpiarId(req.Id), HoraDelMensaje(req.Ts, reloj), LimpiarNombre(req.Nombre));
    }

    /// <summary>El id tal cual lo manda WhatsApp (letras, números, guion, punto y guion bajo, hasta 64) o null.</summary>
    public static string? LimpiarId(string? id)
    {
        var limpio = (id ?? "").Trim();
        return IdValido.IsMatch(limpio) ? limpio : null;
    }

    /// <summary>El nombre del perfil sin invisibles ni saltos de línea, con los espacios juntos y de hasta 100 caracteres, o null si no queda nada.</summary>
    public static string? LimpiarNombre(string? nombre)
    {
        var limpio = Espacios.Replace(Invisibles.Replace(nombre ?? "", " "), " ").Trim();
        if (limpio.Length == 0) return null;
        if (limpio.Length > MaxNombre)
        {
            var corte = MaxNombre;
            if (char.IsHighSurrogate(limpio[corte - 1])) corte--;   // no partir un emoji al medio
            limpio = limpio[..corte].TrimEnd();
        }
        return limpio;
    }

    /// <summary>
    /// La hora real del mensaje (segundos desde 1970, como la manda WhatsApp) en la hora de la clínica, si es razonable: ni más de un minuto en el futuro ni de más de un
    /// día atrás. Un poco en el futuro se acota a "ahora"; si no es razonable, null (se usa la hora del servidor).
    /// </summary>
    public static DateTime? HoraDelMensaje(long? segundosUnix, TimeProvider reloj)
    {
        if (segundosUnix is not > 0) return null;
        DateTimeOffset momento;
        try { momento = DateTimeOffset.FromUnixTimeSeconds(segundosUnix.Value); }
        catch (ArgumentOutOfRangeException) { return null; }

        var ahora = reloj.GetUtcNow();
        if (momento - ahora > HolguraDelFuturo) return null;
        if (ahora - momento >= AntiguedadMaxima) return null;       // "de menos de un día": justo un día ya no
        if (momento > ahora) momento = ahora;
        return TimeZoneInfo.ConvertTime(momento, reloj.LocalTimeZone).DateTime;
    }
}
