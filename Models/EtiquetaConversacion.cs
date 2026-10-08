// Catálogo chico y administrable de etiquetas para conversaciones. Tono = una de las
// clases de chip que ya existen en tema.css (ui-chip--tono), para no inventar colores.
public class EtiquetaConversacion
{
    public int IdEtiqueta { get; set; }
    public string Nombre { get; set; } = "";
    public string Tono { get; set; } = "neutral";
    public bool Activa { get; set; } = true;
}
