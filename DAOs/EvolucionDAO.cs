using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class EvolucionDAO
{
    public void Insertar(Evolucion e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Evolucion (IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento) VALUES (@IdHistoria, @IdProfesional, @IdTurno, @Fecha, @Diagnostico, @Observaciones, @PlanTratamiento)", con);
        cmd.Parameters.AddWithValue("@IdHistoria",      e.IdHistoria);
        cmd.Parameters.AddWithValue("@IdProfesional",   e.IdProfesional);
        cmd.Parameters.AddWithValue("@IdTurno",         (object)e.IdTurno        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Fecha",           e.Fecha);
        cmd.Parameters.AddWithValue("@Diagnostico",     (object)e.Diagnostico    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Observaciones",   (object)e.Observaciones  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlanTratamiento", (object)e.PlanTratamiento ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Evolucion e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Evolucion SET IdHistoria = @IdHistoria, IdProfesional = @IdProfesional, IdTurno = @IdTurno, Fecha = @Fecha, Diagnostico = @Diagnostico, Observaciones = @Observaciones, PlanTratamiento = @PlanTratamiento WHERE IdEvolucion = @Id", con);
        cmd.Parameters.AddWithValue("@IdHistoria",      e.IdHistoria);
        cmd.Parameters.AddWithValue("@IdProfesional",   e.IdProfesional);
        cmd.Parameters.AddWithValue("@IdTurno",         (object)e.IdTurno        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Fecha",           e.Fecha);
        cmd.Parameters.AddWithValue("@Diagnostico",     (object)e.Diagnostico    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Observaciones",   (object)e.Observaciones  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlanTratamiento", (object)e.PlanTratamiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",              e.IdEvolucion);
        cmd.ExecuteNonQuery();
    }

    public List<Evolucion> ObtenerTodos()
    {
        var lista = new List<Evolucion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEvolucion, IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento, TipoCierre, Enfoque, ModoRegistro, ContenidoJson, CodigoSala, Estado, FechaFirma, HashContenido FROM Evolucion", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Evolucion ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEvolucion, IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento, TipoCierre, Enfoque, ModoRegistro, ContenidoJson, CodigoSala, Estado, FechaFirma, HashContenido FROM Evolucion WHERE IdEvolucion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<Evolucion> ObtenerPorHistoria(int idHistoria)
    {
        var lista = new List<Evolucion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdEvolucion, IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento, TipoCierre, Enfoque, ModoRegistro, ContenidoJson, CodigoSala, Estado, FechaFirma, HashContenido FROM Evolucion WHERE IdHistoria = @Id ORDER BY Fecha DESC", con);
        cmd.Parameters.AddWithValue("@Id", idHistoria);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private Evolucion Mapear(SqlDataReader r) => new Evolucion
    {
        IdEvolucion     = (int)r["IdEvolucion"],
        IdHistoria      = (int)r["IdHistoria"],
        IdProfesional   = (int)r["IdProfesional"],
        IdTurno         = r["IdTurno"]         == DBNull.Value ? null : (int?)r["IdTurno"],
        Fecha           = (DateTime)r["Fecha"],
        Diagnostico     = r["Diagnostico"]     == DBNull.Value ? null : (string)r["Diagnostico"],
        Observaciones   = r["Observaciones"]   == DBNull.Value ? null : (string)r["Observaciones"],
        PlanTratamiento = r["PlanTratamiento"] == DBNull.Value ? null : (string)r["PlanTratamiento"],
        TipoCierre      = r["TipoCierre"]      == DBNull.Value ? null : (string)r["TipoCierre"],
        Enfoque         = r["Enfoque"]         == DBNull.Value ? null : (string)r["Enfoque"],
        ModoRegistro    = r["ModoRegistro"]    == DBNull.Value ? null : (string)r["ModoRegistro"],
        ContenidoJson   = r["ContenidoJson"]   == DBNull.Value ? null : (string)r["ContenidoJson"],
        CodigoSala      = r["CodigoSala"]      == DBNull.Value ? null : (string)r["CodigoSala"],
        Estado          = (string)r["Estado"],
        FechaFirma      = r["FechaFirma"]      == DBNull.Value ? null : (DateTime?)r["FechaFirma"],
        HashContenido   = r["HashContenido"]   == DBNull.Value ? null : (string)r["HashContenido"]
    };

    // ── Evolución por enfoque: borrador, firma y addenda (RN-11, RN-16) ──────────

    /// <summary>Inserta una evolución nueva (borrador o ya firmada) y devuelve su id.</summary>
    public int InsertarCompleta(Evolucion e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO Evolucion (IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento,
                                   TipoCierre, Enfoque, ModoRegistro, ContenidoJson, CodigoSala, Estado, FechaFirma, HashContenido)
            VALUES (@IdHistoria, @IdProfesional, @IdTurno, @Fecha, @Diagnostico, @Observaciones, @PlanTratamiento,
                    @TipoCierre, @Enfoque, @Modo, @Contenido, @Sala, @Estado, @FechaFirma, @Hash);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        Parametros(cmd, e);
        return (int)cmd.ExecuteScalar();
    }

    /// <summary>Reemplaza un borrador. No toca nada si la evolución ya está firmada: devuelve false.</summary>
    public bool ActualizarBorrador(Evolucion e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Evolucion SET IdTurno = @IdTurno, Fecha = @Fecha, Diagnostico = @Diagnostico, Observaciones = @Observaciones,
                   PlanTratamiento = @PlanTratamiento, TipoCierre = @TipoCierre, Enfoque = @Enfoque, ModoRegistro = @Modo,
                   ContenidoJson = @Contenido, CodigoSala = @Sala, Estado = @Estado, FechaFirma = @FechaFirma, HashContenido = @Hash
            WHERE IdEvolucion = @Id AND Estado = 'Borrador' AND IdProfesional = @IdProfesional AND IdHistoria = @IdHistoria", con);
        Parametros(cmd, e);
        cmd.Parameters.AddWithValue("@Id", e.IdEvolucion);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static void Parametros(SqlCommand cmd, Evolucion e)
    {
        cmd.Parameters.AddWithValue("@IdHistoria",      e.IdHistoria);
        cmd.Parameters.AddWithValue("@IdProfesional",   e.IdProfesional);
        cmd.Parameters.AddWithValue("@IdTurno",         (object?)e.IdTurno         ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Fecha",           e.Fecha);
        cmd.Parameters.AddWithValue("@Diagnostico",     (object?)e.Diagnostico     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Observaciones",   (object?)e.Observaciones   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlanTratamiento", (object?)e.PlanTratamiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TipoCierre",      (object?)e.TipoCierre      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Enfoque",         (object?)e.Enfoque         ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Modo",            (object?)e.ModoRegistro    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Contenido",       (object?)e.ContenidoJson   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sala",            (object?)e.CodigoSala      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",          e.Estado);
        cmd.Parameters.AddWithValue("@FechaFirma",      (object?)e.FechaFirma      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Hash",            (object?)e.HashContenido   ?? DBNull.Value);
    }

    public int InsertarAddendum(int idEvolucion, int? idProfesional, int idUsuario, string texto)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO EvolucionAddendum (IdEvolucion, IdProfesional, IdUsuario, Texto) VALUES (@Ev, @Prof, @Usu, @Texto);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Ev", idEvolucion);
        cmd.Parameters.AddWithValue("@Prof", (object?)idProfesional ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Usu", idUsuario);
        cmd.Parameters.AddWithValue("@Texto", texto);
        return (int)cmd.ExecuteScalar();
    }

    public List<object> ObtenerAddendaPorHistoria(int idHistoria)
    {
        var lista = new List<object>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT a.IdAddendum, a.IdEvolucion, a.Texto, a.Fecha,
                   ISNULL(p.Nombre + ' ' + ISNULL(p.Apellido, ''), 'Coordinación') AS Autor
            FROM EvolucionAddendum a
            JOIN Evolucion e ON e.IdEvolucion = a.IdEvolucion
            LEFT JOIN Profesional p ON p.IdProfesional = a.IdProfesional
            WHERE e.IdHistoria = @Id ORDER BY a.Fecha", con);
        cmd.Parameters.AddWithValue("@Id", idHistoria);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new
            {
                idAddendum = (int)r["IdAddendum"], idEvolucion = (int)r["IdEvolucion"],
                texto = (string)r["Texto"], fecha = (DateTime)r["Fecha"], autor = ((string)r["Autor"]).Trim()
            });
        return lista;
    }
}
