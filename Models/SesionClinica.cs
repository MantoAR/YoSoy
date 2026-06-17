public class SesionClinica
{
    public int IdSesion { get; set; }
    public int? IdTurno { get; set; }
    public int IdPaciente { get; set; }
    public int IdProfesional { get; set; }
    public DateTime FechaHora { get; set; }
    public string Transcripcion { get; set; }
    public string NotasEvolucion { get; set; }
    public string HistorialEmociones { get; set; }
    public string TipoCierre { get; set; }
}