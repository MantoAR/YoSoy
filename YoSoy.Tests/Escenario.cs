// Escenario base de los tests: jueves 24/09/2026 09:00.
//  - Paciente 1: Juan García, tel. "11 5555-0001", DNI 30000001, asignado al profesional 10.
//  - Paciente 2: Ana Torres, sin teléfono, DNI 31000002, sin profesionales asignados.
//  - Profesional 10: Santiago Manto (Psicología), atiende de lunes a viernes 09:00 a 12:00, cada 30 min.
public class Escenario
{
    public static readonly DateTime Jueves0900 = new(2026, 9, 24, 9, 0, 0);
    public const string TelJuan = "11 5555-0001";

    public readonly BaseEnMemoria Db = new();
    public readonly RelojFijo Reloj = new(Jueves0900);
    public readonly OpcionesFijas Opciones = new();
    public readonly OpcionesFijas<ConsentimientoOptions> Consentimiento = new();
    public readonly FakeConversaciones Conversaciones;
    public readonly FakeServicioMensajes Mensajes = new();
    public readonly FakeRecordatorios Recordatorios = new();
    public readonly FakeNotificador Notificador = new();
    public readonly FakePoliticaWhatsApp WhatsApp = new();
    public readonly TurnoReglasService Reglas;
    public readonly BotReservasService Bot;

    public Escenario()
    {
        var op = Opciones.CurrentValue;
        op.PalabrasRiesgo    = new() { "suicid", "no quiero vivir" };
        op.PalabrasClinicas  = new() { "ansiedad", "medicación" };
        op.LineasEmergencia  = new() { new LineaEmergencia { Nombre = "Emergencias", Telefono = "[COMPLETAR Y VALIDAR]" } };
        op.UrlRegistroWeb    = "https://yosoy.test/registro";
        Consentimiento.CurrentValue.TipoRequerido = BaseEnMemoria.TipoTelemedicina;
        Consentimiento.CurrentValue.UrlFirma      = "https://yosoy.test/consentimiento";

        Db.Pacientes.Add(new Paciente { IdPaciente = 1, Nombre = "Juan García", Dni = "30000001", Telefono = TelJuan, Estado = "Activo" });
        Db.Pacientes.Add(new Paciente { IdPaciente = 2, Nombre = "Ana Torres", Dni = "31000002", Telefono = null!, Estado = "Activo" });
        Db.Profesionales.Add(new ProfesionalResumen { IdProfesional = 10, IdEspecialidad = 1, Nombre = "Santiago Manto", Especialidad = "Psicología" });
        Db.Asignaciones[1] = new() { 10 };
        foreach (var dia in new[] { "lunes", "martes", "miércoles", "jueves", "viernes" })
            Db.Agenda.Add(new Agenda { IdProfesional = 10, Dia = dia, HoraInicio = new(9, 0, 0), HoraFin = new(12, 0, 0), Disponible = true, IntervaloMinutos = 30 });
        Db.Faq.Add(new PreguntaFrecuente { IdPregunta = 1, Pregunta = "¿Cómo pido un turno?", Respuesta = "Por este chat o por la web.", Orden = 1 });

        Conversaciones = new FakeConversaciones(Reloj);
        Reglas = new TurnoReglasService(new FakeTurnoRepositorio(Db), Reloj, Opciones, Consentimiento);
        Bot = new BotReservasService(Conversaciones, Mensajes, new FakeDatos(Db), new FakeFaq(Db), Recordatorios,
            Reglas, Notificador, new InterpreteMenu(), new FiltroSeguridad(Opciones), Opciones, Reloj, WhatsApp);
    }

    public async Task<string> Enviar(string telefono, string texto) =>
        string.Join("\n---\n", (await Bot.ProcesarAsync(telefono, texto, "Simulado")).Respuestas);

    public Conversacion ConversacionDe(string telefono) =>
        Conversaciones.ObtenerPorTelefono(Telefono.Normalizar(telefono), "Simulado")!;

    // Lo que haría NotificadorTurnos al mandar un recordatorio por chat.
    public void SimularRecordatorio(string telefono, int idTurno)
    {
        var c = Conversaciones.ObtenerOCrear(Telefono.Normalizar(telefono), "Simulado");
        c.IdPaciente ??= Db.Turnos.Single(t => t.IdTurno == idTurno).IdPaciente;
        var ctx = ContextoBot.Leer(c.DatosContexto);
        ctx.LimpiarFlujo();
        ctx.IdTurno = idTurno;
        c.PasoActual = PasoBot.RespuestaRecordatorio;
        c.DatosContexto = ctx.Serializar();
        c.FechaUltimaActividad = Reloj.Ahora;
        Conversaciones.Guardar(c);
    }
}
