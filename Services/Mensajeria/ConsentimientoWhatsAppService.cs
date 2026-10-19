using Microsoft.Extensions.Options;

// Consentimiento de los avisos de turnos por WhatsApp. Decide en el servidor, con los datos de la base: la pantalla solo manda qué texto leyó
// la persona (la versión) y si quiere aceptar o revocar.
//
//  · Es del paciente y de nadie más: quién es se lee del JWT (jamás de un id del pedido), así que no se puede aceptar ni revocar en nombre
//    de otro. Ni el profesional ni el Configurador pueden hacerlo.
//  · Aceptar es explícito: sin una aceptación no hay optIn. Nada viene tildado por defecto.
//  · Una fila por aceptación (tabla ConsentimientoWhatsApp); el estado es el de la fila más nueva. Quedan la versión del texto y el
//    SHA-256 del texto exacto que se mostró, quién lo hizo, para qué número y cuándo (UTC).
//  · La aceptación vale para ESE número y ESE texto: si el paciente cambia el teléfono, o se publica un texto nuevo, deja de valer hasta
//    que vuelva a aceptar (y el servicio de WhatsApp no recibe optIn mientras tanto).
//  · A un menor de edad (o con tutor cargado) no se le deja aceptar solo. PROVISORIO, igual que el de la lectura de emociones: lo tiene que
//    validar el equipo legal.
//  · La BAJA que la persona escribe por WhatsApp también revoca (RegistrarBaja).
//  · Todo esto está DORMIDO mientras la llave Mensajeria:AvisosPorWhatsApp esté apagada (por defecto): no se ofrece (texto y estado salen como
//    "no disponible"), no se puede aceptar ni revocar, y no se toca la tabla ConsentimientoWhatsApp. Ver Habilitado.

/// <summary>Lo que NotificadorTurnos necesita: ¿qué le mando al servicio de WhatsApp sobre este paciente?</summary>
public enum PermisoEnvioWhatsApp
{
    /// <summary>Nunca decidió (o su aceptación ya no vale): se manda sin optIn y el servicio decide (solo si ella le escribió primero).</summary>
    SinDecision,
    /// <summary>Aceptación vigente: se manda con optIn:true.</summary>
    ConConsentimiento,
    /// <summary>Revocó: no se manda nada por WhatsApp.</summary>
    Revocado,
    /// <summary>La llave Mensajeria:AvisosPorWhatsApp está apagada: no se manda nada por WhatsApp y ni se mira la tabla del consentimiento.</summary>
    Apagado
}

public interface IPoliticaEnvioWhatsApp
{
    PermisoEnvioWhatsApp Evaluar(int idPaciente, string? telefonoActual);
}

/// <summary>Lo que BotReservasService necesita: la persona de ese teléfono escribió BAJA.</summary>
public interface IBajaDeWhatsApp
{
    /// <summary>Revoca las aceptaciones abiertas del teléfono (los últimos 10 dígitos). Devuelve cuántas.</summary>
    int RegistrarBaja(string telefonoNormalizado);
}

public enum TipoErrorConsentimiento { SoloPacientes, NoEncontrado, SolicitudInvalida, Conflicto }

public record ErrorDeConsentimiento(TipoErrorConsentimiento Tipo, string Codigo, string Mensaje);

/// <param name="Estado">sin-decision | acepta | revocado | telefono-cambiado | texto-desactualizado | no-disponible</param>
/// <param name="Disponible">false si en este momento no se puede aceptar (y no hay una aceptación vigente): mirar <paramref name="Motivo"/>.</param>
/// <param name="Version">La del texto VIGENTE: es la que la pantalla tiene que repetir al aceptar.</param>
public record EstadoConsentimientoWhatsApp(bool Disponible, string Estado, string? Motivo, string? Version, bool Provisorio, string? Titulo,
    IReadOnlyList<string>? Texto, DateTime? AceptadoUtc, DateTime? RevocadoUtc, string? VersionAceptada);

public record ResultadoConsentimientoWhatsApp(EstadoConsentimientoWhatsApp? Estado, ErrorDeConsentimiento? Error)
{
    public bool Ok => Error is null;
}

/// <summary>El texto vigente, para la pantalla de registro (todavía no hay sesión).</summary>
public record TextoDelConsentimientoWhatsApp(bool Disponible, string? Version, bool Provisorio, string? Titulo, IReadOnlyList<string>? Texto);

public class ConsentimientoWhatsAppService : IPoliticaEnvioWhatsApp, IBajaDeWhatsApp
{
    public const string SinDecision = "sin-decision";
    public const string Acepta = "acepta";
    public const string Revocado = "revocado";
    public const string TelefonoCambiado = "telefono-cambiado";
    public const string TextoDesactualizado = "texto-desactualizado";
    public const string NoDisponible = "no-disponible";

    /// <summary>El motivo (y el código del error) cuando la llave Mensajeria:AvisosPorWhatsApp está apagada. Las pantallas lo reconocen y no muestran nada.</summary>
    public const string AvisosApagados = "avisos-apagados";

    private const string MensajeApagado = "Los avisos por WhatsApp no están disponibles en este momento.";
    private const int EdadMayor = 18;

    private readonly IConsentimientoWhatsAppRepositorio _repo;
    private readonly ITextoConsentimientoWhatsApp _textos;
    private readonly IAuditoriaConsentimientoWhatsApp _auditoria;
    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ConsentimientoWhatsAppService> _log;

    public ConsentimientoWhatsAppService(IConsentimientoWhatsAppRepositorio repo, ITextoConsentimientoWhatsApp textos,
        IAuditoriaConsentimientoWhatsApp auditoria, IOptionsMonitor<MensajeriaOptions> opciones, TimeProvider reloj, ILogger<ConsentimientoWhatsAppService> log)
    {
        _repo = repo; _textos = textos; _auditoria = auditoria; _opciones = opciones; _reloj = reloj; _log = log;
    }

    /// <summary>
    /// La llave Mensajeria:AvisosPorWhatsApp. Apagada (por defecto) el consentimiento entero está dormido: no se ofrece en ninguna pantalla, no se puede
    /// aceptar ni revocar, y NO se toca la tabla ConsentimientoWhatsApp, así que la API anda igual sin haber aplicado la migración 16.
    /// </summary>
    public bool Habilitado => _opciones.CurrentValue.AvisosPorWhatsApp;

    private DateTime AhoraUtc => _reloj.GetUtcNow().UtcDateTime;

    // ───────────────────────── Texto (público) ─────────────────────────

    public TextoDelConsentimientoWhatsApp Texto()
    {
        if (!Habilitado) return new TextoDelConsentimientoWhatsApp(false, null, false, null, null);   // apagado: la pantalla de registro no ofrece nada
        var t = _textos.Actual();
        return t is null
            ? new TextoDelConsentimientoWhatsApp(false, null, false, null, null)
            : new TextoDelConsentimientoWhatsApp(true, t.Version, t.Provisorio, t.Titulo, t.Parrafos);
    }

    // ───────────────────────── Paciente ─────────────────────────

    /// <summary>El estado del consentimiento del paciente de la sesión y el texto que tiene que leer.</summary>
    public ResultadoConsentimientoWhatsApp Obtener(Identidad yo)
    {
        if (!EsPaciente(yo, out var idPaciente)) return Error(TipoErrorConsentimiento.SoloPacientes, "solo-pacientes", "Esto es solo para pacientes.");
        if (!Habilitado) return Ok(EstadoApagado());   // sin leer la base: las pantallas no muestran nada
        return Vista(idPaciente);
    }

    /// <summary>Acepta recibir avisos por WhatsApp, con la versión del texto que la persona leyó. Repetirlo no hace nada.</summary>
    public ResultadoConsentimientoWhatsApp Aceptar(Identidad yo, string? version)
    {
        if (!EsPaciente(yo, out var idPaciente)) return Error(TipoErrorConsentimiento.SoloPacientes, "solo-pacientes", "Esto es solo para pacientes.");
        if (!Habilitado) return Error(TipoErrorConsentimiento.Conflicto, AvisosApagados, MensajeApagado);

        var datos = _repo.ObtenerDatosPaciente(idPaciente);
        if (datos is null) return Error(TipoErrorConsentimiento.NoEncontrado, "no-encontrado", "No encontramos tu ficha.");

        var texto = _textos.Actual();
        var telefono = TelefonoArgentino.Normalizar(datos.Telefono);
        if (ReglasParaAceptar(texto, version, telefono, datos.FechaNacimiento, datos.TieneTutor) is { } rechazo)
            return Error(TipoErrorConsentimiento.Conflicto, rechazo.Codigo, rechazo.Mensaje);

        var ultimo = _repo.Ultimo(idPaciente);
        // La misma aceptación, sobre el mismo texto y el mismo número, que ya está vigente: no se agrega otra fila.
        if (ultimo is { EstaAbierta: true } && ultimo.Telefono == telefono && ultimo.VersionTexto == texto!.Version)
            return Vista(idPaciente);

        // Si tenía una abierta, se cierra en el mismo paso por la razón que corresponda (número nuevo, o texto nuevo).
        var motivoSiReemplaza = ultimo is { EstaAbierta: true } && ultimo.Telefono != telefono
            ? ConsentimientoWhatsAppFila.MotivoTelefonoCambiado
            : ConsentimientoWhatsAppFila.MotivoTextoNuevo;
        var fila = _repo.Aceptar(idPaciente, yo.IdUsuario, telefono!, ConsentimientoWhatsAppFila.OrigenPerfil, texto!.Version, texto.Hash,
            AhoraUtc, motivoSiReemplaza);
        // fila == null: otro pedido igual la abrió un instante antes. El estado que se contesta es el de la base.
        if (fila is not null)
            _auditoria.Registrar(yo.IdUsuario, idPaciente, "acepta", fila.IdConsentimientoWhatsApp, $"version: {texto.Version}; origen: perfil");
        return Vista(idPaciente);
    }

    /// <summary>Deja de recibir avisos por WhatsApp. Se puede hacer siempre y repetirlo no hace daño.</summary>
    public ResultadoConsentimientoWhatsApp Revocar(Identidad yo)
    {
        if (!EsPaciente(yo, out var idPaciente)) return Error(TipoErrorConsentimiento.SoloPacientes, "solo-pacientes", "Esto es solo para pacientes.");
        if (!Habilitado) return Error(TipoErrorConsentimiento.Conflicto, AvisosApagados, MensajeApagado);

        var ultimo = _repo.Ultimo(idPaciente);
        if (ultimo is { EstaAbierta: true } && _repo.Revocar(idPaciente, AhoraUtc, ConsentimientoWhatsAppFila.MotivoPaciente))
            _auditoria.Registrar(yo.IdUsuario, idPaciente, "revoca", ultimo.IdConsentimientoWhatsApp, $"version: {ultimo.VersionTexto}; motivo: paciente");
        // Sin aceptación abierta no hay nada que revocar (nunca aceptó, o ya estaba revocada): se contesta el estado actual.
        return Vista(idPaciente);
    }

    // ───────────────────────── Registro (todavía sin cuenta) ─────────────────────────

    /// <summary>
    /// Quien se registra tildó la casilla: ¿se puede registrar su aceptación? null si sí; si no, por qué. Se llama ANTES de crear la cuenta,
    /// así un teléfono que no sirve no deja una cuenta creada con un registro a medias.
    /// </summary>
    public ErrorDeConsentimiento? ValidarAceptacionEnRegistro(string? telefono, DateTime? fechaNacimiento, string? version)
    {
        if (!Habilitado) return new ErrorDeConsentimiento(TipoErrorConsentimiento.Conflicto, AvisosApagados, MensajeApagado);
        var rechazo = ReglasParaAceptar(_textos.Actual(), version, TelefonoArgentino.Normalizar(telefono), fechaNacimiento, tieneTutor: false);
        if (rechazo is null) return null;
        // El teléfono y la edad son datos que mandó la persona en este mismo pedido (400); el texto es un estado del servidor (409).
        return new ErrorDeConsentimiento(rechazo.DatoDelPedido ? TipoErrorConsentimiento.SolicitudInvalida : TipoErrorConsentimiento.Conflicto,
            rechazo.Codigo, rechazo.Mensaje);
    }

    /// <summary>
    /// Registra la aceptación de quien acaba de crear su cuenta (el usuario es ella misma). false si no se pudo: la cuenta ya está creada y
    /// puede aceptar desde su perfil. Nunca lanza: el registro no se puede caer por esto.
    /// </summary>
    public bool AceptarEnRegistro(int idPaciente, int idUsuario, string? telefono, string? version)
    {
        if (!Habilitado) return false;   // sin tocar la tabla
        try
        {
            var texto = _textos.Actual();
            var tel = TelefonoArgentino.Normalizar(telefono);
            if (texto is null || tel is null || version != texto.Version) return false;

            var fila = _repo.Aceptar(idPaciente, idUsuario, tel, ConsentimientoWhatsAppFila.OrigenRegistro, texto.Version, texto.Hash,
                AhoraUtc, ConsentimientoWhatsAppFila.MotivoTextoNuevo);
            if (fila is null) return false;
            _auditoria.Registrar(idUsuario, idPaciente, "acepta", fila.IdConsentimientoWhatsApp, $"version: {texto.Version}; origen: registro");
            return true;
        }
        catch (Exception e)
        {
            _log.LogWarning("WhatsApp: no se pudo registrar la aceptación del registro ({Tipo}).", e.GetType().Name);
            return false;
        }
    }

    // ───────────────────────── Avisos y BAJA ─────────────────────────

    /// <summary>
    /// ¿Qué se le manda al servicio de WhatsApp sobre este paciente? Solo una aceptación vigente (de ESTE número y de ESTE texto) da optIn.
    /// Si la última decisión fue revocar, no se manda nada. Todo lo demás es "sin decisión": el servicio decide.
    /// </summary>
    public PermisoEnvioWhatsApp Evaluar(int idPaciente, string? telefonoActual)
    {
        if (!Habilitado) return PermisoEnvioWhatsApp.Apagado;   // sin leer la tabla
        var estado = EstadoDe(_repo.Ultimo(idPaciente), TelefonoArgentino.Normalizar(telefonoActual), _textos.Actual());
        return estado switch
        {
            Acepta => PermisoEnvioWhatsApp.ConConsentimiento,
            Revocado => PermisoEnvioWhatsApp.Revocado,
            _ => PermisoEnvioWhatsApp.SinDecision
        };
    }

    /// <summary>
    /// La persona de ese número escribió BAJA por WhatsApp: se revocan sus aceptaciones abiertas (puede haber más de un paciente con el mismo
    /// celular: un número que dice basta es basta para todos). El servicio de WhatsApp ya dejó de escribirle; esto hace que la base lo sepa.
    /// </summary>
    public int RegistrarBaja(string telefonoNormalizado)
    {
        // Apagado el consentimiento no hay aceptaciones que revocar ni tabla que tocar. El servicio de WhatsApp igual bloquea a esa persona (BAJA gana sobre todo).
        if (!Habilitado) return 0;
        var telefono = TelefonoArgentino.Normalizar(telefonoNormalizado);
        if (telefono is null) return 0;
        try
        {
            var revocadas = _repo.RevocarVigentesDelTelefono(telefono, AhoraUtc, ConsentimientoWhatsAppFila.MotivoBaja);
            foreach (var (idConsentimiento, idPaciente) in revocadas)
                _auditoria.Registrar(null, idPaciente, "baja", idConsentimiento, "motivo: baja-whatsapp");
            _log.LogInformation("WhatsApp: llegó una BAJA; aceptaciones revocadas: {Cantidad}.", revocadas.Count);
            return revocadas.Count;
        }
        catch (Exception e)
        {
            // El servicio de WhatsApp ya bloqueó los envíos a ese número; si la base no se pudo actualizar, el aviso se rechaza ahí.
            _log.LogWarning("WhatsApp: llegó una BAJA pero no se pudo revocar en la base ({Tipo}).", e.GetType().Name);
            return 0;
        }
    }

    // ───────────────────────── Reglas compartidas ─────────────────────────

    public static string EstadoDe(ConsentimientoWhatsAppFila? ultimo, string? telefonoActual, TextoConsentimiento? texto)
    {
        if (ultimo is null) return SinDecision;
        if (!ultimo.EstaAbierta) return Revocado;
        if (ultimo.Telefono != telefonoActual) return TelefonoCambiado;
        if (texto is null || ultimo.VersionTexto != texto.Version) return TextoDesactualizado;
        return Acepta;
    }

    private sealed record Rechazo(string Codigo, string Mensaje, bool DatoDelPedido);

    /// <summary>null si se puede aceptar; si no, el motivo. Es la misma regla en el registro, en el perfil y para ofrecer la casilla.</summary>
    private Rechazo? ReglasParaAceptar(TextoConsentimiento? texto, string? version, string? telefono, DateTime? fechaNacimiento, bool tieneTutor)
    {
        if (texto is null)
            return new Rechazo("no-configurado", "Los avisos por WhatsApp no están disponibles en este momento.", false);
        // La persona tiene que haber leído ESTE texto: si cambió mientras tanto, vuelve a leerlo.
        if (!string.Equals(version, texto.Version, StringComparison.Ordinal))
            return new Rechazo("version-desactualizada", "El texto cambió. Volvé a leerlo antes de aceptar.", false);
        if (EsMenor(fechaNacimiento, tieneTutor))
            return new Rechazo("menor-de-edad",
                "Los avisos por WhatsApp de una persona menor de 18 años los tiene que activar su madre, padre o tutor. Podés seguir sin activarlos.", true);
        if (telefono is null)
            return new Rechazo("telefono-invalido",
                "Para recibir avisos por WhatsApp cargá un celular argentino válido, con código de área (por ejemplo 11 5555-0001).", true);
        return null;
    }

    // Se considera menor a quien tiene menos de 18 años o tiene un tutor cargado. Sin fecha de nacimiento (y sin tutor) no se puede
    // comprobar: se deja pasar (la fecha es obligatoria en el registro de la pantalla).
    private bool EsMenor(DateTime? fechaNacimiento, bool tieneTutor)
    {
        if (tieneTutor) return true;
        if (fechaNacimiento is not { } nacimiento) return false;
        var hoy = DateOnly.FromDateTime(_reloj.GetLocalNow().DateTime);
        return ConsentimientoVideoService.Edad(DateOnly.FromDateTime(nacimiento), hoy) < EdadMayor;
    }

    private ResultadoConsentimientoWhatsApp Vista(int idPaciente)
    {
        var datos = _repo.ObtenerDatosPaciente(idPaciente);
        if (datos is null) return Error(TipoErrorConsentimiento.NoEncontrado, "no-encontrado", "No encontramos tu ficha.");

        var texto = _textos.Actual();
        var telefono = TelefonoArgentino.Normalizar(datos.Telefono);
        var ultimo = _repo.Ultimo(idPaciente);

        if (texto is null)
            return Ok(new EstadoConsentimientoWhatsApp(false, NoDisponible, "no-configurado", null, false, null, null, ultimo?.AceptadoUtc, ultimo?.RevocadoUtc, ultimo?.VersionTexto));

        var estado = EstadoDe(ultimo, telefono, texto);
        // ¿Puede aceptar (de nuevo) ahora? Se prueba con la versión vigente, que es la que la pantalla va a mandar.
        var rechazo = ReglasParaAceptar(texto, texto.Version, telefono, datos.FechaNacimiento, datos.TieneTutor);
        var disponible = estado == Acepta || rechazo is null;

        return Ok(new EstadoConsentimientoWhatsApp(disponible, estado, disponible ? null : rechazo?.Codigo, texto.Version, texto.Provisorio,
            texto.Titulo, texto.Parrafos, ultimo?.AceptadoUtc, ultimo?.RevocadoUtc, ultimo?.VersionTexto));
    }

    // Lo que se contesta con la llave apagada: nada para mostrar y nada para decidir.
    private static EstadoConsentimientoWhatsApp EstadoApagado() =>
        new(false, NoDisponible, AvisosApagados, null, false, null, null, null, null, null);

    private static bool EsPaciente(Identidad yo, out int idPaciente)
    {
        idPaciente = yo.IdPaciente ?? 0;
        return yo.EsPaciente && yo.IdPaciente is > 0;
    }

    private static ResultadoConsentimientoWhatsApp Ok(EstadoConsentimientoWhatsApp e) => new(e, null);
    private static ResultadoConsentimientoWhatsApp Error(TipoErrorConsentimiento tipo, string codigo, string mensaje) =>
        new(null, new ErrorDeConsentimiento(tipo, codigo, mensaje));
}
