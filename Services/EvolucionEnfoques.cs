using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Evolución por enfoque (P11 de la documentación): plantillas, validación al firmar,
// escalas PHQ-9 y GAD-7 calculadas en el servidor, texto plano para las vistas viejas y
// hash del contenido firmado (RN-11). Sin SQL: se prueba en YoSoy.Tests/EvolucionEnfoquesTests.cs.
//
// Psiquiatría queda afuera a propósito en esta etapa (prescripción = ReNaPDiS, Res. 2214/2025).
public static class EnfoquesEvolucion
{
    public const string General = "General";
    public const string Tcc = "TCC";
    public const string Psicoanalisis = "Psicoanalisis";
    public const string Sistemica = "Sistemica";
    public const string InfantoJuvenil = "InfantoJuvenil";
    public static readonly string[] Todos = { General, Tcc, Psicoanalisis, Sistemica, InfantoJuvenil };
    public static bool Existe(string? e) => e is not null && Todos.Contains(e);
}

public static class ModosEvolucion
{
    public const string Manual = "Manual";
    public const string Dictado = "Dictado";
    public const string Transcripcion = "Transcripcion";
    public const string DictadoTranscripcion = "DictadoTranscripcion";
    public static readonly string[] Todos = { Manual, Dictado, Transcripcion, DictadoTranscripcion };
    public static bool Existe(string? m) => m is not null && Todos.Contains(m);
    public static bool UsaTranscripcion(string m) => m is Transcripcion or DictadoTranscripcion;
}

public static class TiposCierre
{
    // RN-08: los tres valores del Encuentro 5. Se aceptan con y sin tilde (la base ya tiene "Derivación").
    public static readonly string[] Todos = { "Seguimiento", "Derivación", "Alta" };
    public static string? Normalizar(string? t) => t switch
    {
        "Seguimiento" => "Seguimiento",
        "Derivación" or "Derivacion" => "Derivación",
        "Alta" => "Alta",
        _ => null
    };
}

public record CampoPlantilla(string Clave, string Etiqueta, string Tipo, bool Requerido, string? Ayuda = null, string[]? Opciones = null);
public record EscalaDef(string Clave, string Nombre, string Consigna, string[] Items, string[] Respuestas, (int Hasta, string Severidad)[] Bandas);
public record Plantilla(string Enfoque, string Nombre, string Descripcion, CampoPlantilla[] Campos, string[] Escalas, bool SugiereTranscripcion);

public record ConsultanteEvolucion(string? Nombre, string? Vinculo, bool Presente);
public record ContenidoEvolucion(Dictionary<string, string?>? Campos, List<ConsultanteEvolucion>? Consultantes, Dictionary<string, int[]?>? Escalas);
public record ResultadoEscala(string Escala, int Total, string Severidad, bool AlertaRiesgo);

public static class EvolucionEnfoquesService
{
    // Tipos de campo que entiende el frontend (shared/evolucion.js).
    public const string Texto = "texto", TextoLargo = "textoLargo", Seleccion = "seleccion", Consultantes = "consultantes";

    private static readonly string[] RespuestasFrecuencia =
        { "Ningún día", "Varios días", "Más de la mitad de los días", "Casi todos los días" };

    // Versiones en español de uso libre. Antes de usarlas con pacientes reales, el equipo
    // clínico tiene que confirmar el texto contra la versión validada en Argentina.
    public static readonly EscalaDef Phq9 = new("PHQ9", "PHQ-9 (depresión)",
        "Durante las últimas 2 semanas, ¿con qué frecuencia le han molestado los siguientes problemas?",
        new[]
        {
            "Poco interés o placer en hacer cosas",
            "Se ha sentido decaído/a, deprimido/a o sin esperanzas",
            "Dificultad para quedarse o permanecer dormido/a, o ha dormido demasiado",
            "Se ha sentido cansado/a o con poca energía",
            "Sin apetito o ha comido en exceso",
            "Se ha sentido mal consigo mismo/a, o que es un fracaso, o que ha quedado mal consigo mismo/a o con su familia",
            "Dificultad para concentrarse en cosas como leer o mirar televisión",
            "Se ha movido o hablado tan lento que otras personas lo notaron, o lo contrario: muy inquieto/a o agitado/a",
            "Pensamientos de que estaría mejor muerto/a o de lastimarse de alguna manera"
        },
        RespuestasFrecuencia,
        new[] { (4, "Mínima"), (9, "Leve"), (14, "Moderada"), (19, "Moderadamente severa"), (27, "Severa") });

    public static readonly EscalaDef Gad7 = new("GAD7", "GAD-7 (ansiedad)",
        "Durante las últimas 2 semanas, ¿con qué frecuencia le han molestado los siguientes problemas?",
        new[]
        {
            "Se ha sentido nervioso/a, ansioso/a o con los nervios de punta",
            "No ha podido dejar de preocuparse o controlar la preocupación",
            "Se ha preocupado demasiado por diferentes cosas",
            "Ha tenido dificultad para relajarse",
            "Se ha sentido tan inquieto/a que no podía quedarse quieto/a",
            "Se ha molestado o irritado fácilmente",
            "Ha sentido miedo de que algo terrible fuera a pasar"
        },
        RespuestasFrecuencia,
        new[] { (4, "Mínima"), (9, "Leve"), (14, "Moderada"), (21, "Severa") });

    public static readonly IReadOnlyDictionary<string, EscalaDef> Escalas =
        new Dictionary<string, EscalaDef> { [Phq9.Clave] = Phq9, [Gad7.Clave] = Gad7 };

    public static readonly IReadOnlyList<Plantilla> Plantillas = new[]
    {
        new Plantilla(EnfoquesEvolucion.General, "General", "Nota de evolución sin estructura de un enfoque particular.",
            new[] { new CampoPlantilla("notas", "Notas de la sesión", TextoLargo, true) },
            Array.Empty<string>(), SugiereTranscripcion: true),

        new Plantilla(EnfoquesEvolucion.Tcc, "Terapia cognitivo-conductual (TCC)",
            "Registro situación, pensamiento, emoción y conducta, con escalas opcionales.",
            new[]
            {
                new CampoPlantilla("situacion", "Situación", TextoLargo, true, "Qué pasó, cuándo y con quién."),
                new CampoPlantilla("pensamientos", "Pensamientos", TextoLargo, true, "Pensamientos automáticos que aparecieron."),
                new CampoPlantilla("emociones", "Emoción", TextoLargo, true, "Emociones y su intensidad (0 a 100)."),
                new CampoPlantilla("conductas", "Conducta", TextoLargo, true, "Qué hizo o dejó de hacer."),
                new CampoPlantilla("intervenciones", "Intervenciones", TextoLargo, false, "Técnicas trabajadas en la sesión."),
                new CampoPlantilla("tareas", "Tareas para la próxima sesión", TextoLargo, false)
            },
            new[] { Phq9.Clave, Gad7.Clave }, SugiereTranscripcion: true),

        new Plantilla(EnfoquesEvolucion.Psicoanalisis, "Psicoanálisis",
            "Nota libre. La transcripción es opcional y por defecto queda apagada.",
            new[]
            {
                new CampoPlantilla("notas", "Nota libre", TextoLargo, true),
                new CampoPlantilla("lineas", "Líneas de trabajo", TextoLargo, false)
            },
            Array.Empty<string>(), SugiereTranscripcion: false),

        new Plantilla(EnfoquesEvolucion.Sistemica, "Sistémica",
            "Sesión con uno o varios consultantes: quiénes estuvieron y qué pautas se observaron.",
            new[]
            {
                new CampoPlantilla("consultantes", "Consultantes presentes", Consultantes, true, "Agregá a cada persona que participó y su vínculo."),
                new CampoPlantilla("demanda", "Demanda o motivo trabajado", TextoLargo, false),
                new CampoPlantilla("dinamica", "Pautas relacionales observadas", TextoLargo, true),
                new CampoPlantilla("hipotesis", "Hipótesis sistémica", TextoLargo, false),
                new CampoPlantilla("intervenciones", "Intervenciones", TextoLargo, false),
                new CampoPlantilla("prescripciones", "Tareas o prescripciones", TextoLargo, false)
            },
            Array.Empty<string>(), SugiereTranscripcion: true),

        new Plantilla(EnfoquesEvolucion.InfantoJuvenil, "Infanto-juvenil",
            "Sesión con un niño, niña o adolescente: qué se trabajó y qué se le devuelve al tutor.",
            new[]
            {
                new CampoPlantilla("relato", "Relato de la sesión o del juego", TextoLargo, true),
                new CampoPlantilla("desarrollo", "Observaciones del desarrollo", TextoLargo, false),
                new CampoPlantilla("confidencialidad", "Nivel de confidencialidad acordado", Seleccion, true, null,
                    new[] { "Alto: al tutor solo lo acordado con el paciente", "Medio: lineamientos generales", "Bajo: devolución amplia" }),
                new CampoPlantilla("devolucionTutor", "Qué se le devuelve al tutor", TextoLargo, true,
                    "Solo lo acordado. Si no hay nada para devolver, escribilo."),
                new CampoPlantilla("modalidadDevolucion", "Cuándo se hace la devolución", Seleccion, true, null,
                    new[] { "En esta sesión", "En un contacto aparte", "No corresponde por ahora" }),
                new CampoPlantilla("riesgo", "¿Se detectaron indicadores de riesgo?", Seleccion, true,
                    "Si es sí, activá el protocolo de crisis y evaluá la obligación de reportar a los organismos de protección.",
                    new[] { "No", "Sí" })
            },
            Array.Empty<string>(), SugiereTranscripcion: false)
    };

    public static Plantilla? PlantillaDe(string? enfoque) => Plantillas.FirstOrDefault(p => p.Enfoque == enfoque);

    public static ResultadoEscala Calcular(EscalaDef def, int[] respuestas)
    {
        var total = respuestas.Sum();
        var severidad = def.Bandas.First(b => total <= b.Hasta).Severidad;
        // PHQ-9, ítem 9 (pensamientos de muerte o de lastimarse): cualquier respuesta distinta
        // de "Ningún día" se marca para que el profesional evalúe el riesgo en ese momento.
        var alerta = def.Clave == Phq9.Clave && respuestas[8] > 0;
        return new ResultadoEscala(def.Clave, total, severidad, alerta);
    }

    /// <summary>
    /// Limpia lo que mandó el navegador: solo los campos de la plantilla, textos recortados,
    /// escalas solo si la plantilla las admite y completas con valores de 0 a 3. Devuelve la
    /// lista de errores (vacía si está bien). Al firmar, además, exige los obligatorios.
    /// </summary>
    public static (ContenidoEvolucion Limpio, List<string> Errores) Normalizar(Plantilla p, ContenidoEvolucion? c, bool firmar)
    {
        var errores = new List<string>();
        var campos = new Dictionary<string, string?>();
        List<ConsultanteEvolucion>? consultantes = null;

        foreach (var campo in p.Campos)
        {
            if (campo.Tipo == Consultantes)
            {
                consultantes = (c?.Consultantes ?? new())
                    .Where(x => !string.IsNullOrWhiteSpace(x.Nombre))
                    .Select(x => new ConsultanteEvolucion(Recortar(x.Nombre, 120), Recortar(x.Vinculo, 80), x.Presente))
                    .Take(20).ToList();
                if (firmar && campo.Requerido && consultantes.Count == 0)
                    errores.Add($"Falta completar: {campo.Etiqueta}.");
                continue;
            }
            string? valor = null;
            c?.Campos?.TryGetValue(campo.Clave, out valor);
            valor = Recortar(valor, 20000);
            if (campo.Tipo == Seleccion && valor is not null && campo.Opciones is not null && !campo.Opciones.Contains(valor))
            {
                errores.Add($"Valor no válido en: {campo.Etiqueta}.");
                valor = null;
            }
            if (firmar && campo.Requerido && string.IsNullOrWhiteSpace(valor))
                errores.Add($"Falta completar: {campo.Etiqueta}.");
            campos[campo.Clave] = valor;
        }

        var escalas = new Dictionary<string, int[]?>();
        foreach (var (clave, respuestas) in c?.Escalas ?? new())
        {
            if (respuestas is null || respuestas.Length == 0) continue;   // no se aplicó
            if (!p.Escalas.Contains(clave) || !Escalas.TryGetValue(clave, out var def))
            {
                errores.Add($"La escala {clave} no corresponde a este enfoque.");
                continue;
            }
            if (respuestas.Length != def.Items.Length || respuestas.Any(v => v < 0 || v > 3))
            {
                // En un borrador, una escala a medio responder no frena el guardado: se descarta.
                // Al firmar sí: o se completa, o se destilda.
                if (firmar) errores.Add($"{def.Nombre}: respondé los {def.Items.Length} ítems con valores de 0 a 3, o no la apliques.");
                continue;
            }
            escalas[clave] = respuestas;
        }

        return (new ContenidoEvolucion(campos, consultantes, escalas.Count > 0 ? escalas : null), errores);
    }

    public static List<ResultadoEscala> Resultados(ContenidoEvolucion c) =>
        (c.Escalas ?? new()).Where(e => e.Value is not null && Escalas.ContainsKey(e.Key))
            .Select(e => Calcular(Escalas[e.Key], e.Value!)).ToList();

    private static string? Recortar(string? s, int max)
    {
        if (s is null) return null;
        s = s.Trim();
        if (s.Length == 0) return null;
        return s.Length > max ? s[..max] : s;
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>JSON canónico que se guarda en Evolucion.ContenidoJson (incluye los resultados de las escalas).</summary>
    public static string ComoJson(ContenidoEvolucion c) =>
        JsonSerializer.Serialize(new { c.Campos, c.Consultantes, c.Escalas, Resultados = Resultados(c) }, Json);

    /// <summary>Texto legible para Evolucion.Observaciones: lo usan la historia clínica y los reportes ya existentes.</summary>
    public static string TextoPlano(Plantilla p, ContenidoEvolucion c)
    {
        var sb = new StringBuilder();
        foreach (var campo in p.Campos)
        {
            if (campo.Tipo == Consultantes)
            {
                if (c.Consultantes is { Count: > 0 })
                    sb.AppendLine($"{campo.Etiqueta}: " + string.Join("; ", c.Consultantes.Select(x =>
                        $"{x.Nombre}{(string.IsNullOrEmpty(x.Vinculo) ? "" : $" ({x.Vinculo})")}{(x.Presente ? "" : " - ausente")}")));
                continue;
            }
            if (c.Campos is not null && c.Campos.TryGetValue(campo.Clave, out var v) && !string.IsNullOrWhiteSpace(v))
                sb.AppendLine($"{campo.Etiqueta}: {v}");
        }
        foreach (var r in Resultados(c))
            sb.AppendLine($"{Escalas[r.Escala].Nombre}: {r.Total} punto{(r.Total == 1 ? "" : "s")}, {r.Severidad}" + (r.AlertaRiesgo ? " (ítem 9 positivo: evaluar riesgo)" : ""));
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Hash SHA-256 de lo firmado (RN-11). Si alguien cambia la fila por fuera de la
    /// aplicación, el hash deja de coincidir y la historia clínica lo muestra.
    /// </summary>
    public static string Hash(Evolucion e)
    {
        var canon = string.Join("␟",
            e.IdHistoria, e.IdProfesional, e.IdTurno?.ToString() ?? "",
            e.Fecha.ToString("yyyy-MM-ddTHH:mm:ss"), e.Enfoque ?? "", e.ModoRegistro ?? "",
            e.Diagnostico ?? "", e.Observaciones ?? "", e.PlanTratamiento ?? "", e.TipoCierre ?? "",
            e.ContenidoJson ?? "", e.FechaFirma?.ToString("yyyy-MM-ddTHH:mm:ss") ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canon)));
    }
}
