using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class BloqueosAgendaDAO
{
    public List<BloqueosAgenda> ObtenerPorProfesional(int idProfesional)
    {
        var lista = new List<BloqueosAgenda>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"SELECT IdBloqueo, IdProfesional, IdAgenda, FechaDesde, FechaHasta,
                     HoraDesde, HoraHasta, Motivo, Observaciones, Estado, FechaCreacion
              FROM BloqueosAgenda
              WHERE IdProfesional = @Id AND Estado = 'Activo'
              ORDER BY FechaDesde", con);
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public void Insertar(BloqueosAgenda b)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"INSERT INTO BloqueosAgenda
                (IdProfesional, IdAgenda, FechaDesde, FechaHasta, HoraDesde, HoraHasta,
                 Motivo, Observaciones, Estado)
              VALUES
                (@IdProfesional, @IdAgenda, @FechaDesde, @FechaHasta, @HoraDesde, @HoraHasta,
                 @Motivo, @Observaciones, 'Activo')", con);
        cmd.Parameters.AddWithValue("@IdProfesional",  b.IdProfesional);
        cmd.Parameters.AddWithValue("@IdAgenda",       (object)b.IdAgenda      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaDesde",     b.FechaDesde);
        cmd.Parameters.AddWithValue("@FechaHasta",     b.FechaHasta);
        cmd.Parameters.AddWithValue("@HoraDesde",      (object)b.HoraDesde     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraHasta",      (object)b.HoraHasta     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Motivo",         b.Motivo);
        cmd.Parameters.AddWithValue("@Observaciones",  (object)b.Observaciones ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE BloqueosAgenda SET Estado = 'Inactivo' WHERE IdBloqueo = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    private BloqueosAgenda Mapear(SqlDataReader r) => new BloqueosAgenda
    {
        IdBloqueo      = (int)r["IdBloqueo"],
        IdProfesional  = (int)r["IdProfesional"],
        IdAgenda       = r["IdAgenda"]      == DBNull.Value ? null : (int?)r["IdAgenda"],
        FechaDesde     = (DateTime)r["FechaDesde"],
        FechaHasta     = (DateTime)r["FechaHasta"],
        HoraDesde      = r["HoraDesde"]     == DBNull.Value ? null : (TimeSpan?)r["HoraDesde"],
        HoraHasta      = r["HoraHasta"]     == DBNull.Value ? null : (TimeSpan?)r["HoraHasta"],
        Motivo         = (string)r["Motivo"],
        Observaciones  = r["Observaciones"] == DBNull.Value ? null : (string)r["Observaciones"],
        Estado         = (string)r["Estado"],
        FechaCreacion  = (DateTime)r["FechaCreacion"]
    };
}