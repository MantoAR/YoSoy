public class Conversacion
{
    public int IdConversacion { get; set; }
    public string Telefono { get; set; } = "";
    public int? IdPaciente { get; set; }
    public string Canal { get; set; } = "Simulado";
    public string PasoActual { get; set; } = "Inicio";
    public string? DatosContexto { get; set; }
    public string Estado { get; set; } = "Activa";
    public DateTime FechaUltimaActividad { get; set; }
    public DateTime FechaCreacion { get; set; }
    // Quién atiende este hilo (Tomar/Asignar/Devolver) - no es el profesional tratante del
    // paciente (eso es Caso/PacienteProfesional, sin relación). Ver VisibilidadConversacion.
    public int? IdProfesionalAsignado { get; set; }
    // El nombre del perfil de WhatsApp de quien escribe (migración 17; solo con ModoAtencion = "Personal"). Se guarda acá y NUNCA va a un log ni a la auditoría.
    public string? NombreContacto { get; set; }
    // El paciente que tenía la conversación cuando se LEYÓ de la base (no se guarda). ConversacionDAO.Guardar lo usa para escribir IdPaciente solo si quien la leyó lo cambió: así un
    // vínculo hecho a mano (o deshecho) mientras tanto no se pisa con la copia vieja.
    public int? IdPacienteAlLeer { get; set; }
}
