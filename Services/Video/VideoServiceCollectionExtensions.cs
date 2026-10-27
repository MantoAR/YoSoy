using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class VideoServiceCollectionExtensions
{
    // Se registra con una sola línea en Program.cs: builder.Services.AddVideo(builder.Configuration);
    // No valida nada al arrancar: sin Video__SignalingSecreto la API levanta igual y /video/acceso contesta 503.
    public static IServiceCollection AddVideo(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<VideoOptions>().Bind(config.GetSection(VideoOptions.Seccion));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IOrigenSalaRepositorio, OrigenSalaDAO>();
        services.AddSingleton<IAuditoriaVideo, AuditoriaVideo>();
        services.AddSingleton<AccesoSalaService>();
        services.AddSingleton<TokenSalaService>();
        services.AddSingleton<TurnCredentialsService>();

        // Consentimiento por sesión para la lectura de emociones (PR 6): reglas, cifrado en reposo, guardia de lo que se
        // guarda, auditoría y purga por retención. La purga arranca deshabilitada (Video:Emociones:PurgaHabilitada).
        services.AddSingleton<IConsentimientoVideoRepositorio, ConsentimientoVideoDAO>();
        services.AddSingleton<ITranscripcionSesionRepositorio, TranscripcionSesionDAO>();
        services.AddSingleton<IEmocionesPurgaRepositorio, EmocionesPurgaDAO>();
        services.AddSingleton<IAuditoriaEmociones, AuditoriaEmociones>();
        services.AddSingleton<IEmocionesCifrado, EmocionesCifradoService>();
        services.AddSingleton<ITextoConsentimientoVideo, TextoConsentimientoVideo>();
        services.AddSingleton<ConsentimientoVideoService>();
        services.AddSingleton<GuardiaEmocionesService>();
        services.AddSingleton<VistaSesionesClinicas>();
        services.AddSingleton<PurgaEmocionesBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<PurgaEmocionesBackgroundService>());
        services.AddSingleton<ITranscriptorLocal, TranscriptorLocalProceso>();
        services.AddSingleton<TranscripcionBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<TranscripcionBackgroundService>());

        // Rate limit propio de /video/acceso, por IP real (RemoteIpAddress ya es la del cliente detrás de Caddy
        // gracias a UseForwardedHeaders), ventana fija de un minuto, en memoria: mismo estilo que "publico" y las
        // demás de Program.cs. El tope sale de Video:LimitePorMinuto. Cada sesión pide un acceso (o dos o tres si
        // reintenta); 20 por minuto sobra para una oficina entera detrás de la misma IP.
        services.AddRateLimiter(opt => opt.AddPolicy("video", httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = Math.Clamp(httpContext.RequestServices.GetRequiredService<IOptionsMonitor<VideoOptions>>().CurrentValue.LimitePorMinuto, 1, 10000),
                QueueLimit = 0
            })));

        // Política aparte para el consentimiento y el permiso de emociones: la pantalla del profesional pregunta cada
        // pocos segundos mientras dura la sesión (20 por minuto por persona), mucho más que un acceso. El limitador corre
        // antes de la autenticación, así que se particiona por IP real como las demás. Tope: Video:Emociones:LimitePorMinuto.
        services.AddRateLimiter(opt => opt.AddPolicy("video-consentimiento", httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = Math.Clamp(httpContext.RequestServices.GetRequiredService<IOptionsMonitor<VideoOptions>>().CurrentValue.Emociones.LimitePorMinuto, 1, 10000),
                QueueLimit = 0
            })));

        // Política aparte para el permiso de grabación y la subida de fragmentos de audio: la pantalla del profesional
        // pregunta el permiso cada pocos segundos, y además sube un fragmento de audio por pista cada pocos segundos
        // mientras grava. Mismo estilo que "video-consentimiento". Tope: Video:Transcripcion:LimitePorMinuto.
        services.AddRateLimiter(opt => opt.AddPolicy("video-grabacion", httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = Math.Clamp(httpContext.RequestServices.GetRequiredService<IOptionsMonitor<VideoOptions>>().CurrentValue.Transcripcion.LimitePorMinuto, 1, 10000),
                QueueLimit = 0
            })));

        return services;
    }
}
