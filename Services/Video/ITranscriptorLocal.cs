public record SolicitudTranscripcion(string? RutaProfesional, string? RutaPaciente, string Modelo, int HilosCpu,
    string CarpetaModelos, TimeSpan Timeout);

public record SegmentoTranscripcion(string Pista, double Inicio, double Fin, string Texto);

public record ResultadoTranscripcion(int DuracionAudioSeg, IReadOnlyList<SegmentoTranscripcion> Segmentos);

public class TranscripcionFallidaException : Exception
{
    public TranscripcionFallidaException(string motivo) : base(motivo)
    {
    }
}

public interface ITranscriptorLocal
{
    Task<ResultadoTranscripcion> Transcribir(SolicitudTranscripcion solicitud, CancellationToken ct);
}
