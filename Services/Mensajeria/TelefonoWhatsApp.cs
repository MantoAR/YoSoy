/// <summary>
/// Cómo se MUESTRA el teléfono de una conversación de WhatsApp: "+54 9 351 206-6471" (formato internacional de WhatsApp para un celular argentino). Solo formato: no cambia ni
/// reescribe nada de lo guardado.
///
/// Lo que hay guardado en Conversacion.Telefono son los ÚLTIMOS 10 DÍGITOS del número (Telefono.Normalizar): sin el 54, sin el 9 de los celulares y sin el 15; el resto se tira.
/// Por eso, de un número guardado no se puede saber con certeza que es argentino: uno de otro país también queda en 10 dígitos (+1 212 555 1234 queda "2125551234"). Lo que se hace:
///  · Sin el número original (hoy no se guarda): se lo muestra como argentino SOLO si tiene 10 dígitos y empieza con un código de área que figura en la lista oficial de ENACOM
///    (AreasArgentinas). Si no, se muestra tal cual, sin "+54 9" ni agrupar, antes que mostrar un país que no es. Un número extranjero cuyo comienzo coincide con un código de área
///    argentino (por ejemplo +1 351...) seguiría mostrándose como argentino: es el límite que cierra guardar el número completo (ver docs/DEPLOY_CHATS_VISTA_PRODUCCION.md).
///  · Con el número original (el JID de WhatsApp sin el @, p. ej. "5493512066471"): decide por él. "549" + 10 o "54" + 10 dígitos es argentino; cualquier otro prefijo se muestra
///    como "+<número>" sin agrupar, porque no se conocen los códigos de otros países. Nadie lo pasa todavía.
/// </summary>
public static class TelefonoWhatsApp
{
    private const string Pais = "+54 9";

    public static string Formatear(string? guardado, string? original = null)
    {
        var o = SoloDigitos(original);
        if (o.Length > 0)
        {
            var nacional = NacionalDe(o);
            return nacional is null ? "+" + o : Argentino(nacional);
        }

        var g = SoloDigitos(guardado);
        if (g.Length == 0) return (guardado ?? "").Trim();
        // Si por algún camino llegara un número con el prefijo de país, se lo trata igual que el original.
        if (NacionalDe(g) is { } conPais) return Argentino(conPais);
        if (g.Length == 10 && AreasArgentinas.LargoDelCodigo(g) is not null) return Argentino(g);
        return g;
    }

    /// <summary>Los 10 dígitos de un número argentino con su prefijo de país ("549" + 10 o "54" + 10), o null si no lo tiene.</summary>
    private static string? NacionalDe(string digitos)
    {
        if (digitos.Length == 13 && digitos.StartsWith("549", StringComparison.Ordinal)) return digitos[3..];
        if (digitos.Length == 12 && digitos.StartsWith("54", StringComparison.Ordinal)) return digitos[2..];
        return null;
    }

    private static string Argentino(string nacional)
    {
        if (AreasArgentinas.LargoDelCodigo(nacional) is not int largo) return $"{Pais} {nacional}";   // viene como argentino pero el código no existe: se muestra sin agrupar
        var area = nacional[..largo];
        var abonado = nacional[largo..];
        return $"{Pais} {area} {abonado[..^4]}-{abonado[^4..]}";
    }

    private static string SoloDigitos(string? texto) => new((texto ?? "").Where(c => c is >= '0' and <= '9').ToArray());
}
