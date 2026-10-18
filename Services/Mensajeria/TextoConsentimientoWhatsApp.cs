using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

// El texto que se le muestra al paciente para que acepte (o no) recibir avisos de sus turnos por WhatsApp. Es el mismo mecanismo que el del
// consentimiento de la lectura de emociones (TextoConsentimientoVideo): lo sirve la API (no está escrito en las pantallas) para que la versión
// que se ve sea exactamente la que queda registrada con la aceptación, junto con el SHA-256 del texto exacto.
//
// Cada versión es un archivo Services/Mensajeria/Consentimiento/<versión>.txt embebido en el ensamblado. Una versión publicada NO se edita:
// un texto nuevo es una versión nueva (y se elige con Mensajeria:ConsentimientoWhatsApp:VersionTexto). Cambiar de versión hace que los
// avisos por WhatsApp se detengan hasta que cada paciente vuelva a aceptar (ver ConsentimientoWhatsAppService.Evaluar).
//
// Formato del archivo: la primera línea es el título; el resto son párrafos separados por una línea en blanco. Se interpreta con la misma
// regla que el de video (TextoConsentimientoVideo.Interpretar), así el hash no depende de si Git dejó CRLF o LF.
public interface ITextoConsentimientoWhatsApp
{
    /// <summary>El texto de la versión configurada, o null si esa versión no existe (queda un aviso en el log).</summary>
    TextoConsentimiento? Actual();
}

public class TextoConsentimientoWhatsApp : ITextoConsentimientoWhatsApp
{
    private static readonly Regex ReVersion = new("^[A-Za-z0-9._-]{1,40}$", RegexOptions.Compiled);

    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly ILogger<TextoConsentimientoWhatsApp> _log;
    private readonly ConcurrentDictionary<string, TextoConsentimiento?> _cache = new(StringComparer.Ordinal);

    public TextoConsentimientoWhatsApp(IOptionsMonitor<MensajeriaOptions> opciones, ILogger<TextoConsentimientoWhatsApp> log)
    {
        _opciones = opciones; _log = log;
    }

    public TextoConsentimiento? Actual()
    {
        var version = (_opciones.CurrentValue.ConsentimientoWhatsApp.VersionTexto ?? "").Trim();
        return _cache.GetOrAdd(version, Cargar);
    }

    private TextoConsentimiento? Cargar(string version)
    {
        if (!ReVersion.IsMatch(version))
        {
            _log.LogWarning("WhatsApp: Mensajeria:ConsentimientoWhatsApp:VersionTexto no es una versión válida; los avisos por WhatsApp no se pueden aceptar.");
            return null;
        }

        using var flujo = typeof(TextoConsentimientoWhatsApp).Assembly.GetManifestResourceStream($"ConsentimientoWhatsApp.{version}.txt");
        if (flujo is null)
        {
            _log.LogWarning("WhatsApp: no existe el texto de consentimiento de la versión \"{Version}\"; los avisos por WhatsApp no se pueden aceptar.", version);
            return null;
        }
        using var lector = new StreamReader(flujo, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return TextoConsentimientoVideo.Interpretar(version, lector.ReadToEnd());
    }
}
