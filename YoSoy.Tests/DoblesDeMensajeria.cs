// Dobles compartidos por las pruebas de los avisos de turno: lo que toca la base en memoria y un canal de mensajería que anota cada envío. Con ellos se arma
// el NotificadorTurnos REAL (y, si hace falta, el ServicioMensajes real) sin base ni red.

/// <summary>El historial de chat en memoria: guarda lo que inserta ServicioMensajes y sabe si la persona escribió en una conversación.</summary>
public sealed class MensajesChatEnMemoria : IMensajeChatDAO
{
    public readonly List<MensajeChat> Todos = new();
    public int Insertar(MensajeChat m) { m.IdMensaje = Todos.Count + 1; Todos.Add(m); return m.IdMensaje; }

    // Como el índice único (IdConversacion, IdExterno) de la migración 17: el mismo id en la misma conversación no entra dos veces.
    public int? InsertarEntrante(MensajeChat m) =>
        m.IdExterno is not null && Todos.Any(x => x.IdConversacion == m.IdConversacion && x.IdExterno == m.IdExterno) ? null : Insertar(m);
    public void ActualizarEstadoEnvio(int idMensaje, string estado) => Todos.Single(x => x.IdMensaje == idMensaje).EstadoEnvio = estado;
    public List<MensajeChat> ObtenerPorConversacion(int idConversacion, int desdeIdMensaje) => new();
    public List<MensajeChat> ObtenerAnteriores(int idConversacion, int? antesDeIdMensaje, int cantidad) => new();
    public bool TieneMensajeEntrante(int idConversacion) => Todos.Any(m => m.IdConversacion == idConversacion && m.Direccion == "Entrante");
}

/// <summary>Un canal de mensajería que no manda nada a ningún lado: anota a quién, qué y con qué opciones se le habría mandado.</summary>
public sealed class CanalEspia : IMessageChannel
{
    public string Nombre => "Prueba";
    public readonly List<(string Telefono, string Texto, OpcionesEnvio Opciones)> Envios = new();

    public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto) => EnviarAsync(telefonoDestino, texto, OpcionesEnvio.Ninguna);

    public Task<ResultadoEnvio> EnviarAsync(string telefonoDestino, string texto, OpcionesEnvio opciones)
    {
        lock (Envios) Envios.Add((telefonoDestino, texto, opciones));
        return Task.FromResult(new ResultadoEnvio(true, "x"));
    }
}

/// <summary>
/// FakeDatos no carga el teléfono ni el mail del paciente en el aviso: este doble se los agrega (el teléfono, de la ficha de la base en memoria; el mail,
/// el que se le ponga) y anota lo que va a la campanita.
/// </summary>
public sealed class DatosConContactoDelPaciente : IDatosMensajeria
{
    private readonly FakeDatos _datos;
    private readonly BaseEnMemoria _db;

    /// <summary>El mail del paciente en el aviso. null = no tiene mail cargado.</summary>
    public string? MailDelPaciente { get; set; } = "paciente@example.com";

    public readonly List<(int IdPaciente, string Titulo, string Mensaje)> Campanita = new();

    public DatosConContactoDelPaciente(FakeDatos datos, BaseEnMemoria db) { _datos = datos; _db = db; }

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
            aviso.MailPaciente = MailDelPaciente;
        }
        return aviso;
    }
}

/// <summary>Reserva un aviso por canal y guarda con qué resultado quedó cada uno (y en qué orden se reservaron).</summary>
public sealed class RecordatoriosEspia : IRecordatorioDAO
{
    private readonly Dictionary<int, string> _canales = new();
    public readonly List<(string Tipo, string Canal)> Reservados = new();
    public readonly List<(string Canal, string Estado, string? Error)> Resultados = new();

    public int? Reservar(int idTurno, string tipo, string canal, DateTime fechaProgramada, int maxIntentos)
    {
        var id = _canales.Count + 1;
        _canales[id] = canal;
        Reservados.Add((tipo, canal));
        return id;
    }

    public void MarcarResultado(int idRecordatorio, string estado, string? error, DateTime ahora) => Resultados.Add((_canales[idRecordatorio], estado, error));
    public void RegistrarRespuesta(int idTurno, string tipo, string respuesta, DateTime ahora) { }
    public List<int> TurnosConfirmadosSinRecordar(DateTime desde, DateTime hasta, IReadOnlyList<string> canales, int maxIntentos) => new();
}

public sealed class InternasQueNoHacenNada : INotificacionInternaDAO
{
    public int Insertar(NotificacionInterna n) => 0;
}

/// <summary>
/// Un EmailService que no manda nada: anota (destinatario, asunto, texto) de cada aviso. Hace pasar al correo por "configurado" sin SMTP ni red, así se puede ver
/// QUÉ diría el mail que sale.
/// </summary>
public sealed class EmailServiceEspia : EmailService
{
    public readonly List<(string Destinatario, string Asunto, string Texto)> Avisos = new();

    public EmailServiceEspia() : base(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()) { }

    public override bool EnviarAviso(string destinatario, string asunto, string texto)
    {
        lock (Avisos) Avisos.Add((destinatario, asunto, texto));
        return true;
    }
}
