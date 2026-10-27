using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

// «Qué cambió» para la pantalla del paciente, barato a propósito: la pantalla lo consulta cada ~15 segundos mientras la pestaña está visible, y solo si la huella cambió vuelve a pedir los primeros pasos
// completos (que son más caros). Se registra con app.MapNovedadesPaciente().
//
//   GET /pacientes/{id}/novedades    { huella, noLeidas }  — la huella resume en un texto corto lo que hace cambiar el Inicio del paciente: consentimiento, formulario «Sobre mí» (estado y asignación),
//                                    propuesta pendiente, vínculo con el profesional, sus turnos, y las notificaciones sin leer. Solo del propio paciente.
//
// Son pocas consultas con COUNT/CHECKSUM_AGG sobre las filas de UNA persona (índices por IdPaciente); no devuelve datos clínicos ni de otras personas.
public static class NovedadesPacienteEndpoints
{
    public static IEndpointRouteBuilder MapNovedadesPaciente(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pacientes/{id}/novedades", (int id, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo is { EsPaciente: true } && yo.IdPaciente != id)
                return Results.Json(new { mensaje = "Solo podés ver tus propias novedades." }, statusCode: 403);
            var ahora = reloj.GetLocalNow().DateTime;
            return Results.Ok(Calcular(id, ahora));
        }).Solo(Quien.Paciente);
        return app;
    }

    public record Novedades(string Huella, int NoLeidas);

    public static Novedades Calcular(int idPaciente, DateTime ahora)
    {
        var partes = new List<string>();
        var noLeidas = 0;
        // Una propuesta que ya venció pasa a «Vencida» antes de mirar (es una consulta barata; sin esto el paciente la vería hasta que alguien más abriera la lista).
        AsignacionAutomaticaService.VencerVencidas(ahora);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using (var cmd = new SqlCommand(@"
            SELECT
              (SELECT TOP 1 CONCAT(IdInforme, N':', Estado) FROM InformeAdmision WHERE IdPaciente = @P ORDER BY IdInforme DESC),
              (SELECT CONCAT(COUNT(*), N':', ISNULL(MAX(IdConsentimiento), 0), N':', ISNULL(CHECKSUM_AGG(CHECKSUM(IdConsentimiento, Estado, FechaRevocacion)), 0)) FROM Consentimiento WHERE IdPaciente = @P),
              (SELECT CONCAT(COUNT(*), N':', ISNULL(CHECKSUM_AGG(CHECKSUM(IdPacienteProfesional, IdProfesional, EstadoAsignacion)), 0)) FROM PacienteProfesional WHERE IdPaciente = @P),
              (SELECT CONCAT(COUNT(*), N':', ISNULL(CHECKSUM_AGG(CHECKSUM(IdTurno, IdEstadoTurno, Fecha, Hora, IdProfesional)), 0)) FROM Turno WHERE IdPaciente = @P AND Fecha >= DATEADD(DAY, -1, CAST(@Hoy AS DATE))),
              (SELECT CONCAT(COUNT(*), N':', ISNULL(MAX(IdNotificacion), 0)) FROM Notificacion WHERE IdPaciente = @P AND Leida = 0),
              (SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @P AND Leida = 0)", con))
        {
            cmd.Parameters.AddWithValue("@P", idPaciente);
            cmd.Parameters.AddWithValue("@Hoy", ahora.Date);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                for (var i = 0; i < 5; i++) partes.Add(r.IsDBNull(i) ? "-" : Convert.ToString(r.GetValue(i)) ?? "-");
                noLeidas = Convert.ToInt32(r.GetValue(5));
            }
        }
        // Migración 32 (propuesta automática): opcional. Sin ella, no hay propuesta ni estado de asignación que mirar.
        if (AsignacionAutomaticaService.Disponible())
        {
            using var cmd = new SqlCommand(@"
                SELECT (SELECT CONCAT(COUNT(*), N':', ISNULL(CHECKSUM_AGG(CHECKSUM(IdPropuesta, Estado, ExpiraEn)), 0)) FROM PropuestaAsignacion WHERE IdPaciente = @P AND Estado = N'Pendiente'),
                       (SELECT TOP 1 ISNULL(AsignacionEstado, N'') FROM InformeAdmision WHERE IdPaciente = @P ORDER BY IdInforme DESC)", con);
            cmd.Parameters.AddWithValue("@P", idPaciente);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { partes.Add(r.IsDBNull(0) ? "-" : r.GetString(0)); partes.Add(r.IsDBNull(1) ? "-" : r.GetString(1)); }
        }
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", partes))))[..16];
        return new Novedades(huella, noLeidas);
    }
}
