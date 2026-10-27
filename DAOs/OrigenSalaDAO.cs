using Microsoft.Data.SqlClient;

// Lectura de turnos e informes de admisión para decidir el acceso a su sala de video (solo SELECT).
public class OrigenSalaDAO : IOrigenSalaRepositorio
{
    public TurnoParaSala? ObtenerTurno(int idTurno)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT t.IdTurno, t.IdPaciente, t.IdProfesional, et.Descripcion AS Estado, t.Fecha, t.Hora, t.CodigoSala
            FROM Turno t
            JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdTurno = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idTurno);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var fecha = (DateTime)r["Fecha"];
        var hora = (TimeSpan)r["Hora"];
        var idProfesional = (int)r["IdProfesional"];
        var turno = new TurnoParaSala(
            (int)r["IdTurno"], (int)r["IdPaciente"], idProfesional, (string)r["Estado"],
            fecha, hora, r["CodigoSala"] == DBNull.Value ? null : (string)r["CodigoSala"], null);
        r.Close();

        return turno with { DuracionMinutos = DuracionDeLaFranja(idProfesional, fecha, hora) };
    }

    // Los próximos turnos Confirmados de UNA persona (se pasa el id del paciente o el del profesional, nunca los dos sin filtro: sin ninguno devuelve vacío). Tope de 10: alcanza para
    // elegir el turno entrable de hoy y mostrar el próximo.
    public IReadOnlyList<TurnoConNombres> TurnosConfirmadosDe(int? idPaciente, int? idProfesional, DateTime desde)
    {
        if (idPaciente is null && idProfesional is null) return Array.Empty<TurnoConNombres>();
        var filas = new List<(TurnoParaSala Turno, string Paciente, string Profesional)>();
        using (var con = Conexion.ObtenerConexion())
        {
            con.Open();
            var cmd = new SqlCommand($@"
                SELECT TOP 10 t.IdTurno, t.IdPaciente, t.IdProfesional, et.Descripcion AS Estado, t.Fecha, t.Hora, t.CodigoSala,
                       pa.Nombre AS NombrePaciente, p.Nombre + ISNULL(' ' + p.Apellido, '') AS NombreProfesional
                FROM Turno t
                JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
                JOIN Paciente pa ON pa.IdPaciente = t.IdPaciente
                JOIN Profesional p ON p.IdProfesional = t.IdProfesional
                WHERE et.Descripcion = N'Confirmado' AND t.Fecha >= @Desde
                  AND {(idPaciente is not null ? "t.IdPaciente = @Persona" : "t.IdProfesional = @Persona")}
                ORDER BY t.Fecha, t.Hora", con);
            cmd.Parameters.AddWithValue("@Desde", desde.Date);
            cmd.Parameters.AddWithValue("@Persona", (idPaciente ?? idProfesional)!.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                filas.Add((new TurnoParaSala((int)r["IdTurno"], (int)r["IdPaciente"], (int)r["IdProfesional"], (string)r["Estado"],
                    (DateTime)r["Fecha"], (TimeSpan)r["Hora"], r["CodigoSala"] == DBNull.Value ? null : (string)r["CodigoSala"], null),
                    r["NombrePaciente"] == DBNull.Value ? "" : (string)r["NombrePaciente"], (string)r["NombreProfesional"]));
        }
        return filas.Select(f => new TurnoConNombres(f.Turno with { DuracionMinutos = DuracionDeLaFranja(f.Turno.IdProfesional, f.Turno.Fecha, f.Turno.Hora) }, f.Paciente, f.Profesional)).ToList();
    }

    public AdmisionParaSala? ObtenerAdmision(int idInforme)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdInforme, IdPaciente, Estado, FechaEntrevista, CodigoSala FROM InformeAdmision WHERE IdInforme = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idInforme);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new AdmisionParaSala(
            (int)r["IdInforme"], (int)r["IdPaciente"],
            r["Estado"] == DBNull.Value ? null : (string)r["Estado"],
            r["FechaEntrevista"] == DBNull.Value ? null : (DateTime)r["FechaEntrevista"],
            r["CodigoSala"] == DBNull.Value ? null : (string)r["CodigoSala"]);
    }

    // El turno no guarda su duración: es el intervalo de la franja de la agenda del profesional que
    // contiene su hora (la misma cuenta con la que se generan los horarios, ver TurnoReglasService.Slots).
    // Agenda.Dia es texto ('lunes', 'miércoles'...), por eso se compara en C# y no en SQL.
    private static int? DuracionDeLaFranja(int idProfesional, DateTime fecha, TimeSpan hora)
    {
        var franja = new AgendaDAO().ObtenerPorProfesional(idProfesional).FirstOrDefault(a =>
            TurnoReglasService.DiaDeLaSemana(a.Dia) == fecha.DayOfWeek
            && a.HoraInicio is not null && a.HoraFin is not null
            && a.HoraInicio <= hora && hora < a.HoraFin);
        return franja is { IntervaloMinutos: > 0 } ? franja.IntervaloMinutos : null;
    }
}
