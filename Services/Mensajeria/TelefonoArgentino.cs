using System.Text.RegularExpressions;

// Teléfonos de Argentina para el consentimiento de WhatsApp.
//
// Paciente.Telefono es texto libre ("011 15 5555-0001", "+54 9 11 5555-0001", "11 5555 0001"...). Para saber A QUÉ NÚMERO dio su
// consentimiento una persona (y no escribirle a otro) hace falta una forma única: el NÚMERO NACIONAL de 10 dígitos, que es el código de
// área más el abonado, sin el 0 de discado, sin el 15 de los celulares, sin el 54 del país y sin el 9 que WhatsApp le antepone. Es lo que
// le llega a whatsapp-service (que le agrega el 549 él solo).
//
// Telefono.Normalizar (el de siempre) NO se reemplaza: se queda con los últimos 10 dígitos para emparejar un mensaje que llega con un
// paciente, y no entiende el 15 ("011 15 5555-0001" queda como 1555550001, que no es el número). Acá, si el texto no se puede leer como un
// teléfono argentino CON código de área, se devuelve null en vez de adivinar.
public static class TelefonoArgentino
{
    // Lo que una persona escribe en un campo de teléfono: dígitos y separadores. Con letras u otros símbolos no es un teléfono. Dígitos son
    // los ASCII (0-9) y nada más: \d de .NET también acepta los de ancho completo o los arábigos, y el número tiene que llegar así al servicio de
    // WhatsApp y a una columna VARCHAR(10).
    private static readonly Regex Escritura = new(@"^[0-9\s+()./-]+$", RegexOptions.Compiled);

    // Buenos Aires y alrededores es el 11 (más 8 dígitos); el resto de los códigos de área empieza con 2 o con 3. Siempre 10 en total.
    private static readonly Regex Nacional = new(@"^(11[0-9]{8}|[23][0-9]{9})$", RegexOptions.Compiled);

    /// <summary>
    /// El número nacional de 10 dígitos (código de área + abonado), o null si <paramref name="entrada"/> no es un teléfono argentino posible:
    /// vacío, con letras, de otro país, sin código de área o con una cantidad de dígitos que no cierra.
    /// </summary>
    public static string? Normalizar(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada)) return null;
        var texto = entrada.Trim();
        if (!Escritura.IsMatch(texto)) return null;

        var d = new string(texto.Where(c => c is >= '0' and <= '9').ToArray());
        var conPais = texto.StartsWith('+') || d.StartsWith("00");   // "+" y "00" son los dos prefijos internacionales
        if (d.StartsWith("00")) d = d[2..];

        if (d.StartsWith("54"))
        {
            d = d[2..];
            if (d.StartsWith('0')) d = d[1..];              // "+54 011 5555-0001": el 0 sobra
        }
        else if (conPais) return null;                       // "+34 ...", "+1 ...": es de otro país
        else if (d.StartsWith('0')) d = d[1..];              // el 0 de discado nacional

        // El 9 de los celulares ("549 11 ...", "9 11 ..."): ningún código de área empieza con 9, así que si sobra es ese.
        if (d.StartsWith('9') && d.Length >= 11) d = d[1..];

        // Con el 15 de los celulares ("011 15 5555-0001") son 12 dígitos, y el 15 va justo después del código de área (2, 3 o 4 dígitos).
        if (d.Length == 12)
        {
            foreach (var area in new[] { 2, 3, 4 })
            {
                var sinQuince = d[..area] + d[(area + 2)..];
                if (d[area..(area + 2)] == "15" && Nacional.IsMatch(sinQuince)) { d = sinQuince; break; }
            }
        }

        if (!Nacional.IsMatch(d)) return null;
        if (d.Distinct().Count() == 1) return null;          // 1111111111 y compañía
        return d;
    }
}
