// Cuerpo de POST /profesionales/{id}/usuario (el Configurador le crea el usuario a un profesional aprobado).
// Mail: si no viene, se usa el del profesional. Contrasena: se ignora si GenerarContrasena es true.
public class CrearUsuarioProfesionalRequest
{
    public string? Username { get; set; }
    public string? Mail { get; set; }
    public string? Contrasena { get; set; }
    public bool GenerarContrasena { get; set; }
}
