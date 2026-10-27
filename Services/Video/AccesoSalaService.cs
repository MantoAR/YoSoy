using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

// Quién puede entrar a la sala de video de un turno o de una entrevista de admisión, y cuándo.
// Decide en el servidor, con los datos de la base: el cliente solo dice "origen + id" y nunca elige la
// sala ni el rol (ver POST /video/acceso en Endpoints/VideoEndpoints.cs).
//
//  Turno:     el paciente del turno, el profesional del turno y el Configurador (la misma regla que
//             PUT /turnos/{id}/generar-sala: el profesional solo en su turno, el Configurador en cualquiera).
//             El turno tiene que estar Confirmado y tener la sala ya generada (acá no se crea).
//  Admisión:  el paciente del informe y el Configurador, que es quien hace la entrevista. El informe tiene
//             que estar "En entrevista" (programada, ni derivada ni rechazada) y tener sala.
//
//  Ventana:   desde N minutos antes del inicio (10) hasta M minutos después del FIN (30), con el inicio y
//             la duración reales del turno. Incluye el instante de apertura y excluye el de cierre
//             ([desde, hasta)): en el cierre el token ya nacería vencido.
//  Zona:      Turno.Fecha y Turno.Hora no llevan zona: son la hora de la clínica, la del reloj de la aplicación
//             (RelojDeLaClinica, la misma que usan las reglas de turnos). Así "ese horario ya pasó" y "la sala abre"
//             hablan del mismo instante, y no dependen de la zona del servidor.
//
// El orden de los chequeos importa: primero existencia y pertenencia, así quien no tiene nada que ver con un
// turno nunca se entera de su estado, su horario ni si ya tiene sala. Y "no existe" y "existe pero no sos parte"
// se contestan exactamente igual (404, mismo cuerpo): nadie puede averiguar qué ids existen.
public class AccesoSalaService
{
    public const string EstadoTurnoConfirmado = "confirmado";
    public const string EstadoAdmisionEnEntrevista = "en entrevista";

    // Lo que se contesta tanto si la sesión no existe como si existe y la persona no es parte de ella: el mismo
    // texto y el mismo código, para que nadie pueda sondear qué ids existen. Por dentro siguen siendo dos
    // resultados (NoExiste y SinAcceso) porque solo el segundo se audita.
    public const string MensajeNoEncontrada = "No encontramos esa sesión.";
    public const string CodigoNoEncontrada = "no-existe";

    // Los caracteres que acepta el signaling para "sala" (signaling-server/README.md, "Token de sala").
    private static readonly Regex ReSala = new("^[A-Za-z0-9_.:-]{1,100}$", RegexOptions.Compiled);

    private readonly IOrigenSalaRepositorio _repo;
    private readonly TimeProvider _reloj;
    private readonly IOptionsMonitor<VideoOptions> _opciones;
    private readonly ILogger<AccesoSalaService> _log;

    public AccesoSalaService(IOrigenSalaRepositorio repo, TimeProvider reloj, IOptionsMonitor<VideoOptions> opciones,
        ILogger<AccesoSalaService> log)
    {
        _repo = repo; _reloj = reloj; _opciones = opciones; _log = log;
    }

    public ResultadoAcceso Evaluar(Identidad yo, string origen, int id)
    {
        var o = _opciones.CurrentValue;
        var ahora = _reloj.GetUtcNow();
        return origen switch
        {
            OrigenSala.Turno => EvaluarTurno(yo, id, o, ahora),
            OrigenSala.Admision => EvaluarAdmision(yo, id, o, ahora),
            _ => ResultadoAcceso.De(EstadoAcceso.NoExiste, MensajeNoEncontrada, ahora)
        };
    }

    private ResultadoAcceso EvaluarTurno(Identidad yo, int id, VideoOptions o, DateTimeOffset ahora)
    {
        var t = _repo.ObtenerTurno(id);
        if (t is null) return ResultadoAcceso.De(EstadoAcceso.NoExiste, MensajeNoEncontrada, ahora);

        // El rol del token sale de acá, de quién es el usuario en ESTE turno, nunca de lo que mande el cliente.
        string? rol = null;
        if (yo.EsPaciente && yo.IdPaciente == t.IdPaciente) rol = Roles.Paciente;
        else if (yo.EsProfesional && yo.IdProfesional == t.IdProfesional) rol = Roles.Profesional;
        // Etapa 2 de roles: el Configurador es administrador y no entra a sesiones clínicas; a una sala de turno entra el
        // paciente y el profesional del turno (CRM con Gestión de agendas genera la sala, pero no se sienta en ella).
        if (rol is null) return ResultadoAcceso.De(EstadoAcceso.SinAcceso, MensajeNoEncontrada, ahora);

        if (TextoNormalizado.Normalizar(t.Estado) != EstadoTurnoConfirmado)
            return ResultadoAcceso.De(EstadoAcceso.EstadoInvalido, MensajeEstadoTurno(t.Estado), ahora);

        var zona = Zona(o);
        var inicio = EnZona(t.Fecha.Date + t.Hora, zona);
        var duracion = t.DuracionMinutos is > 0 ? t.DuracionMinutos.Value : o.DuracionPorDefectoMinutos;
        return EvaluarVentana(rol, t.CodigoSala, inicio, duracion, o, ahora);
    }

    private ResultadoAcceso EvaluarAdmision(Identidad yo, int id, VideoOptions o, DateTimeOffset ahora)
    {
        var a = _repo.ObtenerAdmision(id);
        if (a is null) return ResultadoAcceso.De(EstadoAcceso.NoExiste, MensajeNoEncontrada, ahora);

        // Solo el paciente del informe y quien hace la admisión (un Profesional con el privilegio Admisión, en el asiento del
        // profesional). Un Profesional sin ese privilegio, el CRM y el Configurador no entran a una admisión.
        string? rol = null;
        if (yo.EsPaciente && yo.IdPaciente == a.IdPaciente) rol = Roles.Paciente;
        else if (yo.HaceAdmision) rol = Roles.Profesional;
        if (rol is null) return ResultadoAcceso.De(EstadoAcceso.SinAcceso, MensajeNoEncontrada, ahora);

        if (TextoNormalizado.Normalizar(a.Estado) != EstadoAdmisionEnEntrevista)
            return ResultadoAcceso.De(EstadoAcceso.EstadoInvalido, "La entrevista de admisión no está programada.", ahora);
        if (a.FechaEntrevista is null)
            return ResultadoAcceso.De(EstadoAcceso.EstadoInvalido, "La entrevista de admisión no tiene fecha.", ahora);

        var inicio = EnZona(a.FechaEntrevista.Value, Zona(o));
        return EvaluarVentana(rol, a.CodigoSala, inicio, o.Admision.DuracionMinutos, o, ahora);
    }

    private ResultadoAcceso EvaluarVentana(string rol, string? sala, DateTimeOffset inicio, int duracionMinutos,
        VideoOptions o, DateTimeOffset ahora)
    {
        var (desde, hasta) = Ventana(inicio, duracionMinutos, o);
        var fase = EstadoDeSala.Fase(desde, hasta, ahora);   // la misma regla que leen los paneles (GET /salas/estado)
        if (fase == FaseSala.PorAbrir)
            return ResultadoAcceso.De(EstadoAcceso.AunNoAbre,
                $"Todavía no se puede entrar: la sala se abre {Acotar(o.Ventana.MinutosAntes, 0, MaxVentanaMinutos)} minutos antes del horario.",
                ahora, desde, hasta);
        if (fase == FaseSala.Cerrada)
            return ResultadoAcceso.De(EstadoAcceso.YaTermino, "Esta sesión ya terminó.", ahora, desde, hasta);

        if (string.IsNullOrWhiteSpace(sala))
            return ResultadoAcceso.De(EstadoAcceso.SalaNoGenerada, "Todavía no se generó la sala de esta sesión.", ahora, desde, hasta);
        if (!ReSala.IsMatch(sala))
        {
            // No debería pasar: los códigos los genera la API con un formato fijo.
            _log.LogError("Video: un código de sala guardado no cumple el formato que acepta el signaling; no se emite token.");
            return ResultadoAcceso.De(EstadoAcceso.SalaInvalida, "La sala de esta sesión no está disponible.", ahora, desde, hasta);
        }
        return ResultadoAcceso.Concedido(rol, sala, ahora, desde, hasta);
    }

    /// <summary>Apertura y cierre de la ventana para una sesión que empieza en <paramref name="inicio"/>.</summary>
    public static (DateTimeOffset Desde, DateTimeOffset Hasta) Ventana(DateTimeOffset inicio, int duracionMinutos, VideoOptions o)
    {
        var antes = TimeSpan.FromMinutes(Acotar(o.Ventana.MinutosAntes, 0, MaxVentanaMinutos));
        var despues = TimeSpan.FromMinutes(Acotar(o.Ventana.MinutosDespues, 0, MaxVentanaMinutos));
        var duracion = TimeSpan.FromMinutes(Acotar(duracionMinutos, 1, MaxDuracionMinutos));
        return (inicio - antes, inicio + duracion + despues);
    }

    public const int MaxVentanaMinutos = 240;
    public const int MaxDuracionMinutos = 480;

    public static int Acotar(int valor, int min, int max) => Math.Clamp(valor, min, max);

    // La zona en la que están escritos Turno.Fecha / Turno.Hora: la de la clínica, que es la del reloj de la aplicación
    // (Turnos:ZonaHoraria, por defecto America/Argentina/Buenos_Aires), no la del sistema operativo. Video:ZonaHoraria
    // es lo que había antes: si está y existe, manda para la ventana; si no existe, se usa la de la clínica.
    private TimeZoneInfo Zona(VideoOptions o)
    {
        var clinica = _reloj.LocalTimeZone;
        if (!string.IsNullOrWhiteSpace(o.ZonaHoraria))
        {
            var propia = ZonaHorariaDeLaClinica.Buscar(o.ZonaHoraria.Trim());
            if (propia is not null)
            {
                AvisarSiNoCoincide(propia, clinica);
                return propia;
            }
            _log.LogWarning("Video:ZonaHoraria \"{Zona}\" no existe en este servidor: se usa la zona horaria de la clínica ({Clinica}).",
                o.ZonaHoraria, clinica.Id);
        }
        return clinica;
    }

    // Dos zonas distintas para la misma hora de pared es una configuración equivocada: la ventana de la sala y las
    // reglas de turnos hablarían de instantes distintos. Se avisa una sola vez, no en cada pedido.
    private int _avisoZonasDistintas;

    private void AvisarSiNoCoincide(TimeZoneInfo video, TimeZoneInfo clinica)
    {
        var ahora = _reloj.GetUtcNow();
        if (video.GetUtcOffset(ahora) == clinica.GetUtcOffset(ahora)) return;
        if (Interlocked.Exchange(ref _avisoZonasDistintas, 1) == 0)
            _log.LogWarning("Video:ZonaHoraria ({Video}) no coincide con la zona horaria de la clínica ({Clinica}): la ventana " +
                "de la sala usa la de Video y las reglas de turnos la de la clínica. Dejá Video:ZonaHoraria vacía o igualala " +
                "a Turnos:ZonaHoraria.", video.Id, clinica.Id);
    }

    // La base guarda la hora tal cual la clínica la escribió (sin zona): se la interpreta en la zona de la
    // clínica para tener el instante real.
    private static DateTimeOffset EnZona(DateTime sinZona, TimeZoneInfo zona)
    {
        var d = DateTime.SpecifyKind(sinZona, DateTimeKind.Unspecified);
        return new DateTimeOffset(d, zona.GetUtcOffset(d));
    }

    private static string MensajeEstadoTurno(string estado) => TextoNormalizado.Normalizar(estado) switch
    {
        "pendiente" => "El turno todavía no está confirmado.",
        "cancelado" => "El turno fue cancelado.",
        "completado" => "El turno ya fue realizado.",
        _ => "El turno no está disponible para una videollamada."
    };
}

public static class OrigenSala
{
    public const string Turno = "turno";
    public const string Admision = "admision";
}

public enum EstadoAcceso { Concedido, NoExiste, SinAcceso, EstadoInvalido, AunNoAbre, YaTermino, SalaNoGenerada, SalaInvalida }

/// <summary>
/// Resultado de evaluar un acceso. Ahora es el instante con el que se evaluó: quien emite el token usa ESE mismo
/// instante, así la decisión y el vencimiento no dependen de dos lecturas distintas del reloj. Desde / Hasta es la
/// ventana de la sesión (la tienen todos los resultados que llegaron a calcularla). Rol y Sala, solo cuando se concede.
/// </summary>
public record ResultadoAcceso(EstadoAcceso Estado, string Mensaje, string? Rol, string? Sala,
    DateTimeOffset Ahora, DateTimeOffset? Desde, DateTimeOffset? Hasta)
{
    public bool Ok => Estado == EstadoAcceso.Concedido;

    // Texto estable para que el frontend decida qué mostrar sin interpretar el mensaje. Ojo: "sin-acceso" es solo el
    // motivo que queda en la auditoría; por HTTP, SinAcceso se contesta con CodigoNoEncontrada, igual que NoExiste.
    public string Codigo => Estado switch
    {
        EstadoAcceso.Concedido => "ok",
        EstadoAcceso.NoExiste => AccesoSalaService.CodigoNoEncontrada,
        EstadoAcceso.SinAcceso => "sin-acceso",
        EstadoAcceso.EstadoInvalido => "estado-invalido",
        EstadoAcceso.AunNoAbre => "aun-no-abre",
        EstadoAcceso.YaTermino => "ya-termino",
        EstadoAcceso.SalaNoGenerada => "sala-no-generada",
        _ => "sala-invalida"
    };

    public static ResultadoAcceso De(EstadoAcceso estado, string mensaje, DateTimeOffset ahora,
        DateTimeOffset? desde = null, DateTimeOffset? hasta = null) =>
        new(estado, mensaje, null, null, ahora, desde, hasta);

    public static ResultadoAcceso Concedido(string rol, string sala, DateTimeOffset ahora, DateTimeOffset desde, DateTimeOffset hasta) =>
        new(EstadoAcceso.Concedido, "", rol, sala, ahora, desde, hasta);
}
