using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class EstadoTurnoDAO
{
    public int Insertar(EstadoTurno et)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO EstadoTurno (Descripcion, Estado) VALUES (@Descripcion, @Estado); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Descripcion", et.Descripcion);
        cmd.Parameters.AddWithValue("@Estado", et.Estado);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(EstadoTurno et)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE EstadoTurno SET Descripcion = @Descripcion, Estado = @Estado WHERE IdEstadoTurno = @Id", con);
        cmd.Parameters.AddWithValue("@Descripcion", et.Descripcion);
        cmd.Parameters.AddWithValue("@Estado", et.Estado);
        cmd.Parameters.AddWithValue("@Id", et.IdEstadoTurno);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE EstadoTurno SET Estado = 'Inactivo' WHERE IdEstadoTurno = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<EstadoTurno> ObtenerTodos()
    {
        var lista = new List<EstadoTurno>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdEstadoTurno, Descripcion, Estado FROM EstadoTurno", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public EstadoTurno ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEstadoTurno, Descripcion, Estado FROM EstadoTurno WHERE IdEstadoTurno = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private EstadoTurno Mapear(SqlDataReader r) => new EstadoTurno
    {
        IdEstadoTurno = (int)r["IdEstadoTurno"],
        Descripcion   = (string)r["Descripcion"],
        Estado        = (string)r["Estado"]
    };
}
