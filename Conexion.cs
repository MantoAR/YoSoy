using Microsoft.Data.SqlClient;

public class Conexion
{
    private static string cadena = "Server=.\\SQLEXPRESS;Database=yosoy;Trusted_Connection=True;TrustServerCertificate=True;";

    public static void Inicializar(string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            cadena = connectionString;
    }

    public static SqlConnection ObtenerConexion()
    {
        return new SqlConnection(cadena);
    }
}
