using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// Emite el token de los 3 logins (Paciente, Profesional, Configurador). 8 horas, sin
// refresh token — pedido así a propósito, es para la demo de hoy, no para producción.
public class JwtEmisorService
{
    public const int HorasExpiracion = 8;
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

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(HorasExpiracion),
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
