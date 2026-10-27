using System.Globalization;

// Acceso a las salas de video: POST /video/acceso. Se registra con app.MapVideo().
//
// El cliente dice de QUÉ sesión quiere entrar (origen "turno" | "admision" + id) y el servidor decide todo lo
// demás: si pertenece, si el estado y la hora lo permiten, qué sala es y con qué rol entra. A cambio le da un
// token de sala de vida corta para el signaling-server (contrato en signaling-server/README.md) y los servidores
// ICE (STUN y, si está configurado, TURN con credenciales efímeras). Reglas y ventana: AccesoSalaService.
//
// Respuestas: 200 { token, sala, rol, expiraUtc, turnDisponible, iceServers } · 400 pedido mal armado · 401 sin
// JWT · 404 no existe O no sos parte (la misma respuesta, mismo status y mismo cuerpo: no se puede sondear qué ids
// existen) · 409 estado / sala / ventana (con "codigo" y, según el caso, "abreUtc" o "cerroUtc") · 429 demasiados
// pedidos · 503 video sin configurar. Nunca se cachea (Cache-Control: no-store) y nunca se loguea el token ni las
// credenciales.
public static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideo(this IEndpointRouteBuilder app)
    {
        var loggers = app.ServiceProvider.GetRequiredService<ILoggerFactory>();
        AvisarEstadoAlArrancar(app.ServiceProvider, loggers.CreateLogger("Video"));

        app.MapPost("/video/acceso", (AccesoSalaRequest? req, HttpContext ctx, IdentidadActualService identidad,
            AccesoSalaService acceso, TokenSalaService tokens, TurnCredentialsService turn, IAuditoriaVideo auditoria,
            AceptacionVideollamadaService aceptacion, IOrigenSalaRepositorio origenSala, INotificacionInternaDAO avisos,
            Microsoft.Extensions.Options.IOptionsMonitor<VideoOptions> videoOpciones, Microsoft.Extensions.Options.IOptionsMonitor<ConsentimientoOptions> consentimientoOpciones, TimeProvider reloj) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var log = loggers.CreateLogger("Video");
            var yo = identidad.Resolver(ctx)!;

            var origen = TextoNormalizado.Normalizar(req?.Origen);
            if ((origen != OrigenSala.Turno && origen != OrigenSala.Admision) || req?.Id is not > 0)
                return Error(400, "solicitud-invalida", "Indicá de qué sesión querés entrar: origen (turno o admision) e id.");
            var id = req!.Id!.Value;

            // Sin el secreto de la señalización no se puede emitir un token. La API levanta igual; acá se avisa
            // con un mensaje genérico y el motivo real (sin el secreto) queda en el log.
            if (tokens.ProblemaDeConfiguracion() is { } problema)
            {
                log.LogWarning("Video: /video/acceso deshabilitado. {Motivo}", problema);
                return Error(503, "video-no-disponible", "La videollamada no está disponible por el momento. Probá de nuevo más tarde.");
            }

            // Si el turno todavía no tiene código de sala y la sala está abierta, la genera el servidor: el paciente puede entrar a esperar sin que el profesional haya hecho nada antes.
            if (origen == OrigenSala.Turno) SalaAutomatica.AsegurarCodigo(id, yo, origenSala, videoOpciones.CurrentValue, consentimientoOpciones.CurrentValue, reloj.GetLocalNow().DateTime);

            var r = acceso.Evaluar(yo, origen, id);
            if (!r.Ok)
            {
                // Se audita todo lo que existe y se rechazó (pertenencia, estado, ventana, sala). Un id que no existe no:
                // no hay nada a lo que atarlo y sería un registro por cada sondeo. El rechazo por pertenencia se audita
                // como sin-acceso aunque la respuesta sea la misma que la de un id que no existe.
                if (r.Estado != EstadoAcceso.NoExiste) auditoria.Registrar(yo.IdUsuario, origen, id, yo.Rol, r);
                return Rechazo(r);
            }

            // El paciente entra a la sala solo si aceptó la videollamada de ESTA sesión (migración 28); revocarla la cierra. El profesional no necesita nada.
            if (r.Rol == Roles.Paciente && yo.IdPaciente is int idPacienteSala && !aceptacion.Aceptada(origen, id, idPacienteSala))
            {
                // La regla no se saltea (tampoco en una cita urgente): se deja el bloqueo a la vista del profesional del turno.
                if (origen == OrigenSala.Turno && origenSala.ObtenerTurno(id) is { } turnoBloqueado)
                    BloqueoConsentimiento.Registrar(avisos, turnoBloqueado.IdProfesional, id, "El paciente", "intentó entrar a la sala sin aceptar usar la videollamada.");
                return Error(409, "aceptacion-video-pendiente", "Antes de entrar tenés que aceptar usar la videollamada.");
            }

            // Todo con el mismo instante con el que se decidió el acceso (r.Ahora).
            var sub = yo.IdUsuario.ToString(CultureInfo.InvariantCulture);
            var expira = tokens.CalcularExp(r.Ahora, r.Hasta!.Value);
            var token = tokens.Emitir(r.Sala!, r.Rol!, sub, expira);
            var ice = turn.Generar(sub, r.Ahora, expira, r.Hasta.Value - r.Desde!.Value);

            auditoria.Registrar(yo.IdUsuario, origen, id, r.Rol!, r);
            // Quién pidió entrar a la sala de este turno (migración 35): es lo que permite saber que el paciente nunca se conectó (ausencias). Nunca frena el acceso.
            if (origen == OrigenSala.Turno) AusenciasDAO.RegistrarConexion(id, string.Equals(r.Rol, Roles.Paciente, StringComparison.OrdinalIgnoreCase) ? "Paciente" : "Profesional");
            return Results.Ok(new
            {
                token,
                sala = r.Sala,
                rol = r.Rol,
                expiraUtc = expira.UtcDateTime,
                turnDisponible = ice.TurnDisponible,
                iceServers = ice.Servidores
            });
        }).RequireAuthorization().RequireRateLimiting("video");

        return app;
    }

    private static IResult Rechazo(ResultadoAcceso r) => r.Estado switch
    {
        // "No existe" y "existe pero no sos parte" son LA MISMA respuesta, armada en un solo lugar y no con r.Codigo /
        // r.Mensaje, para que no puedan diferir nunca. Por dentro siguen separados: solo el segundo se audita.
        EstadoAcceso.NoExiste or EstadoAcceso.SinAcceso => NoEncontrada(),
        EstadoAcceso.AunNoAbre => Results.Json(new { mensaje = r.Mensaje, codigo = r.Codigo, abreUtc = r.Desde?.UtcDateTime }, statusCode: 409),
        EstadoAcceso.YaTermino => Results.Json(new { mensaje = r.Mensaje, codigo = r.Codigo, cerroUtc = r.Hasta?.UtcDateTime }, statusCode: 409),
        _ => Error(409, r.Codigo, r.Mensaje)
    };

    private static IResult NoEncontrada() =>
        Error(404, AccesoSalaService.CodigoNoEncontrada, AccesoSalaService.MensajeNoEncontrada);

    private static IResult Error(int status, string codigo, string mensaje) =>
        Results.Json(new { mensaje, codigo }, statusCode: status);

    // Para quien administra: en el log de arranque queda claro si el video está habilitado. Sin valores secretos.
    private static void AvisarEstadoAlArrancar(IServiceProvider sp, ILogger log)
    {
        var problema = sp.GetRequiredService<TokenSalaService>().ProblemaDeConfiguracion();
        if (problema is null) log.LogInformation("Video: /video/acceso habilitado.");
        else log.LogWarning("Video: /video/acceso deshabilitado hasta que se configure. {Motivo}", problema);
    }
}

public record AccesoSalaRequest(string? Origen, int? Id);
