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
        IEmocionesCifrado cifrado, TurnoReglasService reglas, int idProfesional, bool esAdmin)
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
            FechaHora = DateTime.Now, Transcripcion = req.Transcripcion, NotasEvolucion = req.NotasEvolucion, TipoCierre = tipo
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
        if (r.Repetido)
        {
            // Ya estaba guardado: no se vuelve a evaluar ni a registrar nada de las emociones.
            emociones = new { estado = r.TieneEmociones ? EvaluacionEmociones.Guardadas : EvaluacionEmociones.NoEnviadas, motivo = (string?)null, mensaje = (string?)null };
        }
        else
        {
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
            // HOOK: lo que sigue después del cierre. El panel de la sala lo despacha como el evento ys:cierre-guardado; la feature de alta / derivación se engancha ahí.
            siguientePaso = new { tipo = CierreSesionReglas.SiguientePaso(r.TipoCierre), idPaciente = req.IdPaciente, idProfesional, idSesion = r.IdSesion, idTurno = req.IdTurno }
        };
        return r.Repetido ? Results.Ok(cuerpo) : Results.Created("/sesiones-clinicas", cuerpo);
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
