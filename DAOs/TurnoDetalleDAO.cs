using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class TurnoDetalleDAO
{
    private const string SELECT_BASE = @"
        SELECT t.IdTurno, t.IdPaciente, t.IdProfesional,
               p.Nombre + ISNULL(' ' + p.Apellido, '') AS NombreProfesional,
               e.Nombre AS Especialidad,
               t.IdEstadoTurno, et.Descripcion AS EstadoDescripcion,
               t.Fecha, t.Hora, t.Modalidad, t.Motivo,
               p.Mail AS MailProfesional, p.Telefono AS TelefonoProfesional
        FROM Turno t
        JOIN Profesional p  ON t.IdProfesional  = p.IdProfesional
        JOIN Especialidad e ON p.IdEspecialidad  = e.IdEspecialidad
        JOIN EstadoTurno et ON t.IdEstadoTurno   = et.IdEstadoTurno";

    public List<TurnoDetalle> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<TurnoDetalle>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_BASE + " WHERE t.IdPaciente = @Id ORDER BY t.Fecha, t.Hora", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    public TurnoDetalle? ObtenerProximoPorPaciente(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_BASE + @"
            WHERE t.IdPaciente = @Id
              AND t.Fecha >= CAST(GETDATE() AS DATE)
              AND et.Descripcion != 'Cancelado'
            ORDER BY t.Fecha ASC, t.Hora ASC
            OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public List<ProfesionalResumen> ObtenerProfesionalesPorPaciente(int idPaciente)
    {
        var lista = new List<ProfesionalResumen>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT DISTINCT p.IdProfesional, p.IdEspecialidad,
                   p.Nombre + ISNULL(' ' + p.Apellido, '') AS Nombre,
                   e.Nombre AS Especialidad,
                   p.Mail, p.Telefono
            FROM Turno t
            JOIN Profesional p  ON t.IdProfesional = p.IdProfesional
            JOIN Especialidad e ON p.IdEspecialidad = e.IdEspecialidad
            JOIN EstadoTurno et ON t.IdEstadoTurno  = et.IdEstadoTurno
            WHERE t.IdPaciente = @Id
              AND t.Fecha >= CAST(GETDATE() AS DATE)
              AND et.Descripcion NOT IN ('Cancelado', 'Realizado')", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
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

    private TurnoDetalle Mapear(SqlDataReader r) => new TurnoDetalle
    {
        IdTurno            = (int)r["IdTurno"],
        IdPaciente         = (int)r["IdPaciente"],
        IdProfesional      = (int)r["IdProfesional"],
        NombreProfesional  = (string)r["NombreProfesional"],
        Especialidad       = (string)r["Especialidad"],
        IdEstadoTurno      = (int)r["IdEstadoTurno"],
        EstadoDescripcion  = (string)r["EstadoDescripcion"],
        Fecha              = (DateTime)r["Fecha"],
        Hora               = (TimeSpan)r["Hora"],
        Modalidad          = r["Modalidad"]        == DBNull.Value ? null : (string)r["Modalidad"],
        Motivo             = r["Motivo"]            == DBNull.Value ? null : (string)r["Motivo"],
        MailProfesional    = r["MailProfesional"]   == DBNull.Value ? null : (string)r["MailProfesional"],
        TelefonoProfesional= r["TelefonoProfesional"]==DBNull.Value ? null : (string)r["TelefonoProfesional"]
    };
}
