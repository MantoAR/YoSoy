using System.Data;
using Microsoft.Data.SqlClient;

// Acceso a datos del consentimiento de los avisos por WhatsApp (tabla ConsentimientoWhatsApp, migración
// migrations/add_consentimiento_whatsapp.sql). Las fechas son UTC y se guardan como DATETIME2(3).
public class ConsentimientoWhatsAppDAO : IConsentimientoWhatsAppRepositorio
{
    private const string Columnas = @"IdConsentimientoWhatsApp, IdPaciente, IdUsuario, Telefono, Origen, VersionTexto, HashTexto,
                                      AceptadoUtc, RevocadoUtc, MotivoRevocacion";

    public ConsentimientoWhatsAppFila? Ultimo(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand($@"
            SELECT TOP 1 {Columnas}
            FROM ConsentimientoWhatsApp
            WHERE IdPaciente = @IdPaciente
            ORDER BY IdConsentimientoWhatsApp DESC", con);
        cmd.Parameters.AddWithValue("@IdPaciente", idPaciente);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public ConsentimientoWhatsAppFila? Aceptar(int idPaciente, int idUsuario, string telefono, string origen, string versionTexto,
        string hashTexto, DateTime aceptadoUtc, string motivoSiReemplaza)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        // Cierra la abierta (si hay) y abre la nueva, todo o nada. Si dos pedidos llegan a la vez, el índice único filtrado
        // (UQ_ConsentimientoWhatsApp_Abierto) deja pasar a uno solo: el otro revienta con 2601/2627, se deshace y devuelve null.
        var cmd = new SqlCommand(@"
            SET XACT_ABORT ON;
            BEGIN TRAN;
            UPDATE ConsentimientoWhatsApp
            SET RevocadoUtc = CASE WHEN @Ahora < AceptadoUtc THEN AceptadoUtc ELSE @Ahora END, MotivoRevocacion = @MotivoSiReemplaza
            WHERE IdPaciente = @IdPaciente AND RevocadoUtc IS NULL;
            INSERT INTO ConsentimientoWhatsApp (IdPaciente, IdUsuario, Telefono, Origen, VersionTexto, HashTexto, AceptadoUtc)
            OUTPUT INSERTED.IdConsentimientoWhatsApp
            VALUES (@IdPaciente, @IdUsuario, @Telefono, @Origen, @VersionTexto, @HashTexto, @Ahora);
            COMMIT;", con);
        cmd.Parameters.AddWithValue("@IdPaciente", idPaciente);
        cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
        cmd.Parameters.AddWithValue("@Telefono", telefono);
        cmd.Parameters.AddWithValue("@Origen", origen);
        cmd.Parameters.AddWithValue("@VersionTexto", versionTexto);
        cmd.Parameters.AddWithValue("@HashTexto", hashTexto);
        cmd.Parameters.AddWithValue("@MotivoSiReemplaza", motivoSiReemplaza);
        cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = aceptadoUtc;
        try
        {
            var id = (int)cmd.ExecuteScalar()!;
            return new ConsentimientoWhatsAppFila(id, idPaciente, idUsuario, telefono, origen, versionTexto, hashTexto, aceptadoUtc, null, null);
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            return null;
        }
    }

    public bool Revocar(int idPaciente, DateTime revocadoUtc, string motivo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        // Nunca antes de la aceptación (la restricción de la tabla lo exigiría): con relojes un poco corridos se usa la hora de la aceptación.
        var cmd = new SqlCommand(@"
            UPDATE ConsentimientoWhatsApp
            SET RevocadoUtc = CASE WHEN @Ahora < AceptadoUtc THEN AceptadoUtc ELSE @Ahora END, MotivoRevocacion = @Motivo
            WHERE IdPaciente = @IdPaciente AND RevocadoUtc IS NULL", con);
        cmd.Parameters.AddWithValue("@IdPaciente", idPaciente);
        cmd.Parameters.AddWithValue("@Motivo", motivo);
        cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = revocadoUtc;
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<(int IdConsentimientoWhatsApp, int IdPaciente)> RevocarVigentesDelTelefono(string telefono, DateTime revocadoUtc, string motivo)
    {
        var revocadas = new List<(int, int)>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE ConsentimientoWhatsApp
            SET RevocadoUtc = CASE WHEN @Ahora < AceptadoUtc THEN AceptadoUtc ELSE @Ahora END, MotivoRevocacion = @Motivo
            OUTPUT INSERTED.IdConsentimientoWhatsApp, INSERTED.IdPaciente
            WHERE Telefono = @Telefono AND RevocadoUtc IS NULL", con);
        cmd.Parameters.AddWithValue("@Telefono", telefono);
        cmd.Parameters.AddWithValue("@Motivo", motivo);
        cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = revocadoUtc;
        using var r = cmd.ExecuteReader();
        while (r.Read()) revocadas.Add((r.GetInt32(0), r.GetInt32(1)));
        return revocadas;
    }

    public DatosPacienteParaWhatsApp? ObtenerDatosPaciente(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT p.Telefono, p.FechaNacimiento,
                   CASE WHEN EXISTS (SELECT 1 FROM PacienteTutor pt WHERE pt.IdPaciente = p.IdPaciente) THEN 1 ELSE 0 END AS TieneTutor
            FROM Paciente p
            WHERE p.IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DatosPacienteParaWhatsApp(
            r["Telefono"] == DBNull.Value ? null : (string)r["Telefono"],
            r["FechaNacimiento"] == DBNull.Value ? null : (DateTime)r["FechaNacimiento"],
            (int)r["TieneTutor"] == 1);
    }

    private static ConsentimientoWhatsAppFila Mapear(SqlDataReader r) => new(
        (int)r["IdConsentimientoWhatsApp"], (int)r["IdPaciente"], (int)r["IdUsuario"], ((string)r["Telefono"]).Trim(),
        (string)r["Origen"], (string)r["VersionTexto"], ((string)r["HashTexto"]).Trim(),
        DateTime.SpecifyKind((DateTime)r["AceptadoUtc"], DateTimeKind.Utc),
        r["RevocadoUtc"] == DBNull.Value ? null : DateTime.SpecifyKind((DateTime)r["RevocadoUtc"], DateTimeKind.Utc),
        r["MotivoRevocacion"] == DBNull.Value ? null : (string)r["MotivoRevocacion"]);
}
