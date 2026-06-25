using System.Linq;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

Conexion.Inicializar(builder.Configuration.GetConnectionString("YoSoy"));

var origenesCors = builder.Configuration.GetSection("Cors:Origenes").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p =>
    {
        if (origenesCors.Length > 0)
            p.WithOrigins(origenesCors).AllowAnyMethod().AllowAnyHeader();
        else
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    }));

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHostedService<RecordatorioSesionService>();

var app = builder.Build();
app.UseWebSockets();
app.UseCors();
app.MapReverseProxy();

app.MapGet("/config", () => Results.Ok(new
{
    signalingUrl          = app.Configuration["Signaling:UrlPublica"],
    stun                  = app.Configuration.GetSection("Stun").Get<string[]>() ?? Array.Empty<string>(),
    modoLan               = !bool.TryParse(app.Configuration["Sesion:ModoLan"], out var lan) || lan,
    margenIngresoMinutos  = int.TryParse(app.Configuration["Sesion:MargenIngresoMinutos"], out var mg) ? mg : 5,
    duracionTurnoDefault  = int.TryParse(app.Configuration["Sesion:DuracionTurnoDefaultMinutos"], out var dt) ? dt : 60
}));

static string IpCliente(HttpContext ctx)
{
    // Detrás del túnel/reverse proxy, RemoteIpAddress es siempre 127.0.0.1 (cloudflared pega a localhost),
    // así que todos los usuarios compartirían un único cupo. Usar la IP real que reenvía Cloudflare.
    var cf = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();
    var xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(xff)) return xff.Split(',')[0].Trim();
    return ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
}

static IResult? LimitarTasa(HttpContext ctx, IConfiguration cfg, string bucket, int maximo)
{
    var ip = IpCliente(ctx);
    int ventana = int.TryParse(cfg["RateLimit:VentanaSegundos"], out var v) ? v : 300;
    if (!RateLimiter.Permitido($"{bucket}|{ip}", maximo, ventana))
        return Results.Json(new { mensaje = "Demasiados intentos. Esperá unos minutos." }, statusCode: 429);
    return null;
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "PANTALLAS")),
    RequestPath = "/pantallas",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

// Login
app.MapPost("/login", (LoginRequest req, HttpContext ctx) =>
{
    int maxLogin = int.TryParse(app.Configuration["RateLimit:Login"], out var ml) ? ml : 3;
    var lim = LimitarTasa(ctx, app.Configuration, "login", maxLogin);
    if (lim != null) return lim;

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

    string TokenSesion(string rol) =>
        new JwtService(app.Configuration).GenerarLogin(usuario.IdUsuario, rol, DateTime.Now.AddHours(8));

    var up = new UsuarioPacienteDAO().ObtenerPorUsuario(usuario.IdUsuario);
    if (up != null)
        return Results.Ok(new { rol = "paciente", idUsuario = usuario.IdUsuario, idPaciente = up.IdPaciente, token = TokenSesion("paciente") });

    var upro = new UsuarioProfesionalDAO().ObtenerPorUsuario(usuario.IdUsuario);
    if (upro != null)
        return Results.Ok(new { rol = "profesional", idUsuario = usuario.IdUsuario, idProfesional = upro.IdProfesional, token = TokenSesion("profesional") });

    return Results.Ok(new { rol = "configurador", idUsuario = usuario.IdUsuario, token = TokenSesion("configurador") });
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
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
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
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
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
    p.Estado = "Pendiente";
    new PacienteDAO().Insertar(p);
    return Results.Created("/pacientes", p);
});

app.MapGet("/pacientes/solicitudes", () =>
    new PacienteDAO().ObtenerTodos().Where(p => p.Estado == "Pendiente").ToList());

app.MapPut("/pacientes/{id}/aprobar", (int id, AprobarPacienteRequest req, HttpContext ctx) =>
{
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
    var dao = new PacienteDAO();
    var p = dao.ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    if (req.IdProfesional <= 0) return Results.BadRequest(new { mensaje = "Asigná un profesional antes de aprobar la cuenta." });

    p.Estado = "Activo";
    dao.Modificar(p);

    new PacienteProfesionalDAO().Asignar(id, req.IdProfesional);

    if (new HistoriaClinicaDAO().ObtenerPorPaciente(id) is null)
        new HistoriaClinicaDAO().Insertar(new HistoriaClinica { IdPaciente = id, FechaApertura = DateTime.Now });

    var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    dao.GuardarToken(id, token);

    if (!string.IsNullOrWhiteSpace(p.Mail))
    {
        try { new EmailService(app.Configuration).EnviarAprobacionPaciente(p.Mail, p.Nombre, token); }
        catch { }
    }

    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Aprobación Solicitud Paciente", Entidad = "Paciente", IdEntidad = id });
    return Results.Ok(p);
});

app.MapPut("/pacientes/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
    var dao = new PacienteDAO();
    var p = dao.ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Rechazado";
    dao.Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Rechazo Solicitud Paciente", Entidad = "Paciente", IdEntidad = id });
    return Results.Ok(p);
});

app.MapPost("/pacientes/activar-cuenta", (ActivarCuentaPacienteRequest req) =>
{
    var dao = new PacienteDAO();
    var pac = dao.ObtenerPorToken(req.Token);
    if (pac == null) return Results.NotFound(new { mensaje = "Link inválido o ya utilizado." });

    var uDAO = new UsuarioDAO();
    if (uDAO.ObtenerPorUsername(req.Username) != null)
        return Results.Conflict(new { mensaje = "Ese nombre de usuario ya está en uso." });

    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(req.Password)));

    var nuevoUsuario = new Usuario
    {
        Username     = req.Username,
        PasswordHash = hash,
        Estado       = "Activo",
        Mail         = pac.Mail
    };
    int idUsuario = uDAO.InsertarYObtenerID(nuevoUsuario);

    new UsuarioPacienteDAO().Insertar(new UsuarioPaciente
    {
        IdUsuario        = idUsuario,
        IdPaciente       = pac.IdPaciente,
        FechaVinculacion = DateTime.Now
    });

    dao.LimpiarToken(pac.IdPaciente);

    return Results.Ok(new { mensaje = "Cuenta creada correctamente. Ya podés iniciar sesión." });
});

app.MapPut("/pacientes/{id}", (int id, Paciente p) =>
{
    p.IdPaciente = id;
    new PacienteDAO().Modificar(p);
    return Results.Ok(p);
});

app.MapDelete("/pacientes/{id}", (int id, HttpContext ctx) =>
{
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
    new PacienteDAO().Baja(id);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Baja Paciente", Entidad = "Paciente", IdEntidad = id });
    return Results.NoContent();
});

app.MapPut("/pacientes/{id}/alta", (int id, HttpContext ctx) =>
{
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
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
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
    p.Estado = "Pendiente";
    try
    {
        p.IdProfesional = new ProfesionalDAO().InsertarYObtenerID(p);
    }
    catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 2627)
    {
        return Results.Conflict(new { mensaje = "Ya existe un profesional registrado con esa matrícula." });
    }
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
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
    new ProfesionalDAO().Baja(id);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Baja Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.NoContent();
});

app.MapPut("/profesionales/{id}/alta", (int id, HttpContext ctx) =>
{
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
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
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
    var dao = new ProfesionalDAO();
    var p = dao.ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Activo";
    dao.Modificar(p);

    var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    dao.GuardarToken(id, token);

    try
    {
        new EmailService(app.Configuration).EnviarAprobacionProfesional(p.Mail, p.Nombre, token);
    }
    catch { }

    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Aprobación Solicitud Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(p);
});

app.MapPost("/profesionales/activar-cuenta", (ActivarCuentaProfesionalRequest req) =>
{
    var dao = new ProfesionalDAO();
    var prof = dao.ObtenerPorToken(req.Token);
    if (prof == null) return Results.NotFound(new { mensaje = "Link inválido o ya utilizado." });

    var uDAO = new UsuarioDAO();
    if (uDAO.ObtenerPorUsername(req.Username) != null)
        return Results.Conflict(new { mensaje = "Ese nombre de usuario ya está en uso." });

    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(req.Password)));

    var nuevoUsuario = new Usuario
    {
        Username     = req.Username,
        PasswordHash = hash,
        Estado       = "Activo",
        Mail         = prof.Mail
    };
    int idUsuario = uDAO.InsertarYObtenerID(nuevoUsuario);

    new UsuarioProfesionalDAO().Insertar(new UsuarioProfesional
    {
        IdUsuario        = idUsuario,
        IdProfesional    = prof.IdProfesional,
        FechaVinculacion = DateTime.Now
    });

    dao.LimpiarToken(prof.IdProfesional);

    return Results.Ok(new { mensaje = "Cuenta creada correctamente. Ya podés iniciar sesión." });
});

app.MapPut("/profesionales/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
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

app.MapPost("/pacientes/{id}/profesionales", (int id, PacienteProfesional pp) =>
{
    new PacienteProfesionalDAO().Asignar(id, pp.IdProfesional);
    return Results.Ok();
});

app.MapDelete("/pacientes/{idPac}/profesionales/{idProf}", (int idPac, int idProf) =>
{
    new TurnoDAO().CancelarTurnosPacienteProfesional(idPac, idProf);
    new PacienteProfesionalDAO().Desasignar(idPac, idProf);
    return Results.Ok();
});

app.MapPut("/turnos/{id}/cancelar", (int id) =>
{
    var t = new TurnoDAO().ObtenerPorId(id);
    if (t is null) return Results.NotFound();
    if (t.IdEstadoTurno == 1 || t.IdEstadoTurno == 2)
    {
        var cuando = t.Fecha.Date + t.Hora;
        if (cuando < DateTime.Now.AddHours(24))
            return Results.BadRequest(new { mensaje = "Solo podés cancelar un turno con más de 24 horas de anticipación." });
    }
    t.IdEstadoTurno = 3; // Cancelado
    new TurnoDAO().Modificar(t);
    return Results.Ok();
});

app.MapPut("/turnos/{id}/rechazar", (int id) =>
{
    var dao = new TurnoDAO();
    var t = dao.ObtenerPorId(id);
    if (t is null) return Results.NotFound();
    t.IdEstadoTurno = 3; // Cancelado
    dao.Modificar(t);
    return Results.Ok(t);
});

app.MapPut("/turnos/{id}/confirmar", (int id, string? quien) =>
{
    var dao = new TurnoDAO();
    var t = dao.ObtenerPorId(id);
    if (t is null) return Results.NotFound();

    if ((quien == "paciente" || quien == "profesional") &&
        string.Equals(quien, t.CreadoPor, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { mensaje = "No podés confirmar un turno que vos mismo solicitaste. Debe confirmarlo la otra parte." });

    t.IdEstadoTurno = 2; // Confirmado
    dao.Modificar(t);

    try
    {
        var paciente    = new PacienteDAO().ObtenerPorId(t.IdPaciente);
        var profesional = new ProfesionalDAO().ObtenerPorId(t.IdProfesional);
        var nombrePaciente    = paciente?.Nombre ?? "Paciente";
        var nombreProfesional = $"{profesional?.Nombre} {profesional?.Apellido}".Trim();
        var fechaTexto = t.Fecha.ToString("dd/MM/yyyy");
        var horaTexto  = t.Hora.ToString(@"hh\:mm");
        var email = new EmailService(app.Configuration);

        if (!string.IsNullOrWhiteSpace(paciente?.Mail))
            email.EnviarConfirmacionTurno(paciente!.Mail, nombrePaciente, $"Dr/a. {nombreProfesional}", fechaTexto, horaTexto);
        if (!string.IsNullOrWhiteSpace(profesional?.Mail))
            email.EnviarConfirmacionTurno(profesional!.Mail, nombreProfesional, nombrePaciente, fechaTexto, horaTexto);
    }
    catch { }

    return Results.Ok(t);
});

app.MapGet("/profesionales/{id}/turnos", (int id) =>
    new TurnoDAO().ObtenerPorProfesional(id));

app.MapGet("/profesionales/{id}/pacientes", (int id) =>
{
    var idsPacientes = new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(id);
    var dao = new PacienteDAO();
    return idsPacientes.Select(idP => dao.ObtenerPorId(idP)).Where(p => p != null).ToList();
});

app.MapPost("/turnos", (Turno t) =>
{
    new TurnoDAO().Insertar(t);
    return Results.Created("/turnos", t);
});

static string? ValidarSolicitudTurno(Turno t)
{
    // RN-05: Anticipación mínima de 24 horas
    var cuando = t.Fecha.Date + t.Hora;
    if (cuando < DateTime.Now.AddHours(24))
        return "No podés solicitar un turno con menos de 24 horas de anticipación.";

    static string Norm(string? s) => new string((s ?? "")
        .ToLowerInvariant()
        .Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
        .ToArray());

    var diasEs = new Dictionary<DayOfWeek, string>
    {
        [DayOfWeek.Monday]    = "lunes",
        [DayOfWeek.Tuesday]   = "martes",
        [DayOfWeek.Wednesday] = "miercoles",
        [DayOfWeek.Thursday]  = "jueves",
        [DayOfWeek.Friday]    = "viernes",
        [DayOfWeek.Saturday]  = "sabado",
        [DayOfWeek.Sunday]    = "domingo"
    };

    var diaTurno = diasEs[t.Fecha.DayOfWeek];
    var agenda = new AgendaDAO().ObtenerPorProfesional(t.IdProfesional);
    var slot = agenda.FirstOrDefault(a => a.Disponible && Norm(a.Dia) == diaTurno
        && a.HoraInicio.HasValue && a.HoraFin.HasValue
        && t.Hora >= a.HoraInicio.Value && t.Hora < a.HoraFin.Value);
    if (slot is null)
        return "El horario seleccionado ya no está disponible en la agenda del profesional.";

    var dao = new TurnoDAO();
    if (dao.ExisteTurnoEnHorario(t.IdProfesional, t.Fecha, t.Hora))
        return "El horario seleccionado ya no está disponible. Elegí otro.";
    // RN-07: Sin turnos duplicados el mismo día con el mismo profesional
    if (dao.ExisteTurnoMismoDia(t.IdPaciente, t.IdProfesional, t.Fecha))
        return "Ya tenés un turno agendado con este profesional para esa fecha.";

    return null;
}

app.MapPost("/turnos/solicitar", (Turno t) =>
{
    var error = ValidarSolicitudTurno(t);
    if (error is not null) return Results.BadRequest(new { mensaje = error });

    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(
        "SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = 'Pendiente'", con);
    var result = cmd.ExecuteScalar();
    if (result is null) return Results.Problem("No existe el estado 'Pendiente' en EstadoTurno.");
    t.IdEstadoTurno = (int)result;
    t.CreadoPor = "paciente";
    new TurnoDAO().Insertar(t);
    return Results.Created("/turnos", t);
});

app.MapPost("/turnos/profesional-solicitar", (Turno t) =>
{
    var error = ValidarSolicitudTurno(t);
    if (error is not null) return Results.BadRequest(new { mensaje = error });

    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(
        "SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = 'Pendiente'", con);
    var result = cmd.ExecuteScalar();
    if (result is null) return Results.Problem("No existe el estado 'Pendiente' en EstadoTurno.");
    t.IdEstadoTurno = (int)result;
    t.CreadoPor = "profesional";
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
    var cierresValidos = new[] { "Alta", "Seguimiento", "Derivación" };
    if (string.IsNullOrWhiteSpace(e.TipoCierre) || !cierresValidos.Contains(e.TipoCierre))
        return Results.BadRequest(new { mensaje = "Debés indicar el tipo de cierre de la consulta: Alta, Seguimiento o Derivación." });

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
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
    var d = new DerivacionDAO().ObtenerPorId(id);
    if (d is null) return Results.NotFound();
    d.IdProfesionalDestino = req.IdProfesionalDestino;
    d.Estado = "Aceptada";
    new DerivacionDAO().Modificar(d);

    new TurnoDAO().CancelarTurnosPacienteProfesional(d.IdPaciente, d.IdProfesionalOrigen);
    var dao = new PacienteProfesionalDAO();
    dao.Desasignar(d.IdPaciente, d.IdProfesionalOrigen);
    dao.Asignar(d.IdPaciente, req.IdProfesionalDestino);

    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Aceptación Derivación", Entidad = "Derivacion", IdEntidad = id });
    return Results.Ok();
});

app.MapPut("/derivaciones/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;
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
    int? auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : null;

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

    new TurnoDAO().CancelarTurnosPacienteProfesional(req.IdPaciente, req.IdProfesionalOrigen);
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
    if (string.IsNullOrWhiteSpace(usuario.Mail)) return Results.BadRequest(new { mensaje = "El usuario no tiene mail registrado." });

    var dao = new RecuperacionPasswordDAO();
    if (dao.TienePendiente(usuario.IdUsuario))
        return Results.Conflict(new { mensaje = "Ya tenés una solicitud pendiente." });

    var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    var expiracion = DateTime.UtcNow.AddHours(1);
    dao.Insertar(usuario.IdUsuario, token, expiracion);

    try
    {
        new EmailService(app.Configuration).EnviarResetPassword(usuario.Mail, token);
    }
    catch
    {
        return Results.StatusCode(502);
    }

    return Results.Ok(new { mensaje = "Te enviamos un link a tu mail para restablecer la contraseña." });
});

app.MapPost("/recuperacion-password/reset", (ResetPasswordRequest req) =>
{
    var dao = new RecuperacionPasswordDAO();
    var rec = dao.ObtenerPorToken(req.Token);
    if (rec == null) return Results.NotFound(new { mensaje = "Link inválido o ya utilizado." });
    if (rec.TokenExpiracion < DateTime.UtcNow) return Results.BadRequest(new { mensaje = "El link expiró. Solicitá uno nuevo." });

    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(req.NuevaPassword)));

    var uDAO = new UsuarioDAO();
    var usuario = uDAO.ObtenerPorId(rec.IdUsuario);
    if (usuario == null) return Results.NotFound();
    usuario.PasswordHash = hash;
    uDAO.Modificar(usuario);
    uDAO.ResetearIntentos(rec.IdUsuario);
    dao.MarcarUsado(rec.IdRecuperacion);

    return Results.Ok(new { mensaje = "Contraseña actualizada correctamente." });
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

// TipoProfesional
app.MapGet("/tiposprofesional", () =>
    new TipoProfesionalDAO().ObtenerTodos());

app.MapGet("/tiposprofesional/{id}", (int id) =>
{
    var t = new TipoProfesionalDAO().ObtenerPorId(id);
    return t is null ? Results.NotFound() : Results.Ok(t);
});

app.MapPost("/tiposprofesional", (TipoProfesional t) =>
{
    t.Estado = "Activo";
    new TipoProfesionalDAO().Insertar(t);
    return Results.Created("/tiposprofesional", t);
});

app.MapPut("/tiposprofesional/{id}", (int id, TipoProfesional t) =>
{
    t.IdTipoProfesional = id;
    new TipoProfesionalDAO().Modificar(t);
    return Results.Ok(t);
});

// ProfesionalTipo
app.MapGet("/profesionales/{id}/tipos", (int id) =>
    new ProfesionalTipoDAO().ObtenerPorProfesional(id));

app.MapPost("/profesionales/{id}/tipos", (int id, ProfesionalTipo pt) =>
{
    pt.IdProfesional = id;
    new ProfesionalTipoDAO().Insertar(pt);
    return Results.Created($"/profesionales/{id}/tipos", pt);
});

app.MapDelete("/profesionales/{idProf}/tipos/{idTipo}", (int idProf, int idTipo) =>
{
    new ProfesionalTipoDAO().Eliminar(idProf, idTipo);
    return Results.NoContent();
});

// Servicio
app.MapGet("/servicios", () =>
    new ServicioDAO().ObtenerTodos());

app.MapPost("/servicios", (Servicio s) =>
{
    s.Estado = "Activo";
    new ServicioDAO().Insertar(s);
    return Results.Created("/servicios", s);
});

app.MapPut("/servicios/{id}", (int id, Servicio s) =>
{
    s.IdServicio = id;
    new ServicioDAO().Modificar(s);
    return Results.Ok(s);
});

// AgendaPropietario
app.MapGet("/agendas/{id}/propietarios", (int id) =>
    new AgendaPropietarioDAO().ObtenerPorAgenda(id));

app.MapPost("/agendas/{id}/propietarios", (int id, AgendaPropietario ap) =>
{
    ap.IdAgenda = id;
    new AgendaPropietarioDAO().Insertar(ap);
    return Results.Created($"/agendas/{id}/propietarios", ap);
});

app.MapDelete("/agendas/propietarios/{id}", (int id) =>
{
    new AgendaPropietarioDAO().Eliminar(id);
    return Results.NoContent();
});

// HorarioAgenda
app.MapGet("/agendas/{id}/horarios", (int id) =>
    new HorarioAgendaDAO().ObtenerPorAgenda(id));

app.MapPost("/agendas/{id}/horarios", (int id, HorarioAgenda h) =>
{
    h.IdAgenda = id;
    new HorarioAgendaDAO().Insertar(h);
    return Results.Created($"/agendas/{id}/horarios", h);
});

app.MapPut("/horarios/{id}", (int id, HorarioAgenda h) =>
{
    h.IdHorario = id;
    new HorarioAgendaDAO().Modificar(h);
    return Results.Ok(h);
});

app.MapDelete("/horarios/{id}", (int id) =>
{
    new HorarioAgendaDAO().Eliminar(id);
    return Results.NoContent();
});

// Festivos
app.MapGet("/festivos", () =>
    new FestivoDAO().ObtenerTodos());

app.MapPost("/festivos", (Festivo f) =>
{
    new FestivoDAO().Insertar(f);
    return Results.Created("/festivos", f);
});

app.MapDelete("/festivos/{id}", (int id) =>
{
    new FestivoDAO().Eliminar(id);
    return Results.NoContent();
});

// Bloqueos
app.MapGet("/profesionales/{id}/bloqueos", (int id) =>
    new BloqueosAgendaDAO().ObtenerPorProfesional(id));

app.MapPost("/profesionales/{id}/bloqueos", (int id, BloqueosAgenda b) =>
{
    b.IdProfesional = id;
    new BloqueosAgendaDAO().Insertar(b);
    return Results.Created($"/profesionales/{id}/bloqueos", b);
});

app.MapDelete("/bloqueos/{id}", (int id) =>
{
    new BloqueosAgendaDAO().Eliminar(id);
    return Results.NoContent();
});

// ─── Videollamadas seguras: sesiones de video ────────────────────────────────

static Dictionary<string, JsonElement>? PayloadDeBearer(HttpContext ctx, JwtService jwt, int graciaSegundos = 0)
{
    var header = ctx.Request.Headers["Authorization"].ToString();
    if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer "))
        return null;
    return jwt.Validar(header["Bearer ".Length..], graciaSegundos);
}

static (DateTime inicio, int duracion) VentanaTurno(Turno t, IConfiguration cfg)
{
    int porDefecto = int.TryParse(cfg["Sesion:DuracionTurnoDefaultMinutos"], out var d) ? d : 60;
    int duracion = t.DuracionMinutos
        ?? new ProfesionalDAO().ObtenerPorId(t.IdProfesional)?.DuracionTurnoDefault
        ?? porDefecto;
    var inicio = t.Fecha.Date + t.Hora;
    return (inicio, duracion);
}

static async Task<IResult> AbrirSesion(Turno t, string rol, int idUsuario, IConfiguration cfg, JwtService jwt, TurnCredentialsService turn, MeteredTurnService metered)
{
    var (inicio, duracion) = VentanaTurno(t, cfg);
    int margen = int.TryParse(cfg["Sesion:MargenIngresoMinutos"], out var m) ? m : 5;
    var ahora = DateTime.Now;

    if (ahora < inicio.AddMinutes(-margen))
        return Results.Json(new { mensaje = "Todavía no es horario de la sesión." }, statusCode: 403);

    var dao = new SesionVideoDAO();
    var sesion = dao.ObtenerOCrearPorTurno(t.IdTurno, inicio.AddMinutes(duracion), out var creada);

    if (sesion.Estado == "finalizada")
        return Results.Json(new { mensaje = "La sesión ya finalizó." }, statusCode: 403);
    if (ahora > sesion.FinProgramado)
        return Results.Json(new { mensaje = "La ventana de la sesión ya expiró." }, statusCode: 403);

    var eventoDao = new SesionVideoEventoDAO();
    if (creada)
        eventoDao.Insertar(new SesionVideoEvento { IdSesion = sesion.IdSesion, Tipo = "creada", Detalle = $"Turno {t.IdTurno}" });

    dao.MarcarActiva(sesion.IdSesion);
    eventoDao.Insertar(new SesionVideoEvento { IdSesion = sesion.IdSesion, Tipo = "ingreso", IdUsuario = idUsuario, Rol = rol });

    var token = jwt.GenerarSesion(idUsuario, rol, sesion.RoomId, t.IdTurno, sesion.IdSesion, sesion.FinProgramado);

    var iceServers = new List<IceServer>();
    var coturn = turn.GenerarIceServer(idUsuario);
    if (coturn != null) iceServers.Add(coturn);
    iceServers.AddRange(await metered.ObtenerAsync());

    bool consentimiento = new ConsentimientoDAO().ObtenerPorPaciente(t.IdPaciente).Any(c => c.Estado == "Activo");

    return Results.Ok(new
    {
        roomId = sesion.RoomId,
        token,
        idSesion = sesion.IdSesion,
        rol,
        finProgramado = sesion.FinProgramado,
        iceServers,
        consentimientoOk = consentimiento
    });
}

app.MapPost("/sesiones/turno/{idTurno}/join", async (int idTurno, HttpContext ctx) =>
{
    int maxSesion = int.TryParse(app.Configuration["RateLimit:Sesion"], out var ms) ? ms : 5;
    var lim = LimitarTasa(ctx, app.Configuration, "sesion-join", maxSesion);
    if (lim != null) return lim;

    var jwt = new JwtService(app.Configuration);
    var p = PayloadDeBearer(ctx, jwt);
    if (p == null) return Results.Unauthorized();

    var idUsuario = p["idUsuario"].GetInt32();
    var rol = p["rol"].GetString();

    var t = new TurnoDAO().ObtenerPorId(idTurno);
    if (t is null) return Results.NotFound();

    if (rol == "profesional")
    {
        var up = new UsuarioProfesionalDAO().ObtenerPorUsuario(idUsuario);
        if (up == null || up.IdProfesional != t.IdProfesional)
            return Results.Json(new { mensaje = "No estás asignado a este turno." }, statusCode: 403);
    }
    else if (rol == "paciente")
    {
        var up = new UsuarioPacienteDAO().ObtenerPorUsuario(idUsuario);
        if (up == null || up.IdPaciente != t.IdPaciente)
            return Results.Json(new { mensaje = "No estás asignado a este turno." }, statusCode: 403);
    }
    else return Results.Json(new { mensaje = "Rol no autorizado para la sesión." }, statusCode: 403);

    return await AbrirSesion(t, rol!, idUsuario, app.Configuration, jwt, new TurnCredentialsService(app.Configuration), new MeteredTurnService(app.Configuration));
});

app.MapPost("/sesiones/canjear-link", async (CanjearLinkRequest req, HttpContext ctx) =>
{
    int maxSesion = int.TryParse(app.Configuration["RateLimit:Sesion"], out var ms) ? ms : 5;
    var lim = LimitarTasa(ctx, app.Configuration, "sesion-canjear", maxSesion);
    if (lim != null) return lim;

    var dao = new SesionLinkTokenDAO();
    var link = dao.ObtenerPorToken(req.Token ?? "");
    if (link == null) return Results.NotFound(new { mensaje = "Link inválido." });
    if (link.Usado) return Results.BadRequest(new { mensaje = "Este link ya fue utilizado." });
    if (link.Expiracion < DateTime.Now) return Results.BadRequest(new { mensaje = "El link expiró." });
    if (!dao.MarcarUsadoSiNoUsado(link.IdLink))
        return Results.BadRequest(new { mensaje = "Este link ya fue utilizado." });

    var t = new TurnoDAO().ObtenerPorId(link.IdTurno);
    if (t is null) return Results.NotFound();

    var jwt = new JwtService(app.Configuration);
    return await AbrirSesion(t, link.Rol, link.IdUsuario ?? 0, app.Configuration, jwt, new TurnCredentialsService(app.Configuration), new MeteredTurnService(app.Configuration));
});

app.MapPost("/sesiones/{id}/refrescar-token", (int id, HttpContext ctx) =>
{
    var jwt = new JwtService(app.Configuration);
    var p = PayloadDeBearer(ctx, jwt, 300);
    if (p == null) return Results.Unauthorized();
    if (!p.TryGetValue("idSesion", out var sid) || sid.GetInt32() != id)
        return Results.Json(new { mensaje = "El token no corresponde a esta sesión." }, statusCode: 403);

    var sesion = new SesionVideoDAO().ObtenerPorId(id);
    if (sesion is null) return Results.NotFound();
    if (sesion.Estado == "finalizada" || DateTime.Now > sesion.FinProgramado)
        return Results.Json(new { mensaje = "La sesión no está activa." }, statusCode: 403);

    var token = jwt.GenerarSesion(p["idUsuario"].GetInt32(), p["rol"].GetString()!, sesion.RoomId, sesion.IdTurno, sesion.IdSesion, sesion.FinProgramado);
    return Results.Ok(new { token, finProgramado = sesion.FinProgramado });
});

app.MapPost("/sesiones/{id}/prolongar", (int id, ProlongarRequest req, HttpContext ctx) =>
{
    var jwt = new JwtService(app.Configuration);
    var p = PayloadDeBearer(ctx, jwt, 300);
    if (p == null) return Results.Unauthorized();
    if (p["rol"].GetString() != "profesional")
        return Results.Json(new { mensaje = "Solo el profesional puede prolongar la sesión." }, statusCode: 403);
    if (req.Minutos <= 0 || req.Minutos > 180)
        return Results.BadRequest(new { mensaje = "Cantidad de minutos inválida." });

    var sesion = new SesionVideoDAO().ObtenerPorId(id);
    if (sesion is null) return Results.NotFound();
    if (sesion.Estado == "finalizada") return Results.BadRequest(new { mensaje = "La sesión ya finalizó." });

    var t = new TurnoDAO().ObtenerPorId(sesion.IdTurno);
    var up = new UsuarioProfesionalDAO().ObtenerPorUsuario(p["idUsuario"].GetInt32());
    if (t is null || up == null || up.IdProfesional != t.IdProfesional)
        return Results.Json(new { mensaje = "No es tu sesión." }, statusCode: 403);

    var nuevoFin = sesion.FinProgramado.AddMinutes(req.Minutos);
    new SesionVideoDAO().ActualizarFinProgramado(id, nuevoFin);
    new SesionVideoEventoDAO().Insertar(new SesionVideoEvento
    {
        IdSesion = id, Tipo = "extension",
        Detalle = $"+{req.Minutos} min, nuevo fin {nuevoFin:yyyy-MM-dd HH:mm}",
        IdUsuario = p["idUsuario"].GetInt32(), Rol = "profesional"
    });
    return Results.Ok(new { finProgramado = nuevoFin });
});

app.MapPost("/sesiones/{id}/finalizar", (int id, FinalizarRequest req, HttpContext ctx) =>
{
    var jwt = new JwtService(app.Configuration);
    var p = PayloadDeBearer(ctx, jwt, 300);
    if (p == null) return Results.Unauthorized();
    if (p["rol"].GetString() != "profesional")
        return Results.Json(new { mensaje = "Solo el profesional puede finalizar la sesión." }, statusCode: 403);

    var sesion = new SesionVideoDAO().ObtenerPorId(id);
    if (sesion is null) return Results.NotFound();

    var t = new TurnoDAO().ObtenerPorId(sesion.IdTurno);
    var up = new UsuarioProfesionalDAO().ObtenerPorUsuario(p["idUsuario"].GetInt32());
    if (t is null || up == null || up.IdProfesional != t.IdProfesional)
        return Results.Json(new { mensaje = "No es tu sesión." }, statusCode: 403);

    new SesionVideoDAO().Finalizar(id, req.Motivo);
    new SesionVideoEventoDAO().Insertar(new SesionVideoEvento
    {
        IdSesion = id, Tipo = "finalizada", Detalle = req.Motivo,
        IdUsuario = p["idUsuario"].GetInt32(), Rol = "profesional"
    });
    return Results.Ok(new { mensaje = "Sesión finalizada." });
});

app.MapGet("/sesiones/{id}/estado", (int id, HttpContext ctx) =>
{
    var secreto = app.Configuration["Signaling:SecretoInterno"];
    bool modoLan = !bool.TryParse(app.Configuration["Sesion:ModoLan"], out var lan) || lan;
    if (string.IsNullOrEmpty(secreto))
    {
        if (!modoLan) return Results.Unauthorized();
    }
    else
    {
        var recibido = ctx.Request.Headers["X-Internal-Secret"].ToString();
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(recibido),
                System.Text.Encoding.UTF8.GetBytes(secreto)))
            return Results.Unauthorized();
    }

    var s = new SesionVideoDAO().ObtenerPorId(id);
    if (s is null) return Results.NotFound();
    bool activa = s.Estado != "finalizada" && DateTime.Now <= s.FinProgramado;
    return Results.Ok(new { estado = s.Estado, finProgramado = s.FinProgramado, activa, roomId = s.RoomId });
});

app.MapPost("/sesiones-emociones", (SesionEmocionesRequest req, HttpContext ctx) =>
{
    var jwt = new JwtService(app.Configuration);
    var p = PayloadDeBearer(ctx, jwt, 300);
    if (p == null) return Results.Unauthorized();
    if (p["rol"].GetString() != "profesional")
        return Results.Json(new { mensaje = "Solo el profesional registra el análisis." }, statusCode: 403);

    SesionVideo? sesion = null;
    if (p.TryGetValue("idSesion", out var sid))
        sesion = new SesionVideoDAO().ObtenerPorId(sid.GetInt32());
    if (sesion == null && !string.IsNullOrWhiteSpace(req.Sala))
        sesion = new SesionVideoDAO().ObtenerPorRoomId(req.Sala);

    if (sesion != null)
    {
        var t = new TurnoDAO().ObtenerPorId(sesion.IdTurno);
        if (t != null && !new ConsentimientoDAO().ObtenerPorPaciente(t.IdPaciente).Any(c => c.Estado == "Activo"))
            return Results.Json(new { mensaje = "No hay consentimiento activo del paciente para registrar el análisis." }, statusCode: 403);
    }

    var crypto = new CryptoService(app.Configuration);
    var resumenJson = req.Resumen.ValueKind == JsonValueKind.Undefined ? "{}" : req.Resumen.GetRawText();
    var historialJson = req.Historial.ValueKind == JsonValueKind.Undefined ? "[]" : req.Historial.GetRawText();

    new SesionEmocionesDAO().Insertar(new SesionEmociones
    {
        IdSesion = sesion?.IdSesion,
        IdProfesional = req.IdProfesional,
        RoomId = req.Sala,
        DuracionSegundos = req.Duracion,
        Muestras = req.Muestras,
        Resumen = crypto.Proteger(resumenJson),
        Historial = crypto.Proteger(historialJson)
    });

    if (sesion != null)
        new SesionVideoEventoDAO().Insertar(new SesionVideoEvento
        {
            IdSesion = sesion.IdSesion, Tipo = "emociones",
            Detalle = $"{req.Muestras} muestras", IdUsuario = p["idUsuario"].GetInt32(), Rol = "profesional"
        });

    return Results.Created("/sesiones-emociones", new { ok = true, cifrado = crypto.CifradoActivo });
});

app.MapPost("/sesiones/turno/{idTurno}/invitar", (int idTurno, HttpContext ctx) =>
{
    var jwt = new JwtService(app.Configuration);
    var p = PayloadDeBearer(ctx, jwt);
    if (p == null) return Results.Unauthorized();
    if (p["rol"].GetString() != "profesional")
        return Results.Json(new { mensaje = "Solo el profesional puede enviar invitaciones." }, statusCode: 403);

    var t = new TurnoDAO().ObtenerPorId(idTurno);
    if (t is null) return Results.NotFound();
    var up = new UsuarioProfesionalDAO().ObtenerPorUsuario(p["idUsuario"].GetInt32());
    if (up == null || up.IdProfesional != t.IdProfesional)
        return Results.Json(new { mensaje = "No es tu turno." }, statusCode: 403);

    try { new InvitacionSesionService(app.Configuration).EnviarParaTurno(t, up.IdUsuario); }
    catch { }

    return Results.Ok(new { mensaje = "Invitaciones enviadas." });
});

app.MapPut("/profesionales/{id}/duracion-turno", (int id, DuracionTurnoRequest req, HttpContext ctx) =>
{
    var jwt = new JwtService(app.Configuration);
    var p = PayloadDeBearer(ctx, jwt);
    if (p == null) return Results.Unauthorized();
    if (p["rol"].GetString() != "profesional")
        return Results.Json(new { mensaje = "Solo el profesional puede cambiar la duración." }, statusCode: 403);

    var up = new UsuarioProfesionalDAO().ObtenerPorUsuario(p["idUsuario"].GetInt32());
    if (up == null || up.IdProfesional != id)
        return Results.Json(new { mensaje = "No es tu perfil." }, statusCode: 403);
    if (req.Minutos < 10 || req.Minutos > 240)
        return Results.BadRequest(new { mensaje = "La duración debe estar entre 10 y 240 minutos." });

    new ProfesionalDAO().ActualizarDuracionDefault(id, req.Minutos);
    return Results.Ok(new { duracionTurnoDefault = req.Minutos });
});

app.Run();
