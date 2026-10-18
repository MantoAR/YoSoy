// Consentimiento de los avisos de turnos por WhatsApp. Se registra con app.MapConsentimientoWhatsApp().
//
//   GET  /consentimiento-whatsapp/texto      Sin sesión: la versión y el texto vigentes (los necesita la pantalla de registro).
//   GET  /consentimiento-whatsapp            El paciente de la sesión: su estado, el texto que tiene que leer y, si hay un número de la clínica
//                                            configurado, el link wa.me para escribirle primero (contacto.enlace; null si no hay).
//   POST /consentimiento-whatsapp/aceptar    El paciente acepta: { version } (la del texto que leyó).
//   POST /consentimiento-whatsapp/revocar    El paciente deja de recibir avisos por WhatsApp.
//
// Quién es el paciente sale SIEMPRE del JWT, nunca del pedido: no hay un id en la URL ni en el cuerpo, así que nadie acepta ni revoca por
// otro. Un profesional o el Configurador reciben 403. Nunca se cachea. El registro de un paciente nuevo (POST /pacientes) también puede
// aceptar, con los campos aceptaWhatsApp y versionTextoWhatsApp.
//
// Respuestas de error: { "mensaje", "codigo" }.
public static class ConsentimientoWhatsAppEndpoints
{
    public const string PoliticaDeLimite = "consentimiento-whatsapp";

    public static IEndpointRouteBuilder MapConsentimientoWhatsApp(this IEndpointRouteBuilder app)
    {
        app.MapGet("/consentimiento-whatsapp/texto", (HttpContext ctx, ConsentimientoWhatsAppService servicio) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var t = servicio.Texto();
            return Results.Ok(new { disponible = t.Disponible, version = t.Version, provisorio = t.Provisorio, titulo = t.Titulo, texto = t.Texto });
        }).AllowAnonymous().RequireRateLimiting(PoliticaDeLimite);

        app.MapGet("/consentimiento-whatsapp", (HttpContext ctx, IdentidadActualService identidad, ConsentimientoWhatsAppService servicio,
            ContactoWhatsApp contacto) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Responder(servicio.Obtener(identidad.Resolver(ctx)!), contacto);
        }).RequireAuthorization().RequireRateLimiting(PoliticaDeLimite);

        app.MapPost("/consentimiento-whatsapp/aceptar", (AceptarWhatsAppRequest? req, HttpContext ctx, IdentidadActualService identidad,
            ConsentimientoWhatsAppService servicio, ContactoWhatsApp contacto) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Responder(servicio.Aceptar(identidad.Resolver(ctx)!, req?.Version), contacto);
        }).RequireAuthorization().RequireRateLimiting(PoliticaDeLimite);

        app.MapPost("/consentimiento-whatsapp/revocar", (HttpContext ctx, IdentidadActualService identidad, ConsentimientoWhatsAppService servicio,
            ContactoWhatsApp contacto) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Responder(servicio.Revocar(identidad.Resolver(ctx)!), contacto);
        }).RequireAuthorization().RequireRateLimiting(PoliticaDeLimite);

        return app;
    }

    // "contacto": el link wa.me para escribirle primero a la clínica (opción B), o null si no hay un número configurado.
    internal static IResult Responder(ResultadoConsentimientoWhatsApp r, ContactoWhatsApp contacto)
    {
        if (!r.Ok) return Error(r.Error!);
        var e = r.Estado!;
        var enlace = contacto.Enlace();
        return Results.Ok(new
        {
            disponible = e.Disponible,
            estado = e.Estado,
            motivo = e.Motivo,
            version = e.Version,
            provisorio = e.Provisorio,
            titulo = e.Titulo,
            texto = e.Texto,
            aceptadoUtc = e.AceptadoUtc,
            revocadoUtc = e.RevocadoUtc,
            versionAceptada = e.VersionAceptada,
            contacto = enlace is null ? null : new { enlace }
        });
    }

    internal static IResult Error(ErrorDeConsentimiento e)
    {
        var status = e.Tipo switch
        {
            TipoErrorConsentimiento.SoloPacientes => 403,
            TipoErrorConsentimiento.NoEncontrado => 404,
            TipoErrorConsentimiento.SolicitudInvalida => 400,
            _ => 409
        };
        return Results.Json(new { mensaje = e.Mensaje, codigo = e.Codigo }, statusCode: status);
    }
}

public record AceptarWhatsAppRequest(string? Version);
