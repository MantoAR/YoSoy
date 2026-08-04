const fs = require('fs');
const path = 'D:/YoSoy/Program.cs';
let c = fs.readFileSync(path, 'utf8');

// ── 1. Reemplazar el endpoint derivar ───────────────────────
const derivarViejo = `app.MapPut("/informes-admision/{id}/derivar", (int id, DerivarRequest req) =>
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
});`;

const derivarNuevo = `app.MapPut("/informes-admision/{id}/derivar", (int id, DerivarRequest req) =>
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
        SET IdProfesionalDerivado = @IdProf, ObservacionesEntrevista = ISNULL(@Obs, ObservacionesEntrevista),
            Estado = 'Derivado', MotivoRechazo = NULL
        WHERE IdInforme = @Id", con);
    cmdUpd.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmdUpd.Parameters.AddWithValue("@Obs", (object)req.Observaciones ?? DBNull.Value);
    cmdUpd.Parameters.AddWithValue("@Id", id);
    cmdUpd.ExecuteNonQuery();

    // Vincular paciente con profesional con estado 'Nueva'
    var cmdVinc = new Microsoft.Data.SqlClient.SqlCommand(@"
        IF EXISTS (SELECT 1 FROM PacienteProfesional WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf)
            UPDATE PacienteProfesional SET EstadoAsignacion = 'Nueva', FechaAsignacion = GETDATE()
            WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf
        ELSE
            INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, FechaAsignacion, EstadoAsignacion)
            VALUES (@IdPac, @IdProf, GETDATE(), 'Nueva')", con);
    cmdVinc.Parameters.AddWithValue("@IdPac", idPac);
    cmdVinc.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmdVinc.ExecuteNonQuery();

    // Primera evolucion en la HC a nombre del admisionista (se muestra en azul)
    var cmdEvo = new Microsoft.Data.SqlClient.SqlCommand(@"
        DECLARE @IdHistoria INT = (SELECT TOP 1 IdHistoria FROM HistoriaClinica WHERE IdPaciente = @IdPac);
        IF @IdHistoria IS NULL
        BEGIN
            INSERT INTO HistoriaClinica (IdPaciente, FechaApertura) VALUES (@IdPac, GETDATE());
            SET @IdHistoria = SCOPE_IDENTITY();
        END
        DECLARE @IdAdm INT = (SELECT TOP 1 IdProfesional FROM Profesional WHERE Matricula = 'ADM-001');
        DECLARE @NombreProf NVARCHAR(200) = (SELECT TOP 1 Nombre + ' ' + ISNULL(Apellido,'') FROM Profesional WHERE IdProfesional = @IdProf);
        IF @IdAdm IS NOT NULL
            INSERT INTO Evolucion (IdHistoria, IdProfesional, Fecha, Diagnostico, Observaciones, PlanTratamiento, TipoCierre)
            VALUES (@IdHistoria, @IdAdm, GETDATE(), 'Evaluacion de admision',
                    ISNULL(@Obs, 'Entrevista de admision realizada.'),
                    'Derivado a: ' + ISNULL(@NombreProf, ''), 'Derivacion')", con);
    cmdEvo.Parameters.AddWithValue("@IdPac", idPac);
    cmdEvo.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmdEvo.Parameters.AddWithValue("@Obs", (object)req.Observaciones ?? DBNull.Value);
    cmdEvo.ExecuteNonQuery();

    return Results.NoContent();
});

// ── Mis Pacientes con estado de asignacion ──────────────────
app.MapGet("/profesionales/{id}/pacientes-detalle", (int id) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT p.IdPaciente, p.Nombre, p.Dni, p.Mail, p.Telefono,
               ISNULL(pp.EstadoAsignacion, 'Confirmada') AS EstadoAsignacion, pp.FechaAsignacion
        FROM PacienteProfesional pp
        INNER JOIN Paciente p ON p.IdPaciente = pp.IdPaciente
        WHERE pp.IdProfesional = @Id AND ISNULL(pp.EstadoAsignacion, 'Confirmada') <> 'Rechazada'
        ORDER BY pp.FechaAsignacion DESC", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            idPaciente       = (int)reader["IdPaciente"],
            nombre           = reader["Nombre"].ToString(),
            dni              = reader["Dni"] == DBNull.Value ? "" : reader["Dni"].ToString(),
            mail             = reader["Mail"] == DBNull.Value ? "" : reader["Mail"].ToString(),
            telefono         = reader["Telefono"] == DBNull.Value ? "" : reader["Telefono"].ToString(),
            estadoAsignacion = reader["EstadoAsignacion"].ToString(),
            fechaAsignacion  = reader["FechaAsignacion"] == DBNull.Value ? "" : ((DateTime)reader["FechaAsignacion"]).ToString("dd/MM/yyyy")
        });
    }
    return Results.Ok(lista);
});

// ── Confirmar asignacion (el profesional acepta al paciente) ─
app.MapPut("/asignaciones/estado", (AsignacionEstadoRequest req) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE PacienteProfesional SET EstadoAsignacion = @Estado
        WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf", con);
    cmd.Parameters.AddWithValue("@Estado", req.Estado);
    cmd.Parameters.AddWithValue("@IdPac", req.IdPaciente);
    cmd.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmd.ExecuteNonQuery();
    return Results.NoContent();
});

// ── Rechazo por el profesional: vuelve a la bandeja del admisionista ─
app.MapPut("/informes-admision/rechazar", (RechazoRequest req) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();

    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE PacienteProfesional SET EstadoAsignacion = 'Rechazada'
        WHERE IdPaciente = @IdPac AND IdProfesional = @IdProf;

        UPDATE InformeAdmision SET Estado = 'Rechazado', MotivoRechazo = @Motivo
        WHERE IdInforme = (SELECT TOP 1 IdInforme FROM InformeAdmision WHERE IdPaciente = @IdPac ORDER BY FechaCreacion DESC);", con);
    cmd.Parameters.AddWithValue("@IdPac", req.IdPaciente);
    cmd.Parameters.AddWithValue("@IdProf", req.IdProfesional);
    cmd.Parameters.AddWithValue("@Motivo", (object)req.Motivo ?? DBNull.Value);
    cmd.ExecuteNonQuery();
    return Results.NoContent();
});`;

if (c.includes('/informes-admision/rechazar')) {
    console.log('1. Endpoints ya existian');
} else if (c.includes(derivarViejo)) {
    c = c.replace(derivarViejo, derivarNuevo);
    console.log('1. Endpoint derivar actualizado + 3 endpoints nuevos OK');
} else {
    console.log('1. ERROR: no se encontro el endpoint derivar original');
}

// ── 2. Records de request ───────────────────────────────────
if (!c.includes('record AsignacionEstadoRequest')) {
    c = c + '\npublic record AsignacionEstadoRequest(int IdPaciente, int IdProfesional, string Estado);\npublic record RechazoRequest(int IdPaciente, int IdProfesional, string Motivo);\n';
    console.log('2. Records agregados OK');
} else {
    console.log('2. Records ya existian');
}

// ── 3. MotivoRechazo en el endpoint detalle ─────────────────
const sel = 'ia.FechaEntrevista, ia.ObservacionesEntrevista, ia.IdProfesionalDerivado, ia.CodigoSala,';
if (c.includes('ia.MotivoRechazo')) {
    console.log('3. MotivoRechazo ya estaba en el SELECT');
} else if (c.includes(sel)) {
    c = c.replace(sel, 'ia.FechaEntrevista, ia.ObservacionesEntrevista, ia.IdProfesionalDerivado, ia.CodigoSala, ia.MotivoRechazo,');
    console.log('3. MotivoRechazo agregado al SELECT OK');
} else {
    console.log('3. ERROR: no se encontro el SELECT del detalle');
}

const map = 'codigoSala              = reader["CodigoSala"] == DBNull.Value ? "" : reader["CodigoSala"].ToString(),';
if (c.includes('motivoRechazo')) {
    console.log('4. motivoRechazo ya estaba en el mapeo');
} else if (c.includes(map)) {
    c = c.replace(map, map + '\n            motivoRechazo           = reader["MotivoRechazo"] == DBNull.Value ? "" : reader["MotivoRechazo"].ToString(),');
    console.log('4. motivoRechazo agregado al mapeo OK');
} else {
    console.log('4. ERROR: no se encontro el mapeo');
}

fs.writeFileSync(path, c, 'utf8');
console.log('Listo.');