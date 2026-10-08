using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public interface IPreguntaFrecuenteDAO
{
    List<PreguntaFrecuente> ObtenerActivas();
}

public class PreguntaFrecuenteDAO : IPreguntaFrecuenteDAO
{
    private const string SELECT_BASE =
        "SELECT IdPregunta, Pregunta, Respuesta, Orden, Activa FROM PreguntaFrecuente";

    public List<PreguntaFrecuente> ObtenerTodas() =>
        Listar(SELECT_BASE + " ORDER BY Orden, IdPregunta");

    public List<PreguntaFrecuente> ObtenerActivas() =>
        Listar(SELECT_BASE + " WHERE Activa = 1 ORDER BY Orden, IdPregunta");

    public PreguntaFrecuente? ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_BASE + " WHERE IdPregunta = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public void Insertar(PreguntaFrecuente p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO PreguntaFrecuente (Pregunta, Respuesta, Orden, Activa)
            VALUES (@Pregunta, @Respuesta, @Orden, @Activa);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@Pregunta",  p.Pregunta);
        cmd.Parameters.AddWithValue("@Respuesta", p.Respuesta);
        cmd.Parameters.AddWithValue("@Orden",     p.Orden);
        cmd.Parameters.AddWithValue("@Activa",    p.Activa);
        p.IdPregunta = (int)cmd.ExecuteScalar();
    }

    public void Modificar(PreguntaFrecuente p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE PreguntaFrecuente
            SET Pregunta = @Pregunta, Respuesta = @Respuesta, Orden = @Orden, Activa = @Activa
            WHERE IdPregunta = @Id", con);
        cmd.Parameters.AddWithValue("@Pregunta",  p.Pregunta);
        cmd.Parameters.AddWithValue("@Respuesta", p.Respuesta);
        cmd.Parameters.AddWithValue("@Orden",     p.Orden);
        cmd.Parameters.AddWithValue("@Activa",    p.Activa);
        cmd.Parameters.AddWithValue("@Id",        p.IdPregunta);
        cmd.ExecuteNonQuery();
    }

    // Baja lógica, como el resto de los DAOs del repo.
    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE PreguntaFrecuente SET Activa = 0 WHERE IdPregunta = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    private List<PreguntaFrecuente> Listar(string sql)
    {
        var lista = new List<PreguntaFrecuente>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var r = new SqlCommand(sql, con).ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    private PreguntaFrecuente Mapear(SqlDataReader r) => new PreguntaFrecuente
    {
        IdPregunta = (int)r["IdPregunta"],
        Pregunta   = (string)r["Pregunta"],
        Respuesta  = (string)r["Respuesta"],
        Orden      = (int)r["Orden"],
        Activa     = (bool)r["Activa"]
    };
}
