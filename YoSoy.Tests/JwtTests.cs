using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

// Fase JWT: JwtEmisorService (emisión de los 3 logins) y JwtConfig (la clave, que nunca
// vive en un archivo versionado). No depende de user-secrets de esta máquina — cada test
// arma su propia clave en memoria, autocontenido.
public class JwtTests
{
    private static IConfiguration Config(string? clave = "una-clave-de-prueba-bien-larga-1234567890") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = clave }).Build();

    [Fact]
    public void Emitir_token_de_paciente_lleva_los_claims_correctos()
    {
        var emisor = new JwtEmisorService(Config());
        var token = emisor.Emitir(3, Roles.Paciente, idPaciente: 1);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("3", jwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(Roles.Paciente, jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal("1", jwt.Claims.First(c => c.Type == IdentidadActualService.ClaimIdPaciente).Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == IdentidadActualService.ClaimIdProfesional);

        var minutos = (jwt.ValidTo - DateTime.UtcNow).TotalMinutes;
        Assert.InRange(minutos, 7 * 60 + 55, 8 * 60 + 5);   // ~8 horas, con margen
    }

    [Fact]
    public void Emitir_token_de_configurador_no_lleva_idPaciente_ni_idProfesional()
    {
        var emisor = new JwtEmisorService(Config());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(emisor.Emitir(1, Roles.Configurador));

        Assert.DoesNotContain(jwt.Claims, c => c.Type == IdentidadActualService.ClaimIdPaciente);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == IdentidadActualService.ClaimIdProfesional);
    }

    [Fact]
    public void Token_valido_pasa_la_validacion_con_la_misma_clave()
    {
        var clave = "otra-clave-de-prueba-tambien-larga-9876543210";
        var token = new JwtEmisorService(Config(clave)).Emitir(2, Roles.Profesional, idProfesional: 1);

        var parametros = ParametrosDeValidacion(clave);
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, parametros, out _);
        Assert.Equal("2", principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public void Token_firmado_con_otra_clave_no_pasa_la_validacion()
    {
        var token = new JwtEmisorService(Config("clave-A-bien-larga-1111111111111111")).Emitir(2, Roles.Profesional);
        var parametros = ParametrosDeValidacion("clave-B-bien-larga-2222222222222222");

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, parametros, out _));
    }

    [Fact]
    public void Token_vencido_no_pasa_la_validacion()
    {
        var clave = "clave-para-el-test-de-vencido-33333333333333";
        var claveBytes = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave));
        var vencido = new JwtSecurityToken(
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, "5"), new Claim(ClaimTypes.Role, Roles.Paciente) },
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: new SigningCredentials(claveBytes, SecurityAlgorithms.HmacSha256));
        var tokenStr = new JwtSecurityTokenHandler().WriteToken(vencido);

        Assert.Throws<SecurityTokenExpiredException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(tokenStr, ParametrosDeValidacion(clave), out _));
    }

    [Fact]
    public void ClaveOFallar_tira_un_mensaje_claro_si_falta_la_clave()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtConfig.ClaveOFallar(Config(null)));
        Assert.Contains("Jwt:Key", ex.Message);
    }

    [Fact]
    public void ClaveOFallar_tira_un_mensaje_claro_si_la_clave_es_muy_corta()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtConfig.ClaveOFallar(Config("corta")));
        Assert.Contains("corta", ex.Message);
    }

    private static TokenValidationParameters ParametrosDeValidacion(string clave) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave)),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
}
