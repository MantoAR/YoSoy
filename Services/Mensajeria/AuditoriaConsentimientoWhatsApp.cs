// Auditoría del consentimiento de WhatsApp, con el mecanismo que ya existe (LogAuditoria), igual que AuditoriaEmociones: solo ids y códigos,
// nunca el teléfono ni ningún contenido, y si falla no impide responder. Es una interfaz para poder probar las reglas sin escribir en la base.
//
// Ojo: LogAuditoria.FechaHora es la hora del servidor (GETDATE()). La hora exacta, en UTC, de cada decisión está en
// ConsentimientoWhatsApp.AceptadoUtc y RevocadoUtc.
public interface IAuditoriaConsentimientoWhatsApp
{
    /// <param name="idUsuario">null cuando no hay una persona detrás con sesión (la BAJA que llega por WhatsApp).</param>
    /// <param name="accion">"acepta", "revoca" (desde el perfil) o "baja" (la persona escribió BAJA por WhatsApp).</param>
    void Registrar(int? idUsuario, int idPaciente, string accion, int idConsentimiento, string detalle);
}

public class AuditoriaConsentimientoWhatsApp : IAuditoriaConsentimientoWhatsApp
{
    private readonly ILogger<AuditoriaConsentimientoWhatsApp> _log;

    public AuditoriaConsentimientoWhatsApp(ILogger<AuditoriaConsentimientoWhatsApp> log) => _log = log;

    public void Registrar(int? idUsuario, int idPaciente, string accion, int idConsentimiento, string detalle)
    {
        var texto = accion switch
        {
            "acepta" => "Consentimiento de WhatsApp aceptado",
            "baja" => "Consentimiento de WhatsApp revocado (BAJA por WhatsApp)",
            _ => "Consentimiento de WhatsApp revocado"
        };
        try
        {
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = idUsuario, Accion = texto, Entidad = "Paciente", IdEntidad = idPaciente,
                Detalle = $"consentimiento: {idConsentimiento}; {detalle}"
            });
        }
        catch (Exception e)
        {
            // La auditoría no debe poder tumbar un pedido real (mismo criterio que AuditoriaEmociones y AuditoriaVideo).
            _log.LogWarning("WhatsApp: no se pudo escribir la auditoría del consentimiento ({Tipo}).", e.GetType().Name);
        }
    }
}
