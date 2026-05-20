using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class ProfesionalDAO
{
    public void Insertar(Profesional p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Profesional (Nombre, Matricula, IdEspecialidad, Estado, Mail, Telefono) VALUES (@Nombre, @Matricula, @IdEspecialidad, @Estado, @Mail, @Telefono)", con);
        cmd.Parameters.AddWithValue("@Nombre",         p.Nombre);
        cmd.Parameters.AddWithValue("@Matricula",      p.Matricula);
        cmd.Parameters.AddWithValue("@IdEspecialidad", p.IdEspecialidad);
        cmd.Parameters.AddWithValue("@Estado",         p.Estado);
        cmd.Parameters.AddWithValue("@Mail",           (object)p.Mail     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Telefono",       (object)p.Telefono ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Profesional p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Profesional SET Nombre = @Nombre, Matricula = @Matricula, IdEspecialidad = @IdEspecialidad, Estado = @Estado, Mail = @Mail, Telefono = @Telefono WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre",         p.Nombre);
        cmd.Parameters.AddWithValue("@Matricula",      p.Matricula);
        cmd.Parameters.AddWithValue("@IdEspecialidad", p.IdEspecialidad);
        cmd.Parameters.AddWithValue("@Estado",         p.Estado);
        cmd.Parameters.AddWithValue("@Mail",           (object)p.Mail     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Telefono",       (object)p.Telefono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",             p.IdProfesional);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Profesional SET Estado = 'Inactivo' WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Profesional> ObtenerTodos()
    {
        var lista = new List<Profesional>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdProfesional, Nombre, Matricula, IdEspecialidad, Estado, Mail, Telefono FROM Profesional", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Profesional ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdProfesional, Nombre, Matricula, IdEspecialidad, Estado, Mail, Telefono FROM Profesional WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<Profesional> ObtenerPorEspecialidad(int idEspecialidad)
    {
        var lista = new List<Profesional>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdProfesional, Nombre, Matricula, IdEspecialidad, Estado, Mail, Telefono FROM Profesional WHERE IdEspecialidad = @IdEsp", con);
        cmd.Parameters.AddWithValue("@IdEsp", idEspecialidad);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private Profesional Mapear(SqlDataReader r) => new Profesional
    {
        IdProfesional  = (int)r["IdProfesional"],
        Nombre         = (string)r["Nombre"],
        Matricula      = (string)r["Matricula"],
        IdEspecialidad = (int)r["IdEspecialidad"],
        Estado         = r["Estado"]   == DBNull.Value ? null : (string)r["Estado"],
        Mail           = r["Mail"]     == DBNull.Value ? null : (string)r["Mail"],
        Telefono       = r["Telefono"] == DBNull.Value ? null : (string)r["Telefono"]
    };
}
