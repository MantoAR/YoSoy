using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

// Alta clínica ESTRUCTURADA con plan de control (migración 35), sobre el alta de la migración 27 (docs/ALTA_DERIVACION_CIERRE.md). El profesional completa un informe con secciones; el sistema precarga lo que ya sabe
// (motivo de consulta y objetivos del formulario «Sobre mí»), guarda el informe en la historia clínica ligado a la sesión de cierre, y propone dos controles de seguimiento (a los 3 y a los 6 meses)
// que el profesional puede mover o quitar antes de confirmar. Los controles son RECORDATORIOS para el profesional, no turnos: al acercarse la fecha, aviso por la campanita al profesional y a recepción
// para coordinar el turno de control. Una persona confirma el alta; nada se aplica solo. Detalle en docs/AUSENCIAS_ALTA.md.
public record InformeAltaEstructurado(string? MotivoConsulta, string? Diagnostico, string? ObjetivosTerapeuticos, string? GradoConsecucion, string? Tecnicas,
    string? EstadoAlAlta, string? RecursosPaciente, string? Recomendaciones, string? PrevencionRecaidas);

public record AltaExtension(InformeAltaEstructurado Informe, List<DateTime> Controles);

public static class AltaEstructuradaReglas
{
    // TODO-VALIDAR: los controles por defecto (3 y 6 meses), con cuántos días de anticipación se avisa y qué secciones del informe son obligatorias los tienen que confirmar los psicólogos.
    public static readonly int[] ControlesPorDefectoMeses = { 3, 6 };
    public const int AvisoControlDias = 14;
    public const int CampoMaximo = 2500;
    public const int InformeEstructuradoMaximo = 24000;
    public const int MaxControlesEstructurado = 12;
    public const int ControlHastaDias = 730;

    public const string TipoControlCercano = "control-cercano";
    public const string ControlInexistente = "control-inexistente";
    public const string FaltaMigracion35 = "falta-migracion-35";

    /// <summary>Las fechas de control sugeridas: fecha de alta + 3 y + 6 meses.</summary>
    public static List<DateTime> ControlesSugeridos(DateTime fechaAlta) => ControlesPorDefectoMeses.Select(m => fechaAlta.Date.AddMonths(m)).ToList();

    static string Limpio(string? s) => (s ?? "").Trim();

    /// <summary>El informe en texto, con un encabezado por sección (es lo que queda en la historia clínica y en AltaClinica.Informe). Las secciones vacías opcionales se omiten.</summary>
    public static string Renderizar(InformeAltaEstructurado i)
    {
        var sb = new StringBuilder();
        void Seccion(string titulo, string? texto, bool siempre = false)
        {
            if (!siempre && string.IsNullOrWhiteSpace(texto)) return;
            sb.Append(titulo).Append('\n').Append(Limpio(texto)).Append("\n\n");
        }
        Seccion("1. MOTIVO DE CONSULTA", i.MotivoConsulta);
        Seccion("2. DIAGNÓSTICO O HIPÓTESIS DIAGNÓSTICA", i.Diagnostico, true);
        Seccion("3. OBJETIVOS TERAPÉUTICOS", i.ObjetivosTerapeuticos, true);
        Seccion("   Grado de consecución de los objetivos", i.GradoConsecucion, true);
        Seccion("4. TÉCNICAS UTILIZADAS", i.Tecnicas);
        Seccion("5. ESTADO AL MOMENTO DEL ALTA", i.EstadoAlAlta, true);
        Seccion("   Recursos del paciente", i.RecursosPaciente);
        Seccion("6. RECOMENDACIONES", i.Recomendaciones, true);
        Seccion("   Prevención de recaídas", i.PrevencionRecaidas);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Valida las secciones; devuelve el mensaje de lo que falta (null si está todo bien).</summary>
    public static string? ValidarInforme(InformeAltaEstructurado i)
    {
        if (string.IsNullOrWhiteSpace(i.Diagnostico)) return "Completá el diagnóstico o la hipótesis diagnóstica.";
        if (string.IsNullOrWhiteSpace(i.ObjetivosTerapeuticos)) return "Completá los objetivos terapéuticos.";
        if (string.IsNullOrWhiteSpace(i.GradoConsecucion)) return "Indicá el grado de consecución de los objetivos.";
        if (string.IsNullOrWhiteSpace(i.EstadoAlAlta)) return "Describí el estado del paciente al momento del alta.";
        if (string.IsNullOrWhiteSpace(i.Recomendaciones)) return "Completá las recomendaciones.";
        foreach (var (nombre, valor) in new[]
        {
            ("El motivo de consulta", i.MotivoConsulta), ("El diagnóstico", i.Diagnostico), ("Los objetivos", i.ObjetivosTerapeuticos), ("El grado de consecución", i.GradoConsecucion),
            ("Las técnicas utilizadas", i.Tecnicas), ("El estado al alta", i.EstadoAlAlta), ("Los recursos del paciente", i.RecursosPaciente), ("Las recomendaciones", i.Recomendaciones),
            ("La prevención de recaídas", i.PrevencionRecaidas)
        })
            if (valor is { Length: > CampoMaximo }) return $"{nombre} no puede superar los {CampoMaximo} caracteres.";
        return Renderizar(i).Length > InformeEstructuradoMaximo ? "El informe es demasiado largo." : null;
    }

    /// <summary>Valida las fechas de control (aaaa-mm-dd): después de la fecha de alta, a lo sumo 2 años, sin repetir. Devuelve la lista ordenada.</summary>
    public static string? ValidarControles(IEnumerable<string>? fechas, DateTime fechaAlta, out List<DateTime> controles)
    {
        controles = new();
        foreach (var texto in fechas ?? Enumerable.Empty<string>())
        {
            if (AltaDerivacionReglas.Fecha(texto) is not DateTime f) return "Una de las fechas de control no es válida.";
            if (f.Date <= fechaAlta.Date) return "Los controles tienen que ser posteriores a la fecha de alta.";
            if (f.Date > fechaAlta.Date.AddDays(ControlHastaDias)) return "Un control no puede estar a más de dos años de la fecha de alta.";
            if (controles.Contains(f.Date)) return "Hay dos controles en la misma fecha.";
            controles.Add(f.Date);
        }
        if (controles.Count > MaxControlesEstructurado) return $"Podés programar hasta {MaxControlesEstructurado} controles.";
        controles.Sort();
        return null;
    }

    /// <summary>
    /// Traduce el pedido estructurado al pedido de siempre (informe en texto y plan numérico) para reutilizar las validaciones de la migración 27, y devuelve la extensión (secciones y fechas).
    /// Sin informe estructurado devuelve el pedido tal cual y extensión null.
    /// </summary>
    public static string? Aplicar(AltaClinicaRequest r, DateTime hoy, out AltaClinicaRequest pedido, out AltaExtension? extension)
    {
        pedido = r;
        extension = null;
        if (r.InformeEstructurado is null) return null;

        var errorInforme = ValidarInforme(r.InformeEstructurado);
        if (errorInforme is not null) return errorInforme;

        DateTime fechaAlta = hoy.Date;
        if (!string.IsNullOrWhiteSpace(r.FechaAlta))
        {
            if (AltaDerivacionReglas.Fecha(r.FechaAlta) is not DateTime f) return "La fecha de alta no es válida.";
            fechaAlta = f.Date;
        }
        var errorControles = ValidarControles(r.Controles, fechaAlta, out var controles);
        if (errorControles is not null) return errorControles;

        // El plan numérico de la migración 27 (CK_AltaClinica_Plan): cantidad, cada cuántos días y fecha del primero.
        int? cada = null; string? primero = null;
        if (controles.Count > 0)
        {
            primero = controles[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            cada = controles.Count > 1 ? Math.Clamp((controles[1] - controles[0]).Days, AltaDerivacionReglas.CadaDiasMinimo, AltaDerivacionReglas.CadaDiasMaximo) : 90;
        }
        pedido = r with { Informe = Renderizar(r.InformeEstructurado), ControlesPrevistos = controles.Count, ControlCadaDias = cada, PrimerControl = primero };
        extension = new AltaExtension(r.InformeEstructurado, controles);
        return null;
    }

    public static string TextoControlCercano(string paciente, DateTime fecha) =>
        $"Se acerca el control de seguimiento de {paciente} ({fecha:dd/MM/yyyy}). Coordinen el turno de control.";
}

public record ControlDeSeguimiento(int IdControl, int IdCaso, int IdPaciente, string Paciente, int Orden, DateTime Fecha, string Estado);

public record ResultadoControl
{
    public string? Rechazo { get; init; }
    public bool Repetido { get; init; }
    public int IdControl { get; init; }
    public static ResultadoControl Rechazar(string codigo) => new() { Rechazo = codigo };
}

/// <summary>Lo que el alta estructurada escribe además del alta de siempre (dentro de su transacción) y los controles de seguimiento.</summary>
public class AltaEstructuradaDAO
{
    public static bool Disponible() => AusenciasDAO.Disponible();

    /// <summary>Dentro de la transacción del alta: deja el informe en la historia clínica (una evolución), lo liga a la sesión de cierre y crea los controles. Todo antes de la auditoría.</summary>
    public static void Escribir(SqlConnection con, SqlTransaction tx, int idAlta, int idPaciente, int idProfesional, int idCaso, int? idSesion, DateTime fechaAlta, AltaExtension ext)
    {
        int? idTurno = null;
        if (idSesion is int s)
        {
            using var cmd = new SqlCommand("SELECT IdTurno FROM SesionClinica WHERE IdSesion = @S", con, tx);
            cmd.Parameters.AddWithValue("@S", s);
            idTurno = cmd.ExecuteScalar() as int?;
        }

        int idEvolucion;
        var conTipo = PrimeraEvolucionAdmision.TieneTipoCierre(con, tx);
        using (var cmd = new SqlCommand(@"
            DECLARE @IdHistoria INT = (SELECT TOP 1 IdHistoria FROM HistoriaClinica WHERE IdPaciente = @P);
            IF @IdHistoria IS NULL
            BEGIN
                INSERT INTO HistoriaClinica (IdPaciente, FechaApertura) VALUES (@P, GETDATE());
                SET @IdHistoria = SCOPE_IDENTITY();
            END
            INSERT INTO Evolucion (IdHistoria, IdProfesional, IdTurno, Fecha, Diagnostico, Observaciones, PlanTratamiento" + (conTipo ? ", TipoCierre" : "") + @")
            OUTPUT INSERTED.IdEvolucion
            VALUES (@IdHistoria, @M, @T, @F, @D, @O, @Plan" + (conTipo ? ", N'Alta'" : "") + ")", con, tx))
        {
            cmd.Parameters.AddWithValue("@P", idPaciente);
            cmd.Parameters.AddWithValue("@M", idProfesional);
            cmd.Parameters.AddWithValue("@T", (object?)idTurno ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@F", fechaAlta.Date);
            cmd.Parameters.AddWithValue("@D", ext.Informe.Diagnostico!.Trim());
            cmd.Parameters.AddWithValue("@O", "INFORME DE ALTA\n\n" + AltaEstructuradaReglas.Renderizar(ext.Informe));
            cmd.Parameters.AddWithValue("@Plan", (ext.Informe.Recomendaciones!.Trim() + (string.IsNullOrWhiteSpace(ext.Informe.PrevencionRecaidas) ? "" : "\nPrevención de recaídas: " + ext.Informe.PrevencionRecaidas.Trim())));
            idEvolucion = (int)cmd.ExecuteScalar()!;
        }

        using (var upd = new SqlCommand("UPDATE AltaClinica SET InformeEstructurado = @J, IdEvolucion = @E WHERE IdAltaClinica = @A", con, tx))
        {
            upd.Parameters.AddWithValue("@J", JsonSerializer.Serialize(ext.Informe));
            upd.Parameters.AddWithValue("@E", idEvolucion);
            upd.Parameters.AddWithValue("@A", idAlta);
            upd.ExecuteNonQuery();
        }
        if (idSesion is int ses)
        {
            using var upd = new SqlCommand("UPDATE SesionClinica SET IdEvolucion = @E WHERE IdSesion = @S AND IdEvolucion IS NULL", con, tx);
            upd.Parameters.AddWithValue("@E", idEvolucion);
            upd.Parameters.AddWithValue("@S", ses);
            upd.ExecuteNonQuery();
        }

        var orden = 0;
        foreach (var fecha in ext.Controles)
        {
            using var ins = new SqlCommand("INSERT INTO ControlSeguimiento (IdAltaClinica, IdCaso, IdPaciente, IdProfesional, Orden, FechaControl) VALUES (@A, @C, @P, @M, @O, @F)", con, tx);
            ins.Parameters.AddWithValue("@A", idAlta);
            ins.Parameters.AddWithValue("@C", idCaso);
            ins.Parameters.AddWithValue("@P", idPaciente);
            ins.Parameters.AddWithValue("@M", idProfesional);
            ins.Parameters.AddWithValue("@O", ++orden);
            ins.Parameters.AddWithValue("@F", fecha);
            ins.ExecuteNonQuery();
        }
    }

    /// <summary>Lo que se le precarga al profesional al dar el alta: nombre, motivo de consulta y objetivos del paciente (del formulario «Sobre mí») y los controles sugeridos. Solo lectura.</summary>
    public static (string Paciente, string? Motivo, string? Objetivos)? Precarga(int idCaso, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        int idPaciente;
        using (var cmd = new SqlCommand("SELECT IdPaciente FROM Caso WHERE IdCaso = @C AND IdProfesionalAsignado = @M", con))
        {
            cmd.Parameters.AddWithValue("@C", idCaso);
            cmd.Parameters.AddWithValue("@M", idProfesional);
            if (cmd.ExecuteScalar() is not int p) return null;
            idPaciente = p;
        }
        string nombre;
        using (var cmd = new SqlCommand("SELECT Nombre FROM Paciente WHERE IdPaciente = @P", con))
        {
            cmd.Parameters.AddWithValue("@P", idPaciente);
            nombre = cmd.ExecuteScalar() as string ?? "";
        }
        using (var cmd = new SqlCommand("SELECT TOP 1 MotivoConsulta, ObjetivosPaciente FROM InformeAdmision WHERE IdPaciente = @P ORDER BY IdInforme DESC", con))
        {
            cmd.Parameters.AddWithValue("@P", idPaciente);
            using var r = cmd.ExecuteReader();
            if (r.Read()) return (nombre, r["MotivoConsulta"] as string, r["ObjetivosPaciente"] as string);
        }
        return (nombre, null, null);
    }

    // ───────────── Controles de seguimiento ─────────────

    /// <summary>
    /// Avisa (una sola vez por control) por la campanita al profesional y a recepción cuando falta AvisoControlDias o menos para la fecha de un control. El control pasa de Pendiente a Avisado en la misma
    /// transacción que el aviso, así que correrlo varias veces no duplica nada. idProfesional = null: todos (la pasada del servicio). Devuelve cuántos avisó.
    /// </summary>
    public static int EvaluarControlesCercanos(DateTime ahora, int? idProfesional = null)
    {
        if (!Disponible()) return 0;
        var ids = new List<int>();
        using (var con = Conexion.ObtenerConexion())
        {
            con.Open();
            using var cmd = new SqlCommand("SELECT IdControlSeguimiento FROM ControlSeguimiento WHERE Estado = N'Pendiente' AND FechaControl <= @Limite AND (@M IS NULL OR IdProfesional = @M)", con);
            cmd.Parameters.AddWithValue("@Limite", ahora.Date.AddDays(AltaEstructuradaReglas.AvisoControlDias));
            cmd.Parameters.AddWithValue("@M", (object?)idProfesional ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add((int)r[0]);
        }
        var avisados = 0;
        foreach (var id in ids)
        {
            try { if (Avisar(id)) avisados++; }
            catch (SqlException) { /* se reintenta en la próxima pasada */ }
        }
        return avisados;
    }

    private static bool Avisar(int idControl)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        int idProfesional, idPaciente; DateTime fecha;
        using (var cmd = new SqlCommand("SELECT IdProfesional, IdPaciente, FechaControl FROM ControlSeguimiento WITH (UPDLOCK, HOLDLOCK) WHERE IdControlSeguimiento = @I AND Estado = N'Pendiente'", con, tx))
        {
            cmd.Parameters.AddWithValue("@I", idControl);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) { tx.Rollback(); return false; }
            idProfesional = (int)r["IdProfesional"]; idPaciente = (int)r["IdPaciente"]; fecha = (DateTime)r["FechaControl"];
        }
        var nombre = ADComun.NombrePaciente(con, tx, idPaciente);
        using (var upd = new SqlCommand("UPDATE ControlSeguimiento SET Estado = N'Avisado', AvisadoUtc = SYSUTCDATETIME() WHERE IdControlSeguimiento = @I AND Estado = N'Pendiente'", con, tx))
        {
            upd.Parameters.AddWithValue("@I", idControl);
            if (upd.ExecuteNonQuery() != 1) { tx.Rollback(); return false; }
        }
        var texto = AltaEstructuradaReglas.TextoControlCercano(nombre, fecha);
        ADComun.Avisar(con, tx, new AvisoNuevo("Profesional", idProfesional, AltaEstructuradaReglas.TipoControlCercano, "Se acerca un control de seguimiento", texto));
        ADComun.Avisar(con, tx, new AvisoNuevo("Administrador", null, AltaEstructuradaReglas.TipoControlCercano, "Se acerca un control de seguimiento", texto));
        tx.Commit();
        return true;
    }

    /// <summary>Los controles ya avisados (o por avisar) que el profesional todavía no cerró, para la campanita.</summary>
    public List<ControlDeSeguimiento> Abiertos(int idProfesional)
    {
        var lista = new List<ControlDeSeguimiento>();
        if (!Disponible()) return lista;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT c.IdControlSeguimiento, c.IdCaso, c.IdPaciente, p.Nombre, c.Orden, c.FechaControl, c.Estado
            FROM ControlSeguimiento c JOIN Paciente p ON p.IdPaciente = c.IdPaciente
            WHERE c.IdProfesional = @M AND c.Estado = N'Avisado' ORDER BY c.FechaControl", con);
        cmd.Parameters.AddWithValue("@M", idProfesional);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(new ControlDeSeguimiento((int)r[0], (int)r[1], (int)r[2], (string)r[3], (int)r[4], (DateTime)r[5], (string)r[6]));
        return lista;
    }

    /// <summary>El profesional da por resuelto el control (ya coordinó el turno de control o decidió no hacerlo). Idempotente y auditado.</summary>
    public ResultadoControl Resolver(int idControl, int idProfesional, string? nota, AutorDelCierre autor)
    {
        if (!Disponible()) return ResultadoControl.Rechazar(AltaEstructuradaReglas.FaltaMigracion35);
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        int idPaciente; string estado;
        using (var cmd = new SqlCommand("SELECT IdProfesional, IdPaciente, Estado FROM ControlSeguimiento WITH (UPDLOCK, HOLDLOCK) WHERE IdControlSeguimiento = @I", con, tx))
        {
            cmd.Parameters.AddWithValue("@I", idControl);
            using var r = cmd.ExecuteReader();
            if (!r.Read() || (int)r["IdProfesional"] != idProfesional) return ResultadoControl.Rechazar(AltaEstructuradaReglas.ControlInexistente);
            idPaciente = (int)r["IdPaciente"]; estado = (string)r["Estado"];
        }
        if (estado == "Resuelto") return new ResultadoControl { Repetido = true, IdControl = idControl };
        using (var upd = new SqlCommand("UPDATE ControlSeguimiento SET Estado = N'Resuelto', ResueltoUtc = SYSUTCDATETIME(), IdUsuarioResolucion = @U, Nota = @N WHERE IdControlSeguimiento = @I AND Estado <> N'Resuelto'", con, tx))
        {
            upd.Parameters.AddWithValue("@U", (object?)autor.IdUsuario ?? DBNull.Value);
            upd.Parameters.AddWithValue("@N", string.IsNullOrWhiteSpace(nota) ? DBNull.Value : nota.Trim()[..Math.Min(nota.Trim().Length, 500)]);
            upd.Parameters.AddWithValue("@I", idControl);
            upd.ExecuteNonQuery();
        }
        ADComun.Auditar(con, tx, autor, "Control de seguimiento resuelto", "ControlSeguimiento", idControl, $"Control #{idControl}, paciente #{idPaciente}: {estado} -> Resuelto");
        tx.Commit();
        return new ResultadoControl { IdControl = idControl };
    }
}

/// <summary>La pasada que reutiliza el servicio de fondo que ya existe (RecordatorioBackgroundService): aviso temprano de ausencia, controles cercanos y solicitudes de turno vencidas. Todo idempotente.</summary>
public interface ISeguimientoClinico
{
    void Pasada(DateTime ahora);
}

public class SeguimientoClinico : ISeguimientoClinico
{
    public void Pasada(DateTime ahora)
    {
        SolicitudesTurnoDAO.VencerSolicitudes(ahora);
        new AusenciasDAO().EvaluarAvisosTempranos(ahora);
        AltaEstructuradaDAO.EvaluarControlesCercanos(ahora);
    }
}
