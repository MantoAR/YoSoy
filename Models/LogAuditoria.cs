public class LogAuditoria
{
    public int IdLog { get; set; }
    public int? IdUsuario { get; set; }
    public string Accion { get; set; }
    public string Entidad { get; set; }
    public int? IdEntidad { get; set; }
    public string Detalle { get; set; }
    public DateTime FechaHora { get; set; }
}
