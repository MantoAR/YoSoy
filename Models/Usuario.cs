public class Usuario
{
    public int IdUsuario { get; set; }
    public string Username { get; set; }
    public string PasswordHash { get; set; }
    public string Estado { get; set; }
    public string Mail { get; set; }
    public int IntentosFallidos { get; set; }
    public DateTime? BloqueadoHasta { get; set; }
    // Migración 24: la puso en 1 el Configurador al restablecer la contraseña; el ingreso no entrega sesión hasta que elija una nueva.
    public bool DebeCambiarPassword { get; set; }
}
