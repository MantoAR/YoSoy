// Migración 36 (rama AAA1): Admisión también como privilegio de la Recepcionista (admisionista) y la entrevista de admisión
// por video como privilegio aparte, solo del profesional. Sin base de datos.
using System.Security.Claims;

public class PrivilegiosAdmisionTests
{
    internal static ClaimsPrincipal Usuario(string rol, params string[] privilegios)   // también la usa AsistenciaReglasTests
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "7"), new(ClaimTypes.Role, rol) };
        claims.AddRange(privilegios.Select(p => new Claim(JwtEmisorService.ClaimPrivilegio, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba", ClaimTypes.Name, ClaimTypes.Role));
    }

    [Fact]
    public void La_recepcionista_revisa_formularios_solo_con_el_privilegio_Admision()
    {
        Assert.True(Politicas.Cumple(Usuario(Roles.Crm, Privilegios.Admision), Quien.Admision | Quien.RecepcionAdmision));
        Assert.False(Politicas.Cumple(Usuario(Roles.Crm), Quien.Admision | Quien.RecepcionAdmision));
        Assert.False(Politicas.Cumple(Usuario(Roles.Crm, Privilegios.GestionAgendas), Quien.Admision | Quien.RecepcionAdmision));
    }

    [Fact]
    public void La_admision_de_la_recepcionista_no_es_la_del_profesional()
    {
        // Lo que es solo del profesional con Admisión (historia clínica del paciente en admisión) no se abre para la recepcionista.
        Assert.False(Politicas.Cumple(Usuario(Roles.Crm, Privilegios.Admision), Quien.Admision));
        Assert.True(Politicas.Cumple(Usuario(Roles.Profesional, Privilegios.Admision), Quien.Admision));
    }

    [Fact]
    public void La_entrevista_por_video_la_ofrece_solo_un_profesional_con_el_privilegio()
    {
        Assert.True(Politicas.Cumple(Usuario(Roles.Profesional, Privilegios.EntrevistaVideo), Quien.EntrevistaVideo));
        Assert.False(Politicas.Cumple(Usuario(Roles.Profesional, Privilegios.Admision), Quien.EntrevistaVideo));
        Assert.False(Politicas.Cumple(Usuario(Roles.Crm, Privilegios.EntrevistaVideo, Privilegios.Admision), Quien.EntrevistaVideo));
    }
}
