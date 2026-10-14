// Lo que las reglas del consentimiento de la lectura de emociones necesitan de la base. Es una interfaz para poder
// probar las reglas (estados, ventana, menores, idempotencia) con datos en memoria; la implementación real, con SQL,
// es ConsentimientoVideoDAO. Todas las fechas son UTC.
public interface IConsentimientoVideoRepositorio
{
    /// <summary>La decisión más nueva de esa sesión para ese paciente (la vigente), o null si nunca decidió.</summary>
    ConsentimientoVideoFila? Ultimo(string origen, int idOrigen, int idPaciente);

    ConsentimientoVideoFila Insertar(int idUsuario, int idPaciente, string origen, int idOrigen,
        string versionTexto, string hashTexto, string decision, DateTime decididoUtc);

    /// <summary>Marca la aceptación como revocada. false si ya estaba revocada (o no es una aceptación).</summary>
    bool Revocar(int idConsentimientoVideo, DateTime revocadoUtc);

    /// <summary>
    /// Pone en NULL las emociones guardadas de esa sesión de video (cualquiera sea la decisión con la que se guardaron)
    /// y deja constancia de cuándo y por qué. Nunca toca las notas clínicas. Devuelve cuántas sesiones tocó.
    /// </summary>
    int AnularEmocionesDeLaSesion(string origen, int idOrigen, int idPaciente, string motivo, DateTime ahoraUtc);

    DatosPacienteParaConsentimiento? ObtenerDatosPaciente(int idPaciente);
}

public record ConsentimientoVideoFila(int IdConsentimientoVideo, int IdUsuario, int IdPaciente, string Origen, int IdOrigen,
    string VersionTexto, string HashTexto, string Decision, DateTime DecididoUtc, DateTime? RevocadoUtc)
{
    public const string Acepta = "acepta";
    public const string Rechaza = "rechaza";

    /// <summary>Una aceptación que nadie revocó: lo único que habilita la lectura de emociones.</summary>
    public bool EsAceptacionVigente => Decision == Acepta && RevocadoUtc is null;
}

/// <summary>Lo único que hace falta saber del paciente para decidir si se le ofrece el análisis: su edad y si tiene tutor.</summary>
public record DatosPacienteParaConsentimiento(DateTime? FechaNacimiento, bool TieneTutor);
