using System.Globalization;

public static class Formato
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    // "lunes 28/09 a las 10:00"
    public static string FechaHora(DateTime momento) =>
        $"{momento.ToString("dddd dd/MM", EsAr)} a las {momento:HH:mm}";

    public static string PrimerNombre(string? nombreCompleto) =>
        (nombreCompleto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    // Mismo criterio que el LTRIM(RTRIM(Nombre + ' ' + ISNULL(Apellido, ''))) armado en SQL
    // (ver ConversacionDAO.ListarResumen) - para cuando el nombre se resuelve en C# en vez
    // de en la consulta (un solo profesional, no una lista).
    public static string NombreCompleto(string nombre, string? apellido) =>
        $"{nombre} {apellido}".Trim();
}
