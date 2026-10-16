/// <summary>
/// Política de contraseñas al ELEGIR una (alta de paciente y restablecimiento por mail). El login no la usa: una
/// contraseña ya guardada, corta o larga, sigue sirviendo.
///
/// Hoy el mínimo es 6, que es el que ya pedían el alta de paciente y las pantallas de restablecimiento y de
/// creación de cuenta. Antes, el restablecimiento (POST /recuperacion-password/reset) no lo comprobaba en el
/// servidor: con un token válido se podía dejar una contraseña vacía. Subirlo a 8 es cambiar <see cref="Minimo"/> y
/// el <c>minimo</c> de esas pantallas (docs/PROGRESO_NOCTURNO.md, Tarea 4b).
/// </summary>
public static class PoliticaContrasena
{
    public const int Minimo = 6;
    public const int Maximo = 100;

    /// <summary>El mensaje a mostrar si no cumple, o null si está bien.</summary>
    public static string? Validar(string? contrasena)
    {
        if (string.IsNullOrWhiteSpace(contrasena) || contrasena.Length < Minimo)
            return $"La contraseña debe tener al menos {Minimo} caracteres.";
        if (contrasena.Length > Maximo)
            return $"La contraseña no puede tener más de {Maximo} caracteres.";
        return null;
    }
}
