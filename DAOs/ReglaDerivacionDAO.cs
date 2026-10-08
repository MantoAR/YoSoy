using Microsoft.Data.SqlClient;

// Criterio mínimo viable: especialidad solicitada en la admisión -> grupo destino.
public class ReglaDerivacionDAO
{
    public int Insertar(ReglaDerivacion r)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO ReglaDerivacion (IdEspecialidad, IdGrupoDestino, Prioridad, Activa) OUTPUT INSERTED.IdRegla
            VALUES (@IdEsp, @IdGrupo, @Prioridad, @Activa)", con);
        cmd.Parameters.AddWithValue("@IdEsp", r.IdEspecialidad);
        cmd.Parameters.AddWithValue("@IdGrupo", r.IdGrupoDestino);
        cmd.Parameters.AddWithValue("@Prioridad", r.Prioridad);
        cmd.Parameters.AddWithValue("@Activa", r.Activa);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(ReglaDerivacion r)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE ReglaDerivacion
            SET IdEspecialidad = @IdEsp, IdGrupoDestino = @IdGrupo, Prioridad = @Prioridad, Activa = @Activa
            WHERE IdRegla = @Id", con);
        cmd.Parameters.AddWithValue("@IdEsp", r.IdEspecialidad);
        cmd.Parameters.AddWithValue("@IdGrupo", r.IdGrupoDestino);
        cmd.Parameters.AddWithValue("@Prioridad", r.Prioridad);
        cmd.Parameters.AddWithValue("@Activa", r.Activa);
        cmd.Parameters.AddWithValue("@Id", r.IdRegla);
        cmd.ExecuteNonQuery();
    }

    public void CambiarActiva(int id, bool activa)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE ReglaDerivacion SET Activa = @Activa WHERE IdRegla = @Id", con);
        cmd.Parameters.AddWithValue("@Activa", activa);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<ReglaDerivacion> ObtenerTodas()
    {
        var lista = new List<ReglaDerivacion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdRegla, IdEspecialidad, IdGrupoDestino, Prioridad, Activa
            FROM ReglaDerivacion ORDER BY Prioridad DESC, IdRegla", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    // La de mayor prioridad entre las activas para esa especialidad; null si ninguna coincide.
    public int? ObtenerGrupoPorEspecialidad(int idEspecialidad)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT TOP 1 IdGrupoDestino FROM ReglaDerivacion
            WHERE IdEspecialidad = @IdEsp AND Activa = 1
            ORDER BY Prioridad DESC, IdRegla", con);
        cmd.Parameters.AddWithValue("@IdEsp", idEspecialidad);
        var resultado = cmd.ExecuteScalar();
        return resultado == null ? null : (int)resultado;
    }

    private ReglaDerivacion Mapear(SqlDataReader r) => new ReglaDerivacion
    {
        IdRegla        = (int)r["IdRegla"],
        IdEspecialidad = (int)r["IdEspecialidad"],
        IdGrupoDestino = (int)r["IdGrupoDestino"],
        Prioridad      = (int)r["Prioridad"],
        Activa         = (bool)r["Activa"]
    };
}
