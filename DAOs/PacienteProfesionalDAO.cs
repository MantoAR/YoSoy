using Microsoft.Data.SqlClient;

public class PacienteProfesionalDAO
{
    public List<ProfesionalResumen> ObtenerProfesionalesDePaciente(int idPaciente)
    {
        var lista = new List<ProfesionalResumen>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT p.IdProfesional, p.IdEspecialidad,
                   p.Nombre + ISNULL(' ' + p.Apellido, '') AS Nombre,
                   e.Nombre AS Especialidad,
                   p.Mail, p.Telefono
            FROM PacienteProfesional pp
            JOIN Profesional p  ON pp.IdProfesional = p.IdProfesional
            JOIN Especialidad e ON p.IdEspecialidad  = e.IdEspecialidad
            WHERE pp.IdPaciente = @Id
            ORDER BY e.Nombre, p.Nombre", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new ProfesionalResumen
            {
                IdProfesional  = (int)r["IdProfesional"],
                IdEspecialidad = (int)r["IdEspecialidad"],
                Nombre         = (string)r["Nombre"],
                Especialidad   = (string)r["Especialidad"],
                Mail           = r["Mail"]     == DBNull.Value ? null : (string)r["Mail"],
                Telefono       = r["Telefono"] == DBNull.Value ? null : (string)r["Telefono"]
            });
        return lista;
    }

    public List<int> ObtenerIdPacientesDeProfesional(int idProfesional)
    {
        var lista = new List<int>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("SELECT IdPaciente FROM PacienteProfesional WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idProfesional);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add((int)r["IdPaciente"]);
        return lista;
    }

    public void Asignar(int idPaciente, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM PacienteProfesional WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf)
                INSERT INTO PacienteProfesional (IdPaciente, IdProfesional) VALUES (@IdPac, @IdProf)", con);
        cmd.Parameters.AddWithValue("@IdPac",  idPaciente);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.ExecuteNonQuery();
    }

    public void Desasignar(int idPaciente, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("DELETE FROM PacienteProfesional WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf", con);
        cmd.Parameters.AddWithValue("@IdPac",  idPaciente);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.ExecuteNonQuery();
    }
}
