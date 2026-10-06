using Microsoft.Extensions.Options;

// Consentimiento informado de telemedicina (RN-02): estado y firma desde la página
// PANTALLAS/Pantalla_Paciente/consentimiento.html. Se registran con app.MapConsentimiento().
//
// Firmar (POST) exige rol Paciente y que el id de la URL sea el mismo paciente del
// token (ver IdentidadActualService) — así nadie firma en nombre de otro. Resuelto en
// la rama Cambios-Agus-jwt; el TODO(JWT) que tenía este archivo ya no aplica.
public static class ConsentimientoEndpoints
{
    public static IEndpointRouteBuilder MapConsentimiento(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pacientes/{id}/consentimiento-telemedicina", (int id, IOptionsMonitor<ConsentimientoOptions> opciones) =>
        {
            var op = opciones.CurrentValue;
            if (new PacienteDAO().ObtenerPorId(id) is null) return Results.NotFound();
            var c = new ConsentimientoDAO().ObtenerVigente(id, op.TipoRequerido);
            return Results.Ok(new
            {
                firmado        = c != null,
                fechaFirma     = c?.FechaFirma,
                versionFirmada = c?.Version,
                tipo           = op.TipoRequerido,
                versionTexto   = op.VersionTexto
            });
        });

        // La firma exige { "acepto": true } explícito y la versión del texto que vio el
        // paciente: si el texto cambió mientras tenía la página abierta, tiene que volver a leerlo.
        app.MapPost("/pacientes/{id}/consentimiento-telemedicina", (int id, FirmaConsentimientoRequest req,
            IOptionsMonitor<ConsentimientoOptions> opciones, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx);
            if (yo is null || yo.IdPaciente != id)
                return Results.Json(new { mensaje = "Solo el propio paciente puede firmar su consentimiento." }, statusCode: 403);

            var op = opciones.CurrentValue;
            if (req.Acepto != true)
                return Results.BadRequest(new { mensaje = "Para firmar hay que aceptar el consentimiento de forma explícita." });
            if (req.Version != op.VersionTexto)
                return Results.Json(new { mensaje = "El texto del consentimiento cambió. Volvé a leerlo antes de firmar." }, statusCode: 409);
            if (new PacienteDAO().ObtenerPorId(id) is null) return Results.NotFound();

            var dao = new ConsentimientoDAO();
            var resultado = dao.FirmarSiNoExiste(id, op.TipoRequerido, op.VersionTexto, reloj.GetLocalNow().DateTime);
            if (resultado == -1)
                return Results.Problem($"No existe el tipo de consentimiento \"{op.TipoRequerido}\". " +
                                       "Correr migrations/add_consentimiento_telemedicina.sql.");

            var vigente = dao.ObtenerVigente(id, op.TipoRequerido);
            if (resultado == 0)
                return Results.Json(new { mensaje = "El consentimiento ya estaba firmado.", fechaFirma = vigente?.FechaFirma }, statusCode: 409);
            return Results.Created($"/pacientes/{id}/consentimiento-telemedicina",
                new { firmado = true, fechaFirma = vigente?.FechaFirma, versionFirmada = vigente?.Version });
        }).RequireAuthorization(p => p.RequireRole(Roles.Paciente));

        return app;
    }
}

public record FirmaConsentimientoRequest(bool? Acepto, string? Version);

// Respuesta HTTP de una regla de negocio incumplida: 409 con un cuerpo que el
// frontend puede mostrar tal cual (y el link para resolverlo, si hay).
public static class ResultadoReglaHttp
{
    public static IResult ComoConflicto(this ResultadoRegla r) =>
        Results.Json(new { codigo = r.Codigo, mensaje = r.Mensaje, urlConsentimiento = r.Link }, statusCode: 409);
}
