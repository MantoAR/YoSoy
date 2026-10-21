using System.Data;
using Microsoft.Data.SqlClient;

/// <summary>
/// Un paciente tal como se le muestra a quien va a vincularlo con una conversación: lo justo para estar seguro de que es la persona (nombre completo, los últimos 3 dígitos del DNI y el
/// año de nacimiento). Nada de domicilio, mail, teléfono completo ni datos clínicos. OtrasConversaciones = cuántas conversaciones (de cualquier canal) tiene ya vinculadas, sin contar la
/// que se está mirando: solo el número, para avisar antes de confirmar.
/// </summary>
public record PacienteParaVincular(int IdPaciente, string Nombre, string DniParcial, int? AnioNacimiento, int OtrasConversaciones);

public enum ResultadoVinculo { Vinculada, YaEstabaVinculadaAEse, YaVinculadaAOtro, PacienteNoExiste, PacienteInactivo, ConversacionNoExiste }

/// <summary>Quién hizo el cambio, para la auditoría administrativa (solo ids; nunca el número, el nombre ni el DNI).</summary>
public record QuienAudita(int IdUsuario, string? Espacio);

public interface IVinculoConversacionDAO
{
    /// <summary>Pacientes ACTIVOS cuyo nombre contiene todas las palabras (sin mayúsculas ni tildes), hasta 10.</summary>
    List<PacienteParaVincular> BuscarPorNombre(IReadOnlyList<string> palabras, int? idConversacion);

    /// <summary>El paciente ACTIVO con ese DNI (solo dígitos), si existe.</summary>
    PacienteParaVincular? BuscarPorDni(string dni, int? idConversacion);

    /// <summary>Pacientes activos cuyo teléfono coincide con el de la conversación. Es una SUGERENCIA: nunca vincula.</summary>
    List<PacienteParaVincular> SugerirPorTelefono(string telefonoDeLaConversacion, int idConversacion);

    /// <summary>
    /// Vincula la conversación con el paciente de forma atómica: el UPDATE solo pega si la conversación sigue SIN paciente, así un mensaje que entra o una segunda persona que vincula a la vez no se
    /// pisan. Reinicia el flujo del bot (paso y datos de contexto) y deja el registro de auditoría en la MISMA transacción.
    /// </summary>
    ResultadoVinculo Vincular(int idConversacion, int idPaciente, string origen, QuienAudita quien);

    /// <summary>
    /// Desvincula (atómico, con auditoría en la misma transacción): IdPaciente a NULL, paso del bot a Inicio, datos de contexto vacíos y la IA de la conversación APAGADA (el contexto que
    /// la IA tenía era el de ese paciente; con la migración 25). Devuelve el paciente que tenía, o null si no tenía ninguno (no hay nada que desvincular) o la conversación no existe.
    /// No toca notas ni etiquetas.
    /// </summary>
    int? Desvincular(int idConversacion, QuienAudita quien);
}

public class VinculoConversacionDAO : IVinculoConversacionDAO
{
    public const int MaxResultados = 10;
    public const string EntidadAuditoria = "ConversacionVinculo";   // NO es "Conversacion": esa entidad cae en la auditoría clínica (LogAuditoriaDAO.EntidadesClinicas)

    private const string SqlPaciente = @"
        SELECT TOP (@Max) p.IdPaciente, p.Nombre, p.Dni, p.FechaNacimiento,
               (SELECT COUNT(*) FROM Conversacion c WHERE c.IdPaciente = p.IdPaciente AND c.IdConversacion <> ISNULL(@IdConv, 0)) AS Otras
        FROM Paciente p
        WHERE ISNULL(p.Estado, 'Activo') = 'Activo' ";

    public List<PacienteParaVincular> BuscarPorNombre(IReadOnlyList<string> palabras, int? idConversacion)
    {
        var condiciones = new System.Text.StringBuilder();
        for (var i = 0; i < palabras.Count; i++)
            condiciones.Append($" AND p.Nombre COLLATE Latin1_General_CI_AI LIKE @w{i} ESCAPE '\\'");
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(SqlPaciente + condiciones + " ORDER BY p.Nombre, p.IdPaciente", con);
        cmd.Parameters.Add("@Max", SqlDbType.Int).Value = MaxResultados;
        cmd.Parameters.Add("@IdConv", SqlDbType.Int).Value = (object?)idConversacion ?? DBNull.Value;
        for (var i = 0; i < palabras.Count; i++)
            cmd.Parameters.Add($"@w{i}", SqlDbType.NVarChar, 120).Value = "%" + EscaparLike(palabras[i]) + "%";
        return Leer(cmd);
    }

    public PacienteParaVincular? BuscarPorDni(string dni, int? idConversacion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(SqlPaciente + " AND p.Dni = @Dni", con);
        cmd.Parameters.Add("@Max", SqlDbType.Int).Value = 1;
        cmd.Parameters.Add("@IdConv", SqlDbType.Int).Value = (object?)idConversacion ?? DBNull.Value;
        cmd.Parameters.Add("@Dni", SqlDbType.NVarChar, 20).Value = dni;
        return Leer(cmd).FirstOrDefault();
    }

    // Paciente.Telefono es texto libre: se compara en memoria (como el bot, DatosMensajeriaDAO.PacientesPorTelefono) con CoincidenciaDeTelefono, que entiende el 15, el 9, el +54, el 0,
    // los guiones, los espacios y los paréntesis. Dos pacientes con el mismo teléfono (una familia) salen los dos.
    public List<PacienteParaVincular> SugerirPorTelefono(string telefonoDeLaConversacion, int idConversacion)
    {
        var ids = new List<int>();
        if (Telefono.Normalizar(telefonoDeLaConversacion).Length < 6) return new List<PacienteParaVincular>();
        using (var con = Conexion.ObtenerConexion())
        {
            con.Open();
            using var cmd = new SqlCommand("SELECT IdPaciente, Telefono FROM Paciente WHERE Telefono IS NOT NULL AND ISNULL(Estado, 'Activo') = 'Activo'", con);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (CoincidenciaDeTelefono.Coincide((string)r["Telefono"], telefonoDeLaConversacion)) ids.Add((int)r["IdPaciente"]);
            }
        }
        if (ids.Count == 0) return new List<PacienteParaVincular>();
        ids = ids.Take(5).ToList();
        using var con2 = Conexion.ObtenerConexion();
        con2.Open();
        using var cmd2 = new SqlCommand(SqlPaciente + $" AND p.IdPaciente IN ({string.Join(",", ids)}) ORDER BY p.Nombre, p.IdPaciente", con2);   // ids: enteros que salen de la base
        cmd2.Parameters.Add("@Max", SqlDbType.Int).Value = 5;
        cmd2.Parameters.Add("@IdConv", SqlDbType.Int).Value = idConversacion;
        return Leer(cmd2);
    }

    public ResultadoVinculo Vincular(int idConversacion, int idPaciente, string origen, QuienAudita quien)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        string? estado;
        using (var cmd = new SqlCommand("SELECT Estado FROM Paciente WHERE IdPaciente = @P", con, tx))
        {
            cmd.Parameters.Add("@P", SqlDbType.Int).Value = idPaciente;
            var o = cmd.ExecuteScalar();
            if (o is null) { tx.Rollback(); return ResultadoVinculo.PacienteNoExiste; }
            estado = o is DBNull ? null : (string)o;
        }
        if (!string.Equals(estado ?? "Activo", "Activo", StringComparison.OrdinalIgnoreCase)) { tx.Rollback(); return ResultadoVinculo.PacienteInactivo; }

        int filas;
        using (var cmd = new SqlCommand(@"
            UPDATE Conversacion SET IdPaciente = @P, PasoActual = 'Menu', DatosContexto = NULL
            WHERE IdConversacion = @C AND IdPaciente IS NULL", con, tx))
        {
            cmd.Parameters.Add("@P", SqlDbType.Int).Value = idPaciente;
            cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
            filas = cmd.ExecuteNonQuery();
        }
        if (filas == 0)
        {
            using var cmd = new SqlCommand("SELECT IdPaciente FROM Conversacion WHERE IdConversacion = @C", con, tx);
            cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
            var o = cmd.ExecuteScalar();
            tx.Rollback();
            if (o is null) return ResultadoVinculo.ConversacionNoExiste;
            return o is int actual && actual == idPaciente ? ResultadoVinculo.YaEstabaVinculadaAEse : ResultadoVinculo.YaVinculadaAOtro;
        }

        AuditoriaChats.Insertar(con, tx, quien, "Vinculación de conversación de chat con paciente", EntidadAuditoria, idConversacion,
            $"Conversación #{idConversacion}, paciente #{idPaciente}, origen {origen}");
        tx.Commit();
        return ResultadoVinculo.Vinculada;
    }

    public int? Desvincular(int idConversacion, QuienAudita quien)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        int? anterior;
        using (var cmd = new SqlCommand("SELECT IdPaciente FROM Conversacion WITH (UPDLOCK, ROWLOCK) WHERE IdConversacion = @C", con, tx))
        {
            cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
            var o = cmd.ExecuteScalar();
            anterior = o is int p ? p : null;
        }
        if (anterior is null) { tx.Rollback(); return null; }

        using (var cmd = new SqlCommand(@"
            UPDATE Conversacion SET IdPaciente = NULL, PasoActual = 'Inicio', DatosContexto = NULL
            WHERE IdConversacion = @C AND IdPaciente = @P", con, tx))
        {
            cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
            cmd.Parameters.Add("@P", SqlDbType.Int).Value = anterior.Value;
            if (cmd.ExecuteNonQuery() == 0) { tx.Rollback(); return null; }
        }

        // La IA de la conversación se apaga con el vínculo (la columna es de la migración 25: sin ella no hay nada que apagar).
        bool hayIa;
        using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Conversacion', 'IaActiva') IS NULL THEN 0 ELSE 1 END", con, tx))
            hayIa = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        if (hayIa)
        {
            using var cmd = new SqlCommand("UPDATE Conversacion SET IaActiva = 0 WHERE IdConversacion = @C", con, tx);
            cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
            cmd.ExecuteNonQuery();
        }

        AuditoriaChats.Insertar(con, tx, quien, "Desvinculación de conversación de chat y paciente", EntidadAuditoria, idConversacion,
            $"Conversación #{idConversacion}, paciente #{anterior}" + (hayIa ? ", IA apagada" : ""));
        tx.Commit();
        return anterior;
    }

    private static List<PacienteParaVincular> Leer(SqlCommand cmd)
    {
        var lista = new List<PacienteParaVincular>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new PacienteParaVincular(
                (int)r["IdPaciente"], (string)r["Nombre"], DniParcial((string)r["Dni"]),
                r["FechaNacimiento"] == DBNull.Value ? null : ((DateTime)r["FechaNacimiento"]).Year,
                (int)r["Otras"]));
        return lista;
    }

    /// <summary>"•••456": solo los últimos 3 dígitos del DNI.</summary>
    public static string DniParcial(string? dni)
    {
        var d = new string((dni ?? "").Where(char.IsDigit).ToArray());
        return d.Length == 0 ? "" : "•••" + (d.Length > 3 ? d[^3..] : d);
    }

    private static string EscaparLike(string texto) => texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}

/// <summary>La auditoría ADMINISTRATIVA de los cambios de Chats que se hacen en una transacción (se ve en la auditoría del Configurador). Solo ids y palabras fijas.</summary>
public static class AuditoriaChats
{
    public static void Insertar(SqlConnection con, SqlTransaction tx, QuienAudita quien, string accion, string entidad, int? idEntidad, string detalle)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
            VALUES (@U, @A, @E, @I, @D, @Cat, @Esp)", con, tx);
        cmd.Parameters.Add("@U", SqlDbType.Int).Value = quien.IdUsuario;
        cmd.Parameters.Add("@A", SqlDbType.NVarChar, 100).Value = accion;
        cmd.Parameters.Add("@E", SqlDbType.NVarChar, 50).Value = entidad;
        cmd.Parameters.Add("@I", SqlDbType.Int).Value = (object?)idEntidad ?? DBNull.Value;
        cmd.Parameters.Add("@D", SqlDbType.NVarChar, 500).Value = detalle;
        cmd.Parameters.Add("@Cat", SqlDbType.NVarChar, 20).Value = CategoriaAuditoria.Administrativa;
        cmd.Parameters.Add("@Esp", SqlDbType.NVarChar, 30).Value = (object?)quien.Espacio ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
}
