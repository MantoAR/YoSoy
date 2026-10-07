using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

// Cierre de seguridad, Paso 0: lee la tabla de rutas del propio servidor por reflexión
// (EndpointDataSource), para no mantener a mano la lista de "qué requiere qué". La usan el
// generador de docs/MATRIZ_ENDPOINTS.md (una corrida manual, ver CAMBIOS_AGUS.md) y
// GuardiaEndpointsTests (siempre, en cada corrida de la batería).
public record InfoEndpoint(string Metodo, string Ruta, bool RequiereAuth, string? Roles);

public static class ReflexionEndpoints
{
    public static List<InfoEndpoint> Listar(WebApplicationFactory<Program> factory)
    {
        var fuentes = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();
        var resultado = new List<InfoEndpoint>();
        foreach (var endpoint in fuentes.SelectMany(f => f.Endpoints).OfType<RouteEndpoint>())
        {
            var metodos = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new List<string> { "*" };
            var autorizaciones = endpoint.Metadata.OfType<IAuthorizeData>().ToList();
            var roles = string.Join(",", autorizaciones.Select(a => a.Roles).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct());
            foreach (var metodo in metodos)
                resultado.Add(new InfoEndpoint(metodo, endpoint.RoutePattern.RawText ?? "", autorizaciones.Count > 0, roles.Length > 0 ? roles : null));
        }
        return resultado.OrderBy(e => e.Ruta, StringComparer.Ordinal).ThenBy(e => e.Metodo, StringComparer.Ordinal).ToList();
    }
}
