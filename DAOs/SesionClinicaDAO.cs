using Microsoft.Data.SqlClient;
using System.Collections.Generic;

/// <summary>Lo que quedó guardado: la sesión, y si las emociones entraron (puede no pasar si el paciente revocó justo en el medio).</summary>
public record SesionInsertada(int IdSesion, bool EmocionesGuardadas);

public class SesionClinicaDAO
{
    // Inserta la sesión. Las emociones, si hay, se guardan CIFRADAS y el cifrado necesita el IdSesion (va como dato
    // asociado: así el dato no se puede copiar a otra fila): por eso van en un segundo paso, dentro de la misma
    // transacción. cifrarEmociones recibe el IdSesion recién creado y devuelve lo que se guarda (o null para no guardar).
    public SesionInsertada Insertar(SesionClinica s, Func<int, string?>? cifrarEmociones = null)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        var insertada = InsertarEn(con, tx, s, cifrarEmociones);
        tx.Commit();
        return insertada;
    }

    // El mismo guardado, dentro de una transacción que abre y cierra quien llama (el cierre de sesión suma el turno, el caso y la auditoría
    // a la misma transacción). Con ClaveCierre guarda también la clave y el momento en que se marcó el turno: columnas de la migración 26.
    public SesionInsertada InsertarEn(SqlConnection con, SqlTransaction tx, SesionClinica s, Func<int, string?>? cifrarEmociones = null)
    {
        var conCierre = s.ClaveCierre is not null;
        var cmd = new SqlCommand(
            @"INSERT INTO SesionClinica
                (IdTurno, IdPaciente, IdProfesional, FechaHora, Transcripcion, NotasEvolucion, TipoCierre"
              + (conCierre ? ", ClaveCierre, TurnoRealizadoUtc" : "") + @")
              VALUES
                (@IdTurno, @IdPaciente, @IdProfesional, @FechaHora, @Transcripcion, @NotasEvolucion, @TipoCierre"
              + (conCierre ? ", @ClaveCierre, @TurnoRealizadoUtc" : "") + @");
              SELECT CAST(SCOPE_IDENTITY() AS INT);", con, tx);
        if (conCierre)
        {
            cmd.Parameters.AddWithValue("@ClaveCierre",       s.ClaveCierre!.Value);
            cmd.Parameters.AddWithValue("@TurnoRealizadoUtc", (object?)s.TurnoRealizadoUtc ?? DBNull.Value);
        }
        cmd.Parameters.AddWithValue("@IdTurno",            (object)s.IdTurno           ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdPaciente",         s.IdPaciente);
        cmd.Parameters.AddWithValue("@IdProfesional",      s.IdProfesional);
        cmd.Parameters.AddWithValue("@FechaHora",          s.FechaHora);
        cmd.Parameters.AddWithValue("@Transcripcion",      (object)s.Transcripcion      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NotasEvolucion",     (object)s.NotasEvolucion     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TipoCierre",         (object)s.TipoCierre         ?? DBNull.Value);
        var id = (int)cmd.ExecuteScalar();

        var guardadas = false;
        var emociones = cifrarEmociones?.Invoke(id);
        if (emociones is not null)
        {
            // El consentimiento se vuelve a mirar en la MISMA sentencia que escribe las emociones, con su fila bloqueada: si
            // el paciente revocó entre la validación del servicio y acá, no se escribe nada; y una revocación que llegue
            // después espera a que esta transacción termine, así que encuentra la sesión ya guardada y la anula.
            using var upd = new SqlCommand(@"
                UPDATE SesionClinica
                SET HistorialEmociones = @Emociones, IdConsentimientoVideo = @IdConsentimiento
                WHERE IdSesion = @Id
                  AND EXISTS (SELECT 1 FROM ConsentimientoVideo WITH (UPDLOCK, HOLDLOCK)
                              WHERE IdConsentimientoVideo = @IdConsentimiento AND Decision = 'acepta' AND RevocadoUtc IS NULL)",
                con, tx);
            upd.Parameters.AddWithValue("@Emociones", emociones);
            upd.Parameters.AddWithValue("@IdConsentimiento", (object?)s.IdConsentimientoVideo ?? DBNull.Value);
            upd.Parameters.AddWithValue("@Id", id);
            guardadas = upd.ExecuteNonQuery() == 1;
        }

        return new SesionInsertada(id, guardadas);
    }

    public List<SesionClinica> ObtenerPorPaciente(int idPaciente)
    {
        var lista = new List<SesionClinica>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"SELECT IdSesion, IdTurno, IdPaciente, IdProfesional, FechaHora,
                     Transcripcion, NotasEvolucion, HistorialEmociones, TipoCierre,
                     IdConsentimientoVideo, EmocionesAnuladasUtc, EmocionesAnuladasMotivo
              FROM SesionClinica
              WHERE IdPaciente = @Id
              ORDER BY FechaHora DESC", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) lista.Add(Mapear(reader));
        VinculoSesionEvolucionDAO.Completar(lista, "IdPaciente", idPaciente);
        return lista;
    }

    public List<SesionClinica> ObtenerPorProfesional(int idProfesional)
    {
        var lista = new List<SesionClinica>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            @"SELECT IdSesion, IdTurno, IdPaciente, IdProfesional, FechaHora,
                     Transcripcion, NotasEvolucion, HistorialEmociones, TipoCierre,
                     IdConsentimientoVideo, EmocionesAnuladasUtc, EmocionesAnuladasMotivo
              FROM SesionClinica
              WHERE IdProfesional = @Id
              ORDER BY FechaHora DESC", con);
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) lista.Add(Mapear(reader));
        VinculoSesionEvolucionDAO.Completar(lista, "IdProfesional", idProfesional);
        return lista;
    }

    private SesionClinica Mapear(SqlDataReader r) => new SesionClinica
    {
        IdSesion           = (int)r["IdSesion"],
        IdTurno            = r["IdTurno"] == DBNull.Value ? null : (int?)r["IdTurno"],
        IdPaciente         = (int)r["IdPaciente"],
        IdProfesional      = (int)r["IdProfesional"],
        FechaHora          = (DateTime)r["FechaHora"],
        Transcripcion      = r["Transcripcion"]      == DBNull.Value ? null : (string)r["Transcripcion"],
        NotasEvolucion     = r["NotasEvolucion"]     == DBNull.Value ? null : (string)r["NotasEvolucion"],
        HistorialEmociones = r["HistorialEmociones"] == DBNull.Value ? null : (string)r["HistorialEmociones"],
        TipoCierre         = r["TipoCierre"]         == DBNull.Value ? null : (string)r["TipoCierre"],
        IdConsentimientoVideo   = r["IdConsentimientoVideo"]   == DBNull.Value ? null : (int?)r["IdConsentimientoVideo"],
        EmocionesAnuladasUtc    = r["EmocionesAnuladasUtc"]    == DBNull.Value ? null : DateTime.SpecifyKind((DateTime)r["EmocionesAnuladasUtc"], DateTimeKind.Utc),
        EmocionesAnuladasMotivo = r["EmocionesAnuladasMotivo"] == DBNull.Value ? null : (string)r["EmocionesAnuladasMotivo"]
    };
}
