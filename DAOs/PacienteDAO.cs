using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class PacienteDAO
{
    public void Insertar(Paciente p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Paciente (Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado) VALUES (@Nombre, @Dni, @FechaNacimiento, @Domicilio, @Telefono, @Mail, @Estado)", con);
        cmd.Parameters.AddWithValue("@Nombre",          p.Nombre);
        cmd.Parameters.AddWithValue("@Dni",             p.Dni);
        cmd.Parameters.AddWithValue("@FechaNacimiento", (object)p.FechaNacimiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Domicilio",       (object)p.Domicilio       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Telefono",        (object)p.Telefono        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Mail",            (object)p.Mail            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",          p.Estado);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Paciente p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Paciente SET Nombre = @Nombre, Dni = @Dni, FechaNacimiento = @FechaNacimiento, Domicilio = @Domicilio, Telefono = @Telefono, Mail = @Mail, Estado = @Estado WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre",          p.Nombre);
        cmd.Parameters.AddWithValue("@Dni",             p.Dni);
        cmd.Parameters.AddWithValue("@FechaNacimiento", (object)p.FechaNacimiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Domicilio",       (object)p.Domicilio       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Telefono",        (object)p.Telefono        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Mail",            (object)p.Mail            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",          p.Estado);
        cmd.Parameters.AddWithValue("@Id",              p.IdPaciente);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Paciente SET Estado = 'Inactivo' WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Paciente> ObtenerTodos()
    {
        var lista = new List<Paciente>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdPaciente, Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado FROM Paciente", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Paciente ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdPaciente, Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado FROM Paciente WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public Paciente ObtenerPorDni(string dni)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdPaciente, Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado FROM Paciente WHERE Dni = @Dni", con);
        cmd.Parameters.AddWithValue("@Dni", dni);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private Paciente Mapear(SqlDataReader r) => new Paciente
    {
        IdPaciente       = (int)r["IdPaciente"],
        Nombre           = (string)r["Nombre"],
        Dni              = (string)r["Dni"],
        FechaNacimiento  = r["FechaNacimiento"] == DBNull.Value ? null : (DateTime?)r["FechaNacimiento"],
        Domicilio        = r["Domicilio"] == DBNull.Value ? null : (string)r["Domicilio"],
        Telefono         = r["Telefono"]  == DBNull.Value ? null : (string)r["Telefono"],
        Mail             = r["Mail"]      == DBNull.Value ? null : (string)r["Mail"],
        Estado           = r["Estado"]    == DBNull.Value ? null : (string)r["Estado"]
    };
}
