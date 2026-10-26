using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

// El cierre de sesión (POST /sesiones-clinicas con claveCierre / confirmaRealizacion) contra SQL Server de verdad y la aplicación completa, con datos propios marcados
// "[TEST-PR6]" (los de DatosEmociones) que se borran al terminar. Si la base no tiene la migración 26, estas pruebas se OMITEN (no pasan en falso).
//
// Lo que se prueba, del lado del servidor: el sistema NUNCA da una sesión por realizada solo (hace falta confirmaRealizacion = true, desde Confirmado, del profesional del turno),
// repetir el cierre no duplica nada, un rechazo no deja nada a medias y el rastro de auditoría queda en la misma transacción.
public sealed class FactConBaseDeCierreAttribute : FactAttribute
{
    public FactConBaseDeCierreAttribute()
    {
        var motivo = BaseDeConsentimiento.NoDisponible() ?? BaseDeCierre.NoDisponible();
        if (motivo is not null) Skip = motivo;
    }
}

public static class BaseDeCierre
{
    private static readonly Lazy<string?> Motivo = new(Calcular);
    public static string? NoDisponible() => Motivo.Value;

    private static string? Calcular()
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand(@"
                SELECT CASE WHEN COL_LENGTH('dbo.SesionClinica', 'ClaveCierre') IS NOT NULL AND COL_LENGTH('dbo.SesionClinica', 'TurnoRealizadoUtc') IS NOT NULL
                             AND COL_LENGTH('dbo.SesionClinica', 'IdEvolucion') IS NOT NULL THEN 1 ELSE 0 END", con);
            return (int)cmd.ExecuteScalar()! == 1 ? null : "La base no tiene la migración migrations/add_cierre_sesion.sql (aplicala con sqlcmd para correr estas pruebas).";
        }
        catch (Exception e) { return $"No hay una base disponible ({e.GetType().Name}): falta ConnectionStrings__YoSoy."; }
    }
}

/// <summary>Un profesional con su paciente y un turno Confirmado de AYER (el de DatosEmociones es de 2031: un turno futuro no se puede dar por realizado), más lo que la limpieza extra necesita.</summary>
public sealed class DatosCierre : IDisposable
{
    public readonly DatosEmociones D;
    private readonly List<string> _estados = new();
    public static DateTime Ayer => DateTime.Today.AddDays(-1);

    public DatosCierre(ITestOutputHelper salida)
    {
        D = new DatosEmociones(salida: salida);
        DatosEmociones.Ejecutar("UPDATE Turno SET Fecha = @F WHERE IdTurno = @T", ("@F", Ayer), ("@T", D.IdTurno));
        Tomar("Realizado");          // el catálogo: una base nueva lo tiene vacío
    }

    public int IdTurno => D.IdTurno;
    public int IdPaciente => D.IdPaciente;
    public int IdProfesional => D.IdProfesional;

    public void Tomar(string estado) { EstadosCompartidos.Tomar(estado); _estados.Add(estado); }

    /// <summary>Un turno más del mismo paciente y profesional, en el estado y la fecha indicados.</summary>
    public int NuevoTurno(string estado, DateTime fecha, int? idPaciente = null, int? idProfesional = null, int hora = 11)
    {
        var id = EstadosCompartidos.Tomar(estado);
        _estados.Add(estado);
        return DatosEmociones.Insertar(@"INSERT INTO Turno (IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor)
                                          OUTPUT INSERTED.IdTurno VALUES (@P, @M, @E, @F, @H, 'Virtual', @Motivo, 'paciente')",
            ("@P", idPaciente ?? IdPaciente), ("@M", idProfesional ?? IdProfesional), ("@E", id), ("@F", fecha), ("@H", new TimeSpan(hora, 0, 0)), ("@Motivo", DatosEmociones.Marca + " cierre"));
    }

    public string EstadoDelTurno(int idTurno) =>
        (string)DatosEmociones.Escalar("SELECT e.Descripcion FROM Turno t JOIN EstadoTurno e ON e.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @T", ("@T", idTurno))!;

    public int Sesiones(int? idTurno = null) =>
        Convert.ToInt32(DatosEmociones.Escalar(idTurno is null
            ? "SELECT COUNT(*) FROM SesionClinica WHERE IdPaciente = @P"
            : "SELECT COUNT(*) FROM SesionClinica WHERE IdPaciente = @P AND IdTurno = @T", ("@P", IdPaciente), ("@T", idTurno)));

    public (DateTime? RealizadoUtc, Guid? Clave) MarcasDeLaSesion(int idSesion)
    {
        var f = DatosEmociones.Filas("SELECT TurnoRealizadoUtc, ClaveCierre FROM SesionClinica WHERE IdSesion = @I", ("@I", idSesion)).Single();
        return ((DateTime?)f[0], (Guid?)f[1]);
    }

    public int IdCaso(string estado, int? idProfesional = null) =>
        DatosEmociones.Insertar(@"INSERT INTO Caso (IdPaciente, Estado, IdProfesionalAsignado, Origen) OUTPUT INSERTED.IdCaso VALUES (@P, @E, @M, 'Admision')",
            ("@P", IdPaciente), ("@E", estado), ("@M", idProfesional ?? IdProfesional));

    public string EstadoDelCaso(int idCaso) => (string)DatosEmociones.Escalar("SELECT Estado FROM Caso WHERE IdCaso = @C", ("@C", idCaso))!;

    /// <summary>Las filas de auditoría de este profesional con esa acción (la auditoría es de solo agregar: se identifican por quién actuó).</summary>
    public List<(string Accion, string? Detalle)> Auditoria(string accion) => D.Auditoria(accion);

    public void Dispose()
    {
        D.Intentar("DELETE FROM Caso WHERE IdPaciente = @P", ("@P", IdPaciente));
        D.Dispose();
        foreach (var e in _estados) EstadosCompartidos.Soltar(e);
    }
}

[Trait("Categoria", "Integracion")]
[Trait("Migracion", "26")]
public class CierreSesionBaseRealTests
{
    private readonly ITestOutputHelper _salida;
    public CierreSesionBaseRealTests(ITestOutputHelper salida) { _salida = salida; }

    private static VideoApp App() => new(new Dictionary<string, string?> { ["Video:LimitePorMinuto"] = "1000" }, repositorioReal: true, auditoriaReal: true);

    private static HttpClient Profesional(VideoApp app, DatosEmociones d) => app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);

    private static object Cuerpo(DatosCierre c, Guid clave, bool? confirma = true, int? idTurno = null, string tipo = "Seguimiento", string notas = "nota [TEST-PR6]") => new
    {
        idPaciente = c.IdPaciente, idTurno = idTurno ?? c.IdTurno, notasEvolucion = notas, tipoCierre = tipo,
        claveCierre = clave.ToString(), confirmaRealizacion = confirma
    };

    private static async Task<(HttpResponseMessage R, JsonElement J)> Cerrar(HttpClient cliente, object cuerpo)
    {
        var r = await cliente.PostAsJsonAsync("/sesiones-clinicas", cuerpo);
        return (r, await VideoApp.Json(r));
    }

    // ───────────── El caso feliz ─────────────

    [FactConBaseDeCierre]
    public async Task Sí_se_realizó_guarda_la_sesión_pasa_el_turno_de_Confirmado_a_Realizado_y_deja_la_auditoría_con_ids_solamente()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var clave = Guid.NewGuid();

        var (r, j) = await Cerrar(Profesional(app, c.D), Cuerpo(c, clave));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.False(j.GetProperty("repetido").GetBoolean());
        Assert.Equal("Realizado", j.GetProperty("turno").GetProperty("estado").GetString());
        Assert.True(j.GetProperty("turno").GetProperty("cambio").GetBoolean());
        Assert.Equal("Confirmado", j.GetProperty("turno").GetProperty("estadoAnterior").GetString());
        Assert.Equal("seguimiento", j.GetProperty("siguientePaso").GetProperty("tipo").GetString());
        Assert.Equal("Realizado", c.EstadoDelTurno(c.IdTurno));
        var idSesion = j.GetProperty("idSesion").GetInt32();
        var (realizadoUtc, claveGuardada) = c.MarcasDeLaSesion(idSesion);
        Assert.Equal(clave, claveGuardada);
        Assert.NotNull(realizadoUtc);
        Assert.InRange((DateTime.UtcNow - realizadoUtc!.Value).TotalMinutes, -1, 5);

        var cierre = Assert.Single(c.Auditoria("Cierre de sesión clínica"));
        var turno = Assert.Single(c.Auditoria("Turno marcado como Realizado"));
        Assert.Contains($"SesionClinica #{idSesion}", cierre.Detalle);
        Assert.Contains($"Turno #{c.IdTurno}: Confirmado -> Realizado", turno.Detalle);
        Assert.DoesNotContain("nota", turno.Detalle! + cierre.Detalle, StringComparison.OrdinalIgnoreCase);   // nunca el contenido clínico
    }

    [FactConBaseDeCierre]
    public async Task Repetir_el_mismo_cierre_con_la_misma_clave_no_duplica_nada_ni_vuelve_a_auditar()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var clave = Guid.NewGuid();
        var cliente = Profesional(app, c.D);

        var (r1, j1) = await Cerrar(cliente, Cuerpo(c, clave));
        var (r2, j2) = await Cerrar(cliente, Cuerpo(c, clave));
        var (r3, _) = await Cerrar(cliente, Cuerpo(c, clave, notas: "otra nota que no debe pisar la primera"));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        Assert.True(j2.GetProperty("repetido").GetBoolean());
        Assert.Equal(j1.GetProperty("idSesion").GetInt32(), j2.GetProperty("idSesion").GetInt32());
        Assert.False(j2.GetProperty("turno").GetProperty("cambio").GetBoolean());
        Assert.Equal("Realizado", j2.GetProperty("turno").GetProperty("estado").GetString());
        Assert.Equal(1, c.Sesiones());
        Assert.Single(c.Auditoria("Cierre de sesión clínica"));
        Assert.Single(c.Auditoria("Turno marcado como Realizado"));
        Assert.Equal("nota [TEST-PR6]", d_Notas(c, j1.GetProperty("idSesion").GetInt32()));
    }

    private static string? d_Notas(DatosCierre c, int idSesion) => c.D.Sesion(idSesion).Notas;

    [FactConBaseDeCierre]
    public async Task Dos_pedidos_simultáneos_con_la_misma_clave_dejan_una_sola_sesión()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var clave = Guid.NewGuid();
        var cliente = Profesional(app, c.D);

        var resultados = await Task.WhenAll(Cerrar(cliente, Cuerpo(c, clave)), Cerrar(cliente, Cuerpo(c, clave)));

        Assert.All(resultados, x => Assert.True(x.R.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, x.R.StatusCode + " " + x.J));
        Assert.Equal(1, c.Sesiones());
        Assert.Equal("Realizado", c.EstadoDelTurno(c.IdTurno));
        Assert.Single(c.Auditoria("Turno marcado como Realizado"));
    }

    [FactConBaseDeCierre]
    public async Task Dos_cierres_con_claves_distintas_para_el_mismo_turno_dejan_una_sola_sesión_y_el_segundo_se_rechaza()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var cliente = Profesional(app, c.D);

        var resultados = await Task.WhenAll(Cerrar(cliente, Cuerpo(c, Guid.NewGuid())), Cerrar(cliente, Cuerpo(c, Guid.NewGuid())));

        Assert.Single(resultados, x => x.R.StatusCode == HttpStatusCode.Created);
        var rechazo = Assert.Single(resultados, x => x.R.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("turno-con-sesion", rechazo.J.GetProperty("codigo").GetString());
        Assert.Equal(1, c.Sesiones());
    }

    // ───────────── El sistema no decide solo ─────────────

    [FactConBaseDeCierre]
    public async Task No_se_realizó_guarda_la_nota_y_NO_toca_el_turno()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();

        var (r, j) = await Cerrar(Profesional(app, c.D), Cuerpo(c, Guid.NewGuid(), confirma: false));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.False(j.GetProperty("turno").GetProperty("cambio").GetBoolean());
        Assert.Equal("no-confirmado", j.GetProperty("turno").GetProperty("motivo").GetString());
        Assert.Equal("Confirmado", c.EstadoDelTurno(c.IdTurno));
        Assert.Null(c.MarcasDeLaSesion(j.GetProperty("idSesion").GetInt32()).RealizadoUtc);
        Assert.Empty(c.Auditoria("Turno marcado como Realizado"));
        Assert.Single(c.Auditoria("Cierre de sesión clínica"));
    }

    [FactConBaseDeCierre]
    public async Task Sin_confirmaRealizacion_en_el_pedido_el_turno_no_cambia_nunca()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        // Ni con clave y sin el campo, ni con el pedido de siempre (sin ningún campo del cierre).
        var (r1, _) = await Cerrar(Profesional(app, c.D), new { idPaciente = c.IdPaciente, idTurno = c.IdTurno, notasEvolucion = "n [TEST-PR6]", claveCierre = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal("Confirmado", c.EstadoDelTurno(c.IdTurno));

        var otro = c.NuevoTurno("Confirmado", DatosCierre.Ayer, hora: 12);
        var (r2, j2) = await Cerrar(Profesional(app, c.D), new { idPaciente = c.IdPaciente, idTurno = otro, notasEvolucion = "n [TEST-PR6]", tipoCierre = "Seguimiento" });
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.False(j2.TryGetProperty("repetido", out _));          // el camino de siempre: la misma forma de respuesta de antes
        Assert.Equal("Confirmado", c.EstadoDelTurno(otro));
    }

    [FactConBaseDeCierre]
    public async Task Una_sesión_sin_turno_se_guarda_y_también_es_idempotente()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var clave = Guid.NewGuid();
        var cuerpo = new { idPaciente = c.IdPaciente, notasEvolucion = "n [TEST-PR6]", tipoCierre = "derivacion", claveCierre = clave.ToString(), confirmaRealizacion = false };

        var (r1, j1) = await Cerrar(Profesional(app, c.D), cuerpo);
        var (r2, j2) = await Cerrar(Profesional(app, c.D), cuerpo);

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(JsonValueKind.Null, j1.GetProperty("turno").ValueKind);
        Assert.Equal("Derivación", j1.GetProperty("tipoCierre").GetString());       // sin tilde y en minúsculas también se acepta; se guarda siempre con tilde
        Assert.Equal("derivacion", j1.GetProperty("siguientePaso").GetProperty("tipo").GetString());
        Assert.Equal(j1.GetProperty("idSesion").GetInt32(), j2.GetProperty("idSesion").GetInt32());
        Assert.Equal(1, c.Sesiones());
    }

    // ───────────── Rechazos: no se guarda nada ─────────────

    [FactConBaseDeCierre]
    public async Task Un_turno_que_no_está_Confirmado_no_se_puede_dar_por_realizado_y_no_se_guarda_nada()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        foreach (var estado in new[] { "Pendiente", "Cancelado" })
        {
            var turno = c.NuevoTurno(estado, DatosCierre.Ayer, hora: estado == "Pendiente" ? 12 : 13);

            var (r, j) = await Cerrar(Profesional(app, c.D), Cuerpo(c, Guid.NewGuid(), idTurno: turno));

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("estado-no-permitido", j.GetProperty("codigo").GetString());
            Assert.Contains(estado, j.GetProperty("mensaje").GetString());         // dice cuál es el estado y qué hacer
            Assert.Contains("No se realizó", j.GetProperty("mensaje").GetString());
            Assert.Equal(estado, c.EstadoDelTurno(turno));
            Assert.Equal(0, c.Sesiones(turno));
        }
    }

    [FactConBaseDeCierre]
    public async Task Un_turno_ya_Realizado_por_otro_camino_se_acepta_sin_volver_a_cambiarlo_ni_auditar_la_transición()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var turno = c.NuevoTurno("Realizado", DatosCierre.Ayer, hora: 12);

        var (r, j) = await Cerrar(Profesional(app, c.D), Cuerpo(c, Guid.NewGuid(), idTurno: turno));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.False(j.GetProperty("turno").GetProperty("cambio").GetBoolean());
        Assert.Equal("ya-estaba", j.GetProperty("turno").GetProperty("motivo").GetString());
        Assert.Empty(c.Auditoria("Turno marcado como Realizado"));
        Assert.Null(c.MarcasDeLaSesion(j.GetProperty("idSesion").GetInt32()).RealizadoUtc);
    }

    [FactConBaseDeCierre]
    public async Task Un_turno_de_una_fecha_futura_no_se_puede_dar_por_realizado()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        DatosEmociones.Ejecutar("UPDATE Turno SET Fecha = @F WHERE IdTurno = @T", ("@F", DateTime.Today.AddDays(3)), ("@T", c.IdTurno));

        var (r, j) = await Cerrar(Profesional(app, c.D), Cuerpo(c, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("turno-futuro", j.GetProperty("codigo").GetString());
        Assert.Equal("Confirmado", c.EstadoDelTurno(c.IdTurno));
        Assert.Equal(0, c.Sesiones());
    }

    [FactConBaseDeCierre]
    public async Task Un_turno_de_hoy_si_se_puede_dar_por_realizado()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        DatosEmociones.Ejecutar("UPDATE Turno SET Fecha = @F WHERE IdTurno = @T", ("@F", DateTime.Today), ("@T", c.IdTurno));

        var (r, _) = await Cerrar(Profesional(app, c.D), Cuerpo(c, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("Realizado", c.EstadoDelTurno(c.IdTurno));
    }

    [FactConBaseDeCierre]
    public async Task El_turno_de_otro_profesional_o_de_otro_paciente_se_rechaza_con_rastro_y_sin_tocar_nada()
    {
        using var a = new DatosCierre(_salida);
        using var b = new DatosCierre(_salida);          // otro profesional con su propio paciente y su turno
        using var app = App();

        // B guarda una sesión de SU paciente pero con el turno de A.
        var (r1, j1) = await Cerrar(Profesional(app, b.D), new
        {
            idPaciente = b.IdPaciente, idTurno = a.IdTurno, claveCierre = Guid.NewGuid().ToString(), confirmaRealizacion = true, notasEvolucion = "n [TEST-PR6]"
        });
        // A guarda una sesión de su paciente con el turno de B.
        var (r2, j2) = await Cerrar(Profesional(app, a.D), new
        {
            idPaciente = a.IdPaciente, idTurno = b.IdTurno, claveCierre = Guid.NewGuid().ToString(), confirmaRealizacion = true, notasEvolucion = "n [TEST-PR6]"
        });

        Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);
        Assert.Equal("turno-no-coincide", j1.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        Assert.Equal("turno-no-coincide", j2.GetProperty("codigo").GetString());
        Assert.Equal("Confirmado", a.EstadoDelTurno(a.IdTurno));
        Assert.Equal("Confirmado", b.EstadoDelTurno(b.IdTurno));
        Assert.Equal(0, a.Sesiones() + b.Sesiones());
        var rastro = Assert.Single(b.Auditoria("Cierre de sesión rechazado"));
        Assert.Contains($"turno #{a.IdTurno}, motivo turno-no-coincide", rastro.Detalle);
    }

    [FactConBaseDeCierre]
    public async Task Un_turno_que_no_existe_da_404_y_los_pedidos_mal_armados_dan_400_con_su_código()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var cliente = Profesional(app, c.D);

        var (r0, j0) = await Cerrar(cliente, Cuerpo(c, Guid.NewGuid(), idTurno: 2_000_000_000));
        Assert.Equal(HttpStatusCode.NotFound, r0.StatusCode);
        Assert.Equal("turno-inexistente", j0.GetProperty("codigo").GetString());

        var (r1, j1) = await Cerrar(cliente, new { idPaciente = c.IdPaciente, claveCierre = Guid.NewGuid().ToString(), confirmaRealizacion = true });
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Equal("realizacion-sin-turno", j1.GetProperty("codigo").GetString());

        var (r2, j2) = await Cerrar(cliente, new { idPaciente = c.IdPaciente, idTurno = c.IdTurno, confirmaRealizacion = true });
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.Equal("clave-requerida", j2.GetProperty("codigo").GetString());

        var (r3, j3) = await Cerrar(cliente, new { idPaciente = c.IdPaciente, idTurno = c.IdTurno, claveCierre = "no-es-un-guid", confirmaRealizacion = true });
        Assert.Equal(HttpStatusCode.BadRequest, r3.StatusCode);
        Assert.Equal("clave-invalida", j3.GetProperty("codigo").GetString());

        var (r4, j4) = await Cerrar(cliente, Cuerpo(c, Guid.NewGuid(), tipo: "Cualquier cosa"));
        Assert.Equal(HttpStatusCode.BadRequest, r4.StatusCode);
        Assert.Equal("tipo-cierre-invalido", j4.GetProperty("codigo").GetString());

        Assert.Equal(0, c.Sesiones());
        Assert.Equal("Confirmado", c.EstadoDelTurno(c.IdTurno));
    }

    [FactConBaseDeCierre]
    public async Task Permisos_por_rol_el_paciente_el_configurador_y_un_profesional_sin_ese_paciente_no_pueden_cerrar()
    {
        using var c = new DatosCierre(_salida);
        using var ajeno = new DatosCierre(_salida);
        using var app = App();
        var cuerpo = Cuerpo(c, Guid.NewGuid());

        var paciente = app.Cliente(c.D.IdUsuarioPaciente, Roles.Paciente, idPaciente: c.IdPaciente);
        var configurador = app.Cliente(c.D.IdUsuarioConfigurador, Roles.Configurador);
        var sinPaciente = Profesional(app, ajeno.D);          // profesional que no atiende a este paciente

        Assert.Equal(HttpStatusCode.Forbidden, (await paciente.PostAsJsonAsync("/sesiones-clinicas", cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await configurador.PostAsJsonAsync("/sesiones-clinicas", cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPaciente.PostAsJsonAsync("/sesiones-clinicas", cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.SinSesion().PostAsJsonAsync("/sesiones-clinicas", cuerpo)).StatusCode);
        Assert.Equal("Confirmado", c.EstadoDelTurno(c.IdTurno));
        Assert.Equal(0, c.Sesiones());
    }

    // ───────────── Atomicidad ─────────────

    [FactConBaseDeCierre]
    public async Task Si_falla_el_pase_del_turno_no_queda_la_sesión_ni_la_auditoría_a_medias()
    {
        using var c = new DatosCierre(_salida);
        using var app = App();
        var trigger = "TR_prueba_cierre_" + Guid.NewGuid().ToString("N")[..8];
        // La falla se provoca DESPUÉS de insertar la sesión y su auditoría: el UPDATE del turno (solo de este turno) lanza un error.
        DatosEmociones.Ejecutar($"CREATE TRIGGER {trigger} ON Turno AFTER UPDATE AS IF EXISTS (SELECT 1 FROM inserted WHERE IdTurno = {c.IdTurno}) THROW 51000, 'falla provocada por la prueba', 1;");
        try
        {
            var (r, _) = await Cerrar(Profesional(app, c.D), Cuerpo(c, Guid.NewGuid()));

            Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
            Assert.Equal(0, c.Sesiones());
            Assert.Equal("Confirmado", c.EstadoDelTurno(c.IdTurno));
            Assert.Empty(c.Auditoria("Cierre de sesión clínica"));
            Assert.Empty(c.Auditoria("Turno marcado como Realizado"));
        }
        finally { DatosEmociones.Ejecutar($"DROP TRIGGER {trigger}"); }

        // Resuelta la causa, el mismo cierre se puede reintentar.
        var (r2, _) = await Cerrar(Profesional(app, c.D), Cuerpo(c, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.Equal("Realizado", c.EstadoDelTurno(c.IdTurno));
    }
}
