using System.Text.RegularExpressions;

// Las pantallas del alta y la derivación asistidas, leídas como texto: la campanita del profesional (shared/shell.js, donde el destino acepta o rechaza) y la tarea de Admisión «Traspasos por confirmar»
// (Pantalla_Configurador/js/traspasos.js). El asistente de la sala se vigila en CierreSesionPantallaTests; cómo se ve en un navegador de verdad, en el banco de Playwright que se corre a mano.
public class AltaDerivacionPantallasTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz, "PANTALLAS" }.Concat(partes).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void La_campanita_del_profesional_muestra_las_derivaciones_por_responder_con_aceptar_y_rechazar_y_no_cambia_vinculos()
    {
        var shell = Leer("shared", "shell.js");

        Assert.Contains("/avisos-clinicos`", shell);
        Assert.Contains("texto: \"Aceptar\"", shell);
        Assert.Contains("texto: \"Rechazar\"", shell);
        // Responder es UN pedido a la derivación; no reasigna ni toca vínculos (eso lo confirma Admisión).
        var responder = shell[shell.IndexOf("async function responderDerivacion", StringComparison.Ordinal)..shell.IndexOf("async function marcarAvisoClinico", StringComparison.Ordinal)];
        Assert.Contains("`/derivaciones/${idDerivacion}/responder`", responder);
        Assert.DoesNotContain("reasignar", responder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("traspasar", responder, StringComparison.OrdinalIgnoreCase);
        // Rechazar deja contar el motivo (opcional) y se puede cancelar sin enviar nada.
        Assert.Contains("if (motivo === null) return;", responder);
        // Si la API no tiene los avisos (base sin la migración 27), la campanita sigue como siempre.
        Assert.Matches(@"avisos-clinicos`\)\.catch\(\(\) => \(\{ avisos: \[\], porResponder: \[\] \}\)\)", shell);
        // Los avisos se muestran como texto (YS.esc en dibujar) y el de «derivación pendiente» no se repite sin botones.
        Assert.Contains("a.tipo !== \"derivacion-pendiente\"", shell);
        // La pantalla principal del profesional no se tocó para esto (tiene conflicto conocido con otra rama).
        Assert.DoesNotContain("avisos-clinicos", Leer("Pantalla_Profesional", "js", "nuevo_dashboard.js"));
    }

    [Fact]
    public void La_tarea_de_Admision_pide_confirmacion_antes_de_traspasar_y_cuelga_de_la_pantalla_de_derivaciones()
    {
        var traspasos = Leer("Pantalla_Configurador", "js", "traspasos.js");
        var dashboard = Leer("Pantalla_Configurador", "js", "dashboard.js");
        var html = Leer("Pantalla_Configurador", "dashboard.html");

        Assert.Contains("/derivaciones/traspasos-pendientes", traspasos);
        var confirmar = traspasos[traspasos.IndexOf("async function confirmarTraspaso", StringComparison.Ordinal)..traspasos.IndexOf("async function resolverDerivacionRechazada", StringComparison.Ordinal)];
        // Primero la confirmación explícita, recién después el pedido.
        Assert.True(confirmar.IndexOf("YS.confirmar", StringComparison.Ordinal) < confirmar.IndexOf("/traspasar", StringComparison.Ordinal));
        Assert.Contains("if (!ok) return;", confirmar);
        Assert.Contains("El vínculo del paciente no cambia hasta que confirmes el traspaso", traspasos);
        // Las rechazadas se «dan por resueltas»: tampoco cambian vínculos.
        Assert.Contains("/resolver", traspasos);
        Assert.DoesNotContain("/casos/", traspasos);
        // Se engancha al final de la lista de derivaciones de siempre y su script carga antes que dashboard.js.
        Assert.Contains("await renderTraspasosPorConfirmar(c);", dashboard);
        Assert.True(html.IndexOf("src=\"js/traspasos.js\"", StringComparison.Ordinal) is var i && i > 0 && i < html.IndexOf("src=\"js/dashboard.js\"", StringComparison.Ordinal));
        // Todo lo que viene del servidor se arma con textContent (YS.el con text), nunca con innerHTML.
        Assert.DoesNotContain("innerHTML", traspasos);
    }
}
