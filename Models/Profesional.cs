public class Profesional
{
    public int IdProfesional { get; set; }
    public string Nombre { get; set; }
    public string? Apellido { get; set; }
    public string? SegundoNombre { get; set; }
    public string? SegundoApellido { get; set; }
    public string? Dni { get; set; }
    public string? Sexo { get; set; }
    public DateTime? FechaNacimiento { get; set; }
    public string Matricula { get; set; }
    public int IdEspecialidad { get; set; }
    public string? Provincia { get; set; }
    public string? OrigenContacto { get; set; }
    public string Estado { get; set; }
    public string Mail { get; set; }
    public string Telefono { get; set; }
    public DateTime? FechaSolicitud { get; set; }
    public string? Token { get; set; }
    public int? DuracionTurnoDefault { get; set; }
}
