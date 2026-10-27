using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

// El consentimiento y las emociones guardadas contra SQL Server de verdad: lo que VideoApp y las pruebas de servicio
// prueban con dobles en memoria, acá con la base (ConsentimientoVideoDAO, SesionClinicaDAO, la purga y los endpoints de
// sesiones clínicas) y con la aplicación real de punta a punta.
//
// Cada prueba arma sus PROPIOS datos (usuarios, paciente, profesional, turno de 2031...) con la marca "[TEST-PR6]" y los
// borra al terminar, incluso si falla: no usa ningún dato de la base de desarrollo ni los modifica. Si la base no tiene
// la migración migrations/add_consentimiento_video.sql, estas pruebas se OMITEN (no pasan en falso): aplicala con sqlcmd
// (ver docs/DESPLIEGUE_VIDEO.md) y volvé a correrlas. Ninguna prueba aplica esquema por su cuenta.
public sealed class FactConBaseDeConsentimientoAttribute : FactAttribute
{
    public FactConBaseDeConsentimientoAttribute()
    {
        var motivo = BaseDeConsentimiento.NoDisponible();
        if (motivo is not null) Skip = motivo;
    }
}

public static class BaseDeConsentimiento
{
    private static readonly Lazy<string?> Motivo = new(Calcular);

    /// <summary>null si la base tiene el esquema del PR 6; si no, por qué se omiten las pruebas.</summary>
    public static string? NoDisponible() => Motivo.Value;

    private static string? Calcular()
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand(@"
                SELECT CASE WHEN OBJECT_ID('dbo.ConsentimientoVideo', 'U') IS NOT NULL
                             AND COL_LENGTH('dbo.SesionClinica', 'IdConsentimientoVideo') IS NOT NULL
                             AND COL_LENGTH('dbo.SesionClinica', 'EmocionesAnuladasUtc') IS NOT NULL THEN 1 ELSE 0 END", con);
            return (int)cmd.ExecuteScalar()! == 1
                ? null
                : "La base no tiene la migración migrations/add_consentimiento_video.sql (aplicala con sqlcmd para correr estas pruebas).";
        }
        catch (Exception e)
        {
            return $"No hay una base disponible ({e.GetType().Name}): falta ConnectionStrings__YoSoy.";
        }
    }
}

// Los datos de una prueba: un paciente adulto con su usuario, un profesional con su usuario y asignación, un
// Configurador, y un turno confirmado con sala el 4/3/2031 a las 10:00 (la misma hora que usa VideoApp).
public sealed class DatosEmociones : IDisposable
{
    public const string Marca = "[TEST-PR6]";
    public static readonly DateTime Fecha = new(2031, 3, 4);
    public static readonly TimeSpan Hora = new(10, 0, 0);

    public readonly int IdUsuarioPaciente, IdUsuarioProfesional, IdUsuarioConfigurador, IdPaciente, IdProfesional, IdTurno;
    public readonly string Sala = "SALA-T6-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    private int? _idEspecialidadCreada, _idInforme, _idTutor;
    private bool _estadoTomado;

    private readonly ITestOutputHelper? _salida;

    /// <param name="salida">La salida de la prueba que lo usa: ahí se escribe lo que la limpieza no pudo borrar. Sin ella, en la
    /// salida de error del proceso (que con pruebas en paralelo xUnit no atribuye a ninguna en particular).</param>
    public DatosEmociones(DateTime? nacimiento = null, ITestOutputHelper? salida = null)
    {
        _salida = salida;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        IdUsuarioPaciente = Insertar("INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) OUTPUT INSERTED.IdUsuario VALUES (@A, 'x', 'Activo', @B)",
            ("@A", "t6p_" + sufijo), ("@B", $"t6p_{sufijo}@example.test"));
        IdUsuarioProfesional = Insertar("INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) OUTPUT INSERTED.IdUsuario VALUES (@A, 'x', 'Activo', @B)",
            ("@A", "t6m_" + sufijo), ("@B", $"t6m_{sufijo}@example.test"));
        IdUsuarioConfigurador = Insertar("INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) OUTPUT INSERTED.IdUsuario VALUES (@A, 'x', 'Activo', @B)",
            ("@A", "t6c_" + sufijo), ("@B", $"t6c_{sufijo}@example.test"));

        IdPaciente = Insertar("INSERT INTO Paciente (Nombre, Dni, FechaNacimiento, Estado) OUTPUT INSERTED.IdPaciente VALUES (@N, @D, @F, 'Activo')",
            ("@N", Marca + " Paciente"), ("@D", Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString()),
            ("@F", nacimiento ?? new DateTime(1990, 5, 5)));

        // Una especialidad PROPIA: tomar "la primera que haya" la compartía con otras pruebas que corren en paralelo
        // (y que la borran al terminar), y dejaba filas huérfanas.
        _idEspecialidadCreada = Insertar("INSERT INTO Especialidad (Nombre) OUTPUT INSERTED.IdEspecialidad VALUES (@N)", ("@N", Marca + sufijo));
        var especialidad = _idEspecialidadCreada;
        IdProfesional = Insertar("INSERT INTO Profesional (Nombre, Matricula, IdEspecialidad, Estado) OUTPUT INSERTED.IdProfesional VALUES (@N, @M, @E, 'Activo')",
            ("@N", Marca + " Profesional"), ("@M", "T6-" + sufijo), ("@E", especialidad));

        Ejecutar("INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, EstadoAsignacion) VALUES (@P, @M, 'Activo')", ("@P", IdPaciente), ("@M", IdProfesional));

        // Catálogo COMPARTIDO: se toma del registro (EstadosCompartidos) y se suelta en Dispose; lo borra la última prueba que lo usa.
        var estado = EstadosCompartidos.Tomar("Confirmado");
        _estadoTomado = true;
        IdTurno = Insertar(@"INSERT INTO Turno (IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor, CodigoSala)
                             OUTPUT INSERTED.IdTurno VALUES (@P, @M, @E, @F, @H, 'Virtual', @Motivo, 'paciente', @S)",
            ("@P", IdPaciente), ("@M", IdProfesional), ("@E", estado), ("@F", Fecha), ("@H", Hora),
            ("@Motivo", Marca + " consentimiento de video"), ("@S", Sala));
    }

    public int IdInforme => _idInforme ??= Insertar(@"INSERT INTO InformeAdmision (IdPaciente, Estado, FechaEntrevista, CodigoSala, MotivoConsulta)
        OUTPUT INSERTED.IdInforme VALUES (@P, 'En entrevista', @F, @S, @M)",
        ("@P", IdPaciente), ("@F", Fecha + Hora), ("@S", "ADM-T6-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()), ("@M", Marca));

    public void ConTutor()
    {
        _idTutor = Insertar("INSERT INTO Tutor (Nombre, Dni) OUTPUT INSERTED.IdTutor VALUES (@N, '1')", ("@N", Marca + " Tutor"));
        Ejecutar("INSERT INTO PacienteTutor (IdPaciente, IdTutor) VALUES (@P, @T)", ("@P", IdPaciente), ("@T", _idTutor));
    }

    public void CambiarNacimiento(DateTime? nacimiento) =>
        Ejecutar("UPDATE Paciente SET FechaNacimiento = @F WHERE IdPaciente = @P", ("@F", nacimiento), ("@P", IdPaciente));

    // ── Acceso a la base ──
    public static SqlConnection Abrir()
    {
        var con = Conexion.ObtenerConexion();
        con.Open();
        return con;
    }

    private static SqlCommand Comando(SqlConnection con, string sql, (string, object?)[] p)
    {
        var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    public static void Ejecutar(string sql, params (string, object?)[] p)
    {
        using var con = Abrir();
        using var cmd = Comando(con, sql, p);
        cmd.ExecuteNonQuery();
    }

    public static int Insertar(string sql, params (string, object?)[] p) => Convert.ToInt32(Escalar(sql, p));

    public static object? Escalar(string sql, params (string, object?)[] p)
    {
        using var con = Abrir();
        using var cmd = Comando(con, sql, p);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    public static List<object?[]> Filas(string sql, params (string, object?)[] p)
    {
        using var con = Abrir();
        using var cmd = Comando(con, sql, p);
        using var r = cmd.ExecuteReader();
        var filas = new List<object?[]>();
        while (r.Read())
        {
            var f = new object?[r.FieldCount];
            for (var i = 0; i < f.Length; i++) f[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            filas.Add(f);
        }
        return filas;
    }

    /// <summary>Inserta una sesión clínica directo en la tabla (para armar casos que la API no genera).</summary>
    public int InsertarSesion(DateTime fechaHora, string? emociones, string notas = "nota [TEST-PR6]", int? idConsentimiento = null) =>
        Insertar(@"INSERT INTO SesionClinica (IdPaciente, IdProfesional, FechaHora, NotasEvolucion, HistorialEmociones, IdConsentimientoVideo)
                   OUTPUT INSERTED.IdSesion VALUES (@P, @M, @F, @N, @E, @C)",
            ("@P", IdPaciente), ("@M", IdProfesional), ("@F", fechaHora), ("@N", notas), ("@E", emociones), ("@C", idConsentimiento));

    public (string? Emociones, string? Notas, string? Transcripcion, string? Cierre, int? IdConsentimiento, DateTime? AnuladasUtc, string? Motivo) Sesion(int idSesion)
    {
        var f = Filas(@"SELECT HistorialEmociones, NotasEvolucion, Transcripcion, TipoCierre, IdConsentimientoVideo, EmocionesAnuladasUtc, EmocionesAnuladasMotivo
                        FROM SesionClinica WHERE IdSesion = @I", ("@I", idSesion)).Single();
        return ((string?)f[0], (string?)f[1], (string?)f[2], (string?)f[3], (int?)f[4], (DateTime?)f[5], (string?)f[6]);
    }

    public List<(string Accion, string? Detalle)> Auditoria(string accionComo) =>
        Filas(@"SELECT Accion, Detalle FROM LogAuditoria
                WHERE IdUsuario IN (@A, @B, @C) AND Accion LIKE @L ORDER BY IdLog",
            ("@A", IdUsuarioPaciente), ("@B", IdUsuarioProfesional), ("@C", IdUsuarioConfigurador), ("@L", accionComo))
            .Select(f => ((string)f[0]!, (string?)f[1])).ToList();

    private readonly List<string> _fallosDeLimpieza = new();

    /// <summary>Los borrados de <see cref="Dispose"/> que fallaron (quedaron filas sueltas con la marca). Vacía si todo salió bien.</summary>
    public IReadOnlyList<string> FallosDeLimpieza => _fallosDeLimpieza;

    public void Dispose()
    {
        // En el orden de las claves foráneas. Todo lo de esta prueba, y nada más.
        // La auditoría es de solo agregar (migración 19): no se borra lo que dejó la API. Un usuario que actuó (tiene filas en
        // LogAuditoria) no se puede borrar (FK_LogAuditoria_Usuario) y queda en la base de pruebas, con la marca; ver más abajo.
        Intentar("DELETE FROM SesionClinica WHERE IdPaciente = @P", ("@P", IdPaciente));
        Intentar("DELETE FROM ConsentimientoVideo WHERE IdPaciente = @P", ("@P", IdPaciente));
        Intentar("DELETE FROM InformeAdmision WHERE IdPaciente = @P", ("@P", IdPaciente));
        // El servicio de recordatorios de una aplicación de prueba puede haberle creado recordatorios al turno (Recordatorio.IdTurno)
        // y una notificación al paciente (Notificacion.IdPaciente): van ANTES que el turno y el paciente, o sus claves foráneas
        // frenan esos borrados y quedan filas sueltas con la marca.
        Intentar("DELETE FROM Recordatorio WHERE IdTurno IN (SELECT IdTurno FROM Turno WHERE IdPaciente = @P OR IdProfesional = @M)", ("@P", IdPaciente), ("@M", IdProfesional));
        Intentar("DELETE FROM Notificacion WHERE IdPaciente = @P", ("@P", IdPaciente));
        Intentar("IF OBJECT_ID('dbo.TurnoConexion','U') IS NOT NULL DELETE FROM TurnoConexion WHERE IdTurno IN (SELECT IdTurno FROM Turno WHERE IdPaciente = @P)", ("@P", IdPaciente));
        Intentar("DELETE FROM Turno WHERE IdPaciente = @P", ("@P", IdPaciente));
        Intentar("DELETE FROM PacienteTutor WHERE IdPaciente = @P", ("@P", IdPaciente));
        if (_idTutor is int t) Intentar("DELETE FROM Tutor WHERE IdTutor = @T", ("@T", t));
        Intentar("DELETE FROM PacienteProfesional WHERE IdPaciente = @P", ("@P", IdPaciente));
        // Lo que apunta a ESTE profesional desde otros pacientes (cuando una prueba lo asocia o le hace guardar una
        // sesión del paciente de otra instancia): sin esto, el profesional quedaba sin borrar.
        Intentar("DELETE FROM SesionClinica WHERE IdProfesional = @M", ("@M", IdProfesional));
        Intentar("DELETE FROM PacienteProfesional WHERE IdProfesional = @M", ("@M", IdProfesional));
        Intentar("IF OBJECT_ID('dbo.TurnoConexion','U') IS NOT NULL DELETE FROM TurnoConexion WHERE IdTurno IN (SELECT IdTurno FROM Turno WHERE IdProfesional = @M)", ("@M", IdProfesional));
        Intentar("DELETE FROM Turno WHERE IdProfesional = @M", ("@M", IdProfesional));
        Intentar("DELETE FROM Profesional WHERE IdProfesional = @M", ("@M", IdProfesional));
        Intentar("DELETE FROM Paciente WHERE IdPaciente = @P", ("@P", IdPaciente));
        Intentar("DELETE FROM Usuario WHERE IdUsuario IN (@A, @B, @C) AND NOT EXISTS (SELECT 1 FROM LogAuditoria l WHERE l.IdUsuario = Usuario.IdUsuario)",
            ("@A", IdUsuarioPaciente), ("@B", IdUsuarioProfesional), ("@C", IdUsuarioConfigurador));
        if (_idEspecialidadCreada is int e) Intentar("DELETE FROM Especialidad WHERE IdEspecialidad = @E", ("@E", e));
        if (_estadoTomado)
        {
            _estadoTomado = false;
            // Catálogo COMPARTIDO: lo borra la última prueba que lo usa; si lo está usando un turno que no es de esta prueba, la clave foránea (547) frena el
            // borrado y el estado queda, como en cualquier base real. Es lo único que puede fallar a propósito; cualquier otro error sí se registra.
            if (EstadosCompartidos.Soltar("Confirmado") is { Number: not 547 } falla)
                Registrar("DELETE FROM EstadoTurno WHERE IdEstadoTurno = <el de Confirmado>", falla);
        }

        if (_fallosDeLimpieza.Count > 0)
            Informar($"[DatosEmociones {Sala}] no se pudo borrar {_fallosDeLimpieza.Count} cosa(s); quedan filas sueltas con la marca {Marca}:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", _fallosDeLimpieza));
    }

    private void Informar(string mensaje)
    {
        try
        {
            if (_salida is not null) { _salida.WriteLine(mensaje); return; }
        }
        catch (InvalidOperationException) { /* la prueba ya terminó y no tiene dónde escribir */ }
        Console.Error.WriteLine(mensaje);
    }

    /// <summary>
    /// Un borrado de la limpieza. Si falla no se relanza (un borrado que no corresponde no tiene que tapar el resultado de la
    /// prueba), pero tampoco se calla: queda en <see cref="FallosDeLimpieza"/> y <see cref="Dispose"/> lo escribe en la salida de
    /// la prueba, porque un borrado que falla deja filas sueltas en la base.
    /// </summary>
    public void Intentar(string sql, params (string, object?)[] p)
    {
        try { Ejecutar(sql, p); }
        catch (SqlException e) { Registrar(sql, e); }
    }

    private void Registrar(string sql, SqlException e) =>
        _fallosDeLimpieza.Add($"{sql.Trim()}  ->  SqlException {e.Number}: {e.Message.Split('\n')[0].Trim()}");
}

[Trait("Categoria", "Integracion")]
public class EmocionesBaseRealTests
{
    private readonly ITestOutputHelper _salida;

    public EmocionesBaseRealTests(ITestOutputHelper salida) { _salida = salida; }

    private static readonly Dictionary<string, string?> ConClave = new()
    {
        ["Video:Emociones:ClaveCifrado"] = EscenarioEmociones.ClaveDePrueba,
        ["Video:LimitePorMinuto"] = "1000",
        ["Video:Emociones:LimitePorMinuto"] = "1000"
    };

    private static VideoApp App() => new(ConClave, repositorioReal: true, auditoriaReal: true);

    private static string Iso(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("O");

    private static Dictionary<string, double> Promedios() => new()
    {
        ["neutral"] = 60, ["happy"] = 10, ["sad"] = 20, ["angry"] = 3, ["fearful"] = 3, ["surprised"] = 2, ["disgusted"] = 2
    };

    private static async Task<JsonElement> Aceptar(VideoApp app, DatosEmociones d, string origen = "turno", int? id = null)
    {
        var paciente = app.Cliente(d.IdUsuarioPaciente, Roles.Paciente, idPaciente: d.IdPaciente);
        var (_, texto) = await Get(paciente, $"/video/consentimiento?origen={origen}&id={id ?? d.IdTurno}");
        var r = await paciente.PutAsJsonAsync("/video/consentimiento",
            new { origen, id = id ?? d.IdTurno, decision = "acepta", version = texto.GetProperty("version").GetString() });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await VideoApp.Json(r);
    }

    private static async Task<(HttpResponseMessage R, JsonElement J)> Get(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        return (r, await VideoApp.Json(r));
    }

    // El pedido de guardado del profesional: ventana que arranca en la aceptación (lo que dice el servidor) y dura 2 s.
    private static async Task<(HttpResponseMessage R, JsonElement J)> Guardar(VideoApp app, DatosEmociones d, object? emociones = null,
        string notas = "nota [TEST-PR6]", string? transcripcion = null, string? historialEmocionesViejo = null, HttpClient? cliente = null)
    {
        var profesional = cliente ?? app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);
        if (emociones is null)
        {
            var (_, permiso) = await Get(profesional, $"/video/emociones/permiso?origen=turno&id={d.IdTurno}");
            var desde = permiso.GetProperty("desdeUtc").GetDateTime().ToUniversalTime();
            emociones = new { origen = "turno", id = d.IdTurno, desdeUtc = Iso(desde), hastaUtc = Iso(desde.AddSeconds(2)), muestras = 4, promedios = Promedios() };
        }
        var r = await profesional.PostAsJsonAsync("/sesiones-clinicas", new
        {
            idPaciente = d.IdPaciente, idTurno = d.IdTurno, notasEvolucion = notas, transcripcion, tipoCierre = "Seguimiento",
            emociones, historialEmociones = historialEmocionesViejo
        });
        return (r, await VideoApp.Json(r));
    }

    // ───────────── ConsentimientoVideoDAO ─────────────

    [FactConBaseDeConsentimiento]
    public void El_DAO_guarda_una_fila_por_decision_y_la_ultima_es_la_vigente_con_horas_utc_exactas()
    {
        using var d = new DatosEmociones(salida: _salida);
        var dao = new ConsentimientoVideoDAO();
        var t0 = new DateTime(2031, 3, 4, 10, 0, 0, 123, DateTimeKind.Utc);

        var rechazo = dao.Insertar(d.IdUsuarioPaciente, d.IdPaciente, "turno", d.IdTurno, "v0-provisorio", new string('a', 64), "rechaza", t0);
        var acepta = dao.Insertar(d.IdUsuarioPaciente, d.IdPaciente, "turno", d.IdTurno, "v0-provisorio", new string('b', 64), "acepta", t0.AddMinutes(1));
        var ultimo = dao.Ultimo("turno", d.IdTurno, d.IdPaciente)!;

        Assert.True(acepta.IdConsentimientoVideo > rechazo.IdConsentimientoVideo);
        Assert.Equal(acepta.IdConsentimientoVideo, ultimo.IdConsentimientoVideo);
        Assert.Equal("acepta", ultimo.Decision);
        Assert.Equal(new string('b', 64), ultimo.HashTexto);
        Assert.Equal(t0.AddMinutes(1), ultimo.DecididoUtc);                 // milisegundos incluidos
        Assert.Equal(DateTimeKind.Utc, ultimo.DecididoUtc.Kind);
        Assert.Null(ultimo.RevocadoUtc);
        Assert.True(ultimo.EsAceptacionVigente);
        Assert.Null(dao.Ultimo("turno", d.IdTurno + 1000, d.IdPaciente));   // otra sesión: nada
    }

    [FactConBaseDeConsentimiento]
    public void Revocar_marca_la_hora_una_sola_vez_y_nunca_antes_de_la_decision()
    {
        using var d = new DatosEmociones(salida: _salida);
        var dao = new ConsentimientoVideoDAO();
        var t0 = new DateTime(2031, 3, 4, 10, 0, 0, DateTimeKind.Utc);
        var fila = dao.Insertar(d.IdUsuarioPaciente, d.IdPaciente, "turno", d.IdTurno, "v", new string('c', 64), "acepta", t0);

        // Un reloj corrido hacia atrás no rompe la restricción: se usa la hora de la decisión.
        Assert.True(dao.Revocar(fila.IdConsentimientoVideo, t0.AddSeconds(-30)));
        var revocada = dao.Ultimo("turno", d.IdTurno, d.IdPaciente)!;
        Assert.Equal(t0, revocada.RevocadoUtc);
        Assert.False(revocada.EsAceptacionVigente);

        // Una segunda vez no cambia nada.
        Assert.False(dao.Revocar(fila.IdConsentimientoVideo, t0.AddHours(1)));
        Assert.Equal(t0, dao.Ultimo("turno", d.IdTurno, d.IdPaciente)!.RevocadoUtc);

        // Un rechazo no se revoca.
        var rechazo = dao.Insertar(d.IdUsuarioPaciente, d.IdPaciente, "turno", d.IdTurno, "v", new string('d', 64), "rechaza", t0.AddMinutes(1));
        Assert.False(dao.Revocar(rechazo.IdConsentimientoVideo, t0.AddMinutes(2)));
    }

    [FactConBaseDeConsentimiento]
    public void Los_datos_del_paciente_para_decidir_si_se_le_ofrece_el_analisis()
    {
        using var d = new DatosEmociones(nacimiento: new DateTime(1980, 2, 2), salida: _salida);
        var dao = new ConsentimientoVideoDAO();

        var adulto = dao.ObtenerDatosPaciente(d.IdPaciente)!;
        d.ConTutor();
        var conTutor = dao.ObtenerDatosPaciente(d.IdPaciente)!;
        d.CambiarNacimiento(null);
        var sinFecha = dao.ObtenerDatosPaciente(d.IdPaciente)!;

        Assert.Equal(new DateTime(1980, 2, 2), adulto.FechaNacimiento);
        Assert.False(adulto.TieneTutor);
        Assert.True(conTutor.TieneTutor);
        Assert.Null(sinFecha.FechaNacimiento);
        Assert.Null(dao.ObtenerDatosPaciente(-1));
    }

    // ───────────── Punta a punta con la aplicación real ─────────────

    [FactConBaseDeConsentimiento]
    public async Task Aceptar_y_revocar_quedan_en_la_tabla_con_version_hash_y_auditoria()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var app = App();

        var acepta = await Aceptar(app, d);

        var fila = DatosEmociones.Filas(@"SELECT Decision, VersionTexto, HashTexto, DecididoUtc, RevocadoUtc, IdUsuario, IdPaciente, Origen, IdOrigen
                                          FROM ConsentimientoVideo WHERE IdPaciente = @P", ("@P", d.IdPaciente)).Single();
        Assert.Equal("acepta", fila[0]);
        Assert.Equal("v0-provisorio", fila[1]);
        var hashEsperado = new TextoConsentimientoVideo(new OpcionesFijas<VideoOptions>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TextoConsentimientoVideo>.Instance).Actual()!.Hash;
        Assert.Equal(hashEsperado, ((string)fila[2]!).Trim());
        Assert.InRange(((DateTime)fila[3]!), DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));   // es UTC de verdad
        Assert.Null(fila[4]);
        Assert.Equal((d.IdUsuarioPaciente, d.IdPaciente, "turno", d.IdTurno), ((int)fila[5]!, (int)fila[6]!, (string)fila[7]!, (int)fila[8]!));
        Assert.Equal(JsonValueKind.String, acepta.GetProperty("decididoUtc").ValueKind);

        var paciente = app.Cliente(d.IdUsuarioPaciente, Roles.Paciente, idPaciente: d.IdPaciente);
        var r = await paciente.PostAsJsonAsync("/video/consentimiento/revocar", new { origen = "turno", id = d.IdTurno });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var revocada = DatosEmociones.Filas("SELECT RevocadoUtc FROM ConsentimientoVideo WHERE IdPaciente = @P", ("@P", d.IdPaciente)).Single();
        Assert.NotNull(revocada[0]);
        var auditoria = d.Auditoria("Consentimiento de video%");
        Assert.Equal(new[] { "Consentimiento de video aceptado", "Consentimiento de video revocado" }, auditoria.Select(a => a.Accion).ToArray());
        Assert.Contains("version: v0-provisorio", auditoria[0].Detalle);
        Assert.Contains("emociones anuladas: 0", auditoria[1].Detalle);
        Assert.Equal(("Turno", (int?)d.IdTurno), AuditoriaEntidad(d, "Consentimiento de video aceptado"));
    }

    private static (string?, int?) AuditoriaEntidad(DatosEmociones d, string accion)
    {
        var f = DatosEmociones.Filas("SELECT Entidad, IdEntidad FROM LogAuditoria WHERE IdUsuario = @U AND Accion = @A", ("@U", d.IdUsuarioPaciente), ("@A", accion)).Single();
        return ((string?)f[0], (int?)f[1]);
    }

    [FactConBaseDeConsentimiento]
    public async Task Las_emociones_se_guardan_cifradas_el_profesional_las_lee_y_el_auditor_y_el_configurador_no()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var app = App();
        await Aceptar(app, d);

        var (r, j) = await Guardar(app, d, transcripcion: "transcripción [TEST-PR6]");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var idSesion = j.GetProperty("idSesion").GetInt32();
        Assert.Equal("guardadas", j.GetProperty("emociones").GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("emociones").GetProperty("motivo").ValueKind);

        // En la base: cifrado, atado a la fila y al consentimiento; las notas, en claro como siempre.
        var fila = d.Sesion(idSesion);
        Assert.StartsWith("ys1.k1.", fila.Emociones);
        // El base64 puede contener "sad" por azar. Validar el cifrado autenticado y
        // su contenido descifrado evita confundir coincidencias con texto en claro.
        var cifrado = app.Factory.Services.GetRequiredService<IEmocionesCifrado>();
        Assert.Equal(ResultadoDescifrado.Ok, cifrado.Descifrar(fila.Emociones!,
            EmocionesCifradoService.DatosAsociadosDeSesion(idSesion), out var textoPlano));
        Assert.NotEqual(textoPlano, fila.Emociones);
        using var contenido = JsonDocument.Parse(textoPlano!);
        Assert.Equal(JsonValueKind.Object, contenido.RootElement.ValueKind);
        Assert.NotNull(fila.IdConsentimiento);
        Assert.Equal("nota [TEST-PR6]", fila.Notas);
        Assert.Null(fila.AnuladasUtc);
        Assert.Single(d.Auditoria("Emociones guardadas"));

        // El profesional las recibe descifradas, con las etiquetas de siempre, y con los datos de la ventana.
        var profesional = app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);
        var (rLista, lista) = await Get(profesional, $"/pacientes/{d.IdPaciente}/sesiones-clinicas");
        Assert.Equal(HttpStatusCode.OK, rLista.StatusCode);
        var sesion = lista.EnumerateArray().Single();
        var emociones = JsonDocument.Parse(sesion.GetProperty("historialEmociones").GetString()!).RootElement;
        Assert.Equal(60, emociones.GetProperty("Neutral").GetInt32());
        Assert.Equal(10, emociones.GetProperty("Felicidad").GetInt32());
        Assert.Equal(20, emociones.GetProperty("Tristeza").GetInt32());
        Assert.Equal(4, sesion.GetProperty("emociones").GetProperty("muestras").GetInt32());
        Assert.Equal("nota [TEST-PR6]", sesion.GetProperty("notasEvolucion").GetString());
        Assert.Equal("transcripción [TEST-PR6]", sesion.GetProperty("transcripcion").GetString());
        Assert.Equal(new[] { "emociones", "fechaHora", "historialEmociones", "idEvolucion", "idPaciente", "idProfesional", "idSesion", "idTurno", "notasEvolucion", "tipoCierre", "transcripcion" },
            sesion.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        var (_, porProfesional) = await Get(profesional, $"/profesionales/{d.IdProfesional}/sesiones-clinicas");
        Assert.NotEqual(JsonValueKind.Null, porProfesional.EnumerateArray().Single().GetProperty("historialEmociones").ValueKind);

        // El auditor recibe la sesión SIN las emociones, ni nada que las delate (ni lo cifrado). El Configurador ni siquiera entra (403).
        // El auditor (un profesional que no atiende a este paciente, con el permiso auditor) lee las notas, pero no las emociones.
        var auditor = app.ConToken(app.Factory.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(d.IdUsuarioConfigurador,
            new SesionModulo(Modulos.Profesional, new[] { Roles.Auditor, Roles.Profesional }, Array.Empty<string>(), 987654)));
        var configurador = app.Cliente(d.IdUsuarioConfigurador, Roles.Configurador);
        Assert.Equal(HttpStatusCode.Forbidden, (await configurador.GetAsync($"/pacientes/{d.IdPaciente}/sesiones-clinicas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await configurador.GetAsync($"/profesionales/{d.IdProfesional}/sesiones-clinicas")).StatusCode);
        var rCfg = await auditor.GetAsync($"/pacientes/{d.IdPaciente}/sesiones-clinicas");
        var cuerpoCfg = await rCfg.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, rCfg.StatusCode);
        using var docCfg = JsonDocument.Parse(cuerpoCfg);
        var sesionCfg = docCfg.RootElement.EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, sesionCfg.GetProperty("historialEmociones").ValueKind);
        Assert.Equal(JsonValueKind.Null, sesionCfg.GetProperty("emociones").ValueKind);
        Assert.Equal("nota [TEST-PR6]", sesionCfg.GetProperty("notasEvolucion").GetString());   // lo demás, igual que siempre
        foreach (var rastro in new[] { "ys1.", "Neutral", "Felicidad", "Tristeza", "promedios" }) Assert.DoesNotContain(rastro, cuerpoCfg);

        var cuerpoCfg2 = await (await auditor.GetAsync($"/profesionales/{d.IdProfesional}/sesiones-clinicas")).Content.ReadAsStringAsync();
        foreach (var rastro in new[] { "ys1.", "Neutral", "Felicidad", "Tristeza", "promedios" }) Assert.DoesNotContain(rastro, cuerpoCfg2);
    }

    [FactConBaseDeConsentimiento]
    public async Task Sin_consentimiento_la_sesion_se_guarda_sin_emociones_y_lo_dice()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var app = App();
        var desde = DateTime.UtcNow.AddSeconds(-10);
        var emociones = new { origen = "turno", id = d.IdTurno, desdeUtc = Iso(desde), hastaUtc = Iso(desde.AddSeconds(5)), muestras = 8, promedios = Promedios() };

        var (r, j) = await Guardar(app, d, emociones);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var e = j.GetProperty("emociones");
        Assert.Equal("descartadas", e.GetProperty("estado").GetString());
        Assert.Equal("sin-consentimiento", e.GetProperty("motivo").GetString());
        Assert.Contains("se guardó sin las emociones", e.GetProperty("mensaje").GetString());
        var fila = d.Sesion(j.GetProperty("idSesion").GetInt32());
        Assert.Null(fila.Emociones);
        Assert.Null(fila.IdConsentimiento);
        Assert.Equal("nota [TEST-PR6]", fila.Notas);              // la sesión se guardó igual
        var descartadas = d.Auditoria("Emociones descartadas");
        Assert.Equal($"motivo: sin-consentimiento; sesion: {j.GetProperty("idSesion").GetInt32()}", Assert.Single(descartadas).Detalle);
    }

    [FactConBaseDeConsentimiento]
    public async Task El_campo_historialEmociones_de_antes_del_PR_6_ya_no_se_guarda()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var app = App();
        await Aceptar(app, d);   // aun con una aceptación vigente

        var profesional = app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);
        var r = await profesional.PostAsJsonAsync("/sesiones-clinicas", new
        {
            idPaciente = d.IdPaciente, notasEvolucion = "nota [TEST-PR6]", historialEmociones = "{\"Neutral\":99}"
        });
        var j = await VideoApp.Json(r);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("formato-viejo", j.GetProperty("emociones").GetProperty("motivo").GetString());
        Assert.Null(d.Sesion(j.GetProperty("idSesion").GetInt32()).Emociones);
    }

    [FactConBaseDeConsentimiento]
    public async Task Revocar_despues_de_guardar_anula_las_emociones_y_deja_intactas_las_notas()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var app = App();
        await Aceptar(app, d);
        var (_, guardada) = await Guardar(app, d, notas: "nota clínica [TEST-PR6]", transcripcion: "transcripción [TEST-PR6]");
        var idSesion = guardada.GetProperty("idSesion").GetInt32();
        Assert.NotNull(d.Sesion(idSesion).Emociones);

        var paciente = app.Cliente(d.IdUsuarioPaciente, Roles.Paciente, idPaciente: d.IdPaciente);
        var r = await paciente.PostAsJsonAsync("/video/consentimiento/revocar", new { origen = "turno", id = d.IdTurno });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var fila = d.Sesion(idSesion);
        Assert.Null(fila.Emociones);                                         // anuladas
        Assert.Equal("revocacion", fila.Motivo);
        Assert.InRange(fila.AnuladasUtc!.Value, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
        Assert.Equal("nota clínica [TEST-PR6]", fila.Notas);                 // las notas, la transcripción y el cierre, intactos
        Assert.Equal("transcripción [TEST-PR6]", fila.Transcripcion);
        Assert.Equal("Seguimiento", fila.Cierre);
        Assert.NotNull(fila.IdConsentimiento);

        var revoca = d.Auditoria("Consentimiento de video revocado").Single();
        Assert.Contains("emociones anuladas: 1", revoca.Detalle);

        // El profesional ya no recibe emociones de esa sesión, pero sí que se anularon y por qué.
        var profesional = app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);
        var (_, lista) = await Get(profesional, $"/pacientes/{d.IdPaciente}/sesiones-clinicas");
        var sesion = lista.EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, sesion.GetProperty("historialEmociones").ValueKind);
        Assert.Equal("revocacion", sesion.GetProperty("emociones").GetProperty("anuladasMotivo").GetString());
        Assert.Equal("nota clínica [TEST-PR6]", sesion.GetProperty("notasEvolucion").GetString());

        // Y lo que venga después tampoco se guarda.
        var (_, otra) = await Guardar(app, d, notas: "otra nota [TEST-PR6]");
        Assert.Equal("revocado", otra.GetProperty("emociones").GetProperty("motivo").GetString());
    }

    [FactConBaseDeConsentimiento]
    public async Task Un_dato_cifrado_no_se_puede_mover_a_otra_sesion_ni_alterar()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var app = App();
        await Aceptar(app, d);
        var (_, a) = await Guardar(app, d, notas: "A [TEST-PR6]");
        var (_, b) = await Guardar(app, d, notas: "B [TEST-PR6]");
        var idA = a.GetProperty("idSesion").GetInt32();
        var idB = b.GetProperty("idSesion").GetInt32();
        var cifradoA = d.Sesion(idA).Emociones!;
        var profesional = app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);

        async Task<JsonElement> SesionDe(int id)
        {
            var (_, lista) = await Get(profesional, $"/pacientes/{d.IdPaciente}/sesiones-clinicas");
            return lista.EnumerateArray().Single(s => s.GetProperty("idSesion").GetInt32() == id);
        }

        Assert.NotEqual(JsonValueKind.Null, (await SesionDe(idB)).GetProperty("historialEmociones").ValueKind);

        // El cifrado de A copiado en la fila de B: no se descifra (los datos asociados son el id de cada fila).
        DatosEmociones.Ejecutar("UPDATE SesionClinica SET HistorialEmociones = @C WHERE IdSesion = @I", ("@C", cifradoA), ("@I", idB));
        Assert.Equal(JsonValueKind.Null, (await SesionDe(idB)).GetProperty("historialEmociones").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, (await SesionDe(idA)).GetProperty("historialEmociones").ValueKind);   // la original sigue bien

        // Un carácter cambiado: tampoco. En el medio del dato, donde cada carácter pesa 6 bits: el último de un base64 puede
        // traer bits de relleno y cambiarlo a veces no cambia ningún byte (la prueba andaría o no según el nonce).
        var medio = cifradoA.Length / 2;
        var alterado = cifradoA[..medio] + (cifradoA[medio] == 'A' ? 'B' : 'A') + cifradoA[(medio + 1)..];
        DatosEmociones.Ejecutar("UPDATE SesionClinica SET HistorialEmociones = @C WHERE IdSesion = @I", ("@C", alterado), ("@I", idA));
        var vista = await SesionDe(idA);
        Assert.Equal(JsonValueKind.Null, vista.GetProperty("historialEmociones").ValueKind);
        Assert.Equal("A [TEST-PR6]", vista.GetProperty("notasEvolucion").GetString());   // y la sesión sigue legible
    }

    [FactConBaseDeConsentimiento]
    public async Task Lo_guardado_en_claro_antes_del_PR_6_se_sigue_leyendo_y_el_auditor_tampoco_lo_ve()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var app = App();
        d.InsertarSesion(DateTime.Now.AddDays(-400), "{\"Neutral\":70,\"Tristeza\":30}");
        var profesional = app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);
        // El auditor (un profesional que no atiende a este paciente, con el permiso auditor) lee las notas, pero no las emociones.
        var auditor = app.ConToken(app.Factory.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(d.IdUsuarioConfigurador,
            new SesionModulo(Modulos.Profesional, new[] { Roles.Auditor, Roles.Profesional }, Array.Empty<string>(), 987654)));

        var (_, lista) = await Get(profesional, $"/pacientes/{d.IdPaciente}/sesiones-clinicas");
        var cuerpoCfg = await (await auditor.GetAsync($"/pacientes/{d.IdPaciente}/sesiones-clinicas")).Content.ReadAsStringAsync();

        var sesion = lista.EnumerateArray().Single();
        Assert.Equal("{\"Neutral\":70,\"Tristeza\":30}", sesion.GetProperty("historialEmociones").GetString());   // tal cual, como siempre
        Assert.DoesNotContain("Neutral", cuerpoCfg);
        Assert.DoesNotContain("Tristeza", cuerpoCfg);
    }

    [FactConBaseDeConsentimiento]
    public async Task Sin_clave_de_cifrado_las_emociones_cifradas_no_se_devuelven_ni_se_filtran()
    {
        using var d = new DatosEmociones(salida: _salida);
        string? cifrado;
        int idSesion;
        using (var conClave = App())
        {
            await Aceptar(conClave, d);
            var (_, j) = await Guardar(conClave, d);
            idSesion = j.GetProperty("idSesion").GetInt32();
            cifrado = d.Sesion(idSesion).Emociones;
            Assert.StartsWith("ys1.", cifrado);
        }

        using var sinClave = new VideoApp(new() { ["Video:LimitePorMinuto"] = "1000", ["Video:Emociones:ClaveCifrado"] = null },
            repositorioReal: true, auditoriaReal: true);
        var profesional = sinClave.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);
        var r = await profesional.GetAsync($"/pacientes/{d.IdPaciente}/sesiones-clinicas");
        var cuerpo = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.DoesNotContain("ys1.", cuerpo);                    // nunca se devuelve el texto cifrado
        Assert.Contains("\"historialEmociones\":null", cuerpo);
        Assert.Equal(cifrado, d.Sesion(idSesion).Emociones);      // y no se pierde nada: sigue en la base
    }

    [FactConBaseDeConsentimiento]
    public async Task Un_menor_no_tiene_analisis_en_ningun_punto_del_circuito_y_la_llamada_no_depende_de_eso()
    {
        using var d = new DatosEmociones(nacimiento: new DateTime(2016, 9, 9), salida: _salida);
        using var app = App();
        var paciente = app.Cliente(d.IdUsuarioPaciente, Roles.Paciente, idPaciente: d.IdPaciente);
        var profesional = app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);

        var (rTexto, texto) = await Get(paciente, $"/video/consentimiento?origen=turno&id={d.IdTurno}");
        var decidir = await paciente.PutAsJsonAsync("/video/consentimiento", new { origen = "turno", id = d.IdTurno, decision = "acepta", version = "v0-provisorio" });
        var (_, permiso) = await Get(profesional, $"/video/emociones/permiso?origen=turno&id={d.IdTurno}");
        var (rGuardar, guardar) = await Guardar(app, d, new
        {
            origen = "turno", id = d.IdTurno, desdeUtc = Iso(DateTime.UtcNow.AddSeconds(-10)), hastaUtc = Iso(DateTime.UtcNow), muestras = 8, promedios = Promedios()
        });
        var acceso = await VideoApp.Acceder(paciente, "turno", d.IdTurno);   // la sala sí se puede abrir

        Assert.Equal(HttpStatusCode.OK, rTexto.StatusCode);
        Assert.Equal("menor-de-edad", texto.GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.Conflict, decidir.StatusCode);
        Assert.False(permiso.GetProperty("permitido").GetBoolean());
        Assert.Equal("menor-de-edad", permiso.GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.Created, rGuardar.StatusCode);
        Assert.Equal("menor-de-edad", guardar.GetProperty("emociones").GetProperty("motivo").GetString());
        Assert.Null(d.Sesion(guardar.GetProperty("idSesion").GetInt32()).Emociones);
        Assert.Equal(0, (int)DatosEmociones.Escalar("SELECT COUNT(*) FROM ConsentimientoVideo WHERE IdPaciente = @P", ("@P", d.IdPaciente))!);
        Assert.Equal(HttpStatusCode.OK, acceso.StatusCode);
    }

    [FactConBaseDeConsentimiento]
    public async Task La_entrevista_de_admision_decide_y_guarda_igual_con_quien_tiene_el_privilegio_Admision_en_el_asiento_del_profesional()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var app = App();
        var idInforme = d.IdInforme;
        await Aceptar(app, d, "admision", idInforme);
        // Etapa 2 de roles: la entrevista la hace un profesional con el privilegio Admisión (antes, el Configurador).
        var configurador = app.ConToken(app.Factory.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(d.IdUsuarioConfigurador,
            new SesionModulo(Modulos.Profesional, new[] { Roles.Profesional }, new[] { Privilegios.Admision }, d.IdProfesional)));

        var (_, permiso) = await Get(configurador, $"/video/emociones/permiso?origen=admision&id={idInforme}");
        Assert.True(permiso.GetProperty("permitido").GetBoolean());
        var desde = permiso.GetProperty("desdeUtc").GetDateTime().ToUniversalTime();
        var r = await configurador.PostAsJsonAsync("/sesiones-clinicas", new
        {
            idPaciente = d.IdPaciente, idProfesional = d.IdProfesional, notasEvolucion = "admisión [TEST-PR6]",
            emociones = new { origen = "admision", id = idInforme, desdeUtc = Iso(desde), hastaUtc = Iso(desde.AddSeconds(2)), muestras = 4, promedios = Promedios() }
        });
        var j = await VideoApp.Json(r);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("guardadas", j.GetProperty("emociones").GetProperty("estado").GetString());
        var fila = d.Sesion(j.GetProperty("idSesion").GetInt32());
        Assert.StartsWith("ys1.", fila.Emociones);
        Assert.NotNull(fila.IdConsentimiento);
        Assert.Equal("admision", DatosEmociones.Escalar("SELECT Origen FROM ConsentimientoVideo WHERE IdConsentimientoVideo = @I", ("@I", fila.IdConsentimiento))!);
        Assert.Equal(("InformeAdmision", (int?)idInforme), AuditoriaEntidad(d, "Consentimiento de video aceptado"));
    }

    [FactConBaseDeConsentimiento]
    public async Task Otro_profesional_no_puede_guardar_las_emociones_de_una_sesion_que_no_es_suya()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var otro = new DatosEmociones(salida: _salida);   // otro profesional, con sus propios pacientes
        using var app = App();
        await Aceptar(app, d);
        // El otro profesional está asociado también a este paciente (por ejemplo, por una derivación), pero no es el del turno.
        DatosEmociones.Ejecutar("INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, EstadoAsignacion) VALUES (@P, @M, 'Activo')",
            ("@P", d.IdPaciente), ("@M", otro.IdProfesional));
        var ajeno = app.Cliente(otro.IdUsuarioProfesional, Roles.Profesional, idProfesional: otro.IdProfesional);
        var propio = app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);
        var (_, permiso) = await Get(propio, $"/video/emociones/permiso?origen=turno&id={d.IdTurno}");
        var desde = permiso.GetProperty("desdeUtc").GetDateTime().ToUniversalTime();
        var emociones = new { origen = "turno", id = d.IdTurno, desdeUtc = Iso(desde), hastaUtc = Iso(desde.AddSeconds(2)), muestras = 4, promedios = Promedios() };

        var (rAjeno, jAjeno) = await Guardar(app, d, emociones, cliente: ajeno);

        Assert.Equal(HttpStatusCode.Created, rAjeno.StatusCode);   // la sesión clínica se guarda (está asociado), las emociones no
        Assert.Equal("sesion-ajena", jAjeno.GetProperty("emociones").GetProperty("motivo").GetString());
        Assert.Null(d.Sesion(jAjeno.GetProperty("idSesion").GetInt32()).Emociones);
        // Y ni siquiera puede preguntar por el permiso: para él esa sesión no existe.
        var (rPermisoAjeno, _) = await Get(ajeno, $"/video/emociones/permiso?origen=turno&id={d.IdTurno}");
        Assert.Equal(HttpStatusCode.NotFound, rPermisoAjeno.StatusCode);
    }

    // ───────────── Atomicidad ─────────────

    [FactConBaseDeConsentimiento]
    public void Si_falla_el_cifrado_no_queda_la_sesion_a_medio_guardar()
    {
        using var d = new DatosEmociones(salida: _salida);
        var antes = (int)DatosEmociones.Escalar("SELECT COUNT(*) FROM SesionClinica WHERE IdPaciente = @P", ("@P", d.IdPaciente))!;

        Assert.Throws<InvalidOperationException>(() => new SesionClinicaDAO().Insertar(
            new SesionClinica { IdPaciente = d.IdPaciente, IdProfesional = d.IdProfesional, FechaHora = DateTime.Now, NotasEvolucion = "x [TEST-PR6]" },
            _ => throw new InvalidOperationException("falla el cifrado")));

        var despues = (int)DatosEmociones.Escalar("SELECT COUNT(*) FROM SesionClinica WHERE IdPaciente = @P", ("@P", d.IdPaciente))!;
        Assert.Equal(antes, despues);
    }

    [FactConBaseDeConsentimiento]
    public void La_escritura_de_las_emociones_vuelve_a_mirar_el_consentimiento_y_no_guarda_si_se_revoco()
    {
        using var d = new DatosEmociones(salida: _salida);
        var consentimientos = new ConsentimientoVideoDAO();
        var ahora = DateTime.UtcNow;
        var vigente = consentimientos.Insertar(d.IdUsuarioPaciente, d.IdPaciente, "turno", d.IdTurno, "v0-provisorio", new string('a', 64), "acepta", ahora);
        var revocado = consentimientos.Insertar(d.IdUsuarioPaciente, d.IdPaciente, "turno", d.IdTurno, "v0-provisorio", new string('b', 64), "acepta", ahora.AddSeconds(1));
        Assert.True(consentimientos.Revocar(revocado.IdConsentimientoVideo, ahora.AddSeconds(2)));
        var dao = new SesionClinicaDAO();
        SesionClinica Sesion(int? idConsentimiento, string notas) => new()
        {
            IdPaciente = d.IdPaciente, IdProfesional = d.IdProfesional, FechaHora = DateTime.Now, NotasEvolucion = notas,
            IdConsentimientoVideo = idConsentimiento
        };

        var conInexistente = dao.Insertar(Sesion(int.MaxValue, "a [TEST-PR6]"), _ => "ys1.k1.AAAA");   // un consentimiento que no existe
        var conRevocado = dao.Insertar(Sesion(revocado.IdConsentimientoVideo, "b [TEST-PR6]"), _ => "ys1.k1.BBBB");
        var sinConsentimiento = dao.Insertar(Sesion(null, "c [TEST-PR6]"), _ => "ys1.k1.CCCC");
        // Una sesión sin emociones no cambia.
        var sinEmociones = dao.Insertar(Sesion(null, "d [TEST-PR6]"));

        Assert.False(conInexistente.EmocionesGuardadas);
        Assert.False(conRevocado.EmocionesGuardadas);                       // revocado entre la validación y la escritura
        Assert.False(sinConsentimiento.EmocionesGuardadas);
        Assert.False(sinEmociones.EmocionesGuardadas);
        foreach (var id in new[] { conInexistente.IdSesion, conRevocado.IdSesion, sinConsentimiento.IdSesion, sinEmociones.IdSesion })
        {
            var f = d.Sesion(id);
            Assert.Null(f.Emociones);                                       // nada de lo medido quedó
            Assert.Null(f.IdConsentimiento);
        }
        Assert.Equal("b [TEST-PR6]", d.Sesion(conRevocado.IdSesion).Notas);   // pero la sesión y sus notas se guardaron

        // Con la aceptación vigente sí se guarda, atada a ese consentimiento.
        var ok = dao.Insertar(Sesion(vigente.IdConsentimientoVideo, "e [TEST-PR6]"), id => $"ys1.k1.{id}");
        Assert.True(ok.EmocionesGuardadas);
        var guardada = d.Sesion(ok.IdSesion);
        Assert.Equal($"ys1.k1.{ok.IdSesion}", guardada.Emociones);          // el cifrado recibe el IdSesion de ESA fila
        Assert.Equal(vigente.IdConsentimientoVideo, guardada.IdConsentimiento);
    }

    // ───────────── Purga real ─────────────

    // OJO: la purga es global por diseño (anula lo vencido de TODOS los pacientes). Por eso esta prueba usa un corte de 1990,
    // más viejo que cualquier dato real de una base de desarrollo: lo único que alcanza son las filas que ella misma inserta.
    [FactConBaseDeConsentimiento]
    public void La_purga_real_anula_solo_lo_vencido_por_lotes_y_no_toca_las_notas()
    {
        using var d = new DatosEmociones(salida: _salida);
        var corte = new DateTime(1990, 1, 1);
        Assert.Equal(0, (int)DatosEmociones.Escalar(
            "SELECT COUNT(*) FROM SesionClinica WHERE HistorialEmociones IS NOT NULL AND FechaHora < @C AND IdPaciente <> @P",
            ("@C", corte), ("@P", d.IdPaciente))!);   // si la base tuviera datos de antes de 1990, esta prueba no se corre
        var vieja1 = d.InsertarSesion(corte.AddDays(-30), "ys1.k1.AAAA", "nota vieja 1 [TEST-PR6]");
        var vieja2 = d.InsertarSesion(corte.AddDays(-1), "ys1.k1.BBBB", "nota vieja 2 [TEST-PR6]");
        var justo = d.InsertarSesion(corte, "ys1.k1.CCCC", "nota justo [TEST-PR6]");
        var reciente = d.InsertarSesion(corte.AddDays(30), "ys1.k1.DDDD", "nota reciente [TEST-PR6]");
        var sinEmociones = d.InsertarSesion(corte.AddDays(-90), null, "solo nota [TEST-PR6]");
        var dao = new EmocionesPurgaDAO();
        var ahora = new DateTime(2031, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        int Vencidas() => (int)DatosEmociones.Escalar(
            "SELECT COUNT(*) FROM SesionClinica WHERE IdPaciente = @P AND HistorialEmociones IS NOT NULL AND FechaHora < @C", ("@P", d.IdPaciente), ("@C", corte))!;
        Assert.Equal(2, Vencidas());
        Assert.Equal(2, dao.ContarVencidas(corte));

        // Con un lote de 1 solo se anula una a la vez; el resultado final es el mismo.
        var primera = dao.AnularVencidas(corte, 1, ahora);
        Assert.Equal(1, primera);
        Assert.Equal(1, Vencidas());
        while (Vencidas() > 0) Assert.True(dao.AnularVencidas(corte, 500, ahora) > 0);

        foreach (var id in new[] { vieja1, vieja2 })
        {
            var f = d.Sesion(id);
            Assert.Null(f.Emociones);
            Assert.Equal("retencion", f.Motivo);
            Assert.Equal(ahora, f.AnuladasUtc);
        }
        Assert.Equal("nota vieja 1 [TEST-PR6]", d.Sesion(vieja1).Notas);
        Assert.Equal("nota vieja 2 [TEST-PR6]", d.Sesion(vieja2).Notas);
        Assert.Equal("ys1.k1.CCCC", d.Sesion(justo).Emociones);           // el borde es estricto: exactamente el corte, no
        Assert.Equal("ys1.k1.DDDD", d.Sesion(reciente).Emociones);
        var s = d.Sesion(sinEmociones);
        Assert.Null(s.Motivo);                                              // no se marca lo que nunca tuvo emociones
        Assert.Null(s.AnuladasUtc);
    }

    // El recifrado manual (RecifrarEmociones) modifica datos ya guardados, así que su acceso a la base se prueba aparte de
    // la lógica: solo con filas propias, nunca con una pasada global (que tocaría datos reales de una base de desarrollo).
    [FactConBaseDeConsentimiento]
    public void El_recifrado_lee_por_lotes_y_solo_reemplaza_una_fila_que_no_cambio()
    {
        using var d = new DatosEmociones(salida: _salida);
        var a = d.InsertarSesion(DatosEmociones.Fecha, "{\"Neutral\":50}", "nota a [TEST-PR6]");
        var b = d.InsertarSesion(DatosEmociones.Fecha, "{\"Neutral\":60}", "nota b [TEST-PR6]");
        using var repo = new SqlRecifradoRepositorio();

        var lote = repo.Lote(a - 1, 1000).Where(f => f.Id == a || f.Id == b).ToList();
        var primero = repo.Lote(a - 1, 1);

        Assert.Equal(new[] { a, b }, lote.Select(f => f.Id).ToArray());   // por IdSesion
        Assert.Equal("{\"Neutral\":50}", lote[0].Texto);
        Assert.Single(primero);                                            // el lote respeta el tamaño pedido
        Assert.True(repo.Reemplazar(a, "{\"Neutral\":50}", "ys1.k1.XXXX"));
        Assert.False(repo.Reemplazar(a, "{\"Neutral\":50}", "ys1.k1.YYYY"));   // ya no es ese texto: no se pisa
        Assert.Equal("ys1.k1.XXXX", d.Sesion(a).Emociones);
        Assert.Equal("{\"Neutral\":60}", d.Sesion(b).Emociones);         // la otra fila, intacta
        Assert.Equal("nota a [TEST-PR6]", d.Sesion(a).Notas);              // y las notas, nunca
    }

    // ───────────── La limpieza de los datos de prueba ─────────────

    // El servicio de recordatorios de una aplicación de prueba puede crearle recordatorios al turno y una notificación al
    // paciente. Antes, Dispose no los borraba: el DELETE del turno chocaba con FK_Recordatorio_Turno, el del paciente con
    // Notificacion, y quedaban filas sueltas con la marca [TEST-PR6] en la base.
    [FactConBaseDeConsentimiento]
    public void Dispose_se_lleva_los_recordatorios_y_las_notificaciones_que_le_creo_el_servicio_de_recordatorios()
    {
        var d = new DatosEmociones(salida: _salida);
        try
        {
            DatosEmociones.Ejecutar(@"INSERT INTO Recordatorio (IdTurno, Tipo, Canal, FechaProgramada)
                VALUES (@T, 'Turno24h', 'Chat', @F), (@T, 'Turno24h', 'Email', @F), (@T, 'Turno24h', 'App', @F)",
                ("@T", d.IdTurno), ("@F", DatosEmociones.Fecha));
            DatosEmociones.Ejecutar("INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje) VALUES (@P, 'Recordatorio de turno', @M)",
                ("@P", d.IdPaciente), ("@M", DatosEmociones.Marca));
        }
        finally { d.Dispose(); }

        Assert.Empty(d.FallosDeLimpieza);
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Recordatorio WHERE IdTurno = @T", ("@T", d.IdTurno)));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @P", ("@P", d.IdPaciente)));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdTurno = @T", ("@T", d.IdTurno)));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Paciente WHERE IdPaciente = @P", ("@P", d.IdPaciente)));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Profesional WHERE IdProfesional = @M", ("@M", d.IdProfesional)));
    }

    // Un borrado de la limpieza que falla no se relanza (no tiene que tapar el resultado de la prueba), pero tampoco se calla:
    // queda registrado, y Dispose lo escribe en la salida de la prueba.
    [FactConBaseDeConsentimiento]
    public void Un_borrado_de_la_limpieza_que_falla_queda_registrado_y_se_escribe_en_la_salida_de_la_prueba()
    {
        var salida = new SalidaEnMemoria();
        var d = new DatosEmociones(salida: salida);

        d.Intentar("DELETE FROM TablaQueNoExiste_TEST_PR6 WHERE Id = @I", ("@I", 1));

        var fallo = Assert.Single(d.FallosDeLimpieza);
        Assert.Contains("TablaQueNoExiste_TEST_PR6", fallo);
        Assert.Contains("SqlException 208", fallo);   // 208 = nombre de objeto no válido
        Assert.Empty(salida.Lineas);                  // todavía no: se escribe al limpiar

        d.Dispose();

        var escrito = Assert.Single(salida.Lineas);
        Assert.Contains("no se pudo borrar 1 cosa(s)", escrito);
        Assert.Contains("TablaQueNoExiste_TEST_PR6", escrito);
    }

    private sealed class SalidaEnMemoria : ITestOutputHelper
    {
        public readonly List<string> Lineas = new();
        public void WriteLine(string message) => Lineas.Add(message);
        public void WriteLine(string format, params object[] args) => Lineas.Add(string.Format(format, args));
    }
}
