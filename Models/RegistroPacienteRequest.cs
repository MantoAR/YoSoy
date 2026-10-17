public class RegistroPacienteRequest
{
    public string Nombre { get; set; }
    public string Dni { get; set; }
    public DateTime? FechaNacimiento { get; set; }
    public string Domicilio { get; set; }
    public string Telefono { get; set; }
    public string Mail { get; set; }
    public string Usuario { get; set; }
    public string Contrasena { get; set; }

    // Consentimiento de los avisos por WhatsApp. NUNCA viene aceptado por defecto: solo cuenta un true explícito (ausente o null es "no"),
    // con la versión del texto que la persona leyó (GET /consentimiento-whatsapp/texto). Ver ConsentimientoWhatsAppService.
    public bool? AceptaWhatsApp { get; set; }
    public string VersionTextoWhatsApp { get; set; }
}
