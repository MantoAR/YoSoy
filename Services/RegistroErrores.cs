using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Registro de errores con código (ISO 27002 8.15 registro, 8.16 monitoreo).
//
// Todo error de la API (una excepción o una respuesta 5xx) recibe un código corto, por
// ejemplo "7F3A-91C2". El usuario lo ve en pantalla y en el encabezado X-Codigo-Error; el
// detalle técnico queda en:
//   1. la tabla LogError (migración 18):  SELECT * FROM LogError WHERE Codigo = '7F3A-91C2';
//   2. un archivo diario logs/errores-AAAAMMDD.log (una línea JSON por error), por si la
//      base no responde;
//   3. la consola de "dotnet run".
// Nunca se guarda el cuerpo del pedido. Los mensajes y trazas se enmascaran antes de
// guardarlos (ISO 27002 8.11): un error de SQL puede citar un DNI o un correo.

public record ErrorRegistrado(
    string Codigo, DateTime FechaHora, string Metodo, string Ruta, int StatusCode,
    int? IdUsuario, string? Rol, string? TipoError, string? Mensaje, string? Detalle, string VersionApi);

public static class CodigoError
{
    // 8 caracteres hexadecimales al azar (4.294.967.296 combinaciones), en dos grupos para
    // poder dictarlo por teléfono: "7F3A-91C2".
    public static string Nuevo()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var hex = Convert.ToHexString(bytes);
        return hex[..4] + "-" + hex[4..];
    }
}

public static class Enmascarar
{
    private static readonly Regex Correo = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex DniConPuntos = new(@"\b\d{1,2}\.\d{3}\.\d{3}\b", RegexOptions.Compiled);
    private static readonly Regex CuilConGuiones = new(@"\b\d{2}-\d{7,8}-\d\b", RegexOptions.Compiled);
    private static readonly Regex NumeroLargo = new(@"\d{6,}", RegexOptions.Compiled);

    // DNI, CUIL, teléfonos y correos. Los números cortos (ids, números de línea) quedan.
    public static string? Texto(string? texto, int largoMaximo)
    {
        if (string.IsNullOrEmpty(texto)) return texto;
        var t = Correo.Replace(texto, "[correo]");
        t = CuilConGuiones.Replace(t, "[número]");
        t = DniConPuntos.Replace(t, "[número]");
        t = NumeroLargo.Replace(t, "[número]");
        return t.Length <= largoMaximo ? t : t[..largoMaximo] + "…";
    }
}

public interface IDestinoErrores
{
    void Guardar(ErrorRegistrado e);
}

public class DestinoErroresSql : IDestinoErrores
{
    public void Guardar(ErrorRegistrado e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            INSERT INTO LogError (Codigo, FechaHora, Metodo, Ruta, StatusCode, IdUsuario, Rol, TipoError, Mensaje, Detalle, VersionApi)
            VALUES (@Codigo, @FechaHora, @Metodo, @Ruta, @StatusCode, @IdUsuario, @Rol, @TipoError, @Mensaje, @Detalle, @VersionApi)", con);
        cmd.Parameters.AddWithValue("@Codigo", e.Codigo);
        cmd.Parameters.AddWithValue("@FechaHora", e.FechaHora);
        cmd.Parameters.AddWithValue("@Metodo", e.Metodo);
        cmd.Parameters.AddWithValue("@Ruta", e.Ruta);
        cmd.Parameters.AddWithValue("@StatusCode", e.StatusCode);
        cmd.Parameters.AddWithValue("@IdUsuario", (object?)e.IdUsuario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Rol", (object?)e.Rol ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TipoError", (object?)e.TipoError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Mensaje", (object?)e.Mensaje ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Detalle", (object?)e.Detalle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@VersionApi", e.VersionApi);
        cmd.ExecuteNonQuery();
    }
}

public class DestinoErroresArchivo : IDestinoErrores
{
    private static readonly object Candado = new();
    private readonly string _carpeta;

    public DestinoErroresArchivo(string carpeta) => _carpeta = carpeta;

    public void Guardar(ErrorRegistrado e)
    {
        var linea = JsonSerializer.Serialize(e);
        lock (Candado)
        {
            Directory.CreateDirectory(_carpeta);
            File.AppendAllText(Path.Combine(_carpeta, $"errores-{e.FechaHora:yyyyMMdd}.log"), linea + Environment.NewLine);
        }
    }
}

public class RegistroErrores
{
    public static readonly string VersionApi =
        typeof(RegistroErrores).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    private readonly IDestinoErrores _archivo;
    private readonly IDestinoErrores _base;
    private readonly ILogger<RegistroErrores> _log;

    public RegistroErrores(IDestinoErrores archivo, IDestinoErrores baseDeDatos, ILogger<RegistroErrores> log)
    {
        _archivo = archivo;
        _base = baseDeDatos;
        _log = log;
    }

    public static ErrorRegistrado Armar(HttpContext ctx, string codigo, int status, Exception? ex)
    {
        int? idUsuario = int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        return new ErrorRegistrado(
            Codigo: codigo,
            FechaHora: DateTime.Now,
            Metodo: ctx.Request.Method,
            Ruta: Enmascarar.Texto(ctx.Request.Path.Value, 300) ?? "",
            StatusCode: status,
            IdUsuario: idUsuario,
            Rol: ctx.User.FindFirstValue(ClaimTypes.Role),
            TipoError: ex?.GetType().FullName,
            Mensaje: Enmascarar.Texto(ex?.Message ?? $"Respuesta {status} sin excepción", 1000),
            Detalle: Enmascarar.Texto(ex?.ToString(), 20000),
            VersionApi: VersionApi);
    }

    // Nunca lanza: registrar un error no puede provocar otro. El archivo se escribe en el
    // momento (es local y rápido); la base, en segundo plano, para no demorar la respuesta
    // si es justamente la base la que no responde.
    public void Registrar(ErrorRegistrado e)
    {
        _log.LogError("Error {Codigo} en {Metodo} {Ruta} ({Status}): {Tipo} {Mensaje}",
            e.Codigo, e.Metodo, e.Ruta, e.StatusCode, e.TipoError, e.Mensaje);
        try { _archivo.Guardar(e); }
        catch (Exception ex) { _log.LogWarning(ex, "No se pudo escribir el error {Codigo} en el archivo.", e.Codigo); }
        _ = Task.Run(() =>
        {
            try { _base.Guardar(e); }
            catch (Exception ex) { _log.LogWarning("No se pudo guardar el error {Codigo} en LogError: {Motivo}", e.Codigo, ex.Message); }
        });
    }
}

public static class RegistroErroresExtensions
{
    public const string Encabezado = "X-Codigo-Error";

    // carpetaConfigurada (RegistroErrores:Carpeta): dónde va el archivo diario. Sin ella, logs/ junto a la aplicación; ojo que
    // al reemplazar la carpeta de la aplicación en un despliegue esa carpeta se va con la versión anterior.
    public static IServiceCollection AddRegistroErrores(this IServiceCollection services, string carpetaRaiz, string? carpetaConfigurada = null) =>
        services.AddSingleton(sp => new RegistroErrores(
            new DestinoErroresArchivo(string.IsNullOrWhiteSpace(carpetaConfigurada) ? Path.Combine(carpetaRaiz, "logs") : carpetaConfigurada),
            new DestinoErroresSql(),
            sp.GetRequiredService<ILogger<RegistroErrores>>()));

    // Va PRIMERO en el pipeline, para envolver a todo lo demás.
    public static WebApplication UseRegistroErrores(this WebApplication app)
    {
        var registro = app.Services.GetRequiredService<RegistroErrores>();
        var esDesarrollo = app.Environment.IsDevelopment();

        app.Use(async (ctx, next) =>
        {
            string? codigo = null;
            ctx.Response.OnStarting(() =>
            {
                // 503 = base desactualizada (VerificadorEsquema): ya trae su propio mensaje.
                if (ctx.Response.StatusCode >= 500 && ctx.Response.StatusCode != StatusCodes.Status503ServiceUnavailable)
                {
                    codigo ??= CodigoError.Nuevo();
                    ctx.Response.Headers[Encabezado] = codigo;
                }
                return Task.CompletedTask;
            });

            try
            {
                await next();
            }
            catch (Exception) when (ctx.RequestAborted.IsCancellationRequested)
            {
                // El usuario cerró la pestaña o cortó el pedido: no es un error del sistema.
                return;
            }
            catch (BadHttpRequestException mal) when (!ctx.Response.HasStarted)
            {
                // Pedido mal armado (JSON inválido, parámetro de otro tipo): es un 400 del
                // cliente, no un error del sistema. Se responde igual que antes, sin código.
                LimpiarConservandoCors(ctx);
                ctx.Response.StatusCode = mal.StatusCode;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    mensaje = "El pedido no tiene el formato esperado.",
                    detalle = esDesarrollo ? mal.Message : null
                });
                return;
            }
            catch (Exception ex)
            {
                codigo ??= CodigoError.Nuevo();
                registro.Registrar(RegistroErrores.Armar(ctx, codigo, 500, ex));
                if (ctx.Response.HasStarted) throw;   // ya se mandó parte de la respuesta: no se puede reemplazar

                LimpiarConservandoCors(ctx);
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    codigo,
                    mensaje = $"Ocurrió un error en el sistema. Código: {codigo}. Si se repite, pasale este código a soporte.",
                    // Solo en Desarrollo, para no tener que ir a la base mientras se programa.
                    detalle = esDesarrollo ? Enmascarar.Texto($"{ex.GetType().Name}: {ex.Message}", 500) : null
                });
                return;
            }

            // Respuestas 5xx que no vinieron de una excepción (por ejemplo, Results.Problem).
            if (ctx.Response.StatusCode >= 500 && ctx.Response.StatusCode != StatusCodes.Status503ServiceUnavailable)
            {
                codigo ??= CodigoError.Nuevo();
                if (!ctx.Response.HasStarted) ctx.Response.Headers[Encabezado] = codigo;
                registro.Registrar(RegistroErrores.Armar(ctx, codigo, ctx.Response.StatusCode, null));
            }
        });
        return app;
    }

    // Response.Clear() borra también los encabezados CORS que ya puso UseCors; sin ellos, una
    // pantalla abierta desde otro origen no podría leer el mensaje con el código.
    private static void LimpiarConservandoCors(HttpContext ctx)
    {
        var cors = ctx.Response.Headers
            .Where(h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        ctx.Response.Clear();
        foreach (var h in cors) ctx.Response.Headers[h.Key] = h.Value;
    }
}
