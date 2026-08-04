const fs = require('fs');
const path = 'D:/YoSoy/Program.cs';
let c = fs.readFileSync(path, 'utf8');
let cambios = 0;

// ── 1. Reemplazar el endpoint de entrevista: genera codigo + notificacion ──
const entrevistaViejo = `app.MapPut("/informes-admision/{id}/entrevista", (int id, EntrevistaRequest req) =>
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
});`;

const entrevistaNuevo = `app.MapPut("/informes-admision/{id}/entrevista", (int id, EntrevistaRequest req) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();

    // Generar codigo de sala unico para la entrevista
    var codigoSala = "ADM-" + id + "-" + DateTime.Now.Ticks.ToString("x").ToUpper();

    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE InformeAdmision
        SET FechaEntrevista = @Fecha, ObservacionesEntrevista = @Obs, Estado = 'En entrevista', CodigoSala = @Sala
        WHERE IdInforme = @Id;
        SELECT IdPaciente FROM InformeAdmision WHERE IdInforme = @Id;", con);
    cmd.Parameters.AddWithValue("@Fecha", req.Fecha);
    cmd.Parameters.AddWithValue("@Obs", (object)req.Observaciones ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Sala", codigoSala);
    cmd.Parameters.AddWithValue("@Id", id);
    var idPacObj = cmd.ExecuteScalar();

    if (idPacObj != null)
    {
        var fechaLegible = req.Fecha.ToString("dd/MM/yyyy") + " a las " + req.Fecha.ToString("HH:mm") + " hs";
        var cmdNotif = new Microsoft.Data.SqlClient.SqlCommand(@"
            INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link)
            VALUES (@IdPac, @Titulo, @Mensaje, @Link)", con);
        cmdNotif.Parameters.AddWithValue("@IdPac", (int)idPacObj);
        cmdNotif.Parameters.AddWithValue("@Titulo", "Entrevista de admision programada");
        cmdNotif.Parameters.AddWithValue("@Mensaje", "Tu entrevista de admision fue programada para el " + fechaLegible + ". Codigo de sala: " + codigoSala);
        cmdNotif.Parameters.AddWithValue("@Link", codigoSala);
        cmdNotif.ExecuteNonQuery();
    }

    return Results.Ok(new { codigoSala });
});

// ── Notificaciones ──────────────────────────────────────────
app.MapGet("/pacientes/{id}/notificaciones", (int id) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT IdNotificacion, Titulo, Mensaje, Link, Leida, FechaCreacion
        FROM Notificacion WHERE IdPaciente = @Id ORDER BY FechaCreacion DESC", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            idNotificacion = (int)reader["IdNotificacion"],
            titulo         = reader["Titulo"] == DBNull.Value ? "" : reader["Titulo"].ToString(),
            mensaje        = reader["Mensaje"] == DBNull.Value ? "" : reader["Mensaje"].ToString(),
            link           = reader["Link"] == DBNull.Value ? null : reader["Link"].ToString(),
            leida          = reader["Leida"] != DBNull.Value && (bool)reader["Leida"],
            fecha          = ((DateTime)reader["FechaCreacion"]).ToString("dd/MM/yyyy HH:mm")
        });
    }
    return Results.Ok(lista);
});

app.MapPut("/notificaciones/{id}/leida", (int id) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(
        "UPDATE Notificacion SET Leida = 1 WHERE IdNotificacion = @Id", con);
    cmd.Parameters.AddWithValue("@Id", id);
    cmd.ExecuteNonQuery();
    return Results.NoContent();
});`;

if (c.includes('/pacientes/{id}/notificaciones')) {
    console.log('1. Los endpoints de notificaciones ya existian');
} else if (c.includes(entrevistaViejo)) {
    c = c.replace(entrevistaViejo, entrevistaNuevo);
    cambios++;
    console.log('1. Endpoint entrevista actualizado + endpoints de notificaciones OK');
} else {
    console.log('1. ERROR: no se encontro el endpoint de entrevista original. Revisar manualmente.');
}

// ── 2. Agregar CodigoSala al endpoint detalle ───────────────
const sel1 = 'ia.FechaEntrevista, ia.ObservacionesEntrevista, ia.IdProfesionalDerivado,';
if (c.includes('ia.CodigoSala')) {
    console.log('2. CodigoSala ya estaba en el SELECT');
} else if (c.includes(sel1)) {
    c = c.replace(sel1, 'ia.FechaEntrevista, ia.ObservacionesEntrevista, ia.IdProfesionalDerivado, ia.CodigoSala,');
    cambios++;
    console.log('2. CodigoSala agregado al SELECT OK');
} else {
    console.log('2. ERROR: no se encontro el SELECT del detalle');
}

const map1 = 'idProfesionalDerivado   = reader["IdProfesionalDerivado"] == DBNull.Value ? (int?)null : (int)reader["IdProfesionalDerivado"],';
if (c.includes('codigoSala')) {
    console.log('3. codigoSala ya estaba en el mapeo');
} else if (c.includes(map1)) {
    c = c.replace(map1, map1 + '\n            codigoSala              = reader["CodigoSala"] == DBNull.Value ? "" : reader["CodigoSala"].ToString(),');
    cambios++;
    console.log('3. codigoSala agregado al mapeo OK');
} else {
    console.log('3. ERROR: no se encontro el mapeo del detalle');
}

fs.writeFileSync(path, c, 'utf8');
console.log('\nCambios aplicados: ' + cambios);