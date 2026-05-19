using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class InteraccionIADAO
{
    public void Insertar(InteraccionIA i)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO InteraccionIA (IdPaciente, Fecha, Consulta, Respuesta) VALUES (@IdPaciente, @Fecha, @Consulta, @Respuesta)", con);
        cmd.Parameters.AddWithValue("@IdPaciente", i.IdPaciente);
        cmd.Parameters.AddWithValue("@Fecha",      i.Fecha);
        cmd.Parameters.AddWithValue("@Consulta",   i.Consulta);
        cmd.Parameters.AddWithValue("@Respuesta",  i.Respuesta);
        cmd.ExecuteNonQuery();
    }

    public List<InteraccionIA> ObtenerTodos()
    {
        var lista = new List<InteraccionIA>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdInteraccion, IdPaciente, Fecha, Consulta, Respuesta FROM InteraccionIA", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public InteraccionIA ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdInteraccion, IdPaciente, Fecha, Consulta, Respuesta FROM InteraccionIA WHERE IdInteraccion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<InteraccionIA> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<InteraccionIA>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdInteraccion, IdPaciente, Fecha, Consulta, Respuesta FROM InteraccionIA WHERE IdPaciente = @Id ORDER BY Fecha DESC", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private InteraccionIA Mapear(SqlDataReader r) => new InteraccionIA
    {
        IdInteraccion = (int)r["IdInteraccion"],
        IdPaciente    = (int)r["IdPaciente"],
        Fecha         = (DateTime)r["Fecha"],
        Consulta      = (string)r["Consulta"],
        Respuesta     = (string)r["Respuesta"]
    };
}
