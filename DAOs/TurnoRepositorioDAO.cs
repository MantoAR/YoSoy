using Microsoft.Data.SqlClient;
using System.Collections.Generic;

// Lo que necesita TurnoReglasService de la base. Los estados se resuelven
// siempre por Descripcion, nunca por IdEstadoTurno fijo.
public interface ITurnoRepositorio
{
    List<Agenda> AgendaDe(int idProfesional);
    List<TimeSpan> HorasOcupadas(int idProfesional, DateTime fecha);
    // Horas de las entrevistas de admisión que ese profesional ya programó ese día (migración 26).
    List<TimeSpan> EntrevistasDeAdmision(int idProfesional, DateTime fecha);
    int ContarTurnosActivos(int idPaciente, int idProfesional, DateTime fecha);
    bool TieneConsentimiento(int idPaciente, string descripcionTipo, string? version = null);
    Turno? ObtenerTurno(int idTurno);
    string? DescripcionEstado(int idEstadoTurno);
    int? IdEstado(string descripcion);
    int? InsertarSiLibre(Turno t, string creadoPor);
    void CambiarEstado(int idTurno, int idEstadoTurno);
}

public class TurnoRepositorioDAO : ITurnoRepositorio
{
    public List<Agenda> AgendaDe(int idProfesional) =>
        new AgendaDAO().ObtenerPorProfesional(idProfesional);

    public Turno? ObtenerTurno(int idTurno) =>
        new TurnoDAO().ObtenerPorId(idTurno);

    // Mismo criterio que TurnoDAO.ObtenerOcupadosPorFecha (el que usa la web).
    public List<TimeSpan> HorasOcupadas(int idProfesional, DateTime fecha)
    {
        var lista = new List<TimeSpan>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT t.Hora
            FROM Turno t
            JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdProfesional = @IdProf AND t.Fecha = @Fecha
              AND et.Descripcion NOT IN ('Cancelado', 'Rechazado')", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Fecha",  fecha.Date);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add((TimeSpan)r["Hora"]);
        return lista;
    }

    public List<TimeSpan> EntrevistasDeAdmision(int idProfesional, DateTime fecha)
    {
        var lista = new List<TimeSpan>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT FechaEntrevista
            FROM InformeAdmision
            WHERE IdProfesionalEntrevista = @IdProf AND Estado = 'En entrevista'
              AND FechaEntrevista >= @Fecha AND FechaEntrevista < DATEADD(DAY, 1, @Fecha)", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Fecha",  fecha.Date);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(((DateTime)r["FechaEntrevista"]).TimeOfDay);
        return lista;
    }

    public int ContarTurnosActivos(int idPaciente, int idProfesional, DateTime fecha)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT COUNT(*)
            FROM Turno t
            JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdPaciente = @IdPac AND t.IdProfesional = @IdProf AND t.Fecha = @Fecha
              AND et.Descripcion IN ('Pendiente', 'Confirmado')", con);
        cmd.Parameters.AddWithValue("@IdPac",  idPaciente);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Fecha",  fecha.Date);
        return (int)cmd.ExecuteScalar();
    }

    // LA misma decisión que la página de firma y que el requisito de asignar un profesional (ConsentimientoTratamiento).
    public bool TieneConsentimiento(int idPaciente, string descripcionTipo, string? version = null) =>
        ConsentimientoTratamiento.Estado(idPaciente, descripcionTipo, version).Vigente;

    public string? DescripcionEstado(int idEstadoTurno)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT Descripcion FROM EstadoTurno WHERE IdEstadoTurno = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idEstadoTurno);
        return cmd.ExecuteScalar() as string;
    }

    public int? IdEstado(string descripcion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = @Desc", con);
        cmd.Parameters.AddWithValue("@Desc", descripcion);
        return cmd.ExecuteScalar() as int?;
    }

    /// <summary>
    /// Inserta el turno solo si, en el mismo momento, el horario sigue libre y el
    /// paciente no tiene otro turno activo con ese profesional ese día. Chequeo e
    /// inserción van en una transacción con UPDLOCK/HOLDLOCK para que dos pedidos
    /// simultáneos no terminen con el mismo horario. Devuelve null si ya no se pudo.
    /// </summary>
    public int? InsertarSiLibre(Turno t, string creadoPor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DECLARE @IdNuevo INT = NULL;
            IF NOT EXISTS (
                SELECT 1
                FROM Turno t WITH (UPDLOCK, HOLDLOCK)
                JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
                WHERE t.IdProfesional = @IdProf AND t.Fecha = @Fecha
                  AND (   (t.Hora = @Hora AND et.Descripcion NOT IN ('Cancelado', 'Rechazado'))
                       OR (t.IdPaciente = @IdPac AND et.Descripcion IN ('Pendiente', 'Confirmado'))))
            BEGIN
                INSERT INTO Turno (IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor)
                VALUES (@IdPac, @IdProf, @IdEstado, @Fecha, @Hora, @Modalidad, @Motivo, @CreadoPor);
                SET @IdNuevo = CAST(SCOPE_IDENTITY() AS INT);
            END
            COMMIT;
            SELECT @IdNuevo;", con);
        cmd.Parameters.AddWithValue("@IdPac",     t.IdPaciente);
        cmd.Parameters.AddWithValue("@IdProf",    t.IdProfesional);
        cmd.Parameters.AddWithValue("@IdEstado",  t.IdEstadoTurno);
        cmd.Parameters.AddWithValue("@Fecha",     t.Fecha.Date);
        cmd.Parameters.AddWithValue("@Hora",      t.Hora);
        cmd.Parameters.AddWithValue("@Modalidad", (object?)t.Modalidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Motivo",    (object?)t.Motivo    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreadoPor", creadoPor);
        return cmd.ExecuteScalar() as int?;
    }

    public void CambiarEstado(int idTurno, int idEstadoTurno)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE Turno SET IdEstadoTurno = @IdEstado WHERE IdTurno = @Id", con);
        cmd.Parameters.AddWithValue("@IdEstado", idEstadoTurno);
        cmd.Parameters.AddWithValue("@Id",       idTurno);
        cmd.ExecuteNonQuery();
    }
}
