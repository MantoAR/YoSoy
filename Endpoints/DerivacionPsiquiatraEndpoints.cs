// Derivar a un paciente a psiquiatría desde el cierre de sesión, y reabrirlo. Va en su propio archivo para que Program.cs cambie lo mínimo.
// Es SOLO un cambio de estado que confirma una persona (el pedido lleva «confirma: true» que sale de la ventana de confirmación): no crea tareas ni manda mails.
// Detalle y decisiones: docs/DERIVADO_PSIQUIATRA.md.
public record DerivarPsiquiatraRequest(bool? Confirma, int? IdSesion, int? IdTurno);
public record ReabrirDerivadoRequest(bool? Confirma);

public static class DerivacionPsiquiatraEndpoints
{
    public static IEndpointRouteBuilder MapDerivacionPsiquiatra(this IEndpointRouteBuilder app)
    {
        static IResult Falla(string codigo) =>
            Results.Json(new { codigo, mensaje = DerivacionPsiquiatraReglas.Mensaje(codigo) }, statusCode: DerivacionPsiquiatraReglas.Status(codigo));

        static AutorDelCierre Autor(Identidad yo, int idPaciente) => new(yo.IdUsuario, yo.Modulo ?? yo.Rol, AuditoriaIds.Calidad(yo, idPaciente));

        // El profesional a cargo confirma la derivación. Repetirla (doble clic, dos pestañas) devuelve la que ya está.
        app.MapPost("/pacientes/{id:int}/derivar-psiquiatra", (int id, DerivarPsiquiatraRequest req, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            if (req.Confirma != true) return Falla(DerivacionPsiquiatraReglas.FaltaConfirmacion);

            var r = new DerivacionPsiquiatraDAO().Derivar(id, idProfesional, req.IdSesion, req.IdTurno, Autor(yo, id), reloj.GetLocalNow().DateTime);
            if (r.Rechazo is not null) return Falla(r.Rechazo);

            var cuerpo = new
            {
                idDerivacion = r.IdDerivacion, idPaciente = r.IdPaciente, repetido = r.Repetido, estado = DerivacionPsiquiatraReglas.Estado,
                turnosFuturos = r.TurnosFuturos,
                mensaje = r.Repetido ? "Este paciente ya figuraba como derivado: no se cambió nada."
                    : "El paciente quedó como derivado." + (r.TurnosFuturos > 0 ? $" Tiene {r.TurnosFuturos} turno{(r.TurnosFuturos == 1 ? "" : "s")} por delante: no se cancelaron, te avisamos a vos y a recepción para que decidan." : "")
            };
            return r.Repetido ? Results.Ok(cuerpo) : Results.Created($"/pacientes/{id}/derivacion-psiquiatra", cuerpo);
        }).Solo(Quien.Profesional);

        // Si figura derivado (el profesional a cargo, la recepción, CRM y los auditores).
        app.MapGet("/pacientes/{id:int}/derivacion-psiquiatra", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var puede = yo.HaceAdmision || yo.EsCrm || AutorizacionClinica.PuedeVerPaciente(ctx, id);
            if (!puede) return Results.NotFound();
            var v = new DerivacionPsiquiatraDAO().Vigente(id);
            return Results.Ok(new { derivado = v is not null, desde = v?.FechaUtc, idDerivacion = v?.IdDerivacion });
        }).Solo(Quien.Profesional | Quien.Admision | Quien.Crm);

        // Reabrir: recepción / CRM, con confirmación. Vuelve a poder reservar turnos.
        app.MapPost("/pacientes/{id:int}/reabrir-derivacion", (int id, ReabrirDerivadoRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (req.Confirma != true) return Falla(DerivacionPsiquiatraReglas.FaltaConfirmacion);
            var r = new DerivacionPsiquiatraDAO().Reabrir(id, Autor(yo, id));
            if (r.Rechazo is not null) return Falla(r.Rechazo);
            return Results.Ok(new
            {
                idPaciente = id, repetido = r.Repetido, estado = "Reabierto",
                mensaje = r.Repetido ? "Este paciente no figuraba como derivado: no se cambió nada." : "El paciente volvió a su estado anterior y ya puede reservar turnos."
            });
        }).Solo(Quien.Admision | Quien.Crm);

        return app;
    }
}
