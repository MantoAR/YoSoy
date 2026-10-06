// Grupos, Casos, CasoDerivacion y ReglaDerivacion (Fase 2). Rutas nuevas, ninguna pisa
// las de Derivacion/"Solicitudes de cambio" que ya existían (ver CAMBIOS_AGUS.md,
// sección Fase 2, sobre el choque de nombres).
public static class CasosEndpoints
{
    public static IEndpointRouteBuilder MapCasos(this IEndpointRouteBuilder app)
    {
        // ── Grupos (solo administrador) ──────────────────────────
        app.MapGet("/grupos", () => new GrupoDAO().ObtenerTodos());

        app.MapGet("/grupos/{id}", (int id) =>
        {
            var g = new GrupoDAO().ObtenerPorId(id);
            return g is null ? Results.NotFound() : Results.Ok(g);
        });

        app.MapPost("/grupos", (CrearGrupoRequest req, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede crear grupos." }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.Nombre)) return Results.BadRequest(new { mensaje = "El nombre es obligatorio." });

            var id = new GrupoDAO().Insertar(new Grupo { Nombre = req.Nombre.Trim(), Descripcion = req.Descripcion, Activo = true });
            return Results.Created($"/grupos/{id}", new { idGrupo = id });
        });

        app.MapPut("/grupos/{id}", (int id, CrearGrupoRequest req, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede editar grupos." }, statusCode: 403);
            var dao = new GrupoDAO();
            var g = dao.ObtenerPorId(id);
            if (g is null) return Results.NotFound();
            g.Nombre = req.Nombre.Trim();
            g.Descripcion = req.Descripcion;
            dao.Modificar(g);
            return Results.Ok(g);
        });

        app.MapPut("/grupos/{id}/desactivar", (int id, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede desactivar grupos." }, statusCode: 403);
            new GrupoDAO().Desactivar(id);
            return Results.NoContent();
        });

        app.MapPut("/grupos/{id}/activar", (int id, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede activar grupos." }, statusCode: 403);
            new GrupoDAO().Activar(id);
            return Results.NoContent();
        });

        app.MapGet("/grupos/{id}/miembros", (int id) => new GrupoDAO().ObtenerMiembros(id));

        app.MapPost("/grupos/{id}/miembros", (int id, MiembroGrupoRequest req, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede gestionar los miembros de un grupo." }, statusCode: 403);
            new GrupoDAO().AgregarMiembro(id, req.IdProfesional);
            return Results.NoContent();
        });

        app.MapDelete("/grupos/{id}/miembros/{idProfesional}", (int id, int idProfesional, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede gestionar los miembros de un grupo." }, statusCode: 403);
            new GrupoDAO().QuitarMiembro(id, idProfesional);
            return Results.NoContent();
        });

        // ── Reglas de derivación (solo administrador) ────────────
        app.MapGet("/reglasderivacion", () => new ReglaDerivacionDAO().ObtenerTodas());

        app.MapPost("/reglasderivacion", (CrearReglaRequest req, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede crear reglas de derivación." }, statusCode: 403);
            var id = new ReglaDerivacionDAO().Insertar(new ReglaDerivacion
            {
                IdEspecialidad = req.IdEspecialidad, IdGrupoDestino = req.IdGrupoDestino,
                Prioridad = req.Prioridad, Activa = true
            });
            return Results.Created($"/reglasderivacion/{id}", new { idRegla = id });
        });

        // Edición completa (incluye Activa): la usa el modal genérico de la tabla de
        // configuración (Pantalla_Configurador/js/dashboard.js), igual que /especialidades/{id}.
        app.MapPut("/reglasderivacion/{id}", (int id, ReglaDerivacion req, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede editar reglas de derivación." }, statusCode: 403);
            req.IdRegla = id;
            new ReglaDerivacionDAO().Modificar(req);
            return Results.Ok(req);
        });

        // Soft-delete (Activa = false): la misma tabla genérica llama a DELETE, igual que
        // /especialidades/{id} (EspecialidadDAO.Baja). No borra la fila.
        app.MapDelete("/reglasderivacion/{id}", (int id, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede desactivar reglas." }, statusCode: 403);
            new ReglaDerivacionDAO().CambiarActiva(id, false);
            return Results.NoContent();
        });

        app.MapPut("/reglasderivacion/{id}/desactivar", (int id, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede desactivar reglas." }, statusCode: 403);
            new ReglaDerivacionDAO().CambiarActiva(id, false);
            return Results.NoContent();
        });

        app.MapPut("/reglasderivacion/{id}/activar", (int id, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede activar reglas." }, statusCode: 403);
            new ReglaDerivacionDAO().CambiarActiva(id, true);
            return Results.NoContent();
        });

        // ── Casos ─────────────────────────────────────────────────
        // Administrador: ve todos, con los filtros que pida. Profesional: solo los
        // suyos, sin importar qué filtro de profesional haya pedido (regla 6).
        app.MapGet("/casos", (string? estado, int? grupo, int? profesional, bool? sinAsignar, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin && idPropio is null)
                return Results.Json(new { mensaje = "No se pudo identificar al usuario." }, statusCode: 403);
            int? idProfesionalEfectivo = esAdmin ? profesional : idPropio;
            return Results.Ok(new CasoDAO().ObtenerConFiltros(estado, grupo, idProfesionalEfectivo, sinAsignar ?? false));
        });

        app.MapGet("/casos/{id}", (int id, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            var c = new CasoDAO().ObtenerDetalle(id);
            if (c is null) return Results.NotFound();
            if (!esAdmin && c.IdProfesionalAsignado != idPropio)
                return Results.Json(new { mensaje = "Ese caso no es tuyo." }, statusCode: 403);
            return Results.Ok(c);
        });

        app.MapGet("/casos/{id}/derivaciones", (int id, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            var caso = new CasoDAO().ObtenerPorId(id);
            if (caso is null) return Results.NotFound();
            if (!esAdmin && caso.IdProfesionalAsignado != idPropio)
                return Results.Json(new { mensaje = "Ese caso no es tuyo." }, statusCode: 403);
            return Results.Ok(new CasoDerivacionDAO().ObtenerPorCaso(id));
        });

        app.MapGet("/casos/{id}/historial", (int id, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            var caso = new CasoDAO().ObtenerPorId(id);
            if (caso is null) return Results.NotFound();
            if (!esAdmin && caso.IdProfesionalAsignado != idPropio)
                return Results.Json(new { mensaje = "Ese caso no es tuyo." }, statusCode: 403);
            return Results.Ok(new HistorialAsignacionDAO().ObtenerPorCaso(id));
        });

        app.MapPut("/casos/{id}/reasignar", (int id, ReasignarCasoRequest req, HttpContext ctx, AsignacionService asignacion) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede reasignar un caso." }, statusCode: 403);
            int? idUsuario = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var x) ? x : null;
            try
            {
                asignacion.ReasignarManual(id, req.IdProfesional, idUsuario, req.MotivoCategoria, req.Nota);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { mensaje = ex.Message }, statusCode: 400);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { mensaje = ex.Message }, statusCode: 409);
            }
        });

        // Las categorías cerradas del motivo de reasignación, para armar el <select> sin
        // repetir la lista en el frontend.
        app.MapGet("/casos/motivos-reasignacion", () => MotivoReasignacion.Categorias);

        // El caso (si existe) de un paciente: lo usa el panel "Información del lead" de
        // CRM > Chats, de solo lectura.
        app.MapGet("/pacientes/{id}/caso", (int id) =>
        {
            var casoDao = new CasoDAO();
            var caso = casoDao.ObtenerAbiertoPorPaciente(id);
            if (caso is null) return Results.NotFound();
            return Results.Ok(casoDao.ObtenerDetalle(caso.IdCaso));
        });

        // ── Mis casos (profesional) ───────────────────────────────
        app.MapGet("/profesionales/{id}/casos", (int id, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin && id != idPropio)
                return Results.Json(new { mensaje = "Solo podés ver tus propios casos." }, statusCode: 403);
            return Results.Ok(new CasoDAO().ObtenerPorProfesional(id));
        });

        // ── Derivaciones de casos (solo administrador) ────────────
        app.MapGet("/casosderivaciones/pendientes", (HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador ve la cola de derivaciones." }, statusCode: 403);
            return Results.Ok(new CasoDerivacionDAO().ObtenerPendientes());
        });

        app.MapPut("/casosderivaciones/{id}/confirmar", (int id, HttpContext ctx, AsignacionService asignacion) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede confirmar una derivación." }, statusCode: 403);
            int? idUsuario = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var x) ? x : null;
            try { asignacion.ConfirmarDerivacion(id, idUsuario); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Json(new { mensaje = ex.Message }, statusCode: 409); }
        });

        app.MapPut("/casosderivaciones/{id}/corregir", (int id, CorregirDerivacionRequest req, HttpContext ctx, AsignacionService asignacion) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede corregir una derivación." }, statusCode: 403);
            int? idUsuario = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var x) ? x : null;
            try { asignacion.CorregirDerivacion(id, idUsuario, req.IdGrupo, req.IdProfesional); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Json(new { mensaje = ex.Message }, statusCode: 409); }
        });

        app.MapPut("/casosderivaciones/{id}/rechazar", (int id, HttpContext ctx, AsignacionService asignacion) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede rechazar una derivación." }, statusCode: 403);
            int? idUsuario = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var x) ? x : null;
            try { asignacion.RechazarDerivacion(id, idUsuario); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Json(new { mensaje = ex.Message }, statusCode: 409); }
        });

        return app;
    }
}

public record CrearGrupoRequest(string Nombre, string? Descripcion);
public record MiembroGrupoRequest(int IdProfesional);
public record CrearReglaRequest(int IdEspecialidad, int IdGrupoDestino, int Prioridad);
public record CorregirDerivacionRequest(int? IdGrupo, int? IdProfesional);
public record ReasignarCasoRequest(int IdProfesional, string MotivoCategoria, string? Nota);
