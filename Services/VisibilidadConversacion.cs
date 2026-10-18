// Punto único de decisión para "¿puede esta identidad ver esta conversación?" — mismo
// patrón que AutorizacionCasos (lee IdentidadActualService, nunca un id del cuerpo/URL sin
// contrastar). Usado por todos los endpoints de /mensajeria que tocan una conversación
// puntual o la lista completa. Ver CAMBIOS_AGUS.md, "Pertenencia de conversaciones" —
// resumen de la regla (Paso 1, decidida por el pedido):
//   - Escalada (DerivadaHumano/Urgente) y sin asignar: cualquier Profesional + CRM.
//   - Escalada y asignada: solo quien la tiene asignada (Conversacion.IdProfesionalAsignado)
//     + CRM.
//   - Todo lo demás (Activa, Cerrada - nunca escalada o ya se resolvió el escalamiento):
//     solo el equipo tratante vigente del paciente (PacienteProfesional) + CRM.
//     Sin paciente identificado todavía: solo CRM.
// Paciente (rol) nunca pasa por acá: sus propias conversaciones las resuelve
// IServicioMensajes/BotReservasService por teléfono, no por este módulo administrador.
// Etapa 3 de roles y permisos: el lado "administrador" de los chats es el módulo CRM (antes
// el Configurador, que ya no opera conversaciones). Lo resuelve AutorizacionCasos.Resolver.
public static class VisibilidadConversacion
{
    public static bool Puede(HttpContext ctx, Conversacion c) =>
        Puede(AutorizacionCasos.Resolver(ctx), c.Estado, c.IdProfesionalAsignado, c.IdPaciente);

    public static bool Puede(HttpContext ctx, ConversacionResumen c) =>
        Puede(AutorizacionCasos.Resolver(ctx), c.Estado, c.IdProfesionalAsignado, c.IdPaciente);

    // Pensado para /conversaciones y el buscador (que filtra client-side sobre esa misma
    // lista): una sola pasada, un solo HashSet de "mis pacientes", no N consultas.
    public static List<ConversacionResumen> Filtrar(HttpContext ctx, List<ConversacionResumen> lista)
    {
        var (esAdmin, idProfesional) = AutorizacionCasos.Resolver(ctx);
        if (esAdmin) return lista;
        if (idProfesional is not int yo) return new List<ConversacionResumen>();

        var misPacientes = new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(yo).ToHashSet();
        return lista.Where(c => PuedeConMisPacientes(c.Estado, c.IdProfesionalAsignado, c.IdPaciente, yo, misPacientes)).ToList();
    }

    private static bool Puede((bool EsAdministrador, int? IdProfesional) yo, string estado, int? idProfesionalAsignado, int? idPaciente)
    {
        if (yo.EsAdministrador) return true;
        if (yo.IdProfesional is not int idProfesional) return false;   // paciente o sin identidad: nunca por este camino

        bool escalada = estado is "DerivadaHumano" or "Urgente";
        if (escalada) return idProfesionalAsignado is null || idProfesionalAsignado == idProfesional;

        if (idPaciente is not int idPac) return false;   // sin paciente identificado: solo CRM
        return new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(idProfesional).Contains(idPac);
    }

    private static bool PuedeConMisPacientes(string estado, int? idProfesionalAsignado, int? idPaciente, int idProfesional, HashSet<int> misPacientes)
    {
        bool escalada = estado is "DerivadaHumano" or "Urgente";
        if (escalada) return idProfesionalAsignado is null || idProfesionalAsignado == idProfesional;
        return idPaciente is int idPac && misPacientes.Contains(idPac);
    }
}
