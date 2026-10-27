using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Entrada a la sala desde cualquier lugar: «el turno entrable» (EstadoDeSala.Entrable + GET /salas/entrable), la lógica del cliente (node, DOM falso), el link del recordatorio y la causa
// de la pantalla en blanco de la sala del paciente. Todo en memoria (sin base de datos): el reloj de VideoApp está fijo en martes 4/3/2031 a las 10:00.
public class TurnoEntrableReglaTests
{
    private static readonly DateTime Dia = new(2031, 3, 4);
    private static readonly VideoOptions Opciones = new();   // 10 minutos antes, 30 después del fin, 30 minutos por defecto

    private static TurnoConNombres T(int id, string hora, string estado = "Confirmado", int dias = 0, int? duracion = 30, int pac = 301, int prof = 201) =>
        new(new TurnoParaSala(id, pac, prof, estado, Dia.AddDays(dias), TimeSpan.Parse(hora), null, duracion), "Paciente " + pac, "Profesional " + prof);

    [Fact]
    public void Con_un_solo_turno_en_ventana_es_el_entrable()
    {
        var (entrable, proximo) = EstadoDeSala.Entrable(new[] { T(1, "10:00") }, Dia.AddHours(10), Opciones);
        Assert.Equal(1, entrable!.Turno.IdTurno);
        Assert.Null(proximo);
    }

    [Theory]
    [InlineData("09:50:00", true)]    // justo cuando abre (10 minutos antes)
    [InlineData("09:49:59", false)]   // un segundo antes
    [InlineData("11:00:00", false)]   // cierra al fin + 30 minutos (10:00 + 30 + 30 = 11:00)
    [InlineData("10:59:59", true)]
    public void La_ventana_incluye_el_instante_de_apertura_y_excluye_el_de_cierre(string ahora, bool entrable)
    {
        var (e, _) = EstadoDeSala.Entrable(new[] { T(1, "10:00") }, Dia + TimeSpan.Parse(ahora), Opciones);
        Assert.Equal(entrable, e is not null);
    }

    [Fact]
    public void Con_dos_turnos_abiertos_gana_el_que_empieza_mas_cerca_de_ahora()
    {
        var turnos = new[] { T(1, "09:30", duracion: 60), T(2, "10:05") };   // a las 10:00: el 1 empezó hace 30 min, el 2 empieza en 5
        Assert.Equal(2, EstadoDeSala.Entrable(turnos, Dia.AddHours(10), Opciones).Entrable!.Turno.IdTurno);
        Assert.Equal(2, EstadoDeSala.Entrable(turnos.Reverse(), Dia.AddHours(10), Opciones).Entrable!.Turno.IdTurno);   // no depende del orden en que llegan
        Assert.Equal(1, EstadoDeSala.Entrable(turnos, Dia + new TimeSpan(9, 45, 0), Opciones).Entrable!.Turno.IdTurno);  // a las 9:45 el más cercano es el de las 9:30
    }

    [Fact]
    public void Un_turno_pasado_o_cerrado_no_cuenta_y_no_es_el_proximo()
    {
        var (entrable, proximo) = EstadoDeSala.Entrable(new[] { T(1, "07:00") }, Dia.AddHours(10), Opciones);
        Assert.Null(entrable);
        Assert.Null(proximo);
    }

    [Fact]
    public void Antes_de_los_10_minutos_no_hay_entrable_y_el_turno_es_el_proximo()
    {
        var (entrable, proximo) = EstadoDeSala.Entrable(new[] { T(2, "11:00"), T(1, "10:20") }, Dia.AddHours(10), Opciones);
        Assert.Null(entrable);
        Assert.Equal(1, proximo!.Turno.IdTurno);   // el primero por fecha y hora
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Cancelado")]
    [InlineData("Realizado")]
    [InlineData("Ausente")]
    public void Solo_cuentan_los_turnos_confirmados(string estado)
    {
        var (entrable, proximo) = EstadoDeSala.Entrable(new[] { T(1, "10:00", estado), T(2, "10:30", estado) }, Dia.AddHours(10), Opciones);
        Assert.Null(entrable);
        Assert.Null(proximo);
    }

    [Fact]
    public void El_estado_se_compara_sin_importar_mayusculas_ni_acentos()
    {
        Assert.NotNull(EstadoDeSala.Entrable(new[] { T(1, "10:00", "CONFIRMADO") }, Dia.AddHours(10), Opciones).Entrable);
    }

    [Fact]
    public void Un_turno_de_otro_dia_es_el_proximo_pero_no_el_entrable()
    {
        var (entrable, proximo) = EstadoDeSala.Entrable(new[] { T(1, "10:00", dias: 1) }, Dia.AddHours(10), Opciones);
        Assert.Null(entrable);
        Assert.Equal(1, proximo!.Turno.IdTurno);
    }

    [Fact]
    public void Un_turno_de_ayer_cuya_sala_sigue_abierta_pasada_la_medianoche_es_entrable()
    {
        // Turno de 23:50 con 30 min + 30 de margen: la sala cierra a las 00:50 del día siguiente.
        var (entrable, _) = EstadoDeSala.Entrable(new[] { T(1, "23:50", dias: -1) }, Dia.AddMinutes(20), Opciones);
        Assert.NotNull(entrable);
    }
}

public class EntradaSalaEndpointTests : IDisposable
{
    private readonly VideoApp _app;
    public EntradaSalaEndpointTests()
    {
        // El endpoint lee el reloj de la clínica (TimeProvider): se fija igual que el del acceso a la sala.
        var reloj = new RelojFijo(VideoApp.Inicio);
        _app = new VideoApp(reloj: reloj, servicios: s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(reloj); });
    }
    public void Dispose() => _app.Dispose();

    private static void Poner(VideoApp app, int id, string hora, string estado = "Confirmado", int dias = 0, int? duracion = 30, int pac = VideoApp.IdPaciente, int prof = VideoApp.IdProfesional) =>
        app.Origen.Turnos[id] = new TurnoParaSala(id, pac, prof, estado, VideoApp.Inicio.Date.AddDays(dias), TimeSpan.Parse(hora), null, duracion);

    private static async Task<JsonElement> Pedir(HttpClient c)
    {
        var r = await c.GetAsync("/salas/entrable");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await VideoApp.Json(r);
    }

    [Fact]
    public async Task El_turno_de_las_10_a_las_10_es_el_entrable_del_paciente_con_el_nombre_del_profesional()
    {
        var j = await Pedir(_app.Paciente());   // VideoApp ya trae el turno 1 a las 10:00 y el reloj a las 10:00

        var e = j.GetProperty("entrable");
        Assert.Equal(VideoApp.IdTurno, e.GetProperty("idTurno").GetInt32());
        Assert.Equal("10:00", e.GetProperty("hora").GetString());
        Assert.Equal("09:50", e.GetProperty("horaAbre").GetString());
        Assert.Equal("Profesional 201", e.GetProperty("contraparte").GetString());
        Assert.True(e.GetProperty("esHoy").GetBoolean());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("proximo").ValueKind);
    }

    [Fact]
    public async Task Con_dos_turnos_abiertos_devuelve_el_mas_cercano()
    {
        Poner(_app, 2, "10:05");
        Poner(_app, 3, "09:30", duracion: 60);
        _app.Origen.Turnos.Remove(VideoApp.IdTurno);

        var j = await Pedir(_app.Paciente());

        Assert.Equal(2, j.GetProperty("entrable").GetProperty("idTurno").GetInt32());
    }

    [Fact]
    public async Task Sin_ningun_turno_no_hay_entrable_ni_proximo()
    {
        _app.Origen.Turnos.Clear();

        var j = await Pedir(_app.Paciente());

        Assert.Equal(JsonValueKind.Null, j.GetProperty("entrable").ValueKind);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("proximo").ValueKind);
    }

    [Fact]
    public async Task Fuera_de_la_ventana_el_turno_de_hoy_es_el_proximo_con_la_hora_de_apertura()
    {
        _app.Origen.Turnos.Clear();
        Poner(_app, 5, "10:20");

        var j = await Pedir(_app.Paciente());

        Assert.Equal(JsonValueKind.Null, j.GetProperty("entrable").ValueKind);
        var p = j.GetProperty("proximo");
        Assert.Equal(5, p.GetProperty("idTurno").GetInt32());
        Assert.Equal("10:10", p.GetProperty("horaAbre").GetString());
        Assert.True(p.GetProperty("esHoy").GetBoolean());
    }

    [Fact]
    public async Task Un_turno_de_manana_es_el_proximo_y_no_es_de_hoy()
    {
        _app.Origen.Turnos.Clear();
        Poner(_app, 6, "10:00", dias: 1);

        var p = (await Pedir(_app.Paciente())).GetProperty("proximo");

        Assert.Equal(6, p.GetProperty("idTurno").GetInt32());
        Assert.False(p.GetProperty("esHoy").GetBoolean());
    }

    [Fact]
    public async Task Un_turno_que_ya_paso_no_es_entrable()
    {
        _app.Origen.Turnos.Clear();
        Poner(_app, 7, "07:00");

        var j = await Pedir(_app.Paciente());

        Assert.Equal(JsonValueKind.Null, j.GetProperty("entrable").ValueKind);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("proximo").ValueKind);
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Cancelado")]
    public async Task Un_turno_pendiente_o_cancelado_no_cuenta(string estado)
    {
        _app.Origen.Turnos.Clear();
        Poner(_app, 8, "10:00", estado);

        var j = await Pedir(_app.Paciente());

        Assert.Equal(JsonValueKind.Null, j.GetProperty("entrable").ValueKind);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("proximo").ValueKind);
    }

    [Fact]
    public async Task El_turno_de_otro_paciente_no_se_ve()
    {
        // El turno 1 es del paciente 301. El paciente 302 no tiene nada: no se entera de que existe.
        var ajeno = await Pedir(_app.OtroPaciente());
        Assert.Equal(JsonValueKind.Null, ajeno.GetProperty("entrable").ValueKind);
        Assert.Equal(JsonValueKind.Null, ajeno.GetProperty("proximo").ValueKind);

        Poner(_app, 9, "10:00", pac: VideoApp.IdOtroPaciente, prof: VideoApp.IdOtroProfesional);
        var propio = await Pedir(_app.OtroPaciente());
        Assert.Equal(9, propio.GetProperty("entrable").GetProperty("idTurno").GetInt32());
        var mio = await Pedir(_app.Paciente());
        Assert.Equal(VideoApp.IdTurno, mio.GetProperty("entrable").GetProperty("idTurno").GetInt32());   // y el 301 sigue viendo solo el suyo
    }

    [Fact]
    public async Task El_profesional_ve_su_turno_con_el_nombre_del_paciente_aunque_el_paciente_no_haya_entrado_y_no_ve_el_de_otro()
    {
        var j = await Pedir(_app.Profesional());
        Assert.Equal(VideoApp.IdTurno, j.GetProperty("entrable").GetProperty("idTurno").GetInt32());
        Assert.Equal("Paciente 301", j.GetProperty("entrable").GetProperty("contraparte").GetString());
        Assert.Equal(VideoApp.IdPaciente, j.GetProperty("entrable").GetProperty("idPaciente").GetInt32());

        var otro = await Pedir(_app.Cliente(VideoApp.UsuarioOtroProfesional, Roles.Profesional, idProfesional: VideoApp.IdOtroProfesional));
        Assert.Equal(JsonValueKind.Null, otro.GetProperty("entrable").ValueKind);
    }

    [Fact]
    public async Task Sin_sesion_o_sin_ser_paciente_ni_profesional_se_rechaza()
    {
        var anonimo = _app.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/salas/entrable")).StatusCode);
        var configurador = _app.Cliente(VideoApp.UsuarioConfigurador, Roles.Configurador);
        Assert.Equal(HttpStatusCode.Forbidden, (await configurador.GetAsync("/salas/entrable")).StatusCode);
    }

    [Fact]
    public async Task Entrable_y_el_servicio_de_acceso_dicen_lo_mismo_en_cada_momento_del_dia()
    {
        // La regla es una sola: en cada instante, «hay turno entrable» ⇔ AccesoSalaService concede (o, sin sala generada, dice SalaNoGenerada: la ventana ya abrió y no cerró).
        _app.Origen.Turnos.Clear();
        _app.Origen.Turnos[50] = new TurnoParaSala(50, VideoApp.IdPaciente, VideoApp.IdProfesional, "Confirmado", VideoApp.Inicio.Date, new TimeSpan(10, 0, 0), null, 30);
        var yo = new Identidad(VideoApp.UsuarioPaciente, Roles.Paciente, VideoApp.IdPaciente, null);
        var servicio = new AccesoSalaService(_app.Origen, _app.Reloj, new OpcionesFijas<VideoOptions>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AccesoSalaService>.Instance);
        foreach (var minutos in new[] { 9 * 60, 9 * 60 + 49, 9 * 60 + 50, 10 * 60, 10 * 60 + 59, 11 * 60, 12 * 60 })
        {
            _app.Reloj.Ahora = VideoApp.Inicio.Date.AddMinutes(minutos);
            var j = await Pedir(_app.Paciente());
            var estado = servicio.Evaluar(yo, OrigenSala.Turno, 50).Estado;
            var abierta = estado is EstadoAcceso.Concedido or EstadoAcceso.SalaNoGenerada;
            Assert.True((j.GetProperty("entrable").ValueKind == JsonValueKind.Object) == abierta, $"a las {minutos / 60:00}:{minutos % 60:00} entrable y acceso no coinciden ({estado})");
        }
    }
}

// El link del recordatorio: la base es la configurada y, si no hay, la pública (nunca localhost por omisión). Además, la causa de la pantalla en blanco.
public class UrlsDeAvisoTests
{
    [Fact]
    public void Con_la_base_configurada_el_link_la_usa_y_no_dice_localhost()
    {
        var op = new MensajeriaOptions { UrlIngresoTurno = "https://clinica.example/pantallas/index.html" };

        var link = UrlsDeAviso.SalaDelTurno(op, 42);

        Assert.StartsWith("https://clinica.example/pantallas/index.html?return=", link);
        Assert.DoesNotContain("localhost", link);
        Assert.Equal("/pantallas/Pantalla_Paciente/sesion_video.html?turno=42", Uri.UnescapeDataString(link[(link.IndexOf("return=") + 7)..]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es una url")]
    [InlineData("/pantallas/index.html")]
    [InlineData("ftp://clinica.example/")]
    public void Sin_una_base_valida_cae_a_la_url_publica(string configurada)
    {
        var op = new MensajeriaOptions { UrlIngresoTurno = configurada };

        Assert.StartsWith("https://yosoytelemedicina.tech/pantallas/index.html", UrlsDeAviso.Ingreso(op));
        var link = UrlsDeAviso.SalaDelTurno(op, 7);
        Assert.StartsWith("https://yosoytelemedicina.tech/pantallas/index.html?return=", link);
        Assert.DoesNotContain("localhost", link);
    }

    [Fact]
    public void El_valor_por_defecto_de_la_opcion_ya_no_es_localhost()
    {
        Assert.DoesNotContain("localhost", UrlsDeAviso.SalaDelTurno(new MensajeriaOptions(), 1));
    }

    [Fact]
    public void Una_base_que_ya_trae_parametros_los_conserva()
    {
        var link = UrlsDeAviso.SalaDelTurno(new MensajeriaOptions { UrlIngresoTurno = "https://clinica.example/ingreso?origen=aviso" }, 3);
        Assert.StartsWith("https://clinica.example/ingreso?origen=aviso&return=", link);
    }
}

public class EntradaSalaPantallasTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] ruta) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(ruta).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void La_sala_del_paciente_no_usa_la_clase_sala_espera_que_sala_css_oculta()
    {
        // CAUSA de la pantalla en blanco: shared/sala.css define .sala-espera { display: none } (el cartel «Esperando…» de la escena) y la pantalla previa de la sala del paciente
        // usaba esa misma clase: quedaba escondida y solo se veía el título.
        var css = Leer("PANTALLAS", "shared", "sala.css");
        Assert.Contains(".sala-espera {", css);
        Assert.Matches(@"\.sala-espera \{[^}]*display: none", css);
        var html = Leer("PANTALLAS", "Pantalla_Paciente", "sesion_video.html");
        Assert.DoesNotContain("class=\"sala-espera\"", html);
        Assert.Contains("<div id=\"pantalla-espera\" class=\"sala-previa\">", html);
    }

    [Fact]
    public void Ninguna_pantalla_previa_de_la_sala_usa_una_clase_que_sala_css_oculta()
    {
        // Las clases que sala.css esconde por defecto no pueden estar en el HTML de las páginas que lo cargan, salvo las que arma shared/video.js.
        var css = Leer("PANTALLAS", "shared", "sala.css");
        var ocultas = System.Text.RegularExpressions.Regex.Matches(css, @"(?m)^\.([a-z0-9-]+) \{[^}]*display: none")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Contains("sala-espera", ocultas);   // la prueba ve la regla que motivó esto
        foreach (var pagina in new[] { new[] { "Pantalla_Paciente", "sesion_video.html" }, new[] { "Pantalla_Profesional", "sesion_video.html" } })
        {
            var html = Leer(new[] { "PANTALLAS" }.Concat(pagina).ToArray());
            foreach (var clase in ocultas)
                Assert.DoesNotMatch($"class=\"[^\"]*\\b{clase}\\b[^\"]*\"", html);
        }
    }

    [Fact]
    public void Las_dos_pantallas_cargan_el_modulo_compartido_antes_de_usarlo()
    {
        Assert.Contains("../shared/entrada-sala.js", Leer("PANTALLAS", "Pantalla_Paciente", "dashboard.html"));
        Assert.Contains("../shared/entrada-sala.js", Leer("PANTALLAS", "Pantalla_Profesional", "nuevo_dashboard.html"));
    }

    [Fact]
    public void La_logica_del_cliente_con_un_DOM_falso_en_node()
    {
        var motivo = NodoDisponible();
        if (motivo is not null) { Console.WriteLine("OMITIDA: " + motivo); return; }   // sin node no se puede correr; el resto de las pruebas de esta clase sí

        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = Raiz, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(Path.Combine(Raiz, "YoSoy.Tests", "Pantallas", "entrada-sala.prueba.js"));
        psi.ArgumentList.Add(Raiz);
        using var p = Process.Start(psi)!;
        var salida = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(60000), "node no terminó");
        Assert.True(p.ExitCode == 0, salida.Result + error.Result);
        Assert.Contains("FALLAS 0", salida.Result);
    }

    private static string? NodoDisponible()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
            return p.WaitForExit(15000) && p.ExitCode == 0 ? null : "node respondió con error.";
        }
        catch (Exception e) { return "node no está disponible (" + e.GetType().Name + ")."; }
    }
}
