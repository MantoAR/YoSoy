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

    // Link público de reserva (Fase 3a, Paso 3). Nunca se escriben desde Insertar/Modificar
    // (el PUT /profesionales/{id} genérico no los toca) — solo desde ProfesionalDAO.ActivarLinkPublico,
    // así una edición de perfil que no sabe nada de esto no puede apagar el link por accidente.
    public string? Slug { get; set; }
    public bool LinkPublicoActivo { get; set; }
}
