using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Bloque F: el paciente entra a la sala desde 10 minutos antes aunque el profesional no haya hecho nada (ya no depende de que él «genere la sala»), en cualquier orden de llegada, y el
// paciente que espera cuenta como presente (sin aviso de ausencia). Fuera de la ventana (antes de tiempo o turno pasado) no se entra. Contra SQL Server de verdad con un reloj fijo.
[Trait("Categoria", "Integracion")]
public class SalaSinEsperarAlProfesionalTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private static readonly DateTime Dia = new(2031, 3, 4);                 // martes
    private static readonly TimeSpan Hora = new(10, 0, 0);
    private readonly List<VideoApp> _apps = new();

    public void Dispose() { foreach (var a in _apps) a.Dispose(); _d.Dispose(); }

    private (int Prof, int Pac, int Turno) Escenario(bool consentimiento = true)
    {
        var prof = _d.Profesional(_d.Especialidad(), nombre: "Prof " + _d.Marca);
        foreach (var dia in new[] { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes" }) _d.Agenda(prof, dia, "09:00", "18:00", 30);
        var pac = _d.Paciente("Paciente " + _d.Marca);
        _d.PacienteProfesional(pac, prof);
        if (consentimiento)
        {
            _d.ConsentimientoFirmado(pac);
            // El reloj de la prueba está en 2031: el consentimiento tiene que estar vigente en ese momento.
            DatosDePrueba.Ejecutar("UPDATE Consentimiento SET FechaFirma = @f WHERE IdPaciente = @p", ("@f", Dia.AddDays(-1)), ("@p", pac));
        }
        _d.UsuarioDePaciente(pac);
        _d.UsuarioDeProfesional(prof);
        _d.AlLimpiar("Turno", "IdPaciente", pac);
        var turno = _d.Turno(pac, prof, "Confirmado", Dia, Hora);   // sin CodigoSala: nadie «generó» la sala
        return (prof, pac, turno);
    }

    /// <summary>La aplicación con el reloj de la clínica fijo a esta hora del día del turno (el mismo reloj para el acceso y para la generación de la sala).</summary>
    private VideoApp App(TimeSpan hora)
    {
        var reloj = new RelojFijo(Dia + hora);
        var app = new VideoApp(reloj: reloj, repositorioReal: true, auditoriaReal: true, servicios: s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(reloj); });
        _apps.Add(app);
        return app;
    }

    private HttpClient Paciente(VideoApp app, int pac) => app.Cliente(_d.UsuarioDePaciente(pac), Roles.Paciente, idPaciente: pac);
    private HttpClient Profesional(VideoApp app, int prof) => app.Cliente(_d.UsuarioDeProfesional(prof), Roles.Profesional, idProfesional: prof);
    private static string? Sala(int turno) => DatosDePrueba.Escalar("SELECT CodigoSala FROM Turno WHERE IdTurno = @t", ("@t", turno)) as string;

    private static async Task<(HttpStatusCode Status, JsonElement Json)> Entrar(HttpClient c, int turno)
    {
        var r = await VideoApp.Acceder(c, "turno", turno);
        return (r.StatusCode, await VideoApp.Json(r));
    }

    [Fact]
    public async Task El_paciente_entra_10_minutos_antes_aunque_el_profesional_no_haya_generado_la_sala_y_el_profesional_despues_entra_a_la_misma()
    {
        var (prof, pac, turno) = Escenario();
        var app = App(new TimeSpan(9, 52, 0));
        Assert.Null(Sala(turno));

        var (statusPac, jsonPac) = await Entrar(Paciente(app, pac), turno);

        Assert.Equal(HttpStatusCode.OK, statusPac);
        var sala = Sala(turno);
        Assert.StartsWith($"SALA-{turno}-", sala);                  // la generó el servidor, una sola vez
        Assert.Equal(sala, jsonPac.GetProperty("sala").GetString());
        var (statusPro, jsonPro) = await Entrar(Profesional(app, prof), turno);
        Assert.Equal(HttpStatusCode.OK, statusPro);
        Assert.Equal(sala, jsonPro.GetProperty("sala").GetString()); // los dos en la misma sala
        Assert.Equal(sala, Sala(turno));
    }

    [Fact]
    public async Task Si_el_profesional_entra_primero_el_paciente_entra_a_la_misma_sala()
    {
        var (prof, pac, turno) = Escenario();
        var app = App(new TimeSpan(9, 55, 0));

        var (statusPro, jsonPro) = await Entrar(Profesional(app, prof), turno);
        var (statusPac, jsonPac) = await Entrar(Paciente(app, pac), turno);

        Assert.Equal(HttpStatusCode.OK, statusPro);
        Assert.Equal(HttpStatusCode.OK, statusPac);
        Assert.Equal(jsonPro.GetProperty("sala").GetString(), jsonPac.GetProperty("sala").GetString());
    }

    [Fact]
    public async Task Si_entran_en_simultaneo_se_genera_una_sola_sala_y_los_dos_la_comparten()
    {
        var (prof, pac, turno) = Escenario();
        var app = App(new TimeSpan(10, 0, 0));
        var cPac = Paciente(app, pac);
        var cPro = Profesional(app, prof);

        var resultados = await Task.WhenAll(Entrar(cPac, turno), Entrar(cPro, turno));

        Assert.All(resultados, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(resultados[0].Json.GetProperty("sala").GetString(), resultados[1].Json.GetProperty("sala").GetString());
        Assert.Equal(resultados[0].Json.GetProperty("sala").GetString(), Sala(turno));
    }

    [Fact]
    public async Task Cerrar_y_volver_a_entrar_no_cambia_la_sala()
    {
        var (_, pac, turno) = Escenario();
        var app = App(new TimeSpan(9, 58, 0));

        var primero = await Entrar(Paciente(app, pac), turno);
        var segundo = await Entrar(Paciente(app, pac), turno);

        Assert.True(segundo.Status == HttpStatusCode.OK, segundo.Json.ToString());
        Assert.Equal(primero.Json.GetProperty("sala").GetString(), segundo.Json.GetProperty("sala").GetString());
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM TurnoConexion WHERE IdTurno = @t AND Rol = N'Paciente'", ("@t", turno)));
    }

    [Fact]
    public async Task Fuera_de_la_ventana_no_se_entra_ni_se_genera_nada_mas_de_10_minutos_antes_ni_con_el_turno_pasado()
    {
        var (_, pac, turno) = Escenario();
        var temprano = await Entrar(Paciente(App(new TimeSpan(9, 30, 0)), pac), turno);
        var pasado = await Entrar(Paciente(App(new TimeSpan(11, 30, 0)), pac), turno);

        Assert.Equal(HttpStatusCode.Conflict, temprano.Status);
        Assert.Equal("aun-no-abre", temprano.Json.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Conflict, pasado.Status);
        Assert.Equal("ya-termino", pasado.Json.GetProperty("codigo").GetString());
        Assert.Null(Sala(turno));   // y no se generó ninguna sala
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM TurnoConexion WHERE IdTurno = @t", ("@t", turno)));
    }

    [Fact]
    public async Task Sin_el_consentimiento_firmado_no_se_genera_la_sala_y_un_turno_pendiente_o_ajeno_tampoco()
    {
        var (_, pacSin, turnoSin) = Escenario(consentimiento: false);
        var (profB, pacB, turnoB) = Escenario();
        var otroPac = _d.Paciente("Otro " + _d.Marca);
        var app = App(new TimeSpan(10, 0, 0));

        var sinConsentimiento = await Entrar(Paciente(app, pacSin), turnoSin);
        var ajeno = await Entrar(app.Cliente(_d.UsuarioDePaciente(otroPac), Roles.Paciente, idPaciente: otroPac), turnoB);

        Assert.Equal(HttpStatusCode.Conflict, sinConsentimiento.Status);
        Assert.Null(Sala(turnoSin));
        Assert.Equal(HttpStatusCode.NotFound, ajeno.Status);
        Assert.Null(Sala(turnoB));

        DatosDePrueba.Ejecutar("UPDATE Turno SET IdEstadoTurno = @e WHERE IdTurno = @t", ("@e", _d.EstadoTurno("Pendiente")), ("@t", turnoB));
        var pendiente = await Entrar(Paciente(app, pacB), turnoB);
        Assert.Equal(HttpStatusCode.Conflict, pendiente.Status);
        Assert.Null(Sala(turnoB));
    }

    [Fact]
    public async Task El_paciente_que_espera_cuenta_como_presente_y_no_dispara_el_aviso_de_ausencia_a_los_10_minutos()
    {
        var (prof, pac, turnoEspera) = Escenario();
        var (prof2, pac2, turnoAusente) = Escenario();
        var app = App(new TimeSpan(9, 58, 0));
        await Entrar(Paciente(app, pac), turnoEspera);   // el paciente 1 entra a esperar; el 2 nunca aparece

        new AusenciasDAO().EvaluarAvisosTempranos(Dia + new TimeSpan(10, 12, 0));

        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Tipo = N'ausencia-temprana' AND IdTurno = @t", ("@t", turnoEspera)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Tipo = N'ausencia-temprana' AND IdTurno = @t", ("@t", turnoAusente)));
        DatosDePrueba.Ejecutar("DELETE FROM NotificacionInterna WHERE IdTurno IN (@a, @b)", ("@a", turnoEspera), ("@b", turnoAusente));
        DatosDePrueba.Ejecutar("DELETE FROM AvisoSeguimiento WHERE Clave LIKE @c OR Clave LIKE @d", ("@c", $"%:turno:{turnoEspera}"), ("@d", $"%:turno:{turnoAusente}"));
    }

    [Fact]
    public async Task El_profesional_ve_al_paciente_esperando_hasta_que_entra()
    {
        var (prof, pac, turno) = Escenario();
        var app = App(new TimeSpan(9, 58, 0));
        Assert.Empty(AusenciasDAO.PacientesEsperando(new[] { turno }));

        await Entrar(Paciente(app, pac), turno);
        Assert.Contains(turno, AusenciasDAO.PacientesEsperando(new[] { turno }));

        var j = await VideoApp.Json(await Profesional(app, prof).GetAsync($"/salas/estado?ids={turno}"));
        Assert.True(j.GetProperty("salas")[0].GetProperty("pacienteEsperando").GetBoolean());
        var delPaciente = await VideoApp.Json(await Paciente(app, pac).GetAsync($"/salas/estado?ids={turno}"));
        Assert.False(delPaciente.GetProperty("salas")[0].GetProperty("pacienteEsperando").GetBoolean());   // ese dato es solo para el profesional

        await Entrar(Profesional(app, prof), turno);
        Assert.Empty(AusenciasDAO.PacientesEsperando(new[] { turno }));   // ya entró: deja de estar «esperando»
    }
}

// Pantallas leídas como texto: el paciente no depende de que el profesional genere la sala.
public class SalaSinEsperarPantallasTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] ruta) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(ruta).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void El_boton_del_paciente_se_habilita_con_la_sala_abierta_y_entra_por_el_turno_no_por_el_codigo()
    {
        var pp = Leer("PANTALLAS", "Pantalla_Paciente", "js", "primeros_pasos.js");
        Assert.DoesNotContain("todavía no generó la sala", pp);
        Assert.Contains("const habilitado = !!(s && s.habilitada);", pp);
        Assert.Contains("sesion_video.html?turno=", pp);
        Assert.Contains("Esperando a tu profesional", pp);
    }

    [Fact]
    public void El_profesional_ve_el_indicador_Esperando_en_sus_listas_de_hoy()
    {
        var js = Leer("PANTALLAS", "Pantalla_Profesional", "js", "nuevo_dashboard.js");
        Assert.Contains("chipEsperando", js);
        Assert.Contains("pacienteEsperando", js);
        Assert.Contains("Paciente en espera", Leer("PANTALLAS", "Pantalla_Profesional", "sesion_video.html"));
    }
}
