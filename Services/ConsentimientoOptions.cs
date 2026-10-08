// Sección "Consentimiento" de appsettings.json (RN-02).
public class ConsentimientoOptions
{
    public const string Seccion = "Consentimiento";

    // Tipo que se exige para confirmar un turno. Se busca por Descripcion, nunca por id
    // (lo crea migrations/add_consentimiento_telemedicina.sql).
    public string TipoRequerido { get; set; } = "Consentimiento Informado - Atención por Telemedicina";

    // Versión del texto legal que se muestra y se guarda con cada firma. Cambiarla
    // cuando el texto validado reemplace al placeholder.
    public string VersionTexto { get; set; } = "sin-validar";

    // Página donde el paciente lo firma. La usan el bot y los mensajes de error.
    public string UrlFirma { get; set; } = "";
}
