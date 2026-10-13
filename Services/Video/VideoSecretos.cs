// Reglas de los secretos de video (el de la señalización y el de TURN). Se validan en cada pedido y no al
// arrancar: la API tiene que levantar igual sin ellos (el video es opcional hasta que se configure).
public static class VideoSecretos
{
    public const int Minimo = 32;

    /// <summary>
    /// null si el secreto sirve; si no, el motivo en una frase. El motivo nunca incluye el valor del secreto
    /// (va a los logs). "Jwt:Key" es la clave de login de la API: un secreto de video no puede ser el mismo,
    /// porque quien lo tiene (el proceso de señalización, coturn) podría falsificar un login.
    /// </summary>
    public static string? Problema(string nombre, string? secreto, string? jwtKey)
    {
        if (string.IsNullOrWhiteSpace(secreto)) return $"{nombre} no está configurado.";
        if (secreto.Length < Minimo) return $"{nombre} es demasiado corto ({secreto.Length} caracteres; mínimo {Minimo}).";
        if (!string.IsNullOrEmpty(jwtKey) && secreto == jwtKey)
            return $"{nombre} tiene el mismo valor que Jwt:Key: tienen que ser secretos distintos.";
        return null;
    }
}
