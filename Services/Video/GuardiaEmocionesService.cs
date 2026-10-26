using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

// Lo que llega de la pantalla del profesional para guardar las emociones de una sesión (PR 6). La pantalla manda el
// promedio de cada expresión y la ventana en la que lo tomó; cuál es la sesión de video se dice con origen + id, igual
// que en /video/acceso. Nada de esto se toma por bueno: GuardiaEmocionesService lo valida contra el consentimiento.
public class EmocionesSesionRequest
{
    public string? Origen { get; set; }
    public int? Id { get; set; }
    public DateTimeOffset? DesdeUtc { get; set; }
    public DateTimeOffset? HastaUtc { get; set; }
    public int? Muestras { get; set; }
    /// <summary>Clave en inglés de la expresión (neutral, happy, sad...) → porcentaje de 0 a 100.</summary>
    public Dictionary<string, double>? Promedios { get; set; }
}

public record EvaluacionEmociones(string Estado, string? Motivo, string? JsonPlano, int IdConsentimiento, int Muestras,
    string? Origen, int? IdOrigen)
{
    public const string Guardadas = "guardadas";
    public const string Descartadas = "descartadas";
    public const string NoEnviadas = "no-enviadas";

    /// <summary>Se puede guardar: pasó todas las validaciones.</summary>
    public bool Aprobada => JsonPlano is not null;
}

// Guardia de lo que se guarda: la pantalla puede equivocarse (o estar alterada), así que antes de persistir las emociones
// la API comprueba, con los datos de la base y no con lo que dice la pantalla:
//   · que la sesión de video exista y que la persona que guarda sea quien la atiende;
//   · que el paciente sea el de esa sesión;
//   · que a esa persona se le pueda ofrecer el análisis (no es menor, hay clave de cifrado);
//   · que haya una ACEPTACIÓN VIGENTE (no rechazada, no revocada) y que la ventana muestreada caiga dentro de ella;
//   · que las cantidades y los porcentajes sean posibles.
// Si algo no cierra, la sesión se guarda igual (notas, tipo de cierre) pero SIN emociones, y se dice por qué.
public class GuardiaEmocionesService
{
    private const int MaxHorasDeVentana = 12;
    private const double SumaMinima = 90, SumaMaxima = 110;

    private readonly ConsentimientoVideoService _consentimiento;
    private readonly IConsentimientoVideoRepositorio _repo;
    private readonly IOrigenSalaRepositorio _origen;
    private readonly ITextoConsentimientoVideo _textos;
    private readonly IAuditoriaEmociones _auditoria;
    private readonly TimeProvider _reloj;
    private readonly IOptionsMonitor<VideoOptions> _opciones;

    public GuardiaEmocionesService(ConsentimientoVideoService consentimiento, IConsentimientoVideoRepositorio repo,
        IOrigenSalaRepositorio origen, ITextoConsentimientoVideo textos, IAuditoriaEmociones auditoria,
        TimeProvider reloj, IOptionsMonitor<VideoOptions> opciones)
    {
        _consentimiento = consentimiento; _repo = repo; _origen = origen; _textos = textos;
        _auditoria = auditoria; _reloj = reloj; _opciones = opciones;
    }

    /// <param name="idPacienteSolicitado">El paciente al que se asocia la sesión clínica (el del cuerpo del pedido).</param>
    /// <param name="vinoElFormatoViejo">El pedido trae el campo historialEmociones de antes del PR 6: se ignora.</param>
    public EvaluacionEmociones Evaluar(Identidad yo, int idPacienteSolicitado, EmocionesSesionRequest? e, bool vinoElFormatoViejo)
    {
        if (e is null)
            return vinoElFormatoViejo ? Descartada("formato-viejo", null, null) : new EvaluacionEmociones(EvaluacionEmociones.NoEnviadas, null, null, 0, 0, null, null);

        var origen = TextoNormalizado.Normalizar(e.Origen);
        if ((origen != OrigenSala.Turno && origen != OrigenSala.Admision) || e.Id is not > 0)
            return Descartada("sesion-invalida", null, null);
        var id = e.Id.Value;

        var ses = SesionDeVideoReglas.Resolver(_origen, origen, id);
        if (ses is null) return Descartada("sesion-invalida", origen, id);
        if (!SesionDeVideoReglas.AtiendeLaSesion(yo, ses)) return Descartada("sesion-ajena", origen, id);
        if (ses.IdPaciente != idPacienteSolicitado) return Descartada("paciente-no-coincide", origen, id);

        var (disponible, motivoNoDisponible) = _consentimiento.Disponibilidad(ses.IdPaciente, _textos.Actual());
        if (!disponible) return Descartada(motivoNoDisponible!, origen, id);

        // Sin la videollamada aceptada de esta sesión (o revocada) tampoco se guardan emociones.
        if (_consentimiento.FaltaAceptarVideo(ses.Origen, ses.Id, ses.IdPaciente)) return Descartada("video-no-aceptado", origen, id);

        var ultimo = _repo.Ultimo(ses.Origen, ses.Id, ses.IdPaciente);
        switch (ConsentimientoVideoService.EstadoDe(ultimo))
        {
            case ConsentimientoVideoService.SinDecision: return Descartada("sin-consentimiento", origen, id);
            case ConsentimientoVideoService.Rechaza: return Descartada("rechazado", origen, id);
            case ConsentimientoVideoService.Revocado: return Descartada("revocado", origen, id);
        }

        var o = _opciones.CurrentValue.Emociones;
        var tolerancia = TimeSpan.FromSeconds(Math.Clamp(o.ToleranciaSegundos, 0, 120));
        var ahora = _reloj.GetUtcNow().UtcDateTime;

        // Ventana: dentro de la aceptación vigente, ni anterior a ella ni en el futuro, y de un tamaño razonable.
        if (e.DesdeUtc is not { } desdeO || e.HastaUtc is not { } hastaO) return Descartada("ventana-invalida", origen, id);
        var desde = desdeO.UtcDateTime;
        var hasta = hastaO.UtcDateTime;
        if (hasta <= desde || hasta - desde > TimeSpan.FromHours(MaxHorasDeVentana)) return Descartada("ventana-invalida", origen, id);
        if (desde < ultimo!.DecididoUtc - tolerancia) return Descartada("ventana-anterior-al-consentimiento", origen, id);
        if (hasta > ahora + tolerancia) return Descartada("ventana-futura", origen, id);

        // Cantidad de muestras: la pantalla toma una cada ~0,5 s. Más de lo posible es una cuenta inventada.
        var segundos = (hasta - desde).TotalSeconds;
        var maxMuestras = (int)Math.Ceiling(segundos * Math.Clamp(o.MuestrasMaxPorSegundo, 1, 30)) + 5;
        if (e.Muestras is not { } muestras || muestras < 1 || muestras > maxMuestras) return Descartada("muestras-invalidas", origen, id);

        // Porcentajes: solo las siete expresiones del modelo, de 0 a 100, y que sumen algo parecido a 100 (cada una está redondeada).
        var promedios = e.Promedios;
        if (promedios is null || promedios.Count == 0 || promedios.Count > EmocionesCatalogo.Etiquetas.Count)
            return Descartada("promedios-invalidos", origen, id);
        double suma = 0;
        var limpios = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (clave, valor) in promedios)
        {
            if (!EmocionesCatalogo.Etiquetas.ContainsKey(clave) || double.IsNaN(valor) || double.IsInfinity(valor) || valor < 0 || valor > 100)
                return Descartada("promedios-invalidos", origen, id);
            suma += valor;
            limpios[clave] = (int)Math.Round(valor, MidpointRounding.AwayFromZero);
        }
        if (suma < SumaMinima || suma > SumaMaxima) return Descartada("promedios-invalidos", origen, id);

        var json = JsonSerializer.Serialize(new
        {
            v = 1,
            promedios = limpios,
            muestras,
            desdeUtc = desde.ToString("O", CultureInfo.InvariantCulture),
            hastaUtc = hasta.ToString("O", CultureInfo.InvariantCulture),
            consentimiento = ultimo.IdConsentimientoVideo,
            textoVersion = ultimo.VersionTexto
        });
        return new EvaluacionEmociones(EvaluacionEmociones.Guardadas, null, json, ultimo.IdConsentimientoVideo, muestras, origen, id);
    }

    /// <summary>Deja constancia en la auditoría de lo que pasó con las emociones de esta sesión clínica.</summary>
    public void Registrar(Identidad yo, EvaluacionEmociones ev, int idSesion)
    {
        if (ev.Aprobada) _auditoria.EmocionesGuardadas(yo.IdUsuario, idSesion, ev.IdConsentimiento, ev.Muestras);
        else if (ev.Estado == EvaluacionEmociones.Descartadas)
            _auditoria.EmocionesDescartadas(yo.IdUsuario, ev.Origen, ev.IdOrigen, ev.Motivo ?? "sin-motivo", idSesion);
    }

    /// <summary>
    /// Lo que pasó de verdad con las emociones. La escritura es condicional (SesionClinicaDAO.Insertar vuelve a mirar el
    /// consentimiento): si el paciente revocó entre la validación y la escritura, no se guardaron, y para la respuesta y la
    /// auditoría es un descarte por revocación.
    /// </summary>
    public static EvaluacionEmociones TrasLaEscritura(EvaluacionEmociones ev, bool emocionesGuardadas) =>
        ev.Aprobada && !emocionesGuardadas ? Descartada("revocado", ev.Origen, ev.IdOrigen) : ev;

    private static EvaluacionEmociones Descartada(string motivo, string? origen, int? id) =>
        new(EvaluacionEmociones.Descartadas, motivo, null, 0, 0, origen, id);

    public static string MensajeDeDescarte(string? motivo) => motivo switch
    {
        "sin-consentimiento" => "el paciente todavía no decidió si acepta la lectura de emociones",
        "rechazado" => "el paciente no aceptó la lectura de emociones",
        "revocado" => "el paciente dejó de compartir sus expresiones",
        "video-no-aceptado" => "el paciente no aceptó la videollamada de esta sesión (o la revocó)",
        "menor-de-edad" => "la lectura de emociones no está disponible para esta persona",
        "edad-desconocida" => "falta la fecha de nacimiento del paciente",
        "no-configurado" => "el servidor no tiene configurado el cifrado de los datos",
        "formato-viejo" => "esta pantalla es de una versión anterior: recargala",
        _ => "los datos no se pudieron validar"
    };
}
