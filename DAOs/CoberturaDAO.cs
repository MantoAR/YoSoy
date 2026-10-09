using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class CoberturaDAO
{
    public int Insertar(Cobertura c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Cobertura (IdPaciente, IdObraSocial, NroAfiliado, PlanCobertura, Estado) VALUES (@IdPaciente, @IdObraSocial, @NroAfiliado, @PlanCobertura, @Estado); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@IdPaciente",    c.IdPaciente);
        cmd.Parameters.AddWithValue("@IdObraSocial",  c.IdObraSocial);
        cmd.Parameters.AddWithValue("@NroAfiliado",   (object)c.NroAfiliado   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlanCobertura", (object)c.PlanCobertura ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",        (object)c.Estado        ?? DBNull.Value);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(Cobertura c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Cobertura SET IdPaciente = @IdPaciente, IdObraSocial = @IdObraSocial, NroAfiliado = @NroAfiliado, PlanCobertura = @PlanCobertura, Estado = @Estado WHERE IdCobertura = @Id", con);
        cmd.Parameters.AddWithValue("@IdPaciente",    c.IdPaciente);
        cmd.Parameters.AddWithValue("@IdObraSocial",  c.IdObraSocial);
        cmd.Parameters.AddWithValue("@NroAfiliado",   (object)c.NroAfiliado   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlanCobertura", (object)c.PlanCobertura ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",        (object)c.Estado        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",            c.IdCobertura);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Cobertura SET Estado = 'Inactivo' WHERE IdCobertura = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Cobertura> ObtenerTodos()
    {
        var lista = new List<Cobertura>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdCobertura, IdPaciente, IdObraSocial, NroAfiliado, PlanCobertura, Estado FROM Cobertura", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Cobertura ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdCobertura, IdPaciente, IdObraSocial, NroAfiliado, PlanCobertura, Estado FROM Cobertura WHERE IdCobertura = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<Cobertura> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<Cobertura>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdCobertura, IdPaciente, IdObraSocial, NroAfiliado, PlanCobertura, Estado FROM Cobertura WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private Cobertura Mapear(SqlDataReader r) => new Cobertura
    {
        IdCobertura   = (int)r["IdCobertura"],
        IdPaciente    = (int)r["IdPaciente"],
        IdObraSocial  = (int)r["IdObraSocial"],
        NroAfiliado   = r["NroAfiliado"]   == DBNull.Value ? null : (string)r["NroAfiliado"],
        PlanCobertura = r["PlanCobertura"] == DBNull.Value ? null : (string)r["PlanCobertura"],
        Estado        = r["Estado"]        == DBNull.Value ? null : (string)r["Estado"]
    };
}
