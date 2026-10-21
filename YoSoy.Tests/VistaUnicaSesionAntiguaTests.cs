using System.Text.Json;

// Vista única: lo que hace el navegador con una sesión guardada ANTES de que existiera Administración (Configurador y CRM como dos módulos aparte).
// shared/ui.js se carga con el node de la máquina en un contexto sin DOM (localStorage, fetch y location simulados): se prueba que reconoce la sesión
// antigua, que la actualiza con UN solo pedido a POST /sesion/modulo, y que si falla por cualquier motivo borra la sesión ENTERA y vuelve al ingreso con
// un mensaje (nunca queda una sesión a medias). Sin node, estas pruebas quedan OMITIDAS (no pasan en falso).
public class VistaUnicaSesionAntiguaTests
{
    private const string Script = """
        const vm = require('vm'), fs = require('fs');
        const esc = JSON.parse(process.env.ESCENARIO);
        // Como el real: Object.keys(localStorage) devuelve lo guardado (los métodos están en el prototipo).
        const almacen = (inicial) => Object.assign(Object.create({
            getItem(k) { return Object.prototype.hasOwnProperty.call(this, k) ? this[k] : null; },
            setItem(k, v) { this[k] = String(v); }, removeItem(k) { delete this[k]; },
            clear() { for (const k of Object.keys(this)) delete this[k]; }, key(i) { return Object.keys(this)[i]; } }), inicial || {});
        const ls = almacen(esc.local), ss = almacen(esc.sesion);
        const loc = { hostname: 'x', port: '', protocol: 'http:', origin: 'http://x', href: '', reemplazos: [], replace(u) { this.reemplazos.push(u); } };
        const pedidos = [];
        const ctx = { document: { addEventListener() {}, querySelector() { return null; }, createElement() { return {}; }, body: { classList: { add() {}, remove() {} } } },
            localStorage: ls, sessionStorage: ss, location: loc, console, setTimeout, Headers, Intl, Date, JSON, Object, Array, URLSearchParams, navigator: {},
            fetch: async (url, op) => {
                pedidos.push({ url, metodo: op.method, cuerpo: op.body, auth: op.headers.get('Authorization') });
                if (esc.red) throw new Error('sin red');
                return { ok: esc.estado >= 200 && esc.estado < 300, status: esc.estado, json: async () => esc.respuesta, text: async () => '' };
            } };
        ctx.window = ctx;
        vm.createContext(ctx);
        vm.runInContext(fs.readFileSync(process.env.UI, 'utf8'), ctx);
        (async () => {
            const antigua = ctx.YS.sesion.sesionAntigua();
            let resultado = null;
            if (esc.actualizar) resultado = await ctx.YS.sesion.actualizarSesion();
            process.stdout.write(JSON.stringify({ antigua, resultado, local: { ...ls }, sesion: { ...ss }, pedidos, reemplazos: loc.reemplazos }));
        })();
        """;

    private static readonly string Ui = Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "PANTALLAS", "shared", "ui.js");

    private static JsonElement Correr(object escenario)
    {
        var (codigo, salida, error) = NodeDelServicioDeWhatsApp.Ejecutar(new[] { "-e", Script }, new Dictionary<string, string>
        {
            ["UI"] = Ui, ["ESCENARIO"] = JsonSerializer.Serialize(escenario)
        });
        Assert.True(codigo == 0, "node falló: " + error);
        return JsonDocument.Parse(salida).RootElement.Clone();
    }

    private static string Modulos(params string[] claves) =>
        JsonSerializer.Serialize(claves.Select(c => new { clave = c, nombre = c, descripcion = "" }));

    private static Dictionary<string, string> SesionVieja(string modulo, params string[] claves) => new()
    {
        ["idUsuario"] = "7", ["rol"] = modulo, ["modulo"] = modulo, ["token"] = "token-viejo", ["modulos"] = Modulos(claves),
        ["permisos"] = "[\"" + modulo + "\"]", ["privilegios"] = "[]", ["ys-pref-tema"] = "oscuro"
    };

    private static readonly object Respuesta = new
    {
        rol = "administracion", idUsuario = 7, modulo = "administracion", token = "token-nuevo",
        modulos = new[] { new { clave = "administracion", nombre = "Administración", descripcion = "" } },
        permisos = new[] { "configurador", "crm" }, privilegios = Array.Empty<string>()
    };

    [FactConNodeDelServicioDeWhatsApp]
    public void Solo_una_sesion_con_Configurador_y_CRM_como_dos_modulos_es_antigua()
    {
        foreach (var (claves, esperado) in new (string[], bool)[]
        {
            (new[] { "configurador", "crm" }, true),
            (new[] { "profesional", "configurador", "crm" }, true),
            (new[] { "administracion" }, false),
            (new[] { "administracion", "profesional" }, false),
            (new[] { "crm" }, false),                       // la recepcionista: nada que actualizar
            (new[] { "configurador" }, false),
            (new[] { "crm", "profesional" }, false),
            (new[] { "configurador", "facturacion" }, false),
            (Array.Empty<string>(), false)
        })
        {
            var r = Correr(new { local = SesionVieja("configurador", claves), sesion = new Dictionary<string, string>(), actualizar = false });
            Assert.Equal(esperado, r.GetProperty("antigua").GetBoolean());
        }
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Actualizar_pide_Administracion_con_UN_solo_pedido_y_guarda_la_sesion_completa()
    {
        var r = Correr(new { local = SesionVieja("configurador", "configurador", "crm"), sesion = new Dictionary<string, string> { ["ys-nombre-x"] = "Ana" },
            actualizar = true, estado = 200, respuesta = Respuesta });

        Assert.True(r.GetProperty("resultado").GetBoolean());
        var pedidos = r.GetProperty("pedidos");
        Assert.Equal(1, pedidos.GetArrayLength());                                            // UN intento, sin reintentos
        Assert.EndsWith("/sesion/modulo", pedidos[0].GetProperty("url").GetString());
        Assert.Equal("POST", pedidos[0].GetProperty("metodo").GetString());
        Assert.Equal("administracion", JsonDocument.Parse(pedidos[0].GetProperty("cuerpo").GetString()!).RootElement.GetProperty("modulo").GetString());
        Assert.Equal("Bearer token-viejo", pedidos[0].GetProperty("auth").GetString());       // con la sesión que ya tiene: el servidor vuelve a mirar la base
        var local = r.GetProperty("local");
        Assert.Equal("token-nuevo", local.GetProperty("token").GetString());
        Assert.Equal("administracion", local.GetProperty("modulo").GetString());
        Assert.Equal("administracion", local.GetProperty("rol").GetString());
        Assert.Contains("administracion", local.GetProperty("modulos").GetString());
        Assert.DoesNotContain("\"configurador\"", local.GetProperty("modulos").GetString());
        Assert.Equal("oscuro", local.GetProperty("ys-pref-tema").GetString());               // las preferencias del navegador se conservan
        Assert.Equal(0, r.GetProperty("sesion").EnumerateObject().Count());                   // y lo cacheado del módulo anterior se descarta
        Assert.Equal(0, r.GetProperty("reemplazos").GetArrayLength());
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Con_otro_modulo_activo_se_renueva_ese_mismo_modulo_para_traer_la_lista_nueva()
    {
        var r = Correr(new { local = SesionVieja("profesional", "profesional", "configurador", "crm"), sesion = new Dictionary<string, string>(), actualizar = true, estado = 200,
            respuesta = new { rol = "profesional", idUsuario = 7, modulo = "profesional", token = "t2", idProfesional = 3,
                modulos = new[] { new { clave = "profesional", nombre = "Profesional", descripcion = "" }, new { clave = "administracion", nombre = "Administración", descripcion = "" } },
                permisos = new[] { "profesional" }, privilegios = Array.Empty<string>() } });

        Assert.True(r.GetProperty("resultado").GetBoolean());
        Assert.Equal("profesional", JsonDocument.Parse(r.GetProperty("pedidos")[0].GetProperty("cuerpo").GetString()!).RootElement.GetProperty("modulo").GetString());
        Assert.Equal("profesional", r.GetProperty("local").GetProperty("modulo").GetString());
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Si_falla_borra_la_sesion_entera_vuelve_al_ingreso_con_el_motivo_y_no_reintenta()
    {
        var casos = new object[]
        {
            new { estado = 401, red = false, respuesta = (object?)null },                    // el token ya venció
            new { estado = 403, red = false, respuesta = (object?)new { codigo = "MODULO_NO_HABILITADO" } },   // le quitaron un permiso
            new { estado = 500, red = false, respuesta = (object?)null },
            new { estado = 200, red = true, respuesta = (object?)null },                     // sin red
            new { estado = 200, red = false, respuesta = (object?)new { rol = "administracion" } },   // respuesta incompleta: sin token ni módulos
            new { estado = 200, red = false, respuesta = (object?)new { rol = "crm", idUsuario = 7, modulo = "crm", token = "t",    // el servidor devolvió lo mismo de antes
                modulos = new[] { new { clave = "configurador", nombre = "c", descripcion = "" }, new { clave = "crm", nombre = "c", descripcion = "" } }, permisos = new[] { "crm" }, privilegios = Array.Empty<string>() } },
        };
        foreach (dynamic caso in casos)
        {
            var r = Correr(new { local = SesionVieja("configurador", "configurador", "crm"), sesion = new Dictionary<string, string> { ["ys-nombre-x"] = "Ana" },
                actualizar = true, estado = (int)caso.estado, red = (bool)caso.red, respuesta = (object?)caso.respuesta });

            Assert.False(r.GetProperty("resultado").GetBoolean());
            Assert.Equal(1, r.GetProperty("pedidos").GetArrayLength());                       // un solo intento
            var local = r.GetProperty("local");
            Assert.Equal(new[] { "ys-pref-tema" }, local.EnumerateObject().Select(p => p.Name).ToArray());   // ni token, ni módulo, ni nada a medias
            Assert.Equal(0, r.GetProperty("sesion").EnumerateObject().Count());
            Assert.Equal(new[] { "/pantallas/index.html?motivo=sesion" }, r.GetProperty("reemplazos").EnumerateArray().Select(x => x.GetString()).ToArray());
        }
    }

    [Fact]
    public void El_ingreso_dice_con_claridad_por_que_volvio_y_el_shell_lo_intenta_una_sola_vez()
    {
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        var login = File.ReadAllText(Path.Combine(raiz, "PANTALLAS", "index.html"));
        Assert.Contains("motivo\") === \"sesion\"", login);
        Assert.Contains("No pudimos actualizar tu sesión. Ingresá de nuevo", login);
        var shell = File.ReadAllText(Path.Combine(raiz, "PANTALLAS", "shared", "shell.js"));
        Assert.Contains("cfg.rol !== \"paciente\" && YS.sesion.sesionAntigua()", shell);
        Assert.Contains("YS.sesion.actualizarSesion().then(ok => { if (ok) location.reload(); });", shell);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(shell, "actualizarSesion\\(\\)").Count);   // un solo lugar lo pide: no hay bucles
    }
}
