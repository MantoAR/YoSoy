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

        // Con la unión (Configurador + CRM) pedir Configurador da Administración (otra prueba la cubre); acá, sin CRM, sigue siendo solo Configurador.
        var sinCrm = Accesos(new[] { Profesional, Configurador }, new PrivilegioAsignado(Roles.Profesional, Privilegios.Admision));
        var (_, conf) = ResolverSesion.Para(sinCrm, Modulos.Configurador, 1);
        Assert.Equal(new[] { Roles.Configurador }, conf!.Permisos);
        Assert.Empty(conf.Privilegios);
        Assert.Null(conf.IdProfesional);   // fuera del módulo Profesional no viaja la ficha
    }

    // ───────────── Vista única: la unión Configurador + CRM es el módulo "Administración" ─────────────

    [Fact]
    public void La_union_se_ofrece_como_una_sola_opcion_en_el_lugar_del_CRM_y_los_demas_modulos_siguen_aparte()
    {
        Assert.Equal(new[] { Modulos.Administracion }, Accesos(new[] { Configurador, Crm }).ModulosVisibles);
        Assert.Equal(new[] { Modulos.Profesional, Modulos.Administracion }, Accesos(new[] { Configurador, Crm, Profesional }).ModulosVisibles);
        var conFacturacion = Accesos(new[] { Configurador, Crm, new PermisoAsignado(Modulos.Facturacion, Roles.Facturacion) });
        Assert.Equal(new[] { Modulos.Administracion, Modulos.Facturacion }, conFacturacion.ModulosVisibles);
        // Los módulos de la base no cambian: la unión no se guarda, se arma.
        Assert.Equal(new[] { Modulos.Crm, Modulos.Facturacion, Modulos.Configurador }, conFacturacion.Modulos);
    }

    [Fact]
    public void Con_un_solo_permiso_de_los_dos_no_hay_union_y_todo_sigue_como_antes()
    {
        var soloCrm = Accesos(new[] { Crm });
        Assert.Equal(new[] { Modulos.Crm }, soloCrm.ModulosVisibles);
        Assert.False(soloCrm.TieneAdministracion);
        var (r, sesion) = ResolverSesion.Para(soloCrm, Modulos.Crm, null);
        Assert.Equal(ResolverSesion.Resultado.Ok, r);
        Assert.Equal(Modulos.Crm, sesion!.Modulo);
        Assert.Equal(new[] { Roles.Crm }, sesion.Permisos);

        var soloConfigurador = Accesos(new[] { Configurador });
        Assert.Equal(new[] { Modulos.Configurador }, soloConfigurador.ModulosVisibles);
        Assert.Equal(Modulos.Configurador, ResolverSesion.Para(soloConfigurador, Modulos.Configurador, null).sesion!.Modulo);
    }

    [Theory]
    [InlineData("crm")]
    [InlineData("configurador")]
    [InlineData("profesional")]
    public void Administracion_sin_los_dos_permisos_se_rechaza_con_cualquier_otro_permiso(string unico)
    {
        var permiso = unico switch { "crm" => Crm, "configurador" => Configurador, _ => Profesional };
        var (resultado, sesion) = ResolverSesion.Para(Accesos(new[] { permiso }), Modulos.Administracion, 1);
        Assert.Equal(ResolverSesion.Resultado.ModuloNoHabilitado, resultado);
        Assert.Null(sesion);
        // Ni siquiera con Configurador + Profesional (le falta CRM) ni con CRM + Profesional (le falta Configurador).
        Assert.Equal(ResolverSesion.Resultado.ModuloNoHabilitado, ResolverSesion.Para(Accesos(new[] { permiso, Profesional, Auditor }), Modulos.Administracion, 1).resultado);
    }

    [Fact]
    public void El_token_de_Administracion_lleva_exactamente_los_roles_y_privilegios_de_Configurador_y_CRM()
    {
        var a = Accesos(new[] { Profesional, Auditor, Crm, Configurador },
            new PrivilegioAsignado(Roles.Profesional, Privilegios.Admision),
            new PrivilegioAsignado(Roles.Crm, Privilegios.GestionAgendas));

        foreach (var pedido in new[] { Modulos.Administracion, Modulos.Crm, Modulos.Configurador })
        {
            var (resultado, sesion) = ResolverSesion.Para(a, pedido, 5);
            Assert.Equal(ResolverSesion.Resultado.Ok, resultado);
            Assert.Equal(Modulos.Administracion, sesion!.Modulo);
            Assert.Equal(new[] { Roles.Configurador, Roles.Crm }, sesion.Permisos);          // ni profesional ni auditor
            Assert.Equal(new[] { Privilegios.GestionAgendas }, sesion.Privilegios);          // ni la admisión
            Assert.Null(sesion.IdProfesional);                                               // la ficha solo viaja en el módulo Profesional
        }
        // El módulo Profesional del mismo usuario sigue siendo aparte.
        var (_, prof) = ResolverSesion.Para(a, Modulos.Profesional, 5);
        Assert.Equal(new[] { Roles.Auditor, Roles.Profesional }, prof!.Permisos);
        Assert.Equal(new[] { Privilegios.Admision }, prof.Privilegios);
    }

    [Fact]
    public void Un_token_de_Administracion_se_lee_como_Configurador_y_CRM_a_la_vez()
    {
        var sesion = new SesionModulo(Modulos.Administracion, new[] { Roles.Configurador, Roles.Crm }, new[] { Privilegios.GestionAgendas }, null);
        var yo = IdentidadDe(Emisor().EmitirModulo(7, sesion));

        Assert.Equal(Modulos.Administracion, yo.Modulo);
        Assert.True(yo.EsConfigurador);
        Assert.True(yo.EsCrm);
        Assert.True(yo.GestionaAgendas);
        Assert.False(yo.EsProfesional);
        Assert.False(yo.EsAuditor);
        Assert.False(yo.HaceAdmision);
        Assert.False(yo.EsPaciente);
        // Las políticas por rol que usa el servidor lo aceptan donde piden Configurador o CRM, y nada más.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Emisor().EmitirModulo(7, sesion));
        var tipoRol = jwt.Claims.Any(c => c.Type == "role") ? "role" : ClaimTypes.Role;   // según cómo el manejador de JWT nombre el claim al leerlo
        var usuario = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "prueba", ClaimTypes.NameIdentifier, tipoRol));
        Assert.True(Politicas.Cumple(usuario, Quien.Configurador));
        Assert.True(Politicas.Cumple(usuario, Quien.Crm));
        Assert.True(Politicas.Cumple(usuario, Quien.Agendas));
        Assert.False(Politicas.Cumple(usuario, Quien.Profesional));
        Assert.False(Politicas.Cumple(usuario, Quien.Admision));
        Assert.False(Politicas.Cumple(usuario, Quien.Auditor));
        Assert.False(Politicas.Cumple(usuario, Quien.Paciente));
    }

    [Fact]
    public void Administracion_tiene_nombre_y_descripcion_para_el_selector()
    {
        Assert.Equal("Administración", Modulos.Nombre(Modulos.Administracion));
        Assert.False(string.IsNullOrWhiteSpace(Modulos.Descripcion(Modulos.Administracion)));
        Assert.DoesNotContain(Modulos.Administracion, Modulos.Todos);   // no es un módulo de la base
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
