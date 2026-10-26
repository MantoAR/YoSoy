using Microsoft.Data.SqlClient;

// La revisión del formulario de admisión («Sobre mí») por la recepcionista. Reemplaza a la entrevista de admisión: el paciente completa el formulario, la
// recepcionista lo revisa y decide una de tres cosas, siempre con confirmación explícita de su parte:
//   · Aceptar          → el informe pasa a «Aprobado». La asignación del profesional sigue siendo la de siempre (manual, la hace ella: PUT /informes-admision/{id}/derivar).
//   · Devolver         → «Devuelto», con una aclaración de qué falta. El paciente lo ve en sus primeros pasos y lo completa de nuevo.
//   · Otro recurso     → «Otro recurso»: cierra el ingreso con un motivo. Es el único caso de «No ingresa».
// Cada cambio es una sola transacción, queda con quién y cuándo (IdUsuarioRevision, FechaRevision), el servidor lo audita y repetirlo no hace nada de más (idempotente).
//
// TODO (trabajo siguiente): acá es donde engancha la propuesta AUTOMÁTICA de profesional y horario al aceptar (hoy la elige la recepcionista a mano). Ver el comentario
// «PUNTO DE ENGANCHE» en Aceptar. Cuando exista, el sistema propone y la recepcionista confirma: nunca asigna solo.
public record ResultadoRevision(int Status, string Codigo, string Mensaje, bool Cambio, int? IdPaciente = null, string? Estado = null)
{
    public bool Ok => Status is >= 200 and < 300;
}

public static class RevisionAdmision
{
    public const int MaxAclaracion = 1000;
    public const int MaxMotivo = 500;

    // Estados desde los que se puede aceptar: el normal y los viejos de la entrevista, que quedan sin salida en la pantalla.
    private static readonly string[] PuedeAceptar = { EstadoInforme.Pendiente, "En entrevista", "Rechazado" };

    public static ResultadoRevision Aceptar(int idInforme, int idUsuario, DateTime ahora)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        var (estado, idPaciente) = Leer(con, tx, idInforme);
        if (estado is null) return new(404, "no-existe", "No encontramos ese formulario.", false);
        if (estado is EstadoInforme.Aprobado or EstadoInforme.Derivado)
            return new(200, "ya-aceptado", "Este formulario ya estaba aceptado.", false, idPaciente, estado);
        if (!PuedeAceptar.Contains(estado))
            return new(409, "estado", estado == EstadoInforme.OtroRecurso
                ? "Este ingreso se cerró con «otro recurso»: no se puede aceptar."
                : "Este formulario está devuelto al paciente: tiene que completarlo de nuevo antes de aceptarlo.", false, idPaciente, estado);

        // PUNTO DE ENGANCHE (trabajo siguiente): después de aceptar, proponer profesional y horario según las preferencias del paciente
        // (InformeAdmision.HorarioPreferido / ProfesionalesPreferidos) y las agendas. La recepcionista confirma; no se asigna solo.
        Actualizar(con, tx, idInforme, EstadoInforme.Aprobado, idUsuario, ahora, aclaracion: null, motivo: null);
        AvisosPaciente.CrearUnaVez(con, tx, idPaciente!.Value, $"informe:{idInforme}:aprobado", "Aceptamos tu formulario",
            "La recepción revisó «Sobre mí» y lo aceptó. Ahora estamos eligiendo tu profesional: te avisamos apenas esté asignado.", null);
        tx.Commit();
        return new(200, "aceptado", "Formulario aceptado.", true, idPaciente, EstadoInforme.Aprobado);
    }

    public static ResultadoRevision Devolver(int idInforme, int idUsuario, string? aclaracion, DateTime ahora)
    {
        aclaracion = aclaracion?.Trim();
        if (string.IsNullOrEmpty(aclaracion))
            return new(400, "falta-aclaracion", "Escribí qué le falta al formulario para poder devolverlo.", false);
        if (aclaracion.Length > MaxAclaracion)
            return new(400, "aclaracion-larga", $"La aclaración puede tener hasta {MaxAclaracion} caracteres.", false);

        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        var (estado, idPaciente) = Leer(con, tx, idInforme);
        if (estado is null) return new(404, "no-existe", "No encontramos ese formulario.", false);
        if (estado == EstadoInforme.Devuelto)
        {
            // Repetir la misma devolución no hace nada de más; devolverlo con otra aclaración mientras sigue devuelto, tampoco (el paciente ya fue avisado).
            return new(200, "ya-devuelto", "Este formulario ya estaba devuelto al paciente.", false, idPaciente, estado);
        }
        if (!PuedeAceptar.Contains(estado) && estado != EstadoInforme.Aprobado)
            return new(409, "estado", estado == EstadoInforme.OtroRecurso
                ? "Este ingreso se cerró con «otro recurso»: no se puede devolver."
                : "Este paciente ya tiene profesional asignado: el formulario no se puede devolver.", false, idPaciente, estado);

        Actualizar(con, tx, idInforme, EstadoInforme.Devuelto, idUsuario, ahora, aclaracion, motivo: null);
        AvisosPaciente.CrearUnaVez(con, tx, idPaciente!.Value, $"informe:{idInforme}:devuelto:{ahora.Ticks}", "Necesitamos que aclares algo en tu formulario",
            "La recepción revisó «Sobre mí» y necesita que completes algo: " + aclaracion + " Entrá a tus primeros pasos para completarlo.", null);
        tx.Commit();
        return new(200, "devuelto", "Formulario devuelto al paciente.", true, idPaciente, EstadoInforme.Devuelto);
    }

    public static ResultadoRevision OtroRecurso(int idInforme, int idUsuario, string? motivo, DateTime ahora)
    {
        motivo = motivo?.Trim();
        if (string.IsNullOrEmpty(motivo))
            return new(400, "falta-motivo", "Escribí el motivo para cerrar el ingreso: queda registrado.", false);
        if (motivo.Length > MaxMotivo)
            return new(400, "motivo-largo", $"El motivo puede tener hasta {MaxMotivo} caracteres.", false);

        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        var (estado, idPaciente) = Leer(con, tx, idInforme);
        if (estado is null) return new(404, "no-existe", "No encontramos ese formulario.", false);
        if (estado == EstadoInforme.OtroRecurso)
            return new(200, "ya-cerrado", "Este ingreso ya estaba cerrado con «otro recurso».", false, idPaciente, estado);
        if (estado == EstadoInforme.Derivado)
            return new(409, "estado", "Este paciente ya tiene profesional asignado: el ingreso no se puede cerrar desde acá.", false, idPaciente, estado);

        Actualizar(con, tx, idInforme, EstadoInforme.OtroRecurso, idUsuario, ahora, aclaracion: null, motivo);
        // El caso de seguimiento del ingreso se cierra con él y la sugerencia de profesional que quedara pendiente se descarta: no queda nada esperando en la cola.
        Ejecutar(con, tx, "UPDATE Caso SET Estado = N'Cerrado' WHERE IdInforme = @I AND Estado IN (N'Nuevo', N'Derivado')", ("@I", idInforme));
        Ejecutar(con, tx, @"UPDATE cd SET Estado = N'Rechazada', FechaResolucion = @F, Nota = N'Ingreso cerrado: otro recurso'
                            FROM CasoDerivacion cd JOIN Caso c ON c.IdCaso = cd.IdCaso
                            WHERE c.IdInforme = @I AND cd.Estado = N'Pendiente de confirmar'", ("@I", idInforme), ("@F", ahora));
        AvisosPaciente.CrearUnaVez(con, tx, idPaciente!.Value, $"informe:{idInforme}:otro-recurso", "Sobre tu ingreso a YoSoy",
            "La recepción revisó tu formulario y, por lo que nos contaste, te orienta hacia otro recurso: " + motivo, null);
        tx.Commit();
        return new(200, "otro-recurso", "Ingreso cerrado con «otro recurso».", true, idPaciente, EstadoInforme.OtroRecurso);
    }

    // Bloquea la fila hasta el final de la transacción: dos recepcionistas que actúan a la vez sobre el mismo formulario no se pisan.
    private static (string? Estado, int? IdPaciente) Leer(SqlConnection con, SqlTransaction tx, int idInforme)
    {
        using var cmd = new SqlCommand("SELECT Estado, IdPaciente FROM InformeAdmision WITH (UPDLOCK, ROWLOCK) WHERE IdInforme = @I", con, tx);
        cmd.Parameters.AddWithValue("@I", idInforme);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ((string)r["Estado"], (int)r["IdPaciente"]) : (null, null);
    }

    private static void Actualizar(SqlConnection con, SqlTransaction tx, int idInforme, string estado, int idUsuario, DateTime ahora, string? aclaracion, string? motivo) =>
        Ejecutar(con, tx, @"
            UPDATE InformeAdmision
            SET Estado = @E, IdUsuarioRevision = @U, FechaRevision = @F,
                AclaracionRecepcion = CASE WHEN @E = N'Devuelto' THEN @A ELSE AclaracionRecepcion END,
                MotivoRechazo = CASE WHEN @E = N'Otro recurso' THEN @M ELSE MotivoRechazo END
            WHERE IdInforme = @I",
            ("@E", estado), ("@U", idUsuario), ("@F", ahora), ("@A", (object?)aclaracion ?? DBNull.Value), ("@M", (object?)motivo ?? DBNull.Value), ("@I", idInforme));

    private static void Ejecutar(SqlConnection con, SqlTransaction tx, string sql, params (string, object)[] p)
    {
        using var cmd = new SqlCommand(sql, con, tx);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// Avisos de la campanita del paciente creados UNA sola vez por evento. «Evento» es una clave estable (por ejemplo «informe:12:aprobado» o «turno:345:propuesto»):
/// con la migración 31 hay un índice único (IdPaciente, Evento), así que aunque el mismo hecho se procese dos veces el paciente recibe un solo aviso.
/// </summary>
public static class AvisosPaciente
{
    /// <summary>true si creó el aviso; false si ya existía uno para ese evento. Dentro de la transacción que pasa quien llama.</summary>
    public static bool CrearUnaVez(SqlConnection con, SqlTransaction? tx, int idPaciente, string evento, string titulo, string mensaje, string? link)
    {
        using var cmd = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM Notificacion WHERE IdPaciente = @P AND Evento = @E)
            BEGIN
                INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link, Evento) VALUES (@P, @T, @M, @L, @E);
                SELECT 1;
            END
            ELSE SELECT 0;", con, tx);
        cmd.Parameters.AddWithValue("@P", idPaciente);
        cmd.Parameters.AddWithValue("@E", evento.Length > 100 ? evento[..100] : evento);
        cmd.Parameters.AddWithValue("@T", titulo.Length > 200 ? titulo[..200] : titulo);
        cmd.Parameters.AddWithValue("@M", mensaje.Length > 1000 ? mensaje[..1000] : mensaje);
        cmd.Parameters.AddWithValue("@L", (object?)link ?? DBNull.Value);
        try { return (int)cmd.ExecuteScalar()! == 1; }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return false; }   // otro pedido lo creó en el mismo instante
    }

    public static bool CrearUnaVez(int idPaciente, string evento, string titulo, string mensaje, string? link)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        return CrearUnaVez(con, null, idPaciente, evento, titulo, mensaje, link);
    }
}
