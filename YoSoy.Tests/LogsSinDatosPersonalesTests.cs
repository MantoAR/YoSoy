using System.Text;
using System.Text.RegularExpressions;

// Los logs de la API no escriben datos personales ni secretos: ningún mail, DNI, matrícula, teléfono, contraseña, token ni
// el texto de un mensaje de un paciente. Esta prueba LEE EL CÓDIGO (no lo ejecuta): recorre todas las llamadas a ILogger
// (Log*) y a Console.Write* de la API y falla si alguna interpola un dato así, ya sea en la plantilla ("{Mail}"), en una
// cadena interpolada ($"...{p.Dni}...") o en un argumento (p.Dni). Las palabras comunes dentro del texto no cuentan
// ("no se pudo mandar el mail" está bien); sí cuentan los nombres de lo que se interpola.
//
// Es una red de contención y no un análisis completo: no ve un mail que viaja dentro de un mensaje de error ({Error}) ni lo
// que escribe el propio framework (ver docs/PROGRESO_NOCTURNO.md). Si hace falta loguear un dato así, se enmascara con una
// función que se llame Enmascarar*, Ocultar*, Anonimizar* o Mascar* y la llamada deja de marcarse.
public class LogsSinDatosPersonalesTests
{
    // Palabras (en minúsculas) que, como parte del nombre de lo que se interpola, lo vuelven un dato personal o un secreto.
    private static readonly HashSet<string> PalabrasSensibles = new()
    {
        "dni", "documento", "mail", "email", "correo", "telefono", "celular", "matricula", "texto",
        "contrasena", "password", "passwd", "token", "secreto", "clave", "domicilio", "direccion", "apellido",
        // El nombre del perfil de WhatsApp (Conversacion.NombreContacto, decisión D5): se guarda en la conversación y nunca va a un log.
        "contacto", "perfil"
    };

    private static readonly Regex InicioDeLlamada = new(
        @"\b(?:[A-Za-z_]\w*\.)*Log(?:Trace|Debug|Information|Warning|Error|Critical)\s*\(|\bConsole\.(?:Error\.)?Write(?:Line)?\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex Enmascara = new(@"\b(?:Enmascarar|Ocultar|Anonimizar|Mascar)\w*\s*\(", RegexOptions.Compiled);
    private static readonly Regex Identificador = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    // ---------- Lo que dice el código ----------

    [Fact]
    public void Ninguna_llamada_de_log_de_la_API_interpola_un_dato_personal()
    {
        var hallazgos = new List<string>();
        foreach (var archivo in ArchivosDeLaApi())
        {
            var codigo = File.ReadAllText(archivo);
            foreach (var (linea, texto) in LlamadasDeLog(codigo))
                if (DatosPersonalesEn(texto) is { } dato)
                    hallazgos.Add($"{Path.GetRelativePath(CarpetaDelProyecto(), archivo)}:{linea} interpola «{dato}»");
        }

        Assert.True(hallazgos.Count == 0,
            "Estas llamadas de log interpolan un dato personal (enmascaralo o sacalo): " + string.Join("; ", hallazgos));
    }

    // Para que la prueba de arriba no pase "en vacío" si algún día el escáner deja de encontrar las llamadas.
    [Fact]
    public void El_escaner_encuentra_las_llamadas_de_log_que_hay()
    {
        var llamadas = ArchivosDeLaApi().SelectMany(a => LlamadasDeLog(File.ReadAllText(a))).Count();

        Assert.True(llamadas >= 30, $"Se esperaban muchas llamadas de log en el código y se encontraron {llamadas}.");
    }

    // ---------- El detector ----------

    [Theory]
    [InlineData("_log.LogWarning(\"No se pudo enviar a {Mail}\", mail);", "Mail")]
    [InlineData("_log.LogError(ex, \"Error con el paciente {Dni}\", p.Dni);", "Dni")]
    [InlineData("log.LogInformation($\"Paciente {paciente.Dni} creado\");", "Dni")]
    [InlineData("Console.WriteLine($\"Turno de {t.MailPaciente}\");", "MailPaciente")]
    [InlineData("_log.LogWarning(\"Falló {Valor}\", usuario.Email);", "Email")]                       // el placeholder es genérico, el argumento no
    [InlineData("_log.LogWarning(\"Falló el envío \" +\n    \"a {Telefono}\", tel);", "Telefono")]       // plantilla en dos renglones
    [InlineData("_log.LogInformation(\"Mensaje de {Paciente}: {Texto}\", n, m.Texto);", "Texto")]
    [InlineData("_log.LogDebug(\"Matrícula {Matricula}\", p.Matricula);", "Matricula")]
    [InlineData("_log.LogWarning(\"Contraseña {NuevaPassword}\", req.NuevaPassword);", "NuevaPassword")]
    [InlineData("_log.LogWarning(\"Token {TokenCrudo}\", tokenCrudo);", "TokenCrudo")]
    [InlineData("_log.LogWarning(\"Falló {Nombre}\", t.NombrePaciente);", "NombrePaciente")]            // nombre + paciente
    [InlineData("Console.Error.WriteLine(\"Falló \" + usuario.Mail);", "Mail")]
    [InlineData("_log.LogInformation(\"Escribió {Quien}\", c.NombreContacto);", "NombreContacto")]    // el nombre del perfil de WhatsApp (D5)
    [InlineData("_log.LogInformation($\"Entró un mensaje de {datos.NombreContacto}\");", "NombreContacto")]
    public void Detecta_el_dato_personal_en_la_llamada(string llamada, string esperado)
    {
        var encontradas = LlamadasDeLog(llamada).ToList();

        Assert.Single(encontradas);
        var dato = DatosPersonalesEn(encontradas[0].Texto);
        Assert.NotNull(dato);
        Assert.Equal(esperado, dato, ignoreCase: true);
    }

    [Theory]
    [InlineData("_log.LogWarning(\"No se pudo mandar el mail al profesional {IdProfesional}: {Error}\", t.IdProfesional, error);")]
    [InlineData("_log.LogInformation(\"Video: lectura de emociones habilitada (cifrado configurado, clave {IdClave}).\", o.IdClave);")]
    [InlineData("Logger.LogWarning(\"Identidad resuelta en {Metodo} {Ruta}\", Request.Method, Request.Path);")]
    [InlineData("_log.LogWarning(\"Correo no configurado: falta {Claves}\", string.Join(\", \", faltan));")]
    [InlineData("_log.LogError(ex, \"Error enviando el aviso {Tipo} del turno {IdTurno} por {Canal}\", tipo, idTurno, canal);")]
    [InlineData("_log.LogWarning(\"El teléfono del paciente no es válido.\");")]                        // palabras en el texto, sin interpolar nada
    [InlineData("_log.LogWarning(\"Mail {Mail}\", Enmascarar(mail));")]                                // enmascarado a propósito
    [InlineData("Console.WriteLine(Ayuda);")]
    [InlineData("Console.Error.WriteLine($\"No se pudo trabajar con la base ({e.GetType().Name}).\");")]
    public void No_marca_lo_que_no_lleva_datos_personales(string llamada)
    {
        var encontradas = LlamadasDeLog(llamada).ToList();

        Assert.Single(encontradas);
        Assert.Null(DatosPersonalesEn(encontradas[0].Texto));
    }

    [Fact]
    public void Una_cadena_con_parentesis_y_comillas_adentro_no_confunde_al_escaner()
    {
        var codigo = "_log.LogWarning(\"Falló (algo) con \\\"comillas\\\" y {Motivo}\", motivo); var x = 1;\n" +
                     "_log.LogWarning(\"Segunda con {Dni}\", d);";

        var llamadas = LlamadasDeLog(codigo).ToList();

        Assert.Equal(2, llamadas.Count);
        Assert.Null(DatosPersonalesEn(llamadas[0].Texto));
        Assert.Equal("Dni", DatosPersonalesEn(llamadas[1].Texto));
    }

    [Fact]
    public void Un_renglon_con_dos_llamadas_se_lee_como_dos()
    {
        var llamadas = LlamadasDeLog("a(); _log.LogInformation(\"uno {Id}\", i); _log.LogWarning(\"dos {Mail}\", m);").ToList();

        Assert.Equal(2, llamadas.Count);
        Assert.Null(DatosPersonalesEn(llamadas[0].Texto));
        Assert.Equal("Mail", DatosPersonalesEn(llamadas[1].Texto));
    }

    [Fact]
    public void Un_nombre_que_empieza_con_Id_es_un_identificador_y_no_un_dato()
    {
        Assert.True(EsNombreSensible("Mail"));
        Assert.True(EsNombreSensible("MailPaciente"));
        Assert.True(EsNombreSensible("nuevaPassword"));
        Assert.True(EsNombreSensible("telefono_destino"));
        Assert.False(EsNombreSensible("IdClave"));
        Assert.False(EsNombreSensible("idToken"));
        Assert.False(EsNombreSensible("IdProfesional"));
        Assert.False(EsNombreSensible("Metodo"));
        Assert.False(EsNombreSensible("Error"));
    }

    // ---------- El escáner ----------

    private static string CarpetaDelProyecto()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "YoSoy.csproj"))) return d.FullName;
        throw new InvalidOperationException("No encontré la carpeta del proyecto (YoSoy.csproj) subiendo desde " + AppContext.BaseDirectory);
    }

    // Las carpetas que no son código de la API: no se recorren (node_modules tiene miles de archivos).
    private static readonly string[] CarpetasExcluidas =
    {
        "bin", "obj", "YoSoy.Tests", "publish-vps", "node_modules", "migrations", ".git", ".vs", ".vscode",
        "PANTALLAS", "docs", "deploy", "whatsapp-service", "signaling-server"
    };

    private static IEnumerable<string> ArchivosDeLaApi() => Recorrer(CarpetaDelProyecto());

    private static IEnumerable<string> Recorrer(string carpeta)
    {
        foreach (var archivo in Directory.EnumerateFiles(carpeta, "*.cs")) yield return archivo;
        foreach (var sub in Directory.EnumerateDirectories(carpeta))
            if (!CarpetasExcluidas.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                foreach (var archivo in Recorrer(sub)) yield return archivo;
    }

    // Cada llamada a Log*/Console.Write*: el número de renglón y el texto entre su "(" y el ")" que la cierra.
    private static IEnumerable<(int Linea, string Texto)> LlamadasDeLog(string codigo)
    {
        foreach (Match m in InicioDeLlamada.Matches(codigo))
        {
            var abre = m.Index + m.Length - 1;
            if (CerrarParentesis(codigo, abre) is not { } cierra) continue;
            var linea = codigo.AsSpan(0, m.Index).Count('\n') + 1;
            yield return (linea, codigo.Substring(abre + 1, cierra - abre - 1));
        }
    }

    // El primer dato personal que interpola la llamada (el nombre de lo que se interpola), o null.
    private static string? DatosPersonalesEn(string textoDeLaLlamada)
    {
        if (Enmascara.IsMatch(textoDeLaLlamada)) return null;      // se enmascara a propósito
        var (interpolados, codigo) = Separar(textoDeLaLlamada);
        foreach (var texto in interpolados.Append(codigo))
            foreach (Match id in Identificador.Matches(texto))
                if (EsNombreSensible(id.Value)) return id.Value;
        return null;
    }

    private static bool EsNombreSensible(string identificador)
    {
        var palabras = Regex.Split(identificador, @"(?<=[a-z0-9])(?=[A-Z])|_")
            .Where(p => p.Length > 0).Select(p => p.ToLowerInvariant()).ToList();
        if (palabras.Count == 0) return false;
        if (palabras[0] == "id" && palabras.Count > 1) return false;           // IdClave, IdToken...: un identificador, no el dato
        if (palabras.Contains("nombre") && palabras.Contains("paciente")) return true;
        return palabras.Any(PalabrasSensibles.Contains);
    }

    // Separa la llamada en lo que está DENTRO de las cadenas ({Placeholder} de una plantilla o {expresión} de una cadena
    // interpolada) y el código que está FUERA de ellas (los argumentos). Las palabras comunes del texto no cuentan.
    private static (List<string> Interpolados, string Codigo) Separar(string texto)
    {
        var interpolados = new List<string>();
        var codigo = new StringBuilder();
        for (var i = 0; i < texto.Length; i++)
        {
            if (SaltarComentarioOCaracter(texto, ref i)) { codigo.Append(' '); continue; }
            if (EsInicioDeCadena(texto, i, out var comilla, out var literal, out var interpolada))
            {
                i = LeerCadena(texto, comilla, literal, interpolada, interpolados);
                codigo.Append(' ');
                continue;
            }
            codigo.Append(texto[i]);
        }
        return (interpolados, codigo.ToString());
    }

    private static int? CerrarParentesis(string s, int abre)
    {
        var nivel = 0;
        for (var i = abre; i < s.Length; i++)
        {
            if (SaltarComentarioOCaracter(s, ref i)) continue;
            if (EsInicioDeCadena(s, i, out var comilla, out var literal, out var interpolada))
            {
                i = LeerCadena(s, comilla, literal, interpolada, null);
                continue;
            }
            if (s[i] == '(') nivel++;
            else if (s[i] == ')' && --nivel == 0) return i;
        }
        return null;
    }

    // Comentarios (// y /* */) y literales de carácter ('(' '\'' ...): se saltan enteros. Deja i en su último carácter.
    private static bool SaltarComentarioOCaracter(string s, ref int i)
    {
        if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
        {
            var fin = s.IndexOf('\n', i);
            i = fin < 0 ? s.Length - 1 : fin;
            return true;
        }
        if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
        {
            var fin = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
            i = fin < 0 ? s.Length - 1 : fin + 1;
            return true;
        }
        if (s[i] == '\'')
        {
            var j = i + 1;
            if (j < s.Length && s[j] == '\\') j++;
            j++;
            if (j < s.Length && s[j] == '\'') { i = j; return true; }
        }
        return false;
    }

    // $"..." @"..." $@"..." @$"..." "..."  → la posición de la comilla que abre.
    private static bool EsInicioDeCadena(string s, int i, out int comilla, out bool literal, out bool interpolada)
    {
        comilla = -1; literal = false; interpolada = false;
        var j = i;
        while (j < s.Length && (s[j] == '$' || s[j] == '@') && j - i < 2)
        {
            if (s[j] == '@') literal = true; else interpolada = true;
            j++;
        }
        if (j < s.Length && s[j] == '"' && (j == i || i == 0 || !char.IsLetterOrDigit(s[i - 1]) && s[i - 1] != '_'))
        {
            comilla = j;
            return true;
        }
        return false;
    }

    // Lee la cadena que abre en `comilla` y devuelve la posición de la comilla que la cierra. Si `interpolados` no es
    // null, le agrega lo que hay entre llaves (la expresión de una cadena interpolada o el placeholder de una plantilla).
    private static int LeerCadena(string s, int comilla, bool literal, bool interpolada, List<string>? interpolados)
    {
        var contenido = new StringBuilder();
        for (var i = comilla + 1; i < s.Length; i++)
        {
            var c = s[i];
            if (literal && c == '"' && i + 1 < s.Length && s[i + 1] == '"') { contenido.Append("\"\""); i++; continue; }
            if (!literal && c == '\\' && i + 1 < s.Length) { contenido.Append(c).Append(s[i + 1]); i++; continue; }
            if (c == '"')
            {
                if (!interpolada) AgregarPlaceholders(contenido.ToString(), interpolados);
                return i;
            }
            if (interpolada && c == '{')
            {
                if (i + 1 < s.Length && s[i + 1] == '{') { contenido.Append("{{"); i++; continue; }
                var fin = CerrarLlave(s, i);
                interpolados?.Add(s.Substring(i + 1, fin - i - 1));
                i = fin;
                continue;
            }
            contenido.Append(c);
        }
        return s.Length - 1;
    }

    private static int CerrarLlave(string s, int abre)
    {
        var nivel = 0;
        for (var i = abre; i < s.Length; i++)
        {
            if (EsInicioDeCadena(s, i, out var comilla, out var literal, out var interpolada)) { i = LeerCadena(s, comilla, literal, interpolada, null); continue; }
            if (s[i] == '{') nivel++;
            else if (s[i] == '}' && --nivel == 0) return i;
        }
        return s.Length - 1;
    }

    private static void AgregarPlaceholders(string plantilla, List<string>? destino)
    {
        if (destino is null) return;
        foreach (Match m in Regex.Matches(plantilla, @"(?<!\{)\{([^{}]+)\}(?!\})"))
            destino.Add(m.Groups[1].Value);
    }
}
