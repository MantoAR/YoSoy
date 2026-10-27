// Grupos, Casos, CasoDerivacion y ReglaDerivacion (Fase 2). Rutas nuevas, ninguna pisa
// las de Derivacion/"Solicitudes de cambio" que ya existían (ver CAMBIOS_AGUS.md,
// sección Fase 2, sobre el choque de nombres).
public static class CasosEndpoints
{
    // Marca a los pacientes que figuran derivados a psiquiatría (migración 34) para que las listas lo muestren. Una sola consulta por pedido.
    private static List<CasoDetalle> ConDerivado(List<CasoDetalle> casos)
    {
        var derivados = DerivacionPsiquiatraDAO.Derivados(casos.Select(c => c.IdPaciente));
        foreach (var c in casos) c.Derivado = derivados.Contains(c.IdPaciente);
        return casos;
    }
    private static CasoDetalle ConDerivado(CasoDetalle caso) { ConDerivado(new List<CasoDetalle> { caso }); return caso; }

    public static IEndpointRouteBuilder MapCasos(this IEndpointRouteBuilder app)
    {
        // Cierre de seguridad, Paso 4: ninguno de estos endpoints exigía sesión a nivel de
        // ruta — varios ya devolvían 403 igual gracias al "if (!esAdmin)" interno (porque
        // AutorizacionCasos.Resolver devuelve (false, null) para un pedido anónimo), pero
        // varios otros (/grupos, /reglasderivacion, /grupos/{id}/miembros,
        // /casos/motivos-reasignacion, /pacientes/{id}/caso) no tenían ningún chequeo y
        // quedaban abiertos a cualquiera. MapGroup("") agrupa sin tocar las rutas (ningún
        // prefijo nuevo) y les exige sesión a todos por igual; la pertenencia específica de
        // cada uno (solo administrador, solo el propio profesional) sigue decidida adentro,
        // sin cambios.
        var g = app.MapGroup("").RequireAuthorization();

        // ── Grupos (solo administrador) ──────────────────────────
        g.MapGet("/grupos", () => new GrupoDAO().ObtenerTodos());

        g.MapGet("/grupos/{id}", (int id) =>
        {
            var gr = new GrupoDAO().ObtenerPorId(id);
            return gr is null ? Results.NotFound() : Results.Ok(gr);
        });

        g.MapPost("/grupos", (CrearGrupoRequest req, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede crear grupos." }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.Nombre)) return Results.BadRequest(new { mensaje = "El nombre es obligatorio." });

            var id = new GrupoDAO().Insertar(new Grupo { Nombre = req.Nombre.Trim(), Descripcion = req.Descripcion, Activo = true });
            return Results.Created($"/grupos/{id}", new { idGrupo = id });
        });

        g.MapPut("/grupos/{id}", (int id, CrearGrupoRequest req, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede editar grupos." }, statusCode: 403);
            var dao = new GrupoDAO();
            var gr = dao.ObtenerPorId(id);
            if (gr is null) return Results.NotFound();
            gr.Nombre = req.Nombre.Trim();
            gr.Descripcion = req.Descripcion;
            dao.Modificar(gr);
            return Results.Ok(gr);
        });

        g.MapPut("/grupos/{id}/desactivar", (int id, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede desactivar grupos." }, statusCode: 403);
            new GrupoDAO().Desactivar(id);
            return Results.NoContent();
        });

        g.MapPut("/grupos/{id}/activar", (int id, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede activar grupos." }, statusCode: 403);
            new GrupoDAO().Activar(id);
            return Results.NoContent();
        });

        g.MapGet("/grupos/{id}/miembros", (int id) => new GrupoDAO().ObtenerMiembros(id));

        g.MapPost("/grupos/{id}/miembros", (int id, MiembroGrupoRequest req, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede gestionar los miembros de un grupo." }, statusCode: 403);
            new GrupoDAO().AgregarMiembro(id, req.IdProfesional);
            return Results.NoContent();
        });

        g.MapDelete("/grupos/{id}/miembros/{idProfesional}", (int id, int idProfesional, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede gestionar los miembros de un grupo." }, statusCode: 403);
            new GrupoDAO().QuitarMiembro(id, idProfesional);
            return Results.NoContent();
        });

        // ── Reglas de derivación (solo administrador) ────────────
        g.MapGet("/reglasderivacion", () => new ReglaDerivacionDAO().ObtenerTodas());

        g.MapPost("/reglasderivacion", (CrearReglaRequest req, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede crear reglas de derivación." }, statusCode: 403);
            var id = new ReglaDerivacionDAO().Insertar(new ReglaDerivacion
            {
                IdEspecialidad = req.IdEspecialidad, IdGrupoDestino = req.IdGrupoDestino,
                Prioridad = req.Prioridad, Activa = true
            });
            return Results.Created($"/reglasderivacion/{id}", new { idRegla = id });
        });

        // Edición completa (incluye Activa): la usa el modal genérico de la tabla de
        // configuración (Pantalla_Configurador/js/dashboard.js), igual que /especialidades/{id}.
        g.MapPut("/reglasderivacion/{id}", (int id, ReglaDerivacion req, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede editar reglas de derivación." }, statusCode: 403);
            req.IdRegla = id;
            new ReglaDerivacionDAO().Modificar(req);
            return Results.Ok(req);
        });

        // Soft-delete (Activa = false): la misma tabla genérica llama a DELETE, igual que
        // /especialidades/{id} (EspecialidadDAO.Baja). No borra la fila.
        g.MapDelete("/reglasderivacion/{id}", (int id, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede desactivar reglas." }, statusCode: 403);
            new ReglaDerivacionDAO().CambiarActiva(id, false);
            return Results.NoContent();
        });

        g.MapPut("/reglasderivacion/{id}/desactivar", (int id, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede desactivar reglas." }, statusCode: 403);
            new ReglaDerivacionDAO().CambiarActiva(id, false);
            return Results.NoContent();
        });

        g.MapPut("/reglasderivacion/{id}/activar", (int id, HttpContext ctx) =>
        {
            var esAdmin = AutorizacionCasos.EsConfigurador(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el Configurador puede activar reglas." }, statusCode: 403);
            new ReglaDerivacionDAO().CambiarActiva(id, true);
            return Results.NoContent();
        });

        // ── Casos ─────────────────────────────────────────────────
        // Administrador: ve todos, con los filtros que pida. Profesional: solo los
        // suyos, sin importar qué filtro de profesional haya pedido (regla 6).
        g.MapGet("/casos", (string? estado, int? grupo, int? profesional, bool? sinAsignar, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin && idPropio is null)
                return Results.Json(new { mensaje = "No se pudo identificar al usuario." }, statusCode: 403);
            int? idProfesionalEfectivo = esAdmin ? profesional : idPropio;
            return Results.Ok(ConDerivado(new CasoDAO().ObtenerConFiltros(estado, grupo, idProfesionalEfectivo, sinAsignar ?? false)));
        });

        g.MapGet("/casos/{id}", (int id, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            var c = new CasoDAO().ObtenerDetalle(id);
            if (c is null) return Results.NotFound();
            if (!esAdmin && (idPropio is null || c.IdProfesionalAsignado != idPropio))
                return Results.Json(new { mensaje = "Ese caso no es tuyo." }, statusCode: 403);
            return Results.Ok(ConDerivado(c));
        });

        g.MapGet("/casos/{id}/derivaciones", (int id, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            var caso = new CasoDAO().ObtenerPorId(id);
            if (caso is null) return Results.NotFound();
            if (!esAdmin && (idPropio is null || caso.IdProfesionalAsignado != idPropio))
                return Results.Json(new { mensaje = "Ese caso no es tuyo." }, statusCode: 403);
            return Results.Ok(new CasoDerivacionDAO().ObtenerPorCaso(id));
        });

        g.MapGet("/casos/{id}/historial", (int id, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            var caso = new CasoDAO().ObtenerPorId(id);
            if (caso is null) return Results.NotFound();
            if (!esAdmin && (idPropio is null || caso.IdProfesionalAsignado != idPropio))
                return Results.Json(new { mensaje = "Ese caso no es tuyo." }, statusCode: 403);
            return Results.Ok(new HistorialAsignacionDAO().ObtenerPorCaso(id));
        });

        g.MapPut("/casos/{id}/reasignar", (int id, ReasignarCasoRequest req, HttpContext ctx, AsignacionService asignacion) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo la recepcionista puede reasignar un caso." }, statusCode: 403);
            int? idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
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
        g.MapGet("/casos/motivos-reasignacion", () => MotivoReasignacion.Categorias);

        // El caso (si existe) de un paciente: lo usa el panel "Información del lead" de
        // CRM > Chats (Profesional/Configurador), de solo lectura. Antes sin ningún
        // chequeo (ni siquiera de rol); ahora mismo criterio que ese panel.
        g.MapGet("/pacientes/{id}/caso", (int id) =>
        {
            var casoDao = new CasoDAO();
            var caso = casoDao.ObtenerAbiertoPorPaciente(id);
            if (caso is null) return Results.NotFound();
            return Results.Ok(casoDao.ObtenerDetalle(caso.IdCaso));
        }).Solo(Quien.Profesional | Quien.Crm);

        // ── Mis casos (profesional) ───────────────────────────────
        g.MapGet("/profesionales/{id}/casos", (int id, HttpContext ctx) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin && id != idPropio)
                return Results.Json(new { mensaje = "Solo podés ver tus propios casos." }, statusCode: 403);
            return Results.Ok(ConDerivado(new CasoDAO().ObtenerPorProfesional(id)));
        });

        // ── Derivaciones de casos (solo administrador) ────────────
        g.MapGet("/casosderivaciones/pendientes", (HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo la recepcionista ve la cola de derivaciones." }, statusCode: 403);
            return Results.Ok(new CasoDerivacionDAO().ObtenerPendientes());
        });

        g.MapPut("/casosderivaciones/{id}/confirmar", (int id, HttpContext ctx, AsignacionService asignacion) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo la recepcionista puede confirmar una derivación." }, statusCode: 403);
            int? idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
            // Confirmar la derivación asigna al paciente: sin consentimiento de tratamiento vigente no se hace.
            if (SinConsentimientoDeLaDerivacion(ctx, id, conProfesional: true) is { } sinConsentimiento) return sinConsentimiento;
            try { asignacion.ConfirmarDerivacion(id, idUsuario); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Json(new { mensaje = ex.Message }, statusCode: 409); }
        });

        g.MapPut("/casosderivaciones/{id}/corregir", (int id, CorregirDerivacionRequest req, HttpContext ctx, AsignacionService asignacion) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo la recepcionista puede corregir una derivación." }, statusCode: 403);
            int? idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
            if (SinConsentimientoDeLaDerivacion(ctx, id, conProfesional: req.IdProfesional is not null) is { } sinConsentimiento) return sinConsentimiento;
            try { asignacion.CorregirDerivacion(id, idUsuario, req.IdGrupo, req.IdProfesional); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Json(new { mensaje = ex.Message }, statusCode: 409); }
        });

        g.MapPut("/casosderivaciones/{id}/rechazar", (int id, HttpContext ctx, AsignacionService asignacion) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo la recepcionista puede rechazar una derivación." }, statusCode: 403);
            int? idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
            try { asignacion.RechazarDerivacion(id, idUsuario); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Json(new { mensaje = ex.Message }, statusCode: 409); }
        });

        return app;
    }

    // Confirmar o corregir una derivación de caso asigna al paciente a un profesional. Si la corrección no elige profesional y no había uno sugerido, solo cambia el grupo (no asigna a nadie).
    private static IResult? SinConsentimientoDeLaDerivacion(HttpContext ctx, int idCasoDerivacion, bool conProfesional)
    {
        var d = new CasoDerivacionDAO().ObtenerPorId(idCasoDerivacion);
        if (!conProfesional && d?.IdProfesionalSugerido is null) return null;   // corregir sin profesional elegido ni sugerido: solo cambia el grupo
        var caso = d is null ? null : new CasoDAO().ObtenerPorId(d.IdCaso);
        return caso is null ? null : ConsentimientoTratamiento.BloquearSiNoFirmo(ctx, caso.IdPaciente);
    }
}

public record CrearGrupoRequest(string Nombre, string? Descripcion);
public record MiembroGrupoRequest(int IdProfesional);
public record CrearReglaRequest(int IdEspecialidad, int IdGrupoDestino, int Prioridad);
public record CorregirDerivacionRequest(int? IdGrupo, int? IdProfesional);
public record ReasignarCasoRequest(int IdProfesional, string MotivoCategoria, string? Nota);
