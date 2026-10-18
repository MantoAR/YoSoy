// Cierre de seguridad, Paso 2: punto único de decisión para "¿puede esta identidad ver/
// escribir los datos clínicos de ESTE paciente?" — mismo patrón que AutorizacionCasos y
// VisibilidadConversacion (lee IdentidadActualService, nunca un id del cuerpo/URL sin
// contrastar). "Asociado" usa el mismo criterio que ya vale para chats ("equipo tratante
// vigente"): PacienteProfesionalDAO, la relación que ya mantienen la asignación y los
// turnos — no se inventa una regla nueva.
//
// Antes: cualquier Profesional autenticado veía la historia clínica de cualquier paciente
// (más amplio que Casos, que ya filtraba por asignación). Ahora: solo el profesional
// asociado a ESE paciente (hasta la etapa 2, también el Configurador). El paciente nunca pasa por acá (ninguna
// pantalla de Pantalla_Paciente llama estos endpoints).
//
// Etapa 3 de roles y permisos (16/10): el Configurador deja de tener acceso clínico.
//   - Escribir (PuedePaciente): el profesional tratante, o Admisión mientras el paciente
//     está en admisión (la entrevista deja una evolución).
//   - Leer (PuedeVerPaciente): lo anterior, más el Profesional auditor sobre cualquier
//     paciente. Cada lectura queda en la auditoría clínica (AuditoriaIds) con la calidad
//     ("tratante", "auditor"), así se distingue atender de auditar.
// CRM nunca pasa: su token no tiene el rol profesional.
public static class AutorizacionClinica
{
    public static bool PuedePaciente(HttpContext ctx, int idPaciente)
    {
        var yo = Identidad(ctx);
        if (yo is null || !yo.EsProfesional || yo.IdProfesional is not int idProfesional) return false;
        if (new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(idProfesional).Contains(idPaciente)) return true;
        return yo.HaceAdmision && EnAdmision(idPaciente);
    }

    public static bool PuedeVerPaciente(HttpContext ctx, int idPaciente)
    {
        var yo = Identidad(ctx);
        if (yo is null) return false;
        if (yo.EsAuditor) return true;
        return PuedePaciente(ctx, idPaciente);
    }

    private static Identidad? Identidad(HttpContext ctx) =>
        ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);

    // Tiene un informe de admisión que todavía no se derivó a un profesional.
    public static bool EnAdmision(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM InformeAdmision WHERE IdPaciente = @Id AND Estado <> 'Derivado') THEN 1 ELSE 0 END", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        return (int)cmd.ExecuteScalar()! == 1;
    }
}
