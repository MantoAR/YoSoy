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

// Headers de la superficie pública (Fase 3a, Paso 3): hoy la app corre local, pero esta
// es la primera ruta pensada para quedar expuesta — si en el futuro se despliega detrás
// de un proxy, estos headers ya están. El resto de la app (mensajería incluida) sigue
// siendo solo local.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/public") || ctx.Request.Path.StartsWithSegments("/reservar"))
    {
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
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
    new EspecialidadDAO().Insertar(e);
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
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    u.Estado = "Activo";
    u.PasswordHash = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(u.PasswordHash)));
    new UsuarioDAO().Insertar(u);
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
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    new UsuarioDAO().Baja(id);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Baja Usuario", Entidad = "Usuario", IdEntidad = id });
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

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
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Dar de alta/aprobar/rechazar un profesional es administración (Fase JWT: Configurador).
// PUT /profesionales/{id} (arriba, sin tocar) sigue abierto: lo usa el propio profesional
// desde "Mi perfil" — no está en el alcance de este grupo.
app.MapPut("/profesionales/{id}/alta", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
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
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    var p = new ProfesionalDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    p.Estado = "Activo";
    new ProfesionalDAO().Modificar(p);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Aprobación Solicitud Profesional", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(p);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/profesionales/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
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

    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = req.Activo ? "Activar Link Público" : "Desactivar Link Público", Entidad = "Profesional", IdEntidad = id });
    return Results.Ok(new { slug = p.Slug, linkPublicoActivo = p.LinkPublicoActivo });
});

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
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Turno Solicitado (Link Público)", Entidad = "Turno", IdEntidad = resultado.IdTurno });
    return Results.Created($"/public/profesionales/{slug}/reservar", new { idTurno = resultado.IdTurno });
}).RequireRateLimiting("publico");

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

// Alias sin 's' para compatibilidad con el dashboard
app.MapGet("/agenda/{id}", (int id) =>
{
    var a = new AgendaDAO().ObtenerPorId(id);
    return a is null ? Results.NotFound() : Results.Ok(a);
});

app.MapPost("/agenda", (Agenda a) =>
{
    new AgendaDAO().Insertar(a);
    return Results.Created("/agenda", a);
});

app.MapPut("/agenda/{id}", (int id, Agenda a) =>
{
    a.IdAgenda = id;
    new AgendaDAO().Modificar(a);
    return Results.Ok(a);
});

app.MapDelete("/agenda/{id}", (int id) =>
{
    new AgendaDAO().Eliminar(id);
    return Results.NoContent();
});

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

app.MapGet("/pacientes/{id}/proximo-turno", (int id) =>
{
    var t = new TurnoDetalleDAO().ObtenerProximoPorPaciente(id);
    return t is null ? Results.NotFound() : Results.Ok(t);
});

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

app.MapPost("/turnos", (Turno t) =>
{
    new TurnoDAO().Insertar(t);
    return Results.Created("/turnos", t);
});

// Solicitud de turno desde el paciente — estado siempre Pendiente. Pasa por
// TurnoReglasService.Solicitar (RN-01/02/05/07 + inserción a prueba de doble reserva),
// igual que ya hace el bot — antes insertaba directo, sin ninguna regla (Fase 3a, Paso 1).
app.MapPost("/turnos/solicitar", (Turno t, INotificadorTurnos notificador, TurnoReglasService reglas) =>
{
    var resultado = reglas.Solicitar(t.IdPaciente, t.IdProfesional, t.Fecha.Date + t.Hora, t.Motivo);
    if (!resultado.Regla.Ok) return resultado.Regla.ComoConflicto();
    notificador.TurnoSolicitado(resultado.IdTurno!.Value);
    // Misma forma que antes (el Turno enviado), más el id real que ya no quedaba en 0.
    t.IdTurno = resultado.IdTurno.Value;
    t.CreadoPor = "paciente";
    return Results.Created("/turnos", t);
});

app.MapPut("/turnos/{id}", (int id, Turno t, INotificadorTurnos notificador, TurnoReglasService reglas) =>
{
    t.IdTurno = id;
    var anterior = new TurnoDAO().ObtenerPorId(id);
    var rn02 = reglas.ValidarCambioDeEstado(anterior, t);
    if (!rn02.Ok) return rn02.ComoConflicto();
    new TurnoDAO().Modificar(t);
    if (anterior != null) notificador.CambioDeEstado(id, anterior.IdEstadoTurno);
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
// Fase JWT: historia clínica/evoluciones/eventos de crisis/sesiones clínicas — lo más
// sensible de toda la app (Ley 25.326). Ningún paciente los llama directo (no hay
// pantalla de Pantalla_Paciente que los use — confirmado buscando en todo ese directorio),
// así que el mínimo seguro y sin riesgo de romper nada es: Profesional o Configurador,
// nunca Paciente ni anónimo. Queda pendiente, anotado, el paso más fino ("solo el
// profesional asociado a ESE paciente", no cualquier profesional) — hoy cualquier
// profesional autenticado puede ver la de cualquier paciente, un alcance más amplio que
// Casos (que sí filtra por asignación) pero bastante más chico que "público".
var soloClinico = (RouteHandlerBuilder b) => b.RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

soloClinico(app.MapGet("/historiasclinicas", () =>
    new HistoriaClinicaDAO().ObtenerTodos()));

soloClinico(app.MapGet("/historiasclinicas/{id}", (int id) =>
{
    var h = new HistoriaClinicaDAO().ObtenerPorId(id);
    return h is null ? Results.NotFound() : Results.Ok(h);
}));

soloClinico(app.MapGet("/pacientes/{id}/historiaclinica", (int id) =>
{
    var h = new HistoriaClinicaDAO().ObtenerPorPaciente(id);
    return h is null ? Results.NotFound() : Results.Ok(h);
}));

soloClinico(app.MapPost("/historiasclinicas", (HistoriaClinica h) =>
{
    new HistoriaClinicaDAO().Insertar(h);
    return Results.Created("/historiasclinicas", h);
}));

// Evolucion
soloClinico(app.MapGet("/evoluciones", () =>
    new EvolucionDAO().ObtenerTodos()));

soloClinico(app.MapGet("/evoluciones/{id}", (int id) =>
{
    var e = new EvolucionDAO().ObtenerPorId(id);
    return e is null ? Results.NotFound() : Results.Ok(e);
}));

soloClinico(app.MapGet("/historiasclinicas/{id}/evoluciones", (int id) =>
    new EvolucionDAO().ObtenerPorHistoria(id)));

soloClinico(app.MapPost("/evoluciones", (Evolucion e) =>
{
    new EvolucionDAO().Insertar(e);
    return Results.Created("/evoluciones", e);
}));

soloClinico(app.MapPut("/evoluciones/{id}", (int id, Evolucion e) =>
{
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
    new DerivacionDAO().Insertar(d);
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
    SincronizarCasoPorCambioDeProfesional(d.IdPaciente, req.IdProfesionalDestino, auditor);
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

app.MapPut("/derivaciones/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int auditor = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xId) ? xId : 0;
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
    SincronizarCasoPorCambioDeProfesional(req.IdPaciente, req.IdProfesionalDestino, auditor);
    return Results.Ok();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Fase 2, aditivo: si el paciente tiene un Caso abierto, la solicitud de cambio de
// profesional (Derivacion/"Solicitudes de cambio", sin relación con CasoDerivacion) lo
// actualiza también y deja su propio rastro en HistorialAsignacion. Si no tiene un caso
// abierto (pacientes de antes de la Fase 2, o ya Cerrado), no hace nada: no se
// reconstruye historial viejo. No cambia el comportamiento visible de arriba.
void SincronizarCasoPorCambioDeProfesional(int idPaciente, int idProfesionalNuevo, int idUsuario)
{
    var caso = new CasoDAO().ObtenerAbiertoPorPaciente(idPaciente);
    if (caso is null) return;
    new CasoDAO().ActualizarEstadoYAsignacion(caso.IdCaso, caso.Estado, caso.IdGrupoActual, idProfesionalNuevo);
    new HistorialAsignacionDAO().Insertar(new HistorialAsignacion
    {
        IdCaso = caso.IdCaso, IdProfesional = idProfesionalNuevo, Motivo = "SolicitudCambio",
        IdRealizadoPor = idUsuario == 0 ? null : idUsuario
    });
}

app.MapGet("/profesionales/activos/especialidad/{idEsp}", (int idEsp) =>
    new ProfesionalDAO().ObtenerPorEspecialidad(idEsp)
        .Where(p => p.Estado == "Activo").ToList());

// EventoCrisis
soloClinico(app.MapGet("/eventoscrisis", () =>
    new EventoCrisisDAO().ObtenerTodos()));

soloClinico(app.MapGet("/eventoscrisis/{id}", (int id) =>
{
    var e = new EventoCrisisDAO().ObtenerPorId(id);
    return e is null ? Results.NotFound() : Results.Ok(e);
}));

soloClinico(app.MapGet("/pacientes/{id}/eventoscrisis", (int id) =>
    new EventoCrisisDAO().ObtenerPorPaciente(id)));

soloClinico(app.MapPost("/eventoscrisis", (EventoCrisis e) =>
{
    new EventoCrisisDAO().Insertar(e);
    return Results.Created("/eventoscrisis", e);
}));

soloClinico(app.MapPut("/eventoscrisis/{id}", (int id, EventoCrisis e) =>
{
    e.IdEvento = id;
    new EventoCrisisDAO().Modificar(e);
    return Results.Ok(e);
}));

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
soloClinico(app.MapPost("/sesiones-clinicas", (SesionClinica s) =>
{
    s.FechaHora = DateTime.Now;
    int id = new SesionClinicaDAO().Insertar(s);
    s.IdSesion = id;
    return Results.Created("/sesiones-clinicas", s);
}));

soloClinico(app.MapGet("/pacientes/{id}/sesiones-clinicas", (int id) =>
    new SesionClinicaDAO().ObtenerPorPaciente(id)));

soloClinico(app.MapGet("/profesionales/{id}/sesiones-clinicas", (int id) =>
    new SesionClinicaDAO().ObtenerPorProfesional(id)));


// Reporte: atenciones por profesional
app.MapGet("/reportes/atenciones", (int? idProfesional) =>
{
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
});

// ── INICIO reporte HTML (feature/agustin-reporte-html-interactivo) ──
// LIMITACIÓN CONOCIDA (CB-04): el parámetro idProfesional no se valida contra el
// usuario autenticado. Hereda el comportamiento de GET /reportes/atenciones.
// Requiere el esquema de autenticación de la rama videollamadas-seguras.
// Datos expuestos: nombre, DNI y NHC de pacientes (Ley 25.326).
app.MapGet("/reportes/atenciones/html", (int? idProfesional, HttpContext ctx) =>
{
    var (autorizado, idPermitido, motivo) = AutorizacionReporte.ResolverAlcance(ctx, idProfesional);
    if (!autorizado) return Results.Json(new { mensaje = motivo }, statusCode: 403);
    var datos  = new ReporteAtencionesDAO().Obtener(idPermitido);
    var html   = ReporteHtmlBuilder.Construir(datos, idPermitido);
    var nombre = $"Atenciones_por_Profesional_{DateTime.Now:yyyyMMdd_HHmm}.html";
    return Results.File(System.Text.Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", nombre);
});
// ── FIN reporte HTML ──


// ── Informe de Admision ─────────────────────────────────────
app.MapPost("/informes-admision", (InformeAdmision i, AsignacionService asignacion) =>
{
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
});

app.MapGet("/informes-admision", (string? estado) =>
    new InformeAdmisionDAO().ObtenerTodos(estado));

app.MapGet("/informes-admision/{id}", (int id) =>
{
    var i = new InformeAdmisionDAO().ObtenerPorId(id);
    return i is null ? Results.NotFound() : Results.Ok(i);
});

app.MapGet("/pacientes/{id}/informe-admision", (int id) =>
{
    var i = new InformeAdmisionDAO().ObtenerPorPaciente(id);
    return i is null ? Results.NotFound() : Results.Ok(i);
});

app.MapPut("/informes-admision/{id}/estado", (int id, string estado) =>
{
    new InformeAdmisionDAO().CambiarEstado(id, estado);
    return Results.NoContent();
});


// ── Admision: endpoints para el configurador ────────────────
app.MapGet("/informes-admision-detalle", () =>
{
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
});

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
});

// ── Notificaciones ──────────────────────────────────────────
app.MapGet("/pacientes/{id}/notificaciones", (int id) =>
{
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
});

app.MapPut("/notificaciones/{id}/leida", (int id) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(
        "UPDATE Notificacion SET Leida = 1 WHERE IdNotificacion = @Id", con);
    cmd.Parameters.AddWithValue("@Id", id);
    cmd.ExecuteNonQuery();
    return Results.NoContent();
});

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
    int? idUsuario = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var xAdm) ? xAdm : null;
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
});

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
app.MapPut("/asignaciones/estado", (AsignacionEstadoRequest req) =>
{
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
});

// ── Rechazo por el profesional: vuelve a la bandeja del admisionista ─
app.MapPut("/informes-admision/rechazar", (RechazoRequest req) =>
{
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
});


// Turno propuesto por el profesional (queda pendiente de confirmacion del paciente)
// El profesional propone un turno futuro: pasa por todas las reglas (antes, ninguna).
// Cita urgente (EsUrgente=true, "ahora mismo") usa SolicitarUrgente, que no le pide RN-01/05
// pero sí inserta a prueba de doble reserva — ver TurnoReglasService (Fase 3a, Paso 1).
app.MapPost("/turnos/profesional-solicitar", (TurnoProfesionalRequest req, INotificadorTurnos notificador, TurnoReglasService reglas) =>
{
    var momento = DateTime.Parse(req.Fecha).Date + TimeSpan.Parse(req.Hora);
    var resultado = req.EsUrgente
        ? reglas.SolicitarUrgente(req.IdPaciente, req.IdProfesional, momento, req.Motivo)
        : reglas.Solicitar(req.IdPaciente, req.IdProfesional, momento, req.Motivo, creadoPor: "profesional");
    if (!resultado.Regla.Ok) return resultado.Regla.ComoConflicto();
    notificador.TurnoAgendadoPorProfesional(req.IdPaciente, req.IdProfesional, momento.Date, momento.TimeOfDay);
    return Results.Created("/turnos", new { idTurno = resultado.IdTurno });
});


// ── Paciente confirma el turno propuesto ────────────────────
app.MapPut("/turnos/{id}/confirmar-paciente", (int id, INotificadorTurnos notificador, TurnoReglasService reglas) =>
{
    var rn02 = reglas.ValidarPasoAConfirmado(new TurnoDAO().ObtenerPorId(id));
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
});

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
app.MapPut("/turnos/{id}/generar-sala", (int id) =>
{
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
});

// Turnos ocupados por profesional en una fecha
app.MapGet("/turnos/ocupados", (int idProfesional, string fecha) => 
{
    var dao    = new TurnoDAO();
    var turnos = dao.ObtenerOcupadosPorFecha(idProfesional, fecha);
    return Results.Ok(turnos);
});

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
