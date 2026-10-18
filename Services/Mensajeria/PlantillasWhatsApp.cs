// Los textos que salen POR WHATSAPP sin que la persona los haya pedido: los avisos de un turno (lo recibimos, se propuso, se confirmó, se
// canceló, recordatorio). Es el contenido del consentimiento: "cada aviso dice el día y la hora de tu turno y nada más sobre tu atención".
//
// REGLA: solo el aviso, el día y la hora. Nada clínico ni que revele qué atención recibe la persona: ni el nombre del profesional, ni la
// especialidad, ni el motivo de la consulta, ni la modalidad. Todo eso lo ve la persona dentro de su panel de YoSoy (por eso los avisos que
// lo necesitan traen el link). Un chat de WhatsApp queda en el teléfono, en las notificaciones de la pantalla bloqueada y en copias de
// seguridad que no son de la clínica.
//
// La garantía está en las FIRMAS: cada plantilla recibe el momento del turno y, si hace falta, el link de ingreso. No hay por dónde pasarle un
// nombre, una especialidad ni un motivo (PlantillasWhatsAppTests lo vigila por reflexión y con el notificador de punta a punta). Una plantilla
// nueva con otra firma hace fallar esa prueba: antes de agregar un dato hay que decidir, a conciencia, que puede salir por WhatsApp.
//
// Lo que NO está acá: las respuestas del asistente cuando la persona le escribe primero (BotReservasService: ahí pidió ella ver o pedir turnos,
// y el menú necesita decir con quién), y los avisos de la campanita y del mail, que tienen el texto completo (la campanita es del panel,
// con sesión). Los avisos internos al equipo no salen por WhatsApp.
public static class PlantillasWhatsApp
{
    /// <summary>"Pediste un turno": lo recibimos y queda pendiente.</summary>
    public static string SolicitudRecibida(DateTime momento) =>
        $"Recibimos tu solicitud de turno para el {Formato.FechaHora(momento)}. Queda pendiente de confirmación.";

    /// <summary>El equipo propuso un turno y hay que confirmarlo en el panel.</summary>
    public static string TurnoPropuesto(DateTime momento, string urlIngreso) =>
        $"Te propusieron un turno para el {Formato.FechaHora(momento)}. Para confirmarlo, ingresá a tu panel en la web: {urlIngreso}";

    public static string TurnoConfirmado(DateTime momento) =>
        $"Tu turno del {Formato.FechaHora(momento)} quedó confirmado.";

    public static string TurnoCancelado(DateTime momento) =>
        $"Tu turno del {Formato.FechaHora(momento)} fue cancelado. Si querés pedir otro, escribí \"menu\" por acá o ingresá a la web.";

    /// <summary>
    /// El recordatorio de 24 horas. Lo contesta el asistente ("1" confirma, "2" avisa que no puede) y trae cómo darse de baja de estos avisos.
    /// El detalle (con quién, cómo es el turno) está en el panel.
    /// </summary>
    public static string Recordatorio(DateTime momento, string urlIngreso) =>
        $"Recordatorio: tenés turno el {Formato.FechaHora(momento)}.\nPara ver los detalles o ingresar: {urlIngreso}\n\n" +
        "Respondé 1 para confirmar tu asistencia o 2 si no podés asistir. Para dejar de recibir estos avisos, respondé BAJA.";
}
