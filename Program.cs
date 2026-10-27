using Microsoft.Data.SqlClient;

// Modo mantenimiento (no levanta el servidor): cifra las emociones que se guardaron en claro antes del PR 6. Por defecto
// es una simulación; con --aplicar escribe. Se corre a mano, con el entorno del servicio: ver
// docs/DESPLIEGUE_VIDEO.md ("Recifrar las emociones ya guardadas") y Services/Video/RecifrarEmociones.cs.
if (args.Contains("--recifrar-emociones"))
{
    Environment.ExitCode = RecifrarEmociones.Ejecutar(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
        // Para que una pantalla abierta desde otro origen (Live Server) pueda leer el código de error.
        .WithExposedHeaders(RegistroErroresExtensions.Encabezado)));
// Etapa 1 (errores y auditoría): registro de errores con código (Services/RegistroErrores.cs) y aviso de migraciones
// faltantes (Services/VerificadorEsquema.cs; viene APAGADO, se prende con Esquema:Verificar=true).
builder.Services.AddSingleton<IInspectorEsquema, InspectorEsquemaSql>();
builder.Services.AddSingleton<VerificadorEsquema>();
builder.Services.AddRegistroErrores(builder.Environment.ContentRootPath, builder.Configuration["RegistroErrores:Carpeta"]);
// Etapa 2: roles, permisos y privilegios por módulo (Services/Accesos.cs).
builder.Services.AddSingleton<IAccesosRepositorio, AccesosDAO>();
builder.Services.AddSingleton<EmailService>();
// El reloj de TODA la aplicación, con la zona horaria de la clínica explícita (Turnos:ZonaHoraria, por defecto
// America/Argentina/Buenos_Aires): ya no depende de la zona del sistema operativo del servidor. Tiene que ir antes de
// AddMensajeria y AddVideo, que lo reciben por inyección. Ver Services/RelojDeLaClinica.cs.
builder.Services.AddRelojDeLaClinica(builder.Configuration);
builder.Services.AddMensajeria(builder.Configuration);
builder.Services.AddVideo(builder.Configuration);
builder.Services.AddUsuariosAdmin(builder.Configuration);
builder.Services.AddSingleton<IAsignacionRepositorio, AsignacionRepositorioDAO>();
builder.Services.AddSingleton<AsignacionService>();
builder.Services.AddSingleton<AsignacionAutomaticaService>();   // propuesta automática de profesional y horario al aceptar un formulario (migración 32)
// Circuito de atención: salas, interruptores de IA/transcripción y evolución por enfoque.

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
        // Etapa 2: el token de selección de módulo (sin roles, 5 minutos) solo sirve para
        // elegir módulo. En cualquier otra ruta se rechaza como si no hubiera token, así no
        // alcanza a los endpoints que solo piden "autenticado".
        opt.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                var esSeleccion = ctx.Principal?.FindFirst(JwtEmisorService.ClaimUso)?.Value == JwtEmisorService.UsoSeleccion;
                if (esSeleccion && !ctx.HttpContext.Request.Path.Equals("/sesion/modulo", StringComparison.OrdinalIgnoreCase))
                    ctx.Fail("El token de selección solo sirve para elegir módulo.");
                // Cambio obligatorio de contraseña (migración 24): mismo patrón, con su propia ruta.
                var esCambioClave = ctx.Principal?.FindFirst(JwtEmisorService.ClaimUso)?.Value == JwtEmisorService.UsoCambioClave;
                if (esCambioClave && !ctx.HttpContext.Request.Path.Equals("/sesion/cambiar-password", StringComparison.OrdinalIgnoreCase))
                    ctx.Fail("El token de cambio de clave solo sirve para elegir la clave nueva.");
                return Task.CompletedTask;
            }
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
    // Recuperación de contraseña autoservicio: más estricto que "publico" — es el único
    // límite por IP; el límite por usuario (independiente de la IP) lo hace el propio
    // endpoint contando filas de RecuperacionPassword, ver POST /recuperacion-password.
    opt.AddPolicy("recuperacion", httpContext => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(15), PermitLimit = 5, QueueLimit = 0
        }));
    // Registro público de paciente con acceso: más laxo que "recuperacion" (una familia
    // registrándose junto desde la misma red no debería chocar) pero igual pensado para
    // frenar un alta masiva automatizada.
    opt.AddPolicy("registro", httpContext => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(10), PermitLimit = 10, QueueLimit = 0
        }));
    // Pantalla de WhatsApp del Configurador: ya exige sesión de administrador, así que esto
    // es un respaldo, no la defensa principal — por IP igual que las de arriba (no por el
    // header Authorization crudo: eso permitiría crear un balde nuevo por pedido con solo
    // mandar un header distinto cada vez, sin ningún login). RemoteIpAddress ya es la IP
    // real del cliente gracias a UseForwardedHeaders (fix/recuperacion-ajustes), aunque el
    // pedido pase por Caddy. "estado" es polling (cada pocos segundos mientras se espera un
    // QR): límite laxo. "admin" son acciones deliberadas e infrecuentes (desvincular,
    // confirmar-vinculacion): mucho más estricto, mismo criterio que separa "publico" de
    // "recuperacion".
    opt.AddPolicy("whatsapp-estado", httpContext => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1), PermitLimit = 60, QueueLimit = 0
        }));
    opt.AddPolicy("whatsapp-admin", httpContext => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(5), PermitLimit = 10, QueueLimit = 0
        }));
    // Buscador de pacientes para vincular una conversación (Chats): ya exige sesión de CRM/Administración; el límite por IP frena que se use para recorrer la lista de pacientes.
    opt.AddPolicy("chats-buscar-paciente", httpContext => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1), PermitLimit = 30, QueueLimit = 0
        }));
});

var app = builder.Build();

// Queda en el journal qué zona está usando la API para decidir qué hora es en la clínica.
var relojDeLaClinica = app.Services.GetRequiredService<TimeProvider>();
app.Logger.LogInformation("Zona horaria de la clínica: {Zona}, diferencia con UTC ahora: {Diferencia}.",
    relojDeLaClinica.LocalTimeZone.Id, relojDeLaClinica.GetLocalNow().Offset);

// Falla rápido y claro al arrancar si falta la clave, en vez de un 500 confuso en el
// primer login — se resuelve el singleton una vez ahora mismo para forzar el chequeo.
app.Services.GetRequiredService<JwtEmisorService>();

// Detrás de Caddy (reverse_proxy local, ver docs/SERVIDOR.md): sin esto, Connection.
// RemoteIpAddress es siempre 127.0.0.1 para cualquier pedido real que llega por el proxy,
// y el rate limiter por IP (políticas "publico"/"recuperacion", arriba) termina
// compartiendo un solo contador entre todos los usuarios. Se confía el X-Forwarded-For
// únicamente cuando el pedido llega desde el proxy local (loopback) — así no se puede
// falsear saltándose a Caddy. Tiene que ir antes de UseRateLimiter/UseAuthentication, que
// son los que leen RemoteIpAddress más abajo.
var opcionesForwarded = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
};
opcionesForwarded.KnownProxies.Clear();
opcionesForwarded.KnownNetworks.Clear();
opcionesForwarded.KnownProxies.Add(System.Net.IPAddress.Loopback);
opcionesForwarded.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
app.UseForwardedHeaders(opcionesForwarded);
app.UseRegistroErrores();      // envuelve todo lo que sigue (después de los encabezados reenviados, que corrigen la IP)

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseVerificacionEsquema();  // después de la autorización: un 401/403 no toca la base. Apagado por defecto.

// Cierre de seguridad, Paso 7: antes estos headers solo salían en /public y /reservar
// (pensados para la superficie pública, Fase 3a). Pasan a ser globales — toda la app es
// candidata a quedar detrás de un proxy algún día, no solo esas dos rutas.
// X-Frame-Options: SAMEORIGIN, no DENY: la sala de video sigue embebida en un <iframe>
// (Pantalla_Profesional/js/nuevo_dashboard.js, sesion_video.html) desde el mismo origen —
// DENY la rompería. CSP queda afuera a propósito (ver abajo): hay mucho JS/CSS inline en
// PANTALLAS/ y una política restrictiva rompería pantallas; se deja anotado como mejora
// futura en CAMBIOS_AGUS.md.
// Permissions-Policy: la cámara y el micrófono los usa solo la sala de video, que corre en este mismo
// origen (también dentro del <iframe> del dashboard). Con (self) quedan permitidos para esta página y
// para los iframes del mismo origen, y bloqueados para cualquier contenido embebido de otro origen. El
// resto de las funciones del navegador conserva su comportamiento por defecto.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(self)";
    await next();
});

app.UsePantallaMensajeria();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "PANTALLAS")),
    RequestPath = "/pantallas",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
    // Roles y permisos: sin esto el navegador seguía usando los .js y .css viejos después de
    // actualizar las pantallas (cache heurística por Last-Modified). "no-cache" no impide
    // guardarlos: obliga a preguntarle al servidor si cambiaron (304 si no, casi sin costo).
    OnPrepareResponse = ctx =>
    {
        var ext = Path.GetExtension(ctx.File.Name).ToLowerInvariant();
        if (ext is ".html" or ".js" or ".css")
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
});

app.MapGet("/", () => Results.Redirect("/pantallas/index.html"));

// Login. Un solo login para los tres roles, así que el bloqueo por intentos fallidos es el mismo para todos
// (PoliticaBloqueoLogin: 3 intentos, 15 minutos; BloqueadoHasta en UTC; vencido el bloqueo el conteo arranca de
// nuevo; un login exitoso lo pone en cero).
app.MapPost("/login", (LoginRequest req, JwtEmisorService jwtEmisor, IAccesosRepositorio accesosRepo, TimeProvider reloj) =>
{
    var ahoraUtc = reloj.GetUtcNow().UtcDateTime;
    var dao = new UsuarioDAO();
    var usuario = dao.ObtenerPorUsername(req.Username);

    // Etapa 2: cada ingreso (exitoso o no) y cada bloqueo quedan en la auditoría
    // administrativa. Nunca se guarda la contraseña.
    if (usuario == null || usuario.Estado != "Activo")
    {
        AuditoriaIngreso.Registrar(usuario?.IdUsuario, "Ingreso fallido",
            usuario == null ? $"Usuario inexistente: {Recortar(req.Username, 60)}" : "Cuenta no activa");
        return Results.Unauthorized();
    }

    if (PoliticaBloqueoLogin.EstaBloqueado(usuario.BloqueadoHasta, ahoraUtc))
    {
        AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso rechazado", "Cuenta bloqueada temporalmente");
        return Results.Json(new { mensaje = PoliticaBloqueoLogin.MensajeBloqueo(usuario.BloqueadoHasta!.Value, ahoraUtc) }, statusCode: 423);
    }

    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(req.Password))).ToUpper();

    if (usuario.PasswordHash.ToUpper() != hash)
    {
        var (_, bloqueadoHasta) = dao.RegistrarIntentoFallido(usuario.IdUsuario, ahoraUtc);
        if (PoliticaBloqueoLogin.EstaBloqueado(bloqueadoHasta, ahoraUtc))
        {
            AuditoriaIngreso.Registrar(usuario.IdUsuario, "Cuenta bloqueada", "Bloqueo por intentos fallidos");
            return Results.Json(new { mensaje = PoliticaBloqueoLogin.MensajeBloqueo(bloqueadoHasta!.Value, ahoraUtc) }, statusCode: 423);
        }
        AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso fallido", "Contraseña incorrecta");
        return Results.Unauthorized();
    }

    // Login exitoso: el conteo vuelve a cero (si hay algo que limpiar; así un login normal no escribe).
    if (usuario.IntentosFallidos > 0 || usuario.BloqueadoHasta.HasValue)
        dao.ResetearIntentos(usuario.IdUsuario);

    // Cambio obligatorio de contraseña (migración 24): el Configurador restableció la clave y esta es la temporal. La contraseña ya
    // está verificada (y el bloqueo mirado, arriba); no se entrega sesión: solo un token corto que sirve para elegir la clave nueva.
    if (usuario.DebeCambiarPassword)
    {
        AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso", "Tiene que cambiar la contraseña");
        return Results.Ok(new { requiereCambioPassword = true, idUsuario = usuario.IdUsuario,
            tokenCambioClave = jwtEmisor.EmitirCambioClave(usuario.IdUsuario) });
    }

    return EntregarSesion(usuario, req.Modulo, jwtEmisor, accesosRepo);
});

// Lo que sigue a una contraseña correcta: el rol, los módulos y el token de trabajo. Lo usan POST /login y, después de elegir la
// clave nueva, POST /sesion/cambiar-password: la sesión sale por el mismo camino, con los mismos controles de módulo y permisos.
static IResult EntregarSesion(Usuario usuario, string? modulo, JwtEmisorService jwtEmisor, IAccesosRepositorio accesosRepo)
{
    // El token lleva el mismo idUsuario/idPaciente/idProfesional que ya devolvía esta
    // respuesta (contrato existente, solo se agrega el campo "token" — nada se quita).
    var up = new UsuarioPacienteDAO().ObtenerPorUsuario(usuario.IdUsuario);
    var pestania = string.IsNullOrWhiteSpace(modulo) ? null : modulo.Trim().ToLowerInvariant();
    if (up != null)
    {
        if (pestania is not null && pestania != Roles.Paciente)
        {
            AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso rechazado", $"Cuenta de paciente en la pestaña {Recortar(pestania, 30)}");
            return Results.Json(new { codigo = "ROL_INCORRECTO", mensaje = "Esta es una cuenta de paciente: ingresá desde la pestaña Paciente." }, statusCode: 403);
        }
        AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso", null, Roles.Paciente);
        return Results.Ok(new { rol = "paciente", idUsuario = usuario.IdUsuario, idPaciente = up.IdPaciente,
            token = jwtEmisor.Emitir(usuario.IdUsuario, "paciente", idPaciente: up.IdPaciente) });
    }

    // Equipo (etapa 2): el acceso sale de los roles, permisos y privilegios que asignó el
    // Configurador (migraciones 20 a 23), ya no de "no es paciente ni profesional → configurador".
    var accesos = accesosRepo.Obtener(usuario.IdUsuario);
    var modulos = accesos.ModulosVisibles;   // la unión Configurador + CRM es UNA opción: Administración
    if (modulos.Count == 0)
    {
        AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso sin permisos", "El usuario no tiene rol ni permiso asignados");
        return Results.Json(new { codigo = "SIN_PERMISOS", mensaje = ResolverSesion.MensajeSinPermisos }, statusCode: 403);
    }

    var idProfesional = new UsuarioProfesionalDAO().ObtenerPorUsuario(usuario.IdUsuario)?.IdProfesional;

    // Pestaña elegida en el ingreso (Profesional, CRM, Facturación, Configurador): entra
    // directo a ese módulo si el usuario lo tiene asignado.
    if (pestania is not null)
    {
        if (pestania == Roles.Paciente)
        {
            AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso rechazado", "Cuenta del equipo en la pestaña paciente");
            return Results.Json(new { codigo = "ROL_INCORRECTO", mensaje = "Esta cuenta no es de paciente: elegí la pestaña de tu rol." }, statusCode: 403);
        }
        var (resPestania, sesionPestania) = ResolverSesion.Para(accesos, pestania, idProfesional);
        if (sesionPestania is null)
        {
            AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso rechazado", $"Sin el rol de la pestaña {Recortar(pestania, 30)}");
            return RespuestaSesionRechazada(resPestania, pestania);
        }
        AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso", null, sesionPestania.Modulo);
        return Results.Ok(RespuestaSesion(usuario.IdUsuario, sesionPestania, modulos, jwtEmisor));
    }

    if (modulos.Count == 1)
    {
        var (resultado, sesion) = ResolverSesion.Para(accesos, modulos[0], idProfesional);
        if (sesion is null) return RespuestaSesionRechazada(resultado);
        AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso", null, sesion.Modulo);
        return Results.Ok(RespuestaSesion(usuario.IdUsuario, sesion, modulos, jwtEmisor));
    }

    // Más de un módulo: elige en la pantalla de ingreso con un token que solo sirve para eso.
    AuditoriaIngreso.Registrar(usuario.IdUsuario, "Ingreso", "Eligiendo módulo");
    return Results.Ok(new
    {
        requiereModulo = true,
        idUsuario = usuario.IdUsuario,
        modulos = DescribirModulos(modulos),
        tokenSeleccion = jwtEmisor.EmitirSeleccion(usuario.IdUsuario)
    });
}

// Elegir o cambiar de módulo. Acepta el token de selección (recién ingresado) o el token de
// trabajo de otro módulo. Vuelve a leer los permisos en la base: si el Configurador quitó
// uno, deja de valer acá.
app.MapPost("/sesion/modulo", (SeleccionModuloRequest req, HttpContext ctx, IdentidadActualService identidad,
    JwtEmisorService jwtEmisor, IAccesosRepositorio accesosRepo) =>
{
    var yo = identidad.Resolver(ctx);
    if (yo is null || yo.EsPaciente)
        return Results.Json(new { mensaje = "Solo el equipo de la organización elige módulo." }, statusCode: 403);
    var usuario = new UsuarioDAO().ObtenerPorId(yo.IdUsuario);
    if (usuario is null || usuario.Estado != "Activo")
        return Results.Json(new { mensaje = "Tu cuenta no está activa." }, statusCode: 403);

    var accesos = accesosRepo.Obtener(yo.IdUsuario);
    var idProfesional = new UsuarioProfesionalDAO().ObtenerPorUsuario(yo.IdUsuario)?.IdProfesional;
    var (resultado, sesion) = ResolverSesion.Para(accesos, req.Modulo ?? "", idProfesional);
    if (sesion is null) return RespuestaSesionRechazada(resultado);

    AuditoriaIngreso.Registrar(yo.IdUsuario, yo.EsSeleccion ? "Ingreso a módulo" : "Cambio de módulo",
        yo.EsSeleccion ? null : $"Desde {yo.Modulo ?? yo.Rol}", sesion.Modulo);
    return Results.Ok(RespuestaSesion(yo.IdUsuario, sesion, accesos.ModulosVisibles, jwtEmisor));
}).RequireAuthorization();

// Cambio obligatorio de contraseña (migración 24): lo único que sirve el token de cambio de clave (5 minutos) que devuelve POST /login cuando el
// Configurador restableció la contraseña. Valida la política, exige una clave distinta de la temporal, la guarda, baja la marca y entrega la sesión por el
// mismo camino que el ingreso (módulos, permisos y pestaña elegida). Con cualquier otro token, 403.
app.MapPost("/sesion/cambiar-password", (CambiarPasswordRequest req, HttpContext ctx, IdentidadActualService identidad,
    JwtEmisorService jwtEmisor, IAccesosRepositorio accesosRepo) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    var yo = identidad.Resolver(ctx);
    if (yo is null || !yo.EsCambioClave)
        return Results.Json(new { codigo = "CAMBIO_NO_REQUERIDO", mensaje = "Esta ruta es solo para elegir la clave nueva después de un restablecimiento." }, statusCode: 403);
    var r = CambioPasswordObligatorio.Aplicar(yo.IdUsuario, req.PasswordNueva);
    if (r.Usuario is null) return Results.Json(new { codigo = r.Codigo, mensaje = r.Mensaje }, statusCode: r.Estado);
    AuditoriaIngreso.Registrar(yo.IdUsuario, "Ingreso", "Con la clave nueva, después del cambio obligatorio");
    return EntregarSesion(r.Usuario, req.Modulo, jwtEmisor, accesosRepo);
}).RequireAuthorization().RequireRateLimiting(UsuariosAdminServiceCollectionExtensions.PoliticaCambiarClave);

static string Recortar(string? texto, int largo) =>
    string.IsNullOrEmpty(texto) ? "" : (texto.Length <= largo ? texto : texto[..largo]);

static object DescribirModulos(IEnumerable<string> modulos) =>
    modulos.Select(m => new { clave = m, nombre = Modulos.Nombre(m), descripcion = Modulos.Descripcion(m) }).ToList();

// Misma forma que la respuesta de siempre ("rol", "idUsuario", "idProfesional", "token"),
// más lo de la etapa 2: módulo activo, módulos disponibles, permisos y privilegios.
static object RespuestaSesion(int idUsuario, SesionModulo sesion, IEnumerable<string> modulos, JwtEmisorService jwtEmisor) => new
{
    rol = sesion.RolLegado,
    idUsuario,
    idProfesional = sesion.IdProfesional,
    modulo = sesion.Modulo,
    modulos = DescribirModulos(modulos),
    permisos = sesion.Permisos,
    privilegios = sesion.Privilegios,
    token = jwtEmisor.EmitirModulo(idUsuario, sesion)
};

static IResult RespuestaSesionRechazada(ResolverSesion.Resultado resultado, string? modulo = null) => resultado switch
{
    ResolverSesion.Resultado.SinPermisos =>
        Results.Json(new { codigo = "SIN_PERMISOS", mensaje = ResolverSesion.MensajeSinPermisos }, statusCode: 403),
    ResolverSesion.Resultado.FaltaFichaProfesional =>
        Results.Json(new { codigo = "SIN_FICHA_PROFESIONAL", mensaje = "Tu usuario no tiene una ficha de profesional activa. Comunicate con el administrador de tu organización." }, statusCode: 403),
    _ => Results.Json(new
    {
        codigo = "MODULO_NO_HABILITADO",
        mensaje = modulo is null || Modulos.Nombre(modulo) == modulo
            ? "No tenés habilitado ese rol."
            : $"Tu usuario no tiene el rol {Modulos.Nombre(modulo)}. Elegí la pestaña de tu rol."
    }, statusCode: 403)
};

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
// Roles y permisos: nombre de quien está en sesión, para la barra superior. Antes la barra del
// Configurador pedía /usuarios/{id}, que es de administración; CRM no tiene ese permiso.
app.MapGet("/sesion/yo", (HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    var u = new UsuarioDAO().ObtenerPorId(yo.IdUsuario);
    if (u is null) return Results.NotFound();
    return Results.Ok(new { idUsuario = u.IdUsuario, username = u.Username, modulo = yo.Modulo ?? yo.Rol });
}).RequireAuthorization();

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
    .Solo(Quien.Profesional);

app.MapGet("/tutores/{id}", (int id) =>
{
    var t = new TutorDAO().ObtenerPorId(id);
    return t is null ? Results.NotFound() : Results.Ok(t);
}).Solo(Quien.Profesional);

app.MapPost("/tutores", (Tutor t) =>
{
    t.IdTutor = new TutorDAO().Insertar(t);
    return Results.Created("/tutores", t);
}).Solo(Quien.Profesional);

app.MapPut("/tutores/{id}", (int id, Tutor t) =>
{
    t.IdTutor = id;
    new TutorDAO().Modificar(t);
    return Results.Ok(t);
}).Solo(Quien.Profesional);

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
    var cambiaPassword = !string.IsNullOrEmpty(u.PasswordHash);
    if (cambiaPassword)
        u.PasswordHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(u.PasswordHash)));
    var dao = new UsuarioDAO();
    dao.Modificar(u);
    // Si el Configurador le pone una contraseña nueva a alguien, es para que pueda entrar: se desbloquea
    // (igual que hace la recuperación por mail).
    if (cambiaPassword) dao.ResetearIntentos(id);
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
// Roles y permisos: el listado lo necesitan el Configurador (cuentas), Admisión, el auditor y quien
// gestiona agendas. CRM sin ese privilegio no lo recibe.
app.MapGet("/pacientes", () =>
    new PacienteDAO().ObtenerTodos())
    .Solo(Quien.Configurador | Quien.Admision | Quien.Auditor | Quien.Agendas);

app.MapGet("/pacientes/{id}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != id)
        return Results.Json(new { mensaje = "Solo podés ver tu propia ficha." }, statusCode: 403);
    var p = new PacienteDAO().ObtenerPorId(id);
    return p is null ? Results.NotFound() : Results.Ok(p);
}).RequireAuthorization();

// Registro público de paciente, con acceso: Usuario + Paciente + UsuarioPaciente juntos
// (PacienteDAO.RegistrarConAcceso), para que nunca quede un paciente sin login ni un login
// a medias. La respuesta de conflicto dice si el NOMBRE DE USUARIO está en uso (lo elige la persona) pero
// ya no cuál de los datos personales (DNI, mail) coincidió: ver MensajesConflictoRegistro.
var EsMailValido = (string mail) =>
{
    try { return new System.Net.Mail.MailAddress(mail).Address == mail; }
    catch { return false; }
};
app.MapPost("/pacientes", (RegistroPacienteRequest req, ConsentimientoWhatsAppService consentimientoWhatsApp) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre))
        return Results.BadRequest(new { mensaje = "El nombre es obligatorio." });
    if (string.IsNullOrWhiteSpace(req.Dni))
        return Results.BadRequest(new { mensaje = "El DNI es obligatorio." });
    if (string.IsNullOrWhiteSpace(req.Usuario) || !System.Text.RegularExpressions.Regex.IsMatch(req.Usuario, "^[a-zA-Z0-9_.-]{3,50}$"))
        return Results.BadRequest(new { mensaje = "El usuario tiene que tener entre 3 y 50 caracteres (letras, números, puntos, guiones o guion bajo)." });
    var errorContrasena = PoliticaContrasena.Validar(req.Contrasena);
    if (errorContrasena != null)
        return Results.BadRequest(new { mensaje = errorContrasena });
    if (string.IsNullOrWhiteSpace(req.Mail) || !EsMailValido(req.Mail))
        return Results.BadRequest(new { mensaje = "El mail es obligatorio y tiene que tener un formato válido." });

    // Avisos por WhatsApp: la casilla NUNCA viene tildada por defecto; solo cuenta un true explícito, con la versión del texto que la persona
    // leyó. Se valida ANTES de crear la cuenta (celular argentino válido, mayor de edad, texto vigente): así no queda una cuenta creada con el
    // registro a medias porque el teléfono no servía.
    // Con la llave Mensajeria:AvisosPorWhatsApp apagada (por defecto) el consentimiento de WhatsApp no existe: no se pide, no se valida y NO se toca la tabla
    // ConsentimientoWhatsApp (se puede publicar la API sin haber aplicado la migración 16). Un cliente que igual mande aceptaWhatsApp:true se registra bien, sin avisos.
    var avisosPorWhatsApp = consentimientoWhatsApp.Habilitado;
    var pideWhatsApp = avisosPorWhatsApp && req.AceptaWhatsApp == true;
    if (pideWhatsApp && consentimientoWhatsApp.ValidarAceptacionEnRegistro(req.Telefono, req.FechaNacimiento, req.VersionTextoWhatsApp) is { } errorWhatsApp)
        return ConsentimientoWhatsAppEndpoints.Error(errorWhatsApp);
    // El teléfono es obligatorio al registrarse (después de lo de WhatsApp, que tiene su propio aviso para el celular),
    // con las mismas reglas que Mis datos: números, espacios, +, paréntesis o guiones.
    if (string.IsNullOrWhiteSpace(req.Telefono) || !System.Text.RegularExpressions.Regex.IsMatch(req.Telefono.Trim(), @"^[\d\s+()-]{6,20}$"))
        return Results.BadRequest(new { mensaje = "El teléfono es obligatorio: usá solo números, espacios, + o guiones." });

    var paciente = new Paciente
    {
        Nombre          = req.Nombre,
        Dni             = req.Dni,
        FechaNacimiento = req.FechaNacimiento,
        Domicilio       = req.Domicilio,
        Telefono        = req.Telefono,
        Mail            = req.Mail
    };
    var passwordHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(req.Contrasena)));

    var (campoConflicto, idPacienteNuevo, idUsuarioNuevo) = new PacienteDAO().RegistrarConAcceso(paciente, req.Usuario, passwordHash);
    if (campoConflicto != null)
        return Results.Json(new { mensaje = MensajesConflictoRegistro.Paciente(campoConflicto) }, statusCode: 409);

    paciente.IdPaciente = idPacienteNuevo;
    paciente.Estado = "Activo";

    // La aceptación queda a nombre del usuario recién creado (la propia persona). Si no se pudo registrar, la cuenta igual existe y puede aceptar
    // desde su perfil: la respuesta lo dice ("pedido" y "estado") para que la pantalla se lo avise.
    var aceptoWhatsApp = pideWhatsApp
        && consentimientoWhatsApp.AceptarEnRegistro(idPacienteNuevo, idUsuarioNuevo, req.Telefono, req.VersionTextoWhatsApp);
    return Results.Created("/pacientes", new
    {
        idPaciente = idPacienteNuevo,
        idUsuario  = idUsuarioNuevo,
        paciente.Nombre, paciente.Dni, paciente.FechaNacimiento,
        paciente.Domicilio, paciente.Telefono, paciente.Mail, paciente.Estado,
        consentimientoWhatsApp = new { pedido = pideWhatsApp, estado = !avisosPorWhatsApp ? "no-disponible" : aceptoWhatsApp ? "acepta" : "sin-decision" }
    });
}).RequireRateLimiting("registro");

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
    var yo = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
    // La baja de la ficha baja también a su usuario (misma transacción y auditoría): ver Services/BajaFichas.cs.
    new BajaFichasDAO().Baja(TipoFicha.Paciente, id, yo?.IdUsuario, yo?.Modulo ?? yo?.Rol);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// ALTA ADMINISTRATIVA: solo reactiva la ficha de un paciente dado de baja. No es el alta CLÍNICA (informe de alta y plan de control, el caso pasa a «En alta»), que es
// POST /casos/{id}/alta-clinica en Endpoints/AltaDerivacionEndpoints.cs.
app.MapPut("/pacientes/{id}/alta", (int id, HttpContext ctx) =>
{
    var yo = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
    var p = new PacienteDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    // El alta de la ficha reactiva también a su usuario (simétrica a la baja, misma transacción).
    if (!new BajaFichasDAO().Alta(TipoFicha.Paciente, id, yo?.IdUsuario, yo?.Modulo ?? yo?.Rol).Existe) return Results.NotFound();
    p.Estado = "Activo";
    return Results.Ok(p);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Profesional
// Cierre de seguridad, Paso 4: el volcado completo (todos los profesionales, con mail y
// teléfono) solo lo usa hoy la tabla del Configurador (confirmado en PANTALLAS/) — pasa a
// Configurador-only. La ficha por id y "Mi perfil" (PUT) exigen sesión; PUT, además, solo
// a su propio dueño o al Configurador (antes, sin chequeo, cualquiera podía reescribir el
// perfil de cualquier profesional). POST sigue público (registro).
// Roles y permisos: también Admisión y el auditor, y CRM (asigna casos y conversaciones a profesionales).
app.MapGet("/profesionales", () =>
    new ProfesionalDAO().ObtenerTodos())
    .Solo(Quien.Configurador | Quien.Admision | Quien.Auditor | Quien.Crm);

app.MapGet("/profesionales/{id}", (int id) =>
{
    var p = new ProfesionalDAO().ObtenerPorId(id);
    return p is null ? Results.NotFound() : Results.Ok(p);
}).RequireAuthorization();

// Matrícula o DNI ya registrados → 409 con un mensaje neutral; especialidad inexistente → 400; antes
// salía todo como 500 (RegistroProfesionalService). Con el mismo límite por IP que el registro de
// paciente: el 409 es una pista de "esto ya existe" y no tiene que poder barrerse a ritmo libre.
app.MapPost("/profesionales", (Profesional p, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    p.Estado = "Pendiente";
    var resultado = new RegistroProfesionalService(new ProfesionalDAO()).Registrar(p);
    if (!resultado.Ok) return Results.Json(new { mensaje = resultado.Mensaje }, statusCode: resultado.StatusCode);
    p.IdProfesional = resultado.IdProfesional;
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Profesional", Entidad = "Profesional", Detalle = $"{p.Nombre} {p.Apellido}" });
    return Results.Created("/profesionales", p);
}).RequireRateLimiting("registro");

app.MapPut("/profesionales/{id}", (int id, Profesional p, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (!yo.EsConfigurador && yo.IdProfesional != id)
        return Results.Json(new { mensaje = "Solo podés editar tu propio perfil." }, statusCode: 403);
    var resultado = new RegistroProfesionalService(new ProfesionalDAO()).Modificar(id, p);
    if (!resultado.Ok) return Results.Json(new { mensaje = resultado.Mensaje }, statusCode: resultado.StatusCode);
    return Results.Ok(p);
}).RequireAuthorization();

app.MapDelete("/profesionales/{id}", (int id, HttpContext ctx) =>
{
    var yo = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
    // La baja de la ficha baja también a su usuario (misma transacción y auditoría): ver Services/BajaFichas.cs.
    new BajaFichasDAO().Baja(TipoFicha.Profesional, id, yo?.IdUsuario, yo?.Modulo ?? yo?.Rol);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Dar de alta/aprobar/rechazar un profesional es administración (Fase JWT: Configurador).
// PUT /profesionales/{id} (arriba, sin tocar) sigue abierto: lo usa el propio profesional
// desde "Mi perfil" — no está en el alcance de este grupo.
app.MapPut("/profesionales/{id}/alta", (int id, HttpContext ctx) =>
{
    var yo = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
    var p = new ProfesionalDAO().ObtenerPorId(id);
    if (p is null) return Results.NotFound();
    // El alta de la ficha reactiva también a su usuario (simétrica a la baja, misma transacción).
    if (!new BajaFichasDAO().Alta(TipoFicha.Profesional, id, yo?.IdUsuario, yo?.Modulo ?? yo?.Rol).Existe) return Results.NotFound();
    p.Estado = "Activo";
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

// Crear el usuario de un profesional aprobado (opción A de docs/PROGRESO_NOCTURNO.md, Tarea 3). El Configurador
// lo hace desde "Pacientes y profesionales" o al aprobar una solicitud. El Usuario y su vínculo se crean en UNA
// transacción (CrearUsuarioProfesionalService): un usuario sin vínculo entra como configurador, así que la
// pantalla no arma esto con POST /usuarios + POST /usuarioprofesionales sueltos. Si se pide una contraseña
// generada, vuelve en la respuesta UNA sola vez (sin caché) y no se guarda en ningún lado en claro.
app.MapPost("/profesionales/{id}/usuario", (int id, CrearUsuarioProfesionalRequest req, HttpContext ctx, TimeProvider reloj) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var servicio = new CrearUsuarioProfesionalService(new UsuarioProfesionalDAO(), reloj);
    var r = servicio.Crear(id, new SolicitudCrearUsuarioProfesional(req.Username, req.Mail, req.Contrasena, req.GenerarContrasena));
    if (!r.Ok) return Results.Json(new { mensaje = r.Mensaje }, statusCode: r.StatusCode);

    var auditoria = new LogAuditoriaDAO();
    auditoria.Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Alta Usuario", Entidad = "Usuario", IdEntidad = r.IdUsuario, Detalle = r.Username });
    auditoria.Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Vinculación Usuario-Profesional", Entidad = "Profesional", IdEntidad = id,
        Detalle = $"usuario {r.Username} (id {r.IdUsuario})" });

    ctx.Response.Headers.CacheControl = "no-store";
    return Results.Json(new
    {
        idUsuario = r.IdUsuario, idProfesional = id, username = r.Username, mail = r.Mail, contrasenaTemporal = r.ContrasenaTemporal
    }, statusCode: 201);
}).RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Ids de los profesionales Activos que todavía no tienen usuario (para mostrar "Crear usuario" solo donde corresponde).
app.MapGet("/profesionales/sin-usuario", () => new UsuarioProfesionalDAO().IdsProfesionalesActivosSinUsuario())
    .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

// Link público de reserva (Fase 3a, Paso 3): el profesional prende/apaga el suyo; el
// administrador puede apagar el de cualquiera. Mismo patrón de identidad que Casos/Grupos
// (X-Usuario-Id sin firmar — TODO(JWT)). Auditado solo con ids, sin texto libre.
app.MapPut("/profesionales/{id}/link-publico", (int id, ActivarLinkPublicoRequest req, HttpContext ctx) =>
{
    // Roles y permisos: el administrador de las cuentas es el Configurador (antes AutorizacionCasos,
    // que ahora resuelve CRM).
    var yoLink = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
    if (yoLink is null || (!yoLink.EsConfigurador && yoLink.IdProfesional != id))
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
    if (!yo!.GestionaAgendas && !yo.HaceAdmision && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tu propia vista previa." }, statusCode: 403);
    var rango = Math.Clamp(dias ?? 14, 1, 90);
    // Tope alto: el calendario de Mis horarios muestra los 90 días completos (24 horarios por día son ~2200).
    return Results.Ok(reglas.HorariosLibresDelProfesional(id, rango, 3000));
}).RequireAuthorization();

// Cierre de seguridad, Paso 3: el profesional solo administra sus propias franjas; quien
// gestiona agendas (roles y permisos: CRM con ese privilegio, antes el Configurador), las de cualquiera. Se aplica igual a /agendas y a su alias /agenda (abajo),
// que comparten el mismo AgendaDAO.
app.MapPost("/agendas", (Agenda a, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (!yo.GestionaAgendas) a.IdProfesional = yo.IdProfesional!.Value;
    // Una franja de admisión (migración 26) la marca quien hace admisión o quien gestiona agendas.
    if (!yo.HaceAdmision && !yo.GestionaAgendas) a.EsAdmision = false;
    if (a.FechaInicio is DateTime desde && a.FechaFin is DateTime hasta && hasta.Date < desde.Date)
        return Results.BadRequest(new { mensaje = "La fecha de fin no puede ser anterior a la de inicio." });
    a.IdAgenda = new AgendaDAO().Insertar(a);
    return Results.Created("/agendas", a);
}).Solo(Quien.Profesional | Quien.Agendas);

app.MapPut("/agendas/{id}", (int id, Agenda a, HttpContext ctx, IdentidadActualService identidad) =>
{
    var dao = new AgendaDAO();
    var existente = dao.ObtenerPorId(id);
    if (existente is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx)!;
    if (!yo.GestionaAgendas && yo.IdProfesional != existente.IdProfesional)
        return Results.Json(new { mensaje = "Ese horario no es tuyo." }, statusCode: 403);

    a.IdAgenda = id;
    if (!yo.GestionaAgendas) a.IdProfesional = existente.IdProfesional;
    if (!yo.HaceAdmision && !yo.GestionaAgendas) a.EsAdmision = existente.EsAdmision;
    if (a.FechaInicio is DateTime desde && a.FechaFin is DateTime hasta && hasta.Date < desde.Date)
        return Results.BadRequest(new { mensaje = "La fecha de fin no puede ser anterior a la de inicio." });
    dao.Modificar(a);
    return Results.Ok(a);
}).Solo(Quien.Profesional | Quien.Agendas);

app.MapDelete("/agendas/{id}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var dao = new AgendaDAO();
    var existente = dao.ObtenerPorId(id);
    if (existente is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx)!;
    if (!yo.GestionaAgendas && yo.IdProfesional != existente.IdProfesional)
        return Results.Json(new { mensaje = "Ese horario no es tuyo." }, statusCode: 403);
    dao.Eliminar(id);
    return Results.NoContent();
}).Solo(Quien.Profesional | Quien.Agendas);

// Alias sin 's' para compatibilidad con el dashboard
app.MapGet("/agenda/{id}", (int id) =>
{
    var a = new AgendaDAO().ObtenerPorId(id);
    return a is null ? Results.NotFound() : Results.Ok(a);
}).RequireAuthorization();

app.MapPost("/agenda", (Agenda a, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (!yo.GestionaAgendas) a.IdProfesional = yo.IdProfesional!.Value;
    // Una franja de admisión (migración 26) la marca quien hace admisión o quien gestiona agendas.
    if (!yo.HaceAdmision && !yo.GestionaAgendas) a.EsAdmision = false;
    if (a.FechaInicio is DateTime desde && a.FechaFin is DateTime hasta && hasta.Date < desde.Date)
        return Results.BadRequest(new { mensaje = "La fecha de fin no puede ser anterior a la de inicio." });
    a.IdAgenda = new AgendaDAO().Insertar(a);
    return Results.Created("/agenda", a);
}).Solo(Quien.Profesional | Quien.Agendas);

app.MapPut("/agenda/{id}", (int id, Agenda a, HttpContext ctx, IdentidadActualService identidad) =>
{
    var dao = new AgendaDAO();
    var existente = dao.ObtenerPorId(id);
    if (existente is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx)!;
    if (!yo.GestionaAgendas && yo.IdProfesional != existente.IdProfesional)
        return Results.Json(new { mensaje = "Ese horario no es tuyo." }, statusCode: 403);

    a.IdAgenda = id;
    if (!yo.GestionaAgendas) a.IdProfesional = existente.IdProfesional;
    if (!yo.HaceAdmision && !yo.GestionaAgendas) a.EsAdmision = existente.EsAdmision;
    if (a.FechaInicio is DateTime desde && a.FechaFin is DateTime hasta && hasta.Date < desde.Date)
        return Results.BadRequest(new { mensaje = "La fecha de fin no puede ser anterior a la de inicio." });
    dao.Modificar(a);
    return Results.Ok(a);
}).Solo(Quien.Profesional | Quien.Agendas);

app.MapDelete("/agenda/{id}", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var dao = new AgendaDAO();
    var existente = dao.ObtenerPorId(id);
    if (existente is null) return Results.NotFound();
    var yo = identidad.Resolver(ctx)!;
    if (!yo.GestionaAgendas && yo.IdProfesional != existente.IdProfesional)
        return Results.Json(new { mensaje = "Ese horario no es tuyo." }, statusCode: 403);
    dao.Eliminar(id);
    return Results.NoContent();
}).Solo(Quien.Profesional | Quien.Agendas);

// Turno
// Fase JWT: el dump completo de /turnos (sin filtrar por nadie) era del Configurador.
// Roles y permisos: pasa a CRM con Gestión de agendas.
app.MapGet("/turnos", () =>
    new TurnoDAO().ObtenerTodos())
    .Solo(Quien.Agendas);

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

app.MapGet("/pacientes/{id}/proximo-turno", (int id, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != id)
        return Results.Json(new { mensaje = "Solo podés ver tu propio turno." }, statusCode: 403);
    // "Hoy" es el de la clínica (el reloj de la aplicación), no el GETDATE() del servidor de base de datos.
    var t = new TurnoDetalleDAO().ObtenerProximoPorPaciente(id, reloj.GetLocalNow().DateTime, excluirTerminados: true);   // sin los que ya pasaron ni los ausentes
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
    if (!yo!.HaceAdmision && !yo.EsAuditor && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propios pacientes." }, statusCode: 403);
    var ids = new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(id);
    var dao = new PacienteDAO();
    return Results.Ok(ids.Select(idPac => dao.ObtenerPorId(idPac)).Where(p => p != null).ToList());
}).RequireAuthorization();

// Cierre de seguridad, Paso 4: asignar/desasignar el equipo tratante de un paciente.
// Roles y permisos: es tarea de Admisión (antes del Configurador). Decide quién accede a la historia
// clínica, así que queda en la auditoría clínica.
app.MapPost("/pacientes/{id}/profesionales", (int id, PacienteProfesional pp, HttpContext ctx) =>
{
    // Sin el consentimiento de tratamiento y telemedicina firmado y vigente no se asigna profesional (ConsentimientoTratamiento; la misma decisión que RN-02).
    if (ConsentimientoTratamiento.BloquearSiNoFirmo(ctx, id) is { } sinConsentimiento) return sinConsentimiento;
    new PacienteProfesionalDAO().Asignar(id, pp.IdProfesional);
    AuditoriaIds.Registrar(ctx, "Asignación de profesional", "PacienteProfesional", pp.IdProfesional, id);
    return Results.Ok();
}).Solo(Quien.Admision);

app.MapDelete("/pacientes/{idPac}/profesionales/{idProf}", (int idPac, int idProf, HttpContext ctx) =>
{
    new PacienteProfesionalDAO().Desasignar(idPac, idProf);
    AuditoriaIds.Registrar(ctx, "Desasignación de profesional", "PacienteProfesional", idProf, idPac);
    return Results.Ok();
}).Solo(Quien.Admision);

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
    if (!yo!.GestionaAgendas && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propios turnos." }, statusCode: 403);
    return Results.Ok(new TurnoDAO().ObtenerPorProfesional(id));
}).RequireAuthorization();

// Cierre de seguridad, Paso 3: sin uso por ningún frontend (se confirmó buscando en todo
// PANTALLAS/) y bypasea todas las reglas de TurnoReglasService (RN-01/07, inserción a
// prueba de doble reserva) — los caminos reales son /turnos/solicitar (paciente) y
// /turnos/profesional-solicitar (profesional). Se apaga en vez de protegerlo: dejarlo
// abierto a cualquier rol autenticado no tendría ningún uso legítimo y solo preservaría un
// atajo que ignora esas reglas.
app.MapPost("/turnos", () => Results.StatusCode(StatusCodes.Status410Gone));

// Solicitud de turno desde el paciente — estado siempre Pendiente. Pasa por
// TurnoReglasService.Solicitar (RN-01/02/07 + inserción a prueba de doble reserva),
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
    var reprogramacion = reglas.ValidarReprogramacion(anterior, t);   // un paciente derivado a psiquiatría no puede recibir un turno nuevo
    if (!reprogramacion.Ok) return reprogramacion.ComoConflicto();
    new TurnoDAO().Modificar(t);
    notificador.CambioDeEstado(id, anterior.IdEstadoTurno);
    return Results.Ok(t);
}).Solo(Quien.Profesional | Quien.Agendas);

// Cierre de seguridad, Paso 4: sin uso por ningún frontend (el cancelar real es
// /turnos/{id}/cancelar). Roles y permisos: de quien gestiona agendas (antes el Configurador).
app.MapDelete("/turnos/{id}", (int id) =>
{
    new TurnoDAO().Baja(id);
    return Results.NoContent();
}).Solo(Quien.Agendas);

// Consentimiento (genérico — distinto del RN-02 de telemedicina en ConsentimientoEndpoints.cs)
// Cierre de seguridad, Paso 4: antes sin ningún chequeo. Ninguna pantalla real de
// PANTALLAS/ usa el volcado completo ni las escrituras. Roles y permisos: salen del Configurador
// (documentos de pacientes): lectura para el auditor y Admisión, escritura para Admisión.
app.MapGet("/consentimientos", () =>
    new ConsentimientoDAO().ObtenerTodos())
    .Solo(Quien.Auditor | Quien.Admision);

app.MapGet("/consentimientos/{id}", (int id) =>
{
    var c = new ConsentimientoDAO().ObtenerPorId(id);
    return c is null ? Results.NotFound() : Results.Ok(c);
}).Solo(Quien.Auditor | Quien.Admision);

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
}).Solo(Quien.Admision);

app.MapPut("/consentimientos/{id}", (int id, Consentimiento c) =>
{
    c.IdConsentimiento = id;
    new ConsentimientoDAO().Modificar(c);
    return Results.Ok(c);
}).Solo(Quien.Admision);

app.MapDelete("/consentimientos/{id}", (int id) =>
{
    new ConsentimientoDAO().Baja(id);
    return Results.NoContent();
}).Solo(Quien.Admision);

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
// Roles y permisos: el Configurador deja de ver datos clínicos. Los volcados
// completos pasan al Profesional auditor, y la lectura por paciente es del tratante, del
// auditor o de Admisión mientras el paciente está en admisión (AutorizacionClinica). El
// auditor lee, no escribe en historias de pacientes que no atiende.
var soloClinico = (RouteHandlerBuilder b) => b.Solo(Quien.Profesional);
var soloAuditor = (RouteHandlerBuilder b) => b.Solo(Quien.Auditor);

soloAuditor(app.MapGet("/historiasclinicas", (HttpContext ctx) =>
{
    // Etapa 2: los listados completos también quedan en la auditoría clínica.
    AuditoriaIds.Registrar(ctx, "Consulta del listado completo de historias clínicas", "HistoriaClinica", 0);
    return new HistoriaClinicaDAO().ObtenerTodos();
}));

soloClinico(app.MapGet("/historiasclinicas/{id}", (int id, HttpContext ctx) =>
{
    var h = new HistoriaClinicaDAO().ObtenerPorId(id);
    if (h is null || !AutorizacionClinica.PuedeVerPaciente(ctx, h.IdPaciente)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Consulta de historia clínica", "HistoriaClinica", id, h.IdPaciente);
    return Results.Ok(h);
}));

soloClinico(app.MapGet("/pacientes/{id}/historiaclinica", (int id, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedeVerPaciente(ctx, id)) return Results.NotFound();
    // Auditoría de acceso a datos clínicos (Paso 6): una vez por apertura de la ficha
    // (esta ruta no la llama ningún polling, solo el "abrir la ficha clínica" real).
    AuditoriaIds.Registrar(ctx, "Apertura de historia clínica", "HistoriaClinica", id, id);
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
soloAuditor(app.MapGet("/evoluciones", (HttpContext ctx) =>
{
    AuditoriaIds.Registrar(ctx, "Consulta del listado completo de evoluciones", "Evolucion", 0);
    return new EvolucionDAO().ObtenerTodos();
}));

soloClinico(app.MapGet("/evoluciones/{id}", (int id, HttpContext ctx) =>
{
    var e = new EvolucionDAO().ObtenerPorId(id);
    var idPaciente = e is null ? null : new HistoriaClinicaDAO().ObtenerPorId(e.IdHistoria)?.IdPaciente;
    if (idPaciente is null || !AutorizacionClinica.PuedeVerPaciente(ctx, idPaciente.Value)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Consulta de evolución", "Evolucion", id, idPaciente);
    return Results.Ok(e);
}));

soloClinico(app.MapGet("/historiasclinicas/{id}/evoluciones", (int id, HttpContext ctx) =>
{
    var idPaciente = new HistoriaClinicaDAO().ObtenerPorId(id)?.IdPaciente;
    if (idPaciente is null || !AutorizacionClinica.PuedeVerPaciente(ctx, idPaciente.Value)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Apertura de evoluciones clínicas", "Evolucion", idPaciente.Value, idPaciente.Value);
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
// Roles y permisos: pasa al privilegio Admisión (cambiar el profesional decide quién ve la historia
// clínica). Los registros de estas acciones son de la auditoría clínica.
app.MapGet("/derivaciones", () =>
    new DerivacionDAO().ObtenerTodos())
    .Solo(Quien.Admision);

app.MapGet("/derivaciones/pendientes", () =>
    new DerivacionDetalleDAO().ObtenerPendientes())
    .Solo(Quien.Admision);

app.MapGet("/derivaciones/detalle", () =>
    new DerivacionDetalleDAO().ObtenerTodosConDetalle())
    .Solo(Quien.Admision);

app.MapGet("/derivaciones/{id}", (int id) =>
{
    var d = new DerivacionDAO().ObtenerPorId(id);
    return d is null ? Results.NotFound() : Results.Ok(d);
}).Solo(Quien.Admision);

app.MapGet("/pacientes/{id}/derivaciones", (int id) =>
    new DerivacionDAO().ObtenerPorPaciente(id))
    .Solo(Quien.Admision);

app.MapPost("/derivaciones", (Derivacion d) =>
{
    d.Fecha  = DateTime.Now;
    d.Estado = "Pendiente";
    d.IdDerivacion = new DerivacionDAO().Insertar(d);
    return Results.Created("/derivaciones", d);
}).Solo(Quien.Admision);

app.MapPut("/derivaciones/{id}", (int id, Derivacion d) =>
{
    // Una derivación nacida del cierre de una sesión (Origen 'Cierre') se resuelve por su propio camino (el destino responde y Admisión confirma el traspaso): esta acción
    // de la «solicitud de cambio» de siempre la cambiaría de vínculo sin pasar por ahí.
    if (DerivacionCierreDAO.EsDelCierre(id))
        return Results.Json(new { mensaje = "Esta derivación nació del cierre de una sesión: se resuelve desde «Traspasos por confirmar» (CRM › Derivaciones), no desde acá." }, statusCode: 409);
    d.IdDerivacion = id;
    new DerivacionDAO().Modificar(d);
    return Results.Ok(d);
}).Solo(Quien.Admision);

app.MapPut("/derivaciones/{id}/aceptar", (int id, AceptarDerivacionRequest req, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var d = new DerivacionDAO().ObtenerPorId(id);
    if (d is null) return Results.NotFound();
    // Una derivación nacida del cierre de una sesión (Origen 'Cierre') se resuelve por su propio camino (el destino responde y Admisión confirma el traspaso): esta acción
    // de la «solicitud de cambio» de siempre la cambiaría de vínculo sin pasar por ahí.
    if (DerivacionCierreDAO.EsDelCierre(id))
        return Results.Json(new { mensaje = "Esta derivación nació del cierre de una sesión: se resuelve desde «Traspasos por confirmar» (CRM › Derivaciones), no desde acá." }, statusCode: 409);
    d.IdProfesionalDestino = req.IdProfesionalDestino;
    d.Estado = "Aceptada";
    new DerivacionDAO().Modificar(d);

    var dao = new PacienteProfesionalDAO();
    dao.Desasignar(d.IdPaciente, d.IdProfesionalOrigen);
    dao.Asignar(d.IdPaciente, req.IdProfesionalDestino);

    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Aceptación Derivación", Entidad = "Derivacion", IdEntidad = id , Categoria = CategoriaAuditoria.Clinica, Espacio = "profesional" });
    SincronizarCasoPorCambioDeProfesional(d.IdPaciente, req.IdProfesionalDestino, auditor);
    return Results.Ok();
}).Solo(Quien.Admision);

app.MapPut("/derivaciones/{id}/rechazar", (int id, HttpContext ctx) =>
{
    int? auditor = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
    var d = new DerivacionDAO().ObtenerPorId(id);
    if (d is null) return Results.NotFound();
    // Una derivación nacida del cierre de una sesión (Origen 'Cierre') se resuelve por su propio camino (el destino responde y Admisión confirma el traspaso): esta acción
    // de la «solicitud de cambio» de siempre la cambiaría de vínculo sin pasar por ahí.
    if (DerivacionCierreDAO.EsDelCierre(id))
        return Results.Json(new { mensaje = "Esta derivación nació del cierre de una sesión: se resuelve desde «Traspasos por confirmar» (CRM › Derivaciones), no desde acá." }, statusCode: 409);
    d.Estado = "Rechazada";
    new DerivacionDAO().Modificar(d);
    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Rechazo Derivación", Entidad = "Derivacion", IdEntidad = id , Categoria = CategoriaAuditoria.Clinica, Espacio = "profesional" });
    return Results.Ok(d);
}).Solo(Quien.Admision);

// Cambio directo de profesional (Admisión): registra derivación aceptada + reasigna turnos futuros
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

    new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = auditor, Accion = "Cambio Directo Profesional", Entidad = "Derivacion", Detalle = req.Motivo , Categoria = CategoriaAuditoria.Clinica, Espacio = "profesional" });
    SincronizarCasoPorCambioDeProfesional(req.IdPaciente, req.IdProfesionalDestino, auditor);
    return Results.Ok();
}).Solo(Quien.Admision);

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
soloAuditor(app.MapGet("/eventoscrisis", (HttpContext ctx) =>
{
    AuditoriaIds.Registrar(ctx, "Consulta del listado completo de eventos de crisis", "EventoCrisis", 0);
    return new EventoCrisisDAO().ObtenerTodos();
}));

soloClinico(app.MapGet("/eventoscrisis/{id}", (int id, HttpContext ctx) =>
{
    var e = new EventoCrisisDAO().ObtenerPorId(id);
    if (e is null || !AutorizacionClinica.PuedeVerPaciente(ctx, e.IdPaciente)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Consulta de evento de crisis", "EventoCrisis", id, e.IdPaciente);
    return Results.Ok(e);
}));

soloClinico(app.MapGet("/pacientes/{id}/eventoscrisis", (int id, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedeVerPaciente(ctx, id)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Apertura de eventos de crisis", "EventoCrisis", id, id);
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
// Roles y permisos: el listado completo pasa del Configurador al Profesional auditor.
soloAuditor(app.MapGet("/interaccionesia", (HttpContext ctx) =>
{
    AuditoriaIds.Registrar(ctx, "Consulta del listado completo de interacciones IA", "InteraccionIA", 0);
    return new InteraccionIADAO().ObtenerTodos();
}));

soloClinico(app.MapGet("/interaccionesia/{id}", (int id, HttpContext ctx) =>
{
    var i = new InteraccionIADAO().ObtenerPorId(id);
    if (i is null || !AutorizacionClinica.PuedeVerPaciente(ctx, i.IdPaciente)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Consulta de interacción IA", "InteraccionIA", id, i.IdPaciente);
    return Results.Ok(i);
}));

soloClinico(app.MapGet("/pacientes/{id}/interaccionesia", (int id, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedeVerPaciente(ctx, id)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Apertura de interacciones IA", "InteraccionIA", id, id);
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
    .Solo(Quien.Profesional);

app.MapPost("/pacientetutores", (PacienteTutor pt) =>
{
    new PacienteTutorDAO().Insertar(pt);
    return Results.Created("/pacientetutores", pt);
}).Solo(Quien.Profesional);

app.MapDelete("/pacientetutores/{idPaciente}/{idTutor}", (int idPaciente, int idTutor) =>
{
    new PacienteTutorDAO().Eliminar(idPaciente, idTutor);
    return Results.NoContent();
}).Solo(Quien.Profesional);

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

// ── Permisos por usuario (roles y permisos) ──────────────────────────
// El Configurador asigna módulos, permisos y privilegios a cada usuario del equipo. Reglas
// y validaciones: Services/AccesosEdicion.cs. Cada cambio queda en la auditoría
// administrativa (lo registra la base, migración 21).
app.MapGet("/usuarios/accesos", () =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    using var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT IdUsuario, Username, Estado, NombreCompleto, Roles, Permisos, Privilegios, Matricula, Especialidad, SinPermisos
        FROM vw_UsuarioAccesos
        WHERE TipoCuenta = 'Equipo'
        ORDER BY Username", con);
    using var rd = cmd.ExecuteReader();
    var lista = new List<object>();
    string? Texto(int i) => rd.IsDBNull(i) ? null : rd.GetValue(i).ToString();
    while (rd.Read())
        lista.Add(new
        {
            idUsuario = rd.GetInt32(0), username = Texto(1), estado = Texto(2), nombreCompleto = Texto(3),
            roles = Texto(4), permisos = Texto(5), privilegios = Texto(6), matricula = Texto(7), especialidad = Texto(8),
            sinPermisos = Convert.ToInt32(rd.GetValue(9)) == 1
        });
    return Results.Ok(lista);
}).Solo(Quien.Configurador);

app.MapGet("/usuarios/{id}/accesos", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var dao = new AccesosEdicionDAO();
    var situacion = dao.LeerSituacion(id, identidad.Resolver(ctx)!.IdUsuario);
    if (situacion is null) return Results.NotFound();
    var (permisos, privilegios) = dao.Asignados(id);
    var catalogo = dao.Catalogo().Where(p => AccesosEdicion.ModulosEditables.Contains(p.Modulo));
    return Results.Ok(new
    {
        idUsuario = id,
        esPaciente = situacion.EsPaciente,
        tieneFichaProfesional = situacion.TieneFichaProfesional,
        esUnoMismo = situacion.EsUnoMismo,
        modulos = catalogo.GroupBy(p => p.Modulo).Select(g => new
        {
            clave = g.Key, nombre = Modulos.Nombre(g.Key), descripcion = Modulos.Descripcion(g.Key),
            permisos = g.Select(p => new
            {
                clave = p.Clave, nombre = p.Nombre,
                privilegios = p.Privilegios.Select(v => new { clave = v.Clave, nombre = v.Nombre, descripcion = v.Descripcion })
            })
        }),
        permisos,
        privilegios = privilegios.Select(p => new { permiso = p.Permiso, privilegio = p.Privilegio })
    });
}).Solo(Quien.Configurador);

app.MapPut("/usuarios/{id}/accesos", (int id, AccesosEdicionRequest pedido, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    var dao = new AccesosEdicionDAO();
    var situacion = dao.LeerSituacion(id, yo.IdUsuario);
    if (situacion is null) return Results.NotFound();
    var motivo = AccesosEdicion.Validar(dao.Catalogo(), pedido, situacion);
    if (motivo is not null) return Results.Json(new { mensaje = motivo }, statusCode: 400);
    dao.Guardar(id, pedido, yo.IdUsuario);
    return Results.NoContent();
}).Solo(Quien.Configurador);

// ── Alta de usuarios del equipo e invitación por mail (18/10) ───────────────
// El Configurador registra a la persona (Services/AltaEquipo.cs); le llega un mail para
// confirmar la cuenta y elegir su contraseña. Si el mail no sale (por ejemplo, sin la
// contraseña del correo en desarrollo), en Development la respuesta trae el link para poder
// probar; en producción nunca: el link es la llave de la cuenta.
app.MapGet("/usuarios/equipo/opciones", () =>
    Results.Ok(new { especialidades = new AltaEquipoDAO().Especialidades() }))
    .Solo(Quien.Configurador);

app.MapPost("/usuarios/equipo", (AltaEquipoRequest pedido, HttpContext ctx, IdentidadActualService identidad,
    EmailService email, IWebHostEnvironment entorno, ILogger<Program> log) =>
{
    var yo = identidad.Resolver(ctx)!;
    var r = AltaEquipo.Normalizar(pedido);
    var motivo = AltaEquipo.Validar(r, new AccesosEdicionDAO().Catalogo());
    if (motivo is not null) return Results.Json(new { mensaje = motivo }, statusCode: 400);
    var dao = new AltaEquipoDAO();
    var conflicto = dao.Conflicto(r);
    if (conflicto is not null) return Results.Json(new { mensaje = conflicto }, statusCode: 409);

    var creada = dao.Crear(r, yo.IdUsuario);
    var envio = EnviarInvitacion(email, entorno, log, r.Mail!, r.Nombre, r.Username!, creada.Token, creada.Vence);
    return Results.Created($"/usuarios/{creada.IdUsuario}", new
    {
        idUsuario = creada.IdUsuario, username = r.Username, mail = r.Mail, vence = creada.Vence,
        mailEnviado = envio.Enviado, motivoMail = envio.Motivo, linkActivacion = envio.LinkVisible
    });
}).Solo(Quien.Configurador);

app.MapPost("/usuarios/{id}/invitacion", (int id, HttpContext ctx, IdentidadActualService identidad,
    EmailService email, IWebHostEnvironment entorno, ILogger<Program> log) =>
{
    var yo = identidad.Resolver(ctx)!;
    var dao = new AltaEquipoDAO();
    var datos = dao.DatosParaMail(id);
    if (datos is null) return Results.NotFound();
    if (string.IsNullOrEmpty(datos.Value.Mail)) return Results.Json(new { mensaje = "La cuenta no tiene email." }, statusCode: 400);
    var nueva = dao.NuevaInvitacion(id, yo.IdUsuario);
    if (nueva is null) return Results.Json(new { mensaje = "La cuenta ya está confirmada: no necesita invitación." }, statusCode: 409);
    var envio = EnviarInvitacion(email, entorno, log, datos.Value.Mail!, datos.Value.Nombre, datos.Value.Username, nueva.Value.Token, nueva.Value.Vence);
    return Results.Ok(new { vence = nueva.Value.Vence, mailEnviado = envio.Enviado, motivoMail = envio.Motivo, linkActivacion = envio.LinkVisible });
}).Solo(Quien.Configurador);

// Públicos (sin sesión: la persona todavía no tiene contraseña), con el mismo límite por IP
// que la reserva pública. El token se busca por su hash; nunca se guarda ni se registra.
app.MapGet("/cuentas/invitacion", (string? token) =>
{
    if (string.IsNullOrWhiteSpace(token)) return Results.NotFound();
    var inv = new AltaEquipoDAO().Buscar(token);
    if (inv is null) return Results.Json(new { mensaje = "El link no es válido. Pedile al administrador que te reenvíe la invitación." }, statusCode: 404);
    var motivo = MotivoInvitacionNoUsable(inv);
    if (motivo is not null) return Results.Json(new { mensaje = motivo }, statusCode: 410);
    return Results.Ok(new { nombre = inv.Nombre, username = inv.Username, vence = inv.Vence });
}).RequireRateLimiting("publico");

app.MapPost("/cuentas/activar", (ActivarCuentaRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Token)) return Results.Json(new { mensaje = "El link no es válido." }, statusCode: 404);
    var errorClave = AltaEquipo.ValidarContrasena(req.Password);
    if (errorClave is not null) return Results.Json(new { mensaje = errorClave }, statusCode: 400);
    var dao = new AltaEquipoDAO();
    var inv = dao.Buscar(req.Token);
    if (inv is null) return Results.Json(new { mensaje = "El link no es válido. Pedile al administrador que te reenvíe la invitación." }, statusCode: 404);
    var motivo = MotivoInvitacionNoUsable(inv);
    if (motivo is not null || !dao.Activar(inv, AltaEquipo.HashContrasena(req.Password!)))
        return Results.Json(new { mensaje = motivo ?? "El link ya no sirve. Pedile al administrador que te reenvíe la invitación." }, statusCode: 410);
    return Results.Ok(new { username = inv.Username, mensaje = "Listo, tu cuenta quedó confirmada. Ya podés ingresar." });
}).RequireRateLimiting("publico");

static string? MotivoInvitacionNoUsable(InvitacionInfo inv) =>
    inv.Usada is not null || inv.EstadoCuenta != "Pendiente" ? "Esta cuenta ya está confirmada. Ingresá con tu usuario y contraseña."
    : inv.Anulada is not null ? "Este link fue reemplazado por uno más nuevo. Usá el último mail que recibiste."
    : inv.Vence <= DateTime.Now ? "El link venció. Pedile al administrador que te reenvíe la invitación."
    : null;

static (bool Enviado, string? Motivo, string? LinkVisible) EnviarInvitacion(EmailService email, IWebHostEnvironment entorno, ILogger log,
    string mail, string? nombre, string username, string token, DateTime vence)
{
    var link = $"{email.AppUrl}/pantallas/activar-cuenta.html?token={Uri.EscapeDataString(token)}";
    try
    {
        // false = correo sin configurar (EmailService lo deja en el log, sin abrir ninguna conexión): la cuenta quedó creada y la invitación se puede reenviar.
        if (!email.EnviarInvitacionEquipo(mail, nombre, username, link, vence))
            return (false, "No se pudo enviar el mail (el correo no está configurado en el servidor).", entorno.IsDevelopment() ? link : null);
        return (true, null, null);
    }
    catch (Exception ex)
    {
        // Sin el token en el registro: solo el motivo.
        log.LogWarning("No se pudo enviar la invitación a {Usuario}: {Motivo}", username, ex.Message);
        return (false, "No se pudo enviar el mail (revisá la configuración del correo).", entorno.IsDevelopment() ? link : null);
    }
}

// ── Privilegios (18/10): a quién se le agrega cada uno ─────────
// Admisión se agrega a quien tiene Profesional o Profesional auditor; Gestión de agendas, a
// quien tiene CRM. Mismas reglas y la misma auditoría que el editor de permisos.
app.MapGet("/privilegios", () =>
{
    var catalogo = new AccesosEdicionDAO().Catalogo().Where(p => AccesosEdicion.ModulosEditables.Contains(p.Modulo)).ToList();
    var personas = new List<(int Id, string Username, string? Nombre, string Estado, string Permiso)>();
    var tienen = new HashSet<(int, string)>();
    using (var con = Conexion.ObtenerConexion())
    {
        con.Open();
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
            SELECT u.IdUsuario, u.Username, LTRIM(RTRIM(COALESCE(u.Apellido + N', ', N'') + COALESCE(u.Nombre, N''))), ISNULL(u.Estado, ''), p.Clave
            FROM UsuarioPermiso up
            JOIN Permiso p ON p.IdPermiso = up.IdPermiso AND p.Estado = 'Activo'
            JOIN Usuario u ON u.IdUsuario = up.IdUsuario
            ORDER BY u.Username", con))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read()) personas.Add((rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetString(3), rd.GetString(4)));
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
            SELECT up.IdUsuario, pv.Clave FROM UsuarioPrivilegio up JOIN Privilegio pv ON pv.IdPrivilegio = up.IdPrivilegio", con))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read()) tienen.Add((rd.GetInt32(0), rd.GetString(1)));
    }
    var privilegios = catalogo.SelectMany(p => p.Privilegios).GroupBy(v => v.Clave).Select(g => g.First());
    return Results.Ok(privilegios.Select(v =>
    {
        var admiten = catalogo.Where(p => p.Privilegios.Any(x => x.Clave == v.Clave)).ToList();
        var elegibles = personas.Where(x => admiten.Any(p => p.Clave == x.Permiso))
            .GroupBy(x => x.Id).Select(g => new
            {
                idUsuario = g.Key, username = g.First().Username, nombreCompleto = g.First().Nombre, estado = g.First().Estado,
                permiso = admiten.First(p => g.Any(x => x.Permiso == p.Clave)).Nombre
            }).ToList();
        return new
        {
            clave = v.Clave, nombre = v.Nombre, descripcion = v.Descripcion,
            permisos = admiten.Select(p => new { clave = p.Clave, nombre = p.Nombre }),
            loTienen = elegibles.Where(e => tienen.Contains((e.idUsuario, v.Clave))),
            candidatos = elegibles.Where(e => !tienen.Contains((e.idUsuario, v.Clave)))
        };
    }));
}).Solo(Quien.Configurador);

app.MapPost("/usuarios/{id}/privilegios", (int id, AgregarPrivilegioRequest req, HttpContext ctx, IdentidadActualService identidad) =>
    CambiarPrivilegio(id, req.Privilegio, agregar: true, identidad.Resolver(ctx)!.IdUsuario))
    .Solo(Quien.Configurador);

app.MapDelete("/usuarios/{id}/privilegios/{clave}", (int id, string clave, HttpContext ctx, IdentidadActualService identidad) =>
    CambiarPrivilegio(id, clave, agregar: false, identidad.Resolver(ctx)!.IdUsuario))
    .Solo(Quien.Configurador);

static IResult CambiarPrivilegio(int idUsuario, string? clave, bool agregar, int idAutor)
{
    var dao = new AccesosEdicionDAO();
    var situacion = dao.LeerSituacion(idUsuario, idAutor);
    if (situacion is null) return Results.NotFound();
    var catalogo = dao.Catalogo();
    var editables = catalogo.Where(p => AccesosEdicion.ModulosEditables.Contains(p.Modulo)).Select(p => p.Clave).ToHashSet();
    var (permisos, privilegios) = dao.Asignados(idUsuario);
    permisos = permisos.Where(editables.Contains).ToList();
    privilegios = privilegios.Where(p => editables.Contains(p.Permiso)).ToList();
    var nombre = catalogo.SelectMany(p => p.Privilegios).FirstOrDefault(v => v.Clave == clave)?.Nombre;
    if (nombre is null) return Results.Json(new { mensaje = "Ese privilegio no existe." }, statusCode: 404);

    if (agregar)
    {
        if (privilegios.Any(p => p.Privilegio == clave)) return Results.Json(new { mensaje = $"Ya tiene el privilegio {nombre}." }, statusCode: 409);
        var sobre = catalogo.FirstOrDefault(p => permisos.Contains(p.Clave) && p.Privilegios.Any(v => v.Clave == clave));
        if (sobre is null) return Results.Json(new { mensaje = $"Este usuario no tiene un rol que admita {nombre}." }, statusCode: 400);
        privilegios.Add(new PrivilegioPedido(sobre.Clave, clave!));
    }
    else
    {
        if (!privilegios.Any(p => p.Privilegio == clave)) return Results.NoContent();
        privilegios = privilegios.Where(p => p.Privilegio != clave).ToList();
    }

    var pedido = new AccesosEdicionRequest(permisos, privilegios);
    var motivo = AccesosEdicion.Validar(catalogo, pedido, situacion);
    if (motivo is not null) return Results.Json(new { mensaje = motivo }, statusCode: 400);
    dao.Guardar(idUsuario, pedido, idAutor);
    return Results.NoContent();
}

// Roles y permisos: la auditoría se ve en dos partes y nadie la puede modificar (no hay PUT ni
// DELETE, y la base lo impide con TR_LogAuditoria_SoloAgregar). El Configurador ve la
// administrativa (cuentas, ingresos, cambios de permisos y configuración); el Profesional
// auditor, la clínica (accesos a historias, asignaciones, derivaciones, reportes). La
// categoría la decide el servidor según el token, nunca un parámetro.
app.MapGet("/auditoria", (HttpContext ctx, IdentidadActualService identidad, int pagina = 1, int cantidad = 50) =>
{
    var yo = identidad.Resolver(ctx)!;
    var categoria = yo.EsConfigurador ? CategoriaAuditoria.Administrativa : CategoriaAuditoria.Clinica;
    return new LogAuditoriaDAO().ObtenerPaginado(Math.Max(pagina, 1), Math.Clamp(cantidad, 1, 200), categoria);
}).Solo(Quien.Configurador | Quien.Auditor);

// Recuperación de contraseña, autoservicio: un link de un solo uso por mail, vence a los
// 30 minutos. Sin aprobación de nadie en el medio. Respuesta siempre igual exista o no el
// usuario, tenga o no Mail, haya pasado o no el límite de pedidos — nunca se revela cuál
// de esos casos fue. Una cuenta sin Usuario.Mail no tiene autoservicio (no hay a dónde
// mandarle nada); se la atiende aparte desde "Gestión de usuarios" (PUT /usuarios/{id}).
app.MapPost("/recuperacion-password", (RecuperacionPasswordRequest req, EmailService emailService) =>
{
    var respuestaGenerica = Results.Ok(new { mensaje = "Si el usuario existe, te llegó un mail con instrucciones." });

    var usuario = new UsuarioDAO().ObtenerPorUsername(req.Username);
    if (usuario == null || string.IsNullOrWhiteSpace(usuario.Mail)) return respuestaGenerica;

    var dao = new RecuperacionPasswordDAO();
    if (dao.ContarRecientes(usuario.IdUsuario, TimeSpan.FromMinutes(15)) >= 3) return respuestaGenerica;

    var tokenCrudo = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tokenCrudo)));
    dao.CrearToken(usuario.IdUsuario, tokenHash, DateTime.Now.AddMinutes(RecuperacionPasswordDAO.DuracionTokenMinutos));

    try { emailService.EnviarResetPassword(usuario.Mail, tokenCrudo, RecuperacionPasswordDAO.DuracionTokenMinutos); }
    catch { /* la fila ya quedó creada; no se revela el fallo, el link simplemente no va a llegar */ }

    return respuestaGenerica;
}).RequireRateLimiting("recuperacion");

app.MapPost("/recuperacion-password/reset", (ResetPasswordRequest req) =>
{
    // Antes no se comprobaba nada acá (solo la pantalla): con un token válido se podía dejar una contraseña
    // vacía. Se valida primero, así una contraseña inválida no consume el token.
    var errorContrasena = PoliticaContrasena.Validar(req.NuevaPassword);
    if (errorContrasena != null) return Results.BadRequest(new { mensaje = errorContrasena });

    var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(req.Token ?? "")));
    var dao = new RecuperacionPasswordDAO();
    var idUsuario = dao.ObtenerIdUsuarioPorTokenVigente(tokenHash);
    if (idUsuario == null) return Results.BadRequest(new { mensaje = "Link inválido o vencido. Pedí uno nuevo." });

    var uDAO = new UsuarioDAO();
    var usuario = uDAO.ObtenerPorId(idUsuario.Value);
    if (usuario == null) return Results.BadRequest(new { mensaje = "Link inválido o vencido. Pedí uno nuevo." });

    var hashNuevo = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(req.NuevaPassword ?? "")));
    uDAO.GuardarPasswordRecuperada(usuario.IdUsuario, hashNuevo);
    uDAO.ResetearIntentos(idUsuario.Value);
    dao.MarcarUsado(tokenHash);

    return Results.Ok(new { mensaje = "Contraseña actualizada. Ya podés ingresar con la nueva." });
}).RequireRateLimiting("recuperacion");

// SesionClinica
// PR 6 (consentimiento de la lectura de emociones): el pedido ya no se enlaza directo a la entidad. Las emociones solo se
// guardan con una aceptación vigente del paciente de esa sesión de video (GuardiaEmocionesService) y siempre cifradas; si no
// pasan la guardia, la sesión se guarda igual (notas, tipo de cierre) y la respuesta dice que las emociones se descartaron.
soloClinico(app.MapPost("/sesiones-clinicas", (SesionClinicaRequest req, HttpContext ctx, IdentidadActualService identidad,
    GuardiaEmocionesService guardia, IEmocionesCifrado cifrado, TurnoReglasService reglas) =>
{
    // Mismo criterio que turnos (Paso 3): si quien crea es profesional, la sesión queda a
    // su propio nombre, nunca al de otro id enviado en el cuerpo.
    var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
    if (!esAdmin && idPropio is null)
        return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
    if (!AutorizacionClinica.PuedePaciente(ctx, req.IdPaciente))
        return Results.Json(new { mensaje = "Ese paciente no está asociado a tu atención." }, statusCode: 403);
    var idProfesional = esAdmin ? req.IdProfesional ?? 0 : idPropio!.Value;
    if (idProfesional <= 0)
        return Results.BadRequest(new { mensaje = "Indicá el profesional de la sesión." });

    // Cierre de sesión (Endpoints/CierreSesionEndpoints.cs): con claveCierre o confirmaRealizacion, todo en una transacción.
    if (CierreSesionEndpoints.EsPedidoDeCierre(req))
        return CierreSesionEndpoints.Guardar(req, ctx, identidad, guardia, cifrado, reglas, idProfesional, esAdmin);

    var s = new SesionClinica
    {
        IdTurno = req.IdTurno,
        IdPaciente = req.IdPaciente,
        IdProfesional = idProfesional,
        FechaHora = DateTime.Now,
        Transcripcion = req.Transcripcion,
        NotasEvolucion = req.NotasEvolucion,
        TipoCierre = req.TipoCierre
    };

    var yo = identidad.Resolver(ctx)!;
    var ev = guardia.Evaluar(yo, req.IdPaciente, req.Emociones, vinoElFormatoViejo: !string.IsNullOrWhiteSpace(req.HistorialEmociones));
    if (ev.Aprobada) s.IdConsentimientoVideo = ev.IdConsentimiento;
    var insertada = new SesionClinicaDAO().Insertar(s, ev.Aprobada
        ? idSesion => cifrado.Cifrar(ev.JsonPlano!, EmocionesCifradoService.DatosAsociadosDeSesion(idSesion))
        : null);
    int id = insertada.IdSesion;
    // La escritura vuelve a mirar el consentimiento: si el paciente revocó justo en el medio, la sesión quedó sin emociones.
    ev = GuardiaEmocionesService.TrasLaEscritura(ev, insertada.EmocionesGuardadas);
    guardia.Registrar(yo, ev, id);

    return Results.Created("/sesiones-clinicas", new
    {
        idSesion = id,
        idTurno = s.IdTurno,
        idPaciente = s.IdPaciente,
        idProfesional = s.IdProfesional,
        fechaHora = s.FechaHora,
        tipoCierre = s.TipoCierre,
        emociones = new
        {
            estado = ev.Estado,
            motivo = ev.Motivo,
            mensaje = ev.Estado == EvaluacionEmociones.Descartadas
                ? "La sesión se guardó sin las emociones: " + GuardiaEmocionesService.MensajeDeDescarte(ev.Motivo) + "."
                : null
        }
    });
}));

// Lectura de sesiones clínicas: el Configurador NO recibe las emociones (HistorialEmociones) aunque su pantalla nunca las
// mostró; el profesional las recibe descifradas (VistaSesionesClinicas). Ver docs/VIDEOLLAMADAS.md.
soloClinico(app.MapGet("/pacientes/{id}/sesiones-clinicas", (int id, HttpContext ctx, VistaSesionesClinicas vista) =>
{
    if (!AutorizacionClinica.PuedeVerPaciente(ctx, id)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Apertura de sesiones clínicas", "SesionClinica", id, id);
    // Las emociones (cifradas, PR 6) solo las ve quien atiende al paciente: el auditor lee las notas, no las emociones.
    return Results.Ok(vista.Armar(new SesionClinicaDAO().ObtenerPorPaciente(id), puedeVerEmociones: AutorizacionClinica.PuedePaciente(ctx, id)));
}));

soloClinico(app.MapGet("/profesionales/{id}/sesiones-clinicas", (int id, HttpContext ctx, VistaSesionesClinicas vista) =>
{
    var yo = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)!;
    if (!yo.EsAuditor && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propias sesiones." }, statusCode: 403);
    if (yo.IdProfesional != id) AuditoriaIds.Registrar(ctx, "Consulta de sesiones de un profesional", "SesionClinica", id);
    // Las emociones solo las ve el propio profesional; el auditor que consulta a otro no las recibe.
    return Results.Ok(vista.Armar(new SesionClinicaDAO().ObtenerPorProfesional(id), puedeVerEmociones: yo.IdProfesional == id));
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
    // Roles y permisos: la admisión asistida la hace Admisión (antes el Configurador).
    if (!yo.HaceAdmision) i.IdPaciente = yo.IdPaciente!.Value;
    if (PrimerosPasosEndpoints.RechazarFormularioNuevo(i) is { } rechazo) return rechazo;   // un solo formulario por paciente + preferencias válidas

    var id = new InformeAdmisionDAO().Insertar(i);
    i.IdInforme = id;
    AsignacionAutomaticaService.AvisarFormularioNuevo(id, i.IdPaciente);   // la campanita de la recepción: un aviso por formulario

    // Caso nuevo para el seguimiento de la Fase 2: aditivo, no cambia esta respuesta ni
    // el resto del flujo de admisión. Si la especialidad solicitada tiene una regla de
    // derivación activa, sugiere grupo y profesional; si no, queda "Nuevo" sin grupo,
    // en la cola del administrador (CAMBIOS_AGUS.md, sección Fase 2).
    var idCaso = new CasoDAO().Insertar(new Caso { IdPaciente = i.IdPaciente, Estado = "Nuevo", Origen = "Admision", IdInforme = id });
    var idGrupo = new ReglaDerivacionDAO().ObtenerGrupoPorEspecialidad(i.IdEspecialidad);
    if (idGrupo is not null)
        asignacion.SugerirDerivacion(idCaso, idGrupo.Value, "Regla", idCreadoPor: null);

    return Results.Created($"/informes-admision/{id}", i);
}).Solo(Quien.Paciente | Quien.Admision);

// Sin uso directo por ninguna pantalla (confirmado en PANTALLAS/): queda para el
// Configurador, igual que /pacientes o /profesionales en bloque.
app.MapGet("/informes-admision", (string? estado) =>
    new InformeAdmisionDAO().ObtenerTodos(estado))
    .Solo(Quien.Admision | Quien.Auditor);

app.MapGet("/informes-admision/{id}", (int id, HttpContext ctx) =>
{
    var i = new InformeAdmisionDAO().ObtenerPorId(id);
    if (i is null) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Consulta de informe de admisión", "InformeAdmision", id, i.IdPaciente);
    return Results.Ok(i);
}).Solo(Quien.Admision | Quien.Auditor);

app.MapGet("/pacientes/{id}/informe-admision", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo is { EsPaciente: true } && yo.IdPaciente != id)
        return Results.Json(new { mensaje = "Solo podés ver tu propio informe de admisión." }, statusCode: 403);
    // Roles y permisos: del equipo, solo Admisión, el auditor o quien atiende al paciente. Al resto
    // (Configurador, CRM, otro profesional), lo mismo que si no existiera.
    if (!yo.EsPaciente && !yo.HaceAdmision && !AutorizacionClinica.PuedeVerPaciente(ctx, id))
        return Results.NotFound();
    var i = new InformeAdmisionDAO().ObtenerPorPaciente(id);
    if (i is null) return Results.NotFound();
    // Etapa 2: lectura del informe por alguien del equipo → auditoría clínica.
    if (!yo.EsPaciente) AuditoriaIds.Registrar(ctx, "Consulta de informe de admisión", "InformeAdmision", i.IdInforme, id);
    // Para la pantalla Admisión del paciente: cuándo y con quién fue la entrevista y a quién lo derivaron
    // (las observaciones de la entrevista no van).
    using var con = Conexion.ObtenerConexion();
    con.Open();
    using var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT ia.FechaEntrevista, ia.CodigoSala, ia.AclaracionRecepcion, ia.MotivoRechazo, ia.HorarioPreferido, ia.ProfesionalesPreferidos,
               LTRIM(RTRIM(ent.Nombre + ' ' + ISNULL(ent.Apellido, ''))) AS NombreProfesionalEntrevista,
               LTRIM(RTRIM(der.Nombre + ' ' + ISNULL(der.Apellido, ''))) AS NombreProfesionalDerivado
        FROM InformeAdmision ia
        LEFT JOIN Profesional ent ON ent.IdProfesional = ia.IdProfesionalEntrevista
        LEFT JOIN Profesional der ON der.IdProfesional = ia.IdProfesionalDerivado
        WHERE ia.IdInforme = @Id", con);
    cmd.Parameters.AddWithValue("@Id", i.IdInforme);
    using var r = cmd.ExecuteReader();
    r.Read();
    string? Texto(string col) => r[col] == DBNull.Value ? null : r[col].ToString();
    return Results.Ok(new
    {
        i.IdInforme, i.IdPaciente, i.IdEspecialidad, i.MotivoConsulta, i.TratamientoAnterior, i.TratamientoAnteriorDetalle,
        i.RecibeMedicacion, i.MedicacionDetalle, i.GustosIntereses, i.PreferenciasAtencion, i.TipoEntrevista, i.RedApoyo,
        i.ObjetivosPaciente, i.Estado, i.FechaCreacion,
        fechaEntrevista = r["FechaEntrevista"] == DBNull.Value ? null : ((DateTime)r["FechaEntrevista"]).ToString("dd/MM/yyyy HH:mm"),
        // El código de su propia sala de entrevista (el mismo que le llega a la campanita).
        codigoSala = Texto("CodigoSala"),
        // Primeros pasos: lo que la recepción aclaró al devolverlo, el motivo si cerró el ingreso y las preferencias que el paciente cargó.
        aclaracionRecepcion = Texto("AclaracionRecepcion"),
        motivoCierre = i.Estado == EstadoInforme.OtroRecurso ? Texto("MotivoRechazo") : null,
        horarioPreferido = PreferenciasAdmision.Leer(Texto("HorarioPreferido")),
        profesionalesPreferidos = PreferenciasAdmision.Leer(Texto("ProfesionalesPreferidos")),
        nombreProfesionalEntrevista = Texto("NombreProfesionalEntrevista"),
        nombreProfesionalDerivado = Texto("NombreProfesionalDerivado")
    });
}).RequireAuthorization();

// Sin uso directo por ninguna pantalla: Configurador, mismo criterio que el resto de este
// bloque administrativo.
app.MapPut("/informes-admision/{id}/estado", (int id, string estado) =>
{
    new InformeAdmisionDAO().CambiarEstado(id, estado);
    return Results.NoContent();
}).Solo(Quien.Admision);


// ── Admision: endpoints de Admisión (roles y permisos; antes del configurador) ──
app.MapGet("/informes-admision-detalle", (HttpContext ctxConsentimiento, TimeProvider relojAsignacion) =>
{
    // (Admisión y auditor: ver .Solo() al final de este endpoint.)
    // Propuestas automáticas (migración 32): las vencidas pasan a «Asignaciones para resolver» antes de listar, y se junta la pendiente de cada formulario.
    AsignacionAutomaticaService.VencerVencidas(relojAsignacion.GetLocalNow().DateTime);
    var propuestasPendientes = AsignacionAutomaticaService.PendientesPorInforme();
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT ia.IdInforme, ia.IdPaciente, p.Nombre AS NombrePaciente, p.Dni, p.FechaNacimiento,
               e.Nombre AS Especialidad, ia.MotivoConsulta, ia.TratamientoAnterior, ia.TratamientoAnteriorDetalle,
               ia.RecibeMedicacion, ia.MedicacionDetalle, ia.GustosIntereses, ia.PreferenciasAtencion,
               ia.TipoEntrevista, ia.RedApoyo, ia.ObjetivosPaciente, ia.Estado, ia.FechaCreacion,
               ia.FechaEntrevista, ia.ObservacionesEntrevista, ia.IdProfesionalDerivado, ia.CodigoSala, ia.MotivoRechazo,
               prof.Nombre + ' ' + ISNULL(prof.Apellido, '') AS NombreProfesionalDerivado,
               ent.Nombre + ' ' + ISNULL(ent.Apellido, '') AS NombreProfesionalEntrevista,
               ia.AclaracionRecepcion, ia.HorarioPreferido, ia.ProfesionalesPreferidos, " +
               (AsignacionAutomaticaService.Disponible() ? "ia.AsignacionEstado, ia.AsignacionMotivo"
                   : "CAST(NULL AS NVARCHAR(20)) AS AsignacionEstado, CAST(NULL AS NVARCHAR(500)) AS AsignacionMotivo") + @"
        FROM InformeAdmision ia
        INNER JOIN Paciente p ON p.IdPaciente = ia.IdPaciente
        INNER JOIN Especialidad e ON e.IdEspecialidad = ia.IdEspecialidad
        LEFT JOIN Profesional prof ON prof.IdProfesional = ia.IdProfesionalDerivado
        LEFT JOIN Profesional ent ON ent.IdProfesional = ia.IdProfesionalEntrevista
        ORDER BY ia.FechaCreacion DESC", con);
    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    // Estado del consentimiento de tratamiento y telemedicina de cada paciente (la misma decisión que RN-02 y que el requisito de asignar), una consulta por paciente.
    var opConsentimiento = ctxConsentimiento.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ConsentimientoOptions>>().CurrentValue;
    var consentimientoDe = new System.Collections.Generic.Dictionary<int, EstadoTratamiento>();
    var nombresProfesionales = PreferenciasAdmision.ProfesionalesElegibles().ToDictionary(p => p.IdProfesional, p => p.Nombre + (string.IsNullOrWhiteSpace(p.Especialidad) ? "" : " · " + p.Especialidad));
    while (reader.Read())
    {
        var idPacienteFila = (int)reader["IdPaciente"];
        if (!consentimientoDe.TryGetValue(idPacienteFila, out var estadoConsentimiento))
            consentimientoDe[idPacienteFila] = estadoConsentimiento = ConsentimientoTratamiento.Estado(idPacienteFila, opConsentimiento);
        lista.Add(new {
            idInforme               = (int)reader["IdInforme"],
            idPaciente              = (int)reader["IdPaciente"],
            consentimientoFirmado   = estadoConsentimiento.Vigente,
            consentimientoMotivo    = estadoConsentimiento.Vigente ? null : estadoConsentimiento.Motivo,
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
            nombreProfesionalDerivado = reader["NombreProfesionalDerivado"] == DBNull.Value ? "" : reader["NombreProfesionalDerivado"].ToString(),
            // Primeros pasos (migración 31): lo que aclaró la recepción al devolverlo y las preferencias del paciente (solo se muestran; el cruce con las agendas es un trabajo siguiente).
            aclaracionRecepcion     = reader["AclaracionRecepcion"] == DBNull.Value ? "" : reader["AclaracionRecepcion"].ToString(),
            horarioPreferido        = PreferenciasAdmision.Leer(reader["HorarioPreferido"] as string),
            profesionalesPreferidos = PreferenciasAdmision.Leer(reader["ProfesionalesPreferidos"] as string),
            profesionalesPreferidosNombres = ((reader["ProfesionalesPreferidos"] as string) is { } pj && PreferenciasAdmision.Leer(pj) is { } pe && pe.TryGetProperty("ids", out var pids)
                ? pids.EnumerateArray().Select(x => nombresProfesionales.TryGetValue(x.GetInt32(), out var n) ? n : "Profesional #" + x.GetInt32()).ToList() : new System.Collections.Generic.List<string>()),
            // Propuesta automática (migración 32): dónde quedó la asignación («Propuesta» o «Por resolver», null = nunca pasó por ahí), por qué, y la propuesta que espera al paciente.
            asignacionEstado        = reader["AsignacionEstado"] as string,
            asignacionMotivo        = reader["AsignacionMotivo"] as string,
            propuestaPendiente      = propuestasPendientes.TryGetValue((int)reader["IdInforme"], out var propPend)
                ? new { profesional = propPend.Profesional, fechaHora = propPend.FechaHora.ToString("dd/MM/yyyy HH:mm"), expiraEn = propPend.ExpiraEn.ToString("dd/MM/yyyy HH:mm"), nro = propPend.Nro } : null,
            // Quién programó la entrevista (migración 26); "" en las de antes.
            nombreProfesionalEntrevista = reader["NombreProfesionalEntrevista"] == DBNull.Value ? "" : reader["NombreProfesionalEntrevista"].ToString()!.Trim()
        });
    }
    return Results.Ok(lista);
}).Solo(Quien.Admision | Quien.Auditor | Quien.Crm);   // la recepcionista (CRM) revisa los formularios «Sobre mí»

// Programa la primera entrevista de admisión en forma AUTOMÁTICA: el primer horario libre de las franjas de admisión
// de quien la programa (Mis horarios de trabajo, migración 26), dentro de las 24 horas de recibida la solicitud
// (PlazoEntrevistaAdmision). Libre = sin turno ni otra entrevista suya a esa hora, así quedan una detrás de otra. Queda
// guardado quién la programó (la grilla lo muestra) y al paciente le llega el aviso con el día y la hora.
app.MapPut("/informes-admision/{id}/entrevista", (int id, EntrevistaRequest req, HttpContext ctx, IdentidadActualService identidad,
    TurnoReglasService reglas, TimeProvider reloj) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo.IdProfesional is not int idProfesional)
        return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
    if (!reglas.TieneAgendaDeAdmision(idProfesional))
        return Results.Json(new { codigo = "SIN_AGENDA_ADMISION",
            mensaje = "Para programar entrevistas tenés que tener un horario de admisión en Mis horarios de trabajo." }, statusCode: 409);

    using var con = Conexion.ObtenerConexion();
    con.Open();

    // De a una: dos profesionales de admisión que programan al mismo tiempo no pueden tomar el mismo horario ni la misma
    // admisión. El bloqueo es de toda la organización y dura lo que esta transacción (milisegundos): el segundo espera a
    // que el primero termine y recién ahí busca su horario, ya con la entrevista del primero guardada.
    using var tx = con.BeginTransaction();
    using (var cmdLock = new Microsoft.Data.SqlClient.SqlCommand(@"
        DECLARE @r INT;
        EXEC @r = sp_getapplock @Resource = N'YoSoy.ProgramarEntrevistaAdmision', @LockMode = 'Exclusive',
                                @LockOwner = 'Transaction', @LockTimeout = 15000;
        SELECT @r;", con, tx))
    {
        if ((int)cmdLock.ExecuteScalar()! < 0)
            return Results.Json(new { mensaje = "Otro profesional de admisión está programando una entrevista. Probá de nuevo en unos segundos." }, statusCode: 503);
    }

    DateTime recibida; string estado;
    using (var cmdInf = new Microsoft.Data.SqlClient.SqlCommand("SELECT FechaCreacion, Estado FROM InformeAdmision WHERE IdInforme = @Id", con, tx))
    {
        cmdInf.Parameters.AddWithValue("@Id", id);
        using var r = cmdInf.ExecuteReader();
        if (!r.Read()) return Results.NotFound();
        recibida = (DateTime)r["FechaCreacion"];
        estado = r["Estado"]?.ToString() ?? "";
    }
    if (estado != "Pendiente")
        return Results.Json(new { mensaje = "Esta admisión ya no está pendiente: la entrevista ya se programó o se resolvió." }, statusCode: 409);

    var ahora = reloj.GetLocalNow().DateTime;
    var limite = PlazoEntrevistaAdmision.Limite(recibida, ahora);
    if (reglas.PrimerHorarioDeAdmision(idProfesional, limite) is not DateTime fecha)
        return Results.Json(new { codigo = "SIN_HORARIO_ADMISION",
            mensaje = $"No tenés horarios de admisión libres hasta el {limite:dd/MM/yyyy} a las {limite:HH:mm} hs (las 24 horas desde la solicitud). Agregá horarios en Mis horarios de trabajo." },
            statusCode: 409);

    // Generar codigo de sala unico para la entrevista
    var codigoSala = "ADM-" + id + "-" + DateTime.Now.Ticks.ToString("x").ToUpper();

    // Solo si sigue Pendiente: si otro la programó en el medio, no se pisa.
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE InformeAdmision
        SET FechaEntrevista = @Fecha, ObservacionesEntrevista = @Obs, Estado = 'En entrevista', CodigoSala = @Sala,
            IdProfesionalEntrevista = @IdProf
        OUTPUT inserted.IdPaciente
        WHERE IdInforme = @Id AND Estado = 'Pendiente';", con, tx);
    cmd.Parameters.AddWithValue("@Fecha", fecha);
    cmd.Parameters.AddWithValue("@Obs", (object?)req.Observaciones ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Sala", codigoSala);
    cmd.Parameters.AddWithValue("@IdProf", idProfesional);
    cmd.Parameters.AddWithValue("@Id", id);
    if (cmd.ExecuteScalar() is not int idPac)
        return Results.Json(new { mensaje = "Esta admisión ya no está pendiente: la entrevista ya se programó o se resolvió." }, statusCode: 409);

    var es = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
    var fechaLegible = fecha.ToString("dddd dd/MM/yyyy", es) + " a las " + fecha.ToString("HH:mm") + " hs";
    var cmdNotif = new Microsoft.Data.SqlClient.SqlCommand(@"
        INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link)
        VALUES (@IdPac, @Titulo, @Mensaje, @Link)", con, tx);
    cmdNotif.Parameters.AddWithValue("@IdPac", idPac);
    cmdNotif.Parameters.AddWithValue("@Titulo", "Entrevista de admisión programada");
    cmdNotif.Parameters.AddWithValue("@Mensaje", "Tu entrevista de admisión es el " + fechaLegible + ". Código de sala: " + codigoSala);
    cmdNotif.Parameters.AddWithValue("@Link", codigoSala);
    cmdNotif.ExecuteNonQuery();
    tx.Commit();   // libera el bloqueo: recién ahora puede programar el siguiente

    return Results.Ok(new { codigoSala, fecha = fecha.ToString("dd/MM/yyyy HH:mm") });
}).Solo(Quien.Admision);

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

// A quién se le puede derivar una admisión: profesionales activos con horarios de atención configurados (franjas
// disponibles de turnos, no de admisión). Es la lista de "Profesional asignado" del panel de Admisión.
app.MapGet("/informes-admision/profesionales-derivables", () =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT p.IdProfesional, p.Nombre, p.Apellido, e.Nombre AS Especialidad
        FROM Profesional p
        LEFT JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
        WHERE p.Estado = 'Activo'
          AND EXISTS (SELECT 1 FROM Agenda a
                      WHERE a.IdProfesional = p.IdProfesional AND a.Disponible = 1 AND a.EsAdmision = 0
                        AND a.Dia IS NOT NULL AND a.HoraInicio IS NOT NULL AND a.HoraFin IS NOT NULL
                        AND (a.FechaFin IS NULL OR a.FechaFin >= CAST(GETDATE() AS DATE)))
        ORDER BY p.Nombre, p.Apellido", con);
    using var r = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (r.Read())
        lista.Add(new
        {
            idProfesional = (int)r["IdProfesional"],
            nombre = r["Nombre"].ToString(),
            apellido = r["Apellido"] == DBNull.Value ? "" : r["Apellido"].ToString(),
            especialidad = r["Especialidad"] == DBNull.Value ? "" : r["Especialidad"].ToString()
        });
    return Results.Ok(lista);
}).Solo(Quien.Admision | Quien.Crm);

app.MapPut("/informes-admision/{id}/derivar", (int id, DerivarRequest req, HttpContext ctx, AsignacionService asignacion,
    TurnoReglasService reglas, TimeProvider reloj) =>
{
    // Solo a un profesional con horarios de atención: si no, el paciente queda asignado sin dónde pedir su primer turno.
    if (!reglas.TieneAgendaDeTurnos(req.IdProfesional))
        return Results.Json(new { codigo = "SIN_AGENDA",
            mensaje = "Ese profesional no tiene horarios de atención configurados: no se le puede derivar un paciente." }, statusCode: 409);

    using var con = Conexion.ObtenerConexion();
    con.Open();

    // El paciente del informe y si la entrevista ya se hizo (DerivacionAdmision): programada, con su horario ya llegado
    // y con las observaciones cargadas (las que vienen ahora o las que ya estaban guardadas).
    int idPac; string? estado, obsGuardadas; DateTime? fechaEntrevista;
    using (var cmdGet = new Microsoft.Data.SqlClient.SqlCommand(
        "SELECT IdPaciente, Estado, FechaEntrevista, ObservacionesEntrevista FROM InformeAdmision WHERE IdInforme = @Id", con))
    {
        cmdGet.Parameters.AddWithValue("@Id", id);
        using var r = cmdGet.ExecuteReader();
        if (!r.Read()) return Results.NotFound();
        idPac = (int)r["IdPaciente"];
        estado = r["Estado"] as string;
        fechaEntrevista = r["FechaEntrevista"] as DateTime?;
        obsGuardadas = r["ObservacionesEntrevista"] as string;
    }
    var noSePuede = DerivacionAdmision.Validar(estado, fechaEntrevista,
        string.IsNullOrWhiteSpace(req.Observaciones) ? obsGuardadas : req.Observaciones, reloj.GetLocalNow().DateTime);
    if (noSePuede is { } falta)
        return Results.Json(new { codigo = falta.Codigo, mensaje = falta.Mensaje }, statusCode: 409);

    // Sin el consentimiento de tratamiento y telemedicina firmado y vigente no se deriva (no se asigna profesional): se rechaza ANTES de tocar el informe y se le avisa al paciente.
    if (ConsentimientoTratamiento.BloquearSiNoFirmo(ctx, idPac) is { } sinConsentimiento) return sinConsentimiento;

    // Asignación a mano: cualquier propuesta automática pendiente de este paciente se cancela y su horario queda libre (migración 32). Va antes de asignar: así el paciente no puede
    // aceptar una propuesta mientras la recepción lo está asignando.
    AsignacionAutomaticaService.CancelarPendientesPorAsignacionManual(idPac, ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario, reloj.GetLocalNow().DateTime);

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
    AsignacionAutomaticaService.LimpiarAlAsignar(id);   // ya tiene profesional: el formulario deja de figurar en «Asignaciones para resolver» (migración 32)

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

    // Primera evolucion en la HC a nombre del admisionista (se muestra en azul). Solo en el camino viejo, con entrevista: un formulario aceptado (recorrido actual) no abre
    // ni crea la historia clínica, y no hubo ninguna entrevista que registrar.
    if (estado != EstadoInforme.Aprobado)
    {
    PrimeraEvolucionAdmision.Insertar(con, idPac, req.IdProfesional, req.Observaciones);
    }

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
}).Solo(Quien.Admision | Quien.Crm);

// ── Mis Pacientes con estado de asignacion ──────────────────
app.MapGet("/profesionales/{id}/pacientes-detalle", (int id, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx);
    if (!yo!.HaceAdmision && !yo.EsAuditor && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propios pacientes." }, statusCode: 403);

    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT p.IdPaciente, p.Nombre, p.Dni, p.Mail, p.Telefono,
               ISNULL(pp.EstadoAsignacion, 'Confirmada') AS EstadoAsignacion, pp.FechaAsignacion,
               (SELECT TOP 1 c.Estado FROM Caso c WHERE c.IdPaciente = p.IdPaciente ORDER BY c.IdCaso DESC) AS EstadoCaso
        FROM PacienteProfesional pp
        INNER JOIN Paciente p ON p.IdPaciente = pp.IdPaciente
        WHERE pp.IdProfesional = @Id AND ISNULL(pp.EstadoAsignacion, 'Confirmada') <> 'Rechazada'
        ORDER BY pp.FechaAsignacion DESC", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var reader = cmd.ExecuteReader();
    var filas = new System.Collections.Generic.List<(int IdPaciente, string Nombre, string Dni, string Mail, string Telefono, string EstadoAsignacion, string Fecha, string? EstadoCaso)>();
    while (reader.Read())
        filas.Add(((int)reader["IdPaciente"], reader["Nombre"].ToString()!, reader["Dni"] == DBNull.Value ? "" : reader["Dni"].ToString()!,
            reader["Mail"] == DBNull.Value ? "" : reader["Mail"].ToString()!, reader["Telefono"] == DBNull.Value ? "" : reader["Telefono"].ToString()!,
            reader["EstadoAsignacion"].ToString()!, reader["FechaAsignacion"] == DBNull.Value ? "" : ((DateTime)reader["FechaAsignacion"]).ToString("dd/MM/yyyy"),
            reader["EstadoCaso"] as string));
    reader.Close();
    // «Derivado» (a psiquiatría, migración 34) y «En alta» se ven en la lista con un distintivo. Sin la migración, nadie figura derivado.
    var derivados = DerivacionPsiquiatraDAO.Derivados(filas.Select(f => f.IdPaciente));
    var lista = new System.Collections.Generic.List<object>();
    foreach (var f in filas)
    {
        lista.Add(new {
            idPaciente       = f.IdPaciente,
            nombre           = f.Nombre,
            dni              = f.Dni,
            mail             = f.Mail,
            telefono         = f.Telefono,
            estadoAsignacion = f.EstadoAsignacion,
            fechaAsignacion  = f.Fecha,
            estadoCaso       = f.EstadoCaso,
            derivado         = derivados.Contains(f.IdPaciente),
            enAlta           = f.EstadoCaso == AltaDerivacionReglas.CasoEnAlta
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
}).Solo(Quien.Profesional);

// ── Primer turno de una asignación nueva (Mis pacientes › Confirmar) ─
// El horario no se elige: es el primer libre de la agenda del profesional (TurnoReglasService.PrimerHorarioLibre), así
// los primeros turnos quedan uno detrás de otro, sin huecos. El GET muestra la grilla de horarios libres con el propuesto
// marcado; el POST lo vuelve a calcular en el momento (lo que se ve puede haberse ocupado) y crea el turno.
static bool EsAsignacionNueva(Microsoft.Data.SqlClient.SqlConnection con, Microsoft.Data.SqlClient.SqlTransaction? tx, int idPaciente, int idProfesional)
{
    using var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT COUNT(*) FROM PacienteProfesional
        WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf AND EstadoAsignacion = 'Nueva'", con, tx);
    cmd.Parameters.AddWithValue("@IdPac", idPaciente);
    cmd.Parameters.AddWithValue("@IdProf", idProfesional);
    return (int)cmd.ExecuteScalar()! > 0;
}

app.MapGet("/asignaciones/{idPaciente}/primer-turno", (int idPaciente, TurnoReglasService reglas, HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo.IdProfesional is not int idProfesional)
        return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
    using var con = Conexion.ObtenerConexion();
    con.Open();
    if (!EsAsignacionNueva(con, null, idPaciente, idProfesional))
        return Results.Json(new { mensaje = "Este paciente no tiene una asignación nueva con vos." }, statusCode: 409);
    return Results.Ok(new
    {
        propuesto = reglas.PrimerHorarioLibre(idPaciente, idProfesional),
        // Los mismos días en que se busca el primer horario libre: el calendario de la propuesta los muestra todos.
        libres = reglas.HorariosLibres(idPaciente, idProfesional, TurnoReglasService.DiasPrimerTurno, 3000)
    });
}).Solo(Quien.Profesional);

app.MapPost("/asignaciones/{idPaciente}/primer-turno", (int idPaciente, INotificadorTurnos notificador, TurnoReglasService reglas,
    HttpContext ctx, IdentidadActualService identidad) =>
{
    var yo = identidad.Resolver(ctx)!;
    if (yo.IdProfesional is not int idProfesional)
        return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);

    using var con = Conexion.ObtenerConexion();
    con.Open();

    // De a una por profesional: si confirma dos pacientes casi a la vez, el segundo espera y toma el horario siguiente
    // en vez de chocar con el primero.
    using var tx = con.BeginTransaction();
    using (var cmdLock = new Microsoft.Data.SqlClient.SqlCommand(@"
        DECLARE @r INT;
        EXEC @r = sp_getapplock @Resource = @Recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
        SELECT @r;", con, tx))
    {
        cmdLock.Parameters.AddWithValue("@Recurso", "YoSoy.PrimerTurno." + idProfesional);
        if ((int)cmdLock.ExecuteScalar()! < 0)
            return Results.Json(new { mensaje = "Se está programando otro turno tuyo. Probá de nuevo en unos segundos." }, statusCode: 503);
    }

    if (!EsAsignacionNueva(con, tx, idPaciente, idProfesional))
        return Results.Json(new { mensaje = "Este paciente ya no tiene una asignación nueva con vos: la propuesta ya se envió o se resolvió." }, statusCode: 409);
    if (reglas.PrimerHorarioLibre(idPaciente, idProfesional) is not DateTime momento)
        return Results.Json(new { codigo = "SIN_HORARIO",
            mensaje = $"No tenés horarios libres en los próximos {TurnoReglasService.DiasPrimerTurno} días. Agregá horarios en Mis horarios de trabajo." },
            statusCode: 409);

    var resultado = reglas.Solicitar(idPaciente, idProfesional, momento, "Primer turno - derivacion de admision", creadoPor: "profesional");
    if (!resultado.Regla.Ok) return resultado.Regla.ComoConflicto();

    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE PacienteProfesional SET EstadoAsignacion = 'Confirmada'
        WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf", con, tx))
    {
        cmd.Parameters.AddWithValue("@IdPac", idPaciente);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.ExecuteNonQuery();
    }
    tx.Commit();

    notificador.TurnoAgendadoPorProfesional(idPaciente, idProfesional, momento.Date, momento.TimeOfDay);
    return Results.Created("/turnos", new { idTurno = resultado.IdTurno, fecha = momento });
}).Solo(Quien.Profesional);

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
}).Solo(Quien.Profesional);


// Turno propuesto por el profesional (queda pendiente de confirmacion del paciente)
// El profesional propone un turno futuro: pasa por todas las reglas (antes, ninguna).
// Cita urgente (EsUrgente=true, "ahora mismo") usa SolicitarUrgente, que no le pide RN-01
// pero sí inserta a prueba de doble reserva — ver TurnoReglasService (Fase 3a, Paso 1).
app.MapPost("/turnos/profesional-solicitar", (TurnoProfesionalRequest req, INotificadorTurnos notificador, TurnoReglasService reglas, HttpContext ctx, IdentidadActualService identidad) =>
{
    // Cierre de seguridad, Paso 3: un profesional solo propone turnos a su propio nombre,
    // nunca al de otro id enviado en el cuerpo; el Configurador sí puede elegir cualquiera
    // (agenda en nombre de un profesional, como ya hacía).
    var yo = identidad.Resolver(ctx)!;
    var idProfesional = yo.GestionaAgendas ? req.IdProfesional : yo.IdProfesional!.Value;   // roles y permisos: antes el Configurador

    // Cita urgente = "ahora mismo": la hora la pone el reloj de la clínica, no la PC del profesional (con la PC mal de
    // hora o en otra zona, el turno quedaba a una hora que no era y la sala no abría). Un turno futuro trae su fecha y
    // su hora, que se leen tal como están escritas (MomentoDeTurno).
    DateTime momento;
    if (req.EsUrgente) momento = MomentoDeTurno.DeCitaUrgente(reglas.Ahora);
    else if (!MomentoDeTurno.TryParsear(req.Fecha, req.Hora, out momento))
        return Results.BadRequest(new { mensaje = "La fecha o la hora del turno no tienen un formato válido." });
    var resultado = req.EsUrgente
        ? reglas.SolicitarUrgente(req.IdPaciente, idProfesional, momento, req.Motivo)
        : reglas.Solicitar(req.IdPaciente, idProfesional, momento, req.Motivo, creadoPor: "profesional");
    if (!resultado.Regla.Ok) return resultado.Regla.ComoConflicto();
    notificador.TurnoAgendadoPorProfesional(req.IdPaciente, idProfesional, momento.Date, momento.TimeOfDay);
    return Results.Created("/turnos", new { idTurno = resultado.IdTurno });
}).Solo(Quien.Profesional | Quien.Agendas);


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

    // Lo que el paciente pidió por su cuenta no lo confirma él: lo acepta el profesional (POST /turnos/{id}/aceptar-solicitud). Este camino es para el turno que PROPUSO el profesional.
    if (yo is { EsPaciente: true } && string.Equals(turno.CreadoPor, "paciente", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { mensaje = "Tu solicitud todavía espera la aceptación de tu profesional. Te avisamos apenas la responda." }, statusCode: 409);

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
    if (!yo!.GestionaAgendas && yo.IdProfesional != id) return Results.Json(new { mensaje = "Solo podés ver tus propios turnos." }, statusCode: 403);

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

    // Ni siquiera una cita urgente se saltea el consentimiento: sin el de tratamiento y telemedicina firmado y vigente no se habilita la sala. Queda el bloqueo a la vista del profesional
    // y el paciente recibe el aviso para firmar (campanita y mail).
    if (ConsentimientoTratamiento.BloquearSiNoFirmo(ctx, turno.IdPaciente) is { } sinConsentimiento)
    {
        BloqueoConsentimiento.Registrar(ctx.RequestServices.GetRequiredService<INotificacionInternaDAO>(), turno.IdProfesional, id, new PacienteDAO().ObtenerPorId(turno.IdPaciente)?.Nombre ?? "El paciente",
            "todavía no firmó el consentimiento de tratamiento y telemedicina.");
        return sinConsentimiento;
    }

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
}).Solo(Quien.Profesional | Quien.Agendas);

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
app.MapCierreSesion();
app.MapAltaDerivacion();
app.MapDerivacionPsiquiatra();
app.MapSolicitudesTurno();
app.MapDisponibilidad();
app.MapEstadoSala();
app.MapAgendarTurno();
app.MapAusenciasAlta();
app.MapVideo();
app.MapConsentimientoVideo();
app.MapAceptacionVideollamada();
app.MapConsentimientoWhatsApp();
app.MapUsuariosAdmin();
app.MapPrimerosPasos();
app.MapAsignacionAutomatica();

// ═══════════════════════════════════════════════════════
// REPORTES DEL PROFESIONAL
// ═══════════════════════════════════════════════════════
// Roles y permisos: estos seis endpoints no exigían sesión — cualquiera, sin ingresar, podía pedir
// las emociones registradas en las sesiones de cualquier paciente. Ahora: el propio
// profesional, o el Profesional auditor (que queda en la auditoría clínica). Ninguna pantalla
// los usa todavía, así que no se rompe nada.
static IResult? ReporteAjeno(HttpContext ctx, int idProfesional)
{
    var yo = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
    if (yo is null) return Results.Unauthorized();
    if (yo.IdProfesional == idProfesional) return null;
    if (!yo.EsAuditor) return Results.Json(new { mensaje = "Solo podés ver tus propios reportes." }, statusCode: 403);
    AuditoriaIds.Registrar(ctx, "Consulta de reporte de un profesional", "Reporte", idProfesional);
    return null;
}

// KPIs generales del profesional
app.MapGet("/reportes/profesional/{id}/kpis", (int id, HttpContext ctx) =>
{
    if (ReporteAjeno(ctx, id) is { } rechazo) return rechazo;
    using var con = Conexion.ObtenerConexion();
    con.Open();

    var kpis = new Dictionary<string, object>();

    // Sesiones este mes
    var cmd1 = new SqlCommand(@"
        SELECT COUNT(*) FROM SesionClinica 
        WHERE IdProfesional = @Id 
        AND MONTH(FechaHora) = MONTH(GETDATE()) 
        AND YEAR(FechaHora) = YEAR(GETDATE())", con);
    cmd1.Parameters.AddWithValue("@Id", id);
    kpis["sesionesMes"] = Convert.ToInt32(cmd1.ExecuteScalar());

    // Pacientes activos
    var cmd2 = new SqlCommand(@"
        SELECT COUNT(*) FROM PacienteProfesional 
        WHERE IdProfesional = @Id AND EstadoAsignacion = 'Confirmada'", con);
    cmd2.Parameters.AddWithValue("@Id", id);
    kpis["pacientesActivos"] = Convert.ToInt32(cmd2.ExecuteScalar());

    // Tasa de asistencia (realizados / confirmados)
    var cmd3 = new SqlCommand(@"
        SELECT 
            SUM(CASE WHEN IdEstadoTurno = 4 THEN 1 ELSE 0 END) AS Realizados,
            SUM(CASE WHEN IdEstadoTurno IN (2,4) THEN 1 ELSE 0 END) AS Confirmados
        FROM Turno WHERE IdProfesional = @Id", con);
    cmd3.Parameters.AddWithValue("@Id", id);
    using (var r = cmd3.ExecuteReader())
    {
        if (r.Read())
        {
            int realizados  = r["Realizados"]  == DBNull.Value ? 0 : Convert.ToInt32(r["Realizados"]);
            int confirmados = r["Confirmados"] == DBNull.Value ? 0 : Convert.ToInt32(r["Confirmados"]);
            kpis["tasaAsistencia"] = confirmados > 0 ? Math.Round((double)realizados / confirmados * 100, 1) : 0;
        }
    }

    // Sesiones con alta
    var cmd4 = new SqlCommand(@"
        SELECT COUNT(*) FROM SesionClinica 
        WHERE IdProfesional = @Id AND TipoCierre = 'Alta'", con);
    cmd4.Parameters.AddWithValue("@Id", id);
    kpis["sesionesAlta"] = Convert.ToInt32(cmd4.ExecuteScalar());

    // Total sesiones históricas
    var cmd5 = new SqlCommand(@"
        SELECT COUNT(*) FROM SesionClinica WHERE IdProfesional = @Id", con);
    cmd5.Parameters.AddWithValue("@Id", id);
    kpis["totalSesiones"] = Convert.ToInt32(cmd5.ExecuteScalar());

    return Results.Ok(kpis);
}).Solo(Quien.Profesional);

// Sesiones por mes (últimos 6 meses)
app.MapGet("/reportes/profesional/{id}/sesiones-por-mes", (int id, HttpContext ctx) =>
{
    if (ReporteAjeno(ctx, id) is { } rechazo) return rechazo;
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new SqlCommand(@"
        SELECT YEAR(FechaHora) AS Anio, MONTH(FechaHora) AS Mes, COUNT(*) AS Total
        FROM SesionClinica
        WHERE IdProfesional = @Id AND FechaHora >= DATEADD(MONTH, -6, GETDATE())
        GROUP BY YEAR(FechaHora), MONTH(FechaHora)
        ORDER BY Anio, Mes", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var lista = new List<object>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
        lista.Add(new { anio = (int)r["Anio"], mes = (int)r["Mes"], total = (int)r["Total"] });
    return Results.Ok(lista);
}).Solo(Quien.Profesional);

// Pacientes por tipo de cierre
app.MapGet("/reportes/profesional/{id}/pacientes-por-cierre", (int id, HttpContext ctx) =>
{
    if (ReporteAjeno(ctx, id) is { } rechazo) return rechazo;
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new SqlCommand(@"
        SELECT ISNULL(TipoCierre, 'En seguimiento') AS Cierre, COUNT(*) AS Total
        FROM SesionClinica WHERE IdProfesional = @Id
        GROUP BY TipoCierre", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var lista = new List<object>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
        lista.Add(new { cierre = (string)r["Cierre"], total = (int)r["Total"] });
    return Results.Ok(lista);
}).Solo(Quien.Profesional);

// Emociones globales del profesional (promedio de todas sus sesiones)
app.MapGet("/reportes/profesional/{id}/emociones-globales", (int id, HttpContext ctx) =>
{
    if (ReporteAjeno(ctx, id) is { } rechazo) return rechazo;
    var sesiones = new SesionClinicaDAO().ObtenerPorProfesional(id);
    var acumulado = new Dictionary<string, double>();
    int cuenta = 0;

    foreach (var s in sesiones)
    {
        if (string.IsNullOrEmpty(s.HistorialEmociones)) continue;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(s.HistorialEmociones);
            foreach (var emo in doc.RootElement.EnumerateObject())
            {
                var val = emo.Value.GetDouble();
                acumulado[emo.Name] = acumulado.GetValueOrDefault(emo.Name, 0) + val;
            }
            cuenta++;
        }
        catch { }
    }

    var promedio = new Dictionary<string, double>();
    if (cuenta > 0)
        foreach (var kv in acumulado)
            promedio[kv.Key] = Math.Round(kv.Value / cuenta, 1);

    return Results.Ok(promedio);
}).Solo(Quien.Profesional);

// Evolución emocional de un paciente (sesión a sesión)
app.MapGet("/reportes/paciente/{id}/evolucion-emocional", (int id, HttpContext ctx) =>
{
    if (!AutorizacionClinica.PuedeVerPaciente(ctx, id)) return Results.NotFound();
    AuditoriaIds.Registrar(ctx, "Consulta de evolución emocional", "SesionClinica", id, id);
    var sesiones = new SesionClinicaDAO().ObtenerPorPaciente(id);
    var lista = new List<object>();

    foreach (var s in sesiones.OrderBy(x => x.FechaHora))
    {
        if (string.IsNullOrEmpty(s.HistorialEmociones)) continue;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(s.HistorialEmociones);
            var emociones = new Dictionary<string, double>();
            foreach (var emo in doc.RootElement.EnumerateObject())
                emociones[emo.Name] = emo.Value.GetDouble();
            lista.Add(new {
                fecha = s.FechaHora.ToString("dd/MM/yyyy"),
                emociones
            });
        }
        catch { }

    }
    return Results.Ok(lista);
}).Solo(Quien.Profesional);

// Lista de pacientes del profesional (para el selector)
app.MapGet("/reportes/profesional/{id}/lista-pacientes", (int id, HttpContext ctx) =>
{
    if (ReporteAjeno(ctx, id) is { } rechazo) return rechazo;
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new SqlCommand(@"
        SELECT p.IdPaciente, p.Nombre
        FROM Paciente p
        INNER JOIN PacienteProfesional pp ON p.IdPaciente = pp.IdPaciente
        WHERE pp.IdProfesional = @Id AND pp.EstadoAsignacion = 'Confirmada'", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var lista = new List<object>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
        lista.Add(new { idPaciente = (int)r["IdPaciente"], nombre = (string)r["Nombre"] });
    return Results.Ok(lista);
}).Solo(Quien.Profesional);


app.Run();

// La fecha ya no viene del cliente: la asigna la API desde la agenda de admisión (una "fecha" en el cuerpo se ignora).
public record EntrevistaRequest(string? Observaciones);
public record DerivarRequest(int IdProfesional, string Observaciones);

public record AsignacionEstadoRequest(int IdPaciente, int IdProfesional, string Estado);
public record RechazoRequest(int IdPaciente, int IdProfesional, string Motivo);

public record TurnoProfesionalRequest(int IdPaciente, int IdProfesional, string Fecha, string Hora, string Motivo, bool EsUrgente = false);
public record ActivarLinkPublicoRequest(bool Activo);
public record ReservaPublicaRequest(int IdPaciente, DateTime Momento, string? Motivo);

// Expone la clase Program (autogenerada por los top-level statements) para WebApplicationFactory<Program>.
public partial class Program { }
