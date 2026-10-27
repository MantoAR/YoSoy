using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

public enum ResultadoDescifrado { Ok, NoCifrado, SinClave, ClaveDesconocida, Alterado }

// Cifrado en reposo de las emociones guardadas (SesionClinica.HistorialEmociones).
//
// Por qué en la aplicación y no en la base: SQL Server Express no tiene TDE ni cifra los backups (tabla de ediciones
// de Microsoft), y Always Encrypted necesita un almacén de claves que en este servidor no hay. Cifrando acá, lo que
// queda en la base y en sus backups ya es ilegible sin la clave.
//
// Mecanismo: AES-256-GCM (System.Security.Cryptography.AesGcm, el que trae .NET: no se agrega ningún paquete). Cada
// valor lleva su propio nonce de 12 bytes al azar y una etiqueta de 16 bytes que detecta cualquier alteración. Los
// "datos asociados" (el id de la fila) entran en la etiqueta: un dato copiado a otra fila no se puede descifrar.
//
// Formato guardado:  ys1.<idClave>.<base64url( nonce | etiqueta | cifrado )>
// Un valor que no empieza con "ys1." es de antes del cifrado (JSON en claro): ver Services/Video/EmocionesRecifrado.
public interface IEmocionesCifrado
{
    /// <summary>null si se puede cifrar; si no, por qué, en una frase y sin el valor de la clave (va a los logs).</summary>
    string? ProblemaDeConfiguracion();

    string Cifrar(string textoPlano, string datosAsociados);

    /// <summary>Ok solo si el valor es de este formato, de esta fila y de esta clave, y no fue alterado.</summary>
    ResultadoDescifrado Descifrar(string valorGuardado, string datosAsociados, out string? textoPlano);
}

public class EmocionesCifradoService : IEmocionesCifrado
{
    public const string Prefijo = "ys1";
    private const int LongNonce = 12, LongEtiqueta = 16, LongClave = 32;
    private static readonly Regex ReIdClave = new("^[A-Za-z0-9_-]{1,16}$", RegexOptions.Compiled);

    private readonly IOptionsMonitor<VideoOptions> _opciones;
    private readonly IConfiguration _config;

    public EmocionesCifradoService(IOptionsMonitor<VideoOptions> opciones, IConfiguration config)
    {
        _opciones = opciones; _config = config;
    }

    public static bool EstaCifrado(string? valor) =>
        valor is not null && valor.StartsWith(Prefijo + ".", StringComparison.Ordinal);

    /// <summary>El dato asociado de una sesión clínica: ata el valor cifrado a la fila donde se guarda.</summary>
    public static string DatosAsociadosDeSesion(int idSesion) => "sesion:" + idSesion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string DatosAsociadosDeTranscripcion(int idSesion) => "transcripcion:" + idSesion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string? ProblemaDeConfiguracion() => Evaluar(out _, out _);

    // Devuelve el problema (o null) y, si no hay, la clave y su id.
    private string? Evaluar(out byte[]? clave, out string idClave)
    {
        clave = null;
        var o = _opciones.CurrentValue;
        var e = o.Emociones;
        idClave = e.IdClave ?? "";
        var texto = e.ClaveCifrado?.Trim();
        if (string.IsNullOrEmpty(texto)) return "Video:Emociones:ClaveCifrado no está configurada.";

        byte[] bytes;
        try { bytes = Convert.FromBase64String(texto); }
        catch (FormatException) { return "Video:Emociones:ClaveCifrado no es base64 válido."; }
        if (bytes.Length != LongClave)
            return $"Video:Emociones:ClaveCifrado tiene que ser de {LongClave} bytes en base64 (la configurada es de {bytes.Length}).";
        if (!ReIdClave.IsMatch(idClave))
            return "Video:Emociones:IdClave tiene que tener de 1 a 16 caracteres (letras, números, _ o -).";

        // Una clave de cifrado no se reutiliza como otro secreto: quien tiene uno (el signaling, coturn) no tiene por
        // qué poder leer las emociones.
        if (texto == _config["Jwt:Key"] || texto == o.SignalingSecreto || texto == o.TurnSecreto)
            return "Video:Emociones:ClaveCifrado tiene el mismo valor que otro secreto: tiene que ser una clave propia.";

        clave = bytes;
        return null;
    }

    public string Cifrar(string textoPlano, string datosAsociados)
    {
        if (Evaluar(out var clave, out var idClave) is { } problema) throw new InvalidOperationException(problema);

        var plano = Encoding.UTF8.GetBytes(textoPlano);
        var nonce = RandomNumberGenerator.GetBytes(LongNonce);
        var cifrado = new byte[plano.Length];
        var etiqueta = new byte[LongEtiqueta];
        using (var aes = new AesGcm(clave!, LongEtiqueta))
            aes.Encrypt(nonce, plano, cifrado, etiqueta, Aad(idClave, datosAsociados));

        var todo = new byte[LongNonce + LongEtiqueta + cifrado.Length];
        nonce.CopyTo(todo, 0);
        etiqueta.CopyTo(todo, LongNonce);
        cifrado.CopyTo(todo, LongNonce + LongEtiqueta);
        return $"{Prefijo}.{idClave}.{ABase64Url(todo)}";
    }

    public ResultadoDescifrado Descifrar(string valorGuardado, string datosAsociados, out string? textoPlano)
    {
        textoPlano = null;
        if (!EstaCifrado(valorGuardado)) return ResultadoDescifrado.NoCifrado;

        var partes = valorGuardado.Split('.');
        if (partes.Length != 3) return ResultadoDescifrado.Alterado;

        if (Evaluar(out var clave, out var idClave) is not null) return ResultadoDescifrado.SinClave;
        if (!string.Equals(partes[1], idClave, StringComparison.Ordinal)) return ResultadoDescifrado.ClaveDesconocida;

        byte[] todo;
        try { todo = DeBase64Url(partes[2]); }
        catch (FormatException) { return ResultadoDescifrado.Alterado; }
        if (todo.Length < LongNonce + LongEtiqueta) return ResultadoDescifrado.Alterado;

        var nonce = todo.AsSpan(0, LongNonce);
        var etiqueta = todo.AsSpan(LongNonce, LongEtiqueta);
        var cifrado = todo.AsSpan(LongNonce + LongEtiqueta);
        var plano = new byte[cifrado.Length];
        try
        {
            using var aes = new AesGcm(clave!, LongEtiqueta);
            aes.Decrypt(nonce, cifrado, etiqueta, plano, Aad(idClave, datosAsociados));
        }
        catch (CryptographicException) { return ResultadoDescifrado.Alterado; }

        textoPlano = Encoding.UTF8.GetString(plano);
        return ResultadoDescifrado.Ok;
    }

    private static byte[] Aad(string idClave, string datosAsociados) =>
        Encoding.UTF8.GetBytes($"{Prefijo}|{idClave}|{datosAsociados}");

    private static string ABase64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DeBase64Url(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
    }
}
