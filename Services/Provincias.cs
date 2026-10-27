using System.Globalization;
using System.Text;

/// <summary>
/// Las provincias de Argentina (las 23 y la Ciudad Autónoma de Buenos Aires): la lista del servidor para validar los REGISTROS NUEVOS de paciente y de profesional.
/// La pantalla (PANTALLAS/shared/provincias.js) tiene la suya con el autocompletado; una prueba verifica que las dos sean idénticas, así que hay una sola lista de verdad.
/// No se migra nada: lo que ya está guardado (texto libre) no se toca ni se valida.
/// </summary>
public static class Provincias
{
    public const string MensajeInvalida = "Elegí una provincia de la lista.";

    public static readonly IReadOnlyList<string> Lista = new[]
    {
        "Buenos Aires", "Catamarca", "Chaco", "Chubut", "Ciudad Autónoma de Buenos Aires", "Córdoba", "Corrientes", "Entre Ríos", "Formosa", "Jujuy", "La Pampa", "La Rioja", "Mendoza", "Misiones",
        "Neuquén", "Río Negro", "Salta", "San Juan", "San Luis", "Santa Cruz", "Santa Fe", "Santiago del Estero", "Tierra del Fuego Antártida e Islas del Atlántico Sur", "Tucumán"
    };

    /// <summary>Minúsculas, sin tildes y con los espacios normalizados: «  CÓRDOBA » → «cordoba».</summary>
    public static string Normalizar(string? texto)
    {
        var d = (texto ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return string.Join(' ', sb.ToString().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>El nombre canónico (con su tilde) si el texto es una provincia, sin importar mayúsculas ni tildes; null si no lo es.</summary>
    public static string? Canonica(string? texto)
    {
        var n = Normalizar(texto);
        return n.Length == 0 ? null : Lista.FirstOrDefault(p => Normalizar(p) == n);
    }
}
