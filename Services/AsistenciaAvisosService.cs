// Aviso «Te estamos esperando» (rama AAA1, migración 37). Cada minuto busca los turnos confirmados de hoy, por video, que empezaron
// hace Asistencia.AvisoMinutos (5) o más y menos de Asistencia.ToleranciaMinutos (15), y a cuya sala el paciente no entró; le deja un aviso
// en su campanita UNA sola vez por turno (Notificacion.Evento = «espera-turno:{id}», índice único de la migración 31).
// No cambia el estado del turno: a los 15 minutos lo marca una persona (ver Endpoints/AsistenciaEndpoints.cs).
// Sin datos clínicos: solo la hora. Configuración: Asistencia:AvisosHabilitados (true por defecto).
public class AsistenciaAvisosService : BackgroundService
{
    private readonly TimeProvider _reloj;
    private readonly IConfiguration _config;
    private readonly ILogger<AsistenciaAvisosService> _log;

    public AsistenciaAvisosService(TimeProvider reloj, IConfiguration config, ILogger<AsistenciaAvisosService> log)
    {
        _reloj = reloj; _config = config; _log = log;
    }

    private bool Habilitado => _config.GetValue("Asistencia:AvisosHabilitados", true);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), _reloj, ct); }
        catch (TaskCanceledException) { return; }

        var fallasSeguidas = 0;
        while (!ct.IsCancellationRequested)
        {
            try { EjecutarUnaVez(); fallasSeguidas = 0; }
            catch (Exception ex)
            {
                // Sin la migración 37 (o sin base) falla siempre: se avisa una vez y después cada hora, para no llenar el log.
                if (fallasSeguidas++ % 60 == 0)
                    _log.LogWarning("Asistencia: falló la pasada de avisos ({Tipo}); se reintenta.", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), _reloj, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Una pasada. Devuelve cuántos avisos nuevos dejó.</summary>
    public int EjecutarUnaVez()
    {
        if (!Habilitado) return 0;
        var ahora = _reloj.GetLocalNow().DateTime;
        var dao = new AsistenciaDAO();
        var p = dao.Parametros();
        var avisos = 0;
        foreach (var t in dao.SinIngresoRecientes(ahora, p.ToleranciaMinutos))
        {
            if (AsistenciaReglas.EsPresencial(t.Modalidad)) continue;
            if (!AsistenciaReglas.CorrespondeAviso(t.Inicio, ahora, ingreso: false, p)) continue;
            if (AvisosPaciente.CrearUnaVez(t.IdPaciente, $"espera-turno:{t.IdTurno}", "Te estamos esperando",
                    $"Tu sesión de las {t.Inicio:HH:mm} ya empezó y tu profesional te espera en la sala de video. " +
                    $"Si no podés conectarte, avisanos. Pasados {p.ToleranciaMinutos} minutos el turno puede quedar como ausente.", null))
                avisos++;
        }
        return avisos;
    }
}
