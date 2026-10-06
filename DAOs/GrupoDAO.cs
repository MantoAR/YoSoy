using Microsoft.Data.SqlClient;

public class GrupoDAO
{
    public int Insertar(Grupo g)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO Grupo (Nombre, Descripcion, Activo) OUTPUT INSERTED.IdGrupo
            VALUES (@Nombre, @Descripcion, @Activo)", con);
        cmd.Parameters.AddWithValue("@Nombre", g.Nombre);
        cmd.Parameters.AddWithValue("@Descripcion", (object?)g.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Activo", g.Activo);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(Grupo g)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Grupo SET Nombre = @Nombre, Descripcion = @Descripcion, Activo = @Activo
            WHERE IdGrupo = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre", g.Nombre);
        cmd.Parameters.AddWithValue("@Descripcion", (object?)g.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Activo", g.Activo);
        cmd.Parameters.AddWithValue("@Id", g.IdGrupo);
        cmd.ExecuteNonQuery();
    }

    public void Desactivar(int id) => CambiarActivo(id, false);
    public void Activar(int id) => CambiarActivo(id, true);

    private void CambiarActivo(int id, bool activo)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE Grupo SET Activo = @Activo WHERE IdGrupo = @Id", con);
        cmd.Parameters.AddWithValue("@Activo", activo);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Grupo> ObtenerTodos()
    {
        var lista = new List<Grupo>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdGrupo, Nombre, Descripcion, Activo FROM Grupo ORDER BY Nombre", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    public Grupo? ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdGrupo, Nombre, Descripcion, Activo FROM Grupo WHERE IdGrupo = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    // ── Miembros (ProfesionalGrupo) ──────────────────────────
    public List<ProfesionalResumen> ObtenerMiembros(int idGrupo)
    {
        var lista = new List<ProfesionalResumen>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT p.IdProfesional, p.IdEspecialidad,
                   p.Nombre + ISNULL(' ' + p.Apellido, '') AS Nombre,
                   e.Nombre AS Especialidad, p.Mail, p.Telefono
            FROM ProfesionalGrupo pg
            JOIN Profesional p  ON p.IdProfesional = pg.IdProfesional
            JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
            WHERE pg.IdGrupo = @IdGrupo
            ORDER BY p.Nombre", con);
        cmd.Parameters.AddWithValue("@IdGrupo", idGrupo);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new ProfesionalResumen
            {
                IdProfesional  = (int)r["IdProfesional"],
                IdEspecialidad = (int)r["IdEspecialidad"],
                Nombre         = (string)r["Nombre"],
                Especialidad   = (string)r["Especialidad"],
                Mail           = r["Mail"]     == DBNull.Value ? null : (string)r["Mail"],
                Telefono       = r["Telefono"] == DBNull.Value ? null : (string)r["Telefono"]
            });
        return lista;
    }

    public List<int> ObtenerGruposDeProfesional(int idProfesional)
    {
        var lista = new List<int>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdGrupo FROM ProfesionalGrupo WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add((int)r["IdGrupo"]);
        return lista;
    }

    public void AgregarMiembro(int idGrupo, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM ProfesionalGrupo WHERE IdGrupo = @IdGrupo AND IdProfesional = @IdProf)
                INSERT INTO ProfesionalGrupo (IdGrupo, IdProfesional) VALUES (@IdGrupo, @IdProf)", con);
        cmd.Parameters.AddWithValue("@IdGrupo", idGrupo);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.ExecuteNonQuery();
    }

    public void QuitarMiembro(int idGrupo, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("DELETE FROM ProfesionalGrupo WHERE IdGrupo = @IdGrupo AND IdProfesional = @IdProf", con);
        cmd.Parameters.AddWithValue("@IdGrupo", idGrupo);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.ExecuteNonQuery();
    }

    private Grupo Mapear(SqlDataReader r) => new Grupo
    {
        IdGrupo     = (int)r["IdGrupo"],
        Nombre      = (string)r["Nombre"],
        Descripcion = r["Descripcion"] == DBNull.Value ? null : (string)r["Descripcion"],
        Activo      = (bool)r["Activo"]
    };
}
