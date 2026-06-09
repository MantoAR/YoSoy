public class InvitacionSesionService
{
    private readonly IConfiguration _cfg;

    public InvitacionSesionService(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    public (DateTime inicio, int duracion) Ventana(Turno t)
    {
        int porDefecto = int.TryParse(_cfg["Sesion:DuracionTurnoDefaultMinutos"], out var d) ? d : 60;
        int duracion = t.DuracionMinutos
            ?? new ProfesionalDAO().ObtenerPorId(t.IdProfesional)?.DuracionTurnoDefault
            ?? porDefecto;
        return (t.Fecha.Date + t.Hora, duracion);
    }

    public void EnviarParaTurno(Turno t, int? idUsuarioProfesional = null)
    {
        var (inicio, duracion) = Ventana(t);
        var expira = inicio.AddMinutes(duracion);
        var dao = new SesionLinkTokenDAO();

        var dominio = _cfg["Dominio"];
        if (string.IsNullOrWhiteSpace(dominio)) dominio = "http://localhost:5000";

        int? idProf = idUsuarioProfesional
            ?? new UsuarioProfesionalDAO().ObtenerPorProfesional(t.IdProfesional)?.IdUsuario;

        string CrearLink(string rol, int? idUsuario, string pagina)
        {
            var tok = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            dao.Insertar(new SesionLinkToken { IdTurno = t.IdTurno, Rol = rol, IdUsuario = idUsuario, Token = tok, Expiracion = expira });
            return $"{dominio}/pantallas/{pagina}?link={tok}";
        }

        var paciente = new PacienteDAO().ObtenerPorId(t.IdPaciente);
        var profesional = new ProfesionalDAO().ObtenerPorId(t.IdProfesional);
        var fecha = t.Fecha.ToString("dd/MM/yyyy");
        var hora = t.Hora.ToString(@"hh\:mm");
        var email = new EmailService(_cfg);

        var linkPaciente = CrearLink("paciente", null, "Pantalla_Paciente/sesion_video.html");
        var linkProfesional = CrearLink("profesional", idProf, "Pantalla_Profesional/sesion_video.html");

        if (!string.IsNullOrWhiteSpace(paciente?.Mail))
            email.EnviarInvitacionSesion(paciente!.Mail, paciente.Nombre, linkPaciente, fecha, hora);
        if (!string.IsNullOrWhiteSpace(profesional?.Mail))
            email.EnviarInvitacionSesion(profesional!.Mail, $"{profesional.Nombre} {profesional.Apellido}".Trim(), linkProfesional, fecha, hora);
    }
}
