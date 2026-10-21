using System.Net;
using Microsoft.AspNetCore.Mvc;
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
        // Fase JWT: todo lo de este grupo es la bandeja del lado administrador. Las mitigaciones
        // que ya tenía (flag ChatSimuladoHabilitado, SoloLocal, 404 con X-Forwarded-For, en
        // RequiereMensajeriaHabilitada) siguen intactas, esto es una capa más encima.
        // D2 (chat para el personal): los chats son SOLO del módulo CRM (atención al cliente; el Configurador que tenga
        // además ese módulo entra con el token de CRM). Los profesionales atienden, no contestan mensajes: no ven ningún
        // chat ni reciben sus notificaciones, y un Profesional, un Configurador sin CRM o un paciente reciben 403 en cada
        // ruta de este grupo (ChatsSoloCrmTests lo prueba ruta por ruta y vigila que una ruta nueva no quede
        // abierta). Las excepciones, más abajo, son las que no tienen sesión del otro lado: el
        // simulador y el webhook de whatsapp-service.
        var g = app.MapGroup("/mensajeria").RequiereMensajeriaHabilitada()
            .Solo(Quien.Crm);

        // Qué partes opcionales de la interfaz mostrar (el botón Chats de la barra superior).
        // Fuera del grupo para no responder 404, pero con el mismo criterio que su filtro:
        // con el módulo apagado, o un pedido remoto con SoloLocal, dice false y nada más.
        // Sin auth a propósito: lo consulta cualquier pantalla, antes de saber quién sos.
        // modoAtencion ("asistente" o "personal") le dice a la pantalla cómo rotular los chats, y canalActivo (el Nombre del canal por el que salen las respuestas:
        // "Simulado" o "WhatsApp") qué poner en "Enviar por". Los dos se informan solo con el módulo disponible.
        app.MapGet("/capacidades", (HttpContext http, IMessageChannel canal) =>
        {
            var op = http.RequestServices.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue;
            var disponible = Disponible(op, http);
            return Results.Ok(new
            {
                mensajeria = disponible,
                modoAtencion = disponible ? (op.EsModoPersonal ? "personal" : "asistente") : null,
                canalActivo = disponible ? canal.Nombre : null
            });
        });

        // ¿Está conectado WhatsApp? Para la bandeja de Chats (módulo CRM): le alcanza con saberlo para avisar y para deshabilitar "Enviar por". Es lo mínimo:
        // ni el número, ni el QR, ni nada de la vinculación (eso es del Configurador). Con otro canal activo (el simulado) dice aplica:false.
        g.MapGet("/conexion-whatsapp", async (WhatsAppAdminService admin, IMessageChannel canal, HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (canal.Nombre != "WhatsApp") return Results.Ok(new { aplica = false });
            var e = await admin.ObtenerEstadoAsync(conDesde: false);
            return Results.Ok(new { aplica = true, servicioDisponible = e.ServicioDisponible, conectado = e.Conectado, estado = e.Estado, motivo = e.Motivo });
        }).RequireRateLimiting("whatsapp-estado");

        // ── Chat simulado: lado paciente ─────────────────────────
        // Excepción al RequireAuthorization del grupo: estos dos simulan un mensaje
        // ENTRANTE de un paciente (el equivalente a un webhook de WhatsApp real) — no
        // hay, ni tiene sentido que haya, una sesión de profesional del otro lado.
        // Sin sesión y sin secreto, su único candado es la propia máquina, y vale SIEMPRE (como el del webhook de más abajo), no solo con SoloLocal: con SoloLocal=false (la
        // bandeja vista desde el sitio, como en producción) el resto del grupo se abre a la red, pero estas dos siguen siendo solo locales y a un pedido remoto le contestan 404.
        // Si no, cualquiera desde internet crearía conversaciones en la bandeja del Configurador y podría preguntar por un teléfono si esa persona ya habló con la clínica.
        g.MapPost("/simulado/entrante", async (MensajeEntranteRequest req, RecepcionDeMensajes recepcion, IMessageChannel canal) =>
        {
            try
            {
                var r = await recepcion.ProcesarAsync(req.Telefono ?? "", req.Texto ?? "", canal.Nombre);
                return Results.Ok(r);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).AllowAnonymous().SoloDesdeLaPropiaMaquina();

        g.MapGet("/simulado/conversacion", (string telefono, IConversacionDAO conversaciones, IMessageChannel canal) =>
        {
            var c = conversaciones.ObtenerPorTelefono(Telefono.Normalizar(telefono), canal.Nombre);
            return c is null ? Results.NotFound() : Results.Ok(new { c.IdConversacion, c.Estado });
        }).AllowAnonymous().SoloDesdeLaPropiaMaquina();

        // Webhook real de WhatsApp (whatsapp-service se lo manda por mensaje entrante) —
        // lo que anticipaba el comentario de RequiereMensajeriaHabilitada de abajo. Fuera
        // del grupo "g" a propósito: el candado acá no depende de ChatSimuladoHabilitado/
        // SoloLocal (que son para el módulo de administración), es propio y siempre activo
        // — EsLocal() + el secreto compartido, los dos, ninguno alcanza solo. Mismo 404
        // para las dos fallas, para no revelar cuál fue.
        // Contrato v2 (compatible con el de siempre, { telefono, texto }): además puede traer tipo, id, ts y nombre (ver MensajeEntranteRequest y DatosDelEntrante).
        app.MapPost("/mensajeria/whatsapp/entrante", async (MensajeEntranteRequest req, HttpContext ctx,
            RecepcionDeMensajes recepcion, IMessageChannel canal, IConfiguration config, TimeProvider reloj) =>
        {
            if (!EsLocal(ctx)) return Results.NotFound();

            var esperado = config["Mensajeria:WhatsApp:Secreto"];
            var recibido = ctx.Request.Headers["X-Shared-Secret"].ToString();
            if (string.IsNullOrEmpty(esperado) || string.IsNullOrEmpty(recibido) ||
                !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(esperado), System.Text.Encoding.UTF8.GetBytes(recibido)))
                return Results.NotFound();

            try
            {
                var r = await recepcion.ProcesarAsync(req.Telefono ?? "", req.Texto ?? "", canal.Nombre, DatosDelEntrante.Desde(req, reloj));
                return Results.Ok(r);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).AllowAnonymous();

        // ── WhatsApp: pantalla de administración (estado, QR, desvincular) ──────
        // Grupo propio, no "g": exige Configurador específicamente (no Profesional), y a
        // propósito SIN RequiereMensajeriaHabilitada() — ese filtro depende de
        // ChatSimuladoHabilitado, una configuración independiente de si WhatsApp está
        // vinculado (una clínica puede querer WhatsApp para sus avisos y respuestas con el chat
        // simulado apagado; atarlos haría que esta pantalla diera 404 sin motivo).
        var wa = app.MapGroup("/mensajeria/whatsapp")
            .RequireAuthorization(p => p.RequireRole(Roles.Configurador));

        // El QR viaja adentro de esta misma respuesta (ver WhatsAppAdminService/
        // ServicioWhatsApp.estado() del lado Node) — no hay una ruta /qr separada, así el
        // polling del frontend es un solo pedido. No-store: nunca se cachea una respuesta
        // que puede traer un QR vigente; tampoco se loguea en ningún lado.
        wa.MapGet("/estado", async (WhatsAppAdminService admin, HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await admin.ObtenerEstadoAsync());
        }).RequireRateLimiting("whatsapp-estado");

        // Guarda el número desvinculado en el detalle de auditoría ANTES de desvincular
        // (después ya no se puede volver a preguntar). Se audita aunque no hubiera nada
        // conectado (p. ej. para generar un QR nuevo durante una vinculación que no arrancó) —
        // el Detalle lo aclara.
        wa.MapPost("/desvincular", async (WhatsAppAdminService admin, HttpContext ctx) =>
        {
            var previo = await admin.ObtenerEstadoAsync();
            if (!await admin.DesvincularAsync())
                return Results.Json(new { mensaje = "No se pudo contactar al servicio de WhatsApp." }, statusCode: 503);
            AuditarWhatsApp(ctx, "Desvinculación de WhatsApp",
                previo.Conectado ? $"Número desvinculado: {previo.Numero}" : "Sin número conectado al momento de desvincular.");
            return Results.Ok(new { exito = true });
        }).RequireRateLimiting("whatsapp-admin");

        // Pide un código QR para vincular el número (la pantalla de WhatsApp del Configurador). Sin tocar el reintento automático ni las ventanas de QR del
        // servicio: solo lo que el servicio ya ofrece a pedido de una persona (POST /vincular, idempotente de su lado: si ya hay un QR esperando, no abre otro).
        //   - Ya conectado: 409 (para cambiar de número está "desvincular", con su confirmación).
        //   - El servicio necesita revisión (cuenta restringida, sesión reemplazada...): 409; un QR no lo arregla y no se intenta.
        //   - Todavía conectando: 409 en_curso.
        //   - Ya hay un QR esperando: 200 yaAbierta, sin abrir otro. Con { nuevo: true } se reemplaza por uno nuevo (solo sin número vinculado).
        //   - Abrir una ventana nueva respeta el enfriamiento de 30 s (429 con los segundos que faltan): pedir QR en bucle puede terminar en un bloqueo del número.
        //   - Servicio caído: 503. El pedido se audita (sin número ni QR).
        wa.MapPost("/vincular", async ([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SolicitudQrWhatsApp? req,
            WhatsAppAdminService admin, EnfriamientoQr enfriamiento, HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var previo = await admin.ObtenerEstadoAsync(conDesde: false);
            if (!previo.ServicioDisponible)
                return Results.Json(new { exito = false, codigo = "servicio_no_disponible", mensaje = "No se pudo contactar al servicio de WhatsApp." }, statusCode: 503);
            if (previo.Conectado)
                return Results.Json(new { exito = false, codigo = "ya_vinculado", mensaje = "Ya hay un número vinculado. Para cambiarlo, primero hay que desvincularlo." }, statusCode: 409);
            if (previo.Estado == "requiere_atencion")
                return Results.Json(new { exito = false, codigo = "requiere_atencion", estado = previo.Estado, motivo = previo.Motivo,
                    mensaje = "El servicio de WhatsApp necesita revisión antes de vincular; un código QR no lo arregla." }, statusCode: 409);
            if (previo.Estado is "iniciando" or "reconectando" or "detenido")
                return Results.Json(new { exito = false, codigo = "en_curso", estado = previo.Estado, motivo = previo.Motivo,
                    mensaje = "WhatsApp se está conectando: todavía no se puede pedir un código." }, statusCode: 409);

            var nuevo = req?.Nuevo == true;
            var esperandoEscaneo = previo.Estado == "requiere_vincular" && previo.Motivo == "esperando_escaneo";
            if (esperandoEscaneo && !nuevo)
                return Results.Ok(new { exito = true, yaAbierta = true, estado = previo.Estado, motivo = previo.Motivo });

            var falta = enfriamiento.SegundosRestantes();
            if (falta > 0)
                return Results.Json(new { exito = false, codigo = "enfriamiento", reintentarEnSegundos = falta,
                    mensaje = $"Esperá {falta} segundos antes de pedir otro código." }, statusCode: 429);

            if (esperandoEscaneo)
            {
                // Sin número vinculado "desvincular" no cierra ninguna sesión en WhatsApp: solo descarta la ventana actual y abre una nueva. Pero el estado
                // de arriba puede tener unos segundos (alguien pudo escanear el código entre medio): se lee de nuevo justo antes y, si ya quedó conectado,
                // NO se desvincula (eso sí cerraría la sesión recién vinculada).
                var ahora = await admin.ObtenerEstadoAsync(conDesde: false);
                if (!ahora.ServicioDisponible)
                    return Results.Json(new { exito = false, codigo = "servicio_no_disponible", mensaje = "No se pudo contactar al servicio de WhatsApp." }, statusCode: 503);
                if (ahora.Conectado)
                    return Results.Json(new { exito = false, codigo = "ya_vinculado", mensaje = "Ya hay un número vinculado. Para cambiarlo, primero hay que desvincularlo." }, statusCode: 409);
                enfriamiento.Registrar();
                if (!await admin.DesvincularAsync())
                    return Results.Json(new { exito = false, codigo = "servicio_no_disponible", mensaje = "No se pudo contactar al servicio de WhatsApp." }, statusCode: 503);
                AuditarWhatsApp(ctx, "Código QR nuevo de WhatsApp", "Reemplazó el código que estaba esperando escaneo.");
                return Results.Ok(new { exito = true, yaAbierta = false, estado = "iniciando", motivo = (string?)null });
            }

            enfriamiento.Registrar();
            var resultado = await admin.VincularAsync();
            if (resultado is null)
                return Results.Json(new { exito = false, codigo = "servicio_no_disponible", mensaje = "No se pudo contactar al servicio de WhatsApp." }, statusCode: 503);
            AuditarWhatsApp(ctx, "Pedido de código QR de WhatsApp", $"Estado {previo.Estado}/{previo.Motivo}: {(resultado.Exito ? "ventana abierta" : "rechazado (" + resultado.Codigo + ")")}.");
            return resultado.Exito
                ? Results.Ok(new { exito = true, yaAbierta = resultado.YaAbierta, estado = resultado.Estado, motivo = resultado.Motivo })
                : Results.Json(new { exito = false, codigo = resultado.Codigo, mensaje = resultado.Error, estado = resultado.Estado, motivo = resultado.Motivo }, statusCode: 409);
        }).RequireRateLimiting("whatsapp-admin");

        // Disparado por el frontend cuando su polling nota la transición a conectado — pero
        // la fila de auditoría no se escribe solo porque el navegador lo diga: se vuelve a
        // pedir el estado real antes de confirmar, para que quede respaldada por un chequeo
        // del servidor. Límite conocido y aceptado: si se cierra la pestaña antes de que el
        // polling note el cambio, esa vinculación puntual no queda auditada.
        wa.MapPost("/confirmar-vinculacion", async (WhatsAppAdminService admin, HttpContext ctx) =>
        {
            var e = await admin.ObtenerEstadoAsync();
            if (e.ServicioDisponible && e.Conectado)
                AuditarWhatsApp(ctx, "Vinculación de WhatsApp", $"Número vinculado: {e.Numero}");
            return Results.NoContent();
        }).RequireRateLimiting("whatsapp-admin");

        // ── Modo administrador ──────────────────────────────────
        // desdeMensaje: solo las conversaciones con mensajes posteriores a ese IdMensaje
        // (la bandeja pide así lo nuevo en cada vuelta del polling).
        // idEtiqueta: filtro de la bandeja (Paso 5) - se aplica DESPUÉS de la pertenencia,
        // así solo filtra dentro de lo que esa identidad ya puede ver.
        // Pertenencia (Paso 1/2, ver VisibilidadConversacion y CAMBIOS_AGUS.md): el buscador de
        // la bandeja filtra client-side sobre esta misma lista, así que queda cubierto gratis.
        g.MapGet("/conversaciones", (string? estado, int? desdeMensaje, int? idEtiqueta, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var lista = VisibilidadConversacion.Filtrar(ctx, conversaciones.ListarResumen(string.IsNullOrWhiteSpace(estado) ? null : estado, desdeMensaje));
            var etiquetas = new EtiquetaConversacionDAO().ObtenerTodasLasAsignaciones();
            foreach (var c in lista)
                if (etiquetas.TryGetValue(c.IdConversacion, out var ids)) c.IdsEtiquetas = ids;
            if (idEtiqueta is int tag) lista = lista.Where(c => c.IdsEtiquetas.Contains(tag)).ToList();
            return lista;
        });

        // Leads (vista única de Administración): las mismas conversaciones que /conversaciones, como tabla de solo lectura con búsqueda, filtros por tipo
        // ("paciente" / "sin-identificar"), estado y etiqueta, y paginación en el servidor. Misma pertenencia que /conversaciones; respuesta reducida (sin
        // el texto de los mensajes ni el paso del bot) y, como /conversaciones, sin auditoría de lectura (la apertura de una conversación sí se audita, en Chats).
        // Va en este grupo (Solo CRM): lo ve quien tiene el rol CRM, es decir el CRM y Administración; el menú de Leads existe solo en Administración.
        g.MapGet("/leads", (string? q, string? tipo, string? estado, int? idEtiqueta, int? pagina, int? cantidad, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var (filtro, error) = Leads.Normalizar(q, tipo, estado, idEtiqueta, pagina, cantidad);
            if (filtro is null) return Results.Json(new { mensaje = error }, statusCode: 400);
            var lista = VisibilidadConversacion.Filtrar(ctx, conversaciones.ListarResumen(filtro.Estado));
            var etiquetas = new EtiquetaConversacionDAO().ObtenerTodasLasAsignaciones();
            foreach (var c in lista)
                if (etiquetas.TryGetValue(c.IdConversacion, out var ids)) c.IdsEtiquetas = ids;
            var (total, filas) = Leads.Filtrar(lista, filtro);
            // Una página que ya no existe (por ejemplo, se cerraron conversaciones) vuelve a la primera.
            if (filas.Count == 0 && filtro.Pagina > 1)
            {
                filtro = filtro with { Pagina = 1 };
                (total, filas) = Leads.Filtrar(lista, filtro);
            }
            return Results.Ok(new
            {
                total, pagina = filtro.Pagina, cantidad = filtro.Cantidad,
                paginas = (int)Math.Ceiling(total / (double)filtro.Cantidad),
                filas
            });
        });

        // 404 (no 403) cuando no hay pertenencia: no confirma que la conversación existe.
        g.MapGet("/conversaciones/{id}", (int id, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            return c is null || !VisibilidadConversacion.Puede(ctx, c)
                ? Results.NotFound()
                : Results.Ok(new { c.IdConversacion, c.Telefono, telefonoMostrar = TelefonoWhatsApp.Formatear(c.Telefono), c.IdPaciente, c.Canal, c.Estado, c.PasoActual, c.FechaUltimaActividad });
        });

        // Panel derecho del modo administrador. Solo los datos que necesita la vista
        // (sin domicilio ni fecha de nacimiento). Profesionales y próximo turno salen
        // de las mismas consultas que usa el bot ("pedir turno" y "ver mis turnos").
        // Es el único punto de /mensajeria que se llama solo al abrir o refrescar el panel de
        // una conversación puntual (nunca desde el polling de la lista) - por eso es donde
        // se registra la auditoría de apertura (Paso 4): una vez por cada vez que alguien
        // efectivamente mira estos datos, nunca por el polling de /conversaciones.
        g.MapGet("/conversaciones/{id}/paciente", (int id, HttpContext http, IConversacionDAO conversaciones,
            IDatosMensajeria datos, TimeProvider reloj) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(http, c)) return Results.NotFound();
            AuditarConIds(http, "Apertura de conversación de chat", id, c.IdPaciente);
            var asignado = c.IdProfesionalAsignado is int idProf ? new ProfesionalDAO().ObtenerPorId(idProf) : null;
            var nombreAsignado = asignado is null ? null : Formato.NombreCompleto(asignado.Nombre, asignado.Apellido);
            var p = c.IdPaciente is int idPac ? datos.ObtenerPaciente(idPac) : null;
            if (p is null)
                return Results.Ok(new { c.IdConversacion, c.Telefono, c.Estado, c.Canal, c.FechaCreacion, c.NombreContacto, paciente = (object?)null,
                                        profesionales = Array.Empty<object>(), proximoTurno = (object?)null,
                                        c.IdProfesionalAsignado, nombreProfesionalAsignado = nombreAsignado,
                                        chat = ChatsVinculoEndpoints.DatosDelPanel(c, http) });

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
                },
                c.IdProfesionalAsignado, nombreProfesionalAsignado = nombreAsignado,
                chat = ChatsVinculoEndpoints.DatosDelPanel(c, http)
            });
        });

        // desde = último IdMensaje que ya tiene la pantalla (polling incremental).
        // antes / limite = historial hacia arriba: los 'limite' mensajes anteriores a 'antes'
        // (sin 'antes', los últimos). Sin ninguno de los dos: la conversación entera, como antes.
        g.MapGet("/conversaciones/{id}/mensajes", (int id, int? desde, int? antes, int? limite, HttpContext ctx,
            IConversacionDAO conversaciones, IMensajeChatDAO mensajes) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            return Results.Ok(desde is null && (antes is not null || limite is not null)
                ? mensajes.ObtenerAnteriores(id, antes, Math.Clamp(limite ?? 50, 1, 200))
                : mensajes.ObtenerPorConversacion(id, desde ?? 0));
        });

        // El equipo vio la conversación hasta el mensaje 'hasta' (contador de no leídos).
        g.MapPut("/conversaciones/{id}/leida", (int id, MarcarLeidaRequest req, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            conversaciones.MarcarLeida(id, req.Hasta);
            return Results.NoContent();
        });

        g.MapPost("/conversaciones/{id}/responder", async (int id, RespuestaHumanaRequest req, HttpContext http,
            IConversacionDAO conversaciones, IServicioMensajes mensajes, IDatosMensajeria datos, TimeProvider reloj) =>
        {
            var texto = (req.Texto ?? "").Trim();
            if (texto.Length == 0) return Results.BadRequest(new { mensaje = "El mensaje está vacío." });
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(http, c)) return Results.NotFound();

            var m = await mensajes.EnviarAsync(c, texto, "Humano");
            c.FechaUltimaActividad = reloj.GetLocalNow().DateTime;
            conversaciones.Guardar(c);
            Auditar(http, "Respuesta manual en chat", id, c.IdPaciente is int idPac ? datos.ObtenerPaciente(idPac)?.Nombre : null);
            return Results.Ok(m);
        });

        // Cierra la derivación (o la urgencia): en el modo Asistente el bot vuelve a atender desde el menú y se le avisa a la persona. En el modo Personal
        // no se le manda nada (cerrar lo decide una persona del equipo y no hay asistente que atienda): si vuelve a escribir, vuelve a quedar para atender.
        g.MapPut("/conversaciones/{id}/cerrar", async (int id, HttpContext http,
            IConversacionDAO conversaciones, IServicioMensajes mensajes, IDatosMensajeria datos, TimeProvider reloj, IOptionsMonitor<MensajeriaOptions> opciones) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(http, c)) return Results.NotFound();
            c.Estado               = EstadoConversacion.Cerrada;
            c.PasoActual           = c.IdPaciente is null ? PasoBot.Inicio : PasoBot.Menu;
            c.DatosContexto        = null;
            c.FechaUltimaActividad = reloj.GetLocalNow().DateTime;
            conversaciones.Guardar(c);
            if (!opciones.CurrentValue.EsModoPersonal)
                await mensajes.EnviarAsync(c,
                    "El equipo de YoSoy cerró esta conversación. Si necesitás algo más, escribí \"menu\" y el asistente te va a ayudar.",
                    "Sistema");
            Auditar(http, "Cierre de conversación de chat", id, c.IdPaciente is int idPac2 ? datos.ObtenerPaciente(idPac2)?.Nombre : null);
            return Results.Ok(new { c.IdConversacion, c.Estado });
        });

        // Reabre una conversación cerrada: vuelve a "DerivadaHumano" (no al bot - alguien del
        // equipo la reabrió a propósito, que termine de resolverla una persona).
        g.MapPut("/conversaciones/{id}/reabrir", async (int id, HttpContext http,
            IConversacionDAO conversaciones, IServicioMensajes mensajes, IDatosMensajeria datos, TimeProvider reloj, IOptionsMonitor<MensajeriaOptions> opciones) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(http, c)) return Results.NotFound();
            if (c.Estado != EstadoConversacion.Cerrada)
                return Results.Json(new { mensaje = "Esa conversación no está cerrada." }, statusCode: 400);
            c.Estado               = EstadoConversacion.DerivadaHumano;
            c.FechaUltimaActividad = reloj.GetLocalNow().DateTime;
            conversaciones.Guardar(c);
            if (!opciones.CurrentValue.EsModoPersonal)   // en el modo Personal no se le manda nada a la persona
                await mensajes.EnviarAsync(c, "El equipo de YoSoy reabrió esta conversación.", "Sistema");
            Auditar(http, "Reapertura de conversación de chat", id, c.IdPaciente is int idPacR ? datos.ObtenerPaciente(idPacR)?.Nombre : null);
            return Results.Ok(new { c.IdConversacion, c.Estado });
        });

        // ── Pertenencia de conversaciones: Asignar / Tomar / Devolver ───────────
        // Conversacion.IdProfesionalAsignado es propia - quien atiende este hilo, no el Caso
        // ni PacienteProfesional (ver CAMBIOS_AGUS.md, "Pertenencia de conversaciones"). Las
        // tres acciones solo tienen efecto sobre la visibilidad en conversaciones escaladas
        // (DerivadaHumano/Urgente): es el único estado en el que VisibilidadConversacion mira
        // esta columna; asignarla en Activa/Cerrada no cambiaría nada para nadie, así que ni
        // se ofrece.

        // Asignación directa (administrador): pisa lo que hubiera, a cualquier profesional.
        // Mismas categorías de motivo que /casos/{id}/reasignar, pero sin tocar Caso: ya no
        // hace falta un caso abierto para asignar quién atiende el chat.
        g.MapPut("/conversaciones/{id}/asignar", (int id, AsignarConversacionRequest req, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo CRM y atención al cliente puede asignar una conversación." }, statusCode: 403);
            if (!MotivoReasignacion.Categorias.Contains(req.MotivoCategoria))
                return Results.Json(new { mensaje = $"Motivo no reconocido: \"{req.MotivoCategoria}\". Tiene que ser uno de: {string.Join(", ", MotivoReasignacion.Categorias)}." }, statusCode: 400);
            var c = conversaciones.ObtenerPorId(id);
            if (c is null) return Results.NotFound();
            if (c.Estado is not (EstadoConversacion.DerivadaHumano or EstadoConversacion.Urgente))
                return Results.Json(new { mensaje = "Solo se puede asignar una conversación derivada a una persona o urgente." }, statusCode: 400);
            conversaciones.AsignarProfesional(id, req.IdProfesional);
            AuditarConIds(ctx, "Asignación manual de conversación de chat", id, c.IdPaciente);
            return Results.NoContent();
        });

        // Tomar (Paso 3): cualquier profesional puede tomar una escalación sin asignar.
        // Atómico vía ConversacionDAO.TomarSiNoTieneDueno (UPDATE ... WHERE
        // IdProfesionalAsignado IS NULL): si dos profesionales la toman a la vez, el que
        // pierde la carrera recibe 409, nunca dos dueños a la vez.
        g.MapPut("/conversaciones/{id}/tomar", (int id, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            if (esAdmin) return Results.Json(new { mensaje = "CRM asigna desde /asignar; tomar es para un profesional." }, statusCode: 400);
            if (idPropio is null) return Results.Json(new { mensaje = "No se pudo identificar al profesional." }, statusCode: 403);
            var c = conversaciones.ObtenerPorId(id);
            // A propósito NO pasa por VisibilidadConversacion.Puede acá: esa función respondería
            // 404 para "ya asignada a otro profesional" (para no revelar que existe en un GET
            // pasivo), pero Tomar es la acción de DISPUTAR una escalación recién vista como sin
            // asignar - si alguien ya la tiene, lo correcto es el 409 "ya la tomó otra persona"
            // que pide el Paso 3, no un 404 que haga pensar que desapareció. Si directamente no
            // existe o no es una escalación (nunca se ofrece Tomar para eso), sigue siendo 404.
            if (c is null || c.Estado is not (EstadoConversacion.DerivadaHumano or EstadoConversacion.Urgente))
                return Results.NotFound();
            if (c.IdProfesionalAsignado == idPropio) return Results.NoContent();   // ya la tenía: idempotente
            if (c.IdProfesionalAsignado is not null)
                return Results.Json(new { mensaje = "Ya la tomó otra persona." }, statusCode: 409);
            if (!conversaciones.TomarSiNoTieneDueno(id, idPropio.Value))
                return Results.Json(new { mensaje = "Ya la tomó otra persona." }, statusCode: 409);   // perdió la carrera atómica
            AuditarConIds(ctx, "Toma de conversación de chat sin asignar", id, c.IdPaciente);
            return Results.NoContent();
        });

        // Devolver (Paso 3): quien la tiene asignada (o el administrador) la vuelve a "sin
        // asignar" - por ejemplo, si ya no puede atenderla. Motivo de la misma lista cerrada.
        g.MapPut("/conversaciones/{id}/devolver", (int id, DevolverConversacionRequest req, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var (esAdmin, idPropio) = AutorizacionCasos.Resolver(ctx);
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            if (!esAdmin && (idPropio is null || c.IdProfesionalAsignado != idPropio))
                return Results.Json(new { mensaje = "Solo quien la tiene asignada (o el administrador) puede devolverla." }, statusCode: 403);
            if (c.IdProfesionalAsignado is null)
                return Results.Json(new { mensaje = "Esta conversación ya está sin asignar." }, statusCode: 400);
            if (!MotivoReasignacion.Categorias.Contains(req.MotivoCategoria))
                return Results.Json(new { mensaje = $"Motivo no reconocido: \"{req.MotivoCategoria}\". Tiene que ser uno de: {string.Join(", ", MotivoReasignacion.Categorias)}." }, statusCode: 400);
            conversaciones.AsignarProfesional(id, null);
            AuditarConIds(ctx, "Devolución de conversación de chat a sin asignar", id, c.IdPaciente);
            return Results.NoContent();
        });

        // ── Notas internas ──────────────────────────────────────
        // Privadas del equipo: nunca al paciente, nunca a auditoría (ni acá ni en Auditar).
        g.MapGet("/conversaciones/{id}/notas", (int id, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            return c is null || !VisibilidadConversacion.Puede(ctx, c)
                ? Results.NotFound()
                : Results.Ok(new NotaInternaConversacionDAO().ListarPorConversacion(id));
        });

        g.MapPost("/conversaciones/{id}/notas", (int id, NotaInternaRequest req, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var texto = (req.Texto ?? "").Trim();
            if (texto.Length == 0) return Results.BadRequest(new { mensaje = "La nota está vacía." });
            if (texto.Length > 1000) return Results.BadRequest(new { mensaje = "La nota no puede superar los 1000 caracteres." });
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            int? idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
            var idNota = new NotaInternaConversacionDAO().Insertar(id, texto, idUsuario);
            return Results.Created($"/mensajeria/conversaciones/{id}/notas/{idNota}", new { idNota });
        });

        // Solo quien la escribió (o el administrador) puede borrarla.
        g.MapDelete("/conversaciones/{id}/notas/{idNota}", (int id, int idNota, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var conv = conversaciones.ObtenerPorId(id);
            if (conv is null || !VisibilidadConversacion.Puede(ctx, conv)) return Results.NotFound();
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

        // ── Etiquetas: catálogo (Paso 5 - ABM solo Configurador; antes tan abierto como
        // preguntas-frecuentes, pero el catálogo de etiquetas de chats con información de
        // salud mental no debería poder tocarlo cualquier Profesional). La lectura sigue
        // abierta: cualquiera que vea una conversación necesita poder mostrar sus chips.
        g.MapGet("/etiquetas", () => new EtiquetaConversacionDAO().ObtenerActivas());

        g.MapPost("/etiquetas", (EtiquetaConversacion e, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo CRM y atención al cliente administra el catálogo de etiquetas." }, statusCode: 403);
            var error = ValidarEtiqueta(e);
            if (error != null) return Results.BadRequest(new { mensaje = error });
            try { e.IdEtiqueta = new EtiquetaConversacionDAO().Insertar(e); return Results.Created($"/mensajeria/etiquetas/{e.IdEtiqueta}", e); }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { return Results.Json(new { mensaje = "Ya existe una etiqueta con ese nombre." }, statusCode: 409); }
        });

        g.MapPut("/etiquetas/{id}", (int id, EtiquetaConversacion e, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo CRM y atención al cliente administra el catálogo de etiquetas." }, statusCode: 403);
            var error = ValidarEtiqueta(e);
            if (error != null) return Results.BadRequest(new { mensaje = error });
            var dao = new EtiquetaConversacionDAO();
            if (dao.ObtenerPorId(id) is null) return Results.NotFound();
            e.IdEtiqueta = id;
            try { dao.Modificar(e); return Results.Ok(e); }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { return Results.Json(new { mensaje = "Ya existe una etiqueta con ese nombre." }, statusCode: 409); }
        });

        // Baja lógica: no se borran las ya puestas en conversaciones, solo deja de ofrecerse.
        g.MapDelete("/etiquetas/{id}", (int id, HttpContext ctx) =>
        {
            var (esAdmin, _) = AutorizacionCasos.Resolver(ctx);
            if (!esAdmin) return Results.Json(new { mensaje = "Solo CRM y atención al cliente administra el catálogo de etiquetas." }, statusCode: 403);
            var dao = new EtiquetaConversacionDAO();
            if (dao.ObtenerPorId(id) is null) return Results.NotFound();
            dao.Desactivar(id);
            return Results.NoContent();
        });

        // ── Etiquetas: asignación a una conversación (pertenencia de la conversación,
        // no del catálogo - cualquiera que la vea puede taggearla) ────────────
        g.MapPost("/conversaciones/{id}/etiquetas/{idEtiqueta}", (int id, int idEtiqueta, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            if (new EtiquetaConversacionDAO().ObtenerPorId(idEtiqueta) is null) return Results.NotFound();
            new EtiquetaConversacionDAO().Agregar(id, idEtiqueta);
            return Results.NoContent();
        });

        g.MapDelete("/conversaciones/{id}/etiquetas/{idEtiqueta}", (int id, int idEtiqueta, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
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
            p.IdProfesional = esAdmin ? null : idPropio;   // CRM: siempre global; Profesional: siempre propia
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

        // El cliente pide siempre destinatario=Administrador (la campana de bandeja.js, solo del
        // Configurador desde la D2: un Profesional recibe 403) - el filtro por pertenencia
        // tiene que hacerse acá, nunca confiando en lo que mande el cliente. Una conversación
        // puede haber quedado asignada a otra persona DESPUÉS de crearse el aviso (por
        // ejemplo, "un paciente pidió hablar con alguien" seguido de un Tomar de otro
        // profesional); por eso se revisa la pertenencia ACTUAL de la conversación referida,
        // no un dato grabado en el momento del aviso. Los avisos sin conversación (turnos)
        // ya vienen con su propio IdProfesional y no necesitan este filtro.
        g.MapGet("/notificaciones-internas", (string? destinatario, bool? soloNoLeidas, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var lista = new NotificacionInternaDAO().Listar(string.IsNullOrWhiteSpace(destinatario) ? null : destinatario, soloNoLeidas ?? false);
            var cache = new Dictionary<int, Conversacion?>();
            return lista.Where(n =>
            {
                if (n.IdConversacion is not int idConv) return true;
                if (!cache.TryGetValue(idConv, out var c)) cache[idConv] = c = conversaciones.ObtenerPorId(idConv);
                return c is not null && VisibilidadConversacion.Puede(ctx, c);
            }).ToList();
        });

        g.MapPut("/notificaciones-internas/{id}/leida", (int id, HttpContext ctx, IConversacionDAO conversaciones) =>
        {
            var dao = new NotificacionInternaDAO();
            var n = dao.ObtenerPorId(id);
            if (n is null) return Results.NotFound();
            if (n.IdConversacion is int idConv)
            {
                var c = conversaciones.ObtenerPorId(idConv);
                if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            }
            dao.MarcarLeida(id);
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

        // Chats v2, Fase 1: vincular una conversación con un paciente, interruptor de IA y tomar control (Endpoints/ChatsVinculoEndpoints.cs), y el interruptor global de IA (Configurador).
        g.MapChatsVinculo();
        app.MapConfiguracionChats();

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
    // Services/AutorizacionReporte.cs, para todo el grupo. /simulado/entrante sigue
    // siendo solo de prueba; el webhook real de WhatsApp es un endpoint aparte,
    // POST /mensajeria/whatsapp/entrante (fuera de este grupo), con su propio candado
    // de EsLocal() + secreto compartido — ver ahí mismo.
    public static RouteGroupBuilder RequiereMensajeriaHabilitada(this RouteGroupBuilder grupo) =>
        grupo.AddEndpointFilter(async (ctx, next) =>
            MensajeriaDisponible(ctx.HttpContext) ? await next(ctx) : Results.NotFound());

    // Candado de una ruta suelta: solo responde a pedidos de la propia máquina, sin importar SoloLocal (EsLocal: ni uno que pasó por un proxy). A cualquier otro, 404, el mismo que
    // el del grupo y el del webhook: no confirma que la ruta existe. Corre después del filtro del grupo, que sigue valiendo.
    private static TBuilder SoloDesdeLaPropiaMaquina<TBuilder>(this TBuilder ruta) where TBuilder : IEndpointConventionBuilder =>
        ruta.AddEndpointFilter(async (ctx, next) => EsLocal(ctx.HttpContext) ? await next(ctx) : Results.NotFound());

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

    // Variante estricta para Pertenencia de conversaciones (apertura, asignar, tomar,
    // devolver - ver CAMBIOS_AGUS.md): a diferencia de Auditar, nunca recibe texto libre ni
    // el nombre del paciente, solo ids. "Regla inamovible" del pedido: ni el contenido de
    // los mensajes ni nada más allá de ids y la acción van a logs o auditoría.
    // Cierre de seguridad, Paso 6: el mecanismo en sí se extrajo a Services/AuditoriaIds.cs
    // (compartido con la auditoría de acceso a datos clínicos) — esto queda como alias
    // para no tocar los 4 call sites existentes.
    private static void AuditarConIds(HttpContext http, string accion, int idConversacion, int? idPaciente)
    {
        AuditoriaIds.Registrar(http, accion, "Conversacion", idConversacion, idPaciente);
    }

    // No se usa AuditoriaIds.Registrar acá: pide un IdEntidad (int) obligatorio, y no hay
    // ninguno natural para "la única conexión de WhatsApp de la clínica" — queda sin usar
    // (nullable en el modelo), mismo criterio que otras filas sin un id puntual. Nunca
    // incluye el contenido del QR, solo el número (y ni eso, en /confirmar-vinculacion si
    // no hay).
    private static void AuditarWhatsApp(HttpContext http, string accion, string detalle)
    {
        var idUsuario = http.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(http)?.IdUsuario;
        if (idUsuario is null) return;
        try
        {
            new LogAuditoriaDAO().Insertar(new LogAuditoria { IdUsuario = idUsuario.Value, Accion = accion, Entidad = "WhatsApp", Detalle = detalle });
        }
        catch (Exception) { /* la auditoría no debe impedir responder */ }
    }
}

// Cuerpo de POST /mensajeria/whatsapp/vincular: Nuevo = reemplazar el código que está esperando escaneo por uno nuevo ("Generar uno nuevo").
public record SolicitudQrWhatsApp(bool? Nuevo);

// Cuerpo del webhook de whatsapp-service (y del simulador, que solo usa los dos primeros). Los cuatro últimos son del contrato v2 y opcionales: sin ellos, el cuerpo es el de
// siempre. tipo: texto, audio, imagen... (TiposDeMensaje); id: el del mensaje en WhatsApp; ts: la hora a la que se escribió, en segundos desde 1970; nombre: el del perfil.
public record MensajeEntranteRequest(string? Telefono, string? Texto, string? Tipo = null, string? Id = null, long? Ts = null, string? Nombre = null);
public record RespuestaHumanaRequest(string? Texto);
public record MarcarLeidaRequest(int Hasta);
// Nota: a diferencia de /casos/{id}/reasignar, esta acción no tiene un campo de nota libre
// - no hay un CasoDerivacion (ni nada análogo) donde guardarla sin inventar una tabla nueva
// que nadie pidió para este alcance.
public record AsignarConversacionRequest(int IdProfesional, string MotivoCategoria);
public record DevolverConversacionRequest(string MotivoCategoria);
public record NotaInternaRequest(string? Texto);
