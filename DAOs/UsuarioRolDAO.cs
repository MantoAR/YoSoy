using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class UsuarioRolDAO
{
    public void Insertar(UsuarioRol ur)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO UsuarioRol (IdUsuario, IdRol) VALUES (@IdUsuario, @IdRol)", con);
        cmd.Parameters.AddWithValue("@IdUsuario", ur.IdUsuario);
        cmd.Parameters.AddWithValue("@IdRol",     ur.IdRol);
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int idUsuario, int idRol)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "DELETE FROM UsuarioRol WHERE IdUsuario = @IdUsuario AND IdRol = @IdRol", con);
        cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
        cmd.Parameters.AddWithValue("@IdRol",     idRol);
        cmd.ExecuteNonQuery();
    }

    public List<UsuarioRol> ObtenerPorUsuario(int idUsuario)
    {
        var lista = new List<UsuarioRol>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario, IdRol FROM UsuarioRol WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(new UsuarioRol { IdUsuario = (int)reader["IdUsuario"], IdRol = (int)reader["IdRol"] });
        return lista;
    }
}
