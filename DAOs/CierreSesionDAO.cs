using Microsoft.Data.SqlClient;

/// <summary>Quién cierra, para la auditoría: solo ids y palabras fijas.</summary>
public record AutorDelCierre(int? IdUsuario, string? Espacio, string? Calidad);

/// <summary>
/// Lo que pasó con el cierre. Rechazo == null: se guardó (o ya estaba guardado: Repetido). Rechazo != null: no se escribió nada.
/// </summary>
public class ResultadoCierre
{
    public string? Rechazo { get; init; }
    public string? EstadoDelTurno { get; init; }       // para el mensaje de estado-no-permitido
    public int? IdSesionExistente { get; init; }       // para turno-con-sesion

    public bool Repetido { get; init; }
    public int IdSesion { get; init; }
    public DateTime FechaHora { get; init; }
    public string? TipoCierre { get; init; }
    public bool EmocionesGuardadas { get; init; }
    public bool TieneEmociones { get; init; }          // solo en un cierre repetido (lo que quedó guardado la primera vez)

    // El turno: null si el cierre no tiene turno vinculado.
    public bool HayTurno { get; init; }
    public bool TurnoCambio { get; init; }
    public string? EstadoAnterior { get; init; }
    public string? EstadoActual { get; init; }
    public string? MotivoTurno { get; init; }          // "ya-estaba" | "no-confirmado" (el profesional no lo pidió) | null

    // El caso del paciente: solo cambia con la primera sesión realizada (Asignado -> En atención). null = no cambió.
    public int? IdCasoEnAtencion { get; init; }

    public static ResultadoCierre Rechazar(string codigo, string? estado = null, int? idSesion = null) =>
        new() { Rechazo = codigo, EstadoDelTurno = estado, IdSesionExistente = idSesion };
}

/// <summary>
/// El cierre de sesión en UNA transacción: la sesión clínica, las emociones (si pasaron la guardia), el pase del turno a Realizado (solo si el profesional lo confirmó) y la
/// auditoría. Si cualquier parte falla, no queda nada a medias. Repetir el cierre con la misma clave devuelve lo que ya estaba, sin escribir ni auditar de nuevo.
/// </summary>
public class CierreSesionDAO
{
    private sealed record FilaTurno(int IdPaciente, int IdProfesional, int IdEstadoTurno, string? Estado, DateTime Fecha);

    /// <summary>¿La base tiene las columnas de la migración 26? Una vez que las tiene no se vuelve a preguntar.</summary>
    public bool Migracion26Disponible()
    {
        if (_existe) return true;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"SELECT CASE WHEN COL_LENGTH('dbo.SesionClinica', 'ClaveCierre') IS NOT NULL
                                                      AND COL_LENGTH('dbo.SesionClinica', 'TurnoRealizadoUtc') IS NOT NULL THEN 1 ELSE 0 END", con);
        return _existe = (int)cmd.ExecuteScalar()! == 1;
    }
    private static bool _existe;

    public ResultadoCierre Ejecutar(SesionClinica s, Guid clave, bool confirmaRealizacion, DateTime hoyEnLaClinica,
        Func<int, string?>? cifrarEmociones, AutorDelCierre autor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        try
        {
            return EjecutarEn(con, s, clave, confirmaRealizacion, hoyEnLaClinica, cifrarEmociones, autor);
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            // Dos pedidos con la misma clave llegaron a la vez y el índice único dejó pasar uno solo: el otro es un reintento.
            using var con2 = Conexion.ObtenerConexion();
            con2.Open();
            var previa = BuscarPorClave(con2, null, s.IdProfesional, clave);
            if (previa is null) throw;
            return Repetida(con2, null, previa);
        }
    }

    private ResultadoCierre EjecutarEn(SqlConnection con, SesionClinica s, Guid clave, bool confirma, DateTime hoy,
        Func<int, string?>? cifrar, AutorDelCierre autor)
    {
        using var tx = con.BeginTransaction();

        // 1) El turno, bloqueado hasta el final: dos cierres del mismo turno se ponen en fila (el segundo ve lo que dejó el primero).
        FilaTurno? turno = null;
        if (s.IdTurno is int idTurno)
        {
            turno = LeerTurnoBloqueado(con, tx, idTurno);
            if (turno is null) return ResultadoCierre.Rechazar(CierreSesionReglas.TurnoInexistente);
            if (turno.IdPaciente != s.IdPaciente || turno.IdProfesional != s.IdProfesional)
                return ResultadoCierre.Rechazar(CierreSesionReglas.TurnoNoCoincide);
        }

        // 2) Idempotencia: la misma clave del mismo profesional es el mismo cierre.
        var previa = BuscarPorClave(con, tx, s.IdProfesional, clave);
        if (previa is not null) return Repetida(con, tx, previa);

        // 3) Un turno tiene una sola sesión guardada.
        if (s.IdTurno is int idT)
        {
            var existente = SesionDelTurno(con, tx, idT);
            if (existente is not null) return ResultadoCierre.Rechazar(CierreSesionReglas.TurnoConSesion, turno!.Estado, existente);
        }

        // 4) ¿Se puede pasar el turno a Realizado? Solo si el profesional lo confirmó, solo desde Confirmado y solo si el turno ya ocurrió (o es de hoy).
        var cambia = false;
        int? idCaso = null;
        string? motivo = null;
        if (turno is not null && !confirma) motivo = "no-confirmado";
        if (turno is not null && confirma)
        {
            if (CierreSesionReglas.YaEstaRealizado(turno.Estado)) motivo = "ya-estaba";
            else if (!CierreSesionReglas.PuedePasarARealizado(turno.Estado)) return ResultadoCierre.Rechazar(CierreSesionReglas.EstadoNoPermitido, turno.Estado);
            else if (turno.Fecha.Date > hoy.Date) return ResultadoCierre.Rechazar(CierreSesionReglas.TurnoFuturo);
            else cambia = true;
        }
        int idRealizado = 0;
        if (cambia)
        {
            var id = IdEstado(con, tx, CierreSesionReglas.EstadoRealizado);
            if (id is null) return ResultadoCierre.Rechazar(CierreSesionReglas.CatalogoIncompleto);
            idRealizado = id.Value;
        }

        // 5) Todo lo que se escribe.
        s.ClaveCierre = clave;
        s.TurnoRealizadoUtc = cambia ? DateTime.UtcNow : null;
        var insertada = new SesionClinicaDAO().InsertarEn(con, tx, s, cifrar);
        var audit = new LogAuditoriaDAO();

        Auditar(audit, con, tx, autor, "Cierre de sesión clínica", "SesionClinica", insertada.IdSesion,
            $"SesionClinica #{insertada.IdSesion}, paciente #{s.IdPaciente}" + (s.IdTurno is int t1 ? $", turno #{t1}" : "")
            + (s.TipoCierre is null ? "" : $", cierre {s.TipoCierre}"));

        if (cambia)
        {
            using var upd = new SqlCommand(
                "UPDATE Turno SET IdEstadoTurno = @Nuevo WHERE IdTurno = @Id AND IdEstadoTurno = @Actual", con, tx);
            upd.Parameters.AddWithValue("@Nuevo", idRealizado);
            upd.Parameters.AddWithValue("@Id", s.IdTurno!.Value);
            upd.Parameters.AddWithValue("@Actual", turno!.IdEstadoTurno);
            if (upd.ExecuteNonQuery() != 1) return ResultadoCierre.Rechazar(CierreSesionReglas.EstadoNoPermitido, turno.Estado);   // no se llega: el turno está bloqueado

            Auditar(audit, con, tx, autor, "Turno marcado como Realizado", "Turno", s.IdTurno.Value,
                $"Turno #{s.IdTurno}: {turno.Estado} -> {CierreSesionReglas.EstadoRealizado}, sesión #{insertada.IdSesion}, paciente #{s.IdPaciente}, confirmado por el profesional en el cierre");

            // La primera sesión realizada con el profesional que tiene el caso asignado lo pasa a En atención. Es la única transición automática del caso.
            idCaso = PasarCasoAEnAtencion(con, tx, s.IdPaciente, s.IdProfesional, s.IdTurno.Value);
            if (idCaso is int c)
                Auditar(audit, con, tx, autor, "Caso pasa a En atención", "Caso", c,
                    $"Caso #{c}: Asignado -> En atención, primera sesión realizada #{insertada.IdSesion}, paciente #{s.IdPaciente}, turno #{s.IdTurno}");
        }

        tx.Commit();
        return new ResultadoCierre
        {
            IdSesion = insertada.IdSesion, FechaHora = s.FechaHora, TipoCierre = s.TipoCierre, EmocionesGuardadas = insertada.EmocionesGuardadas,
            HayTurno = turno is not null, TurnoCambio = cambia, EstadoAnterior = turno?.Estado,
            EstadoActual = cambia ? CierreSesionReglas.EstadoRealizado : turno?.Estado, MotivoTurno = motivo, IdCasoEnAtencion = idCaso
        };
    }

    // El caso abierto más reciente del paciente pasa de Asignado a En atención si está asignado a ESTE profesional y esta es la primera sesión realizada entre ellos. El UPDATE lleva
    // la condición de partida en el WHERE: repetirlo (o dos cierres a la vez) no lo vuelve a cambiar. Devuelve el id del caso si lo cambió, o null.
    private static int? PasarCasoAEnAtencion(SqlConnection con, SqlTransaction tx, int idPaciente, int idProfesional, int idTurnoActual)
    {
        int idCaso;
        using (var sel = new SqlCommand(@"
            SELECT TOP 1 IdCaso, Estado, IdProfesionalAsignado FROM Caso WITH (UPDLOCK, ROWLOCK)
            WHERE IdPaciente = @P AND Estado <> 'Cerrado' ORDER BY FechaCreacion DESC, IdCaso DESC", con, tx))
        {
            sel.Parameters.AddWithValue("@P", idPaciente);
            using var r = sel.ExecuteReader();
            if (!r.Read()) return null;
            if ((string)r["Estado"] != "Asignado" || r["IdProfesionalAsignado"] as int? != idProfesional) return null;
            idCaso = (int)r["IdCaso"];
        }

        // Primera sesión: ningún OTRO turno de este paciente con este profesional figura como Realizado (el de ahora ya se actualizó en esta transacción).
        using (var otra = new SqlCommand(@"
            SELECT COUNT(*) FROM Turno t JOIN EstadoTurno e ON e.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdPaciente = @P AND t.IdProfesional = @M AND e.Descripcion = @Real AND t.IdTurno <> @T", con, tx))
        {
            otra.Parameters.AddWithValue("@P", idPaciente);
            otra.Parameters.AddWithValue("@M", idProfesional);
            otra.Parameters.AddWithValue("@Real", CierreSesionReglas.EstadoRealizado);
            otra.Parameters.AddWithValue("@T", idTurnoActual);
            if ((int)otra.ExecuteScalar()! > 0) return null;
        }

        using var upd = new SqlCommand("UPDATE Caso SET Estado = 'En atención' WHERE IdCaso = @C AND Estado = 'Asignado'", con, tx);
        upd.Parameters.AddWithValue("@C", idCaso);
        return upd.ExecuteNonQuery() == 1 ? idCaso : null;
    }

    private static void Auditar(LogAuditoriaDAO dao, SqlConnection con, SqlTransaction tx, AutorDelCierre autor, string accion, string entidad, int idEntidad, string detalle) =>
        dao.Insertar(new LogAuditoria
        {
            IdUsuario = autor.IdUsuario, Accion = accion, Entidad = entidad, IdEntidad = idEntidad,
            Detalle = CierreSesionReglas.Detalle(detalle + (autor.Calidad is null ? "" : $", como {autor.Calidad}")),
            Categoria = CategoriaAuditoria.Clinica, Espacio = autor.Espacio
        }, con, tx);

    private static FilaTurno? LeerTurnoBloqueado(SqlConnection con, SqlTransaction tx, int idTurno)
    {
        using var cmd = new SqlCommand(@"
            SELECT t.IdPaciente, t.IdProfesional, t.IdEstadoTurno, e.Descripcion, t.Fecha
            FROM Turno t WITH (UPDLOCK, HOLDLOCK)
            LEFT JOIN EstadoTurno e ON e.IdEstadoTurno = t.IdEstadoTurno
            WHERE t.IdTurno = @Id", con, tx);
        cmd.Parameters.AddWithValue("@Id", idTurno);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new FilaTurno((int)r["IdPaciente"], (int)r["IdProfesional"], (int)r["IdEstadoTurno"], r["Descripcion"] as string, (DateTime)r["Fecha"])
            : null;
    }

    private static int? IdEstado(SqlConnection con, SqlTransaction tx, string descripcion)
    {
        using var cmd = new SqlCommand("SELECT TOP 1 IdEstadoTurno FROM EstadoTurno WHERE Descripcion = @D ORDER BY IdEstadoTurno", con, tx);
        cmd.Parameters.AddWithValue("@D", descripcion);
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? null : (int)r;
    }

    private static int? SesionDelTurno(SqlConnection con, SqlTransaction tx, int idTurno)
    {
        using var cmd = new SqlCommand("SELECT TOP 1 IdSesion FROM SesionClinica WHERE IdTurno = @T ORDER BY IdSesion", con, tx);
        cmd.Parameters.AddWithValue("@T", idTurno);
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? null : (int)r;
    }

    private sealed record SesionPrevia(int IdSesion, int? IdTurno, int IdPaciente, DateTime FechaHora, string? TipoCierre, bool TieneEmociones);

    private static SesionPrevia? BuscarPorClave(SqlConnection con, SqlTransaction? tx, int idProfesional, Guid clave)
    {
        using var cmd = new SqlCommand(@"
            SELECT IdSesion, IdTurno, IdPaciente, FechaHora, TipoCierre, CASE WHEN HistorialEmociones IS NULL THEN 0 ELSE 1 END AS TieneEmociones
            FROM SesionClinica WHERE IdProfesional = @P AND ClaveCierre = @C", con, tx);
        cmd.Parameters.AddWithValue("@P", idProfesional);
        cmd.Parameters.AddWithValue("@C", clave);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new SesionPrevia((int)r["IdSesion"], r["IdTurno"] as int?, (int)r["IdPaciente"], (DateTime)r["FechaHora"], r["TipoCierre"] as string, (int)r["TieneEmociones"] == 1)
            : null;
    }

    // El cierre ya estaba guardado: se devuelve lo que quedó, con el estado ACTUAL del turno. No se escribe ni se audita nada.
    private static ResultadoCierre Repetida(SqlConnection con, SqlTransaction? tx, SesionPrevia p)
    {
        string? estado = null;
        if (p.IdTurno is int idT)
        {
            using var cmd = new SqlCommand("SELECT e.Descripcion FROM Turno t LEFT JOIN EstadoTurno e ON e.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @T", con, tx);
            cmd.Parameters.AddWithValue("@T", idT);
            estado = cmd.ExecuteScalar() as string;
        }
        return new ResultadoCierre
        {
            Repetido = true, IdSesion = p.IdSesion, FechaHora = p.FechaHora, TipoCierre = p.TipoCierre, TieneEmociones = p.TieneEmociones,
            HayTurno = p.IdTurno is not null, TurnoCambio = false, EstadoAnterior = estado, EstadoActual = estado, MotivoTurno = "ya-estaba"
        };
    }
}
