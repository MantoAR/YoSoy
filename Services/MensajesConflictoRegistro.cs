/// <summary>
/// Qué se le responde a quien se registra (POST /pacientes) cuando algún dato ya existe.
///
/// El nombre de usuario lo elige la persona y necesita saber que tiene que probar con otro: ese mensaje es
/// específico. El DNI y el mail son datos personales: antes cada uno tenía su propio mensaje ("Ya existe un
/// paciente registrado con ese DNI", "Ya existe una cuenta registrada con ese mail"), que le decía a cualquiera
/// qué DNI o mail estaban registrados. Ahora todos los demás conflictos responden lo mismo, sin decir cuál dato
/// coincidió, y orientan a quien de verdad ya tiene cuenta.
///
/// Ojo: esto es una mejora de redacción, no cierra la enumeración (quien puede variar un dato por vez sigue
/// deduciendo cuál existe); lo que la frena de verdad es el límite por IP del alta y, más adelante, verificar el
/// mail. Ver docs/PROGRESO_NOCTURNO.md, Tarea 4.
/// </summary>
public static class MensajesConflictoRegistro
{
    public const string UsuarioEnUso = "Ese nombre de usuario ya está en uso.";

    public const string DatosYaRegistrados =
        "No se pudo completar el registro con esos datos. Si ya tenés una cuenta, ingresá con tu usuario " +
        "o recuperá tu contraseña.";

    /// <param name="campoConflicto">Lo que devuelve PacienteDAO.RegistrarConAcceso: "usuario", "mail", "dni" o "conflicto".</param>
    public static string Paciente(string? campoConflicto) =>
        campoConflicto == "usuario" ? UsuarioEnUso : DatosYaRegistrados;
}
