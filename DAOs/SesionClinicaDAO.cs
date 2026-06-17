using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class SesionClinicaDAO
{
    public int Insertar(SesionClinica s)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"INSERT INTO SesionClinica 
                (IdTurno, IdPaciente, IdProfesional, FechaHora, Transcripcion, NotasEvolucion, HistorialEmociones, TipoCierre)
              VALUES 
                (@IdTurno, @IdPaciente, @IdProfesional, @FechaHora, @Transcripcion, @NotasEvolucion, @HistorialEmociones, @TipoCierre);
              SELECT SCOPE_IDENTITY();", con);
        cmd.Parameters.AddWithValue("@IdTurno",            (object)s.IdTurno           ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdPaciente",         s.IdPaciente);
        cmd.Parameters.AddWithValue("@IdProfesional",      s.IdProfesional);
        cmd.Parameters.AddWithValue("@FechaHora",          s.FechaHora);
        cmd.Parameters.AddWithValue("@Transcripcion",      (object)s.Transcripcion      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NotasEvolucion",     (object)s.NotasEvolucion     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HistorialEmociones", (object)s.HistorialEmociones ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TipoCierre",         (object)s.TipoCierre         ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<SesionClinica> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<SesionClinica>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"SELECT IdSesion, IdTurno, IdPaciente, IdProfesional, FechaHora, 
                     Transcripcion, NotasEvolucion, HistorialEmociones, TipoCierre
              FROM SesionClinica 
              WHERE IdPaciente = @Id 
              ORDER BY FechaHora DESC", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public List<SesionClinica> ObtenerPorProfesional(int idProfesional)
    {
        var lista = new List<SesionClinica>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"SELECT IdSesion, IdTurno, IdPaciente, IdProfesional, FechaHora,
                     Transcripcion, NotasEvolucion, HistorialEmociones, TipoCierre
              FROM SesionClinica
              WHERE IdProfesional = @Id
              ORDER BY FechaHora DESC", con);
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private SesionClinica Mapear(SqlDataReader r) => new SesionClinica
    {
        IdSesion           = (int)r["IdSesion"],
        IdTurno            = r["IdTurno"]            == DBNull.Value ? null : (int?)r["IdTurno"],
        IdPaciente         = (int)r["IdPaciente"],
        IdProfesional      = (int)r["IdProfesional"],
        FechaHora          = (DateTime)r["FechaHora"],
        Transcripcion      = r["Transcripcion"]      == DBNull.Value ? null : (string)r["Transcripcion"],
        NotasEvolucion     = r["NotasEvolucion"]     == DBNull.Value ? null : (string)r["NotasEvolucion"],
        HistorialEmociones = r["HistorialEmociones"] == DBNull.Value ? null : (string)r["HistorialEmociones"],
        TipoCierre         = r["TipoCierre"]         == DBNull.Value ? null : (string)r["TipoCierre"]
    };
}