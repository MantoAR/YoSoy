using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class ProfesionalDAO
{
    public void Insertar(Profesional p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Profesional (Nombre, Apellido, SegundoNombre, SegundoApellido, Dni, Sexo, FechaNacimiento, Matricula, IdEspecialidad, Provincia, OrigenContacto, Estado, Mail, Telefono, FechaSolicitud) VALUES (@Nombre, @Apellido, @SegundoNombre, @SegundoApellido, @Dni, @Sexo, @FechaNacimiento, @Matricula, @IdEspecialidad, @Provincia, @OrigenContacto, @Estado, @Mail, @Telefono, @FechaSolicitud)", con);
        cmd.Parameters.AddWithValue("@Nombre",          p.Nombre);
        cmd.Parameters.AddWithValue("@Apellido",        (object?)p.Apellido        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SegundoNombre",   (object?)p.SegundoNombre   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SegundoApellido", (object?)p.SegundoApellido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Dni",             (object?)p.Dni             ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sexo",            (object?)p.Sexo            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaNacimiento", (object?)p.FechaNacimiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Matricula",       p.Matricula);
        cmd.Parameters.AddWithValue("@IdEspecialidad",  p.IdEspecialidad);
        cmd.Parameters.AddWithValue("@Provincia",       (object?)p.Provincia       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OrigenContacto",  (object?)p.OrigenContacto  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",          p.Estado);
        cmd.Parameters.AddWithValue("@Mail",            (object?)p.Mail            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Telefono",        (object?)p.Telefono        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaSolicitud",  (object?)p.FechaSolicitud  ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Profesional p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Profesional SET Nombre = @Nombre, Apellido = @Apellido, SegundoNombre = @SegundoNombre, SegundoApellido = @SegundoApellido, Dni = @Dni, Sexo = @Sexo, FechaNacimiento = @FechaNacimiento, Matricula = @Matricula, IdEspecialidad = @IdEspecialidad, Provincia = @Provincia, OrigenContacto = @OrigenContacto, Estado = @Estado, Mail = @Mail, Telefono = @Telefono, FechaSolicitud = @FechaSolicitud WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre",          p.Nombre);
        cmd.Parameters.AddWithValue("@Apellido",        (object?)p.Apellido        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SegundoNombre",   (object?)p.SegundoNombre   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SegundoApellido", (object?)p.SegundoApellido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Dni",             (object?)p.Dni             ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sexo",            (object?)p.Sexo            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaNacimiento", (object?)p.FechaNacimiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Matricula",       p.Matricula);
        cmd.Parameters.AddWithValue("@IdEspecialidad",  p.IdEspecialidad);
        cmd.Parameters.AddWithValue("@Provincia",       (object?)p.Provincia       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OrigenContacto",  (object?)p.OrigenContacto  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",          p.Estado);
        cmd.Parameters.AddWithValue("@Mail",            (object?)p.Mail            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Telefono",        (object?)p.Telefono        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaSolicitud",  (object?)p.FechaSolicitud  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",              p.IdProfesional);
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
            "SELECT IdProfesional, Nombre, Apellido, SegundoNombre, SegundoApellido, Dni, Sexo, FechaNacimiento, Matricula, IdEspecialidad, Provincia, OrigenContacto, Estado, Mail, Telefono, FechaSolicitud FROM Profesional", con);
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
            "SELECT IdProfesional, Nombre, Apellido, SegundoNombre, SegundoApellido, Dni, Sexo, FechaNacimiento, Matricula, IdEspecialidad, Provincia, OrigenContacto, Estado, Mail, Telefono, FechaSolicitud FROM Profesional WHERE IdProfesional = @Id", con);
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
            "SELECT IdProfesional, Nombre, Apellido, SegundoNombre, SegundoApellido, Dni, Sexo, FechaNacimiento, Matricula, IdEspecialidad, Provincia, OrigenContacto, Estado, Mail, Telefono, FechaSolicitud FROM Profesional WHERE IdEspecialidad = @IdEsp", con);
        cmd.Parameters.AddWithValue("@IdEsp", idEspecialidad);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private Profesional Mapear(SqlDataReader r) => new Profesional
    {
        IdProfesional   = (int)r["IdProfesional"],
        Nombre          = (string)r["Nombre"],
        Apellido        = r["Apellido"]        == DBNull.Value ? null : (string)r["Apellido"],
        SegundoNombre   = r["SegundoNombre"]   == DBNull.Value ? null : (string)r["SegundoNombre"],
        SegundoApellido = r["SegundoApellido"] == DBNull.Value ? null : (string)r["SegundoApellido"],
        Dni             = r["Dni"]             == DBNull.Value ? null : (string)r["Dni"],
        Sexo            = r["Sexo"]            == DBNull.Value ? null : (string)r["Sexo"],
        FechaNacimiento = r["FechaNacimiento"] == DBNull.Value ? null : (DateTime?)r["FechaNacimiento"],
        Matricula       = (string)r["Matricula"],
        IdEspecialidad  = (int)r["IdEspecialidad"],
        Provincia       = r["Provincia"]       == DBNull.Value ? null : (string)r["Provincia"],
        OrigenContacto  = r["OrigenContacto"]  == DBNull.Value ? null : (string)r["OrigenContacto"],
        Estado          = r["Estado"]          == DBNull.Value ? null : (string)r["Estado"],
        Mail            = r["Mail"]            == DBNull.Value ? null : (string)r["Mail"],
        Telefono        = r["Telefono"]        == DBNull.Value ? null : (string)r["Telefono"],
        FechaSolicitud  = r["FechaSolicitud"]  == DBNull.Value ? null : (DateTime?)r["FechaSolicitud"]
    };
}
