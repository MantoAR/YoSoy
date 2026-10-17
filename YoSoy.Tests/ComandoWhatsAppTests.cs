using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// ComandoWhatsApp.EsBaja: reconoce el mismo pedido de BAJA que whatsapp-service (privacidad.js, interpretarComando). Si las dos listas o la
// forma de comparar se separan, la persona que escribe BAJA quedaría bloqueada en el servicio pero "activada" en la base. Por eso, además
// de probar la regla, una prueba corre el código REAL del servicio con muchos textos y compara.
public class ComandoWhatsAppTests
{
    [Theory]
    [InlineData("BAJA")]
    [InlineData("baja")]
    [InlineData("Baja")]
    [InlineData("  baja  ")]
    [InlineData("¡BAJA!")]
    [InlineData("Baja.")]
    [InlineData("baja\n")]
    [InlineData("STOP")]
    [InlineData("stop")]
    [InlineData("Parar")]
    [InlineData("basta")]
    [InlineData("no molestar")]
    [InlineData("NO MOLESTAR")]
    [InlineData("no   molestar")]
    [InlineData("desuscribir")]
    [InlineData("desuscribirme")]
    [InlineData("dar de baja")]
    [InlineData("Dar de baja!")]
    [InlineData("darme de baja")]
    [InlineData("dar-de-baja")]
    [InlineData("no quiero recibir mensajes")]
    [InlineData("No quiero más mensajes")]
    [InlineData("no quiero mas mensajes")]
    [InlineData("bája")]
    [InlineData("BAJA 😀")]
    public void Estos_textos_son_un_pedido_de_baja(string texto)
    {
        Assert.True(ComandoWhatsApp.EsBaja(texto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hola")]
    [InlineData("1")]
    [InlineData("menu")]
    [InlineData("cancelar")]                          // en el bot es cancelar un turno, no pedir la baja
    [InlineData("quiero cancelar mi turno")]
    [InlineData("baja por favor")]                    // tiene que ser el mensaje ENTERO
    [InlineData("el turno se dio de baja")]
    [InlineData("quiero dar de baja mi turno")]
    [InlineData("no quiero seguir")]
    [InlineData("alta")]                              // el ALTA no es una baja
    [InlineData("start")]
    [InlineData("bajaa")]
    [InlineData("b a j a")]
    [InlineData("ba ja")]
    [InlineData("stopp")]
    public void Estos_textos_no_son_un_pedido_de_baja(string? texto)
    {
        Assert.False(ComandoWhatsApp.EsBaja(texto));
    }

    [Fact]
    public void Las_palabras_de_baja_son_exactamente_las_del_servicio()
    {
        // Las palabras que están escritas en whatsapp-service/privacidad.js (PALABRAS_DE_BAJA): la misma lista, ni una más ni una menos.
        var js = File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "whatsapp-service", "privacidad.js"));
        var bloque = Regex.Match(js, @"const PALABRAS_DE_BAJA = new Set\(\[(.*?)\]\)", RegexOptions.Singleline);
        Assert.True(bloque.Success, "no encontré PALABRAS_DE_BAJA en privacidad.js");
        var delServicio = Regex.Matches(bloque.Groups[1].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.Equal(delServicio, ComandoWhatsApp.PalabrasDeBaja.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Cada_palabra_de_baja_ya_esta_normalizada()
    {
        foreach (var palabra in ComandoWhatsApp.PalabrasDeBaja)
            Assert.Equal(palabra, ComandoWhatsApp.Normalizar(palabra));
    }
}

public sealed class FactConNodeDelServicioDeWhatsAppAttribute : FactAttribute
{
    public FactConNodeDelServicioDeWhatsAppAttribute()
    {
        var motivo = NodeDelServicioDeWhatsApp.NoDisponible();
        if (motivo is not null) Skip = motivo;
    }
}

// El código REAL del servicio (privacidad.js no necesita npm install: solo usa módulos de node). Si no hay node, la prueba queda OMITIDA
// (no pasa en falso) y la lista de palabras sigue cubierta por Las_palabras_de_baja_son_exactamente_las_del_servicio.
public static class NodeDelServicioDeWhatsApp
{
    public static readonly string Carpeta = Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "whatsapp-service");
    private static readonly Lazy<string?> Motivo = new(CalcularMotivo);

    public static string? NoDisponible() => Motivo.Value;

    private static string? CalcularMotivo()
    {
        try
        {
            if (!File.Exists(Path.Combine(Carpeta, "privacidad.js"))) return "No está whatsapp-service/privacidad.js.";
            var (codigo, _, _) = Ejecutar(new[] { "--version" }, new Dictionary<string, string>(), 15000);
            return codigo == 0 ? null : "node respondió con error al pedirle la versión.";
        }
        catch (Exception e)
        {
            return $"node no está disponible en esta máquina ({e.GetType().Name}).";
        }
    }

    public static (int Codigo, string Salida, string Error) Ejecutar(string[] args, Dictionary<string, string> entorno, int milisegundos = 30000)
    {
        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = Carpeta,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (clave, valor) in entorno) psi.Environment[clave] = valor;

        using var proceso = Process.Start(psi)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var error = proceso.StandardError.ReadToEndAsync();
        if (!proceso.WaitForExit(milisegundos))
        {
            try { proceso.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("node no terminó a tiempo.");
        }
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, error.Result);
    }
}

public class ComandoWhatsAppInteropNodeTests
{
    // Para cada texto: ¿el servicio lo toma como una baja?
    private const string Script = """
        const { interpretarComando } = require('./privacidad');
        const entradas = JSON.parse(process.env.ENTRADAS);
        console.log(JSON.stringify(entradas.map(e => interpretarComando(e) === 'baja')));
        """;

    private static readonly string[] Entradas =
    {
        "BAJA", "baja", "Baja", "  baja  ", "¡BAJA!", "Baja.", "baja\n", "\tbaja\t", "STOP", "stop", "Stop!", "Parar", "basta", "BASTA",
        "no molestar", "NO MOLESTAR", "no   molestar", "No molestar.", "desuscribir", "desuscribirme", "dar de baja", "Dar de baja!",
        "darme de baja", "dar-de-baja", "dar_de_baja", "no quiero recibir mensajes", "No quiero más mensajes", "no quiero mas mensajes",
        "bája", "báJa", "BAJA 😀", "😀", "baja\u0301", "bajaß", "ＢＡＪＡ", "ba ja", "b a j a", "baja\u00a0", "\u00a0baja", "baja\u2003",
        "baja\u200b", "baja\u3000", "baja\r\n", "baja,", "(baja)", "«baja»", "¿baja?", "baja - stop", "baja stop", "bajá", "BAJÁ", "bajaa",
        "baja por favor", "el turno se dio de baja", "quiero dar de baja mi turno", "cancelar", "Cancelar", "cancelo", "quiero cancelar mi turno",
        "alta", "ALTA", "start", "reanudar", "quiero recibir mensajes", "hola", "menu", "1", "2", "0", "", " ", "   ", "stopp", "no", "si",
        "no quiero", "no quiero seguir", "me quiero dar de baja", "darme de baja del servicio", "n\u0336o molestar", "no molestar\u0000",
        "ñ", "año", "baja ñ", "ｓｔｏｐ", "İ", "ǅ"
    };

    [FactConNodeDelServicioDeWhatsApp]
    public void Coincide_con_el_servicio_en_todos_estos_textos()
    {
        var (codigo, salida, error) = NodeDelServicioDeWhatsApp.Ejecutar(new[] { "-e", Script },
            new Dictionary<string, string> { ["ENTRADAS"] = JsonSerializer.Serialize(Entradas) });
        Assert.True(codigo == 0, "node falló: " + error);

        var delServicio = JsonSerializer.Deserialize<bool[]>(salida.Trim())!;
        Assert.Equal(Entradas.Length, delServicio.Length);
        var diferencias = new List<string>();
        for (var i = 0; i < Entradas.Length; i++)
            if (ComandoWhatsApp.EsBaja(Entradas[i]) != delServicio[i])
                diferencias.Add($"{JsonSerializer.Serialize(Entradas[i])}: el servicio dice {delServicio[i]}, la API dice {!delServicio[i]}");

        Assert.True(diferencias.Count == 0, "La API y el servicio de WhatsApp no coinciden:\n" + string.Join("\n", diferencias));
        Assert.Contains(true, delServicio);    // y la batería tiene bajas de verdad (no coinciden dos "falsos" cualquiera)
    }
}
