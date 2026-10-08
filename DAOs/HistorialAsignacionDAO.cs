using Microsoft.Data.SqlClient;

public class HistorialAsignacionDAO
{
    public int Insertar(HistorialAsignacion h)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO HistorialAsignacion (IdCaso, IdProfesional, Motivo, IdRealizadoPor, FechaHora)
            OUTPUT INSERTED.IdHistorial
            VALUES (@IdCaso, @IdProf, @Motivo, @IdRealizadoPor, @Fecha)", con);
        cmd.Parameters.AddWithValue("@IdCaso", h.IdCaso);
        cmd.Parameters.AddWithValue("@IdProf", (object?)h.IdProfesional ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Motivo", h.Motivo);
        cmd.Parameters.AddWithValue("@IdRealizadoPor", (object?)h.IdRealizadoPor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Fecha", h.FechaHora == default ? DateTime.Now : h.FechaHora);
        return (int)cmd.ExecuteScalar();
    }

    public List<HistorialAsignacion> ObtenerPorCaso(int idCaso)
    {
        var lista = new List<HistorialAsignacion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdHistorial, IdCaso, IdProfesional, Motivo, IdRealizadoPor, FechaHora
            FROM HistorialAsignacion WHERE IdCaso = @IdCaso ORDER BY FechaHora DESC", con);
        cmd.Parameters.AddWithValue("@IdCaso", idCaso);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new HistorialAsignacion
            {
                IdHistorial    = (int)r["IdHistorial"],
                IdCaso         = (int)r["IdCaso"],
                IdProfesional  = r["IdProfesional"] == DBNull.Value ? null : (int)r["IdProfesional"],
                Motivo         = (string)r["Motivo"],
                IdRealizadoPor = r["IdRealizadoPor"] == DBNull.Value ? null : (int)r["IdRealizadoPor"],
                FechaHora      = (DateTime)r["FechaHora"]
            });
        return lista;
    }

    // Antigüedad de la última vez que este profesional recibió un caso (para el desempate).
    public DateTime? UltimaAsignacion(int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "SELECT MAX(FechaHora) FROM HistorialAsignacion WHERE IdProfesional = @IdProf", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        var resultado = cmd.ExecuteScalar();
        return resultado == DBNull.Value || resultado == null ? null : (DateTime)resultado;
    }
}
