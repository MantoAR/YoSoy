using System.Collections.Concurrent;

public static class RateLimiter
{
    private static readonly ConcurrentDictionary<string, List<long>> _intentos = new();
    private static long _ultimaLimpieza = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static bool Permitido(string clave, int maximo, int ventanaSegundos)
    {
        var ahora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long ventanaMs = ventanaSegundos * 1000L;

        LimpiarVencidos(ahora, ventanaMs);

        var lista = _intentos.GetOrAdd(clave, _ => new List<long>());
        lock (lista)
        {
            lista.RemoveAll(t => ahora - t >= ventanaMs);
            if (lista.Count >= maximo) return false;
            lista.Add(ahora);
            return true;
        }
    }

    private static void LimpiarVencidos(long ahora, long ventanaMs)
    {
        if (ahora - Interlocked.Read(ref _ultimaLimpieza) < ventanaMs) return;
        Interlocked.Exchange(ref _ultimaLimpieza, ahora);
        foreach (var par in _intentos)
        {
            lock (par.Value)
            {
                par.Value.RemoveAll(t => ahora - t >= ventanaMs);
                if (par.Value.Count == 0) _intentos.TryRemove(par.Key, out _);
            }
        }
    }
}
