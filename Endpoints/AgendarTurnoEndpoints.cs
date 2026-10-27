using Microsoft.Data.SqlClient;

// «Agendar turno» del profesional (docs/AGENDAR_TURNO.md): para un paciente de su cartera, en un horario libre de su propia agenda. Lo crea el profesional, queda Confirmado (no requiere aceptación de
// nadie) y el paciente se entera por la campanita. Los caminos que ya existían (Cita urgente = ahora mismo; proponer un turno = queda Pendiente hasta que el paciente lo confirme) no cambian.
public record AgendarTurnoRequest(int IdPaciente, string? Fecha, string? Hora, string? Motivo);

public static class AgendarTurnoEndpoints
{
    public static IEndpointRouteBuilder MapAgendarTurno(this IEndpointRouteBuilder app)
    {
        app.MapPost("/turnos/agendar", (AgendarTurnoRequest req, HttpContext ctx, IdentidadActualService identidad, TurnoReglasService reglas) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProfesional) return Results.Json(new { mensaje = "No se pudo identificar tu usuario profesional." }, statusCode: 403);
            if (req.IdPaciente <= 0) return Results.Json(new { mensaje = "Elegí el paciente." }, statusCode: 400);
            if (!MomentoDeTurno.TryParsear(req.Fecha, req.Hora, out var momento))
                return Results.Json(new { mensaje = "La fecha o la hora del turno no tienen un formato válido." }, statusCode: 400);
            if (req.Motivo is { Length: > 500 }) return Results.Json(new { mensaje = "El motivo no puede superar los 500 caracteres." }, statusCode: 400);

            // Solo un paciente de su cartera (el mismo criterio que usa «Derivar a psiquiatría»).
            if (!PacienteACargo(req.IdPaciente, idProfesional))
                return Results.Json(new { mensaje = "Ese paciente no está a tu cargo: solo podés agendar turnos para tus pacientes." }, statusCode: 403);

            var r = reglas.Agendar(req.IdPaciente, idProfesional, momento, req.Motivo);
            if (!r.Regla.Ok) return r.Regla.ComoConflicto();

            // El aviso al paciente y la constancia (el turno ya existe: si esto fallara, el turno sigue bien y el paciente lo ve en su calendario).
            Avisar(r.IdTurno!.Value, req.IdPaciente, idProfesional, momento, new AutorDelCierre(yo.IdUsuario, yo.Modulo ?? yo.Rol, AuditoriaIds.Calidad(yo, req.IdPaciente)));
            return Results.Created("/turnos", new { idTurno = r.IdTurno, estado = "Confirmado", mensaje = "Agendaste el turno. Le avisamos al paciente." });
        }).Solo(Quien.Profesional);

        return app;
    }

    private static bool PacienteACargo(int idPaciente, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @M AND ISNULL(EstadoAsignacion, 'Confirmada') NOT IN ('Rechazada', 'Cancelada')", con);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        cmd.Parameters.AddWithValue("@M", idProfesional);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static void Avisar(int idTurno, int idPaciente, int idProfesional, DateTime momento, AutorDelCierre autor)
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var tx = con.BeginTransaction();
            var profesional = ADComun.NombreProfesional(con, tx, idProfesional);
            using (var cmd = new SqlCommand("UPDATE Turno SET ConfirmadoPaciente = 1 WHERE IdTurno = @T", con, tx))
            {
                cmd.Parameters.AddWithValue("@T", idTurno);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SqlCommand("INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link) VALUES (@P, @T, @M, NULL)", con, tx))
            {
                cmd.Parameters.AddWithValue("@P", idPaciente);
                cmd.Parameters.AddWithValue("@T", "Tu profesional te agendó un turno");
                cmd.Parameters.AddWithValue("@M", $"{profesional} te agendó un turno para el {Formato.FechaHora(momento)}. Ya está confirmado: lo ves en Turnos.");
                cmd.ExecuteNonQuery();
            }
            ADComun.Auditar(con, tx, autor, "Turno agendado por el profesional", "Turno", idTurno, $"Turno #{idTurno}, paciente #{idPaciente}, profesional #{idProfesional}: creado Confirmado");
            tx.Commit();
        }
        catch (SqlException) { /* el turno ya quedó creado */ }
    }
}
