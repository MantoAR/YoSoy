using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class AgendaDAO
{
    public void Insertar(Agenda a)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Agenda (IdProfesional, Dia, HoraInicio, HoraFin, Disponible, Codigo, Descripcion, FechaInicio, FechaFin, IdServicio) VALUES (@IdProfesional, @Dia, @HoraInicio, @HoraFin, @Disponible, @Codigo, @Descripcion, @FechaInicio, @FechaFin, @IdServicio)", con);
        cmd.Parameters.AddWithValue("@IdProfesional", a.IdProfesional);
        cmd.Parameters.AddWithValue("@Dia",           (object)a.Dia        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraInicio",    (object)a.HoraInicio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraFin",       (object)a.HoraFin    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Disponible",    a.Disponible);
        cmd.Parameters.AddWithValue("@Codigo",        (object)a.Codigo      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Descripcion",   (object)a.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaInicio",   (object)a.FechaInicio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaFin",      (object)a.FechaFin    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdServicio",    (object)a.IdServicio  ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Agenda a)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Agenda SET IdProfesional=@IdProfesional, Dia=@Dia, HoraInicio=@HoraInicio, HoraFin=@HoraFin, Disponible=@Disponible, Codigo=@Codigo, Descripcion=@Descripcion, FechaInicio=@FechaInicio, FechaFin=@FechaFin, IdServicio=@IdServicio WHERE IdAgenda=@Id", con);
        cmd.Parameters.AddWithValue("@IdProfesional", a.IdProfesional);
        cmd.Parameters.AddWithValue("@Dia",           (object)a.Dia        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraInicio",    (object)a.HoraInicio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraFin",       (object)a.HoraFin    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Disponible",    a.Disponible);
        cmd.Parameters.AddWithValue("@Codigo",        (object)a.Codigo      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Descripcion",   (object)a.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaInicio",   (object)a.FechaInicio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaFin",      (object)a.FechaFin    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdServicio",    (object)a.IdServicio  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",            a.IdAgenda);
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("DELETE FROM Agenda WHERE IdAgenda = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Agenda> ObtenerTodos()
    {
        var lista = new List<Agenda>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdAgenda, IdProfesional, Dia, HoraInicio, HoraFin, Disponible, Codigo, Descripcion, FechaInicio, FechaFin, IdServicio FROM Agenda", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public List<Agenda> ObtenerPorProfesional(int idProfesional)
    {
        var lista = new List<Agenda>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdAgenda, IdProfesional, Dia, HoraInicio, HoraFin, Disponible, Codigo, Descripcion, FechaInicio, FechaFin, IdServicio FROM Agenda WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Agenda ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdAgenda, IdProfesional, Dia, HoraInicio, HoraFin, Disponible, Codigo, Descripcion, FechaInicio, FechaFin, IdServicio FROM Agenda WHERE IdAgenda = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private Agenda Mapear(SqlDataReader r) => new Agenda
    {
        IdAgenda      = (int)r["IdAgenda"],
        IdProfesional = (int)r["IdProfesional"],
        Dia           = r["Dia"]        == DBNull.Value ? null : (string)r["Dia"],
        HoraInicio    = r["HoraInicio"] == DBNull.Value ? null : (TimeSpan?)r["HoraInicio"],
        HoraFin       = r["HoraFin"]    == DBNull.Value ? null : (TimeSpan?)r["HoraFin"],
        Disponible    = (bool)r["Disponible"],
        Codigo        = r["Codigo"]      == DBNull.Value ? null : (string)r["Codigo"],
        Descripcion   = r["Descripcion"] == DBNull.Value ? null : (string)r["Descripcion"],
        FechaInicio   = r["FechaInicio"] == DBNull.Value ? null : (DateTime?)r["FechaInicio"],
        FechaFin      = r["FechaFin"]    == DBNull.Value ? null : (DateTime?)r["FechaFin"],
        IdServicio    = r["IdServicio"]  == DBNull.Value ? null : (int?)r["IdServicio"]
    };
}