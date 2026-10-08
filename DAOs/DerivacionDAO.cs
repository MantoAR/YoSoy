using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class DerivacionDAO
{
    public int Insertar(Derivacion d)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Derivacion (IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado) VALUES (@IdPaciente, @IdProfesionalOrigen, @IdProfesionalDestino, @Fecha, @Motivo, @Estado); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@IdPaciente",           d.IdPaciente);
        cmd.Parameters.AddWithValue("@IdProfesionalOrigen",  d.IdProfesionalOrigen);
        cmd.Parameters.AddWithValue("@IdProfesionalDestino", d.IdProfesionalDestino);
        cmd.Parameters.AddWithValue("@Fecha",                d.Fecha);
        cmd.Parameters.AddWithValue("@Motivo",               (object)d.Motivo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",               d.Estado);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(Derivacion d)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Derivacion SET IdPaciente = @IdPaciente, IdProfesionalOrigen = @IdProfesionalOrigen, IdProfesionalDestino = @IdProfesionalDestino, Fecha = @Fecha, Motivo = @Motivo, Estado = @Estado WHERE IdDerivacion = @Id", con);
        cmd.Parameters.AddWithValue("@IdPaciente",           d.IdPaciente);
        cmd.Parameters.AddWithValue("@IdProfesionalOrigen",  d.IdProfesionalOrigen);
        cmd.Parameters.AddWithValue("@IdProfesionalDestino", d.IdProfesionalDestino);
        cmd.Parameters.AddWithValue("@Fecha",                d.Fecha);
        cmd.Parameters.AddWithValue("@Motivo",               (object)d.Motivo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",               d.Estado);
        cmd.Parameters.AddWithValue("@Id",                   d.IdDerivacion);
        cmd.ExecuteNonQuery();
    }

    public List<Derivacion> ObtenerTodos()
    {
        var lista = new List<Derivacion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdDerivacion, IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado FROM Derivacion", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Derivacion ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdDerivacion, IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado FROM Derivacion WHERE IdDerivacion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<Derivacion> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<Derivacion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdDerivacion, IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado FROM Derivacion WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private Derivacion Mapear(SqlDataReader r) => new Derivacion
    {
        IdDerivacion         = (int)r["IdDerivacion"],
        IdPaciente           = (int)r["IdPaciente"],
        IdProfesionalOrigen  = (int)r["IdProfesionalOrigen"],
        IdProfesionalDestino = (int)r["IdProfesionalDestino"],
        Fecha                = (DateTime)r["Fecha"],
        Motivo               = r["Motivo"] == DBNull.Value ? null : (string)r["Motivo"],
        Estado               = (string)r["Estado"]
    };
}
