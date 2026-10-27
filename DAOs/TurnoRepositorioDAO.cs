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

    // Propuestas de asignación automática (migración 32). Mientras una propuesta está Pendiente, su horario cuenta como ocupado para cualquier otra reserva. Por defecto no hay ninguna
    // (los dobles de prueba y las bases sin la migración 32 no cambian de comportamiento).
    List<TimeSpan> HorasReservadasPorPropuestas(int idProfesional, DateTime fecha, int? idPacienteAExcluir, DateTime ahora) => new();
    void VencerPropuestas(DateTime ahora) { }

    // Paciente derivado a psiquiatría (migración 34): no puede reservar ni recibir turnos. Por defecto nadie lo está (dobles de prueba y bases sin la migración).
    bool PacienteDerivado(int idPaciente) => false;

    // Solicitudes de turno del paciente que esperan al profesional (migración 36): anotar hasta cuándo pueden responderse y vencer las que pasaron de plazo. Por defecto no hacen nada.
    void RegistrarSolicitud(int idTurno, DateTime ahora) { }
    void VencerSolicitudes(DateTime ahora) { }
}

public class TurnoRepositorioDAO : ITurnoRepositorio
{
    public bool PacienteDerivado(int idPaciente) => DerivacionPsiquiatraDAO.EstaDerivado(idPaciente);
    public void RegistrarSolicitud(int idTurno, DateTime ahora) => SolicitudesTurnoDAO.RegistrarSolicitud(idTurno, ahora);
    public void VencerSolicitudes(DateTime ahora) => SolicitudesTurnoDAO.VencerSolicitudes(ahora);

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
        var cmd = new SqlCommand($@"
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
              {GuardiaPropuestas()}
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

    // Migración 32: un horario con una propuesta de asignación pendiente de OTRO paciente no se puede reservar. Sin la migración no hay nada que mirar (el texto queda vacío).
    // No compara contra el reloj: las propuestas vencidas se dan de baja antes (VencerPropuestas), así que acá cualquier Pendiente cuenta.
    private static string GuardiaPropuestas() => !AsignacionAutomaticaService.Disponible() ? "" : @"
              AND NOT EXISTS (SELECT 1 FROM PropuestaAsignacion pa WHERE pa.IdProfesional = @IdProf AND pa.Estado = N'Pendiente' AND pa.IdPaciente <> @IdPac
                              AND pa.FechaHora = CAST(@Fecha AS DATETIME) + CAST(@Hora AS DATETIME))";

    /// <summary>
    /// Igual que <see cref="InsertarSiLibre"/> pero DENTRO de la transacción de quien llama (con su misma conexión): así el turno, el vínculo y la propuesta aceptada se confirman o se
    /// deshacen juntos. Mismo chequeo (el horario libre y ningún otro turno activo del paciente ese día con ese profesional), con los bloqueos de la transacción.
    /// </summary>
    public static int? InsertarSiLibreEn(SqlConnection con, SqlTransaction tx, Turno t, string creadoPor)
    {
        using var cmd = new SqlCommand($@"
            DECLARE @IdNuevo INT = NULL;
            IF NOT EXISTS (
                SELECT 1
                FROM Turno t WITH (UPDLOCK, HOLDLOCK)
                JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
                WHERE t.IdProfesional = @IdProf AND t.Fecha = @Fecha
                  AND (   (t.Hora = @Hora AND et.Descripcion NOT IN ('Cancelado', 'Rechazado'))
                       OR (t.IdPaciente = @IdPac AND et.Descripcion IN ('Pendiente', 'Confirmado'))))
              {GuardiaPropuestas()}
            BEGIN
                INSERT INTO Turno (IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor)
                VALUES (@IdPac, @IdProf, @IdEstado, @Fecha, @Hora, @Modalidad, @Motivo, @CreadoPor);
                SET @IdNuevo = CAST(SCOPE_IDENTITY() AS INT);
            END
            SELECT @IdNuevo;", con, tx);
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

    public List<TimeSpan> HorasReservadasPorPropuestas(int idProfesional, DateTime fecha, int? idPacienteAExcluir, DateTime ahora)
    {
        var lista = new List<TimeSpan>();
        if (!AsignacionAutomaticaService.Disponible()) return lista;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT FechaHora FROM PropuestaAsignacion
            WHERE IdProfesional = @IdProf AND Estado = N'Pendiente' AND ExpiraEn > @Ahora
              AND FechaHora >= @Fecha AND FechaHora < DATEADD(DAY, 1, @Fecha)
              AND (@Excluir IS NULL OR IdPaciente <> @Excluir)", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Ahora",  ahora);
        cmd.Parameters.AddWithValue("@Fecha",  fecha.Date);
        cmd.Parameters.AddWithValue("@Excluir", (object?)idPacienteAExcluir ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(r.GetDateTime(0).TimeOfDay);
        return lista;
    }

    public void VencerPropuestas(DateTime ahora) => AsignacionAutomaticaService.VencerVencidas(ahora);

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
