using System.Globalization;

// Llegada tarde, inasistencia y abandono de tratamiento (rama AAA1, migración 37). Se registra con app.MapAsistencia().
// Reglas en Services/AsistenciaReglas.cs; datos en DAOs/AsistenciaDAO.cs; el aviso «Te estamos esperando» en Services/AsistenciaAvisosService.cs.
//
//   Parámetros de la organización (Configuración › Asistencia)
//     GET  /asistencia/parametros                    Cualquiera del equipo.
//     PUT  /asistencia/parametros                    { avisoMinutos, toleranciaMinutos, horasCancelacionSinCargo, ausenciasAlerta, ausenciasAbandono }. Configurador.
//
//   Turnos del día con su asistencia
//     GET  /asistencia/turnos?fecha=yyyy-MM-dd&todos=  El profesional: los suyos (el auditor, todos con todos=true). La Recepcionista con
//                                                     Gestión de pacientes: todos. Ingreso a la sala, demora, «esperando conexión», si ya se
//                                                     puede marcar Ausente y si el turno quedó cobrable. Nunca datos clínicos.
//     PUT    /turnos/{id}/inasistencia              Confirmado -> Ausente, pasada la tolerancia y si el paciente no entró. Queda cobrable.
//     DELETE /turnos/{id}/inasistencia              Ausente -> Confirmado (corrige un error). Las dos: el profesional del turno o la
//                                                     Recepcionista con Gestión de pacientes. Auditoría clínica.
//     PUT    /turnos/{id}/cobro                     { cobrable, motivo }. Solo el profesional del turno: fuerza mayor = sin cargo.
//
//   Ausencias seguidas y abandono
//     GET  /asistencia/alertas                       Pares paciente-profesional con AusenciasAlerta o más ausencias seguidas, sin abandono confirmado.
//     POST /pacientes/{id}/seguimiento-asistencia    { resultado: Sigue|Pausa|No responde|Otro, nota, idProfesional }. Un contacto con el paciente.
//     POST /pacientes/{id}/abandono                  { idProfesional, motivo }. Lo confirma el profesional tratante o el auditor: cancela sus
//                                                     turnos futuros con ese profesional y le avisa al paciente. Queda en la historia clínica.
//     GET  /pacientes/{id}/asistencia-resumen        Para el aviso de la historia clínica: ausencias seguidas, nivel, abandono y último contacto.
public static class AsistenciaEndpoints
{
    public record ParametrosRequest(int? AvisoMinutos, int? ToleranciaMinutos, int? HorasCancelacionSinCargo, int? AusenciasAlerta, int? AusenciasAbandono);
    public record CobroRequest(bool Cobrable, string? Motivo);
    public record SeguimientoRequest(string? Resultado, string? Nota, int? IdProfesional);
    public record AbandonoRequest(int? IdProfesional, string? Motivo);

    private record Par(int IdPaciente, string Paciente, string? Telefono, int IdProfesional, string Profesional, int Ausencias, string Nivel,
        DateTime UltimaAusencia, DateTime? AbandonoLocal);

    private const int MesesHaciaAtras = 6;

    private static IResult Error(int status, string mensaje) => Results.Json(new { mensaje }, statusCode: status);

    private static string? Limpio(string? texto, int maximo)
    {
        var t = texto?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length <= maximo ? t : t[..maximo];
    }

    private static DateTime Ahora(TimeProvider reloj) => reloj.GetLocalNow().DateTime;

    private static DateTime? Local(DateTime? utc, TimeProvider reloj) =>
        utc is DateTime u ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u, DateTimeKind.Utc), reloj.LocalTimeZone) : null;

    // El profesional del turno, o la Recepcionista con Gestión de pacientes.
    private static bool PuedeCambiarAsistencia(Identidad yo, int idProfesionalDelTurno) =>
        yo.GestionaPacientes || (yo.EsProfesional && yo.IdProfesional == idProfesionalDelTurno);

    /// <summary>
    /// Las ausencias seguidas de cada par paciente-profesional (de un paciente, de un profesional o de todos). Un par con el abandono ya
    /// confirmado después de su última ausencia cuenta como resuelto: vuelve a aparecer solo si falta de nuevo.
    /// </summary>
    private static List<Par> Pares(AsistenciaDAO dao, ParametrosAsistencia p, DateTime ahora, TimeProvider reloj, int? idProfesional, int? idPaciente)
    {
        var pasados = dao.TurnosPasados(ahora, MesesHaciaAtras, idProfesional, idPaciente);
        var abandonos = dao.UltimosAbandonos(idPaciente);
        var pares = new List<Par>();
        foreach (var g in pasados.GroupBy(x => (x.IdPaciente, x.IdProfesional)))
        {
            var lista = g.OrderByDescending(x => x.Inicio).ToList();
            var ausencias = AsistenciaReglas.AusenciasConsecutivas(lista.Select(x => x.Estado));
            var abandono = abandonos.TryGetValue(g.Key, out var f) ? Local(f, reloj) : null;
            if (ausencias == 0)
            {
                if (abandono is not null) pares.Add(new Par(g.Key.IdPaciente, lista[0].Paciente, lista[0].Telefono, g.Key.IdProfesional, lista[0].Profesional, 0, AsistenciaReglas.NivelOk, DateTime.MinValue, abandono));
                continue;
            }
            var ultima = lista.First(x => x.Estado == "Ausente").Inicio;
            var resuelto = abandono is DateTime a && a >= ultima;
            pares.Add(new Par(g.Key.IdPaciente, lista[0].Paciente, lista[0].Telefono, g.Key.IdProfesional, lista[0].Profesional,
                resuelto ? 0 : ausencias, resuelto ? AsistenciaReglas.NivelOk : AsistenciaReglas.Nivel(ausencias, p), ultima, abandono));
        }
        return pares;
    }

    public static IEndpointRouteBuilder MapAsistencia(this IEndpointRouteBuilder app)
    {
        // ─────────────────────────────── Parámetros ───────────────────────────────
        app.MapGet("/asistencia/parametros", () => Results.Ok(new AsistenciaDAO().Parametros()))
            .Solo(Quien.Profesional | Quien.Crm | Quien.Configurador);

        app.MapPut("/asistencia/parametros", (ParametrosRequest? req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new AsistenciaDAO();
            var antes = dao.Parametros();
            var nuevos = new ParametrosAsistencia(req?.AvisoMinutos ?? antes.AvisoMinutos, req?.ToleranciaMinutos ?? antes.ToleranciaMinutos,
                req?.HorasCancelacionSinCargo ?? antes.HorasCancelacionSinCargo, req?.AusenciasAlerta ?? antes.AusenciasAlerta,
                req?.AusenciasAbandono ?? antes.AusenciasAbandono);
            if (nuevos.Validar() is string error) return Error(400, error);
            if (nuevos == antes) return Results.Ok(nuevos);
            dao.GuardarParametros(nuevos, yo.IdUsuario);
            AuditoriaAgenda.Registrar(yo.IdUsuario, "Parámetros de asistencia modificados", "ConfiguracionOrganizacion", null,
                $"Aviso {antes.AvisoMinutos}→{nuevos.AvisoMinutos} min · tolerancia {antes.ToleranciaMinutos}→{nuevos.ToleranciaMinutos} min · " +
                $"cancelación sin cargo {antes.HorasCancelacionSinCargo}→{nuevos.HorasCancelacionSinCargo} hs · alerta {antes.AusenciasAlerta}→{nuevos.AusenciasAlerta} · " +
                $"abandono {antes.AusenciasAbandono}→{nuevos.AusenciasAbandono} ausencias seguidas", yo.Modulo ?? yo.Rol);
            return Results.Ok(nuevos);
        }).Solo(Quien.Configurador);

        // ─────────────────────────────── Turnos del día ───────────────────────────────
        app.MapGet("/asistencia/turnos", (string? fecha, bool? todos, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var ahora = Ahora(reloj);
            var dia = ahora.Date;
            if (!string.IsNullOrWhiteSpace(fecha) &&
                !DateTime.TryParseExact(fecha.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dia))
                return Error(400, "La fecha va como yyyy-MM-dd.");
            int? filtro;
            if (yo.GestionaPacientes || (yo.EsAuditor && todos == true)) filtro = null;
            else if (yo.IdProfesional is int propio) filtro = propio;
            else return Error(403, "Tu cuenta no tiene ficha de profesional.");

            var dao = new AsistenciaDAO();
            var p = dao.Parametros();
            var lista = dao.Turnos(dia, dia, filtro).Select(t =>
            {
                var ingreso = t.IngresoUtc is not null;
                return new
                {
                    t.IdTurno, t.IdPaciente, t.Paciente, t.Telefono, t.IdProfesional, t.Profesional,
                    fecha = t.Inicio.ToString("yyyy-MM-dd"), hora = t.Inicio.ToString("HH:mm"), t.Modalidad, t.Estado,
                    ingreso = Local(t.IngresoUtc, reloj)?.ToString("HH:mm"),
                    minutosDemora = ingreso ? AsistenciaReglas.MinutosDemora(t.Inicio, Local(t.IngresoUtc, reloj)!.Value)
                                            : t.Estado == "Confirmado" ? AsistenciaReglas.MinutosDemora(t.Inicio, ahora) : 0,
                    esperandoConexion = t.Estado == "Confirmado" && AsistenciaReglas.EsperandoConexion(t.Inicio, ahora, ingreso),
                    puedeMarcarAusente = AsistenciaReglas.PuedeMarcarAusente(t.Estado, t.Inicio, ahora, ingreso, p),
                    marcarAusenteDesde = t.Inicio.AddMinutes(p.ToleranciaMinutos).ToString("HH:mm"),
                    t.Cobrable, t.MotivoNoCobrable,
                    puedeCambiarCobro = yo.EsProfesional && yo.IdProfesional == t.IdProfesional && t.Estado is "Ausente" or "Cancelado"
                };
            });
            return Results.Ok(new { fecha = dia.ToString("yyyy-MM-dd"), parametros = p, turnos = lista });
        }).Solo(Quien.Profesional | Quien.GestionPacientes);

        app.MapPut("/turnos/{id:int}/inasistencia", (int id, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new AsistenciaDAO();
            var t = dao.Turno(id);
            if (t is null) return Results.NotFound();
            if (!PuedeCambiarAsistencia(yo, t.IdProfesional))
                return Error(403, "Solo el profesional del turno, o la recepción con el privilegio Gestión de pacientes, registra la inasistencia.");
            var motivo = AsistenciaReglas.MotivoParaNoMarcarAusente(t.Estado, t.Inicio, Ahora(reloj), t.IngresoUtc is not null, dao.Parametros());
            if (motivo is not null) return Error(409, motivo);
            if (!dao.MarcarAusente(id, yo.IdUsuario)) return Error(409, "El turno cambió de estado mientras tanto. Actualizá la pantalla.");
            AuditoriaIds.Registrar(ctx, "Inasistencia registrada", "Turno", id, t.IdPaciente);
            var idAusente = new TurnoRepositorioDAO().IdEstado("Ausente");
            return Results.Ok(new { idTurno = id, estado = "Ausente", idEstadoTurno = idAusente, cobrable = true });
        }).Solo(Quien.Profesional | Quien.GestionPacientes);

        app.MapDelete("/turnos/{id:int}/inasistencia", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new AsistenciaDAO();
            var t = dao.Turno(id);
            if (t is null) return Results.NotFound();
            if (!PuedeCambiarAsistencia(yo, t.IdProfesional))
                return Error(403, "Solo el profesional del turno, o la recepción con el privilegio Gestión de pacientes, corrige la inasistencia.");
            if (t.Estado != "Ausente") return Error(409, "Ese turno no está marcado como inasistencia.");
            if (!dao.DeshacerAusente(id, yo.IdUsuario)) return Error(409, "El turno cambió de estado mientras tanto. Actualizá la pantalla.");
            AuditoriaIds.Registrar(ctx, "Inasistencia corregida", "Turno", id, t.IdPaciente);
            var idConfirmado = new TurnoRepositorioDAO().IdEstado("Confirmado");
            return Results.Ok(new { idTurno = id, estado = "Confirmado", idEstadoTurno = idConfirmado });
        }).Solo(Quien.Profesional | Quien.GestionPacientes);

        app.MapPut("/turnos/{id:int}/cobro", (int id, CobroRequest? req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (req is null) return Error(400, "Falta indicar si el turno se cobra.");
            var dao = new AsistenciaDAO();
            var t = dao.Turno(id);
            if (t is null) return Results.NotFound();
            if (yo.IdProfesional != t.IdProfesional) return Error(403, "Solo el profesional del turno decide si se cobra.");
            if (t.Estado is not ("Ausente" or "Cancelado")) return Error(409, "Solo se decide el cobro de un turno ausente o cancelado.");
            var motivo = Limpio(req.Motivo, 300);
            if (!req.Cobrable && (motivo is null || motivo.Length < 3)) return Error(400, "Escribí el motivo de fuerza mayor (queda registrado).");
            dao.MarcarCobro(id, req.Cobrable, motivo);
            AuditoriaIds.Registrar(ctx, req.Cobrable ? "Turno marcado con cargo" : "Turno sin cargo por fuerza mayor", "Turno", id, t.IdPaciente);
            return Results.Ok(new { idTurno = id, cobrable = req.Cobrable, motivoNoCobrable = req.Cobrable ? null : motivo });
        }).Solo(Quien.Profesional);

        // ─────────────────────────────── Ausencias seguidas y abandono ───────────────────────────────
        app.MapGet("/asistencia/alertas", (HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            int? filtro;
            if (yo.GestionaPacientes || yo.EsAuditor) filtro = null;
            else if (yo.IdProfesional is int propio) filtro = propio;
            else return Error(403, "Tu cuenta no tiene ficha de profesional.");

            var dao = new AsistenciaDAO();
            var p = dao.Parametros();
            var seguimientos = dao.UltimosSeguimientos(null);
            var alertas = Pares(dao, p, Ahora(reloj), reloj, filtro, null)
                .Where(x => x.Nivel != AsistenciaReglas.NivelOk)
                .OrderByDescending(x => x.Ausencias).ThenByDescending(x => x.UltimaAusencia)
                .Select(x =>
                {
                    seguimientos.TryGetValue(x.IdPaciente, out var s);
                    return new
                    {
                        x.IdPaciente, x.Paciente, x.Telefono, x.IdProfesional, x.Profesional, x.Ausencias, x.Nivel,
                        ultimaAusencia = x.UltimaAusencia.ToString("yyyy-MM-ddTHH:mm"),
                        ultimoContacto = s is null ? null : new { s.Resultado, s.Nota, fecha = Local(s.FechaUtc, reloj)!.Value.ToString("yyyy-MM-ddTHH:mm") },
                        puedeConfirmarAbandono = yo.EsProfesional && (yo.IdProfesional == x.IdProfesional || yo.EsAuditor)
                    };
                });
            return Results.Ok(new { parametros = p, alertas });
        }).Solo(Quien.Profesional | Quien.GestionPacientes);

        app.MapPost("/pacientes/{id:int}/seguimiento-asistencia", (int id, SeguimientoRequest? req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!yo.GestionaPacientes && !AutorizacionClinica.PuedeVerPaciente(ctx, id)) return Error(403, "No atendés a ese paciente.");
            var resultado = req?.Resultado?.Trim();
            if (resultado is null || !AsistenciaReglas.ResultadosSeguimiento.Contains(resultado))
                return Error(400, "Elegí el resultado del contacto: " + string.Join(", ", AsistenciaReglas.ResultadosSeguimiento) + ".");
            var idSeg = new AsistenciaDAO().RegistrarSeguimiento(id, req!.IdProfesional, resultado, Limpio(req.Nota, 500), yo.IdUsuario);
            AuditoriaIds.Registrar(ctx, "Contacto por inasistencias registrado", "SeguimientoAsistencia", idSeg, id);
            return Results.Ok(new { idSeguimiento = idSeg });
        }).Solo(Quien.Profesional | Quien.GestionPacientes);

        app.MapPost("/pacientes/{id:int}/abandono", (int id, AbandonoRequest? req, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var idProfesional = req?.IdProfesional ?? yo.IdProfesional;
            if (idProfesional is not int idProf) return Error(400, "Falta el profesional.");
            string calidad;
            if (yo.IdProfesional == idProf) calidad = "tratante";
            else if (yo.EsAuditor) calidad = "auditor";
            else return Error(403, "El abandono lo confirma el profesional tratante o el auditor.");

            var dao = new AsistenciaDAO();
            var p = dao.Parametros();
            var ahora = Ahora(reloj);
            var par = Pares(dao, p, ahora, reloj, idProf, id).FirstOrDefault();
            if (par is null || par.Ausencias < p.AusenciasAlerta)
                return Error(409, $"El paciente no tiene {p.AusenciasAlerta} o más ausencias seguidas con ese profesional (o el abandono ya está confirmado).");
            var r = dao.RegistrarAbandono(id, idProf, par.Ausencias, Limpio(req?.Motivo, 500), yo.IdUsuario, calidad, ahora, par.Profesional);
            AuditoriaIds.Registrar(ctx, "Abandono de tratamiento confirmado", "AbandonoTratamiento", r.IdAbandono, id);
            return Results.Ok(new { idAbandono = r.IdAbandono, turnosCancelados = r.TurnosCancelados });
        }).Solo(Quien.Profesional);

        app.MapGet("/pacientes/{id:int}/asistencia-resumen", (int id, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!yo.GestionaPacientes && !AutorizacionClinica.PuedeVerPaciente(ctx, id)) return Error(403, "No atendés a ese paciente.");
            var dao = new AsistenciaDAO();
            var p = dao.Parametros();
            dao.UltimosSeguimientos(id).TryGetValue(id, out var s);
            var pares = Pares(dao, p, Ahora(reloj), reloj, null, id)
                .Where(x => x.Ausencias > 0 || x.AbandonoLocal is not null)
                .Select(x => new
                {
                    x.IdProfesional, x.Profesional, x.Ausencias, x.Nivel,
                    ultimaAusencia = x.Ausencias > 0 ? x.UltimaAusencia.ToString("yyyy-MM-ddTHH:mm") : null,
                    abandonoConfirmado = x.AbandonoLocal?.ToString("yyyy-MM-ddTHH:mm"),
                    puedeConfirmarAbandono = x.Nivel != AsistenciaReglas.NivelOk && yo.EsProfesional && (yo.IdProfesional == x.IdProfesional || yo.EsAuditor)
                });
            return Results.Ok(new
            {
                parametros = p, profesionales = pares,
                ultimoContacto = s is null ? null : new { s.Resultado, s.Nota, fecha = Local(s.FechaUtc, reloj)!.Value.ToString("yyyy-MM-ddTHH:mm") }
            });
        }).Solo(Quien.Profesional | Quien.GestionPacientes);

        return app;
    }
}
