using Microsoft.Data.SqlClient;

public class NotaInternaConversacionDAO
{
    public List<NotaInternaConversacion> ListarPorConversacion(int idConversacion)
    {
        var lista = new List<NotaInternaConversacion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT n.IdNota, n.IdConversacion, n.Texto, n.IdUsuario, n.FechaCreacion, u.Username AS NombreUsuario
            FROM NotaInternaConversacion n
            LEFT JOIN Usuario u ON u.IdUsuario = n.IdUsuario
            WHERE n.IdConversacion = @IdConv
            ORDER BY n.IdNota DESC", con);
        cmd.Parameters.AddWithValue("@IdConv", idConversacion);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new NotaInternaConversacion
            {
                IdNota         = (int)r["IdNota"],
                IdConversacion = (int)r["IdConversacion"],
                Texto          = (string)r["Texto"],
                IdUsuario      = r["IdUsuario"] == DBNull.Value ? null : (int)r["IdUsuario"],
                NombreUsuario  = r["NombreUsuario"] == DBNull.Value ? null : (string)r["NombreUsuario"],
                FechaCreacion  = (DateTime)r["FechaCreacion"]
            });
        return lista;
    }

    public int Insertar(int idConversacion, string texto, int? idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO NotaInternaConversacion (IdConversacion, Texto, IdUsuario) VALUES (@IdConv, @Texto, @IdUsuario);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@IdConv", idConversacion);
        cmd.Parameters.AddWithValue("@Texto", texto);
        cmd.Parameters.AddWithValue("@IdUsuario", (object?)idUsuario ?? DBNull.Value);
        return (int)cmd.ExecuteScalar()!;
    }

    // Solo quien la escribió puede borrarla (o un Configurador, resuelto en el endpoint).
    public (int IdConversacion, int? IdAutor)? ObtenerAutor(int idNota)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdConversacion, IdUsuario FROM NotaInternaConversacion WHERE IdNota = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idNota);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ((int)r["IdConversacion"], r["IdUsuario"] == DBNull.Value ? null : (int)r["IdUsuario"]);
    }

    public void Borrar(int idNota)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("DELETE FROM NotaInternaConversacion WHERE IdNota = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idNota);
        cmd.ExecuteNonQuery();
    }
}
