using Microsoft.Extensions.Options;

public static class TranscripcionReintentarEndpoints
{
    public static IEndpointRouteBuilder MapTranscripcionReintentar(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sesiones-clinicas/{id:int}/transcripcion/reintentar", (int id, HttpContext ctx, IdentidadActualService identidad,
            ITranscripcionSesionRepositorio transcripciones, IOptionsMonitor<VideoOptions> opciones) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var s = new VinculoSesionEvolucionDAO().ObtenerSesion(id);
            if (s is null || yo.IdProfesional != s.IdProfesional)
                return Results.Json(new { codigo = "sesion-inexistente", mensaje = "No encontramos esa sesión entre las tuyas." }, statusCode: 404);

            var t = transcripciones.ObtenerPorSesion(id);
            if (t is null || t.Estado != TranscripcionEstado.Error)
                return Results.Json(new { codigo = "transcripcion-no-en-error", mensaje = "Esta sesión no tiene una transcripción en error para reintentar." }, statusCode: 409);

            var carpeta = Path.Combine(opciones.CurrentValue.Transcripcion.CarpetaAudio, t.IdTranscripcion.ToString());
            if (!Directory.Exists(carpeta))
                return Results.Json(new { codigo = "audio-no-disponible", mensaje = "Ya no queda el audio de esta sesión para reintentar la transcripción." }, statusCode: 409);

            transcripciones.VolverAPendienteDesdeError(t.IdTranscripcion);
            return Results.Ok(new { idSesion = id, estadoTranscripcion = TranscripcionEstado.Pendiente });
        }).Solo(Quien.Profesional);

        return app;
    }
}
