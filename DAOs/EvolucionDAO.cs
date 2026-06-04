using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class EvolucionDAO
{
    public void Insertar(Evolucion e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Evolucion (IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento) VALUES (@IdHistoria, @IdProfesional, @IdTurno, @Fecha, @Diagnostico, @Observaciones, @PlanTratamiento)", con);
        cmd.Parameters.AddWithValue("@IdHistoria",      e.IdHistoria);
        cmd.Parameters.AddWithValue("@IdProfesional",   e.IdProfesional);
        cmd.Parameters.AddWithValue("@IdTurno",         (object)e.IdTurno        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Fecha",           e.Fecha);
        cmd.Parameters.AddWithValue("@Diagnostico",     (object)e.Diagnostico    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Observaciones",   (object)e.Observaciones  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlanTratamiento", (object)e.PlanTratamiento ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Evolucion e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Evolucion SET IdHistoria = @IdHistoria, IdProfesional = @IdProfesional, IdTurno = @IdTurno, Fecha = @Fecha, Diagnostico = @Diagnostico, Observaciones = @Observaciones, PlanTratamiento = @PlanTratamiento WHERE IdEvolucion = @Id", con);
        cmd.Parameters.AddWithValue("@IdHistoria",      e.IdHistoria);
        cmd.Parameters.AddWithValue("@IdProfesional",   e.IdProfesional);
        cmd.Parameters.AddWithValue("@IdTurno",         (object)e.IdTurno        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Fecha",           e.Fecha);
        cmd.Parameters.AddWithValue("@Diagnostico",     (object)e.Diagnostico    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Observaciones",   (object)e.Observaciones  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlanTratamiento", (object)e.PlanTratamiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",              e.IdEvolucion);
        cmd.ExecuteNonQuery();
    }

    public List<Evolucion> ObtenerTodos()
    {
        var lista = new List<Evolucion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEvolucion, IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento FROM Evolucion", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Evolucion ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEvolucion, IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento FROM Evolucion WHERE IdEvolucion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<Evolucion> ObtenerPorHistoria(int idHistoria)
    {
        var lista = new List<Evolucion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEvolucion, IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento FROM Evolucion WHERE IdHistoria = @Id ORDER BY Fecha DESC", con);
        cmd.Parameters.AddWithValue("@Id", idHistoria);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private Evolucion Mapear(SqlDataReader r) => new Evolucion
    {
        IdEvolucion     = (int)r["IdEvolucion"],
        IdHistoria      = (int)r["IdHistoria"],
        IdProfesional   = (int)r["IdProfesional"],
        IdTurno         = r["IdTurno"]         == DBNull.Value ? null : (int?)r["IdTurno"],
        Fecha           = (DateTime)r["Fecha"],
        Diagnostico     = r["Diagnostico"]     == DBNull.Value ? null : (string)r["Diagnostico"],
        Observaciones   = r["Observaciones"]   == DBNull.Value ? null : (string)r["Observaciones"],
        PlanTratamiento = r["PlanTratamiento"] == DBNull.Value ? null : (string)r["PlanTratamiento"]
    };
}
