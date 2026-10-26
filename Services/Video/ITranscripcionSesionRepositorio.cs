public interface ITranscripcionSesionRepositorio
{
    TranscripcionSesion Crear(string origen, int idOrigen, int idConsentimientoVideo,
        long? inicioPistaProfesionalMs, long? inicioPistaPacienteMs);

    TranscripcionSesion? ObtenerActiva(string origen, int idOrigen);

    TranscripcionSesion? ObtenerPorSesion(int idSesion);

    bool VincularSesion(int idTranscripcion, int idSesion);

    bool CambiarEstado(int idTranscripcion, string estado, string? error = null,
        int? duracionAudioSeg = null, int? duracionProcesoSeg = null, string? modelo = null);

    TranscripcionSesion? TomarSiguientePendiente();

    int ReencolarProcesando();
}
