using Microsoft.Data.SqlClient;

public class EtiquetaConversacionDAO
{
    public List<EtiquetaConversacion> ObtenerActivas()
    {
        var lista = new List<EtiquetaConversacion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdEtiqueta, Nombre, Tono, Activa FROM EtiquetaConversacion WHERE Activa = 1 ORDER BY Nombre", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    public EtiquetaConversacion? ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdEtiqueta, Nombre, Tono, Activa FROM EtiquetaConversacion WHERE IdEtiqueta = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public int Insertar(EtiquetaConversacion e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO EtiquetaConversacion (Nombre, Tono) VALUES (@Nombre, @Tono);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Nombre", e.Nombre);
        cmd.Parameters.AddWithValue("@Tono", e.Tono);
        return (int)cmd.ExecuteScalar()!;
    }

    public void Modificar(EtiquetaConversacion e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE EtiquetaConversacion SET Nombre = @Nombre, Tono = @Tono WHERE IdEtiqueta = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre", e.Nombre);
        cmd.Parameters.AddWithValue("@Tono", e.Tono);
        cmd.Parameters.AddWithValue("@Id", e.IdEtiqueta);
        cmd.ExecuteNonQuery();
    }

    // Baja lógica: no se borra de verdad para no perder el historial de qué conversación
    // tuvo qué etiqueta (ConversacionEtiqueta no se toca).
    public void Desactivar(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE EtiquetaConversacion SET Activa = 0 WHERE IdEtiqueta = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<int> ObtenerIdsDeConversacion(int idConversacion)
    {
        var lista = new List<int>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdEtiqueta FROM ConversacionEtiqueta WHERE IdConversacion = @IdConv", con);
        cmd.Parameters.AddWithValue("@IdConv", idConversacion);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add((int)r["IdEtiqueta"]);
        return lista;
    }

    // Todas las conversaciones etiquetadas, para que la bandeja no pida una por una.
    public Dictionary<int, List<int>> ObtenerTodasLasAsignaciones()
    {
        var mapa = new Dictionary<int, List<int>>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdConversacion, IdEtiqueta FROM ConversacionEtiqueta", con);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int idConv = (int)r["IdConversacion"];
            if (!mapa.TryGetValue(idConv, out var l)) mapa[idConv] = l = new List<int>();
            l.Add((int)r["IdEtiqueta"]);
        }
        return mapa;
    }

    // Idempotente: no falla si ya estaba puesta.
    public void Agregar(int idConversacion, int idEtiqueta)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM ConversacionEtiqueta WHERE IdConversacion = @IdConv AND IdEtiqueta = @IdEtq)
                INSERT INTO ConversacionEtiqueta (IdConversacion, IdEtiqueta) VALUES (@IdConv, @IdEtq);", con);
        cmd.Parameters.AddWithValue("@IdConv", idConversacion);
        cmd.Parameters.AddWithValue("@IdEtq", idEtiqueta);
        cmd.ExecuteNonQuery();
    }

    public void Quitar(int idConversacion, int idEtiqueta)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("DELETE FROM ConversacionEtiqueta WHERE IdConversacion = @IdConv AND IdEtiqueta = @IdEtq", con);
        cmd.Parameters.AddWithValue("@IdConv", idConversacion);
        cmd.Parameters.AddWithValue("@IdEtq", idEtiqueta);
        cmd.ExecuteNonQuery();
    }

    private static EtiquetaConversacion Mapear(SqlDataReader r) => new()
    {
        IdEtiqueta = (int)r["IdEtiqueta"],
        Nombre     = (string)r["Nombre"],
        Tono       = (string)r["Tono"],
        Activa     = (bool)r["Activa"]
    };
}
