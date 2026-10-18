// Cierre de seguridad, Paso 6: mecanismo de auditoría "solo ids, nunca contenido" que ya
// usaba la apertura de conversaciones de chat (Mejora de chats y centrado) — se extrae
// acá, compartido, para reusarlo también en el acceso a datos clínicos en vez de duplicar
// el mismo patrón. Nunca interrumpe la respuesta si falla (ver catch): la auditoría no
// debe poder tumbar un pedido real.
//
// Etapa 2 de roles y permisos: estos registros son la AUDITORÍA CLÍNICA (categoría
// 'Clinica', la ve el Profesional auditor). Además de los ids, se guarda en qué calidad se
// accedió (tratante, auditor, configurador, paciente) y desde qué módulo: como Profesional y
// Profesional auditor comparten módulo, esa es la forma de distinguir "atender" de "auditar".
public static class AuditoriaIds
{
    public static void Registrar(HttpContext ctx, string accion, string entidad, int idEntidad, int? idPaciente = null)
    {
        var yo = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
        if (yo is null) return;
        try
        {
            var calidad = Calidad(yo, idPaciente);
            var detalle = $"{entidad} #{idEntidad}" + (idPaciente is int idPac ? $", paciente #{idPac}" : "")
                        + (calidad is null ? "" : $", como {calidad}");
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = yo.IdUsuario, Accion = accion, Entidad = entidad, IdEntidad = idEntidad, Detalle = detalle,
                Categoria = CategoriaAuditoria.Clinica, Espacio = yo.Modulo ?? yo.Rol
            });
        }
        catch (Exception) { /* la auditoría no debe impedir responder */ }
    }

    // "tratante" si el paciente está asignado al profesional; si no, la razón por la que pudo
    // entrar (admisión, auditor; CRM en las conversaciones). Sin paciente (listados completos), la del rol.
    public static string? Calidad(Identidad yo, int? idPaciente)
    {
        if (yo.EsPaciente) return "paciente";
        if (idPaciente is int p && yo.IdProfesional is int prof &&
            new PacienteProfesionalDAO().ObtenerIdPacientesDeProfesional(prof).Contains(p))
            return "tratante";
        // Etapa 3: Admisión entra a pacientes en admisión; el auditor, a cualquiera.
        if (idPaciente is int enAdm && yo.HaceAdmision && AutorizacionClinica.EnAdmision(enAdm)) return "admisión";
        if (yo.EsAuditor) return "auditor";
        if (yo.EsCrm) return "crm";
        if (yo.EsConfigurador) return "configurador";
        if (yo.EsProfesional) return "profesional";
        return null;
    }
}

// AUDITORÍA ADMINISTRATIVA de ingresos (categoría 'Administrativa', la ve el Configurador):
// ingresos, fallidos, bloqueos y cambios de módulo. Sin contraseñas ni tokens, nunca.
public static class AuditoriaIngreso
{
    public static void Registrar(int? idUsuario, string accion, string? detalle, string? modulo = null)
    {
        try
        {
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = idUsuario, Accion = accion, Entidad = "Usuario", IdEntidad = idUsuario,
                Detalle = detalle is null ? null : (detalle.Length <= 500 ? detalle : detalle[..500]),
                Categoria = CategoriaAuditoria.Administrativa, Espacio = modulo
            });
        }
        catch (Exception) { /* igual que AuditoriaIds: registrar no puede impedir el ingreso */ }
    }
}
