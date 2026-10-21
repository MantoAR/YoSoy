using System.Data;
using Microsoft.Data.SqlClient;

/// <summary>
/// Lo que se guarda para el interruptor de IA de Chats v2 (Fase 1: se guarda y se muestra; nada responde con IA todavía). Todo es OPCIONAL a nivel de base:
///  · el interruptor de cada conversación vive en Conversacion.IaActiva (migración 25, migrations/add_conversacion_ia.sql). Sin esa columna la API anda igual: no la lee ni la
///    escribe, lo dice (Disponible = false) y el interruptor por conversación responde 409;
///  · el interruptor GLOBAL es una fila de ConfiguracionOrganizacion (clave Chats.IaActiva); sin la fila, o sin la tabla, vale "apagado".
/// Los dos arrancan APAGADOS. Cada cambio deja un registro de auditoría administrativa en la misma transacción (solo ids y palabras fijas).
/// </summary>
public interface IIaConversacionDAO
{
    /// <summary>¿La base tiene Conversacion.IaActiva (migración 25)?</summary>
    bool ColumnaDisponible();

    /// <summary>El interruptor de esa conversación: null si la columna no existe o la conversación no existe.</summary>
    bool? Obtener(int idConversacion);

    /// <summary>Prende o apaga la IA de la conversación (con auditoría). false = no hay columna o no hay conversación.</summary>
    bool Establecer(int idConversacion, bool activa, QuienAudita quien);

    /// <summary>
    /// "Tomar control": apaga la IA de la conversación y, si se pide (<paramref name="pausarAsistente"/>, modo Asistente), pasa una conversación en estado Activa a DerivadaHumano, que es lo
    /// que ya hace que el asistente de reglas no conteste. Sin la columna igual hace lo segundo. Una sola transacción, con auditoría. null = la conversación no existe.
    /// </summary>
    ResultadoTomarControl? TomarControl(int idConversacion, bool pausarAsistente, QuienAudita quien);
}

public record ResultadoTomarControl(string Estado, bool IaAhoraApagada, bool IaDisponible);

public class IaConversacionDAO : IIaConversacionDAO
{
    public const string EntidadAuditoria = "ConversacionIa";

    private bool _existe;   // una vez que existe, no se vuelve a preguntar

    public bool ColumnaDisponible()
    {
        if (_existe) return true;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Conversacion', 'IaActiva') IS NOT NULL THEN 1 ELSE 0 END", con);
        _existe = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        return _existe;
    }

    public bool? Obtener(int idConversacion)
    {
        if (!ColumnaDisponible()) return null;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT IaActiva FROM Conversacion WHERE IdConversacion = @C", con);
        cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
        var o = cmd.ExecuteScalar();
        return o is bool b ? b : null;
    }

    public bool Establecer(int idConversacion, bool activa, QuienAudita quien)
    {
        if (!ColumnaDisponible()) return false;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        using (var cmd = new SqlCommand("UPDATE Conversacion SET IaActiva = @A WHERE IdConversacion = @C", con, tx))
        {
            cmd.Parameters.Add("@A", SqlDbType.Bit).Value = activa;
            cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
            if (cmd.ExecuteNonQuery() == 0) { tx.Rollback(); return false; }
        }
        AuditoriaChats.Insertar(con, tx, quien, activa ? "IA de conversación de chat prendida" : "IA de conversación de chat apagada", EntidadAuditoria, idConversacion,
            $"Conversación #{idConversacion}");
        tx.Commit();
        return true;
    }

    public ResultadoTomarControl? TomarControl(int idConversacion, bool pausarAsistente, QuienAudita quien)
    {
        var hayColumna = ColumnaDisponible();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        // Bloquea la fila: el estado que se devuelve es el que quedó.
        string estado;
        using (var cmd = new SqlCommand("SELECT Estado FROM Conversacion WITH (UPDLOCK, ROWLOCK) WHERE IdConversacion = @C", con, tx))
        {
            cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
            var o = cmd.ExecuteScalar();
            if (o is null) { tx.Rollback(); return null; }
            estado = (string)o;
        }

        var pausado = false;
        if (pausarAsistente && estado == EstadoConversacion.Activa)
        {
            using var cmd = new SqlCommand("UPDATE Conversacion SET Estado = @E WHERE IdConversacion = @C AND Estado = @Antes", con, tx);
            cmd.Parameters.Add("@E", SqlDbType.NVarChar, 20).Value = EstadoConversacion.DerivadaHumano;
            cmd.Parameters.Add("@Antes", SqlDbType.NVarChar, 20).Value = EstadoConversacion.Activa;
            cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
            pausado = cmd.ExecuteNonQuery() == 1;
            if (pausado) estado = EstadoConversacion.DerivadaHumano;
        }
        if (hayColumna)
        {
            using var cmd = new SqlCommand("UPDATE Conversacion SET IaActiva = 0 WHERE IdConversacion = @C", con, tx);
            cmd.Parameters.Add("@C", SqlDbType.Int).Value = idConversacion;
            cmd.ExecuteNonQuery();
        }

        AuditoriaChats.Insertar(con, tx, quien, "Toma de control de conversación de chat", EntidadAuditoria, idConversacion,
            $"Conversación #{idConversacion}" + (hayColumna ? ", IA apagada" : "") + (pausado ? ", asistente en pausa" : ""));
        tx.Commit();
        return new ResultadoTomarControl(estado, hayColumna, hayColumna);
    }
}
