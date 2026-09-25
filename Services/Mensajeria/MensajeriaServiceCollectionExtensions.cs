using Microsoft.Extensions.Options;

public static class MensajeriaServiceCollectionExtensions
{
    // Se registra con una sola línea en Program.cs: builder.Services.AddMensajeria(builder.Configuration);
    public static IServiceCollection AddMensajeria(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<MensajeriaOptions>()
            .Bind(config.GetSection(MensajeriaOptions.Seccion))
            // Sin palabras de riesgo el bot no podría detectar una urgencia: no arrancar.
            .Validate(o => o.PalabrasRiesgo.Any(p => !string.IsNullOrWhiteSpace(p)),
                "Mensajeria:PalabrasRiesgo no puede estar vacía.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        // Canal activo elegido por configuración. CanalWhatsAppCloud existe pero
        // todavía no se registra (ver el TODO en esa clase).
        var canal = config.GetSection(MensajeriaOptions.Seccion)["CanalActivo"] ?? "Simulado";
        switch (canal)
        {
            case "Simulado":
                services.AddSingleton<IMessageChannel, CanalSimulado>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Mensajeria:CanalActivo = \"{canal}\" no está disponible. Por ahora el único canal es \"Simulado\".");
        }

        // DAOs sin estado: una instancia alcanza.
        services.AddSingleton<IMensajeChatDAO, MensajeChatDAO>();
        services.AddSingleton<IConversacionDAO, ConversacionDAO>();
        services.AddSingleton<IRecordatorioDAO, RecordatorioDAO>();
        services.AddSingleton<IPreguntaFrecuenteDAO, PreguntaFrecuenteDAO>();
        services.AddSingleton<INotificacionInternaDAO, NotificacionInternaDAO>();
        services.AddSingleton<IDatosMensajeria, DatosMensajeriaDAO>();
        services.AddSingleton<ITurnoRepositorio, TurnoRepositorioDAO>();

        services.AddSingleton<IServicioMensajes, ServicioMensajes>();
        // TurnoReglasService también aplica RN-02 en los endpoints de confirmación de turnos.
        services.AddOptions<ConsentimientoOptions>().Bind(config.GetSection(ConsentimientoOptions.Seccion));
        services.AddSingleton<TurnoReglasService>();
        services.AddSingleton<FiltroSeguridad>();
        // Para pasar a una IA conversacional alcanza con cambiar esta línea.
        services.AddSingleton<IInterpreteIntencion, InterpreteMenu>();
        services.AddSingleton<NotificadorTurnos>();
        services.AddSingleton<INotificadorTurnos>(sp => sp.GetRequiredService<NotificadorTurnos>());
        services.AddSingleton<BotReservasService>();

        services.AddSingleton<RecordatorioBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<RecordatorioBackgroundService>());

        return services;
    }
}
