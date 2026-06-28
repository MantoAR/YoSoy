var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "PANTALLAS")),
    RequestPath = "/pantallas",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

// Login
app.MapPost("/login", (LoginRequest req) =>
{
    var dao = new UsuarioDAO();
    var usuario = dao.ObtenerPorUsername(req.Username);

    if (usuario == null || usuario.Estado != "Activo")
        return Results.Unauthorized();

    if (usuario.BloqueadoHasta.HasValue && usuario.BloqueadoHasta.Value > DateTime.Now)
        return Results.Json(new { mensaje = "Cuenta bloqueada temporalmente. Intentá en 15 minutos." }, statusCode: 423);

    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(req.Password))).ToUpper();

    if (usuario.PasswordHash.ToUpper() != hash)
    {
        dao.RegistrarIntentoFallido(usuario.IdUsuario);
        var actualizado = dao.ObtenerPorUsername(req.Username);
        if (actualizado?.BloqueadoHasta.HasValue == true && actualizado.BloqueadoHasta.Value > DateTime.Now)
            return Results.Json(new { mensaje = "Cuenta bloqueada temporalmente. Intentá en 15 minutos." }, statusCode: 423);
        return Results.Unauthorized();
    }

    dao.ResetearIntentos(usuario.IdUsuario);

    var up = new UsuarioPacienteDAO().ObtenerPorUsuario(usuario.IdUsuario);
    if (up != null)
        return Results.Ok(new { rol = "paciente", idUsuario = usuario.IdUsuario, idPaciente = up.IdPaciente });

    var upro = new UsuarioProfesionalDAO().ObtenerPorUsuario(usuario.IdUsuario);
    if (upro != null)
        return Results.Ok(new { rol = "profesional", idUsuario = usuario.IdUsuario, idProfesional = upro.IdProfesional });

    return Results.Ok(new { rol = "configurador", idUsuario = usuario.IdUsuario });
});

// Especialidad
app.MapGet("/especialidades", () =>
    new EspecialidadDAO().ObtenerTodos());

app.MapGet("/especialidades/{id}", (int id) =>
{
    var e = new EspecialidadDAO().ObtenerPorId(id);
    return e is null ? Results.NotFound() : Results.Ok(e);
});

app.MapPost("/especialidades", (Especialidad e) =>
{
    e.Estado = "Activo";
    new EspecialidadDAO().Insertar(e);
    return Results.Created($"/especialidades", e);
});

app.MapPut("/especialidades/{id}", (int id, Especialidad e) =>
{
    e.IdEspecialidad = id;
    new EspecialidadDAO().Modificar(e);
    return Results.Ok(e);
});

app.MapDelete("/especialidades/{id}", (int id) =>
{
    new EspecialidadDAO().Baja(id);
    return Results.NoContent();
});

// Rol
app.MapGet("/roles", () =>
    new RolDAO().ObtenerTodos());

app.MapGet("/roles/{id}", (int id) =>
{
    var r = new RolDAO().ObtenerPorId(id);
    return r is null ? Results.NotFound() : Results.Ok(r);
});

app.MapPost("/roles", (Rol r) =>
{
    r.Estado = "Activo";
    new RolDAO().Insertar(r);
    return Results.Created($"/roles", r);
});

app.MapPut("/roles/{id}", (int id, Rol r) =>
{
    r.IdRol = id;
    new RolDAO().Modificar(r);
    return Results.Ok(r);
});

app.MapDelete("/roles/{id}", (int id) =>
{
    new RolDAO().Baja(id);
    return Results.NoContent();
});

// EstadoTurno
app.MapGet("/estadosturnos", () =>
    new EstadoTurnoDAO().ObtenerTodos());

app.MapGet("/estadosturnos/{id}", (int id) =>
{
    var et = new EstadoTurnoDAO().ObtenerPorId(id);
    return et is null ? Results.NotFound() : Results.Ok(et);
});

app.MapPost("/estadosturnos", (EstadoTurno et) =>
{
    et.Estado = "Activo";
    new EstadoTurnoDAO().Insertar(et);
    return Results.Created($"/estadosturnos", et);
});

app.MapPut("/estadosturnos/{id}", (int id, EstadoTurno et) =>
{
    et.IdEstadoTurno = id;
    new EstadoTurnoDAO().Modificar(et);
    return Results.Ok(et);
});

app.MapDelete("/estadosturnos/{id}", (int id) =>
{
    new EstadoTurnoDAO().Baja(id);
    return Results.NoContent();
});

// TipoConsentimiento
app.MapGet("/tiposconsentimiento", () =>
    new TipoConsentimientoDAO().ObtenerTodos());

app.MapGet("/tiposconsentimiento/{id}", (int id) =>
{
    var tc = new TipoConsentimientoDAO().ObtenerPorId(id);
    return tc is null ? Results.NotFound() : Results.Ok(tc);
});

app.MapPost("/tiposconsentimiento", (TipoConsentimiento tc) =>
{
    tc.Estado = "Activo";
    new TipoConsentimientoDAO().Insertar(tc);
    return Results.Created($"/tiposconsentimiento", tc);
});

app.MapPut("/tiposconsentimiento/{id}", (int id, TipoConsentimiento tc) =>
{
    tc.IdTipoConsentimiento = id;
    new TipoConsentimientoDAO().Modificar(tc);
    return Results.Ok(tc);
});

app.MapDelete("/tiposconsentimiento/{id}", (int id) =>
{
    new TipoConsentimientoDAO().Baja(id);
    return Results.NoContent();
});

// ObraSocial
app.MapGet("/obrassociales", () =>
    new ObraSocialDAO().ObtenerTodos());

app.MapGet("/obrassociales/{id}", (int id) =>
{
    var o = new ObraSocialDAO().ObtenerPorId(id);
    return o is null ? Results.NotFound() : Results.Ok(o);
});

app.MapPost("/obrassociales", (ObraSocial o) =>
{
    new ObraSocialDAO().Insertar(o);
    return Results.Created("/obrassociales", o);
});

app.MapPut("/obrassociales/{id}", (int id, ObraSocial o) =>
{
    o.IdObraSocial = id;
    new ObraSocialDAO().Modificar(o);
    return Results.Ok(o);
});

// Tutor
app.MapGet("/tutores", () =>
    new TutorDAO().ObtenerTodos());

app.MapGet("/tutores/{id}", (int id) =>
{
    var t = new TutorDAO().ObtenerPorId(id);
    return t is null ? Results.NotFound() : Results.Ok(t);
});

app.MapPost("/tutores", (Tutor t) =>
{
    new TutorDAO().Insertar(t);
    return Results.Created("/tutores", t);
});

app.MapPut("/tutores/{id}", (int id, Tutor t) =>
{
    t.IdTutor = id;
    new TutorDAO().Modificar(t);
    return Results.Ok(t);
});

// Usuario
app.MapGet("/usuarios", () =>
    new UsuarioDAO().ObtenerTodos().Select(u => new { u.IdUsuario, u.Username, u.Estado, u.Mail }));

app.MapGet("/usuarios/{id}", (int id) =>
{
    var u = new UsuarioDAO().ObtenerPorId(id);
    return u is null ? Results.NotFound() : Results.Ok(new { u.IdUsuario, u.Username, u.Estado, u.Mail });
});

app.MapPost("/usuarios", (Usuario u, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    u.Estado = "Activo";
    u.PasswordHash = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(u.PasswordHash)));
    new UsuarioDAO().Insertar(u);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Usuario", Entidad = "Usuario", Detalle = u.Username });
    return Results.Created("/usuarios", new { u.IdUsuario, u.Username, u.Estado, u.Mail });
});

app.MapPut("/usuarios/{id}", (int id, Usuario u) =>
{
    u.IdUsuario = id;
    if (!string.IsNullOrEmpty(u.PasswordHash))
        u.PasswordHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(u.PasswordHash)));
    new UsuarioDAO().Modificar(u);
    return Results.Ok(new { u.IdUsuario, u.Username, u.Estado, u.Mail });
});

app.MapDelete("/usuarios/{id}", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    new UsuarioDAO().Baja(id);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Baja Usuario", Entidad = "Usuario", IdEntidad = id });
    return Results.NoContent();
});

// Paciente
app.MapGet("/pacientes", () =>
    new PacienteDAO().ObtenerTodos());

app.MapGet("/pacientes/{id}", (int id) =>
{
    var p = new PacienteDAO().ObtenerPorId(id);
    return p is null ? Results.NotFound() : Results.Ok(p);
});

app.MapPost("/pacientes", (Paciente p) =>
{
    p.Estado = "Activo";
    new PacienteDAO().Insertar(p);
    return Results.Created("/pacientes", p);
});

app.MapPut("/pacientes/{id}", (int id, Paciente p) =>
{
    p.IdPaciente = id;
    new PacienteDAO().Modificar(p);
    return Results.Ok(p);
});

app.MapDelete("/pacientes/{id}", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    new PacienteDAO().Baja(id);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Baja Paciente", Entidad = "Paciente", IdEntidad = id });
    return Results.NoContent();
});

app.MapPut("/pacientes/{id}/alta", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    var p = new PacienteDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Activo";
    new PacienteDAO().Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Paciente", Entidad = "Paciente", IdEntidad = id });
    return Results.Ok(p);
});

// Profesional
app.MapGet("/profesionales", () =>
    new ProfesionalDAO().ObtenerTodos());

app.MapGet("/profesionales/{id}", (int id) =>
{
    var p = new ProfesionalDAO().ObtenerPorId(id);
    return p is null ? Results.NotFound() : Results.Ok(p);
});

app.MapPost("/profesionales", (Profesional p, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    p.Estado = "Pendiente";
    new ProfesionalDAO().Insertar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Profesional", Entidad = "Profesional", Detalle = $"{p.Nombre} {p.Apellido}" });
    return Results.Created("/profesionales", p);
});

app.MapPut("/profesionales/{id}", (int id, Profesional p) =>
{
    p.IdProfesional = id;
    new ProfesionalDAO().Modificar(p);
    return Results.Ok(p);
});

app.MapDelete("/profesionales/{id}", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    new ProfesionalDAO().Baja(id);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Baja Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.NoContent();
});

app.MapPut("/profesionales/{id}/alta", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    var p = new ProfesionalDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Activo";
    new ProfesionalDAO().Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(p);
});

app.MapGet("/profesionales/solicitudes", () =>
    new ProfesionalDAO().ObtenerTodos().Where(p => p.Estado == "Pendiente").ToList());

app.MapPut("/profesionales/{id}/aprobar", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    var p = new ProfesionalDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Activo";
    new ProfesionalDAO().Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Aprobación Solicitud Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(p);
});

app.MapPut("/profesionales/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    var p = new ProfesionalDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Rechazado";
    new ProfesionalDAO().Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Rechazo Solicitud Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(p);
});

// Agenda
app.MapGet("/agendas", () =>
    new AgendaDAO().ObtenerTodos());

app.MapGet("/agendas/{id}", (int id) =>
{
    var a = new AgendaDAO().ObtenerPorId(id);
    return a is null ? Results.NotFound() : Results.Ok(a);
});

app.MapGet("/profesionales/{id}/agenda", (int id) =>
    new AgendaDAO().ObtenerPorProfesional(id));

app.MapPost("/agendas", (Agenda a) =>
{
    new AgendaDAO().Insertar(a);
    return Results.Created("/agendas", a);
});

app.MapPut("/agendas/{id}", (int id, Agenda a) =>
{
    a.IdAgenda = id;
    new AgendaDAO().Modificar(a);
    return Results.Ok(a);
});

app.MapDelete("/agendas/{id}", (int id) =>
{
    new AgendaDAO().Eliminar(id);
    return Results.NoContent();
});

// Turno
app.MapGet("/turnos", () =>
    new TurnoDAO().ObtenerTodos());

app.MapGet("/turnos/{id}", (int id) =>
{
    var t = new TurnoDAO().ObtenerPorId(id);
    return t is null ? Results.NotFound() : Results.Ok(t);
});

app.MapGet("/pacientes/{id}/turnos", (int id) =>
    new TurnoDAO().ObtenerPorPaciente(id));

app.MapGet("/pacientes/{id}/turnos-detalle", (int id) =>
    new TurnoDetalleDAO().ObtenerPorPaciente(id));

app.MapGet("/pacientes/{id}/proximo-turno", (int id) =>
{
    var t = new TurnoDetalleDAO().ObtenerProximoPorPaciente(id);
    return t is null ? Results.NotFound() : Results.Ok(t);
});

app.MapGet("/pacientes/{id}/mis-profesionales", (int id) =>
    new PacienteProfesionalDAO().ObtenerProfesionalesDePaciente(id));

app.MapGet("/profesionales/{id}/pacientes", (int id) =>
{
    var ids = new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(id);
    var dao = new PacienteDAO();
    return ids.Select(idPac => dao.ObtenerPorId(idPac)).Where(p => p != null).ToList();
});

app.MapPost("/pacientes/{id}/profesionales", (int id, PacienteProfesional pp) =>
{
    new PacienteProfesionalDAO().Asignar(id, pp.IdProfesional);
    return Results.Ok();
});

app.MapDelete("/pacientes/{idPac}/profesionales/{idProf}", (int idPac, int idProf) =>
{
    new PacienteProfesionalDAO().Desasignar(idPac, idProf);
    return Results.Ok();
});

app.MapPut("/turnos/{id}/cancelar", (int id) =>
{
    var t = new TurnoDAO().ObtenerPorId(id);
    if (t is null) return Results.NotFound();
    t.IdEstadoTurno = 3; // Cancelado
    new TurnoDAO().Modificar(t);
    return Results.Ok();
});

app.MapGet("/profesionales/{id}/turnos", (int id) =>
    new TurnoDAO().ObtenerPorProfesional(id));

app.MapPost("/turnos", (Turno t) =>
{
    new TurnoDAO().Insertar(t);
    return Results.Created("/turnos", t);
});

// Solicitud de turno desde el paciente — estado siempre Pendiente
app.MapPost("/turnos/solicitar", (Turno t) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(
        "SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = 'Pendiente'", con);
    var result = cmd.ExecuteScalar();
    if (result is null) return Results.Problem("No existe el estado 'Pendiente' en EstadoTurno.");
    t.IdEstadoTurno = (int)result;
    new TurnoDAO().Insertar(t);
    return Results.Created("/turnos", t);
});

app.MapPut("/turnos/{id}", (int id, Turno t) =>
{
    t.IdTurno = id;
    new TurnoDAO().Modificar(t);
    return Results.Ok(t);
});

app.MapDelete("/turnos/{id}", (int id) =>
{
    new TurnoDAO().Baja(id);
    return Results.NoContent();
});

// Consentimiento
app.MapGet("/consentimientos", () =>
    new ConsentimientoDAO().ObtenerTodos());

app.MapGet("/consentimientos/{id}", (int id) =>
{
    var c = new ConsentimientoDAO().ObtenerPorId(id);
    return c is null ? Results.NotFound() : Results.Ok(c);
});

app.MapGet("/pacientes/{id}/consentimientos", (int id) =>
    new ConsentimientoDAO().ObtenerPorPaciente(id));

app.MapPost("/consentimientos", (Consentimiento c) =>
{
    c.Estado = "Activo";
    new ConsentimientoDAO().Insertar(c);
    return Results.Created("/consentimientos", c);
});

app.MapPut("/consentimientos/{id}", (int id, Consentimiento c) =>
{
    c.IdConsentimiento = id;
    new ConsentimientoDAO().Modificar(c);
    return Results.Ok(c);
});

app.MapDelete("/consentimientos/{id}", (int id) =>
{
    new ConsentimientoDAO().Baja(id);
    return Results.NoContent();
});

// HistoriaClinica
app.MapGet("/historiasclinicas", () =>
    new HistoriaClinicaDAO().ObtenerTodos());

app.MapGet("/historiasclinicas/{id}", (int id) =>
{
    var h = new HistoriaClinicaDAO().ObtenerPorId(id);
    return h is null ? Results.NotFound() : Results.Ok(h);
});

app.MapGet("/pacientes/{id}/historiaclinica", (int id) =>
{
    var h = new HistoriaClinicaDAO().ObtenerPorPaciente(id);
    return h is null ? Results.NotFound() : Results.Ok(h);
});

app.MapPost("/historiasclinicas", (HistoriaClinica h) =>
{
    new HistoriaClinicaDAO().Insertar(h);
    return Results.Created("/historiasclinicas", h);
});

// Evolucion
app.MapGet("/evoluciones", () =>
    new EvolucionDAO().ObtenerTodos());

app.MapGet("/evoluciones/{id}", (int id) =>
{
    var e = new EvolucionDAO().ObtenerPorId(id);
    return e is null ? Results.NotFound() : Results.Ok(e);
});

app.MapGet("/historiasclinicas/{id}/evoluciones", (int id) =>
    new EvolucionDAO().ObtenerPorHistoria(id));

app.MapPost("/evoluciones", (Evolucion e) =>
{
    new EvolucionDAO().Insertar(e);
    return Results.Created("/evoluciones", e);
});

app.MapPut("/evoluciones/{id}", (int id, Evolucion e) =>
{
    e.IdEvolucion = id;
    new EvolucionDAO().Modificar(e);
    return Results.Ok(e);
});

// Derivacion
app.MapGet("/derivaciones", () =>
    new DerivacionDAO().ObtenerTodos());

app.MapGet("/derivaciones/pendientes", () =>
    new DerivacionDetalleDAO().ObtenerPendientes());

app.MapGet("/derivaciones/detalle", () =>
    new DerivacionDetalleDAO().ObtenerTodosConDetalle());

app.MapGet("/derivaciones/{id}", (int id) =>
{
    var d = new DerivacionDAO().ObtenerPorId(id);
    return d is null ? Results.NotFound() : Results.Ok(d);
});

app.MapGet("/pacientes/{id}/derivaciones", (int id) =>
    new DerivacionDAO().ObtenerPorPaciente(id));

app.MapPost("/derivaciones", (Derivacion d) =>
{
    d.Fecha  = DateTime.Now;
    d.Estado = "Pendiente";
    new DerivacionDAO().Insertar(d);
    return Results.Created("/derivaciones", d);
});

app.MapPut("/derivaciones/{id}", (int id, Derivacion d) =>
{
    d.IdDerivacion = id;
    new DerivacionDAO().Modificar(d);
    return Results.Ok(d);
});

app.MapPut("/derivaciones/{id}/aceptar", (int id, AceptarDerivacionRequest req, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    var d = new DerivacionDAO().ObtenerPorId(id);
    if (d is null) return Results.NotFound();
    d.IdProfesionalDestino = req.IdProfesionalDestino;
    d.Estado = "Aceptada";
    new DerivacionDAO().Modificar(d);

    var dao = new PacienteProfesionalDAO();
    dao.Desasignar(d.IdPaciente, d.IdProfesionalOrigen);
    dao.Asignar(d.IdPaciente, req.IdProfesionalDestino);

    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Aceptación Derivación", Entidad = "Derivacion", IdEntidad = id });
    return Results.Ok();
});

app.MapPut("/derivaciones/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    var d = new DerivacionDAO().ObtenerPorId(id);
    if (d is null) return Results.NotFound();
    d.Estado = "Rechazada";
    new DerivacionDAO().Modificar(d);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Rechazo Derivación", Entidad = "Derivacion", IdEntidad = id });
    return Results.Ok(d);
});

// Cambio directo de profesional (configurador): registra derivación aceptada + reasigna turnos futuros
app.MapPost("/derivaciones/cambio-directo", (CambioProfesionalRequest req, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;

    using var con = Conexion.ObtenerConexion();
    con.Open();

    var insert = new Microsoft.Data.SqlClient.SqlCommand(@"
        INSERT INTO Derivacion (IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado)
        VALUES (@IdPac, @IdOrigen, @IdDestino, GETDATE(), @Motivo, 'Aceptada')", con);
    insert.Parameters.AddWithValue("@IdPac",    req.IdPaciente);
    insert.Parameters.AddWithValue("@IdOrigen", req.IdProfesionalOrigen);
    insert.Parameters.AddWithValue("@IdDestino",req.IdProfesionalDestino);
    insert.Parameters.AddWithValue("@Motivo",   (object?)req.Motivo ?? DBNull.Value);
    insert.ExecuteNonQuery();

    var dao = new PacienteProfesionalDAO();
    dao.Desasignar(req.IdPaciente, req.IdProfesionalOrigen);
    dao.Asignar(req.IdPaciente, req.IdProfesionalDestino);

    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Cambio Directo Profesional", Entidad = "Derivacion", Detalle = req.Motivo });
    return Results.Ok();
});

app.MapGet("/profesionales/activos/especialidad/{idEsp}", (int idEsp) =>
    new ProfesionalDAO().ObtenerPorEspecialidad(idEsp)
        .Where(p => p.Estado == "Activo").ToList());

// EventoCrisis
app.MapGet("/eventoscrisis", () =>
    new EventoCrisisDAO().ObtenerTodos());

app.MapGet("/eventoscrisis/{id}", (int id) =>
{
    var e = new EventoCrisisDAO().ObtenerPorId(id);
    return e is null ? Results.NotFound() : Results.Ok(e);
});

app.MapGet("/pacientes/{id}/eventoscrisis", (int id) =>
    new EventoCrisisDAO().ObtenerPorPaciente(id));

app.MapPost("/eventoscrisis", (EventoCrisis e) =>
{
    new EventoCrisisDAO().Insertar(e);
    return Results.Created("/eventoscrisis", e);
});

app.MapPut("/eventoscrisis/{id}", (int id, EventoCrisis e) =>
{
    e.IdEvento = id;
    new EventoCrisisDAO().Modificar(e);
    return Results.Ok(e);
});

// InteraccionIA
app.MapGet("/interaccionesia", () =>
    new InteraccionIADAO().ObtenerTodos());

app.MapGet("/interaccionesia/{id}", (int id) =>
{
    var i = new InteraccionIADAO().ObtenerPorId(id);
    return i is null ? Results.NotFound() : Results.Ok(i);
});

app.MapGet("/pacientes/{id}/interaccionesia", (int id) =>
    new InteraccionIADAO().ObtenerPorPaciente(id));

app.MapPost("/interaccionesia", (InteraccionIA i) =>
{
    new InteraccionIADAO().Insertar(i);
    return Results.Created("/interaccionesia", i);
});

// Cobertura
app.MapGet("/coberturas", () =>
    new CoberturaDAO().ObtenerTodos());

app.MapGet("/coberturas/{id}", (int id) =>
{
    var c = new CoberturaDAO().ObtenerPorId(id);
    return c is null ? Results.NotFound() : Results.Ok(c);
});

app.MapGet("/pacientes/{id}/coberturas", (int id) =>
    new CoberturaDAO().ObtenerPorPaciente(id));

app.MapPost("/coberturas", (Cobertura c) =>
{
    new CoberturaDAO().Insertar(c);
    return Results.Created("/coberturas", c);
});

app.MapPut("/coberturas/{id}", (int id, Cobertura c) =>
{
    c.IdCobertura = id;
    new CoberturaDAO().Modificar(c);
    return Results.Ok(c);
});

app.MapDelete("/coberturas/{id}", (int id) =>
{
    new CoberturaDAO().Baja(id);
    return Results.NoContent();
});

// PacienteTutor
app.MapGet("/pacientes/{id}/tutores", (int id) =>
    new PacienteTutorDAO().ObtenerPorPaciente(id));

app.MapPost("/pacientetutores", (PacienteTutor pt) =>
{
    new PacienteTutorDAO().Insertar(pt);
    return Results.Created("/pacientetutores", pt);
});

app.MapDelete("/pacientetutores/{idPaciente}/{idTutor}", (int idPaciente, int idTutor) =>
{
    new PacienteTutorDAO().Eliminar(idPaciente, idTutor);
    return Results.NoContent();
});

// UsuarioRol
app.MapGet("/usuarios/{id}/roles", (int id) =>
    new UsuarioRolDAO().ObtenerPorUsuario(id));

app.MapPost("/usuarioroles", (UsuarioRol ur) =>
{
    new UsuarioRolDAO().Insertar(ur);
    return Results.Created("/usuarioroles", ur);
});

app.MapDelete("/usuarioroles/{idUsuario}/{idRol}", (int idUsuario, int idRol) =>
{
    new UsuarioRolDAO().Eliminar(idUsuario, idRol);
    return Results.NoContent();
});

// UsuarioPaciente
app.MapGet("/usuarios/{id}/paciente", (int id) =>
{
    var up = new UsuarioPacienteDAO().ObtenerPorUsuario(id);
    return up is null ? Results.NotFound() : Results.Ok(up);
});

app.MapPost("/usuariopacientes", (UsuarioPaciente up) =>
{
    new UsuarioPacienteDAO().Insertar(up);
    return Results.Created("/usuariopacientes", up);
});

app.MapDelete("/usuariopacientes/{idUsuario}", (int idUsuario) =>
{
    new UsuarioPacienteDAO().Eliminar(idUsuario);
    return Results.NoContent();
});

// UsuarioProfesional
app.MapGet("/usuarios/{id}/profesional", (int id) =>
{
    var up = new UsuarioProfesionalDAO().ObtenerPorUsuario(id);
    return up is null ? Results.NotFound() : Results.Ok(up);
});

app.MapPost("/usuarioprofesionales", (UsuarioProfesional up) =>
{
    new UsuarioProfesionalDAO().Insertar(up);
    return Results.Created("/usuarioprofesionales", up);
});

app.MapDelete("/usuarioprofesionales/{idUsuario}", (int idUsuario) =>
{
    new UsuarioProfesionalDAO().Eliminar(idUsuario);
    return Results.NoContent();
});

app.MapGet("/auditoria", (int pagina = 1, int cantidad = 50) =>
    new LogAuditoriaDAO().ObtenerPaginado(pagina, cantidad));

app.MapPost("/recuperacion-password", (RecuperacionPasswordRequest req) =>
{
    var usuario = new UsuarioDAO().ObtenerPorUsername(req.Username);
    if (usuario == null) return Results.NotFound(new { mensaje = "Usuario no encontrado." });

    var dao = new RecuperacionPasswordDAO();
    if (dao.TienePendiente(usuario.IdUsuario))
        return Results.Conflict(new { mensaje = "Ya tenés una solicitud pendiente." });

    dao.Insertar(usuario.IdUsuario);
    return Results.Ok();
});

app.MapGet("/recuperacion-password/pendientes", () =>
    new RecuperacionPasswordDAO().ObtenerPendientes());

app.MapPut("/recuperacion-password/{id}/aceptar", (int id) =>
{
    var recDAO = new RecuperacionPasswordDAO();
    var idUsuario = recDAO.ObtenerIdUsuario(id);
    if (idUsuario == null) return Results.NotFound();

    const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
    var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
    var passTemp = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());

    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(passTemp)));

    var uDAO = new UsuarioDAO();
    var usuario = uDAO.ObtenerPorId(idUsuario.Value);
    if (usuario == null) return Results.NotFound();
    usuario.PasswordHash = hash;
    uDAO.Modificar(usuario);
    uDAO.ResetearIntentos(idUsuario.Value);

    recDAO.Aceptar(id, passTemp);
    return Results.Ok(new { passwordTemporal = passTemp });
});

app.MapPut("/recuperacion-password/{id}/rechazar", (int id) =>
{
    var dao = new RecuperacionPasswordDAO();
    if (dao.ObtenerIdUsuario(id) == null) return Results.NotFound();
    dao.Rechazar(id);
    return Results.Ok();
});

app.MapGet("/recuperacion-password/estado/{username}", (string username) =>
{
    var rec = new RecuperacionPasswordDAO().ObtenerUltimaPorUsername(username);
    if (rec == null) return Results.NotFound(new { mensaje = "No hay solicitudes para este usuario." });

    if (rec.Estado == "Aceptada")
        return Results.Ok(new { estado = rec.Estado, passwordTemporal = rec.PasswordTemporal });

    return Results.Ok(new { estado = rec.Estado, passwordTemporal = (string)null });
});

// SesionClinica
app.MapPost("/sesiones-clinicas", (SesionClinica s) =>
{
    s.FechaHora = DateTime.Now;
    int id = new SesionClinicaDAO().Insertar(s);
    s.IdSesion = id;
    return Results.Created("/sesiones-clinicas", s);
});

app.MapGet("/pacientes/{id}/sesiones-clinicas", (int id) =>
    new SesionClinicaDAO().ObtenerPorPaciente(id));

app.MapGet("/profesionales/{id}/sesiones-clinicas", (int id) =>
    new SesionClinicaDAO().ObtenerPorProfesional(id));

app.Run();
