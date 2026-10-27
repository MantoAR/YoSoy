public interface ITranscripcionSesionRepositorio
{
    TranscripcionSesion Crear(string origen, int idOrigen, int idConsentimientoVideo,
        long? inicioPistaProfesionalMs, long? inicioPistaPacienteMs);

    // Como Crear, pero a prueba de la carrera entre las dos pistas: si otro pedido ya creó la fila "grabando" de esta
    // sesión (índice único UX_TranscripcionSesion_Grabando), devuelve esa en vez de fallar.
    TranscripcionSesion CrearOReusar(string origen, int idOrigen, int idConsentimientoVideo,
        long? inicioPistaProfesionalMs, long? inicioPistaPacienteMs);

    TranscripcionSesion? ObtenerActiva(string origen, int idOrigen);

    // Fija el inicio (ms desde el arranque de la sesión) de una pista, solo si todavía no lo tenía. Idempotente.
    bool ActualizarInicioPista(int idTranscripcion, string pista, long inicioMs);

    TranscripcionSesion? ObtenerPorSesion(int idSesion);

    bool VincularSesion(int idTranscripcion, int idSesion);

    bool CambiarEstado(int idTranscripcion, string estado, string? error = null,
        int? duracionAudioSeg = null, int? duracionProcesoSeg = null, string? modelo = null);

    TranscripcionSesion? TomarSiguientePendiente();

    int ReencolarProcesando();

    bool GuardarResultado(int idTranscripcion, int idSesion, int idConsentimientoVideo, string transcripcionCifrada,
        string modelo, int duracionAudioSeg, int duracionProcesoSeg);

    IReadOnlyList<int> AnularGrabandoHuerfanas(DateTime corteUtc);

    IReadOnlyList<int> IdsConCarpetaParaBorrar(DateTime corteErrorUtc);

    bool VolverAPendienteDesdeError(int idTranscripcion);
}
