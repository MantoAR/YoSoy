using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

// Esquema de transición (Fase JWT): mientras Auth:PermitirEncabezadoLegacy esté en true,
// si no vino un Bearer válido, autentica igual al viejo X-Usuario-Id sin firmar — resuelve
// el rol como ya hacía AutorizacionCasos (mirando UsuarioProfesional/UsuarioPaciente) y
// arma los mismos claims que pondría un JWT real, para que .RequireAuthorization()/
// RequireRole() en los endpoints no tengan que saber cuál de los dos caminos los dejó
// entrar. Deja un warning en el log con la ruta, para encontrar qué falta migrar. Con el
// flag en false, no autentica nada (NoResult) — un X-Usuario-Id solo, sin token, da 401.
public class LegacyHeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _config;

    public LegacyHeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, IConfiguration config) : base(options, logger, encoder)
    {
        _config = config;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_config.GetValue<bool>("Auth:PermitirEncabezadoLegacy"))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!int.TryParse(Request.Headers["X-Usuario-Id"], out var idUsuario))
            return Task.FromResult(AuthenticateResult.NoResult());

        Logger.LogWarning("Identidad resuelta por X-Usuario-Id (modo legacy) en {Metodo} {Ruta} — falta migrar este camino a JWT.",
            Request.Method, Request.Path);

        string rol;
        int? idPaciente = null, idProfesional = null;

        var up = new UsuarioProfesionalDAO().ObtenerPorUsuario(idUsuario);
        if (up != null) { rol = Roles.Profesional; idProfesional = up.IdProfesional; }
        else
        {
            var upac = new UsuarioPacienteDAO().ObtenerPorUsuario(idUsuario);
            if (upac != null) { rol = Roles.Paciente; idPaciente = upac.IdPaciente; }
            else rol = Roles.Configurador;
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, idUsuario.ToString()), new(ClaimTypes.Role, rol) };
        if (idPaciente is not null) claims.Add(new(IdentidadActualService.ClaimIdPaciente, idPaciente.Value.ToString()));
        if (idProfesional is not null) claims.Add(new(IdentidadActualService.ClaimIdProfesional, idProfesional.Value.ToString()));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
