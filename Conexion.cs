using Microsoft.Data.SqlClient;

public class Conexion
{
 
    private static string cadena = "Server=DESKTOP-Q065FTM\\SQLEXPRESS;Database=yosoy;Trusted_Connection=True;TrustServerCertificate=True;";

    // Único lugar donde se abre una conexión: los DAOs llaman a ObtenerConexion(). Cuando haya
    // una base por cliente, este es el método que elige la base de la organización del pedido.
    //
    // Si la configuración trae "ConnectionStrings:YoSoy" (appsettings, user-secrets o la
    // variable de entorno ConnectionStrings__YoSoy en el servidor), se usa esa; si no, sigue
    // la cadena de arriba, como hasta ahora. Program.cs llama a Configurar al arrancar.
    public static void Configurar(string? cadenaConfigurada)
    {
        if (!string.IsNullOrWhiteSpace(cadenaConfigurada)) cadena = cadenaConfigurada;
    }

    public static SqlConnection ObtenerConexion()
    {
        return new SqlConnection(cadena);
    }
}