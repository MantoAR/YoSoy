// El tilde de la constancia «Consentimiento firmado» que se abre en una ventana del panel del paciente ocupaba media pantalla: el panel arma la constancia con la clase
// .firmado-icono (js/primeros_pasos.js) pero solo css/consentimiento.css, que el panel no carga, le daba tamaño. Se vigila que el panel tenga las reglas.
public class ConstanciaConsentimientoPantallaTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] ruta) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(ruta).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void El_tilde_de_la_constancia_del_consentimiento_tiene_tamano_fijo_en_el_panel_del_paciente()
    {
        var css = Leer("PANTALLAS", "Pantalla_Paciente", "css", "dashboard.css");
        // El panel arma la constancia con la clase .firmado-icono (js/primeros_pasos.js) pero solo css/consentimiento.css la tenía: sin estas reglas el SVG se estiraba.
        Assert.Matches(@"\.firmado-icono\s*\{[^}]*width:\s*36px;\s*height:\s*36px", css);
        Assert.Matches(@"\.firmado-icono svg\s*\{\s*width:\s*18px;\s*height:\s*18px;\s*\}", css);
        Assert.Contains("firmado-icono", Leer("PANTALLAS", "Pantalla_Paciente", "js", "primeros_pasos.js"));
        // Los otros bloques de éxito con el mismo tilde ya traían su tamaño.
        Assert.Matches(@"\.admision-realizada-icono svg\s*\{\s*width:\s*18px;\s*height:\s*18px;\s*\}", css);
    }
}
