using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class TipoConsentimientoDAO
{
    public int Insertar(TipoConsentimiento tc)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO TipoConsentimiento (Descripcion, Estado) VALUES (@Descripcion, @Estado); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Descripcion", tc.Descripcion);
        cmd.Parameters.AddWithValue("@Estado", tc.Estado);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(TipoConsentimiento tc)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE TipoConsentimiento SET Descripcion = @Descripcion, Estado = @Estado WHERE IdTipoConsentimiento = @Id", con);
        cmd.Parameters.AddWithValue("@Descripcion", tc.Descripcion);
        cmd.Parameters.AddWithValue("@Estado", tc.Estado);
        cmd.Parameters.AddWithValue("@Id", tc.IdTipoConsentimiento);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE TipoConsentimiento SET Estado = 'Inactivo' WHERE IdTipoConsentimiento = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<TipoConsentimiento> ObtenerTodos()
    {
        var lista = new List<TipoConsentimiento>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdTipoConsentimiento, Descripcion, Estado FROM TipoConsentimiento", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public TipoConsentimiento ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdTipoConsentimiento, Descripcion, Estado FROM TipoConsentimiento WHERE IdTipoConsentimiento = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private TipoConsentimiento Mapear(SqlDataReader r) => new TipoConsentimiento
    {
        IdTipoConsentimiento = (int)r["IdTipoConsentimiento"],
        Descripcion          = (string)r["Descripcion"],
        Estado               = (string)r["Estado"]
    };
}
