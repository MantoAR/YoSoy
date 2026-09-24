public static class Telefono
{
    /// <summary>
    /// Deja solo los dígitos y se queda con los últimos 10. Así "+54 9 11 5555-0001"
    /// (formato WhatsApp: 549 + área + número) y "11 5555-0001" (como suele estar
    /// cargado en Paciente.Telefono, que es texto libre) dan lo mismo.
    /// Limitación: un celular cargado con el "15" delante del número no coincide.
    /// </summary>
    public static string Normalizar(string? telefono)
    {
        var digitos = new string((telefono ?? "").Where(char.IsDigit).ToArray());
        return digitos.Length > 10 ? digitos[^10..] : digitos;
    }
}
