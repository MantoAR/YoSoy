using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;

// La DERIVACIÓN INTERNA asistida desde el cierre (Derivacion con Origen 'Cierre') contra SQL Server de verdad y la aplicación completa. Mecanismo elegido: Derivacion (profesional de origen →
// profesional de destino, con motivo), no CasoDerivacion (que enruta casos nuevos a un grupo, sin profesional de origen). Si la base no tiene las migraciones 26 y 27, estas pruebas se OMITEN.
//
// Lo que se prueba: el sistema precarga y avisa (campanita a destino, origen y Admisión; mail a los dos profesionales, sin datos del paciente) pero NO cambia ningún vínculo: el destino
// acepta o rechaza, y el traspaso lo confirma una persona con el rol que ya puede reasignar casos, con motivo e historial.
[Trait("Categoria", "Integracion")]
[Trait("Migracion", "27")]
public class DerivacionAsistidaBaseRealTests
{
    private readonly ITestOutputHelper _salida;
    public DerivacionAsistidaBaseRealTests(ITestOutputHelper salida) { _salida = salida; }

    private sealed class Entorno : IDisposable
    {
        public readonly EmailServiceEspia Mails = new();
        public readonly VideoApp App;
        public Entorno() =>
            App = new VideoApp(new Dictionary<string, string?> { ["Video:LimitePorMinuto"] = "1000" }, repositorioReal: true, auditoriaReal: true,
                servicios: s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(Mails); });
        public void Dispose() => App.Dispose();
    }

    private static HttpClient Profesional(VideoApp app, DatosEmociones d) => app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);
    private static HttpClient Crm(VideoApp app, DatosEmociones d) => app.Cliente(d.IdUsuarioConfigurador, Roles.Crm);

    private static object Derivar(int destino, bool? confirma = true, string? motivo = "Cambio de enfoque terapéutico. [TEST-PR6]", string? observaciones = "Observaciones ficticias.", int? idSesion = null, string? clave = null) =>
        new { confirma, idProfesionalDestino = destino, motivo, observaciones, idSesion, claveCierre = clave };

    private static async Task<(HttpResponseMessage R, JsonElement J)> Enviar(HttpClient c, HttpMethod metodo, string url, object? cuerpo = null)
    {
        var req = new HttpRequestMessage(metodo, url);
        if (cuerpo is not null) req.Content = JsonContent.Create(cuerpo);
        var r = await c.SendAsync(req);
        return (r, await VideoApp.Json(r));
    }

    private static int Contar(string sql, params (string, object?)[] p) => Convert.ToInt32(DatosEmociones.Escalar(sql, p));

    private static string EstadoDerivacion(int id) => (string)DatosEmociones.Escalar("SELECT Estado FROM Derivacion WHERE IdDerivacion = @I", ("@I", id))!;

    private static int AvisosDe(DatosCierre c, string tipo) =>
        Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Destinatario = N'Profesional' AND IdProfesional = @M AND Tipo = @T", ("@M", c.IdProfesional), ("@T", tipo));

    // Los avisos de Admisión no tienen profesional: se reconocen por el apellido ÚNICO que Escenario le pone al profesional de origen (aparece en el texto).
    private static string Apellido(DatosCierre a) => (string)DatosEmociones.Escalar("SELECT Apellido FROM Profesional WHERE IdProfesional = @P", ("@P", a.IdProfesional))!;
    private static int AvisosDeAdmision(DatosCierre origen, string tipo) =>
        Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Destinatario = N'Administrador' AND Tipo = @T AND Mensaje LIKE @L", ("@T", tipo), ("@L", $"%{Apellido(origen)}%"));

    /// <summary>Dos profesionales con mail; el caso del paciente de A, asignado a A, con grupo (la reasignación de casos lo exige).</summary>
    private (DatosCierre A, DatosCierre B, int Caso) Escenario(string estadoCaso = "Asignado")
    {
        var a = new DatosCierre(_salida);
        var b = new DatosCierre(_salida);
        DatosEmociones.Ejecutar("UPDATE Profesional SET Mail = @M, Apellido = @Ap WHERE IdProfesional = @P", ("@M", $"a_{Guid.NewGuid():N}@example.test"), ("@Ap", "Origen" + Guid.NewGuid().ToString("N")[..8]), ("@P", a.IdProfesional));
        DatosEmociones.Ejecutar("UPDATE Profesional SET Mail = @M, Apellido = N'Destino' WHERE IdProfesional = @P", ("@M", $"b_{Guid.NewGuid():N}@example.test"), ("@P", b.IdProfesional));
        var caso = a.IdCaso(estadoCaso);
        a.ConGrupo(caso);
        return (a, b, caso);
    }

    // ───────────── Destinos y precarga ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task La_lista_de_destinos_excluye_al_que_deriva_y_trae_especialidad_y_carga_solo_para_quien_tiene_el_caso()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        b.IdCaso("Asignado");                                                       // B ya tiene un caso (carga 1)
        var (r, j) = await Enviar(Profesional(e.App, a.D), HttpMethod.Get, $"/casos/{caso}/destinos-derivacion");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var lista = j.EnumerateArray().ToList();
        Assert.DoesNotContain(lista, x => x.GetProperty("idProfesional").GetInt32() == a.IdProfesional);
        var delB = lista.Single(x => x.GetProperty("idProfesional").GetInt32() == b.IdProfesional);
        Assert.Equal(1, delB.GetProperty("casosActivos").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(delB.GetProperty("especialidad").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(delB.GetProperty("nombre").GetString()));

        Assert.Equal(HttpStatusCode.Forbidden, (await Profesional(e.App, b.D).GetAsync($"/casos/{caso}/destinos-derivacion")).StatusCode);   // B no tiene el caso
        Assert.Equal(HttpStatusCode.Forbidden, (await e.App.Cliente(a.D.IdUsuarioPaciente, Roles.Paciente, idPaciente: a.IdPaciente).GetAsync($"/casos/{caso}/destinos-derivacion")).StatusCode);
    }

    // ───────────── Crear ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task Derivar_crea_la_derivacion_avisa_a_los_tres_y_NO_cambia_ningun_vinculo()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var vinculosAntes = Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P", ("@P", a.IdPaciente));

        var (r, j) = await Enviar(Profesional(e.App, a.D), HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = j.GetProperty("idDerivacion").GetInt32();
        Assert.Equal("Pendiente del destino", j.GetProperty("estado").GetString());
        var fila = DatosEmociones.Filas("SELECT Origen, IdCaso, IdProfesionalOrigen, IdProfesionalDestino, IdPaciente, Motivo, Observaciones, Estado, Fecha FROM Derivacion WHERE IdDerivacion = @I", ("@I", id)).Single();
        Assert.Equal("Cierre", fila[0]); Assert.Equal(caso, fila[1]); Assert.Equal(a.IdProfesional, fila[2]); Assert.Equal(b.IdProfesional, fila[3]); Assert.Equal(a.IdPaciente, fila[4]);
        Assert.Equal("Cambio de enfoque terapéutico. [TEST-PR6]", fila[5]); Assert.Equal("Observaciones ficticias.", fila[6]); Assert.Equal("Pendiente del destino", fila[7]);
        Assert.Equal(DateTime.Today, (DateTime)fila[8]!);

        // Los tres avisos de la campanita…
        Assert.Equal(1, AvisosDe(b, "derivacion-pendiente"));
        Assert.Equal(1, AvisosDe(a, "derivacion-enviada"));
        Assert.Equal(1, AvisosDeAdmision(a, "derivacion-aviso"));
        // …y el mail a los dos profesionales, sin ningún dato del paciente ni del motivo.
        Assert.Equal(2, e.Mails.Avisos.Count);
        Assert.All(e.Mails.Avisos, m =>
        {
            Assert.DoesNotContain("TEST-PR6", m.Asunto + m.Texto);
            Assert.DoesNotContain("enfoque", m.Texto);
        });
        // El caso y los vínculos siguen exactamente igual: nada cambia hasta que una persona confirme el traspaso.
        Assert.Equal(a.IdProfesional, Convert.ToInt32(DatosEmociones.Escalar("SELECT IdProfesionalAsignado FROM Caso WHERE IdCaso = @C", ("@C", caso))));
        Assert.Equal("Asignado", a.EstadoDelCaso(caso));
        Assert.Equal(vinculosAntes, Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P", ("@P", a.IdPaciente)));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @M", ("@P", a.IdPaciente), ("@M", a.IdProfesional)));
        Assert.Single(a.Auditoria("Derivación interna creada"));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Las_derivaciones_mal_armadas_o_imposibles_se_rechazan_con_mensaje_claro_y_no_cambian_nada()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var cliente = Profesional(e.App, a.D);
        var url = $"/casos/{caso}/derivar";

        async Task Rechaza(object cuerpo, HttpStatusCode status, string codigo, string? contiene = null)
        {
            var (r, j) = await Enviar(cliente, HttpMethod.Post, url, cuerpo);
            Assert.Equal(status, r.StatusCode);
            Assert.Equal(codigo, j.GetProperty("codigo").GetString());
            if (contiene is not null) Assert.Contains(contiene, j.GetProperty("mensaje").GetString());
        }

        await Rechaza(Derivar(b.IdProfesional, confirma: null), HttpStatusCode.BadRequest, "falta-confirmacion");
        await Rechaza(Derivar(b.IdProfesional, confirma: false), HttpStatusCode.BadRequest, "falta-confirmacion");
        await Rechaza(Derivar(a.IdProfesional), HttpStatusCode.BadRequest, "mismo-profesional", "a vos mismo");     // no se puede derivar a uno mismo
        await Rechaza(Derivar(b.IdProfesional, motivo: ""), HttpStatusCode.BadRequest, "datos-invalidos", "motivo");
        await Rechaza(Derivar(b.IdProfesional, motivo: new string('x', 501)), HttpStatusCode.BadRequest, "datos-invalidos", "motivo");
        await Rechaza(Derivar(0), HttpStatusCode.BadRequest, "datos-invalidos", "profesional de destino");
        await Rechaza(Derivar(2_000_000_000), HttpStatusCode.Conflict, "destino-no-disponible");
        DatosEmociones.Ejecutar("UPDATE Profesional SET Estado = 'Inactivo' WHERE IdProfesional = @P", ("@P", b.IdProfesional));
        await Rechaza(Derivar(b.IdProfesional), HttpStatusCode.Conflict, "destino-no-disponible");                  // un destino dado de baja

        Assert.Equal(0, Contar("SELECT COUNT(*) FROM Derivacion WHERE IdCaso = @C", ("@C", caso)));
        Assert.Empty(e.Mails.Avisos);
        Assert.Equal(0, AvisosDe(b, "derivacion-pendiente"));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task No_se_puede_derivar_un_caso_cerrado_ni_uno_ya_en_alta_ni_uno_sin_asignar()
    {
        using var e = new Entorno();
        foreach (var estado in new[] { "Cerrado", "En alta", "Nuevo", "Derivado" })
        {
            var (a, b, caso) = Escenario(estado);
            using var _a = a; using var _b = b;

            var (r, j) = await Enviar(Profesional(e.App, a.D), HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional));

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("estado-no-permitido", j.GetProperty("codigo").GetString());
            Assert.Equal(0, Contar("SELECT COUNT(*) FROM Derivacion WHERE IdCaso = @C", ("@C", caso)));
            if (estado == "Cerrado") Assert.Contains("cerrado", j.GetProperty("mensaje").GetString());
        }
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Solo_el_profesional_a_cargo_del_caso_puede_derivarlo()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var url = $"/casos/{caso}/derivar";

        var (r, j) = await Enviar(Profesional(e.App, b.D), HttpMethod.Post, url, Derivar(a.IdProfesional));          // B intenta derivar el caso de A
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("caso-ajeno", j.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await e.App.Cliente(a.D.IdUsuarioPaciente, Roles.Paciente, idPaciente: a.IdPaciente).PostAsJsonAsync(url, Derivar(b.IdProfesional))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.App.Cliente(a.D.IdUsuarioConfigurador, Roles.Configurador).PostAsJsonAsync(url, Derivar(b.IdProfesional))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Crm(e.App, a.D).PostAsJsonAsync(url, Derivar(b.IdProfesional))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.App.SinSesion().PostAsJsonAsync(url, Derivar(b.IdProfesional))).StatusCode);
        var (rNo, jNo) = await Enviar(Profesional(e.App, a.D), HttpMethod.Post, "/casos/2000000000/derivar", Derivar(b.IdProfesional));
        Assert.Equal(HttpStatusCode.NotFound, rNo.StatusCode);
        Assert.Equal("caso-inexistente", jNo.GetProperty("codigo").GetString());
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM Derivacion WHERE IdCaso = @C", ("@C", caso)));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Derivar_dos_veces_lo_mismo_es_idempotente_y_a_otro_destino_mientras_hay_una_en_curso_se_rechaza()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var c3 = new DatosCierre(_salida); using var e = new Entorno();
        var cliente = Profesional(e.App, a.D);

        var (r1, j1) = await Enviar(cliente, HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional));
        var (r2, j2) = await Enviar(cliente, HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional));
        var (r3, j3) = await Enviar(cliente, HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(c3.IdProfesional));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.True(j2.GetProperty("repetido").GetBoolean());
        Assert.Equal(j1.GetProperty("idDerivacion").GetInt32(), j2.GetProperty("idDerivacion").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, r3.StatusCode);
        Assert.Equal("derivacion-en-curso", j3.GetProperty("codigo").GetString());
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM Derivacion WHERE IdCaso = @C", ("@C", caso)));
        Assert.Equal(1, AvisosDe(b, "derivacion-pendiente"));                      // los avisos y los mails salieron una sola vez
        Assert.Equal(2, e.Mails.Avisos.Count);
        Assert.Single(a.Auditoria("Derivación interna creada"));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Dos_derivaciones_simultaneas_del_mismo_caso_dejan_una_sola_en_curso()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var cliente = Profesional(e.App, a.D);

        var resultados = await Task.WhenAll(Enviar(cliente, HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional)), Enviar(cliente, HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional)));

        Assert.All(resultados, x => Assert.True(x.R.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict, x.R.StatusCode + " " + x.J));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM Derivacion WHERE IdCaso = @C", ("@C", caso)));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Si_falla_uno_de_los_avisos_no_queda_la_derivacion_a_medias_ni_sale_ningun_mail()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var trigger = "TR_prueba_deriv_" + Guid.NewGuid().ToString("N")[..8];
        DatosEmociones.Ejecutar($"CREATE TRIGGER {trigger} ON NotificacionInterna AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE Tipo = N'derivacion-aviso' AND Mensaje LIKE N'%{Apellido(a)}%') THROW 51000, 'falla provocada por la prueba', 1;");
        try
        {
            var (r, _) = await Enviar(Profesional(e.App, a.D), HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional));

            Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
            Assert.Equal(0, Contar("SELECT COUNT(*) FROM Derivacion WHERE IdCaso = @C", ("@C", caso)));
            Assert.Equal(0, AvisosDe(b, "derivacion-pendiente"));
            Assert.Equal(0, AvisosDe(a, "derivacion-enviada"));
            Assert.Empty(a.Auditoria("Derivación interna creada"));
            Assert.Empty(e.Mails.Avisos);
        }
        finally { DatosEmociones.Ejecutar($"DROP TRIGGER {trigger}"); }

        var (r2, _) = await Enviar(Profesional(e.App, a.D), HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional));   // resuelta la causa, se reintenta
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
    }

    // ───────────── El destino acepta o rechaza ─────────────

    private async Task<int> Derivada(Entorno e, DatosCierre a, DatosCierre b, int caso)
    {
        var (r, j) = await Enviar(Profesional(e.App, a.D), HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return j.GetProperty("idDerivacion").GetInt32();
    }

    [FactConBaseDeAltaDerivacion]
    public async Task El_destino_acepta_se_avisa_a_Admision_y_al_origen_y_sigue_sin_cambiar_ningun_vinculo()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var id = await Derivada(e, a, b, caso);
        var mailsAntes = e.Mails.Avisos.Count;

        var (r, j) = await Enviar(Profesional(e.App, b.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = true });
        var (r2, j2) = await Enviar(Profesional(e.App, b.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = true });   // repetir

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Aceptada por el destino", j.GetProperty("estado").GetString());
        Assert.False(j.GetProperty("repetido").GetBoolean());
        Assert.True(j2.GetProperty("repetido").GetBoolean());
        Assert.Equal("Aceptada por el destino", EstadoDerivacion(id));
        Assert.NotNull(DatosEmociones.Escalar("SELECT RespuestaUtc FROM Derivacion WHERE IdDerivacion = @I", ("@I", id)));
        Assert.Equal(1, AvisosDeAdmision(a, "traspaso-pendiente"));                    // la tarea «Confirmar traspaso de…»
        Assert.Equal(1, AvisosDe(a, "derivacion-aceptada"));                           // constancia para el origen
        Assert.Equal(mailsAntes + 1, e.Mails.Avisos.Count);                            // un mail al origen, una sola vez
        Assert.Single(b.Auditoria("Derivación aceptada por el destino"));
        // El vínculo y el caso siguen igual: aceptar NO traspasa.
        Assert.Equal(a.IdProfesional, Convert.ToInt32(DatosEmociones.Escalar("SELECT IdProfesionalAsignado FROM Caso WHERE IdCaso = @C", ("@C", caso))));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @M", ("@P", a.IdPaciente), ("@M", b.IdProfesional)));
        // Y una vez aceptada no se puede rechazar.
        var (r3, j3) = await Enviar(Profesional(e.App, b.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = false });
        Assert.Equal(HttpStatusCode.Conflict, r3.StatusCode);
        Assert.Equal("ya-respondida", j3.GetProperty("codigo").GetString());
        Assert.Equal("Aceptada por el destino", EstadoDerivacion(id));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task El_destino_rechaza_Admision_recibe_el_aviso_para_decidir_y_el_caso_sigue_con_el_origen()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var id = await Derivada(e, a, b, caso);

        var (r, j) = await Enviar(Profesional(e.App, b.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = false, motivo = "No tengo lugar en mi agenda." });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Rechazada por el destino", j.GetProperty("estado").GetString());
        Assert.Equal(1, AvisosDeAdmision(a, "derivacion-rechazada"));
        Assert.Equal(1, AvisosDe(a, "derivacion-rechazada"));
        Assert.Contains("No tengo lugar", (string)DatosEmociones.Escalar("SELECT Mensaje FROM NotificacionInterna WHERE Destinatario = N'Administrador' AND Tipo = N'derivacion-rechazada' AND Mensaje LIKE N'%[[]TEST-PR6]%' AND Mensaje LIKE N'%No tengo lugar%'")!);
        Assert.Equal(a.IdProfesional, Convert.ToInt32(DatosEmociones.Escalar("SELECT IdProfesionalAsignado FROM Caso WHERE IdCaso = @C", ("@C", caso))));
        // Admisión la da por resuelta (idempotente) y el origen puede volver a derivar el caso a otro.
        var crm = Crm(e.App, a.D);
        var (rr, jr) = await Enviar(crm, HttpMethod.Post, $"/derivaciones/{id}/resolver");
        var (rr2, jr2) = await Enviar(crm, HttpMethod.Post, $"/derivaciones/{id}/resolver");
        Assert.Equal(HttpStatusCode.OK, rr.StatusCode);
        Assert.Equal("Resuelta por admisión", jr.GetProperty("estado").GetString());
        Assert.True(jr2.GetProperty("repetido").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await Enviar(Profesional(e.App, a.D), HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional))).R.StatusCode);
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Solo_el_profesional_de_destino_puede_responder_y_una_derivacion_ajena_se_ve_como_inexistente()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var c3 = new DatosCierre(_salida); using var e = new Entorno();
        var id = await Derivada(e, a, b, caso);
        var url = $"/derivaciones/{id}/responder";

        Assert.Equal(HttpStatusCode.NotFound, (await Profesional(e.App, a.D).PutAsJsonAsync(url, new { acepta = true })).StatusCode);        // ni el que derivó
        Assert.Equal(HttpStatusCode.NotFound, (await Profesional(e.App, c3.D).PutAsJsonAsync(url, new { acepta = true })).StatusCode);       // ni un tercero
        Assert.Equal(HttpStatusCode.Forbidden, (await Crm(e.App, a.D).PutAsJsonAsync(url, new { acepta = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.App.Cliente(a.D.IdUsuarioPaciente, Roles.Paciente, idPaciente: a.IdPaciente).PutAsJsonAsync(url, new { acepta = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Profesional(e.App, b.D).PutAsJsonAsync(url, new { motivo = "x" })).StatusCode);       // sin decir si acepta
        Assert.Equal("Pendiente del destino", EstadoDerivacion(id));
    }

    // ───────────── Informe, campanita y avisos ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task El_destino_ve_el_informe_de_derivacion_y_nada_mas_de_la_historia_y_un_tercero_no_ve_nada()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var c3 = new DatosCierre(_salida); using var e = new Entorno();
        var id = await Derivada(e, a, b, caso);

        var (r, j) = await Enviar(Profesional(e.App, b.D), HttpMethod.Get, $"/derivaciones/{id}/informe");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Cambio de enfoque terapéutico. [TEST-PR6]", j.GetProperty("motivo").GetString());
        Assert.Equal("Observaciones ficticias.", j.GetProperty("observaciones").GetString());
        Assert.Equal(a.IdPaciente, j.GetProperty("paciente").GetProperty("id").GetInt32());
        // Solo el informe: ninguna sesión, evolución ni historia.
        Assert.Equal(new[] { "destino", "estado", "fecha", "idDerivacion", "motivo", "observaciones", "origen", "paciente" },
            j.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(HttpStatusCode.OK, (await Profesional(e.App, a.D).GetAsync($"/derivaciones/{id}/informe")).StatusCode);                  // el origen también
        Assert.Equal(HttpStatusCode.NotFound, (await Profesional(e.App, c3.D).GetAsync($"/derivaciones/{id}/informe")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.App.Cliente(a.D.IdUsuarioPaciente, Roles.Paciente, idPaciente: a.IdPaciente).GetAsync($"/derivaciones/{id}/informe")).StatusCode);
        // El destino NO obtiene acceso a la historia clínica ni a las sesiones del paciente.
        Assert.Equal(HttpStatusCode.NotFound, (await Profesional(e.App, b.D).GetAsync($"/pacientes/{a.IdPaciente}/sesiones-clinicas")).StatusCode);
        Assert.Single(b.Auditoria("Consulta de informe de derivación"));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task La_campanita_del_profesional_trae_sus_avisos_y_lo_que_le_toca_responder_y_cada_uno_marca_los_suyos()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var id = await Derivada(e, a, b, caso);

        var (r, j) = await Enviar(Profesional(e.App, b.D), HttpMethod.Get, $"/profesionales/{b.IdProfesional}/avisos-clinicos");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var porResponder = Assert.Single(j.GetProperty("porResponder").EnumerateArray());
        Assert.Equal(id, porResponder.GetProperty("idDerivacion").GetInt32());
        Assert.Equal("Cambio de enfoque terapéutico. [TEST-PR6]", porResponder.GetProperty("motivo").GetString());
        var aviso = Assert.Single(j.GetProperty("avisos").EnumerateArray());
        Assert.Equal("derivacion-pendiente", aviso.GetProperty("tipo").GetString());
        Assert.False(aviso.GetProperty("leida").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await Profesional(e.App, a.D).GetAsync($"/profesionales/{b.IdProfesional}/avisos-clinicos")).StatusCode);   // los avisos de otro no
        var idAviso = aviso.GetProperty("idAviso").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await Profesional(e.App, a.D).PutAsync($"/avisos-clinicos/{idAviso}/leida", null)).StatusCode);          // ni marcarlos
        Assert.Equal(HttpStatusCode.NoContent, (await Profesional(e.App, b.D).PutAsync($"/avisos-clinicos/{idAviso}/leida", null)).StatusCode);
        var (_, j2) = await Enviar(Profesional(e.App, b.D), HttpMethod.Get, $"/profesionales/{b.IdProfesional}/avisos-clinicos");
        Assert.True(j2.GetProperty("avisos")[0].GetProperty("leida").GetBoolean());
    }

    // ───────────── El traspaso: lo confirma una persona, con la reasignación de casos de siempre ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task Admision_confirma_el_traspaso_y_recien_ahi_cambia_el_vinculo_con_motivo_historial_y_avisos()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var id = await Derivada(e, a, b, caso);
        await Enviar(Profesional(e.App, b.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = true });
        var crm = Crm(e.App, a.D);

        // La tarea de Admisión está en la lista de traspasos pendientes.
        var (rl, jl) = await Enviar(crm, HttpMethod.Get, "/derivaciones/traspasos-pendientes");
        Assert.Equal(HttpStatusCode.OK, rl.StatusCode);
        var tarea = jl.EnumerateArray().Single(x => x.GetProperty("idDerivacion").GetInt32() == id);
        Assert.Equal("Aceptada por el destino", tarea.GetProperty("estado").GetString());
        Assert.Contains("TEST-PR6", tarea.GetProperty("nombrePaciente").GetString());

        var (r, j) = await Enviar(crm, HttpMethod.Post, $"/derivaciones/{id}/traspasar");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Traspasada", j.GetProperty("estado").GetString());
        Assert.Equal("Traspasada", EstadoDerivacion(id));
        Assert.Equal(b.IdProfesional, Convert.ToInt32(DatosEmociones.Escalar("SELECT IdProfesionalAsignado FROM Caso WHERE IdCaso = @C", ("@C", caso))));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @M", ("@P", a.IdPaciente), ("@M", b.IdProfesional)));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @M", ("@P", a.IdPaciente), ("@M", a.IdProfesional)));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM HistorialAsignacion WHERE IdCaso = @C AND IdProfesional = @M AND Motivo = 'Manual' AND IdRealizadoPor = @U", ("@C", caso), ("@M", b.IdProfesional), ("@U", a.D.IdUsuarioConfigurador)));
        var manual = DatosEmociones.Filas("SELECT Motivo, Nota, Estado FROM CasoDerivacion WHERE IdCaso = @C AND Origen = 'Manual'", ("@C", caso)).Single();
        Assert.Equal("Otro", manual[0]); Assert.Equal($"Derivación interna #{id}", manual[1]);
        Assert.Equal(1, AvisosDe(b, "traspaso-confirmado"));
        Assert.Equal(1, AvisosDe(a, "traspaso-confirmado"));
        Assert.Single(a.Auditoria("Traspaso confirmado"));
        var (_, jDespues) = await Enviar(crm, HttpMethod.Get, "/derivaciones/traspasos-pendientes");
        Assert.DoesNotContain(jDespues.EnumerateArray(), x => x.GetProperty("idDerivacion").GetInt32() == id);   // ya no figura como pendiente
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Confirmar_el_traspaso_dos_veces_no_duplica_historial_ni_avisos()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var id = await Derivada(e, a, b, caso);
        await Enviar(Profesional(e.App, b.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = true });
        var crm = Crm(e.App, a.D);

        await Enviar(crm, HttpMethod.Post, $"/derivaciones/{id}/traspasar");
        var (r2, j2) = await Enviar(crm, HttpMethod.Post, $"/derivaciones/{id}/traspasar");

        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.True(j2.GetProperty("repetido").GetBoolean());
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM HistorialAsignacion WHERE IdCaso = @C AND Motivo = 'Manual'", ("@C", caso)));
        Assert.Equal(1, AvisosDe(b, "traspaso-confirmado"));
        Assert.Single(a.Auditoria("Traspaso confirmado"));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task El_traspaso_solo_lo_ejecuta_el_rol_que_ya_puede_reasignar_y_solo_si_el_destino_acepto()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var id = await Derivada(e, a, b, caso);
        var url = $"/derivaciones/{id}/traspasar";

        // Pendiente del destino: ni siquiera Admisión puede traspasar.
        var (rp, jp) = await Enviar(Crm(e.App, a.D), HttpMethod.Post, url);
        Assert.Equal(HttpStatusCode.Conflict, rp.StatusCode);
        Assert.Equal("estado-de-derivacion", jp.GetProperty("codigo").GetString());
        await Enviar(Profesional(e.App, b.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = true });

        // Nadie más: ni el origen, ni el destino, ni el Configurador solo, ni un paciente, ni sin sesión.
        Assert.Equal(HttpStatusCode.Forbidden, (await Profesional(e.App, a.D).PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Profesional(e.App, b.D).PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.App.Cliente(a.D.IdUsuarioConfigurador, Roles.Configurador).PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.App.Cliente(a.D.IdUsuarioPaciente, Roles.Paciente, idPaciente: a.IdPaciente).PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.App.SinSesion().PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Profesional(e.App, a.D).GetAsync("/derivaciones/traspasos-pendientes")).StatusCode);
        Assert.Equal("Aceptada por el destino", EstadoDerivacion(id));
        Assert.Equal(a.IdProfesional, Convert.ToInt32(DatosEmociones.Escalar("SELECT IdProfesionalAsignado FROM Caso WHERE IdCaso = @C", ("@C", caso))));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task El_traspaso_se_rechaza_si_el_caso_se_cerro_o_cambio_de_manos_o_el_destino_ya_no_esta()
    {
        using var e = new Entorno();
        // Caso cerrado entre la aceptación y el traspaso.
        var (a, b, caso) = Escenario();
        using (a) using (b)
        {
            var id = await Derivada(e, a, b, caso);
            await Enviar(Profesional(e.App, b.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = true });
            DatosEmociones.Ejecutar("UPDATE Caso SET Estado = 'Cerrado' WHERE IdCaso = @C", ("@C", caso));
            var (r, j) = await Enviar(Crm(e.App, a.D), HttpMethod.Post, $"/derivaciones/{id}/traspasar");
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("estado-no-permitido", j.GetProperty("codigo").GetString());
            Assert.Equal("Aceptada por el destino", EstadoDerivacion(id));
        }
        // El caso pasó a un tercero mientras tanto.
        var (a2, b2, caso2) = Escenario();
        using (a2) using (b2) using (var c3 = new DatosCierre(_salida))
        {
            var id = await Derivada(e, a2, b2, caso2);
            await Enviar(Profesional(e.App, b2.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = true });
            DatosEmociones.Ejecutar("UPDATE Caso SET IdProfesionalAsignado = @T WHERE IdCaso = @C", ("@T", c3.IdProfesional), ("@C", caso2));
            var (r, j) = await Enviar(Crm(e.App, a2.D), HttpMethod.Post, $"/derivaciones/{id}/traspasar");
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("caso-cambio", j.GetProperty("codigo").GetString());
            Assert.Equal(0, Contar("SELECT COUNT(*) FROM HistorialAsignacion WHERE IdCaso = @C AND Motivo = 'Manual'", ("@C", caso2)));
        }
        // El destino se dio de baja.
        var (a3, b3, caso3) = Escenario();
        using (a3) using (b3)
        {
            var id = await Derivada(e, a3, b3, caso3);
            await Enviar(Profesional(e.App, b3.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = true });
            DatosEmociones.Ejecutar("UPDATE Profesional SET Estado = 'Inactivo' WHERE IdProfesional = @P", ("@P", b3.IdProfesional));
            var (r, j) = await Enviar(Crm(e.App, a3.D), HttpMethod.Post, $"/derivaciones/{id}/traspasar");
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("destino-no-disponible", j.GetProperty("codigo").GetString());
        }
    }

    // ───────────── La solicitud de cambio de siempre no se toca ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task Las_acciones_viejas_de_Admision_no_pueden_usarse_sobre_una_derivacion_del_cierre_y_la_solicitud_de_siempre_sigue_igual()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var id = await Derivada(e, a, b, caso);
        await Enviar(Profesional(e.App, b.D), HttpMethod.Put, $"/derivaciones/{id}/responder", new { acepta = true });
        var admision = e.App.ConToken(e.App.Factory.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(a.D.IdUsuarioConfigurador,
            new SesionModulo(Modulos.Profesional, new[] { Roles.Profesional }, new[] { Privilegios.Admision }, a.IdProfesional)));

        // Aceptar a mano la derivación del cierre cambiaría los vínculos sin pasar por el traspaso confirmado: se rechaza.
        var (r, j) = await Enviar(admision, HttpMethod.Put, $"/derivaciones/{id}/aceptar", new { idProfesionalDestino = b.IdProfesional });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("Traspasos por confirmar", j.GetProperty("mensaje").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admision.PutAsync($"/derivaciones/{id}/rechazar", null)).StatusCode);
        Assert.Equal("Aceptada por el destino", EstadoDerivacion(id));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @M", ("@P", a.IdPaciente), ("@M", a.IdProfesional)));

        // La solicitud de cambio de siempre (Origen 'Solicitud') funciona como antes: Admisión la acepta y eso sí cambia el vínculo.
        var idSolicitud = DatosEmociones.Insertar(@"INSERT INTO Derivacion (IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado)
            OUTPUT INSERTED.IdDerivacion VALUES (@P, @A, @B, GETDATE(), N'solicitud de siempre', 'Pendiente')", ("@P", a.IdPaciente), ("@A", a.IdProfesional), ("@B", b.IdProfesional));
        var (r2, _) = await Enviar(admision, HttpMethod.Put, $"/derivaciones/{idSolicitud}/aceptar", new { idProfesionalDestino = b.IdProfesional });
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("Aceptada", EstadoDerivacion(idSolicitud));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @M", ("@P", a.IdPaciente), ("@M", b.IdProfesional)));
    }

    // ───────────── Enlace con la sesión del cierre ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task La_derivacion_se_enlaza_con_la_sesion_del_cierre_solo_si_se_cerro_como_Derivacion()
    {
        var (a, b, caso) = Escenario();
        using var _a = a; using var _b = b; using var e = new Entorno();
        var cliente = Profesional(e.App, a.D);
        async Task<int> Sesion(string tipo, Guid clave)
        {
            var rs = await cliente.PostAsJsonAsync("/sesiones-clinicas", new { idPaciente = a.IdPaciente, notasEvolucion = "n [TEST-PR6]", tipoCierre = tipo, claveCierre = clave.ToString(), confirmaRealizacion = false });
            return (await VideoApp.Json(rs)).GetProperty("idSesion").GetInt32();
        }
        var seguimiento = await Sesion("Seguimiento", Guid.NewGuid());
        var claveDeriv = Guid.NewGuid();
        var deriv = await Sesion("Derivación", claveDeriv);

        var (r1, j1) = await Enviar(cliente, HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional, idSesion: seguimiento));
        Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);
        Assert.Equal("sesion-no-es-del-tipo", j1.GetProperty("codigo").GetString());
        var (r2, j2) = await Enviar(cliente, HttpMethod.Post, $"/casos/{caso}/derivar", Derivar(b.IdProfesional, clave: claveDeriv.ToString()));

        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.Equal(deriv, j2.GetProperty("idSesion").GetInt32());
        Assert.Equal(deriv, Convert.ToInt32(DatosEmociones.Escalar("SELECT IdSesion FROM Derivacion WHERE IdCaso = @C", ("@C", caso))));
    }

    [Fact]
    public void Sin_la_migracion_27_la_derivacion_responde_409_y_la_externa_no_esta_en_esta_tanda()
    {
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        var endpoint = File.ReadAllText(Path.Combine(raiz, "Endpoints", "AltaDerivacionEndpoints.cs"));
        Assert.Contains("if (!ADMigracion.Disponible()) return Falla(AltaDerivacionReglas.FaltaMigracion27);", endpoint);
        // Sin migración, ninguna derivación es «del cierre»: las acciones viejas siguen como siempre.
        Assert.Contains("if (!ADMigracion.Disponible()) return false;", File.ReadAllText(Path.Combine(raiz, "DAOs", "DerivacionCierreDAO.cs")));
        // La derivación externa (a un psiquiatra) no existe: no hay tipo «externa» ni destinatario en texto libre.
        Assert.DoesNotContain("externa", File.ReadAllText(Path.Combine(raiz, "Services", "AltaDerivacionReglas.cs")), StringComparison.OrdinalIgnoreCase);
    }
}
