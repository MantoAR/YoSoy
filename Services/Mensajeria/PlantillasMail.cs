/// <summary>Un mail de aviso de turno: el asunto y el cuerpo, los dos hechos por una plantilla de PlantillasMail.</summary>
public sealed record MailDeTurno(string Asunto, string Cuerpo);

// Los mails a un paciente por un turno (el recordatorio y los avisos por evento: solicitud recibida, turno propuesto, confirmado y cancelado): el asunto y el cuerpo
// dicen el día y la hora, y un link neutro a la web, y nada más. Un mail viaja por servidores de terceros y queda en una casilla que no es de la clínica (puede ser
// compartida o estar abierta en cualquier pantalla), y el asunto se ve en la bandeja sin abrirlo: no dicen con quién es el turno ni cómo es.
//
// REGLA: solo el aviso, el día y la hora, y el link para ingresar. Nada clínico ni que revele qué atención recibe la persona: ni el nombre del profesional, ni la
// especialidad, ni el motivo de la consulta, ni la modalidad. Todo eso lo ve la persona dentro de su panel de YoSoy, con su sesión (por eso los avisos que lo
// necesitan traen el link) y en la campanita, que es del panel y conserva el texto completo.
//
// La garantía está en las FIRMAS, igual que en PlantillasWhatsApp: cada plantilla recibe el momento del turno y, si hace falta, el link de ingreso. No hay por
// dónde pasarle un nombre, una especialidad ni un motivo (PlantillasMailTests lo vigila por reflexión y con el notificador de punta a punta). Una plantilla nueva
// con otra firma hace fallar esa prueba: antes de agregar un dato hay que decidir, a conciencia, que puede salir por mail.
//
// Cada aviso del NotificadorTurnos TRAE su plantilla de mail (el parámetro "mail" de AvisarPaciente no es opcional): así, aunque un día un aviso por evento empiece
// a salir también por mail, no hay forma de que el mail lleve el texto de la campanita.
public static class PlantillasMail
{
    private const string Marca = "YoSoy – ";

    /// <summary>El recordatorio de un turno confirmado, 24 horas antes. Sale por mail y por la campanita (nunca por WhatsApp).</summary>
    public static MailDeTurno Recordatorio(DateTime momento, string urlIngreso) => new(
        Marca + "Recordatorio de turno",
        $"Recordatorio: tenés un turno el {Formato.FechaHora(momento)}.\nPara ver los detalles o ingresar: {urlIngreso}\n\nSi no vas a poder asistir, avisanos desde la web.");

    /// <summary>"Pediste un turno": lo recibimos y queda pendiente.</summary>
    public static MailDeTurno SolicitudRecibida(DateTime momento) => new(
        Marca + "Solicitud de turno recibida",
        $"Recibimos tu solicitud de turno para el {Formato.FechaHora(momento)}. Queda pendiente de confirmación.");

    /// <summary>El equipo propuso un turno y hay que confirmarlo en el panel.</summary>
    public static MailDeTurno TurnoPropuesto(DateTime momento, string urlIngreso) => new(
        Marca + "Te propusieron un turno",
        $"Te propusieron un turno para el {Formato.FechaHora(momento)}. Para confirmarlo, ingresá a tu panel en la web: {urlIngreso}");

    public static MailDeTurno TurnoConfirmado(DateTime momento) => new(
        Marca + "Turno confirmado",
        $"Tu turno del {Formato.FechaHora(momento)} quedó confirmado.");

    public static MailDeTurno TurnoCancelado(DateTime momento, string urlIngreso) => new(
        Marca + "Turno cancelado",
        $"Tu turno del {Formato.FechaHora(momento)} fue cancelado. Si querés pedir otro, ingresá a la web: {urlIngreso}");
}
