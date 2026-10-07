public class ConversacionResumen
{
    public int IdConversacion { get; set; }
    public string Telefono { get; set; } = "";
    public int? IdPaciente { get; set; }
    public string? NombrePaciente { get; set; }
    public string Canal { get; set; } = "";
    public string Estado { get; set; } = "";
    public string PasoActual { get; set; } = "";
    public DateTime FechaUltimaActividad { get; set; }
    public string? UltimoMensaje { get; set; }
    public DateTime? FechaUltimoMensaje { get; set; }
    public int? IdUltimoMensaje { get; set; }
    public string? OrigenUltimoMensaje { get; set; }      // 'Paciente', 'Bot', 'Humano', 'Sistema'
    public int NoLeidos { get; set; }                     // mensajes del paciente que el equipo no vio
    // Derivado del Caso abierto del paciente (no hay columna de asignación propia en
    // Conversacion) - ver CAMBIOS_AGUS.md, "Mejora de chats y centrado".
    public int? IdProfesionalAsignado { get; set; }
    public string? NombreProfesionalAsignado { get; set; }
    public List<int> IdsEtiquetas { get; set; } = new();   // se completa en el endpoint, no en el DAO (ver EtiquetaConversacionDAO)
}
