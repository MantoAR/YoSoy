public class Consentimiento
{
    public int IdConsentimiento { get; set; }
    public int IdPaciente { get; set; }
    public int IdTipoConsentimiento { get; set; }
    public string Version { get; set; }
    public DateTime FechaFirma { get; set; }
    public string Estado { get; set; }
}
