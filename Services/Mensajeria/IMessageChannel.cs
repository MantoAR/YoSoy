// "Codigo" es el motivo corto con el que el canal rechazó el envío, si lo tiene (whatsapp-service lo manda en "codigo": por ejemplo
// sin_consentimiento o baja). Sirve para que NotificadorTurnos distinga un aviso que la persona no aceptó (se omite, no se reintenta) de una falla.
public record ResultadoEnvio(bool Exito, string? IdExterno = null, string? Error = null, string? Codigo = null);

// Lo que un canal necesita saber además del texto y el destino.
//  · OptIn: el paciente aceptó recibir WhatsApp (ConsentimientoWhatsApp vigente). Solo se pone en true con esa aceptación; el canal real se lo
//    avisa a whatsapp-service con "optIn": true. Sin eso, whatsapp-service le manda solo a quien le escribió primero al número de la clínica.
//  · IniciaElNegocio: el mensaje NO responde a nada que la persona haya escrito: lo manda la clínica por un hecho (los avisos de turno de
//    NotificadorTurnos). Es la única excepción a la regla de ServicioMensajes: un mensaje automático (del asistente o del sistema) solo sale
//    dentro de una conversación en la que la persona ya escribió. Por defecto es false: todo mensaje automático nuevo queda sujeto a la regla.
public record OpcionesEnvio(bool OptIn = false, bool IniciaElNegocio = false)
{
    public static readonly OpcionesEnvio Ninguna = new();
}

// Canal por el que sale un mensaje hacia el paciente (chat simulado, WhatsApp,
// y a futuro Instagram/Facebook). El historial no es responsabilidad del canal:
// lo guarda ServicioMensajes en MensajeChat antes de entregar, sea cual sea el canal.
public interface IMessageChannel
{
    string Nombre { get; }
    Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto);

    // Con opciones. Un canal que no las necesita (el simulado) no implementa esto y las ignora; CanalBaileys sí las usa.
    Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto, OpcionesEnvio opciones) => EnviarAsync(telefonoDestino, texto);
}
