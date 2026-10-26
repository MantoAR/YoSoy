// La aceptación de la videollamada, sesión por sesión (migración 28). Se registra con app.MapAceptacionVideollamada(). Ver AceptacionVideollamadaService.
//
//   GET  /video/aceptacion?origen=&id=      El paciente de la sesión: ¿ya aceptó? y el texto que tiene que leer.
//   PUT  /video/aceptacion                  El paciente acepta: { origen, id, version } (la versión del texto que leyó).
//   GET  /video/aceptaciones-vigentes       El paciente: sus sesiones recientes con la videollamada aceptada (y si aceptó emociones), para revocar desde su perfil.
//   POST /video/aceptacion/revocar          El paciente deja de usar el video: { origen, id }. Corta las emociones y avisa al profesional.
//
// Mismas reglas que POST /video/acceso y que el consentimiento de emociones: la persona solo dice DE QUÉ SESIÓN habla; quién es parte lo decide el servidor ("no existe" y "no sos
// parte" son la misma respuesta, 404). Nunca se cachea. Respuestas de error: { mensaje, codigo } (más cerroUtc si la sesión ya terminó).
public static class AceptacionVideollamadaEndpoints
{
    public static IEndpointRouteBuilder MapAceptacionVideollamada(this IEndpointRouteBuilder app)
    {
        app.MapGet("/video/aceptacion", (string? origen, string? id, HttpContext ctx, IdentidadActualService identidad, AceptacionVideollamadaService servicio) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var sesion = Sesion(origen, id);
            if (sesion is null) return Invalida();
            return Responder(servicio.Obtener(identidad.Resolver(ctx)!, sesion.Value.Origen, sesion.Value.Id));
        }).RequireAuthorization().RequireRateLimiting("video-consentimiento");

        // «Mis consentimientos» (perfil del paciente): las sesiones recientes con la videollamada aceptada y si también aceptó las emociones, para poder revocarlas desde ahí.
        app.MapGet("/video/aceptaciones-vigentes", (HttpContext ctx, IdentidadActualService identidad, AceptacionVideollamadaService servicio) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(servicio.VigentesDe(identidad.Resolver(ctx)!).Select(a => new { origen = a.Origen, id = a.IdOrigen, desdeUtc = a.DesdeUtc, emocionesAceptadas = a.EmocionesAceptadas }));
        }).Solo(Quien.Paciente).RequireRateLimiting("video-consentimiento");

        app.MapPut("/video/aceptacion", (AceptacionVideoRequest? req, HttpContext ctx, IdentidadActualService identidad, AceptacionVideollamadaService servicio) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var sesion = Sesion(req?.Origen, req?.Id);
            if (sesion is null) return Invalida();
            return Responder(servicio.Aceptar(identidad.Resolver(ctx)!, sesion.Value.Origen, sesion.Value.Id, req!.Version));
        }).RequireAuthorization().RequireRateLimiting("video-consentimiento");

        app.MapPost("/video/aceptacion/revocar", (SesionVideoRequest? req, HttpContext ctx, IdentidadActualService identidad, AceptacionVideollamadaService servicio) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var sesion = Sesion(req?.Origen, req?.Id);
            if (sesion is null) return Invalida();
            return Responder(servicio.Revocar(identidad.Resolver(ctx)!, sesion.Value.Origen, sesion.Value.Id));
        }).RequireAuthorization().RequireRateLimiting("video-consentimiento");

        return app;
    }

    private static (string Origen, int Id)? Sesion(string? origen, int? id)
    {
        var o = TextoNormalizado.Normalizar(origen);
        if ((o != OrigenSala.Turno && o != OrigenSala.Admision) || id is not > 0) return null;
        return (o, id.Value);
    }

    private static (string Origen, int Id)? Sesion(string? origen, string? id) =>
        Sesion(origen, int.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : (int?)null);

    private static IResult Invalida() =>
        Results.Json(new { mensaje = "Indicá de qué sesión se trata: origen (turno o admision) e id.", codigo = "solicitud-invalida" }, statusCode: 400);

    private static IResult Responder(ResultadoAceptacion r)
    {
        if (!r.Ok)
        {
            var status = r.Tipo switch { TipoResultado.NoEncontrada => 404, TipoResultado.SolicitudInvalida => 400, _ => 409 };
            return r.CerroUtc is { } c
                ? Results.Json(new { mensaje = r.Mensaje, codigo = r.Codigo, cerroUtc = c.UtcDateTime }, statusCode: status)
                : Results.Json(new { mensaje = r.Mensaje, codigo = r.Codigo }, statusCode: status);
        }
        var e = r.Estado!;
        return Results.Ok(new
        {
            exigida = e.Exigida,
            estado = e.Estado,
            version = e.Version,
            titulo = e.Titulo,
            texto = e.Texto,
            marcaProvisoria = e.MarcaProvisoria,
            decididoUtc = e.DecididoUtc,
            revocadoUtc = e.RevocadoUtc
        });
    }
}

public record AceptacionVideoRequest(string? Origen, int? Id, string? Version);
