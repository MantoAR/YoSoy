using Microsoft.Extensions.Options;

// Dobles de prueba en memoria. Los ids de EstadoTurno NO son 1/2/3 a propósito:
// si algún código usara ids fijos en vez de la descripción, los tests fallarían.

public class RelojFijo : TimeProvider
{
    public DateTime Ahora { get; set; }
    public RelojFijo(DateTime ahora) => Ahora = ahora;
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Ahora, DateTimeKind.Utc));
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Avanzar(TimeSpan t) => Ahora += t;
}

public class OpcionesFijas<T> : IOptionsMonitor<T> where T : new()
{
    public T CurrentValue { get; } = new();
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public class OpcionesFijas : OpcionesFijas<MensajeriaOptions> { }

public class BaseEnMemoria
{
    public const int Pendiente = 11, Confirmado = 12, Cancelado = 13, Rechazado = 14;
    public Dictionary<int, string> Estados = new()
    {
        [Pendiente] = "Pendiente", [Confirmado] = "Confirmado", [Cancelado] = "Cancelado", [Rechazado] = "Rechazado"
    };
    public List<Paciente> Pacientes = new();
    public List<ProfesionalResumen> Profesionales = new();
    public Dictionary<int, List<int>> Asignaciones = new();       // IdPaciente → IdProfesional
    public List<Agenda> Agenda = new();
    public List<Turno> Turnos = new();
    // Pacientes con el consentimiento de telemedicina (el tipo que exige RN-02) y con otros tipos.
    public const string TipoTelemedicina = "Consentimiento Informado - Atención por Telemedicina";
    public HashSet<int> ConConsentimiento = new();
    public HashSet<int> ConOtroConsentimiento = new();
    public List<PreguntaFrecuente> Faq = new();
    private int _siguienteTurno = 100;

    public Turno AgregarTurno(int idPaciente, int idProfesional, DateTime momento, int idEstado)
    {
        var t = new Turno
        {
            IdTurno = _siguienteTurno++, IdPaciente = idPaciente, IdProfesional = idProfesional,
            IdEstadoTurno = idEstado, Fecha = momento.Date, Hora = momento.TimeOfDay, Modalidad = "Virtual"
        };
        Turnos.Add(t);
        return t;
    }

    public string EstadoDe(int idTurno) => Estados[Turnos.Single(t => t.IdTurno == idTurno).IdEstadoTurno];
}

public class FakeTurnoRepositorio : ITurnoRepositorio
{
    private readonly BaseEnMemoria _db;
    public FakeTurnoRepositorio(BaseEnMemoria db) => _db = db;

    public List<Agenda> AgendaDe(int idProfesional) => _db.Agenda.Where(a => a.IdProfesional == idProfesional).ToList();

    public List<TimeSpan> HorasOcupadas(int idProfesional, DateTime fecha) => _db.Turnos
        .Where(t => t.IdProfesional == idProfesional && t.Fecha == fecha.Date
                    && _db.Estados[t.IdEstadoTurno] is not ("Cancelado" or "Rechazado"))
        .Select(t => t.Hora).ToList();

    public int ContarTurnosActivos(int idPaciente, int idProfesional, DateTime fecha) => _db.Turnos
        .Count(t => t.IdPaciente == idPaciente && t.IdProfesional == idProfesional && t.Fecha == fecha.Date
                    && _db.Estados[t.IdEstadoTurno] is "Pendiente" or "Confirmado");

    public bool TieneConsentimiento(int idPaciente, string descripcionTipo) =>
        descripcionTipo == BaseEnMemoria.TipoTelemedicina
            ? _db.ConConsentimiento.Contains(idPaciente)
            : _db.ConOtroConsentimiento.Contains(idPaciente);
    public Turno? ObtenerTurno(int idTurno) => _db.Turnos.FirstOrDefault(t => t.IdTurno == idTurno);
    public string? DescripcionEstado(int idEstadoTurno) => _db.Estados.GetValueOrDefault(idEstadoTurno);
    public int? IdEstado(string descripcion) => _db.Estados.Where(e => e.Value == descripcion).Select(e => (int?)e.Key).FirstOrDefault();

    public int? InsertarSiLibre(Turno t, string creadoPor)
    {
        var momento = t.Fecha.Date + t.Hora;
        if (HorasOcupadas(t.IdProfesional, t.Fecha).Contains(t.Hora)) return null;
        if (ContarTurnosActivos(t.IdPaciente, t.IdProfesional, t.Fecha) > 0) return null;
        return _db.AgregarTurno(t.IdPaciente, t.IdProfesional, momento, t.IdEstadoTurno).IdTurno;
    }

    public void CambiarEstado(int idTurno, int idEstadoTurno) =>
        _db.Turnos.Single(t => t.IdTurno == idTurno).IdEstadoTurno = idEstadoTurno;
}

public class FakeDatos : IDatosMensajeria
{
    private readonly BaseEnMemoria _db;
    public FakeDatos(BaseEnMemoria db) => _db = db;

    public List<Paciente> PacientesPorTelefono(string tel) =>
        _db.Pacientes.Where(p => p.Telefono != null && Telefono.Normalizar(p.Telefono) == tel).ToList();
    public Paciente? PacienteActivoPorDni(string dni) => _db.Pacientes.FirstOrDefault(p => p.Dni == dni);
    public Paciente? ObtenerPaciente(int id) => _db.Pacientes.FirstOrDefault(p => p.IdPaciente == id);
    public List<ProfesionalResumen> ProfesionalesDelPaciente(int idPaciente) =>
        _db.Asignaciones.GetValueOrDefault(idPaciente, new()).Select(id => _db.Profesionales.Single(p => p.IdProfesional == id)).ToList();
    public List<Especialidad> EspecialidadesConAgenda() => _db.Profesionales
        .Where(p => _db.Agenda.Any(a => a.IdProfesional == p.IdProfesional))
        .Select(p => new Especialidad { IdEspecialidad = p.IdEspecialidad, Nombre = p.Especialidad, Estado = "Activo" })
        .DistinctBy(e => e.IdEspecialidad).ToList();
    public List<ProfesionalResumen> ProfesionalesConAgenda(int idEsp) => _db.Profesionales
        .Where(p => p.IdEspecialidad == idEsp && _db.Agenda.Any(a => a.IdProfesional == p.IdProfesional)).ToList();

    public List<TurnoAviso> TurnosProximos(int idPaciente, DateTime ahora) => _db.Turnos
        .Where(t => t.IdPaciente == idPaciente && t.Fecha.Date + t.Hora >= ahora
                    && _db.Estados[t.IdEstadoTurno] is "Pendiente" or "Confirmado")
        .OrderBy(t => t.Fecha).ThenBy(t => t.Hora).Select(Aviso).ToList();

    public TurnoAviso? ObtenerTurnoAviso(int idTurno) =>
        _db.Turnos.Where(t => t.IdTurno == idTurno).Select(Aviso).FirstOrDefault();

    public int? BuscarIdTurno(int idPaciente, int idProfesional, DateTime fecha, TimeSpan hora) => _db.Turnos
        .Where(t => t.IdPaciente == idPaciente && t.IdProfesional == idProfesional && t.Fecha == fecha.Date && t.Hora == hora)
        .Select(t => (int?)t.IdTurno).LastOrDefault();

    public List<(int IdPaciente, string Titulo)> Campanita = new();
    public void CrearNotificacionPaciente(int idPaciente, string titulo, string mensaje, string? link) =>
        Campanita.Add((idPaciente, titulo));

    private TurnoAviso Aviso(Turno t) => new()
    {
        IdTurno = t.IdTurno, IdPaciente = t.IdPaciente, IdProfesional = t.IdProfesional,
        NombrePaciente    = _db.Pacientes.Single(p => p.IdPaciente == t.IdPaciente).Nombre,
        NombreProfesional = _db.Profesionales.Single(p => p.IdProfesional == t.IdProfesional).Nombre,
        Fecha = t.Fecha, Hora = t.Hora, Modalidad = t.Modalidad, Estado = _db.Estados[t.IdEstadoTurno]
    };
}

public class FakeConversaciones : IConversacionDAO
{
    public readonly Dictionary<int, Conversacion> Todas = new();
    private readonly RelojFijo _reloj;
    public FakeConversaciones(RelojFijo reloj) => _reloj = reloj;

    public Conversacion? ObtenerPorId(int id) => Todas.TryGetValue(id, out var c) ? Copia(c) : null;
    public Conversacion? ObtenerPorTelefono(string tel, string canal) =>
        Todas.Values.Where(c => c.Telefono == tel && c.Canal == canal).Select(Copia).FirstOrDefault();

    public Conversacion ObtenerOCrear(string tel, string canal)
    {
        var c = ObtenerPorTelefono(tel, canal);
        if (c != null) return c;
        var nueva = new Conversacion
        {
            IdConversacion = Todas.Count + 1, Telefono = tel, Canal = canal,
            FechaUltimaActividad = _reloj.Ahora, FechaCreacion = _reloj.Ahora
        };
        Todas[nueva.IdConversacion] = nueva;
        return Copia(nueva);
    }

    public void Guardar(Conversacion c) => Todas[c.IdConversacion] = Copia(c);
    public List<ConversacionResumen> ListarResumen(string? estado, int? desdeIdMensaje = null) => new();
    public void MarcarLeida(int idConversacion, int hastaIdMensaje) { }

    // Copias para que el test vea solo lo que el bot efectivamente guardó.
    private static Conversacion Copia(Conversacion c) => new()
    {
        IdConversacion = c.IdConversacion, Telefono = c.Telefono, IdPaciente = c.IdPaciente, Canal = c.Canal,
        PasoActual = c.PasoActual, DatosContexto = c.DatosContexto, Estado = c.Estado,
        FechaUltimaActividad = c.FechaUltimaActividad, FechaCreacion = c.FechaCreacion
    };
}

public class FakeServicioMensajes : IServicioMensajes
{
    public readonly List<MensajeChat> Mensajes = new();

    public MensajeChat RegistrarEntrante(Conversacion c, string texto) =>
        Agregar(new MensajeChat { IdConversacion = c.IdConversacion, Direccion = "Entrante", Origen = "Paciente", Texto = texto, EstadoEnvio = "Enviado" });

    public Task<MensajeChat> EnviarAsync(Conversacion c, string texto, string origen) =>
        Task.FromResult(Agregar(new MensajeChat { IdConversacion = c.IdConversacion, Direccion = "Saliente", Origen = origen, Texto = texto, EstadoEnvio = "Enviado" }));

    private MensajeChat Agregar(MensajeChat m) { m.IdMensaje = Mensajes.Count + 1; Mensajes.Add(m); return m; }
}

public class FakeRecordatorios : IRecordatorioDAO
{
    public readonly List<(int IdTurno, string Tipo, string Respuesta)> Respuestas = new();
    public int? Reservar(int idTurno, string tipo, string canal, DateTime fechaProgramada, int maxIntentos) => null;
    public void MarcarResultado(int idRecordatorio, string estado, string? error, DateTime ahora) { }
    public void RegistrarRespuesta(int idTurno, string tipo, string respuesta, DateTime ahora) => Respuestas.Add((idTurno, tipo, respuesta));
    public List<int> TurnosConfirmadosSinRecordar(DateTime desde, DateTime hasta, int cantidadCanales, int maxIntentos) => new();
}

public class FakeNotificador : INotificadorTurnos
{
    public readonly List<string> Eventos = new();
    public void TurnoSolicitado(int idTurno, bool respondidoPorChat = false) => Eventos.Add($"TurnoSolicitado:{idTurno}:{respondidoPorChat}");
    public void TurnoSolicitado(Turno t) => Eventos.Add("TurnoSolicitadoWeb");
    public void TurnoAgendadoPorProfesional(int idPaciente, int idProfesional, DateTime fecha, TimeSpan hora) => Eventos.Add("TurnoAgendado");
    public void TurnoConfirmado(int idTurno) => Eventos.Add($"TurnoConfirmado:{idTurno}");
    public void TurnoCancelado(int idTurno, bool respondidoPorChat = false) => Eventos.Add($"TurnoCancelado:{idTurno}:{respondidoPorChat}");
    public void CambioDeEstado(int idTurno, int idEstadoAnterior) => Eventos.Add($"CambioDeEstado:{idTurno}");
    public void AvisarUrgencia(Conversacion c, string textoPaciente) => Eventos.Add($"Urgencia:{c.IdConversacion}");
    public void AvisarDerivacion(Conversacion c) => Eventos.Add($"Derivacion:{c.IdConversacion}");
    public void AvisarNoAsistencia(int idTurno) => Eventos.Add($"NoAsistencia:{idTurno}");
}

public class FakeFaq : IPreguntaFrecuenteDAO
{
    private readonly BaseEnMemoria _db;
    public FakeFaq(BaseEnMemoria db) => _db = db;
    public List<PreguntaFrecuente> ObtenerActivas() => _db.Faq.Where(p => p.Activa).OrderBy(p => p.Orden).ToList();
}
