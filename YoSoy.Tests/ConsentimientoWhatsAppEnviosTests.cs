using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// El consentimiento de WhatsApp en el camino de los mensajes, sin base ni red de verdad:
//  · NotificadorTurnos: solo una aceptación vigente da optIn; si el paciente revocó no sale nada; un rechazo por falta de consentimiento
//    queda "Omitido" y no "Error" (no se reintenta); el aviso va al número nacional de 10 dígitos.
//  · CanalBaileys: "optIn": true viaja solo con la aceptación, y el código con el que whatsapp-service rechaza llega hasta arriba.
//  · ServicioMensajes: las opciones llegan al canal y el código de rechazo vuelve en el mensaje (sin guardarse ni salir en las respuestas).
//  · BotReservasService: una BAJA se registra y no se contesta, y nada más cuenta como baja.
public class NotificadorWhatsAppConsentimientoTests
{
    // FakeDatos no carga el teléfono del paciente en el aviso: se lo agrega, de la ficha del paciente de la base en memoria.
    private sealed class DatosConTelefono : IDatosMensajeria
    {
        private readonly FakeDatos _datos;
        private readonly BaseEnMemoria _db;
        public DatosConTelefono(FakeDatos datos, BaseEnMemoria db) { _datos = datos; _db = db; }

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
            if (aviso is not null) aviso.TelefonoPaciente = _db.Pacientes.Single(p => p.IdPaciente == aviso.IdPaciente).Telefono;
            return aviso;
        }
    }

    // Reserva un aviso por canal y guarda con qué resultado quedó cada uno.
    private sealed class RecordatoriosDePrueba : IRecordatorioDAO
    {
        private readonly Dictionary<int, string> _canales = new();
        public readonly List<(string Canal, string Estado, string? Error)> Resultados = new();

        public int? Reservar(int idTurno, string tipo, string canal, DateTime fechaProgramada, int maxIntentos)
        {
            var id = _canales.Count + 1;
            _canales[id] = canal;
            return id;
        }

        public void MarcarResultado(int idRecordatorio, string estado, string? error, DateTime ahora) => Resultados.Add((_canales[idRecordatorio], estado, error));
        public void RegistrarRespuesta(int idTurno, string tipo, string respuesta, DateTime ahora) { }
        public List<int> TurnosConfirmadosSinRecordar(DateTime desde, DateTime hasta, IReadOnlyList<string> canales, int maxIntentos) => new();
    }

    private sealed class InternasNulas : INotificacionInternaDAO
    {
        public int Insertar(NotificacionInterna n) => 0;
    }

    private sealed class Arnes
    {
        public readonly Escenario E = new();
        public readonly FakeDatos Datos;
        public readonly RecordatoriosDePrueba Recordatorios = new();
        public readonly LogsCapturados Logs = new();
        public readonly NotificadorTurnos Notificador;
        public readonly Turno Turno;

        public Arnes(string? telefono = Escenario.TelJuan)
        {
            E.Db.Pacientes.Single(p => p.IdPaciente == 1).Telefono = telefono!;
            Datos = new FakeDatos(E.Db);
            Notificador = new NotificadorTurnos(new DatosConTelefono(Datos, E.Db), Recordatorios, new InternasNulas(), E.Conversaciones, E.Mensajes,
                new CanalSimulado(), E.WhatsApp, new FakeTurnoRepositorio(E.Db), new ServiceCollection().BuildServiceProvider(),
                new ConfigurationBuilder().Build(), E.Opciones, E.Reloj, Logs.CreateLogger<NotificadorTurnos>());
            Turno = E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(2), BaseEnMemoria.Confirmado);
        }

        public List<MensajeChat> Salientes => E.Mensajes.Mensajes.Where(m => m.Direccion == "Saliente").ToList();
        public (string Canal, string Estado, string? Error) Chat => Recordatorios.Resultados.Single(r => r.Canal == "Chat");
    }

    // ───────────── optIn ─────────────

    [Fact]
    public void Con_una_aceptacion_vigente_el_aviso_sale_con_optIn()
    {
        var a = new Arnes();
        a.E.WhatsApp.Permisos[1] = PermisoEnvioWhatsApp.ConConsentimiento;

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Single(a.Salientes);
        Assert.True(Assert.Single(a.E.Mensajes.OpcionesDeEnvio).OptIn);
        Assert.Equal("Enviado", a.Chat.Estado);
    }

    [Fact]
    public void Sin_decision_el_aviso_sale_sin_optIn_y_lo_decide_el_servicio_de_WhatsApp()
    {
        var a = new Arnes();

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Single(a.Salientes);
        Assert.False(Assert.Single(a.E.Mensajes.OpcionesDeEnvio).OptIn);
        Assert.Equal("Enviado", a.Chat.Estado);
    }

    [Fact]
    public void Se_le_pregunta_a_la_politica_por_el_paciente_del_turno_con_su_telefono()
    {
        var a = new Arnes();

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Equal((1, Escenario.TelJuan), Assert.Single(a.E.WhatsApp.Consultas));
    }

    // ───────────── Revocado ─────────────

    [Fact]
    public void Si_el_paciente_revoco_no_sale_nada_por_WhatsApp_ni_se_arma_la_conversacion()
    {
        var a = new Arnes();
        a.E.WhatsApp.Permisos[1] = PermisoEnvioWhatsApp.Revocado;

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Empty(a.E.Mensajes.Mensajes);
        Assert.Empty(a.E.Conversaciones.Todas);
        Assert.Equal("Omitido", a.Chat.Estado);
        Assert.Contains("revocó", a.Chat.Error);
    }

    [Fact]
    public void Revocar_el_WhatsApp_no_apaga_los_otros_canales_dentro_de_YoSoy()
    {
        var a = new Arnes();
        a.E.WhatsApp.Permisos[1] = PermisoEnvioWhatsApp.Revocado;

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Equal("Omitido", a.Chat.Estado);
        Assert.Empty(a.E.Mensajes.Mensajes);
        Assert.Single(a.Datos.Campanita);                                          // la campanita de YoSoy sigue
        Assert.Contains(a.Recordatorios.Resultados, r => r.Canal == "App" && r.Estado == "Enviado");
    }

    [Theory]
    [InlineData("TurnoConfirmado")]
    [InlineData("TurnoCancelado")]
    [InlineData("TurnoSolicitado")]
    public void Revocado_corta_todos_los_avisos_de_turno_por_WhatsApp(string aviso)
    {
        var a = new Arnes();
        a.E.WhatsApp.Permisos[1] = PermisoEnvioWhatsApp.Revocado;
        var id = a.Turno.IdTurno;
        if (aviso == "TurnoCancelado") a.E.Db.Turnos.Single(t => t.IdTurno == id).IdEstadoTurno = BaseEnMemoria.Cancelado;

        switch (aviso)
        {
            case "TurnoConfirmado": a.Notificador.TurnoConfirmado(id); break;
            case "TurnoCancelado": a.Notificador.TurnoCancelado(id); break;
            default: a.Notificador.TurnoSolicitado(id); break;
        }

        Assert.Empty(a.E.Mensajes.Mensajes);
        Assert.Equal("Omitido", a.Chat.Estado);
    }

    // ───────────── Lo que contesta el servicio ─────────────

    [Theory]
    [InlineData("sin_consentimiento")]
    [InlineData("baja")]
    public void Si_el_servicio_rechaza_por_falta_de_consentimiento_queda_Omitido_y_no_es_una_falla(string codigo)
    {
        var a = new Arnes();
        a.E.Mensajes.RechazarProximoConCodigo = codigo;

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Equal("Omitido", a.Chat.Estado);
        Assert.DoesNotContain(a.Logs.Lineas, l => l.StartsWith("Error|"));
    }

    [Theory]
    [InlineData("tope_diario")]
    [InlineData("no_conectado")]
    [InlineData("telefono_invalido")]
    [InlineData(null)]
    public void Cualquier_otro_rechazo_sigue_siendo_un_Error_que_se_puede_reintentar(string? codigo)
    {
        var a = new Arnes();
        a.E.Mensajes.RechazarProximoConCodigo = codigo ?? "";

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Equal("Error", a.Chat.Estado);
        Assert.Equal("El canal no pudo entregar el mensaje.", a.Chat.Error);
    }

    [Fact]
    public void Si_no_se_puede_saber_si_hay_consentimiento_el_aviso_de_WhatsApp_no_sale()
    {
        var a = new Arnes();
        a.E.WhatsApp.Falla = new InvalidOperationException("sin tabla");   // por ejemplo, la migración todavía no se aplicó

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Empty(a.E.Mensajes.Mensajes);
        Assert.Equal("Error", a.Chat.Estado);
        Assert.Contains(a.Recordatorios.Resultados, r => r.Canal == "App" && r.Estado == "Enviado");   // lo demás sigue
    }

    // ───────────── El número ─────────────

    [Theory]
    [InlineData("11 5555-0001", "1155550001")]
    [InlineData("011 15 5555-0001", "1155550001")]            // con el 15: los últimos 10 dígitos serían OTRO número
    [InlineData("+54 9 11 5555-0001", "1155550001")]
    [InlineData("0351 15 412-3456", "3514123456")]
    public void El_aviso_va_al_numero_nacional_de_10_digitos(string telefono, string esperado)
    {
        var a = new Arnes(telefono);

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Equal(esperado, Assert.Single(a.E.Conversaciones.Todas.Values).Telefono);
    }

    [Fact]
    public void Un_telefono_que_no_se_entiende_como_argentino_cae_en_lo_de_siempre()
    {
        var a = new Arnes("+34 612 345 678");

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Equal("4612345678", Assert.Single(a.E.Conversaciones.Todas.Values).Telefono);   // los últimos 10 dígitos, como antes
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    public void Sin_telefono_utilizable_queda_Omitido(string? telefono)
    {
        var a = new Arnes(telefono);

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        Assert.Empty(a.E.Mensajes.Mensajes);
        Assert.Equal("Omitido", a.Chat.Estado);
        Assert.Equal("El paciente no tiene teléfono cargado.", a.Chat.Error);
    }
}

public class CanalWhatsAppOptInTests
{
    // Un whatsapp-service de mentira en un puerto loopback real que guarda lo que le llega.
    private sealed class ServidorQueRegistra : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        public readonly List<string> Cuerpos = new();
        public string BaseUrl { get; }
        public Func<(int Status, string Cuerpo)> Responder { get; set; } = () => (200, """{"exito":true,"idExterno":"cola-1"}""");

        // El socket se cierra ANTES de que el listener use el puerto (si no, "el proceso no tiene acceso al archivo").
        private static int PuertoLibre()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        public ServidorQueRegistra()
        {
            BaseUrl = $"http://127.0.0.1:{PuertoLibre()}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = EscucharAsync();
        }

        private async Task EscucharAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch (Exception) { break; }

                string recibido;
                using (var lector = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    recibido = await lector.ReadToEndAsync();
                lock (Cuerpos) Cuerpos.Add(recibido);
                var (status, cuerpo) = Responder();
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(cuerpo);
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.OutputStream.Close();
            }
        }

        public JsonElement Ultimo()
        {
            string cuerpo;
            lock (Cuerpos) cuerpo = Cuerpos.Last();
            using var doc = JsonDocument.Parse(cuerpo);
            return doc.RootElement.Clone();
        }

        public void Dispose() { _cts.Cancel(); _listener.Stop(); _listener.Close(); }
    }

    private static CanalBaileys Canal(string baseUrl) =>
        new(new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) }, NullLogger<CanalBaileys>.Instance);

    [Fact]
    public async Task Con_OptIn_el_pedido_lleva_optIn_true_exacto_un_booleano()
    {
        using var servidor = new ServidorQueRegistra();

        var r = await Canal(servidor.BaseUrl).EnviarAsync("1155550001", "Tu turno del lunes 05/10 a las 10:00 quedó confirmado.", new OpcionesEnvio(OptIn: true));

        Assert.True(r.Exito);
        var cuerpo = servidor.Ultimo();
        Assert.Equal(JsonValueKind.True, cuerpo.GetProperty("optIn").ValueKind);     // true, no "true" ni 1: el servicio solo cuenta el valor exacto
        Assert.Equal("1155550001", cuerpo.GetProperty("telefono").GetString());
        Assert.Equal("Tu turno del lunes 05/10 a las 10:00 quedó confirmado.", cuerpo.GetProperty("texto").GetString());
        Assert.Equal(new[] { "optIn", "telefono", "texto" }, cuerpo.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Sin_OptIn_el_pedido_no_lleva_optIn_para_nada_ni_siquiera_en_false()
    {
        using var servidor = new ServidorQueRegistra();
        var canal = Canal(servidor.BaseUrl);

        await canal.EnviarAsync("1155550001", "a");                                            // el de siempre
        await canal.EnviarAsync("1155550001", "b", OpcionesEnvio.Ninguna);                     // sin opciones
        await canal.EnviarAsync("1155550001", "c", new OpcionesEnvio(OptIn: false));           // con las opciones en false

        Assert.Equal(3, servidor.Cuerpos.Count);
        foreach (var texto in servidor.Cuerpos)
        {
            using var doc = JsonDocument.Parse(texto);
            Assert.False(doc.RootElement.TryGetProperty("optIn", out _), texto);
            Assert.Equal(new[] { "telefono", "texto" }, doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task El_codigo_con_el_que_el_servicio_rechaza_llega_hasta_arriba()
    {
        using var servidor = new ServidorQueRegistra
        {
            Responder = () => (403, """{"exito":false,"error":"No hay constancia de que la persona acepte recibir WhatsApp.","codigo":"sin_consentimiento"}""")
        };

        var r = await Canal(servidor.BaseUrl).EnviarAsync("1155550001", "hola");

        Assert.False(r.Exito);
        Assert.Equal("sin_consentimiento", r.Codigo);
        Assert.Equal("No hay constancia de que la persona acepte recibir WhatsApp.", r.Error);
    }

    [Theory]
    [InlineData(200, """{"exito":true,"idExterno":"cola-9"}""")]
    [InlineData(503, """{"exito":false,"error":"Cola llena."}""")]
    public async Task Sin_codigo_en_la_respuesta_el_resultado_no_trae_codigo(int status, string cuerpo)
    {
        using var servidor = new ServidorQueRegistra { Responder = () => (status, cuerpo) };

        var r = await Canal(servidor.BaseUrl).EnviarAsync("1155550001", "hola");

        Assert.Null(r.Codigo);
    }
}

public class ServicioMensajesOpcionesTests
{
    private sealed class MensajesEnMemoria : IMensajeChatDAO
    {
        public readonly List<MensajeChat> Todos = new();
        public int Insertar(MensajeChat m) { m.IdMensaje = Todos.Count + 1; Todos.Add(m); return m.IdMensaje; }
        public void ActualizarEstadoEnvio(int idMensaje, string estado) => Todos.Single(x => x.IdMensaje == idMensaje).EstadoEnvio = estado;
        public List<MensajeChat> ObtenerPorConversacion(int idConversacion, int desdeIdMensaje) => new();
        public List<MensajeChat> ObtenerAnteriores(int idConversacion, int? antesDeIdMensaje, int cantidad) => new();
    }

    private sealed class CanalQueRegistra : IMessageChannel
    {
        public string Nombre => "Prueba";
        public readonly List<(string Telefono, string Texto, OpcionesEnvio Opciones)> Envios = new();
        public ResultadoEnvio Resultado { get; set; } = new(true, "x");

        public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto) => EnviarAsync(telefonoDestino, texto, OpcionesEnvio.Ninguna);

        public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto, OpcionesEnvio opciones)
        {
            Envios.Add((telefonoDestino, texto, opciones));
            return Task.FromResult(Resultado);
        }
    }

    private static (ServicioMensajes Servicio, MensajesEnMemoria Mensajes, CanalQueRegistra Canal, Conversacion Conversacion) Armar()
    {
        var mensajes = new MensajesEnMemoria();
        var canal = new CanalQueRegistra();
        var servicio = new ServicioMensajes(mensajes, canal, new RelojFijo(Escenario.Jueves0900), NullLogger<ServicioMensajes>.Instance);
        return (servicio, mensajes, canal, new Conversacion { IdConversacion = 7, Telefono = "1155550001", Canal = "Prueba" });
    }

    [Fact]
    public async Task Las_opciones_llegan_al_canal_y_sin_opciones_llega_Ninguna()
    {
        var (servicio, _, canal, c) = Armar();

        await servicio.EnviarAsync(c, "con", "Sistema", new OpcionesEnvio(OptIn: true));
        await servicio.EnviarAsync(c, "sin", "Sistema");

        Assert.Equal(new[] { true, false }, canal.Envios.Select(e => e.Opciones.OptIn));
        Assert.Equal(new[] { "con", "sin" }, canal.Envios.Select(e => e.Texto));
    }

    [Fact]
    public async Task Un_rechazo_deja_el_mensaje_en_Error_con_el_codigo_y_un_envio_bueno_sin_codigo()
    {
        var (servicio, mensajes, canal, c) = Armar();
        canal.Resultado = new ResultadoEnvio(false, Error: "no", Codigo: "sin_consentimiento");

        var rechazado = await servicio.EnviarAsync(c, "uno", "Sistema");
        canal.Resultado = new ResultadoEnvio(true, "x", Codigo: "algo");
        var enviado = await servicio.EnviarAsync(c, "dos", "Sistema");

        Assert.Equal(("Error", "sin_consentimiento"), (rechazado.EstadoEnvio, rechazado.CodigoDeError));
        Assert.Equal(("Enviado", (string?)null), (enviado.EstadoEnvio, enviado.CodigoDeError));
        Assert.Equal(new[] { "Error", "Enviado" }, mensajes.Todos.Select(m => m.EstadoEnvio));   // el estado que se guarda sigue siendo uno de los tres de siempre
    }

    [Fact]
    public void El_codigo_de_error_no_sale_en_las_respuestas_de_la_API()
    {
        var m = new MensajeChat { IdMensaje = 1, Texto = "hola", EstadoEnvio = "Error", CodigoDeError = "baja" };

        var json = JsonSerializer.Serialize(m, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("codigoDeError", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("baja", json);
        Assert.Contains("\"estadoEnvio\":\"Error\"", json);
    }

    [Fact]
    public async Task Un_canal_que_no_conoce_las_opciones_recibe_el_envio_de_siempre()
    {
        // CanalSimulado no implementa la sobrecarga con opciones: cae en la de siempre y no se entera.
        var canal = new CanalSimulado();

        var r = await ((IMessageChannel)canal).EnviarAsync("1155550001", "hola", new OpcionesEnvio(OptIn: true));

        Assert.True(r.Exito);
    }
}

public class BotBajaWhatsAppTests
{
    [Theory]
    [InlineData("BAJA")]
    [InlineData("baja")]
    [InlineData("  Baja.  ")]
    [InlineData("STOP")]
    [InlineData("no molestar")]
    [InlineData("Darme de baja")]
    [InlineData("¡basta!")]
    public async Task Una_baja_se_registra_para_ese_telefono_y_el_bot_no_contesta(string texto)
    {
        var e = new Escenario();

        var respuesta = await e.Bot.ProcesarAsync("+54 9 11 5555-0001", texto, "Simulado");

        Assert.Empty(respuesta.Respuestas);
        Assert.Equal(new[] { "1155550001" }, e.WhatsApp.Bajas);                          // los últimos 10 dígitos, como todo lo que llega por WhatsApp
        Assert.DoesNotContain(e.Mensajes.Mensajes, m => m.Direccion == "Saliente");      // y no se manda nada
        var entrante = Assert.Single(e.Mensajes.Mensajes);
        Assert.Equal(("Entrante", texto.Trim()), (entrante.Direccion, entrante.Texto));  // queda en el historial como cualquier mensaje
    }

    [Fact]
    public async Task La_baja_la_registra_cualquier_telefono_aunque_no_sea_de_un_paciente()
    {
        var e = new Escenario();

        await e.Bot.ProcesarAsync("351 412-3456", "BAJA", "Simulado");

        Assert.Equal(new[] { "3514123456" }, e.WhatsApp.Bajas);
    }

    [Fact]
    public async Task Una_baja_en_medio_de_pedir_un_turno_corta_igual_y_no_toca_el_flujo()
    {
        var e = new Escenario();
        await e.Enviar(Escenario.TelJuan, "hola");
        await e.Enviar(Escenario.TelJuan, "1");   // pedir turno

        var respuesta = await e.Bot.ProcesarAsync(Escenario.TelJuan, "stop", "Simulado");

        Assert.Empty(respuesta.Respuestas);
        Assert.Single(e.WhatsApp.Bajas);
    }

    [Theory]
    [InlineData("hola")]
    [InlineData("menu")]
    [InlineData("cancelar")]                         // es cancelar un turno
    [InlineData("3")]
    [InlineData("baja por favor")]                   // el mensaje tiene que ser ENTERO una palabra de baja
    [InlineData("quiero dar de baja mi turno")]
    [InlineData("alta")]
    [InlineData("no quiero seguir")]
    public async Task Lo_que_no_es_una_baja_sigue_su_camino_con_el_bot(string texto)
    {
        var e = new Escenario();

        var respuesta = await e.Bot.ProcesarAsync(Escenario.TelJuan, texto, "Simulado");

        Assert.Empty(e.WhatsApp.Bajas);
        Assert.NotEmpty(respuesta.Respuestas);       // el bot contestó algo
    }

    [Fact]
    public async Task La_seguridad_clinica_no_se_toca_una_frase_de_riesgo_sigue_activando_la_emergencia()
    {
        var e = new Escenario();

        var respuesta = await e.Bot.ProcesarAsync(Escenario.TelJuan, "no quiero vivir", "Simulado");

        Assert.Empty(e.WhatsApp.Bajas);
        Assert.NotEmpty(respuesta.Respuestas);
        Assert.Contains(e.Notificador.Eventos, ev => ev.StartsWith("Urgencia"));
    }

    [Fact]
    public async Task Una_baja_actualiza_la_actividad_de_la_conversacion()
    {
        var e = new Escenario();
        e.Reloj.Avanzar(TimeSpan.FromHours(3));

        await e.Bot.ProcesarAsync(Escenario.TelJuan, "BAJA", "Simulado");

        Assert.Equal(e.Reloj.Ahora, e.ConversacionDe(Escenario.TelJuan).FechaUltimaActividad);
    }
}
