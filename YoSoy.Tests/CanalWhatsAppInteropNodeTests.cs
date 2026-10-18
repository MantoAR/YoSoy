using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

// Interoperabilidad con el servicio de WhatsApp REAL: lo que manda CanalBaileys (la API en .NET) lo recibe el código de verdad de
// whatsapp-service/servidor.js (crearApp: el mismo Express, la misma validación del secreto y del cuerpo), con una cola de mentira que
// anota lo que le llega. Prueba, de punta a punta de la frontera entre los dos programas, que "optIn": true llega como optIn true y que sin
// él llega como false, y que el código con el que el servicio rechaza vuelve hasta el resultado del envío. El servicio no conecta nada con
// WhatsApp: no hay librería ni red de por medio (solo el loopback de la prueba).
//
// Si no hay node, o falta express (npm ci en whatsapp-service/), las pruebas quedan OMITIDAS (no pasan en falso).
public sealed class FactConExpressDelServicioAttribute : FactAttribute
{
    public FactConExpressDelServicioAttribute()
    {
        var motivo = NodeDelServicioDeWhatsApp.NoDisponible()
            ?? (Directory.Exists(Path.Combine(NodeDelServicioDeWhatsApp.Carpeta, "node_modules", "express"))
                ? null : "Faltan las dependencias del servicio de WhatsApp: correr npm ci en whatsapp-service/.");
        if (motivo is not null) Skip = motivo;
    }
}

public class CanalWhatsAppInteropNodeTests
{
    private const string Secreto = "secreto-del-interop-0123456789";

    // El servidor real (crearApp) en el puerto que le dicen, con una cola que anota y, cuando llegaron los pedidos esperados, imprime lo que
    // anotó y termina (después de que la salida se escribió: en Windows, process.exit() justo después de console.log puede perderla).
    private const string Servidor = """
        process.stderr.write('arranco\n');
        const { crearApp } = require('./servidor');
        process.stderr.write('cargado\n');
        const esperados = Number(process.env.PEDIDOS);
        const anotados = [];
        const rechazo = process.env.RECHAZO ? JSON.parse(process.env.RECHAZO) : null;
        const cola = {
            encolar(telefono, texto, meta) {
                anotados.push({ telefono, texto, optIn: meta.optIn });
                return rechazo ?? { ok: true, idExterno: 'cola-' + anotados.length };
            }
        };
        const servicio = { estado: async () => ({}), desvincular: async () => {} };
        const app = crearApp({ servicio, cola, secreto: process.env.SECRETO, log: { info() {}, error() {} } });
        const servidor = app.listen(Number(process.env.PUERTO), '127.0.0.1', () => process.stderr.write('escuchando en ' + servidor.address().port + '\n'));
        servidor.on('error', e => process.stderr.write('error al escuchar: ' + e.message + '\n'));
        const revisar = setInterval(() => {
            if (anotados.length >= esperados) {
                clearInterval(revisar);
                process.stdout.write(JSON.stringify({ anotados }) + '\n', () => process.exit(0));
            }
        }, 50);
        setTimeout(() => process.stdout.write(JSON.stringify({ anotados, vencio: true }) + '\n', () => process.exit(2)), 25000);
        """;

    private static int PuertoLibre()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    // El proceso de node y lo que escribió por el error estándar (si algo falla, dice hasta dónde llegó). El error estándar SE LEE
    // siempre: con la salida redirigida y sin leerla, en Windows el arranque de node con -e no avanzaba de forma confiable.
    private sealed record ServidorDeNode(Process Proceso, StringBuilder Diagnostico) : IDisposable
    {
        public void Dispose() => Proceso.Dispose();
    }

    private static ServidorDeNode Arrancar(int puerto, int pedidos, string? rechazo = null)
    {
        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = NodeDelServicioDeWhatsApp.Carpeta,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(Servidor);
        psi.Environment["SECRETO"] = Secreto;
        psi.Environment["PUERTO"] = puerto.ToString();
        psi.Environment["PEDIDOS"] = pedidos.ToString();
        if (rechazo is not null) psi.Environment["RECHAZO"] = rechazo;
        var proceso = Process.Start(psi)!;
        var diagnostico = new StringBuilder();
        proceso.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (diagnostico) diagnostico.AppendLine(e.Data); };
        proceso.BeginErrorReadLine();
        return new ServidorDeNode(proceso, diagnostico);
    }

    // Espera a que el servidor acepte conexiones (arrancar node y cargar Express tarda un rato).
    private static async Task EsperarQueEscuche(int puerto, ServidorDeNode servidor)
    {
        var limite = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < limite)
        {
            Assert.False(servidor.Proceso.HasExited, "node terminó antes de escuchar: " + servidor.Diagnostico);
            try
            {
                using var cliente = new TcpClient();
                await cliente.ConnectAsync(IPAddress.Loopback, puerto);
                return;
            }
            catch (SocketException) { await Task.Delay(100); }
        }
        throw new TimeoutException("el servicio no empezó a escuchar a tiempo. Lo que node escribió: " + servidor.Diagnostico);
    }

    private static void Matar(ServidorDeNode servidor)
    {
        if (!servidor.Proceso.HasExited) { try { servidor.Proceso.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }

    private static CanalBaileys Canal(int puerto)
    {
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{puerto}/"), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Add("X-Shared-Secret", Secreto);
        return new CanalBaileys(http, NullLogger<CanalBaileys>.Instance);
    }

    [FactConExpressDelServicio]
    public async Task El_servicio_real_recibe_optIn_true_solo_cuando_la_API_lo_manda()
    {
        var puerto = PuertoLibre();
        using var servidor = Arrancar(puerto, pedidos: 4);
        try
        {
            await EsperarQueEscuche(puerto, servidor);
            var canal = Canal(puerto);

            var conOptIn = await canal.EnviarAsync("1155550001", "con aceptación", new OpcionesEnvio(OptIn: true));
            var sinOpciones = await canal.EnviarAsync("1155550002", "sin opciones");
            var ninguna = await canal.EnviarAsync("1155550003", "ninguna", OpcionesEnvio.Ninguna);
            var optInFalso = await canal.EnviarAsync("1155550004", "en false", new OpcionesEnvio(OptIn: false));

            Assert.All(new[] { conOptIn, sinOpciones, ninguna, optInFalso }, r => Assert.True(r.Exito, r.Error));
            Assert.True(servidor.Proceso.WaitForExit(15000), "node no terminó");
            var salida = (await servidor.Proceso.StandardOutput.ReadToEndAsync()).Trim();
            using var doc = JsonDocument.Parse(salida);
            Assert.False(doc.RootElement.TryGetProperty("vencio", out _), "el servicio no recibió los pedidos a tiempo");
            var anotados = doc.RootElement.GetProperty("anotados").EnumerateArray()
                .Select(a => (a.GetProperty("telefono").GetString()!, a.GetProperty("texto").GetString()!, a.GetProperty("optIn").GetBoolean()));
            Assert.Equal(new[]
            {
                ("1155550001", "con aceptación", true), ("1155550002", "sin opciones", false),
                ("1155550003", "ninguna", false), ("1155550004", "en false", false)
            }, anotados);
        }
        finally { Matar(servidor); }
    }

    [FactConExpressDelServicio]
    public async Task El_servicio_real_rechaza_con_codigo_y_la_API_lo_recibe_en_el_resultado()
    {
        // La cola rechaza como lo hace la real (403 sin_consentimiento): el código llega hasta ResultadoEnvio.Codigo.
        var puerto = PuertoLibre();
        using var servidor = Arrancar(puerto, pedidos: 1,
            rechazo: """{"ok":false,"estado":403,"codigo":"sin_consentimiento","error":"No hay constancia."}""");
        try
        {
            await EsperarQueEscuche(puerto, servidor);

            var r = await Canal(puerto).EnviarAsync("1155550001", "hola");

            Assert.False(r.Exito);
            Assert.Equal("sin_consentimiento", r.Codigo);
            Assert.Equal("No hay constancia.", r.Error);
        }
        finally { Matar(servidor); }
    }

    [FactConExpressDelServicio]
    public async Task El_servicio_real_no_acepta_el_pedido_sin_el_secreto()
    {
        var puerto = PuertoLibre();
        using var servidor = Arrancar(puerto, pedidos: 1);
        try
        {
            await EsperarQueEscuche(puerto, servidor);
            var sinSecreto = new CanalBaileys(new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{puerto}/") }, NullLogger<CanalBaileys>.Instance);

            var r = await sinSecreto.EnviarAsync("1155550001", "hola", new OpcionesEnvio(OptIn: true));

            Assert.False(r.Exito);   // 401: el secreto compartido sigue siendo la primera puerta, con o sin optIn
        }
        finally { Matar(servidor); }
    }
}
