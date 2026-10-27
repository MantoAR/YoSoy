using System.Globalization;
using System.Text.Json;

// Documentos clínicos a partir de plantillas (migración 35). Se registra con app.MapDocumentosClinicos().
//
// Configurador (también en la sesión Administración). Nada de pacientes: la vista previa usa datos de ejemplo.
//   GET    /plantillas-documento                          Lista: estado, versión publicada y si hay borrador.
//   GET    /plantillas-documento/{id}/versiones           Historial de versiones.
//   POST   /plantillas-documento/{id}/borrador            Crea el borrador como copia de la versión publicada (si no hay uno).
//   PUT    /plantillas-documento/{id}/borrador            { campos, cuerpo, provisoria } guarda el borrador.
//   DELETE /plantillas-documento/{id}/borrador            Descarta el borrador.
//   POST   /plantillas-documento/{id}/vista-previa        { campos?, cuerpo? } con datos de ejemplo (sin cuerpo: el borrador o la publicada).
//   POST   /plantillas-documento/{id}/publicar            { motivo } publica el borrador; la anterior pasa a Retirada (RN-D3).
//   POST   /plantillas-documento/{id}/restaurar/{idVersion} { motivo } publica una copia de esa versión como versión nueva.
//   PUT    /plantillas-documento/{id}/activa              { activa }
//
// Profesional (tratante; Admisión con pacientes en admisión; el auditor lee y descarga los de todos, no emite para ajenos):
//   GET    /documentos/plantillas                         Publicadas y activas, con sus campos.
//   GET    /pacientes/{id}/documentos/precarga?clave=&idTurno=
//   POST   /pacientes/{id}/documentos/vista-previa        { clave, datos, idTurno? }
//   POST   /pacientes/{id}/documentos                     { clave, datos, idTurno?, idEventoCrisis? } emite.
//   GET    /pacientes/{id}/documentos
//   GET    /documentos/{id}
//   GET    /documentos/{id}/pdf                           El PDF guardado, como adjunto (Cache-Control: no-store).
//   GET    /documentos/{id}/integridad
//   POST   /documentos/{id}/addenda                       { texto }
//   GET/PUT/DELETE /profesionales/yo/firma                La firma del profesional para el PDF (PNG o JPG).
//
// Auditoría: emisión, lectura y descarga van a la CLÍNICA con la calidad de quien accede (RN-D4, AuditoriaIds); los cambios
// de plantillas, a la ADMINISTRATIVA (RN-D5). Nunca se registra el contenido, solo ids.
public static class DocumentosEndpoints
{
    public record BorradorRequest(JsonElement? Campos, JsonElement? Cuerpo, bool? Provisoria);
    public record MotivoRequest(string? Motivo);
    public record ActivaRequest(bool Activa);
    public record DocumentoRequest(string? Clave, Dictionary<string, string?>? Datos, int? IdTurno, int? IdEventoCrisis);
    public record AddendumRequest(string? Texto);
    public record FirmaRequest(string? ImagenBase64);

    private const int MaxFirmaBytes = 300 * 1024;

    private static IResult Error(int status, string mensaje, object? extra = null) =>
        Results.Json(new { mensaje, detalle = extra }, statusCode: status);

    private static string? JsonDe(JsonElement? e) => e is { ValueKind: JsonValueKind.Array } j ? j.GetRawText() : null;

    // Normaliza el JSON (mismo formato siempre) para que el hash no dependa de espacios ni del orden en que llegó.
    private static (string Campos, string Cuerpo)? Normalizar(string camposJson, string cuerpoJson)
    {
        try
        {
            return (RenderizadorDocumento.Serializar(RenderizadorDocumento.LeerCampos(camposJson)),
                    RenderizadorDocumento.Serializar(RenderizadorDocumento.LeerCuerpo(cuerpoJson)));
        }
        catch (JsonException) { return null; }
    }

    private static void AuditarPlantilla(Identidad yo, string accion, int idPlantilla, string detalle) =>
        AuditoriaAgenda.Registrar(yo.IdUsuario, accion, "PlantillaDocumento", idPlantilla, detalle, yo.Modulo ?? yo.Rol);

    public static string HashContenido(int idPaciente, int idProfesional, int idVersion, DateTime fecha, string datosJson, string contenidoJson) =>
        RenderizadorDocumento.Sha256(string.Join("\n", idPaciente, idProfesional, idVersion,
            fecha.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), datosJson, contenidoJson));

    public static string HashAddendum(int idDocumento, string hashDocumento, int idProfesional, DateTime fecha, string texto) =>
        RenderizadorDocumento.Sha256(string.Join("\n", idDocumento, hashDocumento, idProfesional,
            fecha.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), texto));

    public static IEndpointRouteBuilder MapDocumentosClinicos(this IEndpointRouteBuilder app)
    {
        // ═════════════════════════════ Configurador: plantillas ═════════════════════════════
        app.MapGet("/plantillas-documento", () => Results.Ok(new DocumentosClinicosDAO().Plantillas()))
            .Solo(Quien.Configurador);

        app.MapGet("/plantillas-documento/{id:int}/versiones", (int id) =>
        {
            var dao = new DocumentosClinicosDAO();
            if (dao.Plantilla(id) is null) return Results.NotFound();
            return Results.Ok(dao.Versiones(id).Select(v => new
            {
                v.IdVersion, v.Numero, v.Estado, v.Provisoria, v.MotivoCambio, v.Autor, v.FechaCreacion, v.FechaPublicacion, v.HashSha256,
                campos = JsonSerializer.Deserialize<JsonElement>(v.CamposJson), cuerpo = JsonSerializer.Deserialize<JsonElement>(v.CuerpoJson)
            }));
        }).Solo(Quien.Configurador);

        app.MapPost("/plantillas-documento/{id:int}/borrador", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new DocumentosClinicosDAO();
            if (dao.Plantilla(id) is null) return Results.NotFound();
            if (dao.VersionEnEstado(id, "Borrador") is VersionPlantilla b) return Results.Ok(new { idVersion = b.IdVersion, existente = true });
            var pub = dao.VersionEnEstado(id, "Publicada");
            var idB = dao.GuardarBorrador(id, pub?.CamposJson ?? "[]", pub?.CuerpoJson ?? "[]", pub?.Provisoria ?? true, yo.IdUsuario);
            AuditarPlantilla(yo, "Borrador de plantilla creado", id, $"Plantilla #{id} · desde la versión {pub?.Numero.ToString() ?? "-"}");
            return Results.Created($"/plantillas-documento/{id}/borrador", new { idVersion = idB, existente = false });
        }).Solo(Quien.Configurador);

        app.MapPut("/plantillas-documento/{id:int}/borrador", (int id, BorradorRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new DocumentosClinicosDAO();
            if (dao.Plantilla(id) is null) return Results.NotFound();
            var campos = JsonDe(req.Campos); var cuerpo = JsonDe(req.Cuerpo);
            if (campos is null || cuerpo is null) return Error(400, "Faltan los campos o el cuerpo de la plantilla.");
            if (campos.Length + cuerpo.Length > 200_000) return Error(400, "La plantilla es demasiado grande.");
            var normalizada = Normalizar(campos, cuerpo);
            if (normalizada is null) return Error(400, "La plantilla no tiene un formato válido.");
            var (c, b) = normalizada.Value;
            var idB = dao.GuardarBorrador(id, c, b, req.Provisoria ?? true, yo.IdUsuario);
            AuditarPlantilla(yo, "Borrador de plantilla guardado", id, $"Plantilla #{id}");
            // Se devuelve lo que falta corregir para poder publicar, sin impedir guardar.
            return Results.Ok(new { idVersion = idB, errores = RenderizadorDocumento.Validar(c, b) });
        }).Solo(Quien.Configurador);

        app.MapDelete("/plantillas-documento/{id:int}/borrador", (int id, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!new DocumentosClinicosDAO().DescartarBorrador(id)) return Results.NotFound();
            AuditarPlantilla(yo, "Borrador de plantilla descartado", id, $"Plantilla #{id}");
            return Results.NoContent();
        }).Solo(Quien.Configurador);

        app.MapPost("/plantillas-documento/{id:int}/vista-previa", (int id, BorradorRequest? req, TimeProvider reloj) =>
        {
            var dao = new DocumentosClinicosDAO();
            var p = dao.Plantilla(id);
            if (p is null) return Results.NotFound();
            var v = dao.VersionEnEstado(id, "Borrador") ?? dao.VersionEnEstado(id, "Publicada");
            var campos = JsonDe(req?.Campos) ?? v?.CamposJson ?? "[]";
            var cuerpo = JsonDe(req?.Cuerpo) ?? v?.CuerpoJson ?? "[]";
            try
            {
                var lc = RenderizadorDocumento.LeerCampos(campos);
                var (datos, sistema) = RenderizadorDocumento.Ejemplo(lc, reloj.GetLocalNow().DateTime.Date);
                var doc = RenderizadorDocumento.Renderizar(p.Titulo, req?.Provisoria ?? v?.Provisoria ?? true, lc,
                    RenderizadorDocumento.LeerCuerpo(cuerpo), datos, sistema);
                return Results.Ok(new { documento = doc, errores = RenderizadorDocumento.Validar(campos, cuerpo) });
            }
            catch (JsonException) { return Error(400, "La plantilla no tiene un formato válido."); }
        }).Solo(Quien.Configurador);

        app.MapPost("/plantillas-documento/{id:int}/publicar", (int id, MotivoRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var motivo = req.Motivo?.Trim();
            if (string.IsNullOrEmpty(motivo) || motivo.Length > 300) return Error(400, "Escribí el motivo del cambio (hasta 300 caracteres).");
            var dao = new DocumentosClinicosDAO();
            if (dao.Plantilla(id) is null) return Results.NotFound();
            var borrador = dao.VersionEnEstado(id, "Borrador");
            if (borrador is null) return Error(409, "No hay un borrador para publicar.");
            var errores = RenderizadorDocumento.Validar(borrador.CamposJson, borrador.CuerpoJson);
            if (errores.Count > 0) return Error(400, "La plantilla tiene errores: corregilos antes de publicar.", errores);
            var hash = RenderizadorDocumento.HashVersion(borrador.CamposJson, borrador.CuerpoJson);
            var idV = dao.Publicar(id, borrador.IdVersion, null, motivo, hash, yo.IdUsuario);
            AuditarPlantilla(yo, "Plantilla publicada", id, $"Plantilla #{id} · versión {borrador.Numero}" + (borrador.Provisoria ? " (provisoria)" : ""));
            return Results.Ok(new { idVersion = idV, numero = borrador.Numero });
        }).Solo(Quien.Configurador);

        app.MapPost("/plantillas-documento/{id:int}/restaurar/{idVersion:int}", (int id, int idVersion, MotivoRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var motivo = req.Motivo?.Trim();
            if (string.IsNullOrEmpty(motivo) || motivo.Length > 300) return Error(400, "Escribí el motivo (hasta 300 caracteres).");
            var dao = new DocumentosClinicosDAO();
            var v = dao.Version(idVersion);
            if (v is null || v.IdPlantilla != id) return Results.NotFound();
            if (v.Estado == "Borrador") return Error(409, "Un borrador se publica, no se restaura.");
            var errores = RenderizadorDocumento.Validar(v.CamposJson, v.CuerpoJson);
            if (errores.Count > 0) return Error(400, "Esa versión tiene errores y no se puede publicar.", errores);
            var idV = dao.Publicar(id, null, v, motivo, RenderizadorDocumento.HashVersion(v.CamposJson, v.CuerpoJson), yo.IdUsuario);
            AuditarPlantilla(yo, "Plantilla restaurada", id, $"Plantilla #{id} · se publicó una copia de la versión {v.Numero}");
            return Results.Ok(new { idVersion = idV });
        }).Solo(Quien.Configurador);

        app.MapPut("/plantillas-documento/{id:int}/activa", (int id, ActivaRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!new DocumentosClinicosDAO().CambiarActiva(id, req.Activa)) return Results.NotFound();
            AuditarPlantilla(yo, req.Activa ? "Plantilla activada" : "Plantilla desactivada", id, $"Plantilla #{id}");
            return Results.Ok(new { idPlantilla = id, activa = req.Activa });
        }).Solo(Quien.Configurador);

        // ═════════════════════════════ Profesional: documentos ═════════════════════════════
        app.MapGet("/documentos/plantillas", () =>
            Results.Ok(new DocumentosClinicosDAO().Vigentes().Select(p => new
            {
                p.IdPlantilla, p.Clave, p.Grupo, p.Titulo, p.Descripcion, p.IdVersion, p.Numero, p.Provisoria,
                campos = RenderizadorDocumento.LeerCampos(p.CamposJson)
            })))
            .Solo(Quien.Profesional);

        app.MapGet("/pacientes/{id:int}/documentos/precarga", (int id, string? clave, int? idTurno, HttpContext ctx,
            IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!AutorizacionClinica.PuedePaciente(ctx, id) || yo.IdProfesional is not int idProf)
                return Error(403, "Solo el profesional tratante puede emitir documentos para este paciente.");
            var dao = new DocumentosClinicosDAO();
            var p = clave is null ? null : dao.Vigente(clave);
            if (p is null) return Error(404, "Ese tipo de documento no está disponible.");
            if (idTurno is int t && !dao.TurnoEsDelPaciente(t, id)) return Error(400, "Ese turno no es de este paciente.");
            var ahora = reloj.GetLocalNow().DateTime;
            var sistema = dao.DatosSistema(id, idProf, idTurno, ahora.Date);
            var campos = RenderizadorDocumento.LeerCampos(p.CamposJson);
            var valores = new Dictionary<string, string?>();
            var precargados = new List<string>();
            foreach (var c in campos)
            {
                if (c.Precarga is not null && sistema.GetValueOrDefault(c.Precarga) is string s && s.Length > 0) { valores[c.Clave] = s; precargados.Add(c.Clave); }
                else if (c.ValorInicial is not null) valores[c.Clave] = c.ValorInicial;
            }
            return Results.Ok(new
            {
                plantilla = new { p.IdPlantilla, p.Clave, p.Titulo, p.Grupo, p.Numero, p.Provisoria, campos },
                valores, precargados,
                turnos = p.Clave == "const_asistencia"
                    ? dao.TurnosParaConstancia(id, idProf, ahora).Select(x => new { x.IdTurno, fecha = x.Fecha.ToString("yyyy-MM-dd"), hora = x.Hora.ToString(@"hh\:mm"), x.Estado })
                    : null,
                requiereAutorizacion = p.Clave == "inf_escolar" && !dao.TieneAutorizacionInfo(id)
            });
        }).Solo(Quien.Profesional);

        app.MapPost("/pacientes/{id:int}/documentos/vista-previa", (int id, DocumentoRequest req, HttpContext ctx,
            IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!AutorizacionClinica.PuedePaciente(ctx, id) || yo.IdProfesional is not int idProf)
                return Error(403, "Solo el profesional tratante puede emitir documentos para este paciente.");
            var dao = new DocumentosClinicosDAO();
            var p = req.Clave is null ? null : dao.Vigente(req.Clave);
            if (p is null) return Error(404, "Ese tipo de documento no está disponible.");
            var campos = RenderizadorDocumento.LeerCampos(p.CamposJson);
            var (datos, errores) = RenderizadorDocumento.LimpiarDatos(campos, req.Datos, estricto: false);
            var sistema = RenderizadorDocumento.FormatearSistema(dao.DatosSistema(id, idProf, req.IdTurno, reloj.GetLocalNow().DateTime.Date));
            var doc = RenderizadorDocumento.Renderizar(p.Titulo, p.Provisoria, campos, RenderizadorDocumento.LeerCuerpo(p.CuerpoJson), datos, sistema);
            return Results.Ok(new { documento = doc, errores });
        }).Solo(Quien.Profesional);

        app.MapPost("/pacientes/{id:int}/documentos", (int id, DocumentoRequest req, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (!AutorizacionClinica.PuedePaciente(ctx, id) || yo.IdProfesional is not int idProf)
                return Error(403, "Solo el profesional tratante puede emitir documentos para este paciente.");
            var dao = new DocumentosClinicosDAO();
            var p = req.Clave is null ? null : dao.Vigente(req.Clave);
            if (p is null) return Error(404, "Ese tipo de documento no está disponible.");
            if (req.IdTurno is int t && !dao.TurnoEsDelPaciente(t, id)) return Error(400, "Ese turno no es de este paciente.");
            if (req.IdEventoCrisis is int e && !dao.EventoEsDelPaciente(e, id)) return Error(400, "Ese evento de crisis no es de este paciente.");
            if (p.Clave == "inf_escolar" && !dao.TieneAutorizacionInfo(id))
                return Error(409, "Para emitir un informe para la escuela, primero emití una autorización para compartir información firmada por el paciente o su responsable.");

            var campos = RenderizadorDocumento.LeerCampos(p.CamposJson);
            var (datos, errores) = RenderizadorDocumento.LimpiarDatos(campos, req.Datos, estricto: true);
            if (errores.Count > 0) return Error(400, "Revisá los datos marcados.", errores);

            var ahora = reloj.GetLocalNow().DateTime;
            var fecha = new DateTime(ahora.Year, ahora.Month, ahora.Day, ahora.Hour, ahora.Minute, ahora.Second);   // DATETIME2(0)
            var sistemaCrudo = dao.DatosSistema(id, idProf, req.IdTurno, ahora.Date);
            var sistema = RenderizadorDocumento.FormatearSistema(sistemaCrudo);
            var doc = RenderizadorDocumento.Renderizar(p.Titulo, p.Provisoria, campos, RenderizadorDocumento.LeerCuerpo(p.CuerpoJson), datos, sistema);

            var datosJson = RenderizadorDocumento.Serializar(datos);
            var contenidoJson = RenderizadorDocumento.Serializar(doc);
            var hash = HashContenido(id, idProf, p.IdVersion, fecha, datosJson, contenidoJson);
            var membrete = new MembreteDocumento(sistema["organizacion.nombre"], sistema["organizacion.localidad"], sistema["profesional.nombre"],
                sistema["profesional.matricula"], sistema["profesional.especialidad"]);
            var pdf = PdfDocumentoClinico.Generar(doc, membrete, dao.Firma(idProf), fecha, hash[..12].ToUpperInvariant());

            var idDoc = dao.Insertar(id, dao.HistoriaDe(id), idProf, p.IdVersion, req.IdTurno, req.IdEventoCrisis, datosJson, contenidoJson,
                pdf, hash, RenderizadorDocumento.Sha256(pdf), fecha, yo.IdUsuario);
            AuditoriaIds.Registrar(ctx, "Emisión de documento clínico", "DocumentoClinico", idDoc, id);
            return Results.Created($"/documentos/{idDoc}", new { idDocumento = idDoc, codigo = hash[..12].ToUpperInvariant(), titulo = p.Titulo });
        }).Solo(Quien.Profesional);

        app.MapGet("/pacientes/{id:int}/documentos", (int id, HttpContext ctx) =>
        {
            if (!AutorizacionClinica.PuedeVerPaciente(ctx, id)) return Error(403, "No podés ver los documentos de este paciente.");
            AuditoriaIds.Registrar(ctx, "Consulta de documentos clínicos del paciente", "DocumentoClinico", 0, id);
            return Results.Ok(new DocumentosClinicosDAO().DelPaciente(id).Select(d => new
            {
                d.IdDocumento, d.Clave, d.Titulo, d.Grupo, version = d.NumeroVersion, d.Provisoria, d.IdProfesional, d.Profesional,
                d.FechaEmision, codigo = d.HashSha256[..12].ToUpperInvariant(), d.Addenda
            }));
        }).Solo(Quien.Profesional);

        app.MapGet("/documentos/{id:int}", (int id, HttpContext ctx) =>
        {
            var dao = new DocumentosClinicosDAO();
            var d = dao.Documento(id);
            if (d is null) return Results.NotFound();
            if (!AutorizacionClinica.PuedeVerPaciente(ctx, d.Resumen.IdPaciente)) return Error(403, "No podés ver este documento.");
            AuditoriaIds.Registrar(ctx, "Lectura de documento clínico", "DocumentoClinico", id, d.Resumen.IdPaciente);
            return Results.Ok(new
            {
                d.Resumen.IdDocumento, d.Resumen.Clave, d.Resumen.Titulo, d.Resumen.Grupo, version = d.Resumen.NumeroVersion, d.Resumen.Provisoria,
                d.Resumen.Profesional, d.Resumen.FechaEmision, codigo = d.Resumen.HashSha256[..12].ToUpperInvariant(),
                documento = JsonSerializer.Deserialize<DocumentoRenderizado>(d.ContenidoJson, RenderizadorDocumento.Json),
                addenda = dao.Addenda(id),
                puedeAgregarAddendum = AutorizacionClinica.PuedePaciente(ctx, d.Resumen.IdPaciente)
            });
        }).Solo(Quien.Profesional);

        app.MapGet("/documentos/{id:int}/pdf", (int id, HttpContext ctx) =>
        {
            var dao = new DocumentosClinicosDAO();
            var d = dao.Documento(id);
            if (d is null) return Results.NotFound();
            if (!AutorizacionClinica.PuedeVerPaciente(ctx, d.Resumen.IdPaciente)) return Error(403, "No podés descargar este documento.");
            var pdf = dao.Pdf(id);
            if (pdf is null) return Results.NotFound();
            AuditoriaIds.Registrar(ctx, "Descarga de documento clínico (PDF)", "DocumentoClinico", id, d.Resumen.IdPaciente);
            ctx.Response.Headers.CacheControl = "no-store";
            var nombre = $"{d.Resumen.Clave}-{d.Resumen.FechaEmision:yyyyMMdd}-{d.Resumen.HashSha256[..12].ToUpperInvariant()}.pdf";
            return Results.File(pdf, "application/pdf", nombre);
        }).Solo(Quien.Profesional);

        app.MapGet("/documentos/{id:int}/integridad", (int id, HttpContext ctx) =>
        {
            var dao = new DocumentosClinicosDAO();
            var d = dao.Documento(id);
            if (d is null) return Results.NotFound();
            if (!AutorizacionClinica.PuedeVerPaciente(ctx, d.Resumen.IdPaciente)) return Error(403, "No podés ver este documento.");
            var calculado = HashContenido(d.Resumen.IdPaciente, d.Resumen.IdProfesional, d.IdVersion, d.Resumen.FechaEmision, d.DatosJson, d.ContenidoJson);
            var pdf = dao.Pdf(id);
            var hashPdf = pdf is null ? null : RenderizadorDocumento.Sha256(pdf);
            var addenda = dao.Addenda(id).Select(a => new
            {
                a.IdAddendum,
                integro = HashAddendum(id, d.Resumen.HashSha256, a.IdProfesional, a.Fecha, a.Texto) == a.HashSha256
            }).ToList();
            AuditoriaIds.Registrar(ctx, "Verificación de integridad de documento clínico", "DocumentoClinico", id, d.Resumen.IdPaciente);
            var contenidoIntegro = calculado == d.Resumen.HashSha256;
            var pdfIntegro = hashPdf is not null && hashPdf == d.HashPdfSha256;
            return Results.Ok(new
            {
                integro = contenidoIntegro && pdfIntegro && addenda.All(a => a.integro),
                contenidoIntegro, pdfIntegro, addenda,
                codigo = d.Resumen.HashSha256[..12].ToUpperInvariant(), hash = d.Resumen.HashSha256
            });
        }).Solo(Quien.Profesional);

        app.MapPost("/documentos/{id:int}/addenda", (int id, AddendumRequest req, HttpContext ctx, IdentidadActualService identidad, TimeProvider reloj) =>
        {
            var yo = identidad.Resolver(ctx)!;
            var dao = new DocumentosClinicosDAO();
            var d = dao.Documento(id);
            if (d is null) return Results.NotFound();
            if (!AutorizacionClinica.PuedePaciente(ctx, d.Resumen.IdPaciente) || yo.IdProfesional is not int idProf)
                return Error(403, "Solo el profesional tratante puede agregar un addendum.");
            var texto = req.Texto?.Trim();
            if (string.IsNullOrEmpty(texto) || texto.Length > 4000) return Error(400, "Escribí el addendum (hasta 4000 caracteres).");
            var ahora = reloj.GetLocalNow().DateTime;
            var fecha = new DateTime(ahora.Year, ahora.Month, ahora.Day, ahora.Hour, ahora.Minute, ahora.Second);
            var idA = dao.InsertarAddendum(id, texto, idProf, fecha, HashAddendum(id, d.Resumen.HashSha256, idProf, fecha, texto));
            AuditoriaIds.Registrar(ctx, "Addendum a documento clínico", "DocumentoClinico", id, d.Resumen.IdPaciente);
            return Results.Created($"/documentos/{id}", new { idAddendum = idA });
        }).Solo(Quien.Profesional);

        // ═════════════════════════════ Firma del profesional ═════════════════════════════
        app.MapGet("/profesionales/yo/firma", (HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProf || new DocumentosClinicosDAO().Firma(idProf) is not { Length: > 0 } img) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.File(img, EsPng(img) ? "image/png" : "image/jpeg");
        }).Solo(Quien.Profesional);

        app.MapPut("/profesionales/yo/firma", (FirmaRequest req, HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProf) return Error(403, "Tu cuenta no tiene ficha de profesional.");
            var b64 = req.ImagenBase64 ?? "";
            var coma = b64.IndexOf(',');
            if (b64.StartsWith("data:", StringComparison.Ordinal) && coma > 0) b64 = b64[(coma + 1)..];
            byte[] img;
            try { img = Convert.FromBase64String(b64); } catch (FormatException) { return Error(400, "La imagen no es válida."); }
            if (img.Length == 0 || img.Length > MaxFirmaBytes) return Error(400, "La imagen tiene que pesar hasta 300 KB.");
            if (!EsPng(img) && !EsJpg(img)) return Error(400, "La firma tiene que ser PNG o JPG.");
            new DocumentosClinicosDAO().GuardarFirma(idProf, img);
            AuditoriaAgenda.Registrar(yo.IdUsuario, "Firma del profesional cargada", "Profesional", idProf, $"Profesional #{idProf}", yo.Modulo ?? yo.Rol);
            return Results.NoContent();
        }).Solo(Quien.Profesional);

        app.MapDelete("/profesionales/yo/firma", (HttpContext ctx, IdentidadActualService identidad) =>
        {
            var yo = identidad.Resolver(ctx)!;
            if (yo.IdProfesional is not int idProf) return Results.NotFound();
            new DocumentosClinicosDAO().GuardarFirma(idProf, null);
            AuditoriaAgenda.Registrar(yo.IdUsuario, "Firma del profesional quitada", "Profesional", idProf, $"Profesional #{idProf}", yo.Modulo ?? yo.Rol);
            return Results.NoContent();
        }).Solo(Quien.Profesional);

        return app;
    }

    private static bool EsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
    private static bool EsJpg(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
}
