public class SesionLinkToken
{
    public int IdLink { get; set; }
    public int IdTurno { get; set; }
    public string Rol { get; set; }
    public int? IdUsuario { get; set; }
    public string Token { get; set; }
    public bool Usado { get; set; }
    public DateTime Expiracion { get; set; }
    public DateTime FechaCreacion { get; set; }
}
