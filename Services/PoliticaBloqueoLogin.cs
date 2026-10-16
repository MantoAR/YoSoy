/// <summary>
/// Política de bloqueo de POST /login por intentos fallidos (un solo login para paciente, profesional y
/// configurador: Usuario.IntentosFallidos / Usuario.BloqueadoHasta).
///
/// El umbral y la duración son los de siempre (3 intentos fallidos, 15 minutos de bloqueo). Lo que cambia es
/// cuándo "vence" el conteo:
///  - Antes, después del primer bloqueo el contador no volvía a cero nunca (solo con un login exitoso o un
///    UPDATE a mano): vencido el bloqueo, UN solo error más lo volvía a bloquear 15 minutos, y así
///    indefinidamente. Ahora, vencido el bloqueo el conteo arranca de nuevo: el próximo error cuenta como el
///    primero de tres.
///  - Un login exitoso, y el cambio de contraseña (por recuperación o por el Configurador), ponen todo en cero.
///  - BloqueadoHasta se guarda en UTC y se compara contra el reloj de la aplicación, no mezclando GETDATE() de
///    SQL Server con DateTime.Now de la app: si las dos zonas horarias difieren (un servicio que arrancó
///    antes de un cambio de zona, SQL en otro huso que la app) el bloqueo duraba horas en vez de 15 minutos.
///
/// Los intentos fallidos sin bloqueo (1 o 2) no vencen por tiempo: la tabla no guarda cuándo fue el último y
/// agregar la columna es un cambio de esquema; se limpian con el próximo login exitoso o al vencer un bloqueo.
/// UsuarioDAO.RegistrarIntentoFallido hace en una sola sentencia (atómica) lo mismo que TrasIntentoFallido.
/// </summary>
public static class PoliticaBloqueoLogin
{
    public const int MaxIntentos    = 3;
    public const int MinutosBloqueo = 15;

    /// <summary>¿La cuenta está bloqueada ahora? <paramref name="ahoraUtc"/> y BloqueadoHasta, ambos en UTC.</summary>
    public static bool EstaBloqueado(DateTime? bloqueadoHasta, DateTime ahoraUtc) =>
        bloqueadoHasta.HasValue && bloqueadoHasta.Value > ahoraUtc;

    /// <summary>Minutos que faltan, redondeados para arriba (nunca menos de 1 mientras siga bloqueada).</summary>
    public static int MinutosRestantes(DateTime bloqueadoHasta, DateTime ahoraUtc) =>
        Math.Max(1, (int)Math.Ceiling((bloqueadoHasta - ahoraUtc).TotalMinutes));

    public static string MensajeBloqueo(DateTime bloqueadoHasta, DateTime ahoraUtc)
    {
        var minutos = MinutosRestantes(bloqueadoHasta, ahoraUtc);
        return $"Cuenta bloqueada temporalmente. Intentá en {minutos} {(minutos == 1 ? "minuto" : "minutos")}.";
    }

    /// <summary>El estado de la cuenta después de un intento fallido hecho en <paramref name="ahoraUtc"/>.</summary>
    public static (int IntentosFallidos, DateTime? BloqueadoHasta) TrasIntentoFallido(
        int intentosFallidos, DateTime? bloqueadoHasta, DateTime ahoraUtc)
    {
        var bloqueoVencido = bloqueadoHasta.HasValue && bloqueadoHasta.Value <= ahoraUtc;
        var nuevos = bloqueoVencido ? 1 : intentosFallidos + 1;
        if (nuevos >= MaxIntentos) return (nuevos, ahoraUtc.AddMinutes(MinutosBloqueo));
        return (nuevos, bloqueoVencido ? null : bloqueadoHasta);
    }
}
