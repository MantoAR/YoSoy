using Microsoft.Data.SqlClient;

public class RespuestaRapidaDAO
{
    // Lo que usa el composer: las globales + las propias de ese profesional (null =
    // Configurador, que solo tiene las globales). Globales primero.
    public List<RespuestaRapida> ObtenerParaUso(int? idProfesional)
    {
        var lista = new List<RespuestaRapida>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdRespuestaRapida, Titulo, Texto, IdProfesional, Activa FROM RespuestaRapida
            WHERE Activa = 1 AND (IdProfesional IS NULL OR IdProfesional = @IdProfesional)
            ORDER BY CASE WHEN IdProfesional IS NULL THEN 0 ELSE 1 END, Titulo", con);
        cmd.Parameters.AddWithValue("@IdProfesional", (object?)idProfesional ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    public RespuestaRapida? ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdRespuestaRapida, Titulo, Texto, IdProfesional, Activa FROM RespuestaRapida WHERE IdRespuestaRapida = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public int Insertar(RespuestaRapida p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO RespuestaRapida (Titulo, Texto, IdProfesional) VALUES (@Titulo, @Texto, @IdProfesional);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Titulo", p.Titulo);
        cmd.Parameters.AddWithValue("@Texto", p.Texto);
        cmd.Parameters.AddWithValue("@IdProfesional", (object?)p.IdProfesional ?? DBNull.Value);
        return (int)cmd.ExecuteScalar()!;
    }

    public void Modificar(RespuestaRapida p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE RespuestaRapida SET Titulo = @Titulo, Texto = @Texto WHERE IdRespuestaRapida = @Id", con);
        cmd.Parameters.AddWithValue("@Titulo", p.Titulo);
        cmd.Parameters.AddWithValue("@Texto", p.Texto);
        cmd.Parameters.AddWithValue("@Id", p.IdRespuestaRapida);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE RespuestaRapida SET Activa = 0 WHERE IdRespuestaRapida = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    private static RespuestaRapida Mapear(SqlDataReader r) => new()
    {
        IdRespuestaRapida = (int)r["IdRespuestaRapida"],
        Titulo            = (string)r["Titulo"],
        Texto             = (string)r["Texto"],
        IdProfesional     = r["IdProfesional"] == DBNull.Value ? null : (int)r["IdProfesional"],
        Activa            = (bool)r["Activa"]
    };
}
