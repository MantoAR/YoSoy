using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

// Primeros pasos del paciente y revisión del formulario de admisión. Se registra con app.MapPrimerosPasos().
//
//   GET  /pacientes/{id}/primeros-pasos                             Los 7 pasos del recorrido, calculados con datos reales, y el único botón de «qué tengo que hacer ahora».
//   PUT  /pacientes/{id}/informe-admision                           El paciente completa de nuevo «Sobre mí» cuando la recepcionista se lo devolvió.
//   GET  /profesionales/elegibles                                   Los profesionales que el paciente puede marcar como preferidos (nombre y especialidad).
//   PUT  /informes-admision/{id}/aceptar | /devolver | /otro-recurso La recepcionista revisa el formulario (Admisión). Cada una pide { confirmo: true }.
//   POST /pacientes/{id}/consentimiento-telemedicina/revocar        El paciente revoca su consentimiento (queda la constancia: la fila no se borra).
public static class PrimerosPasosEndpoints
{
    public static IEndpointRouteBuilder MapPrimerosPasos(this IEndpointRouteBuilder app)
    {
        // Los caminos que no reciben las opciones (RN-02 al confirmar un turno) leen la vigencia anual de ConsentimientoTratamiento.VigenciaMeses: se fija acá y sigue los cambios de configuración.
        var opciones = app.ServiceProvider.GetService<IOptionsMonitor<ConsentimientoOptions>>();
        if (opciones is not null)
        {
            ConsentimientoTratamiento.VigenciaMeses = opciones.CurrentValue.VigenciaMeses;
            opciones.OnChange(o => ConsentimientoTratamiento.VigenciaMeses = o.VigenciaMeses);
        }

        app.MapGet("/pacientes/{id}/primeros-pasos", (int id, IOptionsMonitor<ConsentimientoOptions> consentimiento, IOptionsMonitor<VideoOptions> video,
            TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad, AsignacionAutomaticaService asignacion) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo is { EsPaciente: true } && yo.IdPaciente != id)
                return Results.Json(new { mensaje = "Solo podés ver tus propios primeros pasos." }, statusCode: 403);
            if (!yo.EsPaciente && !yo.HaceAdmision && !yo.EsAuditor && !yo.EsConfigurador && !yo.RecepcionAdmision)
                return Results.Json(new { mensaje = "No tenés permiso para ver los primeros pasos de este paciente." }, statusCode: 403);
            if (new PacienteDAO().ObtenerPorId(id) is null) return Results.NotFound();

            var ahora = reloj.GetLocalNow().DateTime;
            var estado = ConsentimientoTratamiento.Estado(id, consentimiento.CurrentValue, ahora);
            var datos = new PrimerosPasosDAO().Cargar(id, estado, ahora, asignacion);
            var r = PrimerosPasos.Calcular(datos, ahora, video.CurrentValue.Ventana.MinutosAntes);
            return Results.Ok(new
            {
                ahora = ahora.ToString("yyyy-MM-ddTHH:mm:ss"),
                pasos = r.Pasos.Select(p => new { p.Clave, p.Titulo, p.Estado, p.Detalle, accion = p.Accion }),
                r.PasoActual, r.Cerrado, r.MotivoCierre, principal = r.Principal, r.Mensaje, hechos = r.Hechos, total = r.Pasos.Count,
                sesion = r.Sesion is null ? null : new
                {
                    r.Sesion.IdTurno, inicio = r.Sesion.Inicio.ToString("yyyy-MM-ddTHH:mm:ss"),
                    habilitaDesde = r.Sesion.HabilitaDesde.ToString("yyyy-MM-ddTHH:mm:ss"), r.Sesion.Habilitada, r.Sesion.CodigoSala
                },
                profesional = datos.Profesional is null ? null : new { datos.Profesional.IdProfesional, datos.Profesional.Nombre, datos.Profesional.Especialidad },
                // La propuesta automática que espera su respuesta (migración 32): profesional, día y hora; puedeCambiar = todavía le quedan propuestas por pedir.
                propuesta = datos.Propuesta is null ? null : new
                {
                    datos.Propuesta.IdPropuesta, datos.Propuesta.IdProfesional, datos.Propuesta.Profesional, datos.Propuesta.Especialidad,
                    fechaHora = datos.Propuesta.FechaHora.ToString("yyyy-MM-ddTHH:mm:ss"), expiraEn = datos.Propuesta.ExpiraEn.ToString("yyyy-MM-ddTHH:mm:ss"),
                    datos.Propuesta.NroPropuesta, datos.Propuesta.PuedeCambiar, maxPropuestas = AsignacionAutomaticaReglas.MaxPropuestas,
                    cuando = AsignacionAutomaticaReglas.Cuando(datos.Propuesta.FechaHora)
                },
                asignacionPorResolver = datos.AsignacionPorResolver,
                consentimiento = new
                {
                    vigente = estado.Vigente, motivo = estado.Vigente ? null : estado.Motivo, fechaFirma = estado.FechaFirma, version = estado.Version,
                    vigenciaMeses = consentimiento.CurrentValue.VigenciaMeses
                }
            });
        }).RequireAuthorization();

        app.MapGet("/profesionales/elegibles", () =>
            Results.Ok(PreferenciasAdmision.ProfesionalesElegibles().Select(p => new { p.IdProfesional, p.Nombre, p.Especialidad })))
            .Solo(Quien.Paciente | Quien.Admision | Quien.RecepcionAdmision);

        // El paciente completa de nuevo el formulario devuelto. Sobre el mismo informe: vuelve a «Pendiente» para otra revisión. No crea otro informe ni otro caso.
        app.MapPut("/pacientes/{id}/informe-admision", (int id, InformeAdmision i, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdPaciente != id)
                return Results.Json(new { mensaje = "Solo podés completar tu propio formulario." }, statusCode: 403);
            var actual = new InformeAdmisionDAO().ObtenerPorPaciente(id);
            if (actual is null) return Results.NotFound();
            if (actual.Estado == EstadoInforme.Pendiente)
                return Results.Json(new { codigo = "ya-enviado", mensaje = "Tu formulario ya está enviado y en revisión." }, statusCode: 409);
            if (actual.Estado != EstadoInforme.Devuelto)
                return Results.Json(new { codigo = "estado", mensaje = "Tu formulario no está pedido de nuevo: no hace falta que lo completes otra vez." }, statusCode: 409);

            var error = PreferenciasAdmision.Validar(i.HorarioPreferido, i.ProfesionalesPreferidos, IdsElegibles());
            if (error is not null) return Results.BadRequest(new { mensaje = error });
            i.IdInforme = actual.IdInforme;
            i.IdPaciente = id;
            if (!new InformeAdmisionDAO().ReenviarDevuelto(i))
                return Results.Json(new { codigo = "estado", mensaje = "Tu formulario cambió mientras lo completabas. Actualizá la pantalla." }, statusCode: 409);
            AuditoriaIds.Registrar(ctx, "Reenvió el formulario de admisión", "InformeAdmision", actual.IdInforme, id);
            AsignacionAutomaticaService.AvisarFormularioNuevo(actual.IdInforme, id);   // vuelve a «Para revisar»: aviso a la recepción
            return Results.Ok(new { idInforme = actual.IdInforme, estado = EstadoInforme.Pendiente });
        }).Solo(Quien.Paciente);

        app.MapPut("/informes-admision/{id}/aceptar", (int id, RevisionRequest req, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad,
            IOptionsMonitor<ConsentimientoOptions> consentimiento, AsignacionAutomaticaService asignacion) =>
        {
            if (Falta(req) is { } faltaConfirmar) return faltaConfirmar;
            var yo = identidad.Resolver(ctx)!;
            var ahora = reloj.GetLocalNow().DateTime;
            AsignacionAutomaticaService.VencerVencidas(ahora);
            // El sistema propone profesional y horario dentro de la misma transacción de la aceptación; la acción de origen es de esta persona de recepción.
            var r = RevisionAdmision.Aceptar(id, yo.IdUsuario, ahora, (con, tx, idInforme, idPaciente) => asignacion.ProponerAlAceptar(con, tx, idInforme, idPaciente, yo.IdUsuario, ahora));
            if (r.Ok && r.IdPaciente is int pac)
            {
                if (r.Cambio) AuditoriaIds.Registrar(ctx, "Aceptó el formulario de admisión", "InformeAdmision", id, pac);
                // Aceptado el formulario, si el consentimiento no está vigente se le avisa para que firme (campanita y mail). El aviso no se repite mientras siga sin leer.
                AvisarSiNoFirmo(ctx, pac, consentimiento.CurrentValue);
            }
            return Responder(r);
        }).Solo(Quien.Admision | Quien.RecepcionAdmision);   // la recepcionista: profesional o CRM, con el privilegio Admisión

        app.MapPut("/informes-admision/{id}/devolver", (int id, RevisionRequest req, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad) =>
        {
            if (Falta(req) is { } faltaConfirmar) return faltaConfirmar;
            var yo = identidad.Resolver(ctx)!;
            var r = RevisionAdmision.Devolver(id, yo.IdUsuario, req.Aclaracion, reloj.GetLocalNow().DateTime);
            if (r.Ok && r.Cambio && r.IdPaciente is int pac) AuditoriaIds.Registrar(ctx, "Devolvió el formulario de admisión con una aclaración", "InformeAdmision", id, pac);
            return Responder(r);
        }).Solo(Quien.Admision | Quien.RecepcionAdmision);   // la recepcionista: profesional o CRM, con el privilegio Admisión

        app.MapPut("/informes-admision/{id}/otro-recurso", (int id, RevisionRequest req, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad) =>
        {
            if (Falta(req) is { } faltaConfirmar) return faltaConfirmar;
            var yo = identidad.Resolver(ctx)!;
            var r = RevisionAdmision.OtroRecurso(id, yo.IdUsuario, req.Motivo, reloj.GetLocalNow().DateTime);
            if (r.Ok && r.Cambio && r.IdPaciente is int pac) AuditoriaIds.Registrar(ctx, "Cerró el ingreso con «otro recurso»", "InformeAdmision", id, pac);
            return Responder(r);
        }).Solo(Quien.Admision | Quien.RecepcionAdmision);   // la recepcionista: profesional o CRM, con el privilegio Admisión

        // Revocar el consentimiento de tratamiento. «Borrar» es revocar CON CONSTANCIA: la fila no se elimina (la auditoría tiene que conservar quién firmó, cuándo y cuándo revocó).
        // TODO-VALIDAR: si el consentimiento revocado debe conservarse tal cual o anonimizarse pasado un tiempo lo decide alguien de formación legal.
        app.MapPost("/pacientes/{id}/consentimiento-telemedicina/revocar", (int id, RevisionRequest req, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad,
            IOptionsMonitor<ConsentimientoOptions> consentimiento) =>
        {
            var yo = identidad.Resolver(ctx);
            if (yo is null || yo.IdPaciente != id)
                return Results.Json(new { mensaje = "Solo el propio paciente puede revocar su consentimiento." }, statusCode: 403);
            if (req.Confirmo != true)
                return Results.BadRequest(new { mensaje = "Para revocar hay que confirmarlo de forma explícita." });
            var paciente = new PacienteDAO().ObtenerPorId(id);
            if (paciente is null) return Results.NotFound();

            var ahora = reloj.GetLocalNow().DateTime;
            var op = consentimiento.CurrentValue;
            var resultado = ConsentimientoRevocacion.Revocar(id, paciente.Nombre, op.TipoRequerido, yo.IdUsuario, ahora);
            if (resultado.Revocadas == 0)
                return Results.Json(new { revocado = false, mensaje = "Tu consentimiento ya estaba revocado o no lo habías firmado." });
            AuditoriaIds.Registrar(ctx, "Revocó el consentimiento de tratamiento", "Consentimiento", resultado.PrimerId, id);
            return Results.Ok(new
            {
                revocado = true,
                mensaje = "Revocaste tu consentimiento. Mientras no lo firmes de nuevo no se te puede asignar un profesional ni confirmar turnos.",
                turnosAvisadosAlProfesional = resultado.TurnosAvisados
            });
        }).Solo(Quien.Paciente);

        return app;
    }

    /// <summary>
    /// Antes de crear un formulario: las preferencias tienen que ser válidas y el paciente no puede tener otro (uno solo por ingreso; si se lo devolvieron lo completa de nuevo con PUT).
    /// null si se puede crear.
    /// </summary>
    public static IResult? RechazarFormularioNuevo(InformeAdmision i)
    {
        var existente = new InformeAdmisionDAO().ObtenerPorPaciente(i.IdPaciente);
        if (existente is not null)
            return Results.Json(new
            {
                codigo = existente.Estado == EstadoInforme.Devuelto ? "devuelto" : "ya-enviado",
                mensaje = existente.Estado == EstadoInforme.Devuelto
                    ? "La recepción te devolvió el formulario: completalo de nuevo desde tus primeros pasos."
                    : "Ya enviaste tu formulario «Sobre mí». No hace falta que lo mandes otra vez."
            }, statusCode: 409);
        var error = PreferenciasAdmision.Validar(i.HorarioPreferido, i.ProfesionalesPreferidos, IdsElegibles());
        return error is null ? null : Results.BadRequest(new { mensaje = error });
    }

    private static IReadOnlySet<int> IdsElegibles() => PreferenciasAdmision.ProfesionalesElegibles().Select(p => p.IdProfesional).ToHashSet();

    private static IResult? Falta(RevisionRequest req) =>
        req.Confirmo == true ? null : Results.BadRequest(new { codigo = "sin-confirmacion", mensaje = "Confirmá la acción para continuar." });

    private static IResult Responder(ResultadoRevision r) =>
        Results.Json(new { r.Codigo, r.Mensaje, r.Cambio, r.Estado, r.Asignacion }, statusCode: r.Status);

    private static void AvisarSiNoFirmo(HttpContext ctx, int idPaciente, ConsentimientoOptions op)
    {
        try
        {
            var estado = ConsentimientoTratamiento.Estado(idPaciente, op);
            if (estado.Vigente) return;
            var servicios = ctx.RequestServices;
            AvisoParaFirmar.Pedir(idPaciente, ConsentimientoTratamiento.UrlFirmaPorDefecto, string.IsNullOrWhiteSpace(op.UrlFirma) ? null : op.UrlFirma,
                servicios.GetService<EmailService>(), servicios.GetService<ILoggerFactory>()?.CreateLogger("Consentimiento"));
        }
        catch (Exception) { /* el aviso no puede tapar la aceptación */ }
    }
}

/// <summary>Cuerpo de las acciones con confirmación explícita. Aclaracion (devolver) y Motivo (otro recurso) según la acción.</summary>
public record RevisionRequest(bool? Confirmo, string? Aclaracion = null, string? Motivo = null);

public record ResultadoRevocacion(int Revocadas, int PrimerId, int TurnosAvisados);

public static class ConsentimientoRevocacion
{
    /// <summary>
    /// Da de baja (con constancia de cuándo y quién) las firmas activas del tipo y avisa: a la recepcionista por la campanita (nada por WhatsApp) y, por cada turno futuro ya
    /// confirmado, al profesional, para que decida una persona (los turnos NO se cancelan solos). Todo en una transacción; si no había nada activo, no hace nada (idempotente).
    /// </summary>
    public static ResultadoRevocacion Revocar(int idPaciente, string? nombrePaciente, string tipo, int idUsuario, DateTime ahora)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        int primerId = 0, revocadas = 0;
        using (var cmd = new SqlCommand(@"
            UPDATE c SET Estado = N'Inactivo', FechaRevocacion = @F, IdUsuarioRevocacion = @U
            OUTPUT inserted.IdConsentimiento
            FROM Consentimiento c JOIN TipoConsentimiento tc ON tc.IdTipoConsentimiento = c.IdTipoConsentimiento
            WHERE c.IdPaciente = @P AND tc.Descripcion = @T AND ISNULL(c.Estado, N'Activo') <> N'Inactivo'", con, tx))
        {
            cmd.Parameters.AddWithValue("@F", ahora);
            cmd.Parameters.AddWithValue("@U", idUsuario);
            cmd.Parameters.AddWithValue("@P", idPaciente);
            cmd.Parameters.AddWithValue("@T", tipo);
            using var r = cmd.ExecuteReader();
            while (r.Read()) { revocadas++; if (primerId == 0) primerId = r.GetInt32(0); }
        }
        if (revocadas == 0) { tx.Rollback(); return new(0, 0, 0); }

        var nombre = string.IsNullOrWhiteSpace(nombrePaciente) ? $"el paciente #{idPaciente}" : nombrePaciente;
        ADComun.Avisar(con, tx, new("Administrador", null, "consentimiento-revocado", "Un paciente revocó su consentimiento",
            $"{nombre} revocó su consentimiento de tratamiento. No se le puede asignar profesional ni confirmar turnos hasta que vuelva a firmar."));

        // Turnos futuros ya confirmados: se avisan al profesional, que decide. No se cancela ninguno.
        var turnos = new List<(int IdTurno, int IdProfesional, DateTime Inicio)>();
        using (var cmd = new SqlCommand(@"
            SELECT t.IdTurno, t.IdProfesional, t.Fecha, t.Hora
            FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdPaciente = @P AND et.Descripcion = N'Confirmado' AND CAST(t.Fecha AS DATETIME) + CAST(t.Hora AS DATETIME) >= @Ahora", con, tx))
        {
            cmd.Parameters.AddWithValue("@P", idPaciente);
            cmd.Parameters.AddWithValue("@Ahora", ahora);
            using var r = cmd.ExecuteReader();
            while (r.Read()) turnos.Add(((int)r["IdTurno"], (int)r["IdProfesional"], ((DateTime)r["Fecha"]).Date + (TimeSpan)r["Hora"]));
        }
        foreach (var t in turnos)
            ADComun.Avisar(con, tx, new("Profesional", t.IdProfesional, "consentimiento-revocado", "Un paciente revocó su consentimiento",
                $"{nombre} revocó su consentimiento de tratamiento y tiene turno confirmado el {t.Inicio:dd/MM/yyyy} a las {t.Inicio:HH:mm} hs. El turno NO se canceló: decidí vos cómo seguir " +
                "Hasta que el paciente vuelva a firmar no se le pueden asignar profesionales ni confirmar turnos nuevos."));
        tx.Commit();
        return new(revocadas, primerId, turnos.Count);
    }
}
