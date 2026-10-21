using System.Text.RegularExpressions;

// Chats v2, Fase 1: la pantalla (bandeja.js, bandeja.css, leads.js, chats-ia.js y el menú). Estas pruebas LEEN el código de la pantalla y lo compilan con Node; lo que hace en un navegador
// de verdad se ensayó aparte con navegadores reales contra la API (ver el informe de la rama). Lo que el servidor decide (403, 409...) lo prueba ChatsVinculoBaseRealTests.
public class ChatsVistaPantallaTests
{
    private static readonly string Raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz, "PANTALLAS" }.Concat(partes).ToArray())).Replace("\r\n", "\n");   // con CRLF o LF
    private static string Bandeja => Leer("Pantalla_ChatSimulado", "js", "bandeja.js");
    private static string Estilos => Leer("Pantalla_ChatSimulado", "css", "bandeja.css");
    private static string ChatsIa => Leer("Pantalla_Configurador", "js", "chats-ia.js");

    // ── Configurar vista: fuera ─────────────────────────────────────────────────────────────
    [Fact]
    public void Configurar_vista_ya_no_esta_ni_en_la_pantalla_ni_en_los_estilos()
    {
        Assert.DoesNotContain("Configurar vista", Bandeja);
        Assert.DoesNotContain("chats-contexto-configurar", Bandeja);
        Assert.DoesNotContain("chats-contexto-configurar", Estilos);
        Assert.Contains("Información del lead", Bandeja);                       // el resto del encabezado del panel sigue
        Assert.Contains("chatsCerrarContexto", Bandeja);
    }

    // ── Teléfono en formato WhatsApp ────────────────────────────────────────────────────────
    [Fact]
    public void El_telefono_se_muestra_como_lo_manda_el_servidor_en_la_lista_el_encabezado_el_panel_y_los_avisos()
    {
        var js = Bandeja;
        Assert.Contains("function telefonoVer(c) { return (c && c.telefonoMostrar) || formatearTelefono(c && c.telefono); }", js);
        Assert.Contains("const nombre = quien || telefonoVer(c);", js);                                                       // lista
        Assert.Contains("<span class=\"chats-conv-tel\">${YS.esc(telefonoVer(c))}</span>", js);
        Assert.Contains("<div class=\"chats-hilo-sub\">${YS.esc(telefonoVer(c))} · WhatsApp", js);                            // encabezado
        Assert.Contains("const tel = chat.telefonoMostrar || formatearTelefono(info.telefono);", js);                          // panel
        Assert.Contains("const nombre = c.nombrePaciente || telefonoVer(c);", js);                                            // aviso de mensaje nuevo
        // Nadie formatea el teléfono de una conversación "a mano" ni con el formato viejo, salvo la función de respaldo.
        Assert.DoesNotContain("formatearTelefono(c.telefono)", js);
        Assert.Contains("telefonoMostrar", Leer("Pantalla_Configurador", "js", "leads.js"));
    }

    [Fact]
    public void El_telefono_que_cargo_el_paciente_se_muestra_tal_cual_y_aparte_del_de_WhatsApp()
    {
        var js = Bandeja;
        Assert.Contains("dato(\"Teléfono del paciente\", p.telefono)", js);
        Assert.Contains("dato(\"WhatsApp de esta conversación\", tel)", js);
    }

    // ── Vincular y desvincular ──────────────────────────────────────────────────────────────
    [Fact]
    public void Sin_paciente_el_panel_ofrece_vincular_y_las_coincidencias_por_telefono_son_solo_sugerencias()
    {
        var js = Bandeja;
        Assert.Contains("id=\"chatsBtnVincular\"", js);
        Assert.Contains("Vincular con un paciente", js);
        Assert.Contains("Coincidencias sugeridas por teléfono", js);
        Assert.Contains("Es solo una sugerencia: no queda vinculado nadie hasta que lo confirmes.", js);
        // La sugerencia solo ABRE la confirmación: dibujarla no hace ningún pedido que cambie algo.
        var sugerencias = Regex.Match(js, @"function sugerenciasHtml\(lista\) \{.*?\n    \}\n", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(sugerencias));
        Assert.DoesNotContain("YS.api", sugerencias);
        Assert.DoesNotContain("YS.fetch", sugerencias);
        Assert.Contains("abrirVincular(id, est.sugerencias[parseInt(b.dataset.sug)])", js);
    }

    [Fact]
    public void Vincular_exige_confirmar_mostrando_nombre_dni_parcial_y_anio_de_nacimiento_con_los_avisos()
    {
        var js = Bandeja;
        // El único PUT a /vinculo está dentro del clic de "Vincular con este paciente", después de mostrar los datos.
        var puts = Regex.Matches(js, @"/vinculo`, \{ method: ""PUT""").Count;
        Assert.Equal(1, puts);
        var iConfirmar = js.IndexOf("boton.addEventListener(\"click\"", StringComparison.Ordinal);
        var iPut = js.IndexOf("/vinculo`, { method: \"PUT\"", StringComparison.Ordinal);
        Assert.True(iConfirmar > 0 && iPut > iConfirmar, "el PUT tiene que estar dentro del clic de confirmar");
        Assert.Contains("id=\"vincConfirmar\">Vincular con este paciente", js);
        foreach (var dato in new[] { "Nombre", "DNI", "Año de nacimiento" }) Assert.Contains($"<span class=\"ui-dato-etiqueta\">{dato}</span>", js);
        Assert.Contains("YS.esc(pac.dniParcial)", js);
        // Los avisos pedidos: coincidencia sugerida, otra conversación del mismo paciente y el asistente automático.
        Assert.Contains("Es solo una coincidencia sugerida, no una confirmación: preguntale a la persona que escribe quién es antes de vincular.", js);
        Assert.Contains("Este paciente ya tiene ", js);
        Assert.Contains("Va a tratar a quien escribe desde este número como este paciente: va a poder ver y cancelar sus turnos y pedir turnos a su nombre.", js);
        Assert.Matches(@"if \(!personal\(\)\)\s+avisos\.push\(\[""ui-alerta--danger"", ""Atención con el asistente automático""", js);   // solo en el modo Asistente
        Assert.Contains("Confirmá la identidad con la persona que escribe", js);
        // Lo que viene del servidor se escapa.
        foreach (var crudo in new[] { "${s.nombre}", "${pac.dniParcial}", "${datosCortos(" }) Assert.DoesNotContain(crudo, js);
        Assert.Equal(2, Regex.Matches(js, @"YS\.esc\(datosCortos\(s\)\)").Count);   // y el texto que arma el ayudante siempre pasa por YS.esc
        Assert.Contains("YS.esc(pac.nombre)", js);
        Assert.Contains("YS.esc(s.nombre)", js);   // (el aviso de "vinculada con ${pac.nombre}" va por YS.toast, que escribe texto y no HTML)
    }

    [Fact]
    public void El_buscador_pide_un_minimo_y_no_busca_antes()
    {
        var js = Bandeja;
        Assert.Contains("/pacientes/buscar?q=${encodeURIComponent(q)}&idConversacion=${idConversacion}", js);
        Assert.Contains("q.length >= 3", js);
        Assert.Contains("Escribí al menos 3 letras del nombre, o el DNI completo.", js);
        Assert.Contains("YS.debounce(async () => {", js);
    }

    [Fact]
    public void Desvincular_pide_confirmacion_y_dice_que_se_revierte_y_que_se_conserva()
    {
        var js = Bandeja;
        Assert.Contains("id=\"chatsBtnDesvincular\"", js);
        var desvincular = Regex.Match(js, @"async function desvincular\(.*?\n    \}\n", RegexOptions.Singleline).Value;
        Assert.Contains("YS.confirmar(", desvincular);
        Assert.Contains("peligro: true", desvincular);
        Assert.Contains("/vinculo`, { method: \"DELETE\" }", desvincular);
        Assert.True(desvincular.IndexOf("YS.confirmar(", StringComparison.Ordinal) < desvincular.IndexOf("method: \"DELETE\"", StringComparison.Ordinal));
        Assert.Contains("se reinicia el flujo del asistente y se apaga la IA de esta conversación. Las notas y las etiquetas se conservan.", desvincular);
    }

    [Fact]
    public void Ver_ficha_solo_en_Administracion_y_lleva_a_Usuarios()
    {
        var js = Bandeja;
        Assert.Contains("function puedeVerFicha() { return YS.shell?.rol === \"administracion\"; }", js);
        Assert.Contains("${puedeVerFicha() ? `<button class=\"ui-btn ui-btn--sm\" type=\"button\" id=\"chatsBtnVerFicha\">", js);
        Assert.Contains("irAlPanel(`usuarios?paciente=${p.idPaciente}`)", js);
        // El destino existe en la lista de Usuarios.
        Assert.Contains("params.paciente", Leer("Pantalla_Configurador", "js", "usuarios.js"));
    }

    // ── Quién responde, tomar control, IA ──────────────────────────────────────────────────
    [Fact]
    public void El_panel_dice_quien_responde_ofrece_tomar_control_y_el_interruptor_de_IA_de_la_conversacion()
    {
        var js = Bandeja;
        Assert.Contains("<div class=\"chats-contexto-seccion-titulo\">Quién responde</div>", js);
        Assert.Contains("chat.quienResponde", js);                                           // lo dice el servidor, no se deduce en la pantalla
        Assert.Contains("id=\"chatsBtnTomarControl\">Tomar control", js);
        Assert.Contains("/tomar-control`, { method: \"POST\"", js);
        Assert.Contains("/ia`, { method: \"PUT\", json: { activa } }", js);
        Assert.Contains("id=\"chatsIaConv\"", js);
        Assert.Contains("La IA todavía no responde mensajes: por ahora solo se guarda esta decisión.", js);
        Assert.Contains("La IA global está apagada (Configuración › Asistente de chats): aunque la prendas acá, no responde.", js);
        Assert.Contains("Falta aplicar la migración 25", js);
        // Tomar control pregunta antes.
        var tomar = Regex.Match(js, @"async function tomarControl\(.*?\n    \}\n", RegexOptions.Singleline).Value;
        Assert.True(tomar.IndexOf("YS.confirmar(", StringComparison.Ordinal) is >= 0 and var iConf && iConf < tomar.IndexOf("tomar-control", StringComparison.Ordinal));
        Assert.Contains("deja de contestar en esta conversación", tomar);
    }

    // ── Interruptor global (Configuración) ─────────────────────────────────────────────────
    [Fact]
    public void El_interruptor_global_esta_en_Configuracion_solo_para_Configurador_y_Administracion()
    {
        var js = ChatsIa;
        Assert.Contains("/configuracion/chats-ia", js);
        Assert.Contains("method: \"PUT\", json: { activa: querida }", js);
        Assert.Contains("ninguna conversación puede responder con IA", js);
        Assert.Contains("La IA todavía no responde mensajes:", js);
        Assert.DoesNotContain("localStorage", js);
        Assert.DoesNotContain("console.", js);

        var html = Leer("Pantalla_Configurador", "dashboard.html");
        Assert.True(html.IndexOf("src=\"js/chats-ia.js\"", StringComparison.Ordinal) is var i && i > 0 && i < html.IndexOf("src=\"js/dashboard.js\"", StringComparison.Ordinal),
            "chats-ia.js tiene que cargarse antes que dashboard.js");
        var dashboard = Leer("Pantalla_Configurador", "js", "dashboard.js");
        Assert.Contains("if (tab === \"chatsia\")            return await renderChatsIa(contenido);", dashboard);
        var secciones = Regex.Match(dashboard, @"const SECCIONES_POR_MODULO = \{.*?\n\};", RegexOptions.Singleline).Value;
        Assert.Equal(2, Regex.Matches(secciones, "\"chatsia\"").Count);                       // dos listas: configurador y administracion
        // Cada módulo es una entrada de la tabla ("modulo: [ ... ],"): se corta por entrada para mirar solo la suya.
        string Entrada(string modulo) => Regex.Match(secciones, modulo + @": \[.*?(?=\n    \w+: \[|\n\};)", RegexOptions.Singleline).Value;
        Assert.Contains("\"chatsia\"", Entrada("configurador"));
        Assert.Contains("\"chatsia\"", Entrada("administracion"));
        Assert.DoesNotContain("chatsia", Entrada("crm"));                                       // el CRM puro no lo tiene
        Assert.DoesNotContain("chatsia", Entrada("profesional"));
        Assert.False(string.IsNullOrEmpty(Entrada("crm")));
        var shell = Leer("shared", "shell.js");
        Assert.Equal(1, Regex.Matches(shell, @"id: ""chatsia""").Count);
        // El ítem está en el grupo de Configuración (el que usan el Configurador y Administración), no en el de CRM.
        var grupo = Regex.Match(shell, @"const GRUPO_CONFIGURACION = \{.*?\n    \};", RegexOptions.Singleline).Value;
        Assert.Contains("id: \"chatsia\"", grupo);
    }

    // ── Todo compila ───────────────────────────────────────────────────────────────────────
    [FactConNodeDelServicioDeWhatsApp]
    public void Los_archivos_de_la_pantalla_compilan()
    {
        foreach (var (nombre, codigo) in new[]
        {
            ("bandeja.js", Bandeja), ("chats-ia.js", ChatsIa), ("leads.js", Leer("Pantalla_Configurador", "js", "leads.js")),
            ("dashboard.js", Leer("Pantalla_Configurador", "js", "dashboard.js")), ("shell.js", Leer("shared", "shell.js"))
        })
        {
            var (salida, _, error) = NodeDelServicioDeWhatsApp.Ejecutar(new[] { "-e", "const vm = require('vm'); try { new vm.Script(process.env.CODIGO); } catch (e) { process.stderr.write(String(e)); process.exit(1); }" },
                new Dictionary<string, string> { ["CODIGO"] = codigo });
            Assert.True(salida == 0, $"{nombre} no compila: {error}");
        }
    }
}
