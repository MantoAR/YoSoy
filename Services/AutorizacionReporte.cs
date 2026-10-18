// PUNTO DE CONTROL DE ACCESO — CB-04
// Cierre de seguridad, Paso 4: antes leía X-Usuario-Id directo del header, sin firmar ni
// verificar, completamente al margen del pipeline de autenticación — ni este archivo ni
// los dos endpoints que lo llaman (/reportes/atenciones[.html]) tenían
// .RequireAuthorization(), así que cualquiera, sin ninguna sesión, podía mandar cualquier
// X-Usuario-Id (o ninguno: caía en idUsuario=0, "sin vínculo", tratado como Configurador
// con acceso total). Ahora lee IdentidadActualService, igual que el resto de la app desde
// la Fase JWT, y ambos endpoints exigen autenticación. El resto de la lógica de
// autorización (alcance propio para un profesional, total para el Configurador) no cambia.
public static class AutorizacionReporte
{
    /// <summary>
    /// Resuelve qué alcance de datos puede ver quien pide el reporte.
    /// </summary>
    /// <param name="ctx">Contexto de la request, de donde sale la identidad.</param>
    /// <param name="idProfesionalSolicitado">Filtro que llegó por query string. Para un
    /// profesional es solo una intención a validar: el id efectivo NUNCA sale de acá.</param>
    /// <returns>
    /// autorizado = false ⇒ el llamador debe responder 403 con motivoRechazo.
    /// idProfesionalPermitido = null ⇒ alcance total (todos los profesionales).
    /// </returns>
    public static (bool autorizado, int? idProfesionalPermitido, string motivoRechazo)
        ResolverAlcance(HttpContext ctx, int? idProfesionalSolicitado)
    {
        var identidad = ctx.RequestServices.GetRequiredService<IdentidadActualService>().Resolver(ctx);
        if (identidad is null) return (false, null, "No se pudo identificar al usuario.");
        var idUsuario = identidad.IdUsuario;

        if (identidad.EsPaciente)
        {
            Auditar(idUsuario, "Reporte HTML denegado", null, "Un paciente no tiene acceso a este reporte.");
            return (false, null, "No tenés acceso a este reporte.");
        }

        // Etapa 3: el alcance total es del Profesional auditor (antes del Configurador).
        if (identidad.EsAuditor)
        {
            Auditar(idUsuario, "Descarga Reporte HTML", idProfesionalSolicitado,
                idProfesionalSolicitado.HasValue
                    ? $"Alcance auditor, filtrado por idProfesional={idProfesionalSolicitado.Value}"
                    : "Alcance auditor, todos los profesionales");
            return (true, idProfesionalSolicitado, "");
        }

        if (identidad.EsProfesional)
        {
            // El profesional del token es su propio alcance y nadie más.
            if (idProfesionalSolicitado.HasValue && idProfesionalSolicitado.Value != identidad.IdProfesional)
            {
                Auditar(idUsuario, "Reporte HTML denegado", identidad.IdProfesional,
                    $"Pidió idProfesional={idProfesionalSolicitado.Value} siendo idProfesional={identidad.IdProfesional}");
                return (false, null, "No podés acceder al reporte de otro profesional.");
            }

            Auditar(idUsuario, "Descarga Reporte HTML", identidad.IdProfesional,
                $"Alcance propio (idProfesional={identidad.IdProfesional})");

            // El id efectivo sale del claim del token, no del query string.
            return (true, identidad.IdProfesional, "");
        }

        // Configurador, CRM y cualquier otro módulo: sin acceso (datos de pacientes).
        Auditar(idUsuario, "Reporte HTML denegado", idProfesionalSolicitado, $"Módulo {identidad.Modulo ?? identidad.Rol} sin acceso a reportes.");
        return (false, null, "Los reportes de atención son del Profesional auditor.");
    }

    // Mismo patrón de auditoría que ya usan los endpoints de alta y baja.
    private static void Auditar(int idUsuario, string accion, int? idEntidad, string detalle) =>
        new LogAuditoriaDAO().Insertar(new LogAuditoria
        {
            IdUsuario = idUsuario,
            Accion    = accion,
            Entidad   = "Reporte",
            IdEntidad = idEntidad,
            Detalle   = detalle,
            // Etapa 3: el reporte lleva nombre, DNI y NHC de pacientes → auditoría clínica.
            Categoria = CategoriaAuditoria.Clinica
        });
}
