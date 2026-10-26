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
        // Cierre de seguridad, Paso 4: antes sin ningún chequeo (revela si un paciente
        // firmó o no el consentimiento de telemedicina). Mismo criterio que el resto de
        // lo paciente-propio: el dueño, o el equipo (Profesional/Configurador).
        app.MapGet("/pacientes/{id}/consentimiento-telemedicina", (int id, IOptionsMonitor<ConsentimientoOptions> opciones, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo is { EsPaciente: true } && yo.IdPaciente != id)
                return Results.Json(new { mensaje = "Solo podés ver tu propio consentimiento." }, statusCode: 403);

            var op = opciones.CurrentValue;
            if (new PacienteDAO().ObtenerPorId(id) is null) return Results.NotFound();
            // La misma decisión que usa RN-02 y el requisito de asignar un profesional (ConsentimientoTratamiento): «firmado» es «vigente».
            var e = ConsentimientoTratamiento.Estado(id, op);
            return Results.Ok(new
            {
                firmado        = e.Vigente,
                fechaFirma     = e.FechaFirma,
                versionFirmada = e.Version,
                // Vigencia anual: hasta cuándo vale la firma (null si no vence o no hay firma vigente).
                venceEl        = e.Vigente && e.FechaFirma is DateTime firmada && op.VigenciaMeses > 0 ? firmada.AddMonths(op.VigenciaMeses) : (DateTime?)null,
                tipo           = op.TipoRequerido,
                versionTexto   = op.VersionTexto,
                // Por qué no está vigente: sin-firma, vencido (pasó la vigencia anual), version-anterior (el texto cambió: hay que volver a firmar) o falta-tutor (un menor: lo firma su tutor).
                motivo         = e.Vigente ? null : e.Motivo,
                esMenor        = e.EsMenor && Migracion28.Disponible(),
                tutores        = e.EsMenor && Migracion28.Disponible() ? new ConsentimientoDAO().TutoresDe(id).Select(t => new { idTutor = t.IdTutor, nombre = t.Nombre, vinculo = t.Vinculo }) : null,
                marcaProvisoria = string.IsNullOrWhiteSpace(op.MarcaProvisoria) ? null : op.MarcaProvisoria
            });
        }).RequireAuthorization();

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
            var hoy = reloj.GetLocalNow().DateTime;
            // Una persona menor de edad (o con tutor) no firma sola: firma su tutor, y queda dicho quién. Provisorio: la regla exacta la tiene que validar el equipo legal.
            // TODO-VALIDAR: cómo se acredita al tutor (hoy: la sesión del menor elige a uno de los tutores cargados en su ficha).
            int? idTutorFirmante = null;
            var estadoPrevio = ConsentimientoTratamiento.Estado(id, op.TipoRequerido, op.VersionTexto, hoy, op.VigenciaMeses);
            if (estadoPrevio.EsMenor && Migracion28.Disponible())
            {
                var tutores = dao.TutoresDe(id);
                if (req.IdTutor is not int elegido || !tutores.Any(t => t.IdTutor == elegido))
                    return Results.BadRequest(new { codigo = "falta-tutor", mensaje = "Como sos menor de edad, el consentimiento lo firma tu madre, padre o tutor. Elegí quién firma y volvé a intentar." });
                idTutorFirmante = elegido;
            }
            var resultado = dao.FirmarSiNoExiste(id, op.TipoRequerido, op.VersionTexto, hoy, idTutorFirmante, op.VigenciaMeses);
            if (resultado == -1)
                return Results.Problem($"No existe el tipo de consentimiento \"{op.TipoRequerido}\". " +
                                       "Correr migrations/add_consentimiento_telemedicina.sql.");

            var vigente = ConsentimientoTratamiento.Estado(id, op.TipoRequerido, op.VersionTexto, hoy, op.VigenciaMeses);
            if (resultado == 0)
                return Results.Json(new { mensaje = "El consentimiento ya estaba firmado.", fechaFirma = vigente.FechaFirma }, statusCode: 409);
            return Results.Created($"/pacientes/{id}/consentimiento-telemedicina",
                new { firmado = true, fechaFirma = vigente.FechaFirma, versionFirmada = vigente.Version, idTutorFirmante });
        }).RequireAuthorization(p => p.RequireRole(Roles.Paciente));

        return app;
    }
}

public record FirmaConsentimientoRequest(bool? Acepto, string? Version, int? IdTutor = null);

// Respuesta HTTP de una regla de negocio incumplida: 409 con un cuerpo que el
// frontend puede mostrar tal cual (y el link para resolverlo, si hay).
public static class ResultadoReglaHttp
{
    public static IResult ComoConflicto(this ResultadoRegla r) =>
        Results.Json(new { codigo = r.Codigo, mensaje = r.Mensaje, urlConsentimiento = r.Link }, statusCode: 409);
}
