// POST /sesion/cambiar-password: la clave nueva y, si la hay, la pestaña (módulo) con la que se había ingresado.
public class CambiarPasswordRequest
{
    public string? PasswordNueva { get; set; }
    public string? Modulo { get; set; }
}
