using Microsoft.Data.SqlClient;

// Migración 33: feriados, bloqueos de agenda, matrículas por especialidad y avisos de vencimiento.
// Las reglas viven en Services/AgendaRestricciones.cs; acá solo SQL.

public record FeriadoDto(int IdFeriado, DateTime Fecha, string Nombre, bool Trabajo);
public record BloqueoDto(int IdBloqueo, int IdProfesional, DateTime Desde, DateTime Hasta, string Motivo, string? Detalle, DateTime Creado);
public record TurnoAfectadoDto(int IdTurno, int IdPaciente, DateTime Fecha, TimeSpan Hora, string Estado);
public record MatriculaDto(int IdMatricula, int IdProfesional, int IdEspecialidad, string Especialidad, string Numero,
    string? Jurisdiccion, DateTime? Vencimiento, bool Principal);
public record MatriculaReporteDto(int IdProfesional, string Profesional, int? IdMatricula, string? Especialidad, string? Numero,
    string? Jurisdiccion, DateTime? Vencimiento, bool Principal, int TurnosDespuesDelVencimiento);
public record AvisoPendienteDto(int IdMatricula, int IdProfesional, string Profesional, string? MailProfesional, string Especialidad,
    string Numero, DateTime Vencimiento, bool Principal);
public record DestinatarioAuditor(int IdProfesional, string? Mail);

public class AgendaRestriccionesDAO
{
    // Estados que ocupan un horario: los mismos que usa el resto (todo menos Cancelado y Rechazado) y sin los cerrados.
    private const string EstadosActivos = "('Pendiente', 'Confirmado')";

    // ─────────────────────────────── Restricciones para el cálculo de horarios ───────────────────────────────
    public RestriccionesAgenda Restricciones(int idProfesional, DateTime desde, DateTime hasta)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();

        var feriados = new List<DateTime>();
        using (var cmd = new SqlCommand(@"
            SELECT f.Fecha FROM Feriado f
            WHERE f.Activo = 1 AND f.Fecha BETWEEN @Desde AND @Hasta
              AND NOT EXISTS (SELECT 1 FROM FeriadoTrabajado t WHERE t.IdFeriado = f.IdFeriado AND t.IdProfesional = @IdProf);", con))
        {
            cmd.Parameters.AddWithValue("@IdProf", idProfesional);
            cmd.Parameters.AddWithValue("@Desde", desde.Date);
            cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
            using var r = cmd.ExecuteReader();
            while (r.Read()) feriados.Add((DateTime)r["Fecha"]);
        }

        var bloqueos = new List<FranjaBloqueada>();
        using (var cmd = new SqlCommand(@"
            SELECT Desde, Hasta, Motivo FROM BloqueoAgenda
            WHERE IdProfesional = @IdProf AND Anulado IS NULL AND Desde < @HastaExcl AND Hasta > @Desde;", con))
        {
            cmd.Parameters.AddWithValue("@IdProf", idProfesional);
            cmd.Parameters.AddWithValue("@Desde", desde.Date);
            cmd.Parameters.AddWithValue("@HastaExcl", hasta.Date.AddDays(1));
            using var r = cmd.ExecuteReader();
            while (r.Read()) bloqueos.Add(new FranjaBloqueada((DateTime)r["Desde"], (DateTime)r["Hasta"], (string)r["Motivo"]));
        }

        return new RestriccionesAgenda(feriados, bloqueos, VencimientoPrincipal(con, idProfesional));
    }

    public DateTime? VencimientoPrincipal(int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        return VencimientoPrincipal(con, idProfesional);
    }

    private static DateTime? VencimientoPrincipal(SqlConnection con, int idProfesional)
    {
        using var cmd = new SqlCommand(
            "SELECT TOP 1 Vencimiento FROM ProfesionalMatricula WHERE IdProfesional = @IdProf AND Principal = 1 AND Activa = 1;", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        return cmd.ExecuteScalar() is DateTime v ? v : null;
    }

    // ─────────────────────────────── Feriados ───────────────────────────────
    public List<FeriadoDto> Feriados(int anio)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(
            "SELECT IdFeriado, Fecha, Nombre FROM Feriado WHERE Activo = 1 AND YEAR(Fecha) = @Anio ORDER BY Fecha;", con);
        cmd.Parameters.AddWithValue("@Anio", anio);
        var lista = new List<FeriadoDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(new FeriadoDto((int)r["IdFeriado"], (DateTime)r["Fecha"], (string)r["Nombre"], false));
        return lista;
    }

    /// <summary>Feriados desde una fecha, con la marca "trabajo" de ese profesional.</summary>
    public List<FeriadoDto> FeriadosDelProfesional(int idProfesional, DateTime desde, DateTime hasta)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT f.IdFeriado, f.Fecha, f.Nombre,
                   CAST(CASE WHEN t.IdFeriado IS NULL THEN 0 ELSE 1 END AS BIT) AS Trabajo
            FROM Feriado f
            LEFT JOIN FeriadoTrabajado t ON t.IdFeriado = f.IdFeriado AND t.IdProfesional = @IdProf
            WHERE f.Activo = 1 AND f.Fecha BETWEEN @Desde AND @Hasta
            ORDER BY f.Fecha;", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Desde", desde.Date);
        cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
        var lista = new List<FeriadoDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(new FeriadoDto((int)r["IdFeriado"], (DateTime)r["Fecha"], (string)r["Nombre"], (bool)r["Trabajo"]));
        return lista;
    }

    public FeriadoDto? Feriado(int idFeriado)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT IdFeriado, Fecha, Nombre FROM Feriado WHERE IdFeriado = @Id AND Activo = 1;", con);
        cmd.Parameters.AddWithValue("@Id", idFeriado);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new FeriadoDto((int)r["IdFeriado"], (DateTime)r["Fecha"], (string)r["Nombre"], false) : null;
    }

    /// <summary>Crea el feriado. Devuelve null si ya hay uno activo esa fecha.</summary>
    public int? CrearFeriado(DateTime fecha, string nombre, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            IF EXISTS (SELECT 1 FROM Feriado WITH (UPDLOCK, HOLDLOCK) WHERE Fecha = @Fecha AND Activo = 1)
                SELECT CAST(NULL AS INT)
            ELSE
                INSERT INTO Feriado (Fecha, Nombre, IdUsuarioCreo) OUTPUT INSERTED.IdFeriado VALUES (@Fecha, @Nombre, @Usuario);", con);
        cmd.Parameters.AddWithValue("@Fecha", fecha.Date);
        cmd.Parameters.AddWithValue("@Nombre", nombre);
        cmd.Parameters.AddWithValue("@Usuario", idUsuario);
        return cmd.ExecuteScalar() as int?;
    }

    public bool AnularFeriado(int idFeriado)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("UPDATE Feriado SET Activo = 0 WHERE IdFeriado = @Id AND Activo = 1;", con);
        cmd.Parameters.AddWithValue("@Id", idFeriado);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void MarcarTrabajoFeriado(int idFeriado, int idProfesional, bool trabaja)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(trabaja
            ? @"IF NOT EXISTS (SELECT 1 FROM FeriadoTrabajado WHERE IdFeriado = @F AND IdProfesional = @P)
                    INSERT INTO FeriadoTrabajado (IdFeriado, IdProfesional) VALUES (@F, @P);"
            : "DELETE FROM FeriadoTrabajado WHERE IdFeriado = @F AND IdProfesional = @P;", con);
        cmd.Parameters.AddWithValue("@F", idFeriado);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        cmd.ExecuteNonQuery();
    }

    // ─────────────────────────────── Bloqueos ───────────────────────────────
    public List<BloqueoDto> Bloqueos(int idProfesional, DateTime desde)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT IdBloqueo, IdProfesional, Desde, Hasta, Motivo, Detalle, Creado FROM BloqueoAgenda
            WHERE IdProfesional = @IdProf AND Anulado IS NULL AND Hasta > @Desde
            ORDER BY Desde;", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Desde", desde);
        var lista = new List<BloqueoDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(LeerBloqueo(r));
        return lista;
    }

    public BloqueoDto? Bloqueo(int idBloqueo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT IdBloqueo, IdProfesional, Desde, Hasta, Motivo, Detalle, Creado FROM BloqueoAgenda
            WHERE IdBloqueo = @Id AND Anulado IS NULL;", con);
        cmd.Parameters.AddWithValue("@Id", idBloqueo);
        using var r = cmd.ExecuteReader();
        return r.Read() ? LeerBloqueo(r) : null;
    }

    private static BloqueoDto LeerBloqueo(SqlDataReader r) => new(
        (int)r["IdBloqueo"], (int)r["IdProfesional"], (DateTime)r["Desde"], (DateTime)r["Hasta"], (string)r["Motivo"],
        r["Detalle"] as string, (DateTime)r["Creado"]);

    public int CrearBloqueo(int idProfesional, DateTime desde, DateTime hasta, string motivo, string? detalle, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            INSERT INTO BloqueoAgenda (IdProfesional, Desde, Hasta, Motivo, Detalle, IdUsuarioCreo)
            OUTPUT INSERTED.IdBloqueo VALUES (@P, @Desde, @Hasta, @Motivo, @Detalle, @U);", con);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        cmd.Parameters.AddWithValue("@Desde", desde);
        cmd.Parameters.AddWithValue("@Hasta", hasta);
        cmd.Parameters.AddWithValue("@Motivo", motivo);
        cmd.Parameters.AddWithValue("@Detalle", (object?)detalle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@U", idUsuario);
        return (int)cmd.ExecuteScalar();
    }

    public bool AnularBloqueo(int idBloqueo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("UPDATE BloqueoAgenda SET Anulado = SYSDATETIME() WHERE IdBloqueo = @Id AND Anulado IS NULL;", con);
        cmd.Parameters.AddWithValue("@Id", idBloqueo);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Turnos Pendientes o Confirmados del profesional que empiezan entre dos momentos (desde inclusive, hasta exclusive).</summary>
    public List<TurnoAfectadoDto> TurnosEntre(int idProfesional, DateTime desde, DateTime? hasta)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand($@"
            SELECT t.IdTurno, t.IdPaciente, t.Fecha, t.Hora, et.Descripcion
            FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdProfesional = @P AND et.Descripcion IN {EstadosActivos}
              AND DATEADD(MINUTE, DATEDIFF(MINUTE, CAST('00:00' AS TIME), t.Hora), CAST(CAST(t.Fecha AS DATE) AS DATETIME2(0))) >= @Desde
              AND (@Hasta IS NULL OR DATEADD(MINUTE, DATEDIFF(MINUTE, CAST('00:00' AS TIME), t.Hora), CAST(CAST(t.Fecha AS DATE) AS DATETIME2(0))) < @Hasta)
            ORDER BY t.Fecha, t.Hora;", con);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        cmd.Parameters.AddWithValue("@Desde", desde);
        cmd.Parameters.Add("@Hasta", System.Data.SqlDbType.DateTime2).Value = (object?)hasta ?? DBNull.Value;
        var lista = new List<TurnoAfectadoDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new TurnoAfectadoDto((int)r["IdTurno"], (int)r["IdPaciente"], (DateTime)r["Fecha"], (TimeSpan)r["Hora"], (string)r["Descripcion"]));
        return lista;
    }

    // ─────────────────────────────── Matrículas ───────────────────────────────
    private const string SelectMatricula = @"
        SELECT m.IdMatricula, m.IdProfesional, m.IdEspecialidad, e.Nombre AS Especialidad, m.Numero, m.Jurisdiccion, m.Vencimiento, m.Principal
        FROM ProfesionalMatricula m JOIN Especialidad e ON e.IdEspecialidad = m.IdEspecialidad
        WHERE m.Activa = 1 ";

    private static MatriculaDto LeerMatricula(SqlDataReader r) => new(
        (int)r["IdMatricula"], (int)r["IdProfesional"], (int)r["IdEspecialidad"], (string)r["Especialidad"], (string)r["Numero"],
        r["Jurisdiccion"] as string, r["Vencimiento"] as DateTime?, (bool)r["Principal"]);

    public List<MatriculaDto> Matriculas(int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(SelectMatricula + "AND m.IdProfesional = @P ORDER BY m.Principal DESC, e.Nombre;", con);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        var lista = new List<MatriculaDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(LeerMatricula(r));
        return lista;
    }

    public MatriculaDto? Matricula(int idMatricula)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(SelectMatricula + "AND m.IdMatricula = @Id;", con);
        cmd.Parameters.AddWithValue("@Id", idMatricula);
        using var r = cmd.ExecuteReader();
        return r.Read() ? LeerMatricula(r) : null;
    }

    public bool EspecialidadValida(int idEspecialidad)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM Especialidad WHERE IdEspecialidad = @Id AND Estado = 'Activo' AND ISNULL(Tipo, '') <> 'Servicio';", con);
        cmd.Parameters.AddWithValue("@Id", idEspecialidad);
        return cmd.ExecuteScalar() is not null;
    }

    public bool TieneMatriculaEn(int idProfesional, int idEspecialidad, int? excepto = null)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"SELECT 1 FROM ProfesionalMatricula
            WHERE IdProfesional = @P AND IdEspecialidad = @E AND Activa = 1 AND IdMatricula <> ISNULL(@X, 0);", con);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        cmd.Parameters.AddWithValue("@E", idEspecialidad);
        cmd.Parameters.AddWithValue("@X", (object?)excepto ?? DBNull.Value);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>
    /// Agrega una matrícula. Si es la principal, la anterior principal pasa a secundaria y la ficha del profesional
    /// (Profesional.Matricula, IdEspecialidad, JurisdiccionMatricula) se actualiza, porque el resto del sistema lee de ahí.
    /// </summary>
    public int CrearMatricula(int idProfesional, int idEspecialidad, string numero, string? jurisdiccion, DateTime? vencimiento,
        bool principal, int idUsuario, SqlConnection? conExterna = null, SqlTransaction? txExterna = null)
    {
        var con = conExterna ?? Conexion.ObtenerConexion();
        try
        {
            if (conExterna is null) con.Open();
            var tx = txExterna ?? con.BeginTransaction();
            try
            {
                if (principal)
                    Ejecutar(con, tx, "UPDATE ProfesionalMatricula SET Principal = 0, Modificada = SYSDATETIME(), IdUsuarioModifico = @U WHERE IdProfesional = @P AND Principal = 1 AND Activa = 1;",
                        ("@P", idProfesional), ("@U", idUsuario));
                using var cmd = new SqlCommand(@"
                    INSERT INTO ProfesionalMatricula (IdProfesional, IdEspecialidad, Numero, Jurisdiccion, Vencimiento, Principal, IdUsuarioModifico)
                    OUTPUT INSERTED.IdMatricula VALUES (@P, @E, @N, @J, @V, @Pr, @U);", con, tx);
                cmd.Parameters.AddWithValue("@P", idProfesional);
                cmd.Parameters.AddWithValue("@E", idEspecialidad);
                cmd.Parameters.AddWithValue("@N", numero);
                cmd.Parameters.AddWithValue("@J", (object?)jurisdiccion ?? DBNull.Value);
                cmd.Parameters.Add("@V", System.Data.SqlDbType.Date).Value = (object?)vencimiento?.Date ?? DBNull.Value;
                cmd.Parameters.AddWithValue("@Pr", principal);
                cmd.Parameters.AddWithValue("@U", idUsuario);
                var id = (int)cmd.ExecuteScalar();
                if (principal) SincronizarFicha(con, tx, idProfesional, idEspecialidad, numero, jurisdiccion);
                if (txExterna is null) tx.Commit();
                return id;
            }
            catch
            {
                if (txExterna is null) tx.Rollback();
                throw;
            }
        }
        finally
        {
            if (conExterna is null) con.Dispose();
        }
    }

    /// <summary>Cambia número, jurisdicción y vencimiento (la renovación). La especialidad no se cambia: se da de alta otra.</summary>
    public void ModificarMatricula(int idMatricula, string numero, string? jurisdiccion, DateTime? vencimiento, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        using (var cmd = new SqlCommand(@"
            UPDATE ProfesionalMatricula SET Numero = @N, Jurisdiccion = @J, Vencimiento = @V, Modificada = SYSDATETIME(), IdUsuarioModifico = @U
            WHERE IdMatricula = @Id AND Activa = 1;", con, tx))
        {
            cmd.Parameters.AddWithValue("@Id", idMatricula);
            cmd.Parameters.AddWithValue("@N", numero);
            cmd.Parameters.AddWithValue("@J", (object?)jurisdiccion ?? DBNull.Value);
            cmd.Parameters.Add("@V", System.Data.SqlDbType.Date).Value = (object?)vencimiento?.Date ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@U", idUsuario);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = new SqlCommand("SELECT IdProfesional, IdEspecialidad, Principal FROM ProfesionalMatricula WHERE IdMatricula = @Id;", con, tx))
        {
            cmd.Parameters.AddWithValue("@Id", idMatricula);
            int? idProf = null, idEsp = null; bool principal = false;
            using (var r = cmd.ExecuteReader())
                if (r.Read()) { idProf = (int)r["IdProfesional"]; idEsp = (int)r["IdEspecialidad"]; principal = (bool)r["Principal"]; }
            if (principal && idProf is int p && idEsp is int e) SincronizarFicha(con, tx, p, e, numero, jurisdiccion);
        }
        tx.Commit();
    }

    /// <summary>Da de baja una matrícula secundaria (la principal no se puede dar de baja: se reemplaza).</summary>
    public bool AnularMatriculaSecundaria(int idMatricula, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"UPDATE ProfesionalMatricula SET Activa = 0, Modificada = SYSDATETIME(), IdUsuarioModifico = @U
            WHERE IdMatricula = @Id AND Activa = 1 AND Principal = 0;", con);
        cmd.Parameters.AddWithValue("@Id", idMatricula);
        cmd.Parameters.AddWithValue("@U", idUsuario);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Al revés que SincronizarFicha: si el Configurador cambió matrícula o especialidad desde la ficha del profesional
    /// (PUT /profesionales/{id}), la matrícula principal acompaña. Si no tenía ninguna cargada, se crea sin vencimiento.
    /// </summary>
    public void SincronizarPrincipalDesdeFicha(int idProfesional, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            DECLARE @N NVARCHAR(50), @E INT, @J NVARCHAR(60);
            SELECT @N = LTRIM(RTRIM(Matricula)), @E = IdEspecialidad, @J = JurisdiccionMatricula FROM Profesional WHERE IdProfesional = @P;
            IF @N IS NULL OR @N = N'' OR @E IS NULL RETURN;
            IF EXISTS (SELECT 1 FROM ProfesionalMatricula WHERE IdProfesional = @P AND Principal = 1 AND Activa = 1)
            BEGIN
                UPDATE ProfesionalMatricula SET Numero = @N, IdEspecialidad = @E, Jurisdiccion = ISNULL(@J, Jurisdiccion),
                       Modificada = SYSDATETIME(), IdUsuarioModifico = @U
                WHERE IdProfesional = @P AND Principal = 1 AND Activa = 1
                  AND (Numero <> @N OR IdEspecialidad <> @E)
                  AND NOT EXISTS (SELECT 1 FROM ProfesionalMatricula o WHERE o.IdProfesional = @P AND o.IdEspecialidad = @E AND o.Activa = 1 AND o.Principal = 0);
            END
            ELSE
            BEGIN
                INSERT INTO ProfesionalMatricula (IdProfesional, IdEspecialidad, Numero, Jurisdiccion, Principal, IdUsuarioModifico)
                VALUES (@P, @E, @N, @J, 1, @U);
            END", con);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        cmd.Parameters.AddWithValue("@U", idUsuario);
        cmd.ExecuteNonQuery();
    }

    private static void SincronizarFicha(SqlConnection con, SqlTransaction tx, int idProfesional, int idEspecialidad, string numero, string? jurisdiccion) =>
        Ejecutar(con, tx, "UPDATE Profesional SET Matricula = @N, IdEspecialidad = @E, JurisdiccionMatricula = @J WHERE IdProfesional = @P;",
            ("@P", idProfesional), ("@E", idEspecialidad), ("@N", numero), ("@J", (object?)jurisdiccion ?? DBNull.Value));

    private static void Ejecutar(SqlConnection con, SqlTransaction tx, string sql, params (string, object)[] ps)
    {
        using var cmd = new SqlCommand(sql, con, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Reporte de matrículas: una fila por matrícula activa, más una fila por profesional activo que no tiene ninguna
    /// cargada. Incluye cuántos turnos activos tiene después del vencimiento de su matrícula principal.
    /// </summary>
    public List<MatriculaReporteDto> Reporte()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand($@"
            SELECT p.IdProfesional, LTRIM(RTRIM(ISNULL(p.Nombre, '') + ' ' + ISNULL(p.Apellido, ''))) AS Profesional,
                   m.IdMatricula, e.Nombre AS Especialidad, m.Numero, m.Jurisdiccion, m.Vencimiento, ISNULL(m.Principal, 0) AS Principal,
                   CASE WHEN m.Principal = 1 AND m.Vencimiento IS NOT NULL THEN
                       (SELECT COUNT(*) FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
                        WHERE t.IdProfesional = p.IdProfesional AND et.Descripcion IN {EstadosActivos} AND CAST(t.Fecha AS DATE) >= m.Vencimiento)
                   ELSE 0 END AS TurnosDespues
            FROM Profesional p
            LEFT JOIN ProfesionalMatricula m ON m.IdProfesional = p.IdProfesional AND m.Activa = 1
            LEFT JOIN Especialidad e ON e.IdEspecialidad = m.IdEspecialidad
            WHERE ISNULL(p.Estado, 'Activo') = 'Activo'
            ORDER BY CASE WHEN m.Vencimiento IS NULL THEN 1 ELSE 0 END, m.Vencimiento, Profesional;", con);
        var lista = new List<MatriculaReporteDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new MatriculaReporteDto((int)r["IdProfesional"], (string)r["Profesional"], r["IdMatricula"] as int?,
                r["Especialidad"] as string, r["Numero"] as string, r["Jurisdiccion"] as string, r["Vencimiento"] as DateTime?,
                Convert.ToBoolean(r["Principal"]), (int)r["TurnosDespues"]));
        return lista;
    }

    // ─────────────────────────────── Avisos diarios ───────────────────────────────
    /// <summary>Matrículas activas que vencen dentro de <paramref name="dias"/> días o ya vencieron, sin aviso hoy.</summary>
    public List<AvisoPendienteDto> AvisosPendientes(DateTime hoy, int dias)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT m.IdMatricula, m.IdProfesional, LTRIM(RTRIM(ISNULL(p.Nombre, '') + ' ' + ISNULL(p.Apellido, ''))) AS Profesional, p.Mail,
                   e.Nombre AS Especialidad, m.Numero, m.Vencimiento, m.Principal
            FROM ProfesionalMatricula m
            JOIN Profesional p  ON p.IdProfesional = m.IdProfesional
            JOIN Especialidad e ON e.IdEspecialidad = m.IdEspecialidad
            WHERE m.Activa = 1 AND m.Vencimiento IS NOT NULL AND m.Vencimiento <= DATEADD(DAY, @Dias, @Hoy)
              AND ISNULL(p.Estado, 'Activo') = 'Activo'
              AND NOT EXISTS (SELECT 1 FROM AvisoMatricula a WHERE a.IdMatricula = m.IdMatricula AND a.Fecha = @Hoy);", con);
        cmd.Parameters.AddWithValue("@Hoy", hoy.Date);
        cmd.Parameters.AddWithValue("@Dias", dias);
        var lista = new List<AvisoPendienteDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new AvisoPendienteDto((int)r["IdMatricula"], (int)r["IdProfesional"], (string)r["Profesional"], r["Mail"] as string,
                (string)r["Especialidad"], (string)r["Numero"], (DateTime)r["Vencimiento"], (bool)r["Principal"]));
        return lista;
    }

    /// <summary>Anota el aviso del día. Devuelve false si otro proceso ya lo anotó (no se manda dos veces).</summary>
    public bool RegistrarAviso(int idMatricula, DateTime hoy, string tipo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM AvisoMatricula WITH (UPDLOCK, HOLDLOCK) WHERE IdMatricula = @M AND Fecha = @Hoy)
            BEGIN INSERT INTO AvisoMatricula (IdMatricula, Fecha, Tipo) VALUES (@M, @Hoy, @T); SELECT 1; END
            ELSE SELECT 0;", con);
        cmd.Parameters.AddWithValue("@M", idMatricula);
        cmd.Parameters.AddWithValue("@Hoy", hoy.Date);
        cmd.Parameters.AddWithValue("@T", tipo);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    /// <summary>Profesionales auditores activos (para avisarles), con el mail de su cuenta.</summary>
    public List<DestinatarioAuditor> Auditores()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT DISTINCT up.IdProfesional, u.Mail
            FROM UsuarioPermiso x
            JOIN Permiso pe ON pe.IdPermiso = x.IdPermiso AND pe.Clave = 'auditor'
            JOIN Usuario u ON u.IdUsuario = x.IdUsuario AND u.Estado = 'Activo'
            JOIN UsuarioProfesional up ON up.IdUsuario = u.IdUsuario;", con);
        var lista = new List<DestinatarioAuditor>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(new DestinatarioAuditor((int)r["IdProfesional"], r["Mail"] as string));
        return lista;
    }

    // ─────────────────────────────── Avisos del profesional (campanita) ───────────────────────────────
    public List<object> AvisosDelProfesional(int idProfesional, int maximo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT TOP (@Max) IdNotificacionInterna, Tipo, Titulo, Mensaje, Leida, FechaCreacion
            FROM NotificacionInterna
            WHERE Destinatario = 'Profesional' AND IdProfesional = @P
            ORDER BY FechaCreacion DESC;", con);
        cmd.Parameters.AddWithValue("@Max", maximo);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        var lista = new List<object>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new
            {
                idNotificacion = (int)r["IdNotificacionInterna"], tipo = (string)r["Tipo"], titulo = (string)r["Titulo"],
                mensaje = (string)r["Mensaje"], leida = (bool)r["Leida"], fecha = (DateTime)r["FechaCreacion"]
            });
        return lista;
    }

    public bool MarcarAvisoLeido(int idNotificacion, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"UPDATE NotificacionInterna SET Leida = 1
            WHERE IdNotificacionInterna = @Id AND Destinatario = 'Profesional' AND IdProfesional = @P;", con);
        cmd.Parameters.AddWithValue("@Id", idNotificacion);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void CrearAvisoProfesional(int idProfesional, string tipo, string titulo, string mensaje)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"INSERT INTO NotificacionInterna (Destinatario, IdProfesional, Tipo, Titulo, Mensaje)
            VALUES ('Profesional', @P, @T, @Ti, @M);", con);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        cmd.Parameters.AddWithValue("@T", tipo);
        cmd.Parameters.AddWithValue("@Ti", titulo);
        cmd.Parameters.AddWithValue("@M", mensaje);
        cmd.ExecuteNonQuery();
    }
}
