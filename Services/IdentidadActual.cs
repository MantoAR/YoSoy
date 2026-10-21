using System.Security.Claims;

// Única función para "quién es el usuario actual" en todo el backend (Fase JWT). El
// ClaimsPrincipal ya viene armado por el pipeline de autenticación de Program.cs (el
// esquema "Smart" elige JWT si hay Bearer, o LegacyHeaderAuthHandler si no y
// Auth:PermitirEncabezadoLegacy está en true) — esta clase solo lee los claims, ya
// resueltos, sea cual sea el camino que los puso ahí. Reemplaza a
// AutorizacionCasos/AutorizacionReporte y a cada int.TryParse(ctx.Request.Headers
// ["X-Usuario-Id"], ...) suelto que leía la identidad directo del header.
//
// Etapa 2 de roles y permisos: el token de trabajo es de un módulo y lleva un claim de rol
// por cada permiso del usuario en ese módulo (en Profesional: "profesional" y, si es
// auditor, también "auditor") y uno de privilegio por cada privilegio. Rol sigue siendo el
// rol principal (el del módulo), así el código que compara Rol no cambia.
public record Identidad(int IdUsuario, string Rol, int? IdPaciente, int? IdProfesional)
{
    public IReadOnlyCollection<string> Permisos { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Privilegios { get; init; } = Array.Empty<string>();
    public string? Modulo { get; init; }
    // Token de selección de módulo (5 minutos): solo sirve para POST /sesion/modulo.
    public bool EsSeleccion { get; init; }
    // Token de cambio obligatorio de contraseña (5 minutos): solo sirve para POST /sesion/cambiar-password.
    public bool EsCambioClave { get; init; }

    private bool Tiene(string rol) => Rol == rol || Permisos.Contains(rol);
    public bool EsConfigurador => Tiene(Roles.Configurador);
    public bool EsProfesional => Tiene(Roles.Profesional);
    public bool EsAuditor => Tiene(Roles.Auditor);
    public bool EsPaciente => Tiene(Roles.Paciente);
    public bool EsCrm => Tiene(Roles.Crm);
    public bool TienePrivilegio(string privilegio) => Privilegios.Contains(privilegio);
    // Roles y permisos: los dos privilegios que cambian lo que se puede hacer (ver Politicas.cs).
    public bool HaceAdmision => EsProfesional && TienePrivilegio(global::Privilegios.Admision);
    public bool GestionaAgendas => EsCrm && TienePrivilegio(global::Privilegios.GestionAgendas);
}

// Mismos 3 valores, en minúscula, que ya devuelve POST /login en el campo "rol" (contrato
// existente, no se cambia) — el claim de rol del token usa la misma palabra.
// Etapa 2: se suman los permisos de los otros módulos. Auditor siempre viaja junto con
// Profesional (el auditor tiene todo lo del profesional), así que lo que exige
// Roles.Profesional le sigue funcionando sin cambios.
public static class Roles
{
    public const string Paciente = "paciente";
    public const string Profesional = "profesional";
    public const string Configurador = "configurador";
    public const string Auditor = "auditor";
    public const string Crm = "crm";
    public const string Facturacion = "facturacion";
}

public class IdentidadActualService
{
    public const string ClaimIdPaciente = "idPaciente";
    public const string ClaimIdProfesional = "idProfesional";

    public Identidad? Resolver(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true) return null;

        var idUsuario = int.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var permisos = ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToList();
        var modulo = ctx.User.FindFirstValue(JwtEmisorService.ClaimModulo);
        // Rol principal: el del módulo (tokens de la etapa 2) o el único rol (tokens de
        // paciente y tokens anteriores). Un token de selección no tiene rol.
        var rol = modulo ?? permisos.FirstOrDefault() ?? "";
        var idPaciente = int.TryParse(ctx.User.FindFirstValue(ClaimIdPaciente), out var ip) ? ip : (int?)null;
        var idProfesional = int.TryParse(ctx.User.FindFirstValue(ClaimIdProfesional), out var ipr) ? ipr : (int?)null;
        return new Identidad(idUsuario, rol, idPaciente, idProfesional)
        {
            Permisos = permisos,
            Privilegios = ctx.User.FindAll(JwtEmisorService.ClaimPrivilegio).Select(c => c.Value).Distinct().ToList(),
            Modulo = modulo,
            EsSeleccion = ctx.User.FindFirstValue(JwtEmisorService.ClaimUso) == JwtEmisorService.UsoSeleccion,
            EsCambioClave = ctx.User.FindFirstValue(JwtEmisorService.ClaimUso) == JwtEmisorService.UsoCambioClave
        };
    }
}
