using System.Globalization;

// Agenda y matrícula (bloques 1 y 2 de rama AAA1, migración 33). Se registra con app.MapAgendaRestricciones().
//
//   Feriados (los carga el Configurador; los ve cualquiera con sesión)
//     GET    /feriados?anio=                         Feriados del año.
//     POST   /feriados                               { fecha: "2026-11-20", nombre }. Configurador.
//     DELETE /feriados/{id}                          Lo da de baja (no se borra). Configurador.
//     GET    /profesionales/yo/feriados              Próximos 12 meses, con mi marca "trabajo este feriado". Profesional.
//     PUT    /profesionales/yo/feriados/{id}         { trabajo: true|false }. Profesional.
//
//   Bloqueos de agenda (el propio profesional, o CRM con Gestión de agendas)
//     GET    /profesionales/{id}/bloqueos            Vigentes y futuros.
//     POST   /profesionales/{id}/bloqueos            { desde, hasta, motivo, detalle }. Devuelve los turnos que quedan adentro.
//     DELETE /bloqueos/{id}                          Lo anula (no se borra).
//
//   Inasistencia: PUT/DELETE /turnos/{id}/inasistencia viven en Endpoints/AsistenciaEndpoints.cs (migración 37).
//
//   Matrícula
//     GET    /profesionales/yo/matricula             Estado de mi matrícula principal (para el aviso de la pantalla). Profesional.
//     GET    /profesionales/{id}/matriculas          El propio profesional, el Configurador o el Profesional auditor.
//     POST   /profesionales/{id}/matriculas          { idEspecialidad, numero, jurisdiccion, vencimiento, principal }. Configurador.
//     PUT    /matriculas/{id}                        { numero, jurisdiccion, vencimiento } (renovación). Configurador.
//     DELETE /matriculas/{id}                        Baja de una secundaria. Configurador.
//     GET    /reportes/matriculas                    Vencimientos de todos. Profesional auditor o Configurador.
//     POST   /matriculas/avisos/ejecutar             Corre ahora la pasada diaria de avisos (para probar). Configurador.
//
//   Avisos del profesional (campanita)
//     GET    /profesionales/yo/avisos
//     PUT    /profesionales/yo/avisos/{id}/leida
//
// Nada de esto lleva datos clínicos salvo la inasistencia (es un hecho de la atención de un paciente): por eso esa va a la
// auditoría clínica y el resto a la administrativa.
public static class AgendaEndpoints
{
    public record FeriadoRequest(string? Fecha, string? Nombre);
    public record TrabajoFeriadoRequest(bool Trabajo);
    public record BloqueoRequest(string? Desde, string? Hasta, string? Motivo, string? Detalle);
    public record MatriculaRequest(int? IdEspecialidad, string? Numero, string? Jurisdiccion, string? Vencimiento, bool Principal);
    public record RenovarMatriculaRequest(string? Numero, string? Jurisdiccion, string? Vencimiento);

    private static IResult Error(int status, string mensaje) => Results.Json(new { mensaje }, statusCode: status);

    private static DateTime Hoy(TimeProvider reloj) => reloj.GetLocalNow().DateTime.Date;

    private static bool LeerFecha(string? texto, out DateTime fecha) =>
        DateTime.TryParseExact(texto?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);

    private static bool LeerMomento(string? texto, out DateTime momento) =>
        DateTime.TryParseExact(texto?.Trim(), new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out momento);

    private static string? Limpio(string? texto, int maximo)
    {
        var t = texto?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length <= maximo ? t : t[..maximo];
    }

    private static void Auditar(Identidad yo, string accion, string entidad, int? idEntidad, string detalle) =>
        AuditoriaAgenda.Registrar(yo.IdUsuario, accion, entidad, idEntidad, detalle, yo.Modulo ?? yo.Rol);

    public static IEndpointRouteBuilder MapAgendaRestricciones(this IEndpointRouteBuilder app)
    {
        // ─────────────────────────────── Feriados ───────────────────────────────
        app.MapGet("/feriados", (int? anio, TimeProvider reloj) =>
            Results.Ok(new AgendaRestriccionesDAO().Feriados(anio ?? Hoy(reloj).Year)))
            .RequireAuthorization();

        app.MapPost("/feriados", (FeriadoRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!LeerFecha(req.Fecha, out var fecha)) return Error(400, "La fecha tiene que tener el formato AAAA-MM-DD.");
            var nombre = Limpio(req.Nombre, 120);
            if (nombre is null || nombre.Length < 3) return Error(400, "Escribí el nombre del feriado.");
            var dao = new AgendaRestriccionesDAO();
            var id = dao.CrearFeriado(fecha, nombre, yo.IdUsuario);
            if (id is null) return Error(409, $"Ya hay un feriado cargado el {fecha:dd/MM/yyyy}.");
            Auditar(yo, "Feriado cargado", "Feriado", id, $"{fecha:dd/MM/yyyy} · {nombre}");
            return Results.Created($"/feriados/{id}", new { idFeriado = id, fecha, nombre });
        }).Solo(Quien.Configurador);

        app.MapDelete("/feriados/{id:int}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new AgendaRestriccionesDAO();
            var f = dao.Feriado(id);
            if (f is null || !dao.AnularFeriado(id)) return Results.NotFound();
            Auditar(yo, "Feriado dado de baja", "Feriado", id, $"{f.Fecha:dd/MM/yyyy} · {f.Nombre}");
            return Results.NoContent();
        }).Solo(Quien.Configurador);

        app.MapGet("/profesionales/yo/feriados", (HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProf) return Error(403, "Tu cuenta no tiene ficha de profesional.");
            var hoy = Hoy(reloj);
            return Results.Ok(new AgendaRestriccionesDAO().FeriadosDelProfesional(idProf, hoy, hoy.AddMonths(12)));
        }).Solo(Quien.Profesional);

        app.MapPut("/profesionales/yo/feriados/{id:int}", (int id, TrabajoFeriadoRequest req, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProf) return Error(403, "Tu cuenta no tiene ficha de profesional.");
            var dao = new AgendaRestriccionesDAO();
            var f = dao.Feriado(id);
            if (f is null) return Results.NotFound();
            if (f.Fecha.Date < Hoy(reloj)) return Error(409, "Ese feriado ya pasó.");
            dao.MarcarTrabajoFeriado(id, idProf, req.Trabajo);
            Auditar(yo, req.Trabajo ? "Trabaja el feriado" : "No trabaja el feriado", "Feriado", id, $"{f.Fecha:dd/MM/yyyy} · profesional #{idProf}");
            return Results.Ok(new { idFeriado = id, trabajo = req.Trabajo });
        }).Solo(Quien.Profesional);

        // ─────────────────────────────── Bloqueos ───────────────────────────────
        app.MapGet("/profesionales/{id:int}/bloqueos", (int id, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!yo.GestionaAgendas && yo.IdProfesional != id) return Error(403, "Solo podés ver tus propios bloqueos.");
            return Results.Ok(new AgendaRestriccionesDAO().Bloqueos(id, reloj.GetLocalNow().DateTime));
        }).Solo(Quien.Profesional | Quien.Agendas);

        app.MapPost("/profesionales/{id:int}/bloqueos", (int id, BloqueoRequest req, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!yo.GestionaAgendas && yo.IdProfesional != id) return Error(403, "Solo podés bloquear tu propia agenda.");
            if (!LeerMomento(req.Desde, out var desde) || !LeerMomento(req.Hasta, out var hasta))
                return Error(400, "Elegí fecha y hora de inicio y de fin.");
            if (hasta <= desde) return Error(400, "El fin tiene que ser posterior al inicio.");
            if (hasta <= reloj.GetLocalNow().DateTime) return Error(400, "Ese período ya pasó.");
            if ((hasta - desde).TotalDays > 366) return Error(400, "Un bloqueo puede durar hasta un año. Para más, cargá otro.");
            var motivo = MotivosBloqueo.Normalizar(req.Motivo);
            if (motivo is null) return Error(400, "Elegí el motivo: " + string.Join(", ", MotivosBloqueo.Todos) + ".");
            var detalle = Limpio(req.Detalle, 200);

            var dao = new AgendaRestriccionesDAO();
            var idBloqueo = dao.CrearBloqueo(id, desde, hasta, motivo, detalle, yo.IdUsuario);
            var afectados = dao.TurnosEntre(id, desde, hasta);
            // El detalle puede tener texto libre: a la auditoría va solo el motivo y el período.
            Auditar(yo, "Bloqueo de agenda", "BloqueoAgenda", idBloqueo,
                $"Profesional #{id} · {motivo} · {desde:dd/MM/yyyy HH:mm} a {hasta:dd/MM/yyyy HH:mm} · {afectados.Count} turno(s) adentro");
            return Results.Created($"/bloqueos/{idBloqueo}", new { idBloqueo, turnosAfectados = afectados });
        }).Solo(Quien.Profesional | Quien.Agendas);

        app.MapDelete("/bloqueos/{id:int}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new AgendaRestriccionesDAO();
            var b = dao.Bloqueo(id);
            if (b is null) return Results.NotFound();
            if (!yo.GestionaAgendas && yo.IdProfesional != b.IdProfesional) return Error(403, "Ese bloqueo no es tuyo.");
            dao.AnularBloqueo(id);
            Auditar(yo, "Bloqueo de agenda anulado", "BloqueoAgenda", id, $"Profesional #{b.IdProfesional} · {b.Motivo} · {b.Desde:dd/MM/yyyy HH:mm} a {b.Hasta:dd/MM/yyyy HH:mm}");
            return Results.NoContent();
        }).Solo(Quien.Profesional | Quien.Agendas);

        // Inasistencia: pasó a Endpoints/AsistenciaEndpoints.cs (migración 37: tolerancia, recepción con Gestión de pacientes y cobro).

        // ─────────────────────────────── Matrículas ───────────────────────────────
        app.MapGet("/profesionales/yo/matricula", (HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProf) return Error(403, "Tu cuenta no tiene ficha de profesional.");
            var hoy = Hoy(reloj);
            var dao = new AgendaRestriccionesDAO();
            var lista = dao.Matriculas(idProf);
            var principal = lista.FirstOrDefault(m => m.Principal);
            var turnosDespues = principal?.Vencimiento is DateTime v ? dao.TurnosEntre(idProf, v, null).Count : 0;
            return Results.Ok(new
            {
                matriculas = lista.Select(m => new
                {
                    m.IdMatricula, m.Especialidad, m.Numero, m.Jurisdiccion, m.Vencimiento, m.Principal,
                    estado = EstadoMatricula.De(m.Vencimiento, hoy), diasRestantes = EstadoMatricula.DiasRestantes(m.Vencimiento, hoy)
                }),
                agendaSuspendida = principal?.Vencimiento is DateTime venc && hoy >= venc.Date,
                turnosDespuesDelVencimiento = turnosDespues
            });
        }).Solo(Quien.Profesional);

        app.MapGet("/profesionales/{id:int}/matriculas", (int id, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!yo.EsConfigurador && !yo.EsAuditor && yo.IdProfesional != id) return Error(403, "No podés ver las matrículas de otro profesional.");
            var hoy = Hoy(reloj);
            return Results.Ok(new AgendaRestriccionesDAO().Matriculas(id).Select(m => new
            {
                m.IdMatricula, m.IdEspecialidad, m.Especialidad, m.Numero, m.Jurisdiccion, m.Vencimiento, m.Principal,
                estado = EstadoMatricula.De(m.Vencimiento, hoy), diasRestantes = EstadoMatricula.DiasRestantes(m.Vencimiento, hoy)
            }));
        }).Solo(Quien.Profesional | Quien.Configurador);

        app.MapPost("/profesionales/{id:int}/matriculas", (int id, MatriculaRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (new ProfesionalDAO().ObtenerPorId(id) is null) return Results.NotFound();
            var numero = Limpio(req.Numero, 50);
            if (numero is null) return Error(400, "Escribí el número de matrícula.");
            if (req.IdEspecialidad is not int idEsp) return Error(400, "Elegí la especialidad.");
            if (!LeerFecha(req.Vencimiento, out var vence)) return Error(400, "Elegí la fecha de vencimiento de la matrícula.");
            var dao = new AgendaRestriccionesDAO();
            if (!dao.EspecialidadValida(idEsp)) return Error(400, "Esa especialidad no está disponible.");
            var principal = req.Principal || !dao.Matriculas(id).Any(m => m.Principal);   // la primera siempre es la principal
            if (dao.TieneMatriculaEn(id, idEsp))
                return Error(409, "Ya tiene una matrícula cargada en esa especialidad. Para renovarla, editala.");
            var idMat = dao.CrearMatricula(id, idEsp, numero, Limpio(req.Jurisdiccion, 60), vence, principal, yo.IdUsuario);
            Auditar(yo, "Matrícula cargada", "Matricula", idMat,
                $"Profesional #{id} · {(principal ? "principal" : "secundaria")} · vence {vence:dd/MM/yyyy}");
            return Results.Created($"/matriculas/{idMat}", new { idMatricula = idMat, principal });
        }).Solo(Quien.Configurador);

        app.MapPut("/matriculas/{id:int}", (int id, RenovarMatriculaRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new AgendaRestriccionesDAO();
            var m = dao.Matricula(id);
            if (m is null) return Results.NotFound();
            var numero = Limpio(req.Numero, 50);
            if (numero is null) return Error(400, "Escribí el número de matrícula.");
            if (!LeerFecha(req.Vencimiento, out var vence)) return Error(400, "Elegí la fecha de vencimiento de la matrícula.");
            dao.ModificarMatricula(id, numero, Limpio(req.Jurisdiccion, 60), vence, yo.IdUsuario);
            Auditar(yo, "Matrícula modificada", "Matricula", id,
                $"Profesional #{m.IdProfesional} · vencimiento {(m.Vencimiento is DateTime a ? a.ToString("dd/MM/yyyy") : "sin cargar")} -> {vence:dd/MM/yyyy}"
                + (numero != m.Numero ? " · cambió el número" : ""));
            return Results.Ok(new { idMatricula = id });
        }).Solo(Quien.Configurador);

        app.MapDelete("/matriculas/{id:int}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new AgendaRestriccionesDAO();
            var m = dao.Matricula(id);
            if (m is null) return Results.NotFound();
            if (m.Principal) return Error(409, "La matrícula principal no se da de baja: cargá otra como principal o renovala.");
            dao.AnularMatriculaSecundaria(id, yo.IdUsuario);
            Auditar(yo, "Matrícula dada de baja", "Matricula", id, $"Profesional #{m.IdProfesional} · secundaria · {m.Especialidad}");
            return Results.NoContent();
        }).Solo(Quien.Configurador);

        app.MapGet("/reportes/matriculas", (HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var hoy = Hoy(reloj);
            var filas = new AgendaRestriccionesDAO().Reporte().Select(r => new
            {
                r.IdProfesional, r.Profesional, r.IdMatricula, r.Especialidad, r.Numero, r.Jurisdiccion, r.Vencimiento, r.Principal,
                r.TurnosDespuesDelVencimiento,
                estado = r.IdMatricula is null ? "Sin matrícula cargada" : EstadoMatricula.De(r.Vencimiento, hoy),
                diasRestantes = EstadoMatricula.DiasRestantes(r.Vencimiento, hoy)
            }).ToList();
            return Results.Ok(new
            {
                hoy,
                resumen = new
                {
                    vencidas = filas.Count(f => f.estado == EstadoMatricula.Vencida),
                    porVencer = filas.Count(f => f.estado == EstadoMatricula.PorVencer),
                    sinFecha = filas.Count(f => f.estado is EstadoMatricula.SinFecha or "Sin matrícula cargada"),
                    vigentes = filas.Count(f => f.estado == EstadoMatricula.Vigente)
                },
                filas
            });
        }).Solo(Quien.Auditor | Quien.Configurador);

        app.MapPost("/matriculas/avisos/ejecutar", (MatriculaAvisosService avisos) =>
            Results.Ok(avisos.EjecutarUnaVez(forzar: true)))
            .Solo(Quien.Configurador);

        // ─────────────────────────────── Avisos del profesional ───────────────────────────────
        app.MapGet("/profesionales/yo/avisos", (HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProf) return Results.Ok(Array.Empty<object>());
            return Results.Ok(new AgendaRestriccionesDAO().AvisosDelProfesional(idProf, 30));
        }).Solo(Quien.Profesional);

        app.MapPut("/profesionales/yo/avisos/{id:int}/leida", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProf) return Results.NotFound();
            return new AgendaRestriccionesDAO().MarcarAvisoLeido(id, idProf) ? Results.NoContent() : Results.NotFound();
        }).Solo(Quien.Profesional);

        return app;
    }

    /// <summary>
    /// Para POST/PUT de /agendas: con la matrícula principal vencida no se pueden cargar ni cambiar horarios.
    /// Devuelve null si se puede seguir.
    /// </summary>
    public static IResult? AgendaSuspendida(int idProfesional, TimeProvider reloj)
    {
        var vence = new AgendaRestriccionesDAO().VencimientoPrincipal(idProfesional);
        if (vence is DateTime v && Hoy(reloj) >= v.Date)
            return Error(409, $"La matrícula venció el {v:dd/MM/yyyy}: no se pueden configurar horarios hasta que el Configurador cargue la renovación.");
        return null;
    }
}

// Auditoría administrativa de agenda y matrícula (la ve el Configurador). Igual que AuditoriaIngreso: si falla, no frena el pedido.
public static class AuditoriaAgenda
{
    public static void Registrar(int? idUsuario, string accion, string entidad, int? idEntidad, string detalle, string? espacio)
    {
        try
        {
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = idUsuario, Accion = accion, Entidad = entidad, IdEntidad = idEntidad,
                Detalle = detalle.Length <= 500 ? detalle : detalle[..500],
                Categoria = CategoriaAuditoria.Administrativa, Espacio = espacio
            });
        }
        catch (Exception) { /* la auditoría no debe impedir responder */ }
    }
}
