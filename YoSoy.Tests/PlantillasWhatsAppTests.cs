using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Los avisos que salen SOLOS por WhatsApp (PlantillasWhatsApp: solicitud, turno propuesto, confirmado y cancelado) dicen el día y la hora de un turno y
// nada más: ni el nombre del profesional, ni la especialidad, ni el motivo de la consulta, ni la modalidad. La campanita del panel (con sesión) sigue con
// el texto completo. El recordatorio NO está acá: no sale por WhatsApp (RecordatoriosSinWhatsAppTests).
//  · Por la firma: ninguna plantilla recibe otra cosa que el momento del turno y el link de ingreso.
//  · Por el texto: dicen el día y la hora, y no traen ninguna palabra que revele la atención.
//  · De punta a punta: con un turno cargado de datos "marcados" (profesional, especialidad, modalidad, motivo, paciente), lo que sale por el
//    chat no trae ninguna marca, y el chat recibe exactamente la plantilla. La campanita SÍ trae la del profesional: así se sabe que la marca,
//    de colarse, se vería.
public class PlantillasWhatsAppTests
{
    private static readonly DateTime Momento = new(2031, 3, 4, 10, 30, 0);
    private const string Url = "https://yosoy.test/ingreso";

    // Palabras que no pueden aparecer en un aviso que sale solo por WhatsApp: revelan (o rozan) qué atención recibe la persona. "profesional" está
    // en la lista a propósito: hoy ningún aviso lo necesita, y si alguno lo llega a necesitar tiene que ser una decisión consciente.
    private static readonly Regex Prohibidas = new(
        @"profesional|especialidad|motivo|consulta|psic|psiq|terapi|tratamiento|diagn|salud mental|modalidad|virtual|presencial|videollamada|\bsala\b|\bdr\.|\bdra\.|\blic\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<(string Nombre, string Texto)> Todas() => new[]
    {
        (nameof(PlantillasWhatsApp.SolicitudRecibida), PlantillasWhatsApp.SolicitudRecibida(Momento)),
        (nameof(PlantillasWhatsApp.TurnoPropuesto), PlantillasWhatsApp.TurnoPropuesto(Momento, Url)),
        (nameof(PlantillasWhatsApp.TurnoConfirmado), PlantillasWhatsApp.TurnoConfirmado(Momento)),
        (nameof(PlantillasWhatsApp.TurnoCancelado), PlantillasWhatsApp.TurnoCancelado(Momento))
    };

    // ───────────── La firma es la garantía ─────────────

    [Fact]
    public void Ninguna_plantilla_recibe_otra_cosa_que_el_momento_y_el_link_de_ingreso()
    {
        var metodos = typeof(PlantillasWhatsApp).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        // Las cuatro de hoy (no hay una de recordatorio: no sale por WhatsApp). Una plantilla nueva obliga a venir acá, y a decidir si lo que recibe puede salir por WhatsApp.
        Assert.Equal(new[] { "SolicitudRecibida", "TurnoCancelado", "TurnoConfirmado", "TurnoPropuesto" },
            metodos.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        foreach (var m in metodos)
        {
            Assert.Equal(typeof(string), m.ReturnType);
            var p = m.GetParameters();
            Assert.True(p.Length is 1 or 2, $"{m.Name}: recibe {p.Length} parámetros");
            Assert.True(p[0].ParameterType == typeof(DateTime) && p[0].Name == "momento", $"{m.Name}: el primer parámetro tiene que ser el momento del turno");
            if (p.Length == 2)
                Assert.True(p[1].ParameterType == typeof(string) && p[1].Name == "urlIngreso", $"{m.Name}: el segundo, si hay, es el link de ingreso");
        }
        Assert.Empty(typeof(PlantillasWhatsApp).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string) && !f.IsLiteral));   // y no hay textos sueltos que se puedan cambiar desde afuera
    }

    // ───────────── El texto ─────────────

    [Fact]
    public void Cada_plantilla_dice_el_dia_y_la_hora()
    {
        foreach (var (nombre, texto) in Todas())
            Assert.True(texto.Contains(Formato.FechaHora(Momento), StringComparison.Ordinal), $"{nombre} no dice el día y la hora: {texto}");
        Assert.Equal("martes 04/03 a las 10:30", Formato.FechaHora(Momento));
    }

    [Fact]
    public void Ninguna_plantilla_trae_una_palabra_que_revele_la_atencion()
    {
        foreach (var (nombre, texto) in Todas())
        {
            var m = Prohibidas.Match(texto);
            Assert.False(m.Success, $"{nombre} dice \"{m.Value}\": {texto}");
        }
    }

    [Fact]
    public void Solo_las_que_lo_necesitan_traen_el_link_y_es_el_que_se_les_paso()
    {
        Assert.Contains(Url, PlantillasWhatsApp.TurnoPropuesto(Momento, Url));
        foreach (var texto in new[] { PlantillasWhatsApp.SolicitudRecibida(Momento), PlantillasWhatsApp.TurnoConfirmado(Momento), PlantillasWhatsApp.TurnoCancelado(Momento) })
            Assert.DoesNotContain("http", texto);
    }

    [Fact]
    public void El_aviso_de_cancelacion_dice_como_pedir_otro_turno_y_el_de_confirmacion_no_pide_nada()
    {
        // "menu" es la palabra con la que el asistente arma el menú (BotReservasService.TextoMenu).
        Assert.Contains("\"menu\"", PlantillasWhatsApp.TurnoCancelado(Momento));
        Assert.Contains("Para confirmarlo", PlantillasWhatsApp.TurnoPropuesto(Momento, Url));
        Assert.DoesNotContain("Respondé", PlantillasWhatsApp.TurnoConfirmado(Momento));
        Assert.DoesNotContain("Respondé", PlantillasWhatsApp.SolicitudRecibida(Momento));
    }

    [Fact]
    public void Ninguna_plantilla_es_larga_ni_trae_saltos_de_mas()
    {
        // Un aviso es una o dos líneas: lo que se lee en la pantalla bloqueada.
        foreach (var (nombre, texto) in Todas())
        {
            Assert.True(texto.Length <= 300, $"{nombre} tiene {texto.Length} caracteres");
            Assert.DoesNotContain("\r", texto);
            Assert.DoesNotContain("\n\n\n", texto);
        }
    }

    [Fact]
    public void No_hay_una_plantilla_de_recordatorio_porque_el_recordatorio_no_sale_por_WhatsApp()
    {
        Assert.DoesNotContain(typeof(PlantillasWhatsApp).GetMethods(BindingFlags.Public | BindingFlags.Static),
            m => m.Name.Contains("Recordatorio", StringComparison.OrdinalIgnoreCase));
    }

    // ───────────── De punta a punta con el notificador ─────────────

    private const string MarcaProfesional = "MarcaProfesional";
    private const string MarcaEspecialidad = "MarcaEspecialidad";
    private const string MarcaModalidad = "MarcaModalidad";
    private const string MarcaMotivo = "MarcaMotivo";
    private const string MarcaPaciente = "MarcaPaciente";
    private static readonly string[] Marcas = { MarcaProfesional, MarcaEspecialidad, MarcaModalidad, MarcaMotivo, MarcaPaciente };

    // FakeDatos no carga el teléfono del paciente en el aviso (se lo agrega) y de la campanita solo guarda el título: acá se guarda el texto.
    private sealed class DatosCapturando : IDatosMensajeria
    {
        private readonly FakeDatos _datos;
        private readonly BaseEnMemoria _db;
        public readonly List<(int IdPaciente, string Titulo, string Mensaje)> Campanita = new();
        public DatosCapturando(FakeDatos datos, BaseEnMemoria db) { _datos = datos; _db = db; }

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
            if (aviso is not null) aviso.TelefonoPaciente = _db.Pacientes.Single(p => p.IdPaciente == aviso.IdPaciente).Telefono;
            return aviso;
        }
    }

    private sealed class RecordatoriosDePrueba : IRecordatorioDAO
    {
        public int? Reservar(int idTurno, string tipo, string canal, DateTime fechaProgramada, int maxIntentos) => 1;
        public void MarcarResultado(int idRecordatorio, string estado, string? error, DateTime ahora) { }
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
        public readonly DatosCapturando Datos;
        public readonly NotificadorTurnos Notificador;
        public readonly Turno Turno;

        public Arnes(int idEstado)
        {
            // Todo lo del turno que NO es el día y la hora, con una marca que se reconoce si aparece donde no tiene que estar.
            E.Db.Pacientes.Single(p => p.IdPaciente == 1).Nombre = MarcaPaciente + " García";
            var profesional = E.Db.Profesionales.Single(p => p.IdProfesional == 10);
            profesional.Nombre = "Dra. " + MarcaProfesional;
            profesional.Especialidad = MarcaEspecialidad;
            E.Opciones.CurrentValue.UrlIngresoTurno = Url;
            E.Opciones.CurrentValue.AvisosPorWhatsApp = true;   // estas pruebas son de lo que SALE por WhatsApp: la llave viene apagada por defecto
            E.WhatsApp.Permisos[1] = PermisoEnvioWhatsApp.ConConsentimiento;

            Turno = E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(2), idEstado);
            Turno.Modalidad = MarcaModalidad;
            Turno.Motivo = MarcaMotivo;

            Datos = new DatosCapturando(new FakeDatos(E.Db), E.Db);
            Notificador = new NotificadorTurnos(Datos, new RecordatoriosDePrueba(), new InternasNulas(), E.Conversaciones, E.Mensajes,
                new CanalSimulado(), E.WhatsApp, new FakeTurnoRepositorio(E.Db), new ServiceCollection().BuildServiceProvider(),
                new ConfigurationBuilder().Build(), E.Opciones, E.Reloj, new LogsCapturados().CreateLogger<NotificadorTurnos>());
        }

        public DateTime MomentoDelTurno => Turno.Fecha.Date + Turno.Hora;
        public List<MensajeChat> Salientes => E.Mensajes.Mensajes.Where(m => m.Direccion == "Saliente").ToList();
    }

    // Cada aviso con el estado que necesita el turno, lo que dispara y la plantilla que se espera por el chat.
    public static IEnumerable<object[]> Avisos() => new[]
    {
        new object[] { "solicitud", BaseEnMemoria.Pendiente },
        new object[] { "propuesta", BaseEnMemoria.Pendiente },
        new object[] { "confirmado", BaseEnMemoria.Confirmado },
        new object[] { "cancelado", BaseEnMemoria.Cancelado }
    };

    private static void Disparar(Arnes a, string aviso)
    {
        switch (aviso)
        {
            case "solicitud": a.Notificador.TurnoSolicitado(a.Turno.IdTurno); break;
            case "propuesta": a.Notificador.TurnoAgendadoPorProfesional(1, 10, a.Turno.Fecha, a.Turno.Hora); break;
            case "confirmado": a.Notificador.TurnoConfirmado(a.Turno.IdTurno); break;
            case "cancelado": a.Notificador.TurnoCancelado(a.Turno.IdTurno); break;
            default: throw new ArgumentException(aviso);
        }
    }

    private static string Esperada(string aviso, DateTime momento) => aviso switch
    {
        "solicitud" => PlantillasWhatsApp.SolicitudRecibida(momento),
        "propuesta" => PlantillasWhatsApp.TurnoPropuesto(momento, Url),
        "confirmado" => PlantillasWhatsApp.TurnoConfirmado(momento),
        "cancelado" => PlantillasWhatsApp.TurnoCancelado(momento),
        _ => throw new ArgumentException(aviso)
    };

    [Theory]
    [MemberData(nameof(Avisos))]
    public void Por_el_chat_sale_exactamente_la_plantilla_y_nada_del_turno_salvo_su_momento(string aviso, int idEstado)
    {
        var a = new Arnes(idEstado);

        Disparar(a, aviso);

        var texto = Assert.Single(a.Salientes).Texto;
        Assert.Equal(Esperada(aviso, a.MomentoDelTurno), texto);
        foreach (var marca in Marcas)
            Assert.DoesNotContain(marca, texto);
        Assert.Contains(Formato.FechaHora(a.MomentoDelTurno), texto);
    }

    [Theory]
    [MemberData(nameof(Avisos))]
    public void La_campanita_del_panel_sigue_con_el_texto_completo_con_el_profesional(string aviso, int idEstado)
    {
        // Es lo que prueba que las marcas se verían si se colaran en el chat, y que el panel (con sesión) no perdió el detalle.
        var a = new Arnes(idEstado);

        Disparar(a, aviso);

        var campanita = Assert.Single(a.Datos.Campanita);
        Assert.Contains(MarcaProfesional, campanita.Mensaje);
        Assert.Contains(Formato.FechaHora(a.MomentoDelTurno), campanita.Mensaje);
    }

    [Fact]
    public void Con_cualquier_texto_en_los_datos_del_turno_el_chat_no_cambia()
    {
        // El chat no lee nada del turno salvo su momento: cambiar los datos no cambia el mensaje.
        foreach (var aviso in new[] { "solicitud", "propuesta", "confirmado" })
        {
            var a = new Arnes(aviso == "solicitud" || aviso == "propuesta" ? BaseEnMemoria.Pendiente : BaseEnMemoria.Confirmado);
            var b = new Arnes(aviso == "solicitud" || aviso == "propuesta" ? BaseEnMemoria.Pendiente : BaseEnMemoria.Confirmado);
            b.E.Db.Profesionales.Single(p => p.IdProfesional == 10).Nombre = "Otro nombre muy distinto";
            b.E.Db.Profesionales.Single(p => p.IdProfesional == 10).Especialidad = "Otra especialidad";
            b.Turno.Modalidad = "Presencial";

            Disparar(a, aviso);
            Disparar(b, aviso);

            Assert.Equal(Assert.Single(a.Salientes).Texto, Assert.Single(b.Salientes).Texto);
        }
    }
}
