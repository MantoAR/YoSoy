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

        return app;
    }
}
