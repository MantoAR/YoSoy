using Microsoft.Data.SqlClient;

// Acceso a AceptacionVideollamada (migración 28). Todas las fechas son UTC. Ver AceptacionVideollamadaService.
public class AceptacionVideollamadaDAO : IAceptacionVideollamadaRepositorio
{
    public bool Disponible() => Migracion28.Disponible();

    public AceptacionVideoFila? Ultima(string origen, int idOrigen, int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT TOP 1 IdAceptacion, IdUsuario, IdPaciente, Origen, IdOrigen, VersionTexto, DecididoUtc, RevocadoUtc
            FROM AceptacionVideollamada
            WHERE Origen = @O AND IdOrigen = @I AND IdPaciente = @P
            ORDER BY IdAceptacion DESC", con);
        cmd.Parameters.AddWithValue("@O", origen);
        cmd.Parameters.AddWithValue("@I", idOrigen);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public AceptacionVideoFila Insertar(int idUsuario, int idPaciente, string origen, int idOrigen, string versionTexto, string hashTexto, DateTime decididoUtc)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        try
        {
            using var cmd = new SqlCommand(@"
                INSERT INTO AceptacionVideollamada (IdUsuario, IdPaciente, Origen, IdOrigen, VersionTexto, HashTexto, DecididoUtc)
                OUTPUT INSERTED.IdAceptacion, INSERTED.IdUsuario, INSERTED.IdPaciente, INSERTED.Origen, INSERTED.IdOrigen, INSERTED.VersionTexto, INSERTED.DecididoUtc, INSERTED.RevocadoUtc
                VALUES (@U, @P, @O, @I, @V, @H, @D)", con);
            cmd.Parameters.AddWithValue("@U", idUsuario);
            cmd.Parameters.AddWithValue("@P", idPaciente);
            cmd.Parameters.AddWithValue("@O", origen);
            cmd.Parameters.AddWithValue("@I", idOrigen);
            cmd.Parameters.AddWithValue("@V", versionTexto);
            cmd.Parameters.AddWithValue("@H", hashTexto);
            cmd.Parameters.AddWithValue("@D", decididoUtc);
            using var r = cmd.ExecuteReader();
            r.Read();
            return Mapear(r);
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            // Dos pedidos a la vez: el índice único dejó pasar uno solo. Vale el que ganó.
            return Ultima(origen, idOrigen, idPaciente)!;
        }
    }

    public bool Revocar(int idAceptacion, DateTime revocadoUtc, int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("UPDATE AceptacionVideollamada SET RevocadoUtc = @R, IdUsuarioRevoca = @U WHERE IdAceptacion = @I AND RevocadoUtc IS NULL", con);
        cmd.Parameters.AddWithValue("@R", revocadoUtc);
        cmd.Parameters.AddWithValue("@U", idUsuario);
        cmd.Parameters.AddWithValue("@I", idAceptacion);
        return cmd.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<AceptacionVideoFila> VigentesDelPaciente(int idPaciente, DateTime desdeUtc)
    {
        var lista = new List<AceptacionVideoFila>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT IdAceptacion, IdUsuario, IdPaciente, Origen, IdOrigen, VersionTexto, DecididoUtc, RevocadoUtc
            FROM AceptacionVideollamada
            WHERE IdPaciente = @P AND RevocadoUtc IS NULL AND DecididoUtc >= @D
            ORDER BY DecididoUtc DESC", con);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        cmd.Parameters.AddWithValue("@D", desdeUtc);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    private static AceptacionVideoFila Mapear(SqlDataReader r) => new(
        (int)r["IdAceptacion"], r["IdUsuario"] as int?, (int)r["IdPaciente"], (string)r["Origen"], (int)r["IdOrigen"], (string)r["VersionTexto"],
        DateTime.SpecifyKind((DateTime)r["DecididoUtc"], DateTimeKind.Utc),
        r["RevocadoUtc"] is DateTime rev ? DateTime.SpecifyKind(rev, DateTimeKind.Utc) : null);
}
