using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// El recordatorio de turno NO sale por WhatsApp: ni por configuración ni por defecto, solo por mail y campanita. (Decisión de producto: el uso saliente de
// WhatsApp está pausado por el riesgo de bloqueo del número, y un recordatorio es justo lo programado y masivo.)
//
// Con el canal de WhatsApp de verdad armado (CanalBaileys, el que se registra con Mensajeria:CanalActivo=Baileys) y un manejador HTTP que anota cualquier
// pedido (no hay red), estas pruebas comprueban que durante un recordatorio no llega ningún pedido a whatsapp-service, en cualquiera de los tres casos
// del consentimiento del paciente (sin decisión, aceptó, revocó); que el mail y la campanita siguen como antes; y que un aviso por evento SÍ llega, así se
// sabe que el arnés vería el pedido si el recordatorio lo hiciera.
public class RecordatoriosSinWhatsAppTests
{
    private const string Url = "https://yosoy.test/ingreso";

    // ───────────── El arnés: el notificador y el servicio de recordatorios reales, con el canal de WhatsApp real y sin red ─────────────

    // Anota cada pedido que el canal le haría a whatsapp-service y contesta que sí. Nada sale de la máquina.
    private sealed class ManejadorEspia : HttpMessageHandler
    {
        public readonly List<(string Metodo, string Ruta, string Cuerpo)> Pedidos = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Pedidos) Pedidos.Add((request.Method.Method, request.RequestUri!.AbsolutePath, cuerpo));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"exito\":true,\"idExterno\":\"cola-1\"}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class MensajesEnMemoria : IMensajeChatDAO
    {
        public readonly List<MensajeChat> Todos = new();
        public int Insertar(MensajeChat m) { m.IdMensaje = Todos.Count + 1; Todos.Add(m); return m.IdMensaje; }
        public void ActualizarEstadoEnvio(int idMensaje, string estado) => Todos.Single(x => x.IdMensaje == idMensaje).EstadoEnvio = estado;
        public List<MensajeChat> ObtenerPorConversacion(int idConversacion, int desdeIdMensaje) => new();
        public List<MensajeChat> ObtenerAnteriores(int idConversacion, int? antesDeIdMensaje, int cantidad) => new();
        public bool TieneMensajeEntrante(int idConversacion) => Todos.Any(m => m.IdConversacion == idConversacion && m.Direccion == "Entrante");
    public DateTime? UltimoEntrante(int idConversacion) => Todos.Where(m => m.IdConversacion == idConversacion && m.Direccion == "Entrante" && !ComandoWhatsApp.EsBaja(m.Texto)).Select(m => (DateTime?)m.FechaHora).Max();
    }

    // El aviso con el teléfono y el mail del paciente (FakeDatos no los carga), y la campanita con su texto.
    private sealed class DatosConContacto : IDatosMensajeria
    {
        private readonly FakeDatos _datos;
        private readonly BaseEnMemoria _db;
        private readonly string? _mail;
        public readonly List<(int IdPaciente, string Titulo, string Mensaje)> Campanita = new();
        public DatosConContacto(FakeDatos datos, BaseEnMemoria db, string? mail) { _datos = datos; _db = db; _mail = mail; }

        public List<Paciente> PacientesPorTelefono(string tel) => _datos.PacientesPorTelefono(tel);
        public Paciente? PacienteActivoPorDni(string dni) => _datos.PacienteActivoPorDni(dni);
        public Paciente? ObtenerPaciente(int id) => _datos.ObtenerPaciente(id);
        public List<ProfesionalResumen> ProfesionalesDelPaciente(int idPaciente) => _datos.ProfesionalesDelPaciente(idPaciente);
        public List<Especialidad> EspecialidadesConAgenda() => _datos.EspecialidadesConAgenda();
        public List<ProfesionalResumen> ProfesionalesConAgenda(int idEspecialidad) => _datos.ProfesionalesConAgenda(idEspecialidad);
        public List<TurnoAviso> TurnosProximos(int idPaciente, DateTime ahora) => _datos.TurnosProximos(idPaciente, ahora);
        public int? BuscarIdTurno(int idPaciente, int idProfesional, DateTime fecha, TimeSpan hora) => _datos.BuscarIdTurno(idPaciente, idProfesional, fecha, hora);
        public void CrearNotificacionPaciente(int idPaciente, string titulo, string mensaje, string? link) => Campanita.Add((idPaciente, titulo, mensaje));

        public TurnoAviso? ObtenerTurnoAviso(int idTurno)
        {
            var aviso = _datos.ObtenerTurnoAviso(idTurno);
            if (aviso is not null)
            {
                aviso.TelefonoPaciente = _db.Pacientes.Single(p => p.IdPaciente == aviso.IdPaciente).Telefono;
                aviso.MailPaciente = _mail;
            }
            return aviso;
        }
    }

    // Reserva un aviso por canal, guarda con qué resultado quedó cada uno y con qué canales se pidieron los turnos sin recordar.
    private sealed class RecordatoriosQueRegistran : IRecordatorioDAO
    {
        private readonly Dictionary<int, string> _canales = new();
        public readonly List<(string Tipo, string Canal)> Reservados = new();
        public readonly List<(string Canal, string Estado, string? Error)> Resultados = new();
        public List<int> IdsPendientes { get; set; } = new();
        public IReadOnlyList<string>? CanalesPedidos { get; private set; }

        public int? Reservar(int idTurno, string tipo, string canal, DateTime fechaProgramada, int maxIntentos)
        {
            var id = _canales.Count + 1;
            _canales[id] = canal;
            Reservados.Add((tipo, canal));
            return id;
        }

        public void MarcarResultado(int idRecordatorio, string estado, string? error, DateTime ahora) => Resultados.Add((_canales[idRecordatorio], estado, error));
        public void RegistrarRespuesta(int idTurno, string tipo, string respuesta, DateTime ahora) { }

        public List<int> TurnosConfirmadosSinRecordar(DateTime desde, DateTime hasta, IReadOnlyList<string> canales, int maxIntentos)
        {
            CanalesPedidos = canales;
            return IdsPendientes;
        }
    }

    private sealed class InternasNulas : INotificacionInternaDAO
    {
        public int Insertar(NotificacionInterna n) => 0;
    }

    private sealed class Arnes
    {
        public readonly Escenario E = new();
        public readonly ManejadorEspia Red = new();
        public readonly MensajesEnMemoria Mensajes = new();
        public readonly DatosConContacto Datos;
        public readonly RecordatoriosQueRegistran Recordatorios = new();
        public readonly NotificadorTurnos Notificador;
        public readonly RecordatorioBackgroundService Servicio;
        public readonly Turno Turno;

        public Arnes(PermisoEnvioWhatsApp permiso = PermisoEnvioWhatsApp.SinDecision, string? mail = "paciente@example.com", string? telefono = Escenario.TelJuan)
        {
            E.Db.Pacientes.Single(p => p.IdPaciente == 1).Telefono = telefono!;
            E.WhatsApp.Permisos[1] = permiso;
            E.Opciones.CurrentValue.UrlIngresoTurno = Url;
            E.Opciones.CurrentValue.AvisosPorWhatsApp = true;   // el caso más expuesto: con los avisos por WhatsApp prendidos, el recordatorio igual no sale por ahí
            Turno = E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(1), BaseEnMemoria.Confirmado);   // mañana a las 9:00: dentro de las 24 horas

            Datos = new DatosConContacto(new FakeDatos(E.Db), E.Db, mail);
            // El canal de WhatsApp de verdad (el que se registra con Mensajeria:CanalActivo=Baileys), con un manejador que anota el pedido en vez de hacerlo.
            var canal = new CanalBaileys(new HttpClient(Red) { BaseAddress = new Uri("http://127.0.0.1:3002/") }, NullLogger<CanalBaileys>.Instance);
            var mensajes = new ServicioMensajes(Mensajes, canal, E.Reloj, NullLogger<ServicioMensajes>.Instance);
            // El correo sin configurar (como en desarrollo): el mail queda "Omitido" sin abrir ninguna conexión.
            var servicios = new ServiceCollection().AddSingleton(new EmailService(new ConfigurationBuilder().Build())).BuildServiceProvider();
            Notificador = new NotificadorTurnos(Datos, Recordatorios, new InternasNulas(), E.Conversaciones, mensajes, canal, E.WhatsApp,
                new FakeTurnoRepositorio(E.Db), servicios, new ConfigurationBuilder().Build(), E.Opciones, E.Reloj, NullLogger<NotificadorTurnos>.Instance);
            Servicio = new RecordatorioBackgroundService(Recordatorios, Notificador, E.Opciones, E.Reloj, NullLogger<RecordatorioBackgroundService>.Instance);
        }

        public bool SalioAlgoPorWhatsApp => Red.Pedidos.Count > 0 || Mensajes.Todos.Count > 0 || E.Conversaciones.Todas.Count > 0 || E.WhatsApp.Consultas.Count > 0;
    }

    // ───────────── El recordatorio no sale por WhatsApp, pase lo que pase con el consentimiento ─────────────

    [Theory]
    [InlineData(PermisoEnvioWhatsApp.SinDecision)]
    [InlineData(PermisoEnvioWhatsApp.ConConsentimiento)]    // el peor caso: aceptó los avisos, y igual el recordatorio no sale por WhatsApp
    [InlineData(PermisoEnvioWhatsApp.Revocado)]
    public void Con_el_canal_de_WhatsApp_armado_el_recordatorio_no_manda_ningun_pedido_a_whatsapp_service(PermisoEnvioWhatsApp permiso)
    {
        var a = new Arnes(permiso);

        a.Notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

        Assert.Empty(a.Red.Pedidos);                    // ni un pedido a whatsapp-service
        Assert.Empty(a.Mensajes.Todos);                 // ni un mensaje guardado en el chat
        Assert.Empty(a.E.Conversaciones.Todas);         // ni se armó la conversación
        Assert.Empty(a.E.WhatsApp.Consultas);           // ni se consultó el consentimiento: el camino de WhatsApp ni se toca
        Assert.False(a.SalioAlgoPorWhatsApp);
    }

    [Fact]
    public void El_recordatorio_reserva_solo_el_mail_y_la_campanita_y_nunca_una_fila_de_Chat()
    {
        var a = new Arnes(PermisoEnvioWhatsApp.ConConsentimiento);

        a.Notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

        Assert.Equal(new[] { ("Turno24h", "Email"), ("Turno24h", "App") }, a.Recordatorios.Reservados.ToArray());
        Assert.DoesNotContain(a.Recordatorios.Resultados, r => r.Canal == "Chat");
    }

    [Fact]
    public void La_campanita_y_el_mail_siguen_igual_con_el_texto_completo_y_ya_no_hablan_de_responder_el_mensaje_del_chat()
    {
        var a = new Arnes();

        a.Notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

        var campanita = Assert.Single(a.Datos.Campanita);
        Assert.Equal((1, "Recordatorio de turno"), (campanita.IdPaciente, campanita.Titulo));
        Assert.Contains("Santiago Manto", campanita.Mensaje);                                      // con quién es: es el panel, con sesión
        Assert.Contains("(Virtual)", campanita.Mensaje);
        Assert.Contains(Formato.FechaHora(a.Turno.Fecha.Date + a.Turno.Hora), campanita.Mensaje);
        Assert.Contains(Url, campanita.Mensaje);
        Assert.Contains("Si no vas a poder asistir, avisanos desde la web.", campanita.Mensaje);
        Assert.DoesNotContain("chat", campanita.Mensaje, StringComparison.OrdinalIgnoreCase);     // el mensaje del chat ya no existe

        Assert.Equal(new[] { ("Email", "Omitido"), ("App", "Enviado") }, a.Recordatorios.Resultados.Select(r => (r.Canal, r.Estado)).ToArray());
        Assert.Equal("EmailService no está configurado.", a.Recordatorios.Resultados.Single(r => r.Canal == "Email").Error);   // el mail se intenta como siempre
    }

    [Fact]
    public void Un_paciente_sin_mail_ni_telefono_recibe_igual_la_campanita_y_el_mail_se_omite_como_siempre()
    {
        var a = new Arnes(mail: null, telefono: null);

        a.Notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

        Assert.Single(a.Datos.Campanita);
        Assert.Equal("El paciente no tiene mail cargado.", a.Recordatorios.Resultados.Single(r => r.Canal == "Email").Error);
        Assert.False(a.SalioAlgoPorWhatsApp);
    }

    [Fact]
    public void Un_recordatorio_de_un_turno_que_ya_no_esta_confirmado_no_manda_nada_a_ningun_canal()
    {
        var a = new Arnes();
        a.E.Db.Turnos.Single(t => t.IdTurno == a.Turno.IdTurno).IdEstadoTurno = BaseEnMemoria.Cancelado;

        a.Notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

        Assert.Empty(a.Recordatorios.Reservados);
        Assert.Empty(a.Datos.Campanita);
        Assert.False(a.SalioAlgoPorWhatsApp);
    }

    // ───────────── El servicio de recordatorios ─────────────

    [Theory]
    [InlineData(PermisoEnvioWhatsApp.SinDecision)]
    [InlineData(PermisoEnvioWhatsApp.ConConsentimiento)]
    public void El_servicio_de_recordatorios_pide_solo_Email_y_App_y_no_le_manda_nada_a_whatsapp_service(PermisoEnvioWhatsApp permiso)
    {
        var a = new Arnes(permiso);
        a.Recordatorios.IdsPendientes = new List<int> { a.Turno.IdTurno };

        var procesados = a.Servicio.EjecutarUnaVez();

        Assert.Equal(1, procesados);
        Assert.Equal(new[] { "Email", "App" }, a.Recordatorios.CanalesPedidos);       // los canales que se buscan: sin "Chat"
        Assert.Empty(a.Red.Pedidos);
        Assert.False(a.SalioAlgoPorWhatsApp);
        Assert.Single(a.Datos.Campanita);                                             // y la campanita salió
        Assert.Equal(new[] { "Email", "App" }, a.Recordatorios.Resultados.Select(r => r.Canal).ToArray());
    }

    [Fact]
    public void El_servicio_de_recordatorios_con_varios_turnos_no_manda_nada_a_whatsapp_service()
    {
        var a = new Arnes(PermisoEnvioWhatsApp.ConConsentimiento);
        var otro = a.E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(1).AddHours(1), BaseEnMemoria.Confirmado);
        a.Recordatorios.IdsPendientes = new List<int> { a.Turno.IdTurno, otro.IdTurno };

        Assert.Equal(2, a.Servicio.EjecutarUnaVez());

        Assert.Empty(a.Red.Pedidos);
        Assert.Equal(2, a.Datos.Campanita.Count);
        Assert.False(a.SalioAlgoPorWhatsApp);
    }

    // ───────────── El control: un aviso por evento SÍ llega, así se sabe que el arnés vería el pedido ─────────────

    [Fact]
    public void Un_aviso_por_evento_con_el_mismo_arnes_si_manda_un_pedido_a_whatsapp_service()
    {
        var a = new Arnes(PermisoEnvioWhatsApp.ConConsentimiento);

        a.Notificador.TurnoConfirmado(a.Turno.IdTurno);

        var pedido = Assert.Single(a.Red.Pedidos);
        Assert.Equal(("POST", "/enviar"), (pedido.Metodo, pedido.Ruta));
        var cuerpo = JsonDocument.Parse(pedido.Cuerpo).RootElement;                               // el texto viaja escapado dentro del JSON
        Assert.True(cuerpo.GetProperty("optIn").GetBoolean());
        Assert.Equal(PlantillasWhatsApp.TurnoConfirmado(a.Turno.Fecha.Date + a.Turno.Hora), cuerpo.GetProperty("texto").GetString());
        Assert.Single(a.Mensajes.Todos);
    }

    // ───────────── No hay forma de prenderlos: ni por configuración, ni por código, ni por defecto ─────────────

    [Fact]
    public void Los_canales_del_recordatorio_son_Email_y_App_y_la_lista_es_de_solo_lectura()
    {
        Assert.Equal(new[] { "Email", "App" }, NotificadorTurnos.CanalesRecordatorio);
        Assert.DoesNotContain("Chat", NotificadorTurnos.CanalesRecordatorio);
        Assert.DoesNotContain(NotificadorTurnos.CanalesRecordatorio, c => c.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase));
        // Del tipo IReadOnlyList: nadie puede hacer CanalesRecordatorio[0] = "Chat" ni agregarle un canal.
        var campo = typeof(NotificadorTurnos).GetField(nameof(NotificadorTurnos.CanalesRecordatorio))!;
        Assert.Equal(typeof(IReadOnlyList<string>), campo.FieldType);
        Assert.True(campo.IsInitOnly);
    }

    private static readonly Regex ClaveQueMezclaRecordatorioYWhatsApp = new(
        @"recordatorio.*(whatsapp|chat|canal|baileys)|(whatsapp|chat|canal|baileys).*recordatorio", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Ninguna_opcion_de_la_aplicacion_mezcla_los_recordatorios_con_WhatsApp()
    {
        // Las propiedades de MensajeriaOptions (lo que se puede configurar con Mensajeria:...).
        var propiedades = typeof(MensajeriaOptions).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("RecordatorioHorasAntes", propiedades);          // la que existe: cuántas horas antes (la de mail y campanita)
        Assert.DoesNotContain(propiedades, n => ClaveQueMezclaRecordatorioYWhatsApp.IsMatch(n));

        // Y las claves que trae appsettings.json, a cualquier profundidad.
        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var claves = new List<string>();
        void Recorrer(JsonElement e, string ruta)
        {
            if (e.ValueKind != JsonValueKind.Object) return;
            foreach (var p in e.EnumerateObject()) { claves.Add($"{ruta}:{p.Name}"); Recorrer(p.Value, $"{ruta}:{p.Name}"); }
        }
        Recorrer(json.RootElement, "");
        Assert.Contains(":Mensajeria:RecordatorioHorasAntes", claves);
        Assert.DoesNotContain(claves, k => ClaveQueMezclaRecordatorioYWhatsApp.IsMatch(k));
    }

    [Fact]
    public void El_codigo_del_recordatorio_no_tiene_texto_ni_plantilla_de_WhatsApp()
    {
        var fuente = File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "Services", "Mensajeria", "NotificadorTurnos.cs")).Replace("\r\n", "\n");
        var inicio = fuente.IndexOf("public void EnviarRecordatorio24h", StringComparison.Ordinal);
        Assert.True(inicio > 0, "no encuentro EnviarRecordatorio24h");
        var metodo = fuente[inicio..fuente.IndexOf("// El de la campanita", inicio, StringComparison.Ordinal)];

        Assert.Contains("textoChat: null", metodo);
        Assert.DoesNotContain("PlantillasWhatsApp", metodo);
        Assert.DoesNotContain("CanalesEvento", metodo);
        Assert.Contains("CanalesRecordatorio", metodo);

        // Y ninguna plantilla de WhatsApp se llama como un recordatorio.
        Assert.DoesNotContain(typeof(PlantillasWhatsApp).GetMethods(), m => m.Name.Contains("Recordatorio", StringComparison.OrdinalIgnoreCase));
    }
}

// Las filas de Recordatorio con la base de verdad: qué turnos faltan por recordar (contando solo los canales que quedan) y el recorrido completo con los
// DAOs reales y el canal de WhatsApp armado. Cada prueba crea sus filas con DatosDePrueba (con una fecha lejana y propia) y las borra al terminar.
[Trait("Categoria", "Integracion")]
public class RecordatoriosSinWhatsAppBaseRealTests
{
    private static readonly string[] EmailYApp = { "Email", "App" };

    // Un momento lejano y casi seguro propio de esta prueba: nadie más tiene turnos ahí, y la ventana de búsqueda es de dos minutos.
    private static DateTime MomentoPropio() =>
        new DateTime(2040, 1, 1, 10, 0, 0).AddDays(Random.Shared.Next(0, 3000)).AddMinutes(Random.Shared.Next(0, 6) * 5);

    private static (int Paciente, int Turno, DateTime Momento) TurnoConfirmado(DatosDePrueba d, DateTime? momento = null)
    {
        var m = momento ?? MomentoPropio();
        var paciente = d.Paciente(mail: d.Marca + "@example.com");
        var turno = d.Turno(paciente, d.Profesional(), "Confirmado", m.Date, m.TimeOfDay);
        return (paciente, turno, m);
    }

    private static void FilaDeRecordatorio(int idTurno, string canal, string estado, int intentos, DateTime momento) =>
        DatosDePrueba.Ejecutar(
            "INSERT INTO Recordatorio (IdTurno, Tipo, Canal, FechaProgramada, Estado, Intentos) VALUES (@t, 'Turno24h', @c, @f, @e, @i)",
            ("@t", idTurno), ("@c", canal), ("@f", momento.AddDays(-1)), ("@e", estado), ("@i", intentos));

    private static bool SeBusca(int idTurno, DateTime momento, IReadOnlyList<string> canales, int maxIntentos = 3) =>
        new RecordatorioDAO().TurnosConfirmadosSinRecordar(momento.AddMinutes(-1), momento.AddMinutes(1), canales, maxIntentos).Contains(idTurno);

    // ───────────── TurnosConfirmadosSinRecordar cuenta solo los canales que se piden ─────────────

    [Fact]
    public void Un_turno_confirmado_sin_ninguna_fila_se_busca()
    {
        using var d = new DatosDePrueba();
        var (_, turno, momento) = TurnoConfirmado(d);

        Assert.True(SeBusca(turno, momento, EmailYApp));
    }

    [Fact]
    public void Con_el_mail_y_la_campanita_resueltos_el_turno_ya_no_se_busca()
    {
        using var d = new DatosDePrueba();
        var (_, turno, momento) = TurnoConfirmado(d);
        FilaDeRecordatorio(turno, "Email", "Enviado", 1, momento);
        FilaDeRecordatorio(turno, "App", "Enviado", 1, momento);

        Assert.False(SeBusca(turno, momento, EmailYApp));
    }

    [Theory]
    [InlineData("Enviado")]
    [InlineData("Omitido")]
    public void Un_canal_enviado_u_omitido_esta_resuelto_pero_el_otro_todavia_falta(string estado)
    {
        using var d = new DatosDePrueba();
        var (_, turno, momento) = TurnoConfirmado(d);
        FilaDeRecordatorio(turno, "App", estado, 1, momento);

        Assert.True(SeBusca(turno, momento, EmailYApp));     // falta el mail
    }

    [Fact]
    public void Un_canal_que_agoto_los_intentos_cuenta_como_resuelto_y_uno_con_intentos_de_sobra_no()
    {
        using var d = new DatosDePrueba();
        var (_, turno, momento) = TurnoConfirmado(d);
        FilaDeRecordatorio(turno, "App", "Enviado", 1, momento);
        FilaDeRecordatorio(turno, "Email", "Error", 1, momento);               // 1 intento de 3: se reintenta

        Assert.True(SeBusca(turno, momento, EmailYApp, maxIntentos: 3));

        DatosDePrueba.Ejecutar("UPDATE Recordatorio SET Intentos = 3 WHERE IdTurno = @t AND Canal = 'Email'", ("@t", turno));
        Assert.False(SeBusca(turno, momento, EmailYApp, maxIntentos: 3));      // agotó los 3: ya no se insiste
    }

    [Fact]
    public void Una_fila_vieja_de_Chat_enviada_no_hace_pasar_por_resuelto_a_un_mail_que_falta_reintentar()
    {
        // El caso que un conteo "de cualquier canal" rompía al sacar el Chat: el recordatorio quedó a medias antes de pasar a esta versión (el WhatsApp salió,
        // la campanita también y el mail falló): con 2 canales en vez de 3, esas 2 filas resueltas parecían "todo resuelto" y el mail nunca se reintentaba.
        using var d = new DatosDePrueba();
        var (_, turno, momento) = TurnoConfirmado(d);
        FilaDeRecordatorio(turno, "Chat", "Enviado", 1, momento);
        FilaDeRecordatorio(turno, "App", "Enviado", 1, momento);
        FilaDeRecordatorio(turno, "Email", "Error", 1, momento);

        Assert.True(SeBusca(turno, momento, EmailYApp), "el mail tiene que seguir reintentándose");
    }

    [Fact]
    public void Con_todo_resuelto_incluida_una_fila_vieja_de_Chat_el_turno_no_se_busca()
    {
        using var d = new DatosDePrueba();
        var (_, turno, momento) = TurnoConfirmado(d);
        FilaDeRecordatorio(turno, "Chat", "Enviado", 1, momento);
        FilaDeRecordatorio(turno, "Email", "Enviado", 1, momento);
        FilaDeRecordatorio(turno, "App", "Enviado", 1, momento);

        Assert.False(SeBusca(turno, momento, EmailYApp));
    }

    [Fact]
    public void Solo_una_fila_vieja_de_Chat_no_alcanza_el_mail_y_la_campanita_siguen_faltando()
    {
        using var d = new DatosDePrueba();
        var (_, turno, momento) = TurnoConfirmado(d);
        FilaDeRecordatorio(turno, "Chat", "Enviado", 1, momento);

        Assert.True(SeBusca(turno, momento, EmailYApp));
    }

    [Fact]
    public void Sin_canales_no_se_busca_ningun_turno_y_con_un_turno_que_no_esta_confirmado_tampoco()
    {
        using var d = new DatosDePrueba();
        var (paciente, turno, momento) = TurnoConfirmado(d);
        Assert.Empty(new RecordatorioDAO().TurnosConfirmadosSinRecordar(momento.AddMinutes(-1), momento.AddMinutes(1), Array.Empty<string>(), 3));

        var cancelado = d.Turno(paciente, d.Profesional(), "Cancelado", momento.Date, momento.TimeOfDay.Add(TimeSpan.FromMinutes(1)));
        Assert.DoesNotContain(cancelado, new RecordatorioDAO().TurnosConfirmadosSinRecordar(momento.AddMinutes(-1), momento.AddMinutes(3), EmailYApp, 3));
        Assert.True(SeBusca(turno, momento, EmailYApp));
    }

    [Fact]
    public void Un_turno_fuera_de_la_ventana_no_se_busca()
    {
        using var d = new DatosDePrueba();
        var (_, turno, momento) = TurnoConfirmado(d);

        Assert.DoesNotContain(turno, new RecordatorioDAO().TurnosConfirmadosSinRecordar(momento.AddHours(1), momento.AddHours(2), EmailYApp, 3));
        Assert.DoesNotContain(turno, new RecordatorioDAO().TurnosConfirmadosSinRecordar(momento.AddHours(-2), momento.AddHours(-1), EmailYApp, 3));
    }

    // ───────────── De punta a punta: la base real, los DAOs reales y el canal de WhatsApp armado ─────────────

    private sealed class ManejadorEspia : HttpMessageHandler
    {
        public readonly List<(string Metodo, string Ruta)> Pedidos = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Pedidos) Pedidos.Add((request.Method.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"exito\":true,\"idExterno\":\"cola-1\"}", Encoding.UTF8, "application/json")
            });
        }
    }

    private static (RecordatorioBackgroundService Servicio, NotificadorTurnos Notificador, ManejadorEspia Red) ArmarConLaBaseReal(DateTime ahora)
    {
        var red = new ManejadorEspia();
        var reloj = new RelojFijo(ahora);
        var canal = new CanalBaileys(new HttpClient(red) { BaseAddress = new Uri("http://127.0.0.1:3002/") }, NullLogger<CanalBaileys>.Instance);
        var mensajes = new ServicioMensajes(new MensajeChatDAO(), canal, reloj, NullLogger<ServicioMensajes>.Instance);
        var politica = new FakePoliticaWhatsApp();
        var opciones = new OpcionesFijas();
        opciones.CurrentValue.UrlIngresoTurno = Url;
        opciones.CurrentValue.AvisosPorWhatsApp = true;
        var servicios = new ServiceCollection().AddSingleton(new EmailService(new ConfigurationBuilder().Build())).BuildServiceProvider();
        var recordatorios = new RecordatorioDAO();
        var notificador = new NotificadorTurnos(new DatosMensajeriaDAO(), recordatorios, new NotificacionInternaDAO(), new ConversacionDAO(), mensajes, canal,
            new PoliticaAceptoSiempre(), new TurnoRepositorioDAO(), servicios, new ConfigurationBuilder().Build(), opciones, reloj,
            NullLogger<NotificadorTurnos>.Instance);
        return (new RecordatorioBackgroundService(recordatorios, notificador, opciones, reloj, NullLogger<RecordatorioBackgroundService>.Instance), notificador, red);
    }

    private const string Url = "https://yosoy.test/ingreso";

    // El peor caso: todos los pacientes aceptaron los avisos por WhatsApp.
    private sealed class PoliticaAceptoSiempre : IPoliticaEnvioWhatsApp
    {
        public PermisoEnvioWhatsApp Evaluar(int idPaciente, string? telefonoActual) => PermisoEnvioWhatsApp.ConConsentimiento;
    }

    [Fact]
    public void Con_la_base_real_y_el_canal_de_WhatsApp_armado_el_recordatorio_sale_por_mail_y_campanita_y_ningun_pedido_llega_a_whatsapp_service()
    {
        using var d = new DatosDePrueba();
        var momento = MomentoPropio();
        var (paciente, turno, _) = TurnoConfirmado(d, momento);
        var telefono = "11" + Random.Shared.Next(10_000_000, 99_999_999);
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Telefono = @t WHERE IdPaciente = @p", ("@t", telefono), ("@p", paciente));
        d.AlLimpiar("Conversacion", "Telefono", telefono);                                        // por si algo (mal) creara una conversación
        var (servicio, _, red) = ArmarConLaBaseReal(momento.AddHours(-20));                       // 20 horas antes: dentro de las 24

        var procesados = servicio.EjecutarUnaVez();

        Assert.True(procesados >= 1, "el turno estaba dentro de la ventana");
        Assert.Empty(red.Pedidos);                                                                // ningún pedido a whatsapp-service
        var filas = ListarFilas(turno);
        Assert.Equal(new[] { "App:Enviado", "Email:Omitido" }, filas);                           // solo el mail (sin configurar) y la campanita
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p", ("@p", paciente)));   // la campanita salió
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Conversacion WHERE Telefono = @t", ("@t", telefono)));       // y no se armó ninguna conversación

        // Es idempotente, como siempre: una segunda pasada no repite nada.
        Assert.DoesNotContain(turno, new RecordatorioDAO().TurnosConfirmadosSinRecordar(momento.AddMinutes(-1), momento.AddMinutes(1), EmailYApp, 3));
        servicio.EjecutarUnaVez();
        Assert.Equal(new[] { "App:Enviado", "Email:Omitido" }, ListarFilas(turno));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p", ("@p", paciente)));
        Assert.Empty(red.Pedidos);
    }

    [Fact]
    public void Las_filas_viejas_de_Chat_no_se_tocan_y_el_mail_pendiente_se_reintenta()
    {
        using var d = new DatosDePrueba();
        var momento = MomentoPropio();
        var (paciente, turno, _) = TurnoConfirmado(d, momento);
        var telefono = "11" + Random.Shared.Next(10_000_000, 99_999_999);
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Telefono = @t WHERE IdPaciente = @p", ("@t", telefono), ("@p", paciente));
        d.AlLimpiar("Conversacion", "Telefono", telefono);
        // El recordatorio había quedado a medias con la versión anterior: el WhatsApp y la campanita salieron y el mail falló una vez.
        FilaDeRecordatorio(turno, "Chat", "Enviado", 1, momento);
        FilaDeRecordatorio(turno, "App", "Enviado", 1, momento);
        FilaDeRecordatorio(turno, "Email", "Error", 1, momento);
        var (servicio, _, red) = ArmarConLaBaseReal(momento.AddHours(-3));

        servicio.EjecutarUnaVez();

        Assert.Empty(red.Pedidos);                                                                // el reintento del mail no abre WhatsApp
        Assert.Equal(new[] { "App:Enviado", "Chat:Enviado", "Email:Omitido" }, ListarFilas(turno));   // la fila vieja de Chat sigue igual; el mail se reintentó ("Omitido": correo sin configurar)
    }

    [Fact]
    public void Control_un_aviso_por_evento_con_el_mismo_armado_si_llega_a_whatsapp_service()
    {
        // Para que se sepa que el espía vería un pedido de WhatsApp si el recordatorio lo hiciera.
        using var d = new DatosDePrueba();
        var momento = MomentoPropio();
        var (paciente, turno, _) = TurnoConfirmado(d, momento);
        var telefono = "11" + Random.Shared.Next(10_000_000, 99_999_999);
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Telefono = @t WHERE IdPaciente = @p", ("@t", telefono), ("@p", paciente));
        d.AlLimpiar("Conversacion", "Telefono", telefono);
        var (_, notificador, red) = ArmarConLaBaseReal(momento.AddHours(-20));

        notificador.TurnoConfirmado(turno);

        Assert.Equal(("POST", "/enviar"), Assert.Single(red.Pedidos));
        Assert.Equal(1, DatosDePrueba.Contar(
            "SELECT COUNT(*) FROM MensajeChat m JOIN Conversacion c ON c.IdConversacion = m.IdConversacion WHERE c.Telefono = @t AND m.EstadoEnvio = 'Enviado'",
            ("@t", telefono)));                                                                    // quedó en el chat de esa persona
        Assert.Equal(1, DatosDePrueba.Contar(
            "SELECT COUNT(*) FROM Recordatorio WHERE IdTurno = @t AND Tipo = 'TurnoConfirmado' AND Canal = 'Chat' AND Estado = 'Enviado'", ("@t", turno)));
        Assert.Empty(ListarFilas(turno));                                                         // y no tocó el recordatorio (Turno24h)
    }

    private static string[] ListarFilas(int idTurno)
    {
        var filas = new List<string>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT Canal, Estado FROM Recordatorio WHERE IdTurno = @t AND Tipo = 'Turno24h' ORDER BY Canal", con);
        cmd.Parameters.AddWithValue("@t", idTurno);
        using var r = cmd.ExecuteReader();
        while (r.Read()) filas.Add($"{r.GetString(0)}:{r.GetString(1)}");
        return filas.ToArray();
    }
}
