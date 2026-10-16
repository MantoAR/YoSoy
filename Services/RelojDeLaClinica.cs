using Microsoft.Extensions.DependencyInjection.Extensions;

// La hora de la clínica.
//
// Turno.Fecha y Turno.Hora (y InformeAdmision.FechaEntrevista) se guardan SIN zona: son la hora de pared de la clínica.
// Para saber si un horario "ya pasó", qué recordatorios tocan o cuándo abre una sala, el sistema necesita saber qué hora
// es ahora EN LA CLÍNICA. Antes lo sacaba de la zona del sistema operativo del servidor (TimeProvider.System): con la
// VPS en UTC, todo andaba tres horas corrido, y las reglas solo daban bien si alguien se acordaba de fijarle la zona al
// servidor.
//
// Ahora la zona es un dato explícito de la configuración y no depende de dónde corra la API. Por orden:
//
//   1. Turnos:ZonaHoraria (variable de entorno Turnos__ZonaHoraria): identificador IANA ("America/Argentina/Buenos_Aires")
//      o de Windows ("Argentina Standard Time"). Si está escrita y no existe en este servidor, la API NO arranca: es
//      mejor enterarse al desplegar que ofrecer horarios corridos sin que nadie lo note.
//   2. Video:ZonaHoraria: lo que ya existía y significa lo mismo ("la zona en la que están escritas Turno.Fecha y
//      Turno.Hora"). Se respeta para que quien ya la tiene configurada no cambie nada. Si está mal escrita se ignora
//      (AccesoSalaService lo avisa en el log), igual que antes.
//   3. America/Argentina/Buenos_Aires.
//
// Con esa zona se arma UN solo TimeProvider para toda la aplicación (RelojDeLaClinica): turnos, bot, recordatorios,
// consentimiento y ventana de video lo reciben por inyección, así que "qué hora es" se decide en un solo lugar.
public static class ZonaHorariaDeLaClinica
{
    public const string Clave = "Turnos:ZonaHoraria";
    public const string ClaveVideo = "Video:ZonaHoraria";
    public const string PorDefecto = "America/Argentina/Buenos_Aires";

    // El mismo huso con el identificador de Windows: en una máquina de desarrollo sin ICU el IANA no se resuelve.
    private const string PorDefectoEnWindows = "Argentina Standard Time";

    public static TimeZoneInfo Resolver(IConfiguration config) => Elegir(config[Clave], config[ClaveVideo]);

    /// <summary>La zona según los dos valores de configuración (cualquiera puede faltar), con las reglas de arriba.</summary>
    public static TimeZoneInfo Elegir(string? turnos, string? video = null)
    {
        if (!string.IsNullOrWhiteSpace(turnos))
            return Buscar(turnos.Trim()) ?? throw new InvalidOperationException(
                $"{Clave} = \"{turnos.Trim()}\" no es una zona horaria que exista en este servidor. " +
                $"Usá un identificador como \"{PorDefecto}\" o dejala vacía para usar esa.");

        if (!string.IsNullOrWhiteSpace(video) && Buscar(video.Trim()) is { } zonaDeVideo)
            return zonaDeVideo;

        return Buscar(PorDefecto) ?? Buscar(PorDefectoEnWindows) ?? throw new InvalidOperationException(
            $"Este servidor no tiene la zona horaria \"{PorDefecto}\" (¿falta el paquete tzdata?). " +
            $"Instalala o configurá {Clave} con una zona que exista.");
    }

    /// <summary>La zona con ese identificador, o null si no existe en este servidor.</summary>
    public static TimeZoneInfo? Buscar(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }
}

/// <summary>
/// El instante real (el del reloj del servidor, sin ninguna conversión) con la zona horaria de la clínica como "local".
/// GetLocalNow() es la hora de pared de la clínica, sea cual sea la zona del sistema operativo.
/// </summary>
public sealed class RelojDeLaClinica : TimeProvider
{
    private readonly TimeProvider _origen;
    private readonly TimeZoneInfo _zona;

    public RelojDeLaClinica(TimeZoneInfo zona, TimeProvider? origen = null)
    {
        _zona = zona ?? throw new ArgumentNullException(nameof(zona));
        _origen = origen ?? TimeProvider.System;
    }

    public override TimeZoneInfo LocalTimeZone => _zona;
    public override DateTimeOffset GetUtcNow() => _origen.GetUtcNow();
    public override long GetTimestamp() => _origen.GetTimestamp();
    public override long TimestampFrequency => _origen.TimestampFrequency;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        _origen.CreateTimer(callback, state, dueTime, period);
}

public static class RelojDeLaClinicaServiceCollectionExtensions
{
    // Se registra con una sola línea en Program.cs, ANTES de AddMensajeria y AddVideo (los dos usan TryAdd, así que el
    // que llega primero manda): builder.Services.AddRelojDeLaClinica(builder.Configuration);
    //
    // La zona se resuelve cuando se crea el reloj (la primera vez que alguien lo pide: Program.cs lo pide apenas arranca,
    // así que una zona mal escrita frena el arranque) y no al registrarlo, para que vea la configuración definitiva.
    public static IServiceCollection AddRelojDeLaClinica(this IServiceCollection services, IConfiguration config)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(_ => new RelojDeLaClinica(ZonaHorariaDeLaClinica.Resolver(config)));
        return services;
    }
}
