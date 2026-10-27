using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

// La sala de un turno ya no espera al profesional. Hasta ahora el código de la sala (Turno.CodigoSala) lo generaba el profesional a mano (PUT /turnos/{id}/generar-sala) y, sin él, el paciente
// recibía «Todavía no se generó la sala» y no podía ni entrar a esperar. El código es solo el nombre de la sala para el signaling-server: no necesita que el profesional esté. Ahora, cuando
// alguien del turno (el paciente o el profesional) pide entrar con la sala ABIERTA (desde 10 minutos antes y hasta que cierra: EstadoDeSala), el servidor la genera él mismo, una sola vez
// (UPDATE condicional: si dos entran a la vez, uno solo escribe y los dos ven el mismo código). Respeta el consentimiento: sin el de tratamiento y telemedicina firmado y vigente no se genera,
// igual que generar-sala. Fuera de la ventana (más de 10 minutos antes, o el turno ya pasó) no hace nada: sigue mandando el rechazo de siempre. Docs: docs/ESTADO_DE_SALA.md.
public static class SalaAutomatica
{
    /// <summary>Genera el código si falta y corresponde. Devuelve true si lo generó ahora. Nunca lanza: si algo falla, el acceso sigue su camino y responde como siempre.</summary>
    public static bool AsegurarCodigo(int idTurno, Identidad yo, IOrigenSalaRepositorio origen, VideoOptions video, ConsentimientoOptions consentimiento, DateTime ahoraLocal)
    {
        try
        {
            var t = origen.ObtenerTurno(idTurno);
            if (t is null || !string.IsNullOrWhiteSpace(t.CodigoSala)) return false;
            if (TextoNormalizado.Normalizar(t.Estado) != AccesoSalaService.EstadoTurnoConfirmado) return false;
            var esDelTurno = (yo.EsPaciente && yo.IdPaciente == t.IdPaciente) || (yo.EsProfesional && yo.IdProfesional == t.IdProfesional);
            if (!esDelTurno) return false;
            if (EstadoDeSala.Evaluar(t.Fecha.Date + t.Hora, t.DuracionMinutos, ahoraLocal, video).Fase != FaseSala.Abierta) return false;
            if (!ConsentimientoTratamiento.Estado(t.IdPaciente, consentimiento, ahoraLocal).Vigente) return false;

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("UPDATE Turno SET CodigoSala = @S WHERE IdTurno = @T AND (CodigoSala IS NULL OR LTRIM(RTRIM(CodigoSala)) = N'')", con);
            cmd.Parameters.AddWithValue("@S", "SALA-" + idTurno + "-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant());
            cmd.Parameters.AddWithValue("@T", idTurno);
            return cmd.ExecuteNonQuery() == 1;
        }
        catch (Exception) { return false; }
    }
}
