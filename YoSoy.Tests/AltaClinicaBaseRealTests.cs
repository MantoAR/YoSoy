using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

// El ALTA CLÍNICA asistida (POST /casos/{id}/alta-clinica) contra SQL Server de verdad y la aplicación completa, con datos propios marcados "[TEST-PR6]" que se borran al terminar. Si la base no tiene
// las migraciones 26 y 27, estas pruebas se OMITEN (no pasan en falso).
//
// Lo que se prueba: nada cambia sin la confirmación explícita; el alta guarda el informe y el plan de control y pasa el caso a «En alta» en una sola transacción, auditada e idempotente;
// NO inactiva al paciente ni a su usuario, ni toca sus consentimientos ni sus vínculos; el alta administrativa de siempre (reactivar una ficha) sigue igual.
[Trait("Categoria", "Integracion")]
[Trait("Migracion", "27")]
public class AltaClinicaBaseRealTests
{
    private readonly ITestOutputHelper _salida;
    public AltaClinicaBaseRealTests(ITestOutputHelper salida) { _salida = salida; }

    private static VideoApp App(Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? servicios = null) =>
        new(new Dictionary<string, string?> { ["Video:LimitePorMinuto"] = "1000" }, repositorioReal: true, auditoriaReal: true, servicios: servicios);

    private static HttpClient Profesional(VideoApp app, DatosEmociones d) => app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);

    private static string Hoy => DateTime.Today.ToString("yyyy-MM-dd");

    private static object Alta(bool? confirma = true, string? informe = "Informe de alta ficticio. [TEST-PR6]", string? fecha = null, int controles = 3, int? cada = 30, string? primero = null,
        int? idSesion = null, string? clave = null) => new
    {
        confirma, informe, fechaAlta = fecha, controlesPrevistos = controles, controlCadaDias = cada, primerControl = primero ?? DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"), idSesion, claveCierre = clave
    };

    private static async Task<(HttpResponseMessage R, JsonElement J)> Post(HttpClient c, string url, object cuerpo)
    {
        var r = await c.PostAsJsonAsync(url, cuerpo);
        return (r, await VideoApp.Json(r));
    }

    private static int Contar(string sql, params (string, object?)[] p) => Convert.ToInt32(DatosEmociones.Escalar(sql, p));

    // ───────────── Caso feliz ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task El_alta_confirmada_guarda_informe_y_plan_pasa_el_caso_a_En_alta_y_deja_la_auditoria()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var idCaso = c.IdCaso("Asignado");

        var (r, j) = await Post(Profesional(app, c.D), $"/casos/{idCaso}/alta-clinica", Alta());

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.False(j.GetProperty("repetido").GetBoolean());
        Assert.Equal("Asignado", j.GetProperty("caso").GetProperty("estadoAnterior").GetString());
        Assert.Equal("En alta", j.GetProperty("caso").GetProperty("estado").GetString());
        Assert.Equal("En alta", c.EstadoDelCaso(idCaso));
        var fila = DatosEmociones.Filas("SELECT Informe, ControlesPrevistos, ControlCadaDias, PrimerControl, FechaAlta, EstadoCasoAnterior, IdSesion FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)).Single();
        Assert.Equal("Informe de alta ficticio. [TEST-PR6]", fila[0]);
        Assert.Equal((3, 30), ((int)fila[1]!, (int)fila[2]!));
        Assert.Equal(DateTime.Today.AddDays(30), (DateTime)fila[3]!);
        Assert.Equal(DateTime.Today, (DateTime)fila[4]!);                     // la fecha de alta, por defecto hoy
        Assert.Equal("Asignado", fila[5]);
        Assert.Null(fila[6]);
        var alta = Assert.Single(c.Auditoria("Alta clínica confirmada"));
        var caso = Assert.Single(c.Auditoria("Caso pasa a En alta"));
        Assert.Contains($"Caso #{idCaso}: Asignado -> En alta", caso.Detalle);
        Assert.DoesNotContain("Informe de alta", alta.Detalle! + caso.Detalle);   // nunca el contenido clínico en la auditoría
    }

    [FactConBaseDeAltaDerivacion]
    public async Task El_alta_clinica_no_inactiva_al_paciente_ni_a_su_usuario_ni_toca_consentimientos_vinculos_o_turnos()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var idCaso = c.IdCaso("En atención");
        var turno = c.IdTurno;
        var antes = (
            Paciente: DatosEmociones.Escalar("SELECT Estado FROM Paciente WHERE IdPaciente = @P", ("@P", c.IdPaciente)),
            Usuario: DatosEmociones.Escalar("SELECT Estado FROM Usuario WHERE IdUsuario = @U", ("@U", c.D.IdUsuarioPaciente)),
            Vinculos: Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @M", ("@P", c.IdPaciente), ("@M", c.IdProfesional)),
            Turno: c.EstadoDelTurno(turno));

        var (r, _) = await Post(Profesional(app, c.D), $"/casos/{idCaso}/alta-clinica", Alta(controles: 0));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(antes.Paciente, DatosEmociones.Escalar("SELECT Estado FROM Paciente WHERE IdPaciente = @P", ("@P", c.IdPaciente)));    // sigue Activo
        Assert.Equal("Activo", antes.Paciente);
        Assert.Equal(antes.Usuario, DatosEmociones.Escalar("SELECT Estado FROM Usuario WHERE IdUsuario = @U", ("@U", c.D.IdUsuarioPaciente)));
        Assert.Equal(antes.Vinculos, Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @M", ("@P", c.IdPaciente), ("@M", c.IdProfesional)));
        Assert.Equal(antes.Turno, c.EstadoDelTurno(turno));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM ConsentimientoVideo WHERE IdPaciente = @P", ("@P", c.IdPaciente)));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM Consentimiento WHERE IdPaciente = @P", ("@P", c.IdPaciente)));
        // Sin controles, el plan queda vacío y coherente.
        var plan = DatosEmociones.Filas("SELECT ControlesPrevistos, ControlCadaDias, PrimerControl FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)).Single();
        Assert.Equal(0, (int)plan[0]!); Assert.Null(plan[1]); Assert.Null(plan[2]);
    }

    [FactConBaseDeAltaDerivacion]
    public async Task El_caso_en_alta_ya_no_cuenta_en_la_carga_del_profesional_para_nuevas_asignaciones()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var otro = c.IdCaso("Asignado");
        var idCaso = c.IdCaso("Asignado");
        var grupo = c.ConGrupo(idCaso);
        DatosEmociones.Ejecutar("INSERT INTO ProfesionalGrupo (IdProfesional, IdGrupo) VALUES (@M, @G)", ("@M", c.IdProfesional), ("@G", grupo));
        var cargaAntes = new AsignacionRepositorioDAO().ObtenerCandidatos(grupo).Single(x => x.IdProfesional == c.IdProfesional).CasosActivos;

        var (r, _) = await Post(Profesional(app, c.D), $"/casos/{idCaso}/alta-clinica", Alta());

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cargaDespues = new AsignacionRepositorioDAO().ObtenerCandidatos(grupo).Single(x => x.IdProfesional == c.IdProfesional).CasosActivos;
        Assert.Equal(2, cargaAntes);
        Assert.Equal(1, cargaDespues);                                       // solo queda el otro caso
        Assert.Equal("Asignado", c.EstadoDelCaso(otro));
    }

    // ───────────── Confirmación y validaciones: sin esto no cambia nada ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task Sin_confirmacion_explicita_no_cambia_nada()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var idCaso = c.IdCaso("Asignado");
        var cliente = Profesional(app, c.D);

        foreach (bool? confirma in new bool?[] { null, false })
        {
            var (r, j) = await Post(cliente, $"/casos/{idCaso}/alta-clinica", Alta(confirma));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("falta-confirmacion", j.GetProperty("codigo").GetString());
        }
        Assert.Equal("Asignado", c.EstadoDelCaso(idCaso));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)));
        Assert.Empty(c.Auditoria("Alta clínica confirmada"));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Los_datos_incompletos_o_incoherentes_se_rechazan_con_un_mensaje_claro_y_no_cambian_nada()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var idCaso = c.IdCaso("Asignado");
        var cliente = Profesional(app, c.D);
        var manana = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        var ayer = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");

        var casos = new (object Cuerpo, string Contiene)[]
        {
            (Alta(informe: ""), "informe de alta"),
            (Alta(informe: "   "), "informe de alta"),
            (Alta(fecha: manana), "posterior a hoy"),
            (Alta(fecha: "no-es-una-fecha"), "fecha de alta"),
            (Alta(controles: 13), "entre 0 y 12"),
            (Alta(controles: -1), "entre 0 y 12"),
            (Alta(controles: 3, cada: null), "cada cuántos días"),
            (Alta(controles: 3, cada: 3), "cada cuántos días"),
            (Alta(controles: 3, cada: 400), "cada cuántos días"),
            (Alta(controles: 3, cada: 30, primero: "mañana"), "primer contacto"),
            (Alta(controles: 3, cada: 30, primero: ayer), "anterior a la fecha de alta"),
        };
        foreach (var (cuerpo, contiene) in casos)
        {
            var (r, j) = await Post(cliente, $"/casos/{idCaso}/alta-clinica", cuerpo);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("datos-invalidos", j.GetProperty("codigo").GetString());
            Assert.Contains(contiene, j.GetProperty("mensaje").GetString());
        }
        Assert.Equal("Asignado", c.EstadoDelCaso(idCaso));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task La_fecha_de_alta_se_puede_editar_hacia_atras()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var idCaso = c.IdCaso("Asignado");
        var antes = DateTime.Today.AddDays(-2);

        var (r, j) = await Post(Profesional(app, c.D), $"/casos/{idCaso}/alta-clinica", Alta(fecha: antes.ToString("yyyy-MM-dd"), primero: Hoy));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(antes.ToString("yyyy-MM-dd"), j.GetProperty("fechaAlta").GetString());
    }

    // ───────────── Idempotencia ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task Confirmar_dos_veces_no_duplica_nada_ni_vuelve_a_auditar_ni_pisa_lo_guardado()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var idCaso = c.IdCaso("Asignado");
        var cliente = Profesional(app, c.D);

        var (r1, j1) = await Post(cliente, $"/casos/{idCaso}/alta-clinica", Alta());
        var (r2, j2) = await Post(cliente, $"/casos/{idCaso}/alta-clinica", Alta(informe: "OTRO informe que no debe pisar al primero"));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.True(j2.GetProperty("repetido").GetBoolean());
        Assert.Equal(j1.GetProperty("idAltaClinica").GetInt32(), j2.GetProperty("idAltaClinica").GetInt32());
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)));
        Assert.Equal("Informe de alta ficticio. [TEST-PR6]", (string)DatosEmociones.Escalar("SELECT Informe FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso))!);
        Assert.Single(c.Auditoria("Alta clínica confirmada"));
        Assert.Single(c.Auditoria("Caso pasa a En alta"));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Dos_confirmaciones_simultaneas_dejan_una_sola_alta()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var idCaso = c.IdCaso("Asignado");
        var cliente = Profesional(app, c.D);

        var resultados = await Task.WhenAll(Post(cliente, $"/casos/{idCaso}/alta-clinica", Alta()), Post(cliente, $"/casos/{idCaso}/alta-clinica", Alta()));

        Assert.All(resultados, x => Assert.True(x.R.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, x.R.StatusCode + " " + x.J));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)));
        Assert.Single(c.Auditoria("Caso pasa a En alta"));
    }

    // ───────────── Estados del caso ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task Solo_un_caso_asignado_o_en_atencion_admite_el_alta()
    {
        using var app = App();
        foreach (var estado in new[] { "Nuevo", "Derivado", "Cerrado", "En alta" })
        {
            using var c = new DatosCierre(_salida);
            var idCaso = c.IdCaso(estado);

            var (r, j) = await Post(Profesional(app, c.D), $"/casos/{idCaso}/alta-clinica", Alta());

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("estado-no-permitido", j.GetProperty("codigo").GetString());
            Assert.Equal(estado, c.EstadoDelCaso(idCaso));
            Assert.Equal(0, Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)));
        }
        using var cc = new DatosCierre(_salida);
        var cerrado = cc.IdCaso("Cerrado");
        var (_, jc) = await Post(Profesional(app, cc.D), $"/casos/{cerrado}/alta-clinica", Alta());
        Assert.Contains("cerrado", jc.GetProperty("mensaje").GetString());
    }

    // ───────────── Permisos ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task Solo_el_profesional_a_cargo_del_caso_puede_dar_el_alta()
    {
        using var a = new DatosCierre(_salida);
        using var b = new DatosCierre(_salida);
        using var app = App();
        var idCaso = a.IdCaso("Asignado");
        var url = $"/casos/{idCaso}/alta-clinica";

        var (rOtro, jOtro) = await Post(Profesional(app, b.D), url, Alta());
        Assert.Equal(HttpStatusCode.Forbidden, rOtro.StatusCode);
        Assert.Equal("caso-ajeno", jOtro.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Cliente(a.D.IdUsuarioPaciente, Roles.Paciente, idPaciente: a.IdPaciente).PostAsJsonAsync(url, Alta())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Cliente(a.D.IdUsuarioConfigurador, Roles.Configurador).PostAsJsonAsync(url, Alta())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Cliente(a.D.IdUsuarioConfigurador, Roles.Crm).PostAsJsonAsync(url, Alta())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.SinSesion().PostAsJsonAsync(url, Alta())).StatusCode);
        var (rNo, jNo) = await Post(Profesional(app, a.D), "/casos/2000000000/alta-clinica", Alta());
        Assert.Equal(HttpStatusCode.NotFound, rNo.StatusCode);
        Assert.Equal("caso-inexistente", jNo.GetProperty("codigo").GetString());

        Assert.Equal("Asignado", a.EstadoDelCaso(idCaso));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)));
    }

    [FactConBaseDeAltaDerivacion]
    public async Task El_informe_guardado_lo_lee_el_profesional_a_cargo_con_auditoria_y_nadie_mas()
    {
        using var a = new DatosCierre(_salida);
        using var b = new DatosCierre(_salida);
        using var app = App();
        var idCaso = a.IdCaso("Asignado");
        await Post(Profesional(app, a.D), $"/casos/{idCaso}/alta-clinica", Alta());

        var rLee = await Profesional(app, a.D).GetAsync($"/casos/{idCaso}/alta-clinica");
        var j = await VideoApp.Json(rLee);
        Assert.Equal(HttpStatusCode.OK, rLee.StatusCode);
        Assert.Equal("Informe de alta ficticio. [TEST-PR6]", j.GetProperty("informe").GetString());
        Assert.Equal(3, j.GetProperty("plan").GetProperty("controles").GetInt32());
        Assert.Single(a.Auditoria("Consulta de alta clínica"));
        Assert.Equal(HttpStatusCode.NotFound, (await Profesional(app, b.D).GetAsync($"/casos/{idCaso}/alta-clinica")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Cliente(a.D.IdUsuarioPaciente, Roles.Paciente, idPaciente: a.IdPaciente).GetAsync($"/casos/{idCaso}/alta-clinica")).StatusCode);
    }

    // ───────────── Enlace con la sesión del cierre ─────────────

    private static async Task<int> SesionDeCierre(HttpClient cliente, DatosCierre c, string tipo, Guid clave)
    {
        var r = await cliente.PostAsJsonAsync("/sesiones-clinicas", new { idPaciente = c.IdPaciente, notasEvolucion = "n [TEST-PR6]", tipoCierre = tipo, claveCierre = clave.ToString(), confirmaRealizacion = false });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await VideoApp.Json(r)).GetProperty("idSesion").GetInt32();
    }

    [FactConBaseDeAltaDerivacion]
    public async Task El_alta_se_enlaza_con_la_sesion_del_cierre_por_su_clave_o_su_id()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var cliente = Profesional(app, c.D);
        var clave = Guid.NewGuid();
        var idSesion = await SesionDeCierre(cliente, c, "Alta", clave);
        var idCaso = c.IdCaso("Asignado");

        var (r, j) = await Post(cliente, $"/casos/{idCaso}/alta-clinica", Alta(clave: clave.ToString()));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(idSesion, j.GetProperty("idSesion").GetInt32());
        Assert.Equal(idSesion, Convert.ToInt32(DatosEmociones.Escalar("SELECT IdSesion FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso))));

        // Por id de sesión también.
        var idCaso2 = c.IdCaso("En atención");
        var otra = await SesionDeCierre(cliente, c, "Alta", Guid.NewGuid());
        var (r2, j2) = await Post(cliente, $"/casos/{idCaso2}/alta-clinica", Alta(idSesion: otra));
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.Equal(otra, j2.GetProperty("idSesion").GetInt32());
    }

    [FactConBaseDeAltaDerivacion]
    public async Task Una_sesion_de_otro_tipo_de_otro_paciente_o_de_otro_profesional_no_sirve_y_no_cambia_nada()
    {
        using var a = new DatosCierre(_salida);
        using var b = new DatosCierre(_salida);
        using var app = App();
        var cliente = Profesional(app, a.D);
        var idCaso = a.IdCaso("Asignado");
        var sesionSeguimiento = await SesionDeCierre(cliente, a, "Seguimiento", Guid.NewGuid());
        var sesionDeB = await SesionDeCierre(Profesional(app, b.D), b, "Alta", Guid.NewGuid());

        var (r1, j1) = await Post(cliente, $"/casos/{idCaso}/alta-clinica", Alta(idSesion: sesionSeguimiento));
        var (r2, j2) = await Post(cliente, $"/casos/{idCaso}/alta-clinica", Alta(idSesion: sesionDeB));
        var (r3, j3) = await Post(cliente, $"/casos/{idCaso}/alta-clinica", Alta(clave: "no-es-un-guid"));

        Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);
        Assert.Equal("sesion-no-es-del-tipo", j1.GetProperty("codigo").GetString());
        Assert.Equal("sesion-no-coincide", j2.GetProperty("codigo").GetString());
        Assert.Equal("sesion-no-coincide", j3.GetProperty("codigo").GetString());
        Assert.Equal("Asignado", a.EstadoDelCaso(idCaso));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)));
    }

    // ───────────── Atomicidad ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task Si_falla_el_cambio_de_estado_del_caso_no_queda_ni_el_alta_ni_la_auditoria_a_medias()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var idCaso = c.IdCaso("Asignado");
        var trigger = "TR_prueba_alta_" + Guid.NewGuid().ToString("N")[..8];
        DatosEmociones.Ejecutar($"CREATE TRIGGER {trigger} ON Caso AFTER UPDATE AS IF EXISTS (SELECT 1 FROM inserted WHERE IdCaso = {idCaso}) THROW 51000, 'falla provocada por la prueba', 1;");
        try
        {
            var (r, _) = await Post(Profesional(app, c.D), $"/casos/{idCaso}/alta-clinica", Alta());

            Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
            Assert.Equal("Asignado", c.EstadoDelCaso(idCaso));
            Assert.Equal(0, Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)));
            Assert.Empty(c.Auditoria("Alta clínica confirmada"));
        }
        finally { DatosEmociones.Ejecutar($"DROP TRIGGER {trigger}"); }

        var (r2, _) = await Post(Profesional(app, c.D), $"/casos/{idCaso}/alta-clinica", Alta());   // resuelta la causa, se puede reintentar
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
    }

    // ───────────── El alta administrativa de siempre no cambió ─────────────

    [FactConBaseDeAltaDerivacion]
    public async Task El_endpoint_de_siempre_que_reactiva_una_ficha_sigue_igual_y_no_toca_el_caso()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var idCaso = c.IdCaso("Asignado");
        DatosEmociones.Ejecutar("UPDATE Paciente SET Estado = 'Inactivo' WHERE IdPaciente = @P", ("@P", c.IdPaciente));

        var r = await app.Cliente(c.D.IdUsuarioConfigurador, Roles.Configurador).PutAsync($"/pacientes/{c.IdPaciente}/alta", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Activo", (string)DatosEmociones.Escalar("SELECT Estado FROM Paciente WHERE IdPaciente = @P", ("@P", c.IdPaciente))!);
        Assert.Equal("Asignado", c.EstadoDelCaso(idCaso));                                                  // el alta administrativa no es un alta clínica
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM AltaClinica WHERE IdCaso = @C", ("@C", idCaso)));
        // Y al revés: el profesional no puede usar el alta clínica para reactivar nada (ni el endpoint viejo).
        Assert.Equal(HttpStatusCode.Forbidden, (await Profesional(app, c.D).PutAsync($"/pacientes/{c.IdPaciente}/alta", null)).StatusCode);
    }

    [Fact]
    public void Sin_la_migracion_27_el_alta_responde_409_con_su_codigo_y_el_endpoint_viejo_tiene_su_aviso()
    {
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        var endpoint = File.ReadAllText(Path.Combine(raiz, "Endpoints", "AltaDerivacionEndpoints.cs"));
        Assert.Contains("if (!dao.Migracion27Disponible()) return Falla(AltaDerivacionReglas.FaltaMigracion27);", endpoint);
        Assert.Contains("falta-migracion-27", File.ReadAllText(Path.Combine(raiz, "Services", "AltaDerivacionReglas.cs")));
        var program = File.ReadAllText(Path.Combine(raiz, "Program.cs")).Replace("\r\n", "\n");
        Assert.Contains("// ALTA ADMINISTRATIVA: solo reactiva la ficha de un paciente dado de baja. No es el alta CLÍNICA", program);
        Assert.Contains("app.MapAltaDerivacion();", program);
        // El alta clínica no llama al servicio que inactiva ni reactiva fichas.
        Assert.DoesNotContain("BajaFichasDAO", File.ReadAllText(Path.Combine(raiz, "DAOs", "AltaDerivacionDAO.cs")));
        Assert.DoesNotContain("BajaFichasDAO", endpoint);
    }
}
