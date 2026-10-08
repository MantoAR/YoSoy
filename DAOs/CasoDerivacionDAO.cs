using Microsoft.Data.SqlClient;

public class CasoDerivacionDAO
{
    private const string SELECT_DETALLE = @"
        SELECT cd.IdCasoDerivacion, cd.IdCaso, pac.Nombre + ISNULL(' ' + pac.Dni, '') AS NombrePaciente,
               cd.IdGrupo, g.Nombre AS NombreGrupo,
               cd.IdProfesionalSugerido, ps.Nombre + ISNULL(' ' + ps.Apellido, '') AS NombreProfesionalSugerido,
               cd.IdProfesionalAsignado, pa.Nombre + ISNULL(' ' + pa.Apellido, '') AS NombreProfesionalAsignado,
               cd.Origen, cd.Estado, cd.Motivo, cd.Nota, cd.FechaCreacion
        FROM CasoDerivacion cd
        JOIN Caso c               ON c.IdCaso = cd.IdCaso
        JOIN Paciente pac         ON pac.IdPaciente = c.IdPaciente
        JOIN Grupo g              ON g.IdGrupo = cd.IdGrupo
        LEFT JOIN Profesional ps  ON ps.IdProfesional = cd.IdProfesionalSugerido
        LEFT JOIN Profesional pa  ON pa.IdProfesional = cd.IdProfesionalAsignado";

    public int Insertar(CasoDerivacion d)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO CasoDerivacion
                (IdCaso, IdGrupo, IdProfesionalSugerido, IdProfesionalAsignado, Origen, Estado, Motivo, Nota, IdCreadoPor, FechaCreacion, FechaResolucion)
            OUTPUT INSERTED.IdCasoDerivacion
            VALUES (@IdCaso, @IdGrupo, @IdProfSug, @IdProfAsig, @Origen, @Estado, @Motivo, @Nota, @IdCreadoPor, @Fecha, @FechaResolucion)", con);
        cmd.Parameters.AddWithValue("@IdCaso", d.IdCaso);
        cmd.Parameters.AddWithValue("@IdGrupo", d.IdGrupo);
        cmd.Parameters.AddWithValue("@IdProfSug", (object?)d.IdProfesionalSugerido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdProfAsig", (object?)d.IdProfesionalAsignado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Origen", d.Origen);
        cmd.Parameters.AddWithValue("@Estado", d.Estado);
        cmd.Parameters.AddWithValue("@FechaResolucion", (object?)d.FechaResolucion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Motivo", (object?)d.Motivo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Nota", (object?)d.Nota ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdCreadoPor", (object?)d.IdCreadoPor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Fecha", d.FechaCreacion == default ? DateTime.Now : d.FechaCreacion);
        return (int)cmd.ExecuteScalar();
    }

    // Confirmar / corregir / rechazar: siempre cierra con una fecha de resolución.
    public void Resolver(int id, string estado, int? idGrupo, int? idProfesionalAsignado)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE CasoDerivacion
            SET Estado = @Estado, IdGrupo = @IdGrupo, IdProfesionalAsignado = @IdProf, FechaResolucion = GETDATE()
            WHERE IdCasoDerivacion = @Id", con);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@IdGrupo", idGrupo);
        cmd.Parameters.AddWithValue("@IdProf", (object?)idProfesionalAsignado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public CasoDerivacion? ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdCasoDerivacion, IdCaso, IdGrupo, IdProfesionalSugerido, IdProfesionalAsignado,
                   Origen, Estado, Motivo, Nota, IdCreadoPor, FechaCreacion, FechaResolucion
            FROM CasoDerivacion WHERE IdCasoDerivacion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    // La última (si hay varias históricas) CasoDerivacion todavía abierta de un caso.
    public CasoDerivacion? ObtenerPendientePorCaso(int idCaso)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT TOP 1 IdCasoDerivacion, IdCaso, IdGrupo, IdProfesionalSugerido, IdProfesionalAsignado,
                   Origen, Estado, Motivo, Nota, IdCreadoPor, FechaCreacion, FechaResolucion
            FROM CasoDerivacion WHERE IdCaso = @IdCaso AND Estado = 'Pendiente de confirmar'
            ORDER BY FechaCreacion DESC", con);
        cmd.Parameters.AddWithValue("@IdCaso", idCaso);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public List<CasoDerivacionDetalle> ObtenerPendientes()
    {
        var lista = new List<CasoDerivacionDetalle>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_DETALLE + " WHERE cd.Estado = 'Pendiente de confirmar' ORDER BY cd.FechaCreacion DESC", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(MapearDetalle(r));
        return lista;
    }

    public List<CasoDerivacionDetalle> ObtenerPorCaso(int idCaso)
    {
        var lista = new List<CasoDerivacionDetalle>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_DETALLE + " WHERE cd.IdCaso = @IdCaso ORDER BY cd.FechaCreacion DESC", con);
        cmd.Parameters.AddWithValue("@IdCaso", idCaso);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(MapearDetalle(r));
        return lista;
    }

    private CasoDerivacion Mapear(SqlDataReader r) => new CasoDerivacion
    {
        IdCasoDerivacion      = (int)r["IdCasoDerivacion"],
        IdCaso                = (int)r["IdCaso"],
        IdGrupo               = (int)r["IdGrupo"],
        IdProfesionalSugerido = r["IdProfesionalSugerido"] == DBNull.Value ? null : (int)r["IdProfesionalSugerido"],
        IdProfesionalAsignado = r["IdProfesionalAsignado"] == DBNull.Value ? null : (int)r["IdProfesionalAsignado"],
        Origen                = (string)r["Origen"],
        Estado                = (string)r["Estado"],
        Motivo                = r["Motivo"] == DBNull.Value ? null : (string)r["Motivo"],
        Nota                  = r["Nota"] == DBNull.Value ? null : (string)r["Nota"],
        IdCreadoPor           = r["IdCreadoPor"] == DBNull.Value ? null : (int)r["IdCreadoPor"],
        FechaCreacion         = (DateTime)r["FechaCreacion"],
        FechaResolucion       = r["FechaResolucion"] == DBNull.Value ? null : (DateTime)r["FechaResolucion"]
    };

    private CasoDerivacionDetalle MapearDetalle(SqlDataReader r) => new CasoDerivacionDetalle
    {
        IdCasoDerivacion          = (int)r["IdCasoDerivacion"],
        IdCaso                    = (int)r["IdCaso"],
        NombrePaciente            = (string)r["NombrePaciente"],
        IdGrupo                   = (int)r["IdGrupo"],
        NombreGrupo               = (string)r["NombreGrupo"],
        IdProfesionalSugerido     = r["IdProfesionalSugerido"] == DBNull.Value ? null : (int)r["IdProfesionalSugerido"],
        NombreProfesionalSugerido = r["NombreProfesionalSugerido"] == DBNull.Value ? null : (string)r["NombreProfesionalSugerido"],
        IdProfesionalAsignado     = r["IdProfesionalAsignado"] == DBNull.Value ? null : (int)r["IdProfesionalAsignado"],
        NombreProfesionalAsignado = r["NombreProfesionalAsignado"] == DBNull.Value ? null : (string)r["NombreProfesionalAsignado"],
        Origen                    = (string)r["Origen"],
        Estado                    = (string)r["Estado"],
        Motivo                    = r["Motivo"] == DBNull.Value ? null : (string)r["Motivo"],
        Nota                      = r["Nota"] == DBNull.Value ? null : (string)r["Nota"],
        FechaCreacion             = (DateTime)r["FechaCreacion"]
    };
}
