/// <summary>
/// Chats v2, Fase 1: las reglas de "¿quién responde en esta conversación?" y del interruptor de IA, en un solo lugar y sin acceso a la base. En esta fase la IA NO existe
/// (se guarda y se muestra el interruptor, pero nada responde con IA: <see cref="IaImplementada"/> es false); la Fase 3 la va a consultar desde acá y no desde otro lado.
///
/// El interruptor gobierna SOLO a la IA. El asistente de reglas (el "Bot" de Mensajeria:ModoAtencion = Asistente) no depende de él: sigue como siempre.
/// </summary>
public static class PoliticaRespuestaAutomatica
{
    public const string Persona = "Persona";
    public const string Bot = "Bot";
    public const string IA = "IA";

    /// <summary>Se pone en true recién cuando exista la IA que responde (Fase 3). Mientras sea false, nunca se informa "IA" como quien responde.</summary>
    public const bool IaImplementada = false;

    /// <summary>La IA puede responder en una conversación solo si el interruptor GLOBAL y el de la CONVERSACIÓN están prendidos. Global apagado: ningún chat responde con IA.</summary>
    public static bool IaEfectiva(bool global, bool conversacion) => global && conversacion;

    /// <summary>
    /// Quién responde hoy en la conversación: "Persona" (modo Personal, o una conversación escalada o urgente), "IA" (solo si hay IA implementada y está efectiva) o "Bot"
    /// (el asistente de reglas, modo Asistente). Una conversación cerrada en modo Asistente dice "Bot": es quien la atiende si la persona vuelve a escribir.
    /// </summary>
    public static string QuienResponde(bool modoPersonal, string estado, bool iaEfectiva, bool iaImplementada = IaImplementada)
    {
        if (modoPersonal || estado is EstadoConversacion.DerivadaHumano or EstadoConversacion.Urgente) return Persona;
        return iaImplementada && iaEfectiva ? IA : Bot;
    }
}
