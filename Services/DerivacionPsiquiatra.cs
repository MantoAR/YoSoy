using Microsoft.Data.SqlClient;

// Paciente «Derivado» a psiquiatría (migración 34). Es SOLO un cambio de estado que confirma una persona: el sistema no crea tareas, no manda mails ni avisa a otra persona de la derivación en sí.
// Lo único que avisa (por la campanita) es que el paciente TIENE turnos futuros, porque esos no se cancelan solos: lo decide una persona.
// Un paciente derivado no puede reservar ni recibir turnos por ningún camino (ver TurnoReglasService.ValidarSolicitud y los demás puntos que llaman a EstaDerivado).
//
// TODO-VALIDAR (con los psicólogos): qué significa clínicamente «derivado» (¿el caso sigue a cargo del profesional? ¿se lo sigue viendo en sus listados?), si hace falta un motivo escrito,
// si la reapertura debe pedir un motivo, y qué pasa con los turnos que ya tenía. Se dejó lo más neutro: el estado, la constancia y el bloqueo de turnos nuevos.
public static class DerivacionPsiquiatraReglas
{
    public const string Estado = "Derivado";
    public const string CodigoBloqueo = "paciente-derivado";
    public const string MensajeBloqueo = "Este paciente fue derivado y no puede reservar turnos con este equipo.";

    public const string FaltaMigracion34 = "falta-migracion-34";
    public const string FaltaConfirmacion = "falta-confirmacion";
    public const string PacienteInexistente = "paciente-inexistente";
    public const string PacienteAjeno = "paciente-ajeno";
    public const string SesionNoCoincide = "sesion-no-coincide";
    public const string NoEstaDerivado = "no-esta-derivado";

    public static int Status(string codigo) => codigo switch
    {
        PacienteInexistente => 404,
        PacienteAjeno => 403,
        FaltaConfirmacion or SesionNoCoincide => 400,
        _ => 409
    };

    public static string Mensaje(string codigo) => codigo switch
    {
        FaltaMigracion34 => "Falta aplicar una actualización de la base de datos (migración 34) para derivar pacientes. No se cambió nada: avisale al equipo técnico.",
        FaltaConfirmacion => "Para derivar al paciente tenés que confirmarlo en la ventana de confirmación. No se cambió nada.",
        PacienteInexistente => "No se encontró al paciente.",
        PacienteAjeno => "Solo podés derivar a un paciente que tengas a tu cargo.",
        SesionNoCoincide => "La sesión indicada no es de este paciente y de este profesional.",
        NoEstaDerivado => "Este paciente no figura como derivado: no hay nada para reabrir.",
        CodigoBloqueo => MensajeBloqueo,
        _ => "No se pudo completar la operación."
    };

    /// <summary>Texto del aviso por turnos futuros (puro, para probarlo sin base). Sin diagnósticos: solo el nombre y la cantidad.</summary>
    public static string AvisoTurnosFuturos(string paciente, int cantidad) =>
        $"{paciente} fue derivado y todavía tiene {cantidad} turno{(cantidad == 1 ? "" : "s")} por delante. No se cancelaron solos: decidí si corresponde cancelarlos.";
}

public record ResultadoDerivacionPsiquiatra
{
    public string? Rechazo { get; init; }
    public bool Repetido { get; init; }
    public int IdDerivacion { get; init; }
    public int IdPaciente { get; init; }
    public int TurnosFuturos { get; init; }
    public DateTime? FechaUtc { get; init; }

    public static ResultadoDerivacionPsiquiatra Rechazar(string codigo) => new() { Rechazo = codigo };
}

public record DatosDerivadoPsiquiatra(int IdDerivacion, int IdPaciente, int IdProfesional, int? IdSesion, int? IdTurno, DateTime FechaUtc);

public class DerivacionPsiquiatraDAO
{
    /// <summary>La tabla de la migración 34. Sin ella nadie figura como derivado.</summary>
    public static bool Disponible()
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.DerivacionPsiquiatrica', 'U') IS NULL THEN 0 ELSE 1 END", con);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }
        catch (SqlException) { return false; }
    }

    private const string ConsultaVigente = "SELECT TOP 1 IdDerivacionPsiquiatrica, IdProfesional, IdSesion, IdTurno, FechaUtc FROM dbo.DerivacionPsiquiatrica WHERE IdPaciente = @P AND ReabiertaUtc IS NULL";

    /// <summary>¿Figura derivado? Es la regla que reutilizan todos los caminos de turnos. Sin la tabla, falso. Abre su propia conexión.</summary>
    public static bool EstaDerivado(int idPaciente)
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            return EstaDerivadoEn(con, null, idPaciente);
        }
        catch (SqlException) { return false; }
    }

    /// <summary>Lo mismo dentro de la conexión (y transacción) de quien llama: así no se abre otra conexión después de haber escrito (ver el bloqueo de la auditoría).</summary>
    public static bool EstaDerivadoEn(SqlConnection con, SqlTransaction? tx, int idPaciente)
    {
        using var cmd = new SqlCommand(@"
            IF OBJECT_ID('dbo.DerivacionPsiquiatrica', 'U') IS NULL SELECT 0
            ELSE SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.DerivacionPsiquiatrica WHERE IdPaciente = @P AND ReabiertaUtc IS NULL) THEN 1 ELSE 0 END", con, tx);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    public DatosDerivadoPsiquiatra? Vigente(int idPaciente)
    {
        if (!Disponible()) return null;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(ConsultaVigente, con);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new DatosDerivadoPsiquiatra((int)r["IdDerivacionPsiquiatrica"], idPaciente, (int)r["IdProfesional"], r["IdSesion"] as int?, r["IdTurno"] as int?, (DateTime)r["FechaUtc"])
            : null;
    }

    /// <summary>Los pacientes que figuran derivados entre estos (para marcarlos en los listados). Sin la tabla, ninguno.</summary>
    public static HashSet<int> Derivados(IEnumerable<int> idsPacientes)
    {
        var resultado = new HashSet<int>();
        var ids = idsPacientes.Distinct().ToList();
        if (ids.Count == 0) return resultado;
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand(@"
                IF OBJECT_ID('dbo.DerivacionPsiquiatrica', 'U') IS NULL SELECT TOP 0 CAST(0 AS INT) AS IdPaciente
                ELSE SELECT IdPaciente FROM dbo.DerivacionPsiquiatrica WHERE ReabiertaUtc IS NULL", con);
            using var r = cmd.ExecuteReader();
            var todos = new HashSet<int>();
            while (r.Read()) todos.Add((int)r["IdPaciente"]);
            foreach (var id in ids) if (todos.Contains(id)) resultado.Add(id);
        }
        catch (SqlException) { /* sin la tabla o sin base: nadie figura derivado */ }
        return resultado;
    }

    /// <summary>
    /// Deriva al paciente. Una sola transacción, idempotente (repetirlo devuelve la derivación que ya estaba, sin escribir ni auditar de nuevo). Primero se LEE todo y recién después se escribe:
    /// la auditoría serializa las transacciones y una lectura por otra conexión después de escribir se colgaría.
    /// </summary>
    public ResultadoDerivacionPsiquiatra Derivar(int idPaciente, int idProfesional, int? idSesion, int? idTurno, AutorDelCierre autor, DateTime ahoraLocal)
    {
        if (!Disponible()) return ResultadoDerivacionPsiquiatra.Rechazar(DerivacionPsiquiatraReglas.FaltaMigracion34);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        try
        {
            using var tx = con.BeginTransaction();

            // ── 1) Leer ──
            string? nombre;
            using (var cmd = new SqlCommand("SELECT Nombre FROM Paciente WHERE IdPaciente = @P", con, tx))
            {
                cmd.Parameters.AddWithValue("@P", idPaciente);
                nombre = cmd.ExecuteScalar() as string;
            }
            if (nombre is null) return ResultadoDerivacionPsiquiatra.Rechazar(DerivacionPsiquiatraReglas.PacienteInexistente);

            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @R AND ISNULL(EstadoAsignacion, 'Confirmada') <> 'Rechazada'", con, tx))
            {
                cmd.Parameters.AddWithValue("@P", idPaciente);
                cmd.Parameters.AddWithValue("@R", idProfesional);
                if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return ResultadoDerivacionPsiquiatra.Rechazar(DerivacionPsiquiatraReglas.PacienteAjeno);
            }

            // Idempotente: si ya figura derivado, se devuelve esa derivación.
            using (var cmd = new SqlCommand(ConsultaVigente + " ORDER BY IdDerivacionPsiquiatrica DESC", con, tx))
            {
                cmd.Parameters.AddWithValue("@P", idPaciente);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                    return new ResultadoDerivacionPsiquiatra { Repetido = true, IdDerivacion = (int)r["IdDerivacionPsiquiatrica"], IdPaciente = idPaciente, FechaUtc = (DateTime)r["FechaUtc"] };
            }

            // La sesión (si se indicó) tiene que ser de este paciente y de este profesional.
            if (idSesion is not null)
            {
                using var cmd = new SqlCommand("SELECT COUNT(*) FROM SesionClinica WHERE IdSesion = @S AND IdPaciente = @P AND IdProfesional = @R", con, tx);
                cmd.Parameters.AddWithValue("@S", idSesion.Value);
                cmd.Parameters.AddWithValue("@P", idPaciente);
                cmd.Parameters.AddWithValue("@R", idProfesional);
                if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return ResultadoDerivacionPsiquiatra.Rechazar(DerivacionPsiquiatraReglas.SesionNoCoincide);
            }
            if (idTurno is not null)
            {
                using var cmd = new SqlCommand("SELECT COUNT(*) FROM Turno WHERE IdTurno = @T AND IdPaciente = @P AND IdProfesional = @R", con, tx);
                cmd.Parameters.AddWithValue("@T", idTurno.Value);
                cmd.Parameters.AddWithValue("@P", idPaciente);
                cmd.Parameters.AddWithValue("@R", idProfesional);
                if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return ResultadoDerivacionPsiquiatra.Rechazar(DerivacionPsiquiatraReglas.SesionNoCoincide);
            }

            int? idCaso = null;
            using (var cmd = new SqlCommand("SELECT TOP 1 IdCaso FROM Caso WHERE IdPaciente = @P ORDER BY IdCaso DESC", con, tx))
            {
                cmd.Parameters.AddWithValue("@P", idPaciente);
                idCaso = cmd.ExecuteScalar() as int?;
            }

            // Los turnos que le quedan por delante (de cualquier profesional): no se cancelan, se avisa.
            var profesionalesConTurnos = new HashSet<int>();
            var turnosFuturos = 0;
            using (var cmd = new SqlCommand(@"
                SELECT t.IdProfesional
                FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
                WHERE t.IdPaciente = @P AND et.Descripcion IN ('Pendiente', 'Confirmado')
                  AND (t.Fecha > @Hoy OR (t.Fecha = @Hoy AND t.Hora > @Hora))", con, tx))
            {
                cmd.Parameters.AddWithValue("@P", idPaciente);
                cmd.Parameters.AddWithValue("@Hoy", ahoraLocal.Date);
                cmd.Parameters.AddWithValue("@Hora", ahoraLocal.TimeOfDay);
                using var r = cmd.ExecuteReader();
                while (r.Read()) { turnosFuturos++; profesionalesConTurnos.Add((int)r["IdProfesional"]); }
            }

            // ── 2) Escribir ──
            int id;
            DateTime fechaUtc;
            using (var ins = new SqlCommand(@"
                INSERT INTO dbo.DerivacionPsiquiatrica (IdPaciente, IdProfesional, IdCaso, IdSesion, IdTurno, IdUsuario)
                OUTPUT INSERTED.IdDerivacionPsiquiatrica, INSERTED.FechaUtc
                VALUES (@P, @R, @C, @S, @T, @U)", con, tx))
            {
                ins.Parameters.AddWithValue("@P", idPaciente);
                ins.Parameters.AddWithValue("@R", idProfesional);
                ins.Parameters.AddWithValue("@C", (object?)idCaso ?? DBNull.Value);
                ins.Parameters.AddWithValue("@S", (object?)idSesion ?? DBNull.Value);
                ins.Parameters.AddWithValue("@T", (object?)idTurno ?? DBNull.Value);
                ins.Parameters.AddWithValue("@U", (object?)autor.IdUsuario ?? DBNull.Value);
                using var r = ins.ExecuteReader();
                r.Read();
                id = (int)r[0];
                fechaUtc = (DateTime)r[1];
            }

            if (turnosFuturos > 0)
            {
                var texto = DerivacionPsiquiatraReglas.AvisoTurnosFuturos(nombre, turnosFuturos);
                ADComun.Avisar(con, tx, new AvisoNuevo("Administrador", null, "derivado-turnos-futuros", "Paciente derivado con turnos por delante", texto));
                foreach (var prof in profesionalesConTurnos.Append(idProfesional).Distinct())
                    ADComun.Avisar(con, tx, new AvisoNuevo("Profesional", prof, "derivado-turnos-futuros", "Paciente derivado con turnos por delante", texto));
            }

            // La auditoría va al final: toma un bloqueo que dura hasta el commit.
            ADComun.Auditar(con, tx, autor, "Paciente derivado a psiquiatría", "Paciente", idPaciente,
                $"Derivación #{id}, paciente #{idPaciente}, profesional #{idProfesional}" + (idSesion is int s ? $", sesión #{s}" : "") + (idTurno is int t ? $", turno #{t}" : "") + $", turnos futuros {turnosFuturos}");

            tx.Commit();
            return new ResultadoDerivacionPsiquiatra { IdDerivacion = id, IdPaciente = idPaciente, TurnosFuturos = turnosFuturos, FechaUtc = fechaUtc };
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            // Dos derivaciones a la vez: el índice único dejó pasar una sola; la otra es una repetición.
            var vigente = Vigente(idPaciente);
            if (vigente is null) throw;
            return new ResultadoDerivacionPsiquiatra { Repetido = true, IdDerivacion = vigente.IdDerivacion, IdPaciente = idPaciente, FechaUtc = vigente.FechaUtc };
        }
    }

    /// <summary>Reabre al paciente (recepción / CRM): vuelve a poder reservar turnos. El caso nunca cambió de estado, así que «vuelve a su estado anterior» sin más. Idempotente.</summary>
    public ResultadoDerivacionPsiquiatra Reabrir(int idPaciente, AutorDelCierre autor)
    {
        if (!Disponible()) return ResultadoDerivacionPsiquiatra.Rechazar(DerivacionPsiquiatraReglas.FaltaMigracion34);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        int idDerivacion;
        using (var cmd = new SqlCommand(ConsultaVigente.Replace("FROM dbo.DerivacionPsiquiatrica", "FROM dbo.DerivacionPsiquiatrica WITH (UPDLOCK, HOLDLOCK)"), con, tx))
        {
            cmd.Parameters.AddWithValue("@P", idPaciente);
            using var r = cmd.ExecuteReader();
            // Idempotente: si no figura derivado (ya se reabrió, o nunca lo estuvo) no hay nada que hacer ni que auditar.
            if (!r.Read()) return new ResultadoDerivacionPsiquiatra { Repetido = true, IdPaciente = idPaciente };
            idDerivacion = (int)r["IdDerivacionPsiquiatrica"];
        }

        using (var upd = new SqlCommand("UPDATE dbo.DerivacionPsiquiatrica SET ReabiertaUtc = SYSUTCDATETIME(), IdUsuarioReapertura = @U WHERE IdDerivacionPsiquiatrica = @I AND ReabiertaUtc IS NULL", con, tx))
        {
            upd.Parameters.AddWithValue("@U", (object?)autor.IdUsuario ?? DBNull.Value);
            upd.Parameters.AddWithValue("@I", idDerivacion);
            upd.ExecuteNonQuery();
        }
        ADComun.Auditar(con, tx, autor, "Paciente derivado reabierto", "Paciente", idPaciente, $"Derivación #{idDerivacion} reabierta, paciente #{idPaciente}");
        tx.Commit();
        return new ResultadoDerivacionPsiquiatra { IdDerivacion = idDerivacion, IdPaciente = idPaciente };
    }
}
