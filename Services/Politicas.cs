using System.Security.Claims;

// Roles y permisos: quién puede llamar a cada endpoint, según el módulo, el
// permiso y el privilegio que viajan en el token (ver Accesos.cs y JwtEmisorService).
//
// Reparto acordado el 16/10:
//   - Configurador: usuarios, permisos, configuración y auditoría administrativa. Nada clínico.
//   - Profesional: lo suyo (sus pacientes, su agenda, sus casos).
//   - Profesional con privilegio Admisión: informes de admisión, primera entrevista,
//     asignaciones y solicitudes de cambio de profesional.
//   - Profesional auditor: además, lectura de todas las historias clínicas, reportes de la
//     organización y auditoría clínica. Lectura: no escribe en historias ajenas.
//   - CRM: chats, casos, derivaciones de casos y etiquetas. Nunca la historia clínica.
//   - CRM con privilegio Gestión de agendas: turnos y agendas de todos los profesionales.
//   - CRM con privilegio Admisión (migración 36, la admisionista): revisa los formularios «Sobre mí», propone y asigna.
//     Sin el privilegio, la recepcionista no ve los formularios (tienen datos sensibles: motivo de consulta, medicación).
//   - Profesional con privilegio Entrevista por video (migración 36): ofrece la entrevista de admisión por video, que es opcional.
//   - CRM con privilegio Gestión de pacientes (migración 37): pacientes y turnos de todos (sin la historia clínica); marca Ausente o cancela.
//
// Uso:  app.MapGet(...).Solo(Quien.Configurador | Quien.Auditor);
// El control vive acá y no en la pantalla: ocultar un menú no protege nada.
[Flags]
public enum Quien
{
    Paciente      = 1,
    Profesional   = 2,    // incluye al auditor (su token lleva los dos roles)
    Auditor       = 4,
    Admision      = 8,    // profesional o auditor con el privilegio Admisión
    Configurador  = 16,
    Crm           = 32,
    Agendas       = 64,   // CRM con el privilegio Gestión de agendas
    RecepcionAdmision = 128,   // CRM con el privilegio Admisión (migración 36)
    EntrevistaVideo   = 256,   // profesional o auditor con el privilegio Entrevista por video (migración 36)
    GestionPacientes  = 512    // CRM con el privilegio Gestión de pacientes (migración 37)
}

public static class Politicas
{
    public static bool Cumple(ClaimsPrincipal u, Quien quien) =>
           (quien.HasFlag(Quien.Paciente) && u.IsInRole(Roles.Paciente))
        || (quien.HasFlag(Quien.Profesional) && u.IsInRole(Roles.Profesional))
        || (quien.HasFlag(Quien.Auditor) && u.IsInRole(Roles.Auditor))
        || (quien.HasFlag(Quien.Admision) && u.IsInRole(Roles.Profesional) && TienePrivilegio(u, Privilegios.Admision))
        || (quien.HasFlag(Quien.Configurador) && u.IsInRole(Roles.Configurador))
        || (quien.HasFlag(Quien.Crm) && u.IsInRole(Roles.Crm))
        || (quien.HasFlag(Quien.Agendas) && u.IsInRole(Roles.Crm) && TienePrivilegio(u, Privilegios.GestionAgendas))
        || (quien.HasFlag(Quien.RecepcionAdmision) && u.IsInRole(Roles.Crm) && TienePrivilegio(u, Privilegios.Admision))
        || (quien.HasFlag(Quien.EntrevistaVideo) && u.IsInRole(Roles.Profesional) && TienePrivilegio(u, Privilegios.EntrevistaVideo))
        || (quien.HasFlag(Quien.GestionPacientes) && u.IsInRole(Roles.Crm) && TienePrivilegio(u, Privilegios.GestionPacientes));

    private static bool TienePrivilegio(ClaimsPrincipal u, string privilegio) =>
        u.HasClaim(JwtEmisorService.ClaimPrivilegio, privilegio);

    /// <summary>Exige sesión y que el token cumpla alguna de las condiciones (OR).</summary>
    public static TBuilder Solo<TBuilder>(this TBuilder builder, Quien quien) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.RequireAuthenticatedUser().RequireAssertion(c => Cumple(c.User, quien)));
}
