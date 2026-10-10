using Microsoft.Data.SqlClient;

// La cadena de conexión viene de la variable de entorno ConnectionStrings__YoSoy (la
// convención de ASP.NET Core para la clave de configuración ConnectionStrings:YoSoy, doble
// guion bajo en vez de los dos puntos). Si falta, falla acá con un mensaje claro en vez de
// caer a algún valor fijo: nunca hubo una cadena "de producción" razonable para poner como
// default.
public class Conexion
{
    private static readonly string cadena = Environment.GetEnvironmentVariable("ConnectionStrings__YoSoy")
        ?? throw new InvalidOperationException(
            "Falta la variable de entorno ConnectionStrings__YoSoy con la cadena de conexión a SQL Server.");

    public static SqlConnection ObtenerConexion()
    {
        return new SqlConnection(cadena);
    }
}