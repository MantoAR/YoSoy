using Microsoft.Extensions.Options;

// Consentimiento por sesión para la lectura de emociones de la sala de video (PR 6). Decide en el servidor, con los
// datos de la base: la pantalla solo dice DE QUÉ SESIÓN habla (origen + id) y qué decide el paciente.
//
//  · Decide el paciente de la sesión, y solo él (una persona que no es parte recibe el mismo 404 que en
//    POST /video/acceso: no se puede sondear qué sesiones existen).
//  · Una fila por decisión; la vigente es la más nueva. Aceptar es explícito: sin decisión no hay análisis.
//  · Revocar corta el análisis y anula las emociones ya guardadas de esa sesión (nunca las notas clínicas).
//  · A un menor de edad (o con tutor) no se le ofrece el análisis; la videollamada anda igual. PROVISORIO: lo tiene
//    que validar el equipo legal (docs/VIDEOLLAMADAS.md).
//  · Sin clave de cifrado configurada no hay análisis: no se podría guardar nada de forma segura.
//
// El servidor es la autoridad: la pantalla del profesional consulta Permiso() cada pocos segundos y frena si no se
// lo da (o si no le contesta). Ningún servidor puede impedir que un navegador alterado analice el video que recibe;
// lo que sí controla es lo que se guarda (ver GuardiaEmocionesService).
public enum TipoResultado { Ok, NoEncontrada, SolicitudInvalida, Conflicto }

public record EstadoConsentimiento(
    bool Disponible, string Estado, string? Motivo, string? Version, bool Provisorio, string? Titulo,
    IReadOnlyList<string>? Texto, DateTime? DecididoUtc, DateTime? RevocadoUtc);

public record ResultadoConsentimiento(TipoResultado Tipo, string Codigo, string Mensaje,
    EstadoConsentimiento? Estado = null, DateTimeOffset? CerroUtc = null)
{
    public bool Ok => Tipo == TipoResultado.Ok;
}

/// <summary>Lo que la pantalla del profesional necesita para saber si puede analizar, y cuánto vale la respuesta.</summary>
public record PermisoEmociones(bool Permitido, string Estado, string? Motivo, DateTime? DesdeUtc, DateTime? RevocadoUtc,
    DateTime AhoraUtc, int ConsultarCadaSegundos, int VigenciaSegundos, string? Version);

public record ResultadoPermiso(TipoResultado Tipo, string Codigo, string Mensaje, PermisoEmociones? Permiso = null)
{
    public bool Ok => Tipo == TipoResultado.Ok;
}

public class ConsentimientoVideoService
{
    public const string SinDecision = "sin-decision";
    public const string Acepta = "acepta";
    public const string Rechaza = "rechaza";
    public const string Revocado = "revocado";
    public const string NoDisponible = "no-disponible";

    private const string MensajeNoEncontrada = "No encontramos esa sesión.";
    private const int EdadMayor = 18;

    private readonly IConsentimientoVideoRepositorio _repo;
    private readonly IOrigenSalaRepositorio _origen;
    private readonly AccesoSalaService _acceso;
    private readonly ITextoConsentimientoVideo _textos;
    private readonly IEmocionesCifrado _cifrado;
    private readonly IAuditoriaEmociones _auditoria;
    private readonly TimeProvider _reloj;
    private readonly IOptionsMonitor<VideoOptions> _opciones;

    public ConsentimientoVideoService(IConsentimientoVideoRepositorio repo, IOrigenSalaRepositorio origen,
        AccesoSalaService acceso, ITextoConsentimientoVideo textos, IEmocionesCifrado cifrado,
        IAuditoriaEmociones auditoria, TimeProvider reloj, IOptionsMonitor<VideoOptions> opciones)
    {
        _repo = repo; _origen = origen; _acceso = acceso; _textos = textos; _cifrado = cifrado;
        _auditoria = auditoria; _reloj = reloj; _opciones = opciones;
    }

    // ───────────────────────── Paciente ─────────────────────────

    /// <summary>El estado de la decisión del paciente y, si la lectura está disponible, el texto que tiene que leer.</summary>
    public ResultadoConsentimiento Obtener(Identidad yo, string origen, int id)
    {
        if (ValidarPaciente(yo, origen, id, exigirVentana: true, out var ses) is { } rechazo) return rechazo;

        var texto = _textos.Actual();
        var (disponible, motivo) = Disponibilidad(ses!.IdPaciente, texto);
        var ultimo = _repo.Ultimo(ses.Origen, ses.Id, ses.IdPaciente, ConsentimientoVideoFila.Emociones);
        return Ok(Vista(ultimo, disponible, motivo, texto, conTexto: disponible));
    }

    /// <summary>Registra "acepta" o "rechaza" para esta sesión, con la versión del texto que la persona leyó.</summary>
    public ResultadoConsentimiento Decidir(Identidad yo, string origen, int id, string? decision, string? version)
    {
        if (ValidarPaciente(yo, origen, id, exigirVentana: true, out var ses) is { } rechazo) return rechazo;

        var d = TextoNormalizado.Normalizar(decision);
        if (d != Acepta && d != Rechaza)
            return Error(TipoResultado.SolicitudInvalida, "solicitud-invalida", "Indicá si aceptás o rechazás la lectura de emociones.");

        var texto = _textos.Actual();
        var (disponible, motivo) = Disponibilidad(ses!.IdPaciente, texto);
        if (!disponible) return Error(TipoResultado.Conflicto, motivo!, MensajeNoDisponible(motivo!));

        // La persona tiene que haber leído ESTE texto: si cambió mientras tanto, vuelve a leerlo.
        if (!string.Equals(version, texto!.Version, StringComparison.Ordinal))
            return Error(TipoResultado.Conflicto, "version-desactualizada", "El texto del consentimiento cambió. Volvé a leerlo antes de decidir.");

        var ultimo = _repo.Ultimo(ses.Origen, ses.Id, ses.IdPaciente, ConsentimientoVideoFila.Emociones);

        // Para dejar de compartir hay una operación propia, que además anula lo ya guardado.
        if (d == Rechaza && ultimo is { EsAceptacionVigente: true })
            return Error(TipoResultado.Conflicto, "usar-revocar", "Ya aceptaste: para dejar de compartir tus expresiones usá la opción de revocar.");

        // La misma decisión, sobre el mismo texto, que ya está vigente: no se agrega otra fila.
        if (ultimo is not null && ultimo.Decision == d && ultimo.RevocadoUtc is null && ultimo.VersionTexto == texto.Version)
            return Ok(Vista(ultimo, true, null, texto, conTexto: false));

        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var fila = _repo.Insertar(yo.IdUsuario, ses.IdPaciente, ses.Origen, ses.Id, texto.Version, texto.Hash, d, ahora, ConsentimientoVideoFila.Emociones);
        _auditoria.Consentimiento(yo.IdUsuario, ses.Origen, ses.Id, d, texto.Version, fila.IdConsentimientoVideo);
        return Ok(Vista(fila, true, null, texto, conTexto: false));
    }

    /// <summary>
    /// El paciente deja de compartir sus expresiones: corta el análisis, y las emociones ya guardadas de esta sesión
    /// se anulan (las notas clínicas no). Se puede hacer aunque la sala ya haya cerrado, y repetirlo no hace daño.
    /// </summary>
    public ResultadoConsentimiento Revocar(Identidad yo, string origen, int id)
    {
        if (ValidarPaciente(yo, origen, id, exigirVentana: false, out var ses) is { } rechazo) return rechazo;

        var texto = _textos.Actual();
        var ultimo = _repo.Ultimo(ses!.Origen, ses.Id, ses.IdPaciente, ConsentimientoVideoFila.Emociones);
        var ahora = _reloj.GetUtcNow().UtcDateTime;

        if (ultimo is { EsAceptacionVigente: true })
        {
            if (_repo.Revocar(ultimo.IdConsentimientoVideo, ahora))
            {
                var anuladas = _repo.AnularEmocionesDeLaSesion(ses.Origen, ses.Id, ses.IdPaciente, "revocacion", ahora);
                _auditoria.Consentimiento(yo.IdUsuario, ses.Origen, ses.Id, "revoca", ultimo.VersionTexto, ultimo.IdConsentimientoVideo, anuladas);
                ultimo = _repo.Ultimo(ses.Origen, ses.Id, ses.IdPaciente, ConsentimientoVideoFila.Emociones);
            }
        }
        else if (ultimo is { Decision: ConsentimientoVideoFila.Acepta })
        {
            // Ya estaba revocada: se vuelve a anular por si una revocación anterior se cortó a la mitad (no hace nada si no hay qué anular).
            var anuladas = _repo.AnularEmocionesDeLaSesion(ses.Origen, ses.Id, ses.IdPaciente, "revocacion", ahora);
            if (anuladas > 0)
                _auditoria.Consentimiento(yo.IdUsuario, ses.Origen, ses.Id, "revoca", ultimo.VersionTexto, ultimo.IdConsentimientoVideo, anuladas);
        }
        // Sin decisión o con un rechazo no hay nada que revocar: se contesta el estado actual.

        return Ok(Vista(ultimo, true, null, texto, conTexto: false));
    }

    // ───────────────────────── Profesional / Configurador ─────────────────────────

    /// <summary>
    /// ¿Puede la pantalla del profesional analizar las expresiones del paciente de esta sesión en este momento?
    /// Solo con una aceptación vigente de una persona a la que se le puede ofrecer el análisis.
    /// </summary>
    public ResultadoPermiso Permiso(Identidad yo, string origen, int id)
    {
        var ses = SesionDeVideoReglas.Resolver(_origen, origen, id);
        if (ses is null || !SesionDeVideoReglas.AtiendeLaSesion(yo, ses))
            return new ResultadoPermiso(TipoResultado.NoEncontrada, AccesoSalaService.CodigoNoEncontrada, MensajeNoEncontrada);

        var e = _opciones.CurrentValue.Emociones;
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        PermisoEmociones Armar(bool permitido, string estado, string? motivo, ConsentimientoVideoFila? f) => new(
            permitido, estado, motivo, f?.DecididoUtc, f?.RevocadoUtc, ahora,
            Math.Clamp(e.ConsultaCadaSegundos, 1, 60), Math.Clamp(e.VigenciaSegundos, 2, 300), f?.VersionTexto);

        var (disponible, motivoNoDisponible) = Disponibilidad(ses.IdPaciente, _textos.Actual());
        if (!disponible)
            return Permitido(Armar(false, NoDisponible, motivoNoDisponible, null));

        var ultimo = _repo.Ultimo(ses.Origen, ses.Id, ses.IdPaciente, ConsentimientoVideoFila.Emociones);
        var estado = EstadoDe(ultimo);
        return Permitido(Armar(estado == Acepta, estado, estado == Acepta || estado == SinDecision ? null : estado, ultimo));
    }

    private static ResultadoPermiso Permitido(PermisoEmociones p) => new(TipoResultado.Ok, "ok", "", p);

    // ───────────────────────── Reglas compartidas ─────────────────────────

    /// <summary>
    /// null si se puede mostrar o guardar emociones de esta persona; si no, el motivo. Es la misma regla para ofrecer el
    /// consentimiento, dar el permiso y aceptar lo que se guarda.
    /// </summary>
    public (bool Disponible, string? Motivo) Disponibilidad(int idPaciente, TextoConsentimiento? texto)
    {
        if (_cifrado.ProblemaDeConfiguracion() is not null || texto is null) return (false, "no-configurado");

        var datos = _repo.ObtenerDatosPaciente(idPaciente);
        if (datos is null) return (false, "no-configurado");

        // Se considera menor a quien tiene menos de 18 años o tiene un tutor cargado. Sin fecha de nacimiento (y sin
        // tutor) no se puede comprobar que sea mayor: por defecto no se ofrece.
        if (datos.TieneTutor) return (false, "menor-de-edad");
        if (datos.FechaNacimiento is not { } nacimiento)
            return _opciones.CurrentValue.Emociones.PermitirEdadDesconocida ? (true, null) : (false, "edad-desconocida");
        var hoy = DateOnly.FromDateTime(_reloj.GetLocalNow().DateTime);
        return Edad(DateOnly.FromDateTime(nacimiento), hoy) < EdadMayor ? (false, "menor-de-edad") : (true, null);
    }

    public static int Edad(DateOnly nacimiento, DateOnly hoy)
    {
        var edad = hoy.Year - nacimiento.Year;
        if (hoy < nacimiento.AddYears(edad)) edad--;
        return edad;
    }

    public static string EstadoDe(ConsentimientoVideoFila? f) => f switch
    {
        null => SinDecision,
        { Decision: ConsentimientoVideoFila.Rechaza } => Rechaza,
        { RevocadoUtc: not null } => Revocado,
        _ => Acepta
    };

    public static string MensajeNoDisponible(string motivo) => motivo switch
    {
        "menor-de-edad" => "La lectura de emociones no está disponible para esta persona.",
        "edad-desconocida" => "La lectura de emociones no está disponible: falta la fecha de nacimiento en la ficha del paciente.",
        _ => "La lectura de emociones no está disponible en este momento."
    };

    // El paciente de ESA sesión (y nadie más). exigirVentana: además, la sesión tiene que estar en un estado en el que se
    // pueda decidir (misma lectura que /video/acceso: turno confirmado, informe en entrevista, sin cerrar).
    private ResultadoConsentimiento? ValidarPaciente(Identidad yo, string origen, int id, bool exigirVentana, out SesionDeVideo? ses)
    {
        ses = SesionDeVideoReglas.Resolver(_origen, origen, id);
        if (ses is null || !SesionDeVideoReglas.EsElPaciente(yo, ses)) { ses = null; return NoEncontrada(); }
        if (!exigirVentana) return null;

        var acceso = _acceso.Evaluar(yo, origen, id);
        switch (acceso.Estado)
        {
            case EstadoAcceso.Concedido:
            case EstadoAcceso.AunNoAbre:   // puede leer y decidir antes de que abra la sala
                return null;
            case EstadoAcceso.NoExiste:
            case EstadoAcceso.SinAcceso:
                ses = null;
                return NoEncontrada();
            default:
                var cerro = acceso.Estado == EstadoAcceso.YaTermino ? acceso.Hasta : null;
                ses = null;
                return new ResultadoConsentimiento(TipoResultado.Conflicto, acceso.Codigo, acceso.Mensaje, CerroUtc: cerro);
        }
    }

    private EstadoConsentimiento Vista(ConsentimientoVideoFila? f, bool disponible, string? motivo, TextoConsentimiento? texto, bool conTexto)
    {
        if (!disponible)
            return new EstadoConsentimiento(false, NoDisponible, motivo, null, false, null, null, null, null);
        // Version es la del texto VIGENTE: es la que la pantalla tiene que repetir al decidir. Si la decisión anterior de esta
        // sesión fue sobre otra versión (el texto cambió mientras tanto), devolver esa dejaría a la persona sin poder volver
        // a decidir: cada intento daría 409 version-desactualizada.
        return new EstadoConsentimiento(true, EstadoDe(f), null,
            texto?.Version ?? f?.VersionTexto, texto?.Provisorio ?? false,
            conTexto ? texto?.Titulo : null, conTexto ? texto?.Parrafos : null,
            f?.DecididoUtc, f?.RevocadoUtc);
    }

    private static ResultadoConsentimiento Ok(EstadoConsentimiento e) => new(TipoResultado.Ok, "ok", "", e);
    private static ResultadoConsentimiento NoEncontrada() =>
        new(TipoResultado.NoEncontrada, AccesoSalaService.CodigoNoEncontrada, MensajeNoEncontrada);
    private static ResultadoConsentimiento Error(TipoResultado tipo, string codigo, string mensaje) => new(tipo, codigo, mensaje);
}

/// <summary>De qué sesión de video se trata y quién es su paciente (y su profesional, si es un turno).</summary>
public record SesionDeVideo(string Origen, int Id, int IdPaciente, int? IdProfesional);

public static class SesionDeVideoReglas
{
    public static SesionDeVideo? Resolver(IOrigenSalaRepositorio repo, string origen, int id) => origen switch
    {
        OrigenSala.Turno => repo.ObtenerTurno(id) is { } t ? new SesionDeVideo(OrigenSala.Turno, t.IdTurno, t.IdPaciente, t.IdProfesional) : null,
        OrigenSala.Admision => repo.ObtenerAdmision(id) is { } a ? new SesionDeVideo(OrigenSala.Admision, a.IdInforme, a.IdPaciente, null) : null,
        _ => null
    };

    public static bool EsElPaciente(Identidad yo, SesionDeVideo s) => yo.EsPaciente && yo.IdPaciente == s.IdPaciente;

    // Quién hace de profesional en la sala: el del turno y, en las admisiones, quien tiene el privilegio Admisión. Mismo
    // criterio que AccesoSalaService (etapa 2 de roles: el Configurador ya no entra a sesiones clínicas).
    public static bool AtiendeLaSesion(Identidad yo, SesionDeVideo s) =>
        (s.Origen == OrigenSala.Admision && yo.HaceAdmision)
        || (s.Origen == OrigenSala.Turno && yo.EsProfesional && yo.IdProfesional is int ip && ip == s.IdProfesional);
}
