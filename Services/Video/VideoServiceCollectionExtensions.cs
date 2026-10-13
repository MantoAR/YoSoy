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

        return services;
    }
}
