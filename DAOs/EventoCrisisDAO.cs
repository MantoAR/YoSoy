using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class EventoCrisisDAO
{
    public void Insertar(EventoCrisis e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO EventoCrisis (IdPaciente, Fecha, NivelRiesgo, Acciones) VALUES (@IdPaciente, @Fecha, @NivelRiesgo, @Acciones)", con);
        cmd.Parameters.AddWithValue("@IdPaciente",  e.IdPaciente);
        cmd.Parameters.AddWithValue("@Fecha",       e.Fecha);
        cmd.Parameters.AddWithValue("@NivelRiesgo", (object)e.NivelRiesgo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Acciones",    (object)e.Acciones    ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(EventoCrisis e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE EventoCrisis SET IdPaciente = @IdPaciente, Fecha = @Fecha, NivelRiesgo = @NivelRiesgo, Acciones = @Acciones WHERE IdEvento = @Id", con);
        cmd.Parameters.AddWithValue("@IdPaciente",  e.IdPaciente);
        cmd.Parameters.AddWithValue("@Fecha",       e.Fecha);
        cmd.Parameters.AddWithValue("@NivelRiesgo", (object)e.NivelRiesgo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Acciones",    (object)e.Acciones    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",          e.IdEvento);
        cmd.ExecuteNonQuery();
    }

    public List<EventoCrisis> ObtenerTodos()
    {
        var lista = new List<EventoCrisis>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEvento, IdPaciente, Fecha, NivelRiesgo, Acciones FROM EventoCrisis", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public EventoCrisis ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEvento, IdPaciente, Fecha, NivelRiesgo, Acciones FROM EventoCrisis WHERE IdEvento = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<EventoCrisis> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<EventoCrisis>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEvento, IdPaciente, Fecha, NivelRiesgo, Acciones FROM EventoCrisis WHERE IdPaciente = @Id ORDER BY Fecha DESC", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private EventoCrisis Mapear(SqlDataReader r) => new EventoCrisis
    {
        IdEvento    = (int)r["IdEvento"],
        IdPaciente  = (int)r["IdPaciente"],
        Fecha       = (DateTime)r["Fecha"],
        NivelRiesgo = r["NivelRiesgo"] == DBNull.Value ? null : (string)r["NivelRiesgo"],
        Acciones    = r["Acciones"]    == DBNull.Value ? null : (string)r["Acciones"]
    };
}
