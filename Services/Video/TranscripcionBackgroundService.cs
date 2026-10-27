using System.Globalization;
using Microsoft.Extensions.Options;

public enum ResultadoCicloTranscripcion { Deshabilitado, SinPendientes, Procesada }

public class TranscripcionBackgroundService : BackgroundService
{
    private readonly ITranscripcionSesionRepositorio _transcripciones;
    private readonly IConsentimientoVideoRepositorio _consentimientos;
    private readonly IEmocionesCifrado _cifrado;
    private readonly ITranscriptorLocal _transcriptor;
    private readonly IOptionsMonitor<VideoOptions> _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<TranscripcionBackgroundService> _log;
    private bool _avisoDeshabilitada;

    public TranscripcionBackgroundService(ITranscripcionSesionRepositorio transcripciones, IConsentimientoVideoRepositorio consentimientos,
        IEmocionesCifrado cifrado, ITranscriptorLocal transcriptor, IOptionsMonitor<VideoOptions> opciones, TimeProvider reloj,
        ILogger<TranscripcionBackgroundService> log)
    {
        _transcripciones = transcripciones; _consentimientos = consentimientos; _cifrado = cifrado; _transcriptor = transcriptor;
        _opciones = opciones; _reloj = reloj; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_opciones.CurrentValue.Transcripcion.Habilitada)
        {
            var reencoladas = _transcripciones.ReencolarProcesando();
            if (reencoladas > 0)
                _log.LogInformation("Video: transcripción local: {Cantidad} grabación(es) 'procesando' volvieron a 'pendiente' al reiniciar.", reencoladas);
        }

        try { await Task.Delay(TimeSpan.FromSeconds(10), _reloj, ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { EjecutarUnaVez(); }
            catch (Exception ex)
            {
                _log.LogError("Video: falló un ciclo de transcripción local ({Tipo}); se reintenta en el próximo ciclo.", ex.GetType().Name);
            }

            var segundos = Math.Clamp(_opciones.CurrentValue.Transcripcion.IntervaloSegundos, 5, 3600);
            try { await Task.Delay(TimeSpan.FromSeconds(segundos), _reloj, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    public ResultadoCicloTranscripcion EjecutarUnaVez()
    {
        var o = _opciones.CurrentValue.Transcripcion;
        if (!o.Habilitada)
        {
            if (!_avisoDeshabilitada)
            {
                _log.LogInformation("Video: transcripción local DESHABILITADA (Video:Transcripcion:Habilitada = false): no se procesa nada.");
                _avisoDeshabilitada = true;
            }
            return ResultadoCicloTranscripcion.Deshabilitado;
        }

        var procesada = ProcesarUna(o);
        Limpiar(o);
        return procesada ? ResultadoCicloTranscripcion.Procesada : ResultadoCicloTranscripcion.SinPendientes;
    }

    private bool ProcesarUna(VideoTranscripcionOptions o)
    {
        var fila = _transcripciones.TomarSiguientePendiente();
        if (fila is null) return false;

        if (!_consentimientos.EstaVigente(fila.IdConsentimientoVideo))
        {
            _transcripciones.CambiarEstado(fila.IdTranscripcion, TranscripcionEstado.Anulada);
            BorrarAudio(o, fila.IdTranscripcion);
            _log.LogInformation("Video: transcripción {Id} anulada antes de procesar (consentimiento revocado).", fila.IdTranscripcion);
            return true;
        }

        var carpeta = Path.Combine(o.CarpetaAudio, fila.IdTranscripcion.ToString(CultureInfo.InvariantCulture));
        var rutaProfesional = Path.Combine(carpeta, PistaGrabacion.Profesional + ".webm");
        var rutaPaciente = Path.Combine(carpeta, PistaGrabacion.Paciente + ".webm");

        var inicio = _reloj.GetUtcNow();
        ResultadoTranscripcion resultado;
        try
        {
            resultado = _transcriptor.Transcribir(new SolicitudTranscripcion(
                File.Exists(rutaProfesional) ? rutaProfesional : null,
                File.Exists(rutaPaciente) ? rutaPaciente : null,
                o.Modelo, Math.Max(1, o.HilosCpu), o.CarpetaModelos,
                TimeSpan.FromMinutes(Math.Max(1, o.TimeoutMinutos))), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Reintentar(fila, o, MotivoCorto(ex));
            return true;
        }
        var duracionProcesoSeg = (int)(_reloj.GetUtcNow() - inicio).TotalSeconds;

        var texto = UnionDeTranscripcion.ArmarTexto(fila.InicioPistaProfesionalMs, fila.InicioPistaPacienteMs, resultado.Segmentos);
        string cifrado;
        try { cifrado = _cifrado.Cifrar(texto, EmocionesCifradoService.DatosAsociadosDeTranscripcion(fila.IdSesion!.Value)); }
        catch (Exception ex)
        {
            Reintentar(fila, o, MotivoCorto(ex));
            return true;
        }

        var guardada = _transcripciones.GuardarResultado(fila.IdTranscripcion, fila.IdSesion!.Value, fila.IdConsentimientoVideo,
            cifrado, o.Modelo, resultado.DuracionAudioSeg, duracionProcesoSeg);
        BorrarAudio(o, fila.IdTranscripcion);

        if (guardada) _log.LogInformation("Video: transcripción {Id} lista ({Duracion}s de audio, {Proceso}s de proceso).",
            fila.IdTranscripcion, resultado.DuracionAudioSeg, duracionProcesoSeg);
        else _log.LogInformation("Video: transcripción {Id} anulada por revocación justo al guardar.", fila.IdTranscripcion);
        return true;
    }

    private void Reintentar(TranscripcionSesion fila, VideoTranscripcionOptions o, string motivo)
    {
        var maxIntentos = Math.Max(1, o.MaxIntentos);
        var estado = fila.Intentos < maxIntentos ? TranscripcionEstado.Pendiente : TranscripcionEstado.Error;
        _transcripciones.CambiarEstado(fila.IdTranscripcion, estado, error: motivo);
        _log.LogWarning("Video: transcripción {Id} falló ({Motivo}), intento {Intento}/{Max}, queda en '{Estado}'.",
            fila.IdTranscripcion, motivo, fila.Intentos, maxIntentos, estado);
    }

    private static string MotivoCorto(Exception ex) => ex switch
    {
        TimeoutException => "timeout",
        TranscripcionFallidaException f => f.Message,
        _ => "error:" + ex.GetType().Name
    };

    private void Limpiar(VideoTranscripcionOptions o)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;

        var corteHuerfanas = ahora.AddHours(-Math.Max(1, o.HorasGrabacionHuerfana));
        foreach (var id in _transcripciones.AnularGrabandoHuerfanas(corteHuerfanas))
            BorrarAudio(o, id);

        var corteError = ahora.AddDays(-Math.Max(0, o.DiasConservarAudioConError));
        foreach (var id in _transcripciones.IdsConCarpetaParaBorrar(corteError))
            BorrarAudio(o, id);
    }

    private void BorrarAudio(VideoTranscripcionOptions o, int idTranscripcion)
    {
        try
        {
            var ruta = Path.Combine(o.CarpetaAudio, idTranscripcion.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(ruta)) Directory.Delete(ruta, recursive: true);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Video: no se pudo borrar la carpeta de audio de la transcripción {Id} ({Tipo}).", idTranscripcion, ex.GetType().Name);
        }
    }
}
