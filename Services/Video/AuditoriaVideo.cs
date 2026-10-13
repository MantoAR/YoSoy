// Auditoría de los accesos a las salas de video, con el mecanismo que ya existe (LogAuditoria), igual que
// AuditoriaIds: solo ids y códigos, nunca contenido, nunca el token ni las credenciales, y si falla no
// impide responder. Es una interfaz para poder probar el endpoint sin escribir en la base.
public interface IAuditoriaVideo
{
    /// <param name="rol">El rol con el que entró (si se concedió) o el rol del usuario en el sistema (si se rechazó).</param>
    void Registrar(int idUsuario, string origen, int idOrigen, string rol, ResultadoAcceso resultado);
}

public class AuditoriaVideo : IAuditoriaVideo
{
    private readonly ILogger<AuditoriaVideo> _log;

    public AuditoriaVideo(ILogger<AuditoriaVideo> log) => _log = log;

    public void Registrar(int idUsuario, string origen, int idOrigen, string rol, ResultadoAcceso resultado)
    {
        try
        {
            var detalle = $"rol: {rol}; resultado: {(resultado.Ok ? "concedido" : "rechazado")}"
                        + (resultado.Ok ? "" : $"; motivo: {resultado.Codigo}");
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = idUsuario,
                Accion = resultado.Ok ? "Acceso a sala de video" : "Acceso a sala de video rechazado",
                Entidad = origen == OrigenSala.Turno ? "Turno" : "InformeAdmision",
                IdEntidad = idOrigen,
                Detalle = detalle
            });
        }
        catch (Exception e)
        {
            // La auditoría no debe poder tumbar un pedido real (mismo criterio que AuditoriaIds). Se deja
            // constancia, sin el detalle del error, de que no quedó registrada.
            _log.LogWarning("Video: no se pudo escribir la auditoría de un acceso ({Tipo}).", e.GetType().Name);
        }
    }
}
