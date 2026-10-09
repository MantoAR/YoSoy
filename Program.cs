var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddSingleton<EmailService>();
builder.Services.AddMensajeria(builder.Configuration);
builder.Services.AddSingleton<IAsignacionRepositorio, AsignacionRepositorioDAO>();
builder.Services.AddSingleton<AsignacionService>();

// JWT (rama Cambios-Agus-jwt): reemplaza la identidad autodeclarada por X-Usuario-Id.
// La clave de firma no vive en ningún archivo versionado — JwtConfig.ClaveOFallar lee
// Jwt:Key de user-secrets (desarrollo) o de la variable de entorno Jwt__Key, y si falta
// tira un mensaje claro en vez de una excepción oscura más adelante.
builder.Services.AddSingleton<JwtEmisorService>();
builder.Services.AddSingleton<IdentidadActualService>();

const string EsquemaJwtOLegacy = "JwtOLegacy";
const string EsquemaLegacy = "Legacy";
builder.Services.AddAuthentication(EsquemaJwtOLegacy)
    .AddPolicyScheme(EsquemaJwtOLegacy, "JWT si hay Bearer, si no el header legacy", opt =>
    {
        // Mismo ClaimsPrincipal resulte de uno u otro esquema — ver IdentidadActualService,
        // que no necesita saber cuál de los dos lo autenticó.
        opt.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ")
                ? Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme
                : EsquemaLegacy;
    })
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(JwtConfig.ClaveOFallar(builder.Configuration))),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, LegacyHeaderAuthHandler>(EsquemaLegacy, _ => { });
builder.Services.AddAuthorization();

// Página pública de reserva (Fase 3a, Paso 3): límite por IP, ventana fija corta, en
// memoria — no hay estado compartido entre procesos y no lo necesita, es una sola
// instancia local. Se aplica al endpoint de disponibilidad y al de reserva (ambos
// alcanzables sin sesión) y a la página /reservar/{slug} en sí.
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = 429;
    opt.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync("""{"mensaje":"Demasiados pedidos. Probá de nuevo en un minuto."}""", token);
    };
    opt.AddPolicy("publico", httpContext => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1), PermitLimit = 20, QueueLimit = 0
        }));
});

var app = builder.Build();

// Falla rápido y claro al arrancar si falta la clave, en vez de un 500 confuso en el
// primer login — se resuelve el singleton una vez ahora mismo para forzar el chequeo.
app.Services.GetRequiredService<JwtEmisorService>();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Cierre de seguridad, Paso 7: antes estos headers solo salían en /public y /reservar
// (pensados para la superficie pública, Fase 3a). Pasan a ser globales — toda la app es
// candidata a quedar detrás de un proxy algún día, no solo esas dos rutas.
// X-Frame-Options: SAMEORIGIN, no DENY: la sala de video sigue embebida en un <iframe>
// (Pantalla_Profesional/js/nuevo_dashboard.js, sesion_video.html) desde el mismo origen —
// DENY la rompería. CSP queda afuera a propósito (ver abajo): hay mucho JS/CSS inline en
// PANTALLAS/ y una política restrictiva rompería pantallas; se deja anotado como mejora
// futura en CAMBIOS_AGUS.md.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    await next();
});

app.UsePantallaMensajeria();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "PANTALLAS")),
    RequestPath = "/pantallas",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

// Login
app.MapPost("/login", (LoginRequest req, JwtEmisorService jwtEmisor) =>
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

    // El token lleva el mismo idUsuario/idPaciente/idProfesional que ya devolvía esta
    // respuesta (contrato existente, solo se agrega el campo "token" — nada se quita).
    var up = new UsuarioPacienteDAO().ObtenerPorUsuario(usuario.IdUsuario);
    if (up != null)
        return Results.Ok(new { rol = "paciente", idUsuario = usuario.IdUsuario, idPaciente = up.IdPaciente,
            token = jwtEmisor.Emitir(usuario.IdUsuario, "paciente", idPaciente: up.IdPaciente) });

    var upro = new UsuarioProfesionalDAO().ObtenerPorUsuario(usuario.IdUsuario);
    if (upro != null)
        return Results.Ok(new { rol = "profesional", idUsuario = usuario.IdUsuario, idProfesional = upro.IdProfesional,
            token = jwtEmisor.Emitir(usuario.IdUsuario, "profesional", idProfesional: upro.IdProfesional) });

    return Results.Ok(new { rol = "configurador", idUsuario = usuario.IdUsuario,
        token = jwtEmisor.Emitir(usuario.IdUsuario, "configurador") });
});

// Especialidad
app.MapGet("/especialidades", () =>
    new EspecialidadDAO().ObtenerTodos());

app.MapGet("/especialidades/{id}", (int id) =>
{
    var e = new EspecialidadDAO().ObtenerPorId(id);
    return e is null ? Results.NotFound() : Results.Ok(e);
});

// Fase JWT: los catálogos se leen desde pantallas de varios roles (ej. "Mi perfil" del
// profesional lista especialidades), así que los GET quedan como estaban; solo las
// escrituras pasan a ser de Configurador.
app.MapPost("/especialidades", (Especialidad e) =>
{
    e.Estado = "Activo";
    e.IdEspecialidad = new EspecialidadDAO().Insertar(e);
    return Results.Created($"/especialidades", e);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/especialidades/{id}", (int id, Especialidad e) =>
{
    e.IdEspecialidad = id;
    new EspecialidadDAO().Modificar(e);
    return Results.Ok(e);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/especialidades/{id}", (int id) =>
{
    new EspecialidadDAO().Baja(id);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Rol
app.MapGet("/roles", () =>
    new RolDAO().ObtenerTodos())
    .RequireAuthorization();

app.MapGet("/roles/{id}", (int id) =>
{
    var r = new RolDAO().ObtenerPorId(id);
    return r is null ? Results.NotFound() : Results.Ok(r);
}).RequireAuthorization();

app.MapPost("/roles", (Rol r) =>
{
    r.Estado = "Activo";
    r.IdRol = new RolDAO().Insertar(r);
    return Results.Created($"/roles", r);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/roles/{id}", (int id, Rol r) =>
{
    r.IdRol = id;
    new RolDAO().Modificar(r);
    return Results.Ok(r);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/roles/{id}", (int id) =>
{
    new RolDAO().Baja(id);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// EstadoTurno
// Cierre de seguridad, Paso 4: las escrituras no tenían ningún chequeo (a diferencia de
// Especialidad/Rol, que ya eran Configurador-only) — se pareja al mismo criterio.
app.MapGet("/estadosturnos", () =>
    new EstadoTurnoDAO().ObtenerTodos())
    .RequireAuthorization();

app.MapGet("/estadosturnos/{id}", (int id) =>
{
    var et = new EstadoTurnoDAO().ObtenerPorId(id);
    return et is null ? Results.NotFound() : Results.Ok(et);
}).RequireAuthorization();

app.MapPost("/estadosturnos", (EstadoTurno et) =>
{
    et.Estado = "Activo";
    et.IdEstadoTurno = new EstadoTurnoDAO().Insertar(et);
    return Results.Created($"/estadosturnos", et);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/estadosturnos/{id}", (int id, EstadoTurno et) =>
{
    et.IdEstadoTurno = id;
    new EstadoTurnoDAO().Modificar(et);
    return Results.Ok(et);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/estadosturnos/{id}", (int id) =>
{
    new EstadoTurnoDAO().Baja(id);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// TipoConsentimiento (mismo criterio que EstadoTurno arriba)
app.MapGet("/tiposconsentimiento", () =>
    new TipoConsentimientoDAO().ObtenerTodos())
    .RequireAuthorization();

app.MapGet("/tiposconsentimiento/{id}", (int id) =>
{
    var tc = new TipoConsentimientoDAO().ObtenerPorId(id);
    return tc is null ? Results.NotFound() : Results.Ok(tc);
}).RequireAuthorization();

app.MapPost("/tiposconsentimiento", (TipoConsentimiento tc) =>
{
    tc.Estado = "Activo";
    tc.IdTipoConsentimiento = new TipoConsentimientoDAO().Insertar(tc);
    return Results.Created($"/tiposconsentimiento", tc);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/tiposconsentimiento/{id}", (int id, TipoConsentimiento tc) =>
{
    tc.IdTipoConsentimiento = id;
    new TipoConsentimientoDAO().Modificar(tc);
    return Results.Ok(tc);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/tiposconsentimiento/{id}", (int id) =>
{
    new TipoConsentimientoDAO().Baja(id);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// ObraSocial (mismo criterio que EstadoTurno/TipoConsentimiento arriba)
app.MapGet("/obrassociales", () =>
    new ObraSocialDAO().ObtenerTodos())
    .RequireAuthorization();

app.MapGet("/obrassociales/{id}", (int id) =>
{
    var o = new ObraSocialDAO().ObtenerPorId(id);
    return o is null ? Results.NotFound() : Results.Ok(o);
}).RequireAuthorization();

app.MapPost("/obrassociales", (ObraSocial o) =>
{
    o.IdObraSocial = new ObraSocialDAO().Insertar(o);
    return Results.Created("/obrassociales", o);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/obrassociales/{id}", (int id, ObraSocial o) =>
{
    o.IdObraSocial = id;
    new ObraSocialDAO().Modificar(o);
    return Results.Ok(o);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Tutor: datos personales de un tutor/guardián (no es un catálogo de referencia como
// Especialidad). Cierre de seguridad, Paso 4: sin ningún chequeo antes; sin uso hoy por
// ninguna pantalla real de PANTALLAS/ (solo por mockup.html, anterior a esta rama).
app.MapGet("/tutores", () =>
    new TutorDAO().ObtenerTodos())
    .RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapGet("/tutores/{id}", (int id) =>
{
    var t = new TutorDAO().ObtenerPorId(id);
    return t is null ? Results.NotFound() : Results.Ok(t);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapPost("/tutores", (Tutor t) =>
{
    t.IdTutor = new TutorDAO().Insertar(t);
    return Results.Created("/tutores", t);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapPut("/tutores/{id}", (int id, Tutor t) =>
{
    t.IdTutor = id;
    new TutorDAO().Modificar(t);
    return Results.Ok(t);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

// Usuario
// Administración de usuarios (Fase JWT): solo Configurador, de punta a punta.
app.MapGet("/usuarios", () =>
    new UsuarioDAO().ObtenerTodos().Select(u => new { u.IdUsuario, u.Username, u.Estado, u.Mail }))
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/usuarios/{id}", (int id) =>
{
    var u = new UsuarioDAO().ObtenerPorId(id);
    return u is null ? Results.NotFound() : Results.Ok(new { u.IdUsuario, u.Username, u.Estado, u.Mail });
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPost("/usuarios", (Usuario u, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    u.Estado = "Activo";
    u.PasswordHash = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(u.PasswordHash)));
    u.IdUsuario = new UsuarioDAO().Insertar(u);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Usuario", Entidad = "Usuario", Detalle = u.Username });
    return Results.Created("/usuarios", new { u.IdUsuario, u.Username, u.Estado, u.Mail });
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/usuarios/{id}", (int id, Usuario u) =>
{
    u.IdUsuario = id;
    if (!string.IsNullOrEmpty(u.PasswordHash))
        u.PasswordHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(u.PasswordHash)));
    new UsuarioDAO().Modificar(u);
    return Results.Ok(new { u.IdUsuario, u.Username, u.Estado, u.Mail });
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/usuarios/{id}", (int id, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    new UsuarioDAO().Baja(id);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Baja Usuario", Entidad = "Usuario", IdEntidad = id });
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Paciente
// Cierre de seguridad, Paso 4: antes nada de esto tenía chequeo — el volcado completo
// (todos los pacientes), la ficha de cualquiera por id, y hasta PUT/DELETE/alta de
// cualquier paciente estaban abiertos sin sesión. POST sigue público (registro).
app.MapGet("/pacientes", () =>
    new PacienteDAO().ObtenerTodos())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/pacientes/{id}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != id)
        return Results.Json(new { mensaje = "Solo podés ver tu propia ficha." }, statusCode: 403);
    var p = new PacienteDAO().ObtenerPorId(id);
    return p is null ? Results.NotFound() : Results.Ok(p);
}).RequireAuthorization();

app.MapPost("/pacientes", (Paciente p) =>
{
    p.Estado = "Activo";
    p.IdPaciente = new PacienteDAO().Insertar(p);
    return Results.Created("/pacientes", p);
});

app.MapPut("/pacientes/{id}", (int id, Paciente p, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != id)
        return Results.Json(new { mensaje = "Solo podés editar tu propia ficha." }, statusCode: 403);
    p.IdPaciente = id;
    new PacienteDAO().Modificar(p);
    return Results.Ok(p);
}).RequireAuthorization();

app.MapDelete("/pacientes/{id}", (int id, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    new PacienteDAO().Baja(id);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Baja Paciente", Entidad = "Paciente", IdEntidad = id });
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/pacientes/{id}/alta", (int id, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var p = new PacienteDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Activo";
    new PacienteDAO().Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Paciente", Entidad = "Paciente", IdEntidad = id });
    return Results.Ok(p);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Profesional
// Cierre de seguridad, Paso 4: el volcado completo (todos los profesionales, con mail y
// teléfono) solo lo usa hoy la tabla del Configurador (confirmado en PANTALLAS/) — pasa a
// Configurador-only. La ficha por id y "Mi perfil" (PUT) exigen sesión; PUT, además, solo
// a su propio dueño o al Configurador (antes, sin chequeo, cualquiera podía reescribir el
// perfil de cualquier profesional). POST sigue público (registro).
app.MapGet("/profesionales", () =>
    new ProfesionalDAO().ObtenerTodos())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/profesionales/{id}", (int id) =>
{
    var p = new ProfesionalDAO().ObtenerPorId(id);
    return p is null ? Results.NotFound() : Results.Ok(p);
}).RequireAuthorization();

app.MapPost("/profesionales", (Profesional p, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    p.Estado = "Pendiente";
    p.IdProfesional = new ProfesionalDAO().Insertar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Profesional", Entidad = "Profesional", Detalle = $"{p.Nombre} {p.Apellido}" });
    return Results.Created("/profesionales", p);
});

app.MapPut("/profesionales/{id}", (int id, Profesional p, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (!yo.EsConfigurador && yo.IdProfesional != id)
        return Results.Json(new { mensaje = "Solo podés editar tu propio perfil." }, statusCode: 403);
    p.IdProfesional = id;
    new ProfesionalDAO().Modificar(p);
    return Results.Ok(p);
}).RequireAuthorization();

app.MapDelete("/profesionales/{id}", (int id, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    new ProfesionalDAO().Baja(id);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Baja Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Dar de alta/aprobar/rechazar un profesional es administración (Fase JWT: Configurador).
// PUT /profesionales/{id} (arriba, sin tocar) sigue abierto: lo usa el propio profesional
// desde "Mi perfil" — no está en el alcance de este grupo.
app.MapPut("/profesionales/{id}/alta", (int id, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var p = new ProfesionalDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Activo";
    new ProfesionalDAO().Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(p);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/profesionales/solicitudes", () =>
    new ProfesionalDAO().ObtenerTodos().Where(p => p.Estado == "Pendiente").ToList())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/profesionales/{id}/aprobar", (int id, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var p = new ProfesionalDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Activo";
    new ProfesionalDAO().Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Aprobación Solicitud Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(p);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/profesionales/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var p = new ProfesionalDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Rechazado";
    new ProfesionalDAO().Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Rechazo Solicitud Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(p);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Link público de reserva (Fase 3a, Paso 3): el profesional prende/apaga el suyo; el
// administrador puede apagar el de cualquiera. Mismo patrón de identidad que Casos/Grupos
// (X-Usuario-Id sin firmar — TODO(JWT)). Auditado solo con ids, sin texto libre.
app.MapPut("/profesionales/{id}/link-publico", (int id, ActivarLinkPublicoRequest req, HttpContext ctx) =>
{
    var (esAdministrador, idProfesionalDeSesion) = AutorizacionCasos.Resolver(ctx);
    if (!esAdministrador && idProfesionalDeSesion != id)
        return Results.Json(new { mensaje = "No podés activar el link público de otro profesional." }, statusCode: 403);

    var dao = new ProfesionalDAO();
    if (dao.ObtenerPorId(id) is null) return Results.NotFound();
    var p = dao.ActivarLinkPublico(id, req.Activo);

    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = req.Activo ? "Activar Link Público" : "Desactivar Link Público", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(new { slug = p.Slug, linkPublicoActivo = p.LinkPublicoActivo });
}).RequireAuthorization();

// ── Superficie pública: página y API de reserva por link (Fase 3a, Paso 3) ──────────
// Sin sesión, sin cookies, sin ids internos en la respuesta. 404 idéntico si el slug no
// existe o si el link está apagado (nunca se revela cuál de las dos cosas pasó).
app.MapGet("/reservar/{slug}", (string slug) =>
{
    if (new ProfesionalDAO().ObtenerPorSlugActivo(slug) is null) return Results.NotFound();
    return Results.File(Path.Combine(builder.Environment.ContentRootPath, "PANTALLAS", "Pantalla_Publica", "reservar.html"), "text/html");
}).RequireRateLimiting("publico");

app.MapGet("/public/profesionales/{slug}/disponibilidad", (string slug, string? dias, TurnoReglasService reglas) =>
{
    int rango = 14;
    if (dias != null && (!int.TryParse(dias, out rango) || rango < 1 || rango > 60))
        return Results.Json(new { mensaje = "El parámetro 'dias' tiene que ser un número entre 1 y 60." }, statusCode: 400);

    var p = new ProfesionalDAO().ObtenerPorSlugActivo(slug);
    if (p is null) return Results.NotFound();

    var especialidad = new EspecialidadDAO().ObtenerPorId(p.IdEspecialidad)?.Nombre;
    return Results.Ok(new
    {
        nombre = $"{p.Nombre} {p.Apellido}".Trim(),
        especialidad,
        horariosLibres = reglas.HorariosLibresDelProfesional(p.IdProfesional, rango, 500)
    });
}).RequireRateLimiting("publico");

app.MapPost("/public/profesionales/{slug}/reservar", (string slug, ReservaPublicaRequest req, INotificadorTurnos notificador, TurnoReglasService reglas, HttpContext ctx) =>
{
    var p = new ProfesionalDAO().ObtenerPorSlugActivo(slug);
    if (p is null) return Results.NotFound();

    var resultado = reglas.Solicitar(req.IdPaciente, p.IdProfesional, req.Momento, req.Motivo, creadoPor: "paciente");
    if (!resultado.Regla.Ok) return resultado.Regla.ComoConflicto();
    notificador.TurnoSolicitado(resultado.IdTurno!.Value);

    // Auditoría (Fase 3a, Paso 4): solo ids y acción, nada clínico — igual que el resto
    // de LogAuditoria. X-Usuario-Id lo manda YS.fetch solo si el paciente tiene sesión
    // (que la tiene: así llegó hasta acá), igual que cualquier otro pedido autenticado.
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Turno Solicitado (Link Público)", Entidad = "Turno", IdEntidad = resultado.IdTurno });
    return Results.Created($"/public/profesionales/{slug}/reservar", new { idTurno = resultado.IdTurno });
}).RequireRateLimiting("publico");

// Agenda
app.MapGet("/agendas", () =>
    new AgendaDAO().ObtenerTodos())
    .RequireAuthorization();

app.MapGet("/agendas/{id}", (int id) =>
{
    var a = new AgendaDAO().ObtenerPorId(id);
    return a is null ? Results.NotFound() : Results.Ok(a);
}).RequireAuthorization();

app.MapGet("/profesionales/{id}/agenda", (int id) =>
    new AgendaDAO().ObtenerPorProfesional(id));

// Vista previa de "así te ven los pacientes" en Mis horarios de trabajo (Fase 3a, Paso 2):
// los próximos horarios libres calculados igual que la reserva real, sin paciente (no aplica
// RN-07). Autenticado (lo pide la propia pantalla del profesional) — la versión pública,
// con sus propios límites, es /public/profesionales/{slug}/disponibilidad (Paso 3).
app.MapGet("/profesionales/{id}/horarios-libres", (int id, int? dias, TurnoReglasService reglas, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx);
    if (!yo!.EsConfigurador && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tu propia vista previa." }, statusCode: 403);
    var rango = Math.Clamp(dias ?? 14, 1, 90);
    return Results.Ok(reglas.HorariosLibresDelProfesional(id, rango, 500));
}).RequireAuthorization();

// Cierre de seguridad, Paso 3: el profesional solo administra sus propias franjas; el
// Configurador, las de cualquiera. Se aplica igual a /agendas y a su alias /agenda (abajo),
// que comparten el mismo AgendaDAO.
app.MapPost("/agendas", (Agenda a, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (!yo.EsConfigurador) a.IdProfesional = yo.IdProfesional!.Value;
    a.IdAgenda = new AgendaDAO().Insertar(a);
    return Results.Created("/agendas", a);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapPut("/agendas/{id}", (int id, Agenda a, HttpContext ctx, IdentidadActualService identidad) =>
{
    var dao = new AgendaDAO();
    var existente = dao.ObtenerPorId(id);
    if (existente is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx)!;
    if (!yo.EsConfigurador && yo.IdProfesional != existente.IdProfesional)
        return Results.Json(new { mensaje = "Ese horario no es tuyo." }, statusCode: 403);

    a.IdAgenda = id;
    if (!yo.EsConfigurador) a.IdProfesional = existente.IdProfesional;
    dao.Modificar(a);
    return Results.Ok(a);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapDelete("/agendas/{id}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var dao = new AgendaDAO();
    var existente = dao.ObtenerPorId(id);
    if (existente is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx)!;
    if (!yo.EsConfigurador && yo.IdProfesional != existente.IdProfesional)
        return Results.Json(new { mensaje = "Ese horario no es tuyo." }, statusCode: 403);
    dao.Eliminar(id);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

// Alias sin 's' para compatibilidad con el dashboard
app.MapGet("/agenda/{id}", (int id) =>
{
    var a = new AgendaDAO().ObtenerPorId(id);
    return a is null ? Results.NotFound() : Results.Ok(a);
}).RequireAuthorization();

app.MapPost("/agenda", (Agenda a, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (!yo.EsConfigurador) a.IdProfesional = yo.IdProfesional!.Value;
    a.IdAgenda = new AgendaDAO().Insertar(a);
    return Results.Created("/agenda", a);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapPut("/agenda/{id}", (int id, Agenda a, HttpContext ctx, IdentidadActualService identidad) =>
{
    var dao = new AgendaDAO();
    var existente = dao.ObtenerPorId(id);
    if (existente is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx)!;
    if (!yo.EsConfigurador && yo.IdProfesional != existente.IdProfesional)
        return Results.Json(new { mensaje = "Ese horario no es tuyo." }, statusCode: 403);

    a.IdAgenda = id;
    if (!yo.EsConfigurador) a.IdProfesional = existente.IdProfesional;
    dao.Modificar(a);
    return Results.Ok(a);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapDelete("/agenda/{id}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var dao = new AgendaDAO();
    var existente = dao.ObtenerPorId(id);
    if (existente is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx)!;
    if (!yo.EsConfigurador && yo.IdProfesional != existente.IdProfesional)
        return Results.Json(new { mensaje = "Ese horario no es tuyo." }, statusCode: 403);
    dao.Eliminar(id);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

// Turno
// Fase JWT: el dump completo de /turnos (sin filtrar por nadie) queda para Configurador.
app.MapGet("/turnos", () =>
    new TurnoDAO().ObtenerTodos())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/turnos/{id}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var t = new TurnoDAO().ObtenerPorId(id);
    if (t is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx);
    if (yo is { EsPaciente: true } && yo.IdPaciente != t.IdPaciente) return Results.Json(new { mensaje = "Ese turno no es tuyo." }, statusCode: 403);
    if (yo is { EsProfesional: true } && yo.IdProfesional != t.IdProfesional) return Results.Json(new { mensaje = "Ese turno no es tuyo." }, statusCode: 403);
    return Results.Ok(t);
}).RequireAuthorization();

// Un paciente solo ve los propios; cualquier profesional o el administrador puede ver los
// de un paciente (parte de su atención) — la restricción real es "no otro paciente".
app.MapGet("/pacientes/{id}/turnos", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx);
    if (yo is { EsPaciente: true } && yo.IdPaciente != id) return Results.Json(new { mensaje = "Solo podés ver tus propios turnos." }, statusCode: 403);
    return Results.Ok(new TurnoDAO().ObtenerPorPaciente(id));
}).RequireAuthorization();

app.MapGet("/pacientes/{id}/turnos-detalle", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx);
    if (yo is { EsPaciente: true } && yo.IdPaciente != id) return Results.Json(new { mensaje = "Solo podés ver tus propios turnos." }, statusCode: 403);
    return Results.Ok(new TurnoDetalleDAO().ObtenerPorPaciente(id));
}).RequireAuthorization();

app.MapGet("/pacientes/{id}/proximo-turno", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != id)
        return Results.Json(new { mensaje = "Solo podés ver tu propio turno." }, statusCode: 403);
    var t = new TurnoDetalleDAO().ObtenerProximoPorPaciente(id);
    return t is null ? Results.NotFound() : Results.Ok(t);
}).RequireAuthorization();

app.MapGet("/pacientes/{id}/mis-profesionales", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx);
    if (yo is { EsPaciente: true } && yo.IdPaciente != id) return Results.Json(new { mensaje = "Solo podés ver tus propios profesionales." }, statusCode: 403);
    return Results.Ok(new PacienteProfesionalDAO().ObtenerProfesionalesDePaciente(id));
}).RequireAuthorization();

// La lista de pacientes de un profesional es privada de ese profesional (no de "cualquier
// profesional", a diferencia de los turnos de un paciente arriba).
app.MapGet("/profesionales/{id}/pacientes", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx);
    if (!yo!.EsConfigurador && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propios pacientes." }, statusCode: 403);
    var ids = new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(id);
    var dao = new PacienteDAO();
    return Results.Ok(ids.Select(idPac => dao.ObtenerPorId(idPac)).Where(p => p != null).ToList());
}).RequireAuthorization();

// Cierre de seguridad, Paso 4: asignar/desasignar el equipo tratante de un paciente es
// administrativo — mismo criterio que /derivaciones/* y /casos/{id}/reasignar, que ya son
// Configurador-only (antes, esto no tenía ningún chequeo).
app.MapPost("/pacientes/{id}/profesionales", (int id, PacienteProfesional pp) =>
{
    new PacienteProfesionalDAO().Asignar(id, pp.IdProfesional);
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/pacientes/{idPac}/profesionales/{idProf}", (int idPac, int idProf) =>
{
    new PacienteProfesionalDAO().Desasignar(idPac, idProf);
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Lo llaman tanto el paciente (Pantalla_Paciente) como el profesional/configurador
// (Pantalla_Profesional) por esta misma ruta. Solo el paciente manda idPaciente (agregado
// en la Fase 3a, Paso 1: un query param nuevo, no cambia el contrato de quien no lo manda)
// y solo para él aplican las reglas (dueño del turno + RN-05) vía TurnoReglasService.Cancelar.
// Fase JWT, agregado: si cancela un profesional (no idPaciente, no Configurador), el turno
// tiene que ser suyo — antes cualquier profesional podía cancelar el turno de cualquier
// otro, sin chequeo ("ver o cancelar un turno ajeno").
app.MapPut("/turnos/{id}/cancelar", (int id, int? idPaciente, INotificadorTurnos notificador, TurnoReglasService reglas, HttpContext ctx, IdentidadActualService identidad) =>
{
    if (idPaciente is not null)
    {
        var regla = reglas.Cancelar(id, idPaciente.Value);
        if (!regla.Ok) return regla.ComoConflicto();
        notificador.TurnoCancelado(id);
        return Results.Ok();
    }

    var t = new TurnoDAO().ObtenerPorId(id);
    if (t is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx);
    if (yo is { EsProfesional: true } && yo.IdProfesional != t.IdProfesional)
        return Results.Json(new { mensaje = "Ese turno no es tuyo." }, statusCode: 403);
    t.IdEstadoTurno = 3; // Cancelado
    new TurnoDAO().Modificar(t);
    notificador.TurnoCancelado(id);
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/profesionales/{id}/turnos", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx);
    if (!yo!.EsConfigurador && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propios turnos." }, statusCode: 403);
    return Results.Ok(new TurnoDAO().ObtenerPorProfesional(id));
}).RequireAuthorization();

// Cierre de seguridad, Paso 3: sin uso por ningún frontend (se confirmó buscando en todo
// PANTALLAS/) y bypasea todas las reglas de TurnoReglasService (RN-01/05/07, inserción a
// prueba de doble reserva) — los caminos reales son /turnos/solicitar (paciente) y
// /turnos/profesional-solicitar (profesional). Se apaga en vez de protegerlo: dejarlo
// abierto a cualquier rol autenticado no tendría ningún uso legítimo y solo preservaría un
// atajo que ignora esas reglas.
app.MapPost("/turnos", () => Results.StatusCode(StatusCodes.Status410Gone));

// Solicitud de turno desde el paciente — estado siempre Pendiente. Pasa por
// TurnoReglasService.Solicitar (RN-01/02/05/07 + inserción a prueba de doble reserva),
// igual que ya hace el bot — antes insertaba directo, sin ninguna regla (Fase 3a, Paso 1).
app.MapPost("/turnos/solicitar", (Turno t, INotificadorTurnos notificador, TurnoReglasService reglas, HttpContext ctx, IdentidadActualService identidad) =>
{
    // Cierre de seguridad, Paso 3: el paciente que pide el turno es el del token, nunca el
    // que venga en el cuerpo (antes se confiaba en t.IdPaciente tal cual llegaba).
    t.IdPaciente = identidad.Resolver(ctx)!.IdPaciente!.Value;
    var resultado = reglas.Solicitar(t.IdPaciente, t.IdProfesional, t.Fecha.Date + t.Hora, t.Motivo);
    if (!resultado.Regla.Ok) return resultado.Regla.ComoConflicto();
    notificador.TurnoSolicitado(resultado.IdTurno!.Value);
    // Misma forma que antes (el Turno enviado), más el id real que ya no quedaba en 0.
    t.IdTurno = resultado.IdTurno.Value;
    t.CreadoPor = "paciente";
    return Results.Created("/turnos", t);
}).RequireAuthorization(p => p.RequireRole(Roles.Paciente));

app.MapPut("/turnos/{id}", (int id, Turno t, INotificadorTurnos notificador, TurnoReglasService reglas, HttpContext ctx, IdentidadActualService identidad) =>
{
    var anterior = new TurnoDAO().ObtenerPorId(id);
    if (anterior is null) return Results.NotFound();
    // Cierre de seguridad, Paso 3: el profesional solo puede modificar sus propios turnos
    // (antes, sin chequeo, podía modificar cualquiera); el paciente no entra por acá, su
    // propio flujo es /cancelar y /confirmar-paciente.
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsProfesional: true } && yo.IdProfesional != anterior.IdProfesional)
        return Results.Json(new { mensaje = "Ese turno no es tuyo." }, statusCode: 403);

    t.IdTurno = id;
    var rn02 = reglas.ValidarCambioDeEstado(anterior, t);
    if (!rn02.Ok) return rn02.ComoConflicto();
    new TurnoDAO().Modificar(t);
    notificador.CambioDeEstado(id, anterior.IdEstadoTurno);
    return Results.Ok(t);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

// Cierre de seguridad, Paso 4: sin uso por ningún frontend (el cancelar real es
// /turnos/{id}/cancelar); Configurador-only por el mismo criterio que el resto de las bajas.
app.MapDelete("/turnos/{id}", (int id) =>
{
    new TurnoDAO().Baja(id);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Consentimiento (genérico — distinto del RN-02 de telemedicina en ConsentimientoEndpoints.cs)
// Cierre de seguridad, Paso 4: antes sin ningún chequeo. El volcado completo y las
// escrituras quedan en Configurador (igual que historia clínica: nada que ninguna pantalla
// real de PANTALLAS/ use hoy); la lectura por paciente, para su propio dueño o el equipo.
app.MapGet("/consentimientos", () =>
    new ConsentimientoDAO().ObtenerTodos())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/consentimientos/{id}", (int id) =>
{
    var c = new ConsentimientoDAO().ObtenerPorId(id);
    return c is null ? Results.NotFound() : Results.Ok(c);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/pacientes/{id}/consentimientos", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != id)
        return Results.Json(new { mensaje = "Solo podés ver tus propios consentimientos." }, statusCode: 403);
    return Results.Ok(new ConsentimientoDAO().ObtenerPorPaciente(id));
}).RequireAuthorization();

app.MapPost("/consentimientos", (Consentimiento c) =>
{
    c.Estado = "Activo";
    c.IdConsentimiento = new ConsentimientoDAO().Insertar(c);
    return Results.Created("/consentimientos", c);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/consentimientos/{id}", (int id, Consentimiento c) =>
{
    c.IdConsentimiento = id;
    new ConsentimientoDAO().Modificar(c);
    return Results.Ok(c);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/consentimientos/{id}", (int id) =>
{
    new ConsentimientoDAO().Baja(id);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// HistoriaClinica
// Fase JWT: historia clínica/evoluciones/eventos de crisis/sesiones clínicas — lo más
// sensible de toda la app (Ley 25.326). Ningún paciente los llama directo (no hay
// pantalla de Pantalla_Paciente que los use — confirmado buscando en todo ese directorio),
// así que el mínimo seguro y sin riesgo de romper nada es: Profesional o Configurador,
// nunca Paciente ni anónimo. Queda pendiente, anotado, el paso más fino ("solo el
// profesional asociado a ESE paciente", no cualquier profesional) — hoy cualquier
// profesional autenticado puede ver la de cualquier paciente, un alcance más amplio que
// Casos (que sí filtra por asignación) pero bastante más chico que "público".
// Cierre de seguridad, Paso 2: "asociado" = equipo tratante vigente del paciente
// (PacienteProfesionalDAO), mismo criterio que ya usan los chats — ver
// Services/AutorizacionClinica.cs. Antes, cualquier profesional autenticado veía la
// historia clínica de cualquier paciente (más ancho que Casos, que sí filtraba por
// asignación); ahora solo el profesional asociado a ESE paciente, o el Configurador.
// Los volcados sin filtro (ObtenerTodos, sin paciente) solo los usa la tabla genérica
// del Configurador (confirmado revisando PANTALLAS/): quedan Configurador-only en vez
// de inventar un "todos los historiales de mis pacientes" que ninguna pantalla pide hoy.
var soloClinico = (RouteHandlerBuilder b) => b.RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));
var soloConfigurador = (RouteHandlerBuilder b) => b.RequireAuthorization(p => p.RequireRole(Roles.Configurador));

soloConfigurador(app.MapGet("/historiasclinicas", () =>
    new HistoriaClinicaDAO().ObtenerTodos()));

soloClinico(app.MapGet("/historiasclinicas/{id}", (int id, HttpContext ctx) =>
{
    var h = new HistoriaClinicaDAO().ObtenerPorId(id);
    if (h is null || !AutorizacionClinica.PuedePaciente(ctx, h.IdPaciente)) return Results.NotFound();
    return Results.Ok(h);
}));

soloClinico(app.MapGet("/pacientes/{id}/historiaclinica", (int id, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedePaciente(ctx, id)) return Results.NotFound();
    // Auditoría de acceso a datos clínicos (Paso 6): una vez por apertura de la ficha
    // (esta ruta no la llama ningún polling, solo el "abrir la ficha clínica" real).
    AuditoriaIds.Registrar(ctx, "Apertura de historia clínica", "HistoriaClinica", id);
    var h = new HistoriaClinicaDAO().ObtenerPorPaciente(id);
    return h is null ? Results.NotFound() : Results.Ok(h);
}));

soloClinico(app.MapPost("/historiasclinicas", (HistoriaClinica h, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedePaciente(ctx, h.IdPaciente))
        return Results.Json(new { mensaje = "Ese paciente no está asociado a tu atención." }, statusCode: 403);
    h.IdHistoria = new HistoriaClinicaDAO().Insertar(h);
    return Results.Created("/historiasclinicas", h);
}));

// Evolucion (la pertenencia se resuelve por el paciente de la Historia, Evolucion no tiene
// IdPaciente directo)
soloConfigurador(app.MapGet("/evoluciones", () =>
    new EvolucionDAO().ObtenerTodos()));

soloClinico(app.MapGet("/evoluciones/{id}", (int id, HttpContext ctx) =>
{
    var e = new EvolucionDAO().ObtenerPorId(id);
    var idPaciente = e is null ? null : new HistoriaClinicaDAO().ObtenerPorId(e.IdHistoria)?.IdPaciente;
    if (idPaciente is null || !AutorizacionClinica.PuedePaciente(ctx, idPaciente.Value)) return Results.NotFound();
    return Results.Ok(e);
}));

soloClinico(app.MapGet("/historiasclinicas/{id}/evoluciones", (int id, HttpContext ctx) =>
{
    var idPaciente = new HistoriaClinicaDAO().ObtenerPorId(id)?.IdPaciente;
    if (idPaciente is null || !AutorizacionClinica.PuedePaciente(ctx, idPaciente.Value)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Apertura de evoluciones clínicas", "Evolucion", idPaciente.Value);
    return Results.Ok(new EvolucionDAO().ObtenerPorHistoria(id));
}));

soloClinico(app.MapPost("/evoluciones", (Evolucion e, HttpContext ctx) =>
{
    var idPaciente = new HistoriaClinicaDAO().ObtenerPorId(e.IdHistoria)?.IdPaciente;
    if (idPaciente is null) return Results.NotFound();
    if (!AutorizacionClinica.PuedePaciente(ctx, idPaciente.Value))
        return Results.Json(new { mensaje = "Ese paciente no está asociado a tu atención." }, statusCode: 403);
    e.IdEvolucion = new EvolucionDAO().Insertar(e);
    return Results.Created("/evoluciones", e);
}));

soloClinico(app.MapPut("/evoluciones/{id}", (int id, Evolucion e, HttpContext ctx) =>
{
    var existente = new EvolucionDAO().ObtenerPorId(id);
    var idPaciente = existente is null ? null : new HistoriaClinicaDAO().ObtenerPorId(existente.IdHistoria)?.IdPaciente;
    if (idPaciente is null || !AutorizacionClinica.PuedePaciente(ctx, idPaciente.Value)) return Results.NotFound();
    e.IdEvolucion = id;
    new EvolucionDAO().Modificar(e);
    return Results.Ok(e);
}));

// Derivacion
// "Solicitudes de cambio" (Derivacion, el mecanismo viejo — no CasoDerivacion de la Fase
// 2): lo aceptan/rechazan desde la pantalla de administrador (abrirModalDeriv en
// Pantalla_Configurador), nunca el profesional directo. Fase JWT: todo Configurador.
app.MapGet("/derivaciones", () =>
    new DerivacionDAO().ObtenerTodos())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/derivaciones/pendientes", () =>
    new DerivacionDetalleDAO().ObtenerPendientes())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/derivaciones/detalle", () =>
    new DerivacionDetalleDAO().ObtenerTodosConDetalle())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/derivaciones/{id}", (int id) =>
{
    var d = new DerivacionDAO().ObtenerPorId(id);
    return d is null ? Results.NotFound() : Results.Ok(d);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/pacientes/{id}/derivaciones", (int id) =>
    new DerivacionDAO().ObtenerPorPaciente(id))
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPost("/derivaciones", (Derivacion d) =>
{
    d.Fecha  = DateTime.Now;
    d.Estado = "Pendiente";
    d.IdDerivacion = new DerivacionDAO().Insertar(d);
    return Results.Created("/derivaciones", d);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/derivaciones/{id}", (int id, Derivacion d) =>
{
    d.IdDerivacion = id;
    new DerivacionDAO().Modificar(d);
    return Results.Ok(d);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/derivaciones/{id}/aceptar", (int id, AceptarDerivacionRequest req, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var d = new DerivacionDAO().ObtenerPorId(id);
    if (d is null) return Results.NotFound();
    d.IdProfesionalDestino = req.IdProfesionalDestino;
    d.Estado = "Aceptada";
    new DerivacionDAO().Modificar(d);

    var dao = new PacienteProfesionalDAO();
    dao.Desasignar(d.IdPaciente, d.IdProfesionalOrigen);
    dao.Asignar(d.IdPaciente, req.IdProfesionalDestino);

    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Aceptación Derivación", Entidad = "Derivacion", IdEntidad = id });
    SincronizarCasoPorCambioDeProfesional(d.IdPaciente, req.IdProfesionalDestino, auditor);
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/derivaciones/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var d = new DerivacionDAO().ObtenerPorId(id);
    if (d is null) return Results.NotFound();
    d.Estado = "Rechazada";
    new DerivacionDAO().Modificar(d);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Rechazo Derivación", Entidad = "Derivacion", IdEntidad = id });
    return Results.Ok(d);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Cambio directo de profesional (configurador): registra derivación aceptada + reasigna turnos futuros
app.MapPost("/derivaciones/cambio-directo", (CambioProfesionalRequest req, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;

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
    SincronizarCasoPorCambioDeProfesional(req.IdPaciente, req.IdProfesionalDestino, auditor);
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Fase 2, aditivo: si el paciente tiene un Caso abierto, la solicitud de cambio de
// profesional (Derivacion/"Solicitudes de cambio", sin relación con CasoDerivacion) lo
// actualiza también y deja su propio rastro en HistorialAsignacion. Si no tiene un caso
// abierto (pacientes de antes de la Fase 2, o ya Cerrado), no hace nada: no se
// reconstruye historial viejo. No cambia el comportamiento visible de arriba.
void SincronizarCasoPorCambioDeProfesional(int idPaciente, int idProfesionalNuevo, int? idUsuario)
{
    var caso = new CasoDAO().ObtenerAbiertoPorPaciente(idPaciente);
    if (caso is null) return;
    new CasoDAO().ActualizarEstadoYAsignacion(caso.IdCaso, caso.Estado, caso.IdGrupoActual, idProfesionalNuevo);
    new HistorialAsignacionDAO().Insertar(new HistorialAsignacion
    {
        IdCaso = caso.IdCaso, IdProfesional = idProfesionalNuevo, Motivo = "SolicitudCambio",
        IdRealizadoPor = idUsuario
    });
}

app.MapGet("/profesionales/activos/especialidad/{idEsp}", (int idEsp) =>
    new ProfesionalDAO().ObtenerPorEspecialidad(idEsp)
        .Where(p => p.Estado == "Activo").ToList())
    .RequireAuthorization();

// EventoCrisis
soloConfigurador(app.MapGet("/eventoscrisis", () =>
    new EventoCrisisDAO().ObtenerTodos()));

soloClinico(app.MapGet("/eventoscrisis/{id}", (int id, HttpContext ctx) =>
{
    var e = new EventoCrisisDAO().ObtenerPorId(id);
    if (e is null || !AutorizacionClinica.PuedePaciente(ctx, e.IdPaciente)) return Results.NotFound();
    return Results.Ok(e);
}));

soloClinico(app.MapGet("/pacientes/{id}/eventoscrisis", (int id, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedePaciente(ctx, id)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Apertura de eventos de crisis", "EventoCrisis", id);
    return Results.Ok(new EventoCrisisDAO().ObtenerPorPaciente(id));
}));

soloClinico(app.MapPost("/eventoscrisis", (EventoCrisis e, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedePaciente(ctx, e.IdPaciente))
        return Results.Json(new { mensaje = "Ese paciente no está asociado a tu atención." }, statusCode: 403);
    e.IdEvento = new EventoCrisisDAO().Insertar(e);
    return Results.Created("/eventoscrisis", e);
}));

soloClinico(app.MapPut("/eventoscrisis/{id}", (int id, EventoCrisis e, HttpContext ctx) =>
{
    var existente = new EventoCrisisDAO().ObtenerPorId(id);
    if (existente is null || !AutorizacionClinica.PuedePaciente(ctx, existente.IdPaciente)) return Results.NotFound();
    e.IdEvento = id;
    new EventoCrisisDAO().Modificar(e);
    return Results.Ok(e);
}));

// InteraccionIA: interacciones con la IA de apoyo emocional — contenido tan sensible como
// una evolución clínica (ver README, "Interacciones con IA de apoyo emocional"). Cierre de
// seguridad, Paso 2: pasa a soloClinico + AutorizacionClinica (antes, sin ningún chequeo,
// ni rol ni pertenencia); sin uso hoy por ninguna pantalla real de PANTALLAS/ (solo por
// mockup.html, anterior a esta rama), así que no hay riesgo de romper nada.
soloConfigurador(app.MapGet("/interaccionesia", () =>
    new InteraccionIADAO().ObtenerTodos()));

soloClinico(app.MapGet("/interaccionesia/{id}", (int id, HttpContext ctx) =>
{
    var i = new InteraccionIADAO().ObtenerPorId(id);
    if (i is null || !AutorizacionClinica.PuedePaciente(ctx, i.IdPaciente)) return Results.NotFound();
    return Results.Ok(i);
}));

soloClinico(app.MapGet("/pacientes/{id}/interaccionesia", (int id, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedePaciente(ctx, id)) return Results.NotFound();
    return Results.Ok(new InteraccionIADAO().ObtenerPorPaciente(id));
}));

soloClinico(app.MapPost("/interaccionesia", (InteraccionIA i, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedePaciente(ctx, i.IdPaciente))
        return Results.Json(new { mensaje = "Ese paciente no está asociado a tu atención." }, statusCode: 403);
    i.IdInteraccion = new InteraccionIADAO().Insertar(i);
    return Results.Created("/interaccionesia", i);
}));

// Cobertura
app.MapGet("/coberturas", () =>
    new CoberturaDAO().ObtenerTodos())
    .RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapGet("/coberturas/{id}", (int id) =>
{
    var c = new CoberturaDAO().ObtenerPorId(id);
    return c is null ? Results.NotFound() : Results.Ok(c);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapGet("/pacientes/{id}/coberturas", (int id) =>
    new CoberturaDAO().ObtenerPorPaciente(id))
    .RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapPost("/coberturas", (Cobertura c) =>
{
    c.IdCobertura = new CoberturaDAO().Insertar(c);
    return Results.Created("/coberturas", c);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/coberturas/{id}", (int id, Cobertura c) =>
{
    c.IdCobertura = id;
    new CoberturaDAO().Modificar(c);
    return Results.Ok(c);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/coberturas/{id}", (int id) =>
{
    new CoberturaDAO().Baja(id);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// PacienteTutor (mismo criterio que Tutor arriba: sin uso hoy fuera de mockup.html)
app.MapGet("/pacientes/{id}/tutores", (int id) =>
    new PacienteTutorDAO().ObtenerPorPaciente(id))
    .RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapPost("/pacientetutores", (PacienteTutor pt) =>
{
    new PacienteTutorDAO().Insertar(pt);
    return Results.Created("/pacientetutores", pt);
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

app.MapDelete("/pacientetutores/{idPaciente}/{idTutor}", (int idPaciente, int idTutor) =>
{
    new PacienteTutorDAO().Eliminar(idPaciente, idTutor);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

// UsuarioRol
// De aquí hasta /auditoria: tablas de asociación Usuario<->Rol/Paciente/Profesional y el
// log — plomería administrativa, ninguna pantalla de paciente/profesional las llama
// directo. Fase JWT: todo Configurador.
app.MapGet("/usuarios/{id}/roles", (int id) =>
    new UsuarioRolDAO().ObtenerPorUsuario(id))
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPost("/usuarioroles", (UsuarioRol ur) =>
{
    new UsuarioRolDAO().Insertar(ur);
    return Results.Created("/usuarioroles", ur);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/usuarioroles/{idUsuario}/{idRol}", (int idUsuario, int idRol) =>
{
    new UsuarioRolDAO().Eliminar(idUsuario, idRol);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// UsuarioPaciente
app.MapGet("/usuarios/{id}/paciente", (int id) =>
{
    var up = new UsuarioPacienteDAO().ObtenerPorUsuario(id);
    return up is null ? Results.NotFound() : Results.Ok(up);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPost("/usuariopacientes", (UsuarioPaciente up) =>
{
    new UsuarioPacienteDAO().Insertar(up);
    return Results.Created("/usuariopacientes", up);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/usuariopacientes/{idUsuario}", (int idUsuario) =>
{
    new UsuarioPacienteDAO().Eliminar(idUsuario);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// UsuarioProfesional
app.MapGet("/usuarios/{id}/profesional", (int id) =>
{
    var up = new UsuarioProfesionalDAO().ObtenerPorUsuario(id);
    return up is null ? Results.NotFound() : Results.Ok(up);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPost("/usuarioprofesionales", (UsuarioProfesional up) =>
{
    new UsuarioProfesionalDAO().Insertar(up);
    return Results.Created("/usuarioprofesionales", up);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapDelete("/usuarioprofesionales/{idUsuario}", (int idUsuario) =>
{
    new UsuarioProfesionalDAO().Eliminar(idUsuario);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/auditoria", (int pagina = 1, int cantidad = 50) =>
    new LogAuditoriaDAO().ObtenerPaginado(pagina, cantidad))
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

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
    new RecuperacionPasswordDAO().ObtenerPendientes())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

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
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/recuperacion-password/{id}/rechazar", (int id) =>
{
    var dao = new RecuperacionPasswordDAO();
    if (dao.ObtenerIdUsuario(id) == null) return Results.NotFound();
    dao.Rechazar(id);
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/recuperacion-password/estado/{username}", (string username) =>
{
    var rec = new RecuperacionPasswordDAO().ObtenerUltimaPorUsername(username);
    if (rec == null) return Results.NotFound(new { mensaje = "No hay solicitudes para este usuario." });

    if (rec.Estado == "Aceptada")
        return Results.Ok(new { estado = rec.Estado, passwordTemporal = rec.PasswordTemporal });

    return Results.Ok(new { estado = rec.Estado, passwordTemporal = (string)null });
});

// SesionClinica
soloClinico(app.MapPost("/sesiones-clinicas", (SesionClinica s, HttpContext ctx) =>
{
    // Mismo criterio que turnos (Paso 3): si quien crea es profesional, la sesión queda a
    // su propio nombre, nunca al de otro id enviado en el cuerpo.
    var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
    if (!esAdmin) s.IdProfesional = idPropio!.Value;
    if (!AutorizacionClinica.PuedePaciente(ctx, s.IdPaciente))
        return Results.Json(new { mensaje = "Ese paciente no está asociado a tu atención." }, statusCode: 403);
    s.FechaHora = DateTime.Now;
    int id = new SesionClinicaDAO().Insertar(s);
    s.IdSesion = id;
    return Results.Created("/sesiones-clinicas", s);
}));

soloClinico(app.MapGet("/pacientes/{id}/sesiones-clinicas", (int id, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedePaciente(ctx, id)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Apertura de sesiones clínicas", "SesionClinica", id);
    return Results.Ok(new SesionClinicaDAO().ObtenerPorPaciente(id));
}));

soloClinico(app.MapGet("/profesionales/{id}/sesiones-clinicas", (int id, HttpContext ctx) =>
{
    var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
    if (!esAdmin && idPropio != id) return Results.Json(new { mensaje = "Solo podés ver tus propias sesiones." }, statusCode: 403);
    return Results.Ok(new SesionClinicaDAO().ObtenerPorProfesional(id));
}));


// Reporte: atenciones por profesional
// Cierre de seguridad, Paso 4: antes no exigía nada, ni rol ni identidad — cualquiera podía
// bajar nombre, DNI y NHC de pacientes. Reusa AutorizacionReporte (mismo alcance que la
// versión HTML, ver CB-04 abajo) en vez de un chequeo nuevo.
app.MapGet("/reportes/atenciones", (int? idProfesional, HttpContext ctx) =>
{
    var (autorizado, idPermitido, motivo) = AutorizacionReporte.ResolverAlcance(ctx, idProfesional);
    if (!autorizado) return Results.Json(new { mensaje = motivo }, statusCode: 403);
    idProfesional = idPermitido;

    using var con = Conexion.ObtenerConexion();
    con.Open();
    var sql = @"
        SELECT 
            p.Nombre,
            p.Dni,
            hc.IdHistoria AS NHC,
            sc.FechaHora,
            sc.TipoCierre,
            prof.Nombre + ' ' + prof.Apellido AS NombreProfesional,
            prof.IdProfesional
        FROM SesionClinica sc
        INNER JOIN Paciente p ON p.IdPaciente = sc.IdPaciente
        LEFT JOIN HistoriaClinica hc ON hc.IdPaciente = sc.IdPaciente
        INNER JOIN Profesional prof ON prof.IdProfesional = sc.IdProfesional
        " + (idProfesional.HasValue ? "WHERE sc.IdProfesional = @IdProf" : "") + @"
        ORDER BY sc.FechaHora DESC";

    var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, con);
    if (idProfesional.HasValue)
        cmd.Parameters.AddWithValue("@IdProf", idProfesional.Value);

    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            nombre            = reader["Nombre"].ToString(),
            apellido          = "",
            dni               = reader["Dni"].ToString(),
            nhc               = reader["NHC"] == DBNull.Value ? (int?)null : (int)reader["NHC"],
            fecha             = ((DateTime)reader["FechaHora"]).ToString("dd/MM/yyyy"),
            hora              = ((DateTime)reader["FechaHora"]).ToString("HH:mm"),
            tipoCierre        = reader["TipoCierre"] == DBNull.Value ? "" : reader["TipoCierre"].ToString(),
            nombreProfesional = reader["NombreProfesional"].ToString(),
            idProfesional     = (int)reader["IdProfesional"]
        });
    }
    return Results.Ok(lista);
}).RequireAuthorization();

// ── INICIO reporte HTML (feature/agustin-reporte-html-interactivo) ──
// CB-04 cerrado (Cierre de seguridad, Paso 4): idProfesional ya se valida contra el
// usuario autenticado — ver AutorizacionReporte.ResolverAlcance, ahora basado en
// IdentidadActualService (JWT) en vez de X-Usuario-Id sin firmar.
// Datos expuestos: nombre, DNI y NHC de pacientes (Ley 25.326) — por eso exige sesión.
app.MapGet("/reportes/atenciones/html", (int? idProfesional, HttpContext ctx) =>
{
    var (autorizado, idPermitido, motivo) = AutorizacionReporte.ResolverAlcance(ctx, idProfesional);
    if (!autorizado) return Results.Json(new { mensaje = motivo }, statusCode: 403);
    var datos  = new ReporteAtencionesDAO().Obtener(idPermitido);
    var html   = ReporteHtmlBuilder.Construir(datos, idPermitido);
    var nombre = $"Atenciones_por_Profesional_{DateTime.Now:yyyyMMdd_HHmm}.html";
    return Results.File(System.Text.Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", nombre);
}).RequireAuthorization();
// ── FIN reporte HTML ──


// ── Informe de Admision ─────────────────────────────────────
// Cierre de seguridad, Paso 4: lo envía el propio paciente desde su panel (confirmado en
// PANTALLAS/Pantalla_Paciente/js/dashboard.js) — el IdPaciente es el del token, nunca el
// que venga en el cuerpo; el Configurador puede completarlo en nombre de alguien (admisión
// asistida), igual que ya puede un profesional crear turnos en nombre de un paciente.
app.MapPost("/informes-admision", (InformeAdmision i, AsignacionService asignacion, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (!yo.EsConfigurador) i.IdPaciente = yo.IdPaciente!.Value;

    var id = new InformeAdmisionDAO().Insertar(i);
    i.IdInforme = id;

    // Caso nuevo para el seguimiento de la Fase 2: aditivo, no cambia esta respuesta ni
    // el resto del flujo de admisión. Si la especialidad solicitada tiene una regla de
    // derivación activa, sugiere grupo y profesional; si no, queda "Nuevo" sin grupo,
    // en la cola del administrador (CAMBIOS_AGUS.md, sección Fase 2).
    var idCaso = new CasoDAO().Insertar(new Caso { IdPaciente = i.IdPaciente, Estado = "Nuevo", Origen = "Admision", IdInforme = id });
    var idGrupo = new ReglaDerivacionDAO().ObtenerGrupoPorEspecialidad(i.IdEspecialidad);
    if (idGrupo is not null)
        asignacion.SugerirDerivacion(idCaso, idGrupo.Value, "Regla", idCreadoPor: null);

    return Results.Created($"/informes-admision/{id}", i);
}).RequireAuthorization(p => p.RequireRole(Roles.Paciente, Roles.Configurador));

// Sin uso directo por ninguna pantalla (confirmado en PANTALLAS/): queda para el
// Configurador, igual que /pacientes o /profesionales en bloque.
app.MapGet("/informes-admision", (string? estado) =>
    new InformeAdmisionDAO().ObtenerTodos(estado))
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/informes-admision/{id}", (int id) =>
{
    var i = new InformeAdmisionDAO().ObtenerPorId(id);
    return i is null ? Results.NotFound() : Results.Ok(i);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapGet("/pacientes/{id}/informe-admision", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != id)
        return Results.Json(new { mensaje = "Solo podés ver tu propio informe de admisión." }, statusCode: 403);
    var i = new InformeAdmisionDAO().ObtenerPorPaciente(id);
    return i is null ? Results.NotFound() : Results.Ok(i);
}).RequireAuthorization();

// Sin uso directo por ninguna pantalla: Configurador, mismo criterio que el resto de este
// bloque administrativo.
app.MapPut("/informes-admision/{id}/estado", (int id, string estado) =>
{
    new InformeAdmisionDAO().CambiarEstado(id, estado);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));


// ── Admision: endpoints para el configurador ────────────────
app.MapGet("/informes-admision-detalle", () =>
{
    // (Configurador-only: ver .RequireAuthorization() al final de este endpoint.)
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT ia.IdInforme, ia.IdPaciente, p.Nombre AS NombrePaciente, p.Dni, p.FechaNacimiento,
               e.Nombre AS Especialidad, ia.MotivoConsulta, ia.TratamientoAnterior, ia.TratamientoAnteriorDetalle,
               ia.RecibeMedicacion, ia.MedicacionDetalle, ia.GustosIntereses, ia.PreferenciasAtencion,
               ia.TipoEntrevista, ia.RedApoyo, ia.ObjetivosPaciente, ia.Estado, ia.FechaCreacion,
               ia.FechaEntrevista, ia.ObservacionesEntrevista, ia.IdProfesionalDerivado, ia.CodigoSala, ia.MotivoRechazo,
               prof.Nombre + ' ' + ISNULL(prof.Apellido, '') AS NombreProfesionalDerivado
        FROM InformeAdmision ia
        INNER JOIN Paciente p ON p.IdPaciente = ia.IdPaciente
        INNER JOIN Especialidad e ON e.IdEspecialidad = ia.IdEspecialidad
        LEFT JOIN Profesional prof ON prof.IdProfesional = ia.IdProfesionalDerivado
        ORDER BY ia.FechaCreacion DESC", con);
    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            idInforme               = (int)reader["IdInforme"],
            idPaciente              = (int)reader["IdPaciente"],
            nombrePaciente          = reader["NombrePaciente"].ToString(),
            dni                     = reader["Dni"].ToString(),
            fechaNacimiento         = reader["FechaNacimiento"] == DBNull.Value ? null : ((DateTime)reader["FechaNacimiento"]).ToString("dd/MM/yyyy"),
            especialidad            = reader["Especialidad"].ToString(),
            motivoConsulta          = reader["MotivoConsulta"] == DBNull.Value ? "" : reader["MotivoConsulta"].ToString(),
            tratamientoAnterior     = reader["TratamientoAnterior"] != DBNull.Value && (bool)reader["TratamientoAnterior"],
            tratamientoAnteriorDetalle = reader["TratamientoAnteriorDetalle"] == DBNull.Value ? "" : reader["TratamientoAnteriorDetalle"].ToString(),
            recibeMedicacion        = reader["RecibeMedicacion"] != DBNull.Value && (bool)reader["RecibeMedicacion"],
            medicacionDetalle       = reader["MedicacionDetalle"] == DBNull.Value ? "" : reader["MedicacionDetalle"].ToString(),
            gustosIntereses         = reader["GustosIntereses"] == DBNull.Value ? "" : reader["GustosIntereses"].ToString(),
            preferenciasAtencion    = reader["PreferenciasAtencion"] == DBNull.Value ? "" : reader["PreferenciasAtencion"].ToString(),
            tipoEntrevista          = reader["TipoEntrevista"] == DBNull.Value ? "" : reader["TipoEntrevista"].ToString(),
            redApoyo                = reader["RedApoyo"] == DBNull.Value ? "" : reader["RedApoyo"].ToString(),
            objetivosPaciente       = reader["ObjetivosPaciente"] == DBNull.Value ? "" : reader["ObjetivosPaciente"].ToString(),
            estado                  = reader["Estado"].ToString(),
            fechaCreacion           = ((DateTime)reader["FechaCreacion"]).ToString("dd/MM/yyyy HH:mm"),
            fechaEntrevista         = reader["FechaEntrevista"] == DBNull.Value ? null : ((DateTime)reader["FechaEntrevista"]).ToString("dd/MM/yyyy HH:mm"),
            observacionesEntrevista = reader["ObservacionesEntrevista"] == DBNull.Value ? "" : reader["ObservacionesEntrevista"].ToString(),
            idProfesionalDerivado   = reader["IdProfesionalDerivado"] == DBNull.Value ? (int?)null : (int)reader["IdProfesionalDerivado"],
            codigoSala              = reader["CodigoSala"] == DBNull.Value ? "" : reader["CodigoSala"].ToString(),
            motivoRechazo           = reader["MotivoRechazo"] == DBNull.Value ? "" : reader["MotivoRechazo"].ToString(),
            nombreProfesionalDerivado = reader["NombreProfesionalDerivado"] == DBNull.Value ? "" : reader["NombreProfesionalDerivado"].ToString()
        });
    }
    return Results.Ok(lista);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/informes-admision/{id}/entrevista", (int id, EntrevistaRequest req) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();

    // Generar codigo de sala unico para la entrevista
    var codigoSala = "ADM-" + id + "-" + DateTime.Now.Ticks.ToString("x").ToUpper();

    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE InformeAdmision
        SET FechaEntrevista = @Fecha, ObservacionesEntrevista = @Obs, Estado = 'En entrevista', CodigoSala = @Sala
        WHERE IdInforme = @Id;
        SELECT IdPaciente FROM InformeAdmision WHERE IdInforme = @Id;", con);
    cmd.Parameters.AddWithValue("@Fecha", req.Fecha);
    cmd.Parameters.AddWithValue("@Obs", (object)req.Observaciones ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Sala", codigoSala);
    cmd.Parameters.AddWithValue("@Id", id);
    var idPacObj = cmd.ExecuteScalar();

    if (idPacObj != null)
    {
        var fechaLegible = req.Fecha.ToString("dd/MM/yyyy") + " a las " + req.Fecha.ToString("HH:mm") + " hs";
        var cmdNotif = new Microsoft.Data.SqlClient.SqlCommand(@"
            INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link)
            VALUES (@IdPac, @Titulo, @Mensaje, @Link)", con);
        cmdNotif.Parameters.AddWithValue("@IdPac", (int)idPacObj);
        cmdNotif.Parameters.AddWithValue("@Titulo", "Entrevista de admision programada");
        cmdNotif.Parameters.AddWithValue("@Mensaje", "Tu entrevista de admision fue programada para el " + fechaLegible + ". Codigo de sala: " + codigoSala);
        cmdNotif.Parameters.AddWithValue("@Link", codigoSala);
        cmdNotif.ExecuteNonQuery();
    }

    return Results.Ok(new { codigoSala });
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// ── Notificaciones ──────────────────────────────────────────
// Cierre de seguridad, Paso 4: antes sin auth ni chequeo de dueño — cualquiera podía leer
// (o marcar como leído) el aviso de cualquier paciente, con título y mensaje completos.
app.MapGet("/pacientes/{id}/notificaciones", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != id)
        return Results.Json(new { mensaje = "Solo podés ver tus propias notificaciones." }, statusCode: 403);

    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT IdNotificacion, Titulo, Mensaje, Link, Leida, FechaCreacion
        FROM Notificacion WHERE IdPaciente = @Id ORDER BY FechaCreacion DESC", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            idNotificacion = (int)reader["IdNotificacion"],
            titulo         = reader["Titulo"] == DBNull.Value ? "" : reader["Titulo"].ToString(),
            mensaje        = reader["Mensaje"] == DBNull.Value ? "" : reader["Mensaje"].ToString(),
            link           = reader["Link"] == DBNull.Value ? null : reader["Link"].ToString(),
            leida          = reader["Leida"] != DBNull.Value && (bool)reader["Leida"],
            fecha          = ((DateTime)reader["FechaCreacion"]).ToString("dd/MM/yyyy HH:mm")
        });
    }
    return Results.Ok(lista);
}).RequireAuthorization();

app.MapPut("/notificaciones/{id}/leida", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var yo = identidad.Resolver(ctx)!;
    if (yo.EsPaciente)
    {
        var chequeo = new Microsoft.Data.SqlClient.SqlCommand("SELECT IdPaciente FROM Notificacion WHERE IdNotificacion = @Id", con);
        chequeo.Parameters.AddWithValue("@Id", id);
        var idPacienteObj = chequeo.ExecuteScalar();
        if (idPacienteObj is null) return Results.NotFound();
        if ((int)idPacienteObj != yo.IdPaciente) return Results.Json(new { mensaje = "Esa notificación no es tuya." }, statusCode: 403);
    }
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(
        "UPDATE Notificacion SET Leida = 1 WHERE IdNotificacion = @Id", con);
    cmd.Parameters.AddWithValue("@Id", id);
    cmd.ExecuteNonQuery();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPut("/informes-admision/{id}/derivar", (int id, DerivarRequest req, HttpContext ctx, AsignacionService asignacion) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();

    // Obtener el paciente del informe
    var cmdGet = new Microsoft.Data.SqlClient.SqlCommand(
        "SELECT IdPaciente FROM InformeAdmision WHERE IdInforme = @Id", con);
    cmdGet.Parameters.AddWithValue("@Id", id);
    var idPacObj = cmdGet.ExecuteScalar();
    if (idPacObj == null) return Results.NotFound();
    int idPac = (int)idPacObj;

    // Actualizar el informe
    var cmdUpd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE InformeAdmision
        SET IdProfesionalDerivado = @IdProf, ObservacionesEntrevista = ISNULL(@Obs, ObservacionesEntrevista),
            Estado = 'Derivado', MotivoRechazo = NULL
        WHERE IdInforme = @Id", con);
    cmdUpd.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmdUpd.Parameters.AddWithValue("@Obs", (object)req.Observaciones ?? DBNull.Value);
    cmdUpd.Parameters.AddWithValue("@Id", id);
    cmdUpd.ExecuteNonQuery();

    // Vincular paciente con profesional con estado 'Nueva'
    var cmdVinc = new Microsoft.Data.SqlClient.SqlCommand(@"
        IF EXISTS (SELECT 1 FROM PacienteProfesional WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf)
            UPDATE PacienteProfesional SET EstadoAsignacion = 'Nueva', FechaAsignacion = GETDATE()
            WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf
        ELSE
            INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, FechaAsignacion, EstadoAsignacion)
            VALUES (@IdPac, @IdProf, GETDATE(), 'Nueva')", con);
    cmdVinc.Parameters.AddWithValue("@IdPac", idPac);
    cmdVinc.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmdVinc.ExecuteNonQuery();

    // Primera evolucion en la HC a nombre del admisionista (se muestra en azul)
    var cmdEvo = new Microsoft.Data.SqlClient.SqlCommand(@"
        DECLARE @IdHistoria INT = (SELECT TOP 1 IdHistoria FROM HistoriaClinica WHERE IdPaciente = @IdPac);
        IF @IdHistoria IS NULL
        BEGIN
            INSERT INTO HistoriaClinica (IdPaciente, FechaApertura) VALUES (@IdPac, GETDATE());
            SET @IdHistoria = SCOPE_IDENTITY();
        END
        DECLARE @IdAdm INT = (SELECT TOP 1 IdProfesional FROM Profesional WHERE Matricula = 'ADM-001');
        DECLARE @NombreProf NVARCHAR(200) = (SELECT TOP 1 Nombre + ' ' + ISNULL(Apellido,'') FROM Profesional WHERE IdProfesional = @IdProf);
        IF @IdAdm IS NOT NULL
            INSERT INTO Evolucion (IdHistoria, IdProfesional, Fecha, Diagnostico, Observaciones, PlanTratamiento, TipoCierre)
            VALUES (@IdHistoria, @IdAdm, GETDATE(), 'Evaluacion de admision',
                    ISNULL(@Obs, 'Entrevista de admision realizada.'),
                    'Derivado a: ' + ISNULL(@NombreProf, ''), N'Derivación')", con);
    cmdEvo.Parameters.AddWithValue("@IdPac", idPac);
    cmdEvo.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmdEvo.Parameters.AddWithValue("@Obs", (object)req.Observaciones ?? DBNull.Value);
    cmdEvo.ExecuteNonQuery();

    // Sincroniza el Caso de esta admisión (Fase 2, aditivo): no cambia nada de lo de
    // arriba ni la respuesta. Si había una derivación sugerida pendiente, la resuelve
    // (confirma si coincide con lo elegido acá, corrige si no); si el caso nunca llegó
    // a tener grupo (ninguna regla coincidió al enviarse la admisión), lo deja asignado
    // igual, sin grupo.
    int? idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var caso = new CasoDAO().ObtenerPorInforme(id);
    if (caso is not null)
    {
        var pendiente = new CasoDerivacionDAO().ObtenerPendientePorCaso(caso.IdCaso);
        if (pendiente is not null)
        {
            if (pendiente.IdProfesionalSugerido == req.IdProfesional)
                asignacion.ConfirmarDerivacion(pendiente.IdCasoDerivacion, idUsuario);
            else
                asignacion.CorregirDerivacion(pendiente.IdCasoDerivacion, idUsuario, nuevoProfesional: req.IdProfesional);
        }
        else
        {
            new CasoDAO().ActualizarEstadoYAsignacion(caso.IdCaso, "Asignado", caso.IdGrupoActual, req.IdProfesional);
            new HistorialAsignacionDAO().Insertar(new HistorialAsignacion
            {
                IdCaso = caso.IdCaso, IdProfesional = req.IdProfesional, Motivo = "Manual", IdRealizadoPor = idUsuario
            });
        }
    }

    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// ── Mis Pacientes con estado de asignacion ──────────────────
app.MapGet("/profesionales/{id}/pacientes-detalle", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx);
    if (!yo!.EsConfigurador && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propios pacientes." }, statusCode: 403);

    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT p.IdPaciente, p.Nombre, p.Dni, p.Mail, p.Telefono,
               ISNULL(pp.EstadoAsignacion, 'Confirmada') AS EstadoAsignacion, pp.FechaAsignacion
        FROM PacienteProfesional pp
        INNER JOIN Paciente p ON p.IdPaciente = pp.IdPaciente
        WHERE pp.IdProfesional = @Id AND ISNULL(pp.EstadoAsignacion, 'Confirmada') <> 'Rechazada'
        ORDER BY pp.FechaAsignacion DESC", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            idPaciente       = (int)reader["IdPaciente"],
            nombre           = reader["Nombre"].ToString(),
            dni              = reader["Dni"] == DBNull.Value ? "" : reader["Dni"].ToString(),
            mail             = reader["Mail"] == DBNull.Value ? "" : reader["Mail"].ToString(),
            telefono         = reader["Telefono"] == DBNull.Value ? "" : reader["Telefono"].ToString(),
            estadoAsignacion = reader["EstadoAsignacion"].ToString(),
            fechaAsignacion  = reader["FechaAsignacion"] == DBNull.Value ? "" : ((DateTime)reader["FechaAsignacion"]).ToString("dd/MM/yyyy")
        });
    }
    return Results.Ok(lista);
}).RequireAuthorization();

// ── Confirmar asignacion (el profesional acepta al paciente) ─
// Cierre de seguridad, Paso 4: el profesional solo confirma/rechaza su propia asignación
// (antes, sin chequeo, cualquiera podía tocar la de cualquier otro par paciente/profesional).
app.MapPut("/asignaciones/estado", (AsignacionEstadoRequest req, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsProfesional: true } && yo.IdProfesional != req.IdProfesional)
        return Results.Json(new { mensaje = "Esa asignación no es tuya." }, statusCode: 403);

    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE PacienteProfesional SET EstadoAsignacion = @Estado
        WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf", con);
    cmd.Parameters.AddWithValue("@Estado", req.Estado);
    cmd.Parameters.AddWithValue("@IdPac", req.IdPaciente);
    cmd.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmd.ExecuteNonQuery();
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

// ── Rechazo por el profesional: vuelve a la bandeja del admisionista ─
app.MapPut("/informes-admision/rechazar", (RechazoRequest req, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsProfesional: true } && yo.IdProfesional != req.IdProfesional)
        return Results.Json(new { mensaje = "Ese paciente no te fue derivado a ti." }, statusCode: 403);

    using var con = Conexion.ObtenerConexion();
    con.Open();

    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE PacienteProfesional SET EstadoAsignacion = 'Rechazada'
        WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf;

        UPDATE InformeAdmision SET Estado = 'Rechazado', MotivoRechazo = @Motivo
        WHERE IdInforme = (SELECT TOP 1 IdInforme FROM InformeAdmision WHERE IdPaciente = @IdPac ORDER BY FechaCreacion DESC);", con);
    cmd.Parameters.AddWithValue("@IdPac", req.IdPaciente);
    cmd.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmd.Parameters.AddWithValue("@Motivo", (object)req.Motivo ?? DBNull.Value);
    cmd.ExecuteNonQuery();
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));


// Turno propuesto por el profesional (queda pendiente de confirmacion del paciente)
// El profesional propone un turno futuro: pasa por todas las reglas (antes, ninguna).
// Cita urgente (EsUrgente=true, "ahora mismo") usa SolicitarUrgente, que no le pide RN-01/05
// pero sí inserta a prueba de doble reserva — ver TurnoReglasService (Fase 3a, Paso 1).
app.MapPost("/turnos/profesional-solicitar", (TurnoProfesionalRequest req, INotificadorTurnos notificador, TurnoReglasService reglas, HttpContext ctx, IdentidadActualService identidad) =>
{
    // Cierre de seguridad, Paso 3: un profesional solo propone turnos a su propio nombre,
    // nunca al de otro id enviado en el cuerpo; el Configurador sí puede elegir cualquiera
    // (agenda en nombre de un profesional, como ya hacía).
    var yo = identidad.Resolver(ctx)!;
    var idProfesional = yo.EsConfigurador ? req.IdProfesional : yo.IdProfesional!.Value;

    var momento = DateTime.Parse(req.Fecha).Date + TimeSpan.Parse(req.Hora);
    var resultado = req.EsUrgente
        ? reglas.SolicitarUrgente(req.IdPaciente, idProfesional, momento, req.Motivo)
        : reglas.Solicitar(req.IdPaciente, idProfesional, momento, req.Motivo, creadoPor: "profesional");
    if (!resultado.Regla.Ok) return resultado.Regla.ComoConflicto();
    notificador.TurnoAgendadoPorProfesional(req.IdPaciente, idProfesional, momento.Date, momento.TimeOfDay);
    return Results.Created("/turnos", new { idTurno = resultado.IdTurno });
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));


// ── Paciente confirma el turno propuesto ────────────────────
app.MapPut("/turnos/{id}/confirmar-paciente", (int id, INotificadorTurnos notificador, TurnoReglasService reglas, HttpContext ctx, IdentidadActualService identidad) =>
{
    var turno = new TurnoDAO().ObtenerPorId(id);
    if (turno is null) return Results.NotFound();
    // Cierre de seguridad, Paso 3: lo confirma el propio paciente, o el profesional en una
    // cita urgente (mismo flujo que ya usa nuevo_dashboard.js), nunca uno ajeno.
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != turno.IdPaciente)
        return Results.Json(new { mensaje = "Ese turno no es tuyo." }, statusCode: 403);
    if (yo is { EsProfesional: true } && yo.IdProfesional != turno.IdProfesional)
        return Results.Json(new { mensaje = "Ese turno no es tuyo." }, statusCode: 403);

    var rn02 = reglas.ValidarPasoAConfirmado(turno);
    if (!rn02.Ok) return rn02.ComoConflicto();
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE Turno SET ConfirmadoPaciente = 1, IdEstadoTurno = 2 WHERE IdTurno = @Id;
        SELECT IdProfesional, IdPaciente FROM Turno WHERE IdTurno = @Id;", con);
    cmd.Parameters.AddWithValue("@Id", id);
    int idProf = 0, idPac = 0;
    using (var r = cmd.ExecuteReader())
    {
        if (r.Read()) { idProf = (int)r["IdProfesional"]; idPac = (int)r["IdPaciente"]; }
    }
    notificador.TurnoConfirmado(id);
    return Results.Ok(new { idProfesional = idProf, idPaciente = idPac });
}).RequireAuthorization();

// ── Turnos confirmados por paciente, pendientes de sala (para el profesional) ──
app.MapGet("/profesionales/{id}/turnos-confirmados", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx);
    if (!yo!.EsConfigurador && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propios turnos." }, statusCode: 403);

    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT t.IdTurno, t.IdPaciente, p.Nombre AS NombrePaciente, t.Fecha, t.Hora,
               t.CodigoSala, t.Motivo
        FROM Turno t
        INNER JOIN Paciente p ON p.IdPaciente = t.IdPaciente
        WHERE t.IdProfesional = @Id AND t.IdEstadoTurno = 2
        ORDER BY t.Fecha, t.Hora", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            idTurno        = (int)reader["IdTurno"],
            idPaciente     = (int)reader["IdPaciente"],
            nombrePaciente = reader["NombrePaciente"].ToString(),
            fecha          = ((DateTime)reader["Fecha"]).ToString("dd/MM/yyyy"),
            hora           = reader["Hora"] == DBNull.Value ? "" : ((TimeSpan)reader["Hora"]).ToString(@"hh\:mm"),
            codigoSala     = reader["CodigoSala"] == DBNull.Value ? "" : reader["CodigoSala"].ToString(),
            motivo         = reader["Motivo"] == DBNull.Value ? "" : reader["Motivo"].ToString()
        });
    }
    return Results.Ok(lista);
}).RequireAuthorization();

// ── El profesional genera la sala: crea codigo y notifica al paciente ──
app.MapPut("/turnos/{id}/generar-sala", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var turno = new TurnoDAO().ObtenerPorId(id);
    if (turno is null) return Results.NotFound();
    // Cierre de seguridad, Paso 3: solo el profesional del turno (o el Configurador) genera
    // la sala.
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsProfesional: true } && yo.IdProfesional != turno.IdProfesional)
        return Results.Json(new { mensaje = "Ese turno no es tuyo." }, statusCode: 403);

    using var con = Conexion.ObtenerConexion();
    con.Open();
    var codigoSala = "SALA-" + id + "-" + DateTime.Now.Ticks.ToString("x").ToUpper();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE Turno SET CodigoSala = @Sala WHERE IdTurno = @Id;
        SELECT IdPaciente, Fecha, Hora FROM Turno WHERE IdTurno = @Id;", con);
    cmd.Parameters.AddWithValue("@Sala", codigoSala);
    cmd.Parameters.AddWithValue("@Id", id);
    int idPac = 0; DateTime fecha = DateTime.Now; TimeSpan hora = TimeSpan.Zero;
    using (var r = cmd.ExecuteReader())
    {
        if (r.Read()) { idPac = (int)r["IdPaciente"]; fecha = (DateTime)r["Fecha"]; if (r["Hora"] != DBNull.Value) hora = (TimeSpan)r["Hora"]; }
    }
    if (idPac > 0)
    {
        var fechaLegible = fecha.ToString("dd/MM/yyyy") + " a las " + hora.ToString(@"hh\:mm") + " hs";
        var cmdNotif = new Microsoft.Data.SqlClient.SqlCommand(@"
            INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link)
            VALUES (@IdPac, @Titulo, @Mensaje, @Link)", con);
        cmdNotif.Parameters.AddWithValue("@IdPac", idPac);
        cmdNotif.Parameters.AddWithValue("@Titulo", "Turno confirmado - Sala lista");
        cmdNotif.Parameters.AddWithValue("@Mensaje", "Tu turno del " + fechaLegible + " esta confirmado. Codigo de sala para ese dia: " + codigoSala);
        cmdNotif.Parameters.AddWithValue("@Link", codigoSala);
        cmdNotif.ExecuteNonQuery();
    }
    return Results.Ok(new { codigoSala });
}).RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

// Turnos ocupados por profesional en una fecha
app.MapGet("/turnos/ocupados", (int idProfesional, string fecha) =>
{
    var dao    = new TurnoDAO();
    var turnos = dao.ObtenerOcupadosPorFecha(idProfesional, fecha);
    return Results.Ok(turnos);
}).RequireAuthorization();

app.MapMensajeria();
app.MapConsentimiento();
app.MapCasos();

app.Run();

public record EntrevistaRequest(DateTime Fecha, string Observaciones);
public record DerivarRequest(int IdProfesional, string Observaciones);

public record AsignacionEstadoRequest(int IdPaciente, int IdProfesional, string Estado);
public record RechazoRequest(int IdPaciente, int IdProfesional, string Motivo);

public record TurnoProfesionalRequest(int IdPaciente, int IdProfesional, string Fecha, string Hora, string Motivo, bool EsUrgente = false);
public record ActivarLinkPublicoRequest(bool Activo);
public record ReservaPublicaRequest(int IdPaciente, DateTime Momento, string? Motivo);

// Expone la clase Program (autogenerada por los top-level statements) para WebApplicationFactory<Program>.
public partial class Program { }
