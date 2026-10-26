using Microsoft.Extensions.Options;

// Consentimiento por sesión para la lectura de emociones de la sala de video (PR 6). Se registra con
// app.MapConsentimientoVideo().
//
//   GET  /video/consentimiento?origen=&id=        El paciente de la sesión: su estado y el texto que tiene que leer.
//   PUT  /video/consentimiento                    El paciente decide: { origen, id, decision: "acepta"|"rechaza", version }.
//   POST /video/consentimiento/revocar            El paciente deja de compartir: { origen, id }. Anula lo ya guardado.
//   GET  /video/emociones/permiso?origen=&id=     El profesional (o el Configurador) de la sesión: ¿puede analizar ahora?
//
// Mismas reglas que POST /video/acceso: la persona solo dice DE QUÉ SESIÓN habla; quién es parte lo decide el servidor
// con la base, y "no existe" y "no sos parte" son la misma respuesta (404, mismo cuerpo). Nunca se cachea. Todo exige el
// JWT de la sesión de YoSoy (no sirve el token de sala). Contrato y reglas: docs/VIDEOLLAMADAS.md.
//
// Respuestas de error: { "mensaje", "codigo" } (más "cerroUtc" cuando la sesión ya terminó).
public static class ConsentimientoVideoEndpoints
{
    public static IEndpointRouteBuilder MapConsentimientoVideo(this IEndpointRouteBuilder app)
    {
        AvisarEstadoAlArrancar(app.ServiceProvider, app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Video"));

        app.MapGet("/video/consentimiento", (string? origen, string? id, HttpContext ctx, IdentidadActualService identidad,
            ConsentimientoVideoService servicio) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var sesion = Sesion(origen, id);
            if (sesion is null) return Invalida();
            return Responder(servicio.Obtener(identidad.Resolver(ctx)!, sesion.Value.Origen, sesion.Value.Id), conTexto: true);
        }).RequireAuthorization().RequireRateLimiting("video-consentimiento");

        app.MapPut("/video/consentimiento", (DecisionRequest? req, HttpContext ctx, IdentidadActualService identidad,
            ConsentimientoVideoService servicio) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var sesion = Sesion(req?.Origen, req?.Id);
            if (sesion is null) return Invalida();
            return Responder(servicio.Decidir(identidad.Resolver(ctx)!, sesion.Value.Origen, sesion.Value.Id, req!.Decision, req.Version), conTexto: false);
        }).RequireAuthorization().RequireRateLimiting("video-consentimiento");

        app.MapPost("/video/consentimiento/revocar", (SesionVideoRequest? req, HttpContext ctx, IdentidadActualService identidad,
            ConsentimientoVideoService servicio) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var sesion = Sesion(req?.Origen, req?.Id);
            if (sesion is null) return Invalida();
            return Responder(servicio.Revocar(identidad.Resolver(ctx)!, sesion.Value.Origen, sesion.Value.Id), conTexto: false);
        }).RequireAuthorization().RequireRateLimiting("video-consentimiento");

        app.MapGet("/video/emociones/permiso", (string? origen, string? id, HttpContext ctx, IdentidadActualService identidad,
            ConsentimientoVideoService servicio) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var sesion = Sesion(origen, id);
            if (sesion is null) return Invalida();
            var r = servicio.Permiso(identidad.Resolver(ctx)!, sesion.Value.Origen, sesion.Value.Id);
            if (!r.Ok) return Error(r.Tipo, r.Codigo, r.Mensaje);
            var p = r.Permiso!;
            return Results.Ok(new
            {
                permitido = p.Permitido,
                estado = p.Estado,
                motivo = p.Motivo,
                desdeUtc = p.DesdeUtc,
                revocadoUtc = p.RevocadoUtc,
                ahoraUtc = p.AhoraUtc,
                consultarCadaSegundos = p.ConsultarCadaSegundos,
                vigenciaSegundos = p.VigenciaSegundos,
                version = p.Version,
                // Lo que el paciente aceptó de la videollamada de esta sesión (migración 28): el indicador del profesional y, si la revocó, el cierre ordenado del video.
                video = new { exigido = p.VideoExigido, estado = p.VideoEstado, revocadoUtc = p.VideoRevocadoUtc }
            });
        }).RequireAuthorization().RequireRateLimiting("video-consentimiento");

        return app;
    }

    // origen ("turno" | "admision", sin importar mayúsculas ni tildes) e id mayor que cero; null si no.
    private static (string Origen, int Id)? Sesion(string? origen, int? id)
    {
        var o = TextoNormalizado.Normalizar(origen);
        if ((o != OrigenSala.Turno && o != OrigenSala.Admision) || id is not > 0) return null;
        return (o, id.Value);
    }

    // En la URL el id llega como texto: uno que no es un entero (abc, 1e3, -4) es un pedido mal armado y se contesta con
    // el mismo 400 con "codigo" que cualquier otro, no con el 400 sin cuerpo del enlace de parámetros.
    private static (string Origen, int Id)? Sesion(string? origen, string? id) =>
        Sesion(origen, int.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : (int?)null);

    private static IResult Invalida() =>
        Error(TipoResultado.SolicitudInvalida, "solicitud-invalida", "Indicá de qué sesión se trata: origen (turno o admision) e id.");

    private static IResult Responder(ResultadoConsentimiento r, bool conTexto)
    {
        if (!r.Ok) return Error(r.Tipo, r.Codigo, r.Mensaje, r.CerroUtc);
        var e = r.Estado!;
        return Results.Ok(new
        {
            disponible = e.Disponible,
            estado = e.Estado,
            motivo = e.Motivo,
            version = e.Version,
            provisorio = e.Provisorio,
            titulo = conTexto ? e.Titulo : null,
            texto = conTexto ? e.Texto : null,
            decididoUtc = e.DecididoUtc,
            revocadoUtc = e.RevocadoUtc
        });
    }

    private static IResult Error(TipoResultado tipo, string codigo, string mensaje, DateTimeOffset? cerroUtc = null)
    {
        var status = tipo switch
        {
            TipoResultado.NoEncontrada => 404,
            TipoResultado.SolicitudInvalida => 400,
            _ => 409
        };
        return cerroUtc is { } c
            ? Results.Json(new { mensaje, codigo, cerroUtc = c.UtcDateTime }, statusCode: status)
            : Results.Json(new { mensaje, codigo }, statusCode: status);
    }

    // Para quien administra: en el log de arranque queda claro si la lectura de emociones se le puede ofrecer a los
    // pacientes y, si no, por qué. Sin valores secretos: el motivo nunca incluye la clave ni nada derivado de ella.
    private static void AvisarEstadoAlArrancar(IServiceProvider sp, ILogger log)
    {
        var problema = sp.GetRequiredService<IEmocionesCifrado>().ProblemaDeConfiguracion();
        var o = sp.GetRequiredService<IOptionsMonitor<VideoOptions>>().CurrentValue.Emociones;
        if (problema is null) log.LogInformation("Video: lectura de emociones habilitada (cifrado configurado, clave {IdClave}).", o.IdClave);
        else log.LogWarning("Video: lectura de emociones deshabilitada: no se ofrece a los pacientes hasta que se configure. {Motivo}", problema);
    }
}

public record SesionVideoRequest(string? Origen, int? Id);
public record DecisionRequest(string? Origen, int? Id, string? Decision, string? Version);
