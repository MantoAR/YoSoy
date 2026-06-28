const fs = require('fs');
const path = 'C:/Users/Araceli/YoSoy/Program.cs';
let content = fs.readFileSync(path, 'utf8');

const nuevoEndpoint = `
// Reporte: atenciones por profesional
app.MapGet("/reportes/atenciones", (int? idProfesional) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var sql = @"
        SELECT 
            p.Nombre,
            p.Apellido,
            p.Dni,
            hc.IdHistoriaClinica AS NHC,
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

    var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, con);
    if (idProfesional.HasValue)
        cmd.Parameters.AddWithValue("@IdProf", idProfesional.Value);

    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            nombre            = reader["Nombre"].ToString(),
            apellido          = reader["Apellido"].ToString(),
            dni               = reader["Dni"].ToString(),
            nhc               = reader["NHC"] == DBNull.Value ? (int?)null : (int)reader["NHC"],
            fecha             = ((DateTime)reader["FechaHora"]).ToString("dd/MM/yyyy"),
            hora              = ((DateTime)reader["FechaHora"]).ToString("HH:mm"),
            tipoCierre        = reader["TipoCierre"] == DBNull.Value ? "" : reader["TipoCierre"].ToString(),
            nombreProfesional = reader["NombreProfesional"].ToString(),
            idProfesional     = (int)reader["IdProfesional"]
        });
    }
    return Results.Ok(lista);
});

`;

content = content.replace('app.Run();', nuevoEndpoint + 'app.Run();');
fs.writeFileSync(path, content, 'utf8');
console.log('Endpoint de reporte agregado OK');