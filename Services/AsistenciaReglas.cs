// Llegada tarde, inasistencia y abandono de tratamiento (rama AAA1, migración 37). Reglas puras: sin base ni reloj, para poder probarlas solas
// (YoSoy.Tests/AsistenciaReglasTests.cs). Las usan Endpoints/AsistenciaEndpoints.cs y Services/AsistenciaAvisosService.cs.
//
// Lo que se decidió (reunión con la Lic. Villalón del 26/10 y decisiones de Araceli del 27/10):
//   - A los AvisoMinutos (5) sin que el paciente entre a la sala, le llega un aviso «Te estamos esperando».
//   - A los ToleranciaMinutos (15) el turno NO pasa solo a Ausente: se habilita el botón para que lo marque una persona
//     (el profesional del turno, o la Recepcionista con el privilegio Gestión de pacientes).
//   - Si el paciente ya entró a la sala, no se puede marcar Ausente (aunque haya llegado tarde).
//   - Cancelar con menos de HorasCancelacionSinCargo (24) o no venir deja el turno «cobrable»; el profesional puede justificar
//     fuerza mayor y dejarlo sin cargo. El cobro en sí es del futuro módulo de Cobros: acá solo queda la marca.
//   - AusenciasAlerta (2) ausencias seguidas con el mismo profesional: alerta «Posible abandono».
//   - AusenciasAbandono (3): el sistema sugiere el abandono; lo confirma el profesional tratante o el auditor.
// Los cinco valores son de la organización (Configuración › Asistencia, tabla ConfiguracionOrganizacion).
public record ParametrosAsistencia(int AvisoMinutos, int ToleranciaMinutos, int HorasCancelacionSinCargo, int AusenciasAlerta, int AusenciasAbandono)
{
    public static readonly ParametrosAsistencia PorDefecto = new(5, 15, 24, 2, 3);

    public const string ClaveAviso = "Asistencia.AvisoMinutos";
    public const string ClaveTolerancia = "Asistencia.ToleranciaMinutos";
    public const string ClaveHorasCancelacion = "Asistencia.HorasCancelacionSinCargo";
    public const string ClaveAlerta = "Asistencia.AusenciasAlerta";
    public const string ClaveAbandono = "Asistencia.AusenciasAbandono";

    /// <summary>null si los valores tienen sentido; si no, el mensaje para la pantalla.</summary>
    public string? Validar()
    {
        if (AvisoMinutos < 1 || AvisoMinutos > 60) return "El aviso al paciente va entre 1 y 60 minutos.";
        if (ToleranciaMinutos < 5 || ToleranciaMinutos > 120) return "La tolerancia va entre 5 y 120 minutos.";
        if (AvisoMinutos >= ToleranciaMinutos) return "El aviso al paciente tiene que llegar antes de que termine la tolerancia.";
        if (HorasCancelacionSinCargo < 0 || HorasCancelacionSinCargo > 168) return "Las horas para cancelar sin cargo van de 0 a 168 (una semana).";
        if (AusenciasAlerta < 1 || AusenciasAlerta > 10) return "La alerta va entre 1 y 10 ausencias seguidas.";
        if (AusenciasAbandono <= AusenciasAlerta || AusenciasAbandono > 10) return "El abandono tiene que pedir más ausencias que la alerta (y no más de 10).";
        return null;
    }

    /// <summary>Arma los parámetros desde las filas Clave/Valor. Lo que falta o no es un número queda con el valor por defecto.</summary>
    public static ParametrosAsistencia Desde(IReadOnlyDictionary<string, string?> filas)
    {
        int Leer(string clave, int defecto) =>
            filas.TryGetValue(clave, out var v) && int.TryParse(v, out var n) ? n : defecto;
        var d = PorDefecto;
        var p = new ParametrosAsistencia(Leer(ClaveAviso, d.AvisoMinutos), Leer(ClaveTolerancia, d.ToleranciaMinutos),
            Leer(ClaveHorasCancelacion, d.HorasCancelacionSinCargo), Leer(ClaveAlerta, d.AusenciasAlerta), Leer(ClaveAbandono, d.AusenciasAbandono));
        return p.Validar() is null ? p : d;   // una fila tocada a mano con un valor sin sentido no rompe nada: rigen los de siempre
    }

    public IReadOnlyDictionary<string, string> ComoFilas() => new Dictionary<string, string>
    {
        [ClaveAviso] = AvisoMinutos.ToString(), [ClaveTolerancia] = ToleranciaMinutos.ToString(),
        [ClaveHorasCancelacion] = HorasCancelacionSinCargo.ToString(), [ClaveAlerta] = AusenciasAlerta.ToString(),
        [ClaveAbandono] = AusenciasAbandono.ToString()
    };
}

public static class AsistenciaReglas
{
    /// <summary>Hasta cuánto después del inicio el turno se muestra como «esperando conexión».</summary>
    public const int VentanaEsperaMinutos = 60;

    public const string NivelOk = "ok", NivelAlerta = "alerta", NivelAbandono = "abandono";

    public static readonly string[] ResultadosSeguimiento = { "Sigue", "Pausa", "No responde", "Otro" };

    /// <summary>Minutos desde el inicio del turno (0 si todavía no empezó).</summary>
    public static int MinutosDemora(DateTime inicio, DateTime ahora) =>
        ahora <= inicio ? 0 : (int)Math.Floor((ahora - inicio).TotalMinutes);

    /// <summary>El turno ya empezó, el paciente no entró a la sala y todavía estamos en la ventana de espera.</summary>
    public static bool EsperandoConexion(DateTime inicio, DateTime ahora, bool ingreso) =>
        !ingreso && ahora >= inicio && ahora < inicio.AddMinutes(VentanaEsperaMinutos);

    /// <summary>Pasaron los minutos del aviso, no los de la tolerancia, y el paciente no entró: corresponde el «Te estamos esperando».</summary>
    public static bool CorrespondeAviso(DateTime inicio, DateTime ahora, bool ingreso, ParametrosAsistencia p) =>
        !ingreso && ahora >= inicio.AddMinutes(p.AvisoMinutos) && ahora < inicio.AddMinutes(p.ToleranciaMinutos);

    /// <summary>null si se puede marcar Ausente; si no, el motivo.</summary>
    public static string? MotivoParaNoMarcarAusente(string estado, DateTime inicio, DateTime ahora, bool ingreso, ParametrosAsistencia p)
    {
        if (estado != "Confirmado") return "Solo se puede registrar la inasistencia de un turno confirmado.";
        if (ingreso) return "El paciente entró a la sala: no es una inasistencia (si llegó tarde, queda registrado igual).";
        if (ahora < inicio.AddMinutes(p.ToleranciaMinutos))
            return ahora < inicio ? "Todavía no llegó la hora del turno."
                                  : $"Todavía corre la tolerancia: se puede marcar Ausente desde las {inicio.AddMinutes(p.ToleranciaMinutos):HH:mm}.";
        return null;
    }

    public static bool PuedeMarcarAusente(string estado, DateTime inicio, DateTime ahora, bool ingreso, ParametrosAsistencia p) =>
        MotivoParaNoMarcarAusente(estado, inicio, ahora, ingreso, p) is null;

    /// <summary>Cancelar dentro de las horas sin cargo (o después de empezado) deja el turno cobrable.</summary>
    public static bool CancelacionCobrable(DateTime inicio, DateTime momentoCancelacion, ParametrosAsistencia p) =>
        momentoCancelacion > inicio.AddHours(-p.HorasCancelacionSinCargo);

    /// <summary>
    /// Ausencias seguidas, desde el turno pasado más reciente hacia atrás. Corta el primer turno al que el paciente vino
    /// («Realizado», o «Confirmado» ya pasado: nadie lo marcó Ausente). Cancelados, rechazados y pendientes no cuentan ni cortan.
    /// </summary>
    public static int AusenciasConsecutivas(IEnumerable<string> estadosDelMasRecienteAlMasViejo)
    {
        var n = 0;
        foreach (var e in estadosDelMasRecienteAlMasViejo)
        {
            if (e == "Ausente") { n++; continue; }
            if (e is "Realizado" or "Confirmado") break;
        }
        return n;
    }

    public static string Nivel(int ausenciasSeguidas, ParametrosAsistencia p) =>
        ausenciasSeguidas >= p.AusenciasAbandono ? NivelAbandono
        : ausenciasSeguidas >= p.AusenciasAlerta ? NivelAlerta
        : NivelOk;

    public static bool EsPresencial(string? modalidad) =>
        modalidad?.Trim().StartsWith("Presencial", StringComparison.OrdinalIgnoreCase) == true;
}
