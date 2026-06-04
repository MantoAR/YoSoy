using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class FestivoDAO
{
    public List<Festivo> ObtenerTodos()
    {
        var lista = new List<Festivo>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdFestivo, Fecha, Descripcion, Tipo, Estado FROM Festivo WHERE Estado = 'Activo' ORDER BY Fecha", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public void Insertar(Festivo f)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Festivo (Fecha, Descripcion, Tipo, Estado) VALUES (@Fecha, @Descripcion, @Tipo, 'Activo')", con);
        cmd.Parameters.AddWithValue("@Fecha",       f.Fecha);
        cmd.Parameters.AddWithValue("@Descripcion", f.Descripcion);
        cmd.Parameters.AddWithValue("@Tipo",        f.Tipo ?? "Nacional");
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE Festivo SET Estado = 'Inactivo' WHERE IdFestivo = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    private Festivo Mapear(SqlDataReader r) => new Festivo
    {
        IdFestivo   = (int)r["IdFestivo"],
        Fecha       = (DateTime)r["Fecha"],
        Descripcion = (string)r["Descripcion"],
        Tipo        = (string)r["Tipo"],
        Estado      = (string)r["Estado"]
    };
}