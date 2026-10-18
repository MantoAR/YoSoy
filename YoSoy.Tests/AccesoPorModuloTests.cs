using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Etapa 2 de roles y permisos: módulos, permisos y privilegios en el token.
public class AccesoPorModuloTests
{
    private static AccesosUsuario Accesos(PermisoAsignado[] permisos, params PrivilegioAsignado[] privilegios) =>
        new(7, permisos, privilegios);

    private static readonly PermisoAsignado Profesional = new(Modulos.Profesional, Roles.Profesional);
    private static readonly PermisoAsignado Auditor = new(Modulos.Profesional, Roles.Auditor);
    private static readonly PermisoAsignado Crm = new(Modulos.Crm, Roles.Crm);
    private static readonly PermisoAsignado Configurador = new(Modulos.Configurador, Roles.Configurador);

    [Fact]
    public void Sin_permisos_no_hay_modulos_y_no_entra()
    {
        var (resultado, sesion) = ResolverSesion.Para(Accesos(Array.Empty<PermisoAsignado>()), Modulos.Profesional, 1);
        Assert.Equal(ResolverSesion.Resultado.SinPermisos, resultado);
        Assert.Null(sesion);
    }

    [Fact]
    public void Profesional_y_auditor_son_un_solo_modulo()
    {
        var a = Accesos(new[] { Profesional, Auditor });
        Assert.Equal(new[] { Modulos.Profesional }, a.Modulos);   // no se elige entre los dos (definido el 15/10)
    }

    [Fact]
    public void Los_modulos_salen_en_el_orden_del_selector()
    {
        var a = Accesos(new[] { Configurador, Crm, Profesional });
        Assert.Equal(new[] { Modulos.Profesional, Modulos.Crm, Modulos.Configurador }, a.Modulos);
    }

    [Fact]
    public void El_auditor_lleva_tambien_el_rol_profesional()
    {
        var (_, sesion) = ResolverSesion.Para(Accesos(new[] { Auditor }), Modulos.Profesional, 3);
        Assert.NotNull(sesion);
        Assert.Contains(Roles.Profesional, sesion!.Permisos);
        Assert.Contains(Roles.Auditor, sesion.Permisos);
        Assert.Equal(3, sesion.IdProfesional);
    }

    [Fact]
    public void Un_modulo_que_no_tiene_se_rechaza()
    {
        var (resultado, _) = ResolverSesion.Para(Accesos(new[] { Crm }), Modulos.Configurador, null);
        Assert.Equal(ResolverSesion.Resultado.ModuloNoHabilitado, resultado);
    }

    [Fact]
    public void Modulo_profesional_sin_ficha_se_rechaza()
    {
        var (resultado, _) = ResolverSesion.Para(Accesos(new[] { Profesional }), Modulos.Profesional, idProfesional: null);
        Assert.Equal(ResolverSesion.Resultado.FaltaFichaProfesional, resultado);
    }

    [Fact]
    public void El_token_de_un_modulo_no_lleva_permisos_ni_privilegios_de_otro()
    {
        var a = Accesos(new[] { Profesional, Crm, Configurador },
            new PrivilegioAsignado(Roles.Profesional, Privilegios.Admision),
            new PrivilegioAsignado(Roles.Crm, Privilegios.GestionAgendas));

        var (_, prof) = ResolverSesion.Para(a, Modulos.Profesional, 1);
        Assert.Equal(new[] { Roles.Profesional }, prof!.Permisos);
        Assert.Equal(new[] { Privilegios.Admision }, prof.Privilegios);
        Assert.Equal(1, prof.IdProfesional);

        var (_, conf) = ResolverSesion.Para(a, Modulos.Configurador, 1);
        Assert.Equal(new[] { Roles.Configurador }, conf!.Permisos);
        Assert.Empty(conf.Privilegios);
        Assert.Null(conf.IdProfesional);   // fuera del módulo Profesional no viaja la ficha
    }

    private static JwtEmisorService Emisor() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "una-clave-de-prueba-bien-larga-1234567890" }).Build());

    private static Identidad IdentidadDe(string token)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "prueba")) };
        return new IdentidadActualService().Resolver(ctx)!;
    }

    [Fact]
    public void Token_de_modulo_profesional_auditor_se_lee_como_profesional_y_auditor()
    {
        var sesion = new SesionModulo(Modulos.Profesional, new[] { Roles.Auditor, Roles.Profesional }, new[] { Privilegios.Admision }, 3);
        var yo = IdentidadDe(Emisor().EmitirModulo(7, sesion));

        Assert.Equal(Roles.Profesional, yo.Rol);          // el código que compara Rol no cambia
        Assert.True(yo.EsProfesional);
        Assert.True(yo.EsAuditor);
        Assert.False(yo.EsConfigurador);
        Assert.True(yo.TienePrivilegio(Privilegios.Admision));
        Assert.Equal(Modulos.Profesional, yo.Modulo);
        Assert.Equal(3, yo.IdProfesional);
    }

    [Fact]
    public void Token_de_seleccion_no_tiene_roles_y_dura_cinco_minutos()
    {
        var token = Emisor().EmitirSeleccion(7);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == ClaimTypes.Role);
        Assert.InRange((jwt.ValidTo - DateTime.UtcNow).TotalMinutes, 4, 5.1);

        var yo = IdentidadDe(token);
        Assert.True(yo.EsSeleccion);
        Assert.False(yo.EsProfesional || yo.EsConfigurador || yo.EsPaciente);
    }

    [Fact]
    public void Los_tokens_anteriores_a_la_etapa_2_se_siguen_leyendo_igual()
    {
        var yo = IdentidadDe(Emisor().Emitir(1, Roles.Configurador));
        Assert.Equal(Roles.Configurador, yo.Rol);
        Assert.True(yo.EsConfigurador);
        Assert.Null(yo.Modulo);
    }

    [Fact]
    public async Task El_token_de_seleccion_no_sirve_en_otras_rutas()
    {
        // /roles solo pide "autenticado": sin el control del token de selección, respondería 200.
        using var factory = new WebApplicationFactory<Program>();
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", emisor.EmitirSeleccion(1));

        var r = await cliente.GetAsync("/roles");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
