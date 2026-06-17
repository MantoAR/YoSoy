using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class ConsentimientoDAO
{
    public void Insertar(Consentimiento c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Consentimiento (IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado) VALUES (@IdPaciente, @IdTipoConsentimiento, @Version, @FechaFirma, @Estado)", con);
        cmd.Parameters.AddWithValue("@IdPaciente",           c.IdPaciente);
        cmd.Parameters.AddWithValue("@IdTipoConsentimiento", c.IdTipoConsentimiento);
        cmd.Parameters.AddWithValue("@Version",              (object)c.Version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaFirma",           c.FechaFirma);
        cmd.Parameters.AddWithValue("@Estado",               (object)c.Estado  ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Consentimiento c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Consentimiento SET IdPaciente = @IdPaciente, IdTipoConsentimiento = @IdTipoConsentimiento, Version = @Version, FechaFirma = @FechaFirma, Estado = @Estado WHERE IdConsentimiento = @Id", con);
        cmd.Parameters.AddWithValue("@IdPaciente",           c.IdPaciente);
        cmd.Parameters.AddWithValue("@IdTipoConsentimiento", c.IdTipoConsentimiento);
        cmd.Parameters.AddWithValue("@Version",              (object)c.Version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaFirma",           c.FechaFirma);
        cmd.Parameters.AddWithValue("@Estado",               (object)c.Estado  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",                   c.IdConsentimiento);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Consentimiento SET Estado = 'Inactivo' WHERE IdConsentimiento = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Consentimiento> ObtenerTodos()
    {
        var lista = new List<Consentimiento>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdConsentimiento, IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado FROM Consentimiento", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Consentimiento ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdConsentimiento, IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado FROM Consentimiento WHERE IdConsentimiento = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<Consentimiento> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<Consentimiento>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdConsentimiento, IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado FROM Consentimiento WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private Consentimiento Mapear(SqlDataReader r) => new Consentimiento
    {
        IdConsentimiento     = (int)r["IdConsentimiento"],
        IdPaciente           = (int)r["IdPaciente"],
        IdTipoConsentimiento = (int)r["IdTipoConsentimiento"],
        Version              = r["Version"] == DBNull.Value ? null : (string)r["Version"],
        FechaFirma           = (DateTime)r["FechaFirma"],
        Estado               = r["Estado"]  == DBNull.Value ? null : (string)r["Estado"]
    };
}
