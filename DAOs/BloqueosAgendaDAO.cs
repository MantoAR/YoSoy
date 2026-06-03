using Microsoft.Data.SqlClient;
using System.Collections.Generic;
public class BloqueosAgendaDAO
{
    public List<BloqueosAgenda> ObtenerPorProfesional(int id)
    {
        var lista = new List<BloqueosAgenda>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdBloqueo,IdProfesional,IdAgenda,FechaDesde,FechaHasta,HoraDesde,HoraHasta,Motivo,Observaciones,Estado,FechaCreacion FROM BloqueosAgenda WHERE IdProfesional=@Id AND Estado='Activo' ORDER BY FechaDesde", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) lista.Add(Mapear(reader));
        return lista;
    }
    public void Insertar(BloqueosAgenda b)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("INSERT INTO BloqueosAgenda (IdProfesional,IdAgenda,FechaDesde,FechaHasta,HoraDesde,HoraHasta,Motivo,Observaciones,Estado) VALUES (@IdP,@IdA,@FD,@FH,@HD,@HH,@M,@O,'Activo')", con);
        cmd.Parameters.AddWithValue("@IdP", b.IdProfesional);
        cmd.Parameters.AddWithValue("@IdA", (object)b.IdAgenda ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FD", b.FechaDesde);
        cmd.Parameters.AddWithValue("@FH", b.FechaHasta);
        cmd.Parameters.AddWithValue("@HD", (object)b.HoraDesde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HH", (object)b.HoraHasta ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@M", b.Motivo);
        cmd.Parameters.AddWithValue("@O", (object)b.Observaciones ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    public void Eliminar(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        new SqlCommand("UPDATE BloqueosAgenda SET Estado='Inactivo' WHERE IdBloqueo=@Id", con) { Parameters = { new("@Id", id) } }.ExecuteNonQuery();
    }
    private BloqueosAgenda Mapear(SqlDataReader r) => new BloqueosAgenda
    {
        IdBloqueo=(int)r["IdBloqueo"], IdProfesional=(int)r["IdProfesional"],
        IdAgenda=r["IdAgenda"]==DBNull.Value?null:(int?)r["IdAgenda"],
        FechaDesde=(DateTime)r["FechaDesde"], FechaHasta=(DateTime)r["FechaHasta"],
        HoraDesde=r["HoraDesde"]==DBNull.Value?null:(TimeSpan?)r["HoraDesde"],
        HoraHasta=r["HoraHasta"]==DBNull.Value?null:(TimeSpan?)r["HoraHasta"],
        Motivo=(string)r["Motivo"],
        Observaciones=r["Observaciones"]==DBNull.Value?null:(string)r["Observaciones"],
        Estado=(string)r["Estado"], FechaCreacion=(DateTime)r["FechaCreacion"]
    };
}
