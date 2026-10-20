// Mismo patrón que AutorizacionReporte (CB-04) para los endpoints de Grupos, Casos y
// Derivaciones de la Fase 2. Reescrito en la Fase JWT para usar IdentidadActualService en
// vez de leer X-Usuario-Id directo — cierra dos huecos que tenía la versión anterior:
// (1) sin ninguna identidad resuelta, antes devolvía (true, null) — "administrador" por
// default — ahora devuelve (false, null), nadie pasa los "if (!esAdmin)" que ya tenían
// todos los endpoints de CasosEndpoints.cs, sin tocarlos uno por uno; (2) un paciente
// (que no está en UsuarioProfesional) también caía en "administrador" por el mismo
// motivo — ahora el rol se lee del token/claims, así que un paciente nunca es admin ni
// profesional acá.
//
// Roles y permisos: "administrador" de casos, derivaciones de casos y chats pasa a
// ser el módulo CRM (antes era el Configurador, que ya no opera nada de esto). La
// configuración del ruteo (grupos y reglas de derivación) sigue siendo del Configurador:
// para eso está EsConfigurador().
public static class AutorizacionCasos
{
    public static (bool EsAdministrador, int? IdProfesional) Resolver(HttpContext ctx)
    {
        var identidad = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
        return identidad switch
        {
            { EsCrm: true } => (true, null),
            { EsProfesional: true } => (false, identidad.IdProfesional),
            _ => (false, null)   // paciente, configurador o sin identidad: ni admin ni profesional
        };
    }

    /// <summary>Grupos y reglas de derivación: configuración de la organización.</summary>
    public static bool EsConfigurador(HttpContext ctx) =>
        ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.EsConfigurador == true;
}
