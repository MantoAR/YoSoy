public class ReglaDerivacion
{
    public int IdRegla { get; set; }
    public int IdEspecialidad { get; set; }
    public int IdGrupoDestino { get; set; }
    public int Prioridad { get; set; }
    public bool Activa { get; set; } = true;
}
