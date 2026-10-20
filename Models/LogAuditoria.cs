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
    // Migración 17: 'Administrativa' (la ve el Configurador) o 'Clinica' (la ve el
    // Profesional auditor), y el módulo desde el que se hizo la acción.
    public string Categoria { get; set; } = CategoriaAuditoria.Administrativa;
    public string? Espacio { get; set; }
    // Roles y permisos: solo para mostrar (GET /auditoria); no se guarda. El auditor no puede leer
    // /usuarios, así que el nombre viene en el mismo pedido.
    public string? NombreUsuario { get; set; }
}

public static class CategoriaAuditoria
{
    public const string Administrativa = "Administrativa";
    public const string Clinica = "Clinica";
}
