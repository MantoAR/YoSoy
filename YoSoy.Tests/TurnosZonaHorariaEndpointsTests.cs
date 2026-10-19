using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// La aplicación REAL (Program.cs, JWT, TurnoReglasService, los endpoints) con la base de turnos y el notificador en
// memoria, y un servidor "en UTC" con la hora fija: sin base de datos. Prueba que el cableado de verdad decide con la
// zona horaria EXPLÍCITA de la clínica y no con la del sistema operativo. El escenario es el de Escenario: jueves
// 24/09/2026, paciente 1, profesional 10 con agenda de lunes a viernes de 09:00 a 12:00 cada 30 minutos.
public sealed class TurnosApp : IDisposable
{
    public const string ClaveJwt = "clave-de-prueba-para-los-turnos-que-no-es-ninguna-real-0123456789";
    public const int UsuarioPaciente = 31, IdPaciente = 1;
    public const int UsuarioProfesional = 21, IdProfesional = 10;

    public readonly Escenario Esc = new();
    public readonly FakeNotificador Notificador = new();
    public readonly RelojFijo Servidor;                  // el reloj del servidor de prueba: en UTC, como una VPS sin configurar
    public readonly RelojDeLaClinica Clinica;
    public readonly WebApplicationFactory<Program> Factory;

    public TurnosApp(DateTime ahoraUtc, bool soloLaConfiguracion = false, Dictionary<string, string?>? config = null)
    {
        Servidor = new RelojFijo(ahoraUtc);
        Clinica = new RelojDeLaClinica(ZonaHorariaDeLaClinica.Elegir(null), Servidor);
        var ajustes = new Dictionary<string, string?> { ["Jwt:Key"] = ClaveJwt };
        if (config is not null) foreach (var (clave, valor) in config) ajustes[clave] = valor;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(ajustes));
            if (soloLaConfiguracion) return;             // el reloj, el repositorio y el notificador son los de Program.cs
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(Clinica);
                s.RemoveAll<ITurnoRepositorio>();
                s.AddSingleton<ITurnoRepositorio>(new FakeTurnoRepositorio(Esc.Db));
                s.RemoveAll<INotificadorTurnos>();
                s.AddSingleton<INotificadorTurnos>(Notificador);
            });
        });
    }

    public HttpClient Cliente(int idUsuario, string rol, int? idPaciente = null, int? idProfesional = null)
    {
        var jwt = Factory.Services.GetRequiredService<JwtEmisorService>().Emitir(idUsuario, rol, idPaciente, idProfesional);
        var c = Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return c;
    }

    public HttpClient Paciente(int usuario = UsuarioPaciente, int idPaciente = IdPaciente) =>
        Cliente(usuario, Roles.Paciente, idPaciente: idPaciente);
    public HttpClient Profesional() => Cliente(UsuarioProfesional, Roles.Profesional, idProfesional: IdProfesional);

    public static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(texto)) return default;
        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.Clone();
    }

    // Lo que manda la pantalla del paciente: el mediodía local de la fecha, pasado a UTC, y la hora aparte.
    public static Task<HttpResponseMessage> Solicitar(HttpClient cliente, string fechaIsoUtc, string hora, int idProfesional = IdProfesional) =>
        cliente.PostAsJsonAsync("/turnos/solicitar", new { idProfesional, fecha = fechaIsoUtc, hora, modalidad = "Virtual", motivo = "[TEST] zona horaria" });

    public static Task<HttpResponseMessage> ProfesionalSolicita(HttpClient cliente, string fecha, string? hora, bool urgente = false,
        int idPaciente = IdPaciente) =>
        cliente.PostAsJsonAsync("/turnos/profesional-solicitar",
            new { idPaciente, idProfesional = IdProfesional, fecha, hora, motivo = "[TEST] zona horaria", esUrgente = urgente });

    public void Dispose() => Factory.Dispose();
}

public class TurnosZonaHorariaEndpointsTests
{
    private const string MedioDiaDelJueves = "2026-09-24T15:00:00.000Z";    // el mediodía de Argentina, pasado a UTC
    private const string MedioDiaDelViernes = "2026-09-25T15:00:00.000Z";

    // 12:30 UTC = jueves 09:30 en la clínica.
    private static readonly DateTime Jueves0930EnLaClinica = new(2026, 9, 24, 12, 30, 0);

    // ---------- "Ese horario ya pasó" con la zona de la clínica ----------

    [Fact]
    public async Task Un_horario_de_hoy_que_todavia_no_paso_en_la_clinica_se_acepta_aunque_el_servidor_este_en_UTC()
    {
        using var app = new TurnosApp(Jueves0930EnLaClinica);

        var r = await TurnosApp.Solicitar(app.Paciente(), MedioDiaDelJueves, "10:00:00");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var turno = Assert.Single(app.Esc.Db.Turnos);
        Assert.Equal(new DateTime(2026, 9, 24), turno.Fecha);
        Assert.Equal(new TimeSpan(10, 0, 0), turno.Hora);
        Assert.Equal(TurnosApp.IdPaciente, turno.IdPaciente);
    }

    [Fact]
    public async Task Un_horario_que_ya_paso_en_la_clinica_da_409_con_el_motivo()
    {
        using var app = new TurnosApp(Jueves0930EnLaClinica);

        var r = await TurnosApp.Solicitar(app.Paciente(), MedioDiaDelJueves, "09:00:00");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var j = await TurnosApp.Json(r);
        Assert.Equal("RN-01", j.GetProperty("codigo").GetString());
        Assert.Equal("Ese horario ya pasó.", j.GetProperty("mensaje").GetString());
        Assert.Empty(app.Esc.Db.Turnos);
    }

    // Pasadas las 21 de la clínica en UTC ya es el día siguiente. Con una agenda vespertina el turno de las 22:30 de
    // hoy se puede pedir; con la zona del servidor se rechazaba como pasado.
    [Fact]
    public async Task De_noche_el_dia_es_el_de_la_clinica_aunque_en_UTC_ya_sea_el_siguiente()
    {
        using var app = new TurnosApp(new DateTime(2026, 9, 25, 1, 15, 0));            // jueves 22:15 en la clínica
        app.Esc.Db.Agenda.Add(new Agenda
        {
            IdProfesional = TurnosApp.IdProfesional, Dia = "jueves", HoraInicio = new(22, 0, 0), HoraFin = new(23, 59, 59),
            Disponible = true, IntervaloMinutos = 30
        });
        var paciente = app.Paciente();

        var pasado = await TurnosApp.Solicitar(paciente, MedioDiaDelJueves, "22:00:00");
        var futuro = await TurnosApp.Solicitar(paciente, MedioDiaDelJueves, "22:30:00");

        Assert.Equal(HttpStatusCode.Conflict, pasado.StatusCode);
        Assert.Equal(HttpStatusCode.Created, futuro.StatusCode);
    }

    [Fact]
    public async Task El_profesional_que_propone_un_horario_pasado_recibe_el_mismo_409()
    {
        using var app = new TurnosApp(Jueves0930EnLaClinica);

        var r = await TurnosApp.ProfesionalSolicita(app.Profesional(), MedioDiaDelJueves, "09:00:00");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("Ese horario ya pasó.", (await TurnosApp.Json(r)).GetProperty("mensaje").GetString());
        Assert.Empty(app.Esc.Db.Turnos);
    }

    // ---------- La fecha del turno se lee como está escrita ----------

    [Fact]
    public async Task El_profesional_propone_un_turno_futuro_con_la_fecha_ISO_que_manda_el_navegador()
    {
        using var app = new TurnosApp(Jueves0930EnLaClinica);

        var r = await TurnosApp.ProfesionalSolicita(app.Profesional(), MedioDiaDelViernes, "09:30:00");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var turno = Assert.Single(app.Esc.Db.Turnos);
        Assert.Equal(new DateTime(2026, 9, 25), turno.Fecha);
        Assert.Equal(new TimeSpan(9, 30, 0), turno.Hora);
        Assert.Equal(new[] { "TurnoAgendado" }, app.Notificador.Eventos);
    }

    // Lo que manda "Proponer el primer turno": la fecha de un <input type="date"> a secas.
    [Fact]
    public async Task El_profesional_propone_el_primer_turno_con_una_fecha_a_secas()
    {
        using var app = new TurnosApp(Jueves0930EnLaClinica);

        var r = await TurnosApp.ProfesionalSolicita(app.Profesional(), "2026-09-25", "10:00:00");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var turno = Assert.Single(app.Esc.Db.Turnos);
        Assert.Equal(new DateTime(2026, 9, 25), turno.Fecha);
        Assert.Equal(new TimeSpan(10, 0, 0), turno.Hora);
    }

    [Theory]
    [InlineData("mañana", "10:00:00")]
    [InlineData("2026-09-25", "a las diez")]
    [InlineData("2026-09-25", "25:00:00")]
    [InlineData("", "10:00:00")]
    [InlineData("2026-09-25", null)]
    public async Task Una_fecha_o_una_hora_ilegible_da_400_y_no_un_error_del_servidor(string fecha, string? hora)
    {
        using var app = new TurnosApp(Jueves0930EnLaClinica);

        var r = await TurnosApp.ProfesionalSolicita(app.Profesional(), fecha, hora);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("fecha o la hora", (await TurnosApp.Json(r)).GetProperty("mensaje").GetString());
        Assert.Empty(app.Esc.Db.Turnos);
    }

    // ---------- Cita urgente: "ahora" lo dice el servidor ----------

    [Fact]
    public async Task La_cita_urgente_toma_la_hora_de_la_clinica_y_no_la_que_manda_la_PC_del_profesional()
    {
        using var app = new TurnosApp(new DateTime(2026, 9, 24, 12, 30, 45).AddTicks(6780000));   // 09:30:45,678 en la clínica

        // Una PC con la fecha y la hora cualquiera (mal configurada, o en otra zona).
        var r = await TurnosApp.ProfesionalSolicita(app.Profesional(), "2000-01-01", "00:00:00", urgente: true);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var turno = Assert.Single(app.Esc.Db.Turnos);
        Assert.Equal(new DateTime(2026, 9, 24), turno.Fecha);
        Assert.Equal(new TimeSpan(9, 30, 45), turno.Hora);
    }

    [Fact]
    public async Task La_cita_urgente_no_necesita_que_la_fecha_ni_la_hora_sean_legibles()
    {
        using var app = new TurnosApp(Jueves0930EnLaClinica);

        var r = await TurnosApp.ProfesionalSolicita(app.Profesional(), "cualquier cosa", null, urgente: true);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(new TimeSpan(9, 30, 0), Assert.Single(app.Esc.Db.Turnos).Hora);
    }
}

// El reloj que arma Program.cs de verdad (sin reemplazarlo): de dónde sale la zona.
public class RelojDeLaAplicacionTests
{
    private static TimeProvider Reloj(Dictionary<string, string?>? config = null)
    {
        using var app = new TurnosApp(new DateTime(2031, 3, 4, 12, 0, 0), soloLaConfiguracion: true, config: config);
        return app.Factory.Services.GetRequiredService<TimeProvider>();
    }

    private static readonly DateTimeOffset UnInstante = new(2031, 3, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Por_defecto_el_reloj_de_la_aplicacion_es_el_de_la_clinica_en_Argentina()
    {
        var reloj = Reloj();

        Assert.IsType<RelojDeLaClinica>(reloj);
        Assert.Equal(TimeSpan.FromHours(-3), reloj.LocalTimeZone.GetUtcOffset(UnInstante));
        Assert.Equal(TimeSpan.FromHours(-3), reloj.GetLocalNow().Offset);   // no depende de la zona de esta máquina
    }

    [Fact]
    public void Turnos_ZonaHoraria_cambia_la_zona_de_toda_la_aplicacion()
    {
        var reloj = Reloj(new() { ["Turnos:ZonaHoraria"] = "UTC" });

        Assert.IsType<RelojDeLaClinica>(reloj);
        Assert.Equal(TimeSpan.Zero, reloj.LocalTimeZone.GetUtcOffset(UnInstante));
        Assert.Equal(TimeSpan.Zero, reloj.GetLocalNow().Offset);
    }

    [Fact]
    public void Video_ZonaHoraria_ya_configurada_sigue_siendo_la_zona_de_la_clinica()
    {
        var reloj = Reloj(new() { ["Video:ZonaHoraria"] = "UTC" });

        Assert.Equal(TimeSpan.Zero, reloj.LocalTimeZone.GetUtcOffset(UnInstante));
    }

    [Fact]
    public void Una_Turnos_ZonaHoraria_inexistente_impide_que_la_aplicacion_arranque()
    {
        var ex = Record.Exception(() => Reloj(new() { ["Turnos:ZonaHoraria"] = "Marte/Olympus_Mons" }));

        Assert.NotNull(ex);
        Assert.Contains("Turnos:ZonaHoraria", Mensajes(ex!));
    }

    private static string Mensajes(Exception ex)
    {
        var sb = new StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException!) sb.AppendLine(e.Message);
        return sb.ToString();
    }
}

// "Próximo turno": con la base real. "Hoy" lo pone el reloj de la clínica (la aplicación), no el GETDATE() del servidor
// de base de datos. Crea y borra sus propias filas (prefijo xtest); las fechas son de 2031, lejos de cualquier dato real.
[Trait("Categoria", "Integracion")]
public class ProximoTurnoZonaHorariaIntegrationTests : IDisposable
{
    private readonly string _marca = "xtest" + Guid.NewGuid().ToString("N")[..10];
    private readonly int _idEspecialidad, _idProfesional, _idPaciente;
    private readonly int _idPendiente, _idCancelado;
    private readonly List<string> _estadosTomados = new();
    private readonly List<int> _turnos = new();

    public ProximoTurnoZonaHorariaIntegrationTests()
    {
        _idEspecialidad = Insertar("INSERT INTO Especialidad (Nombre, Estado) VALUES (@n, N'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@n", _marca + "esp"));
        _idProfesional = Insertar(@"INSERT INTO Profesional (Nombre, Apellido, Matricula, IdEspecialidad, Estado, Mail)
                                    VALUES (N'Profesional', @a, @m, @e, N'Activo', @mail); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@a", _marca), ("@m", _marca + "mp"), ("@e", _idEspecialidad), ("@mail", _marca + "p@example.com"));
        _idPaciente = Insertar("INSERT INTO Paciente (Nombre, Dni, Estado) VALUES (@n, @d, N'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@n", "Paciente " + _marca), ("@d", _marca));
        _idPendiente = Estado("Pendiente");
        _idCancelado = Estado("Cancelado");
    }

    private static int Insertar(string sql, params (string, object?)[] parametros)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void Ejecutar(string sql, params (string, object?)[] parametros)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // El catálogo es compartido (ver EstadosCompartidos): el estado se toma y se suelta al terminar; lo borra la última prueba que lo usa.
    private int Estado(string descripcion)
    {
        var id = EstadosCompartidos.Tomar(descripcion);
        _estadosTomados.Add(descripcion);
        return id;
    }

    private int Turno(DateTime fecha, TimeSpan hora, int idEstado)
    {
        var id = Insertar(@"INSERT INTO Turno (IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor)
                            VALUES (@p, @r, @e, @f, @h, N'Virtual', N'[TEST] proximo turno', N'paciente'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@p", _idPaciente), ("@r", _idProfesional), ("@e", idEstado), ("@f", fecha.Date), ("@h", hora));
        _turnos.Add(id);
        return id;
    }

    public void Dispose()
    {
        foreach (var id in _turnos) Ejecutar("DELETE FROM Turno WHERE IdTurno = @i", ("@i", id));
        Ejecutar("DELETE FROM Paciente WHERE IdPaciente = @i", ("@i", _idPaciente));
        Ejecutar("DELETE FROM Profesional WHERE IdProfesional = @i", ("@i", _idProfesional));
        Ejecutar("DELETE FROM Especialidad WHERE IdEspecialidad = @i", ("@i", _idEspecialidad));
        // Lo borra la última prueba que lo usa; si lo está usando un turno que no es de esta prueba, queda.
        foreach (var descripcion in _estadosTomados) EstadosCompartidos.Soltar(descripcion);
        _estadosTomados.Clear();
    }

    // ---------- El DAO ----------

    [Fact]
    public void El_proximo_turno_se_cuenta_desde_el_hoy_que_se_le_pasa_y_no_desde_GETDATE()
    {
        var ayer = Turno(new DateTime(2031, 3, 3), new TimeSpan(10, 0, 0), _idPendiente);
        var hoy = Turno(new DateTime(2031, 3, 4), new TimeSpan(10, 0, 0), _idPendiente);
        Turno(new DateTime(2031, 3, 5), new TimeSpan(10, 0, 0), _idPendiente);

        // "Hoy" es el 4/3/2031. Con GETDATE() (hoy real, 2026) el primero que cumpliría Fecha >= hoy sería el de "ayer".
        var proximo = new TurnoDetalleDAO().ObtenerProximoPorPaciente(_idPaciente, new DateTime(2031, 3, 4, 23, 59, 0));

        Assert.NotNull(proximo);
        Assert.Equal(hoy, proximo!.IdTurno);
        Assert.NotEqual(ayer, proximo.IdTurno);
    }

    [Fact]
    public void Solo_cuenta_la_fecha_del_hoy_que_se_le_pasa()
    {
        var manana = Turno(new DateTime(2031, 3, 5), new TimeSpan(10, 0, 0), _idPendiente);

        var proximo = new TurnoDetalleDAO().ObtenerProximoPorPaciente(_idPaciente, new DateTime(2031, 3, 5, 0, 0, 1));

        Assert.Equal(manana, proximo!.IdTurno);
    }

    [Fact]
    public void Un_turno_cancelado_no_es_el_proximo()
    {
        Turno(new DateTime(2031, 3, 4), new TimeSpan(9, 0, 0), _idCancelado);
        var pendiente = Turno(new DateTime(2031, 3, 6), new TimeSpan(9, 0, 0), _idPendiente);

        var proximo = new TurnoDetalleDAO().ObtenerProximoPorPaciente(_idPaciente, new DateTime(2031, 3, 4));

        Assert.Equal(pendiente, proximo!.IdTurno);
    }

    [Fact]
    public void Si_no_hay_turnos_desde_hoy_no_hay_proximo()
    {
        Turno(new DateTime(2031, 3, 3), new TimeSpan(9, 0, 0), _idPendiente);

        Assert.Null(new TurnoDetalleDAO().ObtenerProximoPorPaciente(_idPaciente, new DateTime(2031, 3, 4)));
    }

    [Fact]
    public void Los_profesionales_con_turno_se_cuentan_desde_el_hoy_que_se_le_pasa()
    {
        Turno(new DateTime(2031, 3, 3), new TimeSpan(9, 0, 0), _idPendiente);

        var dao = new TurnoDetalleDAO();
        Assert.Empty(dao.ObtenerProfesionalesPorPaciente(_idPaciente, new DateTime(2031, 3, 4)));
        Assert.Single(dao.ObtenerProfesionalesPorPaciente(_idPaciente, new DateTime(2031, 3, 3)));
    }

    // ---------- El endpoint, con el reloj de la clínica ----------

    // 04/03/2031 02:30 UTC = 03/03/2031 23:30 en Argentina: para la clínica todavía es el día 3.
    [Fact]
    public async Task El_endpoint_usa_el_dia_de_la_clinica_y_no_el_de_UTC()
    {
        var delDia3 = Turno(new DateTime(2031, 3, 3), new TimeSpan(10, 0, 0), _idPendiente);
        Turno(new DateTime(2031, 3, 4), new TimeSpan(10, 0, 0), _idPendiente);
        using var app = new TurnosApp(new DateTime(2031, 3, 4, 2, 30, 0));

        var r = await app.Paciente(usuario: 99, idPaciente: _idPaciente).GetAsync($"/pacientes/{_idPaciente}/proximo-turno");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(delDia3, (await TurnosApp.Json(r)).GetProperty("idTurno").GetInt32());
    }

    [Fact]
    public async Task El_endpoint_no_deja_ver_el_proximo_turno_de_otro_paciente()
    {
        Turno(new DateTime(2031, 3, 4), new TimeSpan(10, 0, 0), _idPendiente);
        using var app = new TurnosApp(new DateTime(2031, 3, 4, 12, 0, 0));

        var r = await app.Paciente(usuario: 99, idPaciente: _idPaciente + 100000).GetAsync($"/pacientes/{_idPaciente}/proximo-turno");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
