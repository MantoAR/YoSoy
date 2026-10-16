using System.Globalization;

// Cómo se leen la fecha y la hora de un turno que manda un cliente (POST /turnos/profesional-solicitar).
//
// El navegador manda la fecha como ISO con zona ("2026-10-15T15:00:00.000Z": el mediodía local pasado a UTC) o como
// "2026-10-15" a secas. Antes se leía con DateTime.Parse, que ante un "Z" o un offset CONVIERTE el instante a la zona
// del sistema operativo del servidor: el día que resultaba dependía de dónde corriera la API (con un servidor en UTC+9
// o más, el mediodía de Argentina caía en el día siguiente). Acá se toma el día TAL COMO ESTÁ ESCRITO, sin convertir.
public static class MomentoDeTurno
{
    /// <summary>
    /// Fecha + hora del turno, sin zona (la hora de pared de la clínica). False si alguna de las dos no se puede
    /// leer: el endpoint contesta 400 en vez de explotar con un 500.
    /// </summary>
    public static bool TryParsear(string? fecha, string? hora, out DateTime momento)
    {
        momento = default;
        if (string.IsNullOrWhiteSpace(fecha) || string.IsNullOrWhiteSpace(hora)) return false;
        if (!TryDia(fecha.Trim(), out var dia)) return false;
        if (!TimeSpan.TryParse(hora.Trim(), CultureInfo.InvariantCulture, out var horaDelDia)) return false;
        if (horaDelDia < TimeSpan.Zero || horaDelDia >= TimeSpan.FromDays(1)) return false;
        momento = dia + horaDelDia;
        return true;
    }

    private static bool TryDia(string texto, out DateTime dia)
    {
        // Lo normal: ISO ("2026-10-15", "2026-10-15T15:00:00.000Z", "2026-10-15T12:00:00-03:00"). DateTimeOffset
        // conserva la hora de pared tal como se escribió: .Date es el día escrito, venga con o sin zona. Sin offset se
        // supone UTC (AssumeUniversal) y no la zona del servidor, que es justamente de lo que no hay que depender.
        if (DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var conZona))
        {
            dia = conZona.Date;
            return true;
        }
        // Lo que aceptaba DateTime.Parse con la cultura del servidor (por ejemplo "15/10/2026").
        if (DateTime.TryParse(texto, out var local))
        {
            dia = local.Date;
            return true;
        }
        dia = default;
        return false;
    }

    /// <summary>
    /// Una cita urgente es "ahora mismo", y quién dice qué hora es ahora es el reloj de la clínica (el del servidor),
    /// no la PC del profesional: con la PC mal de hora o en otra zona, el turno quedaba a una hora que no era y la
    /// sala no abría. Se trunca a los segundos, que es lo que mandaba el navegador.
    /// </summary>
    public static DateTime DeCitaUrgente(DateTime ahoraEnLaClinica) =>
        ahoraEnLaClinica.AddTicks(-(ahoraEnLaClinica.Ticks % TimeSpan.TicksPerSecond));
}
