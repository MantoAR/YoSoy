using System.Text.RegularExpressions;

// El panel de cierre de la sala del profesional (PANTALLAS/Pantalla_Profesional/sesion_video.html), leído como texto. Cómo se comporta en un navegador de verdad (decisiones,
// reintento con la misma clave, borrador, selector de próximo turno) queda en el guion manual de docs/CIERRE_SESION.md y en el banco de Playwright que se corre a mano.
// Acá se vigila lo que no puede cambiar sin que alguien lo note: que nada venga elegido, que «Decidir después» no mande nada y que la confirmación sea un clic explícito.
public class CierreSesionPantallaTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Sala => File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "Pantalla_Profesional", "sesion_video.html")).Replace("\r\n", "\n");

    private static string Funcion(string html, string firma)
    {
        var i = html.IndexOf(firma, StringComparison.Ordinal);
        Assert.True(i >= 0, $"no encuentro {firma}");
        var fin = html.IndexOf("\n}\n", i, StringComparison.Ordinal);
        return html[i..(fin + 3)];
    }

    [Fact]
    public void El_panel_ofrece_las_tres_respuestas_sin_ninguna_elegida_y_el_guardado_nace_deshabilitado()
    {
        var sala = Sala;
        var panel = sala[sala.IndexOf("<section id=\"panel-cierre\"", StringComparison.Ordinal)..];
        panel = panel[..panel.IndexOf("</section>", StringComparison.Ordinal)];

        Assert.Contains("Sí, se realizó", panel);
        Assert.Contains("No se realizó", panel);
        Assert.Contains("Decidir después", panel);
        Assert.Equal(3, Regex.Matches(panel, @"<input type=""radio"" name=""cierre-realizo""").Count);
        Assert.DoesNotContain("checked", panel);                                        // nada viene preseleccionado
        Assert.Matches(@"id=""btn-cierre-guardar""[^>]*disabled", panel);
        Assert.Contains("Anotar evolución", panel);
        Assert.Contains("data-tipo=\"Seguimiento\"", panel);
        Assert.Contains("data-tipo=\"Alta\"", panel);
        Assert.Contains("data-tipo=\"Derivación\"", panel);
        Assert.Matches(@"id=""panel-cierre"" class=""card panel-cierre oculto""", sala);   // aparece recién al finalizar la llamada
    }

    [Fact]
    public void Ausente_no_existe_ni_en_el_panel_ni_en_lo_que_la_sala_manda()
    {
        Assert.DoesNotContain("Ausente", Sala, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abandono", Sala, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Solo_el_clic_en_Si_se_realizo_confirma_y_decidir_despues_no_manda_nada()
    {
        var guardar = Funcion(Sala, "async function guardarEnHC() {");

        // La confirmación sale de la decisión explícita, nunca de un valor por defecto.
        Assert.Contains("confirmaRealizacion: hayTurno() && decisionRealizo === 'si'", guardar);
        // «Decidir después» vuelve ANTES de cualquier pedido al servidor.
        var despues = guardar.IndexOf("decisionRealizo === 'despues'", StringComparison.Ordinal);
        var pedido = guardar.IndexOf("YS.api('/sesiones-clinicas'", StringComparison.Ordinal);
        Assert.True(despues >= 0 && pedido > despues, "«Decidir después» tiene que resolverse antes del pedido");
        Assert.Matches(@"decisionRealizo === 'despues'\) \{[^}]*return;", guardar);
        // Con turno, no se guarda sin haber decidido.
        Assert.Contains("hayTurno() && decisionRealizo === null", guardar);
        // La clave del cierre es una sola por cierre: se genera al abrir el panel y se reusa en cada reintento.
        Assert.Contains("claveCierre:        claveCierre", guardar);
        Assert.Contains("claveCierre = claveCierre || nuevaClaveCierre();", Sala);
    }

    [Fact]
    public void Despues_de_guardar_la_sala_suelta_la_sesion_del_panel_y_avisa_con_un_evento()
    {
        var guardar = Funcion(Sala, "async function guardarEnHC() {");

        Assert.Matches(@"panelDeCierreAbierto = false;[^\n]*\n\s*btn\.textContent = '✅ Guardado en HC';\s*guardadoEnHC = true;\s*soltarAvisoSiNoHaceFalta\(\);", guardar);
        Assert.Contains("new CustomEvent('ys:cierre-guardado', { detail: r.siguientePaso })", guardar);
        Assert.Contains("// HOOK:", guardar);
    }

    [Fact]
    public void Alta_y_derivacion_solo_registran_el_tipo_y_avisan_que_falta_completar()
    {
        var sala = Sala;

        Assert.Contains("Falta completar ${d.cosa} del paciente", sala);
        Assert.Contains("'Alta': { cosa: 'el alta', seccion: 'pacientes'", sala);
        Assert.Contains("'Derivación': { cosa: 'la derivación', seccion: 'casos'", sala);
        // Ninguna de las dos hace un pedido propio: lo único que sale de la sala es POST /sesiones-clinicas con el tipo.
        Assert.DoesNotContain("/derivaciones", sala);
        Assert.DoesNotContain("/altas", sala);
    }

    [Fact]
    public void Lo_que_viene_del_servidor_se_muestra_con_textContent_nunca_como_HTML()
    {
        var sala = Sala;
        var resultado = Funcion(sala, "function mostrarResultadoDelCierre(");
        Assert.DoesNotContain("innerHTML", resultado);
        Assert.Contains("li.textContent = t;", resultado);
        Assert.Contains("document.getElementById('cierre-mensaje').textContent = texto;", sala);
    }

    [Fact]
    public void Finalizar_conserva_lo_de_siempre_corta_camara_y_analisis_y_abre_el_panel()
    {
        var finalizar = Funcion(Sala, "async function finalizarSesion() {");

        Assert.Contains("analisis?.detener();", finalizar);
        Assert.Contains("stream?.getTracks().forEach(t => t.stop());", finalizar);
        Assert.Contains("control?.cerrar();", finalizar);
        Assert.Contains("abrirPanelDeCierre();", finalizar);
        // Todo lo anterior se hace antes de mostrar el panel.
        Assert.True(finalizar.IndexOf("control?.cerrar();", StringComparison.Ordinal) < finalizar.IndexOf("abrirPanelDeCierre();", StringComparison.Ordinal));
    }
}
