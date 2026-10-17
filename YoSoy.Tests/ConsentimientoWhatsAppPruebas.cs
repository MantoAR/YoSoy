// Piezas compartidas por las pruebas del consentimiento de los avisos por WhatsApp: dobles en memoria de lo que toca la base y un escenario
// ya armado. Las reglas (estados, teléfono, menores, texto, carreras) se prueban sin base; con la base real hay pasadas aparte.

/// <summary>
/// Lo que NotificadorTurnos y BotReservasService le piden al consentimiento, a mano: el permiso que se le da a cada paciente (por defecto,
/// sin decisión) y qué se le consultó / qué BAJAs llegaron.
/// </summary>
public class FakePoliticaWhatsApp : IPoliticaEnvioWhatsApp, IBajaDeWhatsApp
{
    public readonly Dictionary<int, PermisoEnvioWhatsApp> Permisos = new();
    public readonly List<(int IdPaciente, string? Telefono)> Consultas = new();
    public readonly List<string> Bajas = new();

    /// <summary>Si no es null, Evaluar tira esto (la base caída, la tabla que no existe).</summary>
    public Exception? Falla { get; set; }

    public PermisoEnvioWhatsApp Evaluar(int idPaciente, string? telefonoActual)
    {
        Consultas.Add((idPaciente, telefonoActual));
        if (Falla is not null) throw Falla;
        return Permisos.GetValueOrDefault(idPaciente, PermisoEnvioWhatsApp.SinDecision);
    }

    public int RegistrarBaja(string telefonoNormalizado)
    {
        Bajas.Add(telefonoNormalizado);
        return 0;
    }
}

/// <summary>
/// ConsentimientoWhatsApp y lo que la regla necesita del paciente, en memoria. Se comporta como la tabla: una fila por aceptación, la del
/// paciente es la de mayor id, aceptar cierra la abierta (si hay) y abre la nueva, y revocar solo vale para una que no estaba revocada.
/// </summary>
public class FakeConsentimientoWhatsAppRepo : IConsentimientoWhatsAppRepositorio
{
    public readonly List<ConsentimientoWhatsAppFila> Filas = new();
    public readonly Dictionary<int, DatosPacienteParaWhatsApp> Pacientes = new();

    /// <summary>
    /// true: la próxima Aceptar pierde una carrera, como cuando otro pedido igual abrió una aceptación un instante antes: la fila de ESE pedido
    /// queda en la tabla (con los mismos datos) y esta llamada devuelve null.
    /// </summary>
    public bool SimularCarrera { get; set; }

    /// <summary>Si no es null, cualquier lectura o escritura tira esto (la base caída, la tabla que no existe).</summary>
    public Exception? Falla { get; set; }

    private int _siguiente = 100;

    public ConsentimientoWhatsAppFila? Ultimo(int idPaciente)
    {
        if (Falla is not null) throw Falla;
        return Filas.Where(f => f.IdPaciente == idPaciente).OrderByDescending(f => f.IdConsentimientoWhatsApp).FirstOrDefault();
    }

    public ConsentimientoWhatsAppFila? Aceptar(int idPaciente, int idUsuario, string telefono, string origen, string versionTexto,
        string hashTexto, DateTime aceptadoUtc, string motivoSiReemplaza)
    {
        if (Falla is not null) throw Falla;
        if (SimularCarrera)
        {
            SimularCarrera = false;
            Filas.Add(new ConsentimientoWhatsAppFila(_siguiente++, idPaciente, idUsuario, telefono, origen, versionTexto, hashTexto, aceptadoUtc, null, null));
            return null;
        }

        var i = Filas.FindIndex(f => f.IdPaciente == idPaciente && f.EstaAbierta);
        if (i >= 0)
            Filas[i] = Filas[i] with
            {
                RevocadoUtc = aceptadoUtc < Filas[i].AceptadoUtc ? Filas[i].AceptadoUtc : aceptadoUtc,
                MotivoRevocacion = motivoSiReemplaza
            };
        var fila = new ConsentimientoWhatsAppFila(_siguiente++, idPaciente, idUsuario, telefono, origen, versionTexto, hashTexto, aceptadoUtc, null, null);
        Filas.Add(fila);
        return fila;
    }

    public bool Revocar(int idPaciente, DateTime revocadoUtc, string motivo)
    {
        if (Falla is not null) throw Falla;
        var i = Filas.FindIndex(f => f.IdPaciente == idPaciente && f.EstaAbierta);
        if (i < 0) return false;
        Filas[i] = Filas[i] with
        {
            RevocadoUtc = revocadoUtc < Filas[i].AceptadoUtc ? Filas[i].AceptadoUtc : revocadoUtc,
            MotivoRevocacion = motivo
        };
        return true;
    }

    /// <summary>Los teléfonos con los que se llamó a RevocarVigentesDelTelefono (para ver qué le llega a la base, no solo qué devuelve).</summary>
    public readonly List<string> TelefonosDeBaja = new();

    public List<(int IdConsentimientoWhatsApp, int IdPaciente)> RevocarVigentesDelTelefono(string telefono, DateTime revocadoUtc, string motivo)
    {
        if (Falla is not null) throw Falla;
        TelefonosDeBaja.Add(telefono);
        var revocadas = new List<(int, int)>();
        for (var i = 0; i < Filas.Count; i++)
        {
            if (!Filas[i].EstaAbierta || Filas[i].Telefono != telefono) continue;
            Filas[i] = Filas[i] with { RevocadoUtc = revocadoUtc < Filas[i].AceptadoUtc ? Filas[i].AceptadoUtc : revocadoUtc, MotivoRevocacion = motivo };
            revocadas.Add((Filas[i].IdConsentimientoWhatsApp, Filas[i].IdPaciente));
        }
        return revocadas;
    }

    public DatosPacienteParaWhatsApp? ObtenerDatosPaciente(int idPaciente)
    {
        if (Falla is not null) throw Falla;
        return Pacientes.GetValueOrDefault(idPaciente);
    }
}

public class FakeAuditoriaConsentimientoWhatsApp : IAuditoriaConsentimientoWhatsApp
{
    public record Registro(int? IdUsuario, int IdPaciente, string Accion, int IdConsentimiento, string Detalle);

    private readonly List<Registro> _registros = new();
    public List<Registro> Registros { get { lock (_registros) return _registros.ToList(); } }

    public void Registrar(int? idUsuario, int idPaciente, string accion, int idConsentimiento, string detalle)
    {
        lock (_registros) _registros.Add(new Registro(idUsuario, idPaciente, accion, idConsentimiento, detalle));
    }
}

/// <summary>El texto vigente a mano: se puede cambiar de versión o dejarlo sin texto (null) para probar esos casos.</summary>
public class FakeTextoConsentimientoWhatsApp : ITextoConsentimientoWhatsApp
{
    public TextoConsentimiento? Texto { get; set; } = TextoConsentimientoVideo.Interpretar("v1", "Avisos por WhatsApp\n\nPrimero.\n\nSegundo.");
    public TextoConsentimiento? Actual() => Texto;
    public static TextoConsentimiento Version(string version, string cuerpo = "Primero.") =>
        TextoConsentimientoVideo.Interpretar(version, "Avisos por WhatsApp\n\n" + cuerpo);
}

/// <summary>
/// Todo armado para probar las reglas sin base ni HTTP: el paciente 301 / usuario 31 (el mismo de VideoApp), mayor de edad y con un celular
/// válido, a las 10:00 del martes 4/3/2031 (UTC).
/// </summary>
public sealed class EscenarioWhatsApp
{
    public const int IdPaciente = 301, IdUsuario = 31, IdOtroPaciente = 302, IdOtroUsuario = 32;
    public const string TelefonoDelPaciente = "11 5555-0001";
    public const string NumeroDelPaciente = "1155550001";

    public readonly FakeConsentimientoWhatsAppRepo Repo = new();
    public readonly FakeAuditoriaConsentimientoWhatsApp Auditoria = new();
    public readonly FakeTextoConsentimientoWhatsApp Textos = new();
    public readonly RelojFijo Reloj = new(new DateTime(2031, 3, 4, 10, 0, 0));
    public readonly LogsCapturados Logs = new();
    public readonly ConsentimientoWhatsAppService Servicio;

    public EscenarioWhatsApp()
    {
        Repo.Pacientes[IdPaciente] = new DatosPacienteParaWhatsApp(TelefonoDelPaciente, new DateTime(1990, 5, 5), false);
        Repo.Pacientes[IdOtroPaciente] = new DatosPacienteParaWhatsApp("351 412-3456", new DateTime(1985, 1, 1), false);
        Servicio = new ConsentimientoWhatsAppService(Repo, Textos, Auditoria, Reloj, Logs.CreateLogger<ConsentimientoWhatsAppService>());
    }

    public static Identidad Paciente(int idUsuario = IdUsuario, int idPaciente = IdPaciente) => new(idUsuario, Roles.Paciente, idPaciente, null);
    public static Identidad Profesional() => new(21, Roles.Profesional, null, 201);
    public static Identidad Configurador() => new(11, Roles.Configurador, null, null);

    public string Version => Textos.Texto!.Version;

    /// <summary>El paciente acepta con la versión vigente.</summary>
    public ResultadoConsentimientoWhatsApp Aceptar(Identidad? yo = null) => Servicio.Aceptar(yo ?? Paciente(), Version);
}
