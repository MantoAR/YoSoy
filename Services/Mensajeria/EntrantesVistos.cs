using System.Collections.Concurrent;

/// <summary>
/// Los mensajes de WhatsApp que ya se atendieron (por teléfono e id), EN MEMORIA, para que un mismo mensaje no se procese dos veces. whatsapp-service reintenta si la API
/// falla (y a veces la API ya lo atendió pero la respuesta se perdió, o tardó más del tiempo que espera el servicio): sin esto, el asistente contestaría dos veces, o avanzaría
/// dos pasos del menú con un solo mensaje. Vale para los dos modos; en el modo Personal, además, el índice único de la base (migración 17) lo cubre aunque la API se reinicie.
///
/// Se reserva el id ANTES de atender y se libera si la atención falla (así el reintento sí se atiende), y se olvida a los 30 minutos o cuando hay demasiados. Solo guarda el
/// teléfono normalizado y el id: ningún texto.
/// </summary>
public sealed class EntrantesVistos
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(30);
    public const int Maximo = 5000;

    private readonly TimeProvider _reloj;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _vistos = new();
    private readonly object _candado = new();
    private DateTimeOffset _ultimaPoda;

    public EntrantesVistos(TimeProvider reloj)
    {
        _reloj = reloj;
        _ultimaPoda = reloj.GetUtcNow();
    }

    /// <summary>Anota el mensaje como visto. false si ya lo estaba (es un repetido).</summary>
    public bool Reservar(string telefono, string idExterno)
    {
        var ahora = _reloj.GetUtcNow();
        Podar(ahora);
        var clave = Clave(telefono, idExterno);
        // Ya estaba pero venció: vale como nuevo.
        if (_vistos.TryGetValue(clave, out var visto) && ahora - visto >= Vigencia) _vistos.TryRemove(clave, out _);
        return _vistos.TryAdd(clave, ahora);
    }

    /// <summary>La atención falló: el reintento tiene que poder entrar.</summary>
    public void Liberar(string telefono, string idExterno) => _vistos.TryRemove(Clave(telefono, idExterno), out _);

    public int Cantidad => _vistos.Count;

    private static string Clave(string telefono, string idExterno) => Telefono.Normalizar(telefono) + "|" + idExterno;

    // A lo sumo una vez por minuto, o al pasarse del máximo: se olvida lo vencido y, si sigue habiendo de más, lo más viejo.
    private void Podar(DateTimeOffset ahora)
    {
        if (_vistos.Count <= Maximo && ahora - _ultimaPoda < TimeSpan.FromMinutes(1)) return;
        lock (_candado)
        {
            _ultimaPoda = ahora;
            foreach (var (clave, cuando) in _vistos)
                if (ahora - cuando >= Vigencia) _vistos.TryRemove(clave, out _);
            if (_vistos.Count <= Maximo) return;
            foreach (var (clave, _) in _vistos.OrderBy(v => v.Value).Take(_vistos.Count - Maximo * 9 / 10))
                _vistos.TryRemove(clave, out _);
        }
    }
}
