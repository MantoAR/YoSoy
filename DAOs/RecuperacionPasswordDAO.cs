using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class RecuperacionPasswordDAO
{
    public void Insertar(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO RecuperacionPassword (IdUsuario) VALUES (@IdUsuario)", con);
        cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
        cmd.ExecuteNonQuery();
    }

    public bool TienePendiente(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT COUNT(1) FROM RecuperacionPassword WHERE IdUsuario = @Id AND Estado = 'Pendiente'", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        return (int)cmd.ExecuteScalar() > 0;
    }

    public List<RecuperacionPassword> ObtenerPendientes()
    {
        var lista = new List<RecuperacionPassword>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT r.IdRecuperacion, r.IdUsuario, r.FechaSolicitud, r.Estado,
                   r.PasswordTemporal, r.FechaResolucion, u.Username, u.Mail
            FROM RecuperacionPassword r
            JOIN Usuario u ON r.IdUsuario = u.IdUsuario
            WHERE r.Estado = 'Pendiente'
            ORDER BY r.FechaSolicitud ASC", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public RecuperacionPassword ObtenerUltimaPorUsername(string username)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT TOP 1 r.IdRecuperacion, r.IdUsuario, r.FechaSolicitud, r.Estado,
                   r.PasswordTemporal, r.FechaResolucion, u.Username, u.Mail
            FROM RecuperacionPassword r
            JOIN Usuario u ON r.IdUsuario = u.IdUsuario
            WHERE u.Username = @Username
            ORDER BY r.FechaSolicitud DESC", con);
        cmd.Parameters.AddWithValue("@Username", username);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public void Aceptar(int idRecuperacion, string passwordTemporal)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE RecuperacionPassword
            SET Estado = 'Aceptada', PasswordTemporal = @Pass, FechaResolucion = GETDATE()
            WHERE IdRecuperacion = @Id", con);
        cmd.Parameters.AddWithValue("@Pass", passwordTemporal);
        cmd.Parameters.AddWithValue("@Id",   idRecuperacion);
        cmd.ExecuteNonQuery();
    }

    public void Rechazar(int idRecuperacion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE RecuperacionPassword
            SET Estado = 'Rechazada', FechaResolucion = GETDATE()
            WHERE IdRecuperacion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idRecuperacion);
        cmd.ExecuteNonQuery();
    }

    public int? ObtenerIdUsuario(int idRecuperacion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario FROM RecuperacionPassword WHERE IdRecuperacion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idRecuperacion);
        var result = cmd.ExecuteScalar();
        return result == null ? null : (int?)result;
    }

    private RecuperacionPassword Mapear(SqlDataReader r) => new RecuperacionPassword
    {
        IdRecuperacion   = (int)r["IdRecuperacion"],
        IdUsuario        = (int)r["IdUsuario"],
        FechaSolicitud   = (DateTime)r["FechaSolicitud"],
        Estado           = (string)r["Estado"],
        PasswordTemporal = r["PasswordTemporal"] == DBNull.Value ? null : (string)r["PasswordTemporal"],
        FechaResolucion  = r["FechaResolucion"]  == DBNull.Value ? null : (DateTime?)r["FechaResolucion"],
        Username         = r["Username"] == DBNull.Value ? null : (string)r["Username"],
        Mail             = r["Mail"]     == DBNull.Value ? null : (string)r["Mail"]
    };
}
