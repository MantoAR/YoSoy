using System.Text.RegularExpressions;

// La escena de la sala (el recuadro del otro participante) tiene UN solo juego de reglas de tamaño, en shared/sala.css, y las dos pantallas lo usan tal cual.
// Causa del defecto que esto impide: `.sala-marco { width: 100% }` con `max-height` y sin ancho máximo -> en una pantalla de 1920 px el paciente veía una franja de ~1860x620 y object-fit: cover le cortaba la cara.
public class SalaEscenaTamanioTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] ruta) => File.ReadAllText(Path.Combine(new[] { Raiz, "PANTALLAS" }.Concat(ruta).ToArray())).Replace("\r\n", "\n");
    private static readonly string[] Paginas = { "Pantalla_Paciente/sesion_video.html", "Pantalla_Profesional/sesion_video.html" };

    private static string Regla(string css, string selector)
    {
        var m = Regex.Match(css, Regex.Escape(selector) + @"\s*\{([^}]*)\}");
        Assert.True(m.Success, $"no se encontró la regla {selector}");
        return m.Groups[1].Value;
    }

    [Fact]
    public void El_marco_de_la_escena_tiene_proporcion_fija_y_ancho_maximo()
    {
        var r = Regla(Leer("shared", "sala.css"), ".sala-marco");
        Assert.Contains("aspect-ratio: 16 / 9", r);
        Assert.Matches(@"width:\s*min\(100%,\s*\d+px", r);                 // ancho máximo en px
        Assert.Matches(@"margin:\s*0 auto", r);                            // centrado
        Assert.DoesNotMatch(@"(^|[\s;])width:\s*100%\s*;", r);             // nunca 100% pelado
        Assert.DoesNotContain("max-height", r);                            // el tope de alto sin tope de ancho achata la proporción
    }

    [Fact]
    public void El_video_principal_no_recorta_y_la_miniatura_si()
    {
        var css = Leer("shared", "sala.css");
        Assert.Contains("object-fit: contain", Regla(css, ".sala-marco > .sala-principal"));
        Assert.Contains("object-fit: cover", Regla(css, ".sala-propio-video"));
    }

    [Theory]
    [InlineData("Pantalla_Paciente/sesion_video.html")]
    [InlineData("Pantalla_Profesional/sesion_video.html")]
    public void Las_paginas_usan_la_escena_compartida_sin_reglas_propias_de_tamano(string pagina)
    {
        var html = Leer(pagina.Split('/'));
        Assert.Contains("../shared/sala.css", html);
        Assert.Matches(@"class=""sala-marco""\s+id=""marco-sala""", html);
        var estilo = string.Join("\n", Regex.Matches(html, @"<style>(.*?)</style>", RegexOptions.Singleline).Select(m => m.Groups[1].Value));
        foreach (var propia in new[] { "video-wrap", "video-principal", ".sala-marco {", ".sala-marco>" })
            Assert.DoesNotContain(propia, estilo.Replace(".sala-marco canvas", ""));
        Assert.DoesNotMatch(@"video\s*\{[^}]*object-fit", estilo);
    }

    [Fact]
    public void Ninguna_pagina_de_la_sala_tiene_otro_contenedor_de_video_sin_ancho_maximo()
    {
        // Un <video> suelto de una página de la sala debe estar dentro del marco de la escena (ancho acotado), no directo en el flujo de la página.
        foreach (var pagina in Paginas)
        {
            var html = Leer(pagina.Split('/'));
            var marco = Regex.Match(html, @"<div class=""sala-marco"" id=""marco-sala"">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
            var fuera = html.Replace(marco, "");
            foreach (Match v in Regex.Matches(fuera, @"<video\b[^>]*>"))
                Assert.True(v.Value.Contains("id=\"vid-propio\""), $"{pagina}: <video> fuera de la escena sin ancho acotado: {v.Value}");
        }
    }
}
