/// <summary>
/// ¿El teléfono cargado de un paciente (texto libre) es el de esta conversación? Se usa SOLO para SUGERIR a quien vincula a mano: nunca vincula. Compara con las DOS lecturas del número:
///  · Telefono.Normalizar, los últimos 10 dígitos (lo que se guarda de la conversación);
///  · TelefonoArgentino.Normalizar, el número nacional que entiende guiones, espacios, paréntesis, +54, el 9 de los celulares, el 0 de discado y el 15 ("(351) 15 206-6471",
///    "0351 15 2066471", "+54 9 351 206 6471"), que la primera no reconoce cuando hay un 15.
/// </summary>
public static class CoincidenciaDeTelefono
{
    public static bool Coincide(string? telefonoDelPaciente, string telefonoDeLaConversacion)
    {
        var buscado = Telefono.Normalizar(telefonoDeLaConversacion);
        if (buscado.Length < 6) return false;
        return Telefono.Normalizar(telefonoDelPaciente) == buscado || TelefonoArgentino.Normalizar(telefonoDelPaciente) == buscado;
    }
}
