using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class HistoriaClinicaDAO
{
    public void Insertar(HistoriaClinica h)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO HistoriaClinica (IdPaciente, FechaApertura) VALUES (@IdPaciente, @FechaApertura)", con);
        cmd.Parameters.AddWithValue("@IdPaciente",    h.IdPaciente);
        cmd.Parameters.AddWithValue("@FechaApertura", (object)h.FechaApertura ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<HistoriaClinica> ObtenerTodos()
    {
        var lista = new List<HistoriaClinica>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdHistoria, IdPaciente, FechaApertura FROM HistoriaClinica", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public HistoriaClinica ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdHistoria, IdPaciente, FechaApertura FROM HistoriaClinica WHERE IdHistoria = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public HistoriaClinica ObtenerPorPaciente(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdHistoria, IdPaciente, FechaApertura FROM HistoriaClinica WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private HistoriaClinica Mapear(SqlDataReader r) => new HistoriaClinica
    {
        IdHistoria    = (int)r["IdHistoria"],
        IdPaciente    = (int)r["IdPaciente"],
        FechaApertura = r["FechaApertura"] == DBNull.Value ? null : (DateTime?)r["FechaApertura"]
    };
}
