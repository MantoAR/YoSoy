using Microsoft.Data.SqlClient;

// Solicitudes de turno del paciente (autoservicio, link público del profesional, bot): el turno nace «Pendiente» y NO es un turno confirmado hasta que el PROFESIONAL lo acepta.
//   · Mientras está pendiente el horario queda reservado (InsertarSiLibre cuenta los Pendientes), no habilita la sala (solo un turno Confirmado la abre) y no cuenta para ausencias ni para los
//     recordatorios (que miran solo los Confirmados).
//   · El profesional lo ve en la campanita y en su panel, y lo acepta o lo rechaza (motivo opcional). Si no responde a tiempo, la solicitud VENCE: se libera el horario y se avisa a los dos.
//   · No requieren aceptación: los turnos que crea recepción o el profesional (CreadoPor = 'profesional'), el que sale de aceptar la propuesta automática de admisión (nace Confirmado) y las
//     reprogramaciones que inicia el profesional. // TODO-VALIDAR: hoy el paciente no tiene un camino propio para pedir una reprogramación; cuando lo tenga, tendría que dejar el turno pendiente.
// Detalle y decisiones: docs/SOLICITUDES_TURNO.md.
public static class SolicitudesTurnoReglas
{
    // TODO-VALIDAR: la vigencia de una solicitud la tienen que confirmar los psicólogos. Hoy vence a las 24 hs de pedida, o 2 hs antes del turno si eso es antes. Para que una solicitud hecha sobre
    // la hora no nazca vencida, nunca vence antes de VigenciaMinimaMinutos y nunca después del inicio del turno.
    public const int VigenciaHoras = 24;
    public const int MargenAntesDelTurnoHoras = 2;
    public const int VigenciaMinimaMinutos = 30;

    public const int MotivoMaximo = 300;

    public const string EstadoPendiente = "Pendiente";
    public const string EstadoConfirmado = "Confirmado";
    public const string EstadoCancelado = "Cancelado";
    public const string CreadoPorPaciente = "paciente";

    public const string TipoVencida = "solicitud-vencida";

    public const string TurnoInexistente = "turno-inexistente";
    public const string NoEsSolicitud = "no-es-solicitud";
    public const string SolicitudCerrada = "solicitud-cerrada";
    public const string FaltaConfirmacion = "falta-confirmacion";
    public const string MotivoLargo = "motivo-largo";

    public static DateTime Vencimiento(DateTime creada, DateTime inicioDelTurno)
    {
        var porTiempo = creada.AddHours(VigenciaHoras);
        var porTurno = inicioDelTurno.AddHours(-MargenAntesDelTurnoHoras);
        var v = porTiempo < porTurno ? porTiempo : porTurno;
        var piso = creada.AddMinutes(VigenciaMinimaMinutos);
        if (v < piso) v = piso;
        if (v > inicioDelTurno) v = inicioDelTurno;
        return v;
    }

    public static int Status(string codigo) => codigo switch
    {
        TurnoInexistente => 404,
        FaltaConfirmacion or MotivoLargo => 400,
        _ => 409
    };

    public static string Mensaje(string codigo, string? dato = null) => codigo switch
    {
        TurnoInexistente => "No encontramos esa solicitud entre las tuyas. No se cambió nada.",
        NoEsSolicitud => "Este turno no es una solicitud del paciente que esté esperando tu respuesta. No se cambió nada.",
        SolicitudCerrada => "Esta solicitud ya no está disponible: fue rechazada, cancelada o venció. No se cambió nada.",
        FaltaConfirmacion => "Para aplicarlo tenés que confirmarlo con el botón. No se cambió nada.",
        MotivoLargo => $"El motivo no puede superar los {MotivoMaximo} caracteres. No se cambió nada.",
        _ => "No se pudo completar la acción. No se cambió nada."
    };

    public static string TextoRechazo(string profesional, string momento, string? motivo) =>
        $"{profesional} no pudo aceptar tu solicitud de turno del {momento}." + (string.IsNullOrWhiteSpace(motivo) ? "" : $" Motivo: {motivo.Trim()}.") +
        " El horario quedó libre: podés elegir otro desde la sección Turnos.";

    public static string TextoVencidaPaciente(string profesional, string momento) =>
        $"Tu solicitud de turno con {profesional} del {momento} venció porque no recibió respuesta a tiempo. El horario quedó libre: podés elegir otro desde la sección Turnos.";

    public static string TextoVencidaProfesional(string paciente, string momento) =>
        $"La solicitud de turno de {paciente} para el {momento} venció sin respuesta y el horario se liberó. Si querés atenderlo/a, proponele un nuevo turno.";
}

public record ResultadoSolicitud2
{
    public string? Rechazo { get; init; }
    public bool Repetido { get; init; }
    public int IdTurno { get; init; }
    public int IdPaciente { get; init; }
    public static ResultadoSolicitud2 Rechazar(string codigo) => new() { Rechazo = codigo };
}

public record SolicitudDeTurno(int IdTurno, int IdPaciente, string Paciente, DateTime Inicio, string? Motivo, DateTime? ExpiraEn);

public class SolicitudesTurnoDAO
{
    private static bool _columna;

    /// <summary>¿La base tiene la migración 36 (Turno.SolicitudExpiraEn)? Una vez que la tiene no se vuelve a preguntar.</summary>
    public static bool Disponible()
    {
        if (_columna) return true;
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Turno', 'SolicitudExpiraEn') IS NULL THEN 0 ELSE 1 END", con);
            return _columna = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }
        catch (SqlException) { return false; }
    }

    private static string Momento(DateTime inicio) => Formato.FechaHora(inicio);

    /// <summary>Anota hasta cuándo puede responder el profesional una solicitud recién creada. Sin la migración 36 no hace nada (la solicitud no vence). Nunca lanza.</summary>
    public static void RegistrarSolicitud(int idTurno, DateTime ahora)
    {
        try
        {
            if (!Disponible()) return;
            using var con = Conexion.ObtenerConexion();
            con.Open();
            DateTime inicio;
            using (var cmd = new SqlCommand("SELECT Fecha, Hora FROM Turno WHERE IdTurno = @T", con))
            {
                cmd.Parameters.AddWithValue("@T", idTurno);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return;
                inicio = ((DateTime)r["Fecha"]).Date + (TimeSpan)r["Hora"];
            }
            using var upd = new SqlCommand("UPDATE Turno SET SolicitudExpiraEn = @E WHERE IdTurno = @T AND SolicitudExpiraEn IS NULL", con);
            upd.Parameters.AddWithValue("@E", SolicitudesTurnoReglas.Vencimiento(ahora, inicio));
            upd.Parameters.AddWithValue("@T", idTurno);
            upd.ExecuteNonQuery();
        }
        catch (SqlException) { /* el turno ya está creado: sin vencimiento, sigue pendiente como siempre */ }
    }

    // ───────────── Lista del profesional ─────────────

    /// <summary>Las solicitudes del paciente que esperan la respuesta de este profesional (turnos futuros, Pendientes, pedidos por el paciente), las más próximas primero.</summary>
    public List<SolicitudDeTurno> Pendientes(int idProfesional, DateTime ahora)
    {
        var lista = new List<SolicitudDeTurno>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var expira = Disponible() ? "t.SolicitudExpiraEn" : "CAST(NULL AS DATETIME2)";
        using var cmd = new SqlCommand($@"
            SELECT t.IdTurno, t.IdPaciente, p.Nombre, t.Fecha, t.Hora, t.Motivo, {expira} AS Expira
            FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno JOIN Paciente p ON p.IdPaciente = t.IdPaciente
            WHERE t.IdProfesional = @M AND et.Descripcion = N'Pendiente' AND t.CreadoPor = N'paciente'
              AND CAST(t.Fecha AS DATETIME) + CAST(t.Hora AS DATETIME) > @Ahora
            ORDER BY t.Fecha, t.Hora", con);
        cmd.Parameters.AddWithValue("@M", idProfesional);
        cmd.Parameters.AddWithValue("@Ahora", ahora);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new SolicitudDeTurno((int)r["IdTurno"], (int)r["IdPaciente"], (string)r["Nombre"], ((DateTime)r["Fecha"]).Date + (TimeSpan)r["Hora"],
                r["Motivo"] as string, r["Expira"] as DateTime?));
        return lista;
    }

    // ───────────── Aceptar / rechazar ─────────────

    private record FilaSolicitud(int IdTurno, int IdPaciente, int IdProfesional, string Estado, string? CreadoPor, DateTime Inicio, string Profesional);

    private static FilaSolicitud? Leer(SqlConnection con, SqlTransaction tx, int idTurno)
    {
        using var cmd = new SqlCommand(@"
            SELECT t.IdTurno, t.IdPaciente, t.IdProfesional, et.Descripcion AS Estado, t.CreadoPor, t.Fecha, t.Hora, pr.Nombre + ISNULL(' ' + pr.Apellido, '') AS Profesional
            FROM Turno t WITH (UPDLOCK, HOLDLOCK) JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno JOIN Profesional pr ON pr.IdProfesional = t.IdProfesional
            WHERE t.IdTurno = @T", con, tx);
        cmd.Parameters.AddWithValue("@T", idTurno);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new FilaSolicitud((int)r["IdTurno"], (int)r["IdPaciente"], (int)r["IdProfesional"], (string)r["Estado"], r["CreadoPor"] as string,
            ((DateTime)r["Fecha"]).Date + (TimeSpan)r["Hora"], (string)r["Profesional"]);
    }

    private static int IdEstado(SqlConnection con, SqlTransaction tx, string descripcion)
    {
        using var cmd = new SqlCommand("SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = @D", con, tx);
        cmd.Parameters.AddWithValue("@D", descripcion);
        return cmd.ExecuteScalar() as int? ?? throw new InvalidOperationException($"No existe el estado '{descripcion}' en EstadoTurno.");
    }

    private static void AvisarAlPaciente(SqlConnection con, SqlTransaction tx, int idPaciente, string titulo, string mensaje)
    {
        using var cmd = new SqlCommand("INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link) VALUES (@P, @T, @M, NULL)", con, tx);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        cmd.Parameters.AddWithValue("@T", titulo);
        cmd.Parameters.AddWithValue("@M", mensaje.Length > 1000 ? mensaje[..1000] : mensaje);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// El profesional acepta la solicitud: Pendiente → Confirmado (y el paciente queda como confirmado: fue él quien lo pidió). Idempotente. El aviso al paciente (campanita y mail de «turno
    /// confirmado») lo manda quien llama después del commit, solo si no fue una repetición. Las reglas de consentimiento (RN-02) y del paciente derivado las revisa quien llama antes.
    /// </summary>
    public ResultadoSolicitud2 Aceptar(int idTurno, int idProfesional, AutorDelCierre autor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        var f = Leer(con, tx, idTurno);
        if (f is null || f.IdProfesional != idProfesional) return ResultadoSolicitud2.Rechazar(SolicitudesTurnoReglas.TurnoInexistente);
        if (f.Estado == SolicitudesTurnoReglas.EstadoConfirmado && f.CreadoPor == SolicitudesTurnoReglas.CreadoPorPaciente)
            return new ResultadoSolicitud2 { Repetido = true, IdTurno = idTurno, IdPaciente = f.IdPaciente };
        if (f.Estado == SolicitudesTurnoReglas.EstadoCancelado) return ResultadoSolicitud2.Rechazar(SolicitudesTurnoReglas.SolicitudCerrada);
        if (f.Estado != SolicitudesTurnoReglas.EstadoPendiente || f.CreadoPor != SolicitudesTurnoReglas.CreadoPorPaciente) return ResultadoSolicitud2.Rechazar(SolicitudesTurnoReglas.NoEsSolicitud);
        var confirmado = IdEstado(con, tx, SolicitudesTurnoReglas.EstadoConfirmado);
        var pendiente = IdEstado(con, tx, SolicitudesTurnoReglas.EstadoPendiente);

        using (var upd = new SqlCommand("UPDATE Turno SET IdEstadoTurno = @Nuevo, ConfirmadoPaciente = 1 WHERE IdTurno = @T AND IdEstadoTurno = @Antes", con, tx))
        {
            upd.Parameters.AddWithValue("@Nuevo", confirmado);
            upd.Parameters.AddWithValue("@T", idTurno);
            upd.Parameters.AddWithValue("@Antes", pendiente);
            if (upd.ExecuteNonQuery() != 1) return ResultadoSolicitud2.Rechazar(SolicitudesTurnoReglas.SolicitudCerrada);   // no se llega: el turno está bloqueado
        }
        ADComun.Auditar(con, tx, autor, "Solicitud de turno aceptada", "Turno", idTurno, $"Turno #{idTurno}, paciente #{f.IdPaciente}, profesional #{idProfesional}: Pendiente -> Confirmado");
        tx.Commit();
        return new ResultadoSolicitud2 { IdTurno = idTurno, IdPaciente = f.IdPaciente };
    }

    /// <summary>El profesional rechaza la solicitud: queda Cancelado (el horario se libera) y el paciente recibe el aviso con el motivo, si lo hubo. Idempotente.</summary>
    public ResultadoSolicitud2 Rechazar(int idTurno, int idProfesional, string? motivo, AutorDelCierre autor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        var f = Leer(con, tx, idTurno);
        if (f is null || f.IdProfesional != idProfesional) return ResultadoSolicitud2.Rechazar(SolicitudesTurnoReglas.TurnoInexistente);
        if (f.Estado == SolicitudesTurnoReglas.EstadoCancelado && f.CreadoPor == SolicitudesTurnoReglas.CreadoPorPaciente)
            return new ResultadoSolicitud2 { Repetido = true, IdTurno = idTurno, IdPaciente = f.IdPaciente };
        if (f.Estado != SolicitudesTurnoReglas.EstadoPendiente || f.CreadoPor != SolicitudesTurnoReglas.CreadoPorPaciente) return ResultadoSolicitud2.Rechazar(SolicitudesTurnoReglas.NoEsSolicitud);
        var cancelado = IdEstado(con, tx, SolicitudesTurnoReglas.EstadoCancelado);
        var pendiente = IdEstado(con, tx, SolicitudesTurnoReglas.EstadoPendiente);

        using (var upd = new SqlCommand("UPDATE Turno SET IdEstadoTurno = @Nuevo WHERE IdTurno = @T AND IdEstadoTurno = @Antes", con, tx))
        {
            upd.Parameters.AddWithValue("@Nuevo", cancelado);
            upd.Parameters.AddWithValue("@T", idTurno);
            upd.Parameters.AddWithValue("@Antes", pendiente);
            if (upd.ExecuteNonQuery() != 1) return ResultadoSolicitud2.Rechazar(SolicitudesTurnoReglas.SolicitudCerrada);
        }
        AvisarAlPaciente(con, tx, f.IdPaciente, "Tu solicitud de turno no fue aceptada", SolicitudesTurnoReglas.TextoRechazo(f.Profesional, Momento(f.Inicio), motivo));
        ADComun.Auditar(con, tx, autor, "Solicitud de turno rechazada", "Turno", idTurno,
            $"Turno #{idTurno}, paciente #{f.IdPaciente}, profesional #{idProfesional}: Pendiente -> Cancelado" + (string.IsNullOrWhiteSpace(motivo) ? "" : ", con motivo"));
        tx.Commit();
        return new ResultadoSolicitud2 { IdTurno = idTurno, IdPaciente = f.IdPaciente };
    }

    // ───────────── Vencimiento ─────────────

    /// <summary>
    /// Vence las solicitudes cuyo plazo pasó: el turno queda Cancelado (el horario se libera) y se avisa al paciente (campanita) y al profesional. Idempotente: el estado se cambia solo si sigue
    /// Pendiente, y el aviso sale en la misma transacción, así que correrlo varias veces no duplica nada. Nunca lanza. Devuelve cuántas venció.
    /// </summary>
    public static int VencerSolicitudes(DateTime ahora)
    {
        try
        {
            if (!Disponible()) return 0;
            var ids = new List<int>();
            using (var con = Conexion.ObtenerConexion())
            {
                con.Open();
                using var cmd = new SqlCommand(@"
                    SELECT t.IdTurno FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno
                    WHERE et.Descripcion = N'Pendiente' AND t.CreadoPor = N'paciente' AND t.SolicitudExpiraEn IS NOT NULL AND t.SolicitudExpiraEn <= @Ahora", con);
                cmd.Parameters.AddWithValue("@Ahora", ahora);
                using var r = cmd.ExecuteReader();
                while (r.Read()) ids.Add((int)r[0]);
            }
            var vencidas = 0;
            foreach (var id in ids)
            {
                try { if (VencerUna(id, ahora)) vencidas++; }
                catch (SqlException) { /* se reintenta en la próxima pasada */ }
            }
            return vencidas;
        }
        catch (SqlException) { return 0; }
    }

    private static bool VencerUna(int idTurno, DateTime ahora)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        var f = Leer(con, tx, idTurno);
        if (f is null || f.Estado != SolicitudesTurnoReglas.EstadoPendiente || f.CreadoPor != SolicitudesTurnoReglas.CreadoPorPaciente) { tx.Rollback(); return false; }
        DateTime? expira;
        using (var cmd = new SqlCommand("SELECT SolicitudExpiraEn FROM Turno WHERE IdTurno = @T", con, tx))
        {
            cmd.Parameters.AddWithValue("@T", idTurno);
            expira = cmd.ExecuteScalar() as DateTime?;
        }
        if (expira is null || expira > ahora) { tx.Rollback(); return false; }
        var cancelado = IdEstado(con, tx, SolicitudesTurnoReglas.EstadoCancelado);
        var pendiente = IdEstado(con, tx, SolicitudesTurnoReglas.EstadoPendiente);
        var nombrePaciente = ADComun.NombrePaciente(con, tx, f.IdPaciente);

        using (var upd = new SqlCommand("UPDATE Turno SET IdEstadoTurno = @Nuevo WHERE IdTurno = @T AND IdEstadoTurno = @Antes", con, tx))
        {
            upd.Parameters.AddWithValue("@Nuevo", cancelado);
            upd.Parameters.AddWithValue("@T", idTurno);
            upd.Parameters.AddWithValue("@Antes", pendiente);
            if (upd.ExecuteNonQuery() != 1) { tx.Rollback(); return false; }
        }
        var momento = Momento(f.Inicio);
        AvisarAlPaciente(con, tx, f.IdPaciente, "Tu solicitud de turno venció", SolicitudesTurnoReglas.TextoVencidaPaciente(f.Profesional, momento));
        using (var cmd = new SqlCommand(@"INSERT INTO NotificacionInterna (Destinatario, IdProfesional, IdTurno, Tipo, Titulo, Mensaje) VALUES (N'Profesional', @P, @T, @Ti, @Tt, @M)", con, tx))
        {
            cmd.Parameters.AddWithValue("@P", f.IdProfesional);
            cmd.Parameters.AddWithValue("@T", idTurno);
            cmd.Parameters.AddWithValue("@Ti", SolicitudesTurnoReglas.TipoVencida);
            cmd.Parameters.AddWithValue("@Tt", "Una solicitud de turno venció");
            cmd.Parameters.AddWithValue("@M", SolicitudesTurnoReglas.TextoVencidaProfesional(nombrePaciente, momento));
            cmd.ExecuteNonQuery();
        }
        ADComun.Auditar(con, tx, new AutorDelCierre(null, "Sistema", null), "Solicitud de turno vencida", "Turno", idTurno,
            $"Turno #{idTurno}, paciente #{f.IdPaciente}, profesional #{f.IdProfesional}: Pendiente -> Cancelado por vencimiento");
        tx.Commit();
        return true;
    }
}
