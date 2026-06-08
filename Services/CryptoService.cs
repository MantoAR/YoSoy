using System.Security.Cryptography;
using System.Text;

public class CryptoService
{
    private readonly byte[]? _key;
    private readonly bool _modoLan;

    public CryptoService(IConfiguration config)
    {
        var clave = config["Sesion:ClaveCifrado"];
        _modoLan = !bool.TryParse(config["Sesion:ModoLan"], out var lan) || lan;

        if (!string.IsNullOrWhiteSpace(clave))
        {
            try { _key = Convert.FromBase64String(clave); }
            catch { throw new InvalidOperationException("Sesion:ClaveCifrado no es base64 válido."); }
            if (_key.Length != 32)
                throw new InvalidOperationException("Sesion:ClaveCifrado debe ser de 32 bytes (256 bits) en base64.");
        }

        if (_key == null && !_modoLan)
            throw new InvalidOperationException(
                "Falta Sesion:ClaveCifrado y el modo no es LAN. No se permite guardar datos clínicos en claro.");
    }

    public bool CifradoActivo => _key != null;

    public string Proteger(string textoPlano)
    {
        if (_key == null)
            return "plain:" + textoPlano;

        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(textoPlano);
        var ct = new byte[pt.Length];
        var tag = new byte[16];

        using var gcm = new AesGcm(_key, 16);
        gcm.Encrypt(nonce, pt, ct, tag);

        var combinado = new byte[nonce.Length + tag.Length + ct.Length];
        Buffer.BlockCopy(nonce, 0, combinado, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combinado, nonce.Length, tag.Length);
        Buffer.BlockCopy(ct, 0, combinado, nonce.Length + tag.Length, ct.Length);
        return "gcm:" + Convert.ToBase64String(combinado);
    }

    public string Revelar(string almacenado)
    {
        if (almacenado.StartsWith("plain:"))
            return almacenado.Substring("plain:".Length);

        if (almacenado.StartsWith("gcm:") && _key != null)
        {
            var combinado = Convert.FromBase64String(almacenado.Substring("gcm:".Length));
            var nonce = combinado[..12];
            var tag = combinado[12..28];
            var ct = combinado[28..];
            var pt = new byte[ct.Length];

            using var gcm = new AesGcm(_key, 16);
            gcm.Decrypt(nonce, ct, tag, pt);
            return Encoding.UTF8.GetString(pt);
        }

        return almacenado;
    }
}
