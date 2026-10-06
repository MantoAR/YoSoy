using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class LogAuditoriaDAO
{
    public void Insertar(LogAuditoria l)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle)
            VALUES (@IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle)", con);
        cmd.Parameters.AddWithValue("@IdUsuario", (object?)l.IdUsuario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Accion",    l.Accion);
        cmd.Parameters.AddWithValue("@Entidad",   l.Entidad);
        cmd.Parameters.AddWithValue("@IdEntidad", (object?)l.IdEntidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Detalle",   (object?)l.Detalle   ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<LogAuditoria> ObtenerPaginado(int pagina, int cantidad)
    {
        var lista = new List<LogAuditoria>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdLog, IdUsuario, Accion, Entidad, IdEntidad, Detalle, FechaHora
            FROM LogAuditoria
            ORDER BY FechaHora DESC
            OFFSET @Offset ROWS FETCH NEXT @Cantidad ROWS ONLY", con);
        cmd.Parameters.AddWithValue("@Offset",   (pagina - 1) * cantidad);
        cmd.Parameters.AddWithValue("@Cantidad", cantidad);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private LogAuditoria Mapear(SqlDataReader r) => new LogAuditoria
    {
        IdLog     = (int)r["IdLog"],
        IdUsuario = r["IdUsuario"] == DBNull.Value ? null : (int?)r["IdUsuario"],
        Accion    = (string)r["Accion"],
        Entidad   = (string)r["Entidad"],
        IdEntidad = r["IdEntidad"] == DBNull.Value ? null : (int?)r["IdEntidad"],
        Detalle   = r["Detalle"]   == DBNull.Value ? null : (string)r["Detalle"],
        FechaHora = (DateTime)r["FechaHora"]
    };
}
