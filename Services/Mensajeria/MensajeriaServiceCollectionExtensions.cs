using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // TryAdd: el reloj de la aplicación es RelojDeLaClinica (AddRelojDeLaClinica, que va primero en Program.cs).
        // Esta línea solo cubre a quien use AddMensajeria sin él.
        services.TryAddSingleton(TimeProvider.System);

        // Canal activo elegido por configuración. CanalWhatsAppCloud existe pero
        // todavía no se registra (ver el TODO en esa clase).
        var canal = config.GetSection(MensajeriaOptions.Seccion)["CanalActivo"] ?? "Simulado";
        switch (canal)
        {
            case "Simulado":
                services.AddSingleton<IMessageChannel, CanalSimulado>();
                break;
            case "WhatsAppWebJs":
                // whatsapp-service solo escucha en loopback (ver whatsapp-service/README.md) —
                // nunca una URL configurable desde afuera. AddHttpClient<T> registra el canal
                // mismo como transient; se envuelve en un singleton para que todo lo que
                // inyecta IMessageChannel (NotificadorTurnos, BotReservasService, ambos
                // singleton) reciba siempre la misma instancia, como con CanalSimulado.
                services.AddHttpClient<CanalWhatsAppWebJs>((sp, http) =>
                {
                    http.BaseAddress = new Uri("http://127.0.0.1:3002/");
                    http.Timeout = TimeSpan.FromSeconds(10);
                    AgregarSecretoWhatsApp(http, sp);
                });
                services.AddSingleton<IMessageChannel>(sp => sp.GetRequiredService<CanalWhatsAppWebJs>());
                break;
            default:
                throw new InvalidOperationException(
                    $"Mensajeria:CanalActivo = \"{canal}\" no está disponible. Los valores válidos son \"Simulado\" y \"WhatsAppWebJs\".");
        }

        // WhatsAppAdminService (pantalla de WhatsApp del Configurador: estado/QR/desvincular)
        // se registra siempre, sin condición con CanalActivo — a diferencia del canal de
        // envío de arriba, tiene que poder usarse para vincular el número ANTES de activar
        // "WhatsAppWebJs". La URL sale de MensajeriaOptions (configurable, a diferencia de la
        // de CanalWhatsAppWebJs) para poder apuntar los tests a un listener falso.
        services.AddHttpClient<WhatsAppAdminService>((sp, http) =>
        {
            var opciones = sp.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue;
            http.BaseAddress = new Uri(opciones.WhatsAppServiceUrl);
            http.Timeout = TimeSpan.FromSeconds(10);
            AgregarSecretoWhatsApp(http, sp);
        });

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

    // whatsapp-service exige este secreto en /enviar, /estado y /desvincular (ver
    // whatsapp-service/index.js) — mismo valor que valida el webhook entrante
    // (Endpoints/MensajeriaEndpoints.cs). Sin esto, CanalWhatsAppWebJs.EnviarAsync y
    // WhatsAppAdminService recibirían 401 en cada pedido.
    private static void AgregarSecretoWhatsApp(HttpClient http, IServiceProvider sp)
    {
        var secreto = sp.GetRequiredService<IConfiguration>()["Mensajeria:WhatsApp:Secreto"];
        if (!string.IsNullOrEmpty(secreto)) http.DefaultRequestHeaders.Add("X-Shared-Secret", secreto);
    }
}
