// Auditoría del consentimiento y de lo que pasa con las emociones guardadas, con el mecanismo que ya existe
// (LogAuditoria), igual que AuditoriaVideo: solo ids y códigos, nunca el contenido ni la clave, y si falla no impide
// responder. Es una interfaz para poder probar las reglas sin escribir en la base.
//
// Ojo: LogAuditoria.FechaHora es la hora del servidor (GETDATE()). La hora exacta, en UTC, de cada decisión está en
// ConsentimientoVideo.DecididoUtc y RevocadoUtc.
public interface IAuditoriaEmociones
{
    /// <param name="decision">"acepta", "rechaza" o "revoca".</param>
    /// <param name="emocionesAnuladas">Solo con "revoca": cuántas sesiones con emociones guardadas se anularon.</param>
    void Consentimiento(int idUsuario, string origen, int idOrigen, string decision, string version, int idConsentimiento, int emocionesAnuladas = 0);

    void EmocionesGuardadas(int idUsuario, int idSesion, int idConsentimiento, int muestras);

    /// <param name="idSesion">La sesión clínica que se guardó SIN las emociones. Si el pedido no dice cuál es la sesión de
    /// video (los de la versión anterior de la pantalla), la auditoría queda atada a esta.</param>
    void EmocionesDescartadas(int idUsuario, string? origen, int? idOrigen, string motivo, int? idSesion = null);

    /// <param name="idUsuario">null en la purga automática (no hay una persona detrás).</param>
    void EmocionesPurgadas(int? idUsuario, int cantidad, string motivo);
}

public class AuditoriaEmociones : IAuditoriaEmociones
{
    private readonly ILogger<AuditoriaEmociones> _log;

    public AuditoriaEmociones(ILogger<AuditoriaEmociones> log) => _log = log;

    public void Consentimiento(int idUsuario, string origen, int idOrigen, string decision, string version, int idConsentimiento, int emocionesAnuladas = 0)
    {
        var accion = decision switch
        {
            "acepta" => "Consentimiento de video aceptado",
            "rechaza" => "Consentimiento de video rechazado",
            _ => "Consentimiento de video revocado"
        };
        var detalle = $"version: {version}; consentimiento: {idConsentimiento}"
                    + (decision == "revoca" ? $"; emociones anuladas: {emocionesAnuladas}" : "");
        Escribir(idUsuario, accion, origen == OrigenSala.Turno ? "Turno" : "InformeAdmision", idOrigen, detalle);
    }

    public void EmocionesGuardadas(int idUsuario, int idSesion, int idConsentimiento, int muestras) =>
        Escribir(idUsuario, "Emociones guardadas", "SesionClinica", idSesion,
            $"consentimiento: {idConsentimiento}; muestras: {muestras}");

    public void EmocionesDescartadas(int idUsuario, string? origen, int? idOrigen, string motivo, int? idSesion = null)
    {
        var detalle = $"motivo: {motivo}" + (idSesion is null ? "" : $"; sesion: {idSesion}");
        if (idOrigen is null && idSesion is not null)
            Escribir(idUsuario, "Emociones descartadas", "SesionClinica", idSesion, detalle);
        else
            Escribir(idUsuario, "Emociones descartadas", origen == OrigenSala.Admision ? "InformeAdmision" : "Turno", idOrigen, detalle);
    }

    public void EmocionesPurgadas(int? idUsuario, int cantidad, string motivo) =>
        Escribir(idUsuario, "Purga de emociones", "SesionClinica", null, $"motivo: {motivo}; cantidad: {cantidad}");

    private void Escribir(int? idUsuario, string accion, string entidad, int? idEntidad, string detalle)
    {
        try
        {
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = idUsuario, Accion = accion, Entidad = entidad, IdEntidad = idEntidad, Detalle = detalle
            });
        }
        catch (Exception e)
        {
            // La auditoría no debe poder tumbar un pedido real (mismo criterio que AuditoriaIds y AuditoriaVideo).
            _log.LogWarning("Video: no se pudo escribir la auditoría de emociones ({Tipo}).", e.GetType().Name);
        }
    }
}
