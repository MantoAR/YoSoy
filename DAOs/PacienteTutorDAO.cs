using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class PacienteTutorDAO
{
    public void Insertar(PacienteTutor pt)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO PacienteTutor (IdPaciente, IdTutor) VALUES (@IdPaciente, @IdTutor)", con);
        cmd.Parameters.AddWithValue("@IdPaciente", pt.IdPaciente);
        cmd.Parameters.AddWithValue("@IdTutor",    pt.IdTutor);
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int idPaciente, int idTutor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "DELETE FROM PacienteTutor WHERE IdPaciente = @IdPaciente AND IdTutor = @IdTutor", con);
        cmd.Parameters.AddWithValue("@IdPaciente", idPaciente);
        cmd.Parameters.AddWithValue("@IdTutor",    idTutor);
        cmd.ExecuteNonQuery();
    }

    public List<PacienteTutor> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<PacienteTutor>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdPaciente, IdTutor FROM PacienteTutor WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(new PacienteTutor { IdPaciente = (int)reader["IdPaciente"], IdTutor = (int)reader["IdTutor"] });
        return lista;
    }
}
