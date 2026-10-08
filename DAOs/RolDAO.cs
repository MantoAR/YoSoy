using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class RolDAO
{
    public int Insertar(Rol r)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Rol (Nombre, Estado) VALUES (@Nombre, @Estado); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Nombre", r.Nombre);
        cmd.Parameters.AddWithValue("@Estado", r.Estado);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(Rol r)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Rol SET Nombre = @Nombre, Estado = @Estado WHERE IdRol = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre", r.Nombre);
        cmd.Parameters.AddWithValue("@Estado", r.Estado);
        cmd.Parameters.AddWithValue("@Id", r.IdRol);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Rol SET Estado = 'Inactivo' WHERE IdRol = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Rol> ObtenerTodos()
    {
        var lista = new List<Rol>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdRol, Nombre, Estado FROM Rol", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Rol ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdRol, Nombre, Estado FROM Rol WHERE IdRol = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private Rol Mapear(SqlDataReader r) => new Rol
    {
        IdRol  = (int)r["IdRol"],
        Nombre = (string)r["Nombre"],
        Estado = (string)r["Estado"]
    };
}
