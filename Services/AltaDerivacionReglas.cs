using System.Globalization;

// Las reglas del alta clínica y de la derivación interna asistidas desde el cierre de sesión, sin la base. El sistema precarga, guía, avisa y deja constancia; UNA PERSONA confirma:
// nada de esto cambia un estado sin el campo «confirma» del pedido (el botón «Confirmar el alta» / «Confirmar la derivación» de la pantalla).
public static class AltaDerivacionReglas
{
    // TODO-VALIDAR: los psicólogos tienen que confirmar (1) el nombre y el significado del estado «En alta» (alta clínica dada, plan de control guardado, el caso sigue abierto y a cargo del
    // profesional, fuera de la carga de nuevas asignaciones); (2) los límites del plan de control (hasta 12 contactos, cada 7 a 365 días); y (3) qué debe contener el informe de alta y cuánto se
    // conserva (legal). Se implementó de la forma más neutra: un texto libre y un plan numérico.
    // Estados del caso (Caso.Estado). 'En alta' es el único que agrega la migración 27: el alta clínica ya se dio y el plan de control quedó guardado.
    public const string CasoAsignado = "Asignado";
    public const string CasoEnAtencion = "En atención";
    public const string CasoEnAlta = "En alta";
    public const string CasoCerrado = "Cerrado";

    // TODO-VALIDAR: quién decide qué hacer cuando el profesional de destino RECHAZA una derivación (hoy: Admisión/CRM recibe el aviso, el caso sigue con quien lo tenía y Admisión la da por
    // resuelta cuando decidió cómo seguir) y si el origen debería poder retirar una derivación pendiente. Y quién es «Admisión» para el traspaso: hoy el rol CRM, que es el que ya puede reasignar casos.
    // Estados de la derivación interna (Derivacion.Estado, Origen 'Cierre'). Los de la solicitud de cambio de siempre ('Pendiente', 'Aceptada', 'Rechazada') no se tocan.
    public const string DerivacionPendienteDelDestino = "Pendiente del destino";
    public const string DerivacionAceptadaPorElDestino = "Aceptada por el destino";
    public const string DerivacionRechazadaPorElDestino = "Rechazada por el destino";
    public const string DerivacionTraspasada = "Traspasada";
    public const string DerivacionResueltaPorAdmision = "Resuelta por admisión";
    public const string OrigenCierre = "Cierre";

    public const int InformeMaximo = 8000;
    public const int MotivoMaximo = 500;
    public const int ObservacionesMaximo = 1000;
    public const int MaxControles = 12;
    public const int CadaDiasMinimo = 7, CadaDiasMaximo = 365;

    // Códigos de rechazo (los que la pantalla entiende) con su estado HTTP. Los mensajes dicen qué pasó y qué hacer.
    public const string FaltaMigracion27 = "falta-migracion-27";
    public const string FaltaConfirmacion = "falta-confirmacion";
    public const string CasoInexistente = "caso-inexistente";
    public const string CasoAjeno = "caso-ajeno";
    public const string EstadoNoPermitido = "estado-no-permitido";
    public const string SesionNoCoincide = "sesion-no-coincide";
    public const string SesionNoEsDelTipo = "sesion-no-es-del-tipo";
    public const string DestinoNoDisponible = "destino-no-disponible";
    public const string MismoProfesional = "mismo-profesional";
    public const string DerivacionEnCurso = "derivacion-en-curso";
    public const string DerivacionInexistente = "derivacion-inexistente";
    public const string YaRespondida = "ya-respondida";
    public const string EstadoDeDerivacion = "estado-de-derivacion";
    public const string CasoCambio = "caso-cambio";
    public const string DatosInvalidos = "datos-invalidos";

    public static int Status(string codigo) => codigo switch
    {
        CasoInexistente or DerivacionInexistente => 404,
        CasoAjeno => 403,
        FaltaConfirmacion or DatosInvalidos or MismoProfesional => 400,
        _ => 409
    };

    public static string Mensaje(string codigo, string? estado = null) => codigo switch
    {
        FaltaMigracion27 => "Falta aplicar una actualización de la base de datos (migración 27) para usar el alta y la derivación asistidas. No se guardó nada: avisale al equipo técnico.",
        FaltaConfirmacion => "Para aplicarlo tenés que confirmarlo con el botón del asistente. No se cambió nada.",
        CasoInexistente => "No encontramos el caso de este paciente. No se cambió nada.",
        CasoAjeno => "Ese caso no es tuyo: solo el profesional a cargo puede dar el alta o derivar el caso. No se cambió nada.",
        EstadoNoPermitido => estado switch
        {
            CasoCerrado => "El caso está cerrado: no se puede dar el alta ni derivarlo. No se cambió nada.",
            CasoEnAlta => "El caso ya está en alta: no se puede derivar ni dar el alta de nuevo. No se cambió nada.",
            _ => $"El caso está «{estado}»: solo se puede dar el alta o derivar un caso que está asignado o en atención. No se cambió nada."
        },
        SesionNoCoincide => "La sesión del cierre no corresponde a este paciente o no es tuya. No se cambió nada: guardá el cierre de nuevo desde la sala.",
        SesionNoEsDelTipo => "La sesión del cierre se guardó con otro tipo de cierre. Volvé a la sala y elegí el tipo que corresponde. No se cambió nada.",
        DestinoNoDisponible => "Ese profesional no está disponible para recibir derivaciones. Elegí otro. No se cambió nada.",
        MismoProfesional => "No podés derivar un caso a vos mismo. Elegí a otro profesional.",
        DerivacionEnCurso => "Este caso ya tiene una derivación en curso a otro profesional. Esperá la respuesta o pedile a Admisión que la resuelva. No se cambió nada.",
        DerivacionInexistente => "No encontramos esa derivación.",
        YaRespondida => "Esta derivación ya fue respondida de otra manera. No se cambió nada.",
        EstadoDeDerivacion => $"Esta derivación está «{estado}» y no admite esa acción. No se cambió nada.",
        CasoCambio => "El caso ya no está a cargo del profesional que derivó: revisalo antes de confirmar el traspaso. No se cambió nada.",
        _ => "No se pudo completar la acción. No se cambió nada."
    };

    /// <summary>Una fecha «aaaa-mm-dd» (la que manda la pantalla); null si no es válida.</summary>
    public static DateTime? Fecha(string? texto) =>
        DateTime.TryParseExact(texto?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f.Date : null;

    public record PlanDeControl(int Controles, int? CadaDias, DateTime? Primero);

    /// <summary>Valida el alta; devuelve el mensaje de lo que falta o está mal (null si está todo bien) y los datos ya normalizados.</summary>
    public static string? ValidarAlta(AltaClinicaRequest r, DateTime hoy, out DateTime fechaAlta, out PlanDeControl plan, int informeMaximo = InformeMaximo)
    {
        fechaAlta = hoy.Date;
        plan = new PlanDeControl(0, null, null);

        if (string.IsNullOrWhiteSpace(r.Informe)) return "Escribí el informe de alta.";
        if (r.Informe.Length > informeMaximo) return $"El informe de alta no puede superar los {informeMaximo} caracteres.";

        if (!string.IsNullOrWhiteSpace(r.FechaAlta))
        {
            if (Fecha(r.FechaAlta) is not DateTime f) return "La fecha de alta no es válida.";
            if (f.Date > hoy.Date) return "La fecha de alta no puede ser posterior a hoy.";
            fechaAlta = f.Date;
        }

        var controles = r.ControlesPrevistos ?? 0;
        if (controles < 0 || controles > MaxControles) return $"La cantidad de contactos de control tiene que estar entre 0 y {MaxControles}.";
        if (controles == 0) return null;   // sin controles: lo demás se ignora

        if (r.ControlCadaDias is not int cada || cada < CadaDiasMinimo || cada > CadaDiasMaximo)
            return $"Indicá cada cuántos días se hace el control (entre {CadaDiasMinimo} y {CadaDiasMaximo}).";
        if (Fecha(r.PrimerControl) is not DateTime primero) return "Indicá la fecha del primer contacto de control.";
        if (primero < fechaAlta) return "El primer control no puede ser anterior a la fecha de alta.";
        if (primero > fechaAlta.AddDays(CadaDiasMaximo)) return "El primer control no puede estar a más de un año de la fecha de alta.";

        plan = new PlanDeControl(controles, cada, primero);
        return null;
    }

    public static string? ValidarDerivacion(DerivarCasoRequest r)
    {
        if (r.IdProfesionalDestino is not > 0) return "Elegí el profesional de destino.";
        if (string.IsNullOrWhiteSpace(r.Motivo)) return "Escribí el motivo de la derivación.";
        if (r.Motivo.Length > MotivoMaximo) return $"El motivo no puede superar los {MotivoMaximo} caracteres.";
        if (r.Observaciones is { Length: > ObservacionesMaximo }) return $"Las observaciones no pueden superar los {ObservacionesMaximo} caracteres.";
        return null;
    }

    public static bool CasoSePuedeDerivarODarDeAlta(string? estado) => estado is CasoAsignado or CasoEnAtencion;

    public static string Detalle(string texto) => texto.Length <= 500 ? texto : texto[..500];
}

// InformeEstructurado y Controles son del alta estructurada (migración 35): si vienen, el informe se arma con las secciones y el plan sale de la lista de fechas de control (Services/AltaEstructurada.cs).
public record AltaClinicaRequest(bool? Confirma, string? Informe, string? FechaAlta, int? ControlesPrevistos, int? ControlCadaDias, string? PrimerControl, int? IdSesion, string? ClaveCierre,
    InformeAltaEstructurado? InformeEstructurado = null, List<string>? Controles = null);
public record DerivarCasoRequest(bool? Confirma, int? IdProfesionalDestino, string? Motivo, string? Observaciones, int? IdSesion, string? ClaveCierre);
public record ResponderDerivacionRequest(bool? Acepta, string? Motivo);
public record TraspasarRequest(string? MotivoCategoria, string? Nota);
