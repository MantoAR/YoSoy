using System.Security.Claims;

// Etapa 3 de roles y permisos: quién puede llamar a cada endpoint, según el módulo, el
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
    Agendas       = 64    // CRM con el privilegio Gestión de agendas
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
        || (quien.HasFlag(Quien.Agendas) && u.IsInRole(Roles.Crm) && TienePrivilegio(u, Privilegios.GestionAgendas));

    private static bool TienePrivilegio(ClaimsPrincipal u, string privilegio) =>
        u.HasClaim(JwtEmisorService.ClaimPrivilegio, privilegio);

    /// <summary>Exige sesión y que el token cumpla alguna de las condiciones (OR).</summary>
    public static TBuilder Solo<TBuilder>(this TBuilder builder, Quien quien) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.RequireAuthenticatedUser().RequireAssertion(c => Cumple(c.User, quien)));
}
