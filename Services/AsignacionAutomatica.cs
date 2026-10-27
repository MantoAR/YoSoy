using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

// Propuesta automática de profesional y horario (migración 32). Al ACEPTAR un formulario «Sobre mí» (acción de la recepción), el sistema calcula una propuesta
// —un profesional y su primer horario libre— y el PACIENTE decide:
//
//   recepción acepta el formulario → propuesta Pendiente (48 hs) → el paciente:
//        · «Me sirve»                → se crean el vínculo con el profesional y el turno (confirmado), una sola vez;
//        · «Prefiero otro horario»   → elige otro libre del MISMO profesional (hasta 10) → nueva propuesta;
//        · «Probar con otro profesional» → la siguiente propuesta, sin repetir los que ya rechazó;
//        · al tercer rechazo, o si no hay candidatos, el caso vuelve a la recepción en «Asignaciones para resolver», con el motivo.
//
// El principio no cambia: el sistema detecta, propone y precarga; una persona confirma. El sistema NUNCA asigna un vínculo ni crea un turno por su cuenta: eso solo pasa cuando
// el paciente confirma («Me sirve») o cuando la recepción asigna a mano (PUT /informes-admision/{id}/derivar, que cancela la propuesta pendiente). Cada creación, aceptación,
// rechazo, vencimiento y cancelación queda en LogAuditoria; todo cambio de estado es una transacción y repetirlo no hace nada de más.
//
// Esta clase reúne: las reglas de elección (puras, se prueban sin base) y el servicio que las usa con la base. Los endpoints están en Endpoints/AsignacionAutomaticaEndpoints.cs.
// Sin la migración 32, todo esto se apaga solo (Disponible() es false) y aceptar un formulario anda como antes: la recepción asigna a mano.

public static class PropuestaEstado
{
    public const string Pendiente = "Pendiente";
    public const string Aceptada = "Aceptada";
    public const string Rechazada = "Rechazada";
    public const string Vencida = "Vencida";
    public const string Cancelada = "Cancelada";
}

/// <summary>Dónde quedó la asignación del formulario (InformeAdmision.AsignacionEstado). NULL = nunca pasó por la propuesta automática.</summary>
public static class AsignacionInforme
{
    public const string Propuesta = "Propuesta";
    public const string PorResolver = "Por resolver";
}

public static class AsignacionAutomaticaReglas
{
    // TODO-VALIDAR: las tres cifras siguientes son una decisión clínica/operativa que no está definida; quedan neutras y en un solo lugar.
    /// <summary>Anticipación mínima del primer horario propuesto, en horas desde ahora.</summary>
    public const int HorasAnticipacionMinima = 24;
    /// <summary>Hasta cuántos días hacia adelante se busca un horario.</summary>
    public const int DiasHorizonte = 30;
    /// <summary>Cuánto tiempo tiene el paciente para responder una propuesta antes de que venza.</summary>
    public const int HorasParaResponder = 48;

    public const int MaxPropuestas = 3;
    public const int MaxHorariosAlternativos = 10;

    public const string MotivoSinHorarios = "Sin horarios libres para esa especialidad";
    public const string MensajeBuscando = "Estamos buscando la mejor opción para vos, te avisamos apenas esté lista.";

    private static readonly string[] Dias = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };

    /// <summary>«el lunes 12/11 a las 10:30 hs»: claro para el paciente, sin depender de la cultura del servidor.</summary>
    public static string Cuando(DateTime momento) => $"el {Dias[(int)momento.DayOfWeek]} {momento:dd/MM} a las {momento:HH:mm} hs";

    /// <summary>
    /// La regla de elección, sin base de datos. Orden de intentos:
    ///  1. Si el paciente marcó profesionales preferidos, solo con ellos; si ninguno tiene horario, se amplía a todos los de la especialidad y se anota
    ///     «los profesionales elegidos no tenían disponibilidad».
    ///  2. Si marcó días y/o franja, se filtran los horarios; si no queda ninguno se relaja primero la franja y después los días, y se anota qué se relajó.
    ///     «Me da igual» y los campos vacíos no filtran.
    ///  3. Se elige el horario libre más próximo; si hay empate, el profesional con menos pacientes activos y después el de menor Id.
    /// null = ningún candidato tiene horarios.
    /// </summary>
    public static ElegidoPropuesta? Elegir(IReadOnlyList<CandidatoPropuesta> candidatos, PreferenciasDelPaciente prefs)
    {
        var conPreferidos = !prefs.CualquierProfesional && prefs.IdsProfesionales.Count > 0;
        var grupos = new List<(IReadOnlyList<CandidatoPropuesta> Grupo, bool Ampliado)>();
        if (conPreferidos) grupos.Add((candidatos.Where(c => prefs.IdsProfesionales.Contains(c.IdProfesional)).ToList(), false));
        grupos.Add((candidatos, conPreferidos));

        var filtraDias = prefs.Dias.Count > 0;
        var filtraFranja = !string.IsNullOrEmpty(prefs.Franja) && prefs.Franja != "cualquiera";
        // Los niveles: todo lo que pidió, sin la franja, sin nada. Un nivel que no cambia nada (no pidió esa preferencia) no se repite.
        var niveles = new List<(bool Dias, bool Franja, string? Nota)> { (filtraDias, filtraFranja, null) };
        if (filtraFranja) niveles.Add((filtraDias, false, "Se relajó la franja horaria preferida."));
        if (filtraDias) niveles.Add((false, false, filtraFranja ? "Se relajaron los días y la franja horaria preferidos." : "Se relajaron los días preferidos."));

        foreach (var (grupo, ampliado) in grupos)
            foreach (var (usaDias, usaFranja, nota) in niveles)
            {
                var mejor = Mejor(grupo, usaDias ? prefs.Dias : null, usaFranja ? prefs.Franja : null);
                if (mejor is null) continue;
                var partes = new List<string> { "Primer horario libre" + (conPreferidos && !ampliado ? " entre los profesionales que eligió el paciente." : ".") };
                if (ampliado) partes.Add("Los profesionales elegidos no tenían disponibilidad.");
                if (nota is not null) partes.Add(nota);
                return new ElegidoPropuesta(mejor.Value.Candidato.IdProfesional, mejor.Value.Candidato.Nombre, mejor.Value.Candidato.Especialidad, mejor.Value.Momento, string.Join(" ", partes));
            }
        return null;
    }

    private static (CandidatoPropuesta Candidato, DateTime Momento)? Mejor(IReadOnlyList<CandidatoPropuesta> grupo, IReadOnlyCollection<string>? dias, string? franja)
    {
        var diasPedidos = dias?.Select(d => TurnoReglasService.DiaDeLaSemana(d)).Where(d => d is not null).Select(d => d!.Value).ToHashSet();
        (int Desde, int Hasta)? horas = franja is null ? null : PreferenciasAdmision.Horas(franja);
        (CandidatoPropuesta Candidato, DateTime Momento)? mejor = null;
        foreach (var c in grupo)
            foreach (var h in c.Horarios)
            {
                if (diasPedidos is { Count: > 0 } && !diasPedidos.Contains(h.DayOfWeek)) continue;
                if (horas is { } r && (h.TimeOfDay < TimeSpan.FromHours(r.Desde) || h.TimeOfDay >= TimeSpan.FromHours(r.Hasta))) continue;
                if (mejor is null || Antes(c, h, mejor.Value.Candidato, mejor.Value.Momento)) mejor = (c, h);
            }
        return mejor;
    }

    // Más próximo; si empatan, menos pacientes activos; si empatan, menor Id.
    private static bool Antes(CandidatoPropuesta a, DateTime ha, CandidatoPropuesta b, DateTime hb) =>
        ha != hb ? ha < hb : a.PacientesActivos != b.PacientesActivos ? a.PacientesActivos < b.PacientesActivos : a.IdProfesional < b.IdProfesional;
}

public record CandidatoPropuesta(int IdProfesional, string Nombre, string? Especialidad, int PacientesActivos, IReadOnlyList<DateTime> Horarios);
public record ElegidoPropuesta(int IdProfesional, string Nombre, string? Especialidad, DateTime Momento, string Motivo);

/// <summary>Lo que el paciente marcó en «Sobre mí» (InformeAdmision.HorarioPreferido / ProfesionalesPreferidos), listo para la regla. Vacío = sin filtro.</summary>
public record PreferenciasDelPaciente(IReadOnlyList<string> Dias, string? Franja, bool CualquierProfesional, IReadOnlyList<int> IdsProfesionales)
{
    public static readonly PreferenciasDelPaciente Ninguna = new(Array.Empty<string>(), null, true, Array.Empty<int>());

    public static PreferenciasDelPaciente Desde(string? horarioJson, string? profesionalesJson)
    {
        var dias = new List<string>();
        string? franja = null;
        var cualquiera = true;
        var ids = new List<int>();
        try
        {
            if (!string.IsNullOrWhiteSpace(horarioJson))
            {
                using var h = JsonDocument.Parse(horarioJson);
                if (h.RootElement.TryGetProperty("dias", out var d) && d.ValueKind == JsonValueKind.Array)
                    dias.AddRange(d.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
                if (h.RootElement.TryGetProperty("franja", out var f) && f.ValueKind == JsonValueKind.String) franja = f.GetString();
            }
            if (!string.IsNullOrWhiteSpace(profesionalesJson))
            {
                using var p = JsonDocument.Parse(profesionalesJson);
                cualquiera = p.RootElement.TryGetProperty("cualquiera", out var c) && c.ValueKind == JsonValueKind.True;
                if (p.RootElement.TryGetProperty("ids", out var i) && i.ValueKind == JsonValueKind.Array)
                    ids.AddRange(i.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()));
            }
        }
        catch (JsonException) { /* un texto que no se puede leer cuenta como «sin preferencias» */ }
        return new(dias, franja, cualquiera || ids.Count == 0, ids);
    }
}

/// <summary>La propuesta tal como la ve el paciente en sus primeros pasos.</summary>
public record PropuestaVista(int IdPropuesta, int IdProfesional, string Profesional, string? Especialidad, DateTime FechaHora, DateTime ExpiraEn, int NroPropuesta, bool PuedeCambiar);

public record ResultadoAlAceptar(int? IdPropuesta, string? MotivoPorResolver, bool Fallo = false)
{
    public string Resumen => IdPropuesta is not null ? "propuesta" : MotivoPorResolver is not null ? "por-resolver" : Fallo ? "error" : "sin-cambios";
}

public record ResultadoAsignacionAuto(int Status, string Codigo, string Mensaje, PropuestaVista? Propuesta = null, int? IdTurno = null)
{
    public bool Ok => Status is >= 200 and < 300;
}

public class AsignacionAutomaticaService
{
    private const string Espacio = "asignacion-automatica";

    private readonly ITurnoRepositorio _repo;
    private readonly TurnoReglasService _reglas;
    private readonly IOptionsMonitor<ConsentimientoOptions> _consentimiento;
    private readonly INotificadorTurnos _notificador;
    private readonly ILogger<AsignacionAutomaticaService> _log;

    public AsignacionAutomaticaService(ITurnoRepositorio repo, TurnoReglasService reglas, IOptionsMonitor<ConsentimientoOptions> consentimiento,
        INotificadorTurnos notificador, ILogger<AsignacionAutomaticaService> log)
    {
        _repo = repo;
        _reglas = reglas;
        _consentimiento = consentimiento;
        _notificador = notificador;
        _log = log;
    }

    // ───────────── ¿Está la migración 32? ─────────────

    private static bool _existe;
    private static DateTime _volverAMirar = DateTime.MinValue;

    /// <summary>¿La base tiene la tabla de propuestas y las columnas nuevas del formulario? Una vez que las tiene no se vuelve a preguntar; si no, se mira de nuevo cada 30 segundos.</summary>
    public static bool Disponible()
    {
        if (_existe) return true;
        if (DateTime.UtcNow < _volverAMirar) return false;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.PropuestaAsignacion', 'U') IS NOT NULL AND COL_LENGTH('dbo.InformeAdmision', 'AsignacionEstado') IS NOT NULL THEN 1 ELSE 0 END", con);
        _existe = (int)cmd.ExecuteScalar()! == 1;
        if (!_existe) _volverAMirar = DateTime.UtcNow.AddSeconds(30);
        return _existe;
    }

    // ───────────── Al aceptar el formulario (el punto de enganche de RevisionAdmision.Aceptar) ─────────────

    /// <summary>
    /// Dentro de la transacción de la aceptación: calcula la primera propuesta. Si no se puede (el consentimiento no está vigente, no hay horarios, falla algo), el formulario queda aceptado igual y
    /// pasa a «Asignaciones para resolver» con el motivo. Nunca tumba la aceptación. Idempotente: si el formulario ya tiene una propuesta pendiente, no crea otra.
    /// </summary>
    public ResultadoAlAceptar ProponerAlAceptar(SqlConnection con, SqlTransaction tx, int idInforme, int idPaciente, int idUsuario, DateTime ahora)
    {
        if (!Disponible()) return new(null, null);
        const string punto = "propuesta_automatica";
        try
        {
            tx.Save(punto);
            var informe = LeerInforme(con, tx, idInforme);
            if (informe is null) return new(null, null);
            if (PendienteDe(con, tx, idInforme) is int existente) return new(existente, null);

            var consentimiento = ConsentimientoTratamiento.Estado(idPaciente, _consentimiento.CurrentValue, ahora);
            if (!consentimiento.Vigente)
                return PorResolver(con, tx, informe, idUsuario, ahora, MotivoPorConsentimiento(consentimiento.Motivo));
            return ProponerSiguiente(con, tx, informe, 1, idUsuario, ahora, new HashSet<int>());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo calcular la propuesta de asignación del formulario {Informe}.", idInforme);
            try { tx.Rollback(punto); } catch (Exception) { /* la transacción ya no admite el punto: la aceptación sigue sin propuesta */ }
            return new(null, null, Fallo: true);
        }
    }

    private static string MotivoPorConsentimiento(string motivo) => motivo switch
    {
        ConsentimientoTratamiento.Vencido => "El consentimiento del paciente venció: tiene que firmarlo de nuevo antes de proponerle un profesional.",
        ConsentimientoTratamiento.VersionAnterior => "El texto del consentimiento cambió: el paciente tiene que firmarlo de nuevo antes de proponerle un profesional.",
        ConsentimientoTratamiento.FaltaTutor => "Falta el consentimiento firmado por el tutor: hace falta antes de proponerle un profesional.",
        _ => "El consentimiento del paciente no está vigente (no lo firmó o lo revocó): tiene que firmarlo antes de proponerle un profesional."
    };

    // ───────────── Elegir y guardar una propuesta ─────────────

    /// <summary>
    /// Calcula y guarda la propuesta número <paramref name="nro"/>. Si no hay candidatos, deja el formulario en «Asignaciones para resolver».
    /// <paramref name="candidatos"/>: los candidatos ya leídos. Quien llama con una transacción que YA escribió algo (y en especial en la auditoría, que se escribe con un bloqueo hasta el final)
    /// tiene que leerlos ANTES de escribir: una lectura por otra conexión que espera a esa transacción, mientras esta espera la lectura, no la detecta ni la base y termina en un tiempo agotado.
    /// </summary>
    private ResultadoAlAceptar ProponerSiguiente(SqlConnection con, SqlTransaction tx, InformeCtx informe, int nro, int? idUsuarioOrigen, DateTime ahora,
        ISet<int> profesionalesExcluidos, string? agregarAlMotivo = null, List<CandidatoPropuesta>? candidatos = null)
    {
        var prefs = PreferenciasDelPaciente.Desde(informe.Horario, informe.Profesionales);
        var lista = candidatos ?? CargarCandidatos(informe.IdEspecialidad, ahora, profesionalesExcluidos, new HashSet<(int, DateTime)>(), con, tx);
        for (var intento = 0; intento < 3; intento++)
        {
            var elegido = AsignacionAutomaticaReglas.Elegir(lista, prefs);
            if (elegido is null) break;
            var motivo = agregarAlMotivo is null ? elegido.Motivo : elegido.Motivo + " " + agregarAlMotivo;
            var id = InsertarPropuesta(con, tx, informe, elegido, nro, idUsuarioOrigen, ahora, motivo);
            if (id is null)
            {
                // Otra propuesta tomó ese horario en el mismo instante: se descarta de la lista (en memoria) y se vuelve a elegir.
                lista = lista.Select(c => c.IdProfesional == elegido.IdProfesional ? c with { Horarios = c.Horarios.Where(h => h != elegido.Momento).ToList() } : c).ToList();
                continue;
            }

            Ejecutar(con, tx, "UPDATE InformeAdmision SET AsignacionEstado = @E, AsignacionMotivo = NULL WHERE IdInforme = @I", ("@E", AsignacionInforme.Propuesta), ("@I", informe.IdInforme));
            Auditar(con, tx, idUsuarioOrigen, "Se generó una propuesta de asignación", informe.IdInforme,
                $"propuesta #{id}, nro {nro}, profesional #{elegido.IdProfesional}, {elegido.Momento:dd/MM/yyyy HH:mm}; la recepción aceptó el formulario y el sistema propone, el paciente decide");
            AvisosPaciente.CrearUnaVez(con, tx, informe.IdPaciente, $"propuesta:{id}", "Te propusimos un profesional",
                $"Te propusimos a {elegido.Nombre}{(string.IsNullOrWhiteSpace(elegido.Especialidad) ? "" : " (" + elegido.Especialidad + ")")} {AsignacionAutomaticaReglas.Cuando(elegido.Momento)}. " +
                "Entrá a tus primeros pasos para confirmarlo o elegir otro horario.", null);
            return new(id, null);
        }
        return PorResolver(con, tx, informe, idUsuarioOrigen, ahora, AsignacionAutomaticaReglas.MotivoSinHorarios);
    }

    private ResultadoAlAceptar PorResolver(SqlConnection con, SqlTransaction tx, InformeCtx informe, int? idUsuario, DateTime ahora, string motivo)
    {
        Ejecutar(con, tx, "UPDATE InformeAdmision SET AsignacionEstado = @E, AsignacionMotivo = @M WHERE IdInforme = @I",
            ("@E", AsignacionInforme.PorResolver), ("@M", Cortar(motivo, 500)), ("@I", informe.IdInforme));
        Auditar(con, tx, idUsuario, "La asignación automática quedó para resolver", informe.IdInforme, motivo);
        AvisarRecepcionUnaVez(con, tx, "asignacion-por-resolver", "Una asignación espera a la recepción", $"{NombrePaciente(con, tx, informe.IdPaciente)}: {motivo}", $"(formulario #{informe.IdInforme})");
        return new(null, motivo);
    }

    private int? InsertarPropuesta(SqlConnection con, SqlTransaction tx, InformeCtx informe, ElegidoPropuesta e, int nro, int? idUsuarioOrigen, DateTime ahora, string motivo)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO PropuestaAsignacion (IdInformeAdmision, IdPaciente, IdProfesional, FechaHora, Estado, Motivo, NroPropuesta, FechaCreacion, ExpiraEn, IdUsuarioOrigen)
            OUTPUT INSERTED.IdPropuesta
            VALUES (@I, @P, @R, @F, N'Pendiente', @M, @N, @Ahora, @Expira, @U)", con, tx);
        cmd.Parameters.AddWithValue("@I", informe.IdInforme);
        cmd.Parameters.AddWithValue("@P", informe.IdPaciente);
        cmd.Parameters.AddWithValue("@R", e.IdProfesional);
        cmd.Parameters.AddWithValue("@F", e.Momento);
        cmd.Parameters.AddWithValue("@M", Cortar(motivo, 500));
        cmd.Parameters.AddWithValue("@N", (byte)nro);
        cmd.Parameters.AddWithValue("@Ahora", ahora);
        cmd.Parameters.AddWithValue("@Expira", ahora.AddHours(AsignacionAutomaticaReglas.HorasParaResponder));
        cmd.Parameters.AddWithValue("@U", (object?)idUsuarioOrigen ?? DBNull.Value);
        try { return (int)cmd.ExecuteScalar()!; }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return null; }   // ese horario ya tiene una propuesta pendiente, o este formulario ya tiene la suya
    }

    // ───────────── Candidatos y horarios libres ─────────────

    /// <summary>Profesionales activos de la especialidad pedida (nunca Admisión) con sus horarios libres. Un horario libre es: franja de agenda vigente que no es de admisión, con la anticipación
    /// mínima, dentro del horizonte, sin un turno activo y sin otra propuesta pendiente de otro paciente.</summary>
    private List<CandidatoPropuesta> CargarCandidatos(int idEspecialidad, DateTime ahora, ISet<int> excluidos, ISet<(int, DateTime)> descartados, SqlConnection? conTx = null, SqlTransaction? tx = null)
    {
        var profesionales = new List<(int Id, string Nombre, string? Especialidad, int Activos)>();
        using (var con = Conexion.ObtenerConexion())
        {
            con.Open();
            using var cmd = new SqlCommand(@"
                SELECT p.IdProfesional, LTRIM(RTRIM(p.Nombre + ISNULL(' ' + p.Apellido, N''))) AS Nombre, e.Nombre AS Especialidad,
                       (SELECT COUNT(*) FROM PacienteProfesional pp JOIN Paciente pa ON pa.IdPaciente = pp.IdPaciente
                        WHERE pp.IdProfesional = p.IdProfesional AND ISNULL(pp.EstadoAsignacion, N'Confirmada') NOT IN (N'Rechazada', N'Cancelada')
                          AND ISNULL(pa.Estado, N'Activo') <> N'Inactivo') AS Activos
                FROM Profesional p LEFT JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
                WHERE p.Estado = N'Activo' AND p.IdEspecialidad = @E AND ISNULL(p.Matricula, N'') <> N'ADM-001'
                ORDER BY p.IdProfesional", con);
            cmd.Parameters.AddWithValue("@E", idEspecialidad);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var especialidad = r["Especialidad"] as string;
                if (TextoNormalizado.Normalizar(especialidad) == "admision") continue;   // Admisión entrevista, no atiende pacientes
                profesionales.Add(((int)r["IdProfesional"], (string)r["Nombre"], especialidad, (int)r["Activos"]));
            }
        }
        return profesionales.Where(p => !excluidos.Contains(p.Id))
            .Select(p => new CandidatoPropuesta(p.Id, p.Nombre, p.Especialidad, p.Activos,
                HorariosLibres(p.Id, ahora, conTx, tx).Where(h => !descartados.Contains((p.Id, h))).ToList()))
            .Where(c => c.Horarios.Count > 0)
            .ToList();
    }

    /// <summary>Los horarios libres de un profesional entre «ahora + anticipación» y el horizonte, del más próximo al más lejano.</summary>
    public List<DateTime> HorariosLibres(int idProfesional, DateTime ahora, SqlConnection? conTx = null, SqlTransaction? tx = null)
    {
        var resultado = new List<DateTime>();
        var agenda = _repo.AgendaDe(idProfesional)
            .Where(a => a.Disponible && !a.EsAdmision && a.HoraInicio.HasValue && a.HoraFin.HasValue).ToList();   // las franjas de admisión (migración 29) no se ofrecen
        if (agenda.Count == 0) return resultado;
        var desde = ahora.AddHours(AsignacionAutomaticaReglas.HorasAnticipacionMinima);
        var hasta = ahora.AddDays(AsignacionAutomaticaReglas.DiasHorizonte);
        var ocupados = Ocupados(conTx, tx, idProfesional, desde.Date, hasta.Date, ahora);
        for (var fecha = desde.Date; fecha <= hasta.Date; fecha = fecha.AddDays(1))
        {
            // La vigencia de cada franja (migración 30) se respeta día por día.
            var franjas = agenda.Where(a => TurnoReglasService.DiaDeLaSemana(a.Dia) == fecha.DayOfWeek && TurnoReglasService.VigenteEl(a, fecha)).ToList();
            foreach (var slot in TurnoReglasService.Slots(franjas).Distinct().OrderBy(s => s))
            {
                var momento = fecha + slot;
                if (momento < desde || momento > hasta || ocupados.Contains(momento)) continue;
                resultado.Add(momento);
            }
        }
        return resultado;
    }

    // Turnos que no están cancelados ni rechazados (el mismo criterio que HorasOcupadas) y propuestas pendientes todavía vigentes.
    // Si quien llama tiene una transacción abierta se lee con ella: lo que ya cambió en esa transacción lo ve (y no se espera a sí misma).
    private static HashSet<DateTime> Ocupados(SqlConnection? conTx, SqlTransaction? tx, int idProfesional, DateTime desde, DateTime hasta, DateTime ahora)
    {
        var ocupados = new HashSet<DateTime>();
        var propia = conTx is null ? Conexion.ObtenerConexion() : null;
        propia?.Open();
        var con = conTx ?? propia!;
        using var cmd = new SqlCommand(@"
            SELECT CAST(t.Fecha AS DATETIME) + CAST(t.Hora AS DATETIME)
            FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdProfesional = @R AND t.Fecha >= @D AND t.Fecha <= @H AND et.Descripcion NOT IN (N'Cancelado', N'Rechazado')
            UNION ALL
            SELECT FechaHora FROM PropuestaAsignacion
            WHERE IdProfesional = @R AND Estado = N'Pendiente' AND ExpiraEn > @Ahora AND FechaHora >= @D AND FechaHora < DATEADD(DAY, 1, @H)", con, tx);
        cmd.Parameters.AddWithValue("@R", idProfesional);
        cmd.Parameters.AddWithValue("@D", desde);
        cmd.Parameters.AddWithValue("@H", hasta);
        cmd.Parameters.AddWithValue("@Ahora", ahora);
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read()) ocupados.Add(r.GetDateTime(0));
            return ocupados;
        }
        finally { propia?.Dispose(); }
    }

    // ───────────── Lo que ve el paciente ─────────────

    /// <summary>La propuesta pendiente (y todavía vigente) del paciente, o null. Antes hace caducar las vencidas.</summary>
    public PropuestaVista? PropuestaDelPaciente(int idPaciente, DateTime ahora)
    {
        if (!Disponible()) return null;
        VencerVencidas(ahora);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        return LeerVista(con, null, "pr.IdPaciente = @P AND pr.Estado = N'Pendiente' AND pr.ExpiraEn > @Ahora", ("@P", idPaciente), ("@Ahora", ahora));
    }

    /// <summary>¿Quedó el formulario más reciente del paciente en «Asignaciones para resolver»? (para decirle que lo estamos resolviendo).</summary>
    public static bool PorResolverDelPaciente(int idPaciente)
    {
        if (!Disponible()) return false;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT TOP 1 CASE WHEN Estado = N'Aprobado' AND AsignacionEstado = N'Por resolver' THEN 1 ELSE 0 END FROM InformeAdmision WHERE IdPaciente = @P ORDER BY IdInforme DESC", con);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
    }

    private static PropuestaVista? LeerVista(SqlConnection con, SqlTransaction? tx, string donde, params (string, object?)[] ps)
    {
        using var cmd = new SqlCommand($@"
            SELECT pr.IdPropuesta, pr.IdProfesional, LTRIM(RTRIM(p.Nombre + ISNULL(' ' + p.Apellido, N''))) AS Profesional, e.Nombre AS Especialidad, pr.FechaHora, pr.ExpiraEn, pr.NroPropuesta
            FROM PropuestaAsignacion pr JOIN Profesional p ON p.IdProfesional = pr.IdProfesional LEFT JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
            WHERE {donde}", con, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var nro = Convert.ToInt32(r["NroPropuesta"]);
        return new PropuestaVista((int)r["IdPropuesta"], (int)r["IdProfesional"], (string)r["Profesional"], r["Especialidad"] as string,
            (DateTime)r["FechaHora"], (DateTime)r["ExpiraEn"], nro, nro < AsignacionAutomaticaReglas.MaxPropuestas);
    }

    /// <summary>Los próximos horarios libres del MISMO profesional de la propuesta (hasta 10), sin el que ya se propuso.</summary>
    public (ResultadoAsignacionAuto? Error, List<DateTime> Horarios) HorariosAlternativos(int idPropuesta, int idPaciente, DateTime ahora)
    {
        if (!Disponible()) return (NoDisponible(), new());
        VencerVencidas(ahora);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var p = LeerPropuesta(con, null, idPropuesta);
        if (p is null || p.IdPaciente != idPaciente) return (new(404, "no-existe", "No encontramos esa propuesta."), new());
        if (p.Estado != PropuestaEstado.Pendiente) return (Respondida(p), new());
        if (p.NroPropuesta >= AsignacionAutomaticaReglas.MaxPropuestas)
            return (new(409, "sin-mas-propuestas", "Ya usaste las tres propuestas. Si ninguna te sirve, la recepción te va a ayudar a elegir."), new());
        var libres = HorariosLibres(p.IdProfesional, ahora).Where(h => h != p.FechaHora).Take(AsignacionAutomaticaReglas.MaxHorariosAlternativos).ToList();
        return (null, libres);
    }

    // ───────────── «Me sirve» ─────────────

    /// <summary>
    /// El paciente confirma la propuesta. En una sola transacción: revalida que el horario siga libre y que el consentimiento siga vigente (con las reglas de siempre de los turnos:
    /// RN-01, RN-07 y RN-02), crea el vínculo con el profesional y el turno confirmado, marca el formulario como asignado y la propuesta como aceptada, y avisa al profesional.
    /// Repetirlo (un doble clic) devuelve el mismo turno y no crea otro.
    /// </summary>
    public ResultadoAsignacionAuto Aceptar(int idPropuesta, int idPaciente, int idUsuario, DateTime ahora)
    {
        if (!Disponible()) return NoDisponible();
        VencerVencidas(ahora);
        int? idTurnoCreado = null;
        ResultadoAsignacionAuto resultado;
        PropuestaCtx? aceptada = null;

        using (var con = Conexion.ObtenerConexion())
        {
            con.Open();
            using var tx = con.BeginTransaction();
            // De a una por propuesta (el doble clic espera al primero) y de a una por profesional, como el resto de las confirmaciones de primer turno.
            if (!Bloquear(con, tx, "YoSoy.Propuesta." + idPropuesta) ) return new(503, "ocupado", "Estamos procesando tu respuesta. Probá de nuevo en unos segundos.");
            var p = LeerPropuesta(con, tx, idPropuesta);
            if (p is null || p.IdPaciente != idPaciente) return new(404, "no-existe", "No encontramos esa propuesta.");
            if (p.Estado == PropuestaEstado.Aceptada)
                return new(200, "ya-aceptada", "Ya confirmaste esta propuesta: tu turno está agendado.", null, p.IdTurno);
            if (p.Estado != PropuestaEstado.Pendiente) return Respondida(p);
            if (!Bloquear(con, tx, "YoSoy.PrimerTurno." + p.IdProfesional))
                return new(503, "ocupado", "Estamos agendando otro turno de ese profesional. Probá de nuevo en unos segundos.");

            var informe = LeerInforme(con, tx, p.IdInformeAdmision);
            if (informe is null || informe.Estado != EstadoInforme.Aprobado)
                return Cancelar(con, tx, p, idPaciente, ahora, "El formulario ya no está esperando asignación.", "Esta propuesta ya no está vigente: la recepción ya resolvió tu asignación.");

            // El consentimiento tiene que seguir vigente: si lo revocó o venció mientras tanto, no se asigna (la recepción retoma el caso cuando vuelva a firmar).
            var consentimiento = ConsentimientoTratamiento.Estado(idPaciente, _consentimiento.CurrentValue, ahora);
            if (!consentimiento.Vigente)
            {
                Cerrar(con, tx, p, PropuestaEstado.Cancelada, ahora, "El consentimiento dejó de estar vigente.");
                Auditar(con, tx, idUsuario, "Se canceló la propuesta de asignación", p.IdInformeAdmision, $"propuesta #{p.IdPropuesta}: el consentimiento del paciente ya no estaba vigente", "paciente");
                PorResolver(con, tx, informe, idUsuario, ahora, MotivoPorConsentimiento(consentimiento.Motivo));
                tx.Commit();
                return new(409, "consentimiento-pendiente", "Tu consentimiento ya no está vigente. Firmalo de nuevo desde tus primeros pasos y la recepción retoma tu asignación.");
            }

            // Las reglas de los turnos (RN-01 horario libre y dentro de la agenda, RN-07, y RN-02 si está en modo estricto).
            var regla = _reglas.ValidarSolicitud(idPaciente, p.IdProfesional, p.FechaHora);
            if (!regla.Ok) return Reemplazar(con, tx, informe, p, idPaciente, idUsuario, ahora, regla);

            var estadoConfirmado = _repo.IdEstado("Confirmado") ?? throw new InvalidOperationException("No existe el estado 'Confirmado' en EstadoTurno.");
            var idTurno = TurnoRepositorioDAO.InsertarSiLibreEn(con, tx, new Turno
            {
                IdPaciente = idPaciente, IdProfesional = p.IdProfesional, IdEstadoTurno = estadoConfirmado,
                Fecha = p.FechaHora.Date, Hora = p.FechaHora.TimeOfDay, Modalidad = "Virtual", Motivo = "Primer turno - asignación automática"
            }, "paciente");
            if (idTurno is null) return Reemplazar(con, tx, informe, p, idPaciente, idUsuario, ahora, ResultadoRegla.Falla("RN-01", "Ese horario se acaba de ocupar."));
            Ejecutar(con, tx, "UPDATE Turno SET ConfirmadoPaciente = 1 WHERE IdTurno = @T", ("@T", idTurno.Value));

            // El vínculo (confirmado: el paciente ya aceptó y el turno está confirmado) y el formulario asignado.
            Ejecutar(con, tx, @"
                IF EXISTS (SELECT 1 FROM PacienteProfesional WHERE IdPaciente = @P AND IdProfesional = @R)
                    UPDATE PacienteProfesional SET EstadoAsignacion = N'Confirmada', FechaAsignacion = @F WHERE IdPaciente = @P AND IdProfesional = @R
                ELSE
                    INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, FechaAsignacion, EstadoAsignacion) VALUES (@P, @R, @F, N'Confirmada')",
                ("@P", idPaciente), ("@R", p.IdProfesional), ("@F", ahora));
            Ejecutar(con, tx, @"
                UPDATE InformeAdmision SET IdProfesionalDerivado = @R, Estado = N'Derivado', MotivoRechazo = NULL, AsignacionEstado = NULL, AsignacionMotivo = NULL
                WHERE IdInforme = @I", ("@R", p.IdProfesional), ("@I", p.IdInformeAdmision));
            SincronizarCaso(con, tx, p.IdInformeAdmision, p.IdProfesional, idUsuario, ahora);

            Ejecutar(con, tx, "UPDATE PropuestaAsignacion SET Estado = N'Aceptada', FechaRespuesta = @F, IdTurno = @T WHERE IdPropuesta = @Id AND Estado = N'Pendiente'",
                ("@F", ahora), ("@T", idTurno.Value), ("@Id", p.IdPropuesta));
            Auditar(con, tx, idUsuario, "El paciente aceptó la propuesta de asignación", p.IdInformeAdmision,
                $"propuesta #{p.IdPropuesta} aceptada: vínculo con profesional #{p.IdProfesional} y turno #{idTurno} el {p.FechaHora:dd/MM/yyyy HH:mm}", "paciente");

            var nombre = NombrePaciente(con, tx, idPaciente);
            using (var aviso = new SqlCommand(@"
                INSERT INTO NotificacionInterna (Destinatario, IdProfesional, IdTurno, Tipo, Titulo, Mensaje)
                VALUES (N'Profesional', @R, @T, N'asignacion-nueva', @Ti, @M)", con, tx))
            {
                aviso.Parameters.AddWithValue("@R", p.IdProfesional);
                aviso.Parameters.AddWithValue("@T", idTurno.Value);
                aviso.Parameters.AddWithValue("@Ti", "Tenés un paciente nuevo con primer turno");
                aviso.Parameters.AddWithValue("@M", Cortar($"{nombre} aceptó que lo atiendas y quedó con turno confirmado {AsignacionAutomaticaReglas.Cuando(p.FechaHora)}. Falta que generes la sala.", 1000));
                aviso.ExecuteNonQuery();
            }
            AvisosPaciente.CrearUnaVez(con, tx, idPaciente, $"propuesta:{p.IdPropuesta}:aceptada", "Tu primer turno quedó confirmado",
                $"Quedó confirmado tu primer turno {AsignacionAutomaticaReglas.Cuando(p.FechaHora)}. Tu profesional va a generar la sala de video.", null);
            tx.Commit();
            idTurnoCreado = idTurno;
            aceptada = p;
            resultado = new(200, "aceptada", $"Listo, tu primer turno quedó confirmado {AsignacionAutomaticaReglas.Cuando(p.FechaHora)}.", null, idTurno);
        }

        if (idTurnoCreado is int t && aceptada is not null)
        {
            try { _notificador.TurnoConfirmado(t); }   // el aviso al paciente por los canales de siempre; si falla, el turno ya está creado
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo avisar la confirmación del turno {Turno}.", t); }
        }
        return resultado;
    }

    // El horario ya no está disponible: la propuesta se reemplaza por otra (misma numeración: no fue un rechazo del paciente) y se le explica.
    private ResultadoAsignacionAuto Reemplazar(SqlConnection con, SqlTransaction tx, InformeCtx informe, PropuestaCtx p, int idPaciente, int idUsuario, DateTime ahora, ResultadoRegla regla)
    {
        if (regla.Codigo == "RN-07")
        {
            tx.Rollback();
            return new(409, "turno-del-dia", "Ya tenés un turno con ese profesional ese día. Elegí otro horario o probá con otro profesional.");
        }
        // Primero todo lo que se lee (por otras conexiones); recién después se escribe y se audita.
        var rechazados = ProfesionalesRechazados(con, tx, p.IdInformeAdmision);
        var candidatos = CargarCandidatos(informe.IdEspecialidad, ahora, rechazados, new HashSet<(int, DateTime)>(), con, tx);
        Cerrar(con, tx, p, PropuestaEstado.Cancelada, ahora, "El horario ya no estaba libre.");
        Auditar(con, tx, idUsuario, "Se canceló la propuesta de asignación", p.IdInformeAdmision, $"propuesta #{p.IdPropuesta}: el horario ya no estaba libre", "paciente");
        var nueva = ProponerSiguiente(con, tx, informe, p.NroPropuesta, p.IdUsuarioOrigen, ahora, rechazados, candidatos: candidatos);
        PropuestaVista? vista = nueva.IdPropuesta is int id ? LeerVista(con, tx, "pr.IdPropuesta = @I", ("@I", id)) : null;
        tx.Commit();
        return vista is not null
            ? new(409, "horario-ocupado", $"Ese horario se acaba de ocupar. Te propusimos otro: {vista.Profesional}, {AsignacionAutomaticaReglas.Cuando(vista.FechaHora)}.", vista)
            : new(409, "horario-ocupado", "Ese horario se acaba de ocupar. " + AsignacionAutomaticaReglas.MensajeBuscando);
    }

    private ResultadoAsignacionAuto Cancelar(SqlConnection con, SqlTransaction tx, PropuestaCtx p, int idPaciente, DateTime ahora, string motivo, string mensaje)
    {
        Cerrar(con, tx, p, PropuestaEstado.Cancelada, ahora, motivo);
        Auditar(con, tx, null, "Se canceló la propuesta de asignación", p.IdInformeAdmision, $"propuesta #{p.IdPropuesta}: {motivo}");
        tx.Commit();
        return new(409, "ya-no-vigente", mensaje);
    }

    // ───────────── Rechazos ─────────────

    /// <summary>
    /// El paciente no acepta la propuesta. tipo = «otro-horario» (con la fecha elegida entre las libres del mismo profesional), «otro-profesional» o «ninguno». Cada rechazo o cambio suma una propuesta; al
    /// llegar a la tercera sin aceptar, o si no hay más candidatos, el caso vuelve a la recepción y al paciente se le dice que lo estamos resolviendo.
    /// </summary>
    public ResultadoAsignacionAuto Rechazar(int idPropuesta, int idPaciente, int idUsuario, string? tipo, DateTime? fechaHora, DateTime ahora)
    {
        if (!Disponible()) return NoDisponible();
        if (tipo is not ("otro-horario" or "otro-profesional" or "ninguno"))
            return new(400, "tipo", "Elegí una opción: otro horario, otro profesional o ninguna.");
        if (tipo == "otro-horario" && fechaHora is null) return new(400, "falta-horario", "Elegí el horario que preferís.");
        VencerVencidas(ahora);

        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        if (!Bloquear(con, tx, "YoSoy.Propuesta." + idPropuesta)) return new(503, "ocupado", "Estamos procesando tu respuesta. Probá de nuevo en unos segundos.");
        var p = LeerPropuesta(con, tx, idPropuesta);
        if (p is null || p.IdPaciente != idPaciente) return new(404, "no-existe", "No encontramos esa propuesta.");
        if (p.Estado != PropuestaEstado.Pendiente) return Respondida(p);
        var informe = LeerInforme(con, tx, p.IdInformeAdmision);
        if (informe is null || informe.Estado != EstadoInforme.Aprobado)
            return Cancelar(con, tx, p, idPaciente, ahora, "El formulario ya no está esperando asignación.", "Esta propuesta ya no está vigente: la recepción ya resolvió tu asignación.");

        var textoRespuesta = tipo switch { "otro-horario" => "pidió otro horario", "otro-profesional" => "pidió probar con otro profesional", _ => "no le sirvió" };
        // Los candidatos de la siguiente propuesta se leen antes de escribir nada (ver ProponerSiguiente): sin los profesionales ya rechazados y sin este.
        HashSet<int>? excluidos = null;
        List<CandidatoPropuesta>? siguientes = null;
        if (tipo == "otro-profesional" && p.NroPropuesta < AsignacionAutomaticaReglas.MaxPropuestas)
        {
            excluidos = ProfesionalesRechazados(con, tx, p.IdInformeAdmision);
            excluidos.Add(p.IdProfesional);
            siguientes = CargarCandidatos(informe.IdEspecialidad, ahora, excluidos, new HashSet<(int, DateTime)>(), con, tx);
        }
        PropuestaVista? nueva = null;
        string? motivoPorResolver = null;

        if (tipo == "otro-horario" && p.NroPropuesta < AsignacionAutomaticaReglas.MaxPropuestas)
        {
            // El horario elegido tiene que estar entre los libres del mismo profesional: se vuelve a calcular acá (lo que se vio puede haberse ocupado).
            var libres = HorariosLibres(p.IdProfesional, ahora, con, tx).Where(h => h != p.FechaHora).Take(AsignacionAutomaticaReglas.MaxHorariosAlternativos).ToList();
            if (!libres.Contains(fechaHora!.Value))
            {
                tx.Rollback();
                return new(409, "horario-ocupado", "Ese horario ya no está libre. Elegí otro de la lista actualizada.");
            }
            Cerrar(con, tx, p, PropuestaEstado.Rechazada, ahora, "El paciente pidió otro horario.");
            Auditar(con, tx, idUsuario, "El paciente rechazó la propuesta de asignación", p.IdInformeAdmision, $"propuesta #{p.IdPropuesta}: {textoRespuesta}", "paciente");
            var elegido = new ElegidoPropuesta(p.IdProfesional, "", null, fechaHora.Value, "Horario elegido por el paciente entre los libres del mismo profesional.");
            var id = InsertarPropuesta(con, tx, informe, elegido, p.NroPropuesta + 1, p.IdUsuarioOrigen, ahora, elegido.Motivo);
            if (id is null) { tx.Rollback(); return new(409, "horario-ocupado", "Ese horario ya no está libre. Elegí otro de la lista actualizada."); }
            AuditarNueva(con, tx, idUsuario, informe.IdInforme, id.Value, p.NroPropuesta + 1, p.IdProfesional, fechaHora.Value);
            nueva = LeerVista(con, tx, "pr.IdPropuesta = @I", ("@I", id.Value));
        }
        else
        {
            Cerrar(con, tx, p, PropuestaEstado.Rechazada, ahora, $"El paciente {textoRespuesta}.");
            Auditar(con, tx, idUsuario, "El paciente rechazó la propuesta de asignación", p.IdInformeAdmision, $"propuesta #{p.IdPropuesta}: {textoRespuesta}", "paciente");
            if (p.NroPropuesta >= AsignacionAutomaticaReglas.MaxPropuestas)
                motivoPorResolver = "El paciente no aceptó las tres propuestas.";
            else if (tipo == "ninguno")
                motivoPorResolver = $"El paciente no aceptó la propuesta {p.NroPropuesta} y no quiso otra opción.";
            else
            {
                var siguiente = ProponerSiguiente(con, tx, informe, p.NroPropuesta + 1, p.IdUsuarioOrigen, ahora, excluidos!, candidatos: siguientes);
                if (siguiente.IdPropuesta is int id) nueva = LeerVista(con, tx, "pr.IdPropuesta = @I", ("@I", id));
                else motivoPorResolver = null;   // ProponerSiguiente ya dejó el caso en «Asignaciones para resolver» con su motivo
            }
            if (motivoPorResolver is not null) PorResolver(con, tx, informe, idUsuario, ahora, motivoPorResolver);
        }
        tx.Commit();
        return nueva is not null
            ? new(200, "nueva-propuesta", $"Te propusimos otra opción: {nueva.Profesional}, {AsignacionAutomaticaReglas.Cuando(nueva.FechaHora)}.", nueva)
            : new(200, "por-resolver", AsignacionAutomaticaReglas.MensajeBuscando);
    }

    // Los profesionales cuya propuesta rechazó el paciente en este formulario (no se le vuelven a proponer).
    private static HashSet<int> ProfesionalesRechazados(SqlConnection con, SqlTransaction tx, int idInforme)
    {
        var ids = new HashSet<int>();
        using var cmd = new SqlCommand("SELECT DISTINCT IdProfesional FROM PropuestaAsignacion WHERE IdInformeAdmision = @I AND Estado = N'Rechazada'", con, tx);
        cmd.Parameters.AddWithValue("@I", idInforme);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt32(0));
        return ids;
    }

    // ───────────── La recepción ─────────────

    /// <summary>
    /// «Volver a calcular la propuesta»: para un formulario que quedó en «Asignaciones para resolver» sin haber agotado las tres propuestas (por ejemplo, porque el paciente no tenía el consentimiento
    /// o no había horarios y ahora sí). La pide una persona de recepción; el paciente sigue decidiendo.
    /// </summary>
    public ResultadoAsignacionAuto Reproponer(int idInforme, int idUsuario, DateTime ahora)
    {
        if (!Disponible()) return NoDisponible();
        VencerVencidas(ahora);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        if (!Bloquear(con, tx, "YoSoy.Informe." + idInforme)) return new(503, "ocupado", "Se está procesando este formulario. Probá de nuevo en unos segundos.");
        var informe = LeerInforme(con, tx, idInforme);
        if (informe is null) return new(404, "no-existe", "No encontramos ese formulario.");
        if (informe.Estado != EstadoInforme.Aprobado) return new(409, "estado", "Solo se puede proponer un profesional a un formulario aceptado que todavía no tiene profesional.");
        if (PendienteDe(con, tx, idInforme) is not null)
            return new(200, "ya-propuesta", "El paciente ya tiene una propuesta esperando su respuesta.");
        if (informe.AsignacionEstado != AsignacionInforme.PorResolver)
            return new(409, "estado", "Este formulario no está esperando que lo resuelvas a mano.");
        int maxNro;
        using (var cmd = new SqlCommand("SELECT ISNULL(MAX(NroPropuesta), 0) FROM PropuestaAsignacion WHERE IdInformeAdmision = @I", con, tx))
        {
            cmd.Parameters.AddWithValue("@I", idInforme);
            maxNro = Convert.ToInt32(cmd.ExecuteScalar());
        }
        if (maxNro >= AsignacionAutomaticaReglas.MaxPropuestas)
            return new(409, "sin-propuestas", "Ya se hicieron las tres propuestas. Asignale un profesional a mano.");

        var consentimiento = ConsentimientoTratamiento.Estado(informe.IdPaciente, _consentimiento.CurrentValue, ahora);
        if (!consentimiento.Vigente)
        {
            PorResolver(con, tx, informe, idUsuario, ahora, MotivoPorConsentimiento(consentimiento.Motivo));
            tx.Commit();
            return new(409, "consentimiento-pendiente", MotivoPorConsentimiento(consentimiento.Motivo));
        }
        var resultado = ProponerSiguiente(con, tx, informe, maxNro + 1, idUsuario, ahora, ProfesionalesRechazados(con, tx, idInforme));
        tx.Commit();
        return resultado.IdPropuesta is not null
            ? new(200, "propuesta", "Listo: le propusimos un profesional y un horario al paciente. Está esperando su respuesta.")
            : new(409, "sin-horarios", AsignacionAutomaticaReglas.MotivoSinHorarios + ". Asignale un profesional a mano.");
    }

    /// <summary>
    /// La recepción asignó a mano (PUT /informes-admision/{id}/derivar): cualquier propuesta pendiente del paciente se cancela y su horario queda libre. Devuelve cuántas canceló.
    /// </summary>
    public static int CancelarPendientesPorAsignacionManual(int idPaciente, int? idUsuario, DateTime ahora)
    {
        if (!Disponible()) return 0;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        var canceladas = new List<(int IdPropuesta, int IdInforme)>();
        using (var cmd = new SqlCommand(@"
            UPDATE PropuestaAsignacion SET Estado = N'Cancelada', FechaRespuesta = @F, Motivo = LEFT(ISNULL(Motivo, N'') + N' | Cancelada: la recepción asignó el profesional a mano.', 500)
            OUTPUT INSERTED.IdPropuesta, INSERTED.IdInformeAdmision
            WHERE IdPaciente = @P AND Estado = N'Pendiente'", con, tx))
        {
            cmd.Parameters.AddWithValue("@F", ahora);
            cmd.Parameters.AddWithValue("@P", idPaciente);
            using var r = cmd.ExecuteReader();
            while (r.Read()) canceladas.Add((r.GetInt32(0), r.GetInt32(1)));
        }
        foreach (var (idPropuesta, idInforme) in canceladas)
            Auditar(con, tx, idUsuario, "Se canceló la propuesta de asignación", idInforme, $"propuesta #{idPropuesta}: la recepción asignó el profesional a mano", "recepción");
        tx.Commit();
        return canceladas.Count;
    }

    /// <summary>
    /// El formulario se devolvió al paciente o se cerró con «otro recurso»: una propuesta pendiente ya no tiene sentido. Se cancela (con constancia) y su horario queda libre.
    /// Dentro de la transacción de quien llama.
    /// </summary>
    public static void CancelarDelInformeEnTx(SqlConnection con, SqlTransaction tx, int idInforme, int? idUsuario, DateTime ahora, string motivo)
    {
        if (!Disponible()) return;
        var canceladas = new List<int>();
        using (var cmd = new SqlCommand(@"
            UPDATE PropuestaAsignacion SET Estado = N'Cancelada', FechaRespuesta = @F, Motivo = LEFT(ISNULL(Motivo, N'') + @M, 500)
            OUTPUT INSERTED.IdPropuesta
            WHERE IdInformeAdmision = @I AND Estado = N'Pendiente'", con, tx))
        {
            cmd.Parameters.AddWithValue("@F", ahora);
            cmd.Parameters.AddWithValue("@M", " | Cancelada: " + motivo);
            cmd.Parameters.AddWithValue("@I", idInforme);
            using var r = cmd.ExecuteReader();
            while (r.Read()) canceladas.Add(r.GetInt32(0));
        }
        foreach (var id in canceladas)
            Auditar(con, tx, idUsuario, "Se canceló la propuesta de asignación", idInforme, $"propuesta #{id}: {motivo}", "recepción");
        Ejecutar(con, tx, "UPDATE InformeAdmision SET AsignacionEstado = NULL, AsignacionMotivo = NULL WHERE IdInforme = @I AND AsignacionEstado IS NOT NULL", ("@I", idInforme));
    }

    /// <summary>Con profesional asignado a mano, el formulario ya no espera nada: se limpia el estado de la asignación automática («Propuesta» o «Por resolver»). Se llama después de asignar.</summary>
    public static void LimpiarAlAsignar(int idInforme)
    {
        if (!Disponible()) return;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("UPDATE InformeAdmision SET AsignacionEstado = NULL, AsignacionMotivo = NULL WHERE IdInforme = @I AND AsignacionEstado IS NOT NULL", con);
        cmd.Parameters.AddWithValue("@I", idInforme);
        cmd.ExecuteNonQuery();
    }

    // ───────────── Vencimiento (se evalúa al consultar, sin proceso en segundo plano) ─────────────

    /// <summary>
    /// Pasa a «Vencida» toda propuesta pendiente cuyo plazo terminó (queda auditado) y deja el formulario en «Asignaciones para resolver»: si el paciente no respondió, la recepción decide cómo seguir.
    /// Se llama antes de leer o de responder propuestas; es barato y repetirlo no hace nada de más.
    /// </summary>
    public static void VencerVencidas(DateTime ahora)
    {
        if (!Disponible()) return;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using (var hay = new SqlCommand("SELECT COUNT(*) FROM PropuestaAsignacion WHERE Estado = N'Pendiente' AND ExpiraEn <= @A", con))
        {
            hay.Parameters.AddWithValue("@A", ahora);
            if ((int)hay.ExecuteScalar()! == 0) return;
        }
        using var tx = con.BeginTransaction();
        var vencidas = new List<(int IdPropuesta, int IdInforme, int IdPaciente)>();
        using (var cmd = new SqlCommand(@"
            UPDATE PropuestaAsignacion SET Estado = N'Vencida', FechaRespuesta = @A
            OUTPUT INSERTED.IdPropuesta, INSERTED.IdInformeAdmision, INSERTED.IdPaciente
            WHERE Estado = N'Pendiente' AND ExpiraEn <= @A", con, tx))
        {
            cmd.Parameters.AddWithValue("@A", ahora);
            using var r = cmd.ExecuteReader();
            while (r.Read()) vencidas.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
        }
        foreach (var (idPropuesta, idInforme, idPaciente) in vencidas)
        {
            // TODO-VALIDAR: qué hacer cuando el paciente no responde en el plazo (hoy: pasa a la recepción, que decide).
            const string motivo = "El paciente no respondió la propuesta a tiempo.";
            Auditar(con, tx, null, "Venció la propuesta de asignación", idInforme, $"propuesta #{idPropuesta}: pasó el plazo de {AsignacionAutomaticaReglas.HorasParaResponder} horas sin respuesta");
            Ejecutar(con, tx, "UPDATE InformeAdmision SET AsignacionEstado = @E, AsignacionMotivo = @M WHERE IdInforme = @I AND Estado = N'Aprobado'",
                ("@E", AsignacionInforme.PorResolver), ("@M", motivo), ("@I", idInforme));
            AvisarRecepcionUnaVez(con, tx, "asignacion-por-resolver", "Una asignación espera a la recepción", $"{NombrePaciente(con, tx, idPaciente)}: {motivo}", $"(formulario #{idInforme})");
        }
        tx.Commit();
    }

    /// <summary>Para el listado de la recepción: la propuesta pendiente de cada formulario (profesional y horario), por IdInforme.</summary>
    public static Dictionary<int, (string Profesional, DateTime FechaHora, DateTime ExpiraEn, int Nro)> PendientesPorInforme()
    {
        var mapa = new Dictionary<int, (string, DateTime, DateTime, int)>();
        if (!Disponible()) return mapa;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT pr.IdInformeAdmision, LTRIM(RTRIM(p.Nombre + ISNULL(' ' + p.Apellido, N''))) AS Profesional, pr.FechaHora, pr.ExpiraEn, pr.NroPropuesta
            FROM PropuestaAsignacion pr JOIN Profesional p ON p.IdProfesional = pr.IdProfesional WHERE pr.Estado = N'Pendiente'", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) mapa[r.GetInt32(0)] = ((string)r["Profesional"], (DateTime)r["FechaHora"], (DateTime)r["ExpiraEn"], Convert.ToInt32(r["NroPropuesta"]));
        return mapa;
    }

    // ───────────── Lecturas y escrituras de apoyo ─────────────

    private record InformeCtx(int IdInforme, int IdPaciente, string Estado, int IdEspecialidad, string? Horario, string? Profesionales, string? AsignacionEstado);
    private record PropuestaCtx(int IdPropuesta, int IdInformeAdmision, int IdPaciente, int IdProfesional, DateTime FechaHora, string Estado, int NroPropuesta, DateTime ExpiraEn, int? IdTurno, int? IdUsuarioOrigen);

    private static InformeCtx? LeerInforme(SqlConnection con, SqlTransaction? tx, int idInforme)
    {
        using var cmd = new SqlCommand(@"
            SELECT IdPaciente, Estado, IdEspecialidad, HorarioPreferido, ProfesionalesPreferidos, AsignacionEstado FROM InformeAdmision WHERE IdInforme = @I", con, tx);
        cmd.Parameters.AddWithValue("@I", idInforme);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new InformeCtx(idInforme, (int)r["IdPaciente"], (string)r["Estado"], r["IdEspecialidad"] is int e ? e : 0,
            r["HorarioPreferido"] as string, r["ProfesionalesPreferidos"] as string, r["AsignacionEstado"] as string);
    }

    private static PropuestaCtx? LeerPropuesta(SqlConnection con, SqlTransaction? tx, int idPropuesta)
    {
        using var cmd = new SqlCommand(@"
            SELECT IdPropuesta, IdInformeAdmision, IdPaciente, IdProfesional, FechaHora, Estado, NroPropuesta, ExpiraEn, IdTurno, IdUsuarioOrigen
            FROM PropuestaAsignacion WHERE IdPropuesta = @Id", con, tx);
        cmd.Parameters.AddWithValue("@Id", idPropuesta);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PropuestaCtx((int)r["IdPropuesta"], (int)r["IdInformeAdmision"], (int)r["IdPaciente"], (int)r["IdProfesional"], (DateTime)r["FechaHora"], (string)r["Estado"],
            Convert.ToInt32(r["NroPropuesta"]), (DateTime)r["ExpiraEn"], r["IdTurno"] as int?, r["IdUsuarioOrigen"] as int?);
    }

    private static int? PendienteDe(SqlConnection con, SqlTransaction tx, int idInforme)
    {
        using var cmd = new SqlCommand("SELECT IdPropuesta FROM PropuestaAsignacion WHERE IdInformeAdmision = @I AND Estado = N'Pendiente'", con, tx);
        cmd.Parameters.AddWithValue("@I", idInforme);
        return cmd.ExecuteScalar() as int?;
    }

    private static ResultadoAsignacionAuto Respondida(PropuestaCtx p) => p.Estado switch
    {
        PropuestaEstado.Vencida => new(409, "vencida", "Esta propuesta venció. La recepción va a retomar tu asignación y te avisa."),
        PropuestaEstado.Rechazada => new(409, "ya-respondida", "Ya respondiste esta propuesta."),
        _ => new(409, "ya-no-vigente", "Esta propuesta ya no está vigente. Entrá a tus primeros pasos para ver cómo sigue.")
    };

    private static ResultadoAsignacionAuto NoDisponible() => new(404, "no-disponible", "Esta función todavía no está disponible.");

    private static void Cerrar(SqlConnection con, SqlTransaction tx, PropuestaCtx p, string estado, DateTime ahora, string motivo) =>
        Ejecutar(con, tx, "UPDATE PropuestaAsignacion SET Estado = @E, FechaRespuesta = @F, Motivo = LEFT(ISNULL(Motivo, N'') + @M, 500) WHERE IdPropuesta = @Id AND Estado = N'Pendiente'",
            ("@E", estado), ("@F", ahora), ("@M", " | " + estado + ": " + motivo), ("@Id", p.IdPropuesta));

    private static void AuditarNueva(SqlConnection con, SqlTransaction tx, int? idUsuario, int idInforme, int idPropuesta, int nro, int idProfesional, DateTime momento) =>
        Auditar(con, tx, idUsuario, "Se generó una propuesta de asignación", idInforme, $"propuesta #{idPropuesta}, nro {nro}, profesional #{idProfesional}, {momento:dd/MM/yyyy HH:mm}; horario elegido por el paciente", "paciente");

    // La auditoría de las acciones automáticas lleva a la persona de recepción que aceptó el formulario como origen; las del paciente, al paciente.
    private static void Auditar(SqlConnection con, SqlTransaction tx, int? idUsuario, string accion, int idInforme, string detalle, string? calidad = null) =>
        ADComun.Auditar(con, tx, new AutorDelCierre(idUsuario, Espacio, calidad), accion, "InformeAdmision", idInforme, detalle);

    // El formulario tenía un caso de seguimiento (Fase 2): se lo deja asignado, como hace la asignación manual, y la derivación sugerida que quedara pendiente se resuelve.
    private static void SincronizarCaso(SqlConnection con, SqlTransaction tx, int idInforme, int idProfesional, int idUsuario, DateTime ahora)
    {
        Ejecutar(con, tx, @"
            UPDATE cd SET Estado = CASE WHEN cd.IdProfesionalSugerido = @R THEN N'Confirmada' ELSE N'Corregida' END, IdProfesionalAsignado = @R, FechaResolucion = @F
            FROM CasoDerivacion cd JOIN Caso c ON c.IdCaso = cd.IdCaso
            WHERE c.IdInforme = @I AND cd.Estado = N'Pendiente de confirmar'", ("@R", idProfesional), ("@F", ahora), ("@I", idInforme));
        Ejecutar(con, tx, @"
            UPDATE Caso SET Estado = N'Asignado', IdProfesionalAsignado = @R WHERE IdInforme = @I AND Estado IN (N'Nuevo', N'Derivado');
            INSERT INTO HistorialAsignacion (IdCaso, IdProfesional, Motivo, IdRealizadoPor, FechaHora)
            SELECT IdCaso, @R, N'Manual', @U, @F FROM Caso WHERE IdInforme = @I AND IdProfesionalAsignado = @R AND Estado = N'Asignado'
              AND NOT EXISTS (SELECT 1 FROM HistorialAsignacion h WHERE h.IdCaso = Caso.IdCaso AND h.IdProfesional = @R AND h.FechaHora = @F);",
            ("@R", idProfesional), ("@I", idInforme), ("@U", idUsuario), ("@F", ahora));
    }

    // Un aviso a la recepción (campanita del módulo) por formulario: no se repite mientras el anterior siga sin leer.
    private static void AvisarRecepcionUnaVez(SqlConnection con, SqlTransaction? tx, string tipo, string titulo, string mensaje, string clave)
    {
        using var cmd = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM NotificacionInterna WHERE Destinatario = N'Administrador' AND Tipo = @T AND Leida = 0 AND Mensaje LIKE @L)
                INSERT INTO NotificacionInterna (Destinatario, Tipo, Titulo, Mensaje) VALUES (N'Administrador', @T, @Ti, @M)", con, tx);
        cmd.Parameters.AddWithValue("@T", tipo);
        cmd.Parameters.AddWithValue("@L", "%" + clave);
        cmd.Parameters.AddWithValue("@Ti", titulo);
        cmd.Parameters.AddWithValue("@M", Cortar(mensaje, 1000 - clave.Length - 1) + " " + clave);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Un aviso a la recepción cuando entra un formulario nuevo (o uno devuelto vuelve completado): uno por formulario, sin repetirse mientras siga sin leer.</summary>
    public static void AvisarFormularioNuevo(int idInforme, int idPaciente)
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            AvisarRecepcionUnaVez(con, null, "formulario-nuevo", "Hay un formulario por revisar", $"{NombrePaciente(con, null, idPaciente)} completó «Sobre mí».", $"(formulario #{idInforme})");
        }
        catch (Exception) { /* el aviso no puede tapar el envío del formulario */ }
    }

    private static string NombrePaciente(SqlConnection con, SqlTransaction? tx, int idPaciente)
    {
        using var cmd = new SqlCommand("SELECT Nombre FROM Paciente WHERE IdPaciente = @I", con, tx);
        cmd.Parameters.AddWithValue("@I", idPaciente);
        return cmd.ExecuteScalar() as string is { Length: > 0 } n ? n : $"El paciente #{idPaciente}";
    }

    private static bool Bloquear(SqlConnection con, SqlTransaction tx, string recurso)
    {
        using var cmd = new SqlCommand(@"
            DECLARE @r INT;
            EXEC @r = sp_getapplock @Resource = @Recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            SELECT @r;", con, tx);
        cmd.Parameters.AddWithValue("@Recurso", recurso);
        return (int)cmd.ExecuteScalar()! >= 0;
    }

    private static void Ejecutar(SqlConnection con, SqlTransaction tx, string sql, params (string, object?)[] ps)
    {
        using var cmd = new SqlCommand(sql, con, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static string Cortar(string texto, int max) => texto.Length <= max ? texto : texto[..max];
}
