using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class HorarioAgendaDAO
{
    public List<HorarioAgenda> ObtenerPorAgenda(int idAgenda)
    {
        var lista = new List<HorarioAgenda>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"SELECT IdHorario, IdAgenda, Codigo, Descripcion, HoraInicio, HoraFin,
                     FechaInicio, FechaFin, Lunes, Martes, Miercoles, Jueves, Viernes,
                     Sabado, Domingo, IntervaloMinutos, MaxCitas, TrabajeFestivos, Estado
              FROM HorarioAgenda WHERE IdAgenda = @Id AND Estado = 'Activo'", con);
        cmd.Parameters.AddWithValue("@Id", idAgenda);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public void Insertar(HorarioAgenda h)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"INSERT INTO HorarioAgenda
                (IdAgenda, Codigo, Descripcion, HoraInicio, HoraFin, FechaInicio, FechaFin,
                 Lunes, Martes, Miercoles, Jueves, Viernes, Sabado, Domingo,
                 IntervaloMinutos, MaxCitas, TrabajeFestivos, Estado)
              VALUES
                (@IdAgenda, @Codigo, @Descripcion, @HoraInicio, @HoraFin, @FechaInicio, @FechaFin,
                 @Lunes, @Martes, @Miercoles, @Jueves, @Viernes, @Sabado, @Domingo,
                 @IntervaloMinutos, @MaxCitas, @TrabajeFestivos, 'Activo')", con);
        cmd.Parameters.AddWithValue("@IdAgenda",         h.IdAgenda);
        cmd.Parameters.AddWithValue("@Codigo",           (object)h.Codigo      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Descripcion",      (object)h.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraInicio",       h.HoraInicio);
        cmd.Parameters.AddWithValue("@HoraFin",          h.HoraFin);
        cmd.Parameters.AddWithValue("@FechaInicio",      h.FechaInicio);
        cmd.Parameters.AddWithValue("@FechaFin",         (object)h.FechaFin    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Lunes",            h.Lunes);
        cmd.Parameters.AddWithValue("@Martes",           h.Martes);
        cmd.Parameters.AddWithValue("@Miercoles",        h.Miercoles);
        cmd.Parameters.AddWithValue("@Jueves",           h.Jueves);
        cmd.Parameters.AddWithValue("@Viernes",          h.Viernes);
        cmd.Parameters.AddWithValue("@Sabado",           h.Sabado);
        cmd.Parameters.AddWithValue("@Domingo",          h.Domingo);
        cmd.Parameters.AddWithValue("@IntervaloMinutos", h.IntervaloMinutos);
        cmd.Parameters.AddWithValue("@MaxCitas",         h.MaxCitas);
        cmd.Parameters.AddWithValue("@TrabajeFestivos",  h.TrabajeFestivos);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(HorarioAgenda h)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"UPDATE HorarioAgenda SET
                Codigo = @Codigo, Descripcion = @Descripcion,
                HoraInicio = @HoraInicio, HoraFin = @HoraFin,
                FechaInicio = @FechaInicio, FechaFin = @FechaFin,
                Lunes = @Lunes, Martes = @Martes, Miercoles = @Miercoles,
                Jueves = @Jueves, Viernes = @Viernes, Sabado = @Sabado, Domingo = @Domingo,
                IntervaloMinutos = @IntervaloMinutos, MaxCitas = @MaxCitas,
                TrabajeFestivos = @TrabajeFestivos, Estado = @Estado
              WHERE IdHorario = @Id", con);
        cmd.Parameters.AddWithValue("@Codigo",           (object)h.Codigo      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Descripcion",      (object)h.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraInicio",       h.HoraInicio);
        cmd.Parameters.AddWithValue("@HoraFin",          h.HoraFin);
        cmd.Parameters.AddWithValue("@FechaInicio",      h.FechaInicio);
        cmd.Parameters.AddWithValue("@FechaFin",         (object)h.FechaFin    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Lunes",            h.Lunes);
        cmd.Parameters.AddWithValue("@Martes",           h.Martes);
        cmd.Parameters.AddWithValue("@Miercoles",        h.Miercoles);
        cmd.Parameters.AddWithValue("@Jueves",           h.Jueves);
        cmd.Parameters.AddWithValue("@Viernes",          h.Viernes);
        cmd.Parameters.AddWithValue("@Sabado",           h.Sabado);
        cmd.Parameters.AddWithValue("@Domingo",          h.Domingo);
        cmd.Parameters.AddWithValue("@IntervaloMinutos", h.IntervaloMinutos);
        cmd.Parameters.AddWithValue("@MaxCitas",         h.MaxCitas);
        cmd.Parameters.AddWithValue("@TrabajeFestivos",  h.TrabajeFestivos);
        cmd.Parameters.AddWithValue("@Estado",           h.Estado);
        cmd.Parameters.AddWithValue("@Id",               h.IdHorario);
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE HorarioAgenda SET Estado = 'Inactivo' WHERE IdHorario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    private HorarioAgenda Mapear(SqlDataReader r) => new HorarioAgenda
    {
        IdHorario        = (int)r["IdHorario"],
        IdAgenda         = (int)r["IdAgenda"],
        Codigo           = r["Codigo"]      == DBNull.Value ? null : (string)r["Codigo"],
        Descripcion      = r["Descripcion"] == DBNull.Value ? null : (string)r["Descripcion"],
        HoraInicio       = (TimeSpan)r["HoraInicio"],
        HoraFin          = (TimeSpan)r["HoraFin"],
        FechaInicio      = (DateTime)r["FechaInicio"],
        FechaFin         = r["FechaFin"] == DBNull.Value ? null : (DateTime?)r["FechaFin"],
        Lunes            = (bool)r["Lunes"],
        Martes           = (bool)r["Martes"],
        Miercoles        = (bool)r["Miercoles"],
        Jueves           = (bool)r["Jueves"],
        Viernes          = (bool)r["Viernes"],
        Sabado           = (bool)r["Sabado"],
        Domingo          = (bool)r["Domingo"],
        IntervaloMinutos = (int)r["IntervaloMinutos"],
        MaxCitas         = (int)r["MaxCitas"],
        TrabajeFestivos  = (bool)r["TrabajeFestivos"],
        Estado           = (string)r["Estado"]
    };
}