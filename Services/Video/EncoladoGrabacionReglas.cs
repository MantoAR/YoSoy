// Después de guardar una sesión clínica (POST /sesiones-clinicas, con o sin cierre), si venía de una videollamada que
// se estaba grabando, esa grabación se vincula a la sesión recién guardada y pasa de "grabando" a "pendiente" para
// que el worker de transcripción la tome (fuera de alcance de esta etapa). Si no vino de una grabación, si la
// grabación ya no está "grabando" (se anuló porque el paciente revocó el consentimiento, o nunca hubo una, o ya se
// vinculó antes), la sesión se guarda igual, sin transcripción.
public static class EncoladoGrabacionReglas
{
    public const string SinGrabacion = "sin-grabacion";

    public static string Encolar(ITranscripcionSesionRepositorio transcripciones, GrabacionRefRequest? grabacion, int idSesion)
    {
        if (grabacion is not { Id: > 0 } g) return SinGrabacion;
        var origen = TextoNormalizado.Normalizar(g.Origen);
        if (origen != OrigenSala.Turno && origen != OrigenSala.Admision) return SinGrabacion;

        var activa = transcripciones.ObtenerActiva(origen, g.Id.Value);
        if (activa is null) return SinGrabacion;

        return transcripciones.VincularSesion(activa.IdTranscripcion, idSesion) ? TranscripcionEstado.Pendiente : SinGrabacion;
    }
}
