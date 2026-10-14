using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

// Piezas compartidas por las pruebas del consentimiento de la lectura de emociones (PR 6): dobles en memoria de lo que
// toca la base y un escenario ya armado. Las reglas (estados, menores, ventana, guardia) se prueban sin base; con la base
// real hay una pasada en EmocionesBaseRealTests.

// ConsentimientoVideo y lo que la regla necesita del paciente, en memoria. Se comporta como la tabla: una fila por
// decisión, la vigente es la de mayor id, y revocar solo vale para una aceptación que no estaba revocada.
public class FakeConsentimientoRepo : IConsentimientoVideoRepositorio
{
    public readonly List<ConsentimientoVideoFila> Filas = new();
    public readonly Dictionary<int, DatosPacienteParaConsentimiento> Pacientes = new();
    public readonly List<(string Origen, int IdOrigen, int IdPaciente, string Motivo, DateTime Ahora)> Anulaciones = new();

    /// <summary>Lo que "anula" cada llamada a AnularEmocionesDeLaSesion (cuántas sesiones tenían emociones).</summary>
    public int SesionesConEmociones { get; set; }

    private int _siguiente = 100;

    public ConsentimientoVideoFila? Ultimo(string origen, int idOrigen, int idPaciente) =>
        Filas.Where(f => f.Origen == origen && f.IdOrigen == idOrigen && f.IdPaciente == idPaciente)
             .OrderByDescending(f => f.IdConsentimientoVideo).FirstOrDefault();

    public ConsentimientoVideoFila Insertar(int idUsuario, int idPaciente, string origen, int idOrigen,
        string versionTexto, string hashTexto, string decision, DateTime decididoUtc)
    {
        var fila = new ConsentimientoVideoFila(_siguiente++, idUsuario, idPaciente, origen, idOrigen, versionTexto, hashTexto,
            decision, decididoUtc, null);
        Filas.Add(fila);
        return fila;
    }

    public bool Revocar(int idConsentimientoVideo, DateTime revocadoUtc)
    {
        var i = Filas.FindIndex(f => f.IdConsentimientoVideo == idConsentimientoVideo);
        if (i < 0 || !Filas[i].EsAceptacionVigente) return false;
        Filas[i] = Filas[i] with { RevocadoUtc = revocadoUtc < Filas[i].DecididoUtc ? Filas[i].DecididoUtc : revocadoUtc };
        return true;
    }

    public int AnularEmocionesDeLaSesion(string origen, int idOrigen, int idPaciente, string motivo, DateTime ahoraUtc)
    {
        Anulaciones.Add((origen, idOrigen, idPaciente, motivo, ahoraUtc));
        var n = SesionesConEmociones;
        SesionesConEmociones = 0;   // lo que ya se anuló no se vuelve a anular
        return n;
    }

    public DatosPacienteParaConsentimiento? ObtenerDatosPaciente(int idPaciente) => Pacientes.GetValueOrDefault(idPaciente);
}

public class FakeAuditoriaEmociones : IAuditoriaEmociones
{
    public record Registro(string Tipo, int? IdUsuario, string? Origen, int? IdOrigen, string Detalle, int Cantidad = 0, int? IdSesion = null);

    private readonly List<Registro> _registros = new();
    public List<Registro> Registros { get { lock (_registros) return _registros.ToList(); } }
    private void Agregar(Registro r) { lock (_registros) _registros.Add(r); }

    public void Consentimiento(int idUsuario, string origen, int idOrigen, string decision, string version, int idConsentimiento, int emocionesAnuladas = 0) =>
        Agregar(new Registro("consentimiento:" + decision, idUsuario, origen, idOrigen, $"{version}#{idConsentimiento}", emocionesAnuladas));

    public void EmocionesGuardadas(int idUsuario, int idSesion, int idConsentimiento, int muestras) =>
        Agregar(new Registro("guardadas", idUsuario, null, idSesion, $"#{idConsentimiento}", muestras));

    public void EmocionesDescartadas(int idUsuario, string? origen, int? idOrigen, string motivo, int? idSesion = null) =>
        Agregar(new Registro("descartadas", idUsuario, origen, idOrigen, motivo, IdSesion: idSesion));

    public void EmocionesPurgadas(int? idUsuario, int cantidad, string motivo) =>
        Agregar(new Registro("purgadas", idUsuario, null, null, motivo, cantidad));
}

// Todo armado para probar las reglas sin base ni HTTP. Las mismas personas y el mismo turno que VideoApp: el turno 1
// (paciente 301 / usuario 31, profesional 201 / usuario 21) empieza el martes 4/3/2031 10:00 UTC y dura 30 minutos, así
// que la sala abre 09:50 y cierra 11:00; el informe de admisión 5 es del mismo paciente y a la misma hora.
public sealed class EscenarioEmociones
{
    public const int IdTurno = 1, IdInforme = 5;
    public const int UsuarioPaciente = 31, IdPaciente = 301;
    public const int UsuarioOtroPaciente = 32, IdOtroPaciente = 302;
    public const int UsuarioProfesional = 21, IdProfesional = 201;
    public const int UsuarioOtroProfesional = 22, IdOtroProfesional = 202;
    public const int UsuarioConfigurador = 11;

    public static readonly DateTime Inicio = new(2031, 3, 4, 10, 0, 0);

    /// <summary>Una clave de 32 bytes en base64, sin ningún valor real: solo para las pruebas.</summary>
    public static readonly string ClaveDePrueba = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)(i * 7 % 256)).ToArray());
    public static readonly string OtraClaveDePrueba = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)(i * 11 % 256)).ToArray());

    public readonly OpcionesFijas<VideoOptions> Opciones = new();
    public readonly RelojFijo Reloj = new(Inicio);
    public readonly FakeOrigenSala Origen = new();
    public readonly FakeConsentimientoRepo Repo = new();
    public readonly FakeAuditoriaEmociones Auditoria = new();
    public readonly IConfiguration Config;
    public readonly EmocionesCifradoService Cifrado;
    public readonly TextoConsentimientoVideo Textos;
    public readonly AccesoSalaService Acceso;
    public readonly ConsentimientoVideoService Consentimiento;
    public readonly GuardiaEmocionesService Guardia;

    public EscenarioEmociones(bool conClave = true)
    {
        Opciones.CurrentValue.Emociones.ClaveCifrado = conClave ? ClaveDePrueba : null;

        Origen.Turnos[IdTurno] = new TurnoParaSala(IdTurno, IdPaciente, IdProfesional, "Confirmado",
            Inicio.Date, Inicio.TimeOfDay, "turno-1-sala-de-prueba", 30);
        Origen.Admisiones[IdInforme] = new AdmisionParaSala(IdInforme, IdPaciente, "En entrevista", Inicio, "admision-5-sala-de-prueba");
        // Un adulto: nacido en 1990.
        Repo.Pacientes[IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(1990, 5, 5), false);
        Repo.Pacientes[IdOtroPaciente] = new DatosPacienteParaConsentimiento(new DateTime(1985, 1, 1), false);

        Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "clave-de-login-de-prueba-que-no-es-ninguna-de-las-otras-0123456789"
        }).Build();
        Cifrado = new EmocionesCifradoService(Opciones, Config);
        Textos = new TextoConsentimientoVideo(Opciones, NullLogger<TextoConsentimientoVideo>.Instance);
        Acceso = new AccesoSalaService(Origen, Reloj, Opciones, NullLogger<AccesoSalaService>.Instance);
        Consentimiento = new ConsentimientoVideoService(Repo, Origen, Acceso, Textos, Cifrado, Auditoria, Reloj, Opciones);
        Guardia = new GuardiaEmocionesService(Consentimiento, Repo, Origen, Textos, Auditoria, Reloj, Opciones);
    }

    public Identidad Paciente => new(UsuarioPaciente, Roles.Paciente, IdPaciente, null);
    public Identidad OtroPaciente => new(UsuarioOtroPaciente, Roles.Paciente, IdOtroPaciente, null);
    public Identidad Profesional => new(UsuarioProfesional, Roles.Profesional, null, IdProfesional);
    public Identidad OtroProfesional => new(UsuarioOtroProfesional, Roles.Profesional, null, IdOtroProfesional);
    public Identidad Configurador => new(UsuarioConfigurador, Roles.Configurador, null, null);

    /// <summary>La versión del texto vigente, tal como la ve el paciente.</summary>
    public TextoConsentimiento Texto => Textos.Actual()!;

    public DateTime Ahora => Reloj.GetUtcNow().UtcDateTime;

    /// <summary>El paciente acepta con el texto vigente (sobre el turno 1 por defecto). Falla el test si no queda aceptado.</summary>
    public ResultadoConsentimiento Acepta(string origen = "turno", int id = IdTurno)
    {
        var r = Consentimiento.Decidir(Paciente, origen, id, "acepta", Texto.Version);
        Assert.True(r.Ok, $"No se pudo aceptar: {r.Codigo} {r.Mensaje}");
        return r;
    }

    /// <summary>Un pedido de guardado válido: ventana dentro de la aceptación y porcentajes que suman 100.</summary>
    public EmocionesSesionRequest Emociones(DateTime desde, DateTime hasta, int muestras = 120, string origen = "turno", int id = IdTurno) => new()
    {
        Origen = origen, Id = id,
        DesdeUtc = new DateTimeOffset(DateTime.SpecifyKind(desde, DateTimeKind.Utc)),
        HastaUtc = new DateTimeOffset(DateTime.SpecifyKind(hasta, DateTimeKind.Utc)),
        Muestras = muestras,
        Promedios = new Dictionary<string, double>
        {
            ["neutral"] = 60, ["happy"] = 10, ["sad"] = 20, ["angry"] = 3, ["fearful"] = 3, ["surprised"] = 2, ["disgusted"] = 2
        }
    };
}
