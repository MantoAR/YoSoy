using Microsoft.Extensions.Options;

// Circuito de atención (octubre 2026): consentimientos opcionales, preferencias de sesión del
// profesional, salas de video registradas con sus interruptores de IA y transcripción, y
// evolución por enfoque con borrador, firma inmutable y addenda. Se registran con
// app.MapAtencion() en Program.cs. Requiere la migración 15
// (migrations/add_atencion_consentimientos_ia_evolucion.sql).
public static class AtencionEndpoints
{
    public static IEndpointRouteBuilder MapAtencion(this IEndpointRouteBuilder app)
    {
        // ── Consentimientos opcionales ────────────────────────────────────────────
        // Leer: el propio paciente, su profesional o el Configurador. Firmar y revocar: solo
        // el propio paciente (nadie firma ni revoca en nombre de otro).
        app.MapGet("/pacientes/{id}/consentimientos-opcionales", (int id, HttpContext ctx, IdentidadActualService identidad,
            IOptionsMonitor<AtencionOptions> opciones) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.EsPaciente && yo.IdPaciente != id)
                return Results.Json(new { mensaje = "Solo podés ver tus propios consentimientos." }, statusCode: 403);
            if (yo.EsProfesional && !AutorizacionClinica.PuedeVerPaciente(ctx, id)) return Results.NotFound();
            if (new PacienteDAO().ObtenerPorId(id) is null) return Results.NotFound();

            var dao = new ConsentimientoDAO();
            var version = opciones.CurrentValue.VersionTextoOpcionales;
            return Results.Ok(ConsentimientosOpcionales.Todos.Select(t =>
            {
                var c = dao.ObtenerVigente(id, t.Descripcion);
                return new
                {
                    clave = t.Clave, titulo = t.Titulo, resumen = t.Resumen, versionTexto = version,
                    firmado = c is not null, fechaFirma = c?.FechaFirma, versionFirmada = c?.Version,
                    revocadoEl = c is null ? dao.UltimaRevocacion(id, t.Descripcion) : null
                };
            }));
        }).RequireAuthorization();

        app.MapPost("/pacientes/{id}/consentimientos-opcionales/{clave}", (int id, string clave, FirmaConsentimientoRequest req,
            IOptionsMonitor<AtencionOptions> opciones, TimeProvider reloj, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx);
            if (yo is null || yo.IdPaciente != id)
                return Results.Json(new { mensaje = "Solo el propio paciente puede firmar sus consentimientos." }, statusCode: 403);
            var tipo = ConsentimientosOpcionales.PorClave(clave);
            if (tipo is null) return Results.NotFound();
            if (req.Acepto != true)
                return Results.BadRequest(new { mensaje = "Para firmar hay que aceptar el consentimiento de forma explícita." });
            var version = opciones.CurrentValue.VersionTextoOpcionales;
            if (req.Version != version)
                return Results.Json(new { mensaje = "El texto del consentimiento cambió. Volvé a leerlo antes de firmar." }, statusCode: 409);

            var dao = new ConsentimientoDAO();
            var resultado = dao.FirmarSiNoExiste(id, tipo.Descripcion, version, reloj.GetLocalNow().DateTime);
            if (resultado == -1)
                return Results.Problem($"No existe el tipo de consentimiento \"{tipo.Descripcion}\". " +
                                       "Correr migrations/add_atencion_consentimientos_ia_evolucion.sql.");
            var vigente = dao.ObtenerVigente(id, tipo.Descripcion);
            if (resultado == 0)
                return Results.Json(new { mensaje = "Ese consentimiento ya estaba firmado.", fechaFirma = vigente?.FechaFirma }, statusCode: 409);
            AuditoriaIds.Registrar(ctx, "Firma de consentimiento opcional: " + tipo.Clave, "Consentimiento", vigente?.IdConsentimiento ?? 0, id);
            return Results.Created($"/pacientes/{id}/consentimientos-opcionales",
                new { clave = tipo.Clave, firmado = true, fechaFirma = vigente?.FechaFirma, versionFirmada = vigente?.Version });
        }).RequireAuthorization(p => p.RequireRole(Roles.Paciente));

        app.MapPost("/pacientes/{id}/consentimientos-opcionales/{clave}/revocar", (int id, string clave, TimeProvider reloj,
            HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx);
            if (yo is null || yo.IdPaciente != id)
                return Results.Json(new { mensaje = "Solo el propio paciente puede revocar sus consentimientos." }, statusCode: 403);
            var tipo = ConsentimientosOpcionales.PorClave(clave);
            if (tipo is null) return Results.NotFound();
            if (!new ConsentimientoDAO().Revocar(id, tipo.Descripcion, reloj.GetLocalNow().DateTime))
                return Results.Json(new { mensaje = "No tenías ese consentimiento firmado." }, statusCode: 404);
            // Las salas abiertas dejan de mostrar la función como activa de inmediato:
            // FuncionesSesionService.Estado nunca devuelve activa una función sin consentimiento.
            AuditoriaIds.Registrar(ctx, "Revocación de consentimiento opcional: " + tipo.Clave, "Consentimiento", 0, id);
            return Results.Ok(new { clave = tipo.Clave, firmado = false });
        }).RequireAuthorization(p => p.RequireRole(Roles.Paciente));

        // ── Preferencias de sesión del profesional ────────────────────────────────
        app.MapGet("/profesionales/{id}/configuracion-sesion", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional != id)
                return Results.Json(new { mensaje = "Solo podés ver tu propia configuración." }, statusCode: 403);
            return Results.Ok(new ConfiguracionProfesionalDAO().Obtener(id));
        }).Solo(Quien.Profesional);

        app.MapPut("/profesionales/{id}/configuracion-sesion", (int id, ConfiguracionProfesional c, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional != id)
                return Results.Json(new { mensaje = "Solo podés cambiar tu propia configuración." }, statusCode: 403);
            if (!EnfoquesEvolucion.Existe(c.Enfoque)) return Results.BadRequest(new { mensaje = "Enfoque no válido." });
            if (!ModosEvolucion.Existe(c.ModoEvolucionPorDefecto)) return Results.BadRequest(new { mensaje = "Modo de evolución no válido." });
            c.IdProfesional = id;
            new ConfiguracionProfesionalDAO().Guardar(c);
            return Results.Ok(c);
        }).Solo(Quien.Profesional);

        // ── Plantillas de evolución (una sola fuente: el frontend las dibuja desde acá) ──
        app.MapGet("/plantillas-evolucion", (IOptionsMonitor<AtencionOptions> opciones) => Results.Ok(new
        {
            plantillas = EvolucionEnfoquesService.Plantillas,
            escalas = EvolucionEnfoquesService.Escalas.Values.Select(e => new
            {
                clave = e.Clave, nombre = e.Nombre, consigna = e.Consigna, items = e.Items, respuestas = e.Respuestas,
                bandas = e.Bandas.Select(b => new { hasta = b.Hasta, severidad = b.Severidad })
            }),
            modos = ModosEvolucion.Todos,
            tiposCierre = TiposCierre.Todos,
            motorTranscripcion = opciones.CurrentValue.MotorTranscripcion,
            conservarTranscripcion = opciones.CurrentValue.ConservarTranscripcion
        })).Solo(Quien.Profesional);

        // ── Salas de video ─────────────────────────────────────────────────────────
        // Sesión directa (sin turno): el profesional abre una sala con un paciente suyo. El
        // código lo genera el servidor y queda registrado, así se sabe de quién es la sala.
        app.MapPost("/salas", (CrearSalaRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!AutorizacionClinica.PuedePaciente(ctx, req.IdPaciente))
                return Results.Json(new { mensaje = "Ese paciente no está asociado a tu atención." }, statusCode: 403);
            var codigo = "SALA-D" + req.IdPaciente + "-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));
            AtencionRepositorioDAO.RegistrarSala(codigo, req.IdPaciente, yo.IdProfesional, "Directa", null, null, yo.IdUsuario);
            NotificarSala(req.IdPaciente, codigo);
            return Results.Created($"/salas/{codigo}", new { codigoSala = codigo });
        }).Solo(Quien.Profesional);

        // Estado de la sala: de quién es y cómo están las funciones. El paciente ve lo mismo
        // (para los indicadores) menos los datos que no necesita.
        app.MapGet("/salas/{codigo}", (string codigo, HttpContext ctx, IdentidadActualService identidad,
            FuncionesSesionService funciones, IAtencionRepositorio repo, IOptionsMonitor<AtencionOptions> opciones) =>
        {
            var yo = identidad.Resolver(ctx);
            var r = funciones.Consultar(yo, codigo);
            if (!r.Ok) return Results.Json(new { codigo = r.Codigo, mensaje = r.Mensaje }, statusCode: r.Status);
            var sala = repo.ResolverSala(codigo)!;
            var esEquipo = !yo!.EsPaciente;
            return Results.Ok(new
            {
                codigoSala = sala.CodigoSala,
                origen = sala.Origen,
                idTurno = sala.IdTurno,
                idPaciente = esEquipo ? sala.IdPaciente : (int?)null,
                nombrePaciente = esEquipo ? AtencionRepositorioDAO.NombrePaciente(sala.IdPaciente) : null,
                funciones = r.Estado!.Select(e => new { funcion = e.Funcion, consentida = e.Consentida, activa = e.Activa }),
                motorTranscripcion = opciones.CurrentValue.MotorTranscripcion
            });
        }).RequireAuthorization();

        // Prender o apagar una función. Prender: profesional de la sala o Admisión (entrevista), con
        // consentimiento vigente (RN-10/RN-15). Apagar: también el propio paciente.
        app.MapPut("/salas/{codigo}/funciones", (string codigo, CambioFuncionRequest req, HttpContext ctx,
            IdentidadActualService identidad, FuncionesSesionService funciones) =>
        {
            var r = funciones.Cambiar(identidad.Resolver(ctx), codigo, req.Funcion, req.Activa);
            if (!r.Ok) return Results.Json(new { codigo = r.Codigo, mensaje = r.Mensaje }, statusCode: r.Status);
            return Results.Ok(new { funciones = r.Estado!.Select(e => new { funcion = e.Funcion, consentida = e.Consentida, activa = e.Activa }) });
        }).RequireAuthorization();

        // ── Evolución por enfoque ──────────────────────────────────────────────────
        // Crea un borrador o la firma de una vez. Al firmar se validan los obligatorios de la
        // plantilla y el tipo de cierre (RN-08), y se calcula el hash (RN-11).
        app.MapPost("/evoluciones/por-enfoque", (EvolucionEnfoqueRequest req, HttpContext ctx, IdentidadActualService identidad,
            TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!AutorizacionClinica.PuedePaciente(ctx, req.IdPaciente))
                return Results.Json(new { mensaje = "Ese paciente no está asociado a tu atención." }, statusCode: 403);
            var armado = Armar(req, yo, reloj, evolucionExistente: null);
            if (armado.Error is not null) return armado.Error;
            var e = armado.Evolucion!;
            e.IdEvolucion = new EvolucionDAO().InsertarCompleta(e);
            AuditoriaIds.Registrar(ctx, e.Estado == "Firmada" ? "Firma de evolución" : "Borrador de evolución", "Evolucion", e.IdEvolucion, req.IdPaciente);
            return Results.Created($"/evoluciones/{e.IdEvolucion}", Respuesta(e));
        }).Solo(Quien.Profesional);

        // Reemplaza un borrador propio (y opcionalmente lo firma). Una evolución firmada no se toca.
        app.MapPut("/evoluciones/{id}/borrador", (int id, EvolucionEnfoqueRequest req, HttpContext ctx, IdentidadActualService identidad,
            TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var existente = new EvolucionDAO().ObtenerPorId(id);
            var idPaciente = existente is null ? null : new HistoriaClinicaDAO().ObtenerPorId(existente.IdHistoria)?.IdPaciente;
            if (idPaciente is null || idPaciente != req.IdPaciente || !AutorizacionClinica.PuedePaciente(ctx, idPaciente.Value))
                return Results.NotFound();
            if (existente!.Estado == "Firmada")
                return Results.Json(new { codigo = "RN-11", mensaje = "Esta evolución ya está firmada y no se puede modificar. Agregá un addendum." }, statusCode: 409);
            if (yo.EsProfesional && existente.IdProfesional != yo.IdProfesional)
                return Results.Json(new { mensaje = "Solo quien escribió el borrador puede editarlo." }, statusCode: 403);
            var armado = Armar(req, yo, reloj, existente);
            if (armado.Error is not null) return armado.Error;
            var e = armado.Evolucion!;
            e.IdEvolucion = id;
            if (!new EvolucionDAO().ActualizarBorrador(e))
                return Results.Json(new { codigo = "RN-11", mensaje = "La evolución cambió de estado mientras la editabas. Recargá la historia clínica." }, statusCode: 409);
            AuditoriaIds.Registrar(ctx, e.Estado == "Firmada" ? "Firma de evolución" : "Borrador de evolución", "Evolucion", id, idPaciente);
            return Results.Ok(Respuesta(e));
        }).Solo(Quien.Profesional);

        // Corrección de una evolución firmada: se agrega, nunca se reemplaza (RN-11).
        app.MapPost("/evoluciones/{id}/addenda", (int id, AddendumRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var ev = new EvolucionDAO().ObtenerPorId(id);
            var idPaciente = ev is null ? null : new HistoriaClinicaDAO().ObtenerPorId(ev.IdHistoria)?.IdPaciente;
            if (idPaciente is null || !AutorizacionClinica.PuedePaciente(ctx, idPaciente.Value)) return Results.NotFound();
            if (ev!.Estado != "Firmada")
                return Results.BadRequest(new { mensaje = "Un borrador se corrige editándolo; el addendum es para evoluciones firmadas." });
            var texto = req.Texto?.Trim();
            if (string.IsNullOrEmpty(texto)) return Results.BadRequest(new { mensaje = "Escribí el texto del addendum." });
            if (texto.Length > 10000) texto = texto[..10000];
            var idAdd = new EvolucionDAO().InsertarAddendum(id, yo.IdProfesional, yo.IdUsuario, texto);
            AuditoriaIds.Registrar(ctx, "Addendum de evolución", "Evolucion", id, idPaciente);
            return Results.Created($"/evoluciones/{id}/addenda", new { idAddendum = idAdd });
        }).Solo(Quien.Profesional);

        // Addenda de toda la historia (para dibujarlas debajo de cada evolución).
        app.MapGet("/historiasclinicas/{id}/addenda", (int id, HttpContext ctx) =>
        {
            var idPaciente = new HistoriaClinicaDAO().ObtenerPorId(id)?.IdPaciente;
            if (idPaciente is null || !AutorizacionClinica.PuedeVerPaciente(ctx, idPaciente.Value)) return Results.NotFound();
            return Results.Ok(new EvolucionDAO().ObtenerAddendaPorHistoria(id));
        }).Solo(Quien.Profesional);

        // Verificación de integridad: recalcula el hash de cada evolución firmada por enfoque.
        app.MapGet("/historiasclinicas/{id}/integridad", (int id, HttpContext ctx) =>
        {
            var idPaciente = new HistoriaClinicaDAO().ObtenerPorId(id)?.IdPaciente;
            if (idPaciente is null || !AutorizacionClinica.PuedeVerPaciente(ctx, idPaciente.Value)) return Results.NotFound();
            var resultado = new EvolucionDAO().ObtenerPorHistoria(id)
                .Where(e => e.Estado == "Firmada" && e.HashContenido is not null)
                .Select(e => new { idEvolucion = e.IdEvolucion, integra = EvolucionEnfoquesService.Hash(e) == e.HashContenido });
            return Results.Ok(resultado);
        }).Solo(Quien.Profesional);

        return app;
    }

    private static void NotificarSala(int idPaciente, string codigo)
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
                INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link) VALUES (@IdPac, @Titulo, @Mensaje, @Link)", con);
            cmd.Parameters.AddWithValue("@IdPac", idPaciente);
            cmd.Parameters.AddWithValue("@Titulo", "Tu profesional abrió una sala de video");
            cmd.Parameters.AddWithValue("@Mensaje", "Podés unirte ahora con el código " + codigo + ".");
            cmd.Parameters.AddWithValue("@Link", codigo);
            cmd.ExecuteNonQuery();
        }
        catch (Exception) { /* el aviso no debe impedir abrir la sala */ }
    }

    private record Armado(Evolucion? Evolucion, IResult? Error);

    // Arma la evolución a partir del pedido: plantilla, limpieza, obligatorios si se firma,
    // texto plano para las vistas viejas, historia clínica (la crea si no existe) y hash.
    private static Armado Armar(EvolucionEnfoqueRequest req, Identidad yo, TimeProvider reloj, Evolucion? evolucionExistente)
    {
        var plantilla = EvolucionEnfoquesService.PlantillaDe(req.Enfoque);
        if (plantilla is null) return new(null, Results.BadRequest(new { mensaje = "Enfoque no válido." }));
        var modo = req.ModoRegistro ?? ModosEvolucion.Manual;
        if (!ModosEvolucion.Existe(modo)) return new(null, Results.BadRequest(new { mensaje = "Modo de registro no válido." }));

        var (limpio, errores) = EvolucionEnfoquesService.Normalizar(plantilla, req.Contenido, req.Firmar);
        var tipoCierre = TiposCierre.Normalizar(req.TipoCierre);
        if (req.Firmar && tipoCierre is null) errores.Add("Elegí el tipo de cierre: Seguimiento, Derivación o Alta (RN-08).");
        if (errores.Count > 0)
            return new(null, Results.Json(new { mensaje = string.Join(" ", errores), errores }, statusCode: 400));

        // El profesional que firma es siempre el de la sesión: nunca un id del cuerpo.
        int idProfesional;
        if (yo.IdProfesional is int p) idProfesional = p;
        else if (evolucionExistente is not null) idProfesional = evolucionExistente.IdProfesional;
        else
        {
            // Sin profesional en la sesión (hasta la etapa 2, el Configurador en la entrevista de
            // admisión): a nombre del profesional de admisión. Desde la etapa 3 la entrevista la
            // hace un profesional con Admisión, que firma con su propio nombre.
            var adm = new ProfesionalDAO().ObtenerTodos().FirstOrDefault(x => x.Matricula == "ADM-001");
            if (adm is null) return new(null, Results.BadRequest(new { mensaje = "No hay un profesional de admisión (matrícula ADM-001) para firmar la evolución." }));
            idProfesional = adm.IdProfesional;
        }

        var historia = new HistoriaClinicaDAO().ObtenerPorPaciente(req.IdPaciente);
        if (historia is null)
        {
            new HistoriaClinicaDAO().Insertar(new HistoriaClinica { IdPaciente = req.IdPaciente, FechaApertura = DateTime.Now });
            historia = new HistoriaClinicaDAO().ObtenerPorPaciente(req.IdPaciente);
            if (historia is null) return new(null, Results.Problem("No se pudo abrir la historia clínica del paciente."));
        }

        var ahora = reloj.GetLocalNow().DateTime;
        ahora = new DateTime(ahora.Year, ahora.Month, ahora.Day, ahora.Hour, ahora.Minute, ahora.Second);
        var e = new Evolucion
        {
            IdHistoria = historia.IdHistoria,
            IdProfesional = idProfesional,
            IdTurno = req.IdTurno,
            Fecha = evolucionExistente?.Fecha ?? ahora,
            Diagnostico = Recortar(req.Diagnostico, 500)!,
            PlanTratamiento = Recortar(req.PlanTratamiento, 4000)!,
            Observaciones = EvolucionEnfoquesService.TextoPlano(plantilla, limpio),
            TipoCierre = tipoCierre,
            Enfoque = plantilla.Enfoque,
            ModoRegistro = modo,
            ContenidoJson = EvolucionEnfoquesService.ComoJson(limpio),
            CodigoSala = Recortar(req.CodigoSala, 60),
            Estado = req.Firmar ? "Firmada" : "Borrador",
            FechaFirma = req.Firmar ? ahora : null
        };
        if (req.Firmar) e.HashContenido = EvolucionEnfoquesService.Hash(e);
        return new(e, null);
    }

    private static string? Recortar(string? s, int max)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length > max ? s[..max] : s;
    }

    private static object Respuesta(Evolucion e) => new
    {
        idEvolucion = e.IdEvolucion, estado = e.Estado, fechaFirma = e.FechaFirma, hashContenido = e.HashContenido,
        alertas = EvolucionEnfoquesService.Resultados(
                System.Text.Json.JsonSerializer.Deserialize<ContenidoEvolucion>(e.ContenidoJson ?? "{}",
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(null, null, null))
            .Where(r => r.AlertaRiesgo).Select(r => r.Escala)
    };
}

public record CrearSalaRequest(int IdPaciente);
public record CambioFuncionRequest(string? Funcion, bool Activa);
public record AddendumRequest(string? Texto);
public record EvolucionEnfoqueRequest(
    int IdPaciente, int? IdTurno, string? CodigoSala, string? Enfoque, string? ModoRegistro,
    ContenidoEvolucion? Contenido, string? Diagnostico, string? PlanTratamiento, string? TipoCierre, bool Firmar);
