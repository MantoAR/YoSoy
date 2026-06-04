using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class TurnoDAO
{
    public void Insertar(Turno t)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Turno (IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor) VALUES (@IdPaciente, @IdProfesional, @IdEstadoTurno, @Fecha, @Hora, @Modalidad, @Motivo, @CreadoPor)", con);
        cmd.Parameters.AddWithValue("@IdPaciente",    t.IdPaciente);
        cmd.Parameters.AddWithValue("@IdProfesional", t.IdProfesional);
        cmd.Parameters.AddWithValue("@IdEstadoTurno", t.IdEstadoTurno);
        cmd.Parameters.AddWithValue("@Fecha",         t.Fecha);
        cmd.Parameters.AddWithValue("@Hora",          t.Hora);
        cmd.Parameters.AddWithValue("@Modalidad",     (object?)t.Modalidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Motivo",        (object?)t.Motivo    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreadoPor",     (object?)t.CreadoPor ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Turno t)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Turno SET IdPaciente = @IdPaciente, IdProfesional = @IdProfesional, IdEstadoTurno = @IdEstadoTurno, Fecha = @Fecha, Hora = @Hora, Modalidad = @Modalidad, Motivo = @Motivo WHERE IdTurno = @Id", con);
        cmd.Parameters.AddWithValue("@IdPaciente",    t.IdPaciente);
        cmd.Parameters.AddWithValue("@IdProfesional", t.IdProfesional);
        cmd.Parameters.AddWithValue("@IdEstadoTurno", t.IdEstadoTurno);
        cmd.Parameters.AddWithValue("@Fecha",         t.Fecha);
        cmd.Parameters.AddWithValue("@Hora",          t.Hora);
        cmd.Parameters.AddWithValue("@Modalidad",     (object?)t.Modalidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Motivo",        (object?)t.Motivo    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",            t.IdTurno);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Turno SET IdEstadoTurno = (SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = 'Cancelado') WHERE IdTurno = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Turno> ObtenerTodos()
    {
        var lista = new List<Turno>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdTurno, IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor FROM Turno", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Turno ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdTurno, IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor FROM Turno WHERE IdTurno = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<Turno> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<Turno>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdTurno, IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor FROM Turno WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public List<Turno> ObtenerPorProfesional(int idProfesional)
    {
        var lista = new List<Turno>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdTurno, IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor FROM Turno WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public bool ExisteTurnoMismoDia(int idPaciente, int idProfesional, DateTime fecha)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM Turno WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf AND Fecha = @Fecha AND IdEstadoTurno IN (1, 2)", con);
        cmd.Parameters.AddWithValue("@IdPac",  idPaciente);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Fecha",  fecha.Date);
        return (int)cmd.ExecuteScalar() > 0;
    }

    public bool ExisteTurnoEnHorario(int idProfesional, DateTime fecha, TimeSpan hora)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM Turno WHERE IdProfesional = @IdProf AND Fecha = @Fecha AND Hora = @Hora AND IdEstadoTurno IN (1, 2)", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Fecha",  fecha.Date);
        cmd.Parameters.AddWithValue("@Hora",   hora);
        return (int)cmd.ExecuteScalar() > 0;
    }

    public void CancelarTurnosPacienteProfesional(int idPaciente, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Turno SET IdEstadoTurno = 3 WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf AND IdEstadoTurno IN (1, 2)", con);
        cmd.Parameters.AddWithValue("@IdPac",  idPaciente);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.ExecuteNonQuery();
    }

    private Turno Mapear(SqlDataReader r) => new Turno
    {
        IdTurno       = (int)r["IdTurno"],
        IdPaciente    = (int)r["IdPaciente"],
        IdProfesional = (int)r["IdProfesional"],
        IdEstadoTurno = (int)r["IdEstadoTurno"],
        Fecha         = (DateTime)r["Fecha"],
        Hora          = (TimeSpan)r["Hora"],
        Modalidad     = r["Modalidad"] == DBNull.Value ? null : (string)r["Modalidad"],
        Motivo        = r["Motivo"]    == DBNull.Value ? null : (string)r["Motivo"],
        CreadoPor     = r["CreadoPor"] == DBNull.Value ? null : (string)r["CreadoPor"]
    };
}
