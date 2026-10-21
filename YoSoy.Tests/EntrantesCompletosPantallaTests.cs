using System.Text.RegularExpressions;

// La bandeja y whatsapp-service con lo que traen los mensajes completos. Estas pruebas LEEN el código de la pantalla y del servicio (no lo ejecutan): el comportamiento real de la
// interfaz se mira con un navegador contra la API (ver el reporte de la rama) y el del servicio, con `npm test` en whatsapp-service.
public class EntrantesCompletosPantallaTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Bandeja => File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "Pantalla_ChatSimulado", "js", "bandeja.js"));
    private static string Estilos => File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "Pantalla_ChatSimulado", "css", "bandeja.css"));

    // ───────────── Lo que no es texto: el aviso de que el archivo no se guarda (D4) ─────────────

    [Fact]
    public void Un_mensaje_con_un_tipo_distinto_de_texto_lleva_el_aviso_de_que_el_archivo_no_se_guarda()
    {
        var bandeja = Bandeja;

        Assert.Contains("El archivo no se guarda en YoSoy: mirá el teléfono del número.", bandeja);
        Assert.Matches(@"const sinArchivo = !!msg\.tipo && msg\.tipo !== ""texto"";", bandeja);
        Assert.Matches(@"if \(sinArchivo\) b\.appendChild\(YS\.el\(""div"", \{ class: ""bm-aviso""", bandeja);
        Assert.Contains(" bm--tipo", bandeja);                                   // y la burbuja se distingue
    }

    [Fact]
    public void El_aviso_de_que_el_archivo_no_se_guarda_se_escapa_como_cualquier_texto()
    {
        // El texto del aviso es una constante de la pantalla, y de todos modos entra por YS.esc.
        Assert.Matches(@"<span>\$\{YS\.esc\(AVISO_SIN_ARCHIVO\)\}</span>", Bandeja);
    }

    [Fact]
    public void Los_estilos_del_aviso_y_de_la_burbuja_con_tipo_existen()
    {
        var estilos = Estilos;

        Assert.Contains(".bm--tipo", estilos);
        Assert.Contains(".bm-aviso", estilos);
    }

    // ───────────── El nombre del perfil de WhatsApp (D5) ─────────────

    [Fact]
    public void Sin_paciente_la_lista_muestra_el_nombre_del_perfil_y_el_telefono_pasa_a_la_fila_de_abajo()
    {
        var bandeja = Bandeja;

        Assert.Contains("const quien = c.nombrePaciente || c.nombreContacto;", bandeja);
        Assert.Contains("const nombre = quien || telefonoVer(c);", bandeja);
        Assert.Contains("${quien ? `<span class=\"chats-conv-tel\">", bandeja);
        Assert.Contains("Nombre del perfil de WhatsApp: todavía no está vinculado a un paciente", bandeja);
    }

    [Fact]
    public void La_cabecera_y_el_panel_dicen_Contacto_con_el_nombre_del_perfil_solo_si_no_hay_paciente()
    {
        var bandeja = Bandeja;

        Assert.Contains("c.nombrePaciente || (c.nombreContacto ? `Contacto: ${c.nombreContacto}` : \"Paciente sin identificar\")", bandeja);
        Assert.Contains("p?.nombre || (info.nombreContacto ? `Contacto: ${info.nombreContacto}` : \"Paciente sin identificar\")", bandeja);
        Assert.Contains("info.nombreContacto ? dato(\"Perfil de WhatsApp\", info.nombreContacto) : \"\"", bandeja);
        Assert.Contains("no está confirmado", bandeja);
    }

    [Fact]
    public void La_busqueda_de_la_bandeja_tambien_mira_el_nombre_del_perfil()
    {
        Assert.Contains("YS.normalizar(c.nombreContacto).includes(q)", Bandeja);
    }

    [Fact]
    public void El_nombre_del_perfil_solo_se_inserta_en_el_HTML_escapado()
    {
        // Es texto que escribió un desconocido: toda línea de la pantalla que arma HTML con él tiene que pasarlo por YS.esc (o por el helper dato(), que lo escapa; YS.avatar y YS.normalizar tampoco lo vuelcan).
        var sueltas = Bandeja.Replace("\r\n", "\n").Split('\n')
            .Select((l, i) => (Linea: i + 1, Texto: l))
            .Where(x => Regex.IsMatch(x.Texto, @"\$\{[^}]*nombreContacto[^}]*\}") && !Regex.IsMatch(x.Texto, @"YS\.esc\(|YS\.avatar\(|YS\.normalizar\(|\bdato\(|const nombre = "))
            .Select(x => $"línea {x.Linea}: {x.Texto.Trim()}")
            .ToList();

        Assert.True(sueltas.Count == 0, "Estas líneas interpolan nombreContacto en HTML sin YS.esc:\n  " + string.Join("\n  ", sueltas));
    }

    [Fact]
    public void Las_iniciales_del_avatar_se_toman_por_caracter_y_no_parten_un_emoji()
    {
        // Los nombres de perfil de WhatsApp traen emojis ("Juan Pérez 🌸"): con la primera unidad de texto el avatar mostraba "J" y una mitad de emoji rota. Se mira el código porque la
        // función es de la pantalla; el resultado con un navegador real (JP, y los nombres de siempre sin cambios) lo comprueba el banco de la rama.
        var ui = File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "shared", "ui.js"));
        var funcion = Regex.Match(ui, @"YS\.iniciales = nombre => \{.*?\n    \};", RegexOptions.Singleline);
        Assert.True(funcion.Success, "no encontré YS.iniciales en PANTALLAS/shared/ui.js");

        Assert.Contains("Array.from(p)[0]", funcion.Value);                         // el primer carácter, no la primera unidad de texto
        Assert.Contains(@"/^[\p{L}\p{N}]/u", funcion.Value);                        // las palabras que no empiezan con una letra o un número no aportan una inicial
        Assert.DoesNotMatch(@"partes\[0\]\[0\]", funcion.Value);
    }

    // ───────────── whatsapp-service y la API hablan de lo mismo ─────────────

    [Fact]
    public void Los_avisos_de_whatsapp_service_son_los_mismos_que_arma_la_API_y_cubren_todos_los_tipos_que_no_son_texto()
    {
        var js = File.ReadAllText(Path.Combine(Raiz, "whatsapp-service", "baileys", "mensajes.js"));
        var bloque = Regex.Match(js, @"const AVISOS = Object\.freeze\(\{(?<cuerpo>.*?)\}\);", RegexOptions.Singleline);
        Assert.True(bloque.Success, "no encontré AVISOS en whatsapp-service/baileys/mensajes.js");

        var avisos = Regex.Matches(bloque.Groups["cuerpo"].Value, @"(?<tipo>\w+):\s*""(?<aviso>[^""]+)""")
            .ToDictionary(m => m.Groups["tipo"].Value, m => m.Groups["aviso"].Value);

        foreach (var (tipo, aviso) in avisos)
            Assert.Equal(aviso, TiposDeMensaje.Aviso(tipo));                      // el mismo texto en los dos lugares
        // Todos los tipos que existen, salvo "texto" (no tiene aviso) y "otro" (whatsapp-service no lo manda: lo arma la API si hace falta).
        Assert.Equal(TiposDeMensaje.Todos.Where(t => t is not "texto" and not "otro").OrderBy(t => t, StringComparer.Ordinal),
                     avisos.Keys.OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public void El_contrato_v2_del_webhook_tiene_los_mismos_campos_en_whatsapp_service_y_en_la_API()
    {
        var js = File.ReadAllText(Path.Combine(Raiz, "whatsapp-service", "reenvio.js"));
        var deLaApi = typeof(MensajeEntranteRequest).GetConstructors().Single().GetParameters()
            .Select(p => p.Name!.ToLowerInvariant()).ToList();
        var campos = Regex.Match(js, @"const CAMPOS_V2 = Object\.freeze\(\[(?<lista>[^\]]*)\]\);");
        Assert.True(campos.Success, "no encontré CAMPOS_V2 en whatsapp-service/reenvio.js");

        var delServicio = Regex.Matches(campos.Groups["lista"].Value, @"""(\w+)""").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(new[] { "tipo", "id", "ts", "nombre" }, delServicio);
        Assert.Equal(new[] { "telefono", "texto" }.Concat(delServicio), deLaApi);   // lo que manda el servicio es lo que la API recibe, en el mismo orden
    }
}
