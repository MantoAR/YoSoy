using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class SesionVideoEventoDAO
{
    public void Insertar(SesionVideoEvento e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO SesionVideoEvento (IdSesion, Tipo, Detalle, IdUsuario, Rol)
            VALUES (@IdSesion, @Tipo, @Detalle, @IdUsuario, @Rol)", con);
        cmd.Parameters.AddWithValue("@IdSesion",  e.IdSesion);
        cmd.Parameters.AddWithValue("@Tipo",      e.Tipo);
        cmd.Parameters.AddWithValue("@Detalle",   (object?)e.Detalle   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdUsuario", (object?)e.IdUsuario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Rol",       (object?)e.Rol       ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<SesionVideoEvento> ObtenerPorSesion(int idSesion)
    {
        var lista = new List<SesionVideoEvento>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdEvento, IdSesion, Tipo, Detalle, IdUsuario, Rol, Fecha
            FROM SesionVideoEvento WHERE IdSesion = @Id ORDER BY Fecha ASC", con);
        cmd.Parameters.AddWithValue("@Id", idSesion);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(new SesionVideoEvento
            {
                IdEvento  = (int)reader["IdEvento"],
                IdSesion  = (int)reader["IdSesion"],
                Tipo      = (string)reader["Tipo"],
                Detalle   = reader["Detalle"]   == DBNull.Value ? null : (string)reader["Detalle"],
                IdUsuario = reader["IdUsuario"] == DBNull.Value ? null : (int?)reader["IdUsuario"],
                Rol       = reader["Rol"]       == DBNull.Value ? null : (string)reader["Rol"],
                Fecha     = (DateTime)reader["Fecha"]
            });
        return lista;
    }
}
