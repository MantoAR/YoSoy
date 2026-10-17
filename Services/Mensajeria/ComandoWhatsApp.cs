using System.Text;
using System.Text.RegularExpressions;

// Comandos que una persona le escribe al número de la clínica por WhatsApp. Por ahora, uno: pedir la BAJA.
//
// El servicio de WhatsApp (whatsapp-service/privacidad.js, interpretarComando) ya respeta el pedido: deja de mandarle mensajes a ese número
// hasta que escriba ALTA. Pero no se lo cuenta a la API, y el perfil del paciente seguiría diciendo "activado". Por eso la API reconoce el
// mismo pedido y revoca la aceptación (ConsentimientoWhatsAppService.RegistrarBaja).
//
// Las palabras y la forma de compararlas son LAS MISMAS que las del servicio: el mensaje ENTERO tiene que ser una de ellas, sin distinguir
// mayúsculas, tildes ni signos. "cancelar" NO está (en el bot es cancelar un turno) y "baja" metida en una frase ("el turno se dio de baja")
// tampoco cuenta. Una prueba (ComandoWhatsAppTests) corre el código del servicio con muchos textos y compara.
public static class ComandoWhatsApp
{
    public static readonly IReadOnlySet<string> PalabrasDeBaja = new HashSet<string>(StringComparer.Ordinal)
    {
        "baja", "stop", "parar", "basta", "no molestar", "desuscribir", "desuscribirme", "dar de baja", "darme de baja",
        "no quiero recibir mensajes", "no quiero mas mensajes"
    };

    public static bool EsBaja(string? texto) => PalabrasDeBaja.Contains(Normalizar(texto));

    // Igual que normalizarComando del servicio: sin tildes (se sacan las marcas combinadas), en minúsculas, todo lo que no sea a-z, 0-9 o
    // espacio pasa a espacio, y los espacios se juntan.
    public static string Normalizar(string? texto)
    {
        var sinMarcas = new StringBuilder();
        foreach (var c in (texto ?? "").Normalize(NormalizationForm.FormD))
            if (c < '\u0300' || c > '\u036f') sinMarcas.Append(c);

        var soloAscii = Regex.Replace(sinMarcas.ToString().ToLowerInvariant(), @"[^a-z0-9\s]", " ");
        return Regex.Replace(soloAscii, @"\s+", " ").Trim();
    }
}
