// Enchufe para la etapa siguiente. NO está registrado en DI: si
// Mensajeria:CanalActivo pide "WhatsApp", la aplicación no arranca.
//
// TODO(WhatsApp): falta, como mínimo:
//  1. Envío: POST a https://graph.facebook.com/{version}/{phone-number-id}/messages
//     con el token de acceso leído de user-secrets o variables de entorno
//     (nunca de appsettings.json).
//  2. Webhook de entrada con dos endpoints:
//     - GET de verificación: responder hub.challenge solo si hub.verify_token coincide.
//     - POST de mensajes: validar la firma X-Hub-Signature-256 (HMAC-SHA256 del
//       cuerpo crudo con el App Secret) antes de procesar nada, y derivar el
//       mensaje a BotReservasService con canal "WhatsApp".
//  3. Ventana de 24 h: los mensajes que inicia el negocio (los avisos por evento
//     de NotificadorTurnos; el recordatorio no sale por WhatsApp) fuera de las 24 h desde el último mensaje del paciente
//     solo se pueden mandar con plantillas aprobadas por Meta. Hay que crear esas
//     plantillas y elegir plantilla o texto libre según la ventana.
//  4. Guardar el id de mensaje que devuelve Meta y procesar los estados de
//     entrega (sent/delivered/read/failed) que llegan por el webhook.
public class CanalWhatsAppCloud : IMessageChannel
{
    public string Nombre => "WhatsApp";

    public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto) =>
        throw new NotImplementedException("El canal WhatsApp Cloud API todavía no está implementado.");
}
