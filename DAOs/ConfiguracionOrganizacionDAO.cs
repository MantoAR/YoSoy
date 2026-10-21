using System.Data;
using Microsoft.Data.SqlClient;

/// <summary>
/// Valores que la organización puede cambiar (tabla ConfiguracionOrganizacion: clave, valor, y quién y cuándo lo cambió). Hoy guarda una sola cosa: el interruptor GLOBAL de IA de los
/// chats (clave <see cref="ClaveIaChats"/>, "1" o "0"). Sin la fila o sin la tabla vale "apagado": lo único que no se puede hacer sin la tabla es prenderlo.
/// </summary>
public interface IConfiguracionIaChats
{
    /// <summary>El interruptor global de IA de los chats. false si la fila o la tabla no existen.</summary>
    bool IaGlobalActiva();

    /// <summary>Cambia el interruptor global (y deja el registro de auditoría en la misma transacción). false = no se pudo guardar porque la base no tiene la tabla.</summary>
    bool EstablecerIaGlobal(bool activa, QuienAudita quien);
}

public class ConfiguracionOrganizacionDAO : IConfiguracionIaChats
{
    public const string ClaveIaChats = "Chats.IaActiva";
    public const string EntidadAuditoria = "ConfiguracionOrganizacion";
    private const int TablaInexistente = 208;   // "Invalid object name"

    public bool IaGlobalActiva()
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT Valor FROM ConfiguracionOrganizacion WHERE Clave = @K", con);
            cmd.Parameters.Add("@K", SqlDbType.NVarChar, 100).Value = ClaveIaChats;
            return cmd.ExecuteScalar() is string v && v == "1";
        }
        catch (SqlException ex) when (ex.Number == TablaInexistente) { return false; }
    }

    public bool EstablecerIaGlobal(bool activa, QuienAudita quien)
    {
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var tx = con.BeginTransaction();
            bool antes;
            using (var cmd = new SqlCommand("SELECT Valor FROM ConfiguracionOrganizacion WITH (UPDLOCK, HOLDLOCK) WHERE Clave = @K", con, tx))
            {
                cmd.Parameters.Add("@K", SqlDbType.NVarChar, 100).Value = ClaveIaChats;
                antes = cmd.ExecuteScalar() is string v && v == "1";
            }
            using (var cmd = new SqlCommand(@"
                MERGE ConfiguracionOrganizacion WITH (HOLDLOCK) AS t
                USING (SELECT @K AS Clave) AS s ON t.Clave = s.Clave
                WHEN MATCHED THEN UPDATE SET Valor = @V, FechaModificacion = SYSDATETIME(), IdUsuarioModifico = @U
                WHEN NOT MATCHED THEN INSERT (Clave, Valor, IdUsuarioModifico) VALUES (@K, @V, @U);", con, tx))
            {
                cmd.Parameters.Add("@K", SqlDbType.NVarChar, 100).Value = ClaveIaChats;
                cmd.Parameters.Add("@V", SqlDbType.NVarChar, 10).Value = activa ? "1" : "0";
                cmd.Parameters.Add("@U", SqlDbType.Int).Value = quien.IdUsuario;
                cmd.ExecuteNonQuery();
            }
            AuditoriaChats.Insertar(con, tx, quien, activa ? "IA global de chats prendida" : "IA global de chats apagada", EntidadAuditoria, null,
                $"{ClaveIaChats}: {(antes ? "prendida" : "apagada")} a {(activa ? "prendida" : "apagada")}");
            tx.Commit();
            return true;
        }
        catch (SqlException ex) when (ex.Number == TablaInexistente) { return false; }
    }
}
