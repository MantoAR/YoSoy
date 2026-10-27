using Microsoft.Data.SqlClient;

// Ausencias con umbrales (migración 35). El sistema DETECTA, AVISA y PROPONE; una persona con el rol correcto CONFIRMA. Nunca marca solo a nadie como ausente ni como abandono, ni cancela turnos.
//
//   · Pasados 15 minutos del inicio del turno sin que el paciente haya pedido entrar a la sala, el profesional puede marcarlo como ausente (con confirmación).
//   · A los 10 minutos, si el paciente no está, un aviso (una sola vez por turno) por la campanita del profesional.
//   · A la 2.ª ausencia consecutiva: aviso al profesional y a recepción. A la 3.ª: se crea la propuesta «Proponer abandono» para el profesional, que confirma o descarta (con motivo).
//   · Un paciente «Derivado» (a psiquiatría), «En alta» o en «Abandono» no genera ninguno de esos avisos.
//
// Cómo se detecta sin un servicio nuevo: la pasada de RecordatorioBackgroundService (que ya existe) llama a EvaluarTodos cada ciclo, y además se evalúa de forma perezosa al consultar la campanita del profesional y
// al entrar él a la sala. Todo idempotente: cada aviso tiene una clave única (AvisoSeguimiento), así que correr la pasada diez veces no duplica nada. Detalle en docs/AUSENCIAS_ALTA.md.
public static class AusenciasReglas
{
    // TODO-VALIDAR: los umbrales los tienen que confirmar los psicólogos (Lic. Villalón). Hoy: 15 minutos de tolerancia, aviso a los 10, alerta a la 2.ª ausencia consecutiva y propuesta de abandono a la 3.ª.
    public const int ToleranciaMarcarMinutos = 15;
    public const int AvisoTempranoMinutos = 10;
    public const int AusenciasParaAlerta = 2;
    public const int AusenciasParaProponerAbandono = 3;

    // TODO-VALIDAR: hasta cuántas horas después del inicio sigue valiendo el aviso temprano (pasado ese tiempo ya no es un aviso útil: el turno terminó). El botón de ausente no vence.
    public const int AvisoTempranoVigenciaHoras = 3;

    public const string EstadoAusente = "Ausente";
    public const string EstadoConfirmado = "Confirmado";
    public const string CasoAbandono = "Abandono";
    public const string CasoEnAlta = "En alta";
    public const string CasoAsignado = "Asignado";
    public const string CasoEnAtencion = "En atención";

    public const string TipoAvisoTemprano = "ausencia-temprana";
    public const string TipoSegundaAusencia = "ausencia-segunda";
    public const string TipoAbandonoConfirmado = "ausencia-abandono";

    public const string FaltaMigracion35 = "falta-migracion-35";
    public const string FaltaConfirmacion = "falta-confirmacion";
    public const string TurnoInexistente = "turno-inexistente";
    public const string EstadoNoPermitido = "estado-no-permitido";
    public const string DemasiadoPronto = "demasiado-pronto";
    public const string PacienteConectado = "paciente-conectado";
    public const string TurnoConSesion = "turno-con-sesion";
    public const string PropuestaInexistente = "propuesta-inexistente";
    public const string PropuestaYaResuelta = "propuesta-ya-resuelta";
    public const string CasoNoPermitido = "caso-no-permitido";
    public const string YaNoCorresponde = "ya-no-corresponde";
    public const string FaltaMotivo = "falta-motivo";

    public const int MotivoMaximo = 500;

    public static int Status(string codigo) => codigo switch
    {
        TurnoInexistente or PropuestaInexistente => 404,
        FaltaConfirmacion or FaltaMotivo => 400,
        _ => 409
    };

    public static string Mensaje(string codigo, string? dato = null) => codigo switch
    {
        FaltaMigracion35 => "Falta aplicar una actualización de la base de datos (migración 35) para usar el seguimiento de ausencias. No se cambió nada: avisale al equipo técnico.",
        FaltaConfirmacion => "Para aplicarlo tenés que confirmarlo con el botón. No se cambió nada.",
        TurnoInexistente => "No encontramos ese turno entre los tuyos. No se cambió nada.",
        EstadoNoPermitido => $"Este turno figura como {(string.IsNullOrWhiteSpace(dato) ? "no confirmado" : dato)}: solo un turno Confirmado se puede marcar como ausente. No se cambió nada.",
        DemasiadoPronto => $"Todavía no pasaron {ToleranciaMarcarMinutos} minutos desde el inicio del turno{(dato is null ? "" : $": podés marcarlo como ausente a partir de las {dato}")}. No se cambió nada.",
        PacienteConectado => "El paciente ya pidió entrar a la sala de este turno, así que no se puede marcar como ausente. No se cambió nada.",
        TurnoConSesion => "Este turno ya tiene una sesión guardada, así que no se puede marcar como ausente. No se cambió nada.",
        PropuestaInexistente => "No encontramos esa propuesta entre las tuyas.",
        PropuestaYaResuelta => $"Esta propuesta ya fue {dato ?? "resuelta"}. No se cambió nada.",
        CasoNoPermitido => $"El caso está «{dato}»: solo se puede confirmar el abandono de un caso asignado o en atención. No se cambió nada. Si ya no corresponde, descartá la propuesta.",
        YaNoCorresponde => "El paciente ya no suma 3 ausencias consecutivas (hubo una sesión realizada o se corrigió un turno), así que la propuesta ya no corresponde. No se cambió nada: descartala.",
        FaltaMotivo => "Contá por qué descartás la propuesta (hasta 500 caracteres). No se cambió nada.",
        _ => "No se pudo completar la acción. No se cambió nada."
    };

    public static DateTime Inicio(DateTime fecha, TimeSpan hora) => fecha.Date + hora;

    public static bool PuedeMarcarse(DateTime inicio, DateTime ahora) => ahora >= inicio.AddMinutes(ToleranciaMarcarMinutos);

    /// <summary>¿Corresponde el aviso temprano? Desde los 10 minutos del inicio y hasta AvisoTempranoVigenciaHoras horas después.</summary>
    public static bool CorrespondeAvisoTemprano(DateTime inicio, DateTime ahora) =>
        ahora >= inicio.AddMinutes(AvisoTempranoMinutos) && ahora <= inicio.AddHours(AvisoTempranoVigenciaHoras);

    /// <summary>Cuántas ausencias seguidas hay al frente de la lista (de la más reciente a la más vieja): corta en la primera sesión realizada.</summary>
    public static int ContarConsecutivas(IEnumerable<string> estadosDelMasRecienteAlMasViejo)
    {
        var n = 0;
        foreach (var e in estadosDelMasRecienteAlMasViejo)
        {
            if (!string.Equals(e, EstadoAusente, StringComparison.OrdinalIgnoreCase)) break;
            n++;
        }
        return n;
    }

    /// <summary>El caso de este paciente está en un estado que NO genera alertas de ausencia (en alta, en abandono). «Derivado» a psiquiatría se mira aparte.</summary>
    public static bool CasoSilencia(string? estadoDelCaso) => estadoDelCaso is CasoEnAlta or CasoAbandono;

    public static bool CasoSePuedeAbandonar(string? estadoDelCaso) => estadoDelCaso is CasoAsignado or CasoEnAtencion;

    public static string ClaveAvisoTemprano(int idTurno) => $"ausencia-temprana:turno:{idTurno}";
    public static string ClaveSegundaAusencia(int idPaciente, int idTurno) => $"ausencia-segunda:paciente:{idPaciente}:turno:{idTurno}";

    public static string TextoAvisoTemprano(string paciente, string hora) =>
        $"Tu paciente todavía no se conectó. {paciente} tenía turno a las {hora} hs y pasaron {AvisoTempranoMinutos} minutos. Pasados {ToleranciaMarcarMinutos} minutos vas a poder marcar el turno como ausente.";

    public static string TextoSegundaAusencia(string paciente) =>
        $"{paciente} acumula {AusenciasParaAlerta} ausencias consecutivas. Conviene contactarlo/a para ver cómo está y si quiere seguir con el tratamiento.";

    public static string TextoPropuestaAbandono(string paciente, int ausencias) =>
        $"{paciente} suma {ausencias} ausencias consecutivas. El sistema te propone darlo/a por abandono: vos decidís si confirmás o descartás.";

    public static string TextoAbandonoConfirmado(string paciente, int turnosFuturos) =>
        $"El profesional confirmó el abandono de {paciente}. " + (turnosFuturos > 0
            ? $"Tiene {turnosFuturos} turno{(turnosFuturos == 1 ? "" : "s")} por delante: no se cancelaron solos, decidí si corresponde cancelarlos."
            : "No tiene turnos por delante.");
}

public record ResultadoAusencia
{
    public string? Rechazo { get; init; }
    public string? Dato { get; init; }
    public bool Repetido { get; init; }
    public int IdTurno { get; init; }
    public int IdPaciente { get; init; }
    public int Ausencias { get; init; }
    public bool AlertaSegunda { get; init; }
    public int? IdPropuesta { get; init; }
    public static ResultadoAusencia Rechazar(string codigo, string? dato = null) => new() { Rechazo = codigo, Dato = dato };
}

public record ResultadoPropuesta
{
    public string? Rechazo { get; init; }
    public string? Dato { get; init; }
    public bool Repetido { get; init; }
    public int IdPropuesta { get; init; }
    public int IdPaciente { get; init; }
    public int IdCaso { get; init; }
    public int TurnosFuturos { get; init; }
    public static ResultadoPropuesta Rechazar(string codigo, string? dato = null) => new() { Rechazo = codigo, Dato = dato };
}

public record TurnoParaMarcar(int IdTurno, int IdPaciente, string Paciente, DateTime Inicio, bool PuedeMarcar, bool AvisoTemprano);
public record PropuestaAbandonoFila(int IdPropuesta, int IdCaso, int IdPaciente, string Paciente, int Ausencias, DateTime CreadaUtc, List<TurnoFuturo> TurnosFuturos);
public record TurnoFuturo(int IdTurno, DateTime Inicio, string Profesional);
public record EstadoAusenciaTurno(int IdTurno, string Estado, DateTime Inicio, bool PacienteConectado, bool PuedeMarcar, bool AvisoTemprano, bool Silenciado, DateTime HabilitaDesde);

public class AusenciasDAO
{
    private static bool _existe;

    /// <summary>¿La base tiene la migración 35? Una vez que la tiene no se vuelve a preguntar.</summary>
    public static bool Disponible()
    {
        if (_existe) return true;
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand(@"SELECT CASE WHEN OBJECT_ID('dbo.TurnoConexion','U') IS NOT NULL AND OBJECT_ID('dbo.PropuestaAbandono','U') IS NOT NULL
                AND OBJECT_ID('dbo.AvisoSeguimiento','U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.EstadoTurno WHERE Descripcion = N'Ausente') THEN 1 ELSE 0 END", con);
            return _existe = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }
        catch (SqlException) { return false; }
    }

    // ───────────── Quién entró a la sala ─────────────

    /// <summary>Anota (una sola vez) que este rol pidió entrar a la sala del turno. Nunca lanza: el acceso a la sala no puede depender de esto.</summary>
    public static void RegistrarConexion(int idTurno, string rol)
    {
        try
        {
            if (!Disponible()) return;
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand(@"INSERT INTO dbo.TurnoConexion (IdTurno, Rol) SELECT @T, @R WHERE NOT EXISTS (SELECT 1 FROM dbo.TurnoConexion WHERE IdTurno = @T AND Rol = @R)", con);
            cmd.Parameters.AddWithValue("@T", idTurno);
            cmd.Parameters.AddWithValue("@R", rol);
            cmd.ExecuteNonQuery();
        }
        catch (SqlException) { /* un choque de clave (dos pedidos a la vez) o sin la tabla: no importa */ }
    }

    // ───────────── Lecturas compartidas (dentro de la conexión y transacción de quien llama) ─────────────

    private record FilaTurno(int IdTurno, int IdPaciente, int IdProfesional, string Estado, DateTime Inicio, string Paciente);

    private static FilaTurno? LeerTurno(SqlConnection con, SqlTransaction? tx, int idTurno, bool bloquear)
    {
        using var cmd = new SqlCommand($@"
            SELECT t.IdTurno, t.IdPaciente, t.IdProfesional, et.Descripcion AS Estado, t.Fecha, t.Hora, p.Nombre
            FROM Turno t {(bloquear ? "WITH (UPDLOCK, HOLDLOCK)" : "")}
            JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            JOIN Paciente p ON p.IdPaciente = t.IdPaciente
            WHERE t.IdTurno = @T", con, tx);
        cmd.Parameters.AddWithValue("@T", idTurno);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new FilaTurno((int)r["IdTurno"], (int)r["IdPaciente"], (int)r["IdProfesional"], (string)r["Estado"],
            AusenciasReglas.Inicio((DateTime)r["Fecha"], (TimeSpan)r["Hora"]), (string)r["Nombre"]);
    }

    private static bool Existe(SqlConnection con, SqlTransaction? tx, string sql, params (string, object)[] ps)
    {
        using var cmd = new SqlCommand(sql, con, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar() is not null;
    }

    private static bool PacienteConectado(SqlConnection con, SqlTransaction? tx, int idTurno) =>
        Existe(con, tx, "SELECT 1 FROM dbo.TurnoConexion WHERE IdTurno = @T AND Rol = N'Paciente'", ("@T", idTurno));

    private static bool TieneSesion(SqlConnection con, SqlTransaction? tx, int idTurno) =>
        Existe(con, tx, "SELECT 1 FROM SesionClinica WHERE IdTurno = @T", ("@T", idTurno));

    private record FilaCaso(int IdCaso, string Estado, int? IdProfesionalAsignado);

    /// <summary>El caso más reciente del paciente (el que está en curso). null si no tiene ninguno.</summary>
    private static FilaCaso? CasoDelPaciente(SqlConnection con, SqlTransaction? tx, int idPaciente, bool bloquear = false)
    {
        using var cmd = new SqlCommand($"SELECT TOP 1 IdCaso, Estado, IdProfesionalAsignado FROM Caso {(bloquear ? "WITH (UPDLOCK, HOLDLOCK)" : "")} WHERE IdPaciente = @P ORDER BY IdCaso DESC", con, tx);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new FilaCaso((int)r["IdCaso"], (string)r["Estado"], r["IdProfesionalAsignado"] as int?) : null;
    }

    /// <summary>Un paciente derivado a psiquiatría, en alta o en abandono no genera alertas de ausencia.</summary>
    private static bool Silenciado(SqlConnection con, SqlTransaction? tx, int idPaciente, FilaCaso? caso) =>
        DerivacionPsiquiatraDAO.EstaDerivadoEn(con, tx, idPaciente) || AusenciasReglas.CasoSilencia(caso?.Estado);

    /// <summary>Los estados (Ausente / Realizado) de los turnos ya pasados de este paciente con este profesional, del más reciente al más viejo. «Antes de» deja afuera ese turno y los posteriores.</summary>
    private static List<string> HistorialDeAsistencia(SqlConnection con, SqlTransaction? tx, int idPaciente, int idProfesional, DateTime? antesDe, DateTime ahora)
    {
        using var cmd = new SqlCommand(@"
            SELECT et.Descripcion
            FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdPaciente = @P AND t.IdProfesional = @M AND et.Descripcion IN (N'Ausente', N'Realizado')
              AND CAST(t.Fecha AS DATETIME) + CAST(t.Hora AS DATETIME) < @Tope
            ORDER BY t.Fecha DESC, t.Hora DESC", con, tx);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        cmd.Parameters.AddWithValue("@M", idProfesional);
        cmd.Parameters.AddWithValue("@Tope", antesDe ?? ahora);
        var lista = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add((string)r[0]);
        return lista;
    }

    private static List<TurnoFuturo> TurnosPorDelante(SqlConnection con, SqlTransaction? tx, int idPaciente, DateTime ahora)
    {
        using var cmd = new SqlCommand(@"
            SELECT t.IdTurno, t.Fecha, t.Hora, pr.Nombre + ISNULL(' ' + pr.Apellido, '') AS Profesional
            FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno JOIN Profesional pr ON pr.IdProfesional = t.IdProfesional
            WHERE t.IdPaciente = @P AND et.Descripcion IN (N'Pendiente', N'Confirmado') AND CAST(t.Fecha AS DATETIME) + CAST(t.Hora AS DATETIME) > @Ahora
            ORDER BY t.Fecha, t.Hora", con, tx);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        cmd.Parameters.AddWithValue("@Ahora", ahora);
        var lista = new List<TurnoFuturo>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(new TurnoFuturo((int)r["IdTurno"], AusenciasReglas.Inicio((DateTime)r["Fecha"], (TimeSpan)r["Hora"]), (string)r["Profesional"]));
        return lista;
    }

    /// <summary>Reserva la clave del aviso. true = es la primera vez (hay que avisar); false = ya se avisó. Va en la misma transacción que el aviso.</summary>
    private static bool ReservarClave(SqlConnection con, SqlTransaction tx, string clave)
    {
        using var cmd = new SqlCommand("INSERT INTO dbo.AvisoSeguimiento (Clave) SELECT @C WHERE NOT EXISTS (SELECT 1 FROM dbo.AvisoSeguimiento WHERE Clave = @C)", con, tx);
        cmd.Parameters.AddWithValue("@C", clave);
        return cmd.ExecuteNonQuery() == 1;
    }

    private static void AvisarDelTurno(SqlConnection con, SqlTransaction tx, string destinatario, int? idProfesional, int? idTurno, string tipo, string titulo, string mensaje)
    {
        using var cmd = new SqlCommand(@"INSERT INTO NotificacionInterna (Destinatario, IdProfesional, IdTurno, Tipo, Titulo, Mensaje) VALUES (@D, @P, @T, @Ti, @Tt, @M)", con, tx);
        cmd.Parameters.AddWithValue("@D", destinatario);
        cmd.Parameters.AddWithValue("@P", (object?)idProfesional ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@T", (object?)idTurno ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Ti", tipo);
        cmd.Parameters.AddWithValue("@Tt", titulo.Length > 200 ? titulo[..200] : titulo);
        cmd.Parameters.AddWithValue("@M", mensaje.Length > 1000 ? mensaje[..1000] : mensaje);
        cmd.ExecuteNonQuery();
    }

    // ───────────── Marcar como ausente ─────────────

    /// <summary>El estado del turno para la sala y el panel del profesional: si ya puede marcar, si el paciente se conectó, desde cuándo se habilita.</summary>
    public EstadoAusenciaTurno? EstadoDeAusencia(int idTurno, int idProfesional, DateTime ahora)
    {
        if (!Disponible()) return null;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var t = LeerTurno(con, null, idTurno, bloquear: false);
        if (t is null || t.IdProfesional != idProfesional) return null;
        var conectado = PacienteConectado(con, null, idTurno);
        var silenciado = Silenciado(con, null, t.IdPaciente, CasoDelPaciente(con, null, t.IdPaciente));
        var elegible = t.Estado == AusenciasReglas.EstadoConfirmado && !conectado && !TieneSesion(con, null, idTurno);
        return new EstadoAusenciaTurno(idTurno, t.Estado, t.Inicio, conectado,
            elegible && AusenciasReglas.PuedeMarcarse(t.Inicio, ahora),
            elegible && !silenciado && AusenciasReglas.CorrespondeAvisoTemprano(t.Inicio, ahora),
            silenciado, t.Inicio.AddMinutes(AusenciasReglas.ToleranciaMarcarMinutos));
    }

    /// <summary>
    /// El profesional marca el turno como ausente. Una sola transacción, idempotente, auditada. Se LEE todo primero y recién después se escribe (la auditoría serializa las transacciones).
    /// Con la 2.ª ausencia consecutiva avisa al profesional y a recepción; con la 3.ª crea la propuesta de abandono (NO pasa al paciente a abandono).
    /// </summary>
    public ResultadoAusencia MarcarAusente(int idTurno, int idProfesional, AutorDelCierre autor, DateTime ahora)
    {
        if (!Disponible()) return ResultadoAusencia.Rechazar(AusenciasReglas.FaltaMigracion35);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        // ── 1) Leer ──
        var t = LeerTurno(con, tx, idTurno, bloquear: true);
        if (t is null || t.IdProfesional != idProfesional) return ResultadoAusencia.Rechazar(AusenciasReglas.TurnoInexistente);
        if (t.Estado == AusenciasReglas.EstadoAusente) return new ResultadoAusencia { Repetido = true, IdTurno = idTurno, IdPaciente = t.IdPaciente };
        if (t.Estado != AusenciasReglas.EstadoConfirmado) return ResultadoAusencia.Rechazar(AusenciasReglas.EstadoNoPermitido, t.Estado);
        if (!AusenciasReglas.PuedeMarcarse(t.Inicio, ahora))
            return ResultadoAusencia.Rechazar(AusenciasReglas.DemasiadoPronto, t.Inicio.AddMinutes(AusenciasReglas.ToleranciaMarcarMinutos).ToString("HH:mm"));
        if (PacienteConectado(con, tx, idTurno)) return ResultadoAusencia.Rechazar(AusenciasReglas.PacienteConectado);
        if (TieneSesion(con, tx, idTurno)) return ResultadoAusencia.Rechazar(AusenciasReglas.TurnoConSesion);

        int idEstadoAusente, idEstadoConfirmado;
        using (var cmd = new SqlCommand("SELECT (SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = N'Ausente'), (SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = N'Confirmado')", con, tx))
        {
            using var r = cmd.ExecuteReader();
            r.Read();
            if (r.IsDBNull(0) || r.IsDBNull(1)) return ResultadoAusencia.Rechazar(AusenciasReglas.FaltaMigracion35);
            idEstadoAusente = r.GetInt32(0); idEstadoConfirmado = r.GetInt32(1);
        }

        var caso = CasoDelPaciente(con, tx, t.IdPaciente);
        var silenciado = Silenciado(con, tx, t.IdPaciente, caso);
        var consecutivas = 1 + AusenciasReglas.ContarConsecutivas(HistorialDeAsistencia(con, tx, t.IdPaciente, idProfesional, t.Inicio, ahora));
        var propuestaPendiente = caso is not null && Existe(con, tx, "SELECT 1 FROM dbo.PropuestaAbandono WHERE IdCaso = @C AND Estado = N'Pendiente'", ("@C", caso.IdCaso));

        // ── 2) Escribir ──
        using (var upd = new SqlCommand("UPDATE Turno SET IdEstadoTurno = @Nuevo WHERE IdTurno = @T AND IdEstadoTurno = @Antes", con, tx))
        {
            upd.Parameters.AddWithValue("@Nuevo", idEstadoAusente);
            upd.Parameters.AddWithValue("@T", idTurno);
            upd.Parameters.AddWithValue("@Antes", idEstadoConfirmado);
            if (upd.ExecuteNonQuery() != 1) return ResultadoAusencia.Rechazar(AusenciasReglas.EstadoNoPermitido, t.Estado);   // no se llega: el turno está bloqueado
        }

        var alertaSegunda = false;
        int? idPropuesta = null;
        if (!silenciado)
        {
            if (consecutivas == AusenciasReglas.AusenciasParaAlerta && ReservarClave(con, tx, AusenciasReglas.ClaveSegundaAusencia(t.IdPaciente, idTurno)))
            {
                var texto = AusenciasReglas.TextoSegundaAusencia(t.Paciente);
                AvisarDelTurno(con, tx, "Profesional", idProfesional, idTurno, AusenciasReglas.TipoSegundaAusencia, "Segunda ausencia consecutiva", texto);
                AvisarDelTurno(con, tx, "Administrador", null, idTurno, AusenciasReglas.TipoSegundaAusencia, "Segunda ausencia consecutiva", texto);
                alertaSegunda = true;
            }
            else if (consecutivas >= AusenciasReglas.AusenciasParaProponerAbandono && caso is not null && AusenciasReglas.CasoSePuedeAbandonar(caso.Estado) && !propuestaPendiente)
            {
                // Una propuesta por turno disparador (índice único) y una sola pendiente por caso.
                using var ins = new SqlCommand(@"
                    INSERT INTO dbo.PropuestaAbandono (IdCaso, IdPaciente, IdProfesional, IdTurnoDisparador, Ausencias)
                    OUTPUT INSERTED.IdPropuestaAbandono
                    SELECT @C, @P, @M, @T, @N WHERE NOT EXISTS (SELECT 1 FROM dbo.PropuestaAbandono WHERE IdTurnoDisparador = @T)", con, tx);
                ins.Parameters.AddWithValue("@C", caso.IdCaso);
                ins.Parameters.AddWithValue("@P", t.IdPaciente);
                ins.Parameters.AddWithValue("@M", idProfesional);
                ins.Parameters.AddWithValue("@T", idTurno);
                ins.Parameters.AddWithValue("@N", consecutivas);
                idPropuesta = ins.ExecuteScalar() as int?;
            }
        }

        // La auditoría va al final: toma un bloqueo que dura hasta el commit.
        ADComun.Auditar(con, tx, autor, "Turno marcado como ausente", "Turno", idTurno,
            $"Turno #{idTurno}, paciente #{t.IdPaciente}, profesional #{idProfesional}: {t.Estado} -> Ausente, ausencias consecutivas {consecutivas}" + (silenciado ? ", sin alertas (derivado, en alta o abandono)" : "")
            + (idPropuesta is int p ? $", propuesta de abandono #{p}" : ""));

        tx.Commit();
        return new ResultadoAusencia { IdTurno = idTurno, IdPaciente = t.IdPaciente, Ausencias = consecutivas, AlertaSegunda = alertaSegunda, IdPropuesta = idPropuesta };
    }

    // ───────────── Aviso temprano (pasada perezosa o del servicio de fondo) ─────────────

    /// <summary>
    /// Avisa por la campanita a los profesionales cuyo paciente no se conectó pasados 10 minutos del inicio. Una sola vez por turno (clave única). idProfesional = null: todos (la pasada del servicio).
    /// Devuelve cuántos avisos nuevos creó. Nunca marca ni cambia nada del turno.
    /// </summary>
    public int EvaluarAvisosTempranos(DateTime ahora, int? idProfesional = null)
    {
        if (!Disponible()) return 0;
        var candidatos = new List<(int IdTurno, int IdPaciente, int IdProfesional, string Paciente, DateTime Inicio)>();
        using (var con = Conexion.ObtenerConexion())
        {
            con.Open();
            using var cmd = new SqlCommand(@"
                SELECT t.IdTurno, t.IdPaciente, t.IdProfesional, p.Nombre, t.Fecha, t.Hora
                FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno JOIN Paciente p ON p.IdPaciente = t.IdPaciente
                WHERE et.Descripcion = N'Confirmado' AND t.Fecha BETWEEN @Ayer AND @Hoy AND (@M IS NULL OR t.IdProfesional = @M)
                  AND NOT EXISTS (SELECT 1 FROM dbo.TurnoConexion c WHERE c.IdTurno = t.IdTurno AND c.Rol = N'Paciente')
                  AND NOT EXISTS (SELECT 1 FROM SesionClinica s WHERE s.IdTurno = t.IdTurno)", con);
            cmd.Parameters.AddWithValue("@Ayer", ahora.Date.AddDays(-1));
            cmd.Parameters.AddWithValue("@Hoy", ahora.Date);
            cmd.Parameters.AddWithValue("@M", (object?)idProfesional ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var inicio = AusenciasReglas.Inicio((DateTime)r["Fecha"], (TimeSpan)r["Hora"]);
                if (AusenciasReglas.CorrespondeAvisoTemprano(inicio, ahora))
                    candidatos.Add(((int)r["IdTurno"], (int)r["IdPaciente"], (int)r["IdProfesional"], (string)r["Nombre"], inicio));
            }
        }

        var creados = 0;
        foreach (var c in candidatos)
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var tx = con.BeginTransaction();
            var caso = CasoDelPaciente(con, tx, c.IdPaciente);
            if (Silenciado(con, tx, c.IdPaciente, caso)) { tx.Rollback(); continue; }
            try
            {
                if (!ReservarClave(con, tx, AusenciasReglas.ClaveAvisoTemprano(c.IdTurno))) { tx.Rollback(); continue; }
                AvisarDelTurno(con, tx, "Profesional", c.IdProfesional, c.IdTurno, AusenciasReglas.TipoAvisoTemprano, "Tu paciente todavía no se conectó",
                    AusenciasReglas.TextoAvisoTemprano(c.Paciente, c.Inicio.ToString("HH:mm")));
                tx.Commit();
                creados++;
            }
            catch (SqlException e) when (e.Number is 2601 or 2627) { /* otra pasada llegó primero con la misma clave: ya se avisó */ }
        }
        return creados;
    }

    /// <summary>Los turnos del profesional que ya se pueden marcar como ausentes (para la campanita). Solo lectura.</summary>
    public List<TurnoParaMarcar> PorMarcar(int idProfesional, DateTime ahora)
    {
        var lista = new List<TurnoParaMarcar>();
        if (!Disponible()) return lista;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT t.IdTurno, t.IdPaciente, p.Nombre, t.Fecha, t.Hora
            FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno JOIN Paciente p ON p.IdPaciente = t.IdPaciente
            WHERE et.Descripcion = N'Confirmado' AND t.IdProfesional = @M AND t.Fecha BETWEEN @Desde AND @Hoy
              AND NOT EXISTS (SELECT 1 FROM dbo.TurnoConexion c WHERE c.IdTurno = t.IdTurno AND c.Rol = N'Paciente')
              AND NOT EXISTS (SELECT 1 FROM SesionClinica s WHERE s.IdTurno = t.IdTurno)
            ORDER BY t.Fecha, t.Hora", con);
        cmd.Parameters.AddWithValue("@M", idProfesional);
        cmd.Parameters.AddWithValue("@Desde", ahora.Date.AddDays(-7));
        cmd.Parameters.AddWithValue("@Hoy", ahora.Date);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var inicio = AusenciasReglas.Inicio((DateTime)r["Fecha"], (TimeSpan)r["Hora"]);
            if (AusenciasReglas.PuedeMarcarse(inicio, ahora))
                lista.Add(new TurnoParaMarcar((int)r["IdTurno"], (int)r["IdPaciente"], (string)r["Nombre"], inicio, true, false));
        }
        return lista;
    }

    // ───────────── Propuesta de abandono ─────────────

    public List<PropuestaAbandonoFila> PropuestasPendientes(int idProfesional, DateTime ahora)
    {
        var lista = new List<PropuestaAbandonoFila>();
        if (!Disponible()) return lista;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var crudas = new List<(int, int, int, string, int, DateTime)>();
        using (var cmd = new SqlCommand(@"
            SELECT a.IdPropuestaAbandono, a.IdCaso, a.IdPaciente, p.Nombre, a.Ausencias, a.CreadaUtc
            FROM dbo.PropuestaAbandono a JOIN Paciente p ON p.IdPaciente = a.IdPaciente
            WHERE a.IdProfesional = @M AND a.Estado = N'Pendiente' ORDER BY a.IdPropuestaAbandono", con))
        {
            cmd.Parameters.AddWithValue("@M", idProfesional);
            using var r = cmd.ExecuteReader();
            while (r.Read()) crudas.Add(((int)r[0], (int)r[1], (int)r[2], (string)r[3], (int)r[4], (DateTime)r[5]));
        }
        foreach (var (id, caso, pac, nombre, aus, creada) in crudas)
            lista.Add(new PropuestaAbandonoFila(id, caso, pac, nombre, aus, creada, TurnosPorDelante(con, null, pac, ahora)));
        return lista;
    }

    /// <summary>El profesional confirma el abandono: el caso pasa a «Abandono». NO cancela turnos: los muestra y avisa a recepción para que decida.</summary>
    public ResultadoPropuesta ConfirmarAbandono(int idPropuesta, int idProfesional, AutorDelCierre autor, DateTime ahora)
    {
        if (!Disponible()) return ResultadoPropuesta.Rechazar(AusenciasReglas.FaltaMigracion35);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        // ── 1) Leer ──
        int idCaso, idPaciente, ausenciasPropuestas; string estadoPropuesta;
        using (var cmd = new SqlCommand("SELECT IdCaso, IdPaciente, IdProfesional, Estado, Ausencias FROM dbo.PropuestaAbandono WITH (UPDLOCK, HOLDLOCK) WHERE IdPropuestaAbandono = @I", con, tx))
        {
            cmd.Parameters.AddWithValue("@I", idPropuesta);
            using var r = cmd.ExecuteReader();
            if (!r.Read() || (int)r["IdProfesional"] != idProfesional) return ResultadoPropuesta.Rechazar(AusenciasReglas.PropuestaInexistente);
            idCaso = (int)r["IdCaso"]; idPaciente = (int)r["IdPaciente"]; estadoPropuesta = (string)r["Estado"]; ausenciasPropuestas = (int)r["Ausencias"];
        }
        if (estadoPropuesta == "Confirmada") return new ResultadoPropuesta { Repetido = true, IdPropuesta = idPropuesta, IdPaciente = idPaciente, IdCaso = idCaso };
        if (estadoPropuesta == "Descartada") return ResultadoPropuesta.Rechazar(AusenciasReglas.PropuestaYaResuelta, "descartada");

        var caso = CasoDelPaciente(con, tx, idPaciente, bloquear: true);
        if (caso is null || caso.IdCaso != idCaso) return ResultadoPropuesta.Rechazar(AusenciasReglas.CasoNoPermitido, caso?.Estado ?? "sin caso");
        if (!AusenciasReglas.CasoSePuedeAbandonar(caso.Estado)) return ResultadoPropuesta.Rechazar(AusenciasReglas.CasoNoPermitido, caso.Estado);

        // Si entre medio hubo una sesión realizada, el conteo se reinició y la propuesta ya no corresponde.
        var consecutivas = AusenciasReglas.ContarConsecutivas(HistorialDeAsistencia(con, tx, idPaciente, idProfesional, null, ahora));
        if (consecutivas < AusenciasReglas.AusenciasParaProponerAbandono) return ResultadoPropuesta.Rechazar(AusenciasReglas.YaNoCorresponde);

        var nombre = ADComun.NombrePaciente(con, tx, idPaciente);
        var futuros = TurnosPorDelante(con, tx, idPaciente, ahora);

        // ── 2) Escribir ──
        using (var upd = new SqlCommand("UPDATE Caso SET Estado = @Nuevo WHERE IdCaso = @C AND Estado = @Antes", con, tx))
        {
            upd.Parameters.AddWithValue("@Nuevo", AusenciasReglas.CasoAbandono);
            upd.Parameters.AddWithValue("@C", idCaso);
            upd.Parameters.AddWithValue("@Antes", caso.Estado);
            if (upd.ExecuteNonQuery() != 1) return ResultadoPropuesta.Rechazar(AusenciasReglas.CasoNoPermitido, caso.Estado);   // no se llega: el caso está bloqueado
        }
        using (var upd = new SqlCommand(@"UPDATE dbo.PropuestaAbandono SET Estado = N'Confirmada', ResueltaUtc = SYSUTCDATETIME(), IdUsuarioResolucion = @U, EstadoCasoAnterior = @E
                                          WHERE IdPropuestaAbandono = @I AND Estado = N'Pendiente'", con, tx))
        {
            upd.Parameters.AddWithValue("@U", (object?)autor.IdUsuario ?? DBNull.Value);
            upd.Parameters.AddWithValue("@E", caso.Estado);
            upd.Parameters.AddWithValue("@I", idPropuesta);
            upd.ExecuteNonQuery();
        }
        // Recepción decide qué hacer con los turnos que quedan: se le avisa siempre, con la cantidad.
        AvisarDelTurno(con, tx, "Administrador", null, null, AusenciasReglas.TipoAbandonoConfirmado, "Paciente en abandono", AusenciasReglas.TextoAbandonoConfirmado(nombre, futuros.Count));

        ADComun.Auditar(con, tx, autor, "Propuesta de abandono confirmada", "PropuestaAbandono", idPropuesta,
            $"Propuesta #{idPropuesta}, caso #{idCaso}, paciente #{idPaciente}, ausencias {ausenciasPropuestas}, turnos por delante {futuros.Count} (no se cancelaron)");
        ADComun.Auditar(con, tx, autor, "Caso pasa a Abandono", "Caso", idCaso, $"Caso #{idCaso}: {caso.Estado} -> {AusenciasReglas.CasoAbandono}, propuesta #{idPropuesta}, paciente #{idPaciente}");

        tx.Commit();
        return new ResultadoPropuesta { IdPropuesta = idPropuesta, IdPaciente = idPaciente, IdCaso = idCaso, TurnosFuturos = futuros.Count };
    }

    /// <summary>El profesional descarta la propuesta, con motivo. No cambia nada del paciente ni del caso.</summary>
    public ResultadoPropuesta DescartarAbandono(int idPropuesta, int idProfesional, string motivo, AutorDelCierre autor)
    {
        if (!Disponible()) return ResultadoPropuesta.Rechazar(AusenciasReglas.FaltaMigracion35);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        int idCaso, idPaciente; string estadoPropuesta;
        using (var cmd = new SqlCommand("SELECT IdCaso, IdPaciente, IdProfesional, Estado FROM dbo.PropuestaAbandono WITH (UPDLOCK, HOLDLOCK) WHERE IdPropuestaAbandono = @I", con, tx))
        {
            cmd.Parameters.AddWithValue("@I", idPropuesta);
            using var r = cmd.ExecuteReader();
            if (!r.Read() || (int)r["IdProfesional"] != idProfesional) return ResultadoPropuesta.Rechazar(AusenciasReglas.PropuestaInexistente);
            idCaso = (int)r["IdCaso"]; idPaciente = (int)r["IdPaciente"]; estadoPropuesta = (string)r["Estado"];
        }
        if (estadoPropuesta == "Descartada") return new ResultadoPropuesta { Repetido = true, IdPropuesta = idPropuesta, IdPaciente = idPaciente, IdCaso = idCaso };
        if (estadoPropuesta == "Confirmada") return ResultadoPropuesta.Rechazar(AusenciasReglas.PropuestaYaResuelta, "confirmada");

        using (var upd = new SqlCommand(@"UPDATE dbo.PropuestaAbandono SET Estado = N'Descartada', Motivo = @M, ResueltaUtc = SYSUTCDATETIME(), IdUsuarioResolucion = @U
                                          WHERE IdPropuestaAbandono = @I AND Estado = N'Pendiente'", con, tx))
        {
            upd.Parameters.AddWithValue("@M", motivo);
            upd.Parameters.AddWithValue("@U", (object?)autor.IdUsuario ?? DBNull.Value);
            upd.Parameters.AddWithValue("@I", idPropuesta);
            upd.ExecuteNonQuery();
        }
        ADComun.Auditar(con, tx, autor, "Propuesta de abandono descartada", "PropuestaAbandono", idPropuesta, $"Propuesta #{idPropuesta}, caso #{idCaso}, paciente #{idPaciente}, motivo registrado");
        tx.Commit();
        return new ResultadoPropuesta { IdPropuesta = idPropuesta, IdPaciente = idPaciente, IdCaso = idCaso };
    }
}
