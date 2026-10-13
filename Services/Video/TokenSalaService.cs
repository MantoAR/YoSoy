using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// Emite el token de sala que exige el signaling-server (contrato en signaling-server/README.md):
// JWT HS256 firmado con SIGNALING_SECRETO, que acá se llama Video__SignalingSecreto y tiene el MISMO valor.
// Claims: sala, rol ("profesional" | "paciente"), sub (id de usuario) y exp (obligatorio). El secreto se usa como
// UTF-8, igual que hace el signaling. Con nombres cortos y sin pasar por el mapeo de claims del handler.
public class TokenSalaService
{
    private readonly IOptionsMonitor<VideoOptions> _opciones;
    private readonly IConfiguration _config;

    public TokenSalaService(IOptionsMonitor<VideoOptions> opciones, IConfiguration config)
    {
        _opciones = opciones; _config = config;
    }

    /// <summary>null si se pueden emitir tokens; si no, por qué (sin el valor del secreto).</summary>
    public string? ProblemaDeConfiguracion() =>
        VideoSecretos.Problema("Video__SignalingSecreto", _opciones.CurrentValue.SignalingSecreto, _config["Jwt:Key"]);

    /// <summary>
    /// Vencimiento del token: el menor entre el fin de la ventana de la sesión y ahora + el máximo configurado
    /// (Video:Token:MaxMinutos, 120 por defecto). En segundos enteros, como el exp del JWT.
    /// </summary>
    public DateTimeOffset CalcularExp(DateTimeOffset ahora, DateTimeOffset finDeLaVentana)
    {
        var tope = ahora.AddMinutes(AccesoSalaService.Acotar(_opciones.CurrentValue.Token.MaxMinutos, 1, 720));
        var exp = finDeLaVentana < tope ? finDeLaVentana : tope;
        return DateTimeOffset.FromUnixTimeSeconds(exp.ToUnixTimeSeconds());
    }

    public string Emitir(string sala, string rol, string sub, DateTimeOffset expira)
    {
        if (ProblemaDeConfiguracion() is { } problema) throw new InvalidOperationException(problema);

        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opciones.CurrentValue.SignalingSecreto!));
        var token = new JwtSecurityToken(
            claims: new[]
            {
                new Claim("sala", sala),
                new Claim("rol", rol),
                new Claim(JwtRegisteredClaimNames.Sub, sub)
            },
            expires: expira.UtcDateTime,
            signingCredentials: new SigningCredentials(clave, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
