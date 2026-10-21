using Microsoft.Data.SqlClient;
using System.Collections.Generic;

// El DAO recibe PasswordHash ya generado. El hashing se hace antes de llamar a Insertar/Modificar.
public class UsuarioDAO
{
    public int Insertar(Usuario u)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@Username, @PasswordHash, @Estado, @Mail); SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Username",     u.Username);
        cmd.Parameters.AddWithValue("@PasswordHash", u.PasswordHash);
        cmd.Parameters.AddWithValue("@Estado",       u.Estado);
        cmd.Parameters.AddWithValue("@Mail",         (object)u.Mail ?? DBNull.Value);
        return (int)cmd.ExecuteScalar();
    }

    public void Modificar(Usuario u)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Usuario SET Username = @Username, PasswordHash = @PasswordHash, Estado = @Estado, Mail = @Mail WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Username",     u.Username);
        cmd.Parameters.AddWithValue("@PasswordHash", u.PasswordHash);
        cmd.Parameters.AddWithValue("@Estado",       u.Estado);
        cmd.Parameters.AddWithValue("@Mail",         (object)u.Mail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",           u.IdUsuario);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Usuario SET Estado = 'Inactivo' WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Usuario> ObtenerTodos()
    {
        var lista = new List<Usuario>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdUsuario, Username, PasswordHash, Estado, Mail, IntentosFallidos, BloqueadoHasta, DebeCambiarPassword FROM Usuario", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Usuario ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario, Username, PasswordHash, Estado, Mail, IntentosFallidos, BloqueadoHasta, DebeCambiarPassword FROM Usuario WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public Usuario ObtenerPorUsername(string username)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario, Username, PasswordHash, Estado, Mail, IntentosFallidos, BloqueadoHasta, DebeCambiarPassword FROM Usuario WHERE Username = @Username", con);
        cmd.Parameters.AddWithValue("@Username", username);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    // Suma un intento fallido y, al llegar al umbral, bloquea. Todo en UNA sentencia (atómica: dos intentos
    // simultáneos no se pisan el conteo) y con la hora de la aplicación en UTC, no con GETDATE() de SQL
    // Server: ver PoliticaBloqueoLogin, que hace lo mismo en C# y es contra lo que se prueba este UPDATE.
    // Si el bloqueo anterior ya venció, el conteo arranca de nuevo (este intento es el primero de tres).
    // Devuelve el estado que quedó, para no tener que volver a leer el usuario.
    public (int IntentosFallidos, DateTime? BloqueadoHasta) RegistrarIntentoFallido(int idUsuario, DateTime ahoraUtc)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Usuario
            SET IntentosFallidos = CASE WHEN BloqueadoHasta IS NOT NULL AND BloqueadoHasta <= @Ahora
                                        THEN 1 ELSE IntentosFallidos + 1 END,
                BloqueadoHasta   = CASE
                    WHEN (CASE WHEN BloqueadoHasta IS NOT NULL AND BloqueadoHasta <= @Ahora
                               THEN 1 ELSE IntentosFallidos + 1 END) >= @Max
                        THEN DATEADD(MINUTE, @Minutos, @Ahora)
                    WHEN BloqueadoHasta IS NOT NULL AND BloqueadoHasta <= @Ahora THEN NULL
                    ELSE BloqueadoHasta END
            OUTPUT inserted.IntentosFallidos, inserted.BloqueadoHasta
            WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id",      idUsuario);
        cmd.Parameters.AddWithValue("@Ahora",   ahoraUtc);
        cmd.Parameters.AddWithValue("@Max",     PoliticaBloqueoLogin.MaxIntentos);
        cmd.Parameters.AddWithValue("@Minutos", PoliticaBloqueoLogin.MinutosBloqueo);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (0, null);   // el usuario ya no existe
        return ((int)r["IntentosFallidos"], r["BloqueadoHasta"] == DBNull.Value ? null : (DateTime?)r["BloqueadoHasta"]);
    }

    public void ResetearIntentos(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Usuario SET IntentosFallidos = 0, BloqueadoHasta = NULL WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        cmd.ExecuteNonQuery();
    }

    private Usuario Mapear(SqlDataReader r) => new Usuario
    {
        IdUsuario         = (int)r["IdUsuario"],
        Username          = (string)r["Username"],
        PasswordHash      = (string)r["PasswordHash"],
        Estado            = r["Estado"] == DBNull.Value ? null : (string)r["Estado"],
        Mail              = r["Mail"]   == DBNull.Value ? null : (string)r["Mail"],
        IntentosFallidos  = r["IntentosFallidos"] == DBNull.Value ? 0 : (int)r["IntentosFallidos"],
        BloqueadoHasta    = r["BloqueadoHasta"]   == DBNull.Value ? null : (DateTime?)r["BloqueadoHasta"],
        DebeCambiarPassword = r["DebeCambiarPassword"] != DBNull.Value && (bool)r["DebeCambiarPassword"]
    };
}
