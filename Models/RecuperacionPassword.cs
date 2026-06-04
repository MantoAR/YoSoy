public class RecuperacionPassword
{
    public int IdRecuperacion { get; set; }
    public int IdUsuario { get; set; }
    public DateTime FechaSolicitud { get; set; }
    public string Estado { get; set; }
    public string PasswordTemporal { get; set; }
    public DateTime? FechaResolucion { get; set; }
    public string Username { get; set; }
    public string Mail { get; set; }
    public string? Token { get; set; }
    public DateTime? TokenExpiracion { get; set; }
}
