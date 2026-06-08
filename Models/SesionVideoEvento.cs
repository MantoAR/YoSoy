public class SesionVideoEvento
{
    public int IdEvento { get; set; }
    public int IdSesion { get; set; }
    public string Tipo { get; set; }
    public string? Detalle { get; set; }
    public int? IdUsuario { get; set; }
    public string? Rol { get; set; }
    public DateTime Fecha { get; set; }
}
