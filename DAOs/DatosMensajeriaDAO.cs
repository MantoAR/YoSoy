using Microsoft.Data.SqlClient;
using System.Collections.Generic;

// Lecturas y escrituras sobre tablas existentes que necesitan el bot y el
// notificador. Consultas propias en vez de modificar los DAOs existentes.
public interface IDatosMensajeria
{
    List<Paciente> PacientesPorTelefono(string telefonoNormalizado);
    Paciente? PacienteActivoPorDni(string dni);
    Paciente? ObtenerPaciente(int idPaciente);
    List<ProfesionalResumen> ProfesionalesDelPaciente(int idPaciente);
    List<Especialidad> EspecialidadesConAgenda();
    List<ProfesionalResumen> ProfesionalesConAgenda(int idEspecialidad);
    List<TurnoAviso> TurnosProximos(int idPaciente, DateTime ahora);
    TurnoAviso? ObtenerTurnoAviso(int idTurno);
    int? BuscarIdTurno(int idPaciente, int idProfesional, DateTime fecha, TimeSpan hora);
    void CrearNotificacionPaciente(int idPaciente, string titulo, string mensaje, string? link);
}

public class DatosMensajeriaDAO : IDatosMensajeria
{
    private const string SELECT_TURNO_AVISO = @"
        SELECT t.IdTurno, t.IdPaciente, pa.Nombre AS NombrePaciente, pa.Telefono AS TelefonoPaciente,
               pa.Mail AS MailPaciente, t.IdProfesional,
               pr.Nombre + ISNULL(' ' + pr.Apellido, '') AS NombreProfesional, pr.Mail AS MailProfesional,
               t.Fecha, t.Hora, t.Modalidad, et.Descripcion AS Estado, t.CodigoSala
        FROM Turno t
        JOIN Paciente pa    ON pa.IdPaciente    = t.IdPaciente
        JOIN Profesional pr ON pr.IdProfesional = t.IdProfesional
        JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno";

    // Paciente.Telefono es texto libre: se normaliza en memoria y se compara
    // por los últimos 10 dígitos (ver Telefono.Normalizar).
    public List<Paciente> PacientesPorTelefono(string telefonoNormalizado)
    {
        var lista = new List<Paciente>();
        if (string.IsNullOrEmpty(telefonoNormalizado)) return lista;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdPaciente, Nombre, Dni, Telefono, Mail, Estado
            FROM Paciente
            WHERE Telefono IS NOT NULL AND ISNULL(Estado, 'Activo') = 'Activo'", con);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var tel = (string)r["Telefono"];
            if (Telefono.Normalizar(tel) != telefonoNormalizado) continue;
            lista.Add(new Paciente
            {
                IdPaciente = (int)r["IdPaciente"],
                Nombre     = (string)r["Nombre"],
                Dni        = (string)r["Dni"],
                Telefono   = tel,
                Mail       = r["Mail"]   == DBNull.Value ? null! : (string)r["Mail"],
                Estado     = r["Estado"] == DBNull.Value ? null! : (string)r["Estado"]
            });
        }
        return lista;
    }

    public Paciente? PacienteActivoPorDni(string dni)
    {
        var p = new PacienteDAO().ObtenerPorDni(dni);
        return p != null && (p.Estado ?? "Activo") == "Activo" ? p : null;
    }

    public Paciente? ObtenerPaciente(int idPaciente) =>
        new PacienteDAO().ObtenerPorId(idPaciente);

    // "Mis profesionales" (PacienteProfesional), solo activos y sin asignación rechazada.
    public List<ProfesionalResumen> ProfesionalesDelPaciente(int idPaciente) =>
        ListarProfesionales(@"
            SELECT p.IdProfesional, p.IdEspecialidad, p.Nombre + ISNULL(' ' + p.Apellido, '') AS Nombre,
                   e.Nombre AS Especialidad, p.Mail, p.Telefono
            FROM PacienteProfesional pp
            JOIN Profesional p  ON p.IdProfesional  = pp.IdProfesional
            JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
            WHERE pp.IdPaciente = @Id AND p.Estado = 'Activo'
              AND ISNULL(pp.EstadoAsignacion, 'Confirmada') <> 'Rechazada'
            ORDER BY e.Nombre, p.Nombre", idPaciente);

    public List<ProfesionalResumen> ProfesionalesConAgenda(int idEspecialidad) =>
        ListarProfesionales(@"
            SELECT p.IdProfesional, p.IdEspecialidad, p.Nombre + ISNULL(' ' + p.Apellido, '') AS Nombre,
                   e.Nombre AS Especialidad, p.Mail, p.Telefono
            FROM Profesional p
            JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
            WHERE p.IdEspecialidad = @Id AND p.Estado = 'Activo'
              AND EXISTS (SELECT 1 FROM Agenda a WHERE a.IdProfesional = p.IdProfesional AND a.Disponible = 1)
            ORDER BY p.Nombre", idEspecialidad);

    public List<Especialidad> EspecialidadesConAgenda()
    {
        var lista = new List<Especialidad>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT e.IdEspecialidad, e.Nombre, e.Estado
            FROM Especialidad e
            WHERE ISNULL(e.Estado, 'Activo') = 'Activo'
              AND EXISTS (SELECT 1 FROM Profesional p
                          JOIN Agenda a ON a.IdProfesional = p.IdProfesional AND a.Disponible = 1
                          WHERE p.IdEspecialidad = e.IdEspecialidad AND p.Estado = 'Activo')
            ORDER BY e.Nombre", con);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new Especialidad
            {
                IdEspecialidad = (int)r["IdEspecialidad"],
                Nombre         = (string)r["Nombre"],
                Estado         = r["Estado"] == DBNull.Value ? null! : (string)r["Estado"]
            });
        return lista;
    }

    public List<TurnoAviso> TurnosProximos(int idPaciente, DateTime ahora)
    {
        var lista = new List<TurnoAviso>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_TURNO_AVISO + @"
            WHERE t.IdPaciente = @IdPac
              AND et.Descripcion IN ('Pendiente', 'Confirmado')
              AND CAST(t.Fecha AS DATETIME) + CAST(t.Hora AS DATETIME) >= @Ahora
            ORDER BY t.Fecha, t.Hora", con);
        cmd.Parameters.AddWithValue("@IdPac", idPaciente);
        cmd.Parameters.AddWithValue("@Ahora", ahora);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(MapearTurno(r));
        return lista;
    }

    public TurnoAviso? ObtenerTurnoAviso(int idTurno)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_TURNO_AVISO + " WHERE t.IdTurno = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idTurno);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapearTurno(r) : null;
    }

    // Para los endpoints que insertan sin devolver el id (POST /turnos/solicitar,
    // POST /turnos/profesional-solicitar): el turno más reciente con esos datos.
    public int? BuscarIdTurno(int idPaciente, int idProfesional, DateTime fecha, TimeSpan hora)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT TOP 1 IdTurno FROM Turno
            WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf AND Fecha = @Fecha AND Hora = @Hora
            ORDER BY IdTurno DESC", con);
        cmd.Parameters.AddWithValue("@IdPac",  idPaciente);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Fecha",  fecha.Date);
        cmd.Parameters.AddWithValue("@Hora",   hora);
        return cmd.ExecuteScalar() as int?;
    }

    // Campanita del paciente: misma tabla e INSERT que ya usan los endpoints existentes.
    public void CrearNotificacionPaciente(int idPaciente, string titulo, string mensaje, string? link)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link)
            VALUES (@IdPac, @Titulo, @Mensaje, @Link)", con);
        cmd.Parameters.AddWithValue("@IdPac",   idPaciente);
        cmd.Parameters.AddWithValue("@Titulo",  titulo.Length  > 200  ? titulo[..200]   : titulo);
        cmd.Parameters.AddWithValue("@Mensaje", mensaje.Length > 1000 ? mensaje[..1000] : mensaje);
        cmd.Parameters.AddWithValue("@Link",    link is null ? DBNull.Value : link.Length > 200 ? link[..200] : link);
        cmd.ExecuteNonQuery();
    }

    private List<ProfesionalResumen> ListarProfesionales(string sql, int id)
    {
        var lista = new List<ProfesionalResumen>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new ProfesionalResumen
            {
                IdProfesional  = (int)r["IdProfesional"],
                IdEspecialidad = (int)r["IdEspecialidad"],
                Nombre         = (string)r["Nombre"],
                Especialidad   = (string)r["Especialidad"],
                Mail           = r["Mail"]     == DBNull.Value ? null : (string)r["Mail"],
                Telefono       = r["Telefono"] == DBNull.Value ? null : (string)r["Telefono"]
            });
        return lista;
    }

    private static TurnoAviso MapearTurno(SqlDataReader r) => new TurnoAviso
    {
        IdTurno           = (int)r["IdTurno"],
        IdPaciente        = (int)r["IdPaciente"],
        NombrePaciente    = (string)r["NombrePaciente"],
        TelefonoPaciente  = r["TelefonoPaciente"] == DBNull.Value ? null : (string)r["TelefonoPaciente"],
        MailPaciente      = r["MailPaciente"]     == DBNull.Value ? null : (string)r["MailPaciente"],
        IdProfesional     = (int)r["IdProfesional"],
        NombreProfesional = (string)r["NombreProfesional"],
        MailProfesional   = r["MailProfesional"]  == DBNull.Value ? null : (string)r["MailProfesional"],
        Fecha             = (DateTime)r["Fecha"],
        Hora              = (TimeSpan)r["Hora"],
        Modalidad         = r["Modalidad"]        == DBNull.Value ? null : (string)r["Modalidad"],
        Estado            = (string)r["Estado"],
        CodigoSala        = r["CodigoSala"]       == DBNull.Value ? null : (string)r["CodigoSala"]
    };
}
