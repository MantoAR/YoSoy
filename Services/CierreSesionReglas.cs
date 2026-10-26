using System.Globalization;
using System.Text;

// Las reglas del cierre de sesión (lo que pasa después de la videollamada) que no necesitan la base. El sistema NO decide si una sesión se realizó: el profesional lo confirma en
// el panel (confirmaRealizacion) y recién entonces el turno pasa de Confirmado a Realizado. Ver docs/CIERRE_SESION.md.
public static class CierreSesionReglas
{
    public const string Seguimiento = "Seguimiento";
    public const string Alta = "Alta";
    public const string Derivacion = "Derivación";

    // Los estados de turno se resuelven siempre por descripción, nunca por id fijo (como en TurnoReglasService).
    public const string EstadoConfirmado = "Confirmado";
    public const string EstadoRealizado = "Realizado";

    // Los códigos de rechazo que el panel entiende, con el mensaje que ve el profesional (sin tecnicismos: qué pasó y qué puede hacer).
    public const string TurnoInexistente = "turno-inexistente";
    public const string TurnoNoCoincide = "turno-no-coincide";
    public const string TurnoConSesion = "turno-con-sesion";
    public const string EstadoNoPermitido = "estado-no-permitido";
    public const string TurnoFuturo = "turno-futuro";
    public const string CatalogoIncompleto = "catalogo-incompleto";
    public const string TipoCierreInvalido = "tipo-cierre-invalido";
    public const string ClaveInvalida = "clave-invalida";
    public const string ClaveRequerida = "clave-requerida";
    public const string RealizacionSinTurno = "realizacion-sin-turno";
    public const string FaltaMigracion26 = "falta-migracion-26";
    public const string SoloElProfesionalConfirma = "solo-el-profesional-confirma";

    /// <summary>El pedido trae algún campo del cierre nuevo (los pedidos de antes, sin ellos, siguen el camino de siempre).</summary>
    public static bool EsPedidoDeCierre(string? claveCierre, bool? confirmaRealizacion) =>
        !string.IsNullOrWhiteSpace(claveCierre) || confirmaRealizacion is not null;

    /// <summary>Acepta Seguimiento / Alta / Derivación sin importar mayúsculas ni tilde y devuelve siempre la forma con tilde. Vacío = sin tipo (válido).</summary>
    public static bool TryNormalizarTipo(string? crudo, out string? tipo)
    {
        tipo = null;
        if (string.IsNullOrWhiteSpace(crudo)) return true;
        switch (SinTilde(crudo.Trim()).ToLowerInvariant())
        {
            case "seguimiento": tipo = Seguimiento; return true;
            case "alta": tipo = Alta; return true;
            case "derivacion": tipo = Derivacion; return true;
            default: return false;
        }
    }

    public static bool TryClave(string? crudo, out Guid clave) => Guid.TryParse(crudo?.Trim(), out clave) && clave != Guid.Empty;

    /// <summary>Qué sigue después del cierre, para quien se enganche (el panel decide qué ofrecer). Sin tipo, seguimiento.</summary>
    public static string SiguientePaso(string? tipo) => tipo switch
    {
        Alta => "alta",
        Derivacion => "derivacion",
        _ => "seguimiento"
    };

    public static string Mensaje(string codigo, string? estadoDelTurno = null) => codigo switch
    {
        TurnoInexistente => "No encontramos el turno de esta sesión. No se guardó nada: revisá que estés en la sala de ese turno.",
        TurnoNoCoincide => "Ese turno no corresponde a este paciente y a tu agenda. No se guardó nada.",
        TurnoConSesion => "Este turno ya tiene una sesión guardada. Podés verla en la historia clínica del paciente.",
        EstadoNoPermitido => $"Este turno figura como {(string.IsNullOrWhiteSpace(estadoDelTurno) ? "no confirmado" : estadoDelTurno)} y solo un turno Confirmado se puede dar por realizado. No se guardó nada ni cambió el turno. Si querés guardar solo la nota, elegí «No se realizó».",
        TurnoFuturo => "Este turno es de una fecha futura: todavía no se puede dar por realizado. No se guardó nada.",
        CatalogoIncompleto => "El sistema no tiene cargado el estado «Realizado». No se guardó nada: avisale al equipo técnico.",
        TipoCierreInvalido => "El tipo de cierre tiene que ser Seguimiento, Alta o Derivación.",
        ClaveInvalida => "El cierre llegó sin su identificador. Volvé a abrir el panel de cierre e intentá de nuevo.",
        ClaveRequerida => "Para confirmar que la sesión se realizó hace falta el identificador del cierre. Volvé a abrir el panel de cierre e intentá de nuevo.",
        RealizacionSinTurno => "Para dar una sesión por realizada tiene que estar vinculada a un turno. Elegí «No se realizó» para guardar solo la nota.",
        FaltaMigracion26 => "Falta aplicar una actualización de la base de datos (migración 26) para usar el cierre de sesión. No se guardó nada: avisale al equipo técnico.",
        SoloElProfesionalConfirma => "Solo el profesional del turno puede confirmar que la sesión se realizó.",
        _ => "No se pudo guardar el cierre."
    };

    /// <summary>El estado de turno en el que está permitido pasar a Realizado desde el cierre. Una sola transición, y solo con la confirmación del profesional.</summary>
    public static bool PuedePasarARealizado(string? estadoActual) =>
        string.Equals(estadoActual, EstadoConfirmado, StringComparison.OrdinalIgnoreCase);

    public static bool YaEstaRealizado(string? estadoActual) =>
        string.Equals(estadoActual, EstadoRealizado, StringComparison.OrdinalIgnoreCase);

    /// <summary>El texto de una fila de auditoría: solo ids y palabras fijas (nunca contenido clínico, nombres ni el motivo del turno), con el tope de la columna.</summary>
    public static string Detalle(string texto) => texto.Length <= 500 ? texto : texto[..500];

    private static string SinTilde(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
