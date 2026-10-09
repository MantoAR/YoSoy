using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class EspecialidadDAO
{
    public int Insertar(Especialidad e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Especialidad (Nombre, Estado) VALUES (@Nombre, @Estado); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Nombre", e.Nombre);
        cmd.Parameters.AddWithValue("@Estado", e.Estado);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(Especialidad e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Especialidad SET Nombre = @Nombre, Estado = @Estado WHERE IdEspecialidad = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre", e.Nombre);
        cmd.Parameters.AddWithValue("@Estado", e.Estado);
        cmd.Parameters.AddWithValue("@Id", e.IdEspecialidad);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Especialidad SET Estado = 'Inactivo' WHERE IdEspecialidad = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Especialidad> ObtenerTodos()
    {
        var lista = new List<Especialidad>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdEspecialidad, Nombre, Estado FROM Especialidad", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Especialidad ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEspecialidad, Nombre, Estado FROM Especialidad WHERE IdEspecialidad = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private Especialidad Mapear(SqlDataReader r) => new Especialidad
    {
        IdEspecialidad = (int)r["IdEspecialidad"],
        Nombre         = (string)r["Nombre"],
        Estado         = (string)r["Estado"]
    };
}
