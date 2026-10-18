using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public interface IRecordatorioDAO
{
    int? Reservar(int idTurno, string tipo, string canal, DateTime fechaProgramada, int maxIntentos);
    void MarcarResultado(int idRecordatorio, string estado, string? error, DateTime ahora);
    void RegistrarRespuesta(int idTurno, string tipo, string respuesta, DateTime ahora);
    List<int> TurnosConfirmadosSinRecordar(DateTime desde, DateTime hasta, IReadOnlyList<string> canales, int maxIntentos);
}

public class RecordatorioDAO : IRecordatorioDAO
{
    /// <summary>
    /// Reserva el envío de un aviso. Devuelve el IdRecordatorio a usar si hay que
    /// enviarlo (fila nueva, o una anterior que quedó Pendiente/Error con intentos
    /// disponibles) y null si ya se envió, se omitió o agotó los intentos.
    /// El índice único (IdTurno, Tipo, Canal) evita duplicados aunque haya
    /// dos procesos intentando a la vez.
    /// </summary>
    public int? Reservar(int idTurno, string tipo, string canal, DateTime fechaProgramada, int maxIntentos)
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            var cmd = new SqlCommand(@"
                IF NOT EXISTS (SELECT 1 FROM Recordatorio WHERE IdTurno = @IdTurno AND Tipo = @Tipo AND Canal = @Canal)
                BEGIN
                    INSERT INTO Recordatorio (IdTurno, Tipo, Canal, FechaProgramada)
                    VALUES (@IdTurno, @Tipo, @Canal, @FechaProg);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                END
                ELSE
                    SELECT IdRecordatorio FROM Recordatorio
                    WHERE IdTurno = @IdTurno AND Tipo = @Tipo AND Canal = @Canal
                      AND Estado IN ('Pendiente', 'Error') AND Intentos < @Max;", con);
            cmd.Parameters.AddWithValue("@IdTurno",   idTurno);
            cmd.Parameters.AddWithValue("@Tipo",      tipo);
            cmd.Parameters.AddWithValue("@Canal",     canal);
            cmd.Parameters.AddWithValue("@FechaProg", fechaProgramada);
            cmd.Parameters.AddWithValue("@Max",       maxIntentos);
            var result = cmd.ExecuteScalar();
            return result is null or DBNull ? null : (int)result;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Otro proceso lo reservó primero.
            return null;
        }
    }

    public void MarcarResultado(int idRecordatorio, string estado, string? error, DateTime ahora)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Recordatorio
            SET Estado = @Estado, Error = @Error, Intentos = Intentos + 1,
                FechaEnvio = CASE WHEN @Estado = 'Enviado' THEN @Ahora ELSE FechaEnvio END
            WHERE IdRecordatorio = @Id", con);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@Error",  error is null ? DBNull.Value : error.Length > 1000 ? error[..1000] : error);
        cmd.Parameters.AddWithValue("@Ahora",  ahora);
        cmd.Parameters.AddWithValue("@Id",     idRecordatorio);
        cmd.ExecuteNonQuery();
    }

    // La respuesta se guarda en la fila del canal Chat, que es por donde contesta el paciente.
    public void RegistrarRespuesta(int idTurno, string tipo, string respuesta, DateTime ahora)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Recordatorio SET Respuesta = @Resp, FechaRespuesta = @Ahora
            WHERE IdTurno = @IdTurno AND Tipo = @Tipo AND Canal = 'Chat'", con);
        cmd.Parameters.AddWithValue("@Resp",    respuesta);
        cmd.Parameters.AddWithValue("@Ahora",   ahora);
        cmd.Parameters.AddWithValue("@IdTurno", idTurno);
        cmd.Parameters.AddWithValue("@Tipo",    tipo);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Turnos en estado Confirmado cuyo momento (Fecha + Hora) cae en [desde, hasta]
    /// y a los que todavía les falta algún canal (de <paramref name="canales"/>) del recordatorio Turno24h.
    /// Un canal está resuelto si quedó Enviado u Omitido, o si agotó los intentos.
    /// Solo se cuentan las filas de los canales pedidos: una fila vieja de otro canal (por ejemplo, el "Chat" de cuando los recordatorios
    /// también salían por WhatsApp) no puede hacer pasar por resuelto a un canal que todavía falta.
    /// </summary>
    public List<int> TurnosConfirmadosSinRecordar(DateTime desde, DateTime hasta, IReadOnlyList<string> canales, int maxIntentos)
    {
        var lista = new List<int>();
        if (canales.Count == 0) return lista;   // sin canales no hay nada que mandar

        using var con = Conexion.ObtenerConexion();
        con.Open();
        var nombres = string.Join(", ", canales.Select((_, i) => "@Canal" + i));
        var cmd = new SqlCommand($@"
            SELECT t.IdTurno
            FROM Turno t
            JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            WHERE et.Descripcion = 'Confirmado'
              AND CAST(t.Fecha AS DATETIME) + CAST(t.Hora AS DATETIME) BETWEEN @Desde AND @Hasta
              AND (SELECT COUNT(*) FROM Recordatorio r
                   WHERE r.IdTurno = t.IdTurno AND r.Tipo = 'Turno24h'
                     AND r.Canal IN ({nombres})
                     AND (r.Estado IN ('Enviado', 'Omitido') OR r.Intentos >= @Max)) < @Canales
            ORDER BY t.Fecha, t.Hora", con);
        cmd.Parameters.AddWithValue("@Desde",   desde);
        cmd.Parameters.AddWithValue("@Hasta",   hasta);
        cmd.Parameters.AddWithValue("@Max",     maxIntentos);
        cmd.Parameters.AddWithValue("@Canales", canales.Count);
        for (var i = 0; i < canales.Count; i++) cmd.Parameters.AddWithValue("@Canal" + i, canales[i]);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add((int)r["IdTurno"]);
        return lista;
    }
}
