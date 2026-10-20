public class LoginRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
    // Etapa 2: la pestaña elegida en la pantalla de ingreso (paciente, profesional, crm,
    // facturacion, configurador). Si viene, se entra directo a ese módulo si el usuario lo
    // tiene; si no viene, el servidor decide (un módulo) o pide elegir (varios).
    public string? Modulo { get; set; }
}
