using Microsoft.Data.SqlClient;

public class Conexion
{
    // Cambiá "MANTO" por el nombre de tu servidor SQL Server local
    private static string cadena = "Server=MANTO\\SQLEXPRESS;Database=yosoy;Trusted_Connection=True;TrustServerCertificate=True;";

    public static SqlConnection ObtenerConexion()
    {
        return new SqlConnection(cadena);
    }
}