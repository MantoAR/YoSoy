// Cierre de seguridad, Paso 2: punto único de decisión para "¿puede esta identidad ver/
// escribir los datos clínicos de ESTE paciente?" — mismo patrón que AutorizacionCasos y
// VisibilidadConversacion (lee IdentidadActualService, nunca un id del cuerpo/URL sin
// contrastar). "Asociado" usa el mismo criterio que ya vale para chats ("equipo tratante
// vigente"): PacienteProfesionalDAO, la relación que ya mantienen la asignación y los
// turnos — no se inventa una regla nueva.
//
// Antes: cualquier Profesional autenticado veía la historia clínica de cualquier paciente
// (más amplio que Casos, que ya filtraba por asignación). Ahora: solo el profesional
// asociado a ESE paciente, o el Configurador. El paciente nunca pasa por acá (ninguna
// pantalla de Pantalla_Paciente llama estos endpoints).
public static class AutorizacionClinica
{
    public static bool PuedePaciente(HttpContext ctx, int idPaciente)
    {
        var (esAdmin, idProfesional) = AutorizacionCasos.Resolver(ctx);
        if (esAdmin) return true;
        if (idProfesional is not int yo) return false;
        return new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(yo).Contains(idPaciente);
    }
}
