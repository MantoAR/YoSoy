// Ausencias con umbrales y alta estructurada con plan de control (migración 35). Va en su propio archivo para que Program.cs cambie lo mínimo.
// El sistema detecta, avisa y propone; el profesional confirma. Detalle: docs/AUSENCIAS_ALTA.md.
public record ConfirmaRequest(bool? Confirma);
public record DescartarPropuestaRequest(string? Motivo);
public record ResolverControlRequest(bool? Confirma, string? Nota);

public static class AusenciasAltaEndpoints
{
    public static IEndpointRouteBuilder MapAusenciasAlta(this IEndpointRouteBuilder app)
    {
        static IResult Falla(string codigo, string? dato = null) =>
            Results.Json(new { codigo, mensaje = AusenciasReglas.Mensaje(codigo, dato) }, statusCode: AusenciasReglas.Status(codigo));

        static AutorDelCierre Autor(Identidad yo, int? idPaciente) => new(yo.IdUsuario, yo.Modulo ?? yo.Rol, AuditoriaIds.Calidad(yo, idPaciente));

        // ───────────── Ausencias ─────────────

        // El estado de ausencia de un turno del profesional (la sala lo lee para mostrar el aviso discreto y habilitar «Marcar como ausente»).
        app.MapGet("/turnos/{id:int}/ausencia", (int id, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            var e = new AusenciasDAO().EstadoDeAusencia(id, idProfesional, reglas.Ahora);
            if (e is null) return AusenciasDAO.Disponible() ? Falla(AusenciasReglas.TurnoInexistente) : Results.Ok(new { disponible = false });
            return Results.Ok(new
            {
                disponible = true, idTurno = id, e.Estado, inicio = e.Inicio.ToString("yyyy-MM-ddTHH:mm:ss"), e.PacienteConectado, e.PuedeMarcar, e.AvisoTemprano, e.Silenciado,
                habilitaDesde = e.HabilitaDesde.ToString("yyyy-MM-ddTHH:mm:ss"), toleranciaMinutos = AusenciasReglas.ToleranciaMarcarMinutos
            });
        }).Solo(Quien.Profesional);

        // Marcar como ausente: acción del profesional, con confirmación; recién pasados 15 minutos del inicio y sin que el paciente haya entrado a la sala.
        app.MapPost("/turnos/{id:int}/marcar-ausente", (int id, ConfirmaRequest? req, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            if (req?.Confirma != true) return Falla(AusenciasReglas.FaltaConfirmacion);
            var turno = new TurnoDAO().ObtenerPorId(id);
            var r = new AusenciasDAO().MarcarAusente(id, idProfesional, Autor(yo, turno?.IdPaciente), reglas.Ahora);
            if (r.Rechazo is not null) return Falla(r.Rechazo, r.Dato);
            return Results.Ok(new
            {
                idTurno = id, repetido = r.Repetido, estado = AusenciasReglas.EstadoAusente, ausenciasConsecutivas = r.Ausencias, alertaSegundaAusencia = r.AlertaSegunda,
                propuestaAbandono = r.IdPropuesta is not null, idPropuesta = r.IdPropuesta,
                mensaje = r.Repetido ? "Este turno ya figuraba como ausente: no se cambió nada." : "Marcaste el turno como ausente."
            });
        }).Solo(Quien.Profesional);

        app.MapPost("/propuestas-abandono/{id:int}/confirmar", (int id, ConfirmaRequest? req, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            if (req?.Confirma != true) return Falla(AusenciasReglas.FaltaConfirmacion);
            var r = new AusenciasDAO().ConfirmarAbandono(id, idProfesional, Autor(yo, null), reglas.Ahora);
            if (r.Rechazo is not null) return Falla(r.Rechazo, r.Dato);
            return Results.Ok(new
            {
                idPropuesta = id, repetido = r.Repetido, idCaso = r.IdCaso, estadoCaso = AusenciasReglas.CasoAbandono, turnosFuturos = r.TurnosFuturos,
                mensaje = r.Repetido ? "Esta propuesta ya estaba confirmada: no se cambió nada."
                    : "El paciente quedó en abandono." + (r.TurnosFuturos > 0 ? $" Tiene {r.TurnosFuturos} turno{(r.TurnosFuturos == 1 ? "" : "s")} por delante: no se cancelaron, le avisamos a recepción para que decida." : "")
            });
        }).Solo(Quien.Profesional);

        app.MapPost("/propuestas-abandono/{id:int}/descartar", (int id, DescartarPropuestaRequest? req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            var motivo = req?.Motivo?.Trim();
            if (string.IsNullOrEmpty(motivo) || motivo.Length > AusenciasReglas.MotivoMaximo) return Falla(AusenciasReglas.FaltaMotivo);
            var r = new AusenciasDAO().DescartarAbandono(id, idProfesional, motivo, Autor(yo, null));
            if (r.Rechazo is not null) return Falla(r.Rechazo, r.Dato);
            return Results.Ok(new { idPropuesta = id, repetido = r.Repetido, mensaje = r.Repetido ? "Esta propuesta ya estaba descartada: no se cambió nada." : "Descartaste la propuesta. El paciente sigue como estaba." });
        }).Solo(Quien.Profesional);

        // ───────────── Alta estructurada ─────────────

        // Lo que se precarga en el informe de alta: el nombre, el motivo de consulta y los objetivos del formulario «Sobre mí», y los controles sugeridos (3 y 6 meses).
        app.MapGet("/casos/{id:int}/precarga-alta", (int id, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            var p = AltaEstructuradaDAO.Precarga(id, idProfesional);
            if (p is null) return Results.NotFound();
            AuditoriaIds.Registrar(ctx, "Consulta de precarga de alta", "Caso", id, new CasoDAO().ObtenerPorId(id)?.IdPaciente);
            return Results.Ok(new
            {
                nombrePaciente = p.Value.Paciente, motivoConsulta = p.Value.Motivo, objetivosPaciente = p.Value.Objetivos,
                fechaAlta = reglas.Ahora.ToString("yyyy-MM-dd"),
                controlesSugeridos = AltaEstructuradaReglas.ControlesSugeridos(reglas.Ahora).Select(f => f.ToString("yyyy-MM-dd")),
                mesesSugeridos = AltaEstructuradaReglas.ControlesPorDefectoMeses
            });
        }).Solo(Quien.Profesional);

        app.MapPost("/controles/{id:int}/resolver", (int id, ResolverControlRequest? req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            if (req?.Confirma != true) return Falla(AusenciasReglas.FaltaConfirmacion);
            var r = new AltaEstructuradaDAO().Resolver(id, idProfesional, req.Nota, Autor(yo, null));
            if (r.Rechazo is not null)
                return Results.Json(new { codigo = r.Rechazo, mensaje = r.Rechazo == AltaEstructuradaReglas.ControlInexistente ? "No encontramos ese control entre los tuyos." : AusenciasReglas.Mensaje(r.Rechazo) },
                    statusCode: r.Rechazo == AltaEstructuradaReglas.ControlInexistente ? 404 : 409);
            return Results.Ok(new { idControl = id, repetido = r.Repetido, mensaje = r.Repetido ? "Este control ya estaba resuelto: no se cambió nada." : "Listo, el control quedó resuelto." });
        }).Solo(Quien.Profesional);

        // ───────────── Lo que alimenta la campanita del profesional ─────────────

        // Evalúa de forma perezosa (idempotente) lo que vence o se acerca y devuelve lo que el profesional tiene para resolver: solicitudes de turno, ausentes por marcar, propuestas de abandono y controles.
        app.MapGet("/profesionales/{id:int}/seguimiento", (int id, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tu propio seguimiento." }, statusCode: 403);
            var ahora = reglas.Ahora;
            SolicitudesTurnoDAO.VencerSolicitudes(ahora);
            var ausencias = new AusenciasDAO();
            ausencias.EvaluarAvisosTempranos(ahora, id);
            AltaEstructuradaDAO.EvaluarControlesCercanos(ahora, id);
            return Results.Ok(new
            {
                solicitudes = new SolicitudesTurnoDAO().Pendientes(id, ahora).Select(s => new
                {
                    s.IdTurno, s.IdPaciente, nombrePaciente = s.Paciente, fecha = s.Inicio.ToString("yyyy-MM-dd"), hora = s.Inicio.ToString("HH:mm"), s.Motivo,
                    vence = s.ExpiraEn?.ToString("yyyy-MM-ddTHH:mm:ss")
                }),
                ausentesPorMarcar = ausencias.PorMarcar(id, ahora).Select(t => new { t.IdTurno, t.IdPaciente, nombrePaciente = t.Paciente, fecha = t.Inicio.ToString("yyyy-MM-dd"), hora = t.Inicio.ToString("HH:mm") }),
                propuestasAbandono = ausencias.PropuestasPendientes(id, ahora).Select(p => new
                {
                    p.IdPropuesta, p.IdCaso, p.IdPaciente, nombrePaciente = p.Paciente, p.Ausencias,
                    turnosFuturos = p.TurnosFuturos.Select(t => new { t.IdTurno, fecha = t.Inicio.ToString("yyyy-MM-dd"), hora = t.Inicio.ToString("HH:mm"), profesional = t.Profesional })
                }),
                controles = new AltaEstructuradaDAO().Abiertos(id).Select(c => new { c.IdControl, c.IdCaso, c.IdPaciente, nombrePaciente = c.Paciente, c.Orden, fecha = c.Fecha.ToString("yyyy-MM-dd") })
            });
        }).Solo(Quien.Profesional);

        return app;
    }
}
