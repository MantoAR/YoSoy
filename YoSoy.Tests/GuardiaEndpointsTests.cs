using Microsoft.AspNetCore.Mvc.Testing;

// Cierre de seguridad, Paso 1: guardia automática para que ningún endpoint nuevo quede sin
// decidir. Falla si EndpointDataSource (reflexión real sobre la tabla de rutas del
// servidor, ver ReflexionEndpoints.cs) encuentra un endpoint sin exigir autenticación que no
// esté en la lista explícita de públicos de abajo. Un endpoint nuevo olvidado rompe esta
// prueba en vez de quedar abierto en silencio.
public class GuardiaEndpointsTests
{
    // Lista explícita de públicos (ver docs/MATRIZ_ENDPOINTS.md, "Guardia automática" para
    // el por qué de cada uno). Archivos estáticos (/pantallas/*) no aparecen: los sirve
    // UseStaticFiles, un middleware, nunca un endpoint — EndpointDataSource no los ve.
    private static readonly HashSet<(string Metodo, string Ruta)> Publicos = new()
    {
        ("POST", "/login"),                                          // los 3 roles devuelven distinto según quién sea, misma ruta
        ("POST", "/pacientes"),                                       // registro público de paciente
        ("POST", "/profesionales"),                                   // registro público de profesional
        ("POST", "/recuperacion-password"),                           // pedido de recuperación: todavía no hay sesión
        ("GET", "/recuperacion-password/estado/{username}"),          // consulta de estado del pedido: idem
        ("GET", "/reservar/{slug}"),                                  // página pública de reserva
        ("GET", "/public/profesionales/{slug}/disponibilidad"),       // disponibilidad pública
        ("POST", "/public/profesionales/{slug}/reservar"),            // reserva pública (misma superficie, rate-limited)
        ("GET", "/capacidades"),                                      // lo consulta cualquier pantalla antes de saber quién sos; sin datos sensibles
        ("POST", "/mensajeria/simulado/entrante"),                    // equivalente a un webhook: no hay sesión del otro lado
        ("GET", "/mensajeria/simulado/conversacion"),                 // idem; ambos ya cierran con Disponible()/SoloLocal, no con JWT
        ("GET", "/especialidades"),                                   // catálogo sin PII; lo necesita el registro público de profesional antes de loguearse
        ("GET", "/especialidades/{id}"),                              // idem, por id
        ("GET", "/profesionales/{id}/agenda"),                        // cualquier paciente necesita ver el horario de CUALQUIER profesional al pedir turno (decisión preexistente, ver README)
        ("POST", "/turnos"),                                          // Paso 3: apagado (410 Gone) para todos por igual, no expone nada — ver el comentario en Program.cs
    };

    [Fact]
    public void Ningun_endpoint_sin_autenticacion_queda_fuera_de_la_lista_de_publicos()
    {
        using var factory = new WebApplicationFactory<Program>();
        var endpoints = ReflexionEndpoints.Listar(factory);

        var sinDecidir = endpoints
            .Where(e => !e.RequiereAuth && !Publicos.Contains((e.Metodo, e.Ruta)))
            .ToList();

        Assert.True(sinDecidir.Count == 0,
            "Hay endpoints sin autenticación que no están en la lista explícita de públicos " +
            "de GuardiaEndpointsTests.Publicos (agregalos ahí si de verdad tienen que ser " +
            "públicos, o protegelos si no):\n" +
            string.Join("\n", sinDecidir.Select(e => $"  {e.Metodo} {e.Ruta}")));
    }
}
