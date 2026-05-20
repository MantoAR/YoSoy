using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class ObraSocialDAO
{
    public void Insertar(ObraSocial o)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO ObraSocial (Nombre) VALUES (@Nombre)", con);
        cmd.Parameters.AddWithValue("@Nombre", o.Nombre);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(ObraSocial o)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE ObraSocial SET Nombre = @Nombre WHERE IdObraSocial = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre", o.Nombre);
        cmd.Parameters.AddWithValue("@Id", o.IdObraSocial);
        cmd.ExecuteNonQuery();
    }

    public List<ObraSocial> ObtenerTodos()
    {
        var lista = new List<ObraSocial>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdObraSocial, Nombre FROM ObraSocial", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public ObraSocial ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdObraSocial, Nombre FROM ObraSocial WHERE IdObraSocial = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private ObraSocial Mapear(SqlDataReader r) => new ObraSocial
    {
        IdObraSocial = (int)r["IdObraSocial"],
        Nombre       = (string)r["Nombre"]
    };
}
