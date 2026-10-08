// Cierre de seguridad, Paso 6: mecanismo de auditoría "solo ids, nunca contenido" que ya
// usaba la apertura de conversaciones de chat (Mejora de chats y centrado) — se extrae
// acá, compartido, para reusarlo también en el acceso a datos clínicos en vez de duplicar
// el mismo patrón. Nunca interrumpe la respuesta si falla (ver catch): la auditoría no
// debe poder tumbar un pedido real.
public static class AuditoriaIds
{
    public static void Registrar(HttpContext ctx, string accion, string entidad, int idEntidad, int? idPaciente = null)
    {
        var idUsuario = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx)?.IdUsuario;
        if (idUsuario is null) return;
        try
        {
            var detalle = $"{entidad} #{idEntidad}" + (idPaciente is int idPac ? $", paciente #{idPac}" : "");
            new LogAuditoriaDAO().Insertar(new LogAuditoria
            {
                IdUsuario = idUsuario.Value, Accion = accion, Entidad = entidad, IdEntidad = idEntidad, Detalle = detalle
            });
        }
        catch (Exception) { /* la auditoría no debe impedir responder */ }
    }
}
