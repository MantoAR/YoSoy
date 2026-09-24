using System.Globalization;
using System.Text;

public static class TextoNormalizado
{
    /// <summary>
    /// Minúsculas, sin tildes (la ñ queda como n), solo letras, dígitos y un espacio
    /// entre palabras. "¡Quiero  pedir un TURNO!" → "quiero pedir un turno".
    /// </summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
