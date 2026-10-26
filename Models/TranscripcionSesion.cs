public class TranscripcionSesion
{
    public int IdTranscripcion { get; set; }
    public int? IdSesion { get; set; }
    public string Origen { get; set; } = "";
    public int IdOrigen { get; set; }
    public int IdConsentimientoVideo { get; set; }
    public string Estado { get; set; } = "";
    public int Intentos { get; set; }
    public string? Error { get; set; }
    public long? InicioPistaProfesionalMs { get; set; }
    public long? InicioPistaPacienteMs { get; set; }
    public string? Modelo { get; set; }
    public int? DuracionAudioSeg { get; set; }
    public int? DuracionProcesoSeg { get; set; }
    public DateTime CreadoUtc { get; set; }
    public DateTime ActualizadoUtc { get; set; }
}

public static class TranscripcionEstado
{
    public const string Grabando = "grabando";
    public const string Pendiente = "pendiente";
    public const string Procesando = "procesando";
    public const string Lista = "lista";
    public const string Error = "error";
    public const string Anulada = "anulada";
}

public static class PistaGrabacion
{
    public const string Profesional = "profesional";
    public const string Paciente = "paciente";
}
