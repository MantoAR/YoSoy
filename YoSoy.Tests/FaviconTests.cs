using System.Text.RegularExpressions;

// El favicon: todas las páginas de PANTALLAS/ lo enlazan, los archivos existen y el servidor los entrega con el tipo de contenido correcto (incluido /favicon.ico en la raíz).
public class FaviconTests : IDisposable
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Pantallas => Path.Combine(Raiz, "PANTALLAS");
    private readonly VideoApp _app = new();
    public void Dispose() => _app.Dispose();

    private static IEnumerable<string> Paginas() => Directory.EnumerateFiles(Pantallas, "*.html", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"));

    // Resuelve un href como lo hace el navegador: absoluto desde la raíz del sitio (/pantallas/...) o relativo a la carpeta de la página.
    private static string Archivo(string pagina, string href)
    {
        var limpio = href.Split('?', '#')[0];
        return limpio.StartsWith("/pantallas/", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(Path.Combine(Pantallas, limpio["/pantallas/".Length..]))
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pagina)!, limpio));
    }

    [Fact]
    public void Hay_paginas_que_revisar() => Assert.True(Paginas().Count() >= 20);

    [Fact]
    public void Toda_pagina_enlaza_el_favicon_y_los_archivos_existen()
    {
        var problemas = new List<string>();
        foreach (var pagina in Paginas())
        {
            var html = File.ReadAllText(pagina);
            var head = Regex.Match(html, @"<head\b.*?</head>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Value;
            var rel = Path.GetRelativePath(Raiz, pagina);
            var enlaces = Regex.Matches(head, @"<link\b[^>]*>", RegexOptions.IgnoreCase).Select(m => m.Value).ToList();
            var iconos = enlaces.Where(l => Regex.IsMatch(l, @"rel=""(shortcut )?icon""", RegexOptions.IgnoreCase)).ToList();

            if (iconos.Count != 3) problemas.Add($"{rel}: se esperaban 3 <link rel=\"icon\"> y hay {iconos.Count}");
            foreach (var (patron, nombre) in new[] { (@"type=""image/x-icon""", "favicon.ico"), (@"sizes=""32x32""", "favicon-32.png"), (@"sizes=""16x16""", "favicon-16.png") })
                if (!iconos.Any(l => Regex.IsMatch(l, patron) && l.Contains(nombre))) problemas.Add($"{rel}: falta el icono {nombre}");
            if (!enlaces.Any(l => l.Contains("rel=\"apple-touch-icon\"") && l.Contains("favicon-180.png"))) problemas.Add($"{rel}: falta apple-touch-icon");

            foreach (var l in enlaces.Where(l => l.Contains("favicon")))
            {
                var href = Regex.Match(l, @"href=""([^""]+)""").Groups[1].Value;
                var destino = Archivo(pagina, href);
                if (!File.Exists(destino)) problemas.Add($"{rel}: {href} no existe ({destino})");
                else if (!destino.StartsWith(Path.Combine(Pantallas, "shared", "favicon") + Path.DirectorySeparatorChar)) problemas.Add($"{rel}: {href} no apunta a shared/favicon");
            }
        }
        Assert.True(problemas.Count == 0, string.Join("\n", problemas));
    }

    [Theory]
    [InlineData("favicon.ico", "image/x-icon")]
    [InlineData("favicon-16.png", "image/png")]
    [InlineData("favicon-32.png", "image/png")]
    [InlineData("favicon-48.png", "image/png")]
    [InlineData("favicon-180.png", "image/png")]
    [InlineData("favicon-192.png", "image/png")]
    [InlineData("favicon-512.png", "image/png")]
    public async Task El_servidor_entrega_cada_archivo_con_su_tipo(string nombre, string tipo)
    {
        var r = await _app.Factory.CreateClient().GetAsync("/pantallas/shared/favicon/" + nombre);
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(tipo, r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Pantallas, "shared", "favicon", nombre)), await r.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Favicon_ico_en_la_raiz_lo_entrega_sin_sesion_y_con_cache()
    {
        var r = await _app.Factory.CreateClient().GetAsync("/favicon.ico");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/x-icon", r.Content.Headers.ContentType?.MediaType);
        Assert.Contains("max-age=", r.Headers.CacheControl?.ToString());
        Assert.Equal(File.ReadAllBytes(Path.Combine(Pantallas, "shared", "favicon", "favicon.ico")), await r.Content.ReadAsByteArrayAsync());
    }
}
