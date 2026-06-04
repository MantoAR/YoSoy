using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class ProfesionalTipoDAO
{
    public List<ProfesionalTipo> ObtenerPorProfesional(int idProfesional)
    {
        var lista = new List<ProfesionalTipo>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdProfesionalTipo, IdProfesional, IdTipoProfesional, Estado FROM ProfesionalTipo WHERE IdProfesional = @Id AND Estado = 'Activo'", con);
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public void Insertar(ProfesionalTipo pt)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO ProfesionalTipo (IdProfesional, IdTipoProfesional, Estado) VALUES (@IdProfesional, @IdTipoProfesional, 'Activo')", con);
        cmd.Parameters.AddWithValue("@IdProfesional",     pt.IdProfesional);
        cmd.Parameters.AddWithValue("@IdTipoProfesional", pt.IdTipoProfesional);
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int idProfesional, int idTipoProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE ProfesionalTipo SET Estado = 'Inactivo' WHERE IdProfesional = @IdProfesional AND IdTipoProfesional = @IdTipoProfesional", con);
        cmd.Parameters.AddWithValue("@IdProfesional",     idProfesional);
        cmd.Parameters.AddWithValue("@IdTipoProfesional", idTipoProfesional);
        cmd.ExecuteNonQuery();
    }

    private ProfesionalTipo Mapear(SqlDataReader r) => new ProfesionalTipo
    {
        IdProfesionalTipo = (int)r["IdProfesionalTipo"],
        IdProfesional     = (int)r["IdProfesional"],
        IdTipoProfesional = (int)r["IdTipoProfesional"],
        Estado            = (string)r["Estado"]
    };
}