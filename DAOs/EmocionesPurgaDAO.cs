using System.Data;
using Microsoft.Data.SqlClient;

// SQL de la purga por retención (ver PurgaEmocionesBackgroundService). Solo toca HistorialEmociones y las dos columnas que
// registran la anulación; nunca las notas, la transcripción ni el tipo de cierre.
public class EmocionesPurgaDAO : IEmocionesPurgaRepositorio
{
    public int ContarVencidas(DateTime corteLocal)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM SesionClinica WHERE HistorialEmociones IS NOT NULL AND FechaHora < @Corte", con);
        cmd.Parameters.Add("@Corte", SqlDbType.DateTime).Value = corteLocal;
        return (int)cmd.ExecuteScalar()!;
    }

    public int AnularVencidas(DateTime corteLocal, int lote, DateTime ahoraUtc)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE TOP (@Lote) SesionClinica
            SET HistorialEmociones = NULL, EmocionesAnuladasUtc = @Ahora, EmocionesAnuladasMotivo = 'retencion'
            WHERE HistorialEmociones IS NOT NULL AND FechaHora < @Corte", con);
        cmd.Parameters.AddWithValue("@Lote", lote);
        cmd.Parameters.Add("@Corte", SqlDbType.DateTime).Value = corteLocal;
        cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahoraUtc;
        return cmd.ExecuteNonQuery();
    }
}
