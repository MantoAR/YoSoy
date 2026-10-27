using Microsoft.Data.SqlClient;

public record DestinoDerivacion(int IdProfesional, string Nombre, string Especialidad, int CasosActivos);

public record DerivacionFila(int IdDerivacion, int IdPaciente, string NombrePaciente, int IdProfesionalOrigen, string NombreOrigen, int IdProfesionalDestino, string NombreDestino,
    int? IdCaso, DateTime Fecha, string Estado, string? Motivo, string? Observaciones, int? IdSesion);

public record AvisoClinico(int IdAviso, string Tipo, string Titulo, string Mensaje, bool Leida, DateTime Fecha);

/// <summary>
/// La derivación INTERNA asistida (Derivacion con Origen 'Cierre'): un profesional deriva el caso de un paciente a otro; el destino acepta o rechaza; si acepta, Admisión (CRM) confirma el traspaso
/// con la reasignación de casos de siempre. Nada cambia el vínculo paciente-profesional hasta ese último paso, que ejecuta una persona. Cada cambio de estado: una transacción, auditado, con la
/// condición de partida y los avisos (campanita) en la misma transacción. Los mails salen después, aparte, sin datos del paciente.
/// </summary>
public class DerivacionCierreDAO
{
    private const string Select = @"
        SELECT d.IdDerivacion, d.IdPaciente, pac.Nombre AS NombrePaciente, d.IdProfesionalOrigen, po.Nombre + ISNULL(' ' + po.Apellido, '') AS NombreOrigen,
               d.IdProfesionalDestino, pd.Nombre + ISNULL(' ' + pd.Apellido, '') AS NombreDestino, d.IdCaso, d.Fecha, d.Estado, d.Motivo, d.Observaciones, d.IdSesion
        FROM Derivacion d
        JOIN Paciente pac ON pac.IdPaciente = d.IdPaciente
        JOIN Profesional po ON po.IdProfesional = d.IdProfesionalOrigen
        JOIN Profesional pd ON pd.IdProfesional = d.IdProfesionalDestino
        WHERE d.Origen = N'Cierre'";

    private static DerivacionFila Mapear(SqlDataReader r) => new((int)r["IdDerivacion"], (int)r["IdPaciente"], (string)r["NombrePaciente"], (int)r["IdProfesionalOrigen"], (string)r["NombreOrigen"],
        (int)r["IdProfesionalDestino"], (string)r["NombreDestino"], r["IdCaso"] as int?, (DateTime)r["Fecha"], (string)r["Estado"], r["Motivo"] as string, r["Observaciones"] as string, r["IdSesion"] as int?);

    /// <summary>¿Esta derivación nació del cierre de una sesión (Origen 'Cierre')? Sin la migración 27 ninguna lo es.</summary>
    public static bool EsDelCierre(int idDerivacion)
    {
        if (!ADMigracion.Disponible()) return false;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM Derivacion WHERE IdDerivacion = @I AND Origen = N'Cierre'", con);
        cmd.Parameters.AddWithValue("@I", idDerivacion);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    public DerivacionFila? Obtener(int idDerivacion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        return Leer(con, null, idDerivacion, false);
    }

    private static DerivacionFila? Leer(SqlConnection con, SqlTransaction? tx, int idDerivacion, bool bloquear)
    {
        using var cmd = new SqlCommand(bloquear
            ? Select.Replace("FROM Derivacion d", "FROM Derivacion d WITH (UPDLOCK, HOLDLOCK)") + " AND d.IdDerivacion = @I"
            : Select + " AND d.IdDerivacion = @I", con, tx);
        cmd.Parameters.AddWithValue("@I", idDerivacion);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    // ── Crear ──────────────────────────────────────────────────────────────────────────────────────────

    public (ResultadoAltaDerivacion R, List<MailNuevo> Mails) Crear(int idCaso, int idProfesional, DerivarCasoRequest r, DateTime hoy, AutorDelCierre autor)
    {
        var mails = new List<MailNuevo>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        try
        {
            using var tx = con.BeginTransaction();
            var caso = ADComun.CasoBloqueado(con, tx, idCaso);
            if (caso is null) return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.CasoInexistente), mails);
            if (caso.IdProfesionalAsignado != idProfesional) return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.CasoAjeno), mails);

            int destino = r.IdProfesionalDestino!.Value;

            // Idempotente: la misma derivación (mismo caso, mismo origen y mismo destino) en curso se devuelve tal cual; una en curso a OTRO destino se rechaza.
            using (var cmd = new SqlCommand(@"SELECT IdDerivacion, IdProfesionalOrigen, IdProfesionalDestino, Estado, IdSesion FROM Derivacion WITH (UPDLOCK, HOLDLOCK)
                                              WHERE IdCaso = @C AND Origen = N'Cierre' AND Estado IN (@E1, @E2)", con, tx))
            {
                cmd.Parameters.AddWithValue("@C", idCaso);
                cmd.Parameters.AddWithValue("@E1", AltaDerivacionReglas.DerivacionPendienteDelDestino);
                cmd.Parameters.AddWithValue("@E2", AltaDerivacionReglas.DerivacionAceptadaPorElDestino);
                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                {
                    if ((int)rd["IdProfesionalOrigen"] == idProfesional && (int)rd["IdProfesionalDestino"] == destino)
                        return (new ResultadoAltaDerivacion { Repetido = true, Id = (int)rd["IdDerivacion"], IdCaso = idCaso, IdPaciente = caso.IdPaciente, IdSesion = rd["IdSesion"] as int?,
                                                              EstadoActual = (string)rd["Estado"], IdProfesionalDestino = destino }, mails);
                    return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.DerivacionEnCurso), mails);
                }
            }

            if (!AltaDerivacionReglas.CasoSePuedeDerivarODarDeAlta(caso.Estado))
                return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.EstadoNoPermitido, caso.Estado), mails);

            using (var cmd = new SqlCommand("SELECT Estado FROM Profesional WHERE IdProfesional = @P", con, tx))
            {
                cmd.Parameters.AddWithValue("@P", destino);
                if (cmd.ExecuteScalar() as string != "Activo") return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.DestinoNoDisponible), mails);
            }

            var (idSesion, rechazoSesion) = ADComun.Sesion(con, tx, r.IdSesion, r.ClaveCierre, idProfesional, caso.IdPaciente, CierreSesionReglas.Derivacion);
            if (rechazoSesion is not null) return (ResultadoAltaDerivacion.Rechazar(rechazoSesion), mails);

            int id;
            using (var ins = new SqlCommand(@"
                INSERT INTO Derivacion (IdPaciente, IdProfesionalOrigen, IdProfesionalDestino, Fecha, Motivo, Estado, Origen, IdCaso, Observaciones, IdSesion)
                OUTPUT INSERTED.IdDerivacion
                VALUES (@P, @O, @D, @F, @M, @E, N'Cierre', @C, @Ob, @S)", con, tx))
            {
                ins.Parameters.AddWithValue("@P", caso.IdPaciente);
                ins.Parameters.AddWithValue("@O", idProfesional);
                ins.Parameters.AddWithValue("@D", destino);
                ins.Parameters.AddWithValue("@F", hoy.Date);
                ins.Parameters.AddWithValue("@M", r.Motivo!.Trim());
                ins.Parameters.AddWithValue("@E", AltaDerivacionReglas.DerivacionPendienteDelDestino);
                ins.Parameters.AddWithValue("@C", idCaso);
                ins.Parameters.AddWithValue("@Ob", string.IsNullOrWhiteSpace(r.Observaciones) ? DBNull.Value : r.Observaciones.Trim());
                ins.Parameters.AddWithValue("@S", (object?)idSesion ?? DBNull.Value);
                id = (int)ins.ExecuteScalar()!;
            }

            ADComun.Auditar(con, tx, autor, "Derivación interna creada", "Derivacion", id,
                $"Derivacion #{id}, caso #{idCaso}, paciente #{caso.IdPaciente}, de profesional #{idProfesional} a profesional #{destino}" + (idSesion is int s ? $", sesión #{s}" : ""));

            // Los tres avisos de la campanita, en la misma transacción: destino (para responder), origen (constancia) y Admisión.
            var nombreOrigen = ADComun.NombreProfesional(con, tx, idProfesional);
            var nombreDestino = ADComun.NombreProfesional(con, tx, destino);
            var nombrePaciente = ADComun.NombrePaciente(con, tx, caso.IdPaciente);
            ADComun.Avisar(con, tx, new("Profesional", destino, "derivacion-pendiente", "Derivación pendiente de tu respuesta",
                $"{nombreOrigen} te derivó el caso de {nombrePaciente}. Leé el informe y aceptá o rechazá la derivación."));
            ADComun.Avisar(con, tx, new("Profesional", idProfesional, "derivacion-enviada", "Derivación enviada",
                $"Derivaste el caso de {nombrePaciente} a {nombreDestino}. Te avisamos cuando responda."));
            ADComun.Avisar(con, tx, new("Administrador", null, "derivacion-aviso", "Derivación interna iniciada",
                $"{nombreOrigen} derivó el caso de {nombrePaciente} a {nombreDestino}. Queda esperando la respuesta del profesional de destino."));

            foreach (var p in new[] { destino, idProfesional })
                if (ADComun.MailProfesional(con, tx, p) is string mail)
                    mails.Add(new MailNuevo(mail, "YoSoy: tenés un aviso sobre una derivación", "Tenés un aviso nuevo sobre la derivación de un caso. Ingresá a tu panel de YoSoy para verlo.\n\nEste mail no incluye datos del paciente."));

            tx.Commit();
            return (new ResultadoAltaDerivacion { Id = id, IdCaso = idCaso, IdPaciente = caso.IdPaciente, IdSesion = idSesion, EstadoActual = AltaDerivacionReglas.DerivacionPendienteDelDestino,
                                                  IdProfesionalDestino = destino, Fecha = hoy.Date }, mails);
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            // Dos pedidos a la vez: el índice único dejó pasar uno solo. Es una derivación en curso.
            return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.DerivacionEnCurso), mails);
        }
    }

    // ── Responder (el profesional de destino) ──────────────────────────────────────────────────────────

    public (ResultadoAltaDerivacion R, List<MailNuevo> Mails) Responder(int idDerivacion, int idProfesional, bool acepta, string? motivo, AutorDelCierre autor)
    {
        var mails = new List<MailNuevo>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        var d = Leer(con, tx, idDerivacion, true);
        // Una derivación ajena se ve igual que una inexistente.
        if (d is null || d.IdProfesionalDestino != idProfesional) return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.DerivacionInexistente), mails);

        var nuevo = acepta ? AltaDerivacionReglas.DerivacionAceptadaPorElDestino : AltaDerivacionReglas.DerivacionRechazadaPorElDestino;
        if (d.Estado == nuevo) return (new ResultadoAltaDerivacion { Repetido = true, Id = idDerivacion, IdPaciente = d.IdPaciente, IdCaso = d.IdCaso ?? 0, EstadoActual = d.Estado }, mails);
        if (d.Estado is AltaDerivacionReglas.DerivacionAceptadaPorElDestino or AltaDerivacionReglas.DerivacionRechazadaPorElDestino)
            return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.YaRespondida, d.Estado), mails);
        if (d.Estado != AltaDerivacionReglas.DerivacionPendienteDelDestino)
            return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.EstadoDeDerivacion, d.Estado), mails);

        using (var upd = new SqlCommand(@"UPDATE Derivacion SET Estado = @N, RespuestaUtc = SYSUTCDATETIME(), IdUsuarioRespuesta = @U
                                          WHERE IdDerivacion = @I AND Estado = @Antes", con, tx))
        {
            upd.Parameters.AddWithValue("@N", nuevo);
            upd.Parameters.AddWithValue("@U", (object?)autor.IdUsuario ?? DBNull.Value);
            upd.Parameters.AddWithValue("@I", idDerivacion);
            upd.Parameters.AddWithValue("@Antes", AltaDerivacionReglas.DerivacionPendienteDelDestino);
            if (upd.ExecuteNonQuery() != 1) return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.YaRespondida, d.Estado), mails);
        }

        ADComun.Auditar(con, tx, autor, acepta ? "Derivación aceptada por el destino" : "Derivación rechazada por el destino", "Derivacion", idDerivacion,
            $"Derivacion #{idDerivacion}, caso #{d.IdCaso}, paciente #{d.IdPaciente}: {d.Estado} -> {nuevo}");

        var razon = string.IsNullOrWhiteSpace(motivo) ? "" : $" Motivo: {(motivo.Trim().Length > 300 ? motivo.Trim()[..300] : motivo.Trim())}";
        if (acepta)
        {
            ADComun.Avisar(con, tx, new("Administrador", null, "traspaso-pendiente", "Confirmar traspaso",
                $"Confirmar traspaso de {d.NombrePaciente} de {d.NombreOrigen} a {d.NombreDestino}: {d.NombreDestino} aceptó la derivación. El vínculo no cambia hasta que lo confirmes."));
            ADComun.Avisar(con, tx, new("Profesional", d.IdProfesionalOrigen, "derivacion-aceptada", "Derivación aceptada",
                $"{d.NombreDestino} aceptó la derivación de {d.NombrePaciente}. Admisión va a confirmar el traspaso: hasta entonces el caso sigue a tu cargo."));
        }
        else
        {
            ADComun.Avisar(con, tx, new("Administrador", null, "derivacion-rechazada", "Derivación rechazada: hay que decidir",
                $"{d.NombreDestino} rechazó la derivación de {d.NombrePaciente} (de {d.NombreOrigen}).{razon} Decidí cómo seguir: el caso sigue a cargo de {d.NombreOrigen}."));
            ADComun.Avisar(con, tx, new("Profesional", d.IdProfesionalOrigen, "derivacion-rechazada", "Derivación rechazada",
                $"{d.NombreDestino} rechazó la derivación de {d.NombrePaciente}.{razon} El caso sigue a tu cargo y Admisión va a decidir cómo seguir."));
        }
        if (ADComun.MailProfesional(con, tx, d.IdProfesionalOrigen) is string mail)
            mails.Add(new MailNuevo(mail, "YoSoy: tenés un aviso sobre una derivación", "Tenés un aviso nuevo sobre la derivación de un caso. Ingresá a tu panel de YoSoy para verlo.\n\nEste mail no incluye datos del paciente."));

        tx.Commit();
        return (new ResultadoAltaDerivacion { Id = idDerivacion, IdPaciente = d.IdPaciente, IdCaso = d.IdCaso ?? 0, EstadoAnterior = d.Estado, EstadoActual = nuevo }, mails);
    }

    // ── Traspaso y resolución (Admisión / CRM) ────────────────────────────────────────────────────────

    /// <summary>La derivación que se va a traspasar o resolver (con sus nombres), sin bloquear: el endpoint la mira antes de llamar a la reasignación de casos.</summary>
    public DerivacionFila? Para(int idDerivacion) => Obtener(idDerivacion);

    /// <summary>Marca la derivación como Traspasada (después de que la reasignación de casos ya movió el caso) y avisa a los dos profesionales. Idempotente.</summary>
    public (ResultadoAltaDerivacion R, List<MailNuevo> Mails) CerrarTraspaso(int idDerivacion, AutorDelCierre autor)
    {
        var mails = new List<MailNuevo>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        var d = Leer(con, tx, idDerivacion, true);
        if (d is null) return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.DerivacionInexistente), mails);
        if (d.Estado == AltaDerivacionReglas.DerivacionTraspasada) return (new ResultadoAltaDerivacion { Repetido = true, Id = idDerivacion, EstadoActual = d.Estado, IdCaso = d.IdCaso ?? 0, IdPaciente = d.IdPaciente }, mails);
        if (d.Estado != AltaDerivacionReglas.DerivacionAceptadaPorElDestino) return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.EstadoDeDerivacion, d.Estado), mails);

        using (var upd = new SqlCommand(@"UPDATE Derivacion SET Estado = @N, ResolucionUtc = SYSUTCDATETIME(), IdUsuarioResolucion = @U WHERE IdDerivacion = @I AND Estado = @Antes", con, tx))
        {
            upd.Parameters.AddWithValue("@N", AltaDerivacionReglas.DerivacionTraspasada);
            upd.Parameters.AddWithValue("@U", (object?)autor.IdUsuario ?? DBNull.Value);
            upd.Parameters.AddWithValue("@I", idDerivacion);
            upd.Parameters.AddWithValue("@Antes", AltaDerivacionReglas.DerivacionAceptadaPorElDestino);
            if (upd.ExecuteNonQuery() != 1) return (ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.EstadoDeDerivacion, d.Estado), mails);
        }
        ADComun.Auditar(con, tx, autor, "Traspaso confirmado", "Derivacion", idDerivacion,
            $"Derivacion #{idDerivacion}, caso #{d.IdCaso}, paciente #{d.IdPaciente}: traspaso de profesional #{d.IdProfesionalOrigen} a profesional #{d.IdProfesionalDestino}");
        ADComun.Avisar(con, tx, new("Profesional", d.IdProfesionalDestino, "traspaso-confirmado", "Traspaso confirmado",
            $"Admisión confirmó el traspaso: ahora {d.NombrePaciente} es tu paciente. Ya figura entre tus casos."));
        ADComun.Avisar(con, tx, new("Profesional", d.IdProfesionalOrigen, "traspaso-confirmado", "Traspaso confirmado",
            $"Admisión confirmó el traspaso de {d.NombrePaciente} a {d.NombreDestino}. El caso ya no figura entre los tuyos."));
        foreach (var p in new[] { d.IdProfesionalDestino, d.IdProfesionalOrigen })
            if (ADComun.MailProfesional(con, tx, p) is string mail)
                mails.Add(new MailNuevo(mail, "YoSoy: tenés un aviso sobre una derivación", "Tenés un aviso nuevo sobre la derivación de un caso. Ingresá a tu panel de YoSoy para verlo.\n\nEste mail no incluye datos del paciente."));
        tx.Commit();
        return (new ResultadoAltaDerivacion { Id = idDerivacion, IdCaso = d.IdCaso ?? 0, IdPaciente = d.IdPaciente, EstadoAnterior = AltaDerivacionReglas.DerivacionAceptadaPorElDestino, EstadoActual = AltaDerivacionReglas.DerivacionTraspasada }, mails);
    }

    /// <summary>Admisión da por resuelta una derivación que el destino rechazó (decidió cómo seguir). El caso sigue con quien lo tenía. Idempotente.</summary>
    public ResultadoAltaDerivacion Resolver(int idDerivacion, AutorDelCierre autor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        var d = Leer(con, tx, idDerivacion, true);
        if (d is null) return ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.DerivacionInexistente);
        if (d.Estado == AltaDerivacionReglas.DerivacionResueltaPorAdmision) return new ResultadoAltaDerivacion { Repetido = true, Id = idDerivacion, EstadoActual = d.Estado };
        if (d.Estado != AltaDerivacionReglas.DerivacionRechazadaPorElDestino) return ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.EstadoDeDerivacion, d.Estado);
        using (var upd = new SqlCommand(@"UPDATE Derivacion SET Estado = @N, ResolucionUtc = SYSUTCDATETIME(), IdUsuarioResolucion = @U WHERE IdDerivacion = @I AND Estado = @Antes", con, tx))
        {
            upd.Parameters.AddWithValue("@N", AltaDerivacionReglas.DerivacionResueltaPorAdmision);
            upd.Parameters.AddWithValue("@U", (object?)autor.IdUsuario ?? DBNull.Value);
            upd.Parameters.AddWithValue("@I", idDerivacion);
            upd.Parameters.AddWithValue("@Antes", AltaDerivacionReglas.DerivacionRechazadaPorElDestino);
            if (upd.ExecuteNonQuery() != 1) return ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.EstadoDeDerivacion, d.Estado);
        }
        ADComun.Auditar(con, tx, autor, "Derivación rechazada resuelta por Admisión", "Derivacion", idDerivacion,
            $"Derivacion #{idDerivacion}, caso #{d.IdCaso}, paciente #{d.IdPaciente}: {d.Estado} -> {AltaDerivacionReglas.DerivacionResueltaPorAdmision}");
        tx.Commit();
        return new ResultadoAltaDerivacion { Id = idDerivacion, EstadoAnterior = d.Estado, EstadoActual = AltaDerivacionReglas.DerivacionResueltaPorAdmision };
    }

    // ── Lecturas ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>La cola de Admisión: derivaciones del cierre aceptadas (para confirmar el traspaso) y rechazadas (para decidir).</summary>
    public List<DerivacionFila> TraspasosPendientes()
    {
        var lista = new List<DerivacionFila>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(Select + " AND d.Estado IN (@A, @R) ORDER BY d.IdDerivacion DESC", con);
        cmd.Parameters.AddWithValue("@A", AltaDerivacionReglas.DerivacionAceptadaPorElDestino);
        cmd.Parameters.AddWithValue("@R", AltaDerivacionReglas.DerivacionRechazadaPorElDestino);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r));
        return lista;
    }

    /// <summary>Lo que el profesional ve en la campanita: sus avisos clínicos y las derivaciones que le tocan responder (con el informe: motivo y observaciones, nada más de la historia).</summary>
    public (List<AvisoClinico> Avisos, List<DerivacionFila> PorResponder) AvisosDe(int idProfesional)
    {
        var avisos = new List<AvisoClinico>();
        var porResponder = new List<DerivacionFila>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using (var cmd = new SqlCommand(@"
            SELECT TOP 30 IdNotificacionInterna, Tipo, Titulo, Mensaje, Leida, FechaCreacion FROM NotificacionInterna
            WHERE Destinatario = N'Profesional' AND IdProfesional = @P AND (Tipo LIKE N'derivacion-%' OR Tipo LIKE N'traspaso-%' OR Tipo LIKE N'consentimiento-%' OR Tipo LIKE N'asignacion-%')
            ORDER BY IdNotificacionInterna DESC", con))
        {
            cmd.Parameters.AddWithValue("@P", idProfesional);
            using var r = cmd.ExecuteReader();
            while (r.Read()) avisos.Add(new AvisoClinico((int)r["IdNotificacionInterna"], (string)r["Tipo"], (string)r["Titulo"], (string)r["Mensaje"], (bool)r["Leida"], (DateTime)r["FechaCreacion"]));
        }
        using (var cmd = new SqlCommand(Select + " AND d.IdProfesionalDestino = @P AND d.Estado = @E ORDER BY d.IdDerivacion DESC", con))
        {
            cmd.Parameters.AddWithValue("@P", idProfesional);
            cmd.Parameters.AddWithValue("@E", AltaDerivacionReglas.DerivacionPendienteDelDestino);
            using var r = cmd.ExecuteReader();
            while (r.Read()) porResponder.Add(Mapear(r));
        }
        return (avisos, porResponder);
    }

    public bool MarcarAvisoLeido(int idAviso, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"UPDATE NotificacionInterna SET Leida = 1 WHERE IdNotificacionInterna = @I AND Destinatario = N'Profesional' AND IdProfesional = @P
                                         AND (Tipo LIKE N'derivacion-%' OR Tipo LIKE N'traspaso-%' OR Tipo LIKE N'consentimiento-%')", con);
        cmd.Parameters.AddWithValue("@I", idAviso);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        return cmd.ExecuteNonQuery() == 1;
    }

    /// <summary>Profesionales activos a los que se puede derivar (sin el que deriva), con su especialidad y su carga de casos asignados o en atención.</summary>
    public List<DestinoDerivacion> Destinos(int idProfesionalExcluido)
    {
        var lista = new List<DestinoDerivacion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT p.IdProfesional, p.Nombre + ISNULL(' ' + p.Apellido, '') AS Nombre, e.Nombre AS Especialidad,
                   (SELECT COUNT(*) FROM Caso c WHERE c.IdProfesionalAsignado = p.IdProfesional AND c.Estado IN (N'Asignado', N'En atención')) AS CasosActivos
            FROM Profesional p JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
            WHERE p.Estado = 'Activo' AND p.IdProfesional <> @P
            ORDER BY p.Nombre, p.Apellido", con);
        cmd.Parameters.AddWithValue("@P", idProfesionalExcluido);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(new DestinoDerivacion((int)r["IdProfesional"], (string)r["Nombre"], (string)r["Especialidad"], (int)r["CasosActivos"]));
        return lista;
    }
}
