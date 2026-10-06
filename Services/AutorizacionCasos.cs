// Mismo patrón que AutorizacionReporte (CB-04) para los endpoints nuevos de Grupos,
// Casos y Derivaciones de la Fase 2: identidad autodeclarada por X-Usuario-Id (sin
// firmar), resuelta contra UsuarioProfesional para saber si quien llama es un
// profesional (alcance: solo sus casos) o un administrador/configurador (todo).
// TODO(JWT): al mergear videollamadas-seguras, cambiar la lectura de X-Usuario-Id por
// el claim del token firmado (JwtService.Validar) — el único cambio necesario acá.
public static class AutorizacionCasos
{
    public static (bool EsAdministrador, int? IdProfesional) Resolver(HttpContext ctx)
    {
        var idUsuario = int.TryParse(ctx.Request.Headers["X-Usuario-Id"], out var x) ? x : 0;
        var vinculo = new UsuarioProfesionalDAO().ObtenerPorUsuario(idUsuario);
        return vinculo != null ? (false, vinculo.IdProfesional) : (true, null);
    }
}
