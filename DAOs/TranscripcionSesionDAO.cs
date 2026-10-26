using Microsoft.Data.SqlClient;

public class TranscripcionSesionDAO : ITranscripcionSesionRepositorio
{
    private const string Columnas =
        "IdTranscripcion, IdSesion, Origen, IdOrigen, IdConsentimientoVideo, Estado, Intentos, Error, " +
        "InicioPistaProfesionalMs, InicioPistaPacienteMs, Modelo, DuracionAudioSeg, DuracionProcesoSeg, CreadoUtc, ActualizadoUtc";

    private const string ColumnasInserted =
        "INSERTED.IdTranscripcion, INSERTED.IdSesion, INSERTED.Origen, INSERTED.IdOrigen, INSERTED.IdConsentimientoVideo, " +
        "INSERTED.Estado, INSERTED.Intentos, INSERTED.Error, INSERTED.InicioPistaProfesionalMs, INSERTED.InicioPistaPacienteMs, " +
        "INSERTED.Modelo, INSERTED.DuracionAudioSeg, INSERTED.DuracionProcesoSeg, INSERTED.CreadoUtc, INSERTED.ActualizadoUtc";

    public TranscripcionSesion Crear(string origen, int idOrigen, int idConsentimientoVideo, long? inicioPistaProfesionalMs, long? inicioPistaPacienteMs)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand($@"
            INSERT INTO TranscripcionSesion (Origen, IdOrigen, IdConsentimientoVideo, InicioPistaProfesionalMs, InicioPistaPacienteMs)
            OUTPUT {ColumnasInserted}
            VALUES (@Origen, @IdOrigen, @IdConsentimientoVideo, @InicioPistaProfesionalMs, @InicioPistaPacienteMs)", con);
        cmd.Parameters.AddWithValue("@Origen", origen);
        cmd.Parameters.AddWithValue("@IdOrigen", idOrigen);
        cmd.Parameters.AddWithValue("@IdConsentimientoVideo", idConsentimientoVideo);
        cmd.Parameters.AddWithValue("@InicioPistaProfesionalMs", inicioPistaProfesionalMs ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@InicioPistaPacienteMs", inicioPistaPacienteMs ?? (object)DBNull.Value);
        using var r = cmd.ExecuteReader();
        r.Read();
        return Mapear(r);
    }

    public TranscripcionSesion CrearOReusar(string origen, int idOrigen, int idConsentimientoVideo, long? inicioPistaProfesionalMs, long? inicioPistaPacienteMs)
    {
        try
        {
            return Crear(origen, idOrigen, idConsentimientoVideo, inicioPistaProfesionalMs, inicioPistaPacienteMs);
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            if (ObtenerActiva(origen, idOrigen) is { } reusada) return reusada;
            throw;
        }
    }

    public bool ActualizarInicioPista(int idTranscripcion, string pista, long inicioMs)
    {
        var columna = pista == PistaGrabacion.Profesional ? "InicioPistaProfesionalMs" : "InicioPistaPacienteMs";
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand($@"
            UPDATE TranscripcionSesion
            SET {columna} = @InicioMs, ActualizadoUtc = SYSUTCDATETIME()
            WHERE IdTranscripcion = @Id AND {columna} IS NULL", con);
        cmd.Parameters.AddWithValue("@InicioMs", inicioMs);
        cmd.Parameters.AddWithValue("@Id", idTranscripcion);
        return cmd.ExecuteNonQuery() > 0;
    }

    public TranscripcionSesion? ObtenerActiva(string origen, int idOrigen)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand($@"
            SELECT TOP 1 {Columnas}
            FROM TranscripcionSesion
            WHERE Origen = @Origen AND IdOrigen = @IdOrigen AND Estado = 'grabando'
            ORDER BY IdTranscripcion DESC", con);
        cmd.Parameters.AddWithValue("@Origen", origen);
        cmd.Parameters.AddWithValue("@IdOrigen", idOrigen);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public TranscripcionSesion? ObtenerPorSesion(int idSesion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand($@"
            SELECT TOP 1 {Columnas}
            FROM TranscripcionSesion
            WHERE IdSesion = @IdSesion
            ORDER BY IdTranscripcion DESC", con);
        cmd.Parameters.AddWithValue("@IdSesion", idSesion);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public bool VincularSesion(int idTranscripcion, int idSesion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE TranscripcionSesion
            SET IdSesion = @IdSesion, Estado = 'pendiente', ActualizadoUtc = SYSUTCDATETIME()
            WHERE IdTranscripcion = @Id AND Estado = 'grabando'", con);
        cmd.Parameters.AddWithValue("@IdSesion", idSesion);
        cmd.Parameters.AddWithValue("@Id", idTranscripcion);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool CambiarEstado(int idTranscripcion, string estado, string? error = null, int? duracionAudioSeg = null, int? duracionProcesoSeg = null, string? modelo = null)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE TranscripcionSesion
            SET Estado = @Estado, Error = @Error, DuracionAudioSeg = @DuracionAudioSeg, DuracionProcesoSeg = @DuracionProcesoSeg,
                Modelo = @Modelo, ActualizadoUtc = SYSUTCDATETIME()
            WHERE IdTranscripcion = @Id", con);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@Error", error ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@DuracionAudioSeg", duracionAudioSeg ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@DuracionProcesoSeg", duracionProcesoSeg ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Modelo", modelo ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", idTranscripcion);
        return cmd.ExecuteNonQuery() > 0;
    }

    public TranscripcionSesion? TomarSiguientePendiente()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand($@"
            ;WITH Candidato AS (
                SELECT TOP (1) *
                FROM TranscripcionSesion WITH (READPAST, UPDLOCK, ROWLOCK)
                WHERE Estado = 'pendiente'
                ORDER BY IdTranscripcion
            )
            UPDATE Candidato
            SET Estado = 'procesando', Intentos = Intentos + 1, ActualizadoUtc = SYSUTCDATETIME()
            OUTPUT {ColumnasInserted}", con);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public int ReencolarProcesando()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE TranscripcionSesion
            SET Estado = 'pendiente', ActualizadoUtc = SYSUTCDATETIME()
            WHERE Estado = 'procesando'", con);
        return cmd.ExecuteNonQuery();
    }

    private static TranscripcionSesion Mapear(SqlDataReader r) => new()
    {
        IdTranscripcion = (int)r["IdTranscripcion"],
        IdSesion = r["IdSesion"] == DBNull.Value ? null : (int)r["IdSesion"],
        Origen = (string)r["Origen"],
        IdOrigen = (int)r["IdOrigen"],
        IdConsentimientoVideo = (int)r["IdConsentimientoVideo"],
        Estado = (string)r["Estado"],
        Intentos = (int)r["Intentos"],
        Error = r["Error"] == DBNull.Value ? null : (string)r["Error"],
        InicioPistaProfesionalMs = r["InicioPistaProfesionalMs"] == DBNull.Value ? null : (long)r["InicioPistaProfesionalMs"],
        InicioPistaPacienteMs = r["InicioPistaPacienteMs"] == DBNull.Value ? null : (long)r["InicioPistaPacienteMs"],
        Modelo = r["Modelo"] == DBNull.Value ? null : (string)r["Modelo"],
        DuracionAudioSeg = r["DuracionAudioSeg"] == DBNull.Value ? null : (int)r["DuracionAudioSeg"],
        DuracionProcesoSeg = r["DuracionProcesoSeg"] == DBNull.Value ? null : (int)r["DuracionProcesoSeg"],
        CreadoUtc = DateTime.SpecifyKind((DateTime)r["CreadoUtc"], DateTimeKind.Utc),
        ActualizadoUtc = DateTime.SpecifyKind((DateTime)r["ActualizadoUtc"], DateTimeKind.Utc)
    };
}
