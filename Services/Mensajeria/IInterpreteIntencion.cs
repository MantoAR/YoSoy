public enum TipoIntencion
{
    Desconocido,
    Saludo,
    VolverAlMenu,
    PedirTurno,
    VerTurnos,
    CancelarTurno,
    Faq,
    HablarConPersona,
    OpcionNumerica,
    Afirmacion,
    Negacion
}

public record Intencion(TipoIntencion Tipo, int? Numero = null);

// Separa "qué quiso decir el paciente" del flujo de la conversación.
// Hoy lo resuelve InterpreteMenu (números y palabras clave); una futura
// InterpreteIA puede reemplazarlo registrando otra implementación en DI,
// sin tocar BotReservasService.
public interface IInterpreteIntencion
{
    Intencion Interpretar(string texto);
}
