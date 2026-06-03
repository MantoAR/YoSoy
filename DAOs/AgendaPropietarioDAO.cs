using Microsoft.Data.SqlClient;
using System.Collections.Generic;
public class AgendaPropietarioDAO
{
    public List<AgendaPropietario> ObtenerPorAgenda(int id)
    {
        var lista = new List<AgendaPropietario>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdAgendaPropietario, IdAgenda, IdProfesional, IdTipoProfesional, PuedeCitaUrgente, Estado FROM AgendaPropietario WHERE IdAgenda = @Id AND Estado = 'Activo'", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) lista.Add(Mapear(reader));
        return lista;
    }
    public void Insertar(AgendaPropietario ap)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("INSERT INTO AgendaPropietario (IdAgenda, IdProfesional, IdTipoProfesional, PuedeCitaUrgente, Estado) VALUES (@IdA, @IdP, @IdT, @Urgente, 'Activo')", con);
        cmd.Parameters.AddWithValue("@IdA", ap.IdAgenda);
        cmd.Parameters.AddWithValue("@IdP", ap.IdProfesional);
        cmd.Parameters.AddWithValue("@IdT", ap.IdTipoProfesional);
        cmd.Parameters.AddWithValue("@Urgente", ap.PuedeCitaUrgente);
        cmd.ExecuteNonQuery();
    }
    public void Eliminar(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE AgendaPropietario SET Estado = 'Inactivo' WHERE IdAgendaPropietario = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }
    private AgendaPropietario Mapear(SqlDataReader r) => new AgendaPropietario
    {
        IdAgendaPropietario = (int)r["IdAgendaPropietario"],
        IdAgenda = (int)r["IdAgenda"],
        IdProfesional = (int)r["IdProfesional"],
        IdTipoProfesional = (int)r["IdTipoProfesional"],
        PuedeCitaUrgente = (bool)r["PuedeCitaUrgente"],
        Estado = (string)r["Estado"]
    };
}
