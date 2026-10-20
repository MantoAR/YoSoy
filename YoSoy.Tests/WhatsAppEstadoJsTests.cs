using System.Text.Json;

// PANTALLAS/shared/whatsapp-estado.js: la traducción del estado de WhatsApp (lo que responde la API) a lo que se muestra, que usan la pantalla de
// WhatsApp del Configurador y el aviso de la bandeja de Chats. Es una función pura: se prueba con el node de la máquina (si no hay, las pruebas
// quedan OMITIDAS, no pasan en falso), más pruebas de texto sobre los archivos que la usan (no hay navegador en la suite).
public class WhatsAppEstadoJsTests
{
    private static readonly string Raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(partes).ToArray()));

    private const string Script = """
        const m = require(process.env.MODULO);
        const entradas = JSON.parse(process.env.ENTRADAS);
        console.log(JSON.stringify(entradas.map(e => process.env.FUNCION === 'aviso' ? m.avisoDeBandeja(e) : m.vista(e))));
        """;

    private static JsonElement[] Evaluar(string funcion, params object[] entradas)
    {
        var modulo = Path.Combine(Raiz, "PANTALLAS", "shared", "whatsapp-estado.js");
        var (codigo, salida, error) = NodeDelServicioDeWhatsApp.Ejecutar(new[] { "-e", Script }, new Dictionary<string, string>
        {
            ["MODULO"] = modulo,
            ["FUNCION"] = funcion,
            ["ENTRADAS"] = JsonSerializer.Serialize(entradas)
        });
        Assert.True(codigo == 0, "node falló: " + error);
        return JsonSerializer.Deserialize<JsonElement[]>(salida.Trim())!;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    // desdeHaceMin: hace cuántos minutos está en ese estado (el `desde` del servicio); `ahora` va fijo para que la prueba no dependa del reloj.
    private static object E(string? estado, string? motivo = null, bool conectado = false, bool disponible = true, string? qr = null, int? intentos = null, double? desdeHaceMin = null) =>
        new { servicioDisponible = disponible, conectado, numero = conectado ? "5491100000000" : null, qrPendiente = qr != null, qr, estado, motivo, intentosDeReconexion = intentos,
              desde = desdeHaceMin is null ? null : Ahora.AddMinutes(-desdeHaceMin.Value).ToString("o"), ahora = Ahora.ToUnixTimeMilliseconds() };

    private static string Clave(JsonElement v) => v.GetProperty("clave").GetString()!;
    private static bool Marca(JsonElement v, string nombre) => v.GetProperty(nombre).GetBoolean();

    [FactConNodeDelServicioDeWhatsApp]
    public void Los_estados_en_los_que_el_servicio_abre_la_conexion_son_siempre_Conectando_sin_pasar_por_desconectado()
    {
        var v = Evaluar("vista", E("iniciando"), E("reconectando"), E("detenido"), E("reconectando", intentos: 1));

        Assert.All(v, x =>
        {
            Assert.Equal("conectando", Clave(x));
            Assert.Equal("Conectando…", x.GetProperty("titulo").GetString());
            Assert.False(Marca(x, "puedePedirQr"));      // no se ofrece ningún botón mientras conecta
            Assert.False(Marca(x, "puedeGenerarNuevo"));
        });
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Una_reconexion_larga_aclara_que_sigue_intentando()
    {
        var v = Evaluar("vista", E("reconectando", intentos: 2), E("reconectando", intentos: 5));

        Assert.DoesNotContain("intento", v[0].GetProperty("detalle").GetString());
        Assert.Contains("intento 5", v[1].GetProperty("detalle").GetString());
        Assert.Equal("conectando", Clave(v[1]));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void En_qr_vencido_y_sesion_cerrada_aparece_el_boton_para_pedir_el_codigo()
    {
        var v = Evaluar("vista", E("requiere_vincular", "qr_vencido"), E("requiere_vincular", "sesion_cerrada"));

        Assert.All(v, x =>
        {
            Assert.Equal("vincular", Clave(x));
            Assert.True(Marca(x, "puedePedirQr"));
            Assert.False(Marca(x, "mostrarQr"));
        });
        Assert.NotEqual(v[0].GetProperty("detalle").GetString(), v[1].GetProperty("detalle").GetString()); // cada motivo se explica con su texto
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Con_el_codigo_esperando_escaneo_se_muestra_el_QR_y_se_puede_generar_uno_nuevo()
    {
        var v = Evaluar("vista", E("requiere_vincular", "esperando_escaneo", qr: "data:image/png;base64,xx"), E("requiere_vincular", "esperando_escaneo"));

        Assert.Equal("escanear", Clave(v[0]));
        Assert.True(Marca(v[0], "mostrarQr"));
        Assert.True(Marca(v[0], "puedeGenerarNuevo"));
        Assert.Equal("escanear", Clave(v[1]));      // todavía sin imagen: no hay nada que mostrar ni que regenerar
        Assert.False(Marca(v[1], "mostrarQr"));
        Assert.False(Marca(v[1], "puedeGenerarNuevo"));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Si_el_servicio_necesita_revision_solo_se_informa_el_motivo_sin_botones()
    {
        var v = Evaluar("vista",
            E("requiere_atencion", "cuenta_restringida"), E("requiere_atencion", "sesion_reemplazada"),
            E("requiere_atencion", "version_rechazada"), E("requiere_atencion", "sesion_invalida"), E("requiere_atencion", "uno_nuevo"),
            E("requiere_vincular", "error_borrando_sesion"));

        var detalles = new HashSet<string>();
        foreach (var x in v)
        {
            Assert.Equal("atencion", Clave(x));
            Assert.False(Marca(x, "puedePedirQr"));
            Assert.False(Marca(x, "puedeGenerarNuevo"));
            Assert.False(Marca(x, "puedeDesvincular"));
            Assert.Contains("servidor", x.GetProperty("detalle").GetString());
            detalles.Add(x.GetProperty("detalle").GetString()!);
        }
        Assert.Equal(v.Length, detalles.Count); // un texto distinto para cada motivo (y uno genérico para un motivo que todavía no se conoce)
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Conectado_ofrece_desvincular_y_el_servicio_caido_dice_que_no_esta_disponible()
    {
        var v = Evaluar("vista", E("conectado", conectado: true), E(null, disponible: false));

        Assert.Equal("conectado", Clave(v[0]));
        Assert.True(Marca(v[0], "puedeDesvincular"));
        Assert.False(Marca(v[0], "desconectado"));
        Assert.Equal("no-disponible", Clave(v[1]));
        Assert.True(Marca(v[1], "desconectado"));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Una_API_que_todavia_no_manda_estado_se_interpreta_con_conectado_y_qr()
    {
        var v = Evaluar("vista", E(null), E(null, qr: "data:image/png;base64,xx"));

        Assert.Equal("conectando", Clave(v[0]));
        Assert.Equal("escanear", Clave(v[1]));
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Un_estado_desconocido_se_muestra_como_tal_con_el_motivo_y_sin_botones()
    {
        var v = Evaluar("vista", E("estado_que_no_conozco"), E("estado_que_no_conozco", "motivo_raro"), E("", "x"));

        Assert.Equal("desconocido", Clave(v[0]));
        Assert.Equal("Estado no reconocido (estado_que_no_conozco)", v[0].GetProperty("titulo").GetString());
        Assert.Equal("", v[0].GetProperty("detalle").GetString());
        Assert.Contains("motivo_raro", v[1].GetProperty("detalle").GetString());
        Assert.NotEqual("conectando", Clave(v[1]));
        Assert.All(v.Take(2), x =>
        {
            Assert.False(Marca(x, "conectando"));
            Assert.False(Marca(x, "puedePedirQr"));
            Assert.False(Marca(x, "puedeGenerarNuevo"));
            Assert.False(Marca(x, "puedeDesvincular"));
            Assert.False(Marca(x, "mostrarQr"));
        });
        Assert.Equal("conectando", Clave(v[2]));   // un estado vacío es "no manda estado" (API vieja), no uno desconocido
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Pasados_5_minutos_conectando_dice_cuanto_lleva_con_el_intento_y_el_motivo_sin_pedir_nada()
    {
        var v = Evaluar("vista",
            E("reconectando", intentos: 7, desdeHaceMin: 12.5),
            E("iniciando", "arrancando", desdeHaceMin: 6),
            E("reconectando", intentos: 7, desdeHaceMin: 4.9),
            E("reconectando", intentos: 2, desdeHaceMin: 5),
            E("detenido", desdeHaceMin: 90));

        Assert.Equal("Lleva más de 12 minutos sin conectar. Intento 7.", v[0].GetProperty("detalle").GetString());
        Assert.Equal("Lleva más de 6 minutos sin conectar. Motivo: arrancando.", v[1].GetProperty("detalle").GetString());
        Assert.StartsWith("Sigue intentando", v[2].GetProperty("detalle").GetString());   // menos de 5 minutos: el texto de siempre
        Assert.StartsWith("Un momento", v[3].GetProperty("detalle").GetString());         // justo 5 minutos todavía no pasa del tope
        Assert.StartsWith("Un momento", v[4].GetProperty("detalle").GetString());         // detenido no es "sin conectar", es parado a propósito
        Assert.All(v.Take(2), x =>
        {
            Assert.Equal("conectando", Clave(x));
            Assert.False(Marca(x, "puedePedirQr"));
            Assert.DoesNotContain("administra", x.GetProperty("detalle").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("avis", x.GetProperty("detalle").GetString(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void El_aviso_de_la_bandeja_aparece_si_esta_desconectado_y_aclara_que_la_vinculacion_la_hace_el_Configurador()
    {
        var v = Evaluar("aviso",
            new { aplica = true, servicioDisponible = true, conectado = false, estado = "requiere_vincular", motivo = "qr_vencido" },
            new { aplica = true, servicioDisponible = false, conectado = false },
            new { aplica = true, servicioDisponible = true, conectado = false, estado = "reconectando" },
            new { aplica = true, servicioDisponible = true, conectado = true, estado = "conectado" },
            new { aplica = false });

        Assert.Contains("Configurador", v[0].GetProperty("texto").GetString());
        Assert.Contains("desconectado", v[0].GetProperty("texto").GetString());
        Assert.Contains("Configurador", v[1].GetProperty("texto").GetString());
        Assert.Contains("conectando", v[2].GetProperty("texto").GetString());
        Assert.Equal(JsonValueKind.Null, v[3].ValueKind);   // conectado: nada que avisar
        Assert.Equal(JsonValueKind.Null, v[4].ValueKind);   // con otro canal activo, tampoco
    }

    // ── Lo que no se puede probar sin navegador: que las pantallas usen esto como corresponde ─────────────────────────────

    [Fact]
    public void La_pantalla_de_WhatsApp_usa_el_modulo_compartido_y_muestra_el_boton_de_pedir_codigo()
    {
        var dash = Leer("PANTALLAS", "Pantalla_Configurador", "js", "dashboard.js");
        var html = Leer("PANTALLAS", "Pantalla_Configurador", "dashboard.html");

        Assert.Contains("../shared/whatsapp-estado.js", html);
        Assert.Contains("YS.whatsappEstado.vista(", dash);
        Assert.Contains("v.clave === \"vincular\"", dash);                 // el estado qr_vencido/sesion_cerrada...
        Assert.Contains("Mostrar código QR", dash);                        // ...tiene su botón
        Assert.Contains("/mensajeria/whatsapp/vincular", dash);
        Assert.Contains("Conectado desde", dash);
        Assert.DoesNotContain("Generando el código QR...", dash);          // el texto de antes que no distinguía "conectando" de "hay que vincular"
    }

    [Fact]
    public void El_cuadro_de_desvincular_es_el_propio_de_la_app_y_avisa_que_se_borra_la_sesion_y_hace_falta_un_QR_nuevo()
    {
        var dash = Leer("PANTALLAS", "Pantalla_Configurador", "js", "dashboard.js");
        var desde = dash.IndexOf("async function waDesvincular()", StringComparison.Ordinal);
        var cuerpo = dash.Substring(desde, dash.IndexOf("async function waPedirQr", desde, StringComparison.Ordinal) - desde);

        Assert.Contains("await confirmar(", cuerpo);
        Assert.Contains("Se borra la sesión de WhatsApp", cuerpo);
        Assert.Contains("escanear un código QR nuevo", cuerpo);
        Assert.DoesNotContain("window.confirm", dash.Substring(dash.IndexOf("// ── WHATSAPP", StringComparison.Ordinal)));
        // y el confirmar() de la pantalla es el cuadro propio (YS.confirmar), no el confirm() del navegador
        var definicion = dash.Substring(dash.IndexOf("function confirmar(", StringComparison.Ordinal), 400);
        Assert.Contains("YS.confirmar", definicion);
    }

    [Fact]
    public void La_pantalla_aplica_el_enfriamiento_del_boton_de_codigo_nuevo()
    {
        var dash = Leer("PANTALLAS", "Pantalla_Configurador", "js", "dashboard.js");

        Assert.Contains("data-wa-enfria", dash);
        Assert.Contains("_waEnfriaHasta", dash);
        Assert.Contains("reintentarEnSegundos", dash);     // si la API dice que faltan más segundos, se usan esos
    }

    [Fact]
    public void La_bandeja_avisa_de_forma_permanente_y_deshabilita_Enviar_por_cuando_WhatsApp_esta_desconectado()
    {
        var bandeja = Leer("PANTALLAS", "Pantalla_ChatSimulado", "js", "bandeja.js");
        var shell = Leer("PANTALLAS", "shared", "shell.js");
        var index = Leer("PANTALLAS", "Pantalla_ChatSimulado", "index.html");

        Assert.Contains("/conexion-whatsapp", bandeja);
        Assert.Contains("est.canalActivo !== \"WhatsApp\"", bandeja);       // solo se sondea con WhatsApp como canal
        Assert.Contains("canal.disabled = !!aviso", bandeja);
        Assert.Contains("id=\"chatsAvisoWa\"", bandeja);
        Assert.Contains("whatsapp-estado.js", shell);                       // el módulo CRM dentro del Configurador la carga antes de la bandeja
        Assert.Contains("whatsapp-estado.js", index);
    }
}
