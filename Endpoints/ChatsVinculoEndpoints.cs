using Microsoft.Extensions.Options;

// Chats v2, Fase 1 (vista de WhatsApp): vincular una conversación con un paciente, el interruptor de IA y "tomar control". Todo lo de /mensajeria hereda el grupo del módulo CRM
// (Quien.Crm: el CRM y la sesión Administración; el Configurador solo, el Profesional, el Paciente y Facturación reciben 403; ChatsSoloCrmTests lo recorre ruta por ruta). El interruptor
// GLOBAL de IA es del Configurador y vive fuera de /mensajeria.
//
// Reglas de privacidad que están acá y no en la pantalla:
//  · NUNCA se vincula solo: una sugerencia por teléfono es solo una coincidencia, y vincular lo hace siempre una persona, con una llamada explícita;
//  · el buscador devuelve lo mínimo para estar seguro de quién es (nombre, últimos 3 dígitos del DNI, año de nacimiento) y no tiene datos clínicos ni de contacto;
//  · la auditoría (administrativa, entidad ConversacionVinculo / ConversacionIa) lleva solo ids y palabras fijas: ni el número, ni el nombre, ni el DNI, ni lo que se buscó.
public static class ChatsVinculoEndpoints
{
    public const string PoliticaBuscar = "chats-buscar-paciente";
    public const string OrigenBusqueda = "busqueda";
    public const string OrigenSugerencia = "sugerencia";

    public static RouteGroupBuilder MapChatsVinculo(this RouteGroupBuilder g)
    {
        // Buscar un paciente para vincularlo: por DNI completo (7 u 8 números) o por nombre (al menos 3 letras; todas las palabras tienen que estar). Solo activos, hasta 10.
        // La búsqueda no se audita (lo escrito es un dato personal); lo que se limita es la cantidad de pedidos.
        g.MapGet("/pacientes/buscar", (string? q, int? idConversacion, IVinculoConversacionDAO vinculos) =>
        {
            var texto = (q ?? "").Trim();
            if (texto.Length > 0 && texto.All(c => char.IsDigit(c) || c is '.' or ' '))
            {
                var dni = new string(texto.Where(char.IsDigit).ToArray());
                if (dni.Length is < 7 or > 8) return Results.Json(new { mensaje = "Para buscar por DNI escribí los 7 u 8 números." }, statusCode: 400);
                var uno = vinculos.BuscarPorDni(dni, idConversacion);
                return Results.Ok(uno is null ? Array.Empty<PacienteParaVincular>() : new[] { uno });
            }
            if (texto.Length < 3) return Results.Json(new { mensaje = "Escribí al menos 3 letras del nombre, o el DNI completo." }, statusCode: 400);
            var palabras = texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(5).Select(p => p.Length > 50 ? p[..50] : p).ToList();
            return Results.Ok(vinculos.BuscarPorNombre(palabras, idConversacion));
        }).RequireRateLimiting(PoliticaBuscar);

        // Vincular a mano. Conversación sin paciente → paciente ACTIVO. 404 si no existe la conversación o el paciente; 409 si el paciente está inactivo o la conversación ya tiene otro
        // paciente (hay que desvincular primero); si ya estaba vinculada a ESE paciente responde 200 sin cambios y sin registro nuevo. origen "sugerencia" solo vale si el paciente figura
        // entre las coincidencias del teléfono: el registro tiene que decir la verdad.
        g.MapPut("/conversaciones/{id}/vinculo", (int id, VincularPacienteRequest? req, HttpContext ctx, IConversacionDAO conversaciones, IVinculoConversacionDAO vinculos) =>
        {
            if (req is null || req.IdPaciente <= 0) return Results.Json(new { mensaje = "Falta elegir el paciente." }, statusCode: 400);
            var origen = string.IsNullOrWhiteSpace(req.Origen) ? OrigenBusqueda : req.Origen.Trim().ToLowerInvariant();
            if (origen is not (OrigenBusqueda or OrigenSugerencia)) return Results.Json(new { mensaje = "El origen tiene que ser busqueda o sugerencia." }, statusCode: 400);
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            if (origen == OrigenSugerencia && !vinculos.SugerirPorTelefono(c.Telefono, id).Any(s => s.IdPaciente == req.IdPaciente))
                return Results.Json(new { mensaje = "Ese paciente no figura entre las coincidencias de este teléfono." }, statusCode: 400);

            return vinculos.Vincular(id, req.IdPaciente, origen, Autor(ctx)) switch
            {
                ResultadoVinculo.Vinculada => Results.Ok(new { exito = true, sinCambios = false, idPaciente = req.IdPaciente }),
                ResultadoVinculo.YaEstabaVinculadaAEse => Results.Ok(new { exito = true, sinCambios = true, idPaciente = req.IdPaciente }),
                ResultadoVinculo.YaVinculadaAOtro => Results.Json(new { codigo = "YA_VINCULADA", mensaje = "Esta conversación ya está vinculada a otro paciente. Desvinculala primero." }, statusCode: 409),
                ResultadoVinculo.PacienteInactivo => Results.Json(new { codigo = "PACIENTE_INACTIVO", mensaje = "Ese paciente está dado de baja: no se puede vincular." }, statusCode: 409),
                ResultadoVinculo.PacienteNoExiste => Results.Json(new { mensaje = "El paciente no existe." }, statusCode: 404),
                _ => Results.NotFound()
            };
        });

        // Desvincular. Deja la conversación como una sin identificar: sin paciente y con el flujo del asistente reiniciado. No toca notas ni etiquetas. 409 si no tenía paciente.
        g.MapDelete("/conversaciones/{id}/vinculo", (int id, HttpContext ctx, IConversacionDAO conversaciones, IVinculoConversacionDAO vinculos) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            return vinculos.Desvincular(id, Autor(ctx)) is null
                ? Results.Json(new { codigo = "SIN_VINCULO", mensaje = "Esta conversación no está vinculada a ningún paciente." }, statusCode: 409)
                : Results.NoContent();
        });

        // Interruptor de IA de UNA conversación. Se guarda aunque el global esté apagado (efectiva dice si de verdad podría responder). Sin la migración 25: 409.
        g.MapPut("/conversaciones/{id}/ia", (int id, IaConversacionRequest? req, HttpContext ctx, IConversacionDAO conversaciones, IIaConversacionDAO ia, IConfiguracionIaChats global) =>
        {
            if (req?.Activa is not bool activa) return Results.Json(new { mensaje = "Falta indicar si la IA queda prendida o apagada." }, statusCode: 400);
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            if (!ia.ColumnaDisponible()) return Results.Json(new { codigo = "MIGRACION_25", mensaje = "Falta aplicar la migración 25 en la base para guardar este interruptor." }, statusCode: 409);
            if (!ia.Establecer(id, activa, Autor(ctx))) return Results.NotFound();
            var g_ = global.IaGlobalActiva();
            return Results.Ok(new { activa, global = g_, efectiva = PoliticaRespuestaAutomatica.IaEfectiva(g_, activa) });
        });

        // "Tomar control": una persona se queda con la conversación. Apaga la IA de esa conversación y, en el modo Asistente, saca al asistente de reglas pasándola de Activa a
        // "escalada a persona" (el mismo mecanismo que ya lo frena). En el modo Personal no hay asistente: solo se apaga la IA.
        g.MapPost("/conversaciones/{id}/tomar-control", (int id, HttpContext ctx, IConversacionDAO conversaciones, IIaConversacionDAO ia, IOptionsMonitor<MensajeriaOptions> opciones) =>
        {
            var c = conversaciones.ObtenerPorId(id);
            if (c is null || !VisibilidadConversacion.Puede(ctx, c)) return Results.NotFound();
            var personal = opciones.CurrentValue.EsModoPersonal;
            var r = ia.TomarControl(id, pausarAsistente: !personal, Autor(ctx));
            if (r is null) return Results.NotFound();
            return Results.Ok(new
            {
                estado = r.Estado, iaApagada = r.IaAhoraApagada, iaDisponible = r.IaDisponible,
                quienResponde = PoliticaRespuestaAutomatica.QuienResponde(personal, r.Estado, iaEfectiva: false)
            });
        });

        return g;
    }

    /// <summary>El interruptor GLOBAL de IA de los chats: solo el Configurador (también la sesión Administración). Fuera de /mensajeria porque es configuración de la organización.</summary>
    public static IEndpointRouteBuilder MapConfiguracionChats(this IEndpointRouteBuilder app)
    {
        var cfg = app.MapGroup("/configuracion").Solo(Quien.Configurador);

        cfg.MapGet("/chats-ia", (IConfiguracionIaChats global) => Results.Ok(new { activa = global.IaGlobalActiva(), implementada = PoliticaRespuestaAutomatica.IaImplementada }));

        cfg.MapPut("/chats-ia", (IaConversacionRequest? req, HttpContext ctx, IConfiguracionIaChats global) =>
        {
            if (req?.Activa is not bool activa) return Results.Json(new { mensaje = "Falta indicar si la IA queda prendida o apagada." }, statusCode: 400);
            return global.EstablecerIaGlobal(activa, Autor(ctx))
                ? Results.Ok(new { activa, implementada = PoliticaRespuestaAutomatica.IaImplementada })
                : Results.Json(new { mensaje = "La base no tiene la tabla de configuración (ConfiguracionOrganizacion)." }, statusCode: 503);
        });
        return app;
    }

    /// <summary>
    /// Lo que el panel de una conversación necesita saber además de los datos del paciente: el teléfono formateado, las coincidencias sugeridas por teléfono (solo si no está
    /// vinculada; NUNCA se vincula con ellas), el estado de la IA y quién responde.
    /// </summary>
    public static object DatosDelPanel(Conversacion c, HttpContext http)
    {
        var sp = http.RequestServices;
        var vinculos = sp.GetRequiredService<IVinculoConversacionDAO>();
        var ia = sp.GetRequiredService<IIaConversacionDAO>();
        var global = sp.GetRequiredService<IConfiguracionIaChats>().IaGlobalActiva();
        var personal = sp.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue.EsModoPersonal;
        var porConversacion = ia.Obtener(c.IdConversacion);                       // null: sin la migración 25
        var efectiva = PoliticaRespuestaAutomatica.IaEfectiva(global, porConversacion == true);
        return new
        {
            telefonoMostrar = TelefonoWhatsApp.Formatear(c.Telefono),
            sugerencias = c.IdPaciente is null ? vinculos.SugerirPorTelefono(c.Telefono, c.IdConversacion) : new List<PacienteParaVincular>(),
            ia = new { disponible = porConversacion is not null, activa = porConversacion == true, global, efectiva, implementada = PoliticaRespuestaAutomatica.IaImplementada },
            quienResponde = PoliticaRespuestaAutomatica.QuienResponde(personal, c.Estado, efectiva)
        };
    }

    private static QuienAudita Autor(HttpContext ctx)
    {
        var yo = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)!;
        return new QuienAudita(yo.IdUsuario, yo.Modulo ?? yo.Rol);
    }
}

public record VincularPacienteRequest(int IdPaciente, string? Origen);
public record IaConversacionRequest(bool? Activa);
