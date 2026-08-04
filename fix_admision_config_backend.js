const fs = require('fs');
const path = 'D:/YoSoy/Program.cs';
let c = fs.readFileSync(path, 'utf8');

if (c.includes('/informes-admision-detalle')) {
    console.log('Los endpoints ya existen, no se hace nada.');
    process.exit(0);
}

const endpoints = `
// ── Admision: endpoints para el configurador ────────────────
app.MapGet("/informes-admision-detalle", () =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT ia.IdInforme, ia.IdPaciente, p.Nombre AS NombrePaciente, p.Dni, p.FechaNacimiento,
               e.Nombre AS Especialidad, ia.MotivoConsulta, ia.TratamientoAnterior, ia.TratamientoAnteriorDetalle,
               ia.RecibeMedicacion, ia.MedicacionDetalle, ia.GustosIntereses, ia.PreferenciasAtencion,
               ia.TipoEntrevista, ia.RedApoyo, ia.ObjetivosPaciente, ia.Estado, ia.FechaCreacion,
               ia.FechaEntrevista, ia.ObservacionesEntrevista, ia.IdProfesionalDerivado,
               prof.Nombre + ' ' + ISNULL(prof.Apellido, '') AS NombreProfesionalDerivado
        FROM InformeAdmision ia
        INNER JOIN Paciente p ON p.IdPaciente = ia.IdPaciente
        INNER JOIN Especialidad e ON e.IdEspecialidad = ia.IdEspecialidad
        LEFT JOIN Profesional prof ON prof.IdProfesional = ia.IdProfesionalDerivado
        ORDER BY ia.FechaCreacion DESC", con);
    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            idInforme               = (int)reader["IdInforme"],
            idPaciente              = (int)reader["IdPaciente"],
            nombrePaciente          = reader["NombrePaciente"].ToString(),
            dni                     = reader["Dni"].ToString(),
            fechaNacimiento         = reader["FechaNacimiento"] == DBNull.Value ? null : ((DateTime)reader["FechaNacimiento"]).ToString("dd/MM/yyyy"),
            especialidad            = reader["Especialidad"].ToString(),
            motivoConsulta          = reader["MotivoConsulta"] == DBNull.Value ? "" : reader["MotivoConsulta"].ToString(),
            tratamientoAnterior     = reader["TratamientoAnterior"] != DBNull.Value && (bool)reader["TratamientoAnterior"],
            tratamientoAnteriorDetalle = reader["TratamientoAnteriorDetalle"] == DBNull.Value ? "" : reader["TratamientoAnteriorDetalle"].ToString(),
            recibeMedicacion        = reader["RecibeMedicacion"] != DBNull.Value && (bool)reader["RecibeMedicacion"],
            medicacionDetalle       = reader["MedicacionDetalle"] == DBNull.Value ? "" : reader["MedicacionDetalle"].ToString(),
            gustosIntereses         = reader["GustosIntereses"] == DBNull.Value ? "" : reader["GustosIntereses"].ToString(),
            preferenciasAtencion    = reader["PreferenciasAtencion"] == DBNull.Value ? "" : reader["PreferenciasAtencion"].ToString(),
            tipoEntrevista          = reader["TipoEntrevista"] == DBNull.Value ? "" : reader["TipoEntrevista"].ToString(),
            redApoyo                = reader["RedApoyo"] == DBNull.Value ? "" : reader["RedApoyo"].ToString(),
            objetivosPaciente       = reader["ObjetivosPaciente"] == DBNull.Value ? "" : reader["ObjetivosPaciente"].ToString(),
            estado                  = reader["Estado"].ToString(),
            fechaCreacion           = ((DateTime)reader["FechaCreacion"]).ToString("dd/MM/yyyy HH:mm"),
            fechaEntrevista         = reader["FechaEntrevista"] == DBNull.Value ? null : ((DateTime)reader["FechaEntrevista"]).ToString("dd/MM/yyyy HH:mm"),
            observacionesEntrevista = reader["ObservacionesEntrevista"] == DBNull.Value ? "" : reader["ObservacionesEntrevista"].ToString(),
            idProfesionalDerivado   = reader["IdProfesionalDerivado"] == DBNull.Value ? (int?)null : (int)reader["IdProfesionalDerivado"],
            nombreProfesionalDerivado = reader["NombreProfesionalDerivado"] == DBNull.Value ? "" : reader["NombreProfesionalDerivado"].ToString()
        });
    }
    return Results.Ok(lista);
});

app.MapPut("/informes-admision/{id}/entrevista", (int id, EntrevistaRequest req) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE InformeAdmision
        SET FechaEntrevista = @Fecha, ObservacionesEntrevista = @Obs, Estado = 'En entrevista'
        WHERE IdInforme = @Id", con);
    cmd.Parameters.AddWithValue("@Fecha", req.Fecha);
    cmd.Parameters.AddWithValue("@Obs", (object)req.Observaciones ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Id", id);
    cmd.ExecuteNonQuery();
    return Results.NoContent();
});

app.MapPut("/informes-admision/{id}/derivar", (int id, DerivarRequest req) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();

    // Obtener el paciente del informe
    var cmdGet = new Microsoft.Data.SqlClient.SqlCommand(
        "SELECT IdPaciente FROM InformeAdmision WHERE IdInforme = @Id", con);
    cmdGet.Parameters.AddWithValue("@Id", id);
    var idPacObj = cmdGet.ExecuteScalar();
    if (idPacObj == null) return Results.NotFound();
    int idPac = (int)idPacObj;

    // Actualizar el informe
    var cmdUpd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE InformeAdmision
        SET IdProfesionalDerivado = @IdProf, ObservacionesEntrevista = ISNULL(@Obs, ObservacionesEntrevista), Estado = 'Derivado'
        WHERE IdInforme = @Id", con);
    cmdUpd.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmdUpd.Parameters.AddWithValue("@Obs", (object)req.Observaciones ?? DBNull.Value);
    cmdUpd.Parameters.AddWithValue("@Id", id);
    cmdUpd.ExecuteNonQuery();

    // Vincular paciente con profesional (si no estaba vinculado)
    var cmdVinc = new Microsoft.Data.SqlClient.SqlCommand(@"
        IF NOT EXISTS (SELECT 1 FROM PacienteProfesional WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf)
            INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, FechaAsignacion)
            VALUES (@IdPac, @IdProf, GETDATE())", con);
    cmdVinc.Parameters.AddWithValue("@IdPac", idPac);
    cmdVinc.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmdVinc.ExecuteNonQuery();

    return Results.NoContent();
});

`;

c = c.replace('app.Run();', endpoints + 'app.Run();');

// Agregar los records de request al final del archivo
if (!c.includes('record EntrevistaRequest')) {
    c = c + '\npublic record EntrevistaRequest(DateTime Fecha, string Observaciones);\npublic record DerivarRequest(int IdProfesional, string Observaciones);\n';
}

fs.writeFileSync(path, c, 'utf8');
console.log('Endpoints del configurador agregados OK');