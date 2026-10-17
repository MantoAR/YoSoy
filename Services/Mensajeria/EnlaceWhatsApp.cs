using Microsoft.Extensions.Options;

// El link wa.me con el que un paciente puede escribirle PRIMERO al número de la clínica (la opción B del consentimiento): cuando una persona
// le escribe primero, el servicio de WhatsApp le puede contestar y mandarle avisos (regla "escribió primero", con su propio plazo y con la
// BAJA siempre respetada). El número sale de la configuración (Mensajeria:WhatsAppNumeroClinica): no está escrito en el código ni en las
// pantallas. Sin número, o con uno que no sirve, no hay link y ninguna pantalla muestra nada.
public static class EnlaceWhatsApp
{
    /// <summary>Lo que viene escrito al abrir el link si no se configura otra cosa. Neutro: no dice qué es la clínica ni para qué se escribe.</summary>
    public const string TextoPorDefecto = "Hola";

    private const int MaxTexto = 200;

    /// <summary>
    /// El número en el formato de wa.me (solo dígitos, con el código de país y sin "+" ni ceros), o null si no es un teléfono utilizable.
    /// Un celular argentino se escribe como quiera ("011 15 5555-0000", "+54 9 11 5555-0000") y queda 54 9 + área + abonado, que es como
    /// lo espera WhatsApp; uno de otro país solo vale si viene con "+".
    /// </summary>
    public static string? NumeroParaEnlace(string? configurado)
    {
        if (string.IsNullOrWhiteSpace(configurado)) return null;

        var argentino = TelefonoArgentino.Normalizar(configurado);
        if (argentino is not null) return "549" + argentino;

        var texto = configurado.Trim();
        if (!texto.StartsWith('+')) return null;
        if (texto.Any(c => c is not (>= '0' and <= '9') && !" +()-./".Contains(c))) return null;   // con letras, otros símbolos o dígitos que no son ASCII no es un número
        var digitos = new string(texto.Where(c => c is >= '0' and <= '9').ToArray());
        return digitos.Length is >= 8 and <= 15 && digitos[0] != '0' ? digitos : null;
    }

    /// <summary>"https://wa.me/{número}?text={texto}", o null si no hay un número utilizable. Un texto vacío arma el link sin mensaje precargado.</summary>
    public static string? Construir(string? numeroConfigurado, string? textoInicio)
    {
        var numero = NumeroParaEnlace(numeroConfigurado);
        if (numero is null) return null;

        var texto = Limpiar(textoInicio);
        return texto.Length == 0 ? $"https://wa.me/{numero}" : $"https://wa.me/{numero}?text={Uri.EscapeDataString(texto)}";
    }

    // Sin caracteres de control ni espacios de más, y con un tope: lo que viene de la configuración va a la URL de otra aplicación.
    private static string Limpiar(string? texto)
    {
        var limpio = new string((texto ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        limpio = string.Join(' ', limpio.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return limpio.Length > MaxTexto ? limpio[..MaxTexto].TrimEnd() : limpio;
    }
}

/// <summary>El link para escribirle a la clínica según la configuración vigente (se puede cambiar sin reiniciar: IOptionsMonitor).</summary>
public class ContactoWhatsApp
{
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly ILogger<ContactoWhatsApp> _log;
    private int _avisado;   // el aviso de "número que no sirve" sale una sola vez

    public ContactoWhatsApp(IOptionsMonitor<MensajeriaOptions> opciones, ILogger<ContactoWhatsApp> log)
    {
        _opciones = opciones; _log = log;
    }

    /// <summary>null si no hay número configurado o no es utilizable (en ese caso queda un aviso en el log, sin el valor).</summary>
    public string? Enlace()
    {
        var o = _opciones.CurrentValue;
        var enlace = EnlaceWhatsApp.Construir(o.WhatsAppNumeroClinica, o.WhatsAppTextoInicio);
        if (enlace is null && !string.IsNullOrWhiteSpace(o.WhatsAppNumeroClinica) && Interlocked.Exchange(ref _avisado, 1) == 0)
            _log.LogWarning("WhatsApp: Mensajeria:WhatsAppNumeroClinica no es un número utilizable; no se muestra el link para escribirle a la clínica.");
        return enlace;
    }
}
