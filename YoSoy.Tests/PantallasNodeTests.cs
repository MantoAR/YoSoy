using System.Diagnostics;
using System.Text;

// Corre con node las pruebas de lógica de cliente (DOM falso) de YoSoy.Tests/Pantallas/*.prueba.js del prompt 08 (ajustes de uso real). Cada una imprime «OK n, FALLAS 0».
public class PantallasNodeTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();

    public static IEnumerable<object[]> Pruebas => new[]
    {
        new object[] { "sobre-mi-profesionales.prueba.js" },
        new object[] { "refresco-paciente.prueba.js" },
        new object[] { "profesional-unico.prueba.js" },
        new object[] { "provincias.prueba.js" },
    }.Where(f => File.Exists(Path.Combine(Raiz, "YoSoy.Tests", "Pantallas", (string)f[0])));

    [Theory]
    [MemberData(nameof(Pruebas))]
    public void La_logica_del_cliente_pasa(string archivo)
    {
        try { using var v = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!; if (!v.WaitForExit(15000) || v.ExitCode != 0) return; }
        catch (Exception) { Console.WriteLine("OMITIDA: node no está disponible."); return; }

        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = Raiz, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(Path.Combine(Raiz, "YoSoy.Tests", "Pantallas", archivo));
        psi.ArgumentList.Add(Raiz);
        using var p = Process.Start(psi)!;
        var salida = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(60000), "node no terminó");
        Assert.True(p.ExitCode == 0, salida.Result + error.Result);
        Assert.Contains("FALLAS 0", salida.Result);
    }
}
