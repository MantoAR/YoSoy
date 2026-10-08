using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class AgendaDAO
{
    public int Insertar(Agenda a)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"INSERT INTO Agenda
              (IdProfesional, Dia, HoraInicio, HoraFin, Disponible, IntervaloMinutos)
              VALUES
              (@IdProfesional, @Dia, @HoraInicio, @HoraFin, @Disponible, @Intervalo);
              SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@IdProfesional", a.IdProfesional);
        cmd.Parameters.AddWithValue("@Dia",           (object)a.Dia        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraInicio",    (object)a.HoraInicio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraFin",       (object)a.HoraFin    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Disponible",    a.Disponible);
        cmd.Parameters.AddWithValue("@Intervalo",     a.IntervaloMinutos);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(Agenda a)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"UPDATE Agenda 
              SET IdProfesional    = @IdProfesional,
                  Dia              = @Dia,
                  HoraInicio       = @HoraInicio,
                  HoraFin          = @HoraFin,
                  Disponible       = @Disponible,
                  IntervaloMinutos = @Intervalo
              WHERE IdAgenda = @Id", con);
        cmd.Parameters.AddWithValue("@IdProfesional", a.IdProfesional);
        cmd.Parameters.AddWithValue("@Dia",           (object)a.Dia        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraInicio",    (object)a.HoraInicio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HoraFin",       (object)a.HoraFin    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Disponible",    a.Disponible);
        cmd.Parameters.AddWithValue("@Intervalo",     a.IntervaloMinutos);
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
        var cmd = new SqlCommand(
            "SELECT IdAgenda, IdProfesional, Dia, HoraInicio, HoraFin, Disponible, IntervaloMinutos FROM Agenda", con);
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
            "SELECT IdAgenda, IdProfesional, Dia, HoraInicio, HoraFin, Disponible, IntervaloMinutos FROM Agenda WHERE IdProfesional = @Id", con);
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
            "SELECT IdAgenda, IdProfesional, Dia, HoraInicio, HoraFin, Disponible, IntervaloMinutos FROM Agenda WHERE IdAgenda = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private Agenda Mapear(SqlDataReader r) => new Agenda
    {
        IdAgenda         = (int)r["IdAgenda"],
        IdProfesional    = (int)r["IdProfesional"],
        Dia              = r["Dia"]        == DBNull.Value ? null : (string)r["Dia"],
        HoraInicio       = r["HoraInicio"] == DBNull.Value ? null : (TimeSpan?)r["HoraInicio"],
        HoraFin          = r["HoraFin"]   == DBNull.Value ? null : (TimeSpan?)r["HoraFin"],
        Disponible       = (bool)r["Disponible"],
        IntervaloMinutos = r["IntervaloMinutos"] == DBNull.Value ? 30 : (int)r["IntervaloMinutos"]
    };
}
