using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// Emite el token de los 3 logins (Paciente, Profesional, Configurador). 8 horas, sin
// refresh token — pedido así a propósito, es para la demo de hoy, no para producción.
public class JwtEmisorService
{
    public const int HorasExpiracion = 8;

    // Etapa 2 (acceso por módulo).
    public const string ClaimModulo = "modulo";
    public const string ClaimPrivilegio = "privilegio";
    public const string ClaimUso = "uso";
    public const string UsoSeleccion = "seleccion-modulo";
    public const int MinutosSeleccion = 5;
    // Cambio obligatorio de contraseña (migración 24): token corto que solo sirve para POST /sesion/cambiar-password.
    public const string UsoCambioClave = "cambio-clave";
    public const int MinutosCambioClave = 5;

    private readonly SymmetricSecurityKey _clave;

    public JwtEmisorService(IConfiguration config)
    {
        _clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtConfig.ClaveOFallar(config)));
    }

    public string Emitir(int idUsuario, string rol, int? idPaciente = null, int? idProfesional = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, idUsuario.ToString()),
            new(ClaimTypes.Role, rol)
        };
        if (idPaciente is not null) claims.Add(new(IdentidadActualService.ClaimIdPaciente, idPaciente.Value.ToString()));
        if (idProfesional is not null) claims.Add(new(IdentidadActualService.ClaimIdProfesional, idProfesional.Value.ToString()));

        return Firmar(claims, DateTime.UtcNow.AddHours(HorasExpiracion));
    }

    // Token de trabajo de un módulo: un claim de rol por permiso y uno por privilegio.
    public string EmitirModulo(int idUsuario, SesionModulo sesion)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, idUsuario.ToString()),
            new(ClaimModulo, sesion.Modulo)
        };
        claims.AddRange(sesion.Permisos.Select(p => new Claim(ClaimTypes.Role, p)));
        claims.AddRange(sesion.Privilegios.Select(p => new Claim(ClaimPrivilegio, p)));
        if (sesion.IdProfesional is not null) claims.Add(new(IdentidadActualService.ClaimIdProfesional, sesion.IdProfesional.Value.ToString()));
        return Firmar(claims, DateTime.UtcNow.AddHours(HorasExpiracion));
    }

    // Token de selección: sin roles, 5 minutos, y Program.cs lo rechaza en cualquier ruta
    // que no sea POST /sesion/modulo (evento OnTokenValidated de AddJwtBearer).
    public string EmitirSeleccion(int idUsuario)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, idUsuario.ToString()),
            new(ClaimUso, UsoSeleccion)
        };
        return Firmar(claims, DateTime.UtcNow.AddMinutes(MinutosSeleccion));
    }

    // Token de cambio de clave: mismo patrón que el de selección. Sin roles, 5 minutos, y Program.cs lo rechaza en cualquier ruta
    // que no sea POST /sesion/cambiar-password (evento OnTokenValidated de AddJwtBearer).
    public string EmitirCambioClave(int idUsuario)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, idUsuario.ToString()),
            new(ClaimUso, UsoCambioClave)
        };
        return Firmar(claims, DateTime.UtcNow.AddMinutes(MinutosCambioClave));
    }

    private string Firmar(IEnumerable<Claim> claims, DateTime vence)
    {
        var token = new JwtSecurityToken(
            claims: claims,
            expires: vence,
            signingCredentials: new SigningCredentials(_clave, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// Mensaje único, compartido entre JwtEmisorService y la configuración de AddJwtBearer en
// Program.cs, para que el mismo error claro salga sin importar cuál de los dos lo pide
// primero al arrancar.
public static class JwtConfig
{
    public static string ClaveOFallar(IConfiguration config)
    {
        var clave = config["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(clave))
            throw new InvalidOperationException(
                "Falta configurar Jwt:Key — sin esto la app no puede firmar ni validar tokens. " +
                "En desarrollo: dotnet user-secrets set \"Jwt:Key\" \"<una clave aleatoria larga, 32+ caracteres>\". " +
                "En otro entorno: variable de entorno Jwt__Key.");
        if (clave.Length < 32)
            throw new InvalidOperationException("Jwt:Key es muy corta (menos de 32 caracteres) — generá una más larga y aleatoria.");
        return clave;
    }
}
