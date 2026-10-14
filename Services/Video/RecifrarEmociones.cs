using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

// Herramienta de mantenimiento, de una sola vez y A MANO: cifra las emociones que se guardaron en claro antes del PR 6
// (SesionClinica.HistorialEmociones con JSON legible). No levanta el servidor; se corre con el mismo ejecutable de la API:
//
//   YoSoy --recifrar-emociones            SIMULACIÓN (por defecto): cuenta y dice qué haría; no modifica nada.
//   YoSoy --recifrar-emociones --aplicar  Cifra las filas en claro. Hacer el backup de la base ANTES.
//
// Usa la misma configuración que la API (appsettings.json + variables de entorno: ConnectionStrings__YoSoy y
// Video__Emociones__ClaveCifrado), así que hay que correrla con el entorno del servicio.
//
//  · NUNCA borra ni anula nada: solo reemplaza el texto en claro por su versión cifrada, fila por fila.
//  · Cada fila se cifra, se vuelve a descifrar y se compara con el original ANTES de escribir; y se actualiza solo si
//    la fila sigue teniendo el mismo texto (si cambió mientras tanto, se deja y se cuenta).
//  · Es repetible: lo ya cifrado se saltea. Lo que no es un JSON se deja como está y se cuenta.
//  · No imprime la clave, ni ningún contenido, ni ids: solo cantidades.

/// <summary>Lo que la herramienta le pide a la base. Es una interfaz para poder probar la lógica sin una base.</summary>
public interface IRecifradoRepositorio
{
    /// <summary>Hasta <paramref name="lote"/> sesiones con emociones guardadas y IdSesion mayor a <paramref name="despuesDeId"/>, por IdSesion.</summary>
    List<(int Id, string Texto)> Lote(int despuesDeId, int lote);

    /// <summary>Cambia el texto de UNA sesión solo si todavía es <paramref name="viejo"/>. true si lo cambió.</summary>
    bool Reemplazar(int id, string viejo, string nuevo);
}

public record ResultadoRecifrado(int Total, int YaCifradas, int EnClaro, int NoJson, int Recifradas, int CambiaronMientras, int NoVerificaron);

public static class RecifrarEmociones
{
    private const int LoteDeLectura = 200;

    public static int Ejecutar(string[] args)
    {
        if (args.Contains("--ayuda") || args.Contains("-h") || args.Contains("--help"))
        {
            Console.WriteLine(Ayuda);
            return 0;
        }

        var aplicar = args.Contains("--aplicar");
        var salida = Console.Out;
        salida.WriteLine(aplicar
            ? "Recifrado de emociones guardadas: APLICANDO (modifica la base)."
            : "Recifrado de emociones guardadas: SIMULACIÓN (no se modifica nada).");

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var opciones = new VideoOptions();
        config.GetSection(VideoOptions.Seccion).Bind(opciones);
        var cifrado = new EmocionesCifradoService(new OpcionesDeLaHerramienta(opciones), config);

        var problema = cifrado.ProblemaDeConfiguracion();
        salida.WriteLine($"  Clave de cifrado ............. {(problema is null ? "configurada" : "NO configurada: " + problema)}");
        if (aplicar && problema is not null)
        {
            Console.Error.WriteLine("No se puede aplicar sin una clave de cifrado válida. No se modificó nada.");
            return 1;
        }

        ResultadoRecifrado r;
        try
        {
            using var repo = new SqlRecifradoRepositorio();
            r = Procesar(aplicar, cifrado, repo);
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException or TypeInitializationException)
        {
            // Sin cadena de conexión (Conexion tira InvalidOperationException) o sin base: se dice, sin detalles.
            Console.Error.WriteLine($"No se pudo trabajar con la base ({e.GetType().Name}). Revisá ConnectionStrings__YoSoy. No se completó el recifrado.");
            return 1;
        }

        salida.WriteLine($"  Sesiones con emociones ....... {r.Total}");
        salida.WriteLine($"    ya cifradas ................ {r.YaCifradas}");
        salida.WriteLine($"    en claro (a recifrar) ...... {r.EnClaro}");
        salida.WriteLine($"    en claro pero no son JSON .. {r.NoJson}   (se dejan como están)");
        if (aplicar)
        {
            salida.WriteLine($"  Recifradas ................... {r.Recifradas}");
            if (r.CambiaronMientras > 0) salida.WriteLine($"  Cambiaron mientras tanto ..... {r.CambiaronMientras}   (se dejan: volver a correr)");
            if (r.NoVerificaron > 0) salida.WriteLine($"  No pasaron la verificación ... {r.NoVerificaron}   (se dejan como están)");
        }
        else if (r.EnClaro > 0)
        {
            salida.WriteLine("Para aplicar, DESPUÉS de tu backup de la base:  YoSoy --recifrar-emociones --aplicar");
        }
        else salida.WriteLine("No hay nada en claro para recifrar.");
        return 0;
    }

    /// <summary>
    /// La lógica, sin consola ni configuración. Con <paramref name="aplicar"/> en false solo cuenta. Nunca escribe nada que
    /// no haya vuelto a descifrarse igual, ni sobre una fila que cambió mientras tanto.
    /// </summary>
    public static ResultadoRecifrado Procesar(bool aplicar, IEmocionesCifrado cifrado, IRecifradoRepositorio repo, int lote = LoteDeLectura)
    {
        if (aplicar && cifrado.ProblemaDeConfiguracion() is not null)
            throw new InvalidOperationException("No se puede aplicar sin una clave de cifrado válida.");

        int total = 0, yaCifradas = 0, enClaro = 0, noJson = 0, recifradas = 0, cambiaronMientras = 0, noVerificaron = 0;

        // Se recorre por IdSesion en lotes: no se carga todo en memoria y una fila problemática no frena a las demás.
        var ultimo = 0;
        while (true)
        {
            var filas = repo.Lote(ultimo, Math.Max(1, lote));
            if (filas.Count == 0) break;

            foreach (var (id, texto) in filas)
            {
                ultimo = id;
                total++;
                if (EmocionesCifradoService.EstaCifrado(texto)) { yaCifradas++; continue; }
                if (!EsObjetoJson(texto)) { noJson++; continue; }
                enClaro++;
                if (!aplicar) continue;

                var aad = EmocionesCifradoService.DatosAsociadosDeSesion(id);
                var nuevo = cifrado.Cifrar(texto, aad);
                // Prueba de ida y vuelta antes de tocar la fila.
                if (cifrado.Descifrar(nuevo, aad, out var devuelto) != ResultadoDescifrado.Ok || devuelto != texto)
                {
                    noVerificaron++;
                    continue;
                }

                if (repo.Reemplazar(id, texto, nuevo)) recifradas++; else cambiaronMientras++;
            }
        }

        return new ResultadoRecifrado(total, yaCifradas, enClaro, noJson, recifradas, cambiaronMientras, noVerificaron);
    }

    private static bool EsObjetoJson(string texto)
    {
        try
        {
            using var doc = JsonDocument.Parse(texto);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    private const string Ayuda = @"YoSoy --recifrar-emociones [--aplicar]

Cifra las emociones que se guardaron en claro antes del PR 6 (SesionClinica.HistorialEmociones).
  (sin opciones)  SIMULACIÓN: cuenta qué haría y no modifica nada.
  --aplicar       Cifra de verdad. Hacé el backup de la base antes.

Usa ConnectionStrings__YoSoy y Video__Emociones__ClaveCifrado (y el appsettings.json de al lado del ejecutable).
Nunca borra ni anula nada, y no imprime la clave ni ningún contenido: solo cantidades.";

    private sealed class OpcionesDeLaHerramienta : IOptionsMonitor<VideoOptions>
    {
        public OpcionesDeLaHerramienta(VideoOptions valor) => CurrentValue = valor;
        public VideoOptions CurrentValue { get; }
        public VideoOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<VideoOptions, string?> listener) => null;
    }
}

/// <summary>El acceso real a la base del recifrado: una conexión para toda la pasada.</summary>
public sealed class SqlRecifradoRepositorio : IRecifradoRepositorio, IDisposable
{
    private SqlConnection? _con;

    private SqlConnection Abierta
    {
        get
        {
            if (_con is null)
            {
                _con = global::Conexion.ObtenerConexion();
                _con.Open();
            }
            return _con;
        }
    }

    public List<(int Id, string Texto)> Lote(int despuesDeId, int lote)
    {
        var filas = new List<(int Id, string Texto)>();
        using var cmd = new SqlCommand(
            "SELECT TOP (@Lote) IdSesion, HistorialEmociones FROM SesionClinica " +
            "WHERE HistorialEmociones IS NOT NULL AND IdSesion > @Ultimo ORDER BY IdSesion", Abierta);
        cmd.Parameters.AddWithValue("@Lote", lote);
        cmd.Parameters.AddWithValue("@Ultimo", despuesDeId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) filas.Add(((int)r["IdSesion"], (string)r["HistorialEmociones"]));
        return filas;
    }

    public bool Reemplazar(int id, string viejo, string nuevo)
    {
        using var upd = new SqlCommand(
            "UPDATE SesionClinica SET HistorialEmociones = @Nuevo WHERE IdSesion = @Id AND HistorialEmociones = @Viejo", Abierta);
        upd.Parameters.AddWithValue("@Nuevo", nuevo);
        upd.Parameters.AddWithValue("@Id", id);
        upd.Parameters.AddWithValue("@Viejo", viejo);
        return upd.ExecuteNonQuery() == 1;
    }

    public void Dispose() => _con?.Dispose();
}
