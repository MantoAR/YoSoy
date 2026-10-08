using Microsoft.Data.SqlClient;
using System.Text;

public class CasoDAO
{
    private const string SELECT_DETALLE = @"
        SELECT c.IdCaso, c.IdPaciente, pac.Nombre + ISNULL(' ' + pac.Dni, '') AS NombrePaciente,
               c.Estado, c.IdGrupoActual, g.Nombre AS NombreGrupo,
               c.IdProfesionalAsignado, prof.Nombre + ISNULL(' ' + prof.Apellido, '') AS NombreProfesionalAsignado,
               c.Origen, c.FechaCreacion, c.IdInforme
        FROM Caso c
        JOIN Paciente pac        ON pac.IdPaciente = c.IdPaciente
        LEFT JOIN Grupo g        ON g.IdGrupo = c.IdGrupoActual
        LEFT JOIN Profesional prof ON prof.IdProfesional = c.IdProfesionalAsignado";

    public int Insertar(Caso c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO Caso (IdPaciente, Estado, IdGrupoActual, IdProfesionalAsignado, Origen, FechaCreacion, IdInforme)
            OUTPUT INSERTED.IdCaso
            VALUES (@IdPaciente, @Estado, @IdGrupo, @IdProf, @Origen, @Fecha, @IdInforme)", con);
        cmd.Parameters.AddWithValue("@IdPaciente", c.IdPaciente);
        cmd.Parameters.AddWithValue("@Estado", c.Estado);
        cmd.Parameters.AddWithValue("@IdGrupo", (object?)c.IdGrupoActual ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdProf", (object?)c.IdProfesionalAsignado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Origen", c.Origen);
        cmd.Parameters.AddWithValue("@Fecha", c.FechaCreacion == default ? DateTime.Now : c.FechaCreacion);
        cmd.Parameters.AddWithValue("@IdInforme", (object?)c.IdInforme ?? DBNull.Value);
        return (int)cmd.ExecuteScalar();
    }

    // Para derivar/asignar/cerrar: cambia solo lo que importa, no pisa el resto de la fila.
    public void ActualizarEstadoYAsignacion(int idCaso, string estado, int? idGrupo, int? idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Caso SET Estado = @Estado, IdGrupoActual = @IdGrupo, IdProfesionalAsignado = @IdProf
            WHERE IdCaso = @Id", con);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@IdGrupo", (object?)idGrupo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdProf", (object?)idProfesional ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", idCaso);
        cmd.ExecuteNonQuery();
    }

    public Caso? ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdCaso, IdPaciente, Estado, IdGrupoActual, IdProfesionalAsignado, Origen, FechaCreacion, IdInforme
            FROM Caso WHERE IdCaso = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    // El más reciente no Cerrado de un paciente (debería haber a lo sumo uno "en curso"
    // por vez). Lo usan la sincronización con Solicitudes de cambio y el panel del lead.
    public Caso? ObtenerAbiertoPorPaciente(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT TOP 1 IdCaso, IdPaciente, Estado, IdGrupoActual, IdProfesionalAsignado, Origen, FechaCreacion, IdInforme
            FROM Caso WHERE IdPaciente = @IdPaciente AND Estado <> 'Cerrado'
            ORDER BY FechaCreacion DESC", con);
        cmd.Parameters.AddWithValue("@IdPaciente", idPaciente);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public Caso? ObtenerPorInforme(int idInforme)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdCaso, IdPaciente, Estado, IdGrupoActual, IdProfesionalAsignado, Origen, FechaCreacion, IdInforme
            FROM Caso WHERE IdInforme = @IdInforme", con);
        cmd.Parameters.AddWithValue("@IdInforme", idInforme);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public CasoDetalle? ObtenerDetalle(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_DETALLE + " WHERE c.IdCaso = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapearDetalle(r) : null;
    }

    // Filtros opcionales: estado, grupo, profesional, o sinAsignar (IdProfesionalAsignado NULL).
    public List<CasoDetalle> ObtenerConFiltros(string? estado = null, int? idGrupo = null, int? idProfesional = null, bool sinAsignar = false)
    {
        var where = new List<string>();
        var cmd = new SqlCommand();
        if (estado != null)        { where.Add("c.Estado = @Estado"); cmd.Parameters.AddWithValue("@Estado", estado); }
        if (idGrupo != null)       { where.Add("c.IdGrupoActual = @IdGrupo"); cmd.Parameters.AddWithValue("@IdGrupo", idGrupo); }
        if (idProfesional != null) { where.Add("c.IdProfesionalAsignado = @IdProf"); cmd.Parameters.AddWithValue("@IdProf", idProfesional); }
        if (sinAsignar)             where.Add("c.IdProfesionalAsignado IS NULL");

        var sql = new StringBuilder(SELECT_DETALLE);
        if (where.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", where));
        sql.Append(" ORDER BY c.FechaCreacion DESC");
        cmd.CommandText = sql.ToString();

        var lista = new List<CasoDetalle>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        cmd.Connection = con;
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(MapearDetalle(r));
        return lista;
    }

    // "Mis casos": los de un profesional, sin incluir los Cerrados.
    public List<CasoDetalle> ObtenerPorProfesional(int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_DETALLE + " WHERE c.IdProfesionalAsignado = @IdProf AND c.Estado <> 'Cerrado' ORDER BY c.FechaCreacion DESC", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        using var r = cmd.ExecuteReader();
        var lista = new List<CasoDetalle>();
        while (r.Read()) lista.Add(MapearDetalle(r));
        return lista;
    }

    private Caso Mapear(SqlDataReader r) => new Caso
    {
        IdCaso                = (int)r["IdCaso"],
        IdPaciente            = (int)r["IdPaciente"],
        Estado                = (string)r["Estado"],
        IdGrupoActual         = r["IdGrupoActual"] == DBNull.Value ? null : (int)r["IdGrupoActual"],
        IdProfesionalAsignado = r["IdProfesionalAsignado"] == DBNull.Value ? null : (int)r["IdProfesionalAsignado"],
        Origen                = (string)r["Origen"],
        FechaCreacion         = (DateTime)r["FechaCreacion"],
        IdInforme             = r["IdInforme"] == DBNull.Value ? null : (int)r["IdInforme"]
    };

    private CasoDetalle MapearDetalle(SqlDataReader r) => new CasoDetalle
    {
        IdCaso                    = (int)r["IdCaso"],
        IdPaciente                = (int)r["IdPaciente"],
        NombrePaciente            = (string)r["NombrePaciente"],
        Estado                    = (string)r["Estado"],
        IdGrupoActual             = r["IdGrupoActual"] == DBNull.Value ? null : (int)r["IdGrupoActual"],
        NombreGrupo               = r["NombreGrupo"] == DBNull.Value ? null : (string)r["NombreGrupo"],
        IdProfesionalAsignado     = r["IdProfesionalAsignado"] == DBNull.Value ? null : (int)r["IdProfesionalAsignado"],
        NombreProfesionalAsignado = r["NombreProfesionalAsignado"] == DBNull.Value ? null : (string)r["NombreProfesionalAsignado"],
        Origen                    = (string)r["Origen"],
        FechaCreacion             = (DateTime)r["FechaCreacion"],
        IdInforme                 = r["IdInforme"] == DBNull.Value ? null : (int)r["IdInforme"]
    };
}
