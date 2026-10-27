using System.Data;
using Microsoft.Data.SqlClient;

// Llegada tarde, inasistencia y abandono (rama AAA1, migración 37). Ver Services/AsistenciaReglas.cs para las reglas y
// migrations/add_asistencia_abandono.sql para las tablas. Todas las fechas de Turno son de la clínica (hora local);
// las de AceptacionVideollamada, SeguimientoAsistencia y AbandonoTratamiento son UTC.
public record TurnoAsistencia(int IdTurno, int IdPaciente, int IdProfesional, DateTime Inicio, string? Modalidad, string Estado,
    bool? Cobrable, string? MotivoNoCobrable, string Paciente, string? Telefono, string Profesional, DateTime? IngresoUtc);

public record TurnoPasado(int IdPaciente, int IdProfesional, DateTime Inicio, string Estado, string Paciente, string? Telefono, string Profesional);

public record SeguimientoResumen(int IdPaciente, int? IdProfesional, string Resultado, string? Nota, DateTime FechaUtc);

public record ResultadoAbandono(int IdAbandono, int TurnosCancelados);

public class AsistenciaDAO
{
    private const int TablaInexistente = 208, ColumnaInexistente = 207;

    // ─────────────────────────────── Parámetros ───────────────────────────────
    public ParametrosAsistencia Parametros()
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT Clave, Valor FROM ConfiguracionOrganizacion WHERE Clave LIKE N'Asistencia.%'", con);
            var filas = new Dictionary<string, string?>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) filas[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
            return ParametrosAsistencia.Desde(filas);
        }
        catch (SqlException ex) when (ex.Number == TablaInexistente) { return ParametrosAsistencia.PorDefecto; }
    }

    public void GuardarParametros(ParametrosAsistencia p, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        foreach (var (clave, valor) in p.ComoFilas())
        {
            using var cmd = new SqlCommand(@"
                MERGE ConfiguracionOrganizacion WITH (HOLDLOCK) AS t
                USING (SELECT @K AS Clave) AS s ON t.Clave = s.Clave
                WHEN MATCHED THEN UPDATE SET Valor = @V, FechaModificacion = SYSDATETIME(), IdUsuarioModifico = @U
                WHEN NOT MATCHED THEN INSERT (Clave, Valor, IdUsuarioModifico) VALUES (@K, @V, @U);", con, tx);
            cmd.Parameters.Add("@K", SqlDbType.NVarChar, 100).Value = clave;
            cmd.Parameters.Add("@V", SqlDbType.NVarChar, 10).Value = valor;
            cmd.Parameters.Add("@U", SqlDbType.Int).Value = idUsuario;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ─────────────────────────────── Turnos ───────────────────────────────
    private const string SelectTurno = @"
        SELECT t.IdTurno, t.IdPaciente, t.IdProfesional, t.Fecha, t.Hora, t.Modalidad, e.Descripcion AS Estado,
               t.Cobrable, t.MotivoNoCobrable, pa.Nombre AS Paciente, pa.Telefono,
               LTRIM(RTRIM(pr.Nombre + N' ' + ISNULL(pr.Apellido, N''))) AS Profesional,
               (SELECT MIN(a.DecididoUtc) FROM AceptacionVideollamada a
                 WHERE a.Origen = N'turno' AND a.IdOrigen = t.IdTurno AND a.IdPaciente = t.IdPaciente) AS IngresoUtc
        FROM Turno t
        JOIN EstadoTurno e  ON e.IdEstadoTurno = t.IdEstadoTurno
        JOIN Paciente pa    ON pa.IdPaciente = t.IdPaciente
        JOIN Profesional pr ON pr.IdProfesional = t.IdProfesional";

    private static TurnoAsistencia LeerTurno(SqlDataReader r) => new(
        r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetDateTime(3).Date + r.GetTimeSpan(4),
        r.IsDBNull(5) ? null : r.GetString(5), r.GetString(6),
        r.IsDBNull(7) ? null : r.GetBoolean(7), r.IsDBNull(8) ? null : r.GetString(8),
        r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.GetString(11),
        r.IsDBNull(12) ? null : DateTime.SpecifyKind(r.GetDateTime(12), DateTimeKind.Utc));

    /// <summary>Los turnos entre dos fechas (inclusive), de un profesional o de todos.</summary>
    public List<TurnoAsistencia> Turnos(DateTime desde, DateTime hasta, int? idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(SelectTurno + @"
            WHERE t.Fecha BETWEEN @D AND @H AND (@Prof IS NULL OR t.IdProfesional = @Prof)
            ORDER BY t.Fecha, t.Hora, t.IdTurno;", con);
        cmd.Parameters.Add("@D", SqlDbType.Date).Value = desde.Date;
        cmd.Parameters.Add("@H", SqlDbType.Date).Value = hasta.Date;
        cmd.Parameters.Add("@Prof", SqlDbType.Int).Value = (object?)idProfesional ?? DBNull.Value;
        var lista = new List<TurnoAsistencia>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(LeerTurno(r));
        return lista;
    }

    public TurnoAsistencia? Turno(int idTurno)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(SelectTurno + " WHERE t.IdTurno = @T;", con);
        cmd.Parameters.Add("@T", SqlDbType.Int).Value = idTurno;
        using var r = cmd.ExecuteReader();
        return r.Read() ? LeerTurno(r) : null;
    }

    /// <summary>Confirmado → Ausente, solo si sigue Confirmado (si dos personas lo marcan a la vez, gana una). Queda cobrable.</summary>
    public bool MarcarAusente(int idTurno, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            UPDATE t SET IdEstadoTurno = a.IdEstadoTurno, Cobrable = 1, MotivoNoCobrable = NULL,
                         IdUsuarioAsistencia = @U, FechaAsistenciaUtc = SYSUTCDATETIME()
            FROM Turno t
            JOIN EstadoTurno c ON c.IdEstadoTurno = t.IdEstadoTurno AND c.Descripcion = N'Confirmado'
            CROSS JOIN (SELECT TOP 1 IdEstadoTurno FROM EstadoTurno WHERE Descripcion = N'Ausente') a
            WHERE t.IdTurno = @T;", con);
        cmd.Parameters.Add("@T", SqlDbType.Int).Value = idTurno;
        cmd.Parameters.Add("@U", SqlDbType.Int).Value = idUsuario;
        return cmd.ExecuteNonQuery() == 1;
    }

    /// <summary>Ausente → Confirmado (corrección de un error): se borra también la marca de cobro.</summary>
    public bool DeshacerAusente(int idTurno, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            UPDATE t SET IdEstadoTurno = c.IdEstadoTurno, Cobrable = NULL, MotivoNoCobrable = NULL,
                         IdUsuarioAsistencia = @U, FechaAsistenciaUtc = SYSUTCDATETIME()
            FROM Turno t
            JOIN EstadoTurno a ON a.IdEstadoTurno = t.IdEstadoTurno AND a.Descripcion = N'Ausente'
            CROSS JOIN (SELECT TOP 1 IdEstadoTurno FROM EstadoTurno WHERE Descripcion = N'Confirmado') c
            WHERE t.IdTurno = @T;", con);
        cmd.Parameters.Add("@T", SqlDbType.Int).Value = idTurno;
        cmd.Parameters.Add("@U", SqlDbType.Int).Value = idUsuario;
        return cmd.ExecuteNonQuery() == 1;
    }

    /// <summary>
    /// Deja la marca de una cancelación ya hecha: quién y cuándo, y si es cobrable (null = no corresponde, por ejemplo cuando
    /// cancela el profesional). Sin la migración 37 no hace nada: la cancelación anda igual que antes.
    /// </summary>
    public void RegistrarCancelacion(int idTurno, bool? cobrable, int? idUsuario)
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand(@"UPDATE Turno SET Cobrable = @C, MotivoNoCobrable = NULL, IdUsuarioAsistencia = @U,
                FechaAsistenciaUtc = SYSUTCDATETIME() WHERE IdTurno = @T;", con);
            cmd.Parameters.Add("@T", SqlDbType.Int).Value = idTurno;
            cmd.Parameters.Add("@C", SqlDbType.Bit).Value = (object?)cobrable ?? DBNull.Value;
            cmd.Parameters.Add("@U", SqlDbType.Int).Value = (object?)idUsuario ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
        catch (SqlException ex) when (ex.Number == ColumnaInexistente) { /* falta la migración 37 */ }
    }

    /// <summary>El profesional decide si el turno se cobra (fuerza mayor = sin cargo, con el motivo).</summary>
    public void MarcarCobro(int idTurno, bool cobrable, string? motivo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("UPDATE Turno SET Cobrable = @C, MotivoNoCobrable = @M WHERE IdTurno = @T;", con);
        cmd.Parameters.Add("@T", SqlDbType.Int).Value = idTurno;
        cmd.Parameters.Add("@C", SqlDbType.Bit).Value = cobrable;
        cmd.Parameters.Add("@M", SqlDbType.NVarChar, 300).Value = cobrable ? DBNull.Value : (object?)motivo ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    // ─────────────────────────────── Ausencias seguidas ───────────────────────────────
    /// <summary>Los turnos ya pasados de los últimos meses (del más reciente al más viejo), filtrables por profesional y/o paciente.</summary>
    public List<TurnoPasado> TurnosPasados(DateTime ahora, int meses, int? idProfesional, int? idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT t.IdPaciente, t.IdProfesional, t.Fecha, t.Hora, e.Descripcion, pa.Nombre, pa.Telefono,
                   LTRIM(RTRIM(pr.Nombre + N' ' + ISNULL(pr.Apellido, N'')))
            FROM Turno t
            JOIN EstadoTurno e  ON e.IdEstadoTurno = t.IdEstadoTurno
            JOIN Paciente pa    ON pa.IdPaciente = t.IdPaciente
            JOIN Profesional pr ON pr.IdProfesional = t.IdProfesional
            WHERE t.Fecha BETWEEN @Desde AND @Hoy
              AND e.Descripcion IN (N'Ausente', N'Realizado', N'Confirmado')
              AND (@Prof IS NULL OR t.IdProfesional = @Prof) AND (@Pac IS NULL OR t.IdPaciente = @Pac)
            ORDER BY t.IdPaciente, t.IdProfesional, t.Fecha DESC, t.Hora DESC;", con);
        cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = ahora.Date.AddMonths(-meses);
        cmd.Parameters.Add("@Hoy", SqlDbType.Date).Value = ahora.Date;
        cmd.Parameters.Add("@Prof", SqlDbType.Int).Value = (object?)idProfesional ?? DBNull.Value;
        cmd.Parameters.Add("@Pac", SqlDbType.Int).Value = (object?)idPaciente ?? DBNull.Value;
        var lista = new List<TurnoPasado>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var inicio = r.GetDateTime(2).Date + r.GetTimeSpan(3);
            if (inicio > ahora) continue;   // de hoy pero todavía no empezó
            lista.Add(new TurnoPasado(r.GetInt32(0), r.GetInt32(1), inicio, r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7)));
        }
        return lista;
    }

    /// <summary>El último abandono confirmado de cada par paciente-profesional (UTC).</summary>
    public Dictionary<(int IdPaciente, int IdProfesional), DateTime> UltimosAbandonos(int? idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"SELECT IdPaciente, IdProfesional, MAX(FechaUtc) FROM AbandonoTratamiento
            WHERE @Pac IS NULL OR IdPaciente = @Pac GROUP BY IdPaciente, IdProfesional;", con);
        cmd.Parameters.Add("@Pac", SqlDbType.Int).Value = (object?)idPaciente ?? DBNull.Value;
        var d = new Dictionary<(int, int), DateTime>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[(r.GetInt32(0), r.GetInt32(1))] = DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc);
        return d;
    }

    /// <summary>El último contacto registrado con cada paciente.</summary>
    public Dictionary<int, SeguimientoResumen> UltimosSeguimientos(int? idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT s.IdPaciente, s.IdProfesional, s.Resultado, s.Nota, s.FechaUtc
            FROM SeguimientoAsistencia s
            WHERE (@Pac IS NULL OR s.IdPaciente = @Pac)
              AND s.IdSeguimiento = (SELECT MAX(x.IdSeguimiento) FROM SeguimientoAsistencia x WHERE x.IdPaciente = s.IdPaciente);", con);
        cmd.Parameters.Add("@Pac", SqlDbType.Int).Value = (object?)idPaciente ?? DBNull.Value;
        var d = new Dictionary<int, SeguimientoResumen>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            d[r.GetInt32(0)] = new SeguimientoResumen(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc));
        return d;
    }

    public int RegistrarSeguimiento(int idPaciente, int? idProfesional, string resultado, string? nota, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"INSERT INTO SeguimientoAsistencia (IdPaciente, IdProfesional, Resultado, Nota, IdUsuario)
            VALUES (@P, @Prof, @R, @N, @U); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.Add("@P", SqlDbType.Int).Value = idPaciente;
        cmd.Parameters.Add("@Prof", SqlDbType.Int).Value = (object?)idProfesional ?? DBNull.Value;
        cmd.Parameters.Add("@R", SqlDbType.NVarChar, 20).Value = resultado;
        cmd.Parameters.Add("@N", SqlDbType.NVarChar, 500).Value = (object?)nota ?? DBNull.Value;
        cmd.Parameters.Add("@U", SqlDbType.Int).Value = idUsuario;
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Confirma el abandono: lo registra (de solo agregar), cancela los turnos futuros de ese paciente con ese profesional y le avisa al
    /// paciente (sin datos clínicos). Todo en una transacción. La asignación paciente-profesional NO se toca: el profesional sigue viendo
    /// la historia clínica para cerrar el tratamiento (decidir con el equipo si después se da de baja).
    /// </summary>
    public ResultadoAbandono RegistrarAbandono(int idPaciente, int idProfesional, int ausencias, string? motivo, int idUsuario, string calidad,
        DateTime ahoraLocal, string nombreProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        // Primero se cancelan los turnos futuros (la cantidad va en el registro, que después ya no se puede modificar).
        int cancelados;
        using (var cmd = new SqlCommand(@"
            UPDATE t SET IdEstadoTurno = c.IdEstadoTurno, Cobrable = NULL, IdUsuarioAsistencia = @U, FechaAsistenciaUtc = SYSUTCDATETIME()
            FROM Turno t
            JOIN EstadoTurno e ON e.IdEstadoTurno = t.IdEstadoTurno AND e.Descripcion IN (N'Pendiente', N'Confirmado')
            CROSS JOIN (SELECT TOP 1 IdEstadoTurno FROM EstadoTurno WHERE Descripcion = N'Cancelado') c
            WHERE t.IdPaciente = @P AND t.IdProfesional = @Prof
              AND (t.Fecha > @Hoy OR (t.Fecha = @Hoy AND t.Hora > @Hora));", con, tx))
        {
            cmd.Parameters.Add("@P", SqlDbType.Int).Value = idPaciente;
            cmd.Parameters.Add("@Prof", SqlDbType.Int).Value = idProfesional;
            cmd.Parameters.Add("@U", SqlDbType.Int).Value = idUsuario;
            cmd.Parameters.Add("@Hoy", SqlDbType.Date).Value = ahoraLocal.Date;
            cmd.Parameters.Add("@Hora", SqlDbType.Time).Value = ahoraLocal.TimeOfDay;
            cancelados = cmd.ExecuteNonQuery();
        }
        int idAbandono;
        using (var cmd = new SqlCommand(@"INSERT INTO AbandonoTratamiento (IdPaciente, IdProfesional, AusenciasConsecutivas, Motivo, TurnosCancelados, IdUsuarioConfirmo, Calidad)
            VALUES (@P, @Prof, @A, @M, @N, @U, @C); SELECT CAST(SCOPE_IDENTITY() AS INT);", con, tx))
        {
            cmd.Parameters.Add("@P", SqlDbType.Int).Value = idPaciente;
            cmd.Parameters.Add("@Prof", SqlDbType.Int).Value = idProfesional;
            cmd.Parameters.Add("@A", SqlDbType.Int).Value = ausencias;
            cmd.Parameters.Add("@M", SqlDbType.NVarChar, 500).Value = (object?)motivo ?? DBNull.Value;
            cmd.Parameters.Add("@N", SqlDbType.Int).Value = cancelados;
            cmd.Parameters.Add("@U", SqlDbType.Int).Value = idUsuario;
            cmd.Parameters.Add("@C", SqlDbType.NVarChar, 20).Value = calidad;
            idAbandono = (int)cmd.ExecuteScalar()!;
        }
        AvisosPaciente.CrearUnaVez(con, tx, idPaciente, $"abandono:{idAbandono}", "Tus próximos turnos quedaron cancelados",
            $"Como no pudiste asistir a tus últimas sesiones con {nombreProfesional}, cancelamos los turnos que tenías reservados con ese profesional. " +
            "Si querés retomar, pedí un turno nuevo o escribinos: te ayudamos a encontrar un horario.", null);
        tx.Commit();
        return new ResultadoAbandono(idAbandono, cancelados);
    }

    // ─────────────────────────────── Avisos «Te estamos esperando» ───────────────────────────────
    /// <summary>Turnos de hoy, confirmados, que empezaron hace menos de <paramref name="minutosAtras"/> minutos y a los que el paciente no entró.</summary>
    public List<TurnoAsistencia> SinIngresoRecientes(DateTime ahoraLocal, int minutosAtras)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(SelectTurno + @"
            WHERE t.Fecha = @Hoy AND t.Hora BETWEEN @Desde AND @Hasta AND e.Descripcion = N'Confirmado'
              AND NOT EXISTS (SELECT 1 FROM AceptacionVideollamada a WHERE a.Origen = N'turno' AND a.IdOrigen = t.IdTurno AND a.IdPaciente = t.IdPaciente)
            ORDER BY t.Hora;", con);
        var desde = ahoraLocal.AddMinutes(-minutosAtras);
        cmd.Parameters.Add("@Hoy", SqlDbType.Date).Value = ahoraLocal.Date;
        cmd.Parameters.Add("@Desde", SqlDbType.Time).Value = desde.Date == ahoraLocal.Date ? desde.TimeOfDay : TimeSpan.Zero;
        cmd.Parameters.Add("@Hasta", SqlDbType.Time).Value = ahoraLocal.TimeOfDay;
        var lista = new List<TurnoAsistencia>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(LeerTurno(r));
        return lista;
    }
}
