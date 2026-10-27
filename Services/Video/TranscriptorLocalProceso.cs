using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

public class TranscriptorLocalProceso : ITranscriptorLocal
{
    private readonly IOptionsMonitor<VideoOptions> _opciones;

    public TranscriptorLocalProceso(IOptionsMonitor<VideoOptions> opciones) => _opciones = opciones;

    public async Task<ResultadoTranscripcion> Transcribir(SolicitudTranscripcion s, CancellationToken ct)
    {
        var o = _opciones.CurrentValue.Transcripcion;
        if (string.IsNullOrWhiteSpace(o.RutaScript))
            throw new InvalidOperationException("Video:Transcripcion:RutaScript no está configurada.");
        var python = string.IsNullOrWhiteSpace(o.RutaPython) ? "python3" : o.RutaPython;

        var info = new ProcessStartInfo { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        if (OperatingSystem.IsLinux())
        {
            info.FileName = "nice";
            info.ArgumentList.Add("-n");
            info.ArgumentList.Add("19");
            info.ArgumentList.Add(python);
        }
        else info.FileName = python;

        info.ArgumentList.Add(o.RutaScript);
        info.ArgumentList.Add("--modelo");
        info.ArgumentList.Add(s.Modelo);
        info.ArgumentList.Add("--hilos");
        info.ArgumentList.Add(s.HilosCpu.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add("--carpeta-modelos");
        info.ArgumentList.Add(s.CarpetaModelos);
        if (s.RutaProfesional is not null)
        {
            info.ArgumentList.Add("--pista-profesional");
            info.ArgumentList.Add(s.RutaProfesional);
        }
        if (s.RutaPaciente is not null)
        {
            info.ArgumentList.Add("--pista-paciente");
            info.ArgumentList.Add(s.RutaPaciente);
        }

        using var proceso = new Process { StartInfo = info };
        var salida = new StringBuilder();
        proceso.OutputDataReceived += (_, e) => { if (e.Data is not null) salida.Append(e.Data); };
        proceso.ErrorDataReceived += (_, e) => { };
        proceso.Start();
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(s.Timeout);
        try
        {
            await proceso.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proceso.Kill(entireProcessTree: true); } catch (Exception) { }
            throw new TimeoutException("La transcripción superó el tiempo máximo configurado.");
        }

        if (proceso.ExitCode != 0)
            throw new TranscripcionFallidaException($"script-salida-{proceso.ExitCode}");

        try
        {
            using var doc = JsonDocument.Parse(salida.ToString());
            var raiz = doc.RootElement;
            var duracion = raiz.GetProperty("duracionAudioSeg").GetInt32();
            var segmentos = raiz.GetProperty("segmentos").EnumerateArray()
                .Select(e => new SegmentoTranscripcion(
                    e.GetProperty("pista").GetString()!,
                    e.GetProperty("inicio").GetDouble(),
                    e.GetProperty("fin").GetDouble(),
                    e.GetProperty("texto").GetString() ?? ""))
                .ToList();
            return new ResultadoTranscripcion(duracion, segmentos);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TranscripcionFallidaException("salida-invalida");
        }
    }
}
