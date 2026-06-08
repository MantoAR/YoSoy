using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class SesionVideoDAO
{
    public int Insertar(SesionVideo s)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO SesionVideo (IdTurno, RoomId, Estado, FinProgramado)
            VALUES (@IdTurno, @RoomId, @Estado, @FinProgramado);
            SELECT SCOPE_IDENTITY();", con);
        cmd.Parameters.AddWithValue("@IdTurno",       s.IdTurno);
        cmd.Parameters.AddWithValue("@RoomId",        s.RoomId);
        cmd.Parameters.AddWithValue("@Estado",        s.Estado);
        cmd.Parameters.AddWithValue("@FinProgramado", s.FinProgramado);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public SesionVideo? ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(Select() + " WHERE IdSesion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public SesionVideo? ObtenerPorTurno(int idTurno)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(Select() + " WHERE IdTurno = @IdTurno", con);
        cmd.Parameters.AddWithValue("@IdTurno", idTurno);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public SesionVideo? ObtenerPorRoomId(string roomId)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(Select() + " WHERE RoomId = @RoomId", con);
        cmd.Parameters.AddWithValue("@RoomId", roomId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public void MarcarActiva(int idSesion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE SesionVideo
            SET Estado = 'activa', InicioReal = COALESCE(InicioReal, GETDATE())
            WHERE IdSesion = @Id AND Estado <> 'finalizada'", con);
        cmd.Parameters.AddWithValue("@Id", idSesion);
        cmd.ExecuteNonQuery();
    }

    public void ActualizarFinProgramado(int idSesion, DateTime nuevoFin)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE SesionVideo SET FinProgramado = @Fin WHERE IdSesion = @Id", con);
        cmd.Parameters.AddWithValue("@Fin", nuevoFin);
        cmd.Parameters.AddWithValue("@Id",  idSesion);
        cmd.ExecuteNonQuery();
    }

    public void Finalizar(int idSesion, string? motivo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE SesionVideo
            SET Estado = 'finalizada',
                FinReal = GETDATE(),
                DuracionReal = CASE WHEN InicioReal IS NOT NULL
                                    THEN DATEDIFF(SECOND, InicioReal, GETDATE()) END,
                MotivoFin = @Motivo
            WHERE IdSesion = @Id", con);
        cmd.Parameters.AddWithValue("@Motivo", (object?)motivo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",     idSesion);
        cmd.ExecuteNonQuery();
    }

    private static string Select() =>
        @"SELECT IdSesion, IdTurno, RoomId, Estado, InicioReal, FinProgramado,
                 FinReal, DuracionReal, MotivoFin, FechaCreacion
          FROM SesionVideo";

    private SesionVideo Mapear(SqlDataReader r) => new SesionVideo
    {
        IdSesion      = (int)r["IdSesion"],
        IdTurno       = (int)r["IdTurno"],
        RoomId        = (string)r["RoomId"],
        Estado        = (string)r["Estado"],
        InicioReal    = r["InicioReal"]   == DBNull.Value ? null : (DateTime?)r["InicioReal"],
        FinProgramado = (DateTime)r["FinProgramado"],
        FinReal       = r["FinReal"]      == DBNull.Value ? null : (DateTime?)r["FinReal"],
        DuracionReal  = r["DuracionReal"] == DBNull.Value ? null : (int?)r["DuracionReal"],
        MotivoFin     = r["MotivoFin"]    == DBNull.Value ? null : (string)r["MotivoFin"],
        FechaCreacion = (DateTime)r["FechaCreacion"]
    };
}
