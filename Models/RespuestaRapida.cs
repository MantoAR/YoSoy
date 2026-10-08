// Respuesta rápida del composer de Chats. IdProfesional null = global (la define el
// Configurador); con valor = propia de ese profesional. Reemplaza plantillas.json.
public class RespuestaRapida
{
    public int IdRespuestaRapida { get; set; }
    public string Titulo { get; set; } = "";
    public string Texto { get; set; } = "";
    public int? IdProfesional { get; set; }
    public bool Activa { get; set; } = true;
}
