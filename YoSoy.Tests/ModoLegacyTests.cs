using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// Cierre de seguridad, Paso 5: con Auth:PermitirEncabezadoLegacy en false (el valor en
// appsettings.json desde la Fase JWT), un X-Usuario-Id solo, sin Bearer, no tiene que
// identificar a nadie. Corre contra appsettings.json real (no pisa el flag): si alguna vez
// alguien lo vuelve a poner en true sin querer, esta prueba lo nota.
public class ModoLegacyTests
{
    [Fact]
    public async Task Con_el_flag_en_false_X_Usuario_Id_solo_no_identifica_a_nadie()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Usuario-Id", "1");   // 1 = admin real, a propósito

        var respuesta = await cliente.GetAsync("/auditoria");     // Configurador-only, cualquier endpoint protegido sirve
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_JWT_real_sigue_identificando_con_el_flag_en_false()
    {
        using var factory = new WebApplicationFactory<Program>();
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        using var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", emisor.Emitir(1, Roles.Configurador));

        var respuesta = await cliente.GetAsync("/auditoria");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }
}
