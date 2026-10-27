using System.Data;
using Microsoft.Data.SqlClient;

// Acceso a datos del consentimiento de la lectura de emociones (tabla ConsentimientoVideo, migración
// migrations/add_consentimiento_video.sql). Las fechas son UTC y se guardan como DATETIME2(3).
public class ConsentimientoVideoDAO : IConsentimientoVideoRepositorio
{
    public ConsentimientoVideoFila? Ultimo(string origen, int idOrigen, int idPaciente, string finalidad)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT TOP 1 IdConsentimientoVideo, IdUsuario, IdPaciente, Origen, IdOrigen, VersionTexto, HashTexto,
                         Decision, DecididoUtc, RevocadoUtc, Finalidad
            FROM ConsentimientoVideo
            WHERE Origen = @Origen AND IdOrigen = @IdOrigen AND IdPaciente = @IdPaciente AND Finalidad = @Finalidad
            ORDER BY IdConsentimientoVideo DESC", con);
        cmd.Parameters.AddWithValue("@Origen", origen);
        cmd.Parameters.AddWithValue("@IdOrigen", idOrigen);
        cmd.Parameters.AddWithValue("@IdPaciente", idPaciente);
        cmd.Parameters.AddWithValue("@Finalidad", finalidad);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public ConsentimientoVideoFila Insertar(int idUsuario, int idPaciente, string origen, int idOrigen,
        string versionTexto, string hashTexto, string decision, DateTime decididoUtc, string finalidad)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO ConsentimientoVideo (IdUsuario, IdPaciente, Origen, IdOrigen, VersionTexto, HashTexto, Decision, DecididoUtc, Finalidad)
            OUTPUT INSERTED.IdConsentimientoVideo
            VALUES (@IdUsuario, @IdPaciente, @Origen, @IdOrigen, @VersionTexto, @HashTexto, @Decision, @DecididoUtc, @Finalidad)", con);
        cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
        cmd.Parameters.AddWithValue("@IdPaciente", idPaciente);
        cmd.Parameters.AddWithValue("@Origen", origen);
        cmd.Parameters.AddWithValue("@IdOrigen", idOrigen);
        cmd.Parameters.AddWithValue("@VersionTexto", versionTexto);
        cmd.Parameters.AddWithValue("@HashTexto", hashTexto);
        cmd.Parameters.AddWithValue("@Decision", decision);
        cmd.Parameters.Add("@DecididoUtc", SqlDbType.DateTime2).Value = decididoUtc;
        cmd.Parameters.AddWithValue("@Finalidad", finalidad);
        var id = (int)cmd.ExecuteScalar()!;
        return new ConsentimientoVideoFila(id, idUsuario, idPaciente, origen, idOrigen, versionTexto, hashTexto, decision, decididoUtc, null, finalidad);
    }

    public bool Revocar(int idConsentimientoVideo, DateTime revocadoUtc)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        // Nunca antes de la decisión (la restricción de la tabla lo exigiría): con relojes un poco corridos se usa la hora de la decisión.
        var cmd = new SqlCommand(@"
            UPDATE ConsentimientoVideo
            SET RevocadoUtc = CASE WHEN @RevocadoUtc < DecididoUtc THEN DecididoUtc ELSE @RevocadoUtc END
            WHERE IdConsentimientoVideo = @Id AND Decision = 'acepta' AND RevocadoUtc IS NULL", con);
        cmd.Parameters.AddWithValue("@Id", idConsentimientoVideo);
        cmd.Parameters.Add("@RevocadoUtc", SqlDbType.DateTime2).Value = revocadoUtc;
        return cmd.ExecuteNonQuery() > 0;
    }

    public int AnularEmocionesDeLaSesion(string origen, int idOrigen, int idPaciente, string motivo, DateTime ahoraUtc)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        // Cualquier fila de consentimiento de esa sesión (no solo la última): la sesión pudo guardarse con una decisión
        // anterior de la misma persona. Las notas, la transcripción y el tipo de cierre no se tocan.
        var cmd = new SqlCommand(@"
            UPDATE SesionClinica
            SET HistorialEmociones = NULL, EmocionesAnuladasUtc = @Ahora, EmocionesAnuladasMotivo = @Motivo
            WHERE HistorialEmociones IS NOT NULL
              AND IdConsentimientoVideo IN (SELECT IdConsentimientoVideo FROM ConsentimientoVideo
                                            WHERE Origen = @Origen AND IdOrigen = @IdOrigen AND IdPaciente = @IdPaciente
                                              AND Finalidad = 'emociones')", con);
        cmd.Parameters.AddWithValue("@Origen", origen);
        cmd.Parameters.AddWithValue("@IdOrigen", idOrigen);
        cmd.Parameters.AddWithValue("@IdPaciente", idPaciente);
        cmd.Parameters.AddWithValue("@Motivo", motivo);
        cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahoraUtc;
        return cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<int> AnularGrabacionDeLaSesion(string origen, int idOrigen, int idPaciente, string motivo, DateTime ahoraUtc)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        var tocadas = new List<(int IdTranscripcion, int? IdSesion)>();
        var cmdAnular = new SqlCommand(@"
            UPDATE TranscripcionSesion
            SET Estado = 'anulada', ActualizadoUtc = SYSUTCDATETIME()
            OUTPUT INSERTED.IdTranscripcion, INSERTED.IdSesion
            WHERE Estado <> 'anulada'
              AND Origen = @Origen AND IdOrigen = @IdOrigen
              AND IdConsentimientoVideo IN (SELECT IdConsentimientoVideo FROM ConsentimientoVideo
                                            WHERE Origen = @Origen AND IdOrigen = @IdOrigen AND IdPaciente = @IdPaciente
                                              AND Finalidad = 'grabacion')", con, tx);
        cmdAnular.Parameters.AddWithValue("@Origen", origen);
        cmdAnular.Parameters.AddWithValue("@IdOrigen", idOrigen);
        cmdAnular.Parameters.AddWithValue("@IdPaciente", idPaciente);
        using (var r = cmdAnular.ExecuteReader())
            while (r.Read())
                tocadas.Add(((int)r["IdTranscripcion"], r["IdSesion"] == DBNull.Value ? null : (int)r["IdSesion"]));

        var idsSesion = tocadas.Where(t => t.IdSesion is not null).Select(t => t.IdSesion!.Value).Distinct().ToList();
        if (idsSesion.Count > 0)
        {
            var parametros = idsSesion.Select((_, i) => $"@IdSesion{i}").ToList();
            var cmdSesion = new SqlCommand($@"
                UPDATE SesionClinica
                SET Transcripcion = NULL, TranscripcionAnuladaUtc = @Ahora, TranscripcionAnuladaMotivo = @Motivo
                WHERE Transcripcion IS NOT NULL AND IdSesion IN ({string.Join(",", parametros)})", con, tx);
            cmdSesion.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahoraUtc;
            cmdSesion.Parameters.AddWithValue("@Motivo", motivo);
            for (var i = 0; i < idsSesion.Count; i++) cmdSesion.Parameters.AddWithValue(parametros[i], idsSesion[i]);
            cmdSesion.ExecuteNonQuery();
        }

        tx.Commit();
        return tocadas.Select(t => t.IdTranscripcion).ToList();
    }

    public DatosPacienteParaConsentimiento? ObtenerDatosPaciente(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT p.FechaNacimiento,
                   CASE WHEN EXISTS (SELECT 1 FROM PacienteTutor pt WHERE pt.IdPaciente = p.IdPaciente) THEN 1 ELSE 0 END AS TieneTutor
            FROM Paciente p
            WHERE p.IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DatosPacienteParaConsentimiento(
            r["FechaNacimiento"] == DBNull.Value ? null : (DateTime)r["FechaNacimiento"],
            (int)r["TieneTutor"] == 1);
    }

    public bool EstaVigente(int idConsentimientoVideo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT CASE WHEN EXISTS (SELECT 1 FROM ConsentimientoVideo
                                      WHERE IdConsentimientoVideo = @Id AND Decision = 'acepta' AND RevocadoUtc IS NULL)
                        THEN 1 ELSE 0 END", con);
        cmd.Parameters.AddWithValue("@Id", idConsentimientoVideo);
        return (int)cmd.ExecuteScalar()! == 1;
    }

    private static ConsentimientoVideoFila Mapear(SqlDataReader r) => new(
        (int)r["IdConsentimientoVideo"], (int)r["IdUsuario"], (int)r["IdPaciente"], (string)r["Origen"], (int)r["IdOrigen"],
        (string)r["VersionTexto"], ((string)r["HashTexto"]).Trim(), (string)r["Decision"],
        DateTime.SpecifyKind((DateTime)r["DecididoUtc"], DateTimeKind.Utc),
        r["RevocadoUtc"] == DBNull.Value ? null : DateTime.SpecifyKind((DateTime)r["RevocadoUtc"], DateTimeKind.Utc),
        (string)r["Finalidad"]);
}
