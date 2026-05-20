using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class DerivacionDetalleDAO
{
    private const string SELECT_BASE = @"
        SELECT d.IdDerivacion, d.IdPaciente,
               pac.Nombre + ISNULL(' ' + pac.Dni, '') AS NombrePaciente,
               d.IdProfesionalOrigen,
               po.Nombre + ISNULL(' ' + po.Apellido, '') AS NombreProfesionalOrigen,
               d.IdProfesionalDestino,
               pd.Nombre + ISNULL(' ' + pd.Apellido, '') AS NombreProfesionalDestino,
               e.IdEspecialidad, e.Nombre AS Especialidad,
               d.Fecha, d.Motivo, d.Estado
        FROM Derivacion d
        JOIN Paciente      pac ON d.IdPaciente           = pac.IdPaciente
        JOIN Profesional   po  ON d.IdProfesionalOrigen  = po.IdProfesional
        JOIN Profesional   pd  ON d.IdProfesionalDestino = pd.IdProfesional
        JOIN Especialidad  e   ON po.IdEspecialidad      = e.IdEspecialidad";

    public List<DerivacionDetalle> ObtenerPendientes()
    {
        var lista = new List<DerivacionDetalle>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_BASE + " WHERE d.Estado = 'Pendiente' ORDER BY d.Fecha DESC", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    public List<DerivacionDetalle> ObtenerTodosConDetalle()
    {
        var lista = new List<DerivacionDetalle>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_BASE + " ORDER BY d.Fecha DESC", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    private DerivacionDetalle Mapear(SqlDataReader r) => new DerivacionDetalle
    {
        IdDerivacion             = (int)r["IdDerivacion"],
        IdPaciente               = (int)r["IdPaciente"],
        NombrePaciente           = (string)r["NombrePaciente"],
        IdProfesionalOrigen      = (int)r["IdProfesionalOrigen"],
        NombreProfesionalOrigen  = (string)r["NombreProfesionalOrigen"],
        IdProfesionalDestino     = (int)r["IdProfesionalDestino"],
        NombreProfesionalDestino = (string)r["NombreProfesionalDestino"],
        IdEspecialidad           = (int)r["IdEspecialidad"],
        Especialidad             = (string)r["Especialidad"],
        Fecha                    = (DateTime)r["Fecha"],
        Motivo                   = r["Motivo"] == DBNull.Value ? null : (string)r["Motivo"],
        Estado                   = (string)r["Estado"]
    };
}
