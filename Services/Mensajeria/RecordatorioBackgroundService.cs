using Microsoft.Extensions.Options;

/// <summary>
/// Cada Mensajeria:IntervaloChequeoMinutos busca turnos Confirmados cuyo momento
/// (Turno.Fecha + Turno.Hora) cae dentro de las próximas Mensajeria:RecordatorioHorasAntes
/// horas y les manda el recordatorio por chat, mail y campanita. Es idempotente:
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

    public RecordatorioBackgroundService(IRecordatorioDAO recordatorios, NotificadorTurnos notificador,
        IOptionsMonitor<MensajeriaOptions> opciones, TimeProvider reloj, ILogger<RecordatorioBackgroundService> log)
    {
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
            NotificadorTurnos.CanalesRecordatorio.Length, op.MaxIntentosEnvio);

        foreach (var idTurno in ids)
            _notificador.EnviarRecordatorio24h(idTurno);   // nunca lanza
        return ids.Count;
    }
}
