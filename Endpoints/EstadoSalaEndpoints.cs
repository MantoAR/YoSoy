using Microsoft.Extensions.Options;

// GET /salas/estado?ids=1,2,3 — el estado de la sala (por abrir / abierta / cerrada) de los turnos que el paciente o el profesional pide, con la misma regla que decide el acceso (EstadoDeSala).
// Solo contesta por los turnos propios; los demás ids se omiten (no se confirma que existan). Lo leen el inicio del profesional y el del paciente.
public static class EstadoSalaEndpoints
{
    public static IEndpointRouteBuilder MapEstadoSala(this IEndpointRouteBuilder app)
    {
        app.MapGet("/salas/estado", (string? ids, HttpContext ctx, IdentidadActualService identidad, IOrigenSalaRepositorio origen, IOptionsMonitor<VideoOptions> video, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var pedidos = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).Distinct().Take(50).ToList();
            var ahora = reloj.GetLocalNow().DateTime;
            var salas = new List<object>();
            var esperando = yo.EsProfesional ? AusenciasDAO.PacientesEsperando(pedidos) : new HashSet<int>();
            foreach (var id in pedidos)
            {
                var t = origen.ObtenerTurno(id);
                if (t is null) continue;
                var esMio = (yo.EsPaciente && yo.IdPaciente == t.IdPaciente) || (yo.EsProfesional && yo.IdProfesional == t.IdProfesional);
                if (!esMio) continue;
                var (fase, desde, hasta) = EstadoDeSala.Evaluar(t.Fecha.Date + t.Hora, t.DuracionMinutos, ahora, video.CurrentValue);
                salas.Add(new
                {
                    idTurno = id, estado = t.Estado, fase = EstadoDeSala.Nombre(fase),
                    abre = desde.ToString("yyyy-MM-ddTHH:mm:ss"), cierra = hasta.ToString("yyyy-MM-ddTHH:mm:ss"), horaCierre = hasta.ToString("HH:mm"),
                    pacienteEsperando = fase == FaseSala.Abierta && esperando.Contains(id)
                });
            }
            return Results.Ok(new { ahora = ahora.ToString("yyyy-MM-ddTHH:mm:ss"), salas });
        }).Solo(Quien.Paciente | Quien.Profesional);

        // GET /salas/entrable — «el turno entrable» de quien pregunta (ver EstadoDeSala.Entrable): el Confirmado con la sala abierta ahora (el más cercano si hay más de uno) y, aparte, el próximo
        // Confirmado que todavía no abrió. Solo mira los turnos propios: no recibe ids, así que no hay forma de preguntar por los de otra persona. Lo leen el Inicio y el menú «Sala de video».
        app.MapGet("/salas/entrable", (HttpContext ctx, IdentidadActualService identidad, IOrigenSalaRepositorio origen, IOptionsMonitor<VideoOptions> video, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var ahora = reloj.GetLocalNow().DateTime;
            var propios = yo.EsPaciente ? origen.TurnosConfirmadosDe(yo.IdPaciente, null, ahora.Date.AddDays(-1))
                : yo.EsProfesional ? origen.TurnosConfirmadosDe(null, yo.IdProfesional, ahora.Date.AddDays(-1))
                : Array.Empty<TurnoConNombres>();
            var (entrable, proximo) = EstadoDeSala.Entrable(propios, ahora, video.CurrentValue);

            object? Dibujar(TurnoConNombres? x)
            {
                if (x is null) return null;
                var t = x.Turno;
                var inicio = t.Fecha.Date + t.Hora;
                var (_, desde, hasta) = EstadoDeSala.Evaluar(inicio, t.DuracionMinutos, ahora, video.CurrentValue);
                return new
                {
                    idTurno = t.IdTurno, idPaciente = t.IdPaciente, inicio = inicio.ToString("yyyy-MM-ddTHH:mm:ss"), hora = inicio.ToString("HH:mm"),
                    abre = desde.ToString("yyyy-MM-ddTHH:mm:ss"), horaAbre = desde.ToString("HH:mm"), cierra = hasta.ToString("yyyy-MM-ddTHH:mm:ss"),
                    esHoy = inicio.Date == ahora.Date,
                    // La otra parte del turno: el profesional para el paciente, el paciente para el profesional.
                    contraparte = yo.EsPaciente ? x.Profesional : x.Paciente
                };
            }

            return Results.Ok(new { ahora = ahora.ToString("yyyy-MM-ddTHH:mm:ss"), entrable = Dibujar(entrable), proximo = Dibujar(proximo) });
        }).Solo(Quien.Paciente | Quien.Profesional);

        return app;
    }
}
