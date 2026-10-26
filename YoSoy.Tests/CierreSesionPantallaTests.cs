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

    // ───────────── Próximo turno ─────────────

    [Fact]
    public void Agendar_el_proximo_turno_se_ofrece_siempre_y_solo_propone_nunca_reserva_ni_es_urgente()
    {
        var sala = Sala;
        var enviar = Funcion(sala, "async function enviarProximoTurno() {");
        var abrir = Funcion(sala, "function alternarProximoTurno(abrir) {");
        var cargar = Funcion(sala, "async function cargarHorariosProximo() {");

        // Siempre disponible: el botón está en el panel para cualquier tipo de cierre.
        Assert.Matches(@"id=""btn-cierre-proximo""[^>]*onclick=""alternarProximoTurno\(\)""", sala);
        // Los horarios son los de la reserva real; la propuesta usa el endpoint de siempre, sin cita urgente.
        Assert.Contains("/horarios-libres?dias=14", cargar);
        Assert.Contains("'/turnos/profesional-solicitar'", enviar);
        Assert.DoesNotContain("esUrgente", sala);
        // Paciente y profesional salen de la sala (no se eligen en el selector) y la hora la elige una persona.
        Assert.Contains("document.getElementById('sel-paciente').value", enviar);
        Assert.Contains("idProfesional", enviar);
        Assert.Contains("if (!proximoElegido", enviar);
        // Abrir el selector (o sugerirlo con Seguimiento) nunca envía nada: el envío solo sale del botón «Enviar propuesta».
        Assert.DoesNotContain("enviarProximoTurno", abrir);
        Assert.DoesNotContain("enviarProximoTurno", Funcion(sala, "function resaltarProximoTurno() {"));
        Assert.Matches(@"id=""btn-proximo-enviar"" onclick=""enviarProximoTurno\(\)"" disabled", sala);
        // Con Seguimiento viene sugerido; con Alta o Derivación no se abre solo.
        Assert.Contains("tipoCierreSeleccionado === 'Seguimiento' && !proximoTurnoEnviado", sala);
        // Y dice que queda pendiente hasta que el paciente lo acepte.
        Assert.Contains("Queda pendiente hasta que la acepte.", sala);
    }

    // ───────────── Borrador local ─────────────

    [Fact]
    public void El_borrador_vive_solo_en_el_equipo_nada_se_manda_al_servidor_mientras_se_escribe()
    {
        var sala = Sala;
        var crudo = sala[sala.IndexOf("// ─── BORRADOR LOCAL DE LA NOTA", StringComparison.Ordinal)..sala.IndexOf("// ─── PRÓXIMO TURNO", StringComparison.Ordinal)];
        var bloque = string.Join("\n", crudo.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));   // el código, sin los comentarios que explican lo que NO hace

        Assert.DoesNotContain("YS.api", bloque);
        Assert.DoesNotContain("fetch(", bloque);
        Assert.DoesNotContain("localStorage.setItem", bloque);                // el texto va cifrado a IndexedDB, nunca en claro a localStorage
        Assert.Contains("YS.borrador.guardar(idUsuarioLocal(), claveBorrador,", bloque);
        // Qué se guarda: nota, tipo, decisión y clave. No la transcripción ni las emociones.
        Assert.Contains("{ notas, tipoCierre: tipoCierreSeleccionado, decision: decisionRealizo, claveCierre }", bloque);
        Assert.DoesNotContain("transcripcion", bloque, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("emociones", bloque, StringComparison.OrdinalIgnoreCase);
        // Un borrador no es una firma ni una evolución.
        Assert.DoesNotContain("firm", bloque, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Todavía no está en la historia clínica", bloque);
        Assert.Contains("<script src=\"../shared/borrador.js\"></script>", sala);
    }

    [Fact]
    public void Un_borrador_anterior_se_ofrece_y_no_se_aplica_solo_y_el_guardado_lo_descarta()
    {
        var sala = Sala;
        var iniciar = Funcion(sala, "async function iniciarBorrador() {");

        // Hasta decidir, la caja de la nota queda bloqueada: no se pisa nada.
        Assert.Contains("document.getElementById('notas-evolucion').disabled = true;", iniciar);
        Assert.Contains("Recuperaste un borrador de esta sesión del", iniciar);
        Assert.DoesNotContain(".value =", iniciar);                              // aplicarlo es una acción aparte (seguirConBorrador)
        Assert.Contains("onclick=\"seguirConBorrador()\"", sala);
        Assert.Contains("onclick=\"descartarBorrador()\"", sala);
        // Sin almacenamiento cifrado no se finge: se dice.
        Assert.Contains("No se pudo guardar el borrador en este equipo", iniciar);
        // Un cierre guardado descarta el borrador.
        Assert.Contains("descartarBorradorGuardado('La sesión quedó en la historia clínica: el borrador de este equipo se descartó.');", Funcion(sala, "async function guardarEnHC() {"));
        // El borrador se arranca al entrar a la sala, antes de abrirla.
        Assert.Matches(@"analisis\.iniciar\(\);\s*// 2c\.[^\n]*\n\s*iniciarBorrador\(\);", sala);
    }

    [Fact]
    public void Cerrar_sesion_pregunta_antes_de_descartar_borradores_y_vencer_por_inactividad_no_los_borra()
    {
        var ui = File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "shared", "ui.js"));
        var borrador = File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "shared", "borrador.js"));
        var sesionJs = File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "js", "sesion.js"));
        var panel = File.ReadAllText(Path.Combine(Raiz, "PANTALLAS", "Pantalla_Profesional", "nuevo_dashboard.html"));

        Assert.Contains("YS.borradorAlCerrarSesion && !await YS.borradorAlCerrarSesion(localStorage.getItem(\"idUsuario\"))", ui);
        Assert.True(ui.IndexOf("borradorAlCerrarSesion", StringComparison.Ordinal) < ui.IndexOf("YS.sesion.limpiar();\n            location.href = YS.rutas.login;", StringComparison.Ordinal)
                    || ui.Replace("\r\n", "\n").IndexOf("borradorAlCerrarSesion", StringComparison.Ordinal) < ui.Replace("\r\n", "\n").IndexOf("YS.sesion.limpiar();\n            location.href = YS.rutas.login;", StringComparison.Ordinal));
        Assert.Contains("<script src=\"../shared/borrador.js\"></script>", panel);          // el panel principal también lo carga: ahí está el botón de cerrar sesión
        // La expiración por inactividad solo limpia localStorage (YS.sesion.limpiar), que no toca IndexedDB: el borrador sobrevive.
        Assert.DoesNotContain("indexedDB", sesionJs);
        Assert.DoesNotContain("YS.borrador", sesionJs);
        Assert.Contains("indexedDB", borrador);
    }

    // ───────────── Vínculo con una evolución ─────────────

    [Fact]
    public void El_vinculo_con_una_evolucion_es_manual_solo_despues_de_guardar_y_el_cierre_no_crea_evoluciones()
    {
        var sala = Sala;
        var guardar = Funcion(sala, "async function guardarEnHC() {");
        var buscar = Funcion(sala, "async function buscarEvolucionesVinculables() {");
        var vincular = Funcion(sala, "async function vincularEvolucion(idEvolucion, boton) {");

        // La sala nunca escribe una evolución: ni en el cierre ni en el vínculo.
        Assert.DoesNotContain("/evoluciones'", sala);
        Assert.DoesNotContain("'/evoluciones", sala);
        Assert.DoesNotContain("/historiasclinicas", sala);
        // El bloque del vínculo nace oculto y aparece recién cuando el cierre quedó guardado.
        Assert.Matches(@"id=""cierre-vinculo"" class=""cierre-vinculo oculto""", sala);
        Assert.Contains("document.getElementById('cierre-vinculo').classList.remove('oculto');", guardar);
        // Listar no vincula: el PUT sale solo del botón «Vincular» de una fila.
        Assert.DoesNotContain("method: 'PUT'", buscar);
        Assert.Contains("evoluciones-vinculables", buscar);
        Assert.Contains("method: 'PUT', json: { idEvolucion }", vincular);
        Assert.Contains("boton.onclick = () => vincularEvolucion(e.idEvolucion, boton);", buscar);
        // Lo que viene del servidor se muestra como texto.
        Assert.DoesNotContain("innerHTML", buscar);
    }
}
