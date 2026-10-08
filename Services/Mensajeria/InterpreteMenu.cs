// Intérprete determinístico: números de opción y palabras clave simples.
// No sabe en qué paso está la conversación; eso lo resuelve BotReservasService
// (por ejemplo, qué significa "3" depende del menú que se mostró).
public class InterpreteMenu : IInterpreteIntencion
{
    private static readonly string[] PalabrasMenu     = { "menu", "inicio", "volver", "atras" };
    private static readonly string[] PalabrasSaludo   = { "hola", "buenas", "buen dia", "buenos dias", "buenas tardes", "buenas noches" };
    private static readonly string[] PalabrasPersona  = { "persona", "humano", "humana", "operador", "operadora", "secretaria", "recepcion" };
    private static readonly string[] PalabrasCancelar = { "cancelar", "cancelo", "cancela", "anular", "anulo" };
    private static readonly string[] PalabrasVer      = { "mis turnos", "mi turno", "ver turnos", "ver mis", "proximo turno", "proximos turnos" };
    private static readonly string[] PalabrasPedir    = { "pedir", "sacar", "reservar", "solicitar", "agendar", "quiero", "necesito", "nuevo" };
    private static readonly string[] PalabrasFaq      = { "pregunta", "preguntas", "faq", "frecuentes", "informacion" };
    private static readonly string[] Afirmaciones     = { "si", "dale", "ok", "confirmo", "de acuerdo", "claro", "perfecto" };
    private static readonly string[] Negaciones       = { "no", "nop", "no gracias" };

    public Intencion Interpretar(string texto)
    {
        var t = TextoNormalizado.Normalizar(texto);
        if (t.Length == 0) return new Intencion(TipoIntencion.Desconocido);

        // Solo un número corto: una opción de menú. 0 siempre vuelve al menú.
        // (Un DNI tiene 7 u 8 dígitos y no entra acá.)
        if (t.Length <= 2 && t.All(char.IsDigit))
        {
            var n = int.Parse(t);
            return n == 0 ? new Intencion(TipoIntencion.VolverAlMenu) : new Intencion(TipoIntencion.OpcionNumerica, n);
        }

        if (Afirmaciones.Contains(t)) return new Intencion(TipoIntencion.Afirmacion);
        if (Negaciones.Contains(t))   return new Intencion(TipoIntencion.Negacion);

        // Orden de prioridad: si pide una persona, eso gana sobre cualquier otra cosa.
        if (ContieneAlguna(t, PalabrasPersona))  return new Intencion(TipoIntencion.HablarConPersona);
        if (ContieneAlguna(t, PalabrasCancelar)) return new Intencion(TipoIntencion.CancelarTurno);
        if (ContieneAlguna(t, PalabrasVer))      return new Intencion(TipoIntencion.VerTurnos);
        if (ContienePalabra(t, "turno") && ContieneAlguna(t, PalabrasPedir))
            return new Intencion(TipoIntencion.PedirTurno);
        if (ContieneAlguna(t, PalabrasFaq))      return new Intencion(TipoIntencion.Faq);
        if (ContieneAlguna(t, PalabrasMenu))     return new Intencion(TipoIntencion.VolverAlMenu);
        if (ContieneAlguna(t, PalabrasSaludo))   return new Intencion(TipoIntencion.Saludo);

        return new Intencion(TipoIntencion.Desconocido);
    }

    // Coincidencia por palabra o frase completa: "turnos" no dispara "turno" por accidente
    // en frases, pero "mis turnos" sí coincide como frase.
    private static bool ContieneAlguna(string texto, string[] frases) =>
        frases.Any(f => ContienePalabra(texto, f));

    private static bool ContienePalabra(string texto, string frase) =>
        $" {texto} ".Contains($" {frase} ");
}
