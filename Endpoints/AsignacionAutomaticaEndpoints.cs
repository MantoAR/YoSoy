using System.Globalization;

// La propuesta automática de profesional y horario (migración 32). Se registra con app.MapAsignacionAutomatica(). La lógica está en Services/AsignacionAutomatica.cs.
//
//   GET  /propuestas-asignacion/{id}/horarios         El paciente: los próximos horarios libres del MISMO profesional de su propuesta (hasta 10).
//   POST /propuestas-asignacion/{id}/aceptar          El paciente: «Me sirve, confirmar». Crea el vínculo y el turno confirmado, una sola vez.
//   POST /propuestas-asignacion/{id}/rechazar         El paciente: { tipo: "otro-horario" (con fechaHora) | "otro-profesional" | "ninguno" }.
//   POST /informes-admision/{id}/proponer-asignacion  La recepción: vuelve a calcular la propuesta de un formulario que quedó en «Asignaciones para resolver». Pide { confirmo: true }.
//
// La propuesta que espera al paciente viaja en GET /pacientes/{id}/primeros-pasos (propuesta) y la lista de la recepción, en GET /informes-admision-detalle (asignacionEstado,
// asignacionMotivo y propuestaPendiente): no hay endpoints aparte para eso. El paciente solo ve y responde las SUYAS (el servicio compara el paciente de la propuesta con el de la sesión);
// la única forma de resolver a mano es la asignación de la recepción (PUT /informes-admision/{id}/derivar).
public static class AsignacionAutomaticaEndpoints
{
    public static IEndpointRouteBuilder MapAsignacionAutomatica(this IEndpointRouteBuilder app)
    {
        app.MapGet("/propuestas-asignacion/{id:int}/horarios", (int id, AsignacionAutomaticaService servicio, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var (error, horarios) = servicio.HorariosAlternativos(id, yo.IdPaciente!.Value, reloj.GetLocalNow().DateTime);
            if (error is not null) return Responder(error);
            return Results.Ok(new { horarios = horarios.Select(h => new { fechaHora = h.ToString("yyyy-MM-ddTHH:mm:ss"), cuando = AsignacionAutomaticaReglas.Cuando(h) }) });
        }).Solo(Quien.Paciente);

        app.MapPost("/propuestas-asignacion/{id:int}/aceptar", (int id, AsignacionAutomaticaService servicio, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            return Responder(servicio.Aceptar(id, yo.IdPaciente!.Value, yo.IdUsuario, reloj.GetLocalNow().DateTime));
        }).Solo(Quien.Paciente);

        app.MapPost("/propuestas-asignacion/{id:int}/rechazar", (int id, RechazoPropuestaRequest req, AsignacionAutomaticaService servicio, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            DateTime? fecha = null;
            if (!string.IsNullOrWhiteSpace(req.FechaHora))
            {
                if (!DateTime.TryParseExact(req.FechaHora, new[] { "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
                    return Results.BadRequest(new { codigo = "horario", mensaje = "El horario elegido no tiene un formato válido." });
                fecha = f;
            }
            return Responder(servicio.Rechazar(id, yo.IdPaciente!.Value, yo.IdUsuario, req.Tipo, fecha, reloj.GetLocalNow().DateTime));
        }).Solo(Quien.Paciente);

        app.MapPost("/informes-admision/{id:int}/proponer-asignacion", (int id, RevisionRequest req, AsignacionAutomaticaService servicio, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad) =>
        {
            if (req.Confirmo != true) return Results.BadRequest(new { codigo = "sin-confirmacion", mensaje = "Confirmá la acción para continuar." });
            var yo = identidad.Resolver(ctx)!;
            return Responder(servicio.Reproponer(id, yo.IdUsuario, reloj.GetLocalNow().DateTime));
        }).Solo(Quien.Admision | Quien.RecepcionAdmision);

        return app;
    }

    private static IResult Responder(ResultadoAsignacionAuto r) =>
        Results.Json(new
        {
            r.Codigo, r.Mensaje, r.IdTurno,
            propuesta = r.Propuesta is null ? null : new
            {
                r.Propuesta.IdPropuesta, r.Propuesta.IdProfesional, r.Propuesta.Profesional, r.Propuesta.Especialidad,
                fechaHora = r.Propuesta.FechaHora.ToString("yyyy-MM-ddTHH:mm:ss"), expiraEn = r.Propuesta.ExpiraEn.ToString("yyyy-MM-ddTHH:mm:ss"),
                r.Propuesta.NroPropuesta, r.Propuesta.PuedeCambiar, cuando = AsignacionAutomaticaReglas.Cuando(r.Propuesta.FechaHora)
            }
        }, statusCode: r.Status);
}

public record RechazoPropuestaRequest(string? Tipo, string? FechaHora);
