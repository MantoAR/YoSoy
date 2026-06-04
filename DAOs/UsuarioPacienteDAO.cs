using Microsoft.Data.SqlClient;

public class UsuarioPacienteDAO
{
    public void Insertar(UsuarioPaciente up)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente, FechaVinculacion) VALUES (@IdUsuario, @IdPaciente, @FechaVinculacion)", con);
        cmd.Parameters.AddWithValue("@IdUsuario",        up.IdUsuario);
        cmd.Parameters.AddWithValue("@IdPaciente",       up.IdPaciente);
        cmd.Parameters.AddWithValue("@FechaVinculacion", up.FechaVinculacion);
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("DELETE FROM UsuarioPaciente WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        cmd.ExecuteNonQuery();
    }

    public UsuarioPaciente ObtenerPorUsuario(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario, IdPaciente, FechaVinculacion FROM UsuarioPaciente WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new UsuarioPaciente
        {
            IdUsuario        = (int)reader["IdUsuario"],
            IdPaciente       = (int)reader["IdPaciente"],
            FechaVinculacion = (DateTime)reader["FechaVinculacion"]
        };
    }
}
