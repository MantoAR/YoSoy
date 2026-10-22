using System.Text.Json;

// Ejecuta las funciones reales de bandeja.js con Node y un DOM mínimo, como las
// pruebas de WhatsAppEstadoJsTests. Solo se exponen los cierres dentro del arnés.
public class VentanaConversacionJsTests
{
    private const string Script = """
        const fs = require('fs'), vm = require('vm');
        const entrada = JSON.parse(process.env.ENTRADA);
        let confirmarVinculo;
        let ahora = 1000, quitadas = 0, errores = 0, pedidos = 0, avisos = 0;
        const temporizadores = [];
        function nodo() {
            const clases = new Set();
            return { value: '', textContent: '', style: {}, scrollHeight: 40, disabled: false,
                classList: { add: c => clases.add(c), remove: c => clases.delete(c), contains: c => clases.has(c),
                    toggle(c, si) { if (si) clases.add(c); else clases.delete(c); } },
                setAttribute() {}, appendChild() {}, append() {}, focus() {} };
        }
        const campos = Object.fromEntries(['chatsTexto', 'chatsEnviar', 'chatsBtnPlantillas', 'chatsCandado',
            'chatsVentana', 'chatsAyudaComposer', 'chatsPlantillas'].map(id => ['#' + id, nodo()]));
        const raiz = { querySelector: s => s.startsWith('[data-tmp=')
            ? { remove() { quitadas++; }, replaceWith() { errores++; } } : campos[s] || null };
        const YS = { confirmar: async () => true, esc: s => s,
            modal: () => ({ cerrar() {}, cuerpo: { innerHTML: '', querySelector: () => ({ focus() {}, addEventListener: (evento, fn) => confirmarVinculo = fn }) } }),
            debounce: f => f, el: () => nodo(), toast: () => avisos++,
            api: async () => { pedidos++; throw Object.assign(new Error('rechazo'), {
                status: entrada.status ?? 409,
                datos: { codigo: entrada.codigo ?? 'ventana_24h_cerrada', motivo: entrada.motivo ?? 'vencida',
                    mensaje: entrada.mensaje ?? 'Explicación del servidor' } }); } };
        let codigo = fs.readFileSync(process.env.MODULO, 'utf8');
        codigo = codigo.replace('YS.bandeja = {',
            'YS.pruebas = { est, ventanaActual, actualizarComposer, mandar, enviar, fusionarResumen, tomarControl, abrirVincular, prepararResumen() { actualizarContadores = () => {}; dibujarLista = () => {}; dibujarCabeceraHilo = () => actualizarComposer(est.convs.get(est.actual)); cargarContexto = () => {}; }, raiz: r => raiz = r }; YS.bandeja = {');
        vm.runInNewContext(codigo, { window: { YS }, document: { hidden: false }, performance: { now: () => ahora },
            setTimeout(fn, ms) { temporizadores.push({fn, ms}); return temporizadores.length; }, clearTimeout() {} });
        const p = YS.pruebas;
        p.raiz(raiz);
        const c = { idConversacion: 7, estado: entrada.estado || 'DerivadaHumano',
            ventana: Object.hasOwn(entrada, 'ventana') ? entrada.ventana : { abierta: true, motivo: null, segundosRestantes: 60 }, ventanaRecibidaEn: 1000 };
        if (entrada.sinVentana) delete c.ventana;
        p.est.actual = entrada.otra ? 8 : 7; p.est.generacion = 1; p.est.convs.set(7, c);
        campos['#chatsTexto'].value = entrada.texto || '';
        (async () => {
            let resultado;
            if (entrada.modo === 'reloj') {
                resultado = [1000, 31000, 61000, 91000].map(t => p.ventanaActual(c, t));
            } else if (entrada.modo === 'envio-vencido' || entrada.modo === 'envio-largo') {
                if (entrada.modo === 'envio-vencido') ahora = 61000;
                await p.enviar();
                resultado = { pedidos, avisos, texto: campos['#chatsTexto'].value, pendientes: p.est.pendientes.length };
            } else {
                if (entrada.modo === 'rechazo') {
                    const pendiente = { id: 1, conv: 7, texto: 'Texto rechazado', error: null };
                    p.est.pendientes = [pendiente];
                    await p.mandar(pendiente);
                } else {
                    if (entrada.actualizacion) {
                        p.prepararResumen();
                        ahora = 31000;
                        YS.api = async url => url.endsWith('/conversaciones')
                            ? [{ idConversacion: 7, estado: 'DerivadaHumano', nombrePaciente: 'Actualizado', telefono: '123', ventana: null }] : {};
                        if (entrada.actualizacion === 'tomar-control') await p.tomarControl(7);
                        else {
                            p.abrirVincular(7, { idPaciente: 3, nombre: 'Prueba', dniParcial: '123' });
                            await confirmarVinculo();
                        }
                    }
                    p.actualizarComposer(p.est.convs.get(7));
                    if (entrada.expirar) { ahora = 61000; temporizadores.at(-1).fn(); }
                    if (entrada.reabrir) {
                        c.ventana = { abierta: true, motivo: null, segundosRestantes: 60 };
                        c.ventanaRecibidaEn = ahora;
                        p.actualizarComposer(c);
                    }
                }
                resultado = { bloqueado: campos['#chatsTexto'].disabled, enviarBloqueado: campos['#chatsEnviar'].disabled,
                    nombrePaciente: p.est.convs.get(7).nombrePaciente, recibidaEn: p.est.convs.get(7).ventanaRecibidaEn, temporizadores: temporizadores.length,
                    plantillasBloqueadas: campos['#chatsBtnPlantillas'].disabled,
                    candadoOculto: campos['#chatsCandado'].classList.contains('oculto'),
                    explicacion: campos['#chatsVentana'].textContent, texto: campos['#chatsTexto'].value,
                    pendientes: p.est.pendientes.length, borrador: p.est.borradoresVentana.get(7) || null, quitadas, errores, pedidos };
            }
            console.log(JSON.stringify(resultado));
        })().catch(e => { console.error(e); process.exitCode = 1; });
        """;

    private static JsonElement Evaluar(object entrada)
    {
        var (codigo, salida, error) = NodeDelServicioDeWhatsApp.Ejecutar(new[] { "-e", Script }, new()
        {
            ["MODULO"] = Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "PANTALLAS", "Pantalla_ChatSimulado", "js", "bandeja.js"),
            ["ENTRADA"] = JsonSerializer.Serialize(entrada)
        });
        Assert.True(codigo == 0, error);
        return JsonDocument.Parse(salida.Trim()).RootElement.Clone();
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Sin_campo_ventana_o_con_null_el_compositor_queda_habilitado_sin_temporizador()
    {
        foreach (var entrada in new object[] { new { sinVentana = true }, new { ventana = (object?)null } })
        {
            var r = Evaluar(entrada);
            Assert.False(r.GetProperty("bloqueado").GetBoolean());
            Assert.False(r.GetProperty("enviarBloqueado").GetBoolean());
            Assert.False(r.GetProperty("plantillasBloqueadas").GetBoolean());
            Assert.Equal(0, r.GetProperty("temporizadores").GetInt32());
        }
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Vincular_y_tomar_control_con_resumen_parcial_conservan_la_ventana_cerrada_y_su_reloj()
    {
        foreach (var accion in new[] { "vincular", "tomar-control" })
        {
            var r = Evaluar(new { actualizacion = accion,
                ventana = new { abierta = false, motivo = "vencida", segundosRestantes = 0 } });
            Assert.True(r.GetProperty("bloqueado").GetBoolean());
            Assert.True(r.GetProperty("enviarBloqueado").GetBoolean());
            Assert.Equal(1000, r.GetProperty("recibidaEn").GetInt32());
            Assert.Equal("Actualizado", r.GetProperty("nombrePaciente").GetString());
        }
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Los_segundos_se_descuentan_localmente_y_al_llegar_a_cero_la_ventana_cierra()
    {
        var r = Evaluar(new { modo = "reloj" }).EnumerateArray().ToArray();
        Assert.Equal(new long[] { 60, 30, 0, 0 }, r.Select(v => v.GetProperty("segundosRestantes").GetInt64()));
        Assert.Equal(new[] { true, true, false, false }, r.Select(v => v.GetProperty("abierta").GetBoolean()));
        Assert.Equal("vencida", r[2].GetProperty("motivo").GetString());
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void El_compositor_se_bloquea_con_candado_y_texto_al_vencer_sin_otro_polling()
    {
        var r = Evaluar(new { modo = "compositor", expirar = true });
        Assert.True(r.GetProperty("bloqueado").GetBoolean());
        Assert.True(r.GetProperty("enviarBloqueado").GetBoolean());
        Assert.True(r.GetProperty("plantillasBloqueadas").GetBoolean());
        Assert.False(r.GetProperty("candadoOculto").GetBoolean());
        Assert.Equal(VentanaDeConversacion.MensajeVencida, r.GetProperty("explicacion").GetString());
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Si_nunca_escribio_se_muestra_el_texto_acordado_y_un_nuevo_entrante_habilita_el_compositor()
    {
        var cerrada = new { abierta = false, motivo = "nunca_escribio", segundosRestantes = 0 };
        var r = Evaluar(new { modo = "compositor", ventana = cerrada });
        Assert.True(r.GetProperty("bloqueado").GetBoolean());
        Assert.Equal(VentanaDeConversacion.MensajeNuncaEscribio, r.GetProperty("explicacion").GetString());
        var nueva = Evaluar(new { modo = "compositor", ventana = cerrada, reabrir = true });
        Assert.False(nueva.GetProperty("bloqueado").GetBoolean());
        Assert.True(nueva.GetProperty("candadoOculto").GetBoolean());
        Assert.Equal("", nueva.GetProperty("explicacion").GetString());
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void El_409_de_ventana_recupera_el_texto_y_el_borrador_nuevo_sin_burbuja_de_error()
    {
        var r = Evaluar(new { modo = "rechazo", texto = "Borrador nuevo", mensaje = VentanaDeConversacion.MensajeVencida });
        Assert.Equal("Texto rechazado\nBorrador nuevo", r.GetProperty("texto").GetString());
        Assert.Equal(0, r.GetProperty("pendientes").GetInt32());
        Assert.Equal(1, r.GetProperty("quitadas").GetInt32());
        Assert.Equal(0, r.GetProperty("errores").GetInt32());
        Assert.True(r.GetProperty("bloqueado").GetBoolean());
        Assert.Equal(VentanaDeConversacion.MensajeVencida, r.GetProperty("explicacion").GetString());
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Un_409_tardio_no_pisa_el_texto_de_otra_conversacion_y_conserva_el_borrador_rechazado()
    {
        var r = Evaluar(new { modo = "rechazo", otra = true, texto = "Otra conversación" });
        Assert.Equal("Otra conversación", r.GetProperty("texto").GetString());
        Assert.Equal("Texto rechazado", r.GetProperty("borrador").GetString());
        Assert.Equal(0, r.GetProperty("pendientes").GetInt32());
        Assert.Equal(0, r.GetProperty("errores").GetInt32());
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Otros_errores_conservan_la_burbuja_y_el_reintento()
    {
        foreach (var (status, codigo) in new[] { (409, "otro_codigo"), (500, "ventana_24h_cerrada") })
        {
            var r = Evaluar(new { modo = "rechazo", status, codigo });
            Assert.Equal(1, r.GetProperty("pendientes").GetInt32());
            Assert.Equal(0, r.GetProperty("quitadas").GetInt32());
            Assert.Equal(1, r.GetProperty("errores").GetInt32());
        }
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Enviar_no_hace_un_pedido_si_el_tiempo_local_ya_vencio()
    {
        var r = Evaluar(new { modo = "envio-vencido", texto = "Mi borrador" });
        Assert.Equal(0, r.GetProperty("pedidos").GetInt32());
        Assert.Equal(0, r.GetProperty("pendientes").GetInt32());
        Assert.Equal("Mi borrador", r.GetProperty("texto").GetString());
    }

    [FactConNodeDelServicioDeWhatsApp]
    public void Un_borrador_recuperado_demasiado_largo_se_conserva_sin_enviarse_ni_recortarse()
    {
        var texto = new string('x', 4001);
        var r = Evaluar(new { modo = "envio-largo", texto });
        Assert.Equal(0, r.GetProperty("pedidos").GetInt32());
        Assert.Equal(1, r.GetProperty("avisos").GetInt32());
        Assert.Equal(texto, r.GetProperty("texto").GetString());
    }

    [Fact]
    public void El_DOM_incluye_el_candado_compartido_y_la_explicacion_accesible()
    {
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        var js = File.ReadAllText(Path.Combine(raiz, "PANTALLAS", "Pantalla_ChatSimulado", "js", "bandeja.js"));
        var css = File.ReadAllText(Path.Combine(raiz, "PANTALLAS", "Pantalla_ChatSimulado", "css", "bandeja.css"));
        Assert.Contains("YS.icono(\"candado\")", js);
        Assert.Contains("id=\"chatsVentana\" role=\"status\"", js);
        Assert.Contains("aria-describedby", js);
        Assert.Contains(".chats-candado", css);
        Assert.Contains(".chats-ventana", css);
    }
}
