using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Se OMITE si la base no tiene las migraciones 27 (alta clínica) y 35 (informe estructurado y controles).</summary>
public sealed class FactConBaseDeAltaEstructuradaAttribute : FactAttribute
{
    public FactConBaseDeAltaEstructuradaAttribute()
    {
        if (Convert.ToInt32(DatosDePrueba.Escalar(@"SELECT CASE WHEN OBJECT_ID('dbo.ControlSeguimiento', 'U') IS NOT NULL AND OBJECT_ID('dbo.AltaClinica', 'U') IS NOT NULL
            AND COL_LENGTH('dbo.AltaClinica', 'InformeEstructurado') IS NOT NULL THEN 1 ELSE 0 END")) != 1)
            Skip = "La base no tiene las migraciones 27 y 35.";
    }
}

// Alta clínica ESTRUCTURADA con plan de control (migración 35): informe con secciones ligado a la historia clínica y a la sesión de cierre, controles a los 3 y 6 meses editables, recordatorios (no turnos)
// que avisan por la campanita al acercarse la fecha, sin duplicar. Una persona confirma; idempotente. Nadie recibe mails: el servicio de correo es un espía.
[Trait("Categoria", "Integracion")]
public class AltaEstructuradaBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly EmailServiceEspia _mails = new();
    private readonly WebApplicationFactory<Program> _fabrica;

    public AltaEstructuradaBaseRealTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(_mails); }));
    }

    public void Dispose()
    {
        DatosDePrueba.Ejecutar("DELETE FROM NotificacionInterna WHERE Mensaje LIKE @m", ("@m", "%" + _d.Marca + "%"));
        _fabrica.Dispose();
        _d.Dispose();
    }

    private DateTime Ahora => _fabrica.Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime;
    private HttpClient ComoProfesional(int id) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(id), Roles.Profesional, idProfesional: id);
    private HttpClient ComoPaciente(int id) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(id), Roles.Paciente, idPaciente: id);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }

    private static string Fecha(DateTime f) => f.ToString("yyyy-MM-dd");

    /// <summary>Profesional, paciente con su formulario «Sobre mí», un caso «En atención» y una sesión de cierre tipo Alta.</summary>
    private (int Prof, int Pac, int Caso, int Sesion) Escenario()
    {
        var prof = _d.Profesional(_d.Especialidad(), nombre: "Prof " + _d.Marca);
        var pac = _d.Paciente("Paciente " + _d.Marca, mail: $"{_d.Marca}@example.test");
        _d.PacienteProfesional(pac, prof);
        _d.AlLimpiar("Turno", "IdPaciente", pac);
        _d.AlLimpiar("Notificacion", "IdPaciente", pac);
        var informe = _d.InformeAdmision(pac, "Aceptado", null, null);
        DatosDePrueba.Ejecutar("UPDATE InformeAdmision SET MotivoConsulta = @m, ObjetivosPaciente = @o WHERE IdInforme = @i", ("@m", "Ansiedad [TEST] " + _d.Marca), ("@o", "Dormir mejor [TEST]"), ("@i", informe));
        var caso = Convert.ToInt32(DatosDePrueba.Escalar(
            "INSERT INTO Caso (IdPaciente, Estado, IdProfesionalAsignado, Origen) VALUES (@p, N'En atención', @m, N'Admision'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@p", pac), ("@m", prof)));
        _d.AlLimpiar("Caso", "IdCaso", caso);
        var turno = _d.Turno(pac, prof, "Confirmado", Ahora.Date, TimeSpan.FromHours(9));
        var sesion = Convert.ToInt32(DatosDePrueba.Escalar(
            "INSERT INTO SesionClinica (IdTurno, IdPaciente, IdProfesional, FechaHora, TipoCierre) VALUES (@t, @p, @m, GETDATE(), N'Alta'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@t", turno), ("@p", pac), ("@m", prof)));
        _d.AlLimpiar("SesionClinica", "IdSesion", sesion);
        _d.AlLimpiar("AltaClinica", "IdCaso", caso);
        _d.AlLimpiar("HistoriaClinica", "IdPaciente", pac);
        return (prof, pac, caso, sesion);
    }

    private static object Informe(string? diagnostico = "Trastorno de ansiedad (hipótesis) [TEST]") => new
    {
        motivoConsulta = "Ansiedad", diagnostico, objetivosTerapeuticos = "Reducir la ansiedad", gradoConsecucion = "Logrado en su mayor parte",
        tecnicas = "Reestructuración cognitiva", estadoAlAlta = "Estable", recursosPaciente = "Red de apoyo", recomendaciones = "Continuar con ejercicios", prevencionRecaidas = "Identificar señales tempranas"
    };

    private object Alta(int sesion, string?[]? controles = null, bool confirma = true, object? informe = null) => new
    {
        confirma, informeEstructurado = informe ?? Informe(), idSesion = sesion,
        controles = controles ?? new[] { Fecha(Ahora.AddMonths(3)), Fecha(Ahora.AddMonths(6)) }
    };

    private static string EstadoCaso(int caso) => (string)DatosDePrueba.Escalar("SELECT Estado FROM Caso WHERE IdCaso = @c", ("@c", caso))!;
    private static int Controles(int caso, string? estado = null) => DatosDePrueba.Contar("SELECT COUNT(*) FROM ControlSeguimiento WHERE IdCaso = @c AND (@e IS NULL OR Estado = @e)", ("@c", caso), ("@e", estado));
    private int Avisos(string destinatario) => DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Tipo = N'control-cercano' AND Destinatario = @d AND Mensaje LIKE @m", ("@d", destinatario), ("@m", "%" + _d.Marca + "%"));

    // ───────────── Precarga ─────────────

    [FactConBaseDeAltaEstructurada]
    public async Task La_precarga_trae_el_motivo_y_los_objetivos_del_formulario_y_los_controles_a_3_y_6_meses()
    {
        var (prof, pac, caso, _) = Escenario();

        var r = await ComoProfesional(prof).GetAsync($"/casos/{caso}/precarga-alta");
        var j = await Json(r);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Ansiedad [TEST]", j.GetProperty("motivoConsulta").GetString());
        Assert.Equal("Dormir mejor [TEST]", j.GetProperty("objetivosPaciente").GetString());
        var sugeridos = j.GetProperty("controlesSugeridos").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(new[] { Fecha(Ahora.AddMonths(3)), Fecha(Ahora.AddMonths(6)) }, sugeridos);
    }

    [FactConBaseDeAltaEstructurada]
    public async Task La_precarga_es_solo_del_profesional_a_cargo()
    {
        var (prof, pac, caso, _) = Escenario();
        var ajeno = _d.Profesional(_d.Especialidad(), nombre: "Ajeno " + _d.Marca);

        Assert.Equal(HttpStatusCode.NotFound, (await ComoProfesional(ajeno).GetAsync($"/casos/{caso}/precarga-alta")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ComoPaciente(pac).GetAsync($"/casos/{caso}/precarga-alta")).StatusCode);
    }

    // ───────────── El alta ─────────────

    [FactConBaseDeAltaEstructurada]
    public async Task El_alta_guarda_el_informe_en_la_historia_clinica_ligado_a_la_sesion_y_crea_los_dos_controles()
    {
        var (prof, pac, caso, sesion) = Escenario();

        var r = await ComoProfesional(prof).PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("En alta", EstadoCaso(caso));
        var alta = DatosEmociones.Filas("SELECT Informe, InformeEstructurado, IdEvolucion, IdSesion, ControlesPrevistos FROM AltaClinica WHERE IdCaso = @c", ("@c", caso)).Single();
        Assert.Contains("2. DIAGNÓSTICO O HIPÓTESIS DIAGNÓSTICA", (string)alta[0]!);
        Assert.Contains("Trastorno de ansiedad", (string)alta[1]!);
        Assert.Equal(sesion, (int)alta[3]!);
        Assert.Equal(2, (int)alta[4]!);
        var evolucion = (int)alta[2]!;
        // Quedó en la historia clínica de ESE paciente, y la sesión de cierre apunta a la misma evolución.
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Evolucion e JOIN HistoriaClinica h ON h.IdHistoria = e.IdHistoria WHERE e.IdEvolucion = @e AND h.IdPaciente = @p AND e.IdProfesional = @m",
            ("@e", evolucion), ("@p", pac), ("@m", prof)));
        Assert.Equal(evolucion, DatosDePrueba.Escalar("SELECT IdEvolucion FROM SesionClinica WHERE IdSesion = @s", ("@s", sesion)));
        Assert.Contains("INFORME DE ALTA", (string)DatosDePrueba.Escalar("SELECT Observaciones FROM Evolucion WHERE IdEvolucion = @e", ("@e", evolucion))!);
        // Los dos controles son recordatorios para el profesional, NO turnos.
        Assert.Equal(2, Controles(caso, "Pendiente"));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdPaciente = @p AND CreadoPor <> N'paciente'", ("@p", pac)));
        // Auditoría solo con ids: nunca el contenido clínico.
        var detalle = (string)DatosDePrueba.Escalar("SELECT TOP 1 Detalle FROM LogAuditoria WHERE Accion = N'Alta clínica confirmada' AND Entidad = N'AltaClinica' ORDER BY IdLog DESC")!;
        Assert.DoesNotContain("ansiedad", detalle, StringComparison.OrdinalIgnoreCase);
    }

    [FactConBaseDeAltaEstructurada]
    public async Task Los_controles_se_pueden_quitar_o_cambiar_antes_de_confirmar()
    {
        var (prof, pac, caso, sesion) = Escenario();
        var (prof2, pac2, caso2, sesion2) = Escenario();
        var (prof3, pac3, caso3, sesion3) = Escenario();
        var personalizada = Fecha(Ahora.AddMonths(4).AddDays(3));

        await ComoProfesional(prof).PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion, new[] { Fecha(Ahora.AddMonths(6)) }));                  // se quitó uno
        await ComoProfesional(prof2).PostAsJsonAsync($"/casos/{caso2}/alta-clinica", Alta(sesion2, new string?[0]));                                    // se quitaron los dos
        await ComoProfesional(prof3).PostAsJsonAsync($"/casos/{caso3}/alta-clinica", Alta(sesion3, new[] { personalizada, Fecha(Ahora.AddMonths(6)) })); // se movió uno

        Assert.Equal(1, Controles(caso));
        Assert.Equal(0, Controles(caso2));
        Assert.Equal("En alta", EstadoCaso(caso2));
        Assert.Equal(new[] { personalizada, Fecha(Ahora.AddMonths(6)) },
            DatosEmociones.Filas("SELECT FechaControl FROM ControlSeguimiento WHERE IdCaso = @c ORDER BY Orden", ("@c", caso3)).Select(f => Fecha((DateTime)f[0]!)).ToArray());
    }

    [FactConBaseDeAltaEstructurada]
    public async Task El_alta_exige_confirmacion_y_las_secciones_obligatorias_y_si_falla_no_cambia_nada()
    {
        var (prof, pac, caso, sesion) = Escenario();
        var c = ComoProfesional(prof);

        var sinConfirmar = await c.PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion, confirma: false));
        var sinDiagnostico = await c.PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion, informe: Informe(diagnostico: " ")));
        var anterior = await c.PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion, new[] { Fecha(Ahora.AddDays(-1)) }));
        var repetidas = await c.PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion, new[] { Fecha(Ahora.AddMonths(3)), Fecha(Ahora.AddMonths(3)) }));

        Assert.Equal(HttpStatusCode.BadRequest, sinConfirmar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinDiagnostico.StatusCode);
        Assert.Contains("diagnóstico", (await Json(sinDiagnostico)).GetProperty("mensaje").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, anterior.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, repetidas.StatusCode);
        Assert.Equal("En atención", EstadoCaso(caso));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @c", ("@c", caso)));
        Assert.Equal(0, Controles(caso));
    }

    [FactConBaseDeAltaEstructurada]
    public async Task Confirmar_el_alta_dos_veces_no_duplica_el_informe_ni_los_controles()
    {
        var (prof, pac, caso, sesion) = Escenario();
        var c = ComoProfesional(prof);

        var r1 = await c.PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion));
        var r2 = await c.PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.True((await Json(r2)).GetProperty("repetido").GetBoolean());
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @c", ("@c", caso)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Evolucion e JOIN HistoriaClinica h ON h.IdHistoria = e.IdHistoria WHERE h.IdPaciente = @p AND e.Observaciones LIKE N'INFORME DE ALTA%'", ("@p", pac)));
        Assert.Equal(2, Controles(caso));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Accion = N'Caso pasa a En alta' AND Entidad = N'Caso' AND IdEntidad = @c", ("@c", caso)));
    }

    [FactConBaseDeAltaEstructurada]
    public async Task Solo_el_profesional_a_cargo_da_el_alta()
    {
        var (prof, pac, caso, sesion) = Escenario();
        var ajeno = _d.Profesional(_d.Especialidad(), nombre: "Ajeno " + _d.Marca);

        var r = await ComoProfesional(ajeno).PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ComoPaciente(pac).PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion))).StatusCode);
        Assert.Equal("En atención", EstadoCaso(caso));
    }

    // ───────────── Avisos de control ─────────────

    private async Task<(int Prof, int Pac, int Caso, int Control)> ConUnControlCercano()
    {
        var (prof, pac, caso, sesion) = Escenario();
        var r = await ComoProfesional(prof).PostAsJsonAsync($"/casos/{caso}/alta-clinica", Alta(sesion));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var primero = Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdControlSeguimiento FROM ControlSeguimiento WHERE IdCaso = @c AND Orden = 1", ("@c", caso)));
        DatosDePrueba.Ejecutar("UPDATE ControlSeguimiento SET FechaControl = @f WHERE IdControlSeguimiento = @i", ("@f", Ahora.Date.AddDays(7)), ("@i", primero));
        return (prof, pac, caso, primero);
    }

    [FactConBaseDeAltaEstructurada]
    public async Task Al_acercarse_la_fecha_avisa_al_profesional_y_a_recepcion_una_sola_vez_y_solo_el_control_cercano()
    {
        var (prof, pac, caso, control) = await ConUnControlCercano();

        var primera = AltaEstructuradaDAO.EvaluarControlesCercanos(Ahora, prof);
        var segunda = AltaEstructuradaDAO.EvaluarControlesCercanos(Ahora);                 // la pasada del servicio de fondo
        await ComoProfesional(prof).GetAsync($"/profesionales/{prof}/seguimiento");        // y la consulta de la campanita

        Assert.Equal(1, primera);
        Assert.Equal(0, segunda);
        Assert.Equal(1, Avisos("Profesional"));
        Assert.Equal(1, Avisos("Administrador"));
        Assert.Equal(1, Controles(caso, "Avisado"));
        Assert.Equal(1, Controles(caso, "Pendiente"));   // el de 6 meses sigue esperando
    }

    [FactConBaseDeAltaEstructurada]
    public async Task El_control_avisado_aparece_en_la_campanita_y_se_resuelve_con_auditoria_e_idempotencia()
    {
        var (prof, pac, caso, control) = await ConUnControlCercano();
        var c = ComoProfesional(prof);
        AltaEstructuradaDAO.EvaluarControlesCercanos(Ahora, prof);

        var campana = await Json(await c.GetAsync($"/profesionales/{prof}/seguimiento"));
        var r1 = await c.PostAsJsonAsync($"/controles/{control}/resolver", new { confirma = true });
        var r2 = await c.PostAsJsonAsync($"/controles/{control}/resolver", new { confirma = true });
        var ajeno = _d.Profesional(_d.Especialidad(), nombre: "Ajeno " + _d.Marca);

        Assert.Contains(campana.GetProperty("controles").EnumerateArray(), x => x.GetProperty("idControl").GetInt32() == control);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.True((await Json(r2)).GetProperty("repetido").GetBoolean());
        Assert.Equal(1, Controles(caso, "Resuelto"));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM LogAuditoria WHERE Accion = N'Control de seguimiento resuelto' AND IdEntidad = @i", ("@i", control)));
        Assert.Equal(HttpStatusCode.NotFound, (await ComoProfesional(ajeno).PostAsJsonAsync($"/controles/{control}/resolver", new { confirma = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/controles/{control}/resolver", new { })).StatusCode);
    }
}

// Las reglas puras del alta estructurada (sin base).
public class AltaEstructuradaReglasTests
{
    private static readonly DateTime Alta = new(2026, 11, 2);

    [Fact]
    public void Los_controles_por_defecto_son_a_los_3_y_6_meses()
    {
        Assert.Equal(new[] { new DateTime(2027, 2, 2), new DateTime(2027, 5, 2) }, AltaEstructuradaReglas.ControlesSugeridos(Alta));
    }

    [Fact]
    public void Los_controles_se_ordenan_y_se_rechazan_los_invalidos()
    {
        Assert.Null(AltaEstructuradaReglas.ValidarControles(new[] { "2027-05-02", "2027-02-02" }, Alta, out var ok));
        Assert.Equal(new[] { new DateTime(2027, 2, 2), new DateTime(2027, 5, 2) }, ok);
        Assert.NotNull(AltaEstructuradaReglas.ValidarControles(new[] { "2026-11-02" }, Alta, out _));                 // no posterior al alta
        Assert.NotNull(AltaEstructuradaReglas.ValidarControles(new[] { "2030-01-01" }, Alta, out _));                 // a más de dos años
        Assert.NotNull(AltaEstructuradaReglas.ValidarControles(new[] { "no-es-fecha" }, Alta, out _));
        Assert.NotNull(AltaEstructuradaReglas.ValidarControles(new[] { "2027-02-02", "2027-02-02" }, Alta, out _));   // repetido
        Assert.Null(AltaEstructuradaReglas.ValidarControles(null, Alta, out var vacio));
        Assert.Empty(vacio);
    }

    [Fact]
    public void El_informe_exige_las_secciones_clave_y_se_renderiza_con_encabezados()
    {
        var completo = new InformeAltaEstructurado("Motivo", "Dx", "Obj", "Grado", null, "Estado", null, "Recom", null);
        Assert.Null(AltaEstructuradaReglas.ValidarInforme(completo));
        Assert.NotNull(AltaEstructuradaReglas.ValidarInforme(completo with { Diagnostico = "" }));
        Assert.NotNull(AltaEstructuradaReglas.ValidarInforme(completo with { Recomendaciones = null }));
        Assert.NotNull(AltaEstructuradaReglas.ValidarInforme(completo with { Tecnicas = new string('x', 2501) }));
        var texto = AltaEstructuradaReglas.Renderizar(completo);
        Assert.Contains("1. MOTIVO DE CONSULTA", texto);
        Assert.DoesNotContain("TÉCNICAS", texto);   // las secciones opcionales vacías se omiten
    }
}

// Pantallas y scripts leídos como texto: lo que no puede cambiar sin que alguien lo note.
public class AusenciasAltaPantallasTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] ruta) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(ruta).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void La_campanita_del_profesional_muestra_solicitudes_ausentes_propuestas_y_controles_con_confirmacion()
    {
        var shell = Leer("PANTALLAS", "shared", "shell.js");
        Assert.Contains("/seguimiento", shell);
        Assert.Contains("\"Solicitud de turno\"", shell);
        Assert.Contains("-solicitud`", shell);
        Assert.Contains("\"Marcar como ausente\"", shell);
        Assert.Contains("\"Proponer abandono\"", shell);
        Assert.Contains("/propuestas-abandono/${p.idPropuesta}/confirmar", shell);
        Assert.Contains("/propuestas-abandono/${p.idPropuesta}/descartar", shell);
        Assert.Contains("\"Control de seguimiento\"", shell);
        // Marcar y confirmar siempre pasan por una confirmación de la persona.
        Assert.Matches(@"async function marcarAusente[\s\S]*?YS\.confirmar", shell);
        Assert.Matches(@"async function confirmarAbandono[\s\S]*?YS\.confirmar", shell);
    }

    [Fact]
    public void El_paciente_ve_la_solicitud_como_esperando_confirmacion_del_profesional()
    {
        var js = Leer("PANTALLAS", "Pantalla_Paciente", "js", "dashboard.js");
        Assert.Contains("Esperando confirmación del profesional", js);
    }

    [Fact]
    public void Las_migraciones_35_y_36_son_idempotentes_opcionales_y_tienen_su_deshacer()
    {
        foreach (var (numero, script, deshacer) in new[] { (35, "add_ausencias_alta.sql", "deshacer-migracion-35.sql"), (36, "add_solicitud_turno_vencimiento.sql", "deshacer-migracion-36.sql") })
        {
            var m = VerificadorEsquema.Migraciones.Single(x => x.Numero == numero);
            Assert.True(m.Opcional);
            Assert.Equal(script, m.Archivo);
            var sql = Leer("migrations", script);
            Assert.Contains($"WHERE Numero = {numero})", sql);
            Assert.Contains("BEGIN TRANSACTION", sql);
            Assert.Contains($"DELETE FROM dbo.VersionEsquema WHERE Numero = {numero}", Leer("docs", "produccion", deshacer));
        }
        Assert.Equal(36, VerificadorEsquema.VersionRequerida);
    }

    [Fact]
    public void La_sala_del_profesional_ofrece_marcar_como_ausente_con_confirmacion_y_la_tolerancia_viene_del_servidor()
    {
        var html = Leer("PANTALLAS", "Pantalla_Profesional", "sesion_video.html");
        Assert.Contains("btn-marcar-ausente", html);
        Assert.Contains("/ausencia`", html);
        Assert.Contains("/marcar-ausente`", html);
        Assert.Contains("Tu paciente todavía no se conectó", html);
        Assert.Matches(@"async function marcarAusenteDesdeSala[\s\S]*?confirmar", html);
        // El informe de alta estructurado: las secciones, la precarga y los controles editables.
        Assert.Contains("/precarga-alta", html);
        Assert.Contains("alta-diagnostico", html);
        Assert.Contains("alta-controles-lista", html);
        Assert.Contains("informeEstructurado", html);
    }
}
