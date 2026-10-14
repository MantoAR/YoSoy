using Microsoft.Extensions.Options;

// Qué le hace falta a la purga de la base: contar y anular las emociones guardadas que superaron el plazo de retención.
public interface IEmocionesPurgaRepositorio
{
    /// <summary>Sesiones con emociones guardadas cuya fecha es anterior al corte (hora del servidor, como SesionClinica.FechaHora).</summary>
    int ContarVencidas(DateTime corteLocal);

    /// <summary>Pone en NULL las emociones de hasta <paramref name="lote"/> sesiones vencidas. Devuelve cuántas anuló.</summary>
    int AnularVencidas(DateTime corteLocal, int lote, DateTime ahoraUtc);
}

public record ResultadoPurga(bool Habilitada, int Vencidas, int Anuladas, int RetencionDias);

// Purga por plazo de retención de las emociones guardadas (SesionClinica.HistorialEmociones). Mismo patrón que
// RecordatorioBackgroundService: un ciclo cada Video:Emociones:PurgaCadaHoras, con EjecutarUnaVez() público para poder
// probarlo y disparar una pasada a mano.
//
//  · ARRANCA DESHABILITADA. Con Video:Emociones:PurgaHabilitada en false, cada ciclo solo deja en el log cuántas filas
//    purgaría; no modifica nada. Hay que prender el flag a propósito.
//  · Anular es poner HistorialEmociones en NULL (y dejar fecha y motivo "retencion"). Las notas clínicas, la
//    transcripción y el tipo de cierre no se tocan jamás.
//  · RetencionDias en 0 (o menos) = no se purga nunca.
//  · Un error se registra y no frena al servicio; se reintenta en el próximo ciclo. No se loguea contenido ni ids.
public class PurgaEmocionesBackgroundService : BackgroundService
{
    private readonly IEmocionesPurgaRepositorio _repo;
    private readonly IAuditoriaEmociones _auditoria;
    private readonly IOptionsMonitor<VideoOptions> _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<PurgaEmocionesBackgroundService> _log;

    public PurgaEmocionesBackgroundService(IEmocionesPurgaRepositorio repo, IAuditoriaEmociones auditoria,
        IOptionsMonitor<VideoOptions> opciones, TimeProvider reloj, ILogger<PurgaEmocionesBackgroundService> log)
    {
        _repo = repo; _auditoria = auditoria; _opciones = opciones; _reloj = reloj; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Un rato después del arranque, para no competir con el resto de lo que la API hace al levantar.
        try { await Task.Delay(TimeSpan.FromMinutes(2), _reloj, ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { EjecutarUnaVez(); }
            catch (Exception ex)
            {
                _log.LogError("Video: falló la purga de emociones ({Tipo}); se reintenta en el próximo ciclo.", ex.GetType().Name);
            }

            var horas = Math.Clamp(_opciones.CurrentValue.Emociones.PurgaCadaHoras, 1, 24 * 30);
            try { await Task.Delay(TimeSpan.FromHours(horas), _reloj, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Una pasada completa. Público para poder dispararlo a mano en pruebas.</summary>
    public ResultadoPurga EjecutarUnaVez()
    {
        var e = _opciones.CurrentValue.Emociones;
        var dias = e.RetencionDias;
        if (dias <= 0)
        {
            _log.LogInformation("Video: purga de emociones sin plazo de retención (Video:Emociones:RetencionDias = {Dias}): no se purga nada.", dias);
            return new ResultadoPurga(e.PurgaHabilitada, 0, 0, dias);
        }

        var corte = _reloj.GetLocalNow().DateTime.AddDays(-dias);
        var vencidas = _repo.ContarVencidas(corte);

        if (!e.PurgaHabilitada)
        {
            // Sin el flag explícito no se toca nada: solo se informa.
            _log.LogInformation(
                "Video: purga de emociones DESHABILITADA (simulación): {Cantidad} sesión(es) con emociones superan los {Dias} días y se anularían. "
                + "Para activarla: Video__Emociones__PurgaHabilitada=true.", vencidas, dias);
            return new ResultadoPurga(false, vencidas, 0, dias);
        }

        var lote = Math.Clamp(e.PurgaLote, 1, 5000);
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var anuladas = 0;
        while (anuladas < vencidas)
        {
            var n = _repo.AnularVencidas(corte, lote, ahora);
            if (n <= 0) break;
            anuladas += n;
        }

        if (anuladas > 0)
        {
            _auditoria.EmocionesPurgadas(null, anuladas, "retencion");
            _log.LogInformation("Video: purga de emociones: {Cantidad} sesión(es) anulada(s) por superar los {Dias} días.", anuladas, dias);
        }
        else _log.LogInformation("Video: purga de emociones: nada para purgar (plazo de {Dias} días).", dias);
        return new ResultadoPurga(true, vencidas, anuladas, dias);
    }
}
