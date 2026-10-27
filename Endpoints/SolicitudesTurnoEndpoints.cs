// Solicitudes de turno del paciente que esperan la respuesta del profesional (autoservicio, link público, bot). Va en su propio archivo para que Program.cs cambie lo mínimo.
// El turno nace «Pendiente»; el profesional lo acepta (pasa a Confirmado y se avisa al paciente) o lo rechaza (queda Cancelado, el horario se libera y el paciente recibe el motivo).
// Si no responde a tiempo, vence (SolicitudesTurnoDAO.VencerSolicitudes). Detalle y decisiones: docs/SOLICITUDES_TURNO.md.
public record RechazarSolicitudRequest(string? Motivo);

public static class SolicitudesTurnoEndpoints
{
    public static IEndpointRouteBuilder MapSolicitudesTurno(this IEndpointRouteBuilder app)
    {
        static IResult Falla(string codigo) =>
            Results.Json(new { codigo, mensaje = SolicitudesTurnoReglas.Mensaje(codigo) }, statusCode: SolicitudesTurnoReglas.Status(codigo));

        static AutorDelCierre Autor(Identidad yo, int? idPaciente) => new(yo.IdUsuario, yo.Modulo ?? yo.Rol, AuditoriaIds.Calidad(yo, idPaciente));

        // Las solicitudes que esperan a este profesional (la campanita y el panel las leen de acá).
        app.MapGet("/profesionales/{id:int}/solicitudes-turno", (int id, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propias solicitudes." }, statusCode: 403);
            SolicitudesTurnoDAO.VencerSolicitudes(reglas.Ahora);   // antes de listar: una vencida no se ofrece
            return Results.Ok(new SolicitudesTurnoDAO().Pendientes(id, reglas.Ahora).Select(s => new
            {
                s.IdTurno, s.IdPaciente, nombrePaciente = s.Paciente, fecha = s.Inicio.ToString("yyyy-MM-dd"), hora = s.Inicio.ToString("HH:mm"), s.Motivo,
                vence = s.ExpiraEn?.ToString("yyyy-MM-ddTHH:mm:ss")
            }));
        }).Solo(Quien.Profesional);

        app.MapPost("/turnos/{id:int}/aceptar-solicitud", (int id, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas, INotificadorTurnos notificador) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            SolicitudesTurnoDAO.VencerSolicitudes(reglas.Ahora);   // si ya venció, no se puede aceptar

            var turno = new TurnoDAO().ObtenerPorId(id);
            if (turno is null || turno.IdProfesional != idProfesional) return Falla(SolicitudesTurnoReglas.TurnoInexistente);
            // Las mismas reglas de siempre al confirmar: consentimiento (RN-02) y paciente derivado a psiquiatría. Una solicitud ya confirmada no las vuelve a pedir (idempotencia).
            if (reglas.DescripcionDelEstado(turno.IdEstadoTurno) == SolicitudesTurnoReglas.EstadoPendiente)
            {
                var rn02 = reglas.ValidarPasoAConfirmado(turno);
                if (!rn02.Ok) return rn02.ComoConflicto();
                var derivado = reglas.ValidarPacienteNoDerivado(turno.IdPaciente);
                if (!derivado.Ok) return derivado.ComoConflicto();
            }

            var r = new SolicitudesTurnoDAO().Aceptar(id, idProfesional, Autor(yo, turno.IdPaciente));
            if (r.Rechazo is not null) return Falla(r.Rechazo);
            if (!r.Repetido) notificador.TurnoConfirmado(id);   // campanita y mail del paciente, una vez por turno (el aviso se reserva por turno, tipo y canal)
            return Results.Ok(new
            {
                idTurno = id, repetido = r.Repetido, estado = SolicitudesTurnoReglas.EstadoConfirmado,
                mensaje = r.Repetido ? "Esta solicitud ya estaba aceptada: no se cambió nada." : "Aceptaste la solicitud. Le avisamos al paciente."
            });
        }).Solo(Quien.Profesional);

        app.MapPost("/turnos/{id:int}/rechazar-solicitud", (int id, RechazarSolicitudRequest? req, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            var motivo = req?.Motivo?.Trim();
            if (motivo is { Length: > SolicitudesTurnoReglas.MotivoMaximo }) return Falla(SolicitudesTurnoReglas.MotivoLargo);
            SolicitudesTurnoDAO.VencerSolicitudes(reglas.Ahora);

            var turno = new TurnoDAO().ObtenerPorId(id);
            if (turno is null || turno.IdProfesional != idProfesional) return Falla(SolicitudesTurnoReglas.TurnoInexistente);

            var r = new SolicitudesTurnoDAO().Rechazar(id, idProfesional, motivo, Autor(yo, turno.IdPaciente));
            if (r.Rechazo is not null) return Falla(r.Rechazo);
            return Results.Ok(new
            {
                idTurno = id, repetido = r.Repetido, estado = SolicitudesTurnoReglas.EstadoCancelado,
                mensaje = r.Repetido ? "Esta solicitud ya estaba rechazada: no se cambió nada." : "Rechazaste la solicitud. El horario quedó libre y le avisamos al paciente."
            });
        }).Solo(Quien.Profesional);

        return app;
    }
}
