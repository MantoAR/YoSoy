using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// Herramienta de desarrollo, no una prueba de verdad: emite tokens JWT para los 3 usuarios
// de prueba que ya usa docs/pruebas-ui-cambios-agus/comun.py (ROLES), firmados con el mismo
// Jwt:Key que WebApplicationFactory toma de los user-secrets reales (los mismos que usa
// "dotnet run" en esta máquina) — así los scripts de Playwright pueden simular una sesión
// real sin tocar contraseñas. Se borra cuando el resto de la bandeja de chats quede probada;
// no tiene sentido commitearla aparte del branch de trabajo.
public class _MintTokensPlaywright
{
    [Fact]
    public async Task Imprime_tokens_para_los_3_roles_de_prueba()
    {
        using var factory = new WebApplicationFactory<Program>();
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();

        var configurador = emisor.Emitir(1, Roles.Configurador);
        var profesional = emisor.Emitir(2, Roles.Profesional, idProfesional: 1);
        var paciente = emisor.Emitir(3, Roles.Paciente, idPaciente: 1);

        await Task.CompletedTask;
        Console.WriteLine("TOKEN_CONFIGURADOR=" + configurador);
        Console.WriteLine("TOKEN_PROFESIONAL=" + profesional);
        Console.WriteLine("TOKEN_PACIENTE=" + paciente);
    }
}
