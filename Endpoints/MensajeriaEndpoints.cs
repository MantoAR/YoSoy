using System.Net;
using Microsoft.Data.SqlClient;
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
        // Fase JWT: todo lo de este grupo es la bandeja del lado administrador — exige
        // sesión de Profesional o Configurador. Las mitigaciones que ya tenía (flag
        // ChatSimuladoHabilitado, SoloLocal, 404 con X-Forwarded-For, en
        // RequiereMensajeriaHabilitada) siguen intactas, esto es una capa más encima.
        var g = app.MapGroup("/mensajeria").RequiereMensajeriaHabilitada()
            .RequireAuthorization(p => p.RequireRole(Roles.Profesional, Roles.Configurador));

        // Qué partes opcionales de la interfaz mostrar (el botón Chats de la barra superior).
        // Fuera del grupo para no responder 404, pero con el mismo criterio que su filtro:
        // con el módulo apagado, o un pedido remoto con SoloLocal, dice false y nada más.
        // Sin auth a propósito: lo consulta cualquier pantalla, antes de saber quién sos.
        app.MapGet("/capacidades", (HttpContext http) => Results.Ok(new { mensajeria = MensajeriaDisponible(http) }));

        // ── Chat simulado: lado paciente ─────────────────────────
        // Excepción al RequireAuthorization del grupo: estos dos simulan un mensaje
        // ENTRANTE de un paciente (el equivalente a un webhook de WhatsApp real) — no
        // hay, ni tiene sentido que haya, una sesión de profesional del otro lado.
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
        }).AllowAnonymous();

        g.MapGet("/simulado/conversacion", (string telefono, IConversacionDAO conversaciones, IMessageChannel canal) =>
        {
            var c = conversaciones.ObtenerPorTelefono(Telefono.Normalizar(telefono), canal.Nombre);
            return c is null ? Results.NotFound() : Results.Ok(new { c.IdConversacion, c.Estado });
        }).AllowAnonymous();

        // ── Modo administrador ──────────────────────────────────
        // desdeMensaje: solo las conversaciones con mensajes posteriores a ese IdMensaje
        // (la bandeja pide así lo nuevo en cada vuelta del polling).
        g.MapGet("/conversaciones", (string? estado, int? desdeMensaje, IConversacionDAO conversaciones) =>
        {
            var lista = conversaciones.ListarResumen(string.IsNullOrWhiteSpace(estado) ? null : estado, desdeMensaje);
            var etiquetas = new EtiquetaConversacionDAO().ObtenerTodasLasAsignaciones();
            foreach (var c in lista)
                if (etiquetas.TryGetValue(c.IdConversacion, out var ids)) c.IdsEtiquetas = ids;
            return lista;
        });

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
                return Results.Ok(new { c.IdConversacion, c.Telefono, c.Estado, c.Canal, c.FechaCreacion, paciente = (object?)null,
                                        profesionales = Array.Empty<object>(), proximoTurno = (object?)null });

            var proximo = datos.TurnosProximos(p.IdPaciente, reloj.GetLocalNow().DateTime).FirstOrDefault();
            return Results.Ok(new
            {
                c.IdConversacion, c.Telefono, c.Estado, c.Canal, c.FechaCreacion,
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
        // antes / limite = historial hacia arriba: los 'limite' mensajes anteriores a 'antes'
        // (sin 'antes', los últimos). Sin ninguno de los dos: la conversación entera, como antes.
        g.MapGet("/conversaciones/{id}/mensajes", (int id, int? desde, int? antes, int? limite, IMensajeChatDAO mensajes) =>
            desde is null && (antes is not null || limite is not null)
                ? mensajes.ObtenerAnteriores(id, antes, Math.Clamp(limite ?? 50, 1, 200))
                : mensajes.ObtenerPorConversacion(id, desde ?? 0));

        // El equipo vio la conversación hasta el mensaje 'hasta' (contador de no leídos).
        g.MapPut("/conversaciones/{id}/leida", (int id, MarcarLeidaRequest req, IConversacionDAO conversaciones) =>
        {
            if (conversaciones.ObtenerPorId(id) is null) return Results.NotFound();
            conversaciones.MarcarLeida(id, req.Hasta);
            return Results.NoContent();
        });

        g.MapPost("/conversaciones/{id}/responder", async (int id, RespuestaHumanaRequest req, HttpContext http,
            IConversacionDAO conversaciones, IServicioMensajes mensajes, IDatosMensajeria datos, TimeProvider reloj) =>
        {
            var texto = (req.Texto ?? "").Trim();
            if (texto.Length == 0) return Results.BadRequest(new { mensaje = "El mensaje está vacío." });
            var c = conversaciones.ObtenerPorId(id);
            if (c is null) return Results.NotFound();

            var m = await mensajes.EnviarAsync(c, texto, "Humano");
            c.FechaUltimaActividad = reloj.GetLocalNow().DateTime;
            conversaciones.Guardar(c);
            Auditar(http, "Respuesta manual en chat", id, c.IdPaciente is int idPac ? datos.ObtenerPaciente(idPac)?.Nombre : null);
            return Results.Ok(m);
        });

        // Cierra la derivación (o la urgencia): el bot vuelve a atender desde el menú.
        g.MapPut("/conversaciones/{id}/cerrar", async (int id, HttpContext http,
            IConversacionDAO conversaciones, IServicioMensajes mensajes, IDatosMensajeria datos, TimeProvider reloj) =>
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
            Auditar(http, "Cierre de conversación de chat", id, c.IdPaciente is int idPac2 ? datos.ObtenerPaciente(idPac2)?.Nombre : null);
            return Results.Ok(new { c.IdConversacion, c.Estado });
        });

        // Reabre una conversación cerrada: vuelve a "DerivadaHumano" (no al bot - alguien del
        // equipo la reabrió a propósito, que termine de resolverla una persona).
        g.MapPut("/conversaciones/{id}/reabrir", async (int id, HttpContext http,
            IConversacionDAO conversaciones, IServicioMensajes mensajes, IDatosMensajeria datos, TimeProvider reloj) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null) return Results.NotFound();
            if (c.Estado != EstadoConversacion.Cerrada)
                return Results.Json(new { mensaje = "Esa conversación no está cerrada." }, statusCode: 400);
            c.Estado               = EstadoConversacion.DerivadaHumano;
            c.FechaUltimaActividad = reloj.GetLocalNow().DateTime;
            conversaciones.Guardar(c);
            await mensajes.EnviarAsync(c, "El equipo de YoSoy reabrió esta conversación.", "Sistema");
            Auditar(http, "Reapertura de conversación de chat", id, c.IdPaciente is int idPacR ? datos.ObtenerPaciente(idPacR)?.Nombre : null);
            return Results.Ok(new { c.IdConversacion, c.Estado });
        });

        // Asignar/reasignar el profesional de una conversación es, en realidad, asignar el
        // Caso abierto del paciente - no hay (ni se agrega) una asignación propia de
        // Conversacion. Mismo servicio, mismas categorías de motivo y mismo gate que
        // /casos/{id}/reasignar (ver CasosEndpoints.cs): solo el administrador.
        g.MapPut("/conversaciones/{id}/asignar", (int id, AsignarConversacionRequest req, HttpContext ctx,
            AsignacionService asignacion, IConversacionDAO conversaciones) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo el administrador puede asignar una conversación." }, statusCode: 403);
            var c = conversaciones.ObtenerPorId(id);
            if (c is null) return Results.NotFound();
            if (c.IdPaciente is null)
                return Results.Json(new { mensaje = "Todavía no se identificó al paciente de esta conversación." }, statusCode: 400);
            var caso = new CasoDAO().ObtenerAbiertoPorPaciente(c.IdPaciente.Value);
            if (caso is null)
                return Results.Json(new { mensaje = "Este paciente todavía no tiene un caso abierto para asignar." }, statusCode: 400);
            int? idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
            try
            {
                asignacion.ReasignarManual(caso.IdCaso, req.IdProfesional, idUsuario, req.MotivoCategoria, req.Nota);
                return Results.NoContent();
            }
            catch (ArgumentException ex) { return Results.Json(new { mensaje = ex.Message }, statusCode: 400); }
            catch (InvalidOperationException ex) { return Results.Json(new { mensaje = ex.Message }, statusCode: 400); }
        });

        // ── Notas internas ──────────────────────────────────────
        // Privadas del equipo: nunca al paciente, nunca a auditoría (ni acá ni en Auditar).
        g.MapGet("/conversaciones/{id}/notas", (int id, IConversacionDAO conversaciones) =>
            conversaciones.ObtenerPorId(id) is null
                ? Results.NotFound()
                : Results.Ok(new NotaInternaConversacionDAO().ListarPorConversacion(id)));

        g.MapPost("/conversaciones/{id}/notas", (int id, NotaInternaRequest req, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var texto = (req.Texto ?? "").Trim();
            if (texto.Length == 0) return Results.BadRequest(new { mensaje = "La nota está vacía." });
            if (texto.Length > 1000) return Results.BadRequest(new { mensaje = "La nota no puede superar los 1000 caracteres." });
            if (conversaciones.ObtenerPorId(id) is null) return Results.NotFound();
            int? idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
            var idNota = new NotaInternaConversacionDAO().Insertar(id, texto, idUsuario);
            return Results.Created($"/mensajeria/conversaciones/{id}/notas/{idNota}", new { idNota });
        });

        // Solo quien la escribió (o el administrador) puede borrarla.
        g.MapDelete("/conversaciones/{id}/notas/{idNota}", (int id, int idNota, HttpContext ctx) =>
        {
            var dao = new NotaInternaConversacionDAO();
            var info = dao.ObtenerAutor(idNota);
            if (info is null || info.Value.IdConversacion != id) return Results.NotFound();
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            int? idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
            if (!esAdmin && info.Value.IdAutor != idUsuario)
                return Results.Json(new { mensaje = "Solo quien escribió la nota (o el administrador) puede borrarla." }, statusCode: 403);
            dao.Borrar(idNota);
            return Results.NoContent();
        });

        // ── Etiquetas: catálogo (ABM igual de abierto que preguntas-frecuentes) ──
        g.MapGet("/etiquetas", () => new EtiquetaConversacionDAO().ObtenerActivas());

        g.MapPost("/etiquetas", (EtiquetaConversacion e) =>
        {
            var error = ValidarEtiqueta(e);
            if (error != null) return Results.BadRequest(new { mensaje = error });
            try { e.IdEtiqueta = new EtiquetaConversacionDAO().Insertar(e); return Results.Created($"/mensajeria/etiquetas/{e.IdEtiqueta}", e); }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { return Results.Json(new { mensaje = "Ya existe una etiqueta con ese nombre." }, statusCode: 409); }
        });

        g.MapPut("/etiquetas/{id}", (int id, EtiquetaConversacion e) =>
        {
            var error = ValidarEtiqueta(e);
            if (error != null) return Results.BadRequest(new { mensaje = error });
            var dao = new EtiquetaConversacionDAO();
            if (dao.ObtenerPorId(id) is null) return Results.NotFound();
            e.IdEtiqueta = id;
            try { dao.Modificar(e); return Results.Ok(e); }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { return Results.Json(new { mensaje = "Ya existe una etiqueta con ese nombre." }, statusCode: 409); }
        });

        // Baja lógica: no se borran las ya puestas en conversaciones, solo deja de ofrecerse.
        g.MapDelete("/etiquetas/{id}", (int id) =>
        {
            var dao = new EtiquetaConversacionDAO();
            if (dao.ObtenerPorId(id) is null) return Results.NotFound();
            dao.Desactivar(id);
            return Results.NoContent();
        });

        // ── Etiquetas: asignación a una conversación ────────────
        g.MapPost("/conversaciones/{id}/etiquetas/{idEtiqueta}", (int id, int idEtiqueta, IConversacionDAO conversaciones) =>
        {
            if (conversaciones.ObtenerPorId(id) is null) return Results.NotFound();
            if (new EtiquetaConversacionDAO().ObtenerPorId(idEtiqueta) is null) return Results.NotFound();
            new EtiquetaConversacionDAO().Agregar(id, idEtiqueta);
            return Results.NoContent();
        });

        g.MapDelete("/conversaciones/{id}/etiquetas/{idEtiqueta}", (int id, int idEtiqueta) =>
        {
            new EtiquetaConversacionDAO().Quitar(id, idEtiqueta);
            return Results.NoContent();
        });

        // ── Respuestas rápidas: null = global (del Configurador), con dueño = de ese
        // profesional. Reemplaza plantillas.json como fuente del composer. ──
        g.MapGet("/respuestas-rapidas", (HttpContext ctx) =>
        {
            var identidad = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
            return Results.Ok(new RespuestaRapidaDAO().ObtenerParaUso(identidad?.IdProfesional));
        });

        g.MapPost("/respuestas-rapidas", (RespuestaRapida p, HttpContext ctx) =>
        {
            var error = ValidarRespuesta(p);
            if (error != null) return Results.BadRequest(new { mensaje = error });
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin && idPropio is null)
                return Results.Json(new { mensaje = "No se pudo identificar al profesional." }, statusCode: 403);
            p.IdProfesional = esAdmin ? null : idPropio;   // Configurador: siempre global; Profesional: siempre propia
            p.IdRespuestaRapida = new RespuestaRapidaDAO().Insertar(p);
            return Results.Created($"/mensajeria/respuestas-rapidas/{p.IdRespuestaRapida}", p);
        });

        g.MapPut("/respuestas-rapidas/{id}", (int id, RespuestaRapida p, HttpContext ctx) =>
        {
            var error = ValidarRespuesta(p);
            if (error != null) return Results.BadRequest(new { mensaje = error });
            var dao = new RespuestaRapidaDAO();
            var existente = dao.ObtenerPorId(id);
            if (existente is null) return Results.NotFound();
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            if (!PuedeEditarRespuesta(existente, esAdmin, idPropio))
                return Results.Json(new { mensaje = "No podés editar esta respuesta rápida." }, statusCode: 403);
            p.IdRespuestaRapida = id;
            p.IdProfesional = existente.IdProfesional;   // el dueño no cambia al editar
            dao.Modificar(p);
            return Results.Ok(p);
        });

        g.MapDelete("/respuestas-rapidas/{id}", (int id, HttpContext ctx) =>
        {
            var dao = new RespuestaRapidaDAO();
            var existente = dao.ObtenerPorId(id);
            if (existente is null) return Results.NotFound();
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            if (!PuedeEditarRespuesta(existente, esAdmin, idPropio))
                return Results.Json(new { mensaje = "No podés borrar esta respuesta rápida." }, statusCode: 403);
            dao.Baja(id);
            return Results.NoContent();
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
    // Al mergear con videollamadas-seguras (Caddy/túnel) seguir NOTAS_MERGE.md.
    // TODO(JWT): al mergear videollamadas-seguras, exigir en este filtro el token
    // firmado (JwtService.Validar) y el rol de administrador, igual que el TODO de
    // Services/AutorizacionReporte.cs, para todo el grupo salvo /simulado/entrante,
    // que en producción pasa a ser el webhook del canal real con su propia firma.
    public static RouteGroupBuilder RequiereMensajeriaHabilitada(this RouteGroupBuilder grupo) =>
        grupo.AddEndpointFilter(async (ctx, next) =>
            MensajeriaDisponible(ctx.HttpContext) ? await next(ctx) : Results.NotFound());

    // El mismo criterio para el filtro del grupo y para /capacidades.
    public static bool MensajeriaDisponible(HttpContext http) =>
        Disponible(http.RequestServices.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue, http);

    // Separado del contenedor de servicios para poder probarlo (AccesoMensajeriaTests).
    public static bool Disponible(MensajeriaOptions op, HttpContext http) =>
        op.ChatSimuladoHabilitado && (!op.SoloLocal || EsLocal(http));

    // Un proxy o túnel que corre en la misma máquina (Caddy en videollamadas-seguras,
    // cloudflared) se conecta desde 127.0.0.1, así que la IP sola no alcanza: si el
    // pedido trae X-Forwarded-For o CF-Connecting-IP, pasó por un proxy y se trata
    // como remoto. Falsificar esos headers a mano solo vuelve el control más estricto.
    private static bool EsLocal(HttpContext http)
    {
        var h = http.Request.Headers;
        if (h.ContainsKey("X-Forwarded-For") || h.ContainsKey("CF-Connecting-IP")) return false;
        var ip = http.Connection.RemoteIpAddress;
        return ip is null || IPAddress.IsLoopback(ip);
    }

    // La pantalla del chat simulado se sirve como archivo estático junto con el resto de
    // PANTALLAS: con el módulo apagado también responde 404, para no confirmar que existe.
    // Se registra en Program.cs antes de UseStaticFiles. La comparación ignora mayúsculas
    // porque el sistema de archivos de Windows también las ignora al servir el archivo.
    public static WebApplication UsePantallaMensajeria(this WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/pantallas/Pantalla_ChatSimulado", StringComparison.OrdinalIgnoreCase)
                && !ctx.RequestServices.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue.ChatSimuladoHabilitado)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await next();
        });
        return app;
    }

    private static string? ValidarPregunta(PreguntaFrecuente p)
    {
        if (string.IsNullOrWhiteSpace(p.Pregunta) || string.IsNullOrWhiteSpace(p.Respuesta))
            return "La pregunta y la respuesta son obligatorias.";
        if (p.Pregunta.Length > 300)   return "La pregunta no puede superar los 300 caracteres.";
        if (p.Respuesta.Length > 2000) return "La respuesta no puede superar los 2000 caracteres.";
        return null;
    }

    private static readonly string[] TONOS_ETIQUETA = { "marca", "warn", "danger", "neutral", "ok" };

    private static string? ValidarEtiqueta(EtiquetaConversacion e)
    {
        if (string.IsNullOrWhiteSpace(e.Nombre)) return "El nombre es obligatorio.";
        if (e.Nombre.Length > 40) return "El nombre no puede superar los 40 caracteres.";
        if (!TONOS_ETIQUETA.Contains(e.Tono)) return $"Tono no reconocido. Tiene que ser uno de: {string.Join(", ", TONOS_ETIQUETA)}.";
        return null;
    }

    private static string? ValidarRespuesta(RespuestaRapida p)
    {
        if (string.IsNullOrWhiteSpace(p.Titulo) || string.IsNullOrWhiteSpace(p.Texto))
            return "El título y el texto son obligatorios.";
        if (p.Titulo.Length > 60)   return "El título no puede superar los 60 caracteres.";
        if (p.Texto.Length > 2000)  return "El texto no puede superar los 2000 caracteres.";
        return null;
    }

    // Dueño null = global, solo el administrador la toca; con dueño, solo ese profesional.
    private static bool PuedeEditarRespuesta(RespuestaRapida r, bool esAdmin, int? idPropio) =>
        r.IdProfesional is null ? esAdmin : r.IdProfesional == idPropio;

    // Identidad resuelta por IdentidadActualService (JWT o, en modo legacy, X-Usuario-Id)
    // — este endpoint ya exige rol Profesional o Configurador, así que siempre hay alguien.
    // detalle: para que la auditoría diga algo útil (qué conversación, de qué paciente) en vez
    // de quedar vacía; nombrePaciente es opcional porque no siempre hay un paciente identificado.
    private static void Auditar(HttpContext http, string accion, int idConversacion, string? nombrePaciente = null)
    {
        var idUsuario = http.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(http)?.IdUsuario;
        if (idUsuario is null) return;
        try
        {
            var detalle = $"Conversación #{idConversacion}" + (string.IsNullOrWhiteSpace(nombrePaciente) ? "" : $" ({nombrePaciente})");
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = idUsuario.Value, Accion = accion, Entidad = "Conversacion", IdEntidad = idConversacion, Detalle = detalle
            });
        }
        catch (Exception) { /* la auditoría no debe impedir responder al paciente */ }
    }
}

public record MensajeEntranteRequest(string? Telefono, string? Texto);
public record RespuestaHumanaRequest(string? Texto);
public record MarcarLeidaRequest(int Hasta);
public record AsignarConversacionRequest(int IdProfesional, string MotivoCategoria, string? Nota);
public record NotaInternaRequest(string? Texto);
