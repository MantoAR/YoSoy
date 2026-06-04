using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class ServicioDAO
{
    public List<Servicio> ObtenerTodos()
    {
        var lista = new List<Servicio>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdServicio, Nombre, Estado FROM Servicio WHERE Estado = 'Activo'", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public void Insertar(Servicio s)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("INSERT INTO Servicio (Nombre, Estado) VALUES (@Nombre, @Estado)", con);
        cmd.Parameters.AddWithValue("@Nombre", s.Nombre);
        cmd.Parameters.AddWithValue("@Estado", s.Estado ?? "Activo");
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Servicio s)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE Servicio SET Nombre = @Nombre, Estado = @Estado WHERE IdServicio = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre", s.Nombre);
        cmd.Parameters.AddWithValue("@Estado", s.Estado);
        cmd.Parameters.AddWithValue("@Id",     s.IdServicio);
        cmd.ExecuteNonQuery();
    }

    private Servicio Mapear(SqlDataReader r) => new Servicio
    {
        IdServicio = (int)r["IdServicio"],
        Nombre     = (string)r["Nombre"],
        Estado     = (string)r["Estado"]
    };
}