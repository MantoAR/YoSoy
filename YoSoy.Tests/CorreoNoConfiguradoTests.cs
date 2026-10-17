using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// El correo sin credenciales. appsettings.json no trae la cuenta ni la contraseña (se cargan con dotnet user-secrets o
// con variables de entorno) y la aplicación tiene que seguir andando sin ellas:
//  - EmailService se crea con cualquier configuración, avisa "Correo no configurado" al intentar mandar y NO abre ninguna
//    conexión; con todo configurado intenta mandar y el fallo del SMTP sube como siempre.
//  - El notificador de turnos registra el canal Email como "Omitido" (nunca como "Enviado") y el aviso queda en el log.
//  - Los appsettings*.json del proyecto no traen credenciales.
// Todos los valores son de prueba y falsos. Ninguna prueba manda un mail: el servidor no existe o es un puerto cerrado.
public class CorreoNoConfiguradoTests
{
    private const string Servidor = "smtp.no-existe.invalid";            // .invalid: reservado, nunca resuelve
    private const string Cuenta = "cuenta-de-prueba@ejemplo.test";
    private const string Clave = "clave-de-prueba-que-no-es-real";
    private const string Destinatario = "paciente-de-prueba@ejemplo.test";

    private static IConfiguration Config(params (string Clave, string? Valor)[] valores) => new ConfigurationBuilder()
        .AddInMemoryCollection(valores.ToDictionary(v => v.Clave, v => v.Valor)).Build();

    private static EmailService Servicio(LogsCapturados logs, params (string, string?)[] valores) =>
        new(Config(valores), logs.CreateLogger<EmailService>());

    private static List<string> Avisos(LogsCapturados logs) =>
        logs.Lineas.Where(l => l.StartsWith("Warning|") && l.Contains("Correo no configurado")).ToList();

    // Un puerto de esta máquina en el que no escucha nadie: conectarse falla enseguida ("conexión rechazada").
    private static int PuertoCerrado()
    {
        var oyente = new TcpListener(IPAddress.Loopback, 0);
        oyente.Start();
        var puerto = ((IPEndPoint)oyente.LocalEndpoint).Port;
        oyente.Stop();
        return puerto;
    }

    // ---------- EmailService ----------

    [Fact]
    public void Sin_ninguna_clave_el_servicio_se_crea_y_no_esta_configurado()
    {
        var servicio = new EmailService(Config());

        Assert.False(servicio.Configurado);
    }

    [Theory]
    [InlineData("", Cuenta, Clave, "Email:Host")]
    [InlineData(Servidor, "", Clave, "Email:From")]
    [InlineData(Servidor, "   ", Clave, "Email:From")]
    [InlineData(Servidor, Cuenta, "", "Email:Password")]
    [InlineData(Servidor, Cuenta, " ", "Email:Password")]          // el espacio con el que se apagaba el correo en desarrollo
    public void Si_falta_cualquiera_de_las_tres_claves_no_manda_nada_y_avisa_cual_falta(string host, string cuenta, string clave, string falta)
    {
        var logs = new LogsCapturados();
        var servicio = Servicio(logs, ("Email:Host", host), ("Email:From", cuenta), ("Email:Password", clave));

        var salio = servicio.EnviarAviso(Destinatario, "Asunto", "Texto");

        Assert.False(servicio.Configurado);
        Assert.False(salio);
        var aviso = Assert.Single(Avisos(logs));
        Assert.Contains(falta, aviso);
    }

    [Fact]
    public void Los_tres_tipos_de_mail_avisan_y_no_tiran()
    {
        var logs = new LogsCapturados();
        var servicio = Servicio(logs);

        Assert.False(servicio.EnviarAprobacionProfesional(Destinatario, "Profesional de Prueba", "token-de-prueba"));
        Assert.False(servicio.EnviarAviso(Destinatario, "Asunto", "Texto"));
        Assert.False(servicio.EnviarResetPassword(Destinatario, "token-de-prueba", 30));

        Assert.Equal(3, Avisos(logs).Count);
    }

    [Fact]
    public void El_aviso_nombra_las_claves_pero_no_muestra_valores_ni_destinatarios()
    {
        var conCuenta = new LogsCapturados();
        Servicio(conCuenta, ("Email:Host", Servidor), ("Email:From", Cuenta), ("Email:Password", ""))
            .EnviarResetPassword(Destinatario, "token-de-prueba", 30);

        var conClave = new LogsCapturados();
        Servicio(conClave, ("Email:Host", Servidor), ("Email:From", ""), ("Email:Password", Clave))
            .EnviarAviso(Destinatario, "Asunto", "Texto");

        Assert.Contains("Email:Password", Assert.Single(Avisos(conCuenta)));
        Assert.Contains("Email:From", Assert.Single(Avisos(conClave)));
        foreach (var secreto in new[] { Cuenta, Clave, Destinatario, "token-de-prueba" })
        {
            Assert.DoesNotContain(secreto, conCuenta.Todo);
            Assert.DoesNotContain(secreto, conClave.Todo);
        }
    }

    [Theory]
    [InlineData("no-es-un-numero")]
    [InlineData("0")]
    [InlineData("-5")]
    public void Un_puerto_invalido_no_rompe_el_servicio_y_se_avisa(string puerto)
    {
        var logs = new LogsCapturados();

        var servicio = Servicio(logs, ("Email:Port", puerto));

        Assert.False(servicio.Configurado);
        Assert.Contains(logs.Lineas, l => l.StartsWith("Warning|") && l.Contains("Email:Port"));
    }

    // Lo que NO cambia: con todo configurado intenta mandar de verdad, y si el SMTP falla la excepción sube como antes
    // (quien llama decide qué hacer: el notificador la registra como "Error", el restablecimiento de contraseña la calla).
    [Fact]
    public void Con_todo_configurado_intenta_conectarse_y_el_fallo_del_SMTP_sube()
    {
        var logs = new LogsCapturados();
        var servicio = Servicio(logs, ("Email:Host", "127.0.0.1"), ("Email:Port", PuertoCerrado().ToString()),
            ("Email:From", Cuenta), ("Email:Password", Clave));

        Assert.True(servicio.Configurado);
        Assert.ThrowsAny<Exception>(() => servicio.EnviarAviso(Destinatario, "Asunto", "Texto"));
        Assert.Empty(Avisos(logs));
    }

    // ---------- La aplicación ----------

    [Fact]
    public void La_aplicacion_arranca_sin_credenciales_de_correo()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-de-prueba-para-los-tests-de-correo-0123456789",
                ["Email:From"] = "",
                ["Email:Password"] = ""
            })));

        var servicio = fabrica.Services.GetRequiredService<EmailService>();

        Assert.False(servicio.Configurado);
    }

    // ---------- El notificador de turnos ----------

    // FakeDatos no carga el mail del paciente: se lo agrega.
    private sealed class DatosConMail : IDatosMensajeria
    {
        private readonly FakeDatos _datos;
        private readonly string _mail;
        public DatosConMail(FakeDatos datos, string mail) { _datos = datos; _mail = mail; }

        public List<Paciente> PacientesPorTelefono(string tel) => _datos.PacientesPorTelefono(tel);
        public Paciente? PacienteActivoPorDni(string dni) => _datos.PacienteActivoPorDni(dni);
        public Paciente? ObtenerPaciente(int id) => _datos.ObtenerPaciente(id);
        public List<ProfesionalResumen> ProfesionalesDelPaciente(int idPaciente) => _datos.ProfesionalesDelPaciente(idPaciente);
        public List<Especialidad> EspecialidadesConAgenda() => _datos.EspecialidadesConAgenda();
        public List<ProfesionalResumen> ProfesionalesConAgenda(int idEspecialidad) => _datos.ProfesionalesConAgenda(idEspecialidad);
        public List<TurnoAviso> TurnosProximos(int idPaciente, DateTime ahora) => _datos.TurnosProximos(idPaciente, ahora);
        public int? BuscarIdTurno(int idPaciente, int idProfesional, DateTime fecha, TimeSpan hora) => _datos.BuscarIdTurno(idPaciente, idProfesional, fecha, hora);
        public void CrearNotificacionPaciente(int idPaciente, string titulo, string mensaje, string? link) =>
            _datos.CrearNotificacionPaciente(idPaciente, titulo, mensaje, link);

        public TurnoAviso? ObtenerTurnoAviso(int idTurno)
        {
            var aviso = _datos.ObtenerTurnoAviso(idTurno);
            if (aviso is not null) aviso.MailPaciente = _mail;
            return aviso;
        }
    }

    // Reserva un recordatorio por canal y guarda con qué resultado quedó cada uno.
    private sealed class RecordatorioQueRegistra : IRecordatorioDAO
    {
        private readonly Dictionary<int, string> _canales = new();
        public readonly List<(string Canal, string Estado, string? Error)> Resultados = new();

        public int? Reservar(int idTurno, string tipo, string canal, DateTime fechaProgramada, int maxIntentos)
        {
            var id = _canales.Count + 1;
            _canales[id] = canal;
            return id;
        }

        public void MarcarResultado(int idRecordatorio, string estado, string? error, DateTime ahora) =>
            Resultados.Add((_canales[idRecordatorio], estado, error));

        public void RegistrarRespuesta(int idTurno, string tipo, string respuesta, DateTime ahora) { }
        public List<int> TurnosConfirmadosSinRecordar(DateTime desde, DateTime hasta, int cantidadCanales, int maxIntentos) => new();
    }

    private sealed class InternasNulas : INotificacionInternaDAO
    {
        public int Insertar(NotificacionInterna n) => 0;
    }

    private static (RecordatorioQueRegistra Recordatorios, LogsCapturados Logs, int IdTurno) AvisarElRecordatorio(
        params (string, string?)[] correo)
    {
        var e = new Escenario();
        var turno = e.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(1), BaseEnMemoria.Confirmado);
        var logs = new LogsCapturados();
        var recordatorios = new RecordatorioQueRegistra();
        var config = Config(correo);
        var servicios = new ServiceCollection().AddSingleton(new EmailService(config, logs.CreateLogger<EmailService>())).BuildServiceProvider();
        var notificador = new NotificadorTurnos(
            new DatosConMail(new FakeDatos(e.Db), Destinatario), recordatorios, new InternasNulas(), e.Conversaciones, e.Mensajes,
            new CanalSimulado(), e.WhatsApp, new FakeTurnoRepositorio(e.Db), servicios, config, e.Opciones, e.Reloj,
            logs.CreateLogger<NotificadorTurnos>());

        notificador.EnviarRecordatorio24h(turno.IdTurno);
        return (recordatorios, logs, turno.IdTurno);
    }

    [Theory]
    [InlineData("")]                      // lo normal: la contraseña vacía
    [InlineData(" ")]                     // el espacio con el que se apagaba el correo en desarrollo
    public void El_notificador_registra_Omitido_y_avisa_cuando_falta_la_contrasena(string clave)
    {
        var (recordatorios, logs, _) = AvisarElRecordatorio(("Email:Host", Servidor), ("Email:From", Cuenta), ("Email:Password", clave));

        var mail = Assert.Single(recordatorios.Resultados, r => r.Canal == "Email");
        Assert.Equal("Omitido", mail.Estado);
        Assert.Equal("EmailService no está configurado.", mail.Error);
        Assert.Single(Avisos(logs));
        Assert.DoesNotContain(logs.Lineas, l => l.StartsWith("Error|"));
    }

    // Antes solo se miraban el servidor y la contraseña: con la cuenta vacía el envío fallaba con un error y se registraba
    // como "Error" (con el stack en el log).
    [Fact]
    public void Sin_la_cuenta_tambien_queda_Omitido()
    {
        var (recordatorios, logs, _) = AvisarElRecordatorio(("Email:Host", Servidor), ("Email:From", ""), ("Email:Password", Clave));

        var mail = Assert.Single(recordatorios.Resultados, r => r.Canal == "Email");
        Assert.Equal("Omitido", mail.Estado);
        Assert.Contains("Email:From", Assert.Single(Avisos(logs)));
        Assert.DoesNotContain(logs.Lineas, l => l.StartsWith("Error|"));
    }

    [Fact]
    public void El_resto_de_los_canales_no_se_ven_afectados()
    {
        var (recordatorios, _, _) = AvisarElRecordatorio();

        Assert.Equal(new[] { "Chat", "Email", "App" }, recordatorios.Resultados.Select(r => r.Canal).ToArray());
        Assert.Equal("Enviado", recordatorios.Resultados.Single(r => r.Canal == "App").Estado);   // la campanita sigue andando
    }

    [Fact]
    public void Con_el_correo_configurado_el_notificador_si_intenta_mandar_y_registra_el_error_del_SMTP()
    {
        var (recordatorios, logs, _) = AvisarElRecordatorio(("Email:Host", "127.0.0.1"), ("Email:Port", PuertoCerrado().ToString()),
            ("Email:From", Cuenta), ("Email:Password", Clave));

        var mail = Assert.Single(recordatorios.Resultados, r => r.Canal == "Email");
        Assert.Equal("Error", mail.Estado);
        Assert.Empty(Avisos(logs));
    }

    // ---------- Los archivos de configuración ----------

    private static string CarpetaDelProyecto()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "YoSoy.csproj"))) return d.FullName;
        throw new InvalidOperationException("No encontré la carpeta del proyecto (YoSoy.csproj) subiendo desde " + AppContext.BaseDirectory);
    }

    private static IEnumerable<(string Ruta, string Valor)> Textos(JsonElement nodo, string ruta = "")
    {
        switch (nodo.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in nodo.EnumerateObject())
                    foreach (var hijo in Textos(p.Value, ruta == "" ? p.Name : ruta + ":" + p.Name)) yield return hijo;
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var v in nodo.EnumerateArray())
                    foreach (var hijo in Textos(v, $"{ruta}[{i++}]")) yield return hijo;
                break;
            case JsonValueKind.String:
                yield return (ruta, nodo.GetString() ?? "");
                break;
        }
    }

    // Credenciales de correo y cualquier propiedad con nombre de secreto: tienen que ir vacías en TODOS los
    // appsettings*.json de la carpeta del proyecto (también en uno local sin versionar, que se commitea por error con
    // facilidad). Falla mostrando solo los NOMBRES de las propiedades, nunca sus valores.
    [Fact]
    public void Los_appsettings_del_proyecto_no_traen_credenciales()
    {
        var archivos = Directory.GetFiles(CarpetaDelProyecto(), "appsettings*.json");
        Assert.NotEmpty(archivos);
        var nombreDeSecreto = new Regex("pass|pwd|secret|clave|token|apikey|connectionstring|^Email:From$", RegexOptions.IgnoreCase);

        var conValor = new List<string>();
        foreach (var archivo in archivos)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(archivo), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            conValor.AddRange(Textos(doc.RootElement)
                .Where(t => nombreDeSecreto.IsMatch(t.Ruta) && !string.IsNullOrWhiteSpace(t.Valor))
                .Select(t => $"{Path.GetFileName(archivo)} → {t.Ruta}"));
        }

        Assert.True(conValor.Count == 0,
            "Estas propiedades de appsettings tienen un valor y no deberían (van en user-secrets o en variables de entorno): "
            + string.Join(", ", conValor));
    }
}
