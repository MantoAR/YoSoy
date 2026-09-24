using System.Net;
using Microsoft.Extensions.Options;

// Endpoints del módulo de mensajería: chat simulado (modo paciente y modo
// administrador), preguntas frecuentes y avisos internos. Se registran con
// app.MapMensajeria() en Program.cs.
//
// ADVERTENCIA: el backend todavía no tiene autenticación real. Estos endpoints
// exponen conversaciones de pacientes (datos sensibles, Ley 25.326), así que:
//  - se apagan todos con Mensajeria:ChatSimuladoHabilitado = false, y
//  - con Mensajeria:SoloLocal = true (por defecto) rechazan pedidos que no vengan
//    de la propia máquina. Ojo: detrás de un proxy o túnel local (por ejemplo
//    cloudflared) el pedido llega desde 127.0.0.1 y ese control no alcanza.
// No publicar esto en una URL pública.
// TODO(JWT): al mergear videollamadas-seguras, exigir el token firmado y el rol
// de administrador (igual que el TODO de Services/AutorizacionReporte.cs) en
// todo el grupo salvo /simulado/entrante, que en producción pasa a ser el webhook
// del canal real con su propia validación de firma.
public static class MensajeriaEndpoints
{
    public static IEndpointRouteBuilder MapMensajeria(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/mensajeria").AddEndpointFilter(async (ctx, next) =>
        {
            var op = ctx.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue;
            if (!op.ChatSimuladoHabilitado) return Results.NotFound();
            if (op.SoloLocal && !EsLocal(ctx.HttpContext))
                return Results.Json(new { mensaje = "El chat simulado solo está disponible desde el servidor." }, statusCode: 403);
            return await next(ctx);
        });

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
