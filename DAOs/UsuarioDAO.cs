using Microsoft.Data.SqlClient;
using System.Collections.Generic;

// El DAO recibe PasswordHash ya generado. El hashing se hace antes de llamar a Insertar/Modificar.
public class UsuarioDAO
{
    public void Insertar(Usuario u)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@Username, @PasswordHash, @Estado, @Mail)", con);
        cmd.Parameters.AddWithValue("@Username",     u.Username);
        cmd.Parameters.AddWithValue("@PasswordHash", u.PasswordHash);
        cmd.Parameters.AddWithValue("@Estado",       u.Estado);
        cmd.Parameters.AddWithValue("@Mail",         (object)u.Mail ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Usuario u)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Usuario SET Username = @Username, PasswordHash = @PasswordHash, Estado = @Estado, Mail = @Mail WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Username",     u.Username);
        cmd.Parameters.AddWithValue("@PasswordHash", u.PasswordHash);
        cmd.Parameters.AddWithValue("@Estado",       u.Estado);
        cmd.Parameters.AddWithValue("@Mail",         (object)u.Mail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",           u.IdUsuario);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Usuario SET Estado = 'Inactivo' WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Usuario> ObtenerTodos()
    {
        var lista = new List<Usuario>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdUsuario, Username, PasswordHash, Estado, Mail, IntentosFallidos, BloqueadoHasta FROM Usuario", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Usuario ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario, Username, PasswordHash, Estado, Mail, IntentosFallidos, BloqueadoHasta FROM Usuario WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public Usuario ObtenerPorUsername(string username)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario, Username, PasswordHash, Estado, Mail, IntentosFallidos, BloqueadoHasta FROM Usuario WHERE Username = @Username", con);
        cmd.Parameters.AddWithValue("@Username", username);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public void RegistrarIntentoFallido(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Usuario
            SET IntentosFallidos = IntentosFallidos + 1,
                BloqueadoHasta = CASE WHEN IntentosFallidos + 1 >= 3
                                      THEN DATEADD(MINUTE, 15, GETDATE())
                                      ELSE BloqueadoHasta END
            WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        cmd.ExecuteNonQuery();
    }

    public void ResetearIntentos(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Usuario SET IntentosFallidos = 0, BloqueadoHasta = NULL WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        cmd.ExecuteNonQuery();
    }

    private Usuario Mapear(SqlDataReader r) => new Usuario
    {
        IdUsuario         = (int)r["IdUsuario"],
        Username          = (string)r["Username"],
        PasswordHash      = (string)r["PasswordHash"],
        Estado            = r["Estado"] == DBNull.Value ? null : (string)r["Estado"],
        Mail              = r["Mail"]   == DBNull.Value ? null : (string)r["Mail"],
        IntentosFallidos  = r["IntentosFallidos"] == DBNull.Value ? 0 : (int)r["IntentosFallidos"],
        BloqueadoHasta    = r["BloqueadoHasta"]   == DBNull.Value ? null : (DateTime?)r["BloqueadoHasta"]
    };
}
