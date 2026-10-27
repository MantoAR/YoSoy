using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Documentos clínicos (migración 35): el ÚNICO motor que arma un documento a partir de una plantilla. Lo usan la vista
// previa del Configurador, la vista previa del profesional, la emisión y el PDF: lo que se ve es lo que se emite.
//
// Formato de la plantilla:
//   CamposJson: [{ clave, etiqueta, tipo: texto|textoLargo|numero|fecha|hora|seleccion|check, requerido, opciones, ayuda,
//                  precarga, valorInicial }]
//   CuerpoJson: [{ tipo: titulo|subtitulo|parrafo|lista|firma, texto, condicion? }]
//     - El texto es plano: **negrita** y marcadores {{campo}} (un campo de la plantilla) o {{p.dato}} (un dato del sistema
//       de la lista permitida, ver DatosPermitidos). Sin HTML: lo que llega del usuario nunca se interpreta como marcado.
//     - lista: un ítem por línea. firma: un espacio de firma con su leyenda.
//     - condicion: { campo, op: tieneValor|igual|distinto|mayorIgual, valor }. Si no se cumple, el bloque no aparece.
//   Al PUBLICAR, Validar rechaza marcadores desconocidos, campos inexistentes y precargas fuera de la lista.
public sealed record CampoPlantilla(string Clave, string Etiqueta, string Tipo, bool Requerido = false, List<string>? Opciones = null,
    string? Ayuda = null, string? Precarga = null, string? ValorInicial = null);
public sealed record CondicionBloque(string Campo, string Op, string? Valor = null);
public sealed record BloquePlantilla(string Tipo, string Texto, CondicionBloque? Condicion = null);
public sealed record Segmento(string Texto, bool Negrita);
public sealed record BloqueRenderizado(string Tipo, List<List<Segmento>> Lineas);
public sealed record DocumentoRenderizado(string Titulo, bool Provisoria, List<BloqueRenderizado> Bloques);

public static class RenderizadorDocumento
{
    public static readonly string[] TiposCampo = { "texto", "textoLargo", "numero", "fecha", "hora", "seleccion", "check" };
    public static readonly string[] TiposBloque = { "titulo", "subtitulo", "parrafo", "lista", "firma" };
    public static readonly string[] Operadores = { "tieneValor", "igual", "distinto", "mayorIgual" };

    /// <summary>Datos del sistema que se pueden precargar en un campo o insertar con {{p.dato}}. Nada más.</summary>
    public static readonly IReadOnlyDictionary<string, string> DatosPermitidos = new Dictionary<string, string>
    {
        ["paciente.nombre"] = "Nombre del paciente",
        ["paciente.dni"] = "DNI del paciente",
        ["paciente.edad"] = "Edad del paciente",
        ["tutor1.nombre"] = "Responsable 1: nombre",
        ["tutor1.dni"] = "Responsable 1: DNI",
        ["tutor1.vinculo"] = "Responsable 1: vínculo",
        ["tutor2.nombre"] = "Responsable 2: nombre",
        ["tutor2.dni"] = "Responsable 2: DNI",
        ["tutor2.vinculo"] = "Responsable 2: vínculo",
        ["profesional.nombre"] = "Nombre del profesional",
        ["profesional.matricula"] = "Matrícula del profesional",
        ["profesional.especialidad"] = "Especialidad del profesional",
        ["turno.fecha"] = "Fecha del turno",
        ["turno.horaInicio"] = "Hora de inicio del turno",
        ["turno.horaFin"] = "Hora de fin del turno",
        ["tratamiento.fechaInicio"] = "Inicio del tratamiento",
        ["hoy"] = "Fecha de hoy",
        ["organizacion.localidad"] = "Localidad de la organización",
        ["organizacion.nombre"] = "Nombre de la organización",
    };

    private static readonly Regex Marcador = new(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex ClaveValida = new(@"^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.Compiled);
    private const string Blanco = "______________";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static List<CampoPlantilla> LeerCampos(string json) =>
        JsonSerializer.Deserialize<List<CampoPlantilla>>(json, Json) ?? new();
    public static List<BloquePlantilla> LeerCuerpo(string json) =>
        JsonSerializer.Deserialize<List<BloquePlantilla>>(json, Json) ?? new();
    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Json);

    /// <summary>SHA-256 de una versión de plantilla: CamposJson + salto de línea + CuerpoJson, en UTF-8 (igual que la migración 35).</summary>
    public static string HashVersion(string camposJson, string cuerpoJson) => Sha256(camposJson + "\n" + cuerpoJson);

    public static string Sha256(string texto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
    public static string Sha256(byte[] datos) => Convert.ToHexString(SHA256.HashData(datos)).ToLowerInvariant();

    // ─────────────────────────────── Validación de la plantilla (al publicar) ───────────────────────────────
    /// <summary>Errores de la plantilla, con mensajes para el Configurador. Lista vacía = se puede publicar.</summary>
    public static List<string> Validar(string camposJson, string cuerpoJson)
    {
        var errores = new List<string>();
        List<CampoPlantilla> campos; List<BloquePlantilla> cuerpo;
        try { campos = LeerCampos(camposJson); } catch (JsonException) { return new() { "Los campos no tienen un formato válido." }; }
        try { cuerpo = LeerCuerpo(cuerpoJson); } catch (JsonException) { return new() { "El cuerpo no tiene un formato válido." }; }

        var claves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in campos)
        {
            var nombre = string.IsNullOrWhiteSpace(c.Etiqueta) ? c.Clave : c.Etiqueta;
            if (string.IsNullOrWhiteSpace(c.Clave) || !ClaveValida.IsMatch(c.Clave)) errores.Add($"El campo \"{nombre}\" tiene una clave inválida (letras, números y guion bajo).");
            else if (!claves.Add(c.Clave)) errores.Add($"Hay dos campos con la clave \"{c.Clave}\".");
            if (string.IsNullOrWhiteSpace(c.Etiqueta)) errores.Add($"El campo \"{c.Clave}\" no tiene etiqueta.");
            if (!TiposCampo.Contains(c.Tipo)) errores.Add($"El campo \"{nombre}\" tiene un tipo desconocido: {c.Tipo}.");
            if (c.Tipo == "seleccion" && (c.Opciones is null || c.Opciones.Count(o => !string.IsNullOrWhiteSpace(o)) < 2))
                errores.Add($"El campo \"{nombre}\" es una selección y necesita al menos dos opciones.");
            if (c.Precarga is not null && !DatosPermitidos.ContainsKey(c.Precarga))
                errores.Add($"El campo \"{nombre}\" se precarga con un dato que no está permitido: {c.Precarga}.");
        }

        if (cuerpo.Count == 0) errores.Add("El documento no tiene ningún bloque.");
        for (var i = 0; i < cuerpo.Count; i++)
        {
            var b = cuerpo[i];
            var donde = $"Bloque {i + 1}";
            if (!TiposBloque.Contains(b.Tipo)) errores.Add($"{donde}: tipo desconocido ({b.Tipo}).");
            if (string.IsNullOrWhiteSpace(b.Texto)) errores.Add($"{donde}: está vacío.");
            if (b.Texto?.Contains('<') == true && Regex.IsMatch(b.Texto, @"<\s*[a-zA-Z/!]"))
                errores.Add($"{donde}: no se admite HTML; usá **negrita** y los marcadores.");
            foreach (Match m in Marcador.Matches(b.Texto ?? ""))
            {
                var k = m.Groups[1].Value;
                if (k.StartsWith("p.", StringComparison.Ordinal))
                {
                    if (!DatosPermitidos.ContainsKey(k[2..])) errores.Add($"{donde}: el dato {{{{{k}}}}} no existe.");
                }
                else if (!claves.Contains(k)) errores.Add($"{donde}: el marcador {{{{{k}}}}} no corresponde a ningún campo.");
            }
            if (b.Condicion is CondicionBloque cnd)
            {
                if (!claves.Contains(cnd.Campo)) errores.Add($"{donde}: la condición usa un campo que no existe ({cnd.Campo}).");
                if (!Operadores.Contains(cnd.Op)) errores.Add($"{donde}: la condición tiene una operación desconocida ({cnd.Op}).");
                if (cnd.Op is "igual" or "distinto" && cnd.Valor is null) errores.Add($"{donde}: la condición necesita un valor.");
                if (cnd.Op == "mayorIgual" && !decimal.TryParse(cnd.Valor, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    errores.Add($"{donde}: la condición \"mayor o igual\" necesita un número.");
            }
        }
        if (!cuerpo.Any(b => b.Tipo == "titulo")) errores.Add("El documento necesita un bloque de título.");
        return errores;
    }

    // ─────────────────────────────── Datos que carga el profesional ───────────────────────────────
    /// <summary>
    /// Limpia y valida lo que cargó el profesional: solo las claves de la plantilla, con su tipo. Devuelve los datos limpios
    /// y los errores (por campo). Con <paramref name="estricto"/> en false (vista previa) no exige los requeridos.
    /// </summary>
    public static (Dictionary<string, string> Datos, Dictionary<string, string> Errores) LimpiarDatos(
        IReadOnlyList<CampoPlantilla> campos, IDictionary<string, string?>? entrada, bool estricto)
    {
        var datos = new Dictionary<string, string>(StringComparer.Ordinal);
        var errores = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in campos)
        {
            var v = entrada is not null && entrada.TryGetValue(c.Clave, out var x) ? x?.Trim() : null;
            if (c.Tipo == "check") { datos[c.Clave] = v is "true" or "on" or "1" ? "true" : "false"; continue; }
            if (string.IsNullOrEmpty(v))
            {
                if (estricto && c.Requerido) errores[c.Clave] = "Completá este dato.";
                continue;
            }
            var max = c.Tipo == "textoLargo" ? 4000 : 300;
            if (v.Length > max) { errores[c.Clave] = $"Hasta {max} caracteres."; continue; }
            switch (c.Tipo)
            {
                case "numero" when !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _):
                    errores[c.Clave] = "Tiene que ser un número."; continue;
                case "fecha" when !DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                    errores[c.Clave] = "Fecha inválida."; continue;
                case "hora" when !TimeSpan.TryParseExact(v.Length > 5 ? v[..5] : v, @"hh\:mm", CultureInfo.InvariantCulture, out _):
                    errores[c.Clave] = "Hora inválida (HH:MM)."; continue;
                case "seleccion" when c.Opciones is null || !c.Opciones.Contains(v):
                    errores[c.Clave] = "Elegí una de las opciones."; continue;
            }
            datos[c.Clave] = c.Tipo == "hora" && v.Length > 5 ? v[..5] : v;
        }
        return (datos, errores);
    }

    // ─────────────────────────────── Render ───────────────────────────────
    /// <summary>
    /// Arma el documento. <paramref name="datos"/> son los campos (ya limpios); <paramref name="sistema"/>, los datos del
    /// sistema para {{p.dato}} (claves de DatosPermitidos). Un valor que falta queda como una línea para completar a mano.
    /// </summary>
    public static DocumentoRenderizado Renderizar(string titulo, bool provisoria, IReadOnlyList<CampoPlantilla> campos,
        IReadOnlyList<BloquePlantilla> cuerpo, IReadOnlyDictionary<string, string> datos, IReadOnlyDictionary<string, string?> sistema)
    {
        var tipos = campos.GroupBy(c => c.Clave, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Tipo, StringComparer.Ordinal);
        string Valor(string clave)
        {
            if (clave.StartsWith("p.", StringComparison.Ordinal))
                return sistema.TryGetValue(clave[2..], out var s) && !string.IsNullOrWhiteSpace(s) ? s! : Blanco;
            if (!datos.TryGetValue(clave, out var v) || string.IsNullOrWhiteSpace(v)) return Blanco;
            return Formatear(tipos.GetValueOrDefault(clave, "texto"), v);
        }

        var bloques = new List<BloqueRenderizado>();
        foreach (var b in cuerpo)
        {
            if (b.Condicion is CondicionBloque c && !Cumple(c, datos)) continue;
            var lineas = (b.Tipo == "lista" ? b.Texto.Split('\n') : new[] { b.Texto.Replace("\r", "").Replace('\n', ' ') })
                .Select(l => l.Trim()).Where(l => l.Length > 0)
                .Select(l => Segmentos(Marcador.Replace(l, m => Valor(m.Groups[1].Value))))
                .ToList();
            if (lineas.Count > 0) bloques.Add(new BloqueRenderizado(b.Tipo, lineas));
        }
        return new DocumentoRenderizado(titulo, provisoria, bloques);
    }

    public static bool Cumple(CondicionBloque c, IReadOnlyDictionary<string, string> datos)
    {
        var v = datos.TryGetValue(c.Campo, out var x) ? x?.Trim() ?? "" : "";
        return c.Op switch
        {
            "tieneValor" => v.Length > 0 && v != "false",
            "igual" => string.Equals(v, c.Valor?.Trim() ?? "", StringComparison.OrdinalIgnoreCase),
            "distinto" => !string.Equals(v, c.Valor?.Trim() ?? "", StringComparison.OrdinalIgnoreCase),
            "mayorIgual" => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var a)
                            && decimal.TryParse(c.Valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var b) && a >= b,
            _ => false
        };
    }

    private static string Formatear(string tipo, string v) => tipo switch
    {
        "fecha" => DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f.ToString("dd/MM/yyyy") : v,
        "check" => v == "true" ? "Sí" : "No",
        _ => v
    };

    /// <summary>"Texto con **negrita**" en segmentos. Los ** sueltos quedan como texto.</summary>
    public static List<Segmento> Segmentos(string texto)
    {
        var partes = texto.Split("**");
        var r = new List<Segmento>();
        if (partes.Length % 2 == 0)   // cantidad impar de "**": no se interpreta nada
            return new() { new Segmento(texto, false) };
        for (var i = 0; i < partes.Length; i++)
            if (partes[i].Length > 0) r.Add(new Segmento(partes[i], i % 2 == 1));
        return r;
    }

    /// <summary>Texto plano del documento (para la búsqueda y para el hash).</summary>
    public static string TextoPlano(DocumentoRenderizado d) =>
        d.Titulo + "\n" + string.Join("\n", d.Bloques.SelectMany(b => b.Lineas).Select(l => string.Concat(l.Select(s => s.Texto))));

    // ─────────────────────────────── Datos de ejemplo (vista previa del Configurador) ───────────────────────────────
    /// <summary>Datos inventados para la vista previa de una plantilla: nunca de un paciente real.</summary>
    public static (Dictionary<string, string> Datos, Dictionary<string, string?> Sistema) Ejemplo(IReadOnlyList<CampoPlantilla> campos, DateTime hoy)
    {
        var sistema = new Dictionary<string, string?>
        {
            ["paciente.nombre"] = "Ana Ejemplo", ["paciente.dni"] = "30.000.000", ["paciente.edad"] = "14",
            ["tutor1.nombre"] = "Laura Ejemplo", ["tutor1.dni"] = "20.000.000", ["tutor1.vinculo"] = "madre",
            ["tutor2.nombre"] = "Jorge Ejemplo", ["tutor2.dni"] = "21.000.000", ["tutor2.vinculo"] = "padre",
            ["profesional.nombre"] = "Profesional de Ejemplo", ["profesional.matricula"] = "MP 0000", ["profesional.especialidad"] = "Psicología",
            ["turno.fecha"] = hoy.ToString("yyyy-MM-dd"), ["turno.horaInicio"] = "10:00", ["turno.horaFin"] = "10:50",
            ["tratamiento.fechaInicio"] = hoy.AddMonths(-3).ToString("yyyy-MM-dd"), ["hoy"] = hoy.ToString("yyyy-MM-dd"),
            ["organizacion.localidad"] = "Córdoba", ["organizacion.nombre"] = "Organización de ejemplo",
        };
        var datos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in campos)
        {
            string? v = c.Precarga is not null ? sistema.GetValueOrDefault(c.Precarga) : c.ValorInicial;
            v ??= c.Tipo switch
            {
                "seleccion" => c.Opciones?.FirstOrDefault(),
                "check" => "true",
                "numero" => "1",
                "fecha" => hoy.ToString("yyyy-MM-dd"),
                "hora" => "10:00",
                _ => $"[{c.Etiqueta}]"
            };
            if (v is not null) datos[c.Clave] = v;
        }
        return (datos, FormatearSistema(sistema));
    }

    /// <summary>Las fechas del sistema van como AAAA-MM-DD (para precargar campos de fecha); en {{p.dato}} se muestran dd/mm/aaaa.</summary>
    public static Dictionary<string, string?> FormatearSistema(IReadOnlyDictionary<string, string?> sistema) =>
        sistema.ToDictionary(kv => kv.Key, kv => kv.Value is string s && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
            ? f.ToString("dd/MM/yyyy") : kv.Value);
}
