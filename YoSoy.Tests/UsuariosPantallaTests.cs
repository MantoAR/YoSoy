// Las pantallas de la Fase 2, PR 2 (Configurador › Usuarios y el paso "Elegí tu clave nueva" del ingreso). No hay navegador en la suite: se prueba
// sobre el texto de los archivos y, con el node de la máquina, que el JavaScript compile (si no hay node, esas quedan OMITIDAS, no pasan en falso). El
// comportamiento de verdad (lista, filtros, restablecer, primer ingreso, asignación) se verificó con Chromium real y consta en el informe del PR.
public class UsuariosPantallaTests
{
    private static readonly string Raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz, "PANTALLAS" }.Concat(partes).ToArray()));

    private static string Usuarios => Leer("Pantalla_Configurador", "js", "usuarios.js");
    private static string Dashboard => Leer("Pantalla_Configurador", "js", "dashboard.js");
    private static string Ingreso => Leer("index.html");

    [Fact]
    public void El_panel_carga_usuarios_js_antes_que_dashboard_js_que_ya_navega_al_terminar()
    {
        var html = Leer("Pantalla_Configurador", "dashboard.html");
        var iUsuarios = html.IndexOf("src=\"js/usuarios.js\"", StringComparison.Ordinal);
        var iDashboard = html.IndexOf("src=\"js/dashboard.js\"", StringComparison.Ordinal);
        Assert.True(iUsuarios > 0 && iDashboard > iUsuarios, "usuarios.js tiene que cargarse antes que dashboard.js");
    }

    [Fact]
    public void La_seccion_usuarios_es_la_lista_unica_y_asignaciones_de_Admision_conserva_sus_dos_listas()
    {
        Assert.Matches(@"if \(tab === ""usuarios""\)\s+return await renderListaUsuarios\(contenido, params\);", Dashboard);
        Assert.Matches(@"if \(tab === ""asignaciones""\)\s+return await renderUsuarios\(contenido, params, tab\);", Dashboard);
        Assert.DoesNotMatch(@"tab === ""usuarios"" \|\| tab === ""asignaciones""", Dashboard);
    }

    [Fact]
    public void La_lista_pide_todo_al_servidor_y_no_arma_dos_listas_con_los_volcados_completos()
    {
        var js = Usuarios;
        Assert.Contains("/usuarios/lista?", js);
        Assert.Contains("restablecer-password", js);
        Assert.Contains("/usuarios/pacientes/", js);
        Assert.DoesNotContain("YS.api(`/pacientes`)", js);
        Assert.DoesNotContain("YS.api(`/profesionales`)", js);
        Assert.DoesNotContain("YS.tabla(", js);    // la búsqueda y la paginación son del servidor, no de una tabla en memoria
        foreach (var parametro in new[] { "tipo", "estado", "pagina", "cantidad" }) Assert.Contains(parametro, js);
    }

    [Fact]
    public void La_clave_temporal_vive_solo_en_la_ventana_y_no_se_guarda_en_el_navegador()
    {
        var js = Usuarios;
        Assert.DoesNotContain("localStorage", js);
        Assert.DoesNotContain("sessionStorage", js);
        Assert.DoesNotContain("console.", js);                       // ni siquiera a la consola
        Assert.Contains("Se ve una sola vez", js);
        Assert.Contains("data-secreto", js);                          // la marca que ya usa el resto del panel para lo que no debe quedar
        Assert.Matches(@"cerrable: false", js);                       // no se cierra con Esc o clic afuera sin querer: solo con "Listo"
    }

    [Fact]
    public void Restablecer_pide_confirmacion_y_avisa_lo_que_pasa_y_lo_que_no()
    {
        var js = Usuarios;
        Assert.Contains("Restablecer contraseña", js);
        Assert.Contains("deja de servir", js);
        Assert.Contains("esa sesión no se cierra", js);               // el límite conocido, dicho en la propia pantalla
        Assert.Contains("!f.esUnoMismo", js);                         // sobre uno mismo no se ofrece
    }

    [Fact]
    public void El_ingreso_maneja_el_cambio_obligatorio_con_el_token_en_memoria()
    {
        var html = Ingreso;
        Assert.Contains("requiereCambioPassword", html);
        Assert.Contains("/sesion/cambiar-password", html);
        Assert.Contains("Elegí tu clave nueva", html);
        Assert.Contains("id=\"formClaveNueva\"", html);
        Assert.DoesNotMatch(@"(local|session)Storage\.setItem\([^)]*[Cc]ambioClave", html);
        Assert.DoesNotMatch(@"(local|session)Storage\.setItem\([^)]*claveNueva", html);
        Assert.Contains("let tokenCambioClave = null", html);          // vive en una variable, no en el almacenamiento
        Assert.Contains("autocomplete=\"new-password\"", html);
        Assert.Contains("rolActual", html.Substring(html.IndexOf("async function guardarClaveNueva", StringComparison.Ordinal)));   // la pestaña elegida viaja
    }

    [Fact]
    public void El_menu_del_Configurador_describe_la_lista_unica()
    {
        var shell = Leer("shared", "shell.js");
        Assert.DoesNotContain("Altas y bajas de pacientes y profesionales", shell);
        Assert.Matches(@"id: ""usuarios"", texto: ""Usuarios""", shell);
    }

    // ───────────── Que el JavaScript compile ─────────────

    private static void ComprobarSintaxis(string codigo, string que)
    {
        var (salida, error, estado) = (string.Empty, string.Empty, 0);
        var script = "const vm = require('vm'); try { new vm.Script(process.env.CODIGO); } catch (e) { process.stderr.write(String(e)); process.exit(1); }";
        (estado, salida, error) = NodeDelServicioDeWhatsApp.Ejecutar(new[] { "-e", script }, new Dictionary<string, string> { ["CODIGO"] = codigo });
        Assert.True(estado == 0, $"{que} no compila: {error}");
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void usuarios_js_compila() => ComprobarSintaxis(Usuarios, "usuarios.js");

    [FactConNodeDelServicioDeWhatsApp]
    public void dashboard_js_compila() => ComprobarSintaxis(Dashboard, "dashboard.js");

    [FactConNodeDelServicioDeWhatsApp]
    public void El_script_del_ingreso_compila()
    {
        var html = Ingreso;
        var inicio = html.LastIndexOf("<script>", StringComparison.Ordinal) + "<script>".Length;
        var fin = html.IndexOf("</script>", inicio, StringComparison.Ordinal);
        ComprobarSintaxis(html[inicio..fin], "el script de index.html");
    }
}
