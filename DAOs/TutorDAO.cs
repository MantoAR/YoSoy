using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class TutorDAO
{
    public void Insertar(Tutor t)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Tutor (Nombre, Dni, Vinculo, Contacto) VALUES (@Nombre, @Dni, @Vinculo, @Contacto)", con);
        cmd.Parameters.AddWithValue("@Nombre",   (object)t.Nombre   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Dni",      (object)t.Dni      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Vinculo",  (object)t.Vinculo  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Contacto", (object)t.Contacto ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Tutor t)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Tutor SET Nombre = @Nombre, Dni = @Dni, Vinculo = @Vinculo, Contacto = @Contacto WHERE IdTutor = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre",   (object)t.Nombre   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Dni",      (object)t.Dni      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Vinculo",  (object)t.Vinculo  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Contacto", (object)t.Contacto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", t.IdTutor);
        cmd.ExecuteNonQuery();
    }

    public List<Tutor> ObtenerTodos()
    {
        var lista = new List<Tutor>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdTutor, Nombre, Dni, Vinculo, Contacto FROM Tutor", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Tutor ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdTutor, Nombre, Dni, Vinculo, Contacto FROM Tutor WHERE IdTutor = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private Tutor Mapear(SqlDataReader r) => new Tutor
    {
        IdTutor  = (int)r["IdTutor"],
        Nombre   = r["Nombre"]   == DBNull.Value ? null : (string)r["Nombre"],
        Dni      = r["Dni"]      == DBNull.Value ? null : (string)r["Dni"],
        Vinculo  = r["Vinculo"]  == DBNull.Value ? null : (string)r["Vinculo"],
        Contacto = r["Contacto"] == DBNull.Value ? null : (string)r["Contacto"]
    };
}
