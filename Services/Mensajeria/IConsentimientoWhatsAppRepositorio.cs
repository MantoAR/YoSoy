// Lo que las reglas del consentimiento de los avisos por WhatsApp necesitan de la base. Es una interfaz para poder probar las reglas (estados,
// teléfono, menores, texto, carreras) con datos en memoria; la implementación real, con SQL, es ConsentimientoWhatsAppDAO. Todas las
// fechas son UTC. La tabla se define en migrations/add_consentimiento_whatsapp.sql.
public interface IConsentimientoWhatsAppRepositorio
{
    /// <summary>La fila más nueva del paciente (abierta o revocada: es su estado actual), o null si nunca aceptó.</summary>
    ConsentimientoWhatsAppFila? Ultimo(int idPaciente);

    /// <summary>
    /// Registra una aceptación nueva. Si el paciente tenía una abierta, la cierra con <paramref name="motivoSiReemplaza"/> en el mismo paso
    /// (una sola transacción: nunca queda cerrada la vieja sin la nueva). null si otra aceptación se abrió en el medio (dos pedidos
    /// a la vez): el que llama vuelve a leer el estado.
    /// </summary>
    ConsentimientoWhatsAppFila? Aceptar(int idPaciente, int idUsuario, string telefono, string origen, string versionTexto,
        string hashTexto, DateTime aceptadoUtc, string motivoSiReemplaza);

    /// <summary>Revoca la aceptación abierta del paciente. false si no había ninguna.</summary>
    bool Revocar(int idPaciente, DateTime revocadoUtc, string motivo);

    /// <summary>La BAJA llegó por WhatsApp: revoca TODAS las aceptaciones abiertas de ese número (puede haber más de un paciente). Devuelve cuáles.</summary>
    List<(int IdConsentimientoWhatsApp, int IdPaciente)> RevocarVigentesDelTelefono(string telefono, DateTime revocadoUtc, string motivo);

    DatosPacienteParaWhatsApp? ObtenerDatosPaciente(int idPaciente);
}

public record ConsentimientoWhatsAppFila(int IdConsentimientoWhatsApp, int IdPaciente, int IdUsuario, string Telefono, string Origen,
    string VersionTexto, string HashTexto, DateTime AceptadoUtc, DateTime? RevocadoUtc, string? MotivoRevocacion)
{
    public const string OrigenRegistro = "registro";
    public const string OrigenPerfil = "perfil";
    public const string MotivoPaciente = "paciente";
    public const string MotivoBaja = "baja-whatsapp";
    public const string MotivoTelefonoCambiado = "telefono-cambiado";
    public const string MotivoTextoNuevo = "texto-nuevo";

    /// <summary>Una aceptación que nadie revocó ni reemplazó.</summary>
    public bool EstaAbierta => RevocadoUtc is null;
}

/// <summary>Lo único que hace falta saber del paciente para decidir si puede aceptar: su teléfono, su edad y si tiene un tutor cargado.</summary>
public record DatosPacienteParaWhatsApp(string? Telefono, DateTime? FechaNacimiento, bool TieneTutor);
