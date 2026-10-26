using Microsoft.Extensions.Options;

// La ACEPTACIÓN DE LA VIDEOLLAMADA, sesión por sesión (migración 28, tabla AceptacionVideollamada). Es obligatoria para que el PACIENTE entre a la sala: sin ella, POST /video/acceso le
// contesta 409 «aceptacion-video-pendiente». No es el consentimiento de tratamiento y telemedicina (se firma una vez, ver ConsentimientoTratamiento) ni el de la lectura de emociones
// (ConsentimientoVideo, opcional); es lo que el paciente confirma ANTES DE CADA sesión: «sé que mi profesional me va a ver y a escuchar en vivo».
//
//  · Cada aceptación guarda quién, cuándo (UTC), la versión del texto y su SHA-256. Una por sesión y paciente vigente a la vez (índice único filtrado).
//  · Se revoca cuando quiera: la sala se cierra de forma ordenada (la pantalla del paciente corta y la del profesional lo ve en su próxima consulta), la lectura de emociones se corta y se
//    descarta lo acumulado, y el profesional recibe un aviso. Volver a aceptar es una fila nueva.
//  · Sin la migración 28 (Exigida = false) no se exige nada y la API anda como antes.
public record AceptacionVideoFila(int IdAceptacion, int? IdUsuario, int IdPaciente, string Origen, int IdOrigen, string VersionTexto, DateTime DecididoUtc, DateTime? RevocadoUtc)
{
    public bool Vigente => RevocadoUtc is null;
}

public interface IAceptacionVideollamadaRepositorio
{
    /// <summary>¿La base tiene la migración 28? Sin ella no se exige la aceptación.</summary>
    bool Disponible();

    /// <summary>La fila más nueva de esa sesión para ese paciente (vigente o revocada), o null si nunca aceptó.</summary>
    AceptacionVideoFila? Ultima(string origen, int idOrigen, int idPaciente);

    AceptacionVideoFila Insertar(int idUsuario, int idPaciente, string origen, int idOrigen, string versionTexto, string hashTexto, DateTime decididoUtc);

    /// <summary>Marca la aceptación como revocada. false si ya estaba revocada.</summary>
    bool Revocar(int idAceptacion, DateTime revocadoUtc, int idUsuario);
}

public interface IAuditoriaAceptacionVideo
{
    /// <param name="decision">"acepta" o "revoca".</param>
    void Registrar(int idUsuario, string origen, int idOrigen, string decision, string version, int idAceptacion);
}

public class AuditoriaAceptacionVideo : IAuditoriaAceptacionVideo
{
    private readonly ILogger<AuditoriaAceptacionVideo> _log;
    public AuditoriaAceptacionVideo(ILogger<AuditoriaAceptacionVideo> log) => _log = log;

    public void Registrar(int idUsuario, string origen, int idOrigen, string decision, string version, int idAceptacion)
    {
        try
        {
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = idUsuario,
                Accion = decision == "acepta" ? "Videollamada aceptada" : "Videollamada revocada",
                Entidad = origen == OrigenSala.Turno ? "Turno" : "InformeAdmision",
                IdEntidad = idOrigen,
                Detalle = $"version: {version}; aceptacion: {idAceptacion}"
            });
        }
        catch (Exception e)
        {
            // La auditoría no debe poder tumbar un pedido real (mismo criterio que AuditoriaEmociones).
            _log.LogWarning("Video: no se pudo escribir la auditoría de la videollamada ({Tipo}).", e.GetType().Name);
        }
    }
}

/// <summary>El texto que lee el paciente antes de entrar. PROVISORIO: lo tienen que validar el equipo legal y los psicólogos (ver la marca en Consentimiento:MarcaProvisoria).</summary>
public static class TextoAceptacionVideollamada
{
    public const string Version = "video-v0-provisorio";

    public const string Contenido = @"Antes de empezar la sesión

Esta sesión es una videollamada: tu profesional te va a ver y a escuchar en vivo mientras dure.

Usamos tu cámara y tu micrófono solo para esta sesión. La videollamada no se graba.

Podés dejar de usar el video cuando quieras: la sesión se corta y tu profesional recibe el aviso.

Para entrar tenés que aceptar usar la videollamada. Si no querés, no pasa nada: avisale a tu profesional y definan otra forma de atención.";

    public static TextoConsentimiento Actual() => TextoConsentimientoVideo.Interpretar(Version, Contenido);
}

public record EstadoAceptacion(bool Exigida, string Estado, string Version, string Titulo, IReadOnlyList<string> Texto, string? MarcaProvisoria, DateTime? DecididoUtc, DateTime? RevocadoUtc);

public record ResultadoAceptacion(TipoResultado Tipo, string Codigo, string Mensaje, EstadoAceptacion? Estado = null, DateTimeOffset? CerroUtc = null)
{
    public bool Ok => Tipo == TipoResultado.Ok;
}

public class AceptacionVideollamadaService
{
    public const string Pendiente = "pendiente";
    public const string Acepta = "acepta";
    public const string Revocada = "revocada";

    public const string TipoAvisoRevocacion = "consentimiento-video-revocado";
    public const string TipoAvisoBloqueo = "consentimiento-bloqueo";

    private readonly IAceptacionVideollamadaRepositorio _repo;
    private readonly IOrigenSalaRepositorio _origen;
    private readonly ConsentimientoVideoService _emociones;
    private readonly INotificacionInternaDAO _avisos;
    private readonly IAuditoriaAceptacionVideo _auditoria;
    private readonly TimeProvider _reloj;
    private readonly IOptionsMonitor<ConsentimientoOptions> _consentimiento;

    public AceptacionVideollamadaService(IAceptacionVideollamadaRepositorio repo, IOrigenSalaRepositorio origen, ConsentimientoVideoService emociones,
        INotificacionInternaDAO avisos, IAuditoriaAceptacionVideo auditoria, TimeProvider reloj, IOptionsMonitor<ConsentimientoOptions> consentimiento)
    {
        _repo = repo; _origen = origen; _emociones = emociones; _avisos = avisos; _auditoria = auditoria; _reloj = reloj; _consentimiento = consentimiento;
    }

    public bool Exigida => _repo.Disponible();

    /// <summary>¿Aceptó el paciente la videollamada de esa sesión? (Sin la migración 28 no se exige: true.)</summary>
    public bool Aceptada(string origen, int idOrigen, int idPaciente) =>
        !Exigida || _repo.Ultima(origen, idOrigen, idPaciente) is { Vigente: true };

    public static string EstadoDe(AceptacionVideoFila? f) => f is null ? Pendiente : f.Vigente ? Acepta : Revocada;

    // ───────────────────────── Paciente ─────────────────────────

    public ResultadoAceptacion Obtener(Identidad yo, string origen, int id)
    {
        if (Validar(yo, origen, id, exigirVentana: true, out var ses) is { } rechazo) return rechazo;
        return Ok(Vista(_repo.Ultima(ses!.Origen, ses.Id, ses.IdPaciente)));
    }

    public ResultadoAceptacion Aceptar(Identidad yo, string origen, int id, string? version)
    {
        if (Validar(yo, origen, id, exigirVentana: true, out var ses) is { } rechazo) return rechazo;
        if (!Exigida) return Ok(Vista(null));

        var texto = TextoAceptacionVideollamada.Actual();
        if (!string.Equals(version, texto.Version, StringComparison.Ordinal))
            return Error(TipoResultado.Conflicto, "version-desactualizada", "El texto cambió. Volvé a leerlo antes de aceptar.");

        var ultima = _repo.Ultima(ses!.Origen, ses.Id, ses.IdPaciente);
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        // Ya aceptada, sobre este mismo texto: no se agrega otra fila.
        if (ultima is { Vigente: true } && ultima.VersionTexto == texto.Version) return Ok(Vista(ultima));
        // Aceptada sobre un texto anterior: se la cierra y se acepta el nuevo (el índice único deja una sola vigente).
        if (ultima is { Vigente: true }) _repo.Revocar(ultima.IdAceptacion, ahora, yo.IdUsuario);

        var fila = _repo.Insertar(yo.IdUsuario, ses.IdPaciente, ses.Origen, ses.Id, texto.Version, texto.Hash, ahora);
        _auditoria.Registrar(yo.IdUsuario, ses.Origen, ses.Id, "acepta", texto.Version, fila.IdAceptacion);
        return Ok(Vista(fila));
    }

    /// <summary>
    /// El paciente deja de usar el video: corta la lectura de emociones (y descarta lo acumulado), y avisa al profesional. Se puede hacer aunque la sala ya haya cerrado y repetirlo no
    /// hace daño (el aviso sale una sola vez).
    /// </summary>
    public ResultadoAceptacion Revocar(Identidad yo, string origen, int id)
    {
        if (Validar(yo, origen, id, exigirVentana: false, out var ses) is { } rechazo) return rechazo;
        if (!Exigida) return Ok(Vista(null));

        var ultima = _repo.Ultima(ses!.Origen, ses.Id, ses.IdPaciente);
        if (ultima is { Vigente: true })
        {
            var ahora = _reloj.GetUtcNow().UtcDateTime;
            if (_repo.Revocar(ultima.IdAceptacion, ahora, yo.IdUsuario))
            {
                _auditoria.Registrar(yo.IdUsuario, ses.Origen, ses.Id, "revoca", ultima.VersionTexto, ultima.IdAceptacion);
                // Sin video no hay lectura de emociones: se corta y se anula lo ya guardado de la sesión (las notas clínicas no).
                _emociones.Revocar(yo, ses.Origen, ses.Id);
                AvisarAlProfesional(ses);
            }
            ultima = _repo.Ultima(ses.Origen, ses.Id, ses.IdPaciente);
        }
        return Ok(Vista(ultima));
    }

    // ───────────────────────── Avisos ─────────────────────────

    private void AvisarAlProfesional(SesionDeVideo ses)
    {
        var nombre = $"El paciente #{ses.IdPaciente}";
        try { nombre = new PacienteDAO().ObtenerPorId(ses.IdPaciente)?.Nombre ?? nombre; } catch (Exception) { /* sin el nombre el aviso sale igual */ }
        try
        {
            _avisos.Insertar(new NotificacionInterna
            {
                Destinatario = ses.IdProfesional is null ? "Administrador" : "Profesional",
                IdProfesional = ses.IdProfesional,
                IdTurno = ses.Origen == OrigenSala.Turno ? ses.Id : null,
                Tipo = TipoAvisoRevocacion,
                Titulo = "El paciente dejó de aceptar la videollamada",
                Mensaje = $"{nombre} revocó la aceptación de la videollamada. La sesión de video se cerró y se dejó de analizar y de guardar sus emociones."
            });
        }
        catch (Exception) { /* el aviso no puede tapar la revocación, que ya quedó registrada */ }
    }

    /// <summary>
    /// Deja constancia, para el profesional, de que una sesión urgente no se pudo autorizar porque el paciente no tiene lo que hace falta (consentimiento de tratamiento o aceptación de la
    /// videollamada). No salta la regla: se registra el bloqueo. Idempotente por turno y motivo mientras el aviso siga sin leer.
    /// </summary>
    public void AvisarBloqueo(int idProfesional, int? idTurno, string nombrePaciente, string motivo)
    {
        try
        {
            _avisos.Insertar(new NotificacionInterna
            {
                Destinatario = "Profesional",
                IdProfesional = idProfesional,
                IdTurno = idTurno,
                Tipo = TipoAvisoBloqueo,
                Titulo = "La cita urgente no se pudo habilitar",
                Mensaje = $"{nombrePaciente}: {motivo} Se le avisó al paciente. Cuando lo resuelva, podés volver a generar la sala."
            });
        }
        catch (Exception) { /* idem */ }
    }

    // ───────────────────────── Reglas compartidas ─────────────────────────

    private ResultadoAceptacion? Validar(Identidad yo, string origen, int id, bool exigirVentana, out SesionDeVideo? ses)
    {
        var rechazo = _emociones.ValidarPacienteDeLaSesion(yo, origen, id, exigirVentana, out ses);
        return rechazo is null ? null : new ResultadoAceptacion(rechazo.Tipo, rechazo.Codigo, rechazo.Mensaje, null, rechazo.CerroUtc);
    }

    private EstadoAceptacion Vista(AceptacionVideoFila? f)
    {
        var texto = TextoAceptacionVideollamada.Actual();
        var marca = _consentimiento.CurrentValue.MarcaProvisoria;
        return new EstadoAceptacion(Exigida, Exigida ? EstadoDe(f) : Acepta, texto.Version, texto.Titulo, texto.Parrafos,
            string.IsNullOrWhiteSpace(marca) ? null : marca, f?.DecididoUtc, f?.RevocadoUtc);
    }

    private static ResultadoAceptacion Ok(EstadoAceptacion e) => new(TipoResultado.Ok, "ok", "", e);
    private static ResultadoAceptacion Error(TipoResultado tipo, string codigo, string mensaje) => new(tipo, codigo, mensaje);
}
