// PUNTO DE CONTROL DE ACCESO — CB-04
// ADVERTENCIA: hoy la identidad es AUTODECLARADA. X-Usuario-Id no está firmado ni
// verificado, y puede falsificarse desde la consola del navegador o con curl.
// Esto NO constituye autenticación y NO satisface la Ley 25.326 ni el secreto
// profesional. Cierra el acceso accidental y deja rastro de auditoría.
// TODO(JWT): al mergear videollamadas-seguras, reemplazar la lectura de X-Usuario-Id
// por el claim del token firmado (JwtService.Validar). Es el ÚNICO cambio necesario
// en este archivo: el resto de la lógica de autorización ya es correcta.
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
        // ── ÚNICA LÍNEA A REEMPLAZAR CUANDO ENTRE EL JWT ────────────────────
        // Pasaría a ser el claim "idUsuario" del token firmado, en vez de un
        // header que el navegador escribe solo.
        var idUsuario = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var x) ? x : 0;
        // ───────────────────────────────────────────────────────────────────

        var vinculo = new UsuarioProfesionalDAO().ObtenerPorUsuario(idUsuario);

        if (vinculo != null)
        {
            // El usuario está vinculado a un profesional: su alcance es él y nadie más.
            if (idProfesionalSolicitado.HasValue &&
                idProfesionalSolicitado.Value != vinculo.IdProfesional)
            {
                Auditar(idUsuario, "Reporte HTML denegado", vinculo.IdProfesional,
                    $"Pidió idProfesional={idProfesionalSolicitado.Value} siendo idProfesional={vinculo.IdProfesional}");
                return (false, null, "No podés acceder al reporte de otro profesional.");
            }

            Auditar(idUsuario, "Descarga Reporte HTML", vinculo.IdProfesional,
                $"Alcance propio (idProfesional={vinculo.IdProfesional})");

            // El id efectivo sale del vínculo en base, no del query string.
            return (true, vinculo.IdProfesional, "");
        }

        // Sin vínculo a ningún profesional: se lo trata como configurador.
        Auditar(idUsuario, "Descarga Reporte HTML", idProfesionalSolicitado,
            idProfesionalSolicitado.HasValue
                ? $"Alcance configurador, filtrado por idProfesional={idProfesionalSolicitado.Value}"
                : "Alcance configurador, todos los profesionales");

        return (true, idProfesionalSolicitado, "");
    }

    // Mismo patrón de auditoría que ya usan los endpoints de alta y baja.
    private static void Auditar(int idUsuario, string accion, int? idEntidad, string detalle) =>
        new LogAuditoriaDAO().Insertar(new LogAuditoria
        {
            IdUsuario = idUsuario,
            Accion    = accion,
            Entidad   = "Reporte",
            IdEntidad = idEntidad,
            Detalle   = detalle
        });
}
