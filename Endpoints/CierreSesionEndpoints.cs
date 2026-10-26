// El cierre de sesión: lo que pasa cuando el profesional termina la videollamada y guarda la sesión (POST /sesiones-clinicas con claveCierre / confirmaRealizacion).
// Vive acá, fuera de Program.cs, para que ese archivo cambie lo mínimo. El pedido de siempre (sin esos campos) sigue el camino de siempre dentro de Program.cs.
//
// El sistema propone y una persona confirma: el turno pasa a Realizado SOLO si el pedido trae confirmaRealizacion = true (el botón «Sí, se realizó» del panel), y nunca solo.
public static class CierreSesionEndpoints
{
    public static bool EsPedidoDeCierre(SesionClinicaRequest req) =>
        CierreSesionReglas.EsPedidoDeCierre(req.ClaveCierre, req.ConfirmaRealizacion);

    /// <param name="esAdmin">Quien guarda es el administrador de casos (no un profesional): puede guardar una nota, pero no confirmar que una sesión se realizó.</param>
    public static IResult Guardar(SesionClinicaRequest req, HttpContext ctx, IdentidadActualService identidad, GuardiaEmocionesService guardia,
        IEmocionesCifrado cifrado, TurnoReglasService reglas, ITranscripcionSesionRepositorio transcripciones, int idProfesional, bool esAdmin)
    {
        static IResult Falla(int status, string codigo, string? estado = null, int? idSesion = null) =>
            Results.Json(new { codigo, mensaje = CierreSesionReglas.Mensaje(codigo, estado), idSesion }, statusCode: status);

        var confirma = req.ConfirmaRealizacion == true;

        // Validaciones que no necesitan la base.
        if (!CierreSesionReglas.TryNormalizarTipo(req.TipoCierre, out var tipo)) return Falla(400, CierreSesionReglas.TipoCierreInvalido);
        if (string.IsNullOrWhiteSpace(req.ClaveCierre))
            return Falla(400, confirma ? CierreSesionReglas.ClaveRequerida : CierreSesionReglas.ClaveInvalida);
        if (!CierreSesionReglas.TryClave(req.ClaveCierre, out var clave)) return Falla(400, CierreSesionReglas.ClaveInvalida);
        if (confirma && req.IdTurno is null) return Falla(400, CierreSesionReglas.RealizacionSinTurno);
        if (confirma && esAdmin) return Falla(403, CierreSesionReglas.SoloElProfesionalConfirma);

        var dao = new CierreSesionDAO();
        if (!dao.Migracion26Disponible()) return Falla(409, CierreSesionReglas.FaltaMigracion26);

        var yo = identidad.Resolver(ctx)!;
        var s = new SesionClinica
        {
            IdTurno = req.IdTurno, IdPaciente = req.IdPaciente, IdProfesional = idProfesional,
            FechaHora = DateTime.Now, NotasEvolucion = req.NotasEvolucion, TipoCierre = tipo
        };

        var ev = guardia.Evaluar(yo, req.IdPaciente, req.Emociones, vinoElFormatoViejo: !string.IsNullOrWhiteSpace(req.HistorialEmociones));
        if (ev.Aprobada) s.IdConsentimientoVideo = ev.IdConsentimiento;

        var autor = new AutorDelCierre(yo.IdUsuario, yo.Modulo ?? yo.Rol, AuditoriaIds.Calidad(yo, req.IdPaciente));
        var r = dao.Ejecutar(s, clave, confirma, reglas.Ahora, ev.Aprobada
            ? idSesion => cifrado.Cifrar(ev.JsonPlano!, EmocionesCifradoService.DatosAsociadosDeSesion(idSesion))
            : null, autor);

        if (r.Rechazo is not null)
        {
            AuditarRechazo(yo, autor, req, idProfesional, r.Rechazo);
            var status = r.Rechazo == CierreSesionReglas.TurnoInexistente ? 404 : 409;
            return Falla(status, r.Rechazo, r.EstadoDelTurno, r.IdSesionExistente);
        }

        object emociones;
        string estadoTranscripcion;
        if (r.Repetido)
        {
            // Ya estaba guardado: no se vuelve a evaluar ni a registrar nada de las emociones, ni a encolar la
            // grabación de nuevo (ya se encoló, si correspondía, la primera vez). Se informa el estado actual.
            emociones = new { estado = r.TieneEmociones ? EvaluacionEmociones.Guardadas : EvaluacionEmociones.NoEnviadas, motivo = (string?)null, mensaje = (string?)null };
            estadoTranscripcion = transcripciones.ObtenerPorSesion(r.IdSesion)?.Estado ?? EncoladoGrabacionReglas.SinGrabacion;
        }
        else
        {
            estadoTranscripcion = EncoladoGrabacionReglas.Encolar(transcripciones, req.Grabacion, r.IdSesion);
            // La escritura vuelve a mirar el consentimiento: si el paciente revocó justo en el medio, la sesión quedó sin emociones.
            ev = GuardiaEmocionesService.TrasLaEscritura(ev, r.EmocionesGuardadas);
            guardia.Registrar(yo, ev, r.IdSesion);
            emociones = new
            {
                estado = ev.Estado,
                motivo = ev.Motivo,
                mensaje = ev.Estado == EvaluacionEmociones.Descartadas
                    ? "La sesión se guardó sin las emociones: " + GuardiaEmocionesService.MensajeDeDescarte(ev.Motivo) + "."
                    : null
            };
        }

        var cuerpo = new
        {
            idSesion = r.IdSesion,
            idTurno = req.IdTurno,
            idPaciente = req.IdPaciente,
            idProfesional,
            fechaHora = r.FechaHora,
            tipoCierre = r.TipoCierre,
            repetido = r.Repetido,
            turno = r.HayTurno ? new { cambio = r.TurnoCambio, estadoAnterior = r.EstadoAnterior, estado = r.EstadoActual, motivo = r.MotivoTurno } : null,
            // El caso solo cambia con la primera sesión realizada (Asignado -> En atención); en cualquier otro cierre, cambio = false.
            caso = r.IdCasoEnAtencion is int idCaso
                ? new { cambio = true, idCaso = (int?)idCaso, estadoAnterior = (string?)"Asignado", estado = (string?)"En atención" }
                : new { cambio = false, idCaso = (int?)null, estadoAnterior = (string?)null, estado = (string?)null },
            emociones,
            transcripcion = new { estado = estadoTranscripcion },
            // HOOK: lo que sigue después del cierre. El panel de la sala lo despacha como el evento ys:cierre-guardado; la feature de alta / derivación se engancha ahí.
            siguientePaso = new { tipo = CierreSesionReglas.SiguientePaso(r.TipoCierre), idPaciente = req.IdPaciente, idProfesional, idSesion = r.IdSesion, idTurno = req.IdTurno }
        };
        return r.Repetido ? Results.Ok(cuerpo) : Results.Created("/sesiones-clinicas", cuerpo);
    }

    /// <summary>El vínculo manual entre una sesión y una evolución: qué evoluciones se pueden vincular y el vínculo en sí.</summary>
    public static IEndpointRouteBuilder MapCierreSesion(this IEndpointRouteBuilder app)
    {
        static IResult Falla(int status, string codigo) =>
            Results.Json(new { codigo, mensaje = VinculoSesionEvolucionDAO.Mensaje(codigo) }, statusCode: status);

        // Solo el profesional de la sesión. Lo que devuelve son evoluciones SUYAS, del mismo paciente y del mismo día.
        app.MapGet("/sesiones-clinicas/{id:int}/evoluciones-vinculables", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new VinculoSesionEvolucionDAO();
            if (!new CierreSesionDAO().Migracion26Disponible()) return Results.Json(new { codigo = CierreSesionReglas.FaltaMigracion26, mensaje = CierreSesionReglas.Mensaje(CierreSesionReglas.FaltaMigracion26) }, statusCode: 409);
            var s = dao.ObtenerSesion(id);
            if (s is null || yo.IdProfesional != s.IdProfesional) return Falla(404, VinculoSesionEvolucionDAO.SesionInexistente);
            AuditoriaIds.Registrar(ctx, "Consulta de evoluciones para vincular a una sesión", "SesionClinica", id, s.IdPaciente);
            return Results.Ok(new { idSesion = s.IdSesion, idEvolucion = s.IdEvolucion, evoluciones = dao.Vinculables(s) });
        }).Solo(Quien.Profesional);

        // El profesional vincula SU sesión con UNA evolución suya del mismo paciente y del mismo día. Idempotente: repetir el mismo vínculo da 200 sin tocar nada.
        // No crea ni modifica ninguna evolución.
        app.MapPut("/sesiones-clinicas/{id:int}/evolucion", (int id, VincularEvolucionRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional)
                return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            if (req.IdEvolucion <= 0) return Results.BadRequest(new { mensaje = "Indicá la evolución a vincular." });
            if (!new CierreSesionDAO().Migracion26Disponible()) return Results.Json(new { codigo = CierreSesionReglas.FaltaMigracion26, mensaje = CierreSesionReglas.Mensaje(CierreSesionReglas.FaltaMigracion26) }, statusCode: 409);

            var s = new VinculoSesionEvolucionDAO().ObtenerSesion(id);
            var autor = new AutorDelCierre(yo.IdUsuario, yo.Modulo ?? yo.Rol, s is null ? null : AuditoriaIds.Calidad(yo, s.IdPaciente));
            var r = new VinculoSesionEvolucionDAO().Vincular(id, req.IdEvolucion, idProfesional, autor);
            if (r.Rechazo is not null)
                return Falla(r.Rechazo is VinculoSesionEvolucionDAO.SesionInexistente or VinculoSesionEvolucionDAO.EvolucionInexistente ? 404 : 409, r.Rechazo);
            return Results.Ok(new { idSesion = id, idEvolucion = req.IdEvolucion, vinculada = true, yaEstaba = r.YaEstaba });
        }).Solo(Quien.Profesional);

        return app;
    }

    // Un cierre rechazado por incoherencia deja rastro (solo ids): es lo que permite ver un intento de usar el turno de otro. Es un registro aparte: no hay transacción que lo proteja.
    private static void AuditarRechazo(Identidad yo, AutorDelCierre autor, SesionClinicaRequest req, int idProfesional, string codigo)
    {
        try
        {
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = yo.IdUsuario, Accion = "Cierre de sesión rechazado", Entidad = "Turno", IdEntidad = req.IdTurno ?? 0,
                Detalle = CierreSesionReglas.Detalle($"turno #{req.IdTurno}, motivo {codigo}, paciente enviado #{req.IdPaciente}, profesional #{idProfesional}"),
                Categoria = CategoriaAuditoria.Clinica, Espacio = autor.Espacio
            });
        }
        catch (Exception) { /* igual que AuditoriaIds: el rastro no puede tapar el motivo del rechazo */ }
    }
}
