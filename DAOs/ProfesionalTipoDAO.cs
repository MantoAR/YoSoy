using Microsoft.Data.SqlClient;
using System.Collections.Generic;
public class ProfesionalTipoDAO
{
    public List<ProfesionalTipo> ObtenerPorProfesional(int id)
    {
        var lista = new List<ProfesionalTipo>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdProfesionalTipo, IdProfesional, IdTipoProfesional, Estado FROM ProfesionalTipo WHERE IdProfesional = @Id AND Estado = 'Activo'", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) lista.Add(Mapear(reader));
        return lista;
    }
    public void Insertar(ProfesionalTipo pt)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("INSERT INTO ProfesionalTipo (IdProfesional, IdTipoProfesional, Estado) VALUES (@IdP, @IdT, 'Activo')", con);
        cmd.Parameters.AddWithValue("@IdP", pt.IdProfesional);
        cmd.Parameters.AddWithValue("@IdT", pt.IdTipoProfesional);
        cmd.ExecuteNonQuery();
    }
    public void Eliminar(int idP, int idT)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE ProfesionalTipo SET Estado = 'Inactivo' WHERE IdProfesional = @IdP AND IdTipoProfesional = @IdT", con);
        cmd.Parameters.AddWithValue("@IdP", idP);
        cmd.Parameters.AddWithValue("@IdT", idT);
        cmd.ExecuteNonQuery();
    }
    private ProfesionalTipo Mapear(SqlDataReader r) => new ProfesionalTipo
    {
        IdProfesionalTipo = (int)r["IdProfesionalTipo"],
        IdProfesional = (int)r["IdProfesional"],
        IdTipoProfesional = (int)r["IdTipoProfesional"],
        Estado = (string)r["Estado"]
    };
}
