using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

// El texto que se le muestra al paciente para que acepte o rechace la lectura de emociones. Lo sirve la API (no está
// escrito en las pantallas) para que la versión que se ve sea exactamente la que queda registrada con la decisión.
//
// Cada versión es un archivo Services/Video/Consentimiento/<versión>.txt embebido en el ensamblado. Una versión
// publicada NO se edita: un texto nuevo es una versión nueva (y se elige con Video:Emociones:VersionTexto). Con cada
// decisión se guarda la versión y el SHA-256 del texto exacto que se mostró, así se puede demostrar qué leyó la persona.
//
// Formato del archivo: la primera línea es el título; el resto son párrafos separados por una línea en blanco.
public record TextoConsentimiento(string Version, string Titulo, IReadOnlyList<string> Parrafos, string Hash, bool Provisorio);

public interface ITextoConsentimientoVideo
{
    /// <summary>El texto de la versión configurada, o null si esa versión no existe (queda un aviso en el log).</summary>
    TextoConsentimiento? Actual();
}

public class TextoConsentimientoVideo : ITextoConsentimientoVideo
{
    private static readonly Regex ReVersion = new("^[A-Za-z0-9._-]{1,40}$", RegexOptions.Compiled);

    private readonly IOptionsMonitor<VideoOptions> _opciones;
    private readonly ILogger<TextoConsentimientoVideo> _log;
    private readonly ConcurrentDictionary<string, TextoConsentimiento?> _cache = new(StringComparer.Ordinal);

    public TextoConsentimientoVideo(IOptionsMonitor<VideoOptions> opciones, ILogger<TextoConsentimientoVideo> log)
    {
        _opciones = opciones; _log = log;
    }

    public TextoConsentimiento? Actual()
    {
        var version = (_opciones.CurrentValue.Emociones.VersionTexto ?? "").Trim();
        return _cache.GetOrAdd(version, Cargar);
    }

    private TextoConsentimiento? Cargar(string version)
    {
        if (!ReVersion.IsMatch(version))
        {
            _log.LogWarning("Video: Video:Emociones:VersionTexto no es una versión válida; la lectura de emociones queda no disponible.");
            return null;
        }

        using var flujo = typeof(TextoConsentimientoVideo).Assembly.GetManifestResourceStream($"ConsentimientoVideo.{version}.txt");
        if (flujo is null)
        {
            _log.LogWarning("Video: no existe el texto de consentimiento de la versión \"{Version}\"; la lectura de emociones queda no disponible.", version);
            return null;
        }
        using var lector = new StreamReader(flujo, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Interpretar(version, lector.ReadToEnd());
    }

    /// <summary>Arma el texto a partir del contenido de un archivo. Público para poder probarlo sin recursos embebidos.</summary>
    public static TextoConsentimiento Interpretar(string version, string contenido)
    {
        // Saltos de línea normalizados: el hash no puede depender de si Git (o un editor) dejó CRLF o LF.
        var normal = contenido.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿').Trim('\n');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normal))).ToLowerInvariant();

        var bloques = Regex.Split(normal, @"\n\s*\n")
            .Select(b => Regex.Replace(b.Trim(), @"\s*\n\s*", " "))
            .Where(b => b.Length > 0)
            .ToList();
        var titulo = bloques.Count > 0 ? bloques[0] : version;
        var parrafos = bloques.Skip(1).ToList();
        return new TextoConsentimiento(version, titulo, parrafos, hash,
            version.Contains("provisorio", StringComparison.OrdinalIgnoreCase));
    }
}
