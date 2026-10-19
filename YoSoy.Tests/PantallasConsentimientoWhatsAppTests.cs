using System.Text.RegularExpressions;

// La interfaz del consentimiento de WhatsApp (registro de paciente y perfil), vigilada leyendo los archivos de PANTALLAS/: lo que no se puede
// romper sin que alguien lo note. Lo que hace la interfaz en un navegador de verdad lo prueba el banco de Playwright (no versionado, ver
// docs/PROGRESO_CONSENTIMIENTO_WHATSAPP.md); acá quedan las reglas que son de código:
//  · la casilla nunca viene tildada, ni nada la tilda por su cuenta;
//  · el texto que se ve es el de la API (no uno escrito en la pantalla), y nada de lo que manda el servidor entra como HTML;
//  · el consentimiento no se guarda en el navegador.
public class PantallasConsentimientoWhatsAppTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(partes).ToArray()));

    private static string Registro => Leer("PANTALLAS", "Pantalla_Usuario", "Usuario", "registro.html");
    private static string RegistroJs => Leer("PANTALLAS", "Pantalla_Usuario", "Usuario", "js", "app.js");
    private static string Dashboard => Leer("PANTALLAS", "Pantalla_Paciente", "js", "dashboard.js");
    private static string DashboardHtml => Leer("PANTALLAS", "Pantalla_Paciente", "dashboard.html");
    private static string Modulo => Leer("PANTALLAS", "shared", "consentimiento-whatsapp.js");

    /// <summary>Las funciones de los avisos por WhatsApp del perfil: desde su comentario de apertura hasta la próxima sección.</summary>
    private static string SeccionDelPerfil()
    {
        var js = Dashboard;
        var inicio = js.IndexOf("// ── AVISOS POR WHATSAPP", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "no encuentro la sección de avisos por WhatsApp en dashboard.js");
        var fin = js.IndexOf("function renderSesion()", inicio, StringComparison.Ordinal);
        Assert.True(fin > inicio, "no encuentro el final de la sección de avisos por WhatsApp en dashboard.js");
        return js[inicio..fin];
    }

    // ───────────── El registro ─────────────

    [Fact]
    public void La_casilla_del_registro_viene_sin_tildar_es_opcional_y_el_navegador_no_la_recuerda()
    {
        var html = Registro;
        var casilla = Regex.Match(html, @"<input\b[^>]*\bid=""aceptaWhatsApp""[^>]*>", RegexOptions.Singleline).Value;

        Assert.NotEmpty(casilla);
        Assert.Contains("type=\"checkbox\"", casilla);
        Assert.DoesNotMatch(@"(?i)\bchecked\b", casilla);          // nunca tildada de fábrica
        Assert.DoesNotMatch(@"(?i)\brequired\b", casilla);         // y opcional
        Assert.Contains("autocomplete=\"off\"", casilla);          // el navegador no restaura el tilde al recargar
        Assert.Matches(@"<div id=""bloqueWhatsApp"" class=""oculto"">", html);   // y el bloque solo aparece cuando llega el texto de la API
    }

    [Fact]
    public void El_registro_carga_el_modulo_compartido_antes_que_su_script()
    {
        var html = Registro;

        var modulo = html.IndexOf("shared/consentimiento-whatsapp.js", StringComparison.Ordinal);
        var script = html.IndexOf("js/app.js", StringComparison.Ordinal);
        Assert.True(modulo > 0 && script > modulo, "consentimiento-whatsapp.js tiene que cargarse antes que app.js");
        Assert.True(html.IndexOf("shared/ui.js", StringComparison.Ordinal) < modulo, "y después de ui.js, que usa");
        Assert.True(File.Exists(Path.Combine(Raiz, "PANTALLAS", "shared", "consentimiento-whatsapp.js")));
    }

    [Fact]
    public void El_registro_destilda_la_casilla_y_manda_solo_lo_que_la_persona_tildo()
    {
        var js = RegistroJs;

        Assert.True(Regex.Matches(js, @"caja\.checked = false;").Count >= 2, "se destilda a mano al cargar y otra vez al dibujar el texto");
        // Lo que se manda sale del tilde de la persona (y de que haya texto), y la versión es la del texto que se le mostró.
        Assert.Contains(@"document.getElementById(""aceptaWhatsApp"")?.checked", js);
        Assert.Contains("paciente.aceptaWhatsApp = quiereWhatsApp;", js);
        Assert.Contains("paciente.versionTextoWhatsApp = quiereWhatsApp ? textoWhatsApp.version : null;", js);
        // Sin texto de la API no se ofrece nada.
        Assert.Matches(@"if \(!textoWhatsApp\) return;\s+YS\.whatsapp\.dibujarTexto", js);
    }

    [Fact]
    public void El_registro_valida_el_telefono_y_la_edad_antes_de_mandar_la_casilla_tildada()
    {
        var js = RegistroJs;

        Assert.Contains(@"if (!valor(""telefono"").trim())", js);
        Assert.Contains("if (edad < 18)", js);
        Assert.Contains(@"YS.whatsapp.motivo(""menor-de-edad"")", js);
    }

    // ───────────── Nada tilda la casilla por su cuenta ─────────────

    [Theory]
    [InlineData("registro.html")]
    [InlineData("app.js")]
    [InlineData("dashboard.js")]
    [InlineData("consentimiento-whatsapp.js")]
    public void Ninguna_pantalla_tilda_la_casilla_de_WhatsApp_por_su_cuenta(string archivo)
    {
        var texto = archivo switch
        {
            "registro.html" => Registro,
            "app.js" => RegistroJs,
            "dashboard.js" => Dashboard,
            _ => Modulo
        };

        Assert.DoesNotMatch(@"\.checked\s*=\s*(true|!0|1)\b", texto);
        Assert.DoesNotMatch(@"checked\s*:\s*(true|!0|1)\b", texto);
        Assert.DoesNotMatch(@"defaultChecked", texto);
        Assert.DoesNotMatch(@"setAttribute\(\s*[""']checked", texto);
        Assert.DoesNotMatch(@"aceptaWhatsApp\s*=\s*true", texto);
        Assert.DoesNotMatch(@"<input\b[^>]*type=""checkbox""[^>]*\bchecked\b", texto);
    }

    // ───────────── El perfil ─────────────

    [Fact]
    public void El_perfil_pide_el_estado_a_la_API_y_activa_solo_con_el_tilde_y_la_version_vigente()
    {
        var seccion = SeccionDelPerfil();

        Assert.Contains(@"YS.api(""/consentimiento-whatsapp"")", seccion);
        Assert.Contains("if (!casilla?.checked || !_estadoWhatsApp?.version) return;", seccion);                 // sin tilde no se manda nada
        Assert.Contains(@"json: { version: _estadoWhatsApp.version }", seccion);                                  // la versión que se leyó
        Assert.Matches(@"id: ""btnActivarWhatsApp"", disabled: true,", seccion);                                  // el botón nace deshabilitado
        Assert.Contains(@"boton.disabled = !casilla.checked;", seccion);
        Assert.Contains(@"if (e.disponible && !activo) {", seccion);                                              // no se ofrece si no se puede
        // Desactivar pide confirmación.
        Assert.Matches(@"if \(!await confirmar\(", seccion);
        Assert.Contains("renderAvisosWhatsApp();   // si cambió el teléfono", Dashboard);                         // y al guardar el teléfono se actualiza
    }

    [Fact]
    public void El_perfil_carga_el_modulo_compartido_y_pinta_su_tarjeta_debajo_de_los_datos()
    {
        Assert.True(DashboardHtml.IndexOf("../shared/ui.js", StringComparison.Ordinal) < DashboardHtml.IndexOf("../shared/consentimiento-whatsapp.js", StringComparison.Ordinal));
        Assert.Contains(@"<div class=""card perfil-form oculto"" id=""avisosWhatsApp""", Dashboard);   // nace oculta: se muestra solo si la API la ofrece
        Assert.Contains("    renderAvisosWhatsApp();\n}", Dashboard.Replace("\r\n", "\n"));    // se llama al terminar de dibujar el perfil
    }

    // ───────────── Lo que manda el servidor no se interpreta como HTML ─────────────

    [Fact]
    public void Nada_de_lo_que_manda_el_servidor_entra_como_HTML()
    {
        foreach (var (nombre, texto) in new[] { ("el módulo compartido", Modulo), ("la sección del perfil", SeccionDelPerfil()), ("el script del registro (WhatsApp)", RegistroJs[RegistroJs.IndexOf("// ── Avisos por WhatsApp", StringComparison.Ordinal)..]) })
        {
            Assert.DoesNotMatch(@"\.innerHTML\s*=|insertAdjacentHTML|\.outerHTML|document\.write|html\s*:\s*[^,}\s]", texto);
            Assert.True(true, nombre);
        }
        Assert.Contains("replaceChildren", Modulo);
        Assert.Contains("replaceChildren", SeccionDelPerfil());
    }

    // ───────────── El texto es el de la API ─────────────

    [Fact]
    public void El_texto_del_consentimiento_no_esta_copiado_en_ninguna_pantalla()
    {
        // Cada aceptación guarda la versión y el hash del texto que sirve la API; si una pantalla tuviera su propia copia, lo que se ve podría
        // dejar de ser lo que queda registrado. Ningún párrafo del texto aparece en PANTALLAS/.
        var texto = TextoConsentimientoVideo.Interpretar("v0-provisorio",
            File.ReadAllText(Path.Combine(Raiz, "Services", "Mensajeria", "Consentimiento", "v0-provisorio.txt")));
        var frases = texto.Parrafos.Append(texto.Titulo).Select(p => p[..Math.Min(40, p.Length)]).ToList();

        var pantallas = Directory.EnumerateFiles(Path.Combine(Raiz, "PANTALLAS"), "*", SearchOption.AllDirectories)
            .Where(f => new[] { ".js", ".html" }.Contains(Path.GetExtension(f)) && !f.Contains($"{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}"))
            .ToList();
        foreach (var archivo in pantallas)
        {
            var contenido = File.ReadAllText(archivo);
            foreach (var frase in frases)
                Assert.False(contenido.Contains(frase, StringComparison.Ordinal), $"{Path.GetFileName(archivo)} copia el texto del consentimiento: \"{frase}\"");
        }
    }

    [Fact]
    public void El_modulo_muestra_la_etiqueta_de_provisorio_y_la_version_del_texto()
    {
        Assert.Contains("Texto provisorio", Modulo);
        Assert.Contains("Versión del texto:", Modulo);
        Assert.Contains("t.provisorio", Modulo);
    }

    // ───────────── El consentimiento no se guarda en el navegador ─────────────

    [Fact]
    public void El_consentimiento_no_se_guarda_en_el_navegador()
    {
        // Lo único que puede recordar el navegador es una molestia ("ahora no" a la sugerencia de después de un turno), nunca una aceptación:
        // todo uso del almacenamiento en el módulo es de UNA clave, y lo que se guarda es la fecha de ese "ahora no".
        var modulo = Modulo;
        var usos = Regex.Matches(modulo, @"(?:local|session)Storage\.\w+\(([^)]*)\)").Select(m => m.Groups[1].Value.Trim()).ToList();
        Assert.NotEmpty(usos);
        Assert.All(usos, u => Assert.StartsWith("CLAVE_AHORA_NO", u));
        Assert.Contains(@"const CLAVE_AHORA_NO = ""ys.wa.ahoraNo"";", modulo);
        Assert.Contains("localStorage.setItem(CLAVE_AHORA_NO, new Date().toISOString())", modulo);

        // Ni el registro ni el perfil guardan nada.
        Assert.DoesNotMatch(@"(?i)(local|session)Storage", RegistroJs.Substring(RegistroJs.IndexOf("// ── Avisos por WhatsApp", StringComparison.Ordinal)));
        Assert.DoesNotMatch(@"(?i)(local|session)Storage", SeccionDelPerfil());
    }

    // ───────────── El link para escribirle primero a la clínica (opción B) ─────────────

    [Fact]
    public void El_link_lo_arma_la_API_y_la_pantalla_solo_lo_muestra_si_es_de_WhatsApp()
    {
        var modulo = Modulo;

        Assert.Contains("estado?.contacto?.enlace", modulo);                                             // viene de la API
        Assert.Contains(@"u.protocol === ""https:"" && u.hostname === ""wa.me""", modulo);                 // y solo se muestra si es un link de WhatsApp
        Assert.Contains(@"target: ""_blank""", modulo);
        Assert.Contains(@"rel: ""noopener noreferrer""", modulo);
        Assert.Matches(@"if \(!enlace\) return null;", modulo);                                          // sin link, no hay bloque
        // Ni un link ni un número escritos a mano en la pantalla (EnlaceWhatsAppTests lo vigila en todo PANTALLAS/).
        Assert.DoesNotMatch(@"wa\.me/|api\.whatsapp\.com|web\.whatsapp\.com", modulo + SeccionDelPerfil());
    }

    [Fact]
    public void El_perfil_ofrece_el_link_solo_donde_se_puede_activar_y_no_a_quien_acaba_de_desactivar()
    {
        var seccion = SeccionDelPerfil();

        Assert.Contains(@"const contacto = e.estado === ""revocado"" ? null : YS.whatsapp.contacto(e);", seccion);
        // Está dentro del bloque de la casilla (el que solo existe si se puede activar) y antes del botón de desactivar.
        var bloque = seccion.IndexOf("if (e.disponible && !activo) {", StringComparison.Ordinal);
        var link = seccion.IndexOf("YS.whatsapp.contacto(e)", StringComparison.Ordinal);
        var desactivar = seccion.IndexOf("if (activo || pausado)", StringComparison.Ordinal);
        Assert.True(bloque >= 0 && bloque < link && link < desactivar, "el link tiene que estar dentro del bloque de la casilla");
    }

    [Fact]
    public void Despues_de_pedir_o_confirmar_un_turno_se_sugiere_sin_activar_nada()
    {
        var js = Dashboard;

        // Las dos veces: tras "Turno solicitado" y tras "Turno confirmado".
        foreach (var aviso in new[] { @"toast(""Turno solicitado.", @"toast(""Turno confirmado." })
        {
            var i = js.IndexOf(aviso, StringComparison.Ordinal);
            Assert.True(i >= 0, $"no encuentro {aviso}");
            Assert.Contains("sugerirAvisosWhatsApp();", js.Substring(i, Math.Min(900, js.Length - i)));
        }
        Assert.Contains(@"YS.whatsapp.sugerirTrasUnTurno({ alActivar: () => mostrarSeccion(""perfil"") });", js);

        // La sugerencia nunca acepta: no hay ningún POST ni ruta de "aceptar" entre sus líneas.
        var modulo = Modulo;
        var sugerencia = modulo[modulo.IndexOf("async sugerirTrasUnTurno", StringComparison.Ordinal)..];
        Assert.DoesNotContain("aceptar", sugerencia);
        Assert.DoesNotContain("POST", sugerencia);
        Assert.Contains(@"estado.estado !== ""sin-decision"" || !estado.disponible", sugerencia);   // solo a quien no decidió y puede activarlos
        Assert.Contains("if (sugerenciaPospuesta()) return false;", sugerencia);                       // y no si dijo "ahora no" hace poco
        Assert.Matches(@"if \(respuesta === true\) alActivar\?\.\(\);\s+else posponerSugerencia\(\);", sugerencia);
    }
}
