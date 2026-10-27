using System.Text.Json;
using System.Text.RegularExpressions;

// La sala del profesional corre dentro de un iframe del panel, y el panel (PANTALLAS/js/sesion.js) cierra la sesión por inactividad
// mirando SU documento. Lo que pasa dentro del iframe no le llega, salvo que la sala se lo avise con el mensaje "ys-actividad".
//
// Dos clases de prueba:
//  · las que LEEN los archivos y vigilan que emisor y receptor hablen el mismo idioma y que el aviso esté enganchado donde corresponde;
//  · las que EJECUTAN el código real del aviso (el bloque de sesion_video.html, sacado tal cual) en node con relojes de mentira, para
//    probar la regla: después de finalizar la llamada el aviso SIGUE mientras haya notas sin guardar (o el panel de cierre abierto) y se
//    corta cuando la API confirma el guardado. Si no hay node, esas quedan OMITIDAS (no pasan en falso).
// Cómo se ve en un navegador de verdad (sala 11 minutos sin tocar nada del panel) queda en el guion manual del plan de Cierre de sesión.
public class SalaActividadTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(partes).ToArray()));

    private static string Sala => Leer("PANTALLAS", "Pantalla_Profesional", "sesion_video.html");
    private static string Sesion => Leer("PANTALLAS", "js", "sesion.js");

    // ───────────── Lectura de los archivos ─────────────

    [Fact]
    public void La_sala_avisa_actividad_al_panel_con_el_mensaje_que_el_panel_escucha()
    {
        // Receptor: sesion.js reinicia el temporizador con este mensaje exacto y solo si viene del mismo origen.
        Assert.Matches(@"e\.origin === location\.origin && e\.data === ""ys-actividad""", Sesion);

        // Emisor: manda ese mismo mensaje, al padre y con el origen propio (nunca "*").
        var sala = Sala;
        Assert.Matches(@"window\.parent\.postMessage\('ys-actividad', location\.origin\)", sala);
        Assert.DoesNotMatch(@"postMessage\([^)]*,\s*'\*'\s*\)", sala);
    }

    [Fact]
    public void El_aviso_es_mas_frecuente_que_el_aviso_de_inactividad_del_panel()
    {
        var sala = Sala;
        var cada = int.Parse(Regex.Match(sala, @"const INTERVALO_ACTIVIDAD_MS = (\d+);").Groups[1].Value);
        var avisoPanel = int.Parse(Regex.Match(Sesion, @"const AVISO_MS\s*=\s*(\d+)\s*\*\s*60\s*\*\s*1000;").Groups[1].Value) * 60 * 1000;

        Assert.InRange(cada, 1000, avisoPanel / 2);   // al menos dos avisos antes de que el panel muestre su advertencia
    }

    [Fact]
    public void El_aviso_arranca_al_entrar_a_la_sala_y_su_corte_esta_enganchado_donde_corresponde()
    {
        var sala = Sala;

        // Arranca justo antes de abrir la sala (la espera del paciente también puede pasar de 10 minutos).
        Assert.Matches(@"iniciarAvisoDeActividad\(\);\s*control = YS\.video\.entrar\(", sala);
        // Al finalizar la llamada NO se corta a ciegas: se marca la sala como finalizada y se pregunta si queda algo que perder.
        Assert.Matches(@"async function finalizarSesion\(\) \{\s*salaFinalizada = true;\s*soltarAvisoSiNoHaceFalta\(\);", sala);
        Assert.DoesNotMatch(@"async function finalizarSesion\(\) \{\s*detenerAvisoDeActividad\(\);", sala);
        // Cuando la API confirma el guardado, se vuelve a preguntar (y ahí se corta si ya no queda nada).
        Assert.Matches(@"guardadoEnHC = true;\s*soltarAvisoSiNoHaceFalta\(\);", sala);
        // Si nunca se llegó a la llamada (falló la conexión), se corta sin más.
        Assert.Matches(@"function volverAConexion\(motivo\) \{\s*detenerAvisoDeActividad\(\);", sala);
        // Y no se duplica el reloj si se llama dos veces.
        Assert.Matches(@"function iniciarAvisoDeActividad\(\) \{\s*if \(avisoActividadTimer\) return;", sala);
    }

    [Fact]
    public void Cerrar_o_recargar_con_notas_sin_guardar_pregunta_antes_y_guardar_en_la_historia_lo_levanta()
    {
        var sala = Sala;

        // También pregunta si queda audio grabado sin subir (transcripción local): perderlo no se puede deshacer.
        Assert.Matches(@"window\.addEventListener\('beforeunload', e => \{\s*if \(!hayNotasSinGuardar\(\) && !\(grabador && grabador\.haySinSubir\(\)\)\) return;", sala);
        Assert.Contains("e.preventDefault();", sala);
        // "Sin guardar" = hay texto en las notas y todavía no se guardó en la historia clínica.
        Assert.Matches(@"return !guardadoEnHC && document\.getElementById\('notas-evolucion'\)\.value\.trim\(\) !== '';", sala);
        // La marca se levanta recién cuando la API confirma el guardado.
        Assert.Matches(@"btn\.textContent = '✅ Guardado en HC';\s*guardadoEnHC = true;", sala);
    }

    // ───────────── Ejecución del código real ─────────────

    private static string BloqueDelAviso()
    {
        var sala = Sala.Replace("\r\n", "\n");
        var inicio = sala.IndexOf("const INTERVALO_ACTIVIDAD_MS", StringComparison.Ordinal);
        var fin = sala.IndexOf("window.addEventListener('beforeunload'", StringComparison.Ordinal);
        Assert.True(inicio >= 0 && fin > inicio, "no encuentro el bloque del aviso de actividad en sesion_video.html");
        return sala[inicio..fin];
    }

    // Un reloj de mentira (setInterval/clearInterval controlados) y un "padre" que anota los mensajes. El bloque real corre en un vm
    // con solo eso a mano. Los escenarios imitan lo que hacen finalizarSesion y guardarEnHC: marcar el estado y llamar a
    // soltarAvisoSiNoHaceFalta.
    private const string Banco = """
        const vm = require('vm');
        let ahora = 0, siguiente = 1;
        const relojes = new Map();
        const mensajes = [];
        const notas = { value: '', _oyentes: [], addEventListener(t, f) { this._oyentes.push(f); } };
        const contexto = vm.createContext({
            setInterval(f, ms) { const id = siguiente++; relojes.set(id, { f, ms, prox: ahora + ms }); return id; },
            clearInterval(id) { relojes.delete(id); },
            document: { getElementById: id => id === 'notas-evolucion' ? notas : null },
            window: { parent: { postMessage: (m, o) => mensajes.push({ m, o, t: ahora }) } },
            location: { origin: 'http://panel' },
        });
        vm.runInContext(process.env.BLOQUE + "\nthis.__api = { iniciarAvisoDeActividad, soltarAvisoSiNoHaceFalta, detenerAvisoDeActividad,"
            + " fijar(n, v) { if (n === 'salaFinalizada') salaFinalizada = v; if (n === 'panelDeCierreAbierto') panelDeCierreAbierto = v;"
            + " if (n === 'guardadoEnHC') guardadoEnHC = v; }, activo: () => avisoActividadTimer !== null };", contexto);
        const api = contexto.__api;
        function avanzar(ms) {
            const hasta = ahora + ms;
            for (;;) {
                let prox = null;
                for (const r of relojes.values()) if (r.prox <= hasta && (prox === null || r.prox < prox.prox)) prox = r;
                if (!prox) break;
                ahora = prox.prox; prox.prox += prox.ms; prox.f();
            }
            ahora = hasta;
        }
        const emitidos = () => mensajes.filter(x => x.m === 'ys-actividad').length;
        const escribir = txt => { notas.value = txt; notas._oyentes.forEach(f => f()); };
        """;

    private static JsonElement Correr(string escenario)
    {
        var (codigo, salida, error) = NodeDelServicioDeWhatsApp.Ejecutar(
            new[] { "-e", Banco + "\n" + escenario },
            new Dictionary<string, string> { ["BLOQUE"] = BloqueDelAviso() });
        Assert.True(codigo == 0, $"node terminó con {codigo}: {error}");
        return JsonDocument.Parse(salida.Trim()).RootElement;
    }

    private static string? SinNode => NodeDelServicioDeWhatsApp.NoDisponible();

    [SkippableNode]
    public void Despues_de_finalizar_el_aviso_sigue_mientras_haya_notas_sin_guardar_y_se_corta_al_guardarse()
    {
        var r = Correr("""
            const r = {};
            api.iniciarAvisoDeActividad();                 // entra a la sala: un aviso inmediato
            avanzar(120000);                               // dos minutos de llamada
            r.enLlamada = emitidos();
            escribir('Paciente refiere mejoría');          // anota durante la llamada
            api.fijar('salaFinalizada', true);             // finalizarSesion()
            api.soltarAvisoSiNoHaceFalta();
            r.activoTrasFinalizar = api.activo();
            const antes = emitidos();
            avanzar(600000);                               // diez minutos más, sin guardar
            r.avisosSinGuardar = emitidos() - antes;
            r.activoSinGuardar = api.activo();
            api.fijar('guardadoEnHC', true);               // la API confirmó el guardado
            api.soltarAvisoSiNoHaceFalta();
            r.activoTrasGuardar = api.activo();
            const despues = emitidos();
            avanzar(600000);
            r.avisosTrasGuardar = emitidos() - despues;
            r.origen = [...new Set(mensajes.map(x => x.o))];
            console.log(JSON.stringify(r));
            """);

        Assert.Equal(3, r.GetProperty("enLlamada").GetInt32());                  // 1 al entrar + 2 en dos minutos
        Assert.True(r.GetProperty("activoTrasFinalizar").GetBoolean());          // finalizar NO cortó el aviso
        Assert.Equal(10, r.GetProperty("avisosSinGuardar").GetInt32());          // sigue cada 60 s con las notas sin guardar
        Assert.True(r.GetProperty("activoSinGuardar").GetBoolean());
        Assert.False(r.GetProperty("activoTrasGuardar").GetBoolean());           // guardado confirmado: se corta
        Assert.Equal(0, r.GetProperty("avisosTrasGuardar").GetInt32());
        Assert.Equal(new[] { "http://panel" }, r.GetProperty("origen").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [SkippableNode]
    public void Si_al_finalizar_no_hay_notas_ni_panel_abierto_el_aviso_se_corta_pero_vuelve_si_se_escribe_despues()
    {
        var r = Correr("""
            const r = {};
            api.iniciarAvisoDeActividad();
            avanzar(60000);
            api.fijar('salaFinalizada', true);             // finalizarSesion() sin notas
            api.soltarAvisoSiNoHaceFalta();
            r.cortadoSinNotas = !api.activo();
            escribir('Se me ocurrió algo después de cortar');   // escribe tras finalizar
            r.volvio = api.activo();
            const antes = emitidos();
            avanzar(180000);
            r.avisosDespues = emitidos() - antes;
            console.log(JSON.stringify(r));
            """);

        Assert.True(r.GetProperty("cortadoSinNotas").GetBoolean());
        Assert.True(r.GetProperty("volvio").GetBoolean());
        Assert.Equal(3, r.GetProperty("avisosDespues").GetInt32());   // uno por minuto durante tres minutos (el inmediato de volver ya quedó afuera de la cuenta)
    }

    [SkippableNode]
    public void El_panel_de_cierre_abierto_retiene_el_aviso_aunque_no_haya_notas_y_al_cerrarse_se_corta()
    {
        var r = Correr("""
            const r = {};
            api.iniciarAvisoDeActividad();
            api.fijar('panelDeCierreAbierto', true);
            api.fijar('salaFinalizada', true);
            api.soltarAvisoSiNoHaceFalta();
            r.activoConPanel = api.activo();
            const antes = emitidos();
            avanzar(300000);
            r.avisosConPanel = emitidos() - antes;
            api.fijar('panelDeCierreAbierto', false);
            api.soltarAvisoSiNoHaceFalta();
            r.activoSinPanel = api.activo();
            console.log(JSON.stringify(r));
            """);

        Assert.True(r.GetProperty("activoConPanel").GetBoolean());
        Assert.Equal(5, r.GetProperty("avisosConPanel").GetInt32());
        Assert.False(r.GetProperty("activoSinPanel").GetBoolean());
    }

    [SkippableNode]
    public void Si_borra_las_notas_con_el_aviso_en_marcha_el_proximo_latido_lo_corta()
    {
        var r = Correr("""
            const r = {};
            api.iniciarAvisoDeActividad();
            escribir('algo');
            api.fijar('salaFinalizada', true);
            api.soltarAvisoSiNoHaceFalta();
            r.sigue = api.activo();
            escribir('');                                  // las borra a mano
            avanzar(60000);                                // el latido ve que ya no hay nada que perder
            r.cortado = !api.activo();
            console.log(JSON.stringify(r));
            """);

        Assert.True(r.GetProperty("sigue").GetBoolean());
        Assert.True(r.GetProperty("cortado").GetBoolean());
    }

    /// <summary>Un [Fact] que se omite si no hay node (las pruebas de lectura de archivos no lo necesitan).</summary>
    private sealed class SkippableNodeAttribute : FactAttribute
    {
        public SkippableNodeAttribute()
        {
            var motivo = SinNode;
            if (motivo is not null) Skip = motivo;
        }
    }
}
