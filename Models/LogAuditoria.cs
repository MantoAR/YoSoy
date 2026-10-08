public class LogAuditoria
{
    public int IdLog { get; set; }
    // NULL cuando la acción no tiene sesión (por ejemplo, un registro público).
    public int? IdUsuario { get; set; }
    public string Accion { get; set; }
    public string Entidad { get; set; }
    public int? IdEntidad { get; set; }
    public string Detalle { get; set; }
    public DateTime FechaHora { get; set; }
}
