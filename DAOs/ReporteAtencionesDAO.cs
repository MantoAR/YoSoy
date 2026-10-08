using Microsoft.Data.SqlClient;
using System.Collections.Generic;

// Acceso a datos del reporte "Atenciones por Profesional".
//
// Usa exactamente la misma consulta que hoy alimenta la exportación a CSV desde
// GET /reportes/atenciones: mismos JOINs, mismo filtro opcional por profesional
// y mismo orden. La única diferencia es que mapea a un DTO tipado en lugar de a
// un objeto anónimo, para conservar la fecha como DateTime.
//
// El endpoint original queda intacto: esta clase no lo reemplaza, lo acompaña.
public class ReporteAtencionesDAO
{
    public List<AtencionReporteDto> Obtener(int? idProfesional)
    {
        var lista = new List<AtencionReporteDto>();
        using var con = Conexion.ObtenerConexion();
        con.Open();

        var sql = @"
            SELECT
                p.Nombre,
                p.Dni,
                hc.IdHistoria AS NHC,
                sc.FechaHora,
                sc.TipoCierre,
                prof.Nombre + ' ' + prof.Apellido AS NombreProfesional,
                prof.IdProfesional
            FROM SesionClinica sc
            INNER JOIN Paciente p ON p.IdPaciente = sc.IdPaciente
            LEFT JOIN HistoriaClinica hc ON hc.IdPaciente = sc.IdPaciente
            INNER JOIN Profesional prof ON prof.IdProfesional = sc.IdProfesional
            " + (idProfesional.HasValue ? "WHERE sc.IdProfesional = @IdProf" : "") + @"
            ORDER BY sc.FechaHora DESC";

        var cmd = new SqlCommand(sql, con);
        if (idProfesional.HasValue)
            cmd.Parameters.AddWithValue("@IdProf", idProfesional.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));

        return lista;
    }

    private AtencionReporteDto Mapear(SqlDataReader r) => new AtencionReporteDto
    {
        Nombre            = r["Nombre"]?.ToString() ?? "",
        Dni               = r["Dni"]?.ToString() ?? "",
        Nhc               = r["NHC"]        == DBNull.Value ? null : (int?)r["NHC"],
        FechaHora         = (DateTime)r["FechaHora"],
        TipoCierre        = r["TipoCierre"] == DBNull.Value ? "" : r["TipoCierre"]?.ToString() ?? "",
        NombreProfesional = r["NombreProfesional"]?.ToString() ?? "",
        IdProfesional     = (int)r["IdProfesional"]
    };
}
