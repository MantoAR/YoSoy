using System.Security.Claims;

// Única función para "quién es el usuario actual" en todo el backend (Fase JWT). El
// ClaimsPrincipal ya viene armado por el pipeline de autenticación de Program.cs (el
// esquema "Smart" elige JWT si hay Bearer, o LegacyHeaderAuthHandler si no y
// Auth:PermitirEncabezadoLegacy está en true) — esta clase solo lee los claims, ya
// resueltos, sea cual sea el camino que los puso ahí. Reemplaza a
// AutorizacionCasos/AutorizacionReporte y a cada int.TryParse(ctx.Request.Headers
// ["X-Usuario-Id"], ...) suelto que leía la identidad directo del header.
public record Identidad(int IdUsuario, string Rol, int? IdPaciente, int? IdProfesional)
{
    public bool EsConfigurador => Rol == Roles.Configurador;
    public bool EsProfesional => Rol == Roles.Profesional;
    public bool EsPaciente => Rol == Roles.Paciente;
}

// Mismos 3 valores, en minúscula, que ya devuelve POST /login en el campo "rol" (contrato
// existente, no se cambia) — el claim de rol del token usa la misma palabra.
public static class Roles
{
    public const string Paciente = "paciente";
    public const string Profesional = "profesional";
    public const string Configurador = "configurador";
}

public class IdentidadActualService
{
    public const string ClaimIdPaciente = "idPaciente";
    public const string ClaimIdProfesional = "idProfesional";

    public Identidad? Resolver(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true) return null;

        var idUsuario = int.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var rol = ctx.User.FindFirstValue(ClaimTypes.Role)!;
        var idPaciente = int.TryParse(ctx.User.FindFirstValue(ClaimIdPaciente), out var ip) ? ip : (int?)null;
        var idProfesional = int.TryParse(ctx.User.FindFirstValue(ClaimIdProfesional), out var ipr) ? ipr : (int?)null;
        return new Identidad(idUsuario, rol, idPaciente, idProfesional);
    }
}
