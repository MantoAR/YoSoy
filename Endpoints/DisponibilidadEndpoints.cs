// Los horarios libres que ve el paciente al pedir un turno o reprogramar (docs/DISPONIBILIDAD.md). Salen de la misma función que usa la propuesta automática de asignación.
public static class DisponibilidadEndpoints
{
    public static IEndpointRouteBuilder MapDisponibilidad(this IEndpointRouteBuilder app)
    {
        // GET /pacientes/{id}/disponibilidad?idProfesional=&dias=&ignorarTurno=   (solo el propio paciente)
        app.MapGet("/pacientes/{id:int}/disponibilidad", (int id, int idProfesional, int? dias, int? ignorarTurno, HttpContext ctx, IdentidadActualService identidad, AsignacionAutomaticaService asignacion) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdPaciente != id) return Results.Json(new { mensaje = "Solo podés ver los horarios para tus propios turnos." }, statusCode: 403);
            if (idProfesional <= 0) return Results.Json(new { mensaje = "Elegí un profesional." }, statusCode: 400);
            var r = asignacion.HorariosParaPaciente(id, idProfesional, dias ?? DisponibilidadReglas.DiasPorDefecto, ignorarTurno);
            return Results.Ok(new
            {
                idProfesional, r.Dias, r.Total, r.Codigo, r.Mensaje, r.MensajeHoy, anticipacionHoras = r.AnticipacionHoras
            });
        }).Solo(Quien.Paciente);

        return app;
    }
}
