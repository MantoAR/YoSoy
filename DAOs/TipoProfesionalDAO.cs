using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class TipoProfesionalDAO
{
    public List<TipoProfesional> ObtenerTodos()
    {
        var lista = new List<TipoProfesional>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdTipoProfesional, Nombre, Estado FROM TipoProfesional WHERE Estado = 'Activo'", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public TipoProfesional ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdTipoProfesional, Nombre, Estado FROM TipoProfesional WHERE IdTipoProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public void Insertar(TipoProfesional t)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("INSERT INTO TipoProfesional (Nombre, Estado) VALUES (@Nombre, @Estado)", con);
        cmd.Parameters.AddWithValue("@Nombre", t.Nombre);
        cmd.Parameters.AddWithValue("@Estado", t.Estado ?? "Activo");
        cmd.ExecuteNonQuery();
    }

    public void Modificar(TipoProfesional t)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE TipoProfesional SET Nombre = @Nombre, Estado = @Estado WHERE IdTipoProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre", t.Nombre);
        cmd.Parameters.AddWithValue("@Estado", t.Estado);
        cmd.Parameters.AddWithValue("@Id", t.IdTipoProfesional);
        cmd.ExecuteNonQuery();
    }

    private TipoProfesional Mapear(SqlDataReader r) => new TipoProfesional
    {
        IdTipoProfesional = (int)r["IdTipoProfesional"],
        Nombre            = (string)r["Nombre"],
        Estado            = (string)r["Estado"]
    };
}