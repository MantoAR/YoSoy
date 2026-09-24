public class NotificacionInterna
{
    public int IdNotificacionInterna { get; set; }
    public string Destinatario { get; set; } = "";  // 'Administrador' / 'Profesional'
    public int? IdProfesional { get; set; }
    public int? IdTurno { get; set; }
    public int? IdConversacion { get; set; }
    public string Tipo { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public bool Leida { get; set; }
    public DateTime FechaCreacion { get; set; }
}
