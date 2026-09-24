using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public interface INotificacionInternaDAO
{
    int Insertar(NotificacionInterna n);
}

public class NotificacionInternaDAO : INotificacionInternaDAO
{
    public int Insertar(NotificacionInterna n)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO NotificacionInterna
                (Destinatario, IdProfesional, IdTurno, IdConversacion, Tipo, Titulo, Mensaje)
            VALUES (@Dest, @IdProf, @IdTurno, @IdConv, @Tipo, @Titulo, @Mensaje);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Dest",    n.Destinatario);
        cmd.Parameters.AddWithValue("@IdProf",  (object?)n.IdProfesional  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdTurno", (object?)n.IdTurno        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdConv",  (object?)n.IdConversacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Tipo",    n.Tipo);
        cmd.Parameters.AddWithValue("@Titulo",  n.Titulo.Length  > 200  ? n.Titulo[..200]   : n.Titulo);
        cmd.Parameters.AddWithValue("@Mensaje", n.Mensaje.Length > 1000 ? n.Mensaje[..1000] : n.Mensaje);
        n.IdNotificacionInterna = (int)cmd.ExecuteScalar();
        return n.IdNotificacionInterna;
    }

    public List<NotificacionInterna> Listar(string? destinatario, bool soloNoLeidas)
    {
        var lista = new List<NotificacionInterna>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var sql = @"SELECT IdNotificacionInterna, Destinatario, IdProfesional, IdTurno, IdConversacion,
                           Tipo, Titulo, Mensaje, Leida, FechaCreacion
                    FROM NotificacionInterna WHERE 1 = 1" +
                  (destinatario != null ? " AND Destinatario = @Dest" : "") +
                  (soloNoLeidas ? " AND Leida = 0" : "") +
                  " ORDER BY FechaCreacion DESC";
        var cmd = new SqlCommand(sql, con);
        if (destinatario != null) cmd.Parameters.AddWithValue("@Dest", destinatario);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new NotificacionInterna
            {
                IdNotificacionInterna = (int)r["IdNotificacionInterna"],
                Destinatario          = (string)r["Destinatario"],
                IdProfesional         = r["IdProfesional"]  == DBNull.Value ? null : (int)r["IdProfesional"],
                IdTurno               = r["IdTurno"]        == DBNull.Value ? null : (int)r["IdTurno"],
                IdConversacion        = r["IdConversacion"] == DBNull.Value ? null : (int)r["IdConversacion"],
                Tipo                  = (string)r["Tipo"],
                Titulo                = (string)r["Titulo"],
                Mensaje               = (string)r["Mensaje"],
                Leida                 = (bool)r["Leida"],
                FechaCreacion         = (DateTime)r["FechaCreacion"]
            });
        return lista;
    }

    public void MarcarLeida(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE NotificacionInterna SET Leida = 1 WHERE IdNotificacionInterna = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }
}
