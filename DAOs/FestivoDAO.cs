using Microsoft.Data.SqlClient;
using System.Collections.Generic;
public class FestivoDAO
{
    public List<Festivo> ObtenerTodos()
    {
        var lista = new List<Festivo>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdFestivo,Fecha,Descripcion,Tipo,Estado FROM Festivo WHERE Estado='Activo' ORDER BY Fecha", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) lista.Add(Mapear(reader));
        return lista;
    }
    public void Insertar(Festivo f)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("INSERT INTO Festivo (Fecha,Descripcion,Tipo,Estado) VALUES (@F,@D,@T,'Activo')", con);
        cmd.Parameters.AddWithValue("@F", f.Fecha);
        cmd.Parameters.AddWithValue("@D", f.Descripcion);
        cmd.Parameters.AddWithValue("@T", f.Tipo ?? "Nacional");
        cmd.ExecuteNonQuery();
    }
    public void Eliminar(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        new SqlCommand("UPDATE Festivo SET Estado='Inactivo' WHERE IdFestivo=@Id", con) { Parameters = { new("@Id", id) } }.ExecuteNonQuery();
    }
    private Festivo Mapear(SqlDataReader r) => new Festivo
    {
        IdFestivo=(int)r["IdFestivo"], Fecha=(DateTime)r["Fecha"],
        Descripcion=(string)r["Descripcion"], Tipo=(string)r["Tipo"], Estado=(string)r["Estado"]
    };
}
