const fs = require('fs');
const path = 'D:/YoSoy/Program.cs';
let c = fs.readFileSync(path, 'utf8');

if (c.includes('/turnos/profesional-solicitar')) {
    console.log('El endpoint ya existia.');
    process.exit(0);
}

const endpoint = `
// Turno propuesto por el profesional (queda pendiente de confirmacion del paciente)
app.MapPost("/turnos/profesional-solicitar", (TurnoProfesionalRequest req) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        INSERT INTO Turno (IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor)
        VALUES (@IdPac, @IdProf, 1, @Fecha, @Hora, 'Virtual', @Motivo, 'profesional')", con);
    cmd.Parameters.AddWithValue("@IdPac", req.IdPaciente);
    cmd.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmd.Parameters.AddWithValue("@Fecha", DateTime.Parse(req.Fecha));
    cmd.Parameters.AddWithValue("@Hora", TimeSpan.Parse(req.Hora));
    cmd.Parameters.AddWithValue("@Motivo", (object)req.Motivo ?? DBNull.Value);
    cmd.ExecuteNonQuery();
    return Results.Created("/turnos", null);
});

`;

c = c.replace('app.Run();', endpoint + 'app.Run();');

if (!c.includes('record TurnoProfesionalRequest')) {
    c = c + '\npublic record TurnoProfesionalRequest(int IdPaciente, int IdProfesional, string Fecha, string Hora, string Motivo);\n';
}

fs.writeFileSync(path, c, 'utf8');
console.log('Endpoint /turnos/profesional-solicitar agregado OK');