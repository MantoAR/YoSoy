// Alta clínica y derivación interna ASISTIDAS desde el cierre de sesión. El sistema precarga, guía, avisa y deja constancia; una persona confirma. Va en su propio archivo para que
// Program.cs cambie lo mínimo.
//
// ATENCIÓN, para no confundir dos cosas distintas:
//   · PUT /pacientes/{id}/alta (Program.cs) es el ALTA ADMINISTRATIVA: solo reactiva la ficha de un paciente dado de baja. NO tiene nada que ver con esto y no cambia.
//   · POST /casos/{id}/alta-clinica (acá) es el ALTA CLÍNICA: informe de alta + plan de control, y el caso pasa a «En alta». No inactiva al paciente ni a su usuario.
public static class AltaDerivacionEndpoints
{
    public static IEndpointRouteBuilder MapAltaDerivacion(this IEndpointRouteBuilder app)
    {
        static IResult Falla(string codigo, string? estado = null) =>
            Results.Json(new { codigo, mensaje = AltaDerivacionReglas.Mensaje(codigo, estado) }, statusCode: AltaDerivacionReglas.Status(codigo));

        static IResult Invalido(string mensaje) => Results.Json(new { codigo = AltaDerivacionReglas.DatosInvalidos, mensaje }, statusCode: 400);

        static AutorDelCierre Autor(Identidad yo, int? idPaciente) => new(yo.IdUsuario, yo.Modulo ?? yo.Rol, AuditoriaIds.Calidad(yo, idPaciente));

        static void Mandar(EmailService email, ILogger log, List<MailNuevo> mails)
        {
            // El mail es un aviso aparte: si falla (o el correo no está configurado) la campanita ya quedó guardada y no se deshace nada.
            foreach (var m in mails)
            {
                try { email.EnviarAviso(m.Destinatario, m.Asunto, m.Texto); }
                catch (Exception) { log.LogWarning("No se pudo enviar el mail de aviso de una derivación."); }
            }
        }

        // ───────────── ALTA CLÍNICA ─────────────

        app.MapPost("/casos/{id:int}/alta-clinica", (int id, AltaClinicaRequest req, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            var dao = new AltaClinicaDAO();
            if (!dao.Migracion27Disponible()) return Falla(AltaDerivacionReglas.FaltaMigracion27);
            if (req.Confirma != true) return Falla(AltaDerivacionReglas.FaltaConfirmacion);
            var error = AltaDerivacionReglas.ValidarAlta(req, reglas.Ahora, out var fechaAlta, out var plan);
            if (error is not null) return Invalido(error);

            var caso = new CasoDAO().ObtenerPorId(id);
            var r = dao.Confirmar(id, idProfesional, req, fechaAlta, plan, Autor(yo, caso?.IdPaciente));
            if (r.Rechazo is not null) return Falla(r.Rechazo, r.Estado);

            var cuerpo = new
            {
                idAltaClinica = r.Id, idCaso = r.IdCaso, idPaciente = r.IdPaciente, idSesion = r.IdSesion, repetido = r.Repetido,
                fechaAlta = r.Fecha?.ToString("yyyy-MM-dd"),
                caso = new { cambio = !r.Repetido, estadoAnterior = r.EstadoAnterior, estado = r.EstadoActual },
                plan = new { controles = r.Plan!.Controles, cadaDias = r.Plan.CadaDias, primerControl = r.Plan.Primero?.ToString("yyyy-MM-dd") }
            };
            return r.Repetido ? Results.Ok(cuerpo) : Results.Created($"/casos/{id}/alta-clinica", cuerpo);
        }).Solo(Quien.Profesional);

        // El alta guardada (con el informe): el profesional a cargo y los auditores. Lectura auditada.
        app.MapGet("/casos/{id:int}/alta-clinica", (int id, HttpContext ctx) =>
        {
            if (!new AltaClinicaDAO().Migracion27Disponible()) return Falla(AltaDerivacionReglas.FaltaMigracion27);
            var alta = new AltaClinicaDAO().Obtener(id);
            if (alta is null || !AutorizacionClinica.PuedeVerPaciente(ctx, alta.Value.IdPaciente)) return Results.NotFound();
            AuditoriaIds.Registrar(ctx, "Consulta de alta clínica", "AltaClinica", alta.Value.IdAltaClinica, alta.Value.IdPaciente);
            return Results.Ok(new
            {
                alta.Value.IdAltaClinica, idCaso = id, alta.Value.IdPaciente, alta.Value.IdSesion, fechaAlta = alta.Value.FechaAlta.ToString("yyyy-MM-dd"), alta.Value.Informe,
                plan = new { controles = alta.Value.Controles, cadaDias = alta.Value.CadaDias, primerControl = alta.Value.Primero?.ToString("yyyy-MM-dd") }
            });
        }).Solo(Quien.Profesional);

        // ───────────── DERIVACIÓN INTERNA ─────────────

        // La lista de destinos del formulario (profesionales activos, con especialidad y carga). Solo para quien tiene el caso a su cargo.
        app.MapGet("/casos/{id:int}/destinos-derivacion", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            var caso = new CasoDAO().ObtenerPorId(id);
            if (caso is null) return Falla(AltaDerivacionReglas.CasoInexistente);
            if (caso.IdProfesionalAsignado != idProfesional) return Falla(AltaDerivacionReglas.CasoAjeno);
            return Results.Ok(new DerivacionCierreDAO().Destinos(idProfesional));
        }).Solo(Quien.Profesional);

        app.MapPost("/casos/{id:int}/derivar", (int id, DerivarCasoRequest req, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas, EmailService email, ILogger<DerivacionCierreDAO> log) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            if (!ADMigracion.Disponible()) return Falla(AltaDerivacionReglas.FaltaMigracion27);
            if (req.Confirma != true) return Falla(AltaDerivacionReglas.FaltaConfirmacion);
            var error = AltaDerivacionReglas.ValidarDerivacion(req);
            if (error is not null) return Invalido(error);
            if (req.IdProfesionalDestino == idProfesional) return Falla(AltaDerivacionReglas.MismoProfesional);

            var caso = new CasoDAO().ObtenerPorId(id);
            var (r, mails) = new DerivacionCierreDAO().Crear(id, idProfesional, req, reglas.Ahora, Autor(yo, caso?.IdPaciente));
            if (r.Rechazo is not null) return Falla(r.Rechazo, r.Estado);
            if (!r.Repetido) Mandar(email, log, mails);
            var cuerpo = new { idDerivacion = r.Id, idCaso = r.IdCaso, idPaciente = r.IdPaciente, idSesion = r.IdSesion, estado = r.EstadoActual, idProfesionalDestino = r.IdProfesionalDestino, repetido = r.Repetido };
            return r.Repetido ? Results.Ok(cuerpo) : Results.Created($"/derivaciones/{r.Id}/informe", cuerpo);
        }).Solo(Quien.Profesional);

        // El profesional de destino acepta o rechaza. Aceptar NO cambia ningún vínculo: avisa a Admisión, que confirma el traspaso.
        app.MapPut("/derivaciones/{id:int}/responder", (int id, ResponderDerivacionRequest req, HttpContext ctx, IdentidadActualService identidad, EmailService email, ILogger<DerivacionCierreDAO> log) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            if (!ADMigracion.Disponible()) return Falla(AltaDerivacionReglas.FaltaMigracion27);
            if (req.Acepta is null) return Invalido("Indicá si aceptás o rechazás la derivación.");
            var (r, mails) = new DerivacionCierreDAO().Responder(id, idProfesional, req.Acepta.Value, req.Motivo, Autor(yo, null));
            if (r.Rechazo is not null) return Falla(r.Rechazo, r.Estado);
            if (!r.Repetido) Mandar(email, log, mails);
            return Results.Ok(new { idDerivacion = r.Id, estado = r.EstadoActual, repetido = r.Repetido });
        }).Solo(Quien.Profesional);

        // El informe de derivación: lo único que el destino ve del paciente (y el origen). Nada de la historia clínica.
        app.MapGet("/derivaciones/{id:int}/informe", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var d = ADMigracion.Disponible() ? new DerivacionCierreDAO().Obtener(id) : null;
            if (d is null || (yo.IdProfesional != d.IdProfesionalOrigen && yo.IdProfesional != d.IdProfesionalDestino)) return Results.NotFound();
            AuditoriaIds.Registrar(ctx, "Consulta de informe de derivación", "Derivacion", id, d.IdPaciente);
            return Results.Ok(new
            {
                d.IdDerivacion, d.Estado, fecha = d.Fecha.ToString("yyyy-MM-dd"), d.Motivo, d.Observaciones,
                paciente = new { id = d.IdPaciente, nombre = d.NombrePaciente },
                origen = new { id = d.IdProfesionalOrigen, nombre = d.NombreOrigen },
                destino = new { id = d.IdProfesionalDestino, nombre = d.NombreDestino }
            });
        }).Solo(Quien.Profesional);

        // La campanita del profesional: sus avisos de derivación y traspaso, y las derivaciones que le toca responder.
        app.MapGet("/profesionales/{id:int}/avisos-clinicos", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propios avisos." }, statusCode: 403);
            if (!ADMigracion.Disponible()) return Results.Ok(new { avisos = Array.Empty<object>(), porResponder = Array.Empty<object>() });
            var (avisos, porResponder) = new DerivacionCierreDAO().AvisosDe(id);
            return Results.Ok(new
            {
                avisos = avisos.Select(a => new { a.IdAviso, a.Tipo, a.Titulo, a.Mensaje, a.Leida, fecha = a.Fecha }),
                porResponder = porResponder.Select(d => new
                {
                    d.IdDerivacion, d.IdPaciente, d.NombrePaciente, d.NombreOrigen, d.Motivo, d.Observaciones, fecha = d.Fecha.ToString("yyyy-MM-dd")
                })
            });
        }).Solo(Quien.Profesional);

        app.MapPut("/avisos-clinicos/{id:int}/leida", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            return ADMigracion.Disponible() && new DerivacionCierreDAO().MarcarAvisoLeido(id, idProfesional) ? Results.NoContent() : Results.NotFound();
        }).Solo(Quien.Profesional);

        // ───────────── ADMISIÓN (CRM): la tarea de traspaso ─────────────
        // El traspaso lo confirma una persona con el rol que ya puede reasignar casos (CRM / Administración): usa la reasignación de casos de siempre, con motivo e historial.

        app.MapGet("/derivaciones/traspasos-pendientes", (HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo CRM y atención al cliente ve los traspasos pendientes." }, statusCode: 403);
            if (!ADMigracion.Disponible()) return Results.Ok(Array.Empty<object>());
            return Results.Ok(new DerivacionCierreDAO().TraspasosPendientes().Select(d => new
            {
                d.IdDerivacion, d.IdCaso, d.IdPaciente, d.NombrePaciente, d.IdProfesionalOrigen, d.NombreOrigen, d.IdProfesionalDestino, d.NombreDestino, d.Estado, fecha = d.Fecha.ToString("yyyy-MM-dd")
            }));
        }).Solo(Quien.Crm);

        app.MapPost("/derivaciones/{id:int}/traspasar", (int id, TraspasarRequest? req, HttpContext ctx, IdentidadActualService identidad, AsignacionService asignacion, EmailService email, ILogger<DerivacionCierreDAO> log) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo CRM y atención al cliente puede confirmar un traspaso." }, statusCode: 403);
            var yo = identidad.Resolver(ctx)!;
            if (!ADMigracion.Disponible()) return Falla(AltaDerivacionReglas.FaltaMigracion27);
            var dao = new DerivacionCierreDAO();
            var d = dao.Para(id);
            if (d is null) return Falla(AltaDerivacionReglas.DerivacionInexistente);
            var autor = Autor(yo, d.IdPaciente);

            if (d.Estado != AltaDerivacionReglas.DerivacionTraspasada)
            {
                if (d.Estado != AltaDerivacionReglas.DerivacionAceptadaPorElDestino) return Falla(AltaDerivacionReglas.EstadoDeDerivacion, d.Estado);
                var caso = d.IdCaso is int idCaso ? new CasoDAO().ObtenerPorId(idCaso) : null;
                if (caso is null) return Falla(AltaDerivacionReglas.CasoInexistente);
                if (caso.Estado == AltaDerivacionReglas.CasoCerrado) return Falla(AltaDerivacionReglas.EstadoNoPermitido, caso.Estado);
                if (new ProfesionalDAO().ObtenerPorId(d.IdProfesionalDestino)?.Estado != "Activo") return Falla(AltaDerivacionReglas.DestinoNoDisponible);

                if (caso.IdProfesionalAsignado == d.IdProfesionalOrigen)
                {
                    try
                    {
                        asignacion.ReasignarManual(caso.IdCaso, d.IdProfesionalDestino, yo.IdUsuario, req?.MotivoCategoria ?? "Otro", req?.Nota ?? $"Derivación interna #{id}");
                    }
                    catch (ArgumentException ex) { return Results.Json(new { codigo = AltaDerivacionReglas.DatosInvalidos, mensaje = ex.Message }, statusCode: 400); }
                    catch (InvalidOperationException ex) { return Results.Json(new { codigo = AltaDerivacionReglas.CasoCambio, mensaje = ex.Message }, statusCode: 409); }
                }
                else if (caso.IdProfesionalAsignado != d.IdProfesionalDestino)
                    return Falla(AltaDerivacionReglas.CasoCambio);
                // (si el caso ya está a cargo del destino, un intento anterior movió el caso y solo falta cerrar la derivación)
            }

            var (r, mails) = dao.CerrarTraspaso(id, autor);
            if (r.Rechazo is not null) return Falla(r.Rechazo, r.Estado);
            if (!r.Repetido) Mandar(email, log, mails);
            return Results.Ok(new { idDerivacion = id, estado = r.EstadoActual, repetido = r.Repetido });
        }).Solo(Quien.Crm);

        app.MapPost("/derivaciones/{id:int}/resolver", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo CRM y atención al cliente puede resolver una derivación rechazada." }, statusCode: 403);
            if (!ADMigracion.Disponible()) return Falla(AltaDerivacionReglas.FaltaMigracion27);
            var yo = identidad.Resolver(ctx)!;
            var r = new DerivacionCierreDAO().Resolver(id, Autor(yo, null));
            if (r.Rechazo is not null) return Falla(r.Rechazo, r.Estado);
            return Results.Ok(new { idDerivacion = id, estado = r.EstadoActual, repetido = r.Repetido });
        }).Solo(Quien.Crm);

        return app;
    }
}
