using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

// Con una base de datos (la misma que usan los demás tests de integración): lo que VideoAccesoEndpointsTests
// prueba con dobles en memoria, acá contra SQL Server de verdad: la lectura de turnos e informes (OrigenSalaDAO),
// la duración sacada de la agenda y la auditoría en LogAuditoria.
//
// Los datos son PROPIOS de cada prueba (DatosDePrueba): un paciente, dos profesionales con su usuario, un usuario
// Configurador y la agenda del primer profesional (lunes a viernes de 09:00 a 18:00). Antes se usaban el paciente 1,
// los profesionales 1 y 2 y el usuario 1 de la base de desarrollo, y los usuarios que les correspondían se buscaban
// ahí. Todo lo que se inserta es de 2031, lleva la marca "[TEST]" y se borra al terminar, incluso si el test falla.
[Trait("Categoria", "Integracion")]
public class VideoBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _datos = new();
    private readonly int IdPacienteReal, IdProfesionalReal, IdOtroProfesionalReal, UsuarioConfiguradorReal;
    private static readonly DateTime FechaDePrueba = new(2031, 3, 4);   // martes: el profesional atiende de lunes a viernes
    private static readonly TimeSpan HoraDePrueba = new(10, 0, 0);      // coincide con VideoApp.Inicio

    public VideoBaseRealTests()
    {
        IdPacienteReal = _datos.Paciente();
        IdProfesionalReal = _datos.Profesional();
        IdOtroProfesionalReal = _datos.Profesional();
        UsuarioConfiguradorReal = _datos.Usuario();
        _datos.UsuarioDePaciente(IdPacienteReal);
        _datos.UsuarioDeProfesional(IdProfesionalReal);
        _datos.UsuarioDeProfesional(IdOtroProfesionalReal);
        foreach (var dia in new[] { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes" })
            _datos.Agenda(IdProfesionalReal, dia, "09:00", "18:00", 30);
        // Los estados que insertan las pruebas: si el catálogo de la base no los tiene, se crean (y se borran).
        foreach (var estado in new[] { "Pendiente", "Confirmado", "Cancelado", "Completado" }) _datos.EstadoTurno(estado);
    }

    public void Dispose() => _datos.Dispose();

    // ---------- Acceso a la base ----------

    private static SqlConnection Abrir()
    {
        var con = Conexion.ObtenerConexion();
        con.Open();
        return con;
    }

    private static SqlCommand Comando(SqlConnection con, string sql, (string Nombre, object? Valor)[] parametros)
    {
        var cmd = new SqlCommand(sql, con);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
        return cmd;
    }

    private static void Ejecutar(string sql, params (string Nombre, object? Valor)[] parametros)
    {
        using var con = Abrir();
        using var cmd = Comando(con, sql, parametros);
        cmd.ExecuteNonQuery();
    }

    private static int Entero(string sql, params (string Nombre, object? Valor)[] parametros)
    {
        using var con = Abrir();
        using var cmd = Comando(con, sql, parametros);
        var r = cmd.ExecuteScalar();
        Assert.True(r is not null and not DBNull, "La base no tiene lo que este test necesita: " + sql);
        return Convert.ToInt32(r);
    }

    private static int UsuarioDelPaciente(int idPaciente) =>
        Entero("SELECT IdUsuario FROM UsuarioPaciente WHERE IdPaciente = @Id", ("@Id", idPaciente));

    private static int UsuarioDelProfesional(int idProfesional) =>
        Entero("SELECT IdUsuario FROM UsuarioProfesional WHERE IdProfesional = @Id", ("@Id", idProfesional));

    // Independiente del código bajo prueba: el intervalo de la franja de ese día de la semana que contiene la hora.
    private static int IntervaloDeAgenda(int idProfesional, string dia, TimeSpan hora) =>
        Entero("SELECT TOP 1 IntervaloMinutos FROM Agenda WHERE IdProfesional = @P AND Dia = @D AND HoraInicio <= @H AND @H < HoraFin",
            ("@P", idProfesional), ("@D", dia), ("@H", hora));

    private static string CodigoDePrueba(string prefijo) => prefijo + "-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private int InsertarTurno(string estado, DateTime fecha, TimeSpan hora, string? sala)
    {
        using var con = Abrir();
        using var cmd = Comando(con, @"
            INSERT INTO Turno (IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor, CodigoSala)
            OUTPUT INSERTED.IdTurno
            VALUES (@Pac, @Prof, (SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = @Estado), @Fecha, @Hora,
                    'Virtual', '[TEST] acceso a sala de video', 'paciente', @Sala)",
            new (string, object?)[] { ("@Pac", IdPacienteReal), ("@Prof", IdProfesionalReal), ("@Estado", estado), ("@Fecha", fecha), ("@Hora", hora), ("@Sala", sala) });
        return (int)cmd.ExecuteScalar()!;
    }

    private int InsertarAdmision(string estado, DateTime? fechaEntrevista, string? sala)
    {
        using var con = Abrir();
        using var cmd = Comando(con, @"
            INSERT INTO InformeAdmision (IdPaciente, Estado, FechaEntrevista, CodigoSala, MotivoConsulta)
            OUTPUT INSERTED.IdInforme
            VALUES (@Pac, @Estado, @Fecha, @Sala, '[TEST] acceso a sala de video')",
            new (string, object?)[] { ("@Pac", IdPacienteReal), ("@Estado", estado), ("@Fecha", fechaEntrevista), ("@Sala", sala) });
        return (int)cmd.ExecuteScalar()!;
    }

    // Los recordatorios del turno (Recordatorio.IdTurno) los puede haber creado el servicio de recordatorios de una aplicación
    // de prueba: van antes que el turno, o el DELETE choca con FK_Recordatorio_Turno y la prueba falla a veces.
    private static void BorrarTurno(int idTurno) => Ejecutar(@"
        DELETE FROM Recordatorio WHERE IdTurno = @Id;
        DELETE FROM Turno WHERE IdTurno = @Id", ("@Id", idTurno));

    private static void BorrarAdmision(int idInforme) => Ejecutar(@"
        DELETE FROM InformeAdmision WHERE IdInforme = @Id", ("@Id", idInforme));

    private static List<(int IdUsuario, string Accion, string Detalle)> Auditoria(string entidad, int idEntidad)
    {
        using var con = Abrir();
        using var cmd = Comando(con,
            "SELECT IdUsuario, Accion, Detalle FROM LogAuditoria WHERE Entidad = @E AND IdEntidad = @Id AND Accion LIKE 'Acceso a sala de video%' ORDER BY IdLog",
            new (string, object?)[] { ("@E", entidad), ("@Id", idEntidad) });
        using var r = cmd.ExecuteReader();
        var filas = new List<(int, string, string)>();
        while (r.Read()) filas.Add((r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return filas;
    }

    // ---------- OrigenSalaDAO ----------

    [Fact]
    public void El_DAO_lee_un_turno_con_su_estado_horario_sala_y_la_duracion_de_su_franja()
    {
        var sala = CodigoDePrueba("SALA");
        var id = InsertarTurno("Confirmado", FechaDePrueba, HoraDePrueba, sala);
        try
        {
            var t = new OrigenSalaDAO().ObtenerTurno(id);

            Assert.NotNull(t);
            Assert.Equal(id, t.IdTurno);
            Assert.Equal(IdPacienteReal, t.IdPaciente);
            Assert.Equal(IdProfesionalReal, t.IdProfesional);
            Assert.Equal("Confirmado", t.Estado);            // la descripción del catálogo, no el id
            Assert.Equal(FechaDePrueba, t.Fecha);
            Assert.Equal(HoraDePrueba, t.Hora);
            Assert.Equal(sala, t.CodigoSala);
            Assert.Equal(IntervaloDeAgenda(IdProfesionalReal, "martes", HoraDePrueba), t.DuracionMinutos);
        }
        finally { BorrarTurno(id); }
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Cancelado")]
    [InlineData("Completado")]
    public void El_DAO_devuelve_la_descripcion_de_cada_estado(string estado)
    {
        var id = InsertarTurno(estado, FechaDePrueba, HoraDePrueba, null);
        try
        {
            var t = new OrigenSalaDAO().ObtenerTurno(id);
            Assert.Equal(estado, t!.Estado);
            Assert.Null(t.CodigoSala);   // sin sala generada: null
        }
        finally { BorrarTurno(id); }
    }

    [Fact]
    public void La_duracion_sale_de_la_franja_que_contiene_la_hora_y_es_nula_fuera_de_la_agenda()
    {
        var ids = new List<int>();
        int Turno(DateTime fecha, TimeSpan hora) { var i = InsertarTurno("Confirmado", fecha, hora, "SALA-T"); ids.Add(i); return i; }
        try
        {
            var dao = new OrigenSalaDAO();

            // Dentro de la agenda (lunes a viernes de 09:00 a 18:00): el inicio de la franja cuenta, el fin no.
            Assert.NotNull(dao.ObtenerTurno(Turno(FechaDePrueba, new TimeSpan(9, 0, 0)))!.DuracionMinutos);
            Assert.NotNull(dao.ObtenerTurno(Turno(FechaDePrueba.AddDays(1), new TimeSpan(17, 30, 0)))!.DuracionMinutos);

            // Fuera: justo al terminar la franja, antes de empezar, de noche y en fin de semana.
            Assert.Null(dao.ObtenerTurno(Turno(FechaDePrueba.AddDays(2), new TimeSpan(18, 0, 0)))!.DuracionMinutos);
            Assert.Null(dao.ObtenerTurno(Turno(FechaDePrueba.AddDays(2), new TimeSpan(8, 59, 0)))!.DuracionMinutos);
            Assert.Null(dao.ObtenerTurno(Turno(FechaDePrueba.AddDays(2), new TimeSpan(20, 0, 0)))!.DuracionMinutos);
            Assert.Null(dao.ObtenerTurno(Turno(new DateTime(2031, 3, 8), HoraDePrueba))!.DuracionMinutos);   // sábado
            Assert.Null(dao.ObtenerTurno(Turno(new DateTime(2031, 3, 9), HoraDePrueba))!.DuracionMinutos);   // domingo
        }
        finally { foreach (var id in ids) BorrarTurno(id); }
    }

    [Fact]
    public void El_DAO_devuelve_null_si_el_turno_o_la_admision_no_existen()
    {
        Assert.Null(new OrigenSalaDAO().ObtenerTurno(int.MaxValue));
        Assert.Null(new OrigenSalaDAO().ObtenerAdmision(int.MaxValue));
    }

    [Fact]
    public void El_DAO_lee_una_admision_con_su_estado_fecha_y_sala()
    {
        var sala = CodigoDePrueba("ADM");
        var id = InsertarAdmision("En entrevista", FechaDePrueba + HoraDePrueba, sala);
        try
        {
            var a = new OrigenSalaDAO().ObtenerAdmision(id);

            Assert.NotNull(a);
            Assert.Equal(id, a.IdInforme);
            Assert.Equal(IdPacienteReal, a.IdPaciente);
            Assert.Equal("En entrevista", a.Estado);
            Assert.Equal(FechaDePrueba + HoraDePrueba, a.FechaEntrevista);
            Assert.Equal(sala, a.CodigoSala);
        }
        finally { BorrarAdmision(id); }
    }

    [Fact]
    public void El_DAO_lee_una_admision_pendiente_sin_fecha_ni_sala()
    {
        var id = InsertarAdmision("Pendiente", null, null);
        try
        {
            var a = new OrigenSalaDAO().ObtenerAdmision(id);
            Assert.Equal("Pendiente", a!.Estado);
            Assert.Null(a.FechaEntrevista);
            Assert.Null(a.CodigoSala);
        }
        finally { BorrarAdmision(id); }
    }

    // ---------- De punta a punta ----------

    // La aplicación real con la base real: el repositorio y la auditoría NO son dobles; solo el reloj (fijo, el
    // martes 4/3/2031 a las 10:00 UTC, como las filas de prueba) y los secretos de video (que van por configuración).
    [Fact]
    public async Task De_punta_a_punta_con_la_base_real_el_acceso_se_concede_se_rechaza_y_se_audita_sin_datos_sensibles()
    {
        var sala = CodigoDePrueba("SALA");
        var salaAdmision = CodigoDePrueba("ADM");
        var idTurno = InsertarTurno("Confirmado", FechaDePrueba, HoraDePrueba, sala);
        var idInforme = InsertarAdmision("En entrevista", FechaDePrueba + HoraDePrueba, salaAdmision);
        var usuarioPaciente = UsuarioDelPaciente(IdPacienteReal);
        var usuarioProfesional = UsuarioDelProfesional(IdProfesionalReal);
        var usuarioAjeno = UsuarioDelProfesional(IdOtroProfesionalReal);
        var minutosDeLaFranja = IntervaloDeAgenda(IdProfesionalReal, "martes", HoraDePrueba);

        try
        {
            using var app = new VideoApp(repositorioReal: true, auditoriaReal: true);
            var paciente = app.Cliente(usuarioPaciente, Roles.Paciente, idPaciente: IdPacienteReal);
            var profesional = app.Cliente(usuarioProfesional, Roles.Profesional, idProfesional: IdProfesionalReal);
            var ajeno = app.Cliente(usuarioAjeno, Roles.Profesional, idProfesional: IdOtroProfesionalReal);
            var configurador = app.Cliente(UsuarioConfiguradorReal, Roles.Configurador);

            // El paciente y el profesional del turno entran, cada uno con su rol, a la sala que está en la base.
            var rPaciente = await VideoApp.Acceder(paciente, "turno", idTurno);
            var jPaciente = await VideoApp.Json(rPaciente);
            Assert.Equal(HttpStatusCode.OK, rPaciente.StatusCode);
            var claimsPaciente = TokenDeSala.Verificar(jPaciente.GetProperty("token").GetString()!, VideoApp.SecretoSenal);
            Assert.Equal("paciente", claimsPaciente.GetProperty("rol").GetString());
            Assert.Equal(sala, claimsPaciente.GetProperty("sala").GetString());
            Assert.Equal(usuarioPaciente.ToString(), claimsPaciente.GetProperty("sub").GetString());

            var rProfesional = await VideoApp.Acceder(profesional, "turno", idTurno);
            var jProfesional = await VideoApp.Json(rProfesional);
            Assert.Equal(HttpStatusCode.OK, rProfesional.StatusCode);
            Assert.Equal("profesional", TokenDeSala.Verificar(jProfesional.GetProperty("token").GetString()!, VideoApp.SecretoSenal).GetProperty("rol").GetString());

            // Otro profesional y un turno que no existe: la misma respuesta, un 404 con el mismo cuerpo.
            var rAjeno = await VideoApp.Acceder(ajeno, "turno", idTurno);
            var rNoExiste = await VideoApp.Acceder(paciente, "turno", int.MaxValue);
            Assert.Equal(HttpStatusCode.NotFound, rAjeno.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, rNoExiste.StatusCode);
            Assert.Equal(await rNoExiste.Content.ReadAsStringAsync(), await rAjeno.Content.ReadAsStringAsync());

            // La ventana usa la duración que la agenda real le da a ese turno: abre 10 min antes, cierra 30 min después del fin.
            var cierre = VideoApp.Inicio.AddMinutes(minutosDeLaFranja + 30);
            app.Reloj.Ahora = cierre.AddSeconds(-1);
            Assert.Equal(HttpStatusCode.OK, (await VideoApp.Acceder(paciente, "turno", idTurno)).StatusCode);
            app.Reloj.Ahora = cierre;
            var rCerrado = await VideoApp.Acceder(paciente, "turno", idTurno);
            Assert.Equal(HttpStatusCode.Conflict, rCerrado.StatusCode);
            Assert.Equal("ya-termino", (await VideoApp.Json(rCerrado)).GetProperty("codigo").GetString());

            // Un turno cancelado deja de dar acceso aunque esté en horario.
            app.Reloj.Ahora = VideoApp.Inicio;
            Ejecutar("UPDATE Turno SET IdEstadoTurno = (SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = 'Cancelado') WHERE IdTurno = @Id", ("@Id", idTurno));
            var rCancelado = await VideoApp.Acceder(paciente, "turno", idTurno);
            Assert.Equal(HttpStatusCode.Conflict, rCancelado.StatusCode);
            Assert.Equal("estado-invalido", (await VideoApp.Json(rCancelado)).GetProperty("codigo").GetString());

            // Admisión: el Configurador entra a la entrevista; un profesional no.
            var rAdmision = await VideoApp.Acceder(configurador, "admision", idInforme);
            var jAdmision = await VideoApp.Json(rAdmision);
            Assert.Equal(HttpStatusCode.OK, rAdmision.StatusCode);
            var claimsAdmision = TokenDeSala.Verificar(jAdmision.GetProperty("token").GetString()!, VideoApp.SecretoSenal);
            Assert.Equal(salaAdmision, claimsAdmision.GetProperty("sala").GetString());
            Assert.Equal("profesional", claimsAdmision.GetProperty("rol").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await VideoApp.Acceder(profesional, "admision", idInforme)).StatusCode);

            // Auditoría real, en LogAuditoria: quién, con qué rol, a qué, con qué resultado.
            var deTurno = Auditoria("Turno", idTurno);
            Assert.Equal(new[]
            {
                (usuarioPaciente, "Acceso a sala de video", "rol: paciente; resultado: concedido"),
                (usuarioProfesional, "Acceso a sala de video", "rol: profesional; resultado: concedido"),
                (usuarioAjeno, "Acceso a sala de video rechazado", "rol: profesional; resultado: rechazado; motivo: sin-acceso"),   // respondió el 404 uniforme; la auditoría guarda el motivo real
                (usuarioPaciente, "Acceso a sala de video", "rol: paciente; resultado: concedido"),          // 1 s antes del cierre
                (usuarioPaciente, "Acceso a sala de video rechazado", "rol: paciente; resultado: rechazado; motivo: ya-termino"),
                (usuarioPaciente, "Acceso a sala de video rechazado", "rol: paciente; resultado: rechazado; motivo: estado-invalido"),
            }, deTurno.ToArray());

            var deAdmision = Auditoria("InformeAdmision", idInforme);
            Assert.Equal(new[]
            {
                (UsuarioConfiguradorReal, "Acceso a sala de video", "rol: profesional; resultado: concedido"),
                (usuarioProfesional, "Acceso a sala de video rechazado", "rol: profesional; resultado: rechazado; motivo: sin-acceso"),
            }, deAdmision.ToArray());

            // Nada sensible en la auditoría: ni la sala, ni el token, ni las credenciales, ni los secretos.
            var tokens = new[] { jPaciente.GetProperty("token").GetString()!, jProfesional.GetProperty("token").GetString()!, jAdmision.GetProperty("token").GetString()! };
            foreach (var (_, accion, detalle) in deTurno.Concat(deAdmision))
            {
                var fila = accion + "|" + detalle;
                Assert.DoesNotContain(sala, fila);
                Assert.DoesNotContain(salaAdmision, fila);
                Assert.DoesNotContain("eyJ", fila);
                Assert.DoesNotContain(VideoApp.SecretoSenal, fila);
                Assert.DoesNotContain(VideoApp.SecretoTurn, fila);
                foreach (var token in tokens) Assert.DoesNotContain(token, fila);
            }
        }
        finally
        {
            BorrarTurno(idTurno);
            BorrarAdmision(idInforme);
        }
    }

    // La auditoría no puede tumbar el pedido: si la base la rechaza, el acceso sigue y solo queda un aviso, que
    // dice de qué tipo fue el error y nada más. Acá se provoca con un rol de 600 caracteres, que no entra en
    // LogAuditoria.Detalle (500).
    [Fact]
    public void Si_la_base_rechaza_la_auditoria_no_se_rompe_el_pedido_y_el_aviso_no_trae_el_detalle()
    {
        const int idQueNoExiste = 987654321;
        var logs = new LogsCapturados();
        var auditoria = new AuditoriaVideo(logs.CreateLogger<AuditoriaVideo>());
        var concedido = ResultadoAcceso.Concedido("paciente", "SALA-X", VideoApp.Utc(VideoApp.Inicio), VideoApp.Utc(VideoApp.Inicio), VideoApp.Utc(VideoApp.Inicio.AddHours(1)));

        {
            var error = Record.Exception(() => auditoria.Registrar(UsuarioConfiguradorReal, "turno", idQueNoExiste, new string('r', 600), concedido));

            Assert.Null(error);
            var aviso = Assert.Single(logs.Lineas);
            Assert.StartsWith("Warning|", aviso);
            Assert.Contains("SqlException", aviso);
            Assert.DoesNotContain("rrrrr", aviso);   // ni el valor que no entró
            Assert.Empty(Auditoria("Turno", idQueNoExiste));
        }
    }

    // El servicio de recordatorios de una aplicación de prueba puede crearle recordatorios a cualquier turno confirmado de la
    // base. Antes, borrar el turno sin borrarlos fallaba con FK_Recordatorio_Turno, a veces (según cuándo corriera esa pasada).
    [Fact]
    public void BorrarTurno_se_lleva_tambien_los_recordatorios_que_le_creo_el_servicio_de_recordatorios()
    {
        var id = InsertarTurno("Confirmado", FechaDePrueba, HoraDePrueba, null);
        try
        {
            Ejecutar(@"INSERT INTO Recordatorio (IdTurno, Tipo, Canal, FechaProgramada)
                       VALUES (@Id, 'Turno24h', 'Chat', @F), (@Id, 'Turno24h', 'Email', @F), (@Id, 'Turno24h', 'App', @F)",
                ("@Id", id), ("@F", FechaDePrueba));

            var error = Record.Exception(() => BorrarTurno(id));

            Assert.Null(error);   // antes: SqlException 547 por FK_Recordatorio_Turno
            Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Turno WHERE IdTurno = @Id", ("@Id", id)));
            Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Recordatorio WHERE IdTurno = @Id", ("@Id", id)));
        }
        finally
        {
            Ejecutar("DELETE FROM Recordatorio WHERE IdTurno = @Id; DELETE FROM Turno WHERE IdTurno = @Id", ("@Id", id));
        }
    }
}
