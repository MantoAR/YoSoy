using Microsoft.Data.SqlClient;

public class UsuarioProfesionalDAO : IUsuarioProfesionalAdminRepositorio
{
    public void Insertar(UsuarioProfesional up)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion) VALUES (@IdUsuario, @IdProfesional, @FechaVinculacion)", con);
        cmd.Parameters.AddWithValue("@IdUsuario",        up.IdUsuario);
        cmd.Parameters.AddWithValue("@IdProfesional",    up.IdProfesional);
        cmd.Parameters.AddWithValue("@FechaVinculacion", up.FechaVinculacion);
        cmd.ExecuteNonQuery();
    }

    public void Eliminar(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("DELETE FROM UsuarioProfesional WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        cmd.ExecuteNonQuery();
    }

    public UsuarioProfesional ObtenerPorUsuario(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT IdUsuario, IdProfesional, FechaVinculacion FROM UsuarioProfesional WHERE IdUsuario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idUsuario);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new UsuarioProfesional
        {
            IdUsuario        = (int)reader["IdUsuario"],
            IdProfesional    = (int)reader["IdProfesional"],
            FechaVinculacion = (DateTime)reader["FechaVinculacion"]
        };
    }

    // ── Crear el usuario de un profesional aprobado (CrearUsuarioProfesionalService) ──────────────

    Profesional? IUsuarioProfesionalAdminRepositorio.ObtenerProfesional(int idProfesional) =>
        new ProfesionalDAO().ObtenerPorId(idProfesional);

    /// <summary>
    /// Ids de los profesionales Activos que todavía no tienen usuario (los que pueden entrar por "Crear usuario").
    /// </summary>
    public List<int> IdsProfesionalesActivosSinUsuario()
    {
        var lista = new List<int>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT p.IdProfesional
            FROM Profesional p
            WHERE p.Estado = N'Activo'
              AND NOT EXISTS (SELECT 1 FROM UsuarioProfesional up WHERE up.IdProfesional = p.IdProfesional)
            ORDER BY p.IdProfesional", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add((int)r["IdProfesional"]);
        return lista;
    }

    // Una sola SqlConnection y una SqlTransaction (mismo criterio que PacienteDAO.RegistrarConAcceso: nada de
    // TransactionScope, que con más de una conexión puede promover a MSDTC y no existe en Linux). Se bloquea
    // la fila del profesional con UPDLOCK: dos pedidos para el mismo profesional se hacen cola y el segundo
    // ya ve el vínculo del primero (UsuarioProfesional.IdProfesional no es único en el esquema). El
    // Usuario y el vínculo se crean juntos o no se crea ninguno: un Usuario sin vínculo entra como
    // configurador (POST /login).
    public (ResultadoCreacionUsuario Resultado, int IdUsuario) CrearUsuarioYVincular(
        int idProfesional, string username, string passwordHash, string mail, DateTime fechaVinculacion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand(
                "SELECT Estado FROM Profesional WITH (UPDLOCK, ROWLOCK) WHERE IdProfesional = @Id", con, tx))
            {
                cmd.Parameters.AddWithValue("@Id", idProfesional);
                var estado = cmd.ExecuteScalar();
                if (estado is null) { tx.Rollback(); return (ResultadoCreacionUsuario.ProfesionalInexistente, 0); }
                if (estado == DBNull.Value || (string)estado != "Activo") { tx.Rollback(); return (ResultadoCreacionUsuario.ProfesionalNoActivo, 0); }
            }

            if (Contar(con, tx, "SELECT COUNT(1) FROM UsuarioProfesional WHERE IdProfesional = @V", idProfesional) > 0)
            { tx.Rollback(); return (ResultadoCreacionUsuario.YaTieneUsuario, 0); }
            if (Contar(con, tx, "SELECT COUNT(1) FROM Usuario WHERE Username = @V", username) > 0)
            { tx.Rollback(); return (ResultadoCreacionUsuario.UsernameEnUso, 0); }
            if (Contar(con, tx, "SELECT COUNT(1) FROM Usuario WHERE Mail = @V", mail) > 0)
            { tx.Rollback(); return (ResultadoCreacionUsuario.MailEnUso, 0); }

            int idUsuario;
            using (var cmd = new SqlCommand(
                "INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@Username, @PasswordHash, N'Activo', @Mail); SELECT CAST(SCOPE_IDENTITY() AS INT);", con, tx))
            {
                cmd.Parameters.AddWithValue("@Username",     username);
                cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                cmd.Parameters.AddWithValue("@Mail",         mail);
                idUsuario = (int)cmd.ExecuteScalar()!;
            }

            using (var cmd = new SqlCommand(
                "INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion) VALUES (@IdUsuario, @IdProfesional, @Fecha)", con, tx))
            {
                cmd.Parameters.AddWithValue("@IdUsuario",     idUsuario);
                cmd.Parameters.AddWithValue("@IdProfesional", idProfesional);
                cmd.Parameters.AddWithValue("@Fecha",         fechaVinculacion);
                cmd.ExecuteNonQuery();
            }

            // Etapa 2 de roles: el ingreso del equipo sale de los permisos, así que quien recibe su usuario como profesional recibe también el
            // permiso "profesional" (si no, entraría con 403 SIN_PERMISOS). Solo si la ficha tiene matrícula (RN-17: el trigger de la base lo exige);
            // con una ficha sin matrícula el usuario se crea igual y el Configurador la completa y le asigna el permiso desde Permisos.
            // Antes de la migración 20 las tablas no existen: ahí el rol sigue saliendo de la ficha, como siempre.
            using (var cmd = new SqlCommand(@"
                IF OBJECT_ID('dbo.UsuarioPermiso', 'U') IS NOT NULL
                BEGIN
                    DECLARE @IdRol INT = (SELECT IdRol FROM Rol WHERE Clave = 'profesional');
                    DECLARE @IdPermiso INT = (SELECT IdPermiso FROM Permiso WHERE Clave = 'profesional');
                    IF @IdRol IS NOT NULL AND @IdPermiso IS NOT NULL
                       AND EXISTS (SELECT 1 FROM Profesional WHERE IdProfesional = @IdProf AND LTRIM(RTRIM(ISNULL(Matricula, ''))) <> '' AND IdEspecialidad IS NOT NULL)
                    BEGIN
                        INSERT INTO UsuarioRol (IdUsuario, IdRol) VALUES (@IdUsuario, @IdRol);
                        INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) VALUES (@IdUsuario, @IdRol, @IdPermiso);
                    END
                END", con, tx))
            {
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.Parameters.AddWithValue("@IdProf",    idProfesional);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (ResultadoCreacionUsuario.Creado, idUsuario);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Carrera con otro alta que tomó el usuario o el mail entre el chequeo y el INSERT.
            tx.Rollback();
            if (Contar(con, null, "SELECT COUNT(1) FROM Usuario WHERE Username = @V", username) > 0)
                return (ResultadoCreacionUsuario.UsernameEnUso, 0);
            if (Contar(con, null, "SELECT COUNT(1) FROM Usuario WHERE Mail = @V", mail) > 0)
                return (ResultadoCreacionUsuario.MailEnUso, 0);
            throw;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static int Contar(SqlConnection con, SqlTransaction? tx, string sql, object valor)
    {
        using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.AddWithValue("@V", valor);
        return (int)cmd.ExecuteScalar()!;
    }
}
