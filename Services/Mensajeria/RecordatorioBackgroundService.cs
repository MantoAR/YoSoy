using Microsoft.Extensions.Options;

/// <summary>
/// Cada Mensajeria:IntervaloChequeoMinutos busca turnos Confirmados cuyo momento
/// (Turno.Fecha + Turno.Hora) cae dentro de las próximas Mensajeria:RecordatorioHorasAntes
/// horas y les manda el recordatorio por mail y campanita (nunca por WhatsApp). Es idempotente:
/// correr varias veces no duplica avisos (índice único en Recordatorio).
/// Un error en un turno se registra y no frena al resto ni al servicio.
///
/// Zona horaria: se usa la hora local del servidor, que se asume en Argentina
/// (UTC-3), igual que los turnos, que se guardan sin zona. Ver TurnoReglasService.
/// </summary>
public class RecordatorioBackgroundService : BackgroundService
{
    private readonly IRecordatorioDAO _recordatorios;
    private readonly NotificadorTurnos _notificador;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<RecordatorioBackgroundService> _log;
    private readonly ISeguimientoClinico? _seguimiento;

    public RecordatorioBackgroundService(IRecordatorioDAO recordatorios, NotificadorTurnos notificador,
        IOptionsMonitor<MensajeriaOptions> opciones, TimeProvider reloj, ILogger<RecordatorioBackgroundService> log, ISeguimientoClinico? seguimiento = null)
    {
        _seguimiento   = seguimiento;
        _recordatorios = recordatorios;
        _notificador   = notificador;
        _opciones      = opciones;
        _reloj         = reloj;
        _log           = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var procesados = EjecutarUnaVez();
                if (procesados > 0) _log.LogInformation("Recordatorios: {Cantidad} turno(s) procesado(s).", procesados);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Falló la búsqueda de recordatorios; se reintenta en el próximo ciclo.");
            }

            var intervalo = TimeSpan.FromMinutes(Math.Max(1, _opciones.CurrentValue.IntervaloChequeoMinutos));
            try { await Task.Delay(intervalo, _reloj, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Una pasada completa. Público para poder dispararlo a mano en pruebas.</summary>
    public int EjecutarUnaVez()
    {
        var op    = _opciones.CurrentValue;
        var ahora = _reloj.GetLocalNow().DateTime;
        var ids   = _recordatorios.TurnosConfirmadosSinRecordar(
            ahora, ahora.AddHours(op.RecordatorioHorasAntes),
            NotificadorTurnos.CanalesRecordatorio, op.MaxIntentosEnvio);

        foreach (var idTurno in ids)
            _notificador.EnviarRecordatorio24h(idTurno);   // nunca lanza

        // Las propuestas automáticas de asignación vencidas pasan a la recepción en la misma pasada (con un turno cercano duran pocos minutos).
        try { AsignacionAutomaticaService.VencerVencidas(ahora); }
        catch (Exception ex) { _log.LogWarning(ex, "No se pudieron vencer las propuestas de asignación; se reintenta en el próximo ciclo."); }

        // La misma pasada también vence las solicitudes de turno sin respuesta, avisa las ausencias a los 10 minutos y los controles de seguimiento que se acercan (todo idempotente).
        try { _seguimiento?.Pasada(ahora); }
        catch (Exception ex) { _log.LogError(ex, "Falló la pasada de seguimiento clínico; se reintenta en el próximo ciclo."); }
        return ids.Count;
    }
}
