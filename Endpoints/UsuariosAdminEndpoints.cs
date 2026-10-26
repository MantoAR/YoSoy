using Microsoft.AspNetCore.Mvc;

// Pantalla Usuarios del Configurador (Fase 2, PR 2). Se registra con app.MapUsuariosAdmin(). Todo es SOLO del Configurador
// (CRM, Profesional y Paciente reciben 403; sin sesión, 401), no lleva datos clínicos y cada cambio queda en la auditoría
// administrativa. El cambio obligatorio de contraseña (POST /sesion/cambiar-password) está en Program.cs, junto al ingreso.
//
//   GET    /usuarios/lista?q=&tipo=&estado=&pagina=&cantidad=   La lista única (pacientes, profesionales y equipo), con búsqueda en el servidor.
//   POST   /usuarios/{id}/restablecer-password                  Clave temporal, una sola vez, en la respuesta (Cache-Control: no-store).
//   GET    /usuarios/pacientes/{id}/profesionales               Quién atiende al paciente (nombre, matrícula) y a quién se puede asignar.
//   POST   /usuarios/pacientes/{id}/profesionales               Asigna uno o varios: { idProfesionales: [..] }.
//   DELETE /usuarios/pacientes/{id}/profesionales               Quita uno o varios: { idProfesionales: [..] }.
//
// {id} de los pacientes es el IdPaciente (los pacientes sin cuenta también se pueden asignar). Contrato y reglas: docs/DEPLOY_USUARIOS_PRODUCCION.md.
public static class UsuariosAdminEndpoints
{
    public record AsignacionRequest(List<int>? IdProfesionales);

    public static IEndpointRouteBuilder MapUsuariosAdmin(this IEndpointRouteBuilder app)
    {
        app.MapGet("/usuarios/lista", (string? q, string? tipo, string? estado, int? pagina, int? cantidad,
            HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var (filtro, error) = UsuariosLista.Normalizar(q, tipo, estado, pagina, cantidad);
            if (filtro is null) return Results.Json(new { mensaje = error }, statusCode: 400);
            var dao = new UsuariosListaDAO();
            var ahora = reloj.GetUtcNow().UtcDateTime;
            var r = dao.Buscar(filtro, ahora);
            // Una página que ya no existe (por ejemplo, después de dar de baja a alguien) vuelve a la primera.
            if (r.Filas.Count == 0 && filtro.Pagina > 1)
            {
                filtro = filtro with { Pagina = 1 };
                r = dao.Buscar(filtro, ahora);
            }
            var yo = identidad.Resolver(ctx)!.IdUsuario;
            return Results.Ok(new
            {
                total = r.Total,
                pagina = filtro.Pagina,
                cantidad = filtro.Cantidad,
                paginas = (int)Math.Ceiling(r.Total / (double)filtro.Cantidad),
                filas = r.Filas.Select(f => new
                {
                    f.IdUsuario, f.Tipo, f.Username, f.Nombre, f.Dni, f.Matricula, f.Especialidad, f.Mail, f.Estado,
                    f.Bloqueado, f.BloqueadoHasta, f.DebeCambiarPassword, f.IdPaciente, f.IdProfesional, f.LinkPublicoActivo,
                    tieneCuenta = f.IdUsuario is not null,
                    esUnoMismo = f.IdUsuario == yo
                })
            });
        }).Solo(Quien.Configurador);

        app.MapPost("/usuarios/{id:int}/restablecer-password", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            // Lleva una clave: ni el navegador ni un intermediario la guardan, ni siquiera en un error.
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers.Pragma = "no-cache";
            var yo = identidad.Resolver(ctx)!;
            if (id == yo.IdUsuario)
                return Results.Json(new { codigo = "RESTABLECER_PROPIA", mensaje = "No podés restablecer tu propia contraseña desde acá. Pedile a otra persona del equipo con el rol Configurador." }, statusCode: 403);

            var clave = RestablecerPassword.NuevaTemporal();
            var (resultado, username) = new UsuariosAdminDAO().Restablecer(id, yo.IdUsuario, RestablecerPassword.Hash(clave), yo.Modulo ?? yo.Rol);
            return resultado switch
            {
                ResultadoRestablecer.NoExiste => Results.Json(new { codigo = "USUARIO_INEXISTENTE", mensaje = "No existe ese usuario." }, statusCode: 404),
                ResultadoRestablecer.NoActiva => Results.Json(new { codigo = "CUENTA_NO_ACTIVA", mensaje = "La cuenta no está activa (dada de baja o sin confirmar). Si está sin confirmar, reenviale la invitación." }, statusCode: 409),
                _ => Results.Ok(new
                {
                    idUsuario = id,
                    username,
                    contrasenaTemporal = clave,
                    mensaje = "Se ve una sola vez. La persona tiene que cambiarla al ingresar."
                })
            };
        }).Solo(Quien.Configurador).RequireRateLimiting(UsuariosAdminServiceCollectionExtensions.PoliticaRestablecer);

        app.MapGet("/usuarios/pacientes/{id:int}/profesionales", (int id) =>
        {
            var dao = new AsignacionAdminDAO();
            var paciente = dao.LeerPaciente(id);
            if (paciente is null) return Results.Json(new { codigo = "PACIENTE_INEXISTENTE", mensaje = "No existe ese paciente." }, statusCode: 404);
            return Results.Ok(new
            {
                idPaciente = id,
                nombre = paciente.Value.Nombre,
                estado = paciente.Value.Estado,
                asignados = dao.Asignados(id),
                disponibles = dao.Activos()
            });
        }).Solo(Quien.Configurador);

        app.MapPost("/usuarios/pacientes/{id:int}/profesionales", ([FromBody] AsignacionRequest? pedido, int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var (ids, error) = AsignacionAdmin.Validar(pedido?.IdProfesionales);
            if (ids is null) return Results.Json(new { codigo = "PEDIDO_INVALIDO", mensaje = error }, statusCode: 400);
            var yo = identidad.Resolver(ctx)!;
            // Quien asigna no puede asignarse a sí mismo (si su usuario tiene ficha de profesional): no se da acceso a pacientes a sí mismo.
            var propio = new UsuarioProfesionalDAO().ObtenerPorUsuario(yo.IdUsuario)?.IdProfesional;
            if (propio is int p && ids.Contains(p))
                return Results.Json(new { codigo = "AUTO_ASIGNACION", mensaje = "No podés asignarte a vos mismo como profesional de un paciente." }, statusCode: 403);
            // Sin el consentimiento de tratamiento y telemedicina firmado y vigente no se asigna profesional.
            // (A un paciente inactivo no se le asigna por otra razón, que responde el DAO: acá el rechazo es el de «inactivo».)
            if (new PacienteDAO().ObtenerPorId(id) is { Estado: "Activo" } && ConsentimientoTratamiento.BloquearSiNoFirmo(ctx, id) is { } sinConsentimiento) return sinConsentimiento;
            return Responder(new AsignacionAdminDAO().Asignar(id, ids, yo.IdUsuario, yo.Modulo ?? yo.Rol), "asignados");
        }).Solo(Quien.Configurador).RequireRateLimiting(UsuariosAdminServiceCollectionExtensions.PoliticaAsignar);

        app.MapDelete("/usuarios/pacientes/{id:int}/profesionales", ([FromBody] AsignacionRequest? pedido, int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var (ids, error) = AsignacionAdmin.Validar(pedido?.IdProfesionales);
            if (ids is null) return Results.Json(new { codigo = "PEDIDO_INVALIDO", mensaje = error }, statusCode: 400);
            var yo = identidad.Resolver(ctx)!;
            return Responder(new AsignacionAdminDAO().Quitar(id, ids, yo.IdUsuario, yo.Modulo ?? yo.Rol), "quitados");
        }).Solo(Quien.Configurador).RequireRateLimiting(UsuariosAdminServiceCollectionExtensions.PoliticaAsignar);

        return app;
    }

    private static IResult Responder(ResultadoAsignacionAdmin r, string campo) => r.Estado == 200
        ? Results.Ok(new Dictionary<string, object> { [campo] = r.Cambiados, ["sinCambio"] = r.SinCambio })
        : Results.Json(new { codigo = r.Codigo, mensaje = r.Mensaje }, statusCode: r.Estado);
}
