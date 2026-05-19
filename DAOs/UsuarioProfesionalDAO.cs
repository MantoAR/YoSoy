using Microsoft.Data.SqlClient;

public class UsuarioProfesionalDAO
{
    public void Insertar(UsuarioProfesional up)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion) VALUES (@IdUsuario, @IdProfesional, @FechaVinculacion)", con);
        cmd.Parameters.AddWithValue("@IdUsuario",        up.IdUsuario);
        cmd.Parameters.AddWithValue("@IdProfesional",    up.IdProfesional);
        cmd.Parameters.AddWithValue("@FechaVinculacion", up.FechaVinculacion);
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("DELETE FROM UsuarioProfesional WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        cmd.ExecuteNonQuery();
    }

    public UsuarioProfesional ObtenerPorUsuario(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario, IdProfesional, FechaVinculacion FROM UsuarioProfesional WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new UsuarioProfesional
        {
            IdUsuario        = (int)reader["IdUsuario"],
            IdProfesional    = (int)reader["IdProfesional"],
            FechaVinculacion = (DateTime)reader["FechaVinculacion"]
        };
    }
}
