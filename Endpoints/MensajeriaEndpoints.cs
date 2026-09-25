using System.Net;
using Microsoft.Extensions.Options;

// Endpoints del módulo de mensajería: chat simulado (modo paciente y modo
// administrador), preguntas frecuentes y avisos internos. Se registran con
// app.MapMensajeria() en Program.cs.
//
// ADVERTENCIA: el backend todavía no tiene autenticación real y estos endpoints
// exponen datos sensibles de pacientes (Ley 25.326). Ver RequiereMensajeriaHabilitada.
// No publicar esto en una URL pública.
public static class MensajeriaEndpoints
{
    public static IEndpointRouteBuilder MapMensajeria(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/mensajeria").RequiereMensajeriaHabilitada();

        // ── Chat simulado: lado paciente ────────────────────────
        g.MapPost("/simulado/entrante", async (MensajeEntranteRequest req, BotReservasService bot, IMessageChannel canal) =>
        {
            try
            {
                var r = await bot.ProcesarAsync(req.Telefono ?? "", req.Texto ?? "", canal.Nombre);
                return Results.Ok(r);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        });

        g.MapGet("/simulado/conversacion", (string telefono, IConversacionDAO conversaciones, IMessageChannel canal) =>
        {
            var c = conversaciones.ObtenerPorTelefono(Telefono.Normalizar(telefono), canal.Nombre);
            return c is null ? Results.NotFound() : Results.Ok(new { c.IdConversacion, c.Estado });
        });

        // ── Modo administrador ──────────────────────────────────
        g.MapGet("/conversaciones", (string? estado, IConversacionDAO conversaciones) =>
            conversaciones.ListarResumen(string.IsNullOrWhiteSpace(estado) ? null : estado));

        g.MapGet("/conversaciones/{id}", (int id, IConversacionDAO conversaciones) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            return c is null
                ? Results.NotFound()
                : Results.Ok(new { c.IdConversacion, c.Telefono, c.IdPaciente, c.Canal, c.Estado, c.PasoActual, c.FechaUltimaActividad });
        });

        // Panel derecho del modo administrador. Solo los datos que necesita la vista
        // (sin domicilio ni fecha de nacimiento). Profesionales y próximo turno salen
        // de las mismas consultas que usa el bot ("pedir turno" y "ver mis turnos").
        g.MapGet("/conversaciones/{id}/paciente", (int id, IConversacionDAO conversaciones,
            IDatosMensajeria datos, TimeProvider reloj) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null) return Results.NotFound();
            var p = c.IdPaciente is int idPac ? datos.ObtenerPaciente(idPac) : null;
            if (p is null)
                return Results.Ok(new { c.IdConversacion, c.Telefono, c.Estado, paciente = (object?)null,
                                        profesionales = Array.Empty<object>(), proximoTurno = (object?)null });

            var proximo = datos.TurnosProximos(p.IdPaciente, reloj.GetLocalNow().DateTime).FirstOrDefault();
            return Results.Ok(new
            {
                c.IdConversacion, c.Telefono, c.Estado,
                paciente      = new { p.IdPaciente, p.Nombre, p.Dni, p.Telefono },
                profesionales = datos.ProfesionalesDelPaciente(p.IdPaciente)
                                     .Select(pr => new { pr.IdProfesional, pr.Nombre, pr.Especialidad }),
                proximoTurno  = proximo is null ? null : new
                {
                    proximo.IdTurno, proximo.Momento, proximo.NombreProfesional,
                    Modalidad = proximo.Modalidad ?? "Virtual", proximo.Estado
                }
            });
        });

        // desde = último IdMensaje que ya tiene la pantalla (polling incremental).
        g.MapGet("/conversaciones/{id}/mensajes", (int id, int? desde, IMensajeChatDAO mensajes) =>
            mensajes.ObtenerPorConversacion(id, desde ?? 0));

        g.MapPost("/conversaciones/{id}/responder", async (int id, RespuestaHumanaRequest req, HttpContext http,
            IConversacionDAO conversaciones, IServicioMensajes mensajes, TimeProvider reloj) =>
        {
            var texto = (req.Texto ?? "").Trim();
            if (texto.Length == 0) return Results.BadRequest(new { mensaje = "El mensaje está vacío." });
            var c = conversaciones.ObtenerPorId(id);
            if (c is null) return Results.NotFound();

            var m = await mensajes.EnviarAsync(c, texto, "Humano");
            c.FechaUltimaActividad = reloj.GetLocalNow().DateTime;
            conversaciones.Guardar(c);
            Auditar(http, "Respuesta manual en chat", id);
            return Results.Ok(m);
        });

        // Cierra la derivación (o la urgencia): el bot vuelve a atender desde el menú.
        g.MapPut("/conversaciones/{id}/cerrar", async (int id, HttpContext http,
            IConversacionDAO conversaciones, IServicioMensajes mensajes, TimeProvider reloj) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null) return Results.NotFound();
            c.Estado               = EstadoConversacion.Cerrada;
            c.PasoActual           = c.IdPaciente is null ? PasoBot.Inicio : PasoBot.Menu;
            c.DatosContexto        = null;
            c.FechaUltimaActividad = reloj.GetLocalNow().DateTime;
            conversaciones.Guardar(c);
            await mensajes.EnviarAsync(c,
                "El equipo de YoSoy cerró esta conversación. Si necesitás algo más, escribí \"menu\" y el asistente te va a ayudar.",
                "Sistema");
            Auditar(http, "Cierre de conversación de chat", id);
            return Results.Ok(new { c.IdConversacion, c.Estado });
        });

        g.MapGet("/notificaciones-internas", (string? destinatario, bool? soloNoLeidas) =>
            new NotificacionInternaDAO().Listar(string.IsNullOrWhiteSpace(destinatario) ? null : destinatario, soloNoLeidas ?? false));

        g.MapPut("/notificaciones-internas/{id}/leida", (int id) =>
        {
            new NotificacionInternaDAO().MarcarLeida(id);
            return Results.NoContent();
        });

        // Dispara una pasada del servicio de recordatorios sin esperar el intervalo (pruebas y demo).
        g.MapPost("/recordatorios/ejecutar", (RecordatorioBackgroundService servicio) =>
            Results.Ok(new { turnosProcesados = servicio.EjecutarUnaVez() }));

        // ── ABM de preguntas frecuentes ─────────────────────────
        g.MapGet("/preguntas-frecuentes", () => new PreguntaFrecuenteDAO().ObtenerTodas());

        g.MapPost("/preguntas-frecuentes", (PreguntaFrecuente p) =>
        {
            var error = ValidarPregunta(p);
            if (error != null) return Results.BadRequest(new { mensaje = error });
            new PreguntaFrecuenteDAO().Insertar(p);
            return Results.Created($"/mensajeria/preguntas-frecuentes/{p.IdPregunta}", p);
        });

        g.MapPut("/preguntas-frecuentes/{id}", (int id, PreguntaFrecuente p) =>
        {
            var error = ValidarPregunta(p);
            if (error != null) return Results.BadRequest(new { mensaje = error });
            var dao = new PreguntaFrecuenteDAO();
            if (dao.ObtenerPorId(id) is null) return Results.NotFound();
            p.IdPregunta = id;
            dao.Modificar(p);
            return Results.Ok(p);
        });

        g.MapDelete("/preguntas-frecuentes/{id}", (int id) =>
        {
            var dao = new PreguntaFrecuenteDAO();
            if (dao.ObtenerPorId(id) is null) return Results.NotFound();
            dao.Baja(id);
            return Results.NoContent();
        });

        return app;
    }

    // PUNTO DE CONTROL DE ACCESO DEL MÓDULO DE MENSAJERÍA
    // ADVERTENCIA: esto NO es autenticación. No sabe quién pide los datos: solo
    // permite apagar el módulo entero (Mensajeria:ChatSimuladoHabilitado = false) y,
    // con Mensajeria:SoloLocal = true, rechazar pedidos que no vengan de la propia
    // máquina. Cualquiera que llegue al servidor con el módulo encendido puede leer
    // conversaciones, DNI, teléfonos y turnos (por ejemplo /conversaciones/{id}/paciente
    // recorriendo ids). Detrás de un proxy o túnel local (Caddy, cloudflared) el pedido
    // llega desde 127.0.0.1 y SoloLocal tampoco alcanza. Es una mitigación temporal
    // y no satisface la Ley 25.326 ni el secreto profesional.
    // Responde 404 (no 403) en ambos casos para no confirmar que el endpoint existe.
    // TODO(JWT): al mergear videollamadas-seguras, exigir en este filtro el token
    // firmado (JwtService.Validar) y el rol de administrador, igual que el TODO de
    // Services/AutorizacionReporte.cs, para todo el grupo salvo /simulado/entrante,
    // que en producción pasa a ser el webhook del canal real con su propia firma.
    public static RouteGroupBuilder RequiereMensajeriaHabilitada(this RouteGroupBuilder grupo) =>
        grupo.AddEndpointFilter(async (ctx, next) =>
        {
            var op = ctx.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue;
            if (!op.ChatSimuladoHabilitado) return Results.NotFound();
            if (op.SoloLocal && !EsLocal(ctx.HttpContext)) return Results.NotFound();
            return await next(ctx);
        });

    private static bool EsLocal(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        return ip is null || IPAddress.IsLoopback(ip);
    }

    private static string? ValidarPregunta(PreguntaFrecuente p)
    {
        if (string.IsNullOrWhiteSpace(p.Pregunta) || string.IsNullOrWhiteSpace(p.Respuesta))
            return "La pregunta y la respuesta son obligatorias.";
        if (p.Pregunta.Length > 300)   return "La pregunta no puede superar los 300 caracteres.";
        if (p.Respuesta.Length > 2000) return "La respuesta no puede superar los 2000 caracteres.";
        return null;
    }

    // Mismo patrón que el resto del backend: X-Usuario-Id (autodeclarado, ver TODO(JWT)).
    private static void Auditar(HttpContext http, string accion, int idConversacion)
    {
        if (!int.TryParse(http.Request.Headers["X-Usuario-Id"], out var idUsuario) || idUsuario <= 0) return;
        try
        {
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = idUsuario, Accion = accion, Entidad = "Conversacion", IdEntidad = idConversacion
            });
        }
        catch (Exception) { /* la auditoría no debe impedir responder al paciente */ }
    }
}

public record MensajeEntranteRequest(string? Telefono, string? Texto);
public record RespuestaHumanaRequest(string? Texto);
