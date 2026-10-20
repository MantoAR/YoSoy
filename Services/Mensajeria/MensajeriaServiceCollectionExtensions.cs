using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
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

        // El modo de atención se valida acá, al armar el host (igual que CanalActivo, más abajo): un valor mal escrito frena el arranque con un mensaje claro, porque decide
        // si el asistente contesta o no y no puede caer en silencio en uno de los dos. (Un cambio en caliente del archivo con un valor inválido no corta la aplicación:
        // MensajeriaOptions.EsModoPersonal da false y atiende el asistente, como siempre.)
        var modoAtencion = config.GetSection(MensajeriaOptions.Seccion)["ModoAtencion"];
        if (modoAtencion is not null && !ModosAtencion.EsValido(modoAtencion))
            throw new InvalidOperationException(
                $"Mensajeria:ModoAtencion = \"{modoAtencion}\" no es válido. Los valores válidos son \"Asistente\" (como siempre) y \"Personal\" (el chat es para el personal). " +
                "Se cambia con Mensajeria__ModoAtencion en el .env del servidor.");

        // TryAdd: el reloj de la aplicación es RelojDeLaClinica (AddRelojDeLaClinica, que va primero en Program.cs).
        // Esta línea solo cubre a quien use AddMensajeria sin él.
        services.TryAddSingleton(TimeProvider.System);

        // Canal activo elegido por configuración: Simulado (por defecto) o WhatsApp por whatsapp-service ("Baileys", el único motor de
        // WhatsApp). "WhatsAppWebJs" ya no existe (se sacó whatsapp-web.js): con ese valor la aplicación no arranca, y el mensaje dice qué
        // poner. CanalWhatsAppCloud existe pero todavía no se registra (ver el TODO en esa clase).
        var canal = config.GetSection(MensajeriaOptions.Seccion)["CanalActivo"] ?? "Simulado";
        switch (canal)
        {
            case "Simulado":
                services.AddSingleton<IMessageChannel, CanalSimulado>();
                break;
            case "Baileys":
                // CanalBaileys habla con whatsapp-service por HTTP (/enviar, /estado, /desvincular) y no sabe nada de la librería.
                //
                // whatsapp-service solo escucha en loopback (ver whatsapp-service/README.md) —
                // nunca una URL configurable desde afuera. AddHttpClient<T> registra el canal
                // mismo como transient; se envuelve en un singleton para que todo lo que
                // inyecta IMessageChannel (NotificadorTurnos, BotReservasService, ambos
                // singleton) reciba siempre la misma instancia, como con CanalSimulado.
                services.AddHttpClient<CanalBaileys>((sp, http) =>
                {
                    http.BaseAddress = new Uri("http://127.0.0.1:3002/");
                    http.Timeout = TimeSpan.FromSeconds(10);
                    AgregarSecretoWhatsApp(http, sp);
                });
                services.AddSingleton<IMessageChannel>(sp => sp.GetRequiredService<CanalBaileys>());
                break;
            case "WhatsAppWebJs":
                // El valor del motor que se sacó: se rechaza con un mensaje propio (un servidor que todavía lo tiene en el .env tiene que saber qué cambiar).
                throw new InvalidOperationException(
                    "Mensajeria:CanalActivo = \"WhatsAppWebJs\" ya no existe: WhatsApp Web JS (whatsapp-web.js) se sacó y el único motor de WhatsApp es Baileys. " +
                    "Poné Mensajeria__CanalActivo=Baileys (o \"Simulado\", si no querés que salga nada por WhatsApp) en el .env del servidor. " +
                    "Los valores válidos son \"Simulado\" y \"Baileys\".");
            default:
                throw new InvalidOperationException(
                    $"Mensajeria:CanalActivo = \"{canal}\" no está disponible. Los valores válidos son \"Simulado\" y \"Baileys\".");
        }

        // WhatsAppAdminService (pantalla de WhatsApp del Configurador: estado/QR/desvincular)
        // se registra siempre, sin condición con CanalActivo — a diferencia del canal de
        // envío de arriba, tiene que poder usarse para vincular el número ANTES de activar
        // "Baileys". La URL sale de MensajeriaOptions (configurable, a diferencia de la
        // de CanalBaileys) para poder apuntar los tests a un listener falso.
        services.AddSingleton<EnfriamientoQr>();
        services.AddHttpClient<WhatsAppAdminService>((sp, http) =>
        {
            var opciones = sp.GetRequiredService<IOptionsMonitor<MensajeriaOptions>>().CurrentValue;
            http.BaseAddress = new Uri(opciones.WhatsAppServiceUrl);
            http.Timeout = TimeSpan.FromSeconds(10);
            AgregarSecretoWhatsApp(http, sp);
        });

        // Las columnas de la migración 17 (id y tipo de los mensajes, nombre del contacto): los DAOs de mensajes y conversaciones solo las tocan si esta puerta lo permite
        // (modo Personal y la base con la migración aplicada). En el modo Asistente, nunca.
        services.AddSingleton<ISondaDeColumnasChatPersonal, SondaSqlDeColumnasChatPersonal>();
        services.AddSingleton<IColumnasChatPersonal, ColumnasChatPersonal>();

        // DAOs sin estado: una instancia alcanza.
        services.AddSingleton<IMensajeChatDAO, MensajeChatDAO>();
        services.AddSingleton<IConversacionDAO, ConversacionDAO>();
        services.AddSingleton<IRecordatorioDAO, RecordatorioDAO>();
        services.AddSingleton<IPreguntaFrecuenteDAO, PreguntaFrecuenteDAO>();
        services.AddSingleton<INotificacionInternaDAO, NotificacionInternaDAO>();
        services.AddSingleton<IDatosMensajeria, DatosMensajeriaDAO>();
        services.AddSingleton<ITurnoRepositorio, TurnoRepositorioDAO>();

        // Consentimiento de los avisos por WhatsApp (tabla ConsentimientoWhatsApp): las reglas, el texto versionado y la auditoría. El mismo
        // servicio responde a NotificadorTurnos (¿qué le mando a este paciente?) y a BotReservasService (llegó una BAJA).
        services.AddSingleton<IConsentimientoWhatsAppRepositorio, ConsentimientoWhatsAppDAO>();
        services.AddSingleton<IAuditoriaConsentimientoWhatsApp, AuditoriaConsentimientoWhatsApp>();
        services.AddSingleton<ITextoConsentimientoWhatsApp, TextoConsentimientoWhatsApp>();
        services.AddSingleton<ConsentimientoWhatsAppService>();
        services.AddSingleton<ContactoWhatsApp>();
        services.AddSingleton<IPoliticaEnvioWhatsApp>(sp => sp.GetRequiredService<ConsentimientoWhatsAppService>());
        services.AddSingleton<IBajaDeWhatsApp>(sp => sp.GetRequiredService<ConsentimientoWhatsAppService>());
        // Límite propio de /consentimiento-whatsapp (incluye el texto, que es público): por IP real, ventana de un minuto, en memoria, como las demás.
        // Una persona abre la pantalla, lee y acepta o revoca: 30 por minuto sobra para una oficina entera detrás de la misma IP.
        services.AddRateLimiter(opt => opt.AddPolicy(ConsentimientoWhatsAppEndpoints.PoliticaDeLimite, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                factory: _ => new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(1), PermitLimit = 30, QueueLimit = 0 })));

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
        // El chat para el personal (Mensajeria:ModoAtencion = "Personal") y la puerta que elige, en cada mensaje, quién atiende: el asistente o el personal.
        services.AddSingleton<AtencionPersonalService>();
        services.AddSingleton<EntrantesVistos>();
        services.AddSingleton<RecepcionDeMensajes>();

        services.AddSingleton<RecordatorioBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<RecordatorioBackgroundService>());

        return services;
    }

    // whatsapp-service exige este secreto en /enviar, /estado y /desvincular (ver
    // whatsapp-service/index.js) — mismo valor que valida el webhook entrante
    // (Endpoints/MensajeriaEndpoints.cs). Sin esto, CanalBaileys.EnviarAsync y
    // WhatsAppAdminService recibirían 401 en cada pedido.
    private static void AgregarSecretoWhatsApp(HttpClient http, IServiceProvider sp)
    {
        var secreto = sp.GetRequiredService<IConfiguration>()["Mensajeria:WhatsApp:Secreto"];
        if (!string.IsNullOrEmpty(secreto)) http.DefaultRequestHeaders.Add("X-Shared-Secret", secreto);
    }
}
