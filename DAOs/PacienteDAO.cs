using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class PacienteDAO
{
    public int Insertar(Paciente p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Paciente (Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado) VALUES (@Nombre, @Dni, @FechaNacimiento, @Domicilio, @Telefono, @Mail, @Estado); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Nombre",          p.Nombre);
        cmd.Parameters.AddWithValue("@Dni",             p.Dni);
        cmd.Parameters.AddWithValue("@FechaNacimiento", (object)p.FechaNacimiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Domicilio",       (object)p.Domicilio       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Telefono",        (object)p.Telefono        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Mail",            (object)p.Mail            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",          p.Estado);
        return (int)cmd.ExecuteScalar();
    }

    // Alta pública con acceso: Usuario + Paciente + UsuarioPaciente en una sola transacción
    // (una SqlConnection + SqlTransaction, no TransactionScope — este repo no tiene ningún
    // precedente de transacciones multi-conexión, y TransactionScope con más de una conexión
    // puede promover a MSDTC, que no existe en Linux, donde corre el VPS de producción).
    // Los SELECT COUNT de abajo son el chequeo de "no repetido" antes de escribir nada, mismo
    // criterio que ya usan las validaciones de docs/produccion/04-acceso-paciente.sql. El
    // catch de SqlException 2627/2601 es un resguardo para la carrera rara entre dos pedidos
    // simultáneos con el mismo dato — el pre-check ya cubre el caso normal.
    public (string CampoConflicto, int IdPaciente, int IdUsuario) RegistrarConAcceso(
        Paciente p, string username, string passwordHash)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        try
        {
            using (var chk = new SqlCommand("SELECT COUNT(1) FROM Usuario WHERE Username = @Username", con, tx))
            {
                chk.Parameters.AddWithValue("@Username", username);
                if ((int)chk.ExecuteScalar()! > 0) { tx.Rollback(); return ("usuario", 0, 0); }
            }
            using (var chk = new SqlCommand("SELECT COUNT(1) FROM Usuario WHERE Mail = @Mail", con, tx))
            {
                chk.Parameters.AddWithValue("@Mail", p.Mail);
                if ((int)chk.ExecuteScalar()! > 0) { tx.Rollback(); return ("mail", 0, 0); }
            }
            using (var chk = new SqlCommand("SELECT COUNT(1) FROM Paciente WHERE Dni = @Dni", con, tx))
            {
                chk.Parameters.AddWithValue("@Dni", p.Dni);
                if ((int)chk.ExecuteScalar()! > 0) { tx.Rollback(); return ("dni", 0, 0); }
            }

            int idUsuario;
            using (var cmd = new SqlCommand(
                "INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@Username, @PasswordHash, 'Activo', @Mail); SELECT CAST(SCOPE_IDENTITY() AS INT);", con, tx))
            {
                cmd.Parameters.AddWithValue("@Username",     username);
                cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                cmd.Parameters.AddWithValue("@Mail",         (object)p.Mail ?? DBNull.Value);
                idUsuario = (int)cmd.ExecuteScalar()!;
            }

            int idPaciente;
            using (var cmd = new SqlCommand(
                "INSERT INTO Paciente (Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado) VALUES (@Nombre, @Dni, @FechaNacimiento, @Domicilio, @Telefono, @Mail, 'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);", con, tx))
            {
                cmd.Parameters.AddWithValue("@Nombre",          p.Nombre);
                cmd.Parameters.AddWithValue("@Dni",             p.Dni);
                cmd.Parameters.AddWithValue("@FechaNacimiento", (object)p.FechaNacimiento ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Domicilio",       (object)p.Domicilio       ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Telefono",        (object)p.Telefono        ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Mail",            (object)p.Mail            ?? DBNull.Value);
                idPaciente = (int)cmd.ExecuteScalar()!;
            }

            using (var cmd = new SqlCommand(
                "INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente, FechaVinculacion) VALUES (@IdUsuario, @IdPaciente, @Fecha)", con, tx))
            {
                cmd.Parameters.AddWithValue("@IdUsuario",  idUsuario);
                cmd.Parameters.AddWithValue("@IdPaciente", idPaciente);
                cmd.Parameters.AddWithValue("@Fecha",      DateTime.Now);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (null, idPaciente, idUsuario);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            tx.Rollback();
            return ("conflicto", 0, 0);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
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
