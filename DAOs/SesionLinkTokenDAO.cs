using Microsoft.Data.SqlClient;

public class SesionLinkTokenDAO
{
    public void Insertar(SesionLinkToken l)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO SesionLinkToken (IdTurno, Rol, IdUsuario, Token, Expiracion)
            VALUES (@IdTurno, @Rol, @IdUsuario, @Token, @Expiracion)", con);
        cmd.Parameters.AddWithValue("@IdTurno",    l.IdTurno);
        cmd.Parameters.AddWithValue("@Rol",        l.Rol);
        cmd.Parameters.AddWithValue("@IdUsuario",  (object?)l.IdUsuario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Token",      l.Token);
        cmd.Parameters.AddWithValue("@Expiracion", l.Expiracion);
        cmd.ExecuteNonQuery();
    }

    public SesionLinkToken? ObtenerPorToken(string token)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdLink, IdTurno, Rol, IdUsuario, Token, Usado, Expiracion, FechaCreacion
            FROM SesionLinkToken WHERE Token = @Token", con);
        cmd.Parameters.AddWithValue("@Token", token);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new SesionLinkToken
        {
            IdLink        = (int)reader["IdLink"],
            IdTurno       = (int)reader["IdTurno"],
            Rol           = (string)reader["Rol"],
            IdUsuario     = reader["IdUsuario"] == DBNull.Value ? null : (int?)reader["IdUsuario"],
            Token         = (string)reader["Token"],
            Usado         = (bool)reader["Usado"],
            Expiracion    = (DateTime)reader["Expiracion"],
            FechaCreacion = (DateTime)reader["FechaCreacion"]
        };
    }

    public void MarcarUsado(int idLink)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE SesionLinkToken SET Usado = 1 WHERE IdLink = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idLink);
        cmd.ExecuteNonQuery();
    }
}
