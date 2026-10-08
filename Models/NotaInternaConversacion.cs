// Nota privada del equipo sobre una conversación: nunca la ve el paciente y su texto
// nunca se audita (ver MensajeriaEndpoints.Auditar) - solo queda el id y quién/cuándo.
public class NotaInternaConversacion
{
    public int IdNota { get; set; }
    public int IdConversacion { get; set; }
    public string Texto { get; set; } = "";
    public int? IdUsuario { get; set; }
    public string? NombreUsuario { get; set; }   // solo para mostrar, no viene de esta tabla
    public DateTime FechaCreacion { get; set; }
}
