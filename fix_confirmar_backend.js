const fs = require('fs');
const path = 'D:/YoSoy/Program.cs';
let c = fs.readFileSync(path, 'utf8');

if (c.includes('/turnos/{id}/confirmar-paciente')) {
    console.log('Los endpoints ya existian.');
    process.exit(0);
}

const endpoints = `
// ── Paciente confirma el turno propuesto ────────────────────
app.MapPut("/turnos/{id}/confirmar-paciente", (int id) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE Turno SET ConfirmadoPaciente = 1, IdEstadoTurno = 2 WHERE IdTurno = @Id;
        SELECT IdProfesional, IdPaciente FROM Turno WHERE IdTurno = @Id;", con);
    cmd.Parameters.AddWithValue("@Id", id);
    int idProf = 0, idPac = 0;
    using (var r = cmd.ExecuteReader())
    {
        if (r.Read()) { idProf = (int)r["IdProfesional"]; idPac = (int)r["IdPaciente"]; }
    }
    return Results.Ok(new { idProfesional = idProf, idPaciente = idPac });
});

// ── Turnos confirmados por paciente, pendientes de sala (para el profesional) ──
app.MapGet("/profesionales/{id}/turnos-confirmados", (int id) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        SELECT t.IdTurno, t.IdPaciente, p.Nombre AS NombrePaciente, t.Fecha, t.Hora,
               t.CodigoSala, t.Motivo
        FROM Turno t
        INNER JOIN Paciente p ON p.IdPaciente = t.IdPaciente
        WHERE t.IdProfesional = @Id AND t.ConfirmadoPaciente = 1 AND t.IdEstadoTurno = 2
        ORDER BY t.Fecha, t.Hora", con);
    cmd.Parameters.AddWithValue("@Id", id);
    var reader = cmd.ExecuteReader();
    var lista = new System.Collections.Generic.List<object>();
    while (reader.Read())
    {
        lista.Add(new {
            idTurno        = (int)reader["IdTurno"],
            idPaciente     = (int)reader["IdPaciente"],
            nombrePaciente = reader["NombrePaciente"].ToString(),
            fecha          = ((DateTime)reader["Fecha"]).ToString("dd/MM/yyyy"),
            hora           = reader["Hora"] == DBNull.Value ? "" : ((TimeSpan)reader["Hora"]).ToString(@"hh\:mm"),
            codigoSala     = reader["CodigoSala"] == DBNull.Value ? "" : reader["CodigoSala"].ToString(),
            motivo         = reader["Motivo"] == DBNull.Value ? "" : reader["Motivo"].ToString()
        });
    }
    return Results.Ok(lista);
});

// ── El profesional genera la sala: crea codigo y notifica al paciente ──
app.MapPut("/turnos/{id}/generar-sala", (int id) =>
{
    using var con = Conexion.ObtenerConexion();
    con.Open();
    var codigoSala = "SALA-" + id + "-" + DateTime.Now.Ticks.ToString("x").ToUpper();
    var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
        UPDATE Turno SET CodigoSala = @Sala WHERE IdTurno = @Id;
        SELECT IdPaciente, Fecha, Hora FROM Turno WHERE IdTurno = @Id;", con);
    cmd.Parameters.AddWithValue("@Sala", codigoSala);
    cmd.Parameters.AddWithValue("@Id", id);
    int idPac = 0; DateTime fecha = DateTime.Now; TimeSpan hora = TimeSpan.Zero;
    using (var r = cmd.ExecuteReader())
    {
        if (r.Read()) { idPac = (int)r["IdPaciente"]; fecha = (DateTime)r["Fecha"]; if (r["Hora"] != DBNull.Value) hora = (TimeSpan)r["Hora"]; }
    }
    if (idPac > 0)
    {
        var fechaLegible = fecha.ToString("dd/MM/yyyy") + " a las " + hora.ToString(@"hh\:mm") + " hs";
        var cmdNotif = new Microsoft.Data.SqlClient.SqlCommand(@"
            INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link)
            VALUES (@IdPac, @Titulo, @Mensaje, @Link)", con);
        cmdNotif.Parameters.AddWithValue("@IdPac", idPac);
        cmdNotif.Parameters.AddWithValue("@Titulo", "Turno confirmado - Sala lista");
        cmdNotif.Parameters.AddWithValue("@Mensaje", "Tu turno del " + fechaLegible + " esta confirmado. Codigo de sala para ese dia: " + codigoSala);
        cmdNotif.Parameters.AddWithValue("@Link", codigoSala);
        cmdNotif.ExecuteNonQuery();
    }
    return Results.Ok(new { codigoSala });
});

`;

c = c.replace('app.Run();', endpoints + 'app.Run();');
fs.writeFileSync(path, c, 'utf8');
console.log('3 endpoints agregados OK (confirmar-paciente, turnos-confirmados, generar-sala)');