public class PreguntaFrecuente
{
    public int IdPregunta { get; set; }
    public string Pregunta { get; set; } = "";
    public string Respuesta { get; set; } = "";
    public int Orden { get; set; }
    public bool Activa { get; set; } = true;
}
