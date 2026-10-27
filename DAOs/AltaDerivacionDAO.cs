using Microsoft.Data.SqlClient;

public record ResultadoAltaDerivacion
{
    public string? Rechazo { get; init; }
    public string? Estado { get; init; }          // para el mensaje de estado-no-permitido / estado-de-derivacion
    public bool Repetido { get; init; }
    public int Id { get; init; }                  // IdAltaClinica o IdDerivacion
    public int IdCaso { get; init; }
    public int IdPaciente { get; init; }
    public int? IdSesion { get; init; }
    public string? EstadoAnterior { get; init; }
    public string? EstadoActual { get; init; }
    public DateTime? Fecha { get; init; }
    public AltaDerivacionReglas.PlanDeControl? Plan { get; init; }
    public int? IdProfesionalDestino { get; init; }

    public static ResultadoAltaDerivacion Rechazar(string codigo, string? estado = null) => new() { Rechazo = codigo, Estado = estado };
}

public record AvisoNuevo(string Destinatario, int? IdProfesional, string Tipo, string Titulo, string Mensaje);

/// <summary>Un mail que sale DESPUÉS de confirmar la transacción (sin datos clínicos ni nombres: solo avisa que hay algo en el panel).</summary>
public record MailNuevo(string Destinatario, string Asunto, string Texto);

internal static class ADComun
{
    public static void Auditar(SqlConnection con, SqlTransaction tx, AutorDelCierre autor, string accion, string entidad, int idEntidad, string detalle) =>
        new LogAuditoriaDAO().Insertar(new LogAuditoria
        {
            IdUsuario = autor.IdUsuario, Accion = accion, Entidad = entidad, IdEntidad = idEntidad,
            Detalle = AltaDerivacionReglas.Detalle(detalle + (autor.Calidad is null ? "" : $", como {autor.Calidad}")),
            Categoria = CategoriaAuditoria.Clinica, Espacio = autor.Espacio
        }, con, tx);

    public static void Avisar(SqlConnection con, SqlTransaction tx, AvisoNuevo a)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO NotificacionInterna (Destinatario, IdProfesional, Tipo, Titulo, Mensaje)
            VALUES (@D, @P, @T, @Ti, @M)", con, tx);
        cmd.Parameters.AddWithValue("@D", a.Destinatario);
        cmd.Parameters.AddWithValue("@P", (object?)a.IdProfesional ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@T", a.Tipo);
        cmd.Parameters.AddWithValue("@Ti", a.Titulo.Length > 200 ? a.Titulo[..200] : a.Titulo);
        cmd.Parameters.AddWithValue("@M", a.Mensaje.Length > 1000 ? a.Mensaje[..1000] : a.Mensaje);
        cmd.ExecuteNonQuery();
    }

    public static string NombreProfesional(SqlConnection con, SqlTransaction tx, int id)
    {
        using var cmd = new SqlCommand("SELECT Nombre + ISNULL(' ' + Apellido, '') FROM Profesional WHERE IdProfesional = @I", con, tx);
        cmd.Parameters.AddWithValue("@I", id);
        return cmd.ExecuteScalar() as string ?? $"profesional #{id}";
    }

    public static string NombrePaciente(SqlConnection con, SqlTransaction tx, int id)
    {
        using var cmd = new SqlCommand("SELECT Nombre FROM Paciente WHERE IdPaciente = @I", con, tx);
        cmd.Parameters.AddWithValue("@I", id);
        return cmd.ExecuteScalar() as string ?? $"paciente #{id}";
    }

    public static string? MailProfesional(SqlConnection con, SqlTransaction tx, int id)
    {
        using var cmd = new SqlCommand("SELECT Mail FROM Profesional WHERE IdProfesional = @I", con, tx);
        cmd.Parameters.AddWithValue("@I", id);
        return cmd.ExecuteScalar() as string is { Length: > 0 } m ? m : null;
    }

    /// <summary>La sesión del cierre a la que se enlaza el alta o la derivación: del mismo profesional y paciente, y cerrada con el tipo que corresponde. null = no se pidió ninguna.</summary>
    public static (int? IdSesion, string? Rechazo) Sesion(SqlConnection con, SqlTransaction tx, int? idSesion, string? claveCierre, int idProfesional, int idPaciente, string tipoEsperado)
    {
        if (idSesion is null && string.IsNullOrWhiteSpace(claveCierre)) return (null, null);
        string sql = "SELECT IdSesion, IdPaciente, IdProfesional, TipoCierre FROM SesionClinica WHERE ";
        using var cmd = new SqlCommand(sql + (idSesion is not null ? "IdSesion = @S" : "IdProfesional = @P AND ClaveCierre = @K"), con, tx);
        if (idSesion is not null) cmd.Parameters.AddWithValue("@S", idSesion.Value);
        else
        {
            if (!Guid.TryParse(claveCierre, out var k)) return (null, AltaDerivacionReglas.SesionNoCoincide);
            cmd.Parameters.AddWithValue("@P", idProfesional);
            cmd.Parameters.AddWithValue("@K", k);
        }
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, AltaDerivacionReglas.SesionNoCoincide);
        if ((int)r["IdProfesional"] != idProfesional || (int)r["IdPaciente"] != idPaciente) return (null, AltaDerivacionReglas.SesionNoCoincide);
        if (!string.Equals(r["TipoCierre"] as string, tipoEsperado, StringComparison.Ordinal)) return (null, AltaDerivacionReglas.SesionNoEsDelTipo);
        return ((int)r["IdSesion"], null);
    }

    public record FilaCaso(int IdCaso, int IdPaciente, string Estado, int? IdProfesionalAsignado);

    public static FilaCaso? CasoBloqueado(SqlConnection con, SqlTransaction tx, int idCaso)
    {
        using var cmd = new SqlCommand("SELECT IdCaso, IdPaciente, Estado, IdProfesionalAsignado FROM Caso WITH (UPDLOCK, HOLDLOCK) WHERE IdCaso = @C", con, tx);
        cmd.Parameters.AddWithValue("@C", idCaso);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new FilaCaso((int)r["IdCaso"], (int)r["IdPaciente"], (string)r["Estado"], r["IdProfesionalAsignado"] as int?) : null;
    }
}

/// <summary>
/// El alta CLÍNICA (distinta del alta administrativa de una ficha, PUT /pacientes/{id}/alta, que solo reactiva la ficha). En una sola transacción: guarda el informe y el plan de control, pasa el caso a
/// «En alta» y deja la auditoría. NO inactiva al paciente ni a su usuario, ni toca sus consentimientos, ni sus vínculos. El plan de control se GUARDA, no se programa.
/// </summary>
public class AltaClinicaDAO
{
    public bool Migracion27Disponible() => ADMigracion.Disponible();

    public ResultadoAltaDerivacion Confirmar(int idCaso, int idProfesional, AltaClinicaRequest r, DateTime fechaAlta, AltaDerivacionReglas.PlanDeControl plan, AutorDelCierre autor, AltaExtension? ext = null)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        try
        {
            using var tx = con.BeginTransaction();

            var caso = ADComun.CasoBloqueado(con, tx, idCaso);
            if (caso is null) return ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.CasoInexistente);
            if (caso.IdProfesionalAsignado != idProfesional) return ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.CasoAjeno);

            // Idempotente: si el alta ya está, se devuelve la que quedó (sin escribir ni auditar de nuevo).
            var previa = Leer(con, tx, idCaso);
            if (previa is not null) return previa with { Repetido = true };

            if (!AltaDerivacionReglas.CasoSePuedeDerivarODarDeAlta(caso.Estado))
                return ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.EstadoNoPermitido, caso.Estado);

            var (idSesion, rechazoSesion) = ADComun.Sesion(con, tx, r.IdSesion, r.ClaveCierre, idProfesional, caso.IdPaciente, CierreSesionReglas.Alta);
            if (rechazoSesion is not null) return ResultadoAltaDerivacion.Rechazar(rechazoSesion);

            int id;
            using (var ins = new SqlCommand(@"
                INSERT INTO AltaClinica (IdCaso, IdPaciente, IdProfesional, IdSesion, FechaAlta, Informe, ControlesPrevistos, ControlCadaDias, PrimerControl, EstadoCasoAnterior, IdUsuario)
                OUTPUT INSERTED.IdAltaClinica
                VALUES (@C, @P, @M, @S, @F, @I, @N, @D, @PC, @E, @U)", con, tx))
            {
                ins.Parameters.AddWithValue("@C", idCaso);
                ins.Parameters.AddWithValue("@P", caso.IdPaciente);
                ins.Parameters.AddWithValue("@M", idProfesional);
                ins.Parameters.AddWithValue("@S", (object?)idSesion ?? DBNull.Value);
                ins.Parameters.AddWithValue("@F", fechaAlta);
                ins.Parameters.AddWithValue("@I", r.Informe!.Trim());
                ins.Parameters.AddWithValue("@N", plan.Controles);
                ins.Parameters.AddWithValue("@D", (object?)plan.CadaDias ?? DBNull.Value);
                ins.Parameters.AddWithValue("@PC", (object?)plan.Primero ?? DBNull.Value);
                ins.Parameters.AddWithValue("@E", caso.Estado);
                ins.Parameters.AddWithValue("@U", (object?)autor.IdUsuario ?? DBNull.Value);
                id = (int)ins.ExecuteScalar()!;
            }

            // La condición de partida va en el WHERE: es lo que impide cambiar dos veces el estado.
            using (var upd = new SqlCommand("UPDATE Caso SET Estado = @Nuevo WHERE IdCaso = @C AND Estado = @Antes", con, tx))
            {
                upd.Parameters.AddWithValue("@Nuevo", AltaDerivacionReglas.CasoEnAlta);
                upd.Parameters.AddWithValue("@C", idCaso);
                upd.Parameters.AddWithValue("@Antes", caso.Estado);
                if (upd.ExecuteNonQuery() != 1) return ResultadoAltaDerivacion.Rechazar(AltaDerivacionReglas.EstadoNoPermitido, caso.Estado);   // no se llega: el caso está bloqueado
            }

            // Alta estructurada (migración 35): el informe queda en la historia clínica, ligado a la sesión, y se crean los controles. Todo antes de la auditoría.
            if (ext is not null) AltaEstructuradaDAO.Escribir(con, tx, id, caso.IdPaciente, idProfesional, idCaso, idSesion, fechaAlta, ext);

            ADComun.Auditar(con, tx, autor, "Alta clínica confirmada", "AltaClinica", id,
                $"AltaClinica #{id}, caso #{idCaso}, paciente #{caso.IdPaciente}" + (idSesion is int s ? $", sesión #{s}" : "") + $", controles previstos {plan.Controles}");
            ADComun.Auditar(con, tx, autor, "Caso pasa a En alta", "Caso", idCaso,
                $"Caso #{idCaso}: {caso.Estado} -> {AltaDerivacionReglas.CasoEnAlta}, alta clínica #{id}, paciente #{caso.IdPaciente}");

            tx.Commit();
            return new ResultadoAltaDerivacion
            {
                Id = id, IdCaso = idCaso, IdPaciente = caso.IdPaciente, IdSesion = idSesion, EstadoAnterior = caso.Estado,
                EstadoActual = AltaDerivacionReglas.CasoEnAlta, Fecha = fechaAlta, Plan = plan
            };
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            // Dos confirmaciones a la vez: el índice único dejó pasar una sola; la otra es una repetición.
            using var con2 = Conexion.ObtenerConexion();
            con2.Open();
            using var tx2 = con2.BeginTransaction();
            var previa = Leer(con2, tx2, idCaso);
            if (previa is null) throw;
            return previa with { Repetido = true };
        }
    }

    private static ResultadoAltaDerivacion? Leer(SqlConnection con, SqlTransaction? tx, int idCaso)
    {
        using var cmd = new SqlCommand(@"
            SELECT a.IdAltaClinica, a.IdPaciente, a.IdSesion, a.FechaAlta, a.ControlesPrevistos, a.ControlCadaDias, a.PrimerControl, a.EstadoCasoAnterior, c.Estado AS EstadoActual
            FROM AltaClinica a JOIN Caso c ON c.IdCaso = a.IdCaso WHERE a.IdCaso = @C", con, tx);
        cmd.Parameters.AddWithValue("@C", idCaso);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ResultadoAltaDerivacion
        {
            Id = (int)r["IdAltaClinica"], IdCaso = idCaso, IdPaciente = (int)r["IdPaciente"], IdSesion = r["IdSesion"] as int?,
            EstadoAnterior = (string)r["EstadoCasoAnterior"], EstadoActual = (string)r["EstadoActual"], Fecha = (DateTime)r["FechaAlta"],
            Plan = new AltaDerivacionReglas.PlanDeControl((int)r["ControlesPrevistos"], r["ControlCadaDias"] as int?, r["PrimerControl"] as DateTime?)
        };
    }

    /// <summary>El alta guardada de un caso (con el informe), para quien la lee: el profesional a cargo o un auditor.</summary>
    public (int IdAltaClinica, int IdPaciente, int IdProfesional, int? IdSesion, DateTime FechaAlta, string Informe, int Controles, int? CadaDias, DateTime? Primero, DateTime CreadaUtc)? Obtener(int idCaso)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT IdAltaClinica, IdPaciente, IdProfesional, IdSesion, FechaAlta, Informe, ControlesPrevistos, ControlCadaDias, PrimerControl, CreadaUtc FROM AltaClinica WHERE IdCaso = @C", con);
        cmd.Parameters.AddWithValue("@C", idCaso);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ((int)r["IdAltaClinica"], (int)r["IdPaciente"], (int)r["IdProfesional"], r["IdSesion"] as int?, (DateTime)r["FechaAlta"], (string)r["Informe"],
                (int)r["ControlesPrevistos"], r["ControlCadaDias"] as int?, r["PrimerControl"] as DateTime?, (DateTime)r["CreadaUtc"]);
    }
}

public static class ADMigracion
{
    private static bool _existe;

    /// <summary>¿La base tiene la migración 27? Una vez que la tiene no se vuelve a preguntar.</summary>
    public static bool Disponible()
    {
        if (_existe) return true;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.AltaClinica', 'U') IS NOT NULL AND COL_LENGTH('dbo.Derivacion', 'Origen') IS NOT NULL THEN 1 ELSE 0 END", con);
        return _existe = (int)cmd.ExecuteScalar()! == 1;
    }
}
