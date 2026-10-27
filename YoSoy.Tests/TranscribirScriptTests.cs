using System.Diagnostics;
using System.Text;
using System.Text.Json;

public sealed class FactConPythonYFasterWhisperAttribute : FactAttribute
{
    public FactConPythonYFasterWhisperAttribute()
    {
        var motivo = ScriptTranscribir.NoDisponible();
        if (motivo is not null) Skip = motivo;
    }
}

public static class ScriptTranscribir
{
    public static readonly string Ruta = Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "transcripcion", "transcribir.py");
    private static readonly Lazy<string?> Motivo = new(CalcularMotivo);
    private static readonly string Python = OperatingSystem.IsWindows() ? "python" : "python3";

    public static string? NoDisponible() => Motivo.Value;

    private static string? CalcularMotivo()
    {
        try
        {
            if (!File.Exists(Ruta)) return "No está transcripcion/transcribir.py.";
            var (codigo, _, _) = Ejecutar(new[] { "-c", "import faster_whisper" });
            return codigo == 0 ? null : "Falta instalar faster-whisper (ver transcripcion/requirements.txt).";
        }
        catch (Exception e)
        {
            return $"python no está disponible en esta máquina ({e.GetType().Name}).";
        }
    }

    public static (int Codigo, string Salida, string Error) Ejecutar(string[] args, int milisegundos = 20000)
    {
        var psi = new ProcessStartInfo(Python)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proceso = Process.Start(psi)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var error = proceso.StandardError.ReadToEndAsync();
        if (!proceso.WaitForExit(milisegundos))
        {
            try { proceso.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("python no terminó a tiempo.");
        }
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, error.Result);
    }
}

public class TranscribirScriptTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "yosoy-transcribir-tests", Guid.NewGuid().ToString("N"));

    public TranscribirScriptTests() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        try { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true); }
        catch { }
    }

    private string CrearWavDeSilencio(double segundos)
    {
        var ruta = Path.Combine(_carpeta, "pista.wav");
        const int frecuencia = 16000;
        const short canales = 1;
        const short bitsPorMuestra = 16;
        var muestras = (int)(segundos * frecuencia);
        var datos = new byte[muestras * canales * (bitsPorMuestra / 8)];

        using var flujo = new FileStream(ruta, FileMode.Create);
        using var escritor = new BinaryWriter(flujo);
        escritor.Write(Encoding.ASCII.GetBytes("RIFF"));
        escritor.Write(36 + datos.Length);
        escritor.Write(Encoding.ASCII.GetBytes("WAVE"));
        escritor.Write(Encoding.ASCII.GetBytes("fmt "));
        escritor.Write(16);
        escritor.Write((short)1);
        escritor.Write(canales);
        escritor.Write(frecuencia);
        escritor.Write(frecuencia * canales * (bitsPorMuestra / 8));
        escritor.Write((short)(canales * (bitsPorMuestra / 8)));
        escritor.Write(bitsPorMuestra);
        escritor.Write(Encoding.ASCII.GetBytes("data"));
        escritor.Write(datos.Length);
        escritor.Write(datos);

        return ruta;
    }

    [FactConPythonYFasterWhisper]
    public void Sin_ninguna_pista_valida_el_script_sale_con_error_y_un_mensaje_claro()
    {
        var carpetaModelos = Path.Combine(_carpeta, "modelos");

        var (codigo, _, error) = ScriptTranscribir.Ejecutar(new[]
        {
            ScriptTranscribir.Ruta, "--modelo", "tiny", "--hilos", "1", "--carpeta-modelos", carpetaModelos
        });

        Assert.Equal(1, codigo);
        Assert.Contains("ninguna de las pistas indicadas existe", error);
    }

    [FactConPythonYFasterWhisper]
    public void El_script_transcribe_un_audio_real_y_devuelve_el_json_del_contrato()
    {
        var pista = CrearWavDeSilencio(segundos: 2);
        var carpetaModelos = Path.Combine(_carpeta, "modelos");

        var (codigo, salida, _) = ScriptTranscribir.Ejecutar(new[]
        {
            ScriptTranscribir.Ruta, "--modelo", "tiny", "--hilos", "1", "--carpeta-modelos", carpetaModelos,
            "--pista-profesional", pista
        }, milisegundos: 180000);

        Assert.Equal(0, codigo);
        using var doc = JsonDocument.Parse(salida.Trim());
        var raiz = doc.RootElement;
        Assert.True(raiz.GetProperty("duracionAudioSeg").GetInt32() >= 0);
        foreach (var segmento in raiz.GetProperty("segmentos").EnumerateArray())
        {
            Assert.Equal("profesional", segmento.GetProperty("pista").GetString());
            Assert.True(segmento.TryGetProperty("inicio", out _));
            Assert.True(segmento.TryGetProperty("fin", out _));
            Assert.True(segmento.TryGetProperty("texto", out _));
        }
    }
}
