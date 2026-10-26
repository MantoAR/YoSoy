using Microsoft.Data.SqlClient;

// Junta los datos reales del paciente para calcular sus primeros pasos (Services/PrimerosPasos.cs). Solo lee.
public class PrimerosPasosDAO
{
    public DatosPrimerosPasos Cargar(int idPaciente, EstadoTratamiento consentimiento)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();

        // El formulario más reciente: es el que vale (un paciente devuelto lo completa de nuevo sobre el mismo informe).
        InformePaso? informe = null;
        using (var cmd = new SqlCommand(@"
            SELECT TOP 1 Estado, AclaracionRecepcion, MotivoRechazo
            FROM InformeAdmision WHERE IdPaciente = @P ORDER BY IdInforme DESC", con))
        {
            cmd.Parameters.AddWithValue("@P", idPaciente);
            using var r = cmd.ExecuteReader();
            if (r.Read())
                informe = new InformePaso((string)r["Estado"], r["AclaracionRecepcion"] as string, r["MotivoRechazo"] as string);
        }

        // El profesional vigente: el vínculo más reciente que no fue rechazado ni cancelado (la misma regla que usa el resto del sistema).
        ProfesionalPaso? profesional = null;
        using (var cmd = new SqlCommand(@"
            SELECT TOP 1 p.IdProfesional, LTRIM(RTRIM(p.Nombre + ISNULL(' ' + p.Apellido, ''))) AS Nombre, e.Nombre AS Especialidad
            FROM PacienteProfesional pp
            JOIN Profesional p ON p.IdProfesional = pp.IdProfesional
            LEFT JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
            WHERE pp.IdPaciente = @P AND ISNULL(pp.EstadoAsignacion, 'Confirmada') NOT IN ('Rechazada', 'Cancelada')
            ORDER BY pp.FechaAsignacion DESC, pp.IdPacienteProfesional DESC", con))
        {
            cmd.Parameters.AddWithValue("@P", idPaciente);
            using var r = cmd.ExecuteReader();
            if (r.Read())
                profesional = new ProfesionalPaso((int)r["IdProfesional"], (string)r["Nombre"], r["Especialidad"] as string);
        }

        var turnos = new List<TurnoPaso>();
        using (var cmd = new SqlCommand(@"
            SELECT t.IdTurno, t.Fecha, t.Hora, et.Descripcion AS Estado, t.ConfirmadoPaciente, t.CreadoPor, t.CodigoSala
            FROM Turno t
            JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdPaciente = @P", con))
        {
            cmd.Parameters.AddWithValue("@P", idPaciente);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                turnos.Add(new TurnoPaso((int)r["IdTurno"], ((DateTime)r["Fecha"]).Date + (TimeSpan)r["Hora"], (string)r["Estado"],
                    (bool)r["ConfirmadoPaciente"], r["CreadoPor"] as string, r["CodigoSala"] as string));
        }
        return new DatosPrimerosPasos(consentimiento, informe, profesional, turnos);
    }
}
