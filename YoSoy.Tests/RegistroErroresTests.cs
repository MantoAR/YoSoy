using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;

// Registro de errores con código (etapa 1 de roles y permisos).
public class RegistroErroresTests
{
    [Fact]
    public void El_codigo_tiene_dos_grupos_de_cuatro_para_dictarlo()
    {
        var codigos = Enumerable.Range(0, 1000).Select(_ => CodigoError.Nuevo()).ToList();
        Assert.All(codigos, c => Assert.Matches(new Regex("^[0-9A-F]{4}-[0-9A-F]{4}$"), c));
        Assert.True(codigos.Distinct().Count() > 990);
    }

    [Theory]
    [InlineData("The duplicate key value is (30123456).", "30123456")]
    [InlineData("CUIL 20-30123456-7 inválido", "20-30123456-7")]
    [InlineData("DNI 30.123.456 repetido", "30.123.456")]
    [InlineData("No se pudo enviar a maria.lopez@gmail.com", "maria.lopez@gmail.com")]
    [InlineData("Teléfono 3515551234", "3515551234")]
    public void Enmascara_datos_personales(string texto, string dato)
    {
        var limpio = Enmascarar.Texto(texto, 1000)!;
        Assert.DoesNotContain(dato, limpio);
    }

    [Fact]
    public void Deja_los_numeros_cortos_que_sirven_para_diagnosticar()
    {
        var limpio = Enmascarar.Texto("at Program.<Main>$ in D:\\YoSoy\\Program.cs:line 1234 (IdTurno 57)", 1000)!;
        Assert.Contains("line 1234", limpio);
        Assert.Contains("IdTurno 57", limpio);
    }

    [Fact]
    public void Recorta_textos_largos()
    {
        var limpio = Enmascarar.Texto(new string('x', 50), 10)!;
        Assert.Equal(11, limpio.Length);   // 10 + "…"
    }

    private class DestinoQueFalla : IDestinoErrores
    {
        public void Guardar(ErrorRegistrado e) => throw new IOException("disco lleno");
    }

    private class DestinoEnMemoria : IDestinoErrores
    {
        public List<ErrorRegistrado> Guardados { get; } = new();
        public void Guardar(ErrorRegistrado e) { lock (Guardados) Guardados.Add(e); }
    }

    private static ErrorRegistrado Ejemplo(string codigo = "7F3A-91C2") =>
        new(codigo, new DateTime(2026, 10, 15, 19, 30, 0), "POST", "/pacientes/2/consentimiento-telemedicina", 500,
            7, "paciente", "System.InvalidOperationException", "No existe el tipo", "traza", "dev");

    [Fact]
    public void Registrar_nunca_lanza_aunque_fallen_los_destinos()
    {
        var registro = new RegistroErrores(new DestinoQueFalla(), new DestinoQueFalla(), NullLogger<RegistroErrores>.Instance);
        var ex = Record.Exception(() => registro.Registrar(Ejemplo()));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Registrar_guarda_en_archivo_y_en_base()
    {
        var archivo = new DestinoEnMemoria();
        var baseDatos = new DestinoEnMemoria();
        new RegistroErrores(archivo, baseDatos, NullLogger<RegistroErrores>.Instance).Registrar(Ejemplo());

        Assert.Single(archivo.Guardados);                          // el archivo, en el momento
        for (var i = 0; i < 50 && baseDatos.Guardados.Count == 0; i++) await Task.Delay(20);
        Assert.Single(baseDatos.Guardados);                        // la base, en segundo plano
    }

    [Fact]
    public void El_archivo_tiene_una_linea_JSON_por_error_y_se_busca_por_codigo()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "yosoy-logs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var destino = new DestinoErroresArchivo(carpeta);
            destino.Guardar(Ejemplo("AAAA-0001"));
            destino.Guardar(Ejemplo("BBBB-0002"));

            var lineas = File.ReadAllLines(Path.Combine(carpeta, "errores-20261015.log"));
            Assert.Equal(2, lineas.Length);
            var segundo = JsonSerializer.Deserialize<ErrorRegistrado>(lineas[1])!;
            Assert.Equal("BBBB-0002", segundo.Codigo);
            Assert.Equal("/pacientes/2/consentimiento-telemedicina", segundo.Ruta);
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true);
        }
    }
}
