using Microsoft.Data.SqlClient;

// Implementación real de IAtencionRepositorio (salas, consentimientos opcionales y registro
// de funciones). Requiere migrations/add_atencion_consentimientos_ia_evolucion.sql.
public class AtencionRepositorioDAO : IAtencionRepositorio
{
    // Resuelve un código de sala: primero SalaSesion (salas creadas desde esta versión);
    // si no está, las salas anteriores por Turno.CodigoSala o InformeAdmision.CodigoSala.
    public SalaInfo? ResolverSala(string codigoSala)
    {
        if (string.IsNullOrWhiteSpace(codigoSala) || codigoSala.Length > 60) return null;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT TOP 1 CodigoSala, IdPaciente, IdProfesional, Origen, IdTurno, IdInforme FROM (
                SELECT CodigoSala, IdPaciente, IdProfesional, Origen, IdTurno, IdInforme, 1 AS Prioridad
                FROM SalaSesion WHERE CodigoSala = @Sala
                UNION ALL
                SELECT CodigoSala, IdPaciente, IdProfesional, 'Turno', IdTurno, NULL, 2
                FROM Turno WHERE CodigoSala = @Sala
                UNION ALL
                SELECT CodigoSala, IdPaciente, NULL, 'Admision', NULL, IdInforme, 3
                FROM InformeAdmision WHERE CodigoSala = @Sala
            ) s ORDER BY Prioridad", con);
        cmd.Parameters.AddWithValue("@Sala", codigoSala);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SalaInfo(
            (string)r["CodigoSala"],
            (int)r["IdPaciente"],
            r["IdProfesional"] == DBNull.Value ? null : (int)r["IdProfesional"],
            (string)r["Origen"],
            r["IdTurno"] == DBNull.Value ? null : (int)r["IdTurno"],
            r["IdInforme"] == DBNull.Value ? null : (int)r["IdInforme"]);
    }

    public bool ConsentimientoVigente(int idPaciente, string descripcionTipo) =>
        new ConsentimientoDAO().ObtenerVigente(idPaciente, descripcionTipo) is not null;

    public bool EsProfesionalDelPaciente(int idProfesional, int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT COUNT(1) FROM PacienteProfesional
            WHERE IdProfesional = @Prof AND IdPaciente = @Pac
              AND ISNULL(EstadoAsignacion, 'Confirmada') <> 'Rechazada'", con);
        cmd.Parameters.AddWithValue("@Prof", idProfesional);
        cmd.Parameters.AddWithValue("@Pac", idPaciente);
        return (int)cmd.ExecuteScalar() > 0;
    }

    public Dictionary<string, bool> UltimoEstado(string codigoSala)
    {
        var estado = new Dictionary<string, bool>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT f.Funcion, f.Activa FROM SesionFuncion f
            WHERE f.CodigoSala = @Sala
              AND f.IdSesionFuncion = (SELECT MAX(IdSesionFuncion) FROM SesionFuncion
                                       WHERE CodigoSala = f.CodigoSala AND Funcion = f.Funcion)", con);
        cmd.Parameters.AddWithValue("@Sala", codigoSala);
        using var r = cmd.ExecuteReader();
        while (r.Read()) estado[(string)r["Funcion"]] = (bool)r["Activa"];
        return estado;
    }

    public void RegistrarCambio(string codigoSala, int idPaciente, int? idUsuario, string rol, string funcion, bool activa)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO SesionFuncion (CodigoSala, IdPaciente, IdUsuario, RolUsuario, Funcion, Activa)
            VALUES (@Sala, @Pac, @Usu, @Rol, @Funcion, @Activa)", con);
        cmd.Parameters.AddWithValue("@Sala", codigoSala);
        cmd.Parameters.AddWithValue("@Pac", idPaciente);
        cmd.Parameters.AddWithValue("@Usu", (object?)idUsuario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Rol", rol);
        cmd.Parameters.AddWithValue("@Funcion", funcion);
        cmd.Parameters.AddWithValue("@Activa", activa);
        cmd.ExecuteNonQuery();
    }

    // Registra una sala nueva (turno, entrevista de admisión o sesión directa). Si el código
    // ya existe no hace nada: generar-sala puede llamarse dos veces sobre el mismo turno.
    public static void RegistrarSala(string codigoSala, int idPaciente, int? idProfesional, string origen,
                                     int? idTurno, int? idInforme, int? idUsuarioCreador)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM SalaSesion WHERE CodigoSala = @Sala)
                INSERT INTO SalaSesion (CodigoSala, IdPaciente, IdProfesional, Origen, IdTurno, IdInforme, IdUsuarioCreador)
                VALUES (@Sala, @Pac, @Prof, @Origen, @Turno, @Informe, @Usu)", con);
        cmd.Parameters.AddWithValue("@Sala", codigoSala);
        cmd.Parameters.AddWithValue("@Pac", idPaciente);
        cmd.Parameters.AddWithValue("@Prof", (object?)idProfesional ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Origen", origen);
        cmd.Parameters.AddWithValue("@Turno", (object?)idTurno ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Informe", (object?)idInforme ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Usu", (object?)idUsuarioCreador ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static string? NombrePaciente(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT Nombre FROM Paciente WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        return cmd.ExecuteScalar() as string;
    }
}

// Preferencias de sesión del profesional (enfoque, IA, transcripción, modo de evolución).
public class ConfiguracionProfesional
{
    public int IdProfesional { get; set; }
    public string Enfoque { get; set; } = EnfoquesEvolucion.General;
    public bool IaExpresionesPorDefecto { get; set; }
    public bool TranscripcionPorDefecto { get; set; }
    public string ModoEvolucionPorDefecto { get; set; } = ModosEvolucion.Manual;
}

public class ConfiguracionProfesionalDAO
{
    public ConfiguracionProfesional Obtener(int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT Enfoque, IaExpresionesPorDefecto, TranscripcionPorDefecto, ModoEvolucionPorDefecto
            FROM ConfiguracionProfesional WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new ConfiguracionProfesional { IdProfesional = idProfesional };
        return new ConfiguracionProfesional
        {
            IdProfesional = idProfesional,
            Enfoque = (string)r["Enfoque"],
            IaExpresionesPorDefecto = (bool)r["IaExpresionesPorDefecto"],
            TranscripcionPorDefecto = (bool)r["TranscripcionPorDefecto"],
            ModoEvolucionPorDefecto = (string)r["ModoEvolucionPorDefecto"]
        };
    }

    public void Guardar(ConfiguracionProfesional c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            MERGE ConfiguracionProfesional AS t
            USING (SELECT @Id AS IdProfesional) AS s ON t.IdProfesional = s.IdProfesional
            WHEN MATCHED THEN UPDATE SET Enfoque = @Enfoque, IaExpresionesPorDefecto = @Ia,
                 TranscripcionPorDefecto = @Tr, ModoEvolucionPorDefecto = @Modo, FechaActualizacion = GETDATE()
            WHEN NOT MATCHED THEN INSERT (IdProfesional, Enfoque, IaExpresionesPorDefecto, TranscripcionPorDefecto, ModoEvolucionPorDefecto)
                 VALUES (@Id, @Enfoque, @Ia, @Tr, @Modo);", con);
        cmd.Parameters.AddWithValue("@Id", c.IdProfesional);
        cmd.Parameters.AddWithValue("@Enfoque", c.Enfoque);
        cmd.Parameters.AddWithValue("@Ia", c.IaExpresionesPorDefecto);
        cmd.Parameters.AddWithValue("@Tr", c.TranscripcionPorDefecto);
        cmd.Parameters.AddWithValue("@Modo", c.ModoEvolucionPorDefecto);
        cmd.ExecuteNonQuery();
    }
}
