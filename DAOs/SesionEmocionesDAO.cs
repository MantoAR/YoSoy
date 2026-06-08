using Microsoft.Data.SqlClient;

public class SesionEmocionesDAO
{
    public void Insertar(SesionEmociones e)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO SesionEmociones (IdSesion, IdProfesional, RoomId, DuracionSegundos, Muestras, Resumen, Historial)
            VALUES (@IdSesion, @IdProfesional, @RoomId, @DuracionSegundos, @Muestras, @Resumen, @Historial)", con);
        cmd.Parameters.AddWithValue("@IdSesion",         (object?)e.IdSesion         ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdProfesional",    (object?)e.IdProfesional    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RoomId",           (object?)e.RoomId           ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DuracionSegundos", (object?)e.DuracionSegundos ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Muestras",         (object?)e.Muestras         ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Resumen",          (object?)e.Resumen          ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Historial",        (object?)e.Historial        ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
